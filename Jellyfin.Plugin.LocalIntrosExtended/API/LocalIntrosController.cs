using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mime;
using MediaBrowser.Common;
using MediaBrowser.Controller.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Jellyfin.Plugin.LocalIntrosExtended.Configuration;
using MediaBrowser.Common.Api;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;
using Jellyfin.Database.Implementations.Entities;

namespace Jellyfin.Plugin.LocalIntrosExtended;

[ApiController]
[Authorize(Policy = Policies.RequiresElevation)]
[Route("LocalIntrosExtended")]
[Produces(MediaTypeNames.Application.Json)]
public class LocalIntrosExtendedController : ControllerBase
{
    private readonly ILogger<LocalIntrosExtendedController> logger;
    private readonly IUserManager userManager;

    public LocalIntrosExtendedController(IApplicationHost appHost, ILoggerFactory loggerFactory, IUserManager userManager)
    {
        this.logger = loggerFactory.CreateLogger<LocalIntrosExtendedController>();
        this.userManager = userManager;
    }

    [HttpPost("LoadIntros")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public IActionResult LoadIntros()
    {
        logger.LogDebug("Loading Intros");
        PopulateIntroLibrary();
        return Ok();
    }

    [HttpPost("ClearIntros")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public IActionResult ClearIntros()
    {
        logger.LogInformation("Clearing Intros");
        LocalIntrosPlugin.LibraryManager.GetItemsResult(new InternalItemsQuery
        {
            HasAnyProviderId = new Dictionary<string, string>
            {
                { "prerolls.video", "" }
            }
        }).Items.ToList().ForEach(x =>
        {
            logger.LogInformation($"Removing {x.Path} from library.");
            LocalIntrosPlugin.LibraryManager.DeleteItem(x, new DeleteOptions());
        });
        return Ok();
    }

    private static string introsPath => LocalIntrosPlugin.Instance.Configuration.Local;

    private Dictionary<Guid, BaseItem> PopulateIntroLibrary()
    {
        logger.LogTrace($"Retrieving attributes of {introsPath}");
        var attrs = System.IO.File.GetAttributes(introsPath);

        bool needsConfigUpdate = false;

        Dictionary<Guid, BaseItem> libraryResults = new Dictionary<Guid, BaseItem>();

        logger.LogTrace($"Retrieving existing items from library.");
        var inLibrary = LocalIntrosPlugin.LibraryManager.GetItemsResult(new InternalItemsQuery
        {
            HasAnyProviderId = new Dictionary<string, string>
            {
                { "prerolls.video", "" }
            }
        }).Items;
        logger.LogInformation($"Found {inLibrary.Count()} items in library.");

        logger.LogTrace($"Creating dictionaries for comparison. (path => item, id => isFound, id => item)");
        var byPath = inLibrary.ToDictionary(x => x.Path, x => x);
        var isFound = inLibrary.ToDictionary(x => x.Id, x => false);
        var byId = inLibrary.ToDictionary(x => x.Id, x => x);

        using var _ = logger.BeginScope(new Dictionary<string, object>());

        IEnumerable<string> filesOnDisk;

        if (attrs.HasFlag(FileAttributes.Directory))
        {
            logger.LogInformation($"Retrieving files from directory at {introsPath}");
            filesOnDisk = Directory.EnumerateFiles(introsPath);
        }
        else if (System.IO.File.Exists(introsPath))
        {
            logger.LogInformation($"Retrieving file at {introsPath}");
            filesOnDisk = new FancyList<string> { introsPath };
        }
        else
        {
            throw new DirectoryNotFoundException($"Directory Not Found: {introsPath}. Please check your configuration.");
        }

        logger.LogTrace($"Retrieving item IDs in configuration file");
        var configDetectedVideos = LocalIntrosPlugin.Instance.Configuration.DetectedLocalVideos.Select(x => x.ItemId).ToHashSet();

        logger.LogTrace($"Comparing files on disk to items in library.");
        foreach (var file in filesOnDisk)
        {
            if (byPath.ContainsKey(file))
            {
                logger.LogTrace($"Found {file} in library, marking as found and adding to results.");
                isFound[byPath[file].Id] = true;
                libraryResults[byPath[file].Id] = byPath[file];
                if (!configDetectedVideos.Contains(byPath[file].Id) && !needsConfigUpdate)
                {
                    logger.LogInformation("Flagging for config update.");
                    needsConfigUpdate = true;
                }
            }
            else
            {
                logger.LogInformation($"Adding {file} to library and adding to results.");
                var video = new Video
                {
                    Id = Guid.NewGuid(),
                    Path = file,
                    OfficialRating = "G",
                    ProviderIds = new Dictionary<string, string>
                    {
                        { "prerolls.video", file }
                    },
                    Name = Path.GetFileNameWithoutExtension(file)
                        .Replace("jellyfin", string.Empty, StringComparison.InvariantCultureIgnoreCase)
                        .Replace("pre-roll", string.Empty, StringComparison.InvariantCultureIgnoreCase)
                        .Replace("_", " ", StringComparison.InvariantCultureIgnoreCase)
                        .Replace("-", " ", StringComparison.InvariantCultureIgnoreCase)
                        .Trim()
                };
                LocalIntrosPlugin.LibraryManager.CreateItem(video, null);
                if (!needsConfigUpdate)
                {
                    logger.LogInformation("Flagging for config update.");
                    needsConfigUpdate = true;
                }
                libraryResults[video.Id] = video;
            }
        }
        foreach (var item in isFound.Where(f => !f.Value))
        {
            logger.LogWarning($"Removing {byId[item.Key].Path} from library.");
            LocalIntrosPlugin.LibraryManager.DeleteItem(byId[item.Key], new DeleteOptions());
        }
        if (libraryResults.Count > 0)
        {
            if (inLibrary.Count() == 0)
            {
                logger.LogInformation($"No existing items in library, erasing configuration.");
                LocalIntrosPlugin.Instance.Configuration.DetectedLocalVideos = new ();
                LocalIntroConfigReset();

                UpdateOptionsConfig(libraryResults.Values);
            }
            if (needsConfigUpdate)
            {
                logger.LogInformation($"Updating configuration file.");
                UpdateOptionsConfig(libraryResults.Values);
            }
        }
        if (libraryResults.Count == 0)
        {
            logger.LogWarning($"No videos found in {introsPath}, updating configuration file.");
            UpdateOptionsConfig(libraryResults.Values);
        }
        return libraryResults;
    }

    private void LocalIntroConfigReset()
    {
        LocalIntrosPlugin.Instance.Configuration.Rules = new ();
    }

    private void CleanList(ICollection<Guid> listToClean, HashSet<Guid> existingItems)
    {
        listToClean.Where(x => !existingItems.Contains(x)).ToList().ForEach(x => listToClean.Remove(x));
    }

    private void UpdateOptionsConfig(IEnumerable<BaseItem> libraryResults)
    {
        logger.LogTrace($"Adding detected videos to configuration.");
        LocalIntrosPlugin.Instance.Configuration.DetectedLocalVideos = libraryResults.Select(x => new IntroVideo
        {
            ItemId = x.Id,
            Name = x.Name
        }).ToList();

        var validIds = LocalIntrosPlugin.Instance.Configuration.DetectedLocalVideos.Select(x => x.ItemId).ToHashSet();
        var validUserIds = GetActiveUsers().Select(u => u.Id).ToHashSet();
        var validLibraryIds = LocalIntrosPlugin.LibraryManager.GetVirtualFolders().Select(f => Guid.Parse(f.ItemId)).ToHashSet();

        foreach (var rule in LocalIntrosPlugin.Instance.Configuration.Rules)
        {
            CleanList(rule.IntroIds, validIds);
            CleanList(rule.UserIds, validUserIds);
            CleanList(rule.LibraryIds, validLibraryIds);
        }

        LocalIntrosPlugin.Instance.SaveConfiguration();
    }

    private IEnumerable<User> GetActiveUsers()
    {
        try
        {
            var getUsersMethod = userManager.GetType().GetMethod("GetUsers", Type.EmptyTypes);
            if (getUsersMethod != null)
            {
                var result = getUsersMethod.Invoke(userManager, null);
                if (result is IEnumerable<User> users)
                {
                    return users;
                }
            }

            var usersProp = userManager.GetType().GetProperty("Users");
            if (usersProp != null)
            {
                var result = usersProp.GetValue(userManager);
                if (result is IEnumerable<User> users)
                {
                    return users;
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error retrieving users via reflection");
        }

        return Enumerable.Empty<User>();
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.LocalIntrosExtended.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Entities.Movies;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.LocalIntrosExtended;

public class IntroProvider : IIntroProvider
{
    private readonly ILogger<IntroProvider> logger;

    public IntroProvider(ILoggerFactory loggerFactory)
    {
        logger = loggerFactory.CreateLogger<IntroProvider>();
    }

    public string Name { get; } = "Intros";

    public Task<IEnumerable<IntroInfo>> GetIntros(BaseItem item, User user)
    {
        try
        {
            logger.LogInformation($"[LocalIntrosExtended] GetIntros requested by User: '{user?.Username ?? "Unknown"}' for Media: '{item?.Name ?? "Unknown"}'");
            if (LocalIntrosPlugin.Instance.Configuration.Local != string.Empty)
            {
                logger.LogTrace("Local Config Detected, retrieving local intros.");
                return Task.FromResult(Local(item, user));
            }
            else
            {
                logger.LogError("No Local Config Detected, retrieving library intros.");
                return Task.FromResult(Enumerable.Empty<IntroInfo>());
            }
        }
        catch (Exception e)
        {
            logger.LogError(e, "Error retrieving intros");
            return Task.FromResult(Enumerable.Empty<IntroInfo>());
        }
    }

    private readonly CookieContainer _cookieContainer = new CookieContainer();

    private readonly Random _random = new Random();

    private static string introsPath => LocalIntrosPlugin.Instance.Configuration.Local;

    private (HashSet<string> tags, HashSet<string> genres, HashSet<string> studios, DateTime now, DateTime premiereDate) GetCriteriaList(BaseItem item)
    {
        switch (item.GetBaseItemKind())
        {
            case Data.Enums.BaseItemKind.Movie:
                var movie = item as Movie;
                return (movie.Tags.ToHashSet(), movie.Genres.ToHashSet(), movie.Studios.ToHashSet(), DateTime.Now, item.PremiereDate ?? DateTime.Today);
            case Data.Enums.BaseItemKind.Episode:
                var episode = item as Episode;
                var season = episode.Season;
                var series = episode.Series;
                return (
                    episode.Tags.Concat(season.Tags).Concat(series.Tags).ToHashSet(),
                    episode.Genres.Concat(season.Genres).Concat(series.Genres).ToHashSet(),
                    episode.Studios.Concat(season.Studios).Concat(series.Studios).ToHashSet(),
                    DateTime.Now,
                    episode.PremiereDate ?? season.PremiereDate ?? series.PremiereDate ?? DateTime.Today
                );
        }
        var emp = new HashSet<string>();
        return (emp, emp, emp, DateTime.Now, DateTime.Today);
    }

    private Guid? GetLibraryId(BaseItem item)
    {
        var current = item;
        while (current != null && current.ParentId != Guid.Empty)
        {
            var parent = LocalIntrosPlugin.LibraryManager.GetItemById(current.ParentId);
            if (parent == null || parent.ParentId == Guid.Empty)
            {
                // current is the CollectionFolder (library) because its parent is the RootFolder
                return current.Id;
            }
            current = parent;
        }
        return null;
    }

    private IEnumerable<IntroInfo> Local(BaseItem item, User user)
    {
        if (!File.Exists(introsPath) && !Directory.Exists(introsPath))
        {
            throw new Exception("No intros found in local path");
        }
        var location = File.GetAttributes(introsPath);

        var libraryResults = RetrieveIntroLibrary();

        if (!libraryResults.Any())
        {
            throw new Exception("No intros found in library");
        }

        var (tags, genres, studios, now, premiereDate) = GetCriteriaList(item);
        var libraryId = GetLibraryId(item);

        foreach (var rule in LocalIntrosPlugin.Instance.Configuration.Rules)
        {
            // 1. Check conditions (AND logic)
            if (rule.Genres.Any() && !genres.Any(g => rule.Genres.Contains(g, StringComparer.OrdinalIgnoreCase)))
                continue;

            if (rule.Tags.Any() && !tags.Any(t => rule.Tags.Contains(t, StringComparer.OrdinalIgnoreCase)))
                continue;

            // Exclude tags
            if (rule.ExcludeTags.Any() && tags.Any(t => rule.ExcludeTags.Contains(t, StringComparer.OrdinalIgnoreCase)))
                continue;

            if (rule.Studios.Any() && !studios.Any(s => rule.Studios.Contains(s, StringComparer.OrdinalIgnoreCase)))
                continue;

            if (rule.UserIds.Any() && (user == null || !rule.UserIds.Contains(user.Id)))
                continue;

            if (rule.LibraryIds.Any() && (libraryId == null || !rule.LibraryIds.Contains(libraryId.Value)))
                continue;

            if (rule.TargetType == IntroTargetType.MoviesOnly && item.GetBaseItemKind() != Data.Enums.BaseItemKind.Movie)
                continue;

            if (rule.TargetType == IntroTargetType.EpisodesOnly && item.GetBaseItemKind() != Data.Enums.BaseItemKind.Episode)
                continue;

            // Current date filters
            if (!rule.IsDateInRange(now))
                continue;

            // Media release date filters (handles single-ended ranges cleanly)
            if (rule.ReleaseDateStart != null && premiereDate < rule.ReleaseDateStart.Value)
                continue;

            if (rule.ReleaseDateEnd != null && premiereDate > rule.ReleaseDateEnd.Value)
                continue;

            // 2. Roll frequency
            if (rule.Frequency < 100 && _random.Next(1, 101) > rule.Frequency)
                continue; // Roll failed, continue to next rule

            // 3. Match found! Choose video(s) from this rule's list
            if (rule.IntroIds.Any())
            {
                if (rule.PlayAllIntros)
                {
                    // Play all configured intros in sequence
                    var list = new List<IntroInfo>();
                    foreach (var id in rule.IntroIds)
                    {
                        if (libraryResults.ContainsKey(id))
                        {
                            var itemInfo = libraryResults[id];
                            list.Add(new IntroInfo { Path = itemInfo.Path, ItemId = itemInfo.Id });
                        }
                    }
                    if (list.Any())
                    {
                        var introNames = string.Join(", ", list.Select(x => x.ItemId.HasValue && libraryResults.ContainsKey(x.ItemId.Value) ? libraryResults[x.ItemId.Value].Name : "Unknown"));
                        logger.LogInformation($"[LocalIntrosExtended] User: '{user?.Username ?? "Unknown"}', Media: '{item.Name}' -> Matched Rule: '{rule.Name}' -> Playing Intros (Play All): [{introNames}]");
                        return list;
                    }
                }
                else
                {
                    // Choose one random video (legacy behavior)
                    var selectedId = rule.IntroIds[_random.Next(rule.IntroIds.Count)];
                    if (libraryResults.ContainsKey(selectedId))
                    {
                        var selectedItem = libraryResults[selectedId];
                        logger.LogInformation($"[LocalIntrosExtended] User: '{user?.Username ?? "Unknown"}', Media: '{item.Name}' -> Matched Rule: '{rule.Name}' -> Playing Intro (Random): '{selectedItem.Name}'");
                        return new[] { new IntroInfo { Path = selectedItem.Path, ItemId = selectedItem.Id } };
                    }
                }
            }
        }

        return Enumerable.Empty<IntroInfo>();
    }

    private void UpdateOptionsConfig(IEnumerable<BaseItem> libraryResults)
    {
        LocalIntrosPlugin.Instance.Configuration.DetectedLocalVideos = libraryResults.Select(x => new IntroVideo
        {
            ItemId = x.Id,
            Name = x.Name
        }).OrderBy(i => i.Name).ToList();
        LocalIntrosPlugin.Instance.SaveConfiguration();
    }

    private Dictionary<Guid, BaseItem> RetrieveIntroLibrary() =>
        LocalIntrosPlugin.LibraryManager.GetItemsResult(new InternalItemsQuery
        {
            HasAnyProviderId = new Dictionary<string, string>
            {
                { "prerolls.video", "" }
            }
        }).Items.ToDictionary(x => x.Id, x => x);
}

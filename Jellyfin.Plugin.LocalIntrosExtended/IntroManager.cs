using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.LocalIntrosExtended.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.LocalIntrosExtended;

public class IntroSyncStatus
{
    public int FilesOnDiskCount { get; set; }
    public int DatabaseItemsCount { get; set; }
    public int ConfiguredVideosCount { get; set; }
    public int ConfiguredRulesCount { get; set; }
    public bool NeedsMigration { get; set; }
    public string Message { get; set; } = string.Empty;
}

public static class IntroManager
{
    public const string ProviderKey = "prerolls.video";

    private static string IntrosPath => LocalIntrosPlugin.Instance.Configuration.Local;

    public static string NormalizeVideoName(string filePath)
    {
        return Path.GetFileNameWithoutExtension(filePath)
            .Replace("jellyfin", string.Empty, StringComparison.InvariantCultureIgnoreCase)
            .Replace("pre-roll", string.Empty, StringComparison.InvariantCultureIgnoreCase)
            .Replace("_", " ", StringComparison.InvariantCultureIgnoreCase)
            .Replace("-", " ", StringComparison.InvariantCultureIgnoreCase)
            .Trim();
    }

    public static Dictionary<Guid, BaseItem> RetrieveIntroLibrary()
    {
        return LocalIntrosPlugin.LibraryManager.GetItemsResult(new InternalItemsQuery
        {
            HasAnyProviderId = new Dictionary<string, string>
            {
                { ProviderKey, "" }
            }
        }).Items.ToDictionary(x => x.Id, x => x);
    }

    public static IntroSyncStatus GetSyncStatus(ILogger logger)
    {
        var status = new IntroSyncStatus();
        var config = LocalIntrosPlugin.Instance.Configuration;
        var introsPath = config.Local;

        status.ConfiguredVideosCount = config.DetectedLocalVideos.Count;
        status.ConfiguredRulesCount = config.Rules.Count;

        if (string.IsNullOrWhiteSpace(introsPath) || (!File.Exists(introsPath) && !Directory.Exists(introsPath)))
        {
            status.NeedsMigration = false;
            status.Message = "Geen geldig lokaal intropad geconfigureerd.";
            return status;
        }

        try
        {
            var filesOnDisk = GetFilesOnDisk(introsPath, logger).ToList();
            status.FilesOnDiskCount = filesOnDisk.Count;

            var inLibrary = RetrieveIntroLibrary();
            status.DatabaseItemsCount = inLibrary.Count;

            // Desync condition: files exist on disk, but database has fewer items or 0 items
            if (status.FilesOnDiskCount > 0 && status.DatabaseItemsCount == 0)
            {
                status.NeedsMigration = true;
                status.Message = "De Jellyfin-database bevat momenteel 0 intro-items (mogelijk gewist tijdens de Jellyfin 12 migratie). Je geconfigureerde regels zijn veilig bewaard in de configuratie.";
            }
            else if (status.FilesOnDiskCount > status.DatabaseItemsCount)
            {
                status.NeedsMigration = true;
                status.Message = $"Niet alle bestanden op schijf ({status.FilesOnDiskCount}) zijn geregistreerd in de Jellyfin-database ({status.DatabaseItemsCount}). Synchronisatie aanbevolen.";
            }
            else
            {
                status.NeedsMigration = false;
                status.Message = "Database is gesynchroniseerd met de lokale intro-bestanden.";
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Fout bij controleren van intro synchronisatiestatus");
            status.Message = $"Fout bij controleren van status: {ex.Message}";
        }

        return status;
    }

    public static IEnumerable<string> GetFilesOnDisk(string path, ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(path) || (!File.Exists(path) && !Directory.Exists(path)))
        {
            return Enumerable.Empty<string>();
        }

        var attrs = File.GetAttributes(path);
        if (attrs.HasFlag(FileAttributes.Directory))
        {
            // Only pick up video files; sidecar files (.nfo, .jpg, .DS_Store, ...) would otherwise
            // be registered as videos and can collide on normalized name.
            return Directory.EnumerateFiles(path).Where(IsVideoFile);
        }
        return new[] { path };
    }

    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".m4v", ".mkv", ".webm", ".avi", ".mov", ".wmv", ".mpg", ".mpeg", ".ts", ".m2ts", ".flv", ".ogv"
    };

    private static bool IsVideoFile(string file)
    {
        return !Path.GetFileName(file).StartsWith('.') && VideoExtensions.Contains(Path.GetExtension(file));
    }

    public static Dictionary<Guid, BaseItem> PopulateIntroLibrary(ILogger logger, bool preserveGuids = true)
    {
        var config = LocalIntrosPlugin.Instance.Configuration;
        var introsPath = config.Local;

        if (string.IsNullOrWhiteSpace(introsPath) || (!File.Exists(introsPath) && !Directory.Exists(introsPath)))
        {
            throw new DirectoryNotFoundException($"Directory of bestand niet gevonden: '{introsPath}'. Controleer de configuratie.");
        }

        logger.LogInformation("[LocalIntrosExtended] Starten van veilige intro-bibliotheeksynchronisatie (GUID-behoud: {PreserveGuids}).", preserveGuids);

        var inLibrary = RetrieveIntroLibrary();
        logger.LogInformation("[LocalIntrosExtended] {Count} items aangetroffen in Jellyfin-database.", inLibrary.Count);

        // Tolerate duplicate rows for the same path (left behind by earlier failed syncs); extras get cleaned up below.
        var byPath = inLibrary.Values
            .Where(x => !string.IsNullOrEmpty(x.Path))
            .GroupBy(x => x.Path)
            .ToDictionary(g => g.Key, g => g.First());
        var filesOnDisk = GetFilesOnDisk(introsPath, logger).ToList();
        var keepIds = filesOnDisk
            .Where(byPath.ContainsKey)
            .Select(f => byPath[f].Id)
            .ToHashSet();

        // Clean up database items whose files are no longer on disk BEFORE creating new ones,
        // otherwise a reused GUID can collide with the stale row.
        foreach (var item in inLibrary.Values.Where(x => !keepIds.Contains(x.Id)))
        {
            logger.LogWarning("[LocalIntrosExtended] Bestand niet langer gevonden op schijf. Item verwijderen: '{Path}'", item.Path);
            LocalIntrosPlugin.LibraryManager.DeleteItem(item, new DeleteOptions());
        }

        // Build lookups for GUID preservation
        var configByPath = config.DetectedLocalVideos
            .Where(v => !string.IsNullOrEmpty(v.Path) && v.ItemId != Guid.Empty)
            .GroupBy(v => v.Path, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().ItemId, StringComparer.OrdinalIgnoreCase);

        var configByName = config.DetectedLocalVideos
            .Where(v => !string.IsNullOrEmpty(v.Name) && v.ItemId != Guid.Empty)
            .GroupBy(v => v.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().ItemId, StringComparer.OrdinalIgnoreCase);

        var libraryResults = new Dictionary<Guid, BaseItem>();
        var usedIds = new HashSet<Guid>(keepIds);

        foreach (var file in filesOnDisk)
        {
            if (byPath.TryGetValue(file, out var existingItem))
            {
                libraryResults[existingItem.Id] = existingItem;
            }
            else
            {
                var normName = NormalizeVideoName(file);
                Guid assignedId = Guid.Empty;
                bool isReused = false;

                if (preserveGuids)
                {
                    if (configByPath.TryGetValue(file, out var pathGuid))
                    {
                        assignedId = pathGuid;
                        isReused = true;
                    }
                    else if (configByName.TryGetValue(normName, out var nameGuid))
                    {
                        assignedId = nameGuid;
                        isReused = true;
                    }
                }

                // Never hand out a GUID twice (two files with the same normalized name) or one
                // that still belongs to some other item in the database.
                if (assignedId != Guid.Empty
                    && (usedIds.Contains(assignedId) || LocalIntrosPlugin.LibraryManager.GetItemById(assignedId) is not null))
                {
                    assignedId = Guid.Empty;
                    isReused = false;
                }

                if (assignedId == Guid.Empty)
                {
                    assignedId = Guid.NewGuid();
                }

                usedIds.Add(assignedId);

                logger.LogInformation("[LocalIntrosExtended] Registreren van video '{Name}' met ID {Id} (Bestaande GUID hergebruikt: {IsReused})", normName, assignedId, isReused);

                var video = new Video
                {
                    Id = assignedId,
                    Path = file,
                    OfficialRating = "G",
                    ProviderIds = new Dictionary<string, string>
                    {
                        { ProviderKey, file }
                    },
                    Name = normName
                };

                LocalIntrosPlugin.LibraryManager.CreateItem(video, null);
                libraryResults[video.Id] = video;
            }
        }

        // Always update configuration safely WITHOUT wiping existing rules
        UpdateOptionsConfig(libraryResults.Values, logger);

        logger.LogInformation("[LocalIntrosExtended] Intro-bibliotheeksynchronisatie voltooid. Totaal {Count} actieve intro's.", libraryResults.Count);
        return libraryResults;
    }

    public static int ClearIntroLibrary(ILogger logger)
    {
        var inLibrary = RetrieveIntroLibrary();
        foreach (var item in inLibrary.Values)
        {
            logger.LogInformation("[LocalIntrosExtended] Verwijderen van {Path} uit bibliotheek.", item.Path);
            LocalIntrosPlugin.LibraryManager.DeleteItem(item, new DeleteOptions());
        }

        // Hard reset: also drop detected videos and rules, as the UI promises.
        var config = LocalIntrosPlugin.Instance.Configuration;
        config.DetectedLocalVideos = new List<IntroVideo>();
        config.Rules = new List<IntroRule>();
        LocalIntrosPlugin.Instance.SaveConfiguration();

        return inLibrary.Count;
    }

    private static void UpdateOptionsConfig(IEnumerable<BaseItem> libraryResults, ILogger logger)
    {
        var config = LocalIntrosPlugin.Instance.Configuration;

        config.DetectedLocalVideos = libraryResults.Select(x => new IntroVideo
        {
            ItemId = x.Id,
            Name = x.Name,
            Path = x.Path
        }).OrderBy(i => i.Name).ToList();

        var validVideoIds = config.DetectedLocalVideos.Select(x => x.ItemId).ToHashSet();

        // Validate rules references without wiping the rules themselves
        foreach (var rule in config.Rules)
        {
            rule.IntroIds.RemoveAll(id => !validVideoIds.Contains(id));
        }

        LocalIntrosPlugin.Instance.SaveConfiguration();
        logger.LogTrace("[LocalIntrosExtended] Configuratie veilig opgeslagen met {Count} gedetecteerde video's en {RuleCount} behouden regels.", config.DetectedLocalVideos.Count, config.Rules.Count);
    }
}

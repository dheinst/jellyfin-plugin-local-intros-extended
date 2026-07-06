using System;
using System.Collections.Generic;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.LocalIntrosExtended.Configuration;

public class IntroPluginConfiguration : BasePluginConfiguration
{
    public string Local { get; set; } = string.Empty;

    public List<IntroVideo> DetectedLocalVideos { get; set; } = new List<IntroVideo>();

    public List<IntroRule> Rules { get; set; } = new List<IntroRule>();
}

public class IntroVideo
{
    public string Name { get; set; }

    public Guid ItemId { get; set; }
}

public enum IntroTargetType
{
    All = 0,
    MoviesOnly = 1,
    EpisodesOnly = 2
}

public enum CurrentDateRepeatRangeType
{
    None,
    Weekly,
    Monthly,
    Yearly
}

public class IntroRule
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public List<Guid> IntroIds { get; set; } = new List<Guid>();
    public int Frequency { get; set; } = 100; // 0-100%

    // Conditions
    public List<string> Genres { get; set; } = new List<string>();
    public List<string> Tags { get; set; } = new List<string>();
    public List<string> Studios { get; set; } = new List<string>();
    public List<Guid> UserIds { get; set; } = new List<Guid>();
    public List<Guid> LibraryIds { get; set; } = new List<Guid>();
    public IntroTargetType TargetType { get; set; } = IntroTargetType.All;

    // Date conditions
    public DateTime? DateStart { get; set; }
    public DateTime? DateEnd { get; set; }
    public CurrentDateRepeatRangeType DateRepeatType { get; set; } = CurrentDateRepeatRangeType.None;

    public bool IsDateInRange(DateTime relevantDate)
    {
        if (DateStart == null || DateEnd == null) return true;
        var start = DateStart.Value;
        var end = DateEnd.Value;

        switch (DateRepeatType)
        {
            case CurrentDateRepeatRangeType.None:
                return relevantDate >= start && relevantDate <= end;
            case CurrentDateRepeatRangeType.Weekly:
                var currentDay = relevantDate.DayOfWeek;
                if (start.DayOfWeek <= end.DayOfWeek)
                    return currentDay >= start.DayOfWeek && currentDay <= end.DayOfWeek;
                else // Wraparound (e.g. Friday to Monday)
                    return currentDay >= start.DayOfWeek || currentDay <= end.DayOfWeek;
            case CurrentDateRepeatRangeType.Monthly:
                if (start.Day <= end.Day)
                    return relevantDate.Day >= start.Day && relevantDate.Day <= end.Day;
                else // Wraparound (e.g. 28th to 3rd)
                    return relevantDate.Day >= start.Day || relevantDate.Day <= end.Day;
            case CurrentDateRepeatRangeType.Yearly:
                var currentYear = relevantDate.Year;
                var pretendCurrentDate = new DateTime(currentYear, relevantDate.Month, relevantDate.Day);
                var pretendDateEnd = new DateTime(currentYear, end.Month, end.Day);
                var pretendDateStart = new DateTime(currentYear, start.Month, start.Day);
                if (pretendDateStart <= pretendDateEnd)
                    return pretendCurrentDate >= pretendDateStart && pretendCurrentDate <= pretendDateEnd;
                else // Wraparound over year boundary
                    return pretendCurrentDate >= pretendDateStart || pretendCurrentDate <= pretendDateEnd;
            default:
                return true;
        }
    }
}
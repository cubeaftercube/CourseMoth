// SPDX-License-Identifier: AGPL-3.0-or-later

using CourseMoth.Core.Abstractions;
using CourseMoth.Core.Domain;

namespace CourseMoth.Core.Services;

/// <summary>
/// Streaks, folded over the activity log on read. See TasksStreaksSpec §4.
///
/// A stored counter and a merged activity log disagree after sync with no way to reconcile them,
/// so nothing here is ever persisted.
/// </summary>
public sealed class StreakCalculator : IStreakCalculator
{
    private readonly IActivityRepository _activities;
    private readonly IClock _clock;
    private readonly DayBoundaryOptions _options;

    public StreakCalculator(IActivityRepository activities, IClock clock, DayBoundaryOptions options)
    {
        _activities = activities ?? throw new ArgumentNullException(nameof(activities));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public async Task<int> CurrentAsync(StreakContext context, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        // The context's boundary is authoritative for the streak: it is the setting in force now,
        // supplied by the composition root. The injected options are only the fallback for callers
        // that have no context to hand.
        var today = _clock.StudyDateFor(ActivityTracker.Truncate(_clock.UtcNow), context.Boundary);
        var yesterday = today.AddDays(-1);

        // Look back far enough to cover the longest plausible run; a decade of daily study is
        // only a few thousand rows, and a full scan here is the documented acceptable cost.
        var from = today.AddDays(-LookbackDays);
        var activities = await _activities.ListAsync(from, today, ct).ConfigureAwait(false);

        var qualifying = new HashSet<DateOnly>(
            activities.Where(activity => activity.CountsForStreak).Select(activity => activity.Date));

        // A day still in progress must not look like a broken streak: if yesterday qualified the
        // streak is alive, even before today has any activity. It only breaks once a whole day
        // passes with nothing in it.
        var cursor = qualifying.Contains(today) ? today : yesterday;

        var streak = 0;
        while (qualifying.Contains(cursor))
        {
            streak++;
            cursor = cursor.AddDays(-1);
        }

        return streak;
    }

    public async Task<int> LongestAsync(CancellationToken ct = default)
    {
        // DateOnly.MinValue is the only way to say "from the beginning" — the repository takes a
        // range, and there is no "all" overload.
        var activities = await _activities.ListAsync(DateOnly.MinValue, DateOnly.MaxValue, ct)
            .ConfigureAwait(false);

        var days = activities
            .Where(activity => activity.CountsForStreak)
            .Select(activity => activity.Date)
            .Distinct()
            .OrderBy(date => date)
            .ToList();

        var longest = 0;
        var run = 0;
        DateOnly? previous = null;

        foreach (var day in days)
        {
            run = previous is { } prior && prior.AddDays(1) == day ? run + 1 : 1;
            longest = Math.Max(longest, run);
            previous = day;
        }

        return longest;
    }

    public bool QualifiesForStreak(LearningActivity activity, DayBoundaryOptions options)
    {
        ArgumentNullException.ThrowIfNull(activity);
        ArgumentNullException.ThrowIfNull(options);

        return options.Rule switch
        {
            // Minutes threshold is in minutes; the log stores milliseconds. Multiplying both by
            // 60_000 first also avoids a double rounding surprise at the exact threshold.
            StreakRule.Minutes => activity.WatchedMs >= (long)options.MinutesThreshold * 60_000L,
            _ => activity.CompletedLessons >= 1,
        };
    }

    /// <summary>How far back the current-streak lookup scans — a decade of daily study.</summary>
    private const int LookbackDays = 3_660;
}

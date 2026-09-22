// SPDX-License-Identifier: AGPL-3.0-or-later

namespace CourseMoth.Core.Domain;

/// <summary>
/// One day of learning activity. One row per day that had any activity.
///
/// Streaks are derived from these rows and never stored — a stored counter would
/// disagree with a merged activity log after sync, with no way to reconcile. See TasksStreaksSpec §4.
/// </summary>
public class LearningActivity
{
    /// <summary>Local date, primary key.</summary>
    public DateOnly Date { get; set; }

    public int CompletedLessons { get; set; }

    /// <summary>Actual playback time, not wall-clock time with the app open.</summary>
    public long WatchedMs { get; set; }

    /// <summary>Whether this day satisfied the streak rule.</summary>
    public bool CountsForStreak { get; set; }

    public DateTime UpdatedAt { get; set; }
}

// SPDX-License-Identifier: AGPL-3.0-or-later

namespace CourseMoth.Core.Domain;

/// <summary>
/// How a lesson came to be marked complete. Tracked so that a manual decision can be
/// protected from being silently overwritten by threshold auto-completion.
/// </summary>
public enum CompletionSource
{
    /// <summary>Not completed, or completed by no mechanism yet.</summary>
    None,

    /// <summary>Watch position crossed the course threshold.</summary>
    Threshold,

    /// <summary>The user marked it manually.</summary>
    Manual,

    /// <summary>The user marked the whole course complete.</summary>
    CourseMarked,
}

/// <summary>
/// Watch progress for one lesson. The centre of the system and, by a wide margin,
/// the most conflict-prone entity — it changes every few seconds during playback.
/// See DomainMap §3.4.
/// </summary>
public class WatchState
{
    /// <summary>Primary key: one lesson, one state.</summary>
    public Guid LessonId { get; set; }

    public long PositionMs { get; set; }

    /// <summary>0..1. Cached projection of position/duration; recomputable.</summary>
    public double ProgressPercent { get; set; }

    public bool IsCompleted { get; set; }
    public CompletionSource CompletionSource { get; set; } = CompletionSource.None;
    public DateTime? CompletedAt { get; set; }

    /// <summary>
    /// Set when the user manually unmarks a lesson. While true, threshold auto-completion
    /// is suppressed — otherwise "mark as not completed" could never hold, because two more
    /// seconds of playback would cross the threshold and put the checkmark straight back.
    /// </summary>
    public bool ManualUnmark { get; set; }

    public DateTime UpdatedAt { get; set; }
    public Guid LastDeviceId { get; set; }
}

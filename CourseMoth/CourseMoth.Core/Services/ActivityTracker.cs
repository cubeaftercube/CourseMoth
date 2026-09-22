// SPDX-License-Identifier: AGPL-3.0-or-later

using CourseMoth.Core.Abstractions;
using CourseMoth.Core.Domain;

namespace CourseMoth.Core.Services;

/// <summary>
/// Reads and writes the single day's <see cref="LearningActivity"/> row, computing the study
/// date through <see cref="IClock"/> so the day boundary is honoured everywhere at once.
///
/// Every write to the activity log goes through here. A <c>DateTime.Today</c> anywhere in the
/// codebase would silently disagree with a 04:00 boundary and break streaks for exactly the
/// users who study after midnight.
/// </summary>
public sealed class ActivityTracker
{
    /// <summary>
    /// A gap larger than this between reported positions is a seek, not watching.
    /// See TasksStreaksSpec §2.
    /// </summary>
    private const long MaxCountedDeltaMs = 2_000;

    private readonly IActivityRepository _activities;
    private readonly IClock _clock;
    private readonly DayBoundaryOptions _options;

    public ActivityTracker(IActivityRepository activities, IClock clock, DayBoundaryOptions options)
    {
        _activities = activities ?? throw new ArgumentNullException(nameof(activities));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>The study date an instant belongs to, under the configured boundary.</summary>
    public DateOnly StudyDate(DateTimeOffset instant) => _clock.StudyDateFor(instant, _options);

    /// <summary>
    /// The study date of "now". The instant is rounded down to the whole millisecond first: a
    /// sub-millisecond tick cannot change the answer, and the rounding keeps the value
    /// reproducible when a fake clock hands back an exact instant.
    /// </summary>
    public DateOnly Today() => StudyDate(Truncate(_clock.UtcNow));

    /// <summary>
    /// Wraps a UTC instant in a <see cref="DateTimeOffset"/> without moving it. <c>new
    /// DateTimeOffset(dateTime)</c> would apply the machine's local offset and shift the instant by
    /// hours, which silently lands activity on the wrong study day for anyone east or west of UTC.
    /// </summary>
    public static DateTimeOffset Truncate(DateTime utcNow)
    {
        var utc = utcNow.Kind == DateTimeKind.Utc ? utcNow : DateTime.SpecifyKind(utcNow, DateTimeKind.Utc);
        var truncated = new DateTime(utc.Ticks - (utc.Ticks % TimeSpan.TicksPerMillisecond), DateTimeKind.Utc);

        return new DateTimeOffset(truncated);
    }

    public Task<LearningActivity?> GetAsync(DateOnly date, CancellationToken ct)
        => _activities.GetAsync(date, ct);

    /// <summary>Fetches today's row, falling back to an unsaved one when the day has no activity yet.</summary>
    public async Task<LearningActivity> GetOrCreateAsync(DateOnly date, CancellationToken ct)
    {
        var existing = await _activities.GetAsync(date, ct).ConfigureAwait(false);
        if (existing is not null)
        {
            return existing;
        }

        return new LearningActivity
        {
            Date = date,
            UpdatedAt = _clock.UtcNow,
        };
    }

    public Task SaveAsync(LearningActivity activity, CancellationToken ct)
    {
        activity.UpdatedAt = _clock.UtcNow;
        return _activities.SaveAsync(activity, ct);
    }

    /// <summary>
    /// Accumulates watch time for a position report. Position deltas above
    /// <see cref="MaxCountedDeltaMs"/> are seeks and contribute nothing.
    /// </summary>
    public void AccumulateWatchTime(LearningActivity activity, long deltaMs)
    {
        if (deltaMs > 0 && deltaMs <= MaxCountedDeltaMs)
        {
            activity.WatchedMs += deltaMs;
        }
    }

    /// <summary>Records a lesson completion against the day.</summary>
    public void RecordLessonCompleted(LearningActivity activity)
        => activity.CompletedLessons++;
}

// SPDX-License-Identifier: AGPL-3.0-or-later

using CourseMoth.Core.Abstractions;
using CourseMoth.Core.Domain;

namespace CourseMoth.Core.Services;

/// <summary>
/// The single writer of <see cref="WatchState"/>. Media reports what happened — "position 12:34",
/// "playback ended" — and this service decides what that means. See DomainMap §3.4 and PlayerSpec §7.
///
/// The rule most easily got wrong: a manual "mark incomplete" must be able to hold. Without
/// <see cref="WatchState.ManualUnmark"/> two more seconds of playback would cross the threshold
/// and put the checkmark straight back, making "incomplete" impossible to express.
/// </summary>
public sealed class WatchStateService : IWatchStateService
{
    /// <summary>Threshold used when a lesson's course cannot be resolved.</summary>
    private const double DefaultCompletionThreshold = 0.9;

    private readonly IWatchStateRepository _watchStates;
    private readonly ILessonRepository _lessons;
    private readonly ICourseRepository _courses;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;
    private readonly TimeProvider _timeProvider;
    private readonly ActivityTracker _activity;
    private readonly Guid _deviceId;

    public WatchStateService(
        IWatchStateRepository watchStates,
        ILessonRepository lessons,
        ICourseRepository courses,
        IUnitOfWork unitOfWork,
        IClock clock,
        TimeProvider timeProvider,
        ActivityTracker activity,
        Guid? deviceId = null)
    {
        _watchStates = watchStates ?? throw new ArgumentNullException(nameof(watchStates));
        _lessons = lessons ?? throw new ArgumentNullException(nameof(lessons));
        _courses = courses ?? throw new ArgumentNullException(nameof(courses));
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _activity = activity ?? throw new ArgumentNullException(nameof(activity));
        _deviceId = deviceId ?? Guid.Empty;
    }

    public async Task<WatchState> GetOrCreateAsync(Guid lessonId, CancellationToken ct = default)
    {
        var existing = await _watchStates.GetAsync(lessonId, ct).ConfigureAwait(false);
        if (existing is not null)
        {
            return existing;
        }

        return new WatchState
        {
            LessonId = lessonId,
            PositionMs = 0,
            ProgressPercent = 0d,
            IsCompleted = false,
            CompletionSource = CompletionSource.None,
            ManualUnmark = false,
            UpdatedAt = _clock.UtcNow,
            LastDeviceId = _deviceId,
        };
    }

    public async Task<WatchState> ReportPositionAsync(
        Guid lessonId,
        TimeSpan position,
        TimeSpan? duration,
        CancellationToken ct = default)
    {
        var state = await _watchStates.GetAsync(lessonId, ct).ConfigureAwait(false)
            ?? await GetOrCreateAsync(lessonId, ct).ConfigureAwait(false);

        var lesson = await _lessons.GetAsync(lessonId, ct).ConfigureAwait(false);
        var course = lesson is null ? null : await _courses.GetAsync(lesson.CourseId, ct).ConfigureAwait(false);

        var durationMs = ResolveDurationMs(duration, lesson);

        var positionMs = ClampMs(position);
        var deltaMs = positionMs - state.PositionMs;

        state.PositionMs = positionMs;
        state.ProgressPercent = durationMs > 0
            ? Math.Clamp((double)positionMs / durationMs, 0d, 1d)
            : 0d;

        // Measured against the stored position, so an ordinary periodic report of a second or two
        // accumulates while a seek contributes nothing. See TasksStreaksSpec §2.
        var today = await _activity.GetOrCreateAsync(_activity.Today(), ct).ConfigureAwait(false);
        _activity.AccumulateWatchTime(today, deltaMs);

        var threshold = course?.CompletionThreshold ?? DefaultCompletionThreshold;

        if (ShouldAutoComplete(state, durationMs, positionMs, threshold))
        {
            Complete(state, CompletionSource.Threshold, today);
        }

        // The duration is filled lazily — this report is often the first time anyone learns it.
        if (lesson is not null && durationMs > 0 && lesson.Duration != duration)
        {
            lesson.Duration = TimeSpan.FromMilliseconds(durationMs);
            lesson.UpdatedAt = _clock.UtcNow;
            await _lessons.UpdateAsync(lesson, ct).ConfigureAwait(false);
        }

        Touch(state);

        await _watchStates.SaveAsync(state, ct).ConfigureAwait(false);
        await _activity.SaveAsync(today, ct).ConfigureAwait(false);
        await _unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);

        return state;
    }

    public async Task<WatchState> ReportEndedAsync(Guid lessonId, CancellationToken ct = default)
    {
        var state = await GetOrCreateAsync(lessonId, ct).ConfigureAwait(false);
        var today = await _activity.GetOrCreateAsync(_activity.Today(), ct).ConfigureAwait(false);

        if (!state.IsCompleted)
        {
            // Reaching the end is an explicit completion signal, never inferred from position:
            // engines routinely reset Position to zero as the ended event fires. See PlayerSpec §6.
            Complete(state, CompletionSource.Threshold, today);
        }

        // The user watched to the end. That outranks an earlier manual unmark, and clearing the
        // flag is the explicit action that ends the protection from auto-completion.
        state.ManualUnmark = false;

        Touch(state);

        await _watchStates.SaveAsync(state, ct).ConfigureAwait(false);
        await _activity.SaveAsync(today, ct).ConfigureAwait(false);
        await _unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);

        return state;
    }

    public async Task<WatchState> MarkCompletedAsync(Guid lessonId, CancellationToken ct = default)
    {
        var state = await GetOrCreateAsync(lessonId, ct).ConfigureAwait(false);
        var today = await _activity.GetOrCreateAsync(_activity.Today(), ct).ConfigureAwait(false);

        // An explicit user action clears the protection.
        state.ManualUnmark = false;

        if (state.IsCompleted)
        {
            state.CompletionSource = CompletionSource.Manual;
        }
        else
        {
            Complete(state, CompletionSource.Manual, today);
        }

        Touch(state);

        await _watchStates.SaveAsync(state, ct).ConfigureAwait(false);
        await _activity.SaveAsync(today, ct).ConfigureAwait(false);
        await _unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);

        return state;
    }

    public async Task<WatchState> MarkIncompleteAsync(Guid lessonId, CancellationToken ct = default)
    {
        var state = await GetOrCreateAsync(lessonId, ct).ConfigureAwait(false);

        state.IsCompleted = false;
        state.CompletionSource = CompletionSource.None;
        state.CompletedAt = null;

        // This flag is the whole point: it suppresses threshold auto-completion so a few more
        // seconds of playback cannot put the checkmark back. Position is deliberately kept —
        // unmarking is a statement about completion, not about where the user is in the video.
        state.ManualUnmark = true;

        Touch(state);

        await _watchStates.SaveAsync(state, ct).ConfigureAwait(false);
        await _unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);

        return state;
    }

    public async Task MarkCourseCompletedAsync(Guid courseId, CancellationToken ct = default)
    {
        var lessons = await _lessons.ListByCourseAsync(courseId, ct).ConfigureAwait(false);
        if (lessons.Count == 0)
        {
            return;
        }

        var today = await _activity.GetOrCreateAsync(_activity.Today(), ct).ConfigureAwait(false);
        var wrote = false;

        foreach (var lesson in lessons)
        {
            var state = await GetOrCreateAsync(lesson.Id, ct).ConfigureAwait(false);

            // A lesson that is already complete is left exactly as it is. Re-stamping it to
            // CourseMarked would overwrite the record of how it was actually finished — a lesson
            // watched to the end and a lesson ticked by hand are different facts, and "mark the
            // course complete" is a bulk convenience, not a claim about each individual lesson.
            // It would also credit the day for lessons that were already done, inflating the
            // activity count and through it the streak. See DomainMap §9 and PlayerSpec §7.
            if (state.IsCompleted)
            {
                continue;
            }

            Complete(state, CompletionSource.CourseMarked, today);
            Touch(state);

            await _watchStates.SaveAsync(state, ct).ConfigureAwait(false);
            wrote = true;
        }

        if (!wrote)
        {
            return;
        }

        await _activity.SaveAsync(today, ct).ConfigureAwait(false);
        await _unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public bool IsProtectedFromAutoCompletion(WatchState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        return state.ManualUnmark;
    }

    /// <summary>
    /// Marks a lesson complete and credits the day. Completion is counted once per transition —
    /// the guard is the caller's, so repeated reports at the end of a video do not inflate the log.
    /// </summary>
    private void Complete(WatchState state, CompletionSource source, LearningActivity today)
    {
        state.IsCompleted = true;
        state.CompletionSource = source;
        state.CompletedAt = _clock.UtcNow;
        _activity.RecordLessonCompleted(today);
    }

    private void Touch(WatchState state)
    {
        state.UpdatedAt = _clock.UtcNow;
        state.LastDeviceId = _deviceId;
    }

    /// <summary>
    /// The threshold rule: <c>position / duration &gt;= course.CompletionThreshold</c>, and never
    /// while the user's manual unmark is in force.
    /// </summary>
    private static bool ShouldAutoComplete(
        WatchState state,
        long durationMs,
        long positionMs,
        double threshold)
    {
        if (state.IsCompleted || state.ManualUnmark || durationMs <= 0)
        {
            return false;
        }

        return (double)positionMs / durationMs >= threshold;
    }

    /// <summary>
    /// The engine's duration wins when it has one — it is the value from the file actually playing
    /// — and the lesson's lazily filled duration covers the reports that arrive without one.
    /// </summary>
    private static long ResolveDurationMs(TimeSpan? reported, Lesson? lesson)
    {
        if (reported is { Ticks: > 0 } known)
        {
            return (long)known.TotalMilliseconds;
        }

        if (lesson?.Duration is { Ticks: > 0 } stored)
        {
            return (long)stored.TotalMilliseconds;
        }

        return 0;
    }

    private static long ClampMs(TimeSpan position)
        => position <= TimeSpan.Zero ? 0 : (long)position.TotalMilliseconds;
}

// SPDX-License-Identifier: AGPL-3.0-or-later

using CourseMoth.Core.Abstractions;
using CourseMoth.Core.Domain;
using CourseMoth.Core.Services;
using CourseMoth.Core.Tests.Fakes;
using Xunit;

namespace CourseMoth.Core.Tests;

/// <summary>
/// The completion rules of PlayerSpec §6–§7 and DomainMap §3.4.
///
/// These are the rules the documentation singles out as easy to get wrong, and the ones a bug
/// in would be invisible until a user noticed a checkmark they had deliberately cleared.
/// </summary>
public sealed class WatchStateServiceTests
{
    private static readonly Guid Lesson = Guid.NewGuid();

    private sealed record Harness(
        WatchStateService Service,
        InMemoryRepositories Repos,
        FakeClock Clock,
        Guid Course,
        Guid LessonId);

    /// <summary>
    /// Builds the service over the fakes, with one course and one ten-minute lesson.
    /// Every test gets its own instance: shared state between tests is the fastest route to a
    /// suite that only passes in one order.
    /// </summary>
    private static Harness Build(
        double completionThreshold = 0.9,
        Guid? deviceId = null,
        TimeSpan? lessonDuration = null)
    {
        var repos = new InMemoryRepositories();
        var clock = new FakeClock(new DateTimeOffset(2026, 3, 10, 12, 0, 0, TimeSpan.Zero));
        var lessonId = Guid.NewGuid();

        var course = Sample.Course(completionThreshold: completionThreshold);
        repos.Courses.Seed(course);

        var lesson = Sample.Lesson(
            course.Id,
            id: lessonId,
            duration: lessonDuration,
            relativePath: "01 Lesson.mp4");
        repos.Lessons.Seed(lesson);

        var service = new WatchStateService(
            repos.WatchStates,
            repos.Lessons,
            repos.Courses,
            repos.UnitOfWork,
            clock,
            TimeProvider.System,
            new ActivityTracker(repos.Activities, clock, new DayBoundaryOptions()),
            deviceId);

        return new Harness(service, repos, clock, course.Id, lessonId);
    }

    private static readonly TimeSpan Duration = TimeSpan.FromMinutes(10);

    // ---- The single most important test in the suite ----------------------------------------

    [Fact]
    public async Task Manually_unmarked_lesson_stays_incomplete_after_crossing_threshold()
    {
        var h = Build();
        var lesson = h.LessonId;

        // Given: the user watched past the threshold, so the lesson auto-completed.
        await h.Service.ReportPositionAsync(lesson, TimeSpan.FromMinutes(9.5), Duration);
        var afterWatching = await h.Service.GetOrCreateAsync(lesson);
        Assert.True(afterWatching.IsCompleted, "precondition: 95% of a 90% course completes the lesson");

        // When: the user says "no, I did not finish this", then keeps playing well past the end.
        await h.Service.MarkIncompleteAsync(lesson);
        await h.Service.ReportPositionAsync(lesson, TimeSpan.FromMinutes(9.6), Duration);
        await h.Service.ReportPositionAsync(lesson, TimeSpan.FromMinutes(9.9), Duration);
        var final = await h.Service.GetOrCreateAsync(lesson);

        // Then: the manual decision holds. Without the ManualUnmark guard two more seconds of
        // playback would cross the threshold and put the checkmark straight back.
        Assert.False(final.IsCompleted);
        Assert.Equal(CompletionSource.None, final.CompletionSource);
        Assert.True(final.ManualUnmark);
    }

    [Fact]
    public async Task Manually_unmarked_lesson_is_still_protected_after_reopening_the_player()
    {
        var h = Build();
        var lesson = h.LessonId;

        await h.Service.ReportPositionAsync(lesson, TimeSpan.FromMinutes(9.5), Duration);
        await h.Service.MarkIncompleteAsync(lesson);

        // A new session: the state is re-read from the repository rather than carried in memory.
        var reloaded = await h.Service.GetOrCreateAsync(lesson);
        Assert.True(h.Service.IsProtectedFromAutoCompletion(reloaded));

        await h.Service.ReportPositionAsync(lesson, TimeSpan.FromMinutes(10), Duration);

        Assert.False(reloaded.IsCompleted);
    }

    [Fact]
    public async Task Marking_incomplete_keeps_the_position()
    {
        var h = Build();
        var lesson = h.LessonId;

        await h.Service.ReportPositionAsync(lesson, TimeSpan.FromMinutes(4), Duration);
        await h.Service.MarkIncompleteAsync(lesson);
        var state = await h.Service.GetOrCreateAsync(lesson);

        // Unmarking is a statement about completion, not about where the user is in the video.
        Assert.Equal(TimeSpan.FromMinutes(4).TotalMilliseconds, state.PositionMs);
    }

    [Fact]
    public async Task Manually_marking_complete_clears_the_protection_and_credits_the_source()
    {
        var h = Build();
        var lesson = h.LessonId;

        await h.Service.MarkIncompleteAsync(lesson);
        Assert.True((await h.Service.GetOrCreateAsync(lesson)).ManualUnmark);

        var state = await h.Service.MarkCompletedAsync(lesson);

        Assert.True(state.IsCompleted);
        Assert.Equal(CompletionSource.Manual, state.CompletionSource);
        Assert.False(state.ManualUnmark, "an explicit user action ends the protection");
    }

    // ---- Position at end of media ------------------------------------------------------------

    [Fact]
    public async Task Playback_ended_completes_unconditionally_on_a_barely_started_lesson()
    {
        var h = Build();

        await h.Service.ReportPositionAsync(h.LessonId, TimeSpan.FromSeconds(5), Duration);
        var state = await h.Service.ReportEndedAsync(h.LessonId);

        // Reaching the end is explicit, never inferred from position: 0.8% of the file is not
        // enough to prove anything, but the ended event is.
        Assert.True(state.IsCompleted);
        Assert.Equal(CompletionSource.Threshold, state.CompletionSource);
    }

    [Fact]
    public async Task Playback_ended_does_not_rewrite_a_position_the_engine_reset_to_zero()
    {
        var h = Build();
        var lesson = h.LessonId;

        // Given: the position was captured before the engine reset it. The stored position is
        // the end of the media, not 0:00.
        await h.Service.ReportPositionAsync(lesson, Duration, Duration);
        var beforeEnd = await h.Service.GetOrCreateAsync(lesson);
        Assert.Equal(Duration.TotalMilliseconds, beforeEnd.PositionMs);

        // When: PlaybackEnded fires, and the engine reports nothing further.
        await h.Service.ReportEndedAsync(lesson);
        var afterEnd = await h.Service.GetOrCreateAsync(lesson);

        // Then: reading the position after the event must not have written a zero over it.
        Assert.Equal(Duration.TotalMilliseconds, afterEnd.PositionMs);
        Assert.True(afterEnd.IsCompleted);
    }

    [Fact]
    public async Task Playback_ended_clears_a_manual_unmark_when_the_user_watches_to_the_end()
    {
        var h = Build();
        var lesson = h.LessonId;

        // A deliberate (and documented) tension: PlayerSpec §7 says the session-level suppression
        // holds, while the service treats reaching the very end as a later explicit user action.
        // The behaviour asserted here is the service's stated contract; the two readings differ.
        await h.Service.MarkIncompleteAsync(lesson);
        var state = await h.Service.ReportEndedAsync(lesson);

        Assert.True(state.IsCompleted);
        Assert.False(state.ManualUnmark);
    }

    // ---- The threshold itself ----------------------------------------------------------------

    [Theory]
    [InlineData(89, false)]
    [InlineData(90, true)]
    [InlineData(100, true)]
    public async Task Lesson_completes_when_the_position_reaches_the_course_threshold(
        int percentOfDuration,
        bool expectedCompletion)
    {
        var h = Build();
        var position = TimeSpan.FromMinutes(10 * percentOfDuration / 100d);

        var state = await h.Service.ReportPositionAsync(h.LessonId, position, Duration);

        Assert.Equal(expectedCompletion, state.IsCompleted);
    }

    [Fact]
    public async Task Threshold_is_the_course_value_not_a_global_constant()
    {
        var h = Build(completionThreshold: 0.5);

        // 60% would be incomplete on the default 0.9 course; this course asks for 50%.
        var state = await h.Service.ReportPositionAsync(h.LessonId, TimeSpan.FromMinutes(6), Duration);

        Assert.True(state.IsCompleted);
        Assert.Equal(0.6, state.ProgressPercent, precision: 6);
    }

    [Fact]
    public async Task No_completion_is_inferred_when_the_duration_is_unknown()
    {
        var h = Build();

        // Without a duration there is no ratio to compare, so nothing can be concluded from
        // position alone. The lesson's own duration is not known either.
        var state = await h.Service.ReportPositionAsync(h.LessonId, TimeSpan.FromMinutes(30), null);

        Assert.False(state.IsCompleted);
        Assert.Equal(0d, state.ProgressPercent);
    }

    [Fact]
    public async Task Completion_is_credited_to_the_day_exactly_once()
    {
        var h = Build();
        var lesson = h.LessonId;

        await h.Service.ReportPositionAsync(lesson, TimeSpan.FromMinutes(9.5), Duration);
        await h.Service.ReportPositionAsync(lesson, TimeSpan.FromMinutes(9.7), Duration);
        await h.Service.ReportPositionAsync(lesson, TimeSpan.FromMinutes(9.9), Duration);
        await h.Service.ReportEndedAsync(lesson);

        var activity = await h.Repos.Activities.GetAsync(h.Clock.Today(new DayBoundaryOptions()));

        Assert.NotNull(activity);
        Assert.Equal(1, activity!.CompletedLessons);
    }

    // ---- Watch time --------------------------------------------------------------------------

    [Fact]
    public async Task Ordinary_position_reports_accumulate_watch_time()
    {
        var h = Build();
        var lesson = h.LessonId;

        // The player reports every few seconds; the deltas are small and positive.
        await h.Service.ReportPositionAsync(lesson, TimeSpan.FromSeconds(2), Duration);
        await h.Service.ReportPositionAsync(lesson, TimeSpan.FromSeconds(4), Duration);
        await h.Service.ReportPositionAsync(lesson, TimeSpan.FromSeconds(6), Duration);

        var activity = await h.Repos.Activities.GetAsync(h.Clock.Today(new DayBoundaryOptions()));

        Assert.NotNull(activity);
        Assert.Equal(6_000, activity!.WatchedMs);
    }

    /// <summary>
    /// TasksStreaksSpec §2: accumulate the delta between reported positions where it is positive
    /// and small; a jump is a seek and counts nothing.
    ///
    /// <para>
    /// The test establishes a baseline the way an ordinary session does — a few small periodic
    /// reports — before the jump, so it is the seek rule itself under test. The judgement call
    /// §2 leaves open, whether the very first report of a session counts at all, is asserted
    /// separately in <see cref="A_large_first_position_report_of_a_session_is_treated_as_a_seek"/>
    /// and <see cref="A_small_first_position_report_of_a_session_is_counted"/>.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_seek_does_not_count_as_watching()
    {
        var h = Build();
        var lesson = h.LessonId;

        // Establish a baseline the way an ordinary session does: a few small periodic reports.
        await h.Service.ReportPositionAsync(lesson, TimeSpan.FromSeconds(2), Duration);
        await h.Service.ReportPositionAsync(lesson, TimeSpan.FromSeconds(4), Duration);
        await h.Service.ReportPositionAsync(lesson, TimeSpan.FromSeconds(6), Duration);

        var beforeSeek = await h.Repos.Activities.GetAsync(h.Clock.Today(new DayBoundaryOptions()));
        Assert.NotNull(beforeSeek);
        Assert.Equal(6_000, beforeSeek!.WatchedMs);

        // Jumping from 0:06 to 25:00 is a seek, not 25 minutes of attention.
        await h.Service.ReportPositionAsync(lesson, TimeSpan.FromMinutes(25), Duration);

        var afterSeek = await h.Repos.Activities.GetAsync(h.Clock.Today(new DayBoundaryOptions()));

        Assert.NotNull(afterSeek);
        Assert.Equal(6_000, afterSeek!.WatchedMs);
    }

    /// <summary>
    /// The same instant as <see cref="A_seek_does_not_count_as_watching"/> but without the
    /// baseline: the first report of a session arrives at 0:30 and is itself larger than the
    /// two-second seek threshold.
    ///
    /// <para>
    /// The implementation counts nothing here (it treats the storage default of 0 as the last
    /// position, so 0:30 reads as a 30-second seek), and <see cref="WatchStateService"/> offers no
    /// baseline concept to say otherwise. This test records that behaviour rather than endorsing
    /// it. The reason it matters: a user who resumes a video at 0:30 has just lost 30 seconds of
    /// watch time, while a user who resumes at 0:25:00 loses a quarter of an hour. See
    /// TasksStreaksSpec §2 and the report accompanying this suite — the spec does not settle
    /// whether an unobserved 0 baseline may be counted from.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_large_first_position_report_of_a_session_is_treated_as_a_seek()
    {
        var h = Build();

        // Nothing has been reported before, so the stored position is still the default of 0.
        await h.Service.ReportPositionAsync(h.LessonId, TimeSpan.FromSeconds(30), Duration);

        var activity = await h.Repos.Activities.GetAsync(h.Clock.Today(new DayBoundaryOptions()));

        Assert.NotNull(activity);
        Assert.Equal(0, activity!.WatchedMs);
    }

    /// <summary>
    /// The other half of the same judgement call: a first report small enough to be ordinary
    /// playback does accumulate. Together with the previous test this pins the boundary at
    /// <c>MaxCountedDeltaMs</c> (2 s) rather than at "the first report of a session".
    /// </summary>
    [Fact]
    public async Task A_small_first_position_report_of_a_session_is_counted()
    {
        var h = Build();

        await h.Service.ReportPositionAsync(h.LessonId, TimeSpan.FromSeconds(1), Duration);

        var activity = await h.Repos.Activities.GetAsync(h.Clock.Today(new DayBoundaryOptions()));

        Assert.NotNull(activity);
        Assert.Equal(1_000, activity!.WatchedMs);
    }

    // ---- Rescan / duration -------------------------------------------------------------------

    [Fact]
    public async Task A_report_with_a_duration_fills_the_lesson_duration_in_lazily()
    {
        var h = Build();

        await h.Service.ReportPositionAsync(h.LessonId, TimeSpan.FromSeconds(10), Duration);

        var lesson = await h.Repos.Lessons.GetAsync(h.LessonId);
        Assert.NotNull(lesson);
        Assert.Equal(Duration, lesson!.Duration);
    }

    // ---- Marking a whole course --------------------------------------------------------------

    [Fact]
    public async Task Marking_a_course_complete_completes_every_lesson_and_credits_them()
    {
        var repos = new InMemoryRepositories();
        var clock = new FakeClock(new DateTimeOffset(2026, 3, 10, 12, 0, 0, TimeSpan.Zero));
        var course = Sample.Course();

        repos.Courses.Seed(course);

        var lessons = new[] { Sample.Lesson(course.Id), Sample.Lesson(course.Id, order: 2), Sample.Lesson(course.Id, order: 3) };
        await repos.Lessons.AddRangeAsync(lessons);

        var service = new WatchStateService(
            repos.WatchStates,
            repos.Lessons,
            repos.Courses,
            repos.UnitOfWork,
            clock,
            TimeProvider.System,
            new ActivityTracker(repos.Activities, clock, new DayBoundaryOptions()));

        await service.MarkCourseCompletedAsync(course.Id);

        var today = await repos.Activities.GetAsync(clock.Today(new DayBoundaryOptions()));
        Assert.NotNull(today);
        Assert.Equal(3, today!.CompletedLessons);

        foreach (var lesson in lessons)
        {
            var state = await repos.WatchStates.GetAsync(lesson.Id);
            Assert.NotNull(state);
            Assert.True(state!.IsCompleted);
            Assert.Equal(CompletionSource.CourseMarked, state.CompletionSource);
        }
    }

    [Fact]
    public async Task Marking_a_course_complete_leaves_an_already_completed_lesson_alone()
    {
        var repos = new InMemoryRepositories();
        var clock = new FakeClock(new DateTimeOffset(2026, 3, 10, 12, 0, 0, TimeSpan.Zero));
        var course = Sample.Course();
        repos.Courses.Seed(course);

        var lesson = Sample.Lesson(course.Id);
        repos.Lessons.Seed(lesson);

        // The user marked this one by hand earlier.
        var existing = Sample.WatchState(lesson.Id, isCompleted: true, completionSource: CompletionSource.Manual);
        repos.WatchStates.Seed(existing);

        var service = new WatchStateService(
            repos.WatchStates, repos.Lessons, repos.Courses, repos.UnitOfWork,
            clock, TimeProvider.System,
            new ActivityTracker(repos.Activities, clock, new DayBoundaryOptions()));

        await service.MarkCourseCompletedAsync(course.Id);

        var state = await repos.WatchStates.GetAsync(lesson.Id);
        Assert.Equal(CompletionSource.Manual, state!.CompletionSource);
        Assert.Equal(0, (await repos.Activities.GetAsync(clock.Today(new DayBoundaryOptions())))?.CompletedLessons ?? 0);
    }
}

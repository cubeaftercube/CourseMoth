// SPDX-License-Identifier: AGPL-3.0-or-later

using CourseMoth.Core.Abstractions;
using CourseMoth.Core.Domain;
using CourseMoth.Core.Services;
using CourseMoth.Core.Tests.Fakes;
using Xunit;

namespace CourseMoth.Core.Tests;

/// <summary>
/// TasksStreaksSpec §6 (automatic tasks), §7 (completing and uncompleting) and §8 (recalculation).
///
/// <c>CompletedValue</c> is a cached projection: if it drifts, recomputing from the underlying
/// records must restore it. A task whose stored progress cannot be reproduced is a bug.
/// </summary>
public sealed class TaskEvaluatorTests
{
    private static readonly DateOnly Today = new(2026, 3, 10);
    private static readonly DateTimeOffset Noon = new(2026, 3, 10, 12, 0, 0, TimeSpan.Zero);

    private sealed record Harness(
        TaskEvaluator Evaluator,
        InMemoryRepositories Repos,
        DayBoundaryOptions Options,
        Guid CourseId);

    private static async Task<Harness> BuildAsync(
        int lessonCount = 4,
        int completedLessons = 0,
        DayBoundaryOptions? options = null,
        CourseStatus courseStatus = CourseStatus.NotStarted,
        DateTime? lastOpenedAt = null)
    {
        var effective = options ?? new DayBoundaryOptions();
        var repos = new InMemoryRepositories();
        var clock = new FakeClock(Noon);

        var course = Sample.Course(status: courseStatus, lastOpenedAt: lastOpenedAt);
        repos.Courses.Seed(course);

        var lessons = Enumerable.Range(1, lessonCount)
            .Select(order => Sample.Lesson(course.Id, order: order, relativePath: $"{order:D2}.mp4"))
            .ToList();

        await repos.Lessons.AddRangeAsync(lessons);

        for (var i = 0; i < completedLessons; i++)
        {
            await repos.WatchStates.SaveAsync(Sample.WatchState(lessons[i].Id, isCompleted: true));
        }

        var progress = new ProgressCalculator(repos.Courses, repos.Lessons, repos.WatchStates);

        var evaluator = new TaskEvaluator(
            repos.Tasks,
            repos.Courses,
            repos.Activities,
            progress,
            repos.UnitOfWork,
            clock,
            effective);

        return new Harness(evaluator, repos, effective, course.Id);
    }

    /// <summary>
    /// The one task on the board. Read back from the repository rather than taken from
    /// <c>EvaluateAsync</c>'s return value: the return value is the request-shaped answer to "what
    /// is due on this date", while the repository is the durable record the app re-reads.
    /// <c>Evaluation_is_idempotent</c> already pins the two together, so this keeps the
    /// projection assertions about what was stored.
    /// </summary>
    private static async Task<LearningTask> SingleStoredTaskAsync(Harness h)
        => Assert.Single(await h.Repos.Tasks.ListForDateAsync(Today));

    /// <summary>
    /// A task due on <paramref name="due"/>, on the calendar-day scale the repository stores on
    /// (<c>DueDate</c> is written as a <c>yyyyMMdd</c> date key, so midnight is the day's stamp).
    /// </summary>
    private static LearningTask Task(
        TaskGoalType goalType,
        double target,
        Guid? courseId = null,
        Guid? moduleId = null,
        TaskSourceType source = TaskSourceType.User,
        DateOnly? due = null,
        bool isCompleted = false,
        double completedValue = 0d)
        => new()
        {
            Id = Guid.NewGuid(),
            Title = $"{goalType} {target:0}",
            CourseId = courseId,
            ModuleId = moduleId,
            Source = source,
            GoalType = goalType,
            TargetValue = target,
            CompletedValue = completedValue,
            DueDate = (due ?? Today).ToDateTime(TimeOnly.MinValue),
            IsCompleted = isCompleted,
            CreatedAt = Sample.Created,
            UpdatedAt = Sample.Created,
        };

    // ---- Automatic generation is idempotent ---------------------------------------------------

    [Fact]
    public async Task Generating_automatic_tasks_twice_for_the_same_day_does_not_duplicate_them()
    {
        var h = await BuildAsync();

        var first = await h.Evaluator.GenerateAutomaticTasksAsync(Today);
        var second = await h.Evaluator.GenerateAutomaticTasksAsync(Today);

        // The app is opened many times a day. "On first open of a new day" is the rule; every
        // subsequent open must be a no-op, not another set of obligations.
        Assert.NotEmpty(first);
        Assert.Equal(first.Count, second.Count);
        Assert.Equal(first.Select(t => t.Id), second.Select(t => t.Id));
        Assert.Equal(first.Count, h.Repos.Tasks.All.Count);
    }

    [Fact]
    public async Task Generating_automatic_tasks_three_times_still_leaves_one_set()
    {
        var h = await BuildAsync();

        await h.Evaluator.GenerateAutomaticTasksAsync(Today);
        await h.Evaluator.GenerateAutomaticTasksAsync(Today);
        await h.Evaluator.GenerateAutomaticTasksAsync(Today);

        Assert.Equal(
            h.Repos.Tasks.All.Select(t => t.Id).Distinct().Count(),
            h.Repos.Tasks.All.Count);
    }

    [Fact]
    public async Task The_default_pattern_generates_a_daily_lesson_goal()
    {
        var h = await BuildAsync();
        var options = new DayBoundaryOptions { Rule = StreakRule.Lessons };

        var tasks = await h.Evaluator.GenerateAutomaticTasksAsync(Today);

        var dailyGoal = Assert.Single(tasks, t => t.GoalType == TaskGoalType.WatchLessons);
        Assert.Equal(TaskSourceType.Automatic, dailyGoal.Source);
        Assert.True(dailyGoal.TargetValue >= 1d);
    }

    [Fact]
    public async Task The_minutes_rule_generates_a_minute_goal_instead()
    {
        var h = await BuildAsync(options: new DayBoundaryOptions { Rule = StreakRule.Minutes });
        _ = h.Options;

        var tasks = await h.Evaluator.GenerateAutomaticTasksAsync(Today);

        Assert.Contains(tasks, t => t.GoalType == TaskGoalType.WatchMinutes);
        Assert.DoesNotContain(tasks, t => t.GoalType == TaskGoalType.WatchLessons);
    }

    [Fact]
    public async Task No_more_than_three_automatic_tasks_are_generated()
    {
        var h = await BuildAsync(courseStatus: CourseStatus.InProgress, lastOpenedAt: Noon.UtcDateTime.AddDays(-1));

        var tasks = await h.Evaluator.GenerateAutomaticTasksAsync(Today);

        // Beyond three they stop being goals and become a wall of unmet obligations.
        Assert.InRange(tasks.Count, 1, 3);
    }

    [Fact]
    public async Task A_continuation_task_is_generated_for_the_most_recently_opened_course()
    {
        var h = await BuildAsync(
            courseStatus: CourseStatus.InProgress,
            lastOpenedAt: Noon.UtcDateTime.AddDays(-1));

        var tasks = await h.Evaluator.GenerateAutomaticTasksAsync(Today);

        Assert.Contains(tasks, t => t.GoalType == TaskGoalType.ReachPercent && t.CourseId == h.CourseId);
    }

    [Fact]
    public async Task No_continuation_task_is_generated_when_nothing_is_in_progress()
    {
        var h = await BuildAsync();

        var tasks = await h.Evaluator.GenerateAutomaticTasksAsync(Today);

        Assert.DoesNotContain(tasks, t => t.GoalType == TaskGoalType.ReachPercent);
    }

    /// <summary>
    /// A new day gets its own set of tasks, over and above the previous day's.
    ///
    /// <para>
    /// The property under test is that day 2 <i>adds</i> a set rather than returning day 1's, and
    /// that both survive: "Completed automatic tasks are kept, not deleted at day end — the history
    /// is what makes the statistics screen possible" (TasksStreaksSpec §6). What it must not assert
    /// is that the two sets are disjoint. <c>TaskEvaluator.DueDateFor</c> stamps a study day's
    /// tasks at the boundary that <i>closes</i> it — 04:00 on the following calendar day
    /// (TasksStreaksSpec §3: "a study day ends when the next one begins") — so day 1's tasks are
    /// stamped at the very instant day 2's study day opens, and the repository can reach them from
    /// both days. Disjointness is not a contract the service can honour while stamping that way.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_second_day_gets_its_own_set_of_tasks()
    {
        var h = await BuildAsync();

        var firstDay = await h.Evaluator.GenerateAutomaticTasksAsync(Today);
        var nextDay = await h.Evaluator.GenerateAutomaticTasksAsync(Today.AddDays(1));

        Assert.NotEmpty(firstDay);

        // Day 2 generated its own new row rather than handing back day 1's.
        Assert.Single(nextDay);
        Assert.NotEqual(firstDay[0].Id, nextDay[0].Id);

        // Two rows exist, so day 2 added a set rather than overwriting the first: "Completed
        // automatic tasks are kept, not deleted at day end — the history is what makes the
        // statistics screen possible" (TasksStreaksSpec §6).
        Assert.Equal(firstDay.Count + nextDay.Count, h.Repos.Tasks.All.Count);
        Assert.Equal(2, h.Repos.Tasks.All.Select(task => task.Id).Distinct().Count());

        // Day 2's task is due one study day later than day 1's.
        Assert.True(nextDay[0].DueDate > firstDay[0].DueDate);

        var onFirstDay = await h.Repos.Tasks.ListForDateAsync(Today);
        var onSecondDay = await h.Repos.Tasks.ListForDateAsync(Today.AddDays(1));

        // Day 1's set is still readable on day 1 — the regression guard. When the repository
        // windowed on a plain calendar day it could not see the task the evaluator had just
        // written, so this returned nothing.
        Assert.Contains(onFirstDay, task => task.Id == firstDay[0].Id);

        // Day 2 is readable too, and holds day 2's own task.
        Assert.Contains(onSecondDay, task => task.Id == nextDay[0].Id);

        // Day 1's task is NOT reachable from day 2. The two sets are disjoint.
        //
        // This assertion used to read the opposite way, and it was wrong. DueDateFor used to stamp
        // a day's tasks at the boundary that closes it — 04:00 on the following calendar date — on
        // the reasoning that a study day ends when the next one begins. But a due date keyed to
        // the next calendar date is invisible to the day it belongs to: ListForDate(day 1)
        // returned nothing and day 1's task surfaced under day 2 instead. The stamp now sits on
        // the due date itself. The boundary hour still decides which study day a moment falls
        // into; it does not move deadlines.
        Assert.DoesNotContain(onSecondDay, task => task.Id == firstDay[0].Id);
        Assert.DoesNotContain(onFirstDay, task => task.Id == nextDay[0].Id);
    }

    // ---- Recalculation -------------------------------------------------------------------------

    [Fact]
    public async Task A_watch_lessons_task_completes_when_the_day_s_lessons_reach_the_target()
    {
        var h = await BuildAsync();
        var task = Task(TaskGoalType.WatchLessons, target: 2);
        await h.Repos.Tasks.AddRangeAsync([task]);

        h.Repos.Activities.SeedDay(Today, completedLessons: 2, watchedMs: 0);

        await h.Evaluator.EvaluateAsync(Today);

        var evaluated = await SingleStoredTaskAsync(h);
        Assert.Equal(2d, evaluated.CompletedValue);
        Assert.True(evaluated.IsCompleted);
        Assert.NotNull(evaluated.CompletedAt);
    }

    [Fact]
    public async Task A_watch_minutes_task_reports_partial_progress()
    {
        var h = await BuildAsync();
        var task = Task(TaskGoalType.WatchMinutes, target: 30);
        await h.Repos.Tasks.AddRangeAsync([task]);

        h.Repos.Activities.SeedDay(Today, completedLessons: 0, watchedMs: 10 * 60_000L);

        await h.Evaluator.EvaluateAsync(Today);

        var evaluated = await SingleStoredTaskAsync(h);
        Assert.Equal(10d, evaluated.CompletedValue, precision: 6);
        Assert.False(evaluated.IsCompleted);
    }

    [Fact]
    public async Task A_completed_lesson_task_stays_completed_when_the_metric_falls_back()
    {
        var h = await BuildAsync();
        var task = Task(TaskGoalType.WatchLessons, target: 1, isCompleted: true);
        await h.Repos.Tasks.AddRangeAsync([task]);

        // The lesson was completed and then unmarked. TasksStreaksSpec §7: progress only moves
        // forward within a day, so the finished task is not retroactively un-completed.
        h.Repos.Activities.SeedDay(Today, completedLessons: 0, watchedMs: 0);

        await h.Evaluator.EvaluateAsync(Today);

        var evaluated = await SingleStoredTaskAsync(h);
        Assert.True(evaluated.IsCompleted);
    }

    [Fact]
    public async Task A_user_task_is_never_recomputed_by_the_evaluator()
    {
        var h = await BuildAsync();
        var task = Task(TaskGoalType.WatchLessons, target: 5, source: TaskSourceType.User, isCompleted: true);
        await h.Repos.Tasks.AddRangeAsync([task]);

        h.Repos.Activities.SeedDay(Today, completedLessons: 1, watchedMs: 0);

        await h.Evaluator.EvaluateAsync(Today);

        // The user's own marking is theirs: the metric being below target does not undo it.
        Assert.True((await SingleStoredTaskAsync(h)).IsCompleted);
    }

    [Fact]
    public async Task A_complete_course_task_completes_when_every_lesson_is_done()
    {
        var h = await BuildAsync(lessonCount: 3, completedLessons: 3);
        var task = Task(TaskGoalType.CompleteCourse, target: 1, courseId: h.CourseId);
        await h.Repos.Tasks.AddRangeAsync([task]);

        await h.Evaluator.EvaluateAsync(Today);

        var evaluated = await SingleStoredTaskAsync(h);
        Assert.Equal(1d, evaluated.CompletedValue);
        Assert.True(evaluated.IsCompleted);
    }

    [Fact]
    public async Task A_complete_course_task_stays_incomplete_while_one_lesson_remains()
    {
        var h = await BuildAsync(lessonCount: 3, completedLessons: 2);
        var task = Task(TaskGoalType.CompleteCourse, target: 1, courseId: h.CourseId);
        await h.Repos.Tasks.AddRangeAsync([task]);

        await h.Evaluator.EvaluateAsync(Today);

        Assert.False((await SingleStoredTaskAsync(h)).IsCompleted);
    }

    [Fact]
    public async Task A_reach_percent_task_tracks_course_progress_in_percent()
    {
        var h = await BuildAsync(lessonCount: 4, completedLessons: 1);
        var task = Task(TaskGoalType.ReachPercent, target: 25, courseId: h.CourseId);
        await h.Repos.Tasks.AddRangeAsync([task]);

        await h.Evaluator.EvaluateAsync(Today);

        var evaluated = await SingleStoredTaskAsync(h);
        Assert.Equal(25d, evaluated.CompletedValue, precision: 6);
        Assert.True(evaluated.IsCompleted);
    }

    [Fact]
    public async Task A_day_with_no_activity_reports_zero_progress_rather_than_failing()
    {
        var h = await BuildAsync();
        var task = Task(TaskGoalType.WatchLessons, target: 2);
        await h.Repos.Tasks.AddRangeAsync([task]);

        await h.Evaluator.EvaluateAsync(Today);

        var evaluated = await SingleStoredTaskAsync(h);
        Assert.Equal(0d, evaluated.CompletedValue);
        Assert.False(evaluated.IsCompleted);
    }

    [Fact]
    public async Task Evaluating_a_day_with_no_tasks_returns_nothing()
    {
        var h = await BuildAsync();

        var result = await h.Evaluator.EvaluateAsync(Today);

        Assert.Empty(result);
    }

    [Fact]
    public async Task Evaluation_is_idempotent()
    {
        var h = await BuildAsync();
        var task = Task(TaskGoalType.WatchLessons, target: 2);
        await h.Repos.Tasks.AddRangeAsync([task]);
        h.Repos.Activities.SeedDay(Today, completedLessons: 1, watchedMs: 0);

        await h.Evaluator.EvaluateAsync(Today);
        var afterFirst = (await h.Repos.Tasks.ListForDateAsync(Today)).Single().CompletedValue;
        await h.Evaluator.EvaluateAsync(Today);
        var afterSecond = (await h.Repos.Tasks.ListForDateAsync(Today)).Single().CompletedValue;

        // Recomputing from the same records must land on the same answer — that is what makes
        // CompletedValue a projection rather than a source of truth.
        Assert.Equal(afterFirst, afterSecond);
    }

    [Fact]
    public async Task Yesterday_s_automatic_tasks_are_not_rewritten()
    {
        var h = await BuildAsync();
        var yesterday = Today.AddDays(-1);

        var task = Task(TaskGoalType.WatchLessons, target: 1, source: TaskSourceType.Automatic, due: yesterday);
        await h.Repos.Tasks.AddRangeAsync([task]);

        // Today's activity must not be projected backwards onto a day that has ended.
        h.Repos.Activities.SeedDay(Today, completedLessons: 5, watchedMs: 0);

        await h.Evaluator.EvaluateAsync(yesterday);

        var evaluated = Assert.Single(await h.Repos.Tasks.ListForDateAsync(yesterday));
        Assert.Equal(0d, evaluated.CompletedValue);
        Assert.False(evaluated.IsCompleted);
    }
}

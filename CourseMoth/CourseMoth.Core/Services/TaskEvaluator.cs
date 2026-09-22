// SPDX-License-Identifier: AGPL-3.0-or-later

using CourseMoth.Core.Abstractions;
using CourseMoth.Core.Domain;

namespace CourseMoth.Core.Services;

/// <summary>
/// Recomputes task progress from the underlying records. See TasksStreaksSpec §6 and §8.
///
/// <see cref="LearningTask.CompletedValue"/> is a cached projection, never a source of truth: if
/// it drifts, recomputing from <see cref="LearningActivity"/> and watch state must restore it.
/// A task whose stored progress cannot be reproduced from the underlying records is a bug.
/// </summary>
public sealed class TaskEvaluator : ITaskEvaluator
{
    /// <summary>TasksStreaksSpec §6: beyond three a day's goals become a wall of unmet obligations.</summary>
    private const int MaxAutomaticTasksPerDay = 3;

    private const double DefaultDailyLessonGoal = 2d;
    private const double DefaultDailyMinuteGoal = 30d;

    /// <summary>
    /// Target for the "Continue {course}" task. The spec does not define one; reaching the next
    /// half of the course is a concrete, reachable step rather than "finish it".
    /// </summary>
    private const double ContinuationTargetPercent = 50d;

    private readonly ITaskRepository _tasks;
    private readonly ICourseRepository _courses;
    private readonly IActivityRepository _activities;
    private readonly IProgressCalculator _progress;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;
    private readonly DayBoundaryOptions _options;

    public TaskEvaluator(
        ITaskRepository tasks,
        ICourseRepository courses,
        IActivityRepository activities,
        IProgressCalculator progress,
        IUnitOfWork unitOfWork,
        IClock clock,
        DayBoundaryOptions options)
    {
        _tasks = tasks ?? throw new ArgumentNullException(nameof(tasks));
        _courses = courses ?? throw new ArgumentNullException(nameof(courses));
        _activities = activities ?? throw new ArgumentNullException(nameof(activities));
        _progress = progress ?? throw new ArgumentNullException(nameof(progress));
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public async Task<IReadOnlyList<LearningTask>> EvaluateAsync(DateOnly date, CancellationToken ct = default)
    {
        var tasks = await _tasks.ListForDateAsync(date, ct).ConfigureAwait(false);
        if (tasks.Count == 0)
        {
            return tasks;
        }

        var activity = await _activities.GetAsync(date, ct).ConfigureAwait(false);
        var today = Today();

        var touched = new List<LearningTask>();

        // Course progress is resolved lazily and cached: a day's tasks commonly share one course.
        var courseProgress = new Dictionary<Guid, ProgressSnapshot>();

        foreach (var task in tasks)
        {
            // A finished task is frozen. TasksStreaksSpec §7 is explicit that progress only moves
            // forward within a day: an unmarked lesson must not retroactively un-complete a task
            // that was finished. See the report for how "was completed" is detected.
            if (IsFrozen(task, date, today))
            {
                continue;
            }

            var completedValue = task.GoalType switch
            {
                TaskGoalType.WatchLessons => CompletedLessons(activity),
                TaskGoalType.WatchMinutes => WatchedMinutes(activity),
                TaskGoalType.CompleteModule => await ModuleCompletedAsync(task, ct).ConfigureAwait(false),
                TaskGoalType.CompleteCourse => await CourseCompletedAsync(task, courseProgress, ct).ConfigureAwait(false),
                TaskGoalType.ReachPercent => await ReachPercentAsync(task, courseProgress, ct).ConfigureAwait(false),
                _ => task.CompletedValue,
            };

            var isComplete = completedValue >= task.TargetValue;

            if (task.CompletedValue == completedValue
                && task.IsCompleted == isComplete
                && (!isComplete || task.CompletedAt is not null))
            {
                continue;
            }

            task.CompletedValue = completedValue;
            task.IsCompleted = isComplete;
            task.CompletedAt = isComplete ? task.CompletedAt ?? _clock.UtcNow : null;
            task.UpdatedAt = _clock.UtcNow;

            touched.Add(task);
        }

        foreach (var task in touched)
        {
            await _tasks.UpdateAsync(task, ct).ConfigureAwait(false);
        }

        if (touched.Count == 0)
        {
            return tasks;
        }

        await _unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);

        return tasks;
    }

    public async Task<IReadOnlyList<LearningTask>> GenerateAutomaticTasksAsync(
        DateOnly date,
        CancellationToken ct = default)
    {
        var existing = await _tasks.ListForDateAsync(date, ct).ConfigureAwait(false);

        // Idempotent by construction: the day's automatic tasks are generated once, on first open
        // of the day. Re-opening the app must not produce a second set. See TasksStreaksSpec §6.
        //
        // The repository query is already the answer, because DueDateFor stamps a task on the day
        // it belongs to. It was not always so: while the stamp sat on the closing boundary, the
        // previous day's tasks carried this day's date and had to be filtered out by hand against
        // CreatedAt. That filter is gone with the bug it worked around.
        var automatic = existing
            .Where(task => task.Source == TaskSourceType.Automatic)
            .ToList();

        if (automatic.Count > 0)
        {
            return automatic;
        }

        var generated = await BuildAutomaticTasksAsync(date, ct).ConfigureAwait(false);
        if (generated.Count == 0)
        {
            return generated;
        }

        await _tasks.AddRangeAsync(generated, ct).ConfigureAwait(false);
        await _unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);

        return generated;
    }

    /// <summary>
    /// The day's automatic tasks from the active pattern: a daily lesson and/or minute goal, plus
    /// a continuation task for the most recently opened course in progress. Capped at three.
    /// </summary>
    private async Task<List<LearningTask>> BuildAutomaticTasksAsync(DateOnly date, CancellationToken ct)
    {
        var now = _clock.UtcNow;
        var due = DueDateFor(date);
        var tasks = new List<LearningTask>(MaxAutomaticTasksPerDay);

        // The settings model is not part of the Core contract, so the documented defaults stand in
        // for a configured pattern. See TasksStreaksSpec §10.
        if (_options.Rule == StreakRule.Minutes)
        {
            tasks.Add(NewTask(
                $"Watch {DefaultDailyMinuteGoal:0} minutes",
                TaskGoalType.WatchMinutes,
                DefaultDailyMinuteGoal,
                courseId: null,
                moduleId: null,
                due,
                now));
        }
        else
        {
            tasks.Add(NewTask(
                $"Watch {DefaultDailyLessonGoal:0} lessons",
                TaskGoalType.WatchLessons,
                DefaultDailyLessonGoal,
                courseId: null,
                moduleId: null,
                due,
                now));
        }

        var continuation = await FindContinuationCourseAsync(ct).ConfigureAwait(false);
        if (continuation is not null && tasks.Count < MaxAutomaticTasksPerDay)
        {
            tasks.Add(NewTask(
                $"Continue {continuation.Title}",
                TaskGoalType.ReachPercent,
                ContinuationTargetPercent,
                continuation.Id,
                moduleId: null,
                due,
                now));
        }

        return tasks;
    }

    /// <summary>
    /// The course with the most recent <see cref="Course.LastOpenedAt"/> among those in progress.
    /// A course that was never opened, or is finished, is not a continuation candidate.
    /// </summary>
    private async Task<Course?> FindContinuationCourseAsync(CancellationToken ct)
    {
        var courses = await _courses.ListAsync(ct).ConfigureAwait(false);

        return courses
            .Where(course => course.Status == CourseStatus.InProgress
                || (course.Status == CourseStatus.NotStarted && course.LastOpenedAt is not null))
            .Where(course => course.LastOpenedAt is not null)
            .OrderByDescending(course => course.LastOpenedAt)
            .FirstOrDefault();
    }

    private LearningTask NewTask(
        string title,
        TaskGoalType goalType,
        double target,
        Guid? courseId,
        Guid? moduleId,
        DateTime? dueDate,
        DateTime now)
        => new()
        {
            Id = Guid.NewGuid(),
            Title = title,
            CourseId = courseId,
            ModuleId = moduleId,
            Source = TaskSourceType.Automatic,
            GoalType = goalType,
            TargetValue = target,
            CompletedValue = 0d,
            DueDate = dueDate,
            Recurrence = null,
            IsCompleted = false,
            CompletedAt = null,
            CreatedAt = now,
            UpdatedAt = now,
        };

    /// <summary>
    /// Whether a task must not be recomputed. Two cases, and both are rules from the spec rather
    /// than conveniences:
    ///
    /// 1. A <b>completed</b> task is frozen, per TasksStreaksSpec §7 — "progress only moves
    ///    forward during a day; an unmarked lesson does not retroactively un-complete a finished
    ///    task". The user's marker is theirs, and an automatic completion is history.
    /// 2. A task belonging to a day that has ended is frozen outright: its completion state is
    ///    history and today's activity must not be projected back onto it.
    ///
    /// <para>
    /// <b>Being a user task is not itself a freeze.</b> The source decides the *completion rule*
    /// (a user task may be marked complete while the metric is below target), not whether the
    /// metric is tracked at all. TasksStreaksSpec §8 is unconditional on this point:
    /// "<c>CompletedValue</c> is recomputed when relevant activity changes … It is a cached
    /// projection, not a source of truth: if it drifts, recomputing from
    /// <c>LearningActivity</c> and <c>WatchState</c> must restore it." A user task whose
    /// <c>CompletedValue</c> is never recomputed has no projection at all — it is a field that
    /// only ever reads zero, and "watch 30 minutes" would report 0 minutes forever.
    /// </para>
    ///
    /// <para>
    /// What is <i>not</i> recomputed is `IsCompleted`: the caller below preserves a true flag
    /// rather than letting a metric that fell back clear it. That is the §7 rule, and it applies
    /// to user and automatic tasks alike.
    /// </para>
    /// </summary>
    private bool IsFrozen(LearningTask task, DateOnly date, DateOnly today)
    {
        if (date < today)
        {
            return true;
        }

        return task.IsCompleted;
    }

    /// <summary>
    /// Lessons completed that day, from the activity log — the same source the streak reads, so
    /// the two can never disagree.
    /// </summary>
    private static double CompletedLessons(LearningActivity? activity)
        => activity is null ? 0d : activity.CompletedLessons;

    private static double WatchedMinutes(LearningActivity? activity)
        => activity is null ? 0d : activity.WatchedMs / 60_000d;

    /// <summary>Binary: every lesson in the module completed counts as 1, otherwise 0.</summary>
    private async Task<double> ModuleCompletedAsync(LearningTask task, CancellationToken ct)
    {
        if (task.CourseId is not { } courseId || task.ModuleId is not { } moduleId)
        {
            return 0d;
        }

        var module = await _progress.ForModuleAsync(courseId, moduleId, ct).ConfigureAwait(false);

        return module.TotalLessons > 0 && module.CompletedLessons >= module.TotalLessons ? 1d : 0d;
    }

    private async Task<double> CourseCompletedAsync(
        LearningTask task,
        Dictionary<Guid, ProgressSnapshot> cache,
        CancellationToken ct)
    {
        if (task.CourseId is not { } courseId)
        {
            return 0d;
        }

        var progress = await ProgressForAsync(courseId, cache, ct).ConfigureAwait(false);

        return progress.TotalLessons > 0 && progress.CompletedLessons >= progress.TotalLessons ? 1d : 0d;
    }

    private async Task<double> ReachPercentAsync(
        LearningTask task,
        Dictionary<Guid, ProgressSnapshot> cache,
        CancellationToken ct)
    {
        if (task.CourseId is not { } courseId)
        {
            return 0d;
        }

        var progress = await ProgressForAsync(courseId, cache, ct).ConfigureAwait(false);

        return progress.Percent * 100d;
    }

    private async Task<ProgressSnapshot> ProgressForAsync(
        Guid courseId,
        Dictionary<Guid, ProgressSnapshot> cache,
        CancellationToken ct)
    {
        if (cache.TryGetValue(courseId, out var cached))
        {
            return cached;
        }

        var progress = await _progress.ForCourseAsync(courseId, ct).ConfigureAwait(false);
        cache[courseId] = progress;

        return progress;
    }

    private DateTime? DueDateFor(DateOnly date)
    {
        // Midnight on the day's own calendar date, deliberately NOT shifted by the boundary hour.
        //
        // Stamping at `date + 1 + boundary` — the instant the study day closes — reads well until
        // you try to retrieve it: that instant keys to the *next* calendar date, so the task the
        // evaluator wrote for day D is invisible to ListForDate(D) and reappears under D + 1. A
        // deadline belongs on the day it is due. The boundary still decides which study day a
        // moment falls in (IClock.StudyDateFor); it has no business moving a deadline.
        var due = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);

        return DateTime.SpecifyKind(due, DateTimeKind.Local);
    }

    /// <summary>The study date "now" belongs to, which is what "today" means for freezing tasks.</summary>
    private DateOnly Today() => _clock.StudyDateFor(ActivityTracker.Truncate(_clock.UtcNow), _options);
}

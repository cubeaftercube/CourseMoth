// SPDX-License-Identifier: AGPL-3.0-or-later

using CourseMoth.Core.Services;
using CourseMoth.Core.Tests.Fakes;
using Xunit;

namespace CourseMoth.Core.Tests;

/// <summary>
/// DomainMap §8: progress is <b>completed lessons ÷ total lessons</b>, never a fraction of
/// total duration. Duration is filled lazily and is null for most lessons, so a time-based
/// number is a lie until every file has been opened once.
/// </summary>
public sealed class ProgressCalculatorTests
{
    private static async Task<(ProgressCalculator Calculator, InMemoryRepositories Repos, Guid CourseId)> BuildAsync(
        int lessonCount,
        int completedCount)
    {
        var repos = new InMemoryRepositories();
        var course = Sample.Course();
        repos.Courses.Seed(course);

        var lessons = Enumerable.Range(1, lessonCount)
            .Select(order => Sample.Lesson(course.Id, order: order, relativePath: $"{order:D2} Lesson.mp4"))
            .ToList();

        await repos.Lessons.AddRangeAsync(lessons);

        for (var i = 0; i < completedCount; i++)
        {
            await repos.WatchStates.SaveAsync(
                Sample.WatchState(lessons[i].Id, isCompleted: true, completionSource: Domain.CompletionSource.Manual));
        }

        return (new ProgressCalculator(repos.Courses, repos.Lessons, repos.WatchStates), repos, course.Id);
    }

    [Fact]
    public async Task Course_progress_counts_lessons_not_time()
    {
        var (calculator, repos, courseId) = await BuildAsync(lessonCount: 4, completedCount: 1);

        // The completed lesson is a one-hour file; the rest are one-minute files. A time-based
        // metric would report ~94% here. The documented answer is 25%.
        var lessons = await repos.Lessons.ListByCourseAsync(courseId);
        await repos.Lessons.UpdateAsync(With(lessons[0], TimeSpan.FromHours(1)));
        await repos.Lessons.UpdateAsync(With(lessons[1], TimeSpan.FromMinutes(1)));
        await repos.Lessons.UpdateAsync(With(lessons[2], TimeSpan.FromMinutes(1)));
        await repos.Lessons.UpdateAsync(With(lessons[3], TimeSpan.FromMinutes(1)));

        var progress = await calculator.ForCourseAsync(courseId);

        Assert.Equal(4, progress.TotalLessons);
        Assert.Equal(1, progress.CompletedLessons);
        Assert.Equal(0.25, progress.Percent, precision: 6);
    }

    [Fact]
    public async Task Course_progress_is_zero_when_nothing_is_completed()
    {
        var (calculator, _, courseId) = await BuildAsync(lessonCount: 4, completedCount: 0);

        var progress = await calculator.ForCourseAsync(courseId);

        Assert.Equal(0, progress.CompletedLessons);
        Assert.Equal(0d, progress.Percent);
    }

    [Fact]
    public async Task Course_progress_is_complete_when_every_lesson_is_completed()
    {
        var (calculator, _, courseId) = await BuildAsync(lessonCount: 3, completedCount: 3);

        var progress = await calculator.ForCourseAsync(courseId);

        Assert.Equal(3, progress.CompletedLessons);
        Assert.Equal(1d, progress.Percent);
    }

    [Fact]
    public async Task A_lesson_with_no_watch_state_is_simply_not_completed()
    {
        var (calculator, _, courseId) = await BuildAsync(lessonCount: 4, completedCount: 1);

        // Three lessons have no WatchState row at all — the common state after a fresh import.
        var progress = await calculator.ForCourseAsync(courseId);

        Assert.Equal(4, progress.TotalLessons);
        Assert.Equal(1, progress.CompletedLessons);
    }

    [Fact]
    public async Task Module_progress_counts_only_that_module_s_lessons()
    {
        var repos = new InMemoryRepositories();
        var course = Sample.Course();
        repos.Courses.Seed(course);

        var moduleA = Sample.Module(course.Id, "Intro", order: 1);
        var moduleB = Sample.Module(course.Id, "Advanced", order: 2);

        var a1 = Sample.Lesson(course.Id, moduleA.Id, order: 1);
        var a2 = Sample.Lesson(course.Id, moduleA.Id, order: 2);
        var b1 = Sample.Lesson(course.Id, moduleB.Id, order: 3);
        await repos.Lessons.AddRangeAsync([a1, a2, b1]);

        await repos.WatchStates.SaveAsync(Sample.WatchState(a1.Id, isCompleted: true));
        await repos.WatchStates.SaveAsync(Sample.WatchState(b1.Id, isCompleted: true));

        var calculator = new ProgressCalculator(repos.Courses, repos.Lessons, repos.WatchStates);

        var moduleProgress = await calculator.ForModuleAsync(course.Id, moduleA.Id);
        var courseProgress = await calculator.ForCourseAsync(course.Id);

        Assert.Equal(2, moduleProgress.TotalLessons);
        Assert.Equal(1, moduleProgress.CompletedLessons);
        Assert.Equal(0.5, moduleProgress.Percent, precision: 6);

        Assert.Equal(3, courseProgress.TotalLessons);
        Assert.Equal(2, courseProgress.CompletedLessons);
    }

    [Fact]
    public async Task A_null_module_means_the_whole_course()
    {
        var repos = new InMemoryRepositories();
        var course = Sample.Course();
        repos.Courses.Seed(course);
        await repos.Lessons.AddRangeAsync([Sample.Lesson(course.Id, order: 1), Sample.Lesson(course.Id, order: 2)]);

        var calculator = new ProgressCalculator(repos.Courses, repos.Lessons, repos.WatchStates);

        var viaNull = await calculator.ForModuleAsync(course.Id, null);
        var viaCourse = await calculator.ForCourseAsync(course.Id);

        Assert.Equal(viaCourse.TotalLessons, viaNull.TotalLessons);
        Assert.Equal(viaCourse.Percent, viaNull.Percent);
    }

    [Fact]
    public async Task An_empty_course_reports_zero_rather_than_dividing_by_zero()
    {
        var repos = new InMemoryRepositories();
        var course = Sample.Course();
        repos.Courses.Seed(course);

        var calculator = new ProgressCalculator(repos.Courses, repos.Lessons, repos.WatchStates);
        var progress = await calculator.ForCourseAsync(course.Id);

        Assert.Equal(0, progress.TotalLessons);
        Assert.Equal(0d, progress.Percent);
    }

    [Theory]
    [InlineData(0d, Domain.CourseStatus.NotStarted)]
    [InlineData(0.01d, Domain.CourseStatus.InProgress)]
    [InlineData(0.5d, Domain.CourseStatus.InProgress)]
    [InlineData(1d, Domain.CourseStatus.Completed)]
    public void Course_status_follows_progress(double percent, Domain.CourseStatus expected)
    {
        var snapshot = new Abstractions.ProgressSnapshot(Guid.NewGuid(), 10, (int)(percent * 10), percent, 0d);

        Assert.Equal(expected, ProgressCalculator.StatusFor(snapshot));
    }

    private static Domain.Lesson With(Domain.Lesson lesson, TimeSpan duration)
    {
        lesson.Duration = duration;
        return lesson;
    }
}

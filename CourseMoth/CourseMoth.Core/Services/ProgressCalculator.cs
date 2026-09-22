// SPDX-License-Identifier: AGPL-3.0-or-later

using CourseMoth.Core.Abstractions;
using CourseMoth.Core.Domain;

namespace CourseMoth.Core.Services;

/// <summary>
/// Course and module progress, computed on read and never stored. See DomainMap §8.
///
/// Progress is measured in <b>completed lessons</b>, not in time. Duration is filled lazily
/// and is null for many lessons, so total-watched ÷ total-duration silently lies until every
/// file has been opened once. A count of completed lessons is honest from the first scan.
/// </summary>
public sealed class ProgressCalculator : IProgressCalculator
{
    private readonly ICourseRepository _courses;
    private readonly ILessonRepository _lessons;
    private readonly IWatchStateRepository _watchStates;

    public ProgressCalculator(
        ICourseRepository courses,
        ILessonRepository lessons,
        IWatchStateRepository watchStates)
    {
        _courses = courses ?? throw new ArgumentNullException(nameof(courses));
        _lessons = lessons ?? throw new ArgumentNullException(nameof(lessons));
        _watchStates = watchStates ?? throw new ArgumentNullException(nameof(watchStates));
    }

    public async Task<ProgressSnapshot> ForCourseAsync(Guid courseId, CancellationToken ct = default)
        => await ForModuleAsync(courseId, null, ct).ConfigureAwait(false);

    public async Task<ProgressSnapshot> ForModuleAsync(Guid courseId, Guid? moduleId, CancellationToken ct = default)
    {
        var lessons = await _lessons.ListByCourseAsync(courseId, ct).ConfigureAwait(false);

        var scoped = moduleId is { } id
            ? lessons.Where(lesson => lesson.ModuleId == id).ToList()
            : [.. lessons];

        if (scoped.Count == 0)
        {
            return new ProgressSnapshot(courseId, 0, 0, 0d, 0d);
        }

        var states = await _watchStates
            .GetForLessonsAsync([.. scoped.Select(lesson => lesson.Id)], ct)
            .ConfigureAwait(false);

        var completed = 0;
        var watchedMs = 0d;

        foreach (var lesson in scoped)
        {
            if (!states.TryGetValue(lesson.Id, out var state))
            {
                continue;
            }

            if (state.IsCompleted)
            {
                completed++;
            }

            if (state.PositionMs > 0)
            {
                watchedMs += state.PositionMs;
            }
        }

        var percent = (double)completed / scoped.Count;

        return new ProgressSnapshot(courseId, scoped.Count, completed, percent, watchedMs);
    }

    /// <summary>
    /// Course status derived from progress: 0% → <see cref="CourseStatus.NotStarted"/>,
    /// anything above → <see cref="CourseStatus.InProgress"/>, 100% → <see cref="CourseStatus.Completed"/>.
    /// See DomainMap §8.
    /// </summary>
    public static CourseStatus StatusFor(ProgressSnapshot progress)
    {
        ArgumentNullException.ThrowIfNull(progress);

        if (progress.Percent >= 1d)
        {
            return CourseStatus.Completed;
        }

        return progress.Percent > 0d ? CourseStatus.InProgress : CourseStatus.NotStarted;
    }
}

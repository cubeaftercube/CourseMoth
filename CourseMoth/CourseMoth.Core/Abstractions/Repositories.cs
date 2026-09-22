// SPDX-License-Identifier: AGPL-3.0-or-later

using CourseMoth.Core.Domain;

namespace CourseMoth.Core.Abstractions;

public interface ICourseRepository
{
    Task<Course?> GetAsync(Guid id, CancellationToken ct = default);
    Task<Course?> GetByStableKeyAsync(string stableKey, CancellationToken ct = default);
    Task<IReadOnlyList<Course>> ListAsync(CancellationToken ct = default);
    Task AddAsync(Course course, CancellationToken ct = default);
    Task UpdateAsync(Course course, CancellationToken ct = default);
}

public interface ICourseModuleRepository
{
    Task<IReadOnlyList<CourseModule>> ListByCourseAsync(Guid courseId, CancellationToken ct = default);
    Task AddRangeAsync(IEnumerable<CourseModule> modules, CancellationToken ct = default);
}

public interface ILessonRepository
{
    Task<Lesson?> GetAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Lesson>> ListByCourseAsync(Guid courseId, CancellationToken ct = default);
    Task AddRangeAsync(IEnumerable<Lesson> lessons, CancellationToken ct = default);
    Task UpdateAsync(Lesson lesson, CancellationToken ct = default);
}

public interface IWatchStateRepository
{
    Task<WatchState?> GetAsync(Guid lessonId, CancellationToken ct = default);
    Task<IReadOnlyDictionary<Guid, WatchState>> GetForLessonsAsync(
        IReadOnlyCollection<Guid> lessonIds, CancellationToken ct = default);
    Task SaveAsync(WatchState state, CancellationToken ct = default);
}

public interface ITaskRepository
{
    Task<IReadOnlyList<LearningTask>> ListForDateAsync(DateOnly date, CancellationToken ct = default);
    Task<LearningTask?> GetAsync(Guid id, CancellationToken ct = default);
    Task AddRangeAsync(IEnumerable<LearningTask> tasks, CancellationToken ct = default);
    Task UpdateAsync(LearningTask task, CancellationToken ct = default);
}

public interface IActivityRepository
{
    Task<LearningActivity?> GetAsync(DateOnly date, CancellationToken ct = default);
    Task<IReadOnlyList<LearningActivity>> ListAsync(DateOnly from, DateOnly to, CancellationToken ct = default);
    Task SaveAsync(LearningActivity activity, CancellationToken ct = default);
}

public interface ICategoryRepository
{
    Task<IReadOnlyList<Category>> ListAsync(CancellationToken ct = default);
    Task<Category?> GetByNameAsync(string name, CancellationToken ct = default);
    Task AddAsync(Category category, CancellationToken ct = default);
}

public interface ILibrarySourceRepository
{
    Task<LibrarySource?> GetAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<LibrarySource>> ListAsync(CancellationToken ct = default);
    Task AddAsync(LibrarySource source, CancellationToken ct = default);
}

/// <summary>
/// Commits a set of changes atomically. Core calls it; the Data layer decides what that means.
/// </summary>
public interface IUnitOfWork
{
    Task SaveChangesAsync(CancellationToken ct = default);
}

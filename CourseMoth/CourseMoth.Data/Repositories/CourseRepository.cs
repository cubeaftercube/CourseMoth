// SPDX-License-Identifier: AGPL-3.0-or-later

using CourseMoth.Core.Abstractions;
using CourseMoth.Core.Domain;
using CourseMoth.Data.Entities;
using SQLite;

namespace CourseMoth.Data.Repositories;

/// <summary>
/// SQLite-backed <see cref="ICourseRepository"/>.
///
/// <para>
/// A repository here is a translation layer and nothing else: it moves a domain object to a row
/// and back, and it holds no rules about what a course means. "Should this course be marked
/// completed" is <c>ProgressCalculator</c>'s question — SystemMap §4 lists "Data contains no
/// business rules" as a structural constraint, not a style preference.
/// </para>
/// </summary>
public sealed class CourseRepository : ICourseRepository
{
    private readonly CourseMothDatabase _database;

    public CourseRepository(CourseMothDatabase database)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
    }

    /// <inheritdoc />
    public async Task<Course?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var connection = await ConnectionAsync(ct).ConfigureAwait(false);

        var row = await connection.FindAsync<CourseRow>(id).ConfigureAwait(false);

        return row?.ToDomain();
    }

    /// <inheritdoc />
    public async Task<Course?> GetByStableKeyAsync(string stableKey, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stableKey);

        var connection = await ConnectionAsync(ct).ConfigureAwait(false);

        // FirstOrDefault rather than a single-row read: StableKey is not declared unique, because
        // a duplicate is a merge bug the importer should surface rather than have SQLite reject
        // mid-import and leave a half-written transaction behind.
        var row = await connection
            .Table<CourseRow>()
            .Where(course => course.StableKey == stableKey)
            .FirstOrDefaultAsync()
            .ConfigureAwait(false);

        return row?.ToDomain();
    }

    /// <inheritdoc />
    /// <remarks>
    /// Ordered by title, because every caller today renders a list of courses. Ordering in SQL
    /// rather than in the caller keeps the sort a property of the table's contents.
    /// </remarks>
    public async Task<IReadOnlyList<Course>> ListAsync(CancellationToken ct = default)
    {
        var connection = await ConnectionAsync(ct).ConfigureAwait(false);

        var rows = await connection
            .Table<CourseRow>()
            .OrderBy(course => course.Title)
            .ToListAsync()
            .ConfigureAwait(false);

        return rows.Select(row => row.ToDomain()).ToList();
    }

    /// <inheritdoc />
    public async Task AddAsync(Course course, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(course);

        var connection = await ConnectionAsync(ct).ConfigureAwait(false);

        await connection.InsertAsync(CourseRow.FromDomain(course)).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task UpdateAsync(Course course, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(course);

        var connection = await ConnectionAsync(ct).ConfigureAwait(false);

        // Update rather than InsertOrReplace: a replace is a delete followed by an insert, which
        // would fire any cascade and, once sync tombstones exist, look like a deletion to a
        // change log.
        await connection.UpdateAsync(CourseRow.FromDomain(course)).ConfigureAwait(false);
    }

    private ValueTask<SQLiteAsyncConnection> ConnectionAsync(CancellationToken ct)
        => new(_database.ConnectionAsync(ct));
}

/// <summary>SQLite-backed <see cref="ICourseModuleRepository"/>.</summary>
public sealed class CourseModuleRepository : ICourseModuleRepository
{
    private readonly CourseMothDatabase _database;

    public CourseModuleRepository(CourseMothDatabase database)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
    }

    /// <inheritdoc />
    /// <remarks>
    /// Ordered by <c>Order</c>, which is the module's position within its course and not a
    /// creation-time artefact — a course whose modules were imported out of order still reads
    /// correctly.
    /// </remarks>
    public async Task<IReadOnlyList<CourseModule>> ListByCourseAsync(Guid courseId, CancellationToken ct = default)
    {
        var connection = await _database.ConnectionAsync(ct).ConfigureAwait(false);

        var rows = await connection
            .Table<CourseModuleRow>()
            .Where(module => module.CourseId == courseId)
            .OrderBy(module => module.Order)
            .ToListAsync()
            .ConfigureAwait(false);

        return rows.Select(row => row.ToDomain()).ToList();
    }

    /// <inheritdoc />
    /// <remarks>
    /// Inserted one statement per row inside a transaction. A flat course passes an empty
    /// sequence, which is the normal case and not an error — no synthetic module is ever created
    /// (DomainMap §3.2). The transaction matters because the message is "these modules belong
    /// together": a partial import would leave a course with half its structure and no way to
    /// tell.
    /// </remarks>
    public async Task AddRangeAsync(IEnumerable<CourseModule> modules, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(modules);

        var rows = modules.Select(CourseModuleRow.FromDomain).ToList();

        if (rows.Count == 0)
        {
            return;
        }

        var connection = await _database.ConnectionAsync(ct).ConfigureAwait(false);

        await connection.RunInTransactionAsync(transaction =>
        {
            foreach (var row in rows)
            {
                transaction.Insert(row);
            }
        }).ConfigureAwait(false);
    }
}

/// <summary>SQLite-backed <see cref="ILessonRepository"/>.</summary>
public sealed class LessonRepository : ILessonRepository
{
    private readonly CourseMothDatabase _database;

    public LessonRepository(CourseMothDatabase database)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
    }

    /// <inheritdoc />
    public async Task<Lesson?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var connection = await _database.ConnectionAsync(ct).ConfigureAwait(false);

        var row = await connection.FindAsync<LessonRow>(id).ConfigureAwait(false);

        return row?.ToDomain();
    }

    /// <inheritdoc />
    /// <remarks>
    /// Ordered by module then by position. A flat course has no module, so its lessons sort
    /// together under a null <c>ModuleId</c> — which SQLite orders first, consistent with
    /// "flat course comes before a structured one".
    /// </remarks>
    public async Task<IReadOnlyList<Lesson>> ListByCourseAsync(Guid courseId, CancellationToken ct = default)
    {
        var connection = await _database.ConnectionAsync(ct).ConfigureAwait(false);

        var rows = await connection
            .Table<LessonRow>()
            .Where(lesson => lesson.CourseId == courseId)
            .OrderBy(lesson => lesson.ModuleId)
            .ThenBy(lesson => lesson.Order)
            .ToListAsync()
            .ConfigureAwait(false);

        return rows.Select(row => row.ToDomain()).ToList();
    }

    /// <inheritdoc />
    public async Task AddRangeAsync(IEnumerable<Lesson> lessons, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(lessons);

        var rows = lessons.Select(LessonRow.FromDomain).ToList();

        if (rows.Count == 0)
        {
            return;
        }

        var connection = await _database.ConnectionAsync(ct).ConfigureAwait(false);

        await connection.RunInTransactionAsync(transaction =>
        {
            foreach (var row in rows)
            {
                transaction.Insert(row);
            }
        }).ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Used by a rescan to update <c>Availability</c> and <c>Duration</c> in place, which is why
    /// it must not create a row for a lesson that is gone from disk — the whole point is that
    /// progress survives a missing file (DomainMap §3.3).
    /// </remarks>
    public async Task UpdateAsync(Lesson lesson, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(lesson);

        var connection = await _database.ConnectionAsync(ct).ConfigureAwait(false);

        await connection.UpdateAsync(LessonRow.FromDomain(lesson)).ConfigureAwait(false);
    }
}

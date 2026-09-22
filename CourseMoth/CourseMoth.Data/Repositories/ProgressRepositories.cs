// SPDX-License-Identifier: AGPL-3.0-or-later

using CourseMoth.Core.Abstractions;
using CourseMoth.Core.Domain;
using CourseMoth.Data.Entities;

namespace CourseMoth.Data.Repositories;

/// <summary>
/// SQLite-backed <see cref="IWatchStateRepository"/>.
///
/// <para>
/// This is the hottest table in the database — <c>WatchStateService.ReportPositionAsync</c>
/// writes it every few seconds of playback (DomainMap §3.4). Nothing here does more than one
/// statement per call for that reason: no read-then-write, no enclosing transaction beyond what
/// SQLite gives an individual statement. A "fetch, mutate, save" helper on this repository would
/// turn every position report into a read and a write inside a lock.
/// </para>
/// </summary>
public sealed class WatchStateRepository : IWatchStateRepository
{
    /// <summary>
    /// The bound parameter count SQLite accepts in one statement, minus headroom.
    ///
    /// A single <c>WHERE LessonId IN (...)</c> with more placeholders than this is a hard error
    /// from SQLite (<c>SQLITE_ERROR: too many SQL variables</c>), not a slow query — so the
    /// lookup below chunks rather than issuing one statement and hoping. The limit is 999 on
    /// older builds and 32766 on current ones; 500 is far under both and keeps the chunk count
    /// small for any realistic course.
    /// </summary>
    private const int ParameterChunkSize = 500;

    private readonly CourseMothDatabase _database;

    public WatchStateRepository(CourseMothDatabase database)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
    }

    /// <inheritdoc />
    public async Task<WatchState?> GetAsync(Guid lessonId, CancellationToken ct = default)
    {
        var connection = await _database.ConnectionAsync(ct).ConfigureAwait(false);

        // LessonId is the primary key, so a keyed read is exact rather than a filtered scan.
        var row = await connection.FindAsync<WatchStateRow>(lessonId).ConfigureAwait(false);

        return row?.ToDomain();
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// Returns a dictionary keyed by lesson id, containing an entry <i>only</i> for lessons that
    /// have a state. A lesson with no row has never been watched, and inventing a default
    /// <see cref="WatchState"/> for it here would put the decision of what an unwatched lesson
    /// looks like in the wrong layer — that is <c>WatchStateService.GetOrCreateAsync</c>'s job.
    /// </para>
    ///
    /// <para>
    /// Chunked so that a caller can pass a whole course without hitting SQLite's bound-parameter
    /// ceiling. <c>ProgressCalculator</c> passes the lessons of one course, which is small today,
    /// but the interface takes any collection and the caller has no way to know there is a limit.
    /// </para>
    /// </remarks>
    public async Task<IReadOnlyDictionary<Guid, WatchState>> GetForLessonsAsync(
        IReadOnlyCollection<Guid> lessonIds,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(lessonIds);

        var states = new Dictionary<Guid, WatchState>(lessonIds.Count);

        if (lessonIds.Count == 0)
        {
            return states;
        }

        var connection = await _database.ConnectionAsync(ct).ConfigureAwait(false);

        var ids = lessonIds.ToList();

        for (var offset = 0; offset < ids.Count; offset += ParameterChunkSize)
        {
            var chunk = ids.GetRange(offset, Math.Min(ParameterChunkSize, ids.Count - offset));

            var rows = await connection
                .Table<WatchStateRow>()
                .Where(state => chunk.Contains(state.LessonId))
                .ToListAsync()
                .ConfigureAwait(false);

            foreach (var row in rows)
            {
                states[row.LessonId] = row.ToDomain();
            }
        }

        return states;
    }

    /// <inheritdoc />
    /// <remarks>
    /// <see cref="SQLiteAsyncConnection.InsertOrReplaceAsync"/> rather than an update: the caller
    /// is saving a state that may or may not already exist, and this is the one place where the
    /// "does it exist" question would otherwise cost a second round trip on the hot path. The
    /// sync-tombstone objection to replace (a replace reads as a delete plus an insert to a change
    /// log) does not apply yet — nothing writes a change log, and the entity is upserted by its
    /// natural key rather than deleted.
    /// </remarks>
    public async Task SaveAsync(WatchState state, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(state);

        var connection = await _database.ConnectionAsync(ct).ConfigureAwait(false);

        await connection.InsertOrReplaceAsync(WatchStateRow.FromDomain(state)).ConfigureAwait(false);
    }
}

/// <summary>SQLite-backed <see cref="ITaskRepository"/>.</summary>
public sealed class TaskRepository : ITaskRepository
{
    private readonly CourseMothDatabase _database;

    public TaskRepository(CourseMothDatabase database)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// A task belongs to a day if it is due that day, or if it is undated — an undated task is one
    /// the user has not scheduled, and hiding it from every day would hide it permanently.
    /// </para>
    ///
    /// <para>
    /// <b>Every column compared here is a <c>yyyyMMdd</c> date key, and that is load-bearing.</b>
    /// <c>DueDateKey</c>, <c>RecurrenceUntilKey</c> and the <paramref name="date"/> argument are
    /// all on the same scale, so <c>==</c>, <c>&lt;=</c> and <c>&gt;=</c> mean what they read as.
    /// Storing the due date as a tick count instead (~5.3e18 against a date key's ~2.0e7) makes
    /// every one of these comparisons answer nonsense, and the failure is silent: the task simply
    /// never appears on its own day. See <see cref="SqliteValueConverters.ToDateKey"/>.
    /// </para>
    ///
    /// <para>
    /// <b>Bounds on the recurrence.</b> A recurring task is generated as one instance per
    /// occurrence rather than as a counter (TasksStreaksSpec §7), so instances carry concrete
    /// dates and match the due-date arm. The lower bound keeps a rule whose window has not opened
    /// yet from appearing, and the upper bound is the rule's inclusive end date. The <c>yyyyMMdd</c>
    /// encoding is what makes those comparisons correct in SQL as well as in memory.
    /// </para>
    /// </remarks>
    public async Task<IReadOnlyList<LearningTask>> ListForDateAsync(DateOnly date, CancellationToken ct = default)
    {
        var connection = await _database.ConnectionAsync(ct).ConfigureAwait(false);

        var dayKey = SqliteValueConverters.ToStorage(date);

        var rows = await connection
            .Table<LearningTaskRow>()
            .Where(task => task.DueDateKey == null
                || task.DueDateKey == dayKey
                || (task.RecurrenceDays != null
                    && task.DueDateKey <= dayKey
                    && (task.RecurrenceUntilKey == null || task.RecurrenceUntilKey >= dayKey)))
            .OrderBy(task => task.IsCompleted)
            .ThenBy(task => task.Title)
            .ToListAsync()
            .ConfigureAwait(false);

        return rows.Select(row => row.ToDomain()).ToList();
    }

    /// <inheritdoc />
    public async Task<LearningTask?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var connection = await _database.ConnectionAsync(ct).ConfigureAwait(false);

        var row = await connection.FindAsync<LearningTaskRow>(id).ConfigureAwait(false);

        return row?.ToDomain();
    }

    /// <inheritdoc />
    public async Task AddRangeAsync(IEnumerable<LearningTask> tasks, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(tasks);

        var rows = tasks.Select(LearningTaskRow.FromDomain).ToList();

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
    public async Task UpdateAsync(LearningTask task, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(task);

        var connection = await _database.ConnectionAsync(ct).ConfigureAwait(false);

        await connection.UpdateAsync(LearningTaskRow.FromDomain(task)).ConfigureAwait(false);
    }
}

/// <summary>SQLite-backed <see cref="IActivityRepository"/>.</summary>
public sealed class ActivityRepository : IActivityRepository
{
    private readonly CourseMothDatabase _database;

    public ActivityRepository(CourseMothDatabase database)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
    }

    /// <inheritdoc />
    public async Task<LearningActivity?> GetAsync(DateOnly date, CancellationToken ct = default)
    {
        var connection = await _database.ConnectionAsync(ct).ConfigureAwait(false);

        var key = SqliteValueConverters.ToStorage(date);

        var row = await connection.FindAsync<LearningActivityRow>(key).ConfigureAwait(false);

        return row?.ToDomain();
    }

    /// <inheritdoc />
    /// <remarks>
    /// Inclusive at both ends: <paramref name="to"/> is a day the user was active, not a fence to
    /// stop before. Streak calculation folds over this range and an exclusive upper bound would
    /// silently drop the last day of every window.
    /// </remarks>
    public async Task<IReadOnlyList<LearningActivity>> ListAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken ct = default)
    {
        var connection = await _database.ConnectionAsync(ct).ConfigureAwait(false);

        var fromKey = SqliteValueConverters.ToStorage(from);
        var toKey = SqliteValueConverters.ToStorage(to);

        var rows = await connection
            .Table<LearningActivityRow>()
            .Where(activity => activity.DateKey >= fromKey && activity.DateKey <= toKey)
            .OrderBy(activity => activity.DateKey)
            .ToListAsync()
            .ConfigureAwait(false);

        return rows.Select(row => row.ToDomain()).ToList();
    }

    /// <inheritdoc />
    /// <remarks>
    /// Upsert by the study date. <c>ActivityTracker</c> accumulates into a day's row as playback
    /// is reported, so the row usually exists by the time this is called and sometimes does not
    /// (the first lesson of the day) — the save must not care which.
    /// </remarks>
    public async Task SaveAsync(LearningActivity activity, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(activity);

        var connection = await _database.ConnectionAsync(ct).ConfigureAwait(false);

        await connection.InsertOrReplaceAsync(LearningActivityRow.FromDomain(activity)).ConfigureAwait(false);
    }
}

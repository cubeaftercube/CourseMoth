// SPDX-License-Identifier: AGPL-3.0-or-later

using CourseMoth.Data.Entities;
using SQLite;

namespace CourseMoth.Data;

/// <summary>
/// Owns the one <see cref="SQLiteAsyncConnection"/> this process is allowed to have, and
/// creates the schema on first use.
///
/// <para>
/// <b>Why a single connection, held here.</b> ServerApiSpec §2 ("Storage note") is explicit that
/// there must not be a second writer against this file. SQLite serializes writers with a
/// database-level lock, so a second connection does not corrupt anything by itself — but it does
/// mean the app and the (future) server can each sit on a lock the other is waiting for, and
/// <c>SQLITE_BUSY</c> surfaces as an intermittent failure that is very hard to attribute. One
/// connection, created here and shared, makes that impossible rather than unlikely: every
/// repository in this assembly is handed this same instance.
/// </para>
///
/// <para>
/// <b><see cref="SQLiteOpenFlags.SharedCache"/> and no <see cref="SQLiteOpenFlags.FullMutex"/>.</b>
/// Shared cache is what makes a second connection to the same file see one page cache rather than
/// two that can disagree, which is the property the server relies on if it ever opens its own
/// read-only handle. FullMutex is deliberately absent because <see cref="SQLiteAsyncConnection"/>
/// already serializes work onto its own task pool and pays for correctness there; adding the
/// file-level mutex on top would only make each statement more expensive.
/// </para>
///
/// <para>
/// <b>The path is a constructor parameter, not something this class discovers.</b> This assembly
/// has no MAUI reference (SystemMap §3; the Windows server links it too), so it cannot call
/// <c>FileSystem.AppDataDirectory</c>. The composition root passes that in —
/// <c>{AppDataDirectory}/coursemoth.db</c> per StorageSpec §2 — and the layer below stays
/// platform-free and testable against a temp file.
/// </para>
/// </summary>
public sealed class CourseMothDatabase
{
    /// <summary>
    /// The <c>schema_version.version</c> a database created by this build carries.
    ///
    /// <b>Bump this when a table or column changes</b>, and read
    /// <see cref="ReadSchemaVersionAsync"/> before touching the schema. Nothing else in this
    /// assembly consults it yet — see <see cref="SchemaVersionRow"/>.
    /// </summary>
    public const int SchemaVersion = 1;

    private readonly SemaphoreSlim _initializationGate = new(1, 1);

    private SQLiteAsyncConnection? _connection;

    /// <summary>
    /// Creates the database handle. Nothing touches the disk until <see cref="ConnectionAsync"/>
    /// is first awaited, so constructing one of these is safe on a background thread or in a
    /// test that never goes on to use it.
    /// </summary>
    /// <param name="databasePath">
    /// Full path to the database file. The directory is created if it does not exist — the app
    /// passes a path inside its private data directory, which exists, but the harness and any
    /// future tool pass a temp path that may not.
    /// </param>
    public CourseMothDatabase(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        DatabasePath = databasePath;
    }

    /// <summary>Where this database lives. The composition root supplied it; this layer never guesses.</summary>
    public string DatabasePath { get; }

    /// <summary>
    /// The shared connection, with the schema created if this is the first call.
    ///
    /// <para>
    /// Lazy and once-only. Callers await this rather than receiving a connection from the
    /// constructor, because <c>SQLiteAsyncConnection</c> opens the file on first use — doing that
    /// from a constructor would mean a failure to open the database (a read-only directory, a
    /// locked file) surfaces as a constructor exception from inside the DI container, where it is
    /// reported as a service-resolution failure and its real cause is buried.
    /// </para>
    ///
    /// <para>
    /// The gate makes the "create the schema once" step safe against two callers racing on first
    /// use. The second waits and finds both the connection and the tables already there.
    /// </para>
    /// </summary>
    public async Task<SQLiteAsyncConnection> ConnectionAsync(CancellationToken ct = default)
    {
        if (_connection is { } existing)
        {
            return existing;
        }

        await _initializationGate.WaitAsync(ct).ConfigureAwait(false);

        try
        {
            if (_connection is { } raced)
            {
                return raced;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(DatabasePath))!);

            var connection = new SQLiteAsyncConnection(DatabasePath, OpenFlags);

            await InitializeSchemaAsync(connection).ConfigureAwait(false);

            _connection = connection;

            return connection;
        }
        finally
        {
            _initializationGate.Release();
        }
    }

    /// <summary>
    /// The schema version recorded in the database, or <c>null</c> when the table is empty —
    /// which is the state of a database created by a build that had no version marker.
    /// </summary>
    public async Task<int?> ReadSchemaVersionAsync(CancellationToken ct = default)
    {
        var connection = await ConnectionAsync(ct).ConfigureAwait(false);

        var rows = await connection.Table<SchemaVersionRow>().ToListAsync().ConfigureAwait(false);

        return rows.Count == 0 ? null : rows[0].Version;
    }

    /// <summary>
    /// <see cref="SQLiteOpenFlags.ReadWrite"/> | <see cref="SQLiteOpenFlags.Create"/> |
    /// <see cref="SQLiteOpenFlags.SharedCache"/>, as required by ServerApiSpec §2.
    ///
    /// <see cref="SQLiteOpenFlags.Create"/> is what lets first run work in an empty app data
    /// directory without a special case in the composition root.
    /// </summary>
    private static SQLiteOpenFlags OpenFlags =>
        SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.SharedCache;

    /// <summary>
    /// Creates every table if it is missing. <c>CreateTableAsync</c> is idempotent — it issues
    /// <c>CREATE TABLE IF NOT EXISTS</c> and then adds any columns the table is missing — so this
    /// is safe on both a fresh install and every subsequent launch.
    ///
    /// <para>
    /// <b>What this is not.</b> It is not a migration system. It adds tables and columns; it does
    /// not alter types, drop anything, or backfill. When a change needs one of those, read
    /// <see cref="ReadSchemaVersionAsync"/>, branch on it, and do the work here — the version row
    /// written below is the hook for exactly that.
    /// </para>
    ///
    /// <para>
    /// <c>SyncEntityMetadataRow</c> is created although nothing writes to it yet: the table is
    /// part of the schema this build declares, and creating it now means the first sync change
    /// does not have to migrate a database that already holds a user's library.
    /// </para>
    /// </summary>
    private static async Task InitializeSchemaAsync(SQLiteAsyncConnection connection)
    {
        await connection.CreateTableAsync<CourseRow>().ConfigureAwait(false);
        await connection.CreateTableAsync<CourseModuleRow>().ConfigureAwait(false);
        await connection.CreateTableAsync<LessonRow>().ConfigureAwait(false);
        await connection.CreateTableAsync<WatchStateRow>().ConfigureAwait(false);
        await connection.CreateTableAsync<LearningTaskRow>().ConfigureAwait(false);
        await connection.CreateTableAsync<LearningActivityRow>().ConfigureAwait(false);
        await connection.CreateTableAsync<CategoryRow>().ConfigureAwait(false);
        await connection.CreateTableAsync<LibrarySourceRow>().ConfigureAwait(false);
        await connection.CreateTableAsync<SyncEntityMetadataRow>().ConfigureAwait(false);
        await connection.CreateTableAsync<SchemaVersionRow>().ConfigureAwait(false);

        await RecordSchemaVersionAsync(connection).ConfigureAwait(false);
    }

    /// <summary>
    /// Writes the version row if it is absent. The existing row is left exactly as it is — if it
    /// says 1 and this build declares 2, overwriting it would erase the only evidence of what the
    /// database on disk actually holds.
    /// </summary>
    private static async Task RecordSchemaVersionAsync(SQLiteAsyncConnection connection)
    {
        var existing = await connection.Table<SchemaVersionRow>().ToListAsync().ConfigureAwait(false);

        if (existing.Count > 0)
        {
            return;
        }

        await connection.InsertAsync(new SchemaVersionRow
        {
            Id = 1,
            Version = SchemaVersion,
            CreatedUtc = SqliteValueConverters.ToRoundTripText(DateTime.UtcNow),
        }).ConfigureAwait(false);
    }
}

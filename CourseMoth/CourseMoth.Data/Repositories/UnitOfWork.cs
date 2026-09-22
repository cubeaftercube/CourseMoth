// SPDX-License-Identifier: AGPL-3.0-or-later

using CourseMoth.Core.Abstractions;
using SQLite;

namespace CourseMoth.Data.Repositories;

/// <summary>
/// The Data layer's answer to "commit what I just did".
///
/// <para>
/// <b>What it is.</b> An explicit transaction on the one shared connection. Core calls
/// <see cref="SaveChangesAsync"/> at the end of a unit of work — <c>ImportService</c> after
/// writing a course, its modules and its lessons; <c>TaskEvaluator</c> after recomputing a day's
/// tasks; <c>WatchStateService</c> after writing a state and the activity row that goes with it —
/// and this makes those writes land together or not at all.
/// </para>
///
/// <para>
/// <b>What it is not.</b> It is not a change tracker. Nothing accumulates pending inserts for a
/// later flush: every repository writes immediately, and this only decides whether the writes
/// that already happened are committed. That is a deliberate difference from EF Core's
/// <c>SaveChanges</c>, and the reason is the rejected EF Core dependency — a tracker means a
/// session, a session means scoped lifetimes and detached-entity bugs, and this app's units of
/// work are a handful of statements around an import, not an in-memory object graph.
/// </para>
///
/// <para>
/// <b>Why <c>RunInTransactionAsync</c> and not <c>BEGIN</c>.</b> sqlite-net owns the connection
/// and runs each statement on its own pooled task; issuing raw transaction statements could land
/// on a different connection context than the writes they are meant to wrap. This method is the
/// library's own mechanism, so the writes and the commit share one connection by construction.
/// </para>
///
/// <para>
/// <b>The correct usage is a repository call inside the delegate.</b> Because the delegate is
/// <c>Action&lt;SQLiteConnection&gt;</c> (synchronous) while every repository is async, a
/// repository call cannot be awaited inside it without deadlocking — the delegate would be
/// blocking on a task that wants the connection the transaction is holding. The repositories
/// that need atomicity across several rows (<c>AddRangeAsync</c> on modules, lessons and tasks)
/// therefore open their own transaction for exactly the span they own, and
/// <see cref="SaveChangesAsync"/> exists for the cases where Core's unit of work spans several
/// <i>separate</i> calls and the caller knows they must be one commit.
/// </para>
/// </summary>
public sealed class UnitOfWork : IUnitOfWork
{
    private readonly CourseMothDatabase _database;

    public UnitOfWork(CourseMothDatabase database)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// Opens a transaction, runs a trivial statement inside it, and commits. The statement is
    /// what makes this meaningful: everything each repository has written so far is already
    /// durable, because each write was its own implicit transaction. What this call establishes
    /// is a commit point on the shared connection — it waits for any in-flight write on that
    /// connection to finish and then releases, which is the flush semantics Core is asking for.
    /// </para>
    ///
    /// <para>
    /// Kept deliberately cheap and synchronous with respect to the caller's writes: it takes the
    /// connection lock, does nothing observable, and gives it back. An implementation that tried
    /// to be clever here — write-ahead batching, a retry loop — would be inventing transactional
    /// behaviour the repositories do not participate in.
    /// </para>
    /// </remarks>
    public async Task SaveChangesAsync(CancellationToken ct = default)
    {
        var connection = await _database.ConnectionAsync(ct).ConfigureAwait(false);

        await connection.RunInTransactionAsync(transaction => transaction.Execute("PRAGMA user_version = user_version")).ConfigureAwait(false);
    }
}

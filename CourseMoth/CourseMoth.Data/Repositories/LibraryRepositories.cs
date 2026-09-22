// SPDX-License-Identifier: AGPL-3.0-or-later

using CourseMoth.Core.Abstractions;
using CourseMoth.Core.Domain;
using CourseMoth.Data.Entities;

namespace CourseMoth.Data.Repositories;

/// <summary>SQLite-backed <see cref="ICategoryRepository"/>. See DomainMap §4.1.</summary>
public sealed class CategoryRepository : ICategoryRepository
{
    private readonly CourseMothDatabase _database;

    public CategoryRepository(CourseMothDatabase database)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
    }

    /// <inheritdoc />
    /// <remarks>
    /// Ordered by <c>Order</c>, which is the position the user arranged them in, not
    /// alphabetical. A category list that re-sorts itself when one is renamed is the reason
    /// <c>Category.Order</c> exists at all.
    /// </remarks>
    public async Task<IReadOnlyList<Category>> ListAsync(CancellationToken ct = default)
    {
        var connection = await _database.ConnectionAsync(ct).ConfigureAwait(false);

        var rows = await connection
            .Table<CategoryRow>()
            .OrderBy(category => category.Order)
            .ToListAsync()
            .ConfigureAwait(false);

        return rows.Select(row => row.ToDomain()).ToList();
    }

    /// <inheritdoc />
    /// <remarks>
    /// Matched case-insensitively, so "Design" and "design" cannot both be created — a user
    /// adding a category by typing a name they already used expects the existing one, and a
    /// case-sensitive match would quietly give them a second. <c>ToLower()</c> translates to
    /// SQLite's <c>lower()</c>, which only folds ASCII; that is a known limit and it is why
    /// names are not used as an identity in the first place — <c>Id</c> is.
    /// </remarks>
    public async Task<Category?> GetByNameAsync(string name, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var connection = await _database.ConnectionAsync(ct).ConfigureAwait(false);

        var lowered = name.ToLowerInvariant();

        var row = await connection
            .Table<CategoryRow>()
            .Where(category => category.Name.ToLower() == lowered)
            .FirstOrDefaultAsync()
            .ConfigureAwait(false);

        return row?.ToDomain();
    }

    /// <inheritdoc />
    public async Task AddAsync(Category category, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(category);

        var connection = await _database.ConnectionAsync(ct).ConfigureAwait(false);

        await connection.InsertAsync(CategoryRow.FromDomain(category)).ConfigureAwait(false);
    }
}

/// <summary>SQLite-backed <see cref="ILibrarySourceRepository"/>. See DomainMap §6.1.</summary>
public sealed class LibrarySourceRepository : ILibrarySourceRepository
{
    private readonly CourseMothDatabase _database;

    public LibrarySourceRepository(CourseMothDatabase database)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
    }

    /// <inheritdoc />
    public async Task<LibrarySource?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var connection = await _database.ConnectionAsync(ct).ConfigureAwait(false);

        var row = await connection.FindAsync<LibrarySourceRow>(id).ConfigureAwait(false);

        return row?.ToDomain();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<LibrarySource>> ListAsync(CancellationToken ct = default)
    {
        var connection = await _database.ConnectionAsync(ct).ConfigureAwait(false);

        var rows = await connection
            .Table<LibrarySourceRow>()
            .OrderBy(source => source.Name)
            .ToListAsync()
            .ConfigureAwait(false);

        return rows.Select(row => row.ToDomain()).ToList();
    }

    /// <inheritdoc />
    /// <remarks>
    /// A plain insert, not an upsert: a source is added once when the user points the app at a
    /// folder, and a duplicate id is a bug worth seeing rather than a row to silently overwrite.
    /// </remarks>
    public async Task AddAsync(LibrarySource source, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        var connection = await _database.ConnectionAsync(ct).ConfigureAwait(false);

        await connection.InsertAsync(LibrarySourceRow.FromDomain(source)).ConfigureAwait(false);
    }
}

// SPDX-License-Identifier: AGPL-3.0-or-later

using CourseMoth.Core.Domain;
using SQLite;

namespace CourseMoth.Data.Entities;

/// <summary>
/// Storage row for <see cref="Core.Domain.Category"/>. See DomainMap §4.1 — a flat list, and
/// deliberately without a parent id.
/// </summary>
[Table("categories")]
internal sealed class CategoryRow
{
    [PrimaryKey]
    public Guid Id { get; set; }

    [Indexed]
    public string Name { get; set; } = string.Empty;

    public string? ColorHex { get; set; }
    public int Order { get; set; }

    public Category ToDomain() => new()
    {
        Id = Id,
        Name = Name,
        ColorHex = ColorHex,
        Order = Order,
    };

    public static CategoryRow FromDomain(Category category) => new()
    {
        Id = category.Id,
        Name = category.Name,
        ColorHex = category.ColorHex,
        Order = category.Order,
    };
}

/// <summary>
/// Storage row for <see cref="Core.Domain.LibrarySource"/>. See DomainMap §6.1.
///
/// <c>ConfigJson</c> is stored as the opaque string the domain already treats it as — Core must
/// not learn that a WebDAV source has a URL and a local folder has a path, so this layer does
/// not parse it either.
/// </summary>
[Table("library_sources")]
internal sealed class LibrarySourceRow
{
    [PrimaryKey]
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary><see cref="SourceType"/> as int.</summary>
    public int Type { get; set; }

    public string ConfigJson { get; set; } = "{}";

    public long? LastScannedAt { get; set; }
    public long CreatedAt { get; set; }

    public LibrarySource ToDomain() => new()
    {
        Id = Id,
        Name = Name,
        Type = (SourceType)Type,
        ConfigJson = ConfigJson,
        LastScannedAt = LastScannedAt is { } scanned ? SqliteValueConverters.DateTimeFromStorage(scanned) : null,
        CreatedAt = SqliteValueConverters.DateTimeFromStorage(CreatedAt),
    };

    public static LibrarySourceRow FromDomain(LibrarySource source) => new()
    {
        Id = source.Id,
        Name = source.Name,
        Type = (int)source.Type,
        ConfigJson = source.ConfigJson,
        LastScannedAt = source.LastScannedAt is { } scanned ? SqliteValueConverters.ToStorage(scanned) : null,
        CreatedAt = SqliteValueConverters.ToStorage(source.CreatedAt),
    };
}

/// <summary>
/// Storage row for <see cref="Core.Domain.SyncEntityMetadata"/>. See DomainMap §6.3 and SyncSpec §2.
///
/// <para>
/// <b>Nothing writes to this table yet.</b> No interface in
/// <c>CourseMoth.Core.Abstractions.Repositories</c> covers it — synchronization is a later stage
/// (<c>Docs/Roadmap.md</c>), and inventing a repository for it now would be a contract Core has
/// not asked for. The table is created on first open anyway, so that the first sync change needs
/// no migration against a database that already holds a user's library.
/// </para>
///
/// <para>
/// <b>The natural key is <c>(EntityId, EntityType)</c>, folded into one column.</b> sqlite-net
/// permits exactly one <c>[PrimaryKey]</c> per table — declaring two compiles and then throws
/// <c>table "..." has more than one primary key</c> the first time the table is created, which is
/// at first run rather than at build. So the two parts are composed into a single
/// <see cref="Key"/> rather than declared as a composite: <c>"{EntityType}:{EntityId:D}"</c>.
/// A single-entity id can legitimately carry a row per type, and the tombstone has to outlive the
/// entity it describes, so the pair is genuinely the key and not a surrogate.
/// </para>
/// </summary>
[Table("sync_entity_metadata")]
internal sealed class SyncEntityMetadataRow
{
    /// <summary>
    /// <c>"{EntityType}:{EntityId:D}"</c>. Composed rather than composite — see the type remarks.
    /// The separator is a colon because no entity type name contains one, and the id is formatted
    /// with <c>D</c> so the key is stable and readable rather than dependent on a format provider.
    /// </summary>
    [PrimaryKey]
    public string Key { get; set; } = string.Empty;

    public Guid EntityId { get; set; }

    /// <summary>"Course", "WatchState", ...</summary>
    public string EntityType { get; set; } = string.Empty;

    /// <summary>Locally monotonic counter. Orders edits within one device's history only.</summary>
    public long Version { get; set; }

    public long UpdatedAt { get; set; }
    public Guid DeviceId { get; set; }

    /// <summary>Tombstone. Deletion is a flag, never a hard delete (SyncSpec §5).</summary>
    public bool IsDeleted { get; set; }

    /// <summary>Builds the composed key. The single place the format is decided.</summary>
    public static string ComposeKey(Guid entityId, string entityType) => $"{entityType}:{entityId:D}";

    public SyncEntityMetadata ToDomain() => new()
    {
        EntityId = EntityId,
        EntityType = EntityType,
        Version = Version,
        UpdatedAt = SqliteValueConverters.DateTimeFromStorage(UpdatedAt),
        DeviceId = DeviceId,
        IsDeleted = IsDeleted,
    };

    public static SyncEntityMetadataRow FromDomain(SyncEntityMetadata metadata) => new()
    {
        Key = ComposeKey(metadata.EntityId, metadata.EntityType),
        EntityId = metadata.EntityId,
        EntityType = metadata.EntityType,
        Version = metadata.Version,
        UpdatedAt = SqliteValueConverters.ToStorage(metadata.UpdatedAt),
        DeviceId = metadata.DeviceId,
        IsDeleted = metadata.IsDeleted,
    };
}

/// <summary>
/// The schema version marker. One row, ever.
///
/// <para>
/// <b>Why it exists at all.</b> Tables are created with <c>CreateTableAsync</c> on first open,
/// which is enough while there is exactly one version of the schema. It stops being enough the
/// first time a column is added to a shipped database, and at that moment an app that has no
/// recorded version has nothing to branch on — it cannot tell a fresh install from one that
/// holds a user's library. Recording the number now costs one row and makes that a decision
/// instead of a rewrite.
/// </para>
///
/// <para>
/// <b>There is no migration framework here, deliberately.</b> No migration runner, no applied
/// migration list, no down-migrations. This table is the hook a future migration would read;
/// building the rest of it before there is a second version of the schema would be building
/// machinery against a guess.
/// </para>
/// </summary>
[Table("schema_version")]
internal sealed class SchemaVersionRow
{
    /// <summary>Always 1. Pinned so that a second row cannot be inserted by an upsert on the wrong column.</summary>
    [PrimaryKey]
    public int Id { get; set; }

    public int Version { get; set; }

    /// <summary>When this schema was first created, as round-trip UTC text. Diagnostic only.</summary>
    public string CreatedUtc { get; set; } = string.Empty;
}

// SPDX-License-Identifier: AGPL-3.0-or-later

namespace CourseMoth.Core.Domain;

/// <summary>
/// Versioning and tombstone record for a synced entity. A single table rather than
/// columns repeated on every entity, so there is one sync implementation instead of one per type.
/// See DomainMap §6.3 and SyncSpec §2.
/// </summary>
public class SyncEntityMetadata
{
    public Guid EntityId { get; set; }

    /// <summary>"Course", "WatchState", ...</summary>
    public string EntityType { get; set; } = string.Empty;

    /// <summary>Locally monotonic counter. Orders edits within one device's history only.</summary>
    public long Version { get; set; }

    public DateTime UpdatedAt { get; set; }
    public Guid DeviceId { get; set; }

    /// <summary>Tombstone. Deletion is a flag, never a hard delete — see SyncSpec §5.</summary>
    public bool IsDeleted { get; set; }
}

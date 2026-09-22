// SPDX-License-Identifier: AGPL-3.0-or-later

namespace CourseMoth.Core.Domain;

public enum SourceType
{
    LocalFolder,
    Server,
    WebDav,
    Nextcloud,
}

/// <summary>
/// Where a course came from. The connection details are opaque to the domain —
/// Core must never need to know that a WebDAV source has a URL and a local folder has a path.
/// See DomainMap §6.1.
/// </summary>
public class LibrarySource
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public SourceType Type { get; set; }

    /// <summary>Provider-specific settings. Only the matching ILibrarySource implementation may read it.</summary>
    public string ConfigJson { get; set; } = "{}";

    public DateTime? LastScannedAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

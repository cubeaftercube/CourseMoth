// SPDX-License-Identifier: AGPL-3.0-or-later

namespace CourseMoth.Core.Domain;

/// <summary>
/// A structure fingerprint used to recognise the same course arriving on two devices
/// by different routes. Kept in its own entity rather than a column on Course, because a
/// course can carry several fingerprints at once (one from course.json, one from structure)
/// and matching succeeds on any of them. See DomainMap §4.3 and §7.
/// </summary>
public class CourseFingerprint
{
    public Guid CourseId { get; set; }

    /// <summary>"metadata-id" when derived from a course.json id, "structure" otherwise.</summary>
    public string Kind { get; set; } = "structure";

    /// <summary>SHA-256 hex, or a GUID string from course.json.</summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>Algorithm version. Changing the normalisation changes every fingerprint.</summary>
    public int Version { get; set; } = 1;

    public DateTime ComputedAt { get; set; }
}

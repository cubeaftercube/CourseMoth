// SPDX-License-Identifier: AGPL-3.0-or-later

namespace CourseMoth.Core.Domain;

public enum CourseStatus
{
    NotStarted,
    InProgress,
    Completed,
    Abandoned,
    Hidden,
}

/// <summary>
/// A course. The unit the user thinks in — not the file.
/// </summary>
public class Course
{
    public Guid Id { get; set; }

    /// <summary>Human-readable stable key, e.g. "course:csharp-basics". See DomainMap §7.</summary>
    public string StableKey { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;
    public string? Author { get; set; }
    public string? Description { get; set; }

    public Guid? CategoryId { get; set; }

    /// <summary>Relative path to the cover image inside the course folder.</summary>
    public string? CoverPath { get; set; }

    public CourseStatus Status { get; set; } = CourseStatus.NotStarted;

    /// <summary>Fraction of a lesson that must be watched before it counts as complete. Default 0.9.</summary>
    public double CompletionThreshold { get; set; } = 0.9;

    /// <summary>Where the course came from. Null for a course that arrived without files, via sync or import.</summary>
    public Guid? SourceId { get; set; }

    /// <summary>Course root within the source. A reference, never ownership — see Vision §4.3.</summary>
    public string SourcePath { get; set; } = string.Empty;

    public bool IsFavorite { get; set; }
    public bool IsHidden { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? LastOpenedAt { get; set; }
}

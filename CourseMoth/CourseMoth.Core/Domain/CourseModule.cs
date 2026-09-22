// SPDX-License-Identifier: AGPL-3.0-or-later

namespace CourseMoth.Core.Domain;

/// <summary>
/// A module inside a course.
/// A flat course (Course/Lesson.mp4) has no modules at all — no synthetic "All lessons"
/// module is ever created, because it would leak into progress, UI and sync. See DomainMap §3.2.
/// </summary>
public class CourseModule
{
    public Guid Id { get; set; }
    public Guid CourseId { get; set; }

    public string Title { get; set; } = string.Empty;

    /// <summary>Order within the course.</summary>
    public int Order { get; set; }

    public string StableKey { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

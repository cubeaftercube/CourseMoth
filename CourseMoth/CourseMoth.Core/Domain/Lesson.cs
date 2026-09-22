// SPDX-License-Identifier: AGPL-3.0-or-later

namespace CourseMoth.Core.Domain;

public enum LessonAvailability
{
    /// <summary>Exists on the user's machine or a server but is not downloaded.</summary>
    Remote,

    /// <summary>Downloaded by the app into its own sandbox.</summary>
    Downloaded,

    /// <summary>Reachable through the user's own folder. Never deletable by the app.</summary>
    Local,

    /// <summary>The file is gone — a rescan did not find it. Progress is preserved regardless.</summary>
    Missing,
}

public class Lesson
{
    public Guid Id { get; set; }
    public Guid CourseId { get; set; }

    /// <summary>Null when the course is flat.</summary>
    public Guid? ModuleId { get; set; }

    public string Title { get; set; } = string.Empty;
    public int Order { get; set; }

    public string StableKey { get; set; } = string.Empty;

    /// <summary>Path within the course. The basis of course identity across devices.</summary>
    public string RelativePath { get; set; } = string.Empty;

    public long? FileSizeBytes { get; set; }

    /// <summary>Null until known. Filled lazily; progress falls back to lesson counts meanwhile.</summary>
    public TimeSpan? Duration { get; set; }

    public LessonAvailability Availability { get; set; } = LessonAvailability.Missing;

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

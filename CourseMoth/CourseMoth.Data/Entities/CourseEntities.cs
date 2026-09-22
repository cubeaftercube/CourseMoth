// SPDX-License-Identifier: AGPL-3.0-or-later

using CourseMoth.Core.Domain;
using SQLite;

namespace CourseMoth.Data.Entities;

/// <summary>
/// Storage row for <see cref="Core.Domain.Course"/>. Field-for-field with the domain type —
/// see DomainMap §3.1.
///
/// <para>
/// <b>Enum storage.</b> Every enum column in this assembly is stored as its underlying
/// <c>int</c>, which is sqlite-net's default and the reason none of them are declared here as
/// <c>string</c>. This is a decision, not an accident, and it is pinned: changing an enum to be
/// stored as text later does not migrate anything, it reinterprets every existing row (and
/// <c>Enum.Parse</c> against a numeric string produces the wrong member or throws). Likewise,
/// adding a member to the middle of a Core enum silently renumbers every row stored after it.
/// <b>New members go at the end, with an explicit value if the order ever has to change.</b>
/// </para>
/// </summary>
[Table("courses")]
internal sealed class CourseRow
{
    [PrimaryKey]
    public Guid Id { get; set; }

    /// <summary>Unique in practice, but not declared so: a duplicate is a bug to surface, not to have SQLite swallow.</summary>
    [Indexed]
    public string StableKey { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;
    public string? Author { get; set; }
    public string? Description { get; set; }

    [Indexed]
    public Guid? CategoryId { get; set; }

    public string? CoverPath { get; set; }

    /// <summary><see cref="CourseStatus"/> as int.</summary>
    public int Status { get; set; }

    public double CompletionThreshold { get; set; } = 0.9;

    [Indexed]
    public Guid? SourceId { get; set; }

    public string SourcePath { get; set; } = string.Empty;

    public bool IsFavorite { get; set; }
    public bool IsHidden { get; set; }

    /// <summary>Tick count with the <see cref="DateTimeKind"/> bits re-attached — see <see cref="SqliteValueConverters.ToStorage(DateTime)"/>.</summary>
    public long CreatedAt { get; set; }

    public long UpdatedAt { get; set; }

    /// <summary>Nullable: a course that has never been opened. Zero would decode to 0001-01-01, not to "never".</summary>
    public long? LastOpenedAt { get; set; }

    public Course ToDomain() => new()
    {
        Id = Id,
        StableKey = StableKey,
        Title = Title,
        Author = Author,
        Description = Description,
        CategoryId = CategoryId,
        CoverPath = CoverPath,
        Status = (CourseStatus)Status,
        CompletionThreshold = CompletionThreshold,
        SourceId = SourceId,
        SourcePath = SourcePath,
        IsFavorite = IsFavorite,
        IsHidden = IsHidden,
        CreatedAt = SqliteValueConverters.DateTimeFromStorage(CreatedAt),
        UpdatedAt = SqliteValueConverters.DateTimeFromStorage(UpdatedAt),
        LastOpenedAt = LastOpenedAt is { } opened ? SqliteValueConverters.DateTimeFromStorage(opened) : null,
    };

    public static CourseRow FromDomain(Course course) => new()
    {
        Id = course.Id,
        StableKey = course.StableKey,
        Title = course.Title,
        Author = course.Author,
        Description = course.Description,
        CategoryId = course.CategoryId,
        CoverPath = course.CoverPath,
        Status = (int)course.Status,
        CompletionThreshold = course.CompletionThreshold,
        SourceId = course.SourceId,
        SourcePath = course.SourcePath,
        IsFavorite = course.IsFavorite,
        IsHidden = course.IsHidden,
        CreatedAt = SqliteValueConverters.ToStorage(course.CreatedAt),
        UpdatedAt = SqliteValueConverters.ToStorage(course.UpdatedAt),
        LastOpenedAt = course.LastOpenedAt is { } opened ? SqliteValueConverters.ToStorage(opened) : null,
    };
}

/// <summary>Storage row for <see cref="Core.Domain.CourseModule"/>. See DomainMap §3.2.</summary>
[Table("course_modules")]
internal sealed class CourseModuleRow
{
    [PrimaryKey]
    public Guid Id { get; set; }

    [Indexed]
    public Guid CourseId { get; set; }

    public string Title { get; set; } = string.Empty;
    public int Order { get; set; }
    public string StableKey { get; set; } = string.Empty;

    public long CreatedAt { get; set; }
    public long UpdatedAt { get; set; }

    public CourseModule ToDomain() => new()
    {
        Id = Id,
        CourseId = CourseId,
        Title = Title,
        Order = Order,
        StableKey = StableKey,
        CreatedAt = SqliteValueConverters.DateTimeFromStorage(CreatedAt),
        UpdatedAt = SqliteValueConverters.DateTimeFromStorage(UpdatedAt),
    };

    public static CourseModuleRow FromDomain(CourseModule module) => new()
    {
        Id = module.Id,
        CourseId = module.CourseId,
        Title = module.Title,
        Order = module.Order,
        StableKey = module.StableKey,
        CreatedAt = SqliteValueConverters.ToStorage(module.CreatedAt),
        UpdatedAt = SqliteValueConverters.ToStorage(module.UpdatedAt),
    };
}

/// <summary>
/// Storage row for <see cref="Core.Domain.Lesson"/>. See DomainMap §3.3.
///
/// <para>
/// <c>Duration</c> is the reason this row is not a straight copy of the domain type: the domain
/// holds a nullable <see cref="TimeSpan"/>, which sqlite-net cannot map, and the null is
/// meaningful — "duration not known yet" is a different thing from "zero length". It is stored
/// as a nullable millisecond count. See <see cref="SqliteValueConverters"/>.
/// </para>
/// </summary>
[Table("lessons")]
internal sealed class LessonRow
{
    [PrimaryKey]
    public Guid Id { get; set; }

    [Indexed]
    public Guid CourseId { get; set; }

    /// <summary>Null when the course is flat — no synthetic "All lessons" module is ever created (DomainMap §3.2).</summary>
    [Indexed]
    public Guid? ModuleId { get; set; }

    public string Title { get; set; } = string.Empty;
    public int Order { get; set; }
    public string StableKey { get; set; } = string.Empty;

    /// <summary>Path within the course; the basis of identity across devices.</summary>
    public string RelativePath { get; set; } = string.Empty;

    public long? FileSizeBytes { get; set; }

    /// <summary>Nullable milliseconds. Null means "not known yet", and must stay distinguishable from 0.</summary>
    public long? DurationMs { get; set; }

    /// <summary><see cref="LessonAvailability"/> as int.</summary>
    public int Availability { get; set; }

    public long CreatedAt { get; set; }
    public long UpdatedAt { get; set; }

    public Lesson ToDomain() => new()
    {
        Id = Id,
        CourseId = CourseId,
        ModuleId = ModuleId,
        Title = Title,
        Order = Order,
        StableKey = StableKey,
        RelativePath = RelativePath,
        FileSizeBytes = FileSizeBytes,
        Duration = SqliteValueConverters.DurationFromStorage(DurationMs),
        Availability = (LessonAvailability)Availability,
        CreatedAt = SqliteValueConverters.DateTimeFromStorage(CreatedAt),
        UpdatedAt = SqliteValueConverters.DateTimeFromStorage(UpdatedAt),
    };

    public static LessonRow FromDomain(Lesson lesson) => new()
    {
        Id = lesson.Id,
        CourseId = lesson.CourseId,
        ModuleId = lesson.ModuleId,
        Title = lesson.Title,
        Order = lesson.Order,
        StableKey = lesson.StableKey,
        RelativePath = lesson.RelativePath,
        FileSizeBytes = lesson.FileSizeBytes,
        DurationMs = SqliteValueConverters.ToStorage(lesson.Duration),
        Availability = (int)lesson.Availability,
        CreatedAt = SqliteValueConverters.ToStorage(lesson.CreatedAt),
        UpdatedAt = SqliteValueConverters.ToStorage(lesson.UpdatedAt),
    };
}

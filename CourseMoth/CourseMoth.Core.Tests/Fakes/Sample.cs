// SPDX-License-Identifier: AGPL-3.0-or-later

using CourseMoth.Core.Domain;
using CourseMoth.Core.Parsing;

namespace CourseMoth.Core.Tests.Fakes;

/// <summary>
/// Builders for the domain entities the tests need. Kept trivial on purpose: a test that
/// buries its inputs behind three layers of a fluent builder is a test nobody can read.
/// </summary>
public static class Sample
{
    public static readonly DateTime Created = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public static Course Course(
        string title = "C# Basics",
        Guid? id = null,
        double completionThreshold = 0.9,
        CourseStatus status = CourseStatus.NotStarted,
        DateTime? lastOpenedAt = null,
        string sourcePath = "/library/csharp-basics")
        => new()
        {
            Id = id ?? Guid.NewGuid(),
            StableKey = "course:csharp-basics",
            Title = title,
            Status = status,
            CompletionThreshold = completionThreshold,
            SourcePath = sourcePath,
            CreatedAt = Created,
            UpdatedAt = Created,
            LastOpenedAt = lastOpenedAt,
        };

    public static CourseModule Module(Guid courseId, string title = "Intro", int order = 1, Guid? id = null)
        => new()
        {
            Id = id ?? Guid.NewGuid(),
            CourseId = courseId,
            Title = title,
            Order = order,
            StableKey = $"module:csharp-basics:{order:D2}",
            CreatedAt = Created,
            UpdatedAt = Created,
        };

    public static Lesson Lesson(
        Guid courseId,
        Guid? moduleId = null,
        string title = "Lesson",
        int order = 1,
        string relativePath = "01 Lesson.mp4",
        TimeSpan? duration = null,
        long? fileSizeBytes = null,
        Guid? id = null)
        => new()
        {
            Id = id ?? Guid.NewGuid(),
            CourseId = courseId,
            ModuleId = moduleId,
            Title = title,
            Order = order,
            StableKey = $"lesson:csharp-basics:{order:D2}",
            RelativePath = relativePath,
            Duration = duration,
            FileSizeBytes = fileSizeBytes,
            Availability = LessonAvailability.Local,
            CreatedAt = Created,
            UpdatedAt = Created,
        };

    public static WatchState WatchState(
        Guid lessonId,
        long positionMs = 0,
        bool isCompleted = false,
        CompletionSource completionSource = CompletionSource.None,
        bool manualUnmark = false)
        => new()
        {
            LessonId = lessonId,
            PositionMs = positionMs,
            IsCompleted = isCompleted,
            CompletionSource = completionSource,
            ManualUnmark = manualUnmark,
            UpdatedAt = Created,
        };

    // ---- Parser proposals (ParsedRoot and friends) -------------------------------------------

    public static ParsedLesson ParsedLesson(
        string relativePath,
        string? title = null,
        int order = 1,
        int? number = 1,
        long? fileSizeBytes = 1024,
        bool isExcluded = false,
        bool isMaterial = false,
        ParseConfidence confidence = ParseConfidence.High)
        => new()
        {
            Title = title ?? Path.GetFileNameWithoutExtension(relativePath),
            RelativePath = relativePath,
            Order = order,
            Number = number,
            FileSizeBytes = fileSizeBytes,
            IsExcluded = isExcluded,
            IsMaterial = isMaterial,
            Confidence = confidence,
        };

    public static ParsedCourse ParsedCourse(
        string title = "C# Basics",
        string rootRelativePath = "C# Basics",
        IReadOnlyList<ParsedModule>? modules = null,
        Guid? metadataId = null,
        string? author = null,
        bool isExcluded = false)
        => new()
        {
            Title = title,
            RootRelativePath = rootRelativePath,
            Modules = modules ?? [FlatModule(ParsedLesson("01 Lesson.mp4"))],
            MetadataId = metadataId,
            Author = author,
            IsExcluded = isExcluded,
            Confidence = ParseConfidence.High,
        };

    /// <summary>
    /// A single unnamed module. Not a flat course: a flat course has no module at all, and
    /// <see cref="CourseMoth.Core.Services.ImportService"/> decides whether to create a module row
    /// from <c>ParsedCourse.Modules.Count</c>. Useful when a test needs "a lesson somewhere but
    /// no named module".
    /// </summary>
    public static ParsedModule FlatModule(params ParsedLesson[] lessons)
        => new() { Title = string.Empty, Order = 0, Lessons = lessons };

    /// <summary>
    /// A flat course, as ParserSpec §6 defines one: the video files sit directly in the course
    /// root, so there are no modules at all — not one unnamed module.
    ///
    /// <para>
    /// The lessons go in <see cref="ParsedCourse.Lessons"/>, which is the collection for exactly
    /// this shape. A course has either <c>Modules</c> or <c>Lessons</c> at the top level and never
    /// both (ParserSpec §6: "Video directly in the course root? → no modules, every video is a
    /// lesson"). Passing lessons here rather than wrapping them in an unnamed
    /// <see cref="ParsedModule"/> is what keeps a flat course from producing a
    /// <c>CourseModule</c> row, which DomainMap §3.2 warns would "leak into progress, the UI and
    /// sync forever".
    /// </para>
    /// </summary>
    public static ParsedCourse FlatCourse(params ParsedLesson[] lessons)
        => FlatCourse("C# Basics", "C# Basics", lessons);

    /// <summary>See the overload above.</summary>
    public static ParsedCourse FlatCourse(
        string title,
        string rootRelativePath,
        params ParsedLesson[] lessons)
        => new()
        {
            Title = title,
            RootRelativePath = rootRelativePath,
            Modules = [],
            Lessons = lessons,
            Confidence = ParseConfidence.High,
        };

    public static ParsedModule Module(string title, int order, params ParsedLesson[] lessons)
        => new() { Title = title, Order = order, Lessons = lessons };

    public static ParsedRoot Root(
        IReadOnlyList<ParsedCourse> courses,
        ParseMode mode = ParseMode.SingleCourse)
        => new() { DetectedMode = mode, Courses = courses };
}

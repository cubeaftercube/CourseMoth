// SPDX-License-Identifier: AGPL-3.0-or-later

namespace CourseMoth.Core.Parsing;

public enum ParseMode
{
    /// <summary>Infer whether the chosen folder is a course or a library.</summary>
    Auto,

    /// <summary>The chosen folder is a single course.</summary>
    SingleCourse,

    /// <summary>The chosen folder contains several courses.</summary>
    Library,
}

/// <summary>
/// How confident the parser is in a given element. The review screen highlights only
/// Low and Unknown — without this signal everything would be highlighted and the user
/// would approve blindly. See ParserSpec §11.
/// </summary>
public enum ParseConfidence
{
    Certain,
    High,
    Low,
    Unknown,
}

public enum ParseWarningKind
{
    SkippedNonVideo,
    NoNumberInName,
    AmbiguousStructure,
    DuplicateTitle,
    TooManyCourses,
    TooManyModules,
    OrphanFile,
    UnreadableFile,
}

public record ParseWarning(ParseWarningKind Kind, string Message, string? RelativePath);

public record ParseStats(int CourseCount, int ModuleCount, int LessonCount, int SkippedFileCount);

/// <summary>
/// The parser's proposal. Temporary and mutable by design: it carries Confidence and
/// IsExcluded, which have no place in the domain model, and it is discarded once the
/// user confirms the import. See ParserSpec §12.
/// </summary>
public class ParsedRoot
{
    public ParseMode DetectedMode { get; init; }
    public IReadOnlyList<ParsedCourse> Courses { get; init; } = [];
    public IReadOnlyList<ParseWarning> Warnings { get; init; } = [];
    public ParseStats Stats { get; init; } = new(0, 0, 0, 0);
}

public class ParsedCourse
{
    public string Title { get; set; } = string.Empty;
    public string? Author { get; set; }
    public string? CategoryName { get; set; }
    public string? CoverRelativePath { get; set; }

    public string RootRelativePath { get; init; } = string.Empty;

    /// <summary>Id from course.json, when the file was present. The strongest identity signal.</summary>
    public Guid? MetadataId { get; init; }

    public IReadOnlyList<ParsedModule> Modules { get; set; } = [];

    /// <summary>
    /// Lessons that sit directly in the course root, with no module above them.
    ///
    /// ParserSpec §6 names this shape explicitly: "Video directly in the course root? → no modules,
    /// every video is a lesson." It has to be a separate collection rather than a module with an
    /// empty title, because a flat course must produce no CourseModule row at all (DomainMap §3.2:
    /// a synthetic "All lessons" module "would leak into progress, the UI and sync forever").
    /// A course has either Modules or Lessons at the top level, never both.
    /// </summary>
    public IReadOnlyList<ParsedLesson> Lessons { get; set; } = [];

    public ParseConfidence Confidence { get; init; }

    /// <summary>The user unchecked this course on the review screen.</summary>
    public bool IsExcluded { get; set; }
}

public class ParsedModule
{
    public string Title { get; set; } = string.Empty;
    public int Order { get; set; }
    public IReadOnlyList<ParsedLesson> Lessons { get; set; } = [];

    /// <summary>The user unchecked this whole module on the review screen.</summary>
    public bool IsExcluded { get; set; }
}

public class ParsedLesson
{
    public string Title { get; set; } = string.Empty;
    public required string RelativePath { get; init; }
    public int? Number { get; set; }
    public int Order { get; set; }
    public long? FileSizeBytes { get; init; }

    public ParseConfidence Confidence { get; init; }

    /// <summary>The user unchecked this lesson on the review screen.</summary>
    public bool IsExcluded { get; set; }

    /// <summary>A handout rather than a lesson.</summary>
    public bool IsMaterial { get; set; }
}

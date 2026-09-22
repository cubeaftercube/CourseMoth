// SPDX-License-Identifier: AGPL-3.0-or-later

using CourseMoth.Core.Abstractions;
using CourseMoth.Core.Domain;
using CourseMoth.Core.Parsing;

namespace CourseMoth.Core.Services;

/// <summary>
/// The only bridge from the parser's proposal to persistent state. Nothing reaches the database
/// until <see cref="ImportAsync"/> is called, which is what makes an import reversible right up
/// to the moment the user confirms it. See ParserSpec §1 and SystemMap §3.
/// </summary>
public sealed class ImportService : IImportService
{
    private const double DefaultCompletionThreshold = 0.9;

    private readonly ICourseRepository _courses;
    private readonly ICourseModuleRepository _modules;
    private readonly ILessonRepository _lessons;
    private readonly ICourseFingerprintService _fingerprints;
    private readonly IStableKeyGenerator _stableKeys;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    public ImportService(
        ICourseRepository courses,
        ICourseModuleRepository modules,
        ILessonRepository lessons,
        ICourseFingerprintService fingerprints,
        IStableKeyGenerator stableKeys,
        IUnitOfWork unitOfWork,
        IClock clock)
    {
        _courses = courses ?? throw new ArgumentNullException(nameof(courses));
        _modules = modules ?? throw new ArgumentNullException(nameof(modules));
        _lessons = lessons ?? throw new ArgumentNullException(nameof(lessons));
        _fingerprints = fingerprints ?? throw new ArgumentNullException(nameof(fingerprints));
        _stableKeys = stableKeys ?? throw new ArgumentNullException(nameof(stableKeys));
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public async Task<ImportResult> ImportAsync(ParsedRoot root, Guid? sourceId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(root);

        var courseIds = new List<Guid>();
        var modulesImported = 0;
        var lessonsImported = 0;

        // One timestamp for the whole import: rows created together should not disagree about when
        // they were created because the clock ticked mid-loop.
        var now = _clock.UtcNow;

        foreach (var parsedCourse in root.Courses)
        {
            if (parsedCourse.IsExcluded)
            {
                continue;
            }

            var course = CreateCourse(parsedCourse, sourceId, now);
            await _courses.AddAsync(course, ct).ConfigureAwait(false);

            var moduleSlots = await ImportModulesAsync(parsedCourse, course, now, ct).ConfigureAwait(false);
            var lessonsImportedForCourse = await ImportLessonsAsync(parsedCourse, course, moduleSlots, now, ct)
                .ConfigureAwait(false);

            // Count the modules actually created, not the slot list: a slot is null for a module
            // whose lessons were all excluded, and counting those would overstate the import.
            modulesImported += moduleSlots.Count(slot => slot is not null);
            lessonsImported += lessonsImportedForCourse;
            courseIds.Add(course.Id);
        }

        // A single commit for the entire import: a half-imported library is worse than a failed one.
        await _unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);

        return new ImportResult(courseIds.Count, modulesImported, lessonsImported, courseIds);
    }

    private Course CreateCourse(ParsedCourse parsed, Guid? sourceId, DateTime now)
    {
        var folderName = FolderNameOf(parsed.RootRelativePath);
        var title = string.IsNullOrWhiteSpace(parsed.Title) ? folderName : parsed.Title;

        return new Course
        {
            // A course.json id is the strongest identity signal there is; otherwise a fresh one.
            Id = parsed.MetadataId is { } metadataId && metadataId != Guid.Empty ? metadataId : Guid.NewGuid(),
            StableKey = _stableKeys.ForCourse(Normalize(title)),
            Title = title,
            Author = Nullify(parsed.Author),
            Description = null,

            // CategoryName is a name, not an id. Resolving it belongs to the composition root,
            // which owns the category list; guessing an id here would invent a category.
            CategoryId = null,
            CoverPath = Nullify(parsed.CoverRelativePath),

            // Status is derived from progress on read (DomainMap §8), never decided at import.
            Status = CourseStatus.NotStarted,
            CompletionThreshold = DefaultCompletionThreshold,

            SourceId = sourceId,
            SourcePath = parsed.RootRelativePath,

            IsFavorite = false,
            IsHidden = false,

            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    /// <summary>
    /// Creates the course's modules and returns their ids, positioned so that index i corresponds
    /// to <c>parsed.Modules[i]</c>. A flat course produces none: no synthetic "All lessons" module
    /// is ever created, because it would leak into progress, the UI and sync forever.
    /// See DomainMap §3.2.
    /// </summary>
    private async Task<List<Guid?>> ImportModulesAsync(
        ParsedCourse parsed,
        Course course,
        DateTime now,
        CancellationToken ct)
    {
        var slots = new List<Guid?>(parsed.Modules.Count);

        if (parsed.Modules.Count == 0)
        {
            return slots;
        }

        var modules = new List<CourseModule>();
        var order = 0;

        foreach (var parsedModule in parsed.Modules)
        {
            // A module the user unchecked on the review screen, or one whose files were all
            // excluded or are all handouts, is not a module: it would appear in the UI with
            // nothing inside it.
            if (parsedModule.IsExcluded || !parsedModule.Lessons.Any(IsImportable))
            {
                slots.Add(null);
                continue;
            }

            var id = Guid.NewGuid();

            modules.Add(new CourseModule
            {
                Id = id,
                CourseId = course.Id,
                Title = parsedModule.Title,
                Order = order,
                StableKey = _stableKeys.ForModule(course.StableKey, order, Normalize(parsedModule.Title)),
                CreatedAt = now,
                UpdatedAt = now,
            });

            slots.Add(id);
            order++;
        }

        if (modules.Count > 0)
        {
            await _modules.AddRangeAsync(modules, ct).ConfigureAwait(false);
        }

        return slots;
    }

    private async Task<int> ImportLessonsAsync(
        ParsedCourse parsed,
        Course course,
        List<Guid?> moduleSlots,
        DateTime now,
        CancellationToken ct)
    {
        var lessons = new List<Lesson>();

        // Lesson order is per course, matching how a flat course and a module course both read
        // top to bottom. Lesson.StableKey uses the same numbering.
        var order = 0;

        for (var i = 0; i < parsed.Modules.Count; i++)
        {
            var module = parsed.Modules[i];
            var moduleId = i < moduleSlots.Count ? moduleSlots[i] : null;

            foreach (var parsedLesson in module.Lessons)
            {
                if (!IsImportable(parsedLesson))
                {
                    continue;
                }

                lessons.Add(CreateLesson(parsedLesson, course, moduleId, order, now));
                order++;
            }
        }

        // A course whose video sits directly in its root carries its lessons at the top level,
        // with no module above them (ParserSpec §6). They become lessons with a null ModuleId —
        // the shape DomainMap §3.2 requires, and the reason a flat course creates no module row.
        foreach (var parsedLesson in parsed.Lessons)
        {
            if (!IsImportable(parsedLesson))
            {
                continue;
            }

            lessons.Add(CreateLesson(parsedLesson, course, null, order, now));
            order++;
        }

        if (lessons.Count > 0)
        {
            await _lessons.AddRangeAsync(lessons, ct).ConfigureAwait(false);
        }

        return lessons.Count;
    }

    private Lesson CreateLesson(ParsedLesson parsed, Course course, Guid? moduleId, int order, DateTime now)
        => new()
        {
            Id = Guid.NewGuid(),
            CourseId = course.Id,

            // Null for a flat course, which is the common shape. See DomainMap §3.2.
            ModuleId = moduleId,

            Title = parsed.Title,
            Order = order,

            StableKey = _stableKeys.ForLesson(
                course.StableKey,
                moduleId?.ToString("N") ?? string.Empty,
                order,
                Normalize(parsed.Title)),

            RelativePath = parsed.RelativePath,
            FileSizeBytes = parsed.FileSizeBytes,

            // Unknown until the file is opened or a cheap metadata read fills it in.
            Duration = null,

            // The file exists in the user's own folder and the app is not tracking it as its own
            // download. See the report: LessonAvailability has no member for "a file the user owns
            // that lives outside the sandbox", so Remote is the closest honest value.
            Availability = LessonAvailability.Remote,

            CreatedAt = now,
            UpdatedAt = now,
        };

    /// <summary>
    /// A lesson is imported unless the user unchecked it or it is a handout. Materials are skipped
    /// because they have nothing to play, and a lesson that cannot be played must not be counted
    /// towards progress.
    /// </summary>
    private static bool IsImportable(ParsedLesson lesson) => !lesson.IsExcluded && !lesson.IsMaterial;

    /// <summary>
    /// Stable keys are derived from the same normalisation the fingerprint uses, so one definition
    /// of "normalised name" exists in the project rather than two that drift.
    /// </summary>
    private static string Normalize(string value) => CourseFingerprintService.NormalizeStatic(value);

    private static string FolderNameOf(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        var trimmed = path.TrimEnd('/', '\\');
        var index = trimmed.LastIndexOfAny(['/', '\\']);

        return index >= 0 ? trimmed[(index + 1)..] : trimmed;
    }

    private static string? Nullify(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value;
}

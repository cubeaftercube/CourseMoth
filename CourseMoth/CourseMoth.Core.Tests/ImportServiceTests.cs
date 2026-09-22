// SPDX-License-Identifier: AGPL-3.0-or-later

using CourseMoth.Core.Domain;
using CourseMoth.Core.Parsing;
using CourseMoth.Core.Services;
using CourseMoth.Core.Tests.Fakes;
using Xunit;

namespace CourseMoth.Core.Tests;

/// <summary>
/// ParserSpec §12 and DomainMap §3.2. Import is the only place a parse proposal becomes
/// persistent state, so it is also the only place the review screen's decisions — "I unchecked
/// this", "that is a handout, not a lesson" — can be honoured.
/// </summary>
public sealed class ImportServiceTests
{
    private sealed record Harness(ImportService Service, InMemoryRepositories Repos);

    private static Harness Build()
    {
        var repos = new InMemoryRepositories();
        var clock = new FakeClock(new DateTimeOffset(2026, 3, 10, 12, 0, 0, TimeSpan.Zero));

        var service = new ImportService(
            repos.Courses,
            repos.Modules,
            repos.Lessons,
            new CourseFingerprintService(),
            new StableKeyGenerator(),
            repos.UnitOfWork,
            clock);

        return new Harness(service, repos);
    }

    // ---- A flat course has no synthetic module ----------------------------------------------

    [Fact]
    public async Task A_flat_course_imports_its_lessons_with_no_module()
    {
        var h = Build();

        // A flat course is the Course/Lesson.mp4 shape: the videos sit directly in the course
        // root, so the parser proposes no modules at all. It must not be modelled as one unnamed
        // module holding the lessons — ImportService reads Modules.Count == 0 as "this course has
        // no modules", which is what suppresses the synthetic module row.
        var root = Sample.Root(
        [
            Sample.FlatCourse(title: "C# Basics", rootRelativePath: "C# Basics"),
        ]);

        var result = await h.Service.ImportAsync(root, sourceId: null);

        Assert.Equal(1, result.CoursesImported);
        Assert.Equal(0, result.ModulesImported);
        Assert.Empty(h.Repos.Modules.All);

        var course = Assert.Single(await h.Repos.Courses.ListAsync());
        var lessons = await h.Repos.Lessons.ListByCourseAsync(course.Id);

        Assert.All(lessons, lesson => Assert.Null(lesson.ModuleId));
    }

    [Fact]
    public async Task A_course_with_modules_imports_its_lessons_into_them()
    {
        var h = Build();

        var root = Sample.Root(
        [
            Sample.ParsedCourse(
                rootRelativePath: "C# Basics",
                modules:
                [
                    Sample.Module("Intro", 1, Sample.ParsedLesson("Intro/01 Lesson.mp4")),
                    Sample.Module("Advanced", 2, Sample.ParsedLesson("Advanced/01 Lesson.mp4")),
                ]),
        ]);

        var result = await h.Service.ImportAsync(root, sourceId: null);

        Assert.Equal(2, result.ModulesImported);
        Assert.Equal(2, result.LessonsImported);

        var course = Assert.Single(await h.Repos.Courses.ListAsync());
        var modules = await h.Repos.Modules.ListByCourseAsync(course.Id);
        var lessons = await h.Repos.Lessons.ListByCourseAsync(course.Id);

        Assert.Equal(2, modules.Count);
        Assert.All(lessons, lesson => Assert.NotNull(lesson.ModuleId));
        Assert.Equal(2, lessons.Select(l => l.ModuleId).Distinct().Count());
    }

    // ---- Excluded and material items ----------------------------------------------------------

    [Fact]
    public async Task An_excluded_lesson_is_not_imported()
    {
        var h = Build();

        var root = Sample.Root(
        [
            Sample.ParsedCourse(
                rootRelativePath: "C# Basics",
                modules:
                [
                    Sample.FlatModule(
                        Sample.ParsedLesson("01 Lesson.mp4"),
                        Sample.ParsedLesson("02 Bonus.mp4", order: 2, isExcluded: true)),
                ]),
        ]);

        var result = await h.Service.ImportAsync(root, sourceId: null);

        Assert.Equal(1, result.LessonsImported);

        var course = Assert.Single(await h.Repos.Courses.ListAsync());
        var lessons = await h.Repos.Lessons.ListByCourseAsync(course.Id);

        Assert.Single(lessons);
        Assert.DoesNotContain(lessons, lesson => lesson.RelativePath.Contains("Bonus", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_material_is_not_imported()
    {
        var h = Build();

        var root = Sample.Root(
        [
            Sample.ParsedCourse(
                rootRelativePath: "C# Basics",
                modules:
                [
                    Sample.FlatModule(
                        Sample.ParsedLesson("01 Lesson.mp4"),
                        Sample.ParsedLesson("handout.pdf", order: 2, isMaterial: true)),
                ]),
        ]);

        var result = await h.Service.ImportAsync(root, sourceId: null);

        // A handout has nothing to play, and a lesson that cannot be played must not count
        // towards progress.
        Assert.Equal(1, result.LessonsImported);
    }

    [Fact]
    public async Task A_module_whose_every_lesson_is_excluded_is_not_created()
    {
        var h = Build();

        var root = Sample.Root(
        [
            Sample.ParsedCourse(
                rootRelativePath: "C# Basics",
                modules:
                [
                    Sample.Module("Intro", 1, Sample.ParsedLesson("Intro/01.mp4")),
                    Sample.Module("Empty", 2,
                        Sample.ParsedLesson("Empty/01.mp4", isExcluded: true),
                        Sample.ParsedLesson("Empty/02.mp4", order: 2, isExcluded: true)),
                ]),
        ]);

        var result = await h.Service.ImportAsync(root, sourceId: null);

        // A module with nothing inside it would appear in the UI as an empty section.
        Assert.Equal(1, result.ModulesImported);
        Assert.Equal("Intro", Assert.Single(h.Repos.Modules.All).Title);
    }

    [Fact]
    public async Task An_excluded_course_is_not_imported()
    {
        var h = Build();

        var root = Sample.Root(
        [
            Sample.ParsedCourse(title: "Keep", rootRelativePath: "Keep"),
            Sample.ParsedCourse(title: "Drop", rootRelativePath: "Drop", isExcluded: true),
        ],
        ParseMode.Library);

        var result = await h.Service.ImportAsync(root, sourceId: null);

        Assert.Equal(1, result.CoursesImported);

        var courses = await h.Repos.Courses.ListAsync();
        Assert.Equal("Keep", Assert.Single(courses).Title);
    }

    [Fact]
    public async Task A_module_whose_every_lesson_was_excluded_is_not_created()
    {
        var h = Build();

        var root = Sample.Root(
        [
            Sample.ParsedCourse(
                rootRelativePath: "C# Basics",
                modules:
                [
                    Sample.Module("Intro", 1, Sample.ParsedLesson("Intro/01.mp4")),
                    Sample.Module("Empty", 2,
                        Sample.ParsedLesson("Empty/01.mp4", isExcluded: true),
                        Sample.ParsedLesson("Empty/02.mp4", order: 2, isMaterial: true)),
                ]),
        ]);

        var result = await h.Service.ImportAsync(root, sourceId: null);

        // A module with nothing inside it would appear in the UI as an empty section.
        Assert.Equal(1, result.ModulesImported);
        Assert.Equal("Intro", Assert.Single(h.Repos.Modules.All).Title);
    }

    // ---- Identity ------------------------------------------------------------------------------

    [Fact]
    public async Task A_course_json_id_becomes_the_course_id()
    {
        var h = Build();
        var id = Guid.Parse("7f1f7d4c-7c3c-4d9e-8d0f-5d2f4c2f8b21");

        var root = Sample.Root([Sample.ParsedCourse(rootRelativePath: "C# Basics", metadataId: id)]);

        var result = await h.Service.ImportAsync(root, sourceId: null);

        // The id from course.json is the strongest identity signal and must survive the import,
        // otherwise the same course re-imported on another device is a duplicate.
        Assert.Equal(id, Assert.Single(result.CourseIds));
    }

    [Fact]
    public async Task The_course_title_falls_back_to_the_folder_name()
    {
        var h = Build();

        var root = Sample.Root([Sample.ParsedCourse(title: string.Empty, rootRelativePath: "/library/C# Basics")]);

        await h.Service.ImportAsync(root, sourceId: null);

        Assert.Equal("C# Basics", Assert.Single(await h.Repos.Courses.ListAsync()).Title);
    }

    [Fact]
    public async Task The_source_path_is_stored_as_a_reference()
    {
        var h = Build();
        var root = Sample.Root([Sample.ParsedCourse(rootRelativePath: "/library/C# Basics")]);

        await h.Service.ImportAsync(root, sourceId: null);

        // A reference, never ownership — the app does not own the user's folder.
        Assert.Equal("/library/C# Basics", Assert.Single(await h.Repos.Courses.ListAsync()).SourcePath);
    }

    // ---- Lesson shape --------------------------------------------------------------------------

    [Fact]
    public async Task Lesson_order_is_continous_across_modules()
    {
        var h = Build();

        var root = Sample.Root(
        [
            Sample.ParsedCourse(
                rootRelativePath: "C# Basics",
                modules:
                [
                    Sample.Module("Intro", 1,
                        Sample.ParsedLesson("Intro/01.mp4", order: 1),
                        Sample.ParsedLesson("Intro/02.mp4", order: 2)),
                    Sample.Module("Advanced", 2, Sample.ParsedLesson("Advanced/01.mp4", order: 1)),
                ]),
        ]);

        await h.Service.ImportAsync(root, sourceId: null);

        var course = Assert.Single(await h.Repos.Courses.ListAsync());
        var lessons = await h.Repos.Lessons.ListByCourseAsync(course.Id);

        // Order decides what plays next, so it must read top to bottom across the whole course.
        Assert.Equal([0, 1, 2], lessons.Select(l => l.Order));
    }

    [Fact]
    public async Task Duration_is_left_unknown_until_the_lesson_is_opened()
    {
        var h = Build();
        var root = Sample.Root([Sample.ParsedCourse(rootRelativePath: "C# Basics")]);

        await h.Service.ImportAsync(root, sourceId: null);

        var course = Assert.Single(await h.Repos.Courses.ListAsync());
        var lessons = await h.Repos.Lessons.ListByCourseAsync(course.Id);

        // Filled lazily by the player, or by a cheap metadata read during scanning. Until then
        // progress falls back to counting lessons, which is exactly why it does.
        Assert.All(lessons, lesson => Assert.Null(lesson.Duration));
    }

    [Fact]
    public async Task File_size_from_the_scan_is_preserved()
    {
        var h = Build();
        var root = Sample.Root(
        [
            Sample.ParsedCourse(
                rootRelativePath: "C# Basics",
                modules: [Sample.FlatModule(Sample.ParsedLesson("01 Lesson.mp4", fileSizeBytes: 987654L))]),
        ]);

        await h.Service.ImportAsync(root, sourceId: null);

        var course = Assert.Single(await h.Repos.Courses.ListAsync());
        var lesson = Assert.Single(await h.Repos.Lessons.ListByCourseAsync(course.Id));

        // The size is part of the fingerprint, so losing it here would change the course's
        // identity between the parser path and the database path.
        Assert.Equal(987654L, lesson.FileSizeBytes);
    }

    [Fact]
    public async Task Every_imported_entity_gets_a_stable_key()
    {
        var h = Build();

        var root = Sample.Root(
        [
            Sample.ParsedCourse(
                title: "C# Basics",
                rootRelativePath: "C# Basics",
                modules: [Sample.Module("Intro", 1, Sample.ParsedLesson("Intro/01 Lesson.mp4", title: "Welcome"))]),
        ]);

        await h.Service.ImportAsync(root, sourceId: null);

        var course = Assert.Single(await h.Repos.Courses.ListAsync());
        var module = Assert.Single(h.Repos.Modules.All);
        var lesson = Assert.Single(h.Repos.Lessons.All);

        Assert.StartsWith("course:", course.StableKey, StringComparison.Ordinal);
        Assert.StartsWith("module:", module.StableKey, StringComparison.Ordinal);
        Assert.StartsWith("lesson:", lesson.StableKey, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_status_is_not_decided_at_import()
    {
        var h = Build();
        var root = Sample.Root([Sample.ParsedCourse(rootRelativePath: "C# Basics")]);

        await h.Service.ImportAsync(root, sourceId: null);

        // Status is derived from progress on read (DomainMap §8): a stored status is a stored
        // derived value, and those go out of sync.
        Assert.Equal(CourseStatus.NotStarted, Assert.Single(await h.Repos.Courses.ListAsync()).Status);
    }

    [Fact]
    public async Task The_import_is_committed_once_through_the_unit_of_work()
    {
        var h = Build();
        var root = Sample.Root(
        [
            Sample.ParsedCourse(rootRelativePath: "One"),
            Sample.ParsedCourse(rootRelativePath: "Two"),
        ],
        ParseMode.Library);

        await h.Service.ImportAsync(root, sourceId: null);

        // A half-imported library is worse than a failed one, so the whole import is one commit.
        Assert.Equal(1, h.Repos.UnitOfWork.SaveCount);
    }

    [Fact]
    public async Task An_empty_proposal_imports_nothing_and_does_not_throw()
    {
        var h = Build();

        var result = await h.Service.ImportAsync(new Parsing.ParsedRoot(), sourceId: null);

        Assert.Equal(0, result.CoursesImported);
        Assert.Empty(result.CourseIds);
    }
}

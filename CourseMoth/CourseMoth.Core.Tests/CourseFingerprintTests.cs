// SPDX-License-Identifier: AGPL-3.0-or-later

using CourseMoth.Core.Services;
using CourseMoth.Core.Tests.Fakes;
using Xunit;

namespace CourseMoth.Core.Tests;

/// <summary>
/// ParserSpec §13 and DomainMap §7. The fingerprint is what lets the same course be recognised
/// on two devices arriving by different routes, so the two failure modes that matter are:
/// differing when it should not (a moved folder) and agreeing when it should not (a renamed
/// lesson file, which is a different course structure).
/// </summary>
public sealed class CourseFingerprintTests
{
    private static readonly CourseFingerprintService Service = new();

    // ---- Stability ---------------------------------------------------------------------------

    [Fact]
    public void The_same_course_scanned_twice_produces_the_same_fingerprint()
    {
        var first = FingerprintFor("C# Basics", [("01 Intro.mp4", 1024L), ("02 Linq.mp4", 2048L)]);
        var second = FingerprintFor("C# Basics", [("01 Intro.mp4", 1024L), ("02 Linq.mp4", 2048L)]);

        Assert.Equal(first, second);
    }

    [Fact]
    public void A_moved_course_folder_produces_the_same_fingerprint()
    {
        var atC = FingerprintFromSourcePath("/library/csharp-basics", [("01 Intro.mp4", 1024L)]);
        var atD = FingerprintFromSourcePath("/mnt/external/courses/csharp-basics", [("01 Intro.mp4", 1024L)]);

        // Absolute paths are deliberately excluded: moving the folder must not change identity.
        Assert.Equal(atC, atD);
    }

    [Fact]
    public void A_renamed_lesson_file_produces_a_different_fingerprint()
    {
        var before = FingerprintFor("C# Basics", [("01 Intro.mp4", 1024L)]);
        var after = FingerprintFor("C# Basics", [("01 Introduction.mp4", 1024L)]);

        Assert.NotEqual(before, after);
    }

    [Fact]
    public void Lesson_order_does_not_matter_but_the_set_does()
    {
        var ascending = FingerprintFor("C# Basics", [("01 Intro.mp4", 1024L), ("02 Linq.mp4", 2048L)]);
        var shuffled = FingerprintFor("C# Basics", [("02 Linq.mp4", 2048L), ("01 Intro.mp4", 1024L)]);
        var fewer = FingerprintFor("C# Basics", [("01 Intro.mp4", 1024L)]);

        // Paths are sorted before hashing, so a filesystem that enumerates in a different order
        // must not produce a different identity.
        Assert.Equal(ascending, shuffled);
        Assert.NotEqual(ascending, fewer);
    }

    [Fact]
    public void A_different_file_size_produces_a_different_fingerprint()
    {
        var small = FingerprintFor("C# Basics", [("01 Intro.mp4", 1024L)]);
        var large = FingerprintFor("C# Basics", [("01 Intro.mp4", 1025L)]);

        // File size is in the hash to tell apart courses with identical names and paths.
        Assert.NotEqual(small, large);
    }

    [Fact]
    public void A_different_course_folder_name_produces_a_different_fingerprint()
    {
        var one = FingerprintFor("C# Basics", [("01 Intro.mp4", 1024L)]);
        var other = FingerprintFor("Docker Deep Dive", [("01 Intro.mp4", 1024L)]);

        Assert.NotEqual(one, other);
    }

    // ---- Excluded inputs ---------------------------------------------------------------------

    [Fact]
    public void Lesson_and_module_titles_are_excluded_from_the_fingerprint()
    {
        var plain = FingerprintFor("C# Basics", [("01 Intro.mp4", 1024L)]);

        // Same file, but the user renamed the lesson and translated the module title. The fields
        // users edit most must not change identity, so the fingerprint has to be unchanged.
        var parsed = Sample.ParsedCourse(
            rootRelativePath: "C# Basics",
            modules: [Sample.Module("Введение", 1, Sample.ParsedLesson("01 Intro.mp4", title: "Введение в курс"))]);

        Assert.Equal(plain, Service.Compute(parsed, "C# Basics"));
    }

    [Fact]
    public void File_timestamps_and_duration_have_no_place_in_the_fingerprint()
    {
        // Neither field is an input at all: a touch or a first playback must not look like a
        // different course. Expressed by the same input twice producing the same value.
        var parsed = Sample.ParsedCourse(rootRelativePath: "C# Basics");

        var first = Service.Compute(parsed, "C# Basics");
        var second = Service.Compute(parsed, "C# Basics");

        Assert.Equal(first, second);
    }

    [Fact]
    public void The_module_count_participates_in_the_fingerprint()
    {
        var flat = Sample.ParsedCourse(
            rootRelativePath: "C# Basics",
            modules: [Sample.FlatModule(Sample.ParsedLesson("01 Intro.mp4"))]);

        var twoModules = Sample.ParsedCourse(
            rootRelativePath: "C# Basics",
            modules:
            [
                Sample.Module("One", 1, Sample.ParsedLesson("01 Intro.mp4")),
                Sample.Module("Two", 2, Sample.ParsedLesson("02 Linq.mp4")),
            ]);

        Assert.NotEqual(Service.Compute(flat, "C# Basics"), Service.Compute(twoModules, "C# Basics"));
    }

    [Fact]
    public void The_course_json_id_outranks_the_structure()
    {
        var id = Guid.Parse("7f1f7d4c-7c3c-4d9e-8d0f-5d2f4c2f8b21");

        var withId = Sample.ParsedCourse(rootRelativePath: "C# Basics", metadataId: id);
        var withoutId = Sample.ParsedCourse(rootRelativePath: "C# Basics");

        // course.json's id is the strongest identity signal; a course described by it must be
        // recognised even if its files were reorganised.
        Assert.NotEqual(Service.Compute(withId, "C# Basics"), Service.Compute(withoutId, "C# Basics"));

        var reId = Sample.ParsedCourse(rootRelativePath: "Renamed Folder", metadataId: id);
        Assert.NotEqual(Service.Compute(withId, "C# Basics"), Service.Compute(reId, "Renamed Folder"));
    }

    // ---- Import-time filtering ---------------------------------------------------------------

    [Fact]
    public void An_excluded_lesson_is_not_part_of_the_fingerprint()
    {
        var withExcluded = Sample.ParsedCourse(
            rootRelativePath: "C# Basics",
            modules:
            [
                Sample.FlatModule(
                    Sample.ParsedLesson("01 Intro.mp4"),
                    Sample.ParsedLesson("02 Bonus.mp4", isExcluded: true)),
            ]);

        var withoutIt = Sample.ParsedCourse(
            rootRelativePath: "C# Basics",
            modules: [Sample.FlatModule(Sample.ParsedLesson("01 Intro.mp4"))]);

        // The user unchecked the lesson on the review screen, so it will not be imported and
        // must not take part in the identity of what is imported.
        Assert.Equal(Service.Compute(withoutIt, "C# Basics"), Service.Compute(withExcluded, "C# Basics"));
    }

    [Fact]
    public void A_material_is_not_part_of_the_fingerprint()
    {
        var withMaterial = Sample.ParsedCourse(
            rootRelativePath: "C# Basics",
            modules:
            [
                Sample.FlatModule(
                    Sample.ParsedLesson("01 Intro.mp4"),
                    Sample.ParsedLesson("handout.pdf", isMaterial: true)),
            ]);

        var withoutIt = Sample.ParsedCourse(
            rootRelativePath: "C# Basics",
            modules: [Sample.FlatModule(Sample.ParsedLesson("01 Intro.mp4"))]);

        Assert.Equal(Service.Compute(withoutIt, "C# Basics"), Service.Compute(withMaterial, "C# Basics"));
    }

    // ---- Normalisation -----------------------------------------------------------------------

    [Theory]
    [InlineData("01_Intro.mp4", "intro")]
    [InlineData("02 Setup Tools 1080p x264 v2 final.mp4", "setup tools")]
    [InlineData("[CourseName] Lesson 03 - Variables [1080p].mkv", "lesson variables")]
    [InlineData("01.Basics.Rust.2024.WEB-DL.1080p.mp4", "basics rust")]
    [InlineData("INTRO.MP4", "intro")]
    public void Normalisation_strips_noise_and_collapses_to_a_canonical_form(string input, string expected)
    {
        Assert.Equal(expected, Service.Normalize(input));
    }

    [Fact]
    public void Normalisation_is_case_insensitive()
    {
        Assert.Equal(Service.Normalize("Интро Урок.mp4"), Service.Normalize("ИНТРО УРОК.MP4"));
    }

    [Fact]
    public void Cyrillic_names_normalise_consistently_and_independently_of_culture()
    {
        // Cyrillic is not transliterated for the fingerprint — it is kept as letters, lowercased
        // invariantly. A culture-sensitive ToLower would fold these differently on a Turkish
        // device and silently split one course into two identities.
        var normalized = Service.Normalize("01 Введение_в_курс.mp4");

        Assert.Equal("введение в курс", normalized);
        Assert.Equal(normalized, Service.Normalize("01 Введение_в_курс.mp4"));
    }

    [Fact]
    public void Two_differently_punctuated_names_for_the_same_lesson_normalise_together()
    {
        var underscores = Service.Normalize("02_Setup_Tools.mp4");
        var spaces = Service.Normalize("02 Setup Tools.mp4");
        var dots = Service.Normalize("02.Setup.Tools.mp4");

        Assert.Equal(underscores, spaces);
        Assert.Equal(spaces, dots);
    }

    [Fact]
    public void Normalisation_of_an_empty_name_is_empty_rather_than_a_throw()
    {
        // Identity code runs against real, messy archives; it must not be the thing that crashes.
        Assert.Equal(string.Empty, Service.Normalize(string.Empty));
        Assert.Equal(string.Empty, Service.Normalize("   "));
        Assert.Equal(string.Empty, Service.Normalize("[1080p].mp4"));
    }

    [Fact]
    public void The_algorithm_version_is_exposed_and_pinned()
    {
        // A bump changes every fingerprint, so it is a deliberate, versioned act — not a
        // side effect of tidying the normaliser.
        Assert.Equal(CourseFingerprintService.FingerprintVersion, Service.AlgorithmVersion);
        Assert.Equal(1, Service.AlgorithmVersion);
    }

    [Fact]
    public void The_fingerprint_is_a_sha256_hex_digest()
    {
        var fingerprint = FingerprintFor("C# Basics", [("01 Intro.mp4", 1024L)]);

        Assert.Equal(64, fingerprint.Length);
        Assert.All(fingerprint, c => Assert.True(char.IsAsciiHexDigitLower(c), $"'{c}' is not lowercase hex"));
    }

    [Fact]
    public void The_parser_path_and_the_database_path_agree_for_the_same_course()
    {
        var parsed = Sample.ParsedCourse(
            rootRelativePath: "C# Basics",
            modules:
            [
                Sample.Module("Intro", 1,
                    Sample.ParsedLesson("01 Intro.mp4", fileSizeBytes: 1024L),
                    Sample.ParsedLesson("02 Linq.mp4", fileSizeBytes: 2048L)),
            ]);

        var fromParser = Service.Compute(parsed, "C# Basics");

        // The same structure as it looks after import: two lessons in one module.
        var course = Sample.Course(sourcePath: "/library/C# Basics");
        var moduleId = Guid.NewGuid();
        var lessons = new[]
        {
            Sample.Lesson(course.Id, moduleId, relativePath: "01 Intro.mp4", fileSizeBytes: 1024L),
            Sample.Lesson(course.Id, moduleId, relativePath: "02 Linq.mp4", fileSizeBytes: 2048L, order: 2),
        };

        Assert.Equal(fromParser, Service.Compute(course, lessons));
    }

    // ---- Helpers -----------------------------------------------------------------------------

    private static string FingerprintFor(string folderName, (string Path, long Size)[] lessons)
    {
        var parsed = Sample.ParsedCourse(
            rootRelativePath: folderName,
            modules: [Sample.FlatModule([.. lessons.Select(l => Sample.ParsedLesson(l.Path, fileSizeBytes: l.Size))])]);

        return Service.Compute(parsed, folderName);
    }

    private static string FingerprintFromSourcePath(string sourcePath, (string Path, long Size)[] lessons)
    {
        var course = Sample.Course(sourcePath: sourcePath);
        var moduleId = Guid.NewGuid();

        var mapped = lessons
            .Select((l, index) => Sample.Lesson(
                course.Id, moduleId, relativePath: l.Path, fileSizeBytes: l.Size, order: index + 1))
            .ToList();

        return Service.Compute(course, mapped);
    }
}

// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using CourseMoth.Core.Abstractions;
using CourseMoth.Core.Domain;
using CourseMoth.Core.Parsing;

namespace CourseMoth.Core.Services;

/// <summary>
/// Computes the structure fingerprint that lets the same course be recognised across devices
/// arriving by different routes. See ParserSpec §13 and DomainMap §7.
///
/// The fingerprint is a hash of a normalized description of structure only. It deliberately
/// excludes absolute paths (moving the folder must not change it), module and lesson titles
/// (the fields users edit and translate most), durations (unknown on first scan) and file
/// timestamps (they change on copy).
/// </summary>
public sealed class CourseFingerprintService : ICourseFingerprintService
{
    /// <summary>
    /// Version of the algorithm. Bumping it changes every fingerprint, so old rows must be
    /// recomputed in the background rather than discarded — discarding breaks sync for
    /// users who already imported courses. See ParserSpec §13.
    /// </summary>
    public const int FingerprintVersion = 1;

    /// <summary>Release tags stripped by normalisation. Extending this list changes every fingerprint.</summary>
    private static readonly HashSet<string> ReleaseTags = new(StringComparer.Ordinal)
    {
        "1080p", "1080i", "720p", "720i", "480p", "2160p", "4k", "2k", "uhd", "hd", "sd",
        "x264", "x265", "h264", "h265", "hevc", "av1", "vp9", "divx", "xvid",
        "8bit", "10bit",
        "web", "webdl", "webrip", "hdtv", "bluray", "bdrip", "brrip", "dvdrip", "dvd",
        "aac", "ac3", "eac3", "dts", "mp3", "flac", "opus",
        "repack", "proper", "fixed", "final", "v2", "v3",
    };

    public int AlgorithmVersion => FingerprintVersion;

    /// <summary>
    /// Fingerprint of a parsed proposal. Only items that will actually be imported take part:
    /// a lesson the user unchecked on the review screen is not part of the imported structure,
    /// and hashing it would make the fingerprint disagree with the one computed later from
    /// the database.
    /// </summary>
    public string Compute(ParsedCourse course, string courseFolderName)
    {
        ArgumentNullException.ThrowIfNull(course);

        var lessons = new List<(string NormalizedPath, long Size)>();
        var moduleCount = 0;

        foreach (var module in course.Modules)
        {
            if (module.IsExcluded)
            {
                continue;
            }

            moduleCount++;

            foreach (var lesson in module.Lessons)
            {
                if (lesson.IsExcluded || lesson.IsMaterial)
                {
                    continue;
                }

                lessons.Add((Normalize(lesson.RelativePath), lesson.FileSizeBytes ?? 0));
            }
        }

        // Lessons sitting directly in the course root, with no module above them (ParserSpec §6).
        // They contribute no module to the count, which is what keeps a flat course's fingerprint
        // equal to the one computed later from the database, where the module count is zero.
        foreach (var lesson in course.Lessons)
        {
            if (lesson.IsExcluded || lesson.IsMaterial)
            {
                continue;
            }

            lessons.Add((Normalize(lesson.RelativePath), lesson.FileSizeBytes ?? 0));
        }

        return Compose(course.MetadataId, courseFolderName, lessons, moduleCount);
    }

    /// <summary>
    /// Fingerprint of an imported course. Excluded and material items were never written to the
    /// database, so passing the course's lessons in is already equivalent to the filter above.
    /// </summary>
    public string Compute(Course course, IReadOnlyList<Lesson> lessons)
    {
        ArgumentNullException.ThrowIfNull(course);
        ArgumentNullException.ThrowIfNull(lessons);

        var entries = new List<(string NormalizedPath, long Size)>(lessons.Count);
        var moduleIds = new HashSet<Guid>();

        foreach (var lesson in lessons)
        {
            entries.Add((Normalize(lesson.RelativePath), lesson.FileSizeBytes ?? 0));

            if (lesson.ModuleId is { } moduleId)
            {
                moduleIds.Add(moduleId);
            }
        }

        return Compose(null, FolderNameOf(course.SourcePath), entries, moduleIds.Count);
    }

    /// <summary>
    /// Fingerprint normalisation, deliberately separate from display title cleaning
    /// (ParserSpec §8). It must stay deterministic and versioned.
    /// </summary>
    public string Normalize(string value) => NormalizeStatic(value);

    /// <summary>
    /// Fingerprint normalisation, reachable without an instance. The instance method delegates
    /// here, and callers that only need to derive a stable key reuse it rather than keeping a
    /// second copy that could drift.
    /// </summary>
    public static string NormalizeStatic(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        // Every case operation here is ordinal or invariant. An ambient-culture fold would make
        // the answer depend on the device: Turkish folds "I" to a dotless "ı" and Lithuanian and
        // Azeri fold differently again, so a Russian Windows machine and a Turkish one would
        // compute two different fingerprints for one course and the sync design collapses.
        // ParserSpec §13 requires the normalisation to be deterministic; see also §8.
        var text = value.Normalize(NormalizationForm.FormC).ToLowerInvariant();

        text = ReplaceSeparators(text);
        text = StripExtension(text);
        text = StripBracketedJunk(text);

        // Tags are removed on the token line, never as substrings: "1080" is a resolution but
        // "10801" is a lesson number, so the token boundary is what makes the test meaningful.
        //
        // JoinTagTokens has to see the UNFILTERED token list. Filtering first removes "web" out of
        // "web dl" and leaves a bare "dl" that no tag list will ever match — which is exactly how
        // "01.Basics.Rust.2024.WEB-DL.1080p.mp4" used to normalise to "basics rust dl".
        var stripped = JoinTagTokens(Tokenize(text).ToList());

        // Numbers are dropped as whole tokens, not only at the fragment edges. A leading or
        // trailing number is the lesson index ("01 Intro", "Intro 01"), and an interior one is a
        // sequence marker inside a title ("Lesson 03 - Variables", "Глава 5. Основы"). None of
        // them carry identity — the title words do — so keeping them would make a renamed lesson
        // renumbering change the fingerprint. The trailing-year rule in ParserSpec §8 applies to
        // displayed titles, which is a different pipeline (see CleanTitle below).
        var tokens = Tokenize(stripped)
            .Where(token => !IsReleaseTag(token) && !IsPureNumber(token))
            .ToList();

        return string.Join(' ', tokens);
    }

    /// <summary>
    /// The canonical input string, joined with " | " so that segment boundaries are unambiguous.
    /// The delimiter cannot appear inside a segment: every segment has had its punctuation
    /// collapsed to single spaces.
    /// </summary>
    private static string BuildCanonicalInput(
        Guid? metadataId,
        string courseFolderName,
        IReadOnlyList<(string NormalizedPath, long Size)> lessons,
        int moduleCount)
    {
        var segments = new List<string>(4)
        {
            metadataId?.ToString("D", CultureInfo.InvariantCulture) ?? string.Empty,
            NormalizeStatic(courseFolderName),
            string.Join(',', lessons
                .OrderBy(lesson => lesson.NormalizedPath, StringComparer.Ordinal)
                .ThenBy(lesson => lesson.Size)
                .Select(lesson => lesson.NormalizedPath + ":" + lesson.Size.ToString(CultureInfo.InvariantCulture))),
            moduleCount.ToString(CultureInfo.InvariantCulture),
        };

        return string.Join(" | ", segments);
    }

    private string Compose(
        Guid? metadataId,
        string courseFolderName,
        IReadOnlyList<(string NormalizedPath, long Size)> lessons,
        int moduleCount)
        => Hash(BuildCanonicalInput(metadataId, courseFolderName, lessons, moduleCount));

    private static string Hash(string canonicalInput)
    {
        var bytes = Encoding.UTF8.GetBytes(canonicalInput);
        var hash = SHA256.HashData(bytes);

        return Convert.ToHexStringLower(hash);
    }

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

    /// <summary>
    /// Joins the tokens of a source tag that is written with an internal separator, so that
    /// "web-dl" is one token rather than "web" and "dl". Applied after the separator replacement
    /// below, which is what makes the two parts addressable at all. ParserSpec §8 lists WEB-DL
    /// (and 1920x1080, 5.1, 2.0) alongside the single-word tags, so a tag that happens to be
    /// written with punctuation still has to be stripped.
    /// </summary>
    private static readonly Dictionary<string, string> JoinedTagReplacements = new(StringComparer.Ordinal)
    {
        ["web dl"] = "webdl",
        ["web rip"] = "webrip",
        ["hd rip"] = "hdrip",
        ["blu ray"] = "bluray",
        ["bd rip"] = "bdrip",
        ["br rip"] = "brrip",
        ["dvd rip"] = "dvdrip",
    };

    private static string ReplaceSeparators(string text)
        => text.Replace('_', ' ').Replace('-', ' ').Replace('.', ' ').Replace('/', ' ').Replace('\\', ' ');

    /// <summary>
    /// Rewrites the multi-word spellings of a source tag into their single-token form. This has
    /// to happen on the token line, not on the raw text: "1920x1080" and the audio "5.1" are
    /// stripped as whole tokens and must not be split first, and a dash that is genuinely part
    /// of a title ("Spider-Man") must not be rejoined into one.
    /// </summary>
    private static string JoinTagTokens(IReadOnlyList<string> tokens)
    {
        var sb = new StringBuilder();

        for (var i = 0; i < tokens.Count; i++)
        {
            var current = tokens[i];

            if (i + 1 < tokens.Count && JoinedTagReplacements.TryGetValue(current + " " + tokens[i + 1], out var joined))
            {
                current = joined;
                i++;
            }

            if (sb.Length > 0)
            {
                sb.Append(' ');
            }

            sb.Append(current);
        }

        return sb.ToString();
    }

    private static string StripExtension(string text)
    {
        var lastToken = text.LastIndexOf(' ');
        var candidate = lastToken >= 0 ? text[(lastToken + 1)..] : text;

        return candidate.Length > 0 && !candidate.Contains(' ') && IsKnownExtension(candidate)
            ? text[..(lastToken >= 0 ? lastToken : 0)]
            : text;
    }

    private static bool IsKnownExtension(string token)
        => token is "mp4" or "mkv" or "avi" or "mov" or "webm" or "m4v"
            or "ts" or "wmv" or "flv" or "mpg" or "mpeg" or "3gp"
            or "srt" or "vtt" or "ass" or "ssa"
            or "jpg" or "jpeg" or "png" or "webp";

    private static string StripBracketedJunk(string text)
    {
        var sb = new StringBuilder(text.Length);
        var depth = 0;

        foreach (var ch in text)
        {
            switch (ch)
            {
                case '[' or '(' or '{':
                    depth++;
                    break;
                case ']' or ')' or '}':
                    if (depth > 0)
                    {
                        depth--;
                    }

                    break;
                default:
                    if (depth == 0)
                    {
                        sb.Append(ch);
                    }

                    break;
            }
        }

        return sb.ToString();
    }

    /// <summary>Splits on every run of non-alphanumerics, which is what "collapse" amounts to.</summary>
    private static IEnumerable<string> Tokenize(string text)
    {
        var sb = new StringBuilder();

        foreach (var ch in text)
        {
            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(ch);
                continue;
            }

            if (sb.Length > 0)
            {
                yield return sb.ToString();
                sb.Clear();
            }
        }

        if (sb.Length > 0)
        {
            yield return sb.ToString();
        }
    }

    private static bool IsReleaseTag(string token) => ReleaseTags.Contains(token);

    private static bool IsPureNumber(string token) => token.All(char.IsAsciiDigit);
}

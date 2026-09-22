// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Globalization;
using System.Text;
using CourseMoth.Core.Abstractions;

namespace CourseMoth.Core.Services;

/// <summary>
/// Builds human-readable stable keys such as <c>course:csharp-basics</c> and
/// <c>lesson:csharp-basics:01-intro:03-linq</c>. See DomainMap §7.
///
/// A stable key is a fallback identity only: renaming the folder changes it, so it can
/// never be the primary identifier. The generator is deliberately total — it never throws,
/// because import must not fail on a name it cannot slug.
/// </summary>
public sealed class StableKeyGenerator : IStableKeyGenerator
{
    /// <summary>Separator between path segments of a key.</summary>
    private const char SegmentSeparator = ':';

    /// <summary>Separator inside a slug, replacing runs of unusable characters.</summary>
    private const char SlugSeparator = '-';

    public string ForCourse(string normalizedName)
        => Build("course", [Slug(normalizedName)]);

    public string ForModule(string courseKey, int order, string normalizedTitle)
        => Build("module", [CourseSegment(courseKey), Ordinal(order, normalizedTitle)]);

    public string ForLesson(string courseKey, string moduleKey, int order, string normalizedTitle)
        => Build(
            "lesson",
            [
                CourseSegment(courseKey),
                ModuleSegment(moduleKey),
                Ordinal(order, normalizedTitle),
            ]);

    /// <summary>
    /// The course part of a key: the course slug, with the structural <c>course:</c> prefix removed.
    /// Slugs collapse runs of separators, so <c>course:c-sharp</c> would otherwise leave the
    /// remainder looking like a bare <c>c-sharp</c> and lose the course it belongs to.
    /// </summary>
    private static string CourseSegment(string courseKey) => Slug(StripPrefix(courseKey, "course"));

    /// <summary>
    /// The module part of a key. A module key already carries the course slug as its first segment,
    /// so only its own part is kept and an empty module (a flat course) contributes nothing rather
    /// than a placeholder.
    /// </summary>
    private static string ModuleSegment(string moduleKey)
    {
        var body = StripPrefix(moduleKey, "module");
        if (body.Length == 0)
        {
            return string.Empty;
        }

        var separator = body.IndexOf(SegmentSeparator);
        var own = separator >= 0 ? body[(separator + 1)..] : body;

        return Slug(own);
    }

    private static string Build(string prefix, IReadOnlyList<string> segments)
    {
        var sb = new StringBuilder(prefix);
        foreach (var segment in segments)
        {
            // An empty segment is dropped rather than filled with a placeholder: a flat course's
            // lessons have no module, and inventing a synthetic "untitled" module is exactly the
            // leakage DomainMap §3.2 forbids.
            if (segment.Length == 0)
            {
                continue;
            }

            sb.Append(SegmentSeparator).Append(segment);
        }

        // The prefix is never empty, so a key always has at least one segment.
        return sb.ToString();
    }

    /// <summary>Order prefix plus title, e.g. "01-intro". Order is zero-padded so keys sort naturally.</summary>
    private static string Ordinal(int order, string normalizedTitle)
    {
        var slug = Slug(normalizedTitle);
        var ordinal = order.ToString("D2", CultureInfo.InvariantCulture);

        return slug.Length == 0 ? ordinal : ordinal + SlugSeparator + slug;
    }

    /// <summary>Drops a leading "prefix:" segment so nested keys do not repeat it.</summary>
    private static string StripPrefix(string key, string prefix)
    {
        if (string.IsNullOrEmpty(key))
        {
            return string.Empty;
        }

        var marker = prefix + SegmentSeparator;
        return key.StartsWith(marker, StringComparison.Ordinal) ? key[marker.Length..] : key;
    }

    /// <summary>
    /// Lowercase ASCII-ish slug. Non-ASCII letters (Cyrillic included) are transliterated where
    /// a sensible ASCII form exists and dropped where it does not: a stable key is a
    /// human-readable convenience, and punctuation-free keys are worth more here than
    /// round-tripping every alphabet.
    /// </summary>
    private static string Slug(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(value.Length);
        var pendingSeparator = false;

        foreach (var ch in value)
        {
            var mapped = Transliterate(ch);
            foreach (var c in mapped)
            {
                if (char.IsAsciiLetterOrDigit(c))
                {
                    if (pendingSeparator && sb.Length > 0)
                    {
                        sb.Append(SlugSeparator);
                    }

                    pendingSeparator = false;
                    sb.Append(char.ToLowerInvariant(c));
                }
                else
                {
                    pendingSeparator = true;
                }
            }
        }

        return sb.ToString();
    }

    private static string Transliterate(char ch)
    {
        if (char.IsAscii(ch))
        {
            return char.ToString(ch);
        }

        return char.ToLowerInvariant(ch) switch
        {
            'а' => "a", 'б' => "b", 'в' => "v", 'г' => "g", 'д' => "d", 'е' => "e",
            'ё' => "e", 'ж' => "zh", 'з' => "z", 'и' => "i", 'й' => "y", 'к' => "k",
            'л' => "l", 'м' => "m", 'н' => "n", 'о' => "o", 'п' => "p", 'р' => "r",
            'с' => "s", 'т' => "t", 'у' => "u", 'ф' => "f", 'х' => "h", 'ц' => "ts",
            'ч' => "ch", 'ш' => "sh", 'щ' => "sch", 'ъ' => string.Empty, 'ы' => "y",
            'ь' => string.Empty, 'э' => "e", 'ю' => "yu", 'я' => "ya",
            'і' => "i", 'ї' => "yi", 'є' => "ye", 'ґ' => "g",
            'ä' => "a", 'ö' => "o", 'ü' => "u", 'ß' => "ss",
            'à' => "a", 'á' => "a", 'â' => "a", 'ã' => "a", 'å' => "a", 'æ' => "ae",
            'ç' => "c", 'è' => "e", 'é' => "e", 'ê' => "e", 'ë' => "e", 'ì' => "i",
            'í' => "i", 'î' => "i", 'ï' => "i", 'ñ' => "n", 'ò' => "o", 'ó' => "o",
            'ô' => "o", 'õ' => "o", 'ø' => "o", 'ù' => "u", 'ú' => "u", 'û' => "u",
            'ý' => "y", 'ÿ' => "y",
            _ => string.Empty,
        };
    }
}

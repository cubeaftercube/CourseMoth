// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Globalization;

namespace CourseMoth.Data;

/// <summary>
/// Conversions between domain value types and the primitive types SQLite can actually store.
///
/// <para>
/// <b>Why this file exists.</b> sqlite-net maps a fixed set of CLR types: the integral types,
/// <c>float</c>/<c>double</c>/<c>decimal</c>, <c>string</c>, <c>bool</c>, <c>DateTime</c>,
/// <c>DateTimeOffset</c>, <c>TimeSpan</c>, <c>Guid</c>, <c>byte[]</c> and enums (as integers).
/// Three types in <c>CourseMoth.Core.Domain</c> are outside that set and would each fail at
/// runtime rather than at compile time:
/// </para>
/// <list type="bullet">
///   <item><description><see cref="DateOnly"/> — <c>LearningActivity.Date</c>. Not mappable at all.</description></item>
///   <item><description><see cref="TimeSpan"/>? (nullable) — <c>Lesson.Duration</c>. Bare <c>TimeSpan</c>
///   maps to a tick count, but a <i>nullable</i> one is not handled; the null has to be representable.</description></item>
///   <item><description><c>RecurrenceRule</c> — a C# <c>record</c> holding a <c>DayOfWeek[]</c>, which has
///   no column form whatsoever.</description></item>
/// </list>
///
/// <para>
/// Every conversion here is a <b>one-way, lossless, total</b> function on the domain side: any
/// value Core can hold round-trips, and <c>null</c> stays <c>null</c> rather than being folded
/// into a sentinel. That last point is the one that matters — a sentinel such as
/// <c>DateTime.MinValue</c> for "no duration" would be indistinguishable from a genuine zero
/// duration, and a <see cref="DateOnly"/> sentinel would silently shift every streak
/// calculation by the value of the sentinel. Nullable storage columns (<c>long?</c>) make the
/// distinction part of the schema instead.
/// </para>
/// </summary>
internal static class SqliteValueConverters
{
    /// <summary>
    /// <see cref="DateOnly"/> to its <c>yyyyMMdd</c> integer form, e.g. 2026-09-22 to 20260922.
    ///
    /// <para>
    /// <b>Why an integer and not <c>DateOnly.DayNumber</c>.</b> Both are exact and both sort
    /// correctly. The compact form is chosen because the database is meant to be readable and
    /// diffable (DomainMap §1: "the database is small and easy to read back"), and a column of
    /// <c>20260922</c> is legible where <c>739857</c> is not.
    /// </para>
    ///
    /// <para>
    /// <b>Which date this is.</b> Always the local <i>study</i> date, computed once by
    /// <c>IClock.StudyDateFor</c> and never recomputed (TasksStreaksSpec §3). No timezone is
    /// stored alongside it and none should be added: converting this column through UTC is what
    /// would break the streak.
    /// </para>
    /// </summary>
    public static long ToStorage(DateOnly date) => (date.Year * 10000L) + (date.Month * 100L) + date.Day;

    /// <summary>
    /// A <see cref="DateTime"/> reduced to the date key of the day it falls on.
    ///
    /// <para>
    /// <b>This exists because a due date and a recurrence bound have to be on the same scale.</b>
    /// A task's <c>DueDate</c> is a <see cref="DateTime"/> while its recurrence window is a pair
    /// of days, and <c>TaskRepository.ListForDateAsync</c> compares them to each other. Storing
    /// the due date as a tick count puts it at ~5.3e18 and the window bounds at ~2.0e7, and every
    /// comparison between the two then answers nonsense — a due-dated task silently disappears
    /// from its own day. Reducing the due date to a date key puts both sides on one scale, and
    /// makes the SQL <c>=</c>/<c>&lt;=</c>/<c>&gt;=</c> mean what they read as.
    /// </para>
    ///
    /// <para>
    /// The time of day is dropped, deliberately: a task is due on a day, and the day is the
    /// unit the streak and the task list work in. The value is taken verbatim from the DateTime
    /// rather than through <c>ToLocalTime()</c> — the domain sets a due date by constructing it at
    /// midnight in the day the user picked, and converting it would shift the day across the line.
    /// </para>
    /// </summary>
    public static long ToDateKey(DateTime value)
        => (value.Year * 10000L) + (value.Month * 100L) + value.Day;

    /// <summary>
    /// Date key back to a <see cref="DateTime"/> at midnight.
    ///
    /// The <see cref="DateTimeKind"/> comes back <see cref="DateTimeKind.Unspecified"/> rather
    /// than <c>Utc</c>: the key carries a day, not an instant, so asserting a zone on the way out
    /// would invent information the column never held.
    /// </summary>
    public static DateTime DateKeyToDateTime(long value) => DateFromStorage(value).ToDateTime(TimeOnly.MinValue);

    /// <summary>Inverse of <see cref="ToStorage(DateOnly)"/>.</summary>
    public static DateOnly DateFromStorage(long value)
    {
        var year = (int)(value / 10000L);
        var month = (int)((value % 10000L) / 100L);
        var day = (int)(value % 100L);

        return new DateOnly(year, month, day);
    }

    /// <summary>
    /// <see cref="TimeSpan"/> to whole milliseconds.
    ///
    /// <para>
    /// Ticks would be exact too, but the rest of the domain measures playback in milliseconds
    /// (<c>WatchState.PositionMs</c>, <c>LearningActivity.WatchedMs</c>), and a duration column
    /// in the same unit as every other duration needs no mental conversion when reading the
    /// table. Sub-millisecond precision is not something a media container reports.
    /// </para>
    ///
    /// <para>
    /// A <c>null</c> domain value maps to a <c>null</c> column and back. This is load-bearing:
    /// <c>Lesson.Duration</c> is null until the lesson is first opened, and progress falls back
    /// to counting lessons meanwhile (DomainMap §3.3). Zero is a legitimate duration, so it
    /// cannot double as the null marker.
    /// </para>
    /// </summary>
    public static long? ToStorage(TimeSpan? duration) => duration is { } value ? (long)value.TotalMilliseconds : null;

    /// <summary>Inverse of <see cref="ToStorage(TimeSpan?)"/>.</summary>
    public static TimeSpan? DurationFromStorage(long? milliseconds)
        => milliseconds is { } value ? TimeSpan.FromMilliseconds(value) : null;

    /// <summary>
    /// Inverted index for <see cref="ToStorage(DayOfWeek)"/>.
    ///
    /// <b>Do not reorder.</b> These integers are the stored form of every existing recurrence
    /// rule. The order below is <see cref="DayOfWeek"/>'s own order shifted so that Monday is 0,
    /// which is how a recurrence rule reads to a user ("weekdays", "Mon/Wed/Fri").
    /// </summary>
    private static readonly DayOfWeek[] DaysByStorageIndex =
    [
        DayOfWeek.Monday,
        DayOfWeek.Tuesday,
        DayOfWeek.Wednesday,
        DayOfWeek.Thursday,
        DayOfWeek.Friday,
        DayOfWeek.Saturday,
        DayOfWeek.Sunday,
    ];

    /// <summary>
    /// <see cref="DayOfWeek"/> to 0..6 with Monday as 0.
    ///
    /// Deliberately <i>not</i> <c>(int)dayOfWeek</c>: <see cref="DayOfWeek"/> numbers from Sunday,
    /// and reusing that numbering would put Sunday at 0 — the one day a "weekdays" rule must
    /// exclude. Pinning the mapping here means the serialized form never depends on the enum's
    /// declaration order, which is exactly the kind of thing that changes under someone's hands.
    /// </summary>
    public static int ToStorage(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => 0,
        DayOfWeek.Tuesday => 1,
        DayOfWeek.Wednesday => 2,
        DayOfWeek.Thursday => 3,
        DayOfWeek.Friday => 4,
        DayOfWeek.Saturday => 5,
        DayOfWeek.Sunday => 6,
        _ => throw new ArgumentOutOfRangeException(nameof(day), day, "Unknown DayOfWeek value."),
    };

    /// <summary>Inverse of <see cref="ToStorage(DayOfWeek)"/>.</summary>
    public static DayOfWeek DayOfWeekFromStorage(int value)
        => value is >= 0 and <= 6
            ? DaysByStorageIndex[value]
            : throw new ArgumentOutOfRangeException(nameof(value), value, "Stored day index is outside 0..6.");

    /// <summary>
    /// Re-encodes the tick count of a <see cref="DateTime"/> so that <c>Kind</c> survives the
    /// trip through SQLite.
    ///
    /// <para>
    /// <b>Why the Kind bit has to be carried by hand.</b> sqlite-net serializes a
    /// <see cref="DateTime"/> with <c>ToBinary()</c> and reads it back with
    /// <c>FromBinary()</c>, which does preserve <c>Kind</c> — but the round trip is silent about
    /// it, and the whole domain is written against UTC timestamps
    /// (<c>IClock.UtcNow</c> is documented as UTC). A <c>CreatedAt</c> that came back as
    /// <c>Unspecified</c> would compare equal to the value it was written as and still be wrong
    /// the first time anything calls <c>ToLocalTime()</c> on it, which is a bug that surfaces
    /// months later in a sync merge.
    /// </para>
    ///
    /// <para>
    /// The two spare high bits of the tick count's sign position hold
    /// <see cref="DateTimeKind"/>: bit 62 is set for <c>Utc</c>, bit 61 for <c>Local</c>. Both
    /// clear means <c>Unspecified</c>. Ticks never approach 2^61
    /// (<see cref="DateTime.MaxValue"/> is about 3.16e18, and 2^61 is about 2.31e18), so the bits
    /// are genuinely free. Values written this way must be read back through
    /// <see cref="DateTimeFromStorage(long)"/>; a raw tick count would decode as a year-9999 date
    /// if the Utc bit happened to be set.
    /// </para>
    ///
    /// <para>
    /// This is a stricter guarantee than the domain needs today — nothing currently reads
    /// <c>Kind</c> back. It is here because it is free at write time and impossible to retrofit:
    /// rows written without it cannot be told apart from rows written with it.
    /// </para>
    /// </summary>
    public static long ToStorage(DateTime value)
    {
        var encoded = value.Ticks;

        encoded |= value.Kind switch
        {
            DateTimeKind.Utc => UtcKindBit,
            DateTimeKind.Local => LocalKindBit,
            _ => 0L,
        };

        return encoded;
    }

    /// <summary>Inverse of <see cref="ToStorage(DateTime)"/>.</summary>
    public static DateTime DateTimeFromStorage(long value)
    {
        var kind = (value & UtcKindBit) != 0
            ? DateTimeKind.Utc
            : (value & LocalKindBit) != 0
                ? DateTimeKind.Local
                : DateTimeKind.Unspecified;

        return new DateTime(value & TickMask, kind);
    }

    /// <summary>Set for <see cref="DateTimeKind.Utc"/>; bit 62, unused by any real tick count.</summary>
    private const long UtcKindBit = 1L << 62;

    /// <summary>Set for <see cref="DateTimeKind.Local"/>; bit 61, unused by any real tick count.</summary>
    private const long LocalKindBit = 1L << 61;

    /// <summary>The 62 bits a real tick count can occupy.</summary>
    private const long TickMask = (1L << 62) - 1;

    /// <summary>
    /// Formats a UTC instant as <c>o</c>-style round-trip text with an explicit <c>Z</c>.
    ///
    /// Used only by the day key in <c>schema_version</c>, which is a diagnostic record rather
    /// than a queryable field.
    /// </summary>
    public static string ToRoundTripText(DateTime value)
        => value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
}

// SPDX-License-Identifier: AGPL-3.0-or-later

using CourseMoth.Core.Abstractions;

namespace CourseMoth.Core.Tests.Fakes;

/// <summary>
/// An <see cref="IClock"/> pinned to a known instant in a known zone.
///
/// The zone matters: the day boundary is meaningless without one (TasksStreaksSpec §3), and a
/// test that inherits the machine's zone would pass in one place and fail in another.
/// </summary>
public sealed class FakeClock : IClock
{
    private readonly TimeZoneInfo _zone;

    public FakeClock(DateTimeOffset now, TimeZoneInfo? zone = null)
    {
        _zone = zone ?? TimeZoneInfo.Utc;
        Now = now.ToOffset(_zone.GetUtcOffset(now.UtcDateTime));
    }

    /// <summary>The zone the boundary is evaluated in. Defaults to UTC for determinism.</summary>
    public TimeZoneInfo Zone => _zone;

    /// <summary>Current instant, already expressed in <see cref="Zone"/>'s fixed offset.</summary>
    public DateTimeOffset Now { get; private set; }

    public DateTime UtcNow => Now.UtcDateTime;

    public DateOnly StudyDateFor(DateTimeOffset instant, DayBoundaryOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var local = TimeZoneInfo.ConvertTime(instant, _zone);
        var shifted = local - options.DayBoundary;

        return DateOnly.FromDateTime(shifted.DateTime);
    }

    /// <summary>The study date of the current instant.</summary>
    public DateOnly Today(DayBoundaryOptions options) => StudyDateFor(Now, options);

    /// <summary>The instant reached by adding <paramref name="amount"/> to the current instant.</summary>
    public FakeClock Advance(TimeSpan amount)
    {
        Now = Now + amount;
        return this;
    }

    /// <summary>Builds a clock whose local time is exactly the supplied wall-clock time.</summary>
    public static FakeClock AtLocal(int year, int month, int day, int hour, int minute, TimeSpan? utcOffset = null)
    {
        var offset = utcOffset ?? TimeSpan.Zero;
        return new FakeClock(new DateTimeOffset(year, month, day, hour, minute, 0, offset), UtcLike(offset));
    }

    /// <summary>
    /// A zone with a constant <paramref name="offset"/> year round. Windows and Linux disagree
    /// about IANA id spelling, but a fixed custom zone is identical everywhere — and the day
    /// boundary rule depends on the offset, not on the identity of the zone.
    /// </summary>
    private static TimeZoneInfo UtcLike(TimeSpan offset)
        => TimeZoneInfo.CreateCustomTimeZone(
            $"Fixed{offset:hh\\:mm}",
            offset,
            $"Fixed {offset}",
            $"Fixed {offset}");
}

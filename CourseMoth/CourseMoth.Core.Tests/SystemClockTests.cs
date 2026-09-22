// SPDX-License-Identifier: AGPL-3.0-or-later

using CourseMoth.Core.Abstractions;
using CourseMoth.Core.Services;
using Xunit;

namespace CourseMoth.Core.Tests;

/// <summary>
/// The clock is where the day boundary actually lives, so it is worth testing on its own rather
/// than only through the streak and activity services that consume it.
/// </summary>
public sealed class SystemClockTests
{
    [Fact]
    public void UtcNow_comes_from_the_injected_time_provider()
    {
        // TimeProvider is injected rather than DateTime.UtcNow being called directly precisely
        // so a test can place the app at 01:30 without changing the machine clock.
        var instant = new DateTimeOffset(2026, 3, 10, 1, 30, 0, TimeSpan.Zero);
        var clock = new SystemClock(new FixedTimeProvider(instant));

        Assert.Equal(instant.UtcDateTime, clock.UtcNow);
    }

    [Fact]
    public void Two_clocks_over_different_instants_do_not_disagree_by_accident()
    {
        var a = new SystemClock(new FixedTimeProvider(new DateTimeOffset(2026, 3, 10, 1, 30, 0, TimeSpan.Zero)));
        var b = new SystemClock(new FixedTimeProvider(new DateTimeOffset(2026, 3, 10, 23, 30, 0, TimeSpan.Zero)));

        Assert.NotEqual(a.UtcNow, b.UtcNow);
        Assert.Equal(TimeSpan.FromHours(22), b.UtcNow - a.UtcNow);
    }

    [Fact]
    public void A_null_options_argument_is_rejected()
    {
        var clock = new SystemClock(new FixedTimeProvider(DateTimeOffset.UnixEpoch));

        Assert.Throws<ArgumentNullException>(
            () => clock.StudyDateFor(DateTimeOffset.UnixEpoch, null!));
    }

    [Fact]
    public void A_null_time_provider_is_rejected()
    {
        Assert.Throws<ArgumentNullException>(() => new SystemClock(null!));
    }

    [Fact]
    public void The_study_date_is_stable_across_repeated_calls()
    {
        var options = new DayBoundaryOptions();
        var instant = new DateTimeOffset(2026, 3, 11, 2, 15, 0, TimeSpan.Zero);
        var clock = new SystemClock(new FixedTimeProvider(instant));

        var first = clock.StudyDateFor(instant, options);
        var second = clock.StudyDateFor(instant, options);

        // The date is assigned once, when the activity is recorded, and never recomputed.
        Assert.Equal(first, second);
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now;

        public FixedTimeProvider(DateTimeOffset now) => _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }
}

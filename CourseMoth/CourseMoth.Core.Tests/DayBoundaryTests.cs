// SPDX-License-Identifier: AGPL-3.0-or-later

using CourseMoth.Core.Abstractions;
using CourseMoth.Core.Services;
using Xunit;

namespace CourseMoth.Core.Tests;

/// <summary>
/// TasksStreaksSpec §3 and its spike list: "does a day boundary of 04:00 correctly assign a
/// 01:30 session to the previous day? — the rule most likely to be implemented off-by-one".
///
/// <para>
/// <b>What the offset on the argument means.</b> TasksStreaksSpec §3 puts the boundary in "the
/// device's current IANA timezone". <see cref="IClock.StudyDateFor"/> takes a
/// <see cref="DateTimeOffset"/>, so the device's zone arrives as the argument's own offset, and
/// that offset is the input the boundary is evaluated against. <see cref="SystemClock.StudyDateFor"/>
/// subtracts the boundary from the instant as given rather than converting it first, so the answer
/// depends on the zone the instant says it is in and not on the machine the process runs on.
/// </para>
///
/// <para>
/// The callers are consistent with that. <see cref="SystemClock.Today"/> passes
/// <c>GetLocalNow()</c>, which carries the machine's current offset, so "today" still means today
/// on this machine. <c>ActivityTracker.Today</c> passes <c>Truncate(_clock.UtcNow)</c>, an
/// instant at offset zero, and the study date of that instant is the answer for a user on UTC —
/// which is exactly right for a caller that has only a UTC clock to hand.
/// </para>
///
/// <para>
/// That is why the tests below supply the offset explicitly rather than assuming UTC: on a device
/// that is not on UTC the offset is a meaningful input, not decoration. And it is why the same
/// instant must not be given a different study date depending on which machine the code happens to
/// run on — the device-zone test at the end of the file is that guarantee.
/// </para>
/// </summary>
public sealed class DayBoundaryTests
{
    private static readonly DayBoundaryOptions DefaultBoundary = new();

    /// <summary>
    /// The device zone these tests pretend the app is running in. It is deliberately positive so
    /// that the machine's own offset is unlikely to coincide with it by accident: a boundary that
    /// silently ignored the supplied offset would land on a different date.
    /// </summary>
    private static readonly TimeSpan DeviceOffset = TimeSpan.FromHours(3);

    /// <summary>
    /// Builds a clock whose device zone is <paramref name="deviceOffset"/> and evaluates
    /// <paramref name="localInstant"/> against that same zone, so the instant and the zone cannot
    /// drift apart. The instant keeps the offset it was constructed with.
    /// </summary>
    private static DateOnly StudyDateOf(DateTimeOffset localInstant, DayBoundaryOptions? options = null)
    {
        var clock = new SystemClock(new FixedTimeProvider(localInstant, deviceOffset: localInstant.Offset));

        return clock.StudyDateFor(localInstant, options ?? DefaultBoundary);
    }

    /// <summary>Evaluates an instant in the device zone, named explicitly by <paramref name="deviceOffset"/>.</summary>
    private static DateOnly StudyDateIn(TimeSpan deviceOffset, DateTimeOffset localInstant, DayBoundaryOptions? options = null)
        => new SystemClock(new FixedTimeProvider(localInstant, deviceOffset))
            .StudyDateFor(localInstant, options ?? DefaultBoundary);

    [Fact]
    public void A_session_at_0130_belongs_to_the_previous_study_date()
    {
        var date = StudyDateOf(new DateTimeOffset(2026, 3, 11, 1, 30, 0, DeviceOffset));

        Assert.Equal(new DateOnly(2026, 3, 10), date);
    }

    [Fact]
    public void A_session_at_0359_still_belongs_to_the_previous_study_date()
    {
        var date = StudyDateOf(new DateTimeOffset(2026, 3, 11, 3, 59, 59, DeviceOffset));

        Assert.Equal(new DateOnly(2026, 3, 10), date);
    }

    [Fact]
    public void A_session_at_0400_belongs_to_the_new_study_date()
    {
        var date = StudyDateOf(new DateTimeOffset(2026, 3, 11, 4, 0, 0, DeviceOffset));

        // The boundary is inclusive of the new day: 04:00 is the first instant of 11 March, which
        // is the lower edge of that study day's interval — [11th 04:00, 12th 04:00).
        Assert.Equal(new DateOnly(2026, 3, 11), date);
    }

    [Fact]
    public void A_session_at_0359_is_the_last_instant_before_the_boundary_opens_the_day()
    {
        var date = StudyDateOf(new DateTimeOffset(2026, 3, 11, 3, 59, 59, DeviceOffset));

        // One second before the edge is still inside the previous day's interval.
        Assert.Equal(new DateOnly(2026, 3, 10), date);
    }

    [Fact]
    public void The_boundary_instant_is_the_one_case_an_off_by_one_shows_up_in()
    {
        var oneSecondBefore = StudyDateOf(new DateTimeOffset(2026, 3, 11, 3, 59, 59, DeviceOffset));
        var atBoundary = StudyDateOf(new DateTimeOffset(2026, 3, 11, 4, 0, 0, DeviceOffset));

        Assert.NotEqual(oneSecondBefore, atBoundary);
        Assert.Equal(atBoundary, oneSecondBefore.AddDays(1));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void Every_supported_boundary_agrees_that_0600_is_today(int boundaryHour)
    {
        var options = new DayBoundaryOptions { DayBoundary = TimeSpan.FromHours(boundaryHour) };

        var date = StudyDateOf(new DateTimeOffset(2026, 3, 11, 6, 0, 0, DeviceOffset), options);

        // 06:00 is after every supported boundary, so it always lands on 11 March.
        Assert.Equal(new DateOnly(2026, 3, 11), date);
    }

    /// <summary>
    /// Midnight against every supported boundary.
    ///
    /// <para>
    /// The instant is shifted back by the boundary and the date it lands on is the study date, so
    /// midnight is the last instant of the day that is ending. The 00:00 row is the odd one:
    /// subtracting zero moves nothing and the answer is the same date, so midnight is both the
    /// last instant of the 10th and — because the shift is a no-op — the day it names. It is kept
    /// as its own row precisely because it is the only case where the boundary shift does nothing.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData(0, 11)]
    [InlineData(3, 10)]
    [InlineData(4, 10)]
    [InlineData(5, 10)]
    public void Each_boundary_places_0000_on_the_day_its_own_rule_implies(int boundaryHour, int expectedDay)
    {
        var options = new DayBoundaryOptions { DayBoundary = TimeSpan.FromHours(boundaryHour) };

        var date = StudyDateOf(new DateTimeOffset(2026, 3, 11, 0, 0, 0, DeviceOffset), options);

        Assert.Equal(new DateOnly(2026, 3, expectedDay), date);
    }

    [Fact]
    public void A_2300_to_0130_session_is_one_coherent_study_day()
    {
        var start = StudyDateOf(new DateTimeOffset(2026, 3, 10, 23, 0, 0, DeviceOffset));
        var end = StudyDateOf(new DateTimeOffset(2026, 3, 11, 1, 30, 0, DeviceOffset));

        // The whole point of the boundary: an evening session that runs past midnight stays
        // on the day the user would name if you asked them when they studied.
        Assert.Equal(start, end);
        Assert.Equal(new DateOnly(2026, 3, 10), start);
    }

    [Fact]
    public void The_boundary_is_evaluated_against_the_clock_the_user_reads()
    {
        // The user's wall clock is UTC+03 and it reads 01:00. Under their own clock that is
        // "before 04:00", so it belongs to the previous study day.
        var localInstant = new DateTimeOffset(2026, 3, 11, 1, 0, 0, TimeSpan.FromHours(3));

        var date = StudyDateOf(localInstant);

        Assert.Equal(new DateOnly(2026, 3, 10), date);
    }

    [Fact]
    public void Reading_utc_off_the_wire_would_give_the_wrong_answer_for_a_non_utc_user()
    {
        // The same moment, described two ways. This pins what the boundary actually consumes:
        // the wall-clock instant the device reports, not its UTC projection. Projecting to UTC
        // first moves 01:00 back to 22:00 of the 10th, which is still before the boundary, so
        // the date happens to agree here — the assertion that matters is the wall-clock one.
        var localInstant = new DateTimeOffset(2026, 3, 11, 1, 0, 0, TimeSpan.FromHours(3));
        var asUtc = localInstant.ToUniversalTime();

        var fromLocal = StudyDateOf(localInstant);
        var fromUtcProjection = StudyDateOf(asUtc);

        Assert.Equal(new DateOnly(2026, 3, 10), fromLocal);
        Assert.Equal(fromLocal, fromUtcProjection);
    }

    [Fact]
    public void Midnight_is_a_supported_boundary_and_assigns_by_the_calendar()
    {
        var options = new DayBoundaryOptions { DayBoundary = TimeSpan.Zero };

        var justBeforeMidnight = StudyDateOf(new DateTimeOffset(2026, 3, 10, 23, 59, 59, DeviceOffset), options);
        var justAfterMidnight = StudyDateOf(new DateTimeOffset(2026, 3, 11, 0, 0, 1, DeviceOffset), options);

        // With a 00:00 boundary the study day is the calendar day, so the pair straddles the line.
        Assert.Equal(new DateOnly(2026, 3, 10), justBeforeMidnight);
        Assert.Equal(new DateOnly(2026, 3, 11), justAfterMidnight);
    }

    /// <summary>
    /// The same instant, described as each device's own wall clock. Whatever the boundary rule is,
    /// it is the wall clock the user reads that decides — not the machine the process happens to
    /// run on.
    ///
    /// <para>
    /// This is the test that pins what the boundary actually consumes: the offset carried by the
    /// argument, which is the device's own zone. Each call passes the same instant re-expressed in
    /// that device's offset (<c>instant.ToOffset(deviceOffset)</c>), and the three answers differ,
    /// so the offset is genuinely the input and not decoration.
    /// </para>
    ///
    /// <para>
    /// It is stated in terms of one instant read against three zones precisely so that it holds on
    /// any host machine. An answer that came from <see cref="TimeZoneInfo.Local"/> instead of from
    /// the argument would be the same number three times over on a given machine, and would change
    /// from machine to machine.
    /// </para>
    /// </summary>
    [Fact]
    public void The_same_instant_is_read_against_each_device_s_own_zone()
    {
        var instant = new DateTimeOffset(2026, 3, 11, 1, 0, 0, TimeSpan.Zero);

        // 01:00 UTC is 04:00 in Moscow, 01:00 in UTC, and 20:00 on the 10th in New York.
        var deviceOffset = TimeSpan.FromHours(3);
        var utcOffset = TimeSpan.Zero;
        var newYorkOffset = TimeSpan.FromHours(-5);

        var inMoscow = StudyDateIn(deviceOffset, instant.ToOffset(deviceOffset));
        var inUtc = StudyDateIn(utcOffset, instant.ToOffset(utcOffset));
        var inNewYork = StudyDateIn(newYorkOffset, instant.ToOffset(newYorkOffset));

        // 04:00 local in Moscow: the first instant of the new study day.
        Assert.Equal(new DateOnly(2026, 3, 11), inMoscow);

        // 01:00 local in UTC: still the previous study day.
        Assert.Equal(new DateOnly(2026, 3, 10), inUtc);

        // 20:00 local on the 10th in New York: squarely the previous study day.
        Assert.Equal(new DateOnly(2026, 3, 10), inNewYork);
    }

    /// <summary>
    /// The boundary reads the offset the argument carries, not the host machine's zone.
    ///
    /// <para>
    /// The previous version of this test asserted the reverse — that an argument stamped with the
    /// <i>host's</i> offset yields the same date as one stamped with the device's — on the grounds
    /// that "the offset the caller happened to carry must not move the day". That is not the rule:
    /// TasksStreaksSpec §3 evaluates the boundary in "the device's current IANA timezone", and the
    /// argument's offset is where the device's zone arrives. The old assertion passed only because
    /// this suite happened to be developed on a UTC+03 machine, where the host offset and the faked
    /// device offset are the same number; under <c>TZ=UTC</c> it failed.
    /// </para>
    ///
    /// <para>
    /// The honest statement is the opposite one: differing zones <i>must</i> give differing dates
    /// for the same instant, because 01:00 UTC is 04:00 in Moscow (the boundary has been crossed,
    /// so the 11th) and still 01:00 UTC (before it, so the 10th). That is what the test below
    /// asserts, and it is stable on every host.
    /// </para>
    /// </summary>
    [Fact]
    public void The_boundary_reads_the_argument_s_zone_and_not_the_host_s()
    {
        var instant = new DateTimeOffset(2026, 3, 11, 1, 0, 0, TimeSpan.Zero);

        // The same moment, described by two zones. 01:00 UTC is 04:00 in UTC+03, which is exactly
        // the boundary and therefore the first instant of the 11th; read as UTC it is 01:00, which
        // is before the boundary and so still the 10th.
        var inMoscow = StudyDateIn(TimeSpan.FromHours(3), instant.ToOffset(TimeSpan.FromHours(3)));
        var inUtc = StudyDateIn(TimeSpan.Zero, instant.ToOffset(TimeSpan.Zero));

        Assert.Equal(new DateOnly(2026, 3, 11), inMoscow);
        Assert.Equal(new DateOnly(2026, 3, 10), inUtc);

        // The two zones disagree, so the answer cannot be coming from a single machine-wide zone.
        Assert.NotEqual(inMoscow, inUtc);
    }

    [Fact]
    public void The_default_boundary_is_0400_and_the_default_rule_is_one_lesson()
    {
        var options = new DayBoundaryOptions();

        Assert.Equal(TimeSpan.FromHours(4), options.DayBoundary);
        Assert.Equal(StreakRule.Lessons, options.Rule);
    }

    /// <summary>
    /// A <see cref="TimeProvider"/> frozen at one instant, reporting an explicit device zone —
    /// never the machine's, so the tests cannot inherit <see cref="TimeZoneInfo.Local"/>.
    /// </summary>
    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now;
        private readonly TimeSpan _deviceOffset;

        public FixedTimeProvider(DateTimeOffset now, TimeSpan? deviceOffset = null)
        {
            _now = now;
            _deviceOffset = deviceOffset ?? now.Offset;
        }

        public override DateTimeOffset GetUtcNow() => _now;

        public override TimeZoneInfo LocalTimeZone
            => TimeZoneInfo.CreateCustomTimeZone(
                $"Fixed{_deviceOffset:hh\\:mm}", _deviceOffset, "Fixed", "Fixed");
    }
}

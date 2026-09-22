// SPDX-License-Identifier: AGPL-3.0-or-later

using CourseMoth.Core.Abstractions;
using CourseMoth.Core.Services;
using CourseMoth.Core.Tests.Fakes;
using Xunit;

namespace CourseMoth.Core.Tests;

/// <summary>
/// TasksStreaksSpec §4. Streaks are a fold over the activity log on read — never stored,
/// because a stored counter and a merged activity log disagree after sync.
///
/// The behaviour that matters most here is the "today or yesterday" rule: showing 0 at
/// breakfast because today has not happened yet is actively demotivating.
/// </summary>
public sealed class StreakCalculatorTests
{
    /// <summary>12:00 on 10 March 2026, well past the 04:00 boundary, so today is 10 March.</summary>
    private static readonly DateTimeOffset Noon = new(2026, 3, 10, 12, 0, 0, TimeSpan.Zero);

    private static (StreakCalculator Calculator, InMemoryActivityRepository Activities, DayBoundaryOptions Options)
        Build(DateTimeOffset now, DayBoundaryOptions? options = null)
    {
        var effective = options ?? new DayBoundaryOptions();
        var activities = new InMemoryActivityRepository();
        var clock = new FakeClock(now);
        var calculator = new StreakCalculator(activities, clock, effective);

        return (calculator, activities, effective);
    }

    private static Task<int> CurrentAsync(
        StreakCalculator calculator,
        DayBoundaryOptions options)
        => calculator.CurrentAsync(new StreakContext(options, Guid.Empty));

    // ---- A day still in progress --------------------------------------------------------------

    [Fact]
    public async Task Yesterday_qualifying_keeps_the_streak_alive_before_today_has_any_activity()
    {
        var (calculator, activities, options) = Build(Noon);

        // Given: three consecutive study days ending yesterday. Today has no row at all —
        // this is the state of the app at 12:00 before the user has studied.
        activities.SeedDay(new DateOnly(2026, 3, 7), completedLessons: 1, watchedMs: 0);
        activities.SeedDay(new DateOnly(2026, 3, 8), completedLessons: 2, watchedMs: 0);
        activities.SeedDay(new DateOnly(2026, 3, 9), completedLessons: 1, watchedMs: 0);

        var streak = await CurrentAsync(calculator, options);

        // When: the streak is shown at breakfast. It must not look broken.
        Assert.Equal(3, streak);
    }

    [Fact]
    public async Task A_full_day_with_no_activity_breaks_the_streak()
    {
        var (calculator, activities, options) = Build(Noon);

        // The gap is 9 March: a whole day passed with nothing in it.
        activities.SeedDay(new DateOnly(2026, 3, 7), 1, 0);
        activities.SeedDay(new DateOnly(2026, 3, 8), 1, 0);

        var streak = await CurrentAsync(calculator, options);

        Assert.Equal(0, streak);
    }

    [Fact]
    public async Task Today_qualifying_extends_the_streak_including_today()
    {
        var (calculator, activities, options) = Build(Noon);

        activities.SeedDay(new DateOnly(2026, 3, 8), 1, 0);
        activities.SeedDay(new DateOnly(2026, 3, 9), 1, 0);
        activities.SeedDay(new DateOnly(2026, 3, 10), 1, 0);

        var streak = await CurrentAsync(calculator, options);

        Assert.Equal(3, streak);
    }

    [Fact]
    public async Task Today_qualifying_with_yesterday_empty_counts_only_today()
    {
        var (calculator, activities, options) = Build(Noon);

        // Studying today after a lapse is the first day of the new streak, not a continuation.
        activities.SeedDay(new DateOnly(2026, 3, 10), 1, 0);

        var streak = await CurrentAsync(calculator, options);

        Assert.Equal(1, streak);
    }

    [Fact]
    public async Task An_empty_log_has_no_streak()
    {
        var (calculator, _, options) = Build(Noon);

        Assert.Equal(0, await CurrentAsync(calculator, options));
    }

    [Fact]
    public async Task A_day_that_did_not_qualify_does_not_extend_the_streak()
    {
        var (calculator, activities, options) = Build(Noon);

        activities.SeedDay(new DateOnly(2026, 3, 8), 1, 0);
        activities.SeedDay(new DateOnly(2026, 3, 9), 0, 0, countsForStreak: false);
        activities.SeedDay(new DateOnly(2026, 3, 10), 1, 0);

        // The row exists — the user opened the app — but the day did not satisfy the rule.
        var streak = await CurrentAsync(calculator, options);

        Assert.Equal(1, streak);
    }

    // ---- The rule ------------------------------------------------------------------------------

    [Fact]
    public async Task The_default_rule_is_one_completed_lesson()
    {
        var (calculator, activities, options) = Build(Noon);

        activities.SeedDay(new DateOnly(2026, 3, 10), completedLessons: 1, watchedMs: 0, countsForStreak: true);

        Assert.Equal(StreakRule.Lessons, options.Rule);
        Assert.Equal(1, await CurrentAsync(calculator, options));
    }

    [Theory]
    [InlineData(19, false)]
    [InlineData(20, true)]
    [InlineData(60, true)]
    public void The_minutes_rule_qualifies_at_its_threshold(int minutesWatched, bool expected)
    {
        var (calculator, _, _) = Build(Noon);
        var options = new DayBoundaryOptions { Rule = StreakRule.Minutes, MinutesThreshold = 20 };

        var activity = new Domain.LearningActivity
        {
            Date = new DateOnly(2026, 3, 10),
            WatchedMs = minutesWatched * 60_000L,
        };

        Assert.Equal(expected, calculator.QualifiesForStreak(activity, options));
    }

    [Fact]
    public void A_lesson_rule_ignores_watched_time_entirely()
    {
        var (calculator, _, _) = Build(Noon);
        var options = new DayBoundaryOptions { Rule = StreakRule.Lessons };

        var watchedButNothingFinished = new Domain.LearningActivity
        {
            Date = new DateOnly(2026, 3, 10),
            CompletedLessons = 0,
            WatchedMs = 90 * 60_000L,
        };

        // Task completion and streak eligibility are separate mechanisms: a user who watches
        // 90 minutes without completing a lesson has not had a study day under this rule.
        Assert.False(calculator.QualifiesForStreak(watchedButNothingFinished, options));
    }

    // ---- The boundary --------------------------------------------------------------------------

    [Fact]
    public async Task A_session_at_0130_counts_towards_the_previous_day()
    {
        // It is 01:30 on 11 March; under a 04:00 boundary the study day is still 10 March.
        var (calculator, activities, options) = Build(new DateTimeOffset(2026, 3, 11, 1, 30, 0, TimeSpan.Zero));

        activities.SeedDay(new DateOnly(2026, 3, 10), 1, 0);

        var streak = await CurrentAsync(calculator, options);

        // "Today" for the streak is 10 March, so the day qualifies and the streak is alive.
        Assert.Equal(1, streak);
    }

    [Fact]
    public async Task The_streak_does_not_break_merely_because_the_calendar_moved_on()
    {
        // 01:30 after a late session: the calendar says 11 March, the study day is 10 March.
        var (calculator, activities, options) = Build(new DateTimeOffset(2026, 3, 11, 1, 30, 0, TimeSpan.Zero));

        activities.SeedDay(new DateOnly(2026, 3, 8), 1, 0);
        activities.SeedDay(new DateOnly(2026, 3, 9), 1, 0);
        activities.SeedDay(new DateOnly(2026, 3, 10), 1, 0);

        // Reading "today" from DateTime.Today here would make 10 March look like history and
        // report a streak that stops at 9 March.
        Assert.Equal(3, await CurrentAsync(calculator, options));
    }

    // ---- Longest -------------------------------------------------------------------------------

    [Fact]
    public async Task The_longest_streak_survives_a_break()
    {
        var (calculator, activities, _) = Build(Noon);

        activities.SeedDay(new DateOnly(2026, 1, 1), 1, 0);
        activities.SeedDay(new DateOnly(2026, 1, 2), 1, 0);
        activities.SeedDay(new DateOnly(2026, 1, 3), 1, 0);
        activities.SeedDay(new DateOnly(2026, 1, 4), 1, 0);
        // A gap, then a short current run.
        activities.SeedDay(new DateOnly(2026, 3, 9), 1, 0);
        activities.SeedDay(new DateOnly(2026, 3, 10), 1, 0);

        var current = await CurrentAsync(calculator, new DayBoundaryOptions());
        var longest = await calculator.LongestAsync();

        Assert.Equal(2, current);
        Assert.Equal(4, longest);
    }

    [Fact]
    public async Task The_longest_streak_is_zero_for_an_empty_log()
    {
        var (calculator, _, _) = Build(Noon);

        Assert.Equal(0, await calculator.LongestAsync());
    }

    [Fact]
    public async Task The_longest_streak_counts_a_single_day_as_length_one()
    {
        var (calculator, activities, _) = Build(Noon);

        activities.SeedDay(new DateOnly(2026, 3, 10), 1, 0);

        Assert.Equal(1, await calculator.LongestAsync());
    }
}

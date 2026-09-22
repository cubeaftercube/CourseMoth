// SPDX-License-Identifier: AGPL-3.0-or-later

using CourseMoth.Core.Domain;
using SQLite;

namespace CourseMoth.Data.Entities;

/// <summary>
/// Storage row for <see cref="Core.Domain.WatchState"/> — the centre of the system and, by a
/// wide margin, the most frequently written row in the database (DomainMap §3.4). See
/// <see cref="CourseRow"/> for the enum-as-int decision that applies to
/// <c>CompletionSource</c> here.
/// </summary>
[Table("watch_states")]
internal sealed class WatchStateRow
{
    /// <summary>Primary key: one lesson, one state. Not a separate Id — the domain says LessonId is the key.</summary>
    [PrimaryKey]
    public Guid LessonId { get; set; }

    public long PositionMs { get; set; }

    /// <summary>0..1. A cached projection of position/duration, recomputable and therefore not authoritative.</summary>
    public double ProgressPercent { get; set; }

    public bool IsCompleted { get; set; }

    /// <summary><see cref="CompletionSource"/> as int.</summary>
    public int CompletionSource { get; set; }

    public long? CompletedAt { get; set; }

    /// <summary>
    /// While true, threshold auto-completion is suppressed, so that "mark as not completed"
    /// can hold. Stored because it is a user decision, not a derived value.
    /// </summary>
    public bool ManualUnmark { get; set; }

    public long UpdatedAt { get; set; }
    public Guid LastDeviceId { get; set; }

    public WatchState ToDomain() => new()
    {
        LessonId = LessonId,
        PositionMs = PositionMs,
        ProgressPercent = ProgressPercent,
        IsCompleted = IsCompleted,
        CompletionSource = (CompletionSource)CompletionSource,
        CompletedAt = CompletedAt is { } completed ? SqliteValueConverters.DateTimeFromStorage(completed) : null,
        ManualUnmark = ManualUnmark,
        UpdatedAt = SqliteValueConverters.DateTimeFromStorage(UpdatedAt),
        LastDeviceId = LastDeviceId,
    };

    public static WatchStateRow FromDomain(WatchState state) => new()
    {
        LessonId = state.LessonId,
        PositionMs = state.PositionMs,
        ProgressPercent = state.ProgressPercent,
        IsCompleted = state.IsCompleted,
        CompletionSource = (int)state.CompletionSource,
        CompletedAt = state.CompletedAt is { } completed ? SqliteValueConverters.ToStorage(completed) : null,
        ManualUnmark = state.ManualUnmark,
        UpdatedAt = SqliteValueConverters.ToStorage(state.UpdatedAt),
        LastDeviceId = state.LastDeviceId,
    };
}

/// <summary>
/// Storage row for <see cref="Core.Domain.RecurrenceRule"/>.
///
/// <para>
/// <b>This is the type sqlite-net has no answer for.</b> <see cref="RecurrenceRule"/> is a C#
/// <c>record</c> — a reference type with a primary constructor — holding a
/// <see cref="DayOfWeek"/> array. There is no column form for it and no attribute that produces
/// one. Two representations were possible:
/// </para>
/// <list type="number">
///   <item><description>Serialize the whole rule to a JSON string in one column.</description></item>
///   <item><description>Flatten it into primitive columns: a bitmask int for the days and a
///   nullable tick count for <c>Until</c>.</description></item>
/// </list>
///
/// <para>
/// <b>The second was chosen</b>, because it is what the rest of this assembly already does.
/// <c>Lesson.Duration</c> and <c>WatchState.CompletedAt</c> are nullable primitives, not JSON
/// blobs; a rule stored as JSON would be the single column in the schema that cannot be compared,
/// sorted, indexed or read by eye. A bitmask is also strictly smaller, and it makes "which days
/// does this task repeat on" a question SQLite can answer.
/// </para>
///
/// <para>
/// The cost is a custom encoding, and it is paid deliberately: a JSON blob would round-trip
/// through <c>System.Text.Json</c> with no code of ours at all, whereas
/// <see cref="SqliteValueConverters.ToStorage(DayOfWeek)"/> is a mapping that has to be kept
/// honest. It is pinned there with the reason it does not reuse <see cref="DayOfWeek"/>'s own
/// numbering, and it is covered by the round-trip harness.
/// </para>
///
/// <para>
/// <b>The null case is not encoded.</b> A task with no recurrence has <c>RecurrenceDays = null</c>
/// and <c>RecurrenceUntil = null</c>; a task that repeats until a date but on no particular day
/// would be <c>Days = []</c>, which is a non-null empty array and therefore a distinct stored
/// value. Folding "no rule" into a zero bitmask would make it indistinguishable from
/// <c>Days = []</c> and would create a rule that never fires.
/// </para>
/// </summary>
[Table("learning_tasks")]
internal sealed class LearningTaskRow
{
    [PrimaryKey]
    public Guid Id { get; set; }

    public string Title { get; set; } = string.Empty;

    /// <summary>Null for a library-wide task.</summary>
    [Indexed]
    public Guid? CourseId { get; set; }

    /// <summary>Null unless the task is scoped to a single module.</summary>
    public Guid? ModuleId { get; set; }

    /// <summary><see cref="TaskSourceType"/> as int.</summary>
    public int Source { get; set; }

    /// <summary><see cref="TaskGoalType"/> as int.</summary>
    public int GoalType { get; set; }

    public double TargetValue { get; set; }
    public double CompletedValue { get; set; }

    /// <summary>
    /// The day the task is due, as a <c>yyyyMMdd</c> date key — <b>not</b> a tick count.
    ///
    /// <para>
    /// This column is compared against <see cref="RecurrenceUntil"/> and the caller's day key in
    /// <c>TaskRepository.ListForDateAsync</c>. All three must use the same encoding or the
    /// comparisons are between unlike quantities: a tick count is ~5.3e18 and a date key is
    /// ~2.0e7, so a due-dated task would never match its own day. Storing it as a date key puts
    /// the due date, the recurrence bound and the query argument on one scale.
    /// </para>
    ///
    /// <para>
    /// The time of day is dropped: a task is due on a day. See
    /// <see cref="SqliteValueConverters.ToDateKey"/>.
    /// </para>
    /// </summary>
    public long? DueDateKey { get; set; }

    /// <summary>
    /// Bit 0 is Monday through bit 6 is Sunday, per
    /// <see cref="SqliteValueConverters.ToStorage(DayOfWeek)"/>. Null means the task does not
    /// recur at all; a value of 0 means it recurs on no day, which is a different (and empty) rule.
    /// </summary>
    public int? RecurrenceDays { get; set; }

    /// <summary>
    /// Last day the rule fires, inclusive, as a <c>yyyyMMdd</c> date key — the same encoding as
    /// <see cref="DueDateKey"/>, because the two are compared to each other. Only meaningful
    /// alongside <see cref="RecurrenceDays"/>.
    /// </summary>
    public long? RecurrenceUntilKey { get; set; }

    public bool IsCompleted { get; set; }
    public long? CompletedAt { get; set; }

    public long CreatedAt { get; set; }
    public long UpdatedAt { get; set; }

    public LearningTask ToDomain() => new()
    {
        Id = Id,
        Title = Title,
        CourseId = CourseId,
        ModuleId = ModuleId,
        Source = (TaskSourceType)Source,
        GoalType = (TaskGoalType)GoalType,
        TargetValue = TargetValue,
        CompletedValue = CompletedValue,
        DueDate = DueDateKey is { } due ? SqliteValueConverters.DateKeyToDateTime(due) : null,
        Recurrence = RecurrenceFromStorage(RecurrenceDays, RecurrenceUntilKey),
        IsCompleted = IsCompleted,
        CompletedAt = CompletedAt is { } completed ? SqliteValueConverters.DateTimeFromStorage(completed) : null,
        CreatedAt = SqliteValueConverters.DateTimeFromStorage(CreatedAt),
        UpdatedAt = SqliteValueConverters.DateTimeFromStorage(UpdatedAt),
    };

    public static LearningTaskRow FromDomain(LearningTask task)
    {
        var (days, until) = RecurrenceToStorage(task.Recurrence);

        return new LearningTaskRow
        {
            Id = task.Id,
            Title = task.Title,
            CourseId = task.CourseId,
            ModuleId = task.ModuleId,
            Source = (int)task.Source,
            GoalType = (int)task.GoalType,
            TargetValue = task.TargetValue,
            CompletedValue = task.CompletedValue,
            DueDateKey = task.DueDate is { } due ? SqliteValueConverters.ToDateKey(due) : null,
            RecurrenceDays = days,
            RecurrenceUntilKey = until,
            IsCompleted = task.IsCompleted,
            CompletedAt = task.CompletedAt is { } completed ? SqliteValueConverters.ToStorage(completed) : null,
            CreatedAt = SqliteValueConverters.ToStorage(task.CreatedAt),
            UpdatedAt = SqliteValueConverters.ToStorage(task.UpdatedAt),
        };
    }

    /// <summary>
    /// Packs a rule's days into a bitmask and its <c>Until</c> into a date key.
    /// Returns <c>(null, null)</c> for a task that does not recur.
    ///
    /// <c>Until</c> is a date key, not a tick count, because the repository compares it against
    /// the due date and the caller's day — all three are day-granular and all three share the
    /// <c>yyyyMMdd</c> encoding.
    /// </summary>
    private static (int? Days, long? Until) RecurrenceToStorage(RecurrenceRule? rule)
    {
        if (rule is null)
        {
            return (null, null);
        }

        var mask = 0;

        foreach (var day in rule.Days)
        {
            mask |= 1 << SqliteValueConverters.ToStorage(day);
        }

        // The empty rule is stored as a real 0 rather than as null: Days = [] is a rule that
        // exists and matches nothing, which is not the same as having no rule.
        return (mask, rule.Until is { } until ? SqliteValueConverters.ToDateKey(until) : null);
    }

    /// <summary>
    /// Unpacks a bitmask back into a rule. The days come back in Monday-first order, which is
    /// the order <see cref="SqliteValueConverters.ToStorage(DayOfWeek)"/> assigns them; a
    /// <see cref="RecurrenceRule"/>, whose only members are a day set and an optional end date,
    /// carries no meaning in its ordering.
    /// </summary>
    private static RecurrenceRule? RecurrenceFromStorage(int? mask, long? untilKey)
    {
        if (mask is not { } bits)
        {
            return null;
        }

        var days = new List<DayOfWeek>(7);

        for (var index = 0; index < 7; index++)
        {
            if ((bits & (1 << index)) != 0)
            {
                days.Add(SqliteValueConverters.DayOfWeekFromStorage(index));
            }
        }

        return new RecurrenceRule(
            days.ToArray(),
            untilKey is { } end ? SqliteValueConverters.DateKeyToDateTime(end) : null);
    }
}

/// <summary>
/// Storage row for <see cref="Core.Domain.LearningActivity"/>. See DomainMap §5.2.
///
/// <para>
/// <b>This is the type that silently corrupts every streak calculation if it is got wrong.</b>
/// The domain keys the table by a <see cref="DateOnly"/>, which sqlite-net cannot map — there is
/// no attribute, no converter hook and no convenience overload that will accept it. The date is
/// stored as a <c>yyyyMMdd</c> integer (see <see cref="SqliteValueConverters.ToStorage(DateOnly)"/>),
/// which is exact and sorts correctly, and it remains the primary key: this table is
/// "one row per day that had any activity" and nothing else identifies a row.
/// </para>
///
/// <para>
/// <b>The date is the local study date, never a UTC date.</b> It is computed once by
/// <c>IClock.StudyDateFor</c> when the activity is recorded and never recomputed — a streak that
/// rewrites itself on a timezone change is worse than one that is a few hours off
/// (TasksStreaksSpec §3). Converting this column through UTC anywhere in this file would break
/// that rule, so the column is written and read verbatim.
/// </para>
/// </summary>
[Table("learning_activities")]
internal sealed class LearningActivityRow
{
    /// <summary>
    /// The study date as <c>yyyyMMdd</c>. Primary key: one row per day, by construction.
    /// </summary>
    [PrimaryKey]
    public long DateKey { get; set; }

    public int CompletedLessons { get; set; }

    /// <summary>Actual playback time, not wall-clock time with the app open.</summary>
    public long WatchedMs { get; set; }

    public bool CountsForStreak { get; set; }

    public long UpdatedAt { get; set; }

    public LearningActivity ToDomain() => new()
    {
        Date = SqliteValueConverters.DateFromStorage(DateKey),
        CompletedLessons = CompletedLessons,
        WatchedMs = WatchedMs,
        CountsForStreak = CountsForStreak,
        UpdatedAt = SqliteValueConverters.DateTimeFromStorage(UpdatedAt),
    };

    public static LearningActivityRow FromDomain(LearningActivity activity) => new()
    {
        DateKey = SqliteValueConverters.ToStorage(activity.Date),
        CompletedLessons = activity.CompletedLessons,
        WatchedMs = activity.WatchedMs,
        CountsForStreak = activity.CountsForStreak,
        UpdatedAt = SqliteValueConverters.ToStorage(activity.UpdatedAt),
    };
}

# Tasks & Streaks Specification

Daily tasks, streaks, and activity tracking — the motivational layer.

Without an explicit specification this layer quietly becomes illogical: a streak that counts a day twice, a task that can never be completed, a "study day" that depends on which screen you opened.

> **Related:** [DomainMap](../Maps/DomainMap.md#5-motivation) · [UXMap](../Maps/UXMap.md#37-tasks) · [UserFlowMap](../Maps/UserFlowMap.md#13-scenario-the-daily-routine) · [OpenQuestions](../OpenQuestions.md)

---

## 1. Core concepts

| Concept | Definition |
|---|---|
| **Activity** | A single day's record: lessons completed, time watched |
| **Study day** | A day that satisfies the streak rule |
| **Streak** | Consecutive study days ending today or yesterday |
| **Task** | A goal with a target value, automatic or user-created |

**Task completion and streak eligibility are separate mechanisms.** Completing a task is a goal the user set; a study day is a threshold the app defines. They interact but are not the same thing — a user who watches 40 minutes without completing any task still has a study day.

---

## 2. Activity tracking

```csharp
public class LearningActivity
{
    public DateOnly Date             { get; set; }   // primary key
    public int      CompletedLessons { get; set; }
    public long     WatchedMs        { get; set; }
    public bool     CountsForStreak  { get; set; }
    public DateTime UpdatedAt        { get; set; }
}
```

One row per day that has any activity. Days with no activity have no row.

### What updates it

| Event | Effect |
|---|---|
| Lesson completed | `CompletedLessons++`, `WatchedMs += ` session time |
| Playback position reported | `WatchedMs` accumulates |
| Day rolls over | A new row is created lazily on the next activity |

`WatchedMs` measures **actual playback time**, not wall-clock time with the app open. A video paused on a second monitor while the user makes coffee is not study time.

Deriving it: accumulate the delta between reported positions where the delta is positive and small. A jump from `0:30` to `25:00` is a seek, not 24 minutes of watching. The rule:

```text
delta = position - lastPosition
if 0 < delta <= 2 seconds   → count it
otherwise                   → it was a seek, count nothing
```

The source is the player's periodic position reports ([PlayerSpec §4](PlayerSpec.md#4-playback-events)), which arrive every few seconds during playback.

---

## 3. Study day and the day boundary

### Definition

A day counts for the streak when:

```text
Default:  at least one lesson completed
Optional: at least N minutes watched
```

Both are configurable — the user picks the mode and the threshold. Default is one lesson: it is the strongest signal of real progress and it is binary, which makes the rule easy to reason about.

### Day boundary

People study after midnight. A session from 23:00 to 01:30 is coherent learning, and splitting it across two days means neither day looks like a study day — breaking a streak the user genuinely maintained.

```text
Day boundary     configurable
Default          04:00
Options          00:00 · 03:00 · 04:00 · 05:00 · custom
```

With a 04:00 boundary, 01:30 belongs to the previous day.

### Timezone

```text
Timezone used for the boundary    the device's current IANA timezone
```

The date assigned to an activity is computed **once, when the activity is recorded**. A day's record never moves afterwards. Historical days keep the date they were assigned under the settings in force at the time.

This means:

| Scenario | Behaviour |
|---|---|
| User stays in one timezone | Nothing to think about |
| User travels two zones for a week | A day may be slightly short or long; the streak is unaffected |
| User changes the day boundary setting | Applies to the current day onward; past days are not recomputed |
| User's clock changes by hours | Cannot be fully defended against; no action |

**Do not recompute history.** Recomputing past days on every timezone change will reshuffle the calendar unpredictably, and a streak that rewrites itself is worse than one that is occasionally a few hours off.

### The travel case, concretely

Flying six timezones east makes a day 18 hours long; flying west makes it 30. An activity recorded at the old boundary time lands on the day that contains it at that moment. The user sees a slightly unusual calendar, not a broken streak.

---

## 4. Streaks

### Calculation

```text
current streak  = consecutive CountsForStreak days ending today or yesterday
longest streak  = the longest such run in the record
```

**Never stored.** Streaks are derived from `LearningActivity` rows on read ([DomainMap §5.2](../Maps/DomainMap.md#52-learningactivity)). A stored counter and a merged activity log will disagree after sync, and there is no good way to reconcile them.

### "Today or yesterday"

If a day is not yet complete, the streak must not appear broken. At 10:00, before the user has studied:

```text
Yesterday was a study day    → streak includes it, still "alive"
Yesterday was not            → streak is 0
```

The streak only breaks when a full day passes with no activity. Showing `0` at breakfast because today has not happened yet would be actively demotivating.

### Display

```text
🔥 7 days
```

Plus, when the current day is not yet a study day:

```text
🔥 7 days · today doesn't count yet
```

Showing the streak without this qualifier leads to the "I lost my streak" complaint on a day the user has not finished yet.

### Break

When the streak breaks, the longest streak is preserved. The user sees:

```text
Streak broken. Longest: 23 days. Start again?
```

### Freeze

Not planned. A "streak freeze" mechanic belongs to a different kind of product, and every rule added to the streak makes it harder to explain why the number is what it is.

---

## 5. Tasks

```csharp
public class LearningTask
{
    public Guid   Id        { get; set; }
    public string Title     { get; set; }

    public Guid?  CourseId  { get; set; }
    public Guid?  ModuleId  { get; set; }

    public TaskSourceType Source   { get; set; }   // Automatic | User
    public TaskGoalType   GoalType { get; set; }
    public double TargetValue      { get; set; }
    public double CompletedValue   { get; set; }

    public DateTime? DueDate       { get; set; }
    public RecurrenceRule? Recurrence { get; set; }

    public bool   IsCompleted      { get; set; }
    public DateTime? CompletedAt   { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
```

### Goal types

| Type | Measured in | Completion rule |
|---|---|---|
| `WatchLessons` | lessons | `CompletedLessons` for the day reaches the target |
| `WatchMinutes` | minutes | `WatchedMs` for the day reaches the target |
| `CompleteModule` | module | every lesson in the module is completed |
| `CompleteCourse` | course | every lesson in the course is completed |
| `ReachPercent` | percent | course progress reaches the target |

### Scope

A task may be scoped to a course, to a module, or to nothing:

```text
No CourseId              → the goal applies across the whole library
CourseId only            → only activity in that course counts
CourseId + ModuleId      → only activity in that module counts
```

Scoping is what makes "watch 30 minutes" mean something different from "watch 30 minutes of Docker".

---

## 6. Automatic tasks

### Generation

Generated lazily, **once per day, on first app open**. Not by a background timer — the app may not be running, and a task that appears while the user is asleep serves no purpose.

```text
On first open of a new day:
    generate the day's automatic tasks from the active pattern
```

### The pattern

The user configures a pattern rather than individual tasks:

```text
Enable automatic tasks       on
Daily goal                   N lessons   OR   N minutes
Include course continuation  on
```

| Generated task | When |
|---|---|
| "Watch N lessons" | Always, if the goal is lessons |
| "Watch N minutes" | Always, if the goal is minutes |
| "Continue {course}" | The most recently opened in-progress course |

**Cap automatic tasks at 2–3 per day.** Beyond that they stop being goals and become a wall of unmet obligations, which is the opposite of the intent.

### History

Completed automatic tasks are **kept**, not deleted at day end. The task history is what makes the statistics screen possible, and it costs almost nothing — a handful of rows per day.

### Continuation choice

`Continue {course}` uses the course with the most recent `LastOpenedAt` among those `InProgress`. If no course is in progress, no continuation task is generated.

---

## 7. User tasks

### Fields

```text
Title        required
Course       optional
Module       optional
Goal type    required
Target       required
Recurrence   optional (none / daily / weekly / selected days)
Due date     optional
```

### Recurrence

```csharp
public record RecurrenceRule(DayOfWeek[] Days, DateTime? Until);
```

| Rule | Behaviour |
|---|---|
| None | One task with a due date |
| Daily | A new instance each day |
| Weekly | A new instance each week on the same weekday as creation |
| Selected days | A new instance on each selected weekday |

**Recurring tasks produce instances, not a counter.** Each day gets its own row with its own completion state. A counter cannot answer "did I complete Tuesday's task", and the calendar view requires that answer.

### Completing and uncompleting

- Manual completion sets `IsCompleted` and `CompletedAt`. It is allowed even when `CompletedValue < TargetValue` — the user knows better than the metric.
- Uncompleting clears both. The accumulated `CompletedValue` is kept.
- Auto-completed tasks stay completed if the metric later falls below target. Progress only moves forward during a day; an unmarked lesson does not retroactively un-complete a finished task.

### Deadlines

```text
DueDate in the future   → shown under "Upcoming"
DueDate is today        → shown under "Today"
DueDate passed          → shown as overdue, not silently removed
```

An overdue task is not automatically failed. It stays visible until the user completes or deletes it.

---

## 8. Recalculation

`CompletedValue` is recomputed when relevant activity changes:

```text
Lesson completed          → recompute tasks scoped to that course
Day rolls over            → reset daily automatic tasks
User marks a lesson       → recompute affected tasks
```

`CompletedValue` is a **cached projection**, not a source of truth. If it drifts, recomputing from `LearningActivity` and `WatchState` must restore the correct value. A task whose stored progress cannot be reproduced from the underlying records is a bug, not a feature.

---

## 9. Statistics

Derived from `LearningActivity`, no separate aggregate storage:

```text
Streak             current and longest
Week / month       hours watched, lessons completed
Heatmap            a year of activity
Completed courses
Per-category       progress breakdown
```

Statistics are the one place where a full scan of the activity table is acceptable — it is one row per day, so a decade of use is a few thousand rows.

---

## 10. Settings

```text
Enable automatic tasks           on
Daily goal                       2 lessons
Streak rule                      at least 1 lesson
Streak minutes threshold         20        (when the rule is minutes)
Day boundary                     04:00
Keep task history                on
```

---

## 11. Deferred

```text
Streak freeze / repair
Achievements and badges
Leaderboards
Custom recurring schedules beyond weekly
Reminders per task                 (stage 9)
Task templates
```

---

## 12. Open questions

- **Multiple automatic patterns.** Whether a user should be able to run several patterns, or whether one goal plus course continuation is enough. Start with one; expand only if asked.
- **Streak display at the boundary.** Whether a day counts while the boundary is still in progress (e.g. it is 02:00 and the day has not rolled over yet). Current answer: the day has not ended, so the streak is shown as alive pending.
- **Long inactivity.** Whether the streak should reset silently after weeks away, or whether the user should be told it broke. Silent reset is gentler; an explicit message may be better for re-engagement. Decide during stage 4.
- **Learning over a year.** The heatmap and statistics have not been checked against a decade of data. Measure before the statistics screen ships.

---

## 13. What must be verified by spike

| # | Question | Why |
|---|---|---|
| 1 | Does `WatchedMs` accumulate correctly when playback is paused, backgrounded, and resumed? | The seek-versus-watch rule is easy to get wrong |
| 2 | Does a day boundary of 04:00 correctly assign a 01:30 session to the previous day? | The rule most likely to be implemented off-by-one |
| 3 | Does the streak survive a timezone change while travelling? | Cannot be tested thoroughly, but the basic case must hold |
| 4 | Does export/import round-trip `LearningActivity` and preserve the streak? | The activity log is what sync must not corrupt |

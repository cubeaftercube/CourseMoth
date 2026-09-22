# Domain Map / Data Model Map

A map of the **project's entities**: what we have, what fields and relations they carry, who creates them, who changes them, and how they live over time.

This is one of the most important documents to get right before writing code. A badly designed data model hurts later in: progress, duplicate courses, renamed files, synchronization, course deletion, and loading from the cloud.

> **Related documents:** [SystemMap](SystemMap.md) · [Vision](../Vision.md) · [SyncSpec](../Specs/SyncSpec.md) · [ParserSpec](../Specs/ParserSpec.md) · [OpenQuestions](../OpenQuestions.md)

---

## 1. Domain Boundaries

**The domain stores knowledge about learning, not course content.**

This split is the document's main rule:

| Stored in the domain | **Not** stored in the domain |
|---|---|
| Course, module, lesson as a fact | Video files |
| Where the file lives (path, URI, size) | The video bytes themselves |
| How much was watched and what was completed | Player buffer position |
| Tasks, activity, streak | Settings of a particular player engine |
| Categories, tags, favorites | The active session and its tokens |

From this follows the key property: **the database is small and easy to read back**. It fits into an export, into a diff, and into a synchronization file. Video never takes part in synchronization — it either sits on the user's machine or is downloaded from the source.

---

## 2. Entities

### Overview

```text
LibrarySource ──┐
                ├──▶ Course ──┬──▶ CourseModule ──┐
Category ───────┘             │                   │
                              └───────────────────┴──▶ Lesson ──▶ WatchState
                                                          │
                                                          ├──▶ DownloadJob
                                                          │
                                                          └──▶ Subtitle

Course ──▶ CourseFingerprint          (identity, §7)

LearningTask ──▶ (Course | Module | —)     what to learn
LearningActivity                            by day, for the streak

SyncEntityMetadata                          versions and tombstones for everything
```

### Core

| Entity | What it describes |
|---|---|
| `Course` | Course: metadata, status, source |
| `CourseModule` | Module/section inside a course |
| `Lesson` | Lesson: file reference, order, duration |
| `WatchState` | Lesson watch progress — **the center of the whole system** |
| `Subtitle` | Subtitle track attached to a lesson |

### Organization

| Entity | What it describes |
|---|---|
| `Category` | Course topic (Programming, Languages, Design) |
| `Tag` | Free-form label, many-to-many with a course |
| `CourseFingerprint` | Structure fingerprint for finding duplicates |

### Motivation

| Entity | What it describes |
|---|---|
| `LearningTask` | Task: automatic or user-created |
| `LearningActivity` | A single day of activity: how much was watched, what was completed |

### Infrastructure

| Entity | What it describes |
|---|---|
| `LibrarySource` | Where the course came from: local folder, server, cloud |
| `DownloadJob` | Download task for a single file |
| `SyncEntityMetadata` | Version, authoring device, tombstone |

---

## 3. Core Entities

### 3.1. `Course`

```csharp
public class Course
{
    public Guid   Id            { get; set; }   // local identifier
    public string StableKey     { get; set; }   // human-readable stable key, §7
    public string Title         { get; set; }
    public string? Author       { get; set; }
    public string? Description  { get; set; }

    public Guid?  CategoryId    { get; set; }
    public string? CoverPath    { get; set; }   // relative path to the cover

    public CourseStatus Status  { get; set; }
    public double CompletionThreshold { get; set; } = 0.9;   // lesson completion threshold

    public Guid?  SourceId      { get; set; }   // → LibrarySource
    public string SourcePath    { get; set; }   // course root within the source

    public bool   IsFavorite    { get; set; }
    public bool   IsHidden      { get; set; }

    public DateTime  CreatedAt  { get; set; }
    public DateTime  UpdatedAt  { get; set; }
    public DateTime? LastOpenedAt { get; set; }
}
```

```csharp
public enum CourseStatus { NotStarted, InProgress, Completed, Abandoned, Hidden }
```

**Who creates it:** import (`Parser` → `Core.ImportService`).
**Who changes it:** the user (title, category, status, cover), `Core` (status by progress, `LastOpenedAt`).
**Why it matters:** `SourcePath` is a **reference**, not ownership. Deleting a course from the app does not delete the user's folder without explicit consent ([Vision, §4.3](../Vision.md#43-the-users-data-belongs-to-the-user)).

---

### 3.2. `CourseModule`

```csharp
public class CourseModule
{
    public Guid   Id        { get; set; }
    public Guid   CourseId  { get; set; }
    public string Title     { get; set; }
    public int    Order     { get; set; }       // order within the course
    public string StableKey { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
```

**A course without modules.** If the structure is flat (`Course/Lesson.mp4`), no module is **created**. `Lesson.ModuleId` stays `null`. Synthetic modules like "All lessons" are not created — otherwise they leak into the UI, progress, and synchronization, and there is no getting rid of them.

---

### 3.3. `Lesson`

```csharp
public class Lesson
{
    public Guid   Id        { get; set; }
    public Guid   CourseId  { get; set; }
    public Guid?  ModuleId  { get; set; }       // null if the course is flat

    public string Title     { get; set; }
    public int    Order     { get; set; }

    public string StableKey { get; set; }
    public string RelativePath { get; set; }    // path within the course — the basis of identity
    public long?  FileSizeBytes { get; set; }
    public TimeSpan? Duration   { get; set; }   // null until known

    public LessonAvailability Availability { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
```

```csharp
public enum LessonAvailability
{
    Remote,        // exists on the user's machine or a server but not downloaded
    Downloaded,    // downloaded by the app into its sandbox
    Local,         // reachable through the user's own folder
    Missing        // the file is gone — a rescan did not find it
}
```

**Who creates it:** import.
**Who changes it:** `Core` (availability), `Downloads` (availability), rescanning (path, duration, `Missing`).
**Why it matters:** `Duration` is filled in lazily — on the first opening of the lesson in the player, or during scanning if the metadata is cheap to get. Until it is there, course progress is counted by the number of completed lessons, not by time.

---

### 3.4. `WatchState` — the center of the system

```csharp
public class WatchState
{
    public Guid   LessonId       { get; set; }   // primary key: one lesson — one state

    public long   PositionMs     { get; set; }
    public double ProgressPercent{ get; set; }   // 0..1

    public bool   IsCompleted    { get; set; }
    public CompletionSource CompletionSource { get; set; }
    public DateTime? CompletedAt { get; set; }

    public DateTime UpdatedAt    { get; set; }
    public Guid   LastDeviceId   { get; set; }
}
```

```csharp
public enum CompletionSource { Threshold, Manual, CourseMarked }
```

**This is the most conflict-prone entity in the project.** Everything else is metadata that changes rarely. `WatchState` changes every few seconds of watching, and is therefore the main source of conflicts during synchronization.

**Who changes it:** `Core.WatchStateService` on events from `Media`, and the user manually.
**The separation rule:** `Media` reports "position 12:34". `Core` decides what that means: update `PositionMs`, recompute `ProgressPercent`, whether the threshold was crossed, set `IsCompleted`. **Media does not write to `WatchState` directly.**

**Why `CompletionSource` is mandatory.** A manual "mark as not completed" must survive a rewatch. If the user unchecked the box by hand, autocompletion by threshold must not put it back the same day. Distinguishing the source lets us set a rule: `Manual + unchecked box` is protected from `Threshold` until the user's next explicit action.

**Module and course progress is not stored.** These are computed values — see §8.

---

### 3.5. `Subtitle`

```csharp
public class Subtitle
{
    public Guid   Id        { get; set; }
    public Guid   LessonId  { get; set; }
    public string Path      { get; set; }   // relative path next to the video
    public string? Language { get; set; }   // "ru", "en" — from a .ru.srt suffix, when recognized
    public bool   IsDefault { get; set; }
}
```

A separate entity rather than a `Lesson.SubtitlePath` field, because a lesson really can have several tracks (`lesson.ru.srt`, `lesson.en.srt`), and the user will want to switch between them. Parsing details — [ParserSpec](../Specs/ParserSpec.md), playback — [PlayerSpec](../Specs/PlayerSpec.md).

---

## 4. Organization

### 4.1. `Category`

```csharp
public class Category
{
    public Guid   Id       { get; set; }
    public string Name     { get; set; }
    public string? ColorHex { get; set; }
    public int    Order    { get; set; }
}
```

A flat list without hierarchy. Nested categories are a temptation that the MVP does not need and that gets in the way of synchronization later.

### 4.2. `Tag`

A free-form label, a many-to-many relation with a course (`CourseTag`). The difference from a category: a course has **one** category and **any number** of tags. A category takes part in navigation, tags — in filtering.

### 4.3. `CourseFingerprint`

```csharp
public class CourseFingerprint
{
    public Guid   CourseId   { get; set; }
    public string Kind       { get; set; }   // "structure" | "metadata-id"
    public string Value      { get; set; }   // SHA-256 or a GUID from course.json
    public DateTime ComputedAt { get; set; }
}
```

The fingerprint is **pulled out of `Course` into a separate table** deliberately. A course can have several fingerprints at once: `metadata-id` — if there was a GUID in `course.json`, `structure` — a hash of the normalized structure. Matching goes by any of them, and storing a list in a `Course` column is a dead end.

Details — §7.

---

## 5. Motivation

### 5.1. `LearningTask`

```csharp
public class LearningTask
{
    public Guid   Id        { get; set; }
    public string Title     { get; set; }

    public Guid?  CourseId  { get; set; }    // null for library-wide tasks
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

```csharp
public enum TaskSourceType { Automatic, User }
public enum TaskGoalType
{
    WatchLessons,     // watch N lessons
    WatchMinutes,     // watch N minutes
    CompleteModule,   // finish a module
    CompleteCourse,   // finish a course
    ReachPercent      // reach X% of a course
}
```

**The `TargetValue` / `CompletedValue` split** allows showing task progress ("1 of 2 lessons"), not just a checkbox.

**Automatic and user tasks differ by source, not by table.** Auto-generated ones are recreated for each day; user ones live until completion or cancellation.

Generation and completion rules — [TasksStreaksSpec](../Specs/TasksStreaksSpec.md).

---

### 5.2. `LearningActivity`

```csharp
public class LearningActivity
{
    public DateOnly Date            { get; set; }   // local date — primary key
    public int      CompletedLessons { get; set; }
    public long     WatchedMs        { get; set; }
    public bool     CountsForStreak  { get; set; }
    public DateTime UpdatedAt        { get; set; }
}
```

One row = one day. From it we derive the current streak, the longest streak, the activity calendar, and statistics — all of these are **computed**, not stored.

**Why we don't count the streak on read.** A streak is a fold over all days. Storing it separately means ending up with a value that goes out of sync when data from another device is merged. Only the fact for each day is stored; the streak is computed from it.

Details and the time zone rule — [TasksStreaksSpec](../Specs/TasksStreaksSpec.md).

---

## 6. Infrastructure

### 6.1. `LibrarySource`

```csharp
public class LibrarySource
{
    public Guid   Id       { get; set; }
    public string Name     { get; set; }           // "Курсы на D:", "Домашний сервер"
    public SourceType Type { get; set; }           // LocalFolder | Server | WebDav | Nextcloud
    public string ConfigJson { get; set; }         // path, address, token — not parsed by Core
    public DateTime? LastScannedAt { get; set; }
    public DateTime CreatedAt { get; set; }
}
```

`ConfigJson` is an opaque set of connection parameters as far as the domain is concerned. `Core` must not know that WebDAV has a URL and a local folder has a path. Only the matching `ILibrarySource` provider can parse it.

### 6.2. `DownloadJob`

```csharp
public class DownloadJob
{
    public Guid   Id       { get; set; }
    public Guid   LessonId { get; set; }
    public DownloadState State { get; set; }
    public double ProgressPercent { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
}
```

```csharp
public enum DownloadState { Pending, Downloading, Paused, Completed, Failed, Canceled }
```

A task = **one file**. Downloading a whole course is N tasks grouped in the UI. There are deliberately no batch entities like "download module" in the model: they add nothing but require their own states and synchronization.

### 6.3. `SyncEntityMetadata`

```csharp
public class SyncEntityMetadata
{
    public Guid   EntityId   { get; set; }
    public string EntityType { get; set; }   // "Course", "WatchState", ...
    public long   Version    { get; set; }   // locally monotonic
    public DateTime UpdatedAt { get; set; }
    public Guid   DeviceId   { get; set; }
    public bool   IsDeleted  { get; set; }   // tombstone
}
```

A separate table rather than columns in every entity. The reason: one synchronization implementation for all types instead of repeating the logic in every table. Details — [SyncSpec](../Specs/SyncSpec.md).

---

## 7. Course Identity — the Key to Synchronization

This is where mistakes happen most often. The same course arrives on different devices by different routes:

- the same course sits both locally and in the cloud;
- the user re-imported a folder that was already imported;
- the folder was renamed or moved;
- the course came from another device through synchronization.

The app **must** understand that this is the same course. Otherwise progress will not merge, and a duplicate will appear instead.

### Three Mechanisms, in Priority Order

**1. The identifier from `course.json` — the strongest**

If there is a `course.json` with an `id` field next to the course, that is the final word. The file lives inside the course folder, so renaming or moving the folder does not break it. A course described by `course.json` synchronizes flawlessly.

**2. The structure fingerprint — the main mechanism**

`SHA-256` of a normalized description of the structure:

```text
course.json-id | имя папки курса | отсортированные относительные пути уроков
| размер файлов | количество модулей
```

What goes in and why:
- **Relative paths**, not absolute ones — so that moving the folder does not change the fingerprint.
- **File sizes** — to tell apart courses with the same names but different contents.
- **No module titles** — they are the thing users edit and translate most often, while the structure stays the same.

**3. `StableKey` — a stable key for linking entities**

```text
course:csharp-basics
module:csharp-basics:01-intro
lesson:csharp-basics:01-intro:03-linq
```

A human-readable key derived from the normalized name. It is used as a fallback when merging and it makes a data export readable by eye. You **must not** rely on it as a primary identifier: the user can rename the folder, and the key changes.

### What to Do When the Fingerprint Matches

```text
Точное совпадение отпечатка        → предложить объединить прогресс
Совпадение по StableKey, но не по
отпечатку (файлы другие)           → спросить пользователя
Совпадение только по названию      → НЕ объединять автоматически
```

You cannot silently merge by folder name: "Module 1" exists in a thousand different courses.

### What to Do on Rescan

Rescanning finds three kinds of changes, and each has its own fate:

| Change | How it is detected | What happens to progress |
|---|---|---|
| New file | A path that did not exist | Create a lesson, progress is empty |
| Deleted file | A path from the database not found | `Availability = Missing`, **progress is preserved** |
| Renamed file | Fingerprint matched | Re-link, progress is preserved |

Progress is **never** deleted automatically when a file disappears. The file may come back — from a flash drive, from another disk, after a repair. It is deleted only together with the course, by an explicit user decision.

---

## 8. Computed Values

The following is **not stored** in the database. This matters: every stored derived value is an occasion for going out of sync.

| Value | How it is computed |
|---|---|
| Lesson progress | `WatchState.ProgressPercent` (the only exception — position cache) |
| Module progress | completed lessons of the module ÷ total lessons of the module |
| Course progress | completed lessons of the course ÷ total lessons of the course |
| Course status | 0% → `NotStarted`, >0% → `InProgress`, ≥100% → `Completed` |
| Streak | a fold over `LearningActivity` |
| Task completion | comparing `CompletedValue` with `TargetValue` |

**Why progress by lesson count and not by time.** The option "total watched ÷ total duration" is more accurate, but it requires a known duration for all lessons, and that is filled in lazily (`Lesson.Duration` may be `null`). While the duration is unknown for at least some lessons, time-based progress lies. The lesson-count option is honest always. Changing the metric is a setting, not the default behavior.

---

## 9. Deletion

The cross-cutting rule: **entities are not physically deleted while that could break synchronization.**

| What we delete | How |
|---|---|
| Course | The user chooses: metadata / downloaded files / local files |
| Lesson | Cascade with the course; progress is not preserved |
| Task | A mark, not a deletion — the user can cancel it |
| Download | Cancellation or a `Canceled` mark |

`SyncEntityMetadata.IsDeleted` — tombstone. Physically purging old tombstones is a separate synchronization task, not the user's.

**The user's local files are never deleted** without a separate explicit checkbox. This is a direct requirement of [Vision, §4.3](../Vision.md#43-the-users-data-belongs-to-the-user).

---

## 10. What Needs to Be Done Before Writing Code

- [x] Decide which entities are in the MVP — done, and the split held: `Tag`, `Subtitle`, `DownloadJob` and `CourseFingerprint` are still not implemented.
- [ ] Lock down the `course.json` schema as a contract — course identity depends on it. Still open; the parser will force it.
- [x] Determine how `StableKey` differs from `RelativePath` — they serve different jobs and both were kept. `RelativePath` is where the file is *now* and rebinds on rescan; `StableKey` is derived from the *name*, survives a move within the source, and is the human-readable fallback for sync.
- [x] Decide the fate of `LearningActivity` when the time zone changes — see [OpenQuestions](../OpenQuestions.md#time-zones-and-the-day-boundary). A day's date is computed once when the activity is recorded and never recomputed.
- [x] Choose: `sqlite-net-pcl` or EF Core — **`sqlite-net-pcl`**, as the docs anticipated.

### Decisions taken while building the storage layer

These were not in the plan and are worth knowing before touching the schema:

| Decision | Why |
|---|---|
| `DateOnly` is stored as a `long` in `yyyyMMdd` form | sqlite-net cannot map `DateOnly`, and a tick count would break range queries and sorting. Verified across a leap day and a year boundary. |
| `TimeSpan?` is stored as `long?` milliseconds | Null must stay a real null, never a sentinel: "duration unknown" and "duration zero" are different facts, and progress depends on telling them apart. |
| `RecurrenceRule` is stored as a bitmask plus a date key | It is the one field in the schema that must be comparable and sortable. Serialising it as JSON would have made it the only column that could not be queried. |
| Enums are stored as integers | sqlite-net's default. Pinned deliberately: changing it later would silently reinterpret every existing row. |
| `SyncEntityMetadata` has a composed primary key | It is the one table keyed by `(EntityType, EntityId)` rather than a single id. Declaring two `[PrimaryKey]` members compiles and then throws on first table creation. |

A defect worth recording, because it is the kind that does not announce itself: the task repository initially compared a due date encoded as ticks against a window encoded as `yyyyMMdd`. The scales are 11 orders of magnitude apart, so every comparison was nonsense and **a task with a due date silently vanished from its own day** — no error, it simply was not there. Any change to how dates are encoded must keep the encoder and the query argument on the same scale.

### MVP Entities

```text
Course, CourseModule, Lesson, WatchState, Category,
LearningTask, LearningActivity, LibrarySource, SyncEntityMetadata
```

### Deferred

```text
Tag, CourseFingerprint (как отдельная таблица), Subtitle, DownloadJob
```

`Tag` and `Subtitle` are introduced when the corresponding screens appear. `DownloadJob` — at stage 7. Until then `CourseFingerprint` can be stored as a single column in `Course`; extracting it into a table is part of the synchronization work.

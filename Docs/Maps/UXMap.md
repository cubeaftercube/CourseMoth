# UX Map / Screen Map

A map of **screens and user scenarios**. Not design, but logical structure: which screens exist, what is on them, and what of that is needed for the MVP.

> **Related documents:** [Vision](../Vision.md) · [UserFlowMap](UserFlowMap.md) · [DomainMap](DomainMap.md) · [Roadmap](../Roadmap.md)

---

## 1. Interface principles

1. **The library is the center of the app, not the player.** The user comes to watch not "video", but "what to learn next".
2. **The player is not overloaded.** Controls appear on tap and disappear. The screen is occupied by video.
3. **Progress is visible everywhere.** Every course card shows where the user stopped.
4. **The user can always undo.** Import is reversible until confirmation, a mark is removed, a download is cancelled.
5. **Nothing is deleted without asking.** Especially files the user did not upload through the app.

---

## 2. Navigation

The main navigation is `Shell` with tabs:

```text
┌─────────┬──────────┬────────┬───────────┬──────────┐
│  Home   │ Library  │ Tasks  │ Downloads │Settings  │
└─────────┴──────────┴────────┴───────────┴──────────┘
```

Screens **outside the tabs** (open on top, with a back path):

```text
Player             ← opens from a course, a lesson, "Continue", a notification
Import review      ← opens after a folder is selected
Course page        ← opens from the library, home, search
Module page        ← opens from the course page
```

The player is **modal**, not a tab. It captures the whole screen, it has its own exit logic, and it must not survive an app restart as the "last tab".

---

## 3. Screens

### 3.1. Home

Answers the question "what should I do right now". Not a list of courses — the library is there for that.

```text
┌──────────────────────────────────────┐
│  🔥 7 days                           │
│  Today: 1 of 2 tasks                 │
├──────────────────────────────────────┤
│  CONTINUE                            │
│  ┌────────────────────────────────┐  │
│  │ [cover] Programming in C#      │  │
│  │ Module 3 · Lesson 5            │  │
│  │ ████████████░░░░░░░  62%       │  │
│  │            [ Continue ]        │  │
│  └────────────────────────────────┘  │
├──────────────────────────────────────┤
│  TODAY'S TASKS                       │
│  ○ Watch 1 lesson            1/1     │
│  ○ 30 minutes of learning    18/30   │
├──────────────────────────────────────┤
│  RECENT COURSES                      │
│  [Docker]  [English]  [Git]          │
└──────────────────────────────────────┘
```

**Content (by priority):** streak → tasks of the day → "Continue" → recent courses.

**Empty state** — first launch, no courses. Instead of an empty screen — an explanation and a "Add folder with courses" button. This is the most important onboarding screen, we do not make a separate onboarding.

---

### 3.2. Library

List of all courses.

**Filters:** All · In progress · Completed · Not started · Favorites · By category · By source

**Sorting:** Recently opened · Title · Progress · Date added · Duration

**Display:** Grid (covers) / List (with progress and metadata)

**Course card:**

```text
[cover or placeholder]
Programming in C#
Ivan Ivanov · Programming
████████████░░░░░░░░  62%
12 of 19 lessons
```

**Actions:** open · favorite · hide · rescan · delete · edit metadata

---

### 3.3. Course page

```text
┌──────────────────────────────────────┐
│  ← [cover]                           │
│  Programming in C#                   │
│  Ivan Ivanov · Programming           │
│  ████████████░░░░░░░  62%            │
│  [ Continue ]          [ ⋮ ]         │
├──────────────────────────────────────┤
│  ▼ Module 1 · Introduction    ✓ 3/3  │
│      ✓ 01 About the course   12:40   │
│      ✓ 02 Installation       08:15   │
│      ✓ 03 First program      15:20   │
│  ▼ Module 2 · Basics          ▶ 1/4  │
│      ✓ 01 Variables          11:05   │
│      ● 02 Data types         18:30   │  ← in progress
│      ○ 03 Conditionals       14:00   │
│      ○ 04 Loops              16:45   │
│  ▶ Module 3 · Functions       ○ 0/5  │
├──────────────────────────────────────┤
│  [ Mark course complete ]            │
└──────────────────────────────────────┘
```

**Required:** cover · title · author · category · status · progress bar · "Continue" · list of modules and lessons with marks · duration · "Mark course complete" button.

**Later:** notes · file sizes · offline availability · threshold settings for the course.

**Actions in the `⋮` menu:** edit metadata · rescan · download all · delete course · favorite · hide.

Duration estimation — in the MVP it can be left empty (see [DomainMap §8](DomainMap.md#8-computed-values)); lesson rows must look fine without it too.

---

### 3.4. Module page

May **not exist** as a separate screen. If there are few modules and they fit in an accordion on the course page — that is better: less navigation, course and module progress are visible at the same time.

A separate screen is justified when a module has more than ~15 lessons and the list becomes heavy.

**Default decision:** in the MVP a module is a section on the course page. A separate page — only if it turns out to be needed.

---

### 3.5. Player

The heart of the app. The screen is occupied by video.

**In viewing mode the controls are hidden.** They appear on tap and disappear after a few seconds.

```text
┌──────────────────────────────────────┐
│                                      │
│            [    VIDEO   ]            │
│                                      │
│                                      │
├──────────────────────────────────────┤
│ 00:12:34              ▓▓▓▓░░░ 01:42:10│
│ ◀◀   ⏸   ▶▶     1.5x   💬   ⛶   📌  │
└──────────────────────────────────────┘
```

**Controls:** pause/play · seek ±10s · progress bar (drag) · speed · subtitles · full screen · PiP

**Top panel (also on tap):** back · course and lesson title · next lesson · lesson list

**Gestures:**
- double tap left/right — seek ±10s
- horizontal swipe — seek
- vertical swipe — volume / brightness
- tap — show/hide controls
- tap on the title — lesson list

**Required in the MVP:** playback · pause · seek · position saving · next/previous lesson · speed 0.5–3x · full screen · manual completion mark.

**Later:** subtitles · PiP · autoplay next · skip intro.

**Speeds:** 0.5 · 0.75 · 1 · 1.25 · 1.5 · 1.75 · 2 · 2.5 · 3. Presets, no free input in the MVP.

**Exiting the player** — the position is saved immediately, not "sometime later".

---

### 3.6. Import review

The screen that sets CourseMoth apart from an ordinary scanner. After selecting a folder the user sees **exactly what was recognized** and can fix it before it is written to the database.

```text
┌──────────────────────────────────────┐
│  We recognized:                      │
│  12 courses · 148 modules · 1203 lessons│
├──────────────────────────────────────┤
│  ▼ Programming in C#                 │
│      3 modules · 19 lessons          │
│      [edit] [skip]                   │
│      Module 1 · Introduction  3 lessons│
│      Module 2 · Basics        4 lessons│
│      Module 3 · Functions     5 lessons│
│  ▶ Docker (2 modules · 11 lessons)   │
├──────────────────────────────────────┤
│  ⚠ Skipped: 14 files (not video)     │
│  ⚠ 2 files without a number in the name│
├──────────────────────────────────────┤
│      [ Cancel ]      [ Import ]      │
└──────────────────────────────────────┘
```

**What can be fixed:**
- course title, author, category, cover
- parsing mode: "folder = course" / "folder = library"
- module titles and order
- lesson titles and order
- order of modules and lessons
- exclude a file from the import
- mark a file as material rather than a lesson

**Skipped files are shown, not silently ignored.** This is a direct requirement: the user must see what the app did not understand.

**Import is irreversible only after confirmation.** Until "Import" is pressed nothing is written to the database.

---

### 3.7. Tasks

**Sections (segments at the top):** Today · Upcoming · Completed · All

```text
┌──────────────────────────────────────┐
│  [ Today │ Upcoming │ Done ]         │
├──────────────────────────────────────┤
│  ○ Watch 1 lesson             1/1  ✓ │
│    Automatic · 7 day streak          │
├──────────────────────────────────────┤
│  ○ 30 minutes of learning     18/30  │
│    ████████████░░░░░░░░              │
├──────────────────────────────────────┤
│  ○ Finish module "Basics"            │
│    Programming in C#                 │
│    by September 25   ▶ 1/4           │
└──────────────────────────────────────┘
```

**Distinguish automatic and user tasks** — visually, not only by text. The user must understand what they created themselves and what the system generated.

**Actions:** create · edit · complete manually · delete · repeat

**Creating a task:**
```text
Title:     [                    ]
Course:    [ Programming ▾ ]        (optional)
Module:    [ Module 2 ▾ ]           (optional)
Goal:      [ Watch N lessons ▾ ]
Value:     [ 2 ]
Repeat:    [ Daily ▾ ]
Deadline:  [ 25.09.2025 ]           (optional)
```

---

### 3.8. Downloads

Appears at stage 7. Before that there may be no tab — it is not needed while there is nothing to download.

**Sections:** Active · Queue · Completed · Errors

```text
┌──────────────────────────────────────┐
│  ▼ Active (2)                        │
│  Module 3 · 02 Data types            │
│  ██████████░░░░░░░░  48%  12.4 MB/s  │
│  [ ⏸ ] [ ✕ ]                         │
├──────────────────────────────────────┤
│  ▼ Queued (7)                        │
│  Module 3 · 03 Conditionals  142 MB  │
├──────────────────────────────────────┤
│  Total: 1.2 GB of 5 GB limit         │
│  ████████░░░░░░░░░░░░░░░░░░░░░░░░░░░ │
└──────────────────────────────────────┘
```

**Actions:** pause · resume · cancel · retry · delete file

---

### 3.9. Settings

```text
Player
  Default lesson completion threshold    90%
  Autoplay next lesson                   on
  Default speed                          1.0x
  Enable subtitles automatically         off
  Download over Wi-Fi only               on
  Ask on progress conflict               on

Tasks
  Enable automatic tasks                 on
  Daily goal                             2 tasks / 30 minutes
  Streak threshold                       1 lesson
  Start of the study day                 04:00

Downloads                                    (stage 7)
  Storage limit                          5 GB
  Automatically delete watched           off
  Keep the last N lessons                5
  Delete lessons older than N days       —

Appearance
  Light / Dark / System
  Accent color

Data
  Export data
  Import data
  Backup
  Clear cache

Sources
  List of LibrarySource, adding and removing

Server                                       (stage 6)
  Enable server                          off
  Address, port, QR, connected devices

About
  Version, license, repository link
```

---

### 3.10. Statistics

Later than the MVP. Built on `LearningActivity`.

- Mini activity calendar (heatmap) for a year
- Hours per week / month
- Current and longest streak
- Completed courses
- Progress by category

---

## 4. States and feedback

| State | How we show it |
|---|---|
| First launch, no courses | Explanation + "Add folder" |
| Scanning in progress | Progress + "can be minimized" |
| File is gone (`Missing`) | Mark on the lesson, suggestion to rescan |
| File not downloaded for offline | "Download" button, the lesson streams |
| No space | Warning + suggestion to clean up |
| Sync conflict | Dialog with options (see below) |
| Playback error | Clear text + "open in the system player" |
| Empty task list | "No tasks" + suggestion to create one |

**Conflict dialog** — by default we always ask:
```text
Lesson progress diverged

This device:        watched up to 10:00
Another device:     watched up to 25:00

( ) Keep as it is here
( ) Use from the other device
( ) Take the greater progress
```

---

## 5. Screens by stage

| Screen | Stage | MVP |
|---|---|---|
| Home | 4 | simplified (without tasks) in stage 1 |
| Library | 1 | ✅ |
| Course page | 1 | ✅ |
| Import review | 1 | ✅ |
| Player | 2 | ✅ basic |
| Tasks | 4 | ✅ |
| Player: subtitles, PiP | 8 | — |
| Settings | 1 | ✅ basic |
| Downloads | 7 | — |
| Server / devices | 6 | — |
| Statistics | after MVP | — |
| Module page | as needed | — |

---

## 6. What will not be in the interface

So as not to blur the product:

- **Recommendation feeds.** Courses are not recommended, the user already has them.
- **Social features.** No "share your progress", other than technical synchronization.
- **Ads and in-app stores.**
- **Forced registration.** An account is not needed at all.
- **Gamification beyond streaks.** No points, levels, or achievements in the MVP.

---

## 7. Open questions

- Is **search** through the library needed in the MVP? If there are dozens of courses — yes, if a handful — no.
  → Decide after stage 1.
- **Android tablet** — the same layout or a two-pane one (course list + lessons)?
  → [OpenQuestions](../OpenQuestions.md)
- **Landscape in the player on a phone** — force it or hand it to the system?
  → Check in stage 2.

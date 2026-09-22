# User Flow Map

A description of **user paths**, not screens.

Screens answer the question "what is in the app". Scenarios answer the question "how does the user get through a task from start to finish, and what happens in the system along the way". The second matters more: scenarios are what show which buttons and states are missing.

> **Related documents:** [UXMap](UXMap.md) · [Vision](../Vision.md) · [DomainMap](DomainMap.md) · [Roadmap](../Roadmap.md)

---

## 1. How to read this

Every scenario follows the same scheme:

```text
Step → what the user does
     → what the system does
     → what happens on failure
```

Scenarios are tagged with a stage from the [Roadmap](../Roadmap.md). MVP scenarios are the ones that must work in the first version.

---

## 2. Scenario: "First launch"

**Stage 0**

```text
1. The user launches the app for the first time
   → Empty database, no courses
   → Not an empty list is shown, but an explanation and a single button

2. Reads: "Add a folder with courses"
   → No five-screen onboarding, no sign-up

3. Taps "Add folder"
   → Moves into the import scenario
```

**Criterion:** between installation and the first course in the library — no more than three actions.

---

## 3. Scenario: "Importing a local folder"

**Stage 1 · the project's key scenario**

```text
1. The user taps "Add folder"
   → Windows: system folder picker
   → Android: Storage Access Framework, document tree selection

2. Selects a folder
   → The permission is persisted (on Android — persistable URI permission)
   → A LibrarySource record is created

3. The system scans the folder
   → Parser walks the tree looking for video files
   → Progress is shown, the operation can be collapsed

4. Parser determines the structure
   → Decides: this folder is a course, or a library of courses
   → Builds ParsedCourse (a temporary model)

5. A review screen is shown
   → "We recognized: 12 courses, 148 modules, 1203 lessons"
   → Skipped and uncertain files are visible

6. The user reviews and edits
   → Renames, reorders, excludes files
   → Can switch the "folder = course" / "folder = library" mode

7. Taps "Import"
   → Core.ImportService.Confirm()
   → Only now does data reach SQLite

8. Sees the courses in the library
   → Status NotStarted, progress 0%
```

**If something went wrong:**

| Situation | Behavior |
|---|---|
| Folder is empty | "No video files were found in this folder" |
| Too many files (>5000) | Warning, option to limit the depth |
| The user closed the app at step 6 | Nothing imported, the database is clean |
| The folder was already imported | Fingerprint matched → offer to rescan the existing course |
| No permission for the folder | Explanation and a repeated permission request |

**Criterion:** after step 7, the library contains exactly what the user saw at step 6.

---

## 4. Scenario: "Watching a lesson"

**Stage 2 · the project's key scenario**

```text
1. The user opens a course from the library
   → Course page, modules expanded

2. Taps a lesson
   → The player opens, playback from the start (or from the saved position)

3. Watches
   → Every few seconds the position is saved
   → The lesson progress bar grows

4. Pauses and leaves
   → The position is saved immediately, not "later"

5. Comes back three days later
   → In the library the course shows "continue from 12:34"
   → On the home screen — a "Continue" card

6. Continues
   → The player opens at 12:34, not from the start
```

**Position saving rule:** saved on pause, on leaving the player, on backgrounding the app, and periodically during playback. Not only "on close" — the app can be killed.

---

## 5. Scenario: "Continue from where you stopped"

**Stage 2**

This is a separate scenario, even though it looks like part of the previous one. The difference is that here the user **doesn't remember** where they stopped — the system has to remember for them.

```text
1. The user opens the app
   → Not the library, but the home screen

2. Sees a "Continue" card
   → Course, module, lesson number, percentage, button

3. Taps it
   → Straight to the player, bypassing the course page

4. Continues from the same second
```

**Rule for picking the lesson for "Continue":** the first unfinished lesson in course order, not "the last opened one". If the user jumped ahead and came back — they must be led to where the gap is.

---

## 6. Scenario: "Completing a lesson"

**Stage 3**

```text
1. Playback reaches the threshold (90% by default)
   → Core marks the lesson completed
   → CompletionSource = Threshold

2. Progress is recalculated
   → Module: 3 of 4 lessons
   → Course: 12 of 19 lessons, 62%

3. If the module is fully closed
   → A ✓ mark on the module

4. If the course is fully closed
   → Status Completed
   → A LearningActivity record for today
   → Streak and tasks recalculated

5. The player may offer the next lesson
   → Depends on the "autoplay next" setting
```

**Manual intervention:**

| Action | Result |
|---|---|
| "Mark as completed" | `IsCompleted = true`, `CompletionSource = Manual` |
| "Mark as not completed" | `IsCompleted = false`, the position **is kept** |
| "Mark the whole course as completed" | All lessons = `CourseMarked` |

**Rollback protection:** after a manual unmark, threshold auto-completion must not bring the checkmark back the same day. Otherwise "not completed" is impossible to hold.

---

## 7. Scenario: "Importing progress onto another device (via file)"

**Stage 5 · the scenario that surfaced a model requirement**

This scenario is worth reading carefully: it shows that export **cannot** be a simple "dump of paths".

```text
1. On the PC: the user taps "Export data"
   → A JSON with all entities is built
   → The file is saved

2. Transfers the file to the phone
   → By whatever means: messenger, cable, cloud

3. On the phone: "Import data"
   → The system reads the file

4. PROBLEM: the courses on the phone live at different paths
   → The Excel file says: D:\Курсы\CSharp
   → On the phone that is content://.../Курсы/CSharp

5. The system matches courses
   → By structure fingerprint, not by path
   → Matched: progress is applied
   → Not matched: the course stays in the database with the status "files not found"

6. The user can specify the path manually
   → "Course "Программирование на C#" not found. Specify the folder?"
```

**Data model requirement:** progress import must work **without files**. The course exists in the database, lessons have progress, files are marked `Missing`. When the user specifies the folder — the lessons rebind, and the progress is already there.

The same requirement extends to sync: **progress travels separately from video**.

---

## 8. Scenario: "Downloading a course from PC to phone"

**Stage 6–7**

```text
1. On the PC: "Allow access from other devices"
   → A server comes up, a QR is shown

2. On the phone: "Connect to server"
   → Scans the QR
   → Gets the address, port, and a one-time token
   → Pairing confirmed, token saved

3. Sees the list of courses from the PC
   → The same courses as on the PC, with structure

4. Chooses what to download
   → Whole course / module / individual lessons / "the next uncompleted one"

5. Downloads run in the background
   → Progress is visible on the downloads screen
   → The app can be backgrounded

6. Watches offline
   → The files live in the app's sandbox
```

**If something went wrong:**

| Situation | Behavior |
|---|---|
| The PC was shut down mid-download | The file is marked incomplete, resumed on the next connection |
| The phone left Wi-Fi | The download stalls, retried on return |
| Not enough space | A warning before the start, not mid-download |
| The device was unpaired on the PC | The token stops working, offer to pair again |

---

## 9. Scenario: "Watched on PC, continued on phone"

**Stage 6 · the motivational scenario**

```text
1. The user watches a course on the PC
2. Stops in the middle of a lesson
3. Taps "Continue on another device"
   → A QR is shown with: the course, the lesson, the position, the server address, the token
4. The phone scans it
   → Progress sync
   → The same lesson opens
5. The user continues from the same second
```

**Why this is valuable:** this is the only scenario that justifies sync existing at all. If it works badly — the whole complexity of sync isn't worth it.

**A simpler option to start with:** the same scenario, but via a "Synchronize" button on both devices, without a QR. Verify whether that's enough before building the pretty QR flow.

---

## 10. Scenario: "Rescanning a folder"

**Stage 1 · protection against progress loss**

```text
1. The user added new lessons to the course folder
2. On the course page: "Rescan"
3. The system compares the files on disk against the database
   → New        → add
   → Missing    → Availability = Missing (progress is kept!)
   → Renamed    → rebind by fingerprint
4. A list of changes is shown
   → "Found 3 new lessons, 1 file not found"
5. The user confirms
```

**The main rule:** progress is **never** deleted automatically. A file can come back from a flash drive or another disk. A missing file is no reason to erase the fact that the user studied.

---

## 11. Scenario: "Deleting a course"

**Stage 1**

```text
1. The user: "Delete course"
2. The system does NOT delete right away
3. Shows a dialog with explicit checkboxes:

   Delete "Программирование на C#"?
   [ ] Metadata and progress      (default — yes)
   [ ] Downloaded video files     (live in the app's sandbox)
   [ ] Source files in the folder (default — NO)

4. The user selects and confirms
```

**The user's source files are never deleted by default.** The app didn't create them and doesn't own them. This is a direct requirement from [Vision](../Vision.md#43-the-users-data-belongs-to-the-user).

---

## 12. Scenario: "Resolving a sync conflict"

**Stage 5–6**

```text
1. One lesson's progress changed on two devices
2. On merge the system detects the divergence
3. Shows a dialog:

   Lesson progress diverges
   This device:    up to 10:00
   Other device:   up to 25:00
   ( ) Keep as here
   ( ) Use from the other device
   ( ) Take the greater progress

4. The user chooses
5. The decision is applied, the conflict closes
```

**By default — ask.** An automatic "always take the greater progress" mode is a setting you enable deliberately, not default behavior. Automation silently erases someone else's progress, and that's worse than an extra question.

---

## 13. Scenario: "The daily routine"

**Stage 4 · the entire motivational part exists for this scenario**

```text
MORNING
1. The user opens the app
2. Sees: a 7-day streak, today's tasks, a "Continue" button

DAY
3. Watches a lesson
4. The "Watch 1 lesson" task completes automatically
5. The "30 minutes" task progress grows: 18/30

EVENING
6. The app reminds: "2 minutes to your goal"     (stage 9)
7. The user finishes watching, the task closes

NIGHT
8. The day's LearningActivity is updated
9. The streak becomes 8
```

**A missed day:**
```text
1. The user didn't study
2. The streak breaks
3. Notification: "Streak broken. Start over?"    (stage 9)
4. The streak restarts at 1, the maximum is preserved
```

**What counts as a study day:** by default — at least one completed lesson. Configurable to "N minutes of watching". See [TasksStreaksSpec](../Specs/TasksStreaksSpec.md).

---

## 14. Scenario: "Moving to a new device"

**Stage 5–6 · the acceptance scenario for sync**

```text
1. The user installs the app on a new device
2. Imports progress (via file or via server)
3. Imports courses (folders/download)
4. Matching: progress lines up with courses
5. The user continues where they stopped
```

This scenario is the **main check** of the entire data model. If it doesn't work, course identification is designed wrong, and no local improvements will help.

It's worth running manually as an acceptance test before shipping sync.

---

## 15. Scenarios by stage

| Scenario | Stage | Criticality |
|---|---|---|
| First launch | 0 | ✅ MVP |
| Importing a local folder | 1 | ✅ MVP |
| Watching a lesson | 2 | ✅ MVP |
| Continue from where you stopped | 2 | ✅ MVP |
| Completing a lesson | 3 | ✅ MVP |
| Daily routine | 4 | ✅ MVP |
| Rescanning | 1 | ✅ MVP |
| Deleting a course | 1 | ✅ MVP |
| Progress import via file | 5 | Post-MVP |
| Moving to a new device | 5–6 | Post-MVP |
| Sync conflict | 5–6 | Post-MVP |
| Downloading from PC | 6–7 | Post-MVP |
| Continue on another device | 6 | Post-MVP |

---

## 16. What these scenarios proved

Walking through the scenarios surfaced four requirements that aren't obvious from the feature descriptions:

1. **Progress must live separately from files.** Import and sync must work when there are no files. The consequence is a structure fingerprint, not a path, as the basis for matching. *(§7)*

2. **Position is saved forcibly, not on close.** Apps get killed, phones run out of battery. *(§4)*

3. **A manual mark must be protected from auto-overwrite.** Otherwise "mark as not completed" doesn't work — the threshold brings the checkmark back. *(§6)*

4. **A missing file does not erase progress.** Never, and not automatically. *(§10)*

# Roadmap

A roadmap is **not a list of desired features** — it is a route through the project's stages of maturity.

> from a simple local viewer → to a personal learning hub

Every stage has a **definition of done**. A stage is not considered closed until the definition is met — even if all the code is written.

> **Related documents:** [Vision](Vision.md) · [SystemMap](Maps/SystemMap.md) · [OpenQuestions](OpenQuestions.md)

---

## Planning principle

The stages are ordered by two rules:

1. **First, the things that cannot be redone later.** The data model, course identity, module boundaries — all of it first. Screens and the server can be redone; the progress schema cannot.
2. **First, the things that are cheapest to verify.** Every risky technical assumption is verified by a spike **before** code starts depending on it.

---

## Overview

| Stage | Name | Result | Status |
|---|---|---|---|
| 0 | Project skeleton | The app launches on Windows and Android | 🔶 in progress |
| 1 | Local library | Folder → a recognized course in the library | ⬜ |
| 2 | Basic player | I watch and resume from where I stopped | ⬜ |
| 3 | Progress and statuses | The app knows what has been completed | 🔶 domain done, UI not |
| 4 | Tasks and streaks | A study routine emerges | 🔶 domain done, UI not |
| 5 | Data transfer | Progress travels between devices as a file | ⬜ |
| 6 | PC as the core | The PC serves courses and progress | ⬜ |
| 7 | Downloads and space | Comfortable on a phone without manual cleanup | ⬜ |
| 8 | Player improvements | Subtitles, PiP, autoplay next | ⬜ |
| 9 | Notifications | The app reminds you about itself | ⬜ |
| 10 | External clouds | WebDAV, Nextcloud | ⬜ |

---

## Stage 0. Project skeleton

**Goal:** the app launches, the documentation exists, the module boundaries are defined.

### Tasks

- [x] Repository, README, LICENSE (AGPLv3)
- [ ] CONTRIBUTING
- [ ] Bring `slnx` to the `src/` layout from [SystemMap](Maps/SystemMap.md#target-structure) — the projects are separated; the directory move is not done
- [x] Split platform-independent modules into ordinary `net10.0` libraries — `Core` and `Data` carry no MAUI reference
- [x] Basic navigation: `Shell` with five tabs
- [x] Themes: light / dark / system
- [x] DI: service registration — every service and every ViewModel resolves
- [x] Empty screens: Home, Library, Tasks, Downloads, Settings
- [x] Documentation in `Docs/`

### Definition of done

```text
The app launches on Windows and Android.
Switching between the five tabs works.
The dark theme toggles.
The domain builds and is tested without MAUI.
```

The last item is the main one. **It is met.** `CourseMoth.Core` and `CourseMoth.Data` are plain `net10.0` libraries, and `CourseMoth.Core.Tests` runs 132 tests with no platform target and no device.

**Verified rather than assumed:** the Windows app was launched and each of the five tabs opened in turn; the storage layer was exercised against a real SQLite file, including Cyrillic titles and paths, a leap-day `DateOnly`, and a `TimeSpan?` duration. Android builds clean but has not been run on a device.

---

## Stage 1. Local library

**Goal:** the user picks a folder and gets a recognized course.

This is **the most important stage of the project**. If it is done badly, nothing else matters.

### Tasks

- [ ] Folder picker: Windows — system dialog, Android — SAF with persistable permission
- [ ] Scan the tree and find video files
- [ ] Structure parser: `ParsedCourse` without writing to the database
- [ ] Determine the "folder = course" / "folder = library" mode
- [ ] Extract numbers from names, clean up titles
- [ ] Import review screen with the ability to make corrections
- [ ] Save to SQLite: `Course`, `CourseModule`, `Lesson`
- [ ] Library: grid/list, filters, sorting
- [ ] Course page: modules, lessons, progress bar
- [ ] Rescan while preserving progress
- [ ] Delete a course with a choice dialog
- [ ] `samples/` with test folder structures

### Definition of done

```text
The user imports their real folder of courses
and sees exactly the structure in the library that they expected.
```

Not a "test folder", but **their own**. Testing against `samples/` proves nothing — real courses are always messier.

### Stage risks

- **SAF on Android** — the nastiest spot in the project. A spike is mandatory before work begins.
- The dirty-name parser — easy to slide into a pile of hacks without [ParserSpec](Specs/ParserSpec.md).

---

## Stage 2. Basic player

**Goal:** you can watch a course and resume from where you stopped.

### Tasks

- [ ] Open a lesson, play, pause
- [ ] Seeking, progress bar
- [ ] Save position: on pause, on exit, on minimize, periodically
- [ ] Resume from the saved position
- [ ] Next / previous lesson
- [ ] Playback speed 0.5–3x
- [ ] Fullscreen mode
- [ ] "Continue" screen on the home page
- [ ] Playback error handling

### Definition of done

```text
The user watches a lesson on their phone, closes the app,
opens it three days later — and lands on the same second.
```

### What is verified by spike beforehand

- Whether speed switching works **without pitch distortion** on both platforms.
- Whether video from a SAF `content://` URI plays directly, and not only by path.
- Whether the position survives minimizing and the app being killed.

The media engine, and why that one — [PlayerSpec](Specs/PlayerSpec.md).

---

## Stage 3. Progress and statuses

**Goal:** the app understands what has been completed and what has not.

### Tasks

- [ ] Auto-complete a lesson at a threshold (90%)
- [ ] `CompletionSource`: distinguish auto-completion from a manual mark
- [ ] Protect a manual mark from being overwritten automatically
- [ ] Compute progress lesson → module → course
- [ ] Automatic course status
- [ ] Manually complete a lesson and a course
- [ ] Clear the mark
- [ ] Indicate missing files (`Missing`)

### Definition of done

```text
The user marks a lesson as not completed manually —
and the mark does not come back on its own.
```

This is the definition precisely because protecting a manual mark from auto-completion is the only non-trivial part of the stage. Everything else is arithmetic.

---

## Stage 4. Tasks and streaks

**Goal:** a study routine emerges — the very thing the project exists for.

### Tasks

- [ ] `LearningActivity`: track activity by day
- [ ] Streak calculation: current and longest
- [ ] Automatic generation of daily tasks
- [ ] Custom tasks: goal, course, deadline, repeat
- [ ] Compute task completion from progress
- [ ] Tasks screen with sections
- [ ] Home screen: streak, today's tasks, "Continue"
- [ ] Settings: daily goal, streak threshold, day start

### Definition of done

```text
The user studies for a week and sees a 7-day streak,
without ever running into an incorrect count.
```

### Stage risks

Date and time zone logic. Details — [TasksStreaksSpec](Specs/TasksStreaksSpec.md) and [OpenQuestions](OpenQuestions.md#time-zones-and-the-day-boundary).

---

## Stage 5. Data transfer

**Goal:** data can be moved between devices manually.

### Tasks

- [ ] Export all data to JSON
- [ ] Import with matching by structure fingerprint
- [ ] Import progress **without files** (course with a "files not found" status)
- [ ] Manually specify a folder for a course that was not found
- [ ] Export schema versioning
- [ ] Conflict resolution with a dialog
- [ ] Backups

### Definition of done

```text
The user exports data on a PC, imports it on a phone,
points at the folder — and continues from the same place.
```

This scenario is the main test of **the entire data model**. If it does not work, course identity has been designed incorrectly. Small patches will not cure it.

### What is verified by spike beforehand

The export format and the matching algorithm — on paper, before code. A mistake here costs a full rework of synchronization.

---

## Stage 6. PC as the core

**Goal:** the PC serves courses and progress, the phone receives them.

### Tasks

- [ ] Local HTTP server: course list, structure, files
- [ ] Streaming playback with `Range` support
- [ ] Pairing by QR with a one-time token
- [ ] Client: discovery, connection, course list
- [ ] Progress synchronization
- [ ] Device revocation
- [ ] Server screen: status, address, connected devices

### Definition of done

```text
The phone connects to the PC by QR,
sees the library, plays video and syncs progress.
```

### ⚠ Stage risk — requires verification before planning

Embedding `Kestrel` inside a MAUI app is **officially unsupported** on Android and requires workarounds. This may change the architecture of the stage.

The options and their consequences are laid out in [OpenQuestions → Local server](OpenQuestions.md#local-network-server-inside-maui). **Decide before stage 6 begins**, and preferably before stage 0.

Spike: whether the server comes up on Windows, whether it is visible from the local network, whether it gets through the firewall.

---

## Stage 7. Downloads and space management

**Goal:** the app is comfortable on a phone without manual cleanup.

### Tasks

- [ ] Download queue with states
- [ ] Download a course / module / selected lessons
- [ ] Resume after an interruption
- [ ] Auto-download the next lesson
- [ ] Space limit and warnings
- [ ] Cleanup policies: delete watched, keep the last N
- [ ] Downloads screen
- [ ] Background downloads (foreground service on Android)

### Definition of done

```text
The user downloads a course to their phone, watches half of it,
and the app frees up space for the next one on its own — without their involvement.
```

---

## Stage 8. Player improvements

**Goal:** watching a course is as convenient as on an online platform.

### Tasks

- [ ] Subtitles: `.srt` and `.vtt`, automatic attachment, track selection
- [ ] Picture-in-Picture
- [ ] Auto-advance to the next lesson with a setting
- [ ] Skip intro
- [ ] Gestures: seeking, volume, brightness
- [ ] Precise speeds and remembering the choice per course

### Definition of done

```text
The user watches a lesson in a small window
while taking notes in another app.
```

---

## Stage 9. Notifications

**Goal:** the app reminds you about itself and protects the streak.

### Tasks

- [ ] Daily task reminder
- [ ] Streak loss warning
- [ ] Download completion and error notifications
- [ ] Task deadline reminder
- [ ] Permissions: `POST_NOTIFICATIONS` on Android
- [ ] Notification settings

### Definition of done

```text
On a phone, notifications work and are enabled by default.
On a PC, they are off and are enabled in the settings.
```

---

## Stage 10. External clouds

**Goal:** courses can be taken not only from the PC, but also from your own cloud.

### Tasks

- [ ] `ILibrarySource` abstraction
- [ ] WebDAV
- [ ] Nextcloud / ownCloud
- [ ] Authorization: login/password, token
- [ ] Caching during streaming
- [ ] Your own server as one of the sources — through the same abstraction

### Definition of done

```text
The user connects their Nextcloud
and sees its courses on par with local ones.
```

---

## MVP

The minimum viable product is **stages 0–4** without everything else.

```text
1.  Import a local folder                     (stage 1)
2.  Automatic structure recognition           (stage 1)
3.  Recognized structure review screen        (stage 1)
4.  Course library                            (stage 1)
5.  Course page, modules and lessons          (stage 1)
6.  Video player with position memory         (stage 2)
7.  Playback speed                            (stage 2)
8.  Auto-complete a lesson at 90%             (stage 3)
9.  Progress of lesson, module, course        (stage 3)
10. Manual completion mark                    (stage 3)
11. Categories                                (stage 1)
12. Simple daily tasks                        (stage 4)
13. Streak                                    (stage 4)
14. Dark theme                                (stage 0)
```

### What is not in the MVP

Deliberately deferred so the project does not sprawl:

```text
Full synchronization · PC as a server · WebDAV · Nextcloud
Streaming · Notifications · Picture-in-Picture · Subtitles
Complex custom tasks · Plugins · Community themes
Downloads and space management · Statistics · Search
```

### MVP definition of done

Everything comes down to a single scenario:

```text
The user picked a folder → the app recognized the course
→ the user opened a lesson → watched it → progress was saved
→ the course got a status
```

**This loop has to feel good.** Not "work", but feel good. If it is annoying, none of the deferred features will save the project.

---

## Order, if you pick only one stage

If it is unclear where to start right now — **stage 1**.

It delivers the most value per unit of work: without it there is no player (nothing to play), no progress (nothing to count), and no sync (nothing to sync).

But **before** stage 1 you need to:

1. Bring the solution to the target structure (stage 0) — otherwise stage 1 starts with a refactor.
2. Run the spikes: SAF on Android, playback from `content://`, speed without pitch distortion.
3. Settle the licensing question — it affects the README and contributor trust.

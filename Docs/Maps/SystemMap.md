# System Map

A map of **what parts the system consists of** and how they talk to each other.

Answers the questions: where is the UI, where is the business logic, where is file handling, where is the database, where is the network, and what can be changed independently.

> **Related documents:** [Vision](../Vision.md) · [DomainMap](DomainMap.md) · [Roadmap](../Roadmap.md) · [OpenQuestions](../OpenQuestions.md)

---

## 1. The main principle

> **The connection between files, courses, progress and tasks must be explicit.**

The application must not "just know" where something lives. Every piece of work is a separate module with a clear responsibility. No module reaches into the file system or the database "quietly", bypassing its own layer.

---

## 2. Layers and the direction of dependencies

Dependencies go **strictly in one direction**. The arrow `A → B` means "A knows about B, B does not know about A".

```text
                    ┌──────────────┐
                    │   UI / App   │  screens, ViewModels, navigation
                    └──────┬───────┘
                           │
        ┌──────────────────┼──────────────────┐
        │                  │                  │
        ▼                  ▼                  ▼
   ┌─────────┐      ┌────────────┐      ┌──────────┐
   │  Sync   │      │ Downloads  │      │  Tasks   │
   └────┬────┘      └──────┬─────┘      └────┬─────┘
        │                  │                 │
        └──────────┬───────┴─────────────────┘
                   ▼
             ┌──────────┐        ┌─────────┐
             │   Data   │────────│ Parser  │
             └────┬─────┘        └────┬────┘
                  │                   │
                  └─────────┬─────────┘
                            ▼
                      ┌──────────┐      ┌─────────┐
                      │   Core   │─────▶│  Media  │
                      └────┬─────┘      └─────────┘
                           ▼
                      ┌──────────┐
                      │  Shared  │
                      └──────────┘
```

The direction is bottom-up: lower layers do not know about upper ones. `Core` does not know about SQLite. `Parser` does not know about the database. `Media` does not know about progress — it reports "12 seconds played", and `Core` decides what to do with that.

---

## 3. Modules

### `Shared` — common contracts

Low-level types that pull nothing else behind them: identifiers, serialization formats, `StableKey`, the `course.json` format, common enums.

**What must not be here:** domain entities, logic, dependencies on MAUI. `Shared` depends on nothing.

---

### `Core` — the domain

The heart of the project. Rules live here, not infrastructure.

- Domain entities and invariants.
- Interfaces of all services (`ICourseRepository`, `ICourseParser`, `IMediaPlayer`, `IProgressCalculator`, `ITaskGenerator`, `ISyncService`, `IDownloadManager`, `IStorageManager`).
- Lesson completion rules (90% threshold, manual marking).
- Progress calculation: lesson → module → course.
- Rules for generating daily tasks and counting the streak.
- Course deletion rules.

**Key property:** `Core` knows nothing about SQLite, the file system or MAUI. It can be covered by tests without a single platform dependency. This makes `Core` the most valuable module for tests and for contributors.

---

### `Data` — storage

SQLite and everything connected to it.

- Table schema and migrations.
- Repositories — implementations of the interfaces from `Core`.
- Transactions.
- Change log and entity versions — for synchronization.
- Export and import of a data snapshot to JSON.

**What must not be here:** business rules. A repository can "save a lesson", but does not decide "whether the lesson is complete".

---

### `Parser` — structure recognition

Turns the file system into a "course → module → lesson" model.

- Walking folders / the source listing.
- Determining course boundaries (course folder vs library folder).
- Determining modules and lessons by nesting.
- Extracting numbers from names (`01.mp4`, `S01E04.mp4`, `Урок 3 - ...`).
- Cleaning up titles.
- Finding subtitles and covers.
- Reading `course.json`, if present.
- Building `ParsedCourse` — a **temporary** model for the review screen.

**Key property:** the parser **writes nothing to the database**. It returns a proposal that the user confirms. This is what makes an import reversible until the moment of confirmation.

Details — [ParserSpec](../Specs/ParserSpec.md).

---

### `Media` — the player

A wrapper around the media engine. Isolates the rest of the system from which particular player is used.

- Playback, pause, seek.
- Playback speed.
- Subtitles.
- PiP.
- Playback position and its events.
- Distinguishing a local file from a network stream.

**Key property:** `Media` **does not store progress**. It only reports what happened ("position 12:34", "end reached"). Where to record this and what it means is decided by `Core`.

This is the only module that is inevitably tied to the platform. It, and only it, must know about ExoPlayer, LibVLC or `MediaElement`. Details — [PlayerSpec](../Specs/PlayerSpec.md).

---

### `Downloads` — downloads

Downloading courses from external sources into local storage.

- The download queue and its states.
- Downloading a course / module / individual lessons.
- Resuming after an interruption.
- Checking free space and limits.
- Cleanup policies (delete watched, keep the last N).
- Placing files in the application sandbox.

Details — [StorageSpec](../Specs/StorageSpec.md).

---

### `Tasks` — tasks, streaks, statistics

The motivational part.

- Generating automatic tasks from the state of progress.
- Custom tasks with deadlines and repetitions.
- Task completion rules.
- Tracking activity by day, counting the streak.
- Aggregates for statistics.

Details — [TasksStreaksSpec](../Specs/TasksStreaksSpec.md).

---

### `Sync` — synchronization

The most complex part of the project. Brings the state from several devices together into one.

- Export and import of a full data snapshot.
- Determining course identity between devices.
- Conflict resolution (by default — ask the user).
- Versioning of entities and tombstones.
- Optional E2E encryption.
- Exchange via file, cloud or a local server.

Details — [SyncSpec](../Specs/SyncSpec.md).

---

### `Server` — the PC as a core

An optional component: a desktop application raises a local HTTP server and serves courses to other devices.

- Course list and structure.
- File downloads.
- Streaming playback with `Range` support.
- Progress synchronization.
- Device pairing via QR.

**Key property:** the module is entirely optional. Its absence must not break a single feature. Details — [ServerApiSpec](../Specs/ServerApiSpec.md).

---

### `Client` — connecting to a core

The mirror side of `Server`: discovering a core on the network, pairing, fetching the course list, downloading, streaming, synchronization.

**Important detail:** the `ILibrarySource` implementation for your own server is the same abstraction as for WebDAV. `Client` must not be a special case in the source code.

---

### `UI / App` — the application

The MAUI project itself: pages, ViewModels, navigation, DI, themes, localization, platform permissions.

**Key property:** this layer must be **thin**. It calls services and draws the result. It does not parse folders, does not compute progress and does not resolve conflicts.

---

## 4. Rules that must not be broken

| Rule | Why |
|---|---|
| `Core` does not reference MAUI | Otherwise the domain cannot be tested without the platform |
| `Parser` writes nothing to the database | An import must be reversible until confirmation |
| `Media` does not store progress | Otherwise the lesson completion logic cannot be reused |
| `Data` contains no business rules | Otherwise the rules smear across layers and the DB |
| The UI does not touch the file system directly | Otherwise paths and permissions leak into every screen |
| `Server` is optional | Offline-first: the application must work without it |
| No module depends on the UI | Otherwise the UI cannot be replaced and the head cannot be tested |

---

## 5. Repository map

### Target structure

```text
CourseMoth.slnx
src/
  CourseMoth.Shared/      # contracts, no dependencies
  CourseMoth.Core/        # domain (netX.0, no MAUI)
  CourseMoth.Data/        # SQLite (netX.0, no MAUI)
  CourseMoth.Parser/      # structure recognition (netX.0, no MAUI)
  CourseMoth.Media/       # player (MAUI, platform-dependent)
  CourseMoth.Downloads/   # downloads
  CourseMoth.Tasks/       # tasks and streaks
  CourseMoth.Sync/        # synchronization
  CourseMoth.Server/      # local server
  CourseMoth.Client/      # connecting to a core
  CourseMoth.App/         # UI and navigation (MAUI) + heads/ for platforms
tests/
  CourseMoth.Core.Tests/
  CourseMoth.Parser.Tests/
  CourseMoth.Sync.Tests/
samples/
  sample-courses/
  sample-metadata/
docs/
```

### Current state

Three of the eleven modules exist, as separate `net10.0` libraries. The rest are still folders-not-yet-projects, and the `src/` move has not happened — the projects sit under `CourseMoth/` because Visual Studio held file locks on the directory when the move was attempted.

```text
CourseMoth.slnx
CourseMoth/
  CourseMoth.Core/        # ✅ domain — 9 services, no MAUI reference
  CourseMoth.Data/        # ✅ SQLite — schema, 9 repositories, no MAUI reference
  CourseMoth.Core.Tests/  # ✅ 132 tests, runs without a platform
  CourseMoth/             # ✅ MAUI app — Shell, five tabs, DI, themes
  CourseMoth.WinUI/       # ✅ Windows head
  CourseMoth.Droid/       # ⬜ Android head (builds, unverified on device)
  CourseMoth.iOS/         # ⬜ iOS head (untested — no Apple hardware)
  CourseMoth.Mac/         # ⬜ Mac Catalyst head (untested)
Docs/
readme.md
```

**`Parser` is the missing piece.** `Tasks` and `Sync` are not separate projects yet either, but their logic already lives in `Core` (`TaskEvaluator`, `StreakCalculator`) and is therefore covered by tests. The parser has no home at all, which is why import does not work end to end.

> The modules-versus-projects question is settled — see [OpenQuestions](../OpenQuestions.md#modules-and-projects). The criterion applied was platform dependency, and it held: `Core` and `Data` are plain libraries, `Media` will be the first that genuinely needs the platform.

### The rule for splitting into projects

Not every module has to be a separate `.csproj`. The criterion is **whether it needs the platform**:

- **Platform-independent** (`Shared`, `Core`, `Data`, `Parser`, `Tasks`, `Sync`) — ordinary `netX.0` libraries. These are exactly what gives testability and they are the main reason for the split.
- **Platform-dependent** (`Media`, part of `Downloads`) — require MAUI and therefore live in a multi-targeted project.
- **Heads** (`WinUI`, `Droid`, `iOS`, `Mac`) — just the entry point and platform registration.

---

## 6. How the modules communicate

Between modules — **only interfaces from `Core`**, and only through DI. No `new SqliteCourseRepository()` in the UI.

Data flows for the key scenarios:

**Folder import**
```text
UI  →  Parser.Scan(path)  →  ParsedCourse (proposal)
UI  →  [review screen, user edits]
UI  →  Core.ImportService.Confirm(parsed)  →  Data  →  SQLite
```

**Lesson playback**
```text
UI  →  Media.Open(lesson)  →  playback position events
Media  →  Core.WatchStateService.Report(position)
Core  →  Data  →  lesson progress, module and course statuses
Core  →  Tasks.Evaluate(today)  →  task completion and the streak
```

**Import from a source**
```text
UI  →  ILibrarySource (Local / Server / WebDAV)
     →  Downloads.Queue(lessons)  →  application sandbox
     →  Parser  →  Core  →  Data
```

**Synchronization**
```text
Data.ChangeLog  →  Sync.BuildSnapshot()  →  [file / cloud / server]
                                            ↓
                      Sync.Merge(remote)  →  conflicts  →  UI (dialog)
```

---

## 7. What can be changed independently

| Change | Do not touch |
|---|---|
| The player engine (MediaElement → LibVLC) | `Core`, `Data`, UI |
| Storage (SQLite → something else) | `Core`, UI, `Parser` |
| Source (folder → server → WebDAV) | `Core`, `Data`, UI |
| The structure parser | `Data`, UI, `Media` |
| The export format | UI, `Media`, `Parser` |
| Theme and layout | Everything else |

The last column is exactly the modularity check. If swapping the player requires edits in `Core` — the boundary was drawn incorrectly.

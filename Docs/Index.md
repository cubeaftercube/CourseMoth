# CourseMoth Documentation

**A personal offline university for downloaded courses.**

Read this page first. It is the entry point to every other document.

---

## What this project is

CourseMoth turns a pile of downloaded course folders into a structured, trackable learning library.

You point it at a folder → it works out the course/module/lesson structure → you watch through a player that remembers where you stopped → progress, daily tasks, and streaks give you a reason to come back.

It runs **fully offline**. No account, no telemetry, no servers you did not choose. Progress can move between your devices when you want it to.

Full statement: [Vision](Vision.md)

---

## Where to start

| If you are… | Read |
|---|---|
| New to the project | [Vision](Vision.md) → [Roadmap](Roadmap.md) |
| Deciding whether to contribute | [Vision](Vision.md) → [Roadmap](Roadmap.md) → [OpenQuestions](OpenQuestions.md) |
| About to write code | [SystemMap](Maps/SystemMap.md) → [DomainMap](Maps/DomainMap.md) → [OpenQuestions](OpenQuestions.md) |
| Working on a specific module | That module's spec under [`Specs/`](Specs/) |
| Curious what is unresolved | [OpenQuestions](OpenQuestions.md) |
| Looking for something to do | [Roadmap](Roadmap.md), then [OpenQuestions → Spikes](OpenQuestions.md#spikes-to-run) |

---

## Documents

### Product layer — what we are building and why

| Document | Contents | Status |
|---|---|---|
| [Vision](Vision.md) | The problem, the audience, the principles, what the project is and is not | ✅ |
| [Roadmap](Roadmap.md) | Ten stages from skeleton to cloud sources, with the MVP scope | ✅ |

### Design layer — how it is put together

| Document | Contents | Status |
|---|---|---|
| [SystemMap](Maps/SystemMap.md) | Modules, boundaries, dependency direction, repository layout | ✅ |
| [DomainMap](Maps/DomainMap.md) | Entities, fields, relations, identity, lifecycle | ✅ |
| [UXMap](Maps/UXMap.md) | Screens, navigation, states, what is in the MVP | ✅ |
| [UserFlowMap](Maps/UserFlowMap.md) | End-to-end user scenarios and what they revealed | ✅ |

### Specifications — how each hard part works

| Document | Contents | Stage |
|---|---|---|
| [ParserSpec](Specs/ParserSpec.md) | Turning folder trees into course structure | 1 |
| [PlayerSpec](Specs/PlayerSpec.md) | Playback, position memory, speed, subtitles, PiP | 2 |
| [TasksStreaksSpec](Specs/TasksStreaksSpec.md) | Daily tasks, streaks, activity tracking, day boundary | 4 |
| [SyncSpec](Specs/SyncSpec.md) | Versioning, merge, conflicts, identity, encryption, pairing | 5 |
| [ServerApiSpec](Specs/ServerApiSpec.md) | The PC as a server: endpoints, auth, streaming | 6 |
| [StorageSpec](Specs/StorageSpec.md) | File locations, downloads, space, platform constraints | 7 |

### Open

| Document | Contents | Status |
|---|---|---|
| [OpenQuestions](OpenQuestions.md) | Everything unresolved, all risks, and the spike backlog | ✅ |

---

## The five things worth knowing before reading anything else

1. **Offline-first is not a feature, it is a constraint.** Any design that breaks without a network is wrong. [Vision §4.1](Vision.md#41-offline-first)

2. **The user's files are never deleted.** An imported folder is a reference, not ownership. This is enforced structurally, not by a checkbox. [Vision §4.3](Vision.md#43-the-users-data-belongs-to-the-user)

3. **Progress travels separately from video.** A course can exist with 62% progress and no files at all — that is the normal state between an export and a folder selection. This single fact drives the identity design. [SyncSpec §1](Specs/SyncSpec.md#1-what-syncs-and-what-does-not)

4. **The parser never writes to the database.** It proposes; the user confirms. Import is reversible until the moment it is not. [ParserSpec §1](Specs/ParserSpec.md#1-goal)

5. **`Core` must be testable without MAUI.** If it is not, the domain logic will not be tested, and the domain logic is the part that matters. [OpenQuestions → Modules and projects](OpenQuestions.md#modules-and-projects)

---

## Current state

**Stage 0 is done and stage 1 is partly done.** The domain layer and the storage layer exist and are tested; the app builds and launches on Windows with all five tabs working.

| Area | State |
|---|---|
| Documentation | ✅ Complete for stages 0–10 |
| Solution structure | ✅ `Core`, `Data`, `Core.Tests` as separate `net10.0` libraries |
| Domain code | ✅ 9 services, 132 tests, no MAUI dependency |
| Storage | ✅ SQLite schema, 9 repositories, verified round-trip |
| Application shell | ✅ Shell with five tabs, DI wired, runs on Windows |
| **Add-folder button** | ❌ **A real mouse click does nothing — see below** |
| **Parser** | ❌ **Not started** |
| Player | ❌ Not started |
| `LICENSE` | ✅ AGPLv3 |
| `README` | ✅ Updated |
| `CONTRIBUTING` | ❌ Not written |
| `samples/` | ❌ Not created |

### Two things that do not work

**1. The "Add folder with courses" button ignores a mouse click.** The click is not reaching the
handler at all; the button is enabled, visible and on screen. Invoking the same code path
programmatically *does* open the folder dialog and return a folder, so the import logic behind the
button is sound — the input path to it is not. Replacing the `Command` binding with a `Clicked`
handler in code-behind has been done but **not verified**, because the check needed a real mouse
click. This is the next thing to investigate, and it is tracked in
[OpenQuestions](OpenQuestions.md#the-add-folder-button-does-not-respond-to-a-mouse-click).

**2. The parser does not exist.** Everything downstream of a parsed course is built —
`ImportService` turns a `ParsedRoot` into courses, modules and lessons, the fingerprint is computed,
the rows are persisted — but nothing produces a `ParsedRoot` from a directory. Even once the button
responds, it will stop at the dialog until this is written. [ParserSpec](Specs/ParserSpec.md) is
its specification.

**Next actions:** make the button respond, then build the parser, then run the five spikes below.

---

## Spikes

Ten technical assumptions carry real risk. Five of them block stage 1.

The full list, with effort estimates, is in [OpenQuestions → Spikes to run](OpenQuestions.md#spikes-to-run).

The short version — run these **before writing feature code**:

```text
1. SAF folder access on Android, persisted across reboot
2. MediaElement playing a content:// URI
3. Pitch preservation at 0.5x and 3x
4. Subtitle rendering
5. Position surviving process death
```

---

## Conventions

**Documentation language:** English, so that contributors can work from the specifications directly.

**Identifiers:** code identifiers, type names, and file paths stay in English in all documents.

**Document status:** each document opens with a `> Related:` line linking its neighbours. Claims that are unverified are marked with **⚠️ unverified** rather than stated as fact.

---

## Contributing

Not yet written. When it exists it will cover:

- Licence headers (`SPDX-License-Identifier: AGPL-3.0-or-later`)
- Where to start, and which modules are approachable for a first contribution
- How the module boundaries in [SystemMap](Maps/SystemMap.md) are enforced
- Testing expectations per module

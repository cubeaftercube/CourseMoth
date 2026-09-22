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

The repository holds a **stock .NET MAUI template**. No project structure from [SystemMap](Maps/SystemMap.md) exists yet, no domain code has been written, and the documentation you are reading is the most developed part of the project.

That is deliberate: the design decisions that are expensive to reverse — the data model, course identity, module boundaries — are settled before code depends on them.

| Area | State |
|---|---|
| Documentation | ✅ Complete for stages 0–10 |
| Repository structure | ❌ Stock MAUI template |
| Domain code | ❌ Not started |
| Player | ❌ Not started |
| Tests | ❌ Not started |
| `LICENSE` | ✅ AGPLv3 |
| `README` | ⚠️ Needs updating (claims "license TBD") |
| `CONTRIBUTING` | ❌ Not written |
| `samples/` | ❌ Not created |

**Next actions:** [OpenQuestions § Summary by stage](OpenQuestions.md#summary-by-stage) and the spike list below it.

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

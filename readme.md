# CourseMoth

A personal offline university for downloaded courses.

Turn a pile of downloaded course folders into a structured, trackable learning library: point it at a folder, get a course tree, watch through a player that remembers where you stopped, and keep yourself going with daily tasks and streaks.

Fully offline. No account, no telemetry, no servers you did not choose. Your data stays yours.

## Platforms

| Platform | Status |
|---|---|
| Windows 10+ | Actively developed |
| Android 10+ | Actively developed |
| iOS | Code present, untested — no Apple hardware available |
| macOS (Mac Catalyst) | Same |

## Features

Currently a skeleton — see [Roadmap](Docs/Roadmap.md) for what is planned and in what order.

**MVP target:**

- Import a local folder and auto-detect the course / module / lesson structure
- Review and correct the detected structure before anything is saved
- Library with categories, statuses, and filters
- Course page with modules, lessons, and progress
- Video player that remembers position, speed, and where you left off
- Progress for lessons, modules, and courses
- Daily tasks and streaks
- Dark theme

**Not in the MVP:** synchronization, PC-as-server, WebDAV, streaming, notifications, picture-in-picture, subtitles, downloads.

## Stack

- .NET MAUI (.NET 10)
- SQLite
- MVVM + dependency injection
- Modular architecture — see [SystemMap](Docs/Maps/SystemMap.md)

## Documentation

**[Docs/Index.md](Docs/Index.md)** is the entry point.

| Layer | Documents |
|---|---|
| Product | [Vision](Docs/Vision.md) · [Roadmap](Docs/Roadmap.md) |
| Design | [SystemMap](Docs/Maps/SystemMap.md) · [DomainMap](Docs/Maps/DomainMap.md) · [UXMap](Docs/Maps/UXMap.md) · [UserFlowMap](Docs/Maps/UserFlowMap.md) |
| Specs | [Parser](Docs/Specs/ParserSpec.md) · [Player](Docs/Specs/PlayerSpec.md) · [Tasks & Streaks](Docs/Specs/TasksStreaksSpec.md) · [Sync](Docs/Specs/SyncSpec.md) · [Server API](Docs/Specs/ServerApiSpec.md) · [Storage](Docs/Specs/StorageSpec.md) |
| Open | [OpenQuestions](Docs/OpenQuestions.md) |

Documentation is in English so contributors can work from it directly.

## Status

Early. The documentation is complete for all planned stages; the code is a stock .NET MAUI template. The design decisions that are expensive to reverse — data model, course identity, module boundaries — are settled before code depends on them.

Open technical questions and the spike backlog are in [OpenQuestions](Docs/OpenQuestions.md).

## Contributing

`CONTRIBUTING.md` is not written yet. Until it is, start with [Docs/Index.md](Docs/Index.md), and check [OpenQuestions → Spikes to run](Docs/OpenQuestions.md#spikes-to-run) for work that does not require settling the architecture first.

## License

**GNU Affero General Public License v3.0** — see [LICENSE](LICENSE).

You may use, study, modify, and redistribute this software, including commercially, provided derivative works remain under the same license. Because it is AGPL, this extends to running a modified version as a network service: users interacting with it over a network must be able to obtain the source.

Source files carry an SPDX identifier:

```csharp
// SPDX-License-Identifier: AGPL-3.0-or-later
```

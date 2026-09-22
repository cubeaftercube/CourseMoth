# Vision / Product Charter

> **CourseMoth — a personal offline university for downloaded courses.**

This document is the top level. It does not describe features and does not describe code. It answers one question:

> **Does a specific decision match the spirit of the project?**

If a feature, an architectural decision, or a line of code contradicts this document — we discuss the document, not the feature.

---

## 1. The Problem

People accumulate downloaded courses for years: from open sources, from conferences, from materials they bought, from internal training archives. This is legally obtained content — lectures, webinars, screencasts — and it piles up into tens and hundreds of gigabytes.

Then the same thing happens:

- **The files sit in a heap.** Courses are scattered across drives, external disks, phones, and home servers. Finding the module you need is a task in itself.
- **An ordinary player doesn't understand structure.** For VLC or a system player, `Курс/Модуль 3/05 - LINQ.mp4` is just a file among a thousand others. No course, no module, no "resume where you left off."
- **Progress doesn't exist.** The player remembers the playback position in a single file. It doesn't know that a lesson is finished, that a module is closed, that a course was abandoned halfway through three months ago.
- **There's no reason to come back.** Without structure, progress, and ritual, a downloaded course stays dead weight. People buy or download courses faster than they complete them.

The result: the content exists, but the learning doesn't.

---

## 2. Who It's For

- **Self-taught people with a large archive.** They've already downloaded dozens of courses and want to finally get through them, not keep growing the collection.
- **People with slow or expensive internet.** Online platforms require a constant connection and burn traffic. Downloaded content has to work without a network.
- **Those who care about privacy.** What a person studies is their own business. No accounts, no clouds by default, no telemetry.
- **Those whose content is spread across devices.** Courses on a PC, watching from a phone while lying on the couch, and progress has to be one.
- **A Russian-speaking and open-source audience.** People who value tools they can fix, extend, and verify themselves.

Not the target audience: those who want to watch video "here and now" without accumulating anything, and those who want a streaming service with recommendations.

---

## 3. Why an Ordinary Video Player Doesn't Fit

| Ordinary player | CourseMoth |
|---|---|
| The unit is a file | The unit is a **course**, module, lesson |
| Remembers the position in the current file | Remembers **progress across the whole library** |
| Doesn't know structure | **Recognizes** folder structure |
| No connection between files | Lessons are linked: next, autoplay, module progress |
| No reason to come back | **Tasks, streaks, statistics** |
| Plays a file locally | Locally **and** from the home network |
| One file — one device | **One progress across all devices** |

The key difference isn't the number of features. It's that a player answers the question "how do I play this file", while CourseMoth answers the question **"what should I study next"**.

---

## 4. Principles

These aren't wishes, they're constraints within which decisions are made.

### 4.1. Offline-first

The application works fully without the internet. The network is an extension, not a requirement. First launch, folder import, playback, progress, tasks — all offline. Any feature that breaks without a network is designed wrong.

### 4.2. Privacy by Default

No accounts. No telemetry. No calls to external servers except those the user enabled themselves. The application's data lives on the user's device and goes nowhere without an explicit action.

### 4.3. The User's Data Belongs to the User

- Exporting all data into an open format is an obligation, not a feature.
- No vendor lock-in: the database, the configs, and `course.json` are readable.
- The application **never** deletes the source course files without explicit consent. An imported folder is a reference, not ownership.

### 4.4. Local Core

The source of truth is the local database. Sync, the server, and clouds adapt to it, not the other way around. The application must remain fully functional if all network modules are turned off.

### 4.5. Open Code

The project is under **AGPLv3**. Anyone can read, build, modify, and run it. Derivative works — including ones hosted as a network service — must stay open.

A conscious limitation: AGPLv3 **permits commercial use**. The requirement that "forks can't charge money" is not enforced by the license — see [OpenQuestions](OpenQuestions.md#license).

### 4.6. Modularity

The boundaries between subsystems are explicit: the parser knows nothing about the database, the player knows nothing about sync, the UI knows nothing about the file system. A feature that can't be replaced is designed wrong.

### 4.7. Freedom of Choice for the User

Where there's a conflict — we ask, we don't decide silently. What to encrypt is chosen by the user. What to delete is chosen by the user. Where to sync is chosen by the user.

---

## 5. What the Project Is and Isn't

### Is

- **A personal offline library** of downloaded courses with recognized structure.
- **A learning tracker** — progress, statuses, statistics.
- **A motivation tool** — tasks, streaks, ritual.
- **A bridge between devices** — progress follows the user.
- **A hub for your own collection** — a PC can be the core and serve out courses.
- **An open standard for describing a course** — `course.json`, which anyone can support.

### Isn't

- **Not a catalog store.** The application doesn't sell or recommend courses.
- **Not a streaming service.** This isn't "video on subscription".
- **Not an online platform.** No registration, no courses on a server, no social features.
- **Not cloud storage.** Storing files is the user's concern, not the project's.
- **Not a download manager in general.** Downloads exist exactly as far as they're needed for courses.
- **Not a tool for downloading content from the internet.** The application works with what the user already has, or with their own storage. It doesn't bypass protections and doesn't pull content from other people's platforms.
- **Not a player "for all occasions".** The player is optimized for educational video, not for movies and music.

---

## 6. What Counts as Success

### The MVP is successful when one scenario works

> The user picked a folder → the application recognized a course → the user opened a lesson → watched it → progress was saved → the course got a status.

This cycle has to work **pleasantly**. Not "it works", but pleasantly — otherwise the rest of the features don't matter.

### The project is alive when

- There are people other than the author who use it.
- There are contributors who figured out the code without a personal conversation with the author.
- The `course.json` format is used by someone else.
- Downloaded courses get finished more often than before.

---

## 7. How to Use This Document

Before adding a feature or making an architectural decision — run through these questions:

1. **Does it work offline?** If not — it needs a serious justification.
2. **Does anything leave the data out?** If yes — only through an explicit action by the user.
3. **Can the user take their data with them?** If it makes export harder — the decision is bad.
4. **Will this break a module boundary?** If the parser starts writing to the database — refuse.
5. **Does it fit the "Isn't" section?** If yes — refuse.
6. **Who needs this right now?** If the answer is "it'll be needed later" — into the [Roadmap](Roadmap.md), not into the code.

---

## Related Documents

- [Index](Index.md) — the entry point to the documentation
- [Roadmap](Roadmap.md) — stages and the MVP
- [SystemMap](Maps/SystemMap.md) — what parts the system consists of
- [DomainMap](Maps/DomainMap.md) — entities and relations
- [UXMap](Maps/UXMap.md) — screens
- [UserFlowMap](Maps/UserFlowMap.md) — user scenarios
- [OpenQuestions](OpenQuestions.md) — what isn't decided yet and what's risky

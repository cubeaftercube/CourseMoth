# Open Questions and Risks

Everything still unresolved, doubtful, or technically unverified.

This document exists so that forgotten problems do not resurface in the middle of development.

> **Related:** [Vision](Vision.md) · [Roadmap](Roadmap.md) · [SystemMap](Maps/SystemMap.md)

**Status markers:** 🔴 blocking · 🟠 decide before the referenced stage · ⚪ informational

---

## License

**Status:** 🟠 decided, with a caveat that must be stated honestly

The project is licensed under **AGPLv3**.

### What was originally wanted

> Code is open for study and contribution, forks are allowed, forks must stay open, and you cannot make money from forks.

### What AGPLv3 actually provides

| Requirement | AGPLv3 |
|---|---|
| Code is open | ✅ |
| Forks are allowed | ✅ |
| Forks must stay open | ✅ — stronger than GPL, since hosting a modified version as a network service also triggers the obligation |
| **Forks cannot make money** | ❌ **AGPLv3 permits commercial use** |

**No OSI-approved open-source license can forbid commercial use** — that prohibition is incompatible with the Open Source Definition itself.

### The consequence

A company may legally take CourseMoth, host it, and charge for it, provided they publish their modifications under AGPLv3. This is accepted.

### If the prohibition matters later

The only way to forbid commercial use is to leave open-source licensing entirely and adopt a source-available license (BUSL, PolyForm Noncommercial, or a custom one). That would require changing `README.md` to stop describing the project as open-source.

**Decided: stay with AGPLv3.** The requirement that forks stay open was the binding one, and AGPLv3 delivers it more completely than GPLv3.

### Custom headers

AGPLv3 §5 requires modified files to carry prominent notices. Contributors should add a short SPDX header to new source files:

```csharp
// SPDX-License-Identifier: AGPL-3.0-or-later
```

This should go into `CONTRIBUTING.md` when it is written.

---

## Modules and projects

**Status:** 🔴 blocking stage 0

[SystemMap](Maps/SystemMap.md#5-repository-map) describes eleven modules. The repository currently contains a stock .NET MAUI template with no modules at all.

### The question

Do the modules become separate `.csproj` projects, or folders inside the MAUI project?

### Why it blocks

The reason to separate them is not tidiness. It is that **`Core` must be testable without MAUI**. A domain model that can only be exercised through a running app is a domain model that will not be tested, and the completion rules, progress calculations, and fingerprinting are precisely the code most in need of tests.

If the modules are folders inside the MAUI project, `CourseMoth.Core.Tests` cannot exist.

### Recommendation

```text
Separate netX.0 class libraries:
    Shared · Core · Data · Parser · Tasks · Sync

Stay in the multi-targeted MAUI project:
    Media · parts of Downloads

Separate heads:
    WinUI · Droid · iOS · Mac
```

The criterion is platform dependency, not size. A module that needs no platform should not be in a platform-targeted project.

### Decision needed before stage 0 closes.

---

## Local network server inside MAUI

**Status:** 🔴 blocking stage 6, affects packaging from stage 0

Embedding `Kestrel` / `ASP.NET Core` inside a .NET MAUI app is **not officially supported** on Android or iOS — the `Microsoft.AspNetCore.App` framework reference is unsupported on `net*-android` and `net*-ios`.

Community workarounds exist ([JamesNK/aspnetcore-maui](https://github.com/JamesNK/aspnetcore-maui), [dotnet/aspnetcore#35077](https://github.com/dotnet/aspnetcore/issues/35077)) and require MSBuild hacks to ship ASP.NET assemblies. They are untested against .NET 10 Android trimming.

### Options

| Option | Assessment |
|---|---|
| **A.** Standalone ASP.NET Core process alongside the app | Supported stack; a second process to manage |
| **B.** Embedded Kestrel in-process | Community hacks, trimming risk, unsupported on mobile |
| **C.** Windows-only server; Android is always a client | Simplest, matches the actual use case |

### Recommendation: C, later extensible to A

The server exists so the PC can serve the phone. The phone never needs to serve anything. Option C keeps the unsupported code path out of the project entirely.

Do **not** choose B for stage 6. Discovering trimming breaks weeks in, on the platform with the worst debugging experience, is the predictable outcome.

Full detail: [ServerApiSpec §2](Specs/ServerApiSpec.md#2-architecture--an-unresolved-decision)

### Decision needed before stage 6. Affects packaging decisions made in stage 0.

---

## Player engine

**Status:** 🟠 blocking stage 2

`CommunityToolkit.Maui.MediaElement` wraps the platform-native players (ExoPlayer on Android, WinUI `MediaPlayerElement` on Windows, AVPlayer on iOS/macOS). It is the recommended engine.

### The finding that changes the plan

The original design discussions treated **LibVLCSharp** as the fallback if MediaElement proved insufficient. **That fallback does not exist for Windows** — LibVLCSharp's MAUI `MediaPlayerElement` supports iOS, Android, and UWP only, with no usable WinUI story.

If MediaElement is insufficient, the realistic path is **platform handlers** (raw ExoPlayer plus WinUI `MediaPlayerElement`), which is two separate codebases. That is a much larger commitment and should be avoided if at all possible.

### Two behaviours remain unverified

| Behaviour | Why it matters |
|---|---|
| **Pitch preservation at 0.5x–3x** | A lecture at 2x that sounds like a chipmunk is unusable. For course content this is not a nicety. |
| **`.srt` / `.vtt` rendering** | Whether MediaElement renders subtitle tracks at all could not be confirmed |

Both need a device test on **both platforms** before the speed and subtitle UI is designed.

### Known hazards

- Trimmed constructors on .NET 10 Android ([#3114](https://github.com/CommunityToolkit/Maui/issues/3114)) → needs `TrimMode=partial`
- Foreground-service permission denials on Android 14+ ([#3103](https://github.com/CommunityToolkit/Maui/issues/3103))
- Codec gaps on low-end devices (no HEVC/AV1) → prefer H.264

Full detail: [PlayerSpec §2](Specs/PlayerSpec.md#2-engine-choice)

---

## Android file access

**Status:** 🟠 resolved by design, but requires a spike

### The constraint

Google Play has restricted `MANAGE_EXTERNAL_STORAGE` since May 2021 and **still enforces it**. Permitted uses are narrow — file managers, backup, antivirus, document management, on-device search, encryption, device migration. **Media access is explicitly not among them.**

A video player for personal course archives does not qualify.

### The decision

**Use the Storage Access Framework from the start** (`ACTION_OPEN_DOCUMENT_TREE` + `TakePersistableUriPermission`).

The user's plan is to distribute as an APK via GitHub Releases, possibly moving to Google Play later. An APK-only build *could* ask for broad storage permission, but that path leads to a rewrite at the exact moment the project tries to grow.

### Two failure modes to design around

| Failure | Cause |
|---|---|
| `SecurityException: Permission Denial` | A `content://` URI reconstructed by hand instead of obtained from `DocumentsContract.BuildChildDocumentsUriUsingTree` |
| Permission evaporates after restart | `ACTION_PICK` / `GET_CONTENT` used instead of `ACTION_OPEN_DOCUMENT_TREE` — only the latter grants durable access |

### Unverified

Whether **MediaElement** plays a `content://` URI directly (ExoPlayer does; whether the abstraction passes it through is untested).

Full detail: [StorageSpec §3](Specs/StorageSpec.md#3-platform-constraints)

---

## Android local network permission

**Status:** 🟠 affects stage 6, but must be designed for now

| Android version | Requirement |
|---|---|
| 10–15 | No local network permission needed |
| 16 (API 36) | Local Network Protection, **opt-in** during development. `NEARBY_WIFI_DEVICES` temporarily satisfies it. |
| 17 (API 37) | `ACCESS_LOCAL_NETWORK` becomes a **mandatory runtime permission** |

A runtime permission cannot be retrofitted gracefully onto a shipped pairing flow. The pairing design should account for a permission request step from the beginning, even though stage 6 is far off.

Related: cleartext HTTP has been blocked by default since Android 9, so plain-HTTP communication with the PC server needs `usesCleartextTraffic="true"` or a scoped `network_security_config`. Localhost cleartext is auto-permitted only from Android 17.

---

## Windows packaging

**Status:** ⚪ decided in principle, needs verification

**Unpackaged x64 build from GitHub Releases is viable:**

```bash
dotnet publish -f net10.0-windows10.0.19041.0 -c Release \
  -p:RuntimeIdentifierOverride=win-x64 \
  -p:WindowsPackageType=None \
  -p:WindowsAppSDKSelfContained=true
```

Note: on .NET 10 use portable RIDs (`win-x64`, not `win10-x64`).

### Tradeoffs

| | Unpackaged | MSIX |
|---|---|---|
| App SDK runtime | Embedded with `WindowsAppSDKSelfContained=true` | Separate install |
| Code signing | Not required | Required |
| Store distribution | No | Yes |
| Auto-update | Manual | Built in |
| Single-file EXE | **No** — WinUI 3 native dependencies stay as separate files | No |
| Push notifications | Limited | Supported |

Neither packaging mode restricts writing to user folders or hosting a network server. The relevant constraint there is the **Windows firewall**, which prompts on first bind.

### To verify

Publish a real `WindowsPackageType=None` build on a clean Windows 10 machine and confirm the self-contained App SDK payload runs without an installer.

---

## Course identity

**Status:** 🔴 blocking stage 5 (sync)

Everything in synchronization rests on recognizing the same course arriving on two devices by different routes.

### The approach

```text
1. course.json id          → exact
2. Structure fingerprint   → SHA-256 of normalized structure
3. StableKey               → human-readable fallback
```

Structure, not paths — a course may exist on a device with no files at all, holding only progress.

### Unresolved details

- **`StableKey` versus `RelativePath`** — these currently overlap almost entirely. Either `StableKey` earns its place or it should be dropped. Decide while designing the schema.
- **Fingerprint versioning** — changing the normalization algorithm changes every fingerprint and breaks matching for already-imported courses. The version is stored alongside each fingerprint and old ones are recomputed in the background, not discarded. This mechanism is designed but untested.
- **Renamed course folders** — the fingerprint covers relative paths, not the course folder name alone, so a rename should not break matching. Should be verified against real data.

### To verify

Import the same course on a PC and a phone by different routes and confirm the fingerprints match.

Full detail: [SyncSpec §4](Specs/SyncSpec.md#4-course-identity)

---

## Time zones and the day boundary

**Status:** 🟠 blocking stage 4

### The decision

The date assigned to an activity is computed **once, when the activity is recorded**, using the day boundary (default 04:00) and the device's timezone at that moment. A day's record never moves afterwards.

Consequences:

| Scenario | Behaviour |
|---|---|
| User travels two timezones | A day may be slightly short or long; the streak survives |
| User changes the day boundary setting | Applies from the current day onward |
| Weeks pass in another timezone | Historical days keep their original dates |

**History is never recomputed.** A streak that rewrites itself on a timezone change is worse than one that is occasionally a few hours off.

### Unresolved

Whether the setting change should offer to recompute the current day only, and how to present the unusual calendar this produces to a travelling user.

---

## Feature scope

**Status:** ⚪ deferred deliberately

### Not in the MVP

```text
Full synchronization · PC as server · WebDAV · Nextcloud
Streaming · Notifications · Picture-in-Picture · Subtitles
Complex user tasks · Plugins · Community themes
Downloads and space management · Statistics · Search
```

### Adjacent decisions not yet made

| Question | Note |
|---|---|
| Library search in MVP? | Depends on library size; decide after stage 1 |
| Tablet layout on Android | Same layout or two-pane? |
| Landscape in the player | Force or defer to the system? Check during stage 2 |
| iOS and macOS | Untested — no Apple hardware is available to the project. Windows and Android remain first-class. |
| Removable storage on Android | SAF gives no app-private directory on an SD card. Deferred. |

---

## `course.json` as a public contract

**Status:** ⚪ strategic, not blocking

`course.json` is described in [ParserSpec §10](Specs/ParserSpec.md#10-coursejson) and is intended to become an open format anyone can support.

### Unresolved

- **A JSON Schema** should be published so other tools can validate the format. Not yet written.
- **The `id` field** — whether it is a GUID generated by CourseMoth or an opaque string from any tool. Currently a GUID, which may be too restrictive for third-party generators.
- **Version migration** — `schemaVersion` exists, but no migration path is defined for a future version 2.
- **Round-trip fidelity** — whether CourseMoth should preserve unknown fields when rewriting the file. It currently does not rewrite the file at all without consent, which sidesteps the question.
- **Where the format is documented publicly** — currently only inside this repository. A published specification would help adoption.

If third-party adoption is a goal, this format deserves a document of its own rather than a section in the parser spec.

---

## Summary by stage

| Stage | Blocking questions |
|---|---|
| 0 | Modules and projects · server architecture (packaging implications) |
| 1 | Android file access (SAF spike) |
| 2 | Player engine (pitch and subtitle spikes) |
| 3 | — |
| 4 | Time zone and day boundary |
| 5 | Course identity |
| 6 | Server architecture · Android local network permission |
| 7 | Background downloads on Android |
| 8 | PiP on Windows (separate implementation) |
| 9 | — |
| 10 | Fingerprint versioning at scale |

---

## Spikes to run

Ordered by how much they block:

| # | Spike | Blocks | Effort |
|---|---|---|---|
| 1 | SAF folder pick, persisted, readable after reboot, on Android | Stage 1 | Days |
| 2 | MediaElement plays a `content://` URI directly | Stage 1 | Hours |
| 3 | Pitch preservation at 0.5x and 3x, both platforms | Stage 2 | Hours |
| 4 | `.srt` / `.vtt` rendering in MediaElement | Stage 2 | Hours |
| 5 | Position survives backgrounding and process death | Stage 2 | Hours |
| 6 | Unpackaged Windows build on a clean machine | Distribution | Hours |
| 7 | ASP.NET Core server reachable from another LAN device; firewall behaviour | Stage 6 | Days |
| 8 | Fingerprint match for the same course on two devices | Stage 5 | Hours |
| 9 | Export/import round-trip without data loss | Stage 5 | Hours |
| 10 | Background downloads with a foreground service on Android | Stage 7 | Days |

Spikes 1–5 should be run **before stage 1 begins**. They are hours-to-days of work, and each one invalidates a design assumption that would otherwise be discovered after code is built on top of it.

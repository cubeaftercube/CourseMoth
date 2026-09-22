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

**Status:** ✅ resolved

### Decision

**Modules become separate projects when, and only when, they need no platform.**

```text
Separate net10.0 class libraries:
    Core ✅ · Data ✅ · Parser · Tasks · Sync

Stay in the multi-targeted MAUI project:
    Media · parts of Downloads

Separate heads:
    WinUI · Droid · iOS · Mac
```

The criterion is platform dependency, not size. It was applied and it held: `CourseMoth.Core` and `CourseMoth.Data` are plain `net10.0` libraries with no MAUI reference, and `CourseMoth.Core.Tests` runs 132 tests with no platform target and no device.

`Tasks` and `Sync` are not separate projects yet, but their logic already lives in `Core` (`TaskEvaluator`, `StreakCalculator`) and is therefore covered by the same tests. Splitting them out would buy nothing today.

### What is still open

- **`src/` layout not adopted.** The projects are separated, but they still sit under `CourseMoth/` rather than `src/`. The move was attempted and blocked by file locks held by a running Visual Studio, and was abandoned rather than forced — restructuring the tree under an open IDE risks more than it gains. Worth doing on a clean checkout.
- **`Shared` was never created.** Its contents (stable keys, `course.json`) currently live in `Core`. Whether it earns its own project is a question for when a second consumer appears — the parser, most likely.
- **`CourseMoth.Parser` does not exist**, and that is the reason folder import does not work end to end. See [Index](Index.md#two-things-that-do-not-work).

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

## Third-party UI control suites

**Status:** ✅ decided — do not adopt

**Decision (2026-09-22): the project does not take a dependency on a commercial control suite.** The specific candidate was Syncfusion, via the `syncfusion/maui-ui-builder` agent skill that generates MAUI XAML over their controls. It was evaluated and rejected.

### Why

Syncfusion's licence is not merely restrictive, it is *incompatible with this project's licence by its own terms*. Their EULA's Open Source Project Terms forbid use of the controls in a project under **any copyleft licence, including GPL** — AGPLv3 is squarely covered. Using them in an open-source project requires a separate Master License Agreement, and under that agreement the project may only be MIT, Apache, or BSD.

Even setting AGPL aside, their terms forbid redistributing the binaries within an open-source project: every person who builds the code must obtain a Syncfusion licence independently. That would break [§4.1 Offline-first](Vision.md#41-offline-first) — "works fully without the internet" — by making a third-party account a build prerequisite.

Independently of the licence, a Community licence was not obtainable in this case — but the licence terms alone are what disqualify the dependency, and they would disqualify it for every contributor as well. The decision does not rest on the practical obstacle.

### The rule that came out of it

Any dependency added to this project must be **distributable under AGPLv3 by anyone, without registration, account, or per-user licence.** This is a hard constraint, not a preference — it follows directly from [Vision §4.5](Vision.md#45-open-code) and §4.1.

A permissively-licensed control library would be fine. A commercially-licensed one cannot be adopted here at all, however good it is.

### What was not rejected

The *idea* of generating MAUI UI code from a requirement is sound. Only the licensing model of this particular implementation is disqualified. A generator producing plain MAUI controls would be welcome.

---

## The Add-folder button does not respond to a mouse click

**Status:** 🔴 open — the Windows build is not usable until this is fixed

A real mouse click on "Add folder with courses" does nothing. No dialog, no status text, no error.
Verified by the user directly and reproduced independently.

### What is established

| Fact | How it was established |
|---|---|
| The button is enabled, visible and on screen | UI Automation: `IsEnabled=True`, `IsOffscreen=False` |
| The command handler runs when invoked **programmatically** | A temporary counter printed `entered AddFolderAsync N time(s)` after `InvokePattern.Invoke()` |
| The folder dialog **works** when the command runs | The user selected a folder and the status line showed the chosen path |
| The dialog returns a folder after ~3 s | Status text appeared between 1 s and 3 s after invocation |

### What is therefore **not** broken

The picker, the ViewModel, the command, and the DI graph. All four were suspected and all four were
cleared. The failure is on the **input path** — the click is not reaching the handler.

### What was tried

- **Replaced `Command="{Binding AddFolderCommand}"` with `Clicked="OnAddFolderClicked"`** in
  `HomePage.xaml`, with the handler calling the ViewModel method directly. Rationale: the page's
  `BindingContext` is a service provider passed down by `AppShell`, not the ViewModel
  ([CourseMothPage](../CourseMoth/CourseMoth/Pages/CourseMothPage.cs)), which makes a `Command`
  binding on it the most fragile link in the chain. **Applied, built clean, not verified** — the
  check needs a genuine mouse click, and the session ended before one could be made.

### What to try next

In rough order of likelihood:

1. **Hit-testing.** Something may be covering the button and swallowing the press. The button sits
   inside a `ScrollView` that is itself inside a `Grid`; check the sibling `ScrollView` that shows
   the loaded state, and the `ActivityIndicator` above it, for an invisible overlay.
2. **A `ScrollView` that cannot scroll.** If a `ScrollView` marks the press handled while deciding
   whether a drag is a scroll, a tap inside it never becomes a `Clicked`.
3. **The handler is reached but the exception is swallowed.** Re-check that an exception thrown in
   `OnAddFolderClicked` reaches the screen at all.

### The verification lesson

Three separate claims in this area were believed and were wrong. Recording them so the same mistake
is not repeated:

- A status line reading `Selected: <path>` was taken as proof the picker worked. An independent
  verification run showed the **same path came back on six invocations across three launches**,
  including runs where no dialog was ever seen. A plausible-looking string is not evidence.
- A build was judged to contain a fix by looking for a string in the wrong assembly — the picker
  lives in the WinUI head, and the string was searched for in `CourseMoth.dll`.
- A test was trusted to exercise a real click when it was exercising a programmatic invoke. The two
  take different paths through the UI and can disagree, which is exactly what happened here.

**The rule that follows: verify the specific thing being claimed.** A command that runs when invoked
is not a button that works when clicked.

---

## Slopwatch: one open finding

**Status:** 🟠 open, minor

`slopwatch analyze -d . --no-baseline --fail-on warning` reports exactly **one** issue across the
whole solution:

```
ViewModelBase.cs(50,9): error SW003: Empty catch block swallows exceptions without handling
    catch (OperationCanceledException)
    {
        // Navigating away is not an error and must not surface as one.
    }
```

The behaviour is correct — a cancelled load is not a failure and must not surface as one — but the
tool cannot tell an intentional empty catch from a careless one, and it is right not to guess. The
fix is to declare the intent rather than leave it implicit:

```csharp
[SlopwatchSuppress("SW003", "Cancellation is a navigation event, not a failure, and must not surface as an error.")]
```

Nothing else in the solution trips any rule: no disabled tests, no warning suppressions, no
project-level `NoWarn`, no arbitrary delays in tests, no CPM bypass.

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
| 0 | ✅ closed — modules became projects, see [above](#modules-and-projects) |
| **1** | 🔴 **the Add-folder button ignores a mouse click** · then the parser · Android file access (SAF spike) |
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

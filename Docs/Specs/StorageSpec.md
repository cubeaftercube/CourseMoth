# Storage Specification

Where course files live, how downloads work, how space is accounted for, and what happens when it runs out.

A large share of this document is platform constraints. Android's rules around file access are the single biggest source of friction in the whole project, and they are not negotiable by design decisions.

> **Related:** [SystemMap](../Maps/SystemMap.md) · [DomainMap](../Maps/DomainMap.md#32-coursemodule) · [UserFlowMap](../Maps/UserFlowMap.md#10-scenario-rescanning-a-folder) · [OpenQuestions](../OpenQuestions.md)

---

## 1. Two storage models

The user's decision in the original design discussions was "both approaches are needed", and this is what that means concretely. They are genuinely different and must not be blurred:

| | **Reference mode** | **Managed mode** |
|---|---|---|
| Where files are | The user's own folder | The app's private directory |
| Who put them there | The user | The app |
| The app can delete them | **Never without explicit consent** | Yes, as part of its own policies |
| Used for | Imported local folders, PC server shares | Courses downloaded for offline viewing |
| `Lesson.Availability` | `Local` | `Downloaded` |

A lesson can be in either mode at different times. The same course may be a local folder on the PC and a set of downloads on the phone.

**The distinction is enforced in the domain, not merely in the UI.** Deleting a course with `Availability = Local` lessons must not be able to touch those paths — this is a [Vision §4.3](../Vision.md#43-the-users-data-belongs-to-the-user) requirement, and it should be structurally impossible rather than a checked condition someone might forget.

---

## 2. Locations

### Managed (downloads)

```text
{AppDataDirectory}/courses/{courseId}/{lessonId}{extension}
{AppDataDirectory}/courses/{courseId}/{lessonId}.part      — in-flight download
{AppDataDirectory}/covers/{courseId}.jpg                   — cached covers
{AppDataDirectory}/subtitles/{lessonId}/{language}.srt
{AppDataDirectory}/coursemoth.db                            — database
```

Keyed by GUID, not by path or title. Titles change, paths break, GUIDs do not.

### Reference (imported)

Nothing is copied. The database stores a source identifier plus a relative path:

```text
LibrarySource.Type   = LocalFolder | Server | WebDav | ...
LibrarySource.Config = the provider-specific root (path, URL, ...)
Lesson.RelativePath  = "Module 1/01 Intro.mp4"
```

The provider reconstructs an absolute path or URI at access time. This is why `IFileSystemReader` exists ([ParserSpec §2](ParserSpec.md#2-input)) — paths are a provider concern, not a domain concern.

---

## 3. Platform constraints

### Windows

| Aspect | Detail |
|---|---|
| Minimum version | Windows 10, build 10.0.19041 |
| App data | `FileSystem.AppDataDirectory` → `%LOCALAPPDATA%\<app>\Data` |
| Arbitrary folder access | Works in both packaged and unpackaged builds |
| Distribution | Unpackaged x64 build from GitHub Releases is viable |

**Unpackaged publish:**

```bash
dotnet publish -f net10.0-windows10.0.19041.0 -c Release \
  -p:RuntimeIdentifierOverride=win-x64 \
  -p:WindowsPackageType=None \
  -p:WindowsAppSDKSelfContained=true
```

Note: on .NET 10 use portable RIDs (`win-x64`, not `win10-x64`).

Without `WindowsAppSDKSelfContained=true` the user must install the Windows App SDK runtime separately. A WinUI 3 app cannot be a single-file EXE — native dependencies ship as separate files, so the release artifact is a folder (or an archive of one).

Neither packaged nor unpackaged restricts writing to user folders or hosting a network server. The relevant constraint there is the **Windows firewall**, which prompts on first bind and needs an inbound rule for other devices to connect — this matters for stage 6, not here.

### Android

| Aspect | Detail |
|---|---|
| Minimum version | Android 10 (API 29) |
| App data | App-private internal storage, no permission needed |
| User folder access | Storage Access Framework only |
| Broad file access | `MANAGE_EXTERNAL_STORAGE` — **not permitted for this app** |
| Background downloads | Foreground service |
| Local network (future) | New permission arriving in Android 16/17 |

#### Why SAF is mandatory, not a preference

Google Play policy has restricted `MANAGE_EXTERNAL_STORAGE` since May 2021 and **still enforces it**. The permitted use cases are narrow — file managers, backup, antivirus, document management, on-device search, encryption, device migration. Media access is explicitly listed as **not** an allowed use.

> **A video player for personal course archives does not qualify.**

The user's stated plan is to distribute as an APK via GitHub Releases, and possibly through Google Play later if the project succeeds. That later goal determines the design now: **build on SAF from the start.** An APK-only sideload build *could* request broad storage permission, but that path leads to a rewrite at the exact moment the project is trying to grow, and to a Play Console declaration that would be rejected.

#### SAF details

```text
Intent.ActionOpenDocumentTree
ContentResolver.TakePersistableUriPermission(uri, FLAG_GRANT_READ_URI_PERMISSION)
  → access survives reboot
DocumentsContract.BuildChildDocumentsUriUsingTree(uri, docId)
  → enumerate children
```

Two failure modes worth designing around:

- **Reconstructing URIs by hand breaks.** A `content://` URI assembled from path fragments rather than obtained from the tree API throws `SecurityException: Permission Denial`. Every URI must come from `BuildChildDocumentsUriUsingTree`.
- **`ACTION_PICK` / `GET_CONTENT` URIs are not persistable.** Only `ACTION_OPEN_DOCUMENT_TREE` grants durable access. Using the wrong intent produces a permission that evaporates on restart.

#### Playback from SAF

ExoPlayer plays `content://` URIs directly, provided the read grant is held. Whether **MediaElement** passes the URI through correctly is a spike ([PlayerSpec §14](PlayerSpec.md#14-what-must-be-verified-by-spike)).

---

## 4. Download queue

### Model

One `DownloadJob` per **file**. Downloading a whole course is N jobs grouped in the UI.

```csharp
public enum DownloadState { Pending, Downloading, Paused, Completed, Failed, Canceled }
```

No "download a module" entity. It adds a second state machine that has to be kept consistent with the first, for no capability the UI cannot provide by grouping.

### Concurrency

```text
Max concurrent downloads     2 by default (configurable)
Wi-Fi only                   on by default
Retry on failure             3 attempts with exponential backoff
Retry delay base             5s, 10s, 20s
```

Two, not many: simultaneous downloads on a phone compete for bandwidth and battery, and the perceived speedup is minimal.

### Resumption

```text
HTTP Range request      → resume from the size of the .part file
Server without Range    → restart from zero
Size mismatch at end    → discard .part and retry
```

A `.part` file is **never** treated as playable. `Availability` becomes `Downloaded` only after the size check passes.

### Failure handling

| Failure | Behaviour |
|---|---|
| Network dropped | Back off, retry up to 3 times, then `Failed` |
| Source unreachable | Pause the queue, surface the problem once rather than per file |
| Out of space | Stop the queue, keep completed files, warn |
| File changed on the server | Discard partial, restart |
| User cancels | Delete `.part`, mark `Canceled` |

---

## 5. Space accounting

### Before a download starts

```text
1. Sum the sizes of all queued files
2. Compare against free space minus a safety margin (500 MB)
3. If the total will not fit → refuse before starting, not halfway
```

Refusing mid-download leaves the user with a half-populated course and no clear remedy. Refusing up front gives them a single decision: free space, or download less.

### Reporting

The user must always be able to see:

```text
Space used by downloaded courses
Number of courses stored offline
Per-course size
Free space on the device
```

`/api/storage/usage` aggregates from `Lesson.FileSizeBytes` where `Availability = Downloaded`. No separate running counter — counters drift, sums do not.

### The limit

```text
Storage limit            configurable, in GB
Warn at                  90% of the limit
On exceeding             prompt, do not auto-delete
```

If file sizes are unknown for some lessons (metadata not yet read), the estimate is marked as approximate. Presenting a precise-looking number that is wrong is worse than presenting an estimate that admits it.

---

## 6. Cleanup policies

All off by default. Each is a decision the user makes, not the app.

| Policy | Meaning | Risk if enabled |
|---|---|---|
| Delete watched lessons | Remove downloads once `IsCompleted` | User may want to rewatch |
| Keep only the last N lessons | Rolling window within a course | Lessons vanish unexpectedly |
| Delete downloads older than N days | Age-based | Slow-course learners lose material |
| Auto-download next lesson | Pre-fetch | Uses data without being asked |

**None of these may ever touch `Availability = Local` files.** Cleanup operates exclusively inside the app-managed directory.

Cleanup runs when the storage limit is approached or on demand — not on a background timer that acts while the user is unaware.

---

## 7. Deletion

### Deleting a download

```text
Remove the file
Availability  Downloaded → Remote
WatchState    untouched
```

Progress is never affected by deleting a file.

### Deleting a course

Presented as explicit checkboxes, with safe defaults:

```text
Delete "Программирование на C#"?
[ ] Metadata and progress          (default: checked)
[ ] Downloaded video files         (default: checked)
[ ] Source files in the folder     (default: UNCHECKED — disabled for Local lessons)
```

**"Source files in the folder" must be structurally impossible to enable for `Local` lessons.** The app did not create those files and does not own them. This is not a checkbox the code trusts — the deletion path for `Local` lessons should not exist at all.

Deleting metadata while keeping files leaves the files as ordinary files the user still owns, which is a legitimate outcome.

### Backup before delete

Optional, off by default: write a JSON snapshot next to the database before removing a course. Cheap insurance for an irreversible action.

---

## 8. Settings

```text
Storage limit                        5 GB
Warn at                              90% of the limit
Download over Wi-Fi only             on
Max concurrent downloads             2
Auto-download next lesson            off
Delete watched lessons               off
Keep only the last N lessons         off
Delete downloads older than N days   off
Backup before deleting a course      off
```

---

## 9. Sources

Storage connects directly to the source abstraction. Full details in [ServerApiSpec](ServerApiSpec.md); the storage-relevant part:

```csharp
public interface ILibrarySource
{
    Task<bool> TestConnectionAsync(CancellationToken ct);
    Task<IReadOnlyList<RemoteEntry>> ListAsync(string path, CancellationToken ct);
    Task<Stream> OpenReadAsync(string path, CancellationToken ct);
    Task DownloadAsync(string path, string destination, IProgress<long> progress, CancellationToken ct);
    Task<RemoteEntry?> GetInfoAsync(string path, CancellationToken ct);
}
```

`Server` (our own PC server) implements this the same way WebDAV will. The PC is not a special case in the code — if it becomes one, the abstraction has failed.

---

## 10. Open questions

- **Android 16/17 local network permission.** Android 16 introduces Local Network Protection as opt-in; Android 17 makes an `ACCESS_LOCAL_NETWORK` runtime permission mandatory. This affects stage 6, not storage, but it must be designed for now — a permission request cannot be retrofitted gracefully. `NEARBY_WIFI_DEVICES` temporarily satisfies the requirement.
- **`usesCleartextTraffic`.** Plain-HTTP communication with the PC server is blocked by default since Android 9. Serving over plain HTTP on the LAN will need `usesCleartextTraffic="true"` or a scoped `network_security_config`. Decide between plain HTTP with an exception and self-signed HTTPS during stage 6.
- **SD card storage on Android.** Whether to allow managed downloads on removable storage. `FileSystem.AppDataDirectory` does not cover it; SAF does not give an app-private directory. Deferred.
- **Multiple storage locations.** Whether a user should be able to designate an external drive for downloads on Windows. Deferred.

---

## 11. What must be verified by spike

| # | Question | Why it matters |
|---|---|---|
| 1 | SAF folder pick, persisted, readable after reboot | The whole Android import path |
| 2 | Can MediaElement play a `content://` URI directly? | If not, importing on Android is a different design |
| 3 | Do background downloads survive Android's process management with a foreground service? | Every course download on a phone |
| 4 | Does the unpackaged Windows build run on a clean machine? | Distribution viability |
| 5 | Does a server bind trigger a recoverable firewall prompt? | Stage 6 reachability |

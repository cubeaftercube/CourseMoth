# Player Specification

The player is the heart of the application. Everything else exists so that a lesson can be watched comfortably and its progress remembered.

> **Related:** [SystemMap](../Maps/SystemMap.md) · [DomainMap](../Maps/DomainMap.md) · [UXMap](../Maps/UXMap.md#35-player) · [OpenQuestions](../OpenQuestions.md)

---

## 1. Scope

**In scope:** playback, position memory, speed, subtitles, picture-in-picture, fullscreen, next/previous lesson, error handling, and the boundary between the player and progress tracking.

**Out of scope:** parsing, downloading, streaming from a server (see [StorageSpec](StorageSpec.md) and [ServerApiSpec](ServerApiSpec.md)).

---

## 2. Engine choice

### Decision

**Use `CommunityToolkit.Maui.MediaElement`.**

It is not a third-party engine — it is a wrapper over the platform-native players:

| Platform | Underlying engine |
|---|---|
| Android | ExoPlayer / Media3 |
| Windows | WinUI `MediaPlayerElement` |
| iOS / macOS | AVPlayer |

That matters: choosing MediaElement does **not** mean giving up ExoPlayer on Android. It means getting ExoPlayer through a supported cross-platform abstraction.

### What it provides

- Playback, pause, seek (`SeekTo` / `Position`)
- Playback speed
- Local files, `http(s)` URLs, `embed://` assets
- HLS / DASH on Android and Windows
- Picture-in-Picture on Android 8+ and iOS 14+
- Background audio with a foreground service on Android 14+

### Correspondence with our requirements

| Requirement | MediaElement | Status |
|---|---|---|
| Playback, pause, seek | Yes | ✅ |
| Position memory | Yes | ✅ |
| Next / previous lesson | Application-level | ✅ |
| Fullscreen | Application-level | ✅ |
| Playback speed | Yes | ⚠️ Verify pitch behaviour |
| Subtitles `.srt` / `.vtt` | Unconfirmed | ⚠️ **Unverified** |
| Picture-in-Picture | Android / iOS yes | ⚠️ Windows needs platform work |

### Known hazards

| Hazard | Consequence |
|---|---|
| **Trimmer regression on .NET 10 Android** ([CommunityToolkit/Maui#3114](https://github.com/CommunityToolkit/Maui/issues/3114)) — constructors are stripped | Set `TrimMode=partial` for the Android head |
| Foreground-service permission denials on Android 14+ ([#3103](https://github.com/CommunityToolkit/Maui/issues/3103)) | Background audio needs explicit handling |
| Codec gaps on low-end devices (no HEVC / AV1) | Prefer H.264 content; fall back to the system player |

### Rejected alternatives

| Option | Why not |
|---|---|
| **LibVLCSharp** | Best codec coverage, but its MAUI `MediaPlayerElement` supports iOS / Android / UWP only — **there is no usable WinUI story**. Since Windows is a first-class target, this is disqualifying. |
| **Raw ExoPlayer + WinUI `MediaPlayerElement` via handlers** | Maximum control, but two separate codebases to maintain. Only justified for a specific feature MediaElement cannot deliver. |
| **`Plugin.Maui.Audio`** | Audio only — irrelevant for a video player. |

> **This reverses a plan from the original design discussions**, which treated LibVLCSharp as the fallback if MediaElement proved insufficient. That fallback does not exist for Windows. If MediaElement turns out to be insufficient, the realistic path is platform handlers, not LibVLC — and that is a much bigger commitment. **Verify the two unconfirmed behaviours (§5 and §7) before building on top of them.**

### Abstraction requirement

The engine must stay replaceable, because the above is a real risk, not a formality. No `MediaElement` type may appear outside `CourseMoth.Media`.

```csharp
public interface IMediaPlayer
{
    Task LoadAsync(MediaSource source, CancellationToken ct);
    Task PlayAsync();
    Task PauseAsync();
    Task SeekAsync(TimeSpan position);
    Task SetSpeedAsync(double speed);

    IObservable<PlaybackEvent> Events { get; }
}
```

---

## 3. Media source

```csharp
public abstract record MediaSource
{
    public sealed record LocalFile(string Path)      : MediaSource;
    public sealed record SafUri(string Uri)          : MediaSource;   // Android content://
    public sealed record RemoteStream(Uri Url)       : MediaSource;
    public sealed record DownloadedFile(string Path) : MediaSource;   // app sandbox
}
```

The player does not decide *which* source to use for a lesson — that is `Core`'s call based on `Lesson.Availability` and the user's offline preference. The player receives a resolved source and plays it.

**Why a distinct `SafUri`.** On Android a picked folder yields `content://` URIs, not file paths. Passing a `content://` URI through a `Path`-shaped API invites `SecurityException` at runtime. Making it a separate case forces every call site to handle it.

---

## 4. Playback events

`Media` reports what happened; it never writes progress.

```csharp
public abstract record PlaybackEvent
{
    public sealed record PositionChanged(TimeSpan Position)  : PlaybackEvent;
    public sealed record DurationKnown(TimeSpan Duration)    : PlaybackEvent;
    public sealed record PlaybackStarted                     : PlaybackEvent;
    public sealed record PlaybackPaused                      : PlaybackEvent;
    public sealed record PlaybackEnded                       : PlaybackEvent;
    public sealed record PlaybackFailed(string Reason)       : PlaybackEvent;
}
```

`Core.WatchStateService` subscribes to these events and decides what they mean. This split is what makes the completion rules testable without a real video file.

---

## 5. Speed

**Range:** 0.5x to 3x, as presets:

```text
0.5 · 0.75 · 1.0 · 1.25 · 1.5 · 1.75 · 2.0 · 2.5 · 3.0
```

Presets only. Free-form speed entry is not in scope.

### The unverified part

> **⚠️ Pitch preservation is unconfirmed.**
>
> For course content this is not a nicety — a lecture at 2x that sounds like a chipmunk is unusable. Whether MediaElement preserves pitch on both Android and Windows could not be confirmed from documentation. Community sources treat it as unverified.

**This is a spike, not an assumption.** Test on real devices, both platforms, at 0.5x and 3x, with actual speech audio, before building the speed UI. If pitch correction is absent, the options are: a platform handler override, or dropping speed as an MVP feature.

### Per-course memory

Speed is remembered **per course**, not globally. A user watching a foreign-language course at 0.75x and a technical one at 1.5x should not have to reset it every lesson.

Default speed for new courses comes from settings.

---

## 6. Position memory

### When the position is saved

```text
Every 5 seconds during playback
On pause
On leaving the player
On the app going to background
On switching to the next/previous lesson
```

**Not only on close.** The app gets killed by the OS, phones run out of battery, and users force-quit. A position that is only saved on a clean exit is a position that gets lost regularly.

### Save order — a correctness detail

On `PlaybackEnded`, some engines reset `Position` to zero. If the position is read *after* the ended event fires, the saved progress may be `0:00` rather than the end of the video.

**Therefore:** capture the position **before** the end-of-media event, and treat "reached the end" as an explicit completion signal rather than inferring it from position.

### Resume

Resuming seeks to the stored `PositionMs`. Two guards:

| Condition | Behaviour |
|---|---|
| Position is within the last 5 seconds | Start from the beginning, not from 99% |
| Position is under 5 seconds | Start from the beginning |

Starting a lesson two seconds before the credits is a nuisance users notice immediately.

### Clearing

When a lesson is completed, `PositionMs` is **not** reset. If the user rewatches, playback starts from the beginning because the lesson is already complete — but the stored position stays available for reference. Resetting it would lose information for no benefit.

---

## 7. Completion interaction

The completion rule itself lives in [DomainMap](../Maps/DomainMap.md#34-watchstate--the-center-of-the-system) and [TasksStreaksSpec](TasksStreaksSpec.md). The player's part is narrow:

```text
PlaybackEnded              → lesson is complete, unconditionally
position / duration ≥ 0.90 → lesson is complete
otherwise                  → not complete
```

The threshold is per-course (`Course.CompletionThreshold`), defaulting to 0.9.

### Manual marking takes precedence

The player must surface manual marking without second-guessing it:

- **"Mark complete"** → `CompletionSource = Manual`
- **"Mark incomplete"** → `IsCompleted = false`, position kept, and **threshold auto-completion is suppressed for the rest of the session**.

That last clause is the whole point. Without it, a user marks a lesson incomplete, plays two more seconds, crosses 90%, and the checkmark returns — making "incomplete" impossible to hold.

---

## 8. Subtitles

### Formats

```text
.srt   ⚠️ unverified in MediaElement
.vtt   ⚠️ unverified in MediaElement
.ass   later
.ssa   later
```

> **⚠️ Subtitle rendering support in MediaElement could not be confirmed.** Track selection and rendering need a device test before the subtitle UI is designed. If unsupported, the fallback is a platform handler — or rendering subtitles in an overlay ourselves.

### Expected behaviour

- Tracks found next to the video are attached automatically ([ParserSpec §9](ParserSpec.md#9-companion-files))
- Multiple tracks (`.ru.srt`, `.en.srt`) appear as a selectable list
- The user can turn subtitles off
- The user can attach a file manually
- The chosen track is remembered per course

### Encoding

`.srt` files in the wild are frequently **not UTF-8** — Windows-1251 is common in Russian-language course archives. A track that renders as mojibake is worse than no track.

**Rule:** detect encoding on load (BOM, then heuristic), and if the result contains replacement characters, surface a visible warning rather than rendering garbage silently. Provide a manual encoding override in the player.

---

## 9. Picture-in-Picture

### Android

Real PiP. `PictureInPictureParams` / `Activity.SetPictureInPictureParams`, API 26+, requires `SupportsPictureInPicture=true` on the activity and handling `OnPictureInPictureModeChanged`.

MediaElement exposes PiP on Android 8+. If it proves insufficient, `Shiny.Maui.Controls` offers `TryEnterPictureInPictureAsync()`.

### Windows

**There is no equivalent of Android PiP on Windows.** The realistic approach is `AppWindow` in one of two forms:

| Approach | Behaviour |
|---|---|
| `AppWindowPresenterKind.CompactOverlay` | True compact overlay, but no topmost control, no maximize/minimize, no transparency |
| `OverlappedPresenter` with `IsAlwaysOnTop` and `SetBorderAndTitleBar(false, false)` | More control, borderless always-on-top window |

Recommended for Windows: the `OverlappedPresenter` route, since `CompactOverlay` does not expose topmost without a `SetWindowPos` P/Invoke.

`AppWindow.IsShownInSwitchers = false` hides the PiP window from the taskbar and Alt-Tab.

> **This is not one feature across two platforms.** Android PiP and a Windows always-on-top window are different implementations with different capabilities, and they will need different UI affordances. Budget for them separately.

### Behaviour

- PiP engages on leaving the app during playback (setting-controlled)
- Audio continues
- Restoring returns to the same position
- PiP is not available on the lesson list or course page, only in the player

---

## 10. Fullscreen

- Landscape on phones, filling the screen
- Controls auto-hide after ~3 seconds of no interaction
- Tap shows controls; tap again hides
- Back gesture exits fullscreen first, then exits the player
- On Windows, a borderless window state rather than a separate window

Orientation handling on Android — whether to force landscape or defer to the system — is an open question to settle during stage 2.

---

## 11. Errors

Playback fails for mundane reasons: a missing file, an unsupported codec, a dropped network stream.

```csharp
public sealed record PlaybackFailed(string Reason) : PlaybackEvent;
```

| Cause | Message shown | Offered action |
|---|---|---|
| File missing | "The file could not be found" | Rescan the course |
| Codec unsupported | "This format is not supported on this device" | Open in the system player |
| Network stream dropped | "Connection lost" | Retry, or download the lesson |
| Permission denied (Android) | "No access to this folder" | Re-pick the folder |
| Unknown | The raw error, plus a "copy details" button | Open an issue |

**An error must never be a silent black screen.** The player is where a user spends their time; a failure it cannot explain is the most frustrating thing an app of this kind can do.

---

## 12. Settings

Per-player, in the settings screen:

```text
Default completion threshold        90%
Autoplay next lesson                on
Default playback speed              1.0x
Enable subtitles automatically      on
Remember speed per course           on
Enable PiP when leaving the app     on
Skip intro                          —  (stage 8)
```

---

## 13. Deferred

```text
Skip intro / outro                  stage 8
Smart chapters                      not planned
Video annotations / notes           not planned
Picture-in-picture on Windows       stage 8, separate implementation
Audio-only mode                     not planned
Cast / AirPlay                      not planned
```

---

## 14. What must be verified by spike

Before stage 2 begins, all four answers are needed:

| # | Question | If the answer is bad |
|---|---|---|
| 1 | Does speed change preserve pitch on Android **and** Windows? | Platform handler, or drop speed from MVP |
| 2 | Do `.srt` / `.vtt` render via MediaElement on both platforms? | Platform handler, or overlay rendering |
| 3 | Does MediaElement play a SAF `content://` URI directly on Android? | Resolve to a file descriptor through a handler |
| 4 | Does position survive app backgrounding and process death? | Save on a timer and on lifecycle events more aggressively |

Answering these four is a few days of work. Building stage 2 on unverified assumptions is weeks of rework.

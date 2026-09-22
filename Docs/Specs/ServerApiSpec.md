# Server API Specification

The PC as the core of the library: serving courses, streaming video, and syncing progress to other devices.

> **Related:** [SystemMap](../Maps/SystemMap.md) · [SyncSpec](SyncSpec.md) · [StorageSpec](StorageSpec.md) · [OpenQuestions](../OpenQuestions.md)

---

## 1. The problem this solves

A user's course archive lives on a PC. They want to watch it on a phone, lying on the couch, and they want one progress across both.

Two things are needed:

1. **Content transfer** — the phone gets the courses it wants, either downloaded or streamed.
2. **Progress sync** — both devices agree on what has been watched.

The server is optional. Its absence must break nothing ([Vision §4.4](../Vision.md#44-local-core)).

---

## 2. Architecture — an unresolved decision

> **⚠️ This is the most consequential open question in the project.**

Running `Kestrel` **inside** a .NET MAUI app is **not officially supported** on Android or iOS. The `Microsoft.AspNetCore.App` framework reference is unsupported on `net*-android` and `net*-ios`. Community workarounds exist ([JamesNK/aspnetcore-maui](https://github.com/JamesNK/aspnetcore-maui), [dotnet/aspnetcore#35077](https://github.com/dotnet/aspnetcore/issues/35077)) but require MSBuild hacks to ship ASP.NET assemblies, and are untested against .NET 10 Android trimming.

Windows (WinUI 3) is substantially less problematic — the same hacks are not required.

### Three options

| Option | How it works | Cost |
|---|---|---|
| **A. Standalone server** | A small `ASP.NET Core` process shipped alongside the app, started and stopped by the app | Separate process to manage, but a supported, portable stack |
| **B. Embedded Kestrel** | `WebApplication` hosted in-process inside MAUI | Community hacks; Android trimming risk; unsupported |
| **C. Windows-only server** | The server exists only in the Windows build; Android only ever acts as a client | Simplest, and matches the actual use case |

### Recommendation: **C, falling back to A**

The server's entire purpose is "the PC serves the phone". The phone never needs to serve anything — its downloads are its own business. Option C eliminates the unsupported code path entirely, and it is the only option where the risky platform is not involved.

The only scenario C does not cover is phone-to-phone transfer, which is a genuine but rare case. Option A covers it later if demand appears.

**Do not start stage 6 with option B.** The downside is discovering trimming breaks three weeks in, on the platform where debugging is worst.

### Storage note

The server reads the library from the **same database** as the app. It must not open a second writer against SQLite. Either:

- the server runs in-process (option B) and shares the connection, or
- the app owns the database and the server reads through it (options A and C).

---

## 3. Endpoints

### Health and discovery

```http
GET /api/health
→ 200 { "status": "ok", "version": "0.1.0", "deviceName": "<device name>" }
```

### Library

```http
GET /api/library/courses
→ 200 [{ id, title, author, category, status, progress, lessonCount, completedCount, coverUrl }]

GET /api/courses/{courseId}
→ 200 { id, title, author, description, category, coverUrl, modules: [...] }

GET /api/courses/{courseId}/structure
→ 200 { modules: [{ id, title, order, lessons: [{ id, title, order, duration, sizeBytes, availability }] }] }
```

### Files

```http
GET /api/files/{lessonId}/info
→ 200 { lessonId, name, sizeBytes, contentType, modifiedAt, etag }

GET /api/files/{lessonId}/stream
→ 206 Partial Content, supports Range
→ 200 OK when no Range header is present

GET /api/files/{lessonId}/download
→ 200 OK, Content-Disposition: attachment
```

`Range` support is **mandatory**, not optional. Without it, seeking in a streamed video is impossible, and the feature is useless.

**Implementation note:** in ASP.NET Core this is `Results.File(path, enableRangeProcessing: true)` or `PhysicalFileResult`. It must be explicitly enabled — the default does not do it.

### Sync

```http
POST /api/sync/push
← { deviceId, changes: [...] }
→ 200 { applied: n, conflicts: [...] }

GET /api/sync/pull/{sinceSequence}
→ 200 { changes: [...], latestSequence: n }
```

`sinceSequence` comes from `ChangeEntry.Sequence` ([SyncSpec §2](SyncSpec.md#2-versioning)) and is what makes incremental sync possible.

### Pairing

```http
POST /api/pairing/start
→ 200 { sessionId, secret, expiresAt, qrPayload }

POST /api/pairing/confirm
← { sessionId, secret, deviceName, deviceId }
→ 200 { accessToken, refreshToken, deviceId }

GET /api/pairing/status/{sessionId}
→ 200 { status: "pending" | "confirmed" | "expired" }
```

### Devices

```http
GET    /api/devices
→ 200 [{ deviceId, name, lastSeenAt, pairedAt }]

DELETE /api/devices/{deviceId}
→ 204, token revoked
```

A user must be able to see every paired device and revoke any of them. A sync feature that cannot be audited is a sync feature that will not be trusted.

---

## 4. Authentication

```text
Authorization: Bearer <access-token>
```

| Token | Lifetime | Storage |
|---|---|---|
| Access token | 1 hour | Memory |
| Refresh token | Long-lived, revocable | Secure storage |
| Pairing session | 5 minutes, single use | Memory |

### Why streaming needs temporary signed URLs

Media players — especially on Android — generally do not send custom `Authorization` headers. A `MediaSource` created from a URL has no hook for one.

Therefore, URLs for media are signed instead:

```http
GET /api/files/{lessonId}/stream?token=<signed>&expires=<unix>
```

```text
Signature   HMAC-SHA256 over (lessonId, expires, deviceId)
Lifetime    5 minutes, renewed on seek
Binding     Tied to the requesting device
```

The signature must cover the expiry and the device, not just the file — otherwise a leaked URL is a permanent grant.

---

## 5. Access modes

The server is not simply on or off. The user controls what it exposes:

```text
Server enabled                  master switch
Local network only              never expose beyond the LAN
Allow download                  files can be transferred
Allow streaming                 files can be played without transfer
Allow sync                      progress can be exchanged
Show in discovery               respond to mDNS/UDP probes
```

The app must show plainly when the server is running:

```text
Server active
Address: 192.168.1.10:8443
Paired devices: 2
Last request: 3 minutes ago
```

A network service running invisibly in the background is exactly the kind of thing users are right not to trust.

---

## 6. Discovery

| Method | Reliability | Complexity |
|---|---|---|
| **QR code** | High | Low |
| Manual IP entry | High | Lowest |
| mDNS / DNS-SD | Medium | Medium |
| UDP broadcast | Medium | Medium |

**Start with QR.** It carries everything needed — address, port, session, secret — in one scan, and it works regardless of network topology. Anyone who can run a network scanner can find an mDNS responder, but not everyone can, and mDNS is frequently broken by consumer routers and AP isolation.

Manual IP entry exists as a fallback for users whose camera does not work.

---

## 7. Platform constraints

### Windows

| Constraint | Handling |
|---|---|
| Firewall prompt on first bind | Expected; the user must allow inbound. A failed prompt makes the server unreachable with no visible error. |
| Reachability from LAN | Depends on the network profile — public networks block inbound by default |
| MSIX vs unpackaged | Neither restricts binding. Packaged apps need a network capability declared; unpackaged apps do not. |
| Background operation | Normal desktop process; no restriction |

**The firewall prompt is the most likely reason the feature "does not work", and it produces no error the user can see.** Detect the situation — server is listening but no connection ever arrives — and tell the user to check the firewall rather than leaving them to guess.

### Android (when it acts as a client)

| Constraint | Handling |
|---|---|
| **Local network permission** | Android 16 introduces Local Network Protection (opt-in); **Android 17 makes `ACCESS_LOCAL_NETWORK` a mandatory runtime permission**. `NEARBY_WIFI_DEVICES` temporarily satisfies it. |
| Cleartext HTTP blocked since Android 9 | Plain HTTP on the LAN needs `usesCleartextTraffic="true"` or a scoped `network_security_config` |
| Background network access restricted from Android 15 | Foreground service or `WorkManager` for transfers outside the foreground |
| Trimming | ASP.NET assemblies must survive; see §2 |

The local-network permission is a genuine scheduling constraint: it cannot be retrofitted gracefully onto a shipped pairing flow, so the design should account for it now even though stage 6 is far off.

---

## 8. Transport security

| Scenario | Transport |
|---|---|
| LAN, `Allow streaming` off | Plain HTTP, acceptable |
| LAN, device pairing | HTTP with a one-time secret over the local network |
| Anything crossing the internet | **HTTPS required** |

A self-signed certificate is the pragmatic option for a home server. It requires either a certificate trust prompt on first connect or a documented exception — both are friction, and both are better than plaintext over the internet.

The user's choice of transport is their own, but the app must not silently send data over the open internet unencrypted.

---

## 9. Settings

```text
Enable server                        off
Port                                 8443
Bind address                         all interfaces / LAN only
Allow download                       on
Allow streaming                      on
Allow sync                           on
Require pairing for new devices      on
Session timeout                      30 minutes
```

---

## 10. Deferred

```text
HTTPS with automatic certificate generation
Wake-on-LAN
Remote access beyond the LAN (relay, VPN guidance)
Multi-user libraries
Transcoding for bandwidth-limited clients
```

---

## 11. Open questions

- **Option A vs C.** Whether a standalone server process is warranted, or Windows-only is sufficient for the foreseeable future. Decide before stage 6; affects packaging significantly.
- **Port conflict.** 8443 may already be in use. Whether to fall back to an ephemeral port and encode it in the QR, or require a fixed port for firewall rules. Fixed is friendlier to firewall rules; ephemeral is friendlier to first run.
- **Server on Windows without the app open.** Whether the server should survive the app being closed, or be a tray-resident process. Not planned, but users will ask.
- **mDNS naming.** `coursemoth.local` is convenient and clashes on networks with several users. Decide whether to append a suffix.

---

## 12. What must be verified by spike

| # | Question | Why |
|---|---|---|
| 1 | Does an ASP.NET Core server bind and become reachable from another device on the LAN? | The whole feature |
| 2 | Does `Range` streaming work from a media player without auth headers? | Seeking; if not, the streaming feature is broken |
| 3 | Does the Windows firewall prompt appear, and can the user recover if they deny it? | The most common silent failure |
| 4 | Can the Android client reach the server on the same network given cleartext restrictions? | Pairing |
| 5 | (Option A only) Can a standalone server process be bundled and launched from an unpackaged MAUI build? | Only if A is chosen |

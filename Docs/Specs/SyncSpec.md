# Sync Specification

Synchronizing learning progress across devices.

This is potentially the most complex part of the project, and the easiest to get subtly wrong. The complexity does not come from moving bytes — it comes from deciding what the bytes *mean* when two devices disagree.

> **Related:** [DomainMap](../Maps/DomainMap.md#7-course-identity--the-key-to-synchronization) · [UserFlowMap](../Maps/UserFlowMap.md#7-scenario-importing-progress-onto-another-device-via-file) · [ServerApiSpec](ServerApiSpec.md) · [OpenQuestions](../OpenQuestions.md)

---

## 1. What syncs and what does not

### Syncs

```text
Courses (metadata)        Modules              Lessons (metadata)
WatchStates               Categories           Tags
LearningTasks             LearningActivities   Favorites
User settings (optional)
```

### Does not sync

| Excluded | Why |
|---|---|
| **Video files** | Enormous, and they already exist on each device or can be downloaded |
| Device-local paths | `D:\Курсы` on the PC is `content://...` on the phone |
| Downloaded-file state | A file being downloaded here says nothing about there |
| Server/session tokens | Device-specific by definition |
| Player engine settings | Meaningless across platforms |

**The organizing principle: progress travels separately from video.** A course may exist on a device with no files at all, holding only progress. This is not a degenerate case — it is the normal state between an export and a folder selection, and the data model must support it ([UserFlowMap §7](../Maps/UserFlowMap.md#7-scenario-importing-progress-onto-another-device-via-file)).

---

## 2. Versioning

Every synced entity carries:

```csharp
public class SyncEntityMetadata
{
    public Guid     EntityId   { get; set; }
    public string   EntityType { get; set; }   // "Course", "WatchState", ...
    public long     Version    { get; set; }
    public DateTime UpdatedAt  { get; set; }
    public Guid     DeviceId   { get; set; }
    public bool     IsDeleted  { get; set; }   // tombstone
}
```

`Version` is a **locally monotonic counter**, incremented on every edit of that entity. It is not a timestamp and not globally ordered — it only orders edits *within one device's history*.

### Change log

For efficient deltas, a second table records what changed:

```csharp
public class ChangeEntry
{
    public long   Sequence   { get; set; }   // auto-increment, per device
    public string EntityType { get; set; }
    public Guid   EntityId   { get; set; }
    public long   Version    { get; set; }
    public DateTime ChangedAt { get; set; }
}
```

`ChangeEntry.Sequence` lets a peer ask "what changed since sequence N". Without it, every sync is a full comparison of every row on both sides.

### Why not vector clocks or Lamport timestamps

They matter when concurrent edits to the same entity are routine. Here, the overwhelming majority of entities are edited on one device at a time — a user watches a course on one device, then another. The genuinely concurrent case is rare enough that asking the user is acceptable, and asking is what the design already requires.

A simpler clock means a simpler merge, and merge bugs are the expensive kind.

---

## 3. Merge algorithm

```text
For each entity in the remote set:
    local = find(EntityId)

    if local is null
        → apply remote

    else if remote.Version > local.Version
        → apply remote

    else if remote.Version < local.Version
        → keep local

    else  (equal versions)
        → identical, no action
```

Applying a remote entity writes it and sets its version to the remote version — not `local + 1`. Version numbering is per-origin, and re-incrementing on apply breaks the comparison for every subsequent sync.

### Conflicts

A conflict is **the same entity changed on both devices since the last common sync**. The strict version comparison above resolves non-conflicting cases silently; conflicts are detected by comparing against the last-synced version recorded per peer.

When a conflict is found:

```text
WatchState      → always ask the user
Everything else → last write wins by UpdatedAt, but log the resolution
```

Only `WatchState` genuinely needs to ask. It is the only entity where a "wrong" automatic answer loses real information — how far a lesson was watched. Category names and course titles are cheap to fix manually and are not worth interrupting the user over.

### Conflict dialog

```text
Lesson progress differs

This device:   watched to 10:00
Other device:  watched to 25:00

( ) Keep this device
( ) Use the other device
( ) Take the greater progress
```

**Default: ask.** An automatic "always take the greater progress" mode exists as a setting, but must be enabled deliberately. Silent automation that discards a user's progress is worse than an extra question.

---

## 4. Course identity

The hardest problem in sync, and the one that determines whether the whole feature works.

One course arrives on two devices by different routes: imported from a folder on the PC, downloaded from the server on the phone, or restored from an export file. The app must recognize these as one course, or progress will not merge and the library will fill with duplicates.

### Matching, in priority order

```text
1. course.json id      → exact match, no ambiguity
2. Structure fingerprint → SHA-256 of normalized structure (ParserSpec §13)
3. StableKey           → fallback, human-readable
```

### Rules

| Situation | Action |
|---|---|
| Exact fingerprint match | Offer to merge progress |
| StableKey match, different fingerprint | Ask the user |
| Title match only | **Never merge automatically** |

Merging on folder name alone is wrong: "Module 1" exists in every course ever made.

### When nothing matches

An imported snapshot may reference courses the device has never seen. They are created **without files**:

```text
Course exists, Status = InProgress, Progress = 62%
All lessons: Availability = Missing
```

The library shows them with a "files not found" marker and an offer to point at a folder. Once pointed, lessons rebind by relative path and progress is already correct.

This is why identity is based on structure rather than paths, and why `Availability` is a property of a lesson rather than an assumption.

---

## 5. Deletion and tombstones

Deleting an entity sets `IsDeleted = true` rather than removing the row. A hard delete is indistinguishable from "the other device has not seen this yet", and the entity would be resurrected on the next sync.

```text
Delete on A  → tombstone, Version++
Sync A → B   → B also tombstones
Permanent removal from both after the retention window
```

### Retention

```text
Tombstone retention    180 days (configurable)
```

Long, because a device may be offline for months. A phone left in a drawer for six months must not resurrect a course the user deleted from the PC in the meantime. A shorter window trades storage for correctness; tombstones are tiny, so storage loses.

### Edge case: delete then recreate

If an entity is deleted and a new one created with the same identity, the tombstone wins and the recreation is lost. Mitigated by using new GUIDs on recreate and matching `Course` by fingerprint, which is checked after tombstone resolution.

---

## 6. Transport channels

Four ways progress moves, ordered by complexity:

### 6.1 Export / import file (stage 5)

```text
Export  → JSON containing all synced entities
Import  → merge by the algorithm in §3
```

Simplest and most reliable. No network, no server, no pairing. **This is the first thing built and the fallback when everything else fails.**

### 6.2 Cloud file (stage 10)

```text
/sync/progress.json     or     /sync/progress.enc
```

Each device downloads the file, merges locally, uploads its own state if newer. Works when devices are never online together.

Requires conflict handling, because two devices may upload different states before seeing each other's.

### 6.3 Local server (stage 6)

`POST /api/sync/push` and `GET /api/sync/pull/{sinceSequence}` against the PC. Fast, no cloud, and the PC is likely to be on whenever the phone is at home. Full details in [ServerApiSpec](ServerApiSpec.md).

### 6.4 Pairing code / QR

**Not a transport.** QR carries connection details and a secret; the actual transfer happens over the local network via §6.3.

Conflating the two is a common design mistake — the code looks like the sync mechanism but is only a bootstrap for it.

---

## 7. Export format

```json
{
  "schemaVersion": 1,
  "exportedAt": "2026-09-22T12:00:00Z",
  "deviceId": "…",
  "deviceName": "PC",
  "fingerprintVersion": 1,
  "courses": [],
  "modules": [],
  "lessons": [],
  "watchStates": [],
  "categories": [],
  "tags": [],
  "tasks": [],
  "activities": []
}
```

### Requirements

| Requirement | Reason |
|---|---|
| `schemaVersion` is mandatory | Old exports must stay readable |
| `fingerprintVersion` included | Fingerprints are only comparable within one algorithm version |
| `deviceName` is human-readable | Shown in conflict dialogs |
| Courses export **without** paths | Paths are meaningless on another device |
| Lessons export `RelativePath` | Needed to rebind files later |
| Format is readable JSON | [Vision §4.3](../Vision.md#43-the-users-data-belongs-to-the-user) — the user must be able to inspect their data |

**Forward compatibility rule:** an importer must ignore unknown fields rather than failing. A newer export must degrade gracefully in an older app instead of refusing to load.

### Export scope

```text
Full export       everything
Course export     one course with its progress
Task export       tasks only
```

Full export is the primary path. The others are conveniences for sharing a single course's progress.

---

## 8. Encryption

The user chooses:

```text
No encryption                    for a home network
End-to-end encryption            for cloud transport
```

### No encryption

Plain JSON. Appropriate when the transport is already trusted (local network, a cloud the user controls) and the data is learning progress.

### E2E encryption

```text
Algorithm     AES-GCM
Key derivation Argon2id (or PBKDF2-HMAC-SHA256 as a fallback)
```

```text
Header:  { "kdf": "argon2id", "salt": "…", "nonce": "…", "version": 1 }
Body:    encrypted snapshot
```

### The short-code problem

> **A short pairing code must never be used as an encryption key.**

A 10-character code has roughly 50 bits of entropy at best and is frequently much weaker. Deriving an encryption key from it produces something that looks encrypted and is not.

Therefore:

| Code type | Length | Purpose | Can derive a key? |
|---|---|---|---|
| Pairing code | 6–10 chars | **Identify the session only** | **No** |
| QR secret | 256 bits, random | Transport key exchange | Yes |

The QR code carries a long random secret, which is exactly why QR is the preferred pairing method. A manually typed short code identifies the session; the key is exchanged over the established connection.

### What the server sees

With E2E enabled, the server stores and forwards an opaque blob. It cannot read course titles, progress, or statistics. The server operator — who may be the user themselves — is not required to be trusted.

---

## 9. Pairing

```text
1. PC generates:  sessionId, secret (256-bit), expiresAt (5 minutes)
2. QR encodes:    host, port, sessionId, secret, expiresAt, protocol version
3. Phone scans, connects, presents sessionId + secret
4. PC confirms, issues:  session token, refresh token, deviceId
5. Phone stores the tokens; the session is now paired
```

### QR payload

```text
coursemoth://pair?
  host=192.168.1.10
  &port=8443
  &session=abc123
  &secret=<256-bit base64url>
  &expires=1710000000
  &v=1
```

A custom scheme lets the OS hand the link to the app when scanned by a camera app rather than in-app.

### Rules

| Rule | Reason |
|---|---|
| Session expires in 5 minutes | A QR left on screen overnight must not stay valid |
| Secret is single-use | Replay protection |
| Server shows the request before confirming | The user sees *which* device is asking |
| Tokens are revocable | A lost phone must be cut off |
| Access tokens are short-lived | Refresh tokens do the long-lived work |

---

## 10. Security posture

```text
Access tokens      short-lived (1 hour)
Refresh tokens     long-lived, revocable
Streaming URLs     temporary signed, short expiry
```

Temporary signed URLs exist because media players generally do not send custom `Authorization` headers. See [ServerApiSpec](ServerApiSpec.md).

Every paired device is listed in settings with its name, last-seen time, and a revoke button. A sync feature a user cannot audit is a sync feature they cannot trust.

---

## 11. Sync triggers

```text
On app start                     if a sync target is configured
After a lesson is completed      debounced, at most once per minute
On manual "Sync now"
On pairing a new device
Never                          on every position update
```

Syncing on every position update would push a request every five seconds of playback for a change that is meaningless until the lesson is left. Debouncing is not an optimization here — it is what makes the feature usable at all.

---

## 12. Deferred

```text
Real-time sync between open sessions
Selective sync (choose which courses to sync)
Sync history and rollback UI
Conflict resolution history
Multi-user support
```

---

## 13. Open questions

- **Clock trust.** `UpdatedAt` comes from device clocks, which can be wrong or deliberately changed. It is used only for last-write-wins on non-`WatchState` entities, where a wrong answer is cheap. Acceptable for now; revisit if it causes visible problems.
- **Tombstone versus missing course.** A course tombstoned on one device and never seen on another is correctly deleted. A course deleted by hand from the database and synced is indistinguishable from one never created. Manual database editing is not supported, so this is accepted.
- **Partial sync failure.** If a sync is interrupted halfway, the applied entities are already committed. Sync must be resumable by sequence number rather than transactional across the whole set.
- **Maximum snapshot size.** Not yet bounded. A library of 10,000 lessons produces a large export. Measure before stage 10.

---

## 14. What must be verified by spike

| # | Question | Why |
|---|---|---|
| 1 | Does fingerprint matching actually identify the same course across a PC and a phone? | The entire feature rests on this |
| 2 | Does export/import round-trip without data loss? | Cheapest possible failure to find |
| 3 | Does progress import correctly when the files are absent? | The state between export and folder selection |
| 4 | Is Argon2id available on both platforms, or is PBKDF2 required? | Affects the E2E design |
| 5 | Does a tombstone correctly suppress a resurrected course across three devices? | Two-device tests hide tombstone bugs |

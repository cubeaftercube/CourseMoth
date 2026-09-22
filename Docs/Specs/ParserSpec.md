# Parser Specification

How CourseMoth turns a folder tree into a course structure.

The parser is **the most treacherous part of the project**. It works with filenames invented by other people, for their own reasons, with no regard for convenience. Without a specification it turns into a pile of hacks within six months.

> **Related:** [DomainMap](../Maps/DomainMap.md#7-course-identity--the-key-to-synchronization) · [SystemMap](../Maps/SystemMap.md) · [UXMap](../Maps/UXMap.md#36-import-review) · [UserFlowMap](../Maps/UserFlowMap.md#3-scenario-importing-a-local-folder)

> **Note on examples:** sample filenames and paths below are frequently Russian, because Russian-language course archives are a primary target. Contributors can assume every heuristic here is exercised by non-ASCII names with mixed separators.

---

## 1. Goal

Convert a file tree into a "course → module → lesson" structure **without writing anything to the database**.

This is the defining property: **the parser has no side effects**. It reads and returns a proposal. The user reviews and confirms it. Only then does data reach SQLite.

Consequences: the parser can be re-run freely, cancelled at any point, and tested without a database.

---

## 2. Input

```csharp
public interface ICourseParser
{
    Task<ParsedRoot> ParseAsync(ParseRequest request, CancellationToken ct);
}

public record ParseRequest
{
    public required IFileSystemReader Source { get; init; }  // read abstraction
    public required string RootPath          { get; init; }
    public ParseMode Mode                    { get; init; }  // Auto | SingleCourse | Library
    public ParseOptions Options              { get; init; }
}
```

`IFileSystemReader` is an abstraction because the source is not always a local folder — it may be a server or cloud listing. The parser must not know where the names came from.

`ParseMode.Auto` is the default: the app infers course boundaries itself and **always** lets the user override the mode on the review screen.

---

## 3. Pipeline

```text
1. Walk the tree            → collect candidates
2. Filter                   → video, subtitles, covers, junk
3. Detect boundaries        → where a course starts, where a library starts
4. Assign levels            → what is a module, what is a lesson
5. Extract numbers          → parse names
6. Clean titles             → human-readable titles
7. Score confidence         → what to highlight in review
8. Build ParsedRoot         → temporary model
```

---

## 4. Walking and filtering

### Video extensions

**Required:**
```text
.mp4  .mkv  .avi  .mov  .webm  .m4v
```

**Later:** `.ts` `.wmv` `.flv` `.mpg` `.3gp`

An extension **does not** prove a file is video. A `.mp4` that is not actually playable must be rejected when metadata is read — not by crashing the player later.

### Skipped entries

```text
Hidden files and folders (leading dot)
System: $RECYCLE.BIN, System Volume Information, .DS_Store
Shell: Thumbs.db, desktop.ini
Temporary: *.part, *.tmp, *.crdownload, *.!ut
```

### Limits

```text
Maximum walk depth:            8 levels  (configurable)
Warn above 5000 files:         ask whether to continue
Maximum files per folder:      no limit, but >500 is a red flag
```

Depth is capped because nested archives and junk trees break every heuristic.

---

## 5. Detecting course boundaries

The folder the user picked is one of two shapes:

> **Where a flat course's lessons live.** A course whose video sits directly in its root carries
> them in `ParsedCourse.Lessons` with an **empty `Modules` list** — not in a module with a blank
> title. The distinction is load-bearing: a course has either `Modules` or `Lessons` at the top
> level, never both, and only the empty-`Modules` shape produces no `CourseModule` row
> ([DomainMap §3.2](../Maps/DomainMap.md#32-coursemodule)). A blank-titled module would be
> written to the database as a real module and leak into progress, the UI and sync.

**Mode A — one folder = one course**
```text
/Программирование на C#
  /01 Введение
    01 О курсе.mp4
  /02 Основы
    01 Переменные.mp4
```

**Mode B — folder = library of courses**
```text
/Курсы
  /Программирование на C#/...
  /Docker/...
  /Английский/...
```

### Auto-detection heuristics

```text
Video files directly in the root?               → Mode A
course.json in the root?                        → Mode A (unambiguous)
All root children are folders containing video? → Mode B
Otherwise                                       → Mode A
```

Auto-detection **is wrong sometimes**. Mode B producing a "course" per folder when each folder holds one lesson is the classic failure. Therefore:

- if Mode B yields more than 50 courses with a single lesson each, warn the user and suggest Mode A;
- switching mode on the review screen rebuilds the whole tree instantly, with no rescan.

---

## 6. Assigning levels

```text
Nested folders containing video?    → they are modules; video inside them are lessons
Video directly in the course root?  → no modules; every video is a lesson
More than two levels deep?          → deepest level with video = lessons,
                                      the level above = modules, the rest is collapsed
```

**Collapsing depth.** For `Course/Section/Subsection/lesson.mp4`, the module is taken from the level closest to the files, and `Section` becomes a prefix on the module title.

```text
Курс/Раздел/Подраздел/01.mp4
  → module: "Раздел · Подраздел"
```

That keeps one module instead of two without losing information the user may care about.

**Folders with no video inside** (images, documents only) are not modules.

**Module cap per course:** more than 200 modules means the structure was misread — surface a warning.

---

## 7. Extracting lesson numbers

Lesson order matters: it decides what plays next. Filenames do not always carry a number.

### Patterns (evaluated in order)

```text
S01E04            → season 1, episode 4        ⇒ sortKey = (1, 4)
01                → number 1                   ⇒ sortKey = 1
001               → number 1
1.                → number 1
1 - Title         → number 1
Урок 3            → number 3
Урок 3 - Title    → number 3
Глава 5           → number 5
Часть 2           → number 2
Part 5            → number 5
Lesson 02         → number 2
[03] Title        → number 3
(14) Title        → number 14
Module 1 - 02     → number 2      (the trailing number wins)
```

### Source priority

```text
1. Explicit number at the start of the name  →  "01 - Введение"
2. Explicit number at the end of the name    →  "Введение - 01"
3. SxxExx pattern                            →  "S01E04"
4. Keyword plus number                       →  "Урок 3"
5. Anything else                             →  no number found
```

**When no number is found** the lesson is not dropped. It gets `sortKey = null` and sorts by **filename, alphabetically**, after all numbered items. Numeric keys always sort before alphabetic ones.

### What is not a number

```text
2024          — a year       (four digits outside the number range)
1080          — a resolution
264, 265      — a codec
10bit         — bit depth
```

Rule: numbers above 365 are not treated as lesson numbers — except inside `SxxExx`, where the episode part may exceed it.

---

## 8. Cleaning titles

Technical noise is stripped from the filename. What remains must read like a human wrote it.

### Stripped

```text
Extension                       .mp4
Leading number                  02_, 02 -, 02.
Resolution                      1080p, 720p, 4K, 1920x1080
Codec                           x264, x265, H264, H265, HEVC, AV1
Bit depth                       10bit, 8bit
Source                          WEB-DL, WEBRip, HDTV, BluRay, DVDRip
Audio                           AAC, AC3, DTS, MP3, 5.1, 2.0
Release tags                    v2, final, FINAL, fixed, repack, proper
Repeated separators             __, --, .., runs of whitespace
Bracketed junk                  [site.com], [Telegram], [1080p]
```

### Examples

```text
02_Setup_Tools_1080p_x264_v2_final.mp4
  → "Setup Tools"

[CourseName] Урок 03 - Переменные и типы [1080p].mkv
  → "Переменные и типы"

01.Основы.Rust.2024.WEB-DL.1080p.mp4
  → "Основы Rust"          (a trailing year is stripped; a leading one is not)
```

### Setting

```text
Use original filenames as titles    — off by default
```

Users whose filenames carry more meaning than convenience must be able to switch cleaning off wholesale.

### What cleaning never does

**It does not translate and does not correct.** `Urok 3` does not become `Урок 3`. Guessing at intent produces subtly wrong data the user will never notice.

---

## 9. Companion files

### Subtitles

Looked up **next to the video**, matched on base name:

```text
lesson.mp4  +  lesson.srt            → track with no language
lesson.mp4  +  lesson.en.srt         → track "en"
lesson.mp4  +  lesson.ru.srt         → track "ru"
lesson.mp4  +  lesson.forced.srt     → forced track
```

Supported: `.srt`, `.vtt`. Later: `.ass`, `.ssa`.

Match rule: `video_name` + optional language/role suffix + `.srt|.vtt`. A file with a different base name is **not** auto-attached — it can be linked manually in review.

### Covers

```text
cover.jpg / cover.png
folder.jpg / poster.jpg
<course folder name>.jpg in the course root
```

First match by priority wins. The user can replace it in review.

---

## 10. `course.json`

If `course.json` sits in the course root, **it is the source of truth**. The parser guesses nothing it describes.

```json
{
  "schemaVersion": 1,
  "id": "7f1f7d4c-7c3c-4d9e-8d0f-5d2f4c2f8b21",
  "title": "Программирование на C#",
  "author": "Иван Иванов",
  "category": "Программирование",
  "description": "Курс для начинающих",
  "completionThreshold": 0.9,
  "cover": "cover.jpg",
  "modules": [
    {
      "title": "Введение",
      "lessons": [
        { "file": "01 О курсе.mp4", "title": "О курсе" },
        { "file": "02 Установка инструментов.mp4", "title": "Установка инструментов" }
      ]
    }
  ]
}
```

### Rules

| Field | If absent |
|---|---|
| `id` | Generated on import and written back (with consent) |
| `title` | Falls back to the folder name |
| `modules` | Structure is detected from folders |
| `file` | **Required.** A lesson without a file is skipped with a warning |
| `cover` | Standard names are searched |

**Lesson order from `course.json` outranks numbers in filenames.** A user who wrote the file expressed an explicit intent.

**Files on disk not mentioned in `course.json`** appear on the review screen in a separate "Not described in course.json" block — they must not vanish silently.

### Updating the file

The app **never overwrites** `course.json` without explicit consent. It is the user's file — possibly tracked in git, possibly written by another tool.

Setting: `Update course.json after edits` — off by default.

### Creating the file

After a successful import, offer:
```text
Save the structure to course.json?
This lets the course be matched on another device.
```

The answer is remembered, including a refusal.

---

## 11. Confidence scoring

Every parsed item carries a `Confidence`, so the **review screen highlights only what is doubtful** instead of forcing a full re-read.

```csharp
public enum ParseConfidence
{
    Certain,   // from course.json, or an unambiguous structure
    High,      // number and title parsed confidently
    Low,       // no number found, or the title was heavily mangled
    Unknown    // could not be determined
}
```

Only `Low` and `Unknown` are highlighted. On a real archive that is typically 5–15% of files — exactly the set a user should actually review.

**Without this signal the review screen is useless:** highlight everything and the user approves blindly.

---

## 12. Output

Temporary models only. The entities from [DomainMap](../Maps/DomainMap.md) are **not** used here:

```csharp
public class ParsedRoot
{
    public ParseMode DetectedMode { get; init; }
    public IReadOnlyList<ParsedCourse> Courses { get; init; }
    public IReadOnlyList<ParseWarning> Warnings { get; init; }
    public ParseStats Stats { get; init; }
}

public class ParsedCourse
{
    public string Title { get; set; }          // editable in review
    public string? Author { get; set; }
    public string? CategoryName { get; set; }
    public string? CoverRelativePath { get; set; }
    public string RootRelativePath { get; init; }
    public Guid? MetadataId { get; init; }     // from course.json
    public IReadOnlyList<ParsedModule> Modules { get; set; }
    public IReadOnlyList<ParsedLesson> Lessons { get; set; }   // flat course: no modules above them
    public ParseConfidence Confidence { get; init; }
    public bool IsExcluded { get; set; }       // user unchecked it
}

public class ParsedModule
{
    public string Title { get; set; }
    public int Order { get; set; }
    public IReadOnlyList<ParsedLesson> Lessons { get; set; }
    public bool IsExcluded { get; set; }       // user unchecked the whole module
}

public class ParsedLesson
{
    public string Title { get; set; }
    public required string RelativePath { get; init; }
    public int? Number { get; set; }
    public int Order { get; set; }
    public long? FileSizeBytes { get; init; }
    public IReadOnlyList<ParsedSubtitle> Subtitles { get; init; }
    public ParseConfidence Confidence { get; init; }
    public bool IsExcluded { get; set; }
    public bool IsMaterial { get; set; }       // a handout, not a lesson
}

public record ParseWarning(ParseWarningKind Kind, string Message, string? RelativePath);

public enum ParseWarningKind
{
    SkippedNonVideo, NoNumberInName, AmbiguousStructure,
    DuplicateTitle, TooManyCourses, TooManyModules, OrphanFile, UnreadableFile
}
```

**Why a temporary model rather than domain entities.** `ParsedCourse` is mutable, single-use, carries `Confidence`, and has no `Guid`. Domain entities are effectively immutable and persist. Merging the two means dragging `Confidence` and `IsExcluded` into SQLite forever.

---

## 13. Structure fingerprint

The parser supplies the material for the fingerprint that identifies a course across devices ([DomainMap §7](../Maps/DomainMap.md#7-course-identity--the-key-to-synchronization)).

```text
fingerprint = SHA256(
    metadataId                              if course.json carried an id
    | normalize(course folder name)
    | root-relative lesson paths sorted by path,
        each as normalize(path) + ":" + file size
    | module count
)
```

**Deliberately excluded:**

| Excluded | Why |
|---|---|
| Absolute paths | Moving the folder must not change the fingerprint |
| Module titles | Edited by the user and translated |
| Lesson titles | Same |
| Duration | May be unknown on first scan |
| File timestamps | Change on copy |

**Fingerprint normalization is separate** from display title cleaning:

```text
lowercase
strip leading and trailing numbers
strip extension
strip release tags (1080p, x264, ...)
collapse any run of non-alphanumerics into a single space
trim
```

Normalization **must be deterministic and versioned**. Changing it changes every fingerprint and breaks matching for already-imported courses. The algorithm version is stored alongside the fingerprint:

```csharp
public const int FingerprintVersion = 1;
```

On a version bump, old fingerprints are recomputed in the background — not discarded. Discarding them breaks sync for users who already imported courses.

---

## 14. Test data

The parser is tested against folders in `samples/`. The set **must** include messy cases, not just tidy ones:

```text
samples/
  course-simple/          Course/Lesson.mp4                    — flat structure
  course-modules/         Course/Module/Lesson.mp4             — the common shape
  course-messy-names/     02_Setup_Tools_1080p_x264_v2.mp4     — junk in names
  course-no-numbers/      Intro.mp4, Basics.mp4                — no numbers at all
  course-subtitles/       Lesson.mp4 + Lesson.ru.srt + Lesson.en.srt
  course-with-json/       course.json + files
  course-json-partial/    course.json describing only some files
  course-duplicate/       two copies of one course in different folders
  course-deep/            Course/Section/Subsection/Lesson.mp4
  course-mixed/           video in the root AND in a subfolder — ambiguous
  library/                several courses in one folder
```

Video files are stubs of a few bytes. The parser never needs the content, and the repository stays small.

**Tests must cover:** every `ParseWarningKind`, every number pattern from §7, and every cleaning case from §8.

---

## 15. Open questions

- **`.m3u` playlists** — one lesson or many? Not supported yet.
- **Multi-episode files** (`S01E01-E04` in one file) — currently treated as one lesson.
- **Course materials** (`.pdf`, `.zip` alongside video) — ignored today. `ParsedLesson.IsMaterial` exists, but there is no UI for it.
- **Multi-disc courses** (`Disk 1`, `Disk 2`) — parsed as modules. Verify against real archives.

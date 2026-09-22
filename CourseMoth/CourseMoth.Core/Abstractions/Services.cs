// SPDX-License-Identifier: AGPL-3.0-or-later

using CourseMoth.Core.Domain;
using CourseMoth.Core.Parsing;

namespace CourseMoth.Core.Abstractions;

/// <summary>
/// Progress through a course. All values are computed, never stored — a saved derived
/// value is a value that will drift out of sync. See DomainMap §8.
/// </summary>
public record ProgressSnapshot(
    Guid CourseId,
    int TotalLessons,
    int CompletedLessons,
    double Percent,
    double WatchedMs);

/// <summary>State needed to seed a streak calculation. Supplied by the composition root, not read from a database by Core.</summary>
public record StreakContext(DayBoundaryOptions Boundary, Guid DeviceId);

public interface IProgressCalculator
{
    /// <summary>Progress for a single course.</summary>
    Task<ProgressSnapshot> ForCourseAsync(Guid courseId, CancellationToken ct = default);

    /// <summary>Progress for a module, or for the whole course when moduleId is null.</summary>
    Task<ProgressSnapshot> ForModuleAsync(Guid courseId, Guid? moduleId, CancellationToken ct = default);
}

/// <summary>
/// All progress writes go through here. Media reports what happened; Core decides what it means.
/// See SystemMap §3 ("Media does not store progress").
/// </summary>
public interface IWatchStateService
{
    Task<WatchState> GetOrCreateAsync(Guid lessonId, CancellationToken ct = default);

    /// <summary>Record a playback position. Completes the lesson when the threshold is crossed.</summary>
    Task<WatchState> ReportPositionAsync(Guid lessonId, TimeSpan position, TimeSpan? duration, CancellationToken ct = default);

    /// <summary>Playback reached the end of the media. Completes the lesson unconditionally.</summary>
    Task<WatchState> ReportEndedAsync(Guid lessonId, CancellationToken ct = default);

    /// <summary>The user marked the lesson complete. Takes precedence over the threshold.</summary>
    Task<WatchState> MarkCompletedAsync(Guid lessonId, CancellationToken ct = default);

    /// <summary>
    /// The user marked the lesson incomplete. Position is kept and threshold auto-completion
    /// is suppressed until the user acts again.
    /// </summary>
    Task<WatchState> MarkIncompleteAsync(Guid lessonId, CancellationToken ct = default);

    /// <summary>Mark every lesson in a course complete.</summary>
    Task MarkCourseCompletedAsync(Guid courseId, CancellationToken ct = default);

    /// <summary>True when this state is protected from threshold auto-completion.</summary>
    bool IsProtectedFromAutoCompletion(WatchState state);
}

/// <summary>
/// Computes and matches course fingerprints. The algorithm is versioned — changing the
/// normalisation changes every fingerprint. See ParserSpec §13 and DomainMap §7.
/// </summary>
public interface ICourseFingerprintService
{
    int AlgorithmVersion { get; }

    /// <summary>Fingerprint from a parsed course.</summary>
    string Compute(ParsedCourse course, string courseFolderName);

    /// <summary>Fingerprint from an already-imported course and its lessons.</summary>
    string Compute(Course course, IReadOnlyList<Lesson> lessons);

    /// <summary>Normalises a single path fragment. Exposed for tests.</summary>
    string Normalize(string value);
}

/// <summary>
/// Imports a confirmed parse result into the domain. This is the ONLY place the parser's
/// proposal becomes persistent state — before this call, nothing has been written.
/// See ParserSpec §1.
/// </summary>
public interface IImportService
{
    Task<ImportResult> ImportAsync(ParsedRoot root, Guid? sourceId, CancellationToken ct = default);
}

public record ImportResult(int CoursesImported, int ModulesImported, int LessonsImported, IReadOnlyList<Guid> CourseIds);

/// <summary>Evaluates task completion from recorded activity and progress.</summary>
public interface ITaskEvaluator
{
    /// <summary>Recompute CompletedValue and IsCompleted for the given day's tasks.</summary>
    Task<IReadOnlyList<LearningTask>> EvaluateAsync(DateOnly date, CancellationToken ct = default);

    /// <summary>Create the day's automatic tasks if they do not exist yet. Idempotent.</summary>
    Task<IReadOnlyList<LearningTask>> GenerateAutomaticTasksAsync(DateOnly date, CancellationToken ct = default);
}

public interface IStreakCalculator
{
    /// <summary>
    /// Current streak. Counts consecutive qualifying days ending today or yesterday —
    /// a day still in progress must not look like a broken streak.
    /// </summary>
    Task<int> CurrentAsync(StreakContext context, CancellationToken ct = default);

    Task<int> LongestAsync(CancellationToken ct = default);

    /// <summary>Whether a day qualifies, given the configured rule.</summary>
    bool QualifiesForStreak(LearningActivity activity, DayBoundaryOptions options);
}

/// <summary>Day boundary and streak rule, from user settings.</summary>
public class DayBoundaryOptions
{
    /// <summary>Hour at which a new study day begins. Default 04:00 — people study after midnight.</summary>
    public TimeSpan DayBoundary { get; set; } = TimeSpan.FromHours(4);

    public StreakRule Rule { get; set; } = StreakRule.Lessons;

    /// <summary>Threshold when <see cref="Rule"/> is Minutes.</summary>
    public int MinutesThreshold { get; set; } = 20;
}

public enum StreakRule
{
    Lessons,
    Minutes,
}

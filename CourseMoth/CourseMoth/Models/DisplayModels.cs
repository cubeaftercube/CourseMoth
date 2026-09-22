// SPDX-License-Identifier: AGPL-3.0-or-later

namespace CourseMoth.Models;

/// <summary>
/// View-facing shapes for the course list (UXMap §3.2) and the "Continue" card (UXMap §3.1).
///
/// These are projections, not domain entities: they carry only what a template binds to, in the
/// form a template can bind without a converter. Building them is the ViewModel's job and is the
/// only "shaping" the UI layer is allowed to do.
/// </summary>
/// <param name="Id">Course identity, used to navigate to the course page.</param>
/// <param name="Title">Course title.</param>
/// <param name="Subtitle">Author · category, as shown under the title on a course card.</param>
/// <param name="ProgressPercent">0..1, for the ProgressBar.</param>
/// <param name="ProgressText">Pre-formatted "62%" for the label next to the bar.</param>
/// <param name="ProgressDetail">Pre-formatted "12 of 19 lessons".</param>
/// <param name="CoverPath">Cover image inside the course folder, or null for a placeholder.</param>
/// <param name="HasCover">Whether <paramref name="CoverPath"/> holds something usable.</param>
public sealed record CourseCard(
    Guid Id,
    string Title,
    string Subtitle,
    double ProgressPercent,
    string ProgressText,
    string ProgressDetail,
    string? CoverPath,
    bool HasCover);

/// <summary>
/// The "Continue" card on Home (UXMap §3.1): the course, where the user stopped, and the button
/// that resumes it.
/// </summary>
/// <param name="CourseId">Course to resume.</param>
/// <param name="CourseTitle">Course title.</param>
/// <param name="ModuleTitle">Module of the lesson, or null when the course is flat.</param>
/// <param name="LessonTitle">The lesson the user stopped on.</param>
/// <param name="LessonId">Lesson to open in the player.</param>
/// <param name="ProgressPercent">0..1 for the ProgressBar.</param>
/// <param name="ProgressText">Pre-formatted "62%".</param>
/// <param name="CoverPath">Cover image, or null.</param>
public sealed record ContinueCard(
    Guid CourseId,
    string CourseTitle,
    string? ModuleTitle,
    string LessonTitle,
    Guid LessonId,
    double ProgressPercent,
    string ProgressText,
    string? CoverPath);

/// <summary>
/// One task row (UXMap §3.7). Automatic and user-created tasks must be distinguishable
/// <i>visually</i>, not only by their text, which is what <see cref="IsAutomatic"/> and
/// <see cref="SourceLabel"/> are for.
/// </summary>
/// <param name="Id">Task identity.</param>
/// <param name="Title">What the user has to do.</param>
/// <param name="SourceLabel">"Automatic" or "My task".</param>
/// <param name="IsAutomatic">True for a task the app generated.</param>
/// <param name="HasProgress">Whether the task tracks a target ("1 of 2 lessons").</param>
/// <param name="ProgressPercent">0..1 for the ProgressBar; 0 when <see cref="HasProgress"/> is false.</param>
/// <param name="ProgressText">Pre-formatted "1/1" or "18/30".</param>
/// <param name="Detail">Course and due date, as shown under the title, or null.</param>
/// <param name="IsCompleted">Whether the goal is met.</param>
public sealed record TaskItem(
    Guid Id,
    string Title,
    string SourceLabel,
    bool IsAutomatic,
    bool HasProgress,
    double ProgressPercent,
    string ProgressText,
    string? Detail,
    bool IsCompleted);

/// <summary>A row in Settings. <see cref="IsEnabled"/> is false for anything not implemented yet.</summary>
/// <param name="Label">The setting's name.</param>
/// <param name="Value">The current value, for read-only rows.</param>
/// <param name="Note">A short "not implemented yet" note, or null on a working row.</param>
/// <param name="IsEnabled">False renders the row disabled and dimmed.</param>
public sealed record SettingRow(
    string Label,
    string? Value = null,
    string? Note = null,
    bool IsEnabled = true)
{
    public bool HasValue => !string.IsNullOrEmpty(Value);

    public bool HasNote => !string.IsNullOrEmpty(Note);

    public double Opacity => IsEnabled ? 1.0 : 0.45;
}

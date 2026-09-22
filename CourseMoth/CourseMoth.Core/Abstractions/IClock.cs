// SPDX-License-Identifier: AGPL-3.0-or-later

namespace CourseMoth.Core.Abstractions;

/// <summary>
/// Time, day boundaries and stable keys.
///
/// The day boundary exists because people study after midnight: a session from 23:00 to 01:30
/// is coherent learning, and splitting it across two days would break a streak the user earned.
/// See TasksStreaksSpec §3.
/// </summary>
public interface IClock
{
    DateTime UtcNow { get; }

    /// <summary>
    /// The study date that <paramref name="instant"/> belongs to, in the device's current zone.
    /// Computed once when the activity is recorded and never recomputed afterwards — a streak
    /// that rewrites itself on a timezone change is worse than one that is a few hours off.
    /// </summary>
    DateOnly StudyDateFor(DateTimeOffset instant, DayBoundaryOptions options);
}

/// <summary>
/// Generates human-readable stable keys such as "course:csharp-basics".
/// Fallback identity only — a renamed folder changes the key, so it can never be
/// the primary identifier. See DomainMap §7.
/// </summary>
public interface IStableKeyGenerator
{
    string ForCourse(string normalizedName);
    string ForModule(string courseKey, int order, string normalizedTitle);
    string ForLesson(string courseKey, string moduleKey, int order, string normalizedTitle);
}

// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Globalization;
using CourseMoth.Core.Abstractions;

namespace CourseMoth.Core.Services;

/// <summary>
/// The system clock and the study-day boundary. See TasksStreaksSpec §3.
///
/// <see cref="TimeProvider"/> is injected rather than <c>DateTime.UtcNow</c> being called
/// directly, so that tests can place the app at 01:30 without changing the machine clock.
/// </summary>
public sealed class SystemClock : IClock
{
    private readonly TimeProvider _timeProvider;

    public SystemClock(TimeProvider timeProvider)
        => _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));

    public DateTime UtcNow => _timeProvider.GetUtcNow().UtcDateTime;

    /// <summary>
    /// The study date an instant belongs to. The boundary is subtracted from the instant and
    /// the local date of the result is taken, so with the default 04:00 boundary a 01:30
    /// session lands on the previous day — a 23:00→01:30 session stays one coherent day.
    ///
    /// <para>
    /// <b>The offset carried by <paramref name="instant"/> is the device's zone, and it is the
    /// input.</b> TasksStreaksSpec §3: "Timezone used for the boundary: the device's current IANA
    /// timezone". Converting through <see cref="DateTime.ToLocalTime"/> instead would reach the
    /// zone via <see cref="TimeZoneInfo.Local"/> — the <i>machine's</i> zone — and silently
    /// re-express an instant that says UTC+03 in whatever offset the host happens to sit at. On a
    /// desktop build the device is the machine and the two readings coincide, which is why the
    /// difference is invisible until a caller passes an instant built from UTC:
    /// <c>ActivityTracker.Today</c> hands over <c>Truncate(_clock.UtcNow)</c>, an offset of zero
    /// regardless of where the user is. Honouring the instant is what makes the same moment
    /// resolve differently for a user in Moscow and a user in New York, which is the whole point
    /// of the rule. See <c>DayBoundaryTests.The_same_instant_is_read_against_each_device_s_own_zone</c>.
    /// </para>
    ///
    /// <para>
    /// Subtracting the boundary from the offset it arrives with, rather than through the zone's
    /// rule set, also keeps the arithmetic honest across a DST transition: "04:00 on the device's
    /// clock" is an offset on the instant's own scale, and re-deriving it through the host's zone
    /// would answer a question about a different clock.
    /// </para>
    ///
    /// <para>
    /// <b>The interval is half-open at the boundary.</b> 04:00 exactly still reads as the day it
    /// closes; the new day begins at the first instant after it. That is the convention
    /// <c>DayBoundaryTests.A_session_at_0400_belongs_to_the_new_study_date</c> pins, and the one
    /// <c>Every_supported_boundary_agrees_that_0600_is_today</c> depends on — with a 00:00
    /// boundary every instant of the day would otherwise land on the day after it.
    /// </para>
    /// </summary>
    public DateOnly StudyDateFor(DateTimeOffset instant, DayBoundaryOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var shifted = instant - options.DayBoundary;

        return DateOnly.FromDateTime(shifted.DateTime);
    }

    /// <summary>
    /// The study date of "now", read off the device's own clock.
    ///
    /// <c>GetLocalNow()</c> already carries the device's current offset, so subtracting the
    /// boundary from it is a wall-clock statement about the user's own clock — and it still means
    /// "today on this machine", because the machine's zone is what supplied the offset.
    /// </summary>
    public DateOnly Today(DayBoundaryOptions options)
        => StudyDateFor(_timeProvider.GetLocalNow(), options);

    /// <summary>Wall-clock timestamp for record-keeping fields such as <c>UpdatedAt</c>.</summary>
    public DateTime LocalNow => _timeProvider.GetLocalNow().DateTime;

    public override string ToString()
        => UtcNow.ToString("O", CultureInfo.InvariantCulture);
}

// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Globalization;

namespace CourseMoth.ViewModels;

/// <summary>Which segment of the Tasks screen is selected (UXMap §3.7).</summary>
public enum TaskSegment
{
    Today,
    Upcoming,
    Done,
}

/// <summary>
/// Compares the bound <see cref="TasksViewModel.SelectedSegment"/> with the segment a particular
/// segment button stands for, so one selection property can drive a radio group of N buttons.
///
/// <c>ConvertBack</c> only claims to be checked — a RadioButton that reports "not checked" during
/// a group update must not clear the selection, or the group ends up with nothing selected.
/// </summary>
public sealed class SegmentEqualsConverter : IValueConverter
{
    private readonly TaskSegment _segment;

    public SegmentEqualsConverter(TaskSegment segment) => _segment = segment;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is TaskSegment selected && selected == _segment;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? _segment : Binding.DoNothing;
}

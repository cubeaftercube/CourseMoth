// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CourseMoth.Core.Abstractions;
using CourseMoth.Core.Domain;
using CourseMoth.Models;

namespace CourseMoth.ViewModels;

/// <summary>
/// Library (UXMap §3.2) — every course, with the filters and sorts the UXMap lists.
///
/// The filters are client-side projections of one repository read. When the library grows past a
/// few hundred courses this should move behind a paged query in Core; at MVP size it does not
/// justify one.
/// </summary>
public sealed partial class LibraryViewModel : ViewModelBase
{
    private readonly ICourseRepository _courses;
    private readonly IProgressCalculator _progress;

    private readonly List<CourseCard> _all = [];

    public LibraryViewModel(ICourseRepository courses, IProgressCalculator progress)
    {
        _courses = courses;
        _progress = progress;
    }

    public string Title => "Library";

    public ObservableCollection<CourseCard> Courses { get; } = [];

    /// <summary>Filter chips, in the order UXMap §3.2 lists them.</summary>
    public IReadOnlyList<string> Filters { get; } =
        ["All", "In progress", "Completed", "Not started", "Favorites"];

    /// <summary>Sort options, in the order UXMap §3.2 lists them.</summary>
    public IReadOnlyList<string> Sorts { get; } =
        ["Recently opened", "Title", "Progress", "Date added"];

    [ObservableProperty]
    private string _selectedFilter = "All";

    [ObservableProperty]
    private string _selectedSort = "Recently opened";

    /// <summary>False until the first successful load — otherwise "No courses yet" flashes on startup.</summary>
    public bool IsEmpty => IsLoaded && !HasError && Courses.Count == 0;

    public string EmptyTitle => "No courses yet";

    public string EmptyMessage =>
        "Add a folder with your downloaded courses. CourseMoth reads the structure — courses, " +
        "modules, lessons — and keeps your progress.";

    protected override async Task LoadCoreAsync(CancellationToken ct)
    {
        var courses = await _courses.ListAsync(ct).ConfigureAwait(true);

        _all.Clear();

        foreach (var course in courses.Where(c => !c.IsHidden))
        {
            var snapshot = await _progress.ForCourseAsync(course.Id, ct).ConfigureAwait(true);

            _all.Add(new CourseCard(
                course.Id,
                course.Title,
                BuildSubtitle(course),
                snapshot.Percent,
                FormatPercent(snapshot.Percent),
                $"{snapshot.CompletedLessons} of {snapshot.TotalLessons} lessons",
                course.CoverPath,
                !string.IsNullOrWhiteSpace(course.CoverPath)));
        }

        ApplyView();
    }

    partial void OnSelectedFilterChanged(string value) => ApplyView();

    partial void OnSelectedSortChanged(string value) => ApplyView();

    /// <summary>
    /// Rebuilds the visible list from the loaded set. Runs on the UI thread: the caller is a
    /// property-changed handler on a command path, never a background completion.
    /// </summary>
    private void ApplyView()
    {
        var visible = Sort(Filter(_all));

        Courses.Clear();

        foreach (var card in visible)
        {
            Courses.Add(card);
        }

        OnPropertyChanged(nameof(IsEmpty));
    }

    private IEnumerable<CourseCard> Filter(IEnumerable<CourseCard> source) => SelectedFilter switch
    {
        "In progress" => source.Where(c => c.ProgressPercent is > 0 and < 1),
        "Completed" => source.Where(c => c.ProgressPercent >= 1),
        "Not started" => source.Where(c => c.ProgressPercent <= 0),
        // TODO: favorites need Course.IsFavorite carried onto the card; the repository read is
        // in place but the flag is not projected yet.
        "Favorites" => [],
        _ => source,
    };

    private IEnumerable<CourseCard> Sort(IEnumerable<CourseCard> source) => SelectedSort switch
    {
        "Title" => source.OrderBy(c => c.Title, StringComparer.CurrentCultureIgnoreCase),
        "Progress" => source.OrderByDescending(c => c.ProgressPercent),
        // Ordering by "date added" needs CreatedAt on the card; the repository read supplies it
        // but the projection does not carry it yet.
        _ => source,
    };

    /// <summary>Opens the course page (UXMap §3.3, stage 1).</summary>
    [RelayCommand]
    private async Task OpenCourseAsync(CourseCard? card)
    {
        if (card is null)
        {
            return;
        }

        // TODO: await Shell.Current.GoToAsync($"course?id={card.Id}") once the course page and
        // its route exist.
        await Task.CompletedTask.ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task RefreshAsync() => await LoadAsync().ConfigureAwait(true);

    private static string BuildSubtitle(Course course)
    {
        if (!string.IsNullOrWhiteSpace(course.Author))
        {
            return course.Author;
        }

        return course.Status switch
        {
            CourseStatus.Completed => "Completed",
            CourseStatus.InProgress => "In progress",
            CourseStatus.Abandoned => "Abandoned",
            _ => "Not started",
        };
    }

    private static string FormatPercent(double fraction) =>
        $"{Math.Round(Math.Clamp(fraction, 0, 1) * 100)}%";
}

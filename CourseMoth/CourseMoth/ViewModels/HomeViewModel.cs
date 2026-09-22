// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CourseMoth.Core.Abstractions;
using CourseMoth.Core.Domain;
using CourseMoth.Models;
using CourseMoth.Services;

namespace CourseMoth.ViewModels;

/// <summary>
/// Home (UXMap §3.1) — "what should I do right now".
///
/// Content by priority: streak → today's tasks → Continue → recent courses. The empty state is
/// the onboarding screen: this is the first thing a new user sees, and the UXMap is explicit
/// that there is no separate onboarding, so the explanation and the "Add folder" button live here.
/// </summary>
public sealed partial class HomeViewModel : ViewModelBase
{
    private readonly ICourseRepository _courses;
    private readonly ILessonRepository _lessons;
    private readonly ICourseModuleRepository _modules;
    private readonly IProgressCalculator _progress;
    private readonly ITaskRepository _tasks;
    private readonly IStreakCalculator _streak;
    private readonly IClock _clock;
    private readonly IThemeService _theme;
    private readonly IFolderPicker _folderPicker;

    public HomeViewModel(
        ICourseRepository courses,
        ILessonRepository lessons,
        ICourseModuleRepository modules,
        IProgressCalculator progress,
        ITaskRepository tasks,
        IStreakCalculator streak,
        IClock clock,
        IThemeService theme,
        IFolderPicker folderPicker)
    {
        _courses = courses;
        _lessons = lessons;
        _modules = modules;
        _progress = progress;
        _tasks = tasks;
        _streak = streak;
        _clock = clock;
        _theme = theme;
        _folderPicker = folderPicker;
    }

    public string Title => "Home";

    public ObservableCollection<TaskItem> TodayTasks { get; } = [];

    public ObservableCollection<CourseCard> RecentCourses { get; } = [];

    [ObservableProperty]
    private int _streakDays;

    [ObservableProperty]
    private int _tasksCompletedToday;

    [ObservableProperty]
    private int _tasksTotalToday;

    [ObservableProperty]
    private ContinueCard? _continueCard;

    /// <summary>
    /// What the last "Add folder" attempt produced, shown to the user.
    ///
    /// This exists because the action previously ended in silence: the dialog closed, the path
    /// was discarded, and nothing on screen changed. A button that does something invisible is
    /// indistinguishable from a button that is broken, which is exactly how it was reported.
    /// </summary>
    [ObservableProperty]
    private string? _importStatus;

    public bool HasImportStatus => !string.IsNullOrWhiteSpace(ImportStatus);

    partial void OnImportStatusChanged(string? value) => OnPropertyChanged(nameof(HasImportStatus));

    /// <summary>First launch: there is nothing in the library at all.</summary>
    public bool IsLibraryEmpty => IsLoaded && !HasError && ContinueCard is null && RecentCourses.Count == 0;

    public bool HasContinue => ContinueCard is not null;

    public bool HasTodayTasks => TodayTasks.Count > 0;

    public string StreakText => StreakDays == 1 ? "1 day" : $"{StreakDays} days";

    public string TodayTasksSummary => TasksTotalToday == 0
        ? "No tasks today"
        : $"Today: {TasksCompletedToday} of {TasksTotalToday} tasks";

    protected override async Task LoadCoreAsync(CancellationToken ct)
    {
        var courses = await _courses.ListAsync(ct).ConfigureAwait(true);

        TodayTasks.Clear();
        RecentCourses.Clear();
        ContinueCard = null;

        if (courses.Count == 0)
        {
            StreakDays = 0;
            TasksCompletedToday = 0;
            TasksTotalToday = 0;
            NotifyDerived();
            return;
        }

        await LoadContinueCardAsync(courses, ct).ConfigureAwait(true);
        await LoadTodayTasksAsync(ct).ConfigureAwait(true);
        await LoadStreakAsync(ct).ConfigureAwait(true);
        await LoadRecentCoursesAsync(courses, ct).ConfigureAwait(true);

        NotifyDerived();
    }

    /// <summary>
    /// The most recently opened course that is neither finished nor hidden. "Continue" points at
    /// the course the user last touched, which is what makes it answer "right now".
    /// </summary>
    private async Task LoadContinueCardAsync(IReadOnlyList<Course> courses, CancellationToken ct)
    {
        var course = courses
            .Where(c => !c.IsHidden && c.Status != CourseStatus.Completed && c.Status != CourseStatus.Abandoned)
            .OrderByDescending(c => c.LastOpenedAt ?? c.UpdatedAt)
            .FirstOrDefault();

        if (course is null)
        {
            return;
        }

        var lessons = await _lessons.ListByCourseAsync(course.Id, ct).ConfigureAwait(true);
        var lesson = lessons.OrderBy(l => l.Order).FirstOrDefault();

        if (lesson is null)
        {
            return;
        }

        var snapshot = await _progress.ForCourseAsync(course.Id, ct).ConfigureAwait(true);
        var moduleTitle = await ResolveModuleTitleAsync(course.Id, lesson.ModuleId, ct).ConfigureAwait(true);

        ContinueCard = new ContinueCard(
            course.Id,
            course.Title,
            moduleTitle,
            lesson.Title,
            lesson.Id,
            snapshot.Percent,
            FormatPercent(snapshot.Percent),
            course.CoverPath);
    }

    private async Task<string?> ResolveModuleTitleAsync(Guid courseId, Guid? moduleId, CancellationToken ct)
    {
        if (moduleId is null)
        {
            return null;
        }

        var modules = await _modules.ListByCourseAsync(courseId, ct).ConfigureAwait(true);

        return modules.FirstOrDefault(m => m.Id == moduleId)?.Title;
    }

    private async Task LoadTodayTasksAsync(CancellationToken ct)
    {
        // The study day, not the calendar day: a 23:00→01:30 session has to stay one day
        // (TasksStreaksSpec §3). Asking the clock keeps the boundary rule in Core.
        var today = _clock.StudyDateFor(DateTimeOffset.Now, new DayBoundaryOptions());
        var tasks = await _tasks.ListForDateAsync(today, ct).ConfigureAwait(true);

        foreach (var task in tasks)
        {
            TodayTasks.Add(ToTaskItem(task));
        }

        TasksTotalToday = tasks.Count;
        TasksCompletedToday = tasks.Count(t => t.IsCompleted);
    }

    private async Task LoadStreakAsync(CancellationToken ct)
    {
        var context = new StreakContext(new DayBoundaryOptions(), Guid.Empty);

        StreakDays = await _streak.CurrentAsync(context, ct).ConfigureAwait(true);
    }

    private async Task LoadRecentCoursesAsync(IReadOnlyList<Course> courses, CancellationToken ct)
    {
        var recent = courses
            .Where(c => !c.IsHidden)
            .OrderByDescending(c => c.LastOpenedAt ?? c.UpdatedAt)
            .Take(6);

        foreach (var course in recent)
        {
            var snapshot = await _progress.ForCourseAsync(course.Id, ct).ConfigureAwait(true);
            RecentCourses.Add(ToCard(course, snapshot));
        }
    }

    /// <summary>
    /// Opens a folder and hands it to the importer. The scan, the parse and the review screen
    /// come after the parse layer exists; until then the folder is only remembered as a path so
    /// the button is honest about what it did.
    /// </summary>
    [RelayCommand]
    public async Task AddFolderAsync()
    {
        ErrorMessage = null;

        string? path;

        try
        {
            path = await _folderPicker.PickAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            // The dialog is platform code and can fail for platform reasons — no COM apartment,
            // no owner window, a sandbox that refuses the shell. Swallowing that turns a specific
            // failure into "the button does nothing", which is unfixable from the outside.
            ErrorMessage = $"Could not open the folder dialog: {ex.GetType().Name}: {ex.Message}";
            return;
        }

        // Cancelling is a decision, not a failure, and must not produce an error message.
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        // TODO: hand the path to IParser (Parser project) and push the Import review screen
        // (UXMap §3.6). Nothing may be written to the database until the user confirms, and
        // Core's IImportService takes a ParsedRoot — which the parser that does not exist yet
        // has to produce. Until then the chosen folder is reported back and discarded.
        //
        // Saying so out loud is deliberate. The honest state of the app is "this folder was
        // chosen and nothing was scanned", and a message that says exactly that is better than
        // a button that appears to do nothing at all.
        ImportStatus =
            $"Selected: {path}\n" +
            "Scanning is not built yet, so nothing was added. The folder was not modified.";
    }

    /// <summary>Resumes the lesson on the Continue card.</summary>
    [RelayCommand]
    private async Task ContinueAsync()
    {
        if (ContinueCard is not { } card)
        {
            return;
        }

        // TODO: navigate to the player (route registered once PlayerPage exists, stage 2).
        // The player is modal and outside the tab bar (UXMap §2).
        await Task.CompletedTask.ConfigureAwait(true);
    }

    /// <summary>Opens a course page from the recent strip.</summary>
    [RelayCommand]
    private async Task OpenCourseAsync(CourseCard? card)
    {
        if (card is null)
        {
            return;
        }

        // TODO: navigate to the course page (stage 1, UXMap §3.3), same reason as above.
        await Task.CompletedTask.ConfigureAwait(true);
    }

    private static TaskItem ToTaskItem(LearningTask task)
    {
        var hasProgress = task.TargetValue > 0;
        var percent = hasProgress ? Math.Clamp(task.CompletedValue / task.TargetValue, 0, 1) : 0;

        return new TaskItem(
            task.Id,
            task.Title,
            task.Source == TaskSourceType.Automatic ? "Automatic" : "My task",
            task.Source == TaskSourceType.Automatic,
            hasProgress,
            percent,
            hasProgress ? $"{FormatNumber(task.CompletedValue)}/{FormatNumber(task.TargetValue)}" : string.Empty,
            task.DueDate is { } due ? $"by {due:d MMMM}" : null,
            task.IsCompleted);
    }

    private static CourseCard ToCard(Course course, ProgressSnapshot snapshot) => new(
        course.Id,
        course.Title,
        BuildSubtitle(course),
        snapshot.Percent,
        FormatPercent(snapshot.Percent),
        $"{snapshot.CompletedLessons} of {snapshot.TotalLessons} lessons",
        course.CoverPath,
        !string.IsNullOrWhiteSpace(course.CoverPath));

    internal static string BuildSubtitle(Course course)
    {
        var author = string.IsNullOrWhiteSpace(course.Author) ? null : course.Author;

        return author ?? (course.Status == CourseStatus.Completed ? "Completed" : "Course");
    }

    internal static string FormatPercent(double fraction) =>
        $"{Math.Round(Math.Clamp(fraction, 0, 1) * 100)}%";

    internal static string FormatNumber(double value) =>
        value == Math.Floor(value) ? ((int)value).ToString() : value.ToString("0.#");

    private void NotifyDerived()
    {
        OnPropertyChanged(nameof(IsLibraryEmpty));
        OnPropertyChanged(nameof(HasContinue));
        OnPropertyChanged(nameof(HasTodayTasks));
        OnPropertyChanged(nameof(StreakText));
        OnPropertyChanged(nameof(TodayTasksSummary));
    }

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        if (e.PropertyName == nameof(IsLoaded))
        {
            NotifyDerived();
        }
    }
}

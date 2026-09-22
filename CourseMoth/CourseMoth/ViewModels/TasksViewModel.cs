// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CourseMoth.Core.Abstractions;
using CourseMoth.Core.Domain;
using CourseMoth.Models;

namespace CourseMoth.ViewModels;

/// <summary>
/// Tasks (UXMap §3.7) — segmented Today / Upcoming / Done.
///
/// The UXMap also lists an "All" segment in §3.7's heading and omits it from the diagram; the
/// diagram is the more specific statement, so the three segments shown there are what is built.
/// TODO: confirm which of the two is intended.
/// </summary>
public sealed partial class TasksViewModel : ViewModelBase
{
    private readonly ITaskRepository _tasks;
    private readonly ITaskEvaluator _evaluator;
    private readonly IClock _clock;

    private readonly List<TaskItem> _today = [];
    private readonly List<TaskItem> _upcoming = [];
    private readonly List<TaskItem> _done = [];

    public TasksViewModel(ITaskRepository tasks, ITaskEvaluator evaluator, IClock clock)
    {
        _tasks = tasks;
        _evaluator = evaluator;
        _clock = clock;
    }

    public string Title => "Tasks";

    public ObservableCollection<TaskItem> Items { get; } = [];

    public IReadOnlyList<TaskSegment> Segments { get; } =
        [TaskSegment.Today, TaskSegment.Upcoming, TaskSegment.Done];

    [ObservableProperty]
    private TaskSegment _selectedSegment = TaskSegment.Today;

    public bool IsEmpty => IsLoaded && !HasError && Items.Count == 0;

    public string EmptyTitle => SelectedSegment switch
    {
        TaskSegment.Today => "No tasks today",
        TaskSegment.Upcoming => "Nothing scheduled",
        _ => "Nothing completed yet",
    };

    /// <summary>UXMap §4: an empty task list offers to create one rather than only being empty.</summary>
    public string EmptyMessage => SelectedSegment switch
    {
        TaskSegment.Today => "Create a task, or turn on automatic tasks in Settings to have the app plan your day.",
        TaskSegment.Upcoming => "Tasks with a deadline or a repeat rule will appear here.",
        _ => "Tasks you complete will be listed here.",
    };

    protected override async Task LoadCoreAsync(CancellationToken ct)
    {
        var today = _clock.StudyDateFor(DateTimeOffset.Now, new DayBoundaryOptions());

        // Recompute the day's automatic tasks before reading them back: the streak and the
        // "1 of 2 tasks" figure are only meaningful against an evaluated day (UXMap §3.1).
        await _evaluator.GenerateAutomaticTasksAsync(today, ct).ConfigureAwait(true);
        await _evaluator.EvaluateAsync(today, ct).ConfigureAwait(true);

        var todaysTasks = await _tasks.ListForDateAsync(today, ct).ConfigureAwait(true);

        _today.Clear();
        _upcoming.Clear();
        _done.Clear();

        foreach (var task in todaysTasks)
        {
            _today.Add(ToItem(task));
        }

        ApplySegment();
    }

    partial void OnSelectedSegmentChanged(TaskSegment value) => ApplySegment();

    private void ApplySegment()
    {
        var source = SelectedSegment switch
        {
            TaskSegment.Upcoming => _upcoming,
            TaskSegment.Done => _done,
            _ => _today,
        };

        Items.Clear();

        foreach (var item in source)
        {
            Items.Add(item);
        }

        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(EmptyTitle));
        OnPropertyChanged(nameof(EmptyMessage));
    }

    /// <summary>Marks a task done by hand (UXMap §3.7, "complete manually").</summary>
    [RelayCommand]
    private async Task CompleteTaskAsync(TaskItem? item)
    {
        if (item is null || item.IsCompleted)
        {
            return;
        }

        var task = await _tasks.GetAsync(item.Id).ConfigureAwait(true);

        if (task is null)
        {
            return;
        }

        task.IsCompleted = true;
        task.CompletedAt = _clock.UtcNow;
        task.CompletedValue = task.TargetValue;

        await _tasks.UpdateAsync(task).ConfigureAwait(true);
        await LoadAsync().ConfigureAwait(true);
    }

    /// <summary>Creates a user task (UXMap §3.7). The editor sheet is not built yet.</summary>
    [RelayCommand]
    private async Task CreateTaskAsync()
    {
        // TODO: the create-task form (title / course / module / goal / value / repeat / deadline)
        // is specified in UXMap §3.7 but has no screen yet.
        await Task.CompletedTask.ConfigureAwait(true);
    }

    /// <summary>Opens the course or module a task points at.</summary>
    [RelayCommand]
    private async Task OpenTaskAsync(TaskItem? item)
    {
        if (item is null)
        {
            return;
        }

        // TODO: navigate to the course page / player once those routes exist.
        await Task.CompletedTask.ConfigureAwait(true);
    }

    private static TaskItem ToItem(LearningTask task)
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

    private static string FormatNumber(double value) =>
        value == Math.Floor(value) ? ((int)value).ToString() : value.ToString("0.#");
}

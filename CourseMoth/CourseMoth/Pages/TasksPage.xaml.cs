// SPDX-License-Identifier: AGPL-3.0-or-later

using CourseMoth.ViewModels;

namespace CourseMoth.Pages;

public partial class TasksPage : CourseMothPage
{
    public TasksPage(IServiceProvider services)
        : base(services)
    {
        InitializeComponent();

        ViewModel = (TasksViewModel)BindingContext;

        BuildSegments();
    }

    protected override object ViewModel { get; }

    /// <summary>
    /// The segmented control is built here rather than in XAML: MAUI has no segmented control,
    /// and a RadioButton group is the idiomatic stand-in. The buttons come from the ViewModel's
    /// segment list so that list has exactly one definition.
    /// </summary>
    private void BuildSegments()
    {
        var viewModel = (TasksViewModel)ViewModel;

        for (var i = 0; i < viewModel.Segments.Count; i++)
        {
            var segment = viewModel.Segments[i];

            var button = new RadioButton
            {
                Content = segment.ToString(),
                HorizontalOptions = LayoutOptions.Center,
                GroupName = nameof(TasksViewModel.SelectedSegment),
            };

            // A path-based binding rather than the .NET 9 lambda overload: the lambda form is
            // resolved by the source generator, which cannot see a path built inside a loop.
            // The source is named explicitly because the page's BindingContext holds a service
            // provider here, not the ViewModel.
            button.SetBinding(
                RadioButton.IsCheckedProperty,
                new Binding(
                    nameof(TasksViewModel.SelectedSegment),
                    BindingMode.TwoWay,
                    new SegmentEqualsConverter(segment),
                    source: viewModel));

            Segments.Add(button, i);
        }
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        await ((TasksViewModel)ViewModel).LoadAsync();
    }
}

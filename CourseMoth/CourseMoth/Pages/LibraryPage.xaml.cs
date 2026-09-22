// SPDX-License-Identifier: AGPL-3.0-or-later

using CourseMoth.Models;
using CourseMoth.ViewModels;

namespace CourseMoth.Pages;

public partial class LibraryPage : CourseMothPage
{
    public LibraryPage(IServiceProvider services)
        : base(services)
    {
        InitializeComponent();

        ViewModel = (LibraryViewModel)BindingContext;
    }

    protected override object ViewModel { get; }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        await ((LibraryViewModel)ViewModel).LoadAsync();
    }

    /// <summary>
    /// An item tap is handled here rather than by a command binding on the template: reaching the
    /// page's command from inside a DataTemplate needs a relative-source binding, and the command
    /// source is not on the page's BindingContext anyway. One line of code-behind is cheaper than
    /// a binding that has to be threaded through a binding context that holds a service provider.
    /// </summary>
    private async void OnCourseTapped(object? sender, TappedEventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is CourseCard card)
        {
            await ((LibraryViewModel)ViewModel).OpenCourseCommand.ExecuteAsync(card);
        }
    }
}

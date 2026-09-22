// SPDX-License-Identifier: AGPL-3.0-or-later

using CourseMoth.ViewModels;

namespace CourseMoth.Pages;

public partial class HomePage : CourseMothPage
{
    public HomePage(IServiceProvider services)
        : base(services)
    {
        InitializeComponent();

        ViewModel = (HomeViewModel)BindingContext;
    }

    protected override object ViewModel { get; }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        // Reloaded on every appearance: the streak and the task counters are a function of
        // "now", and a tab showing yesterday's numbers would be quietly wrong.
        await ((HomeViewModel)ViewModel).LoadAsync();
    }

    /// <summary>
    /// Opens the folder dialog.
    ///
    /// <para>
    /// This is an event handler rather than a <c>Command</c> binding, and the difference is not
    /// stylistic. With <c>Command="{Binding AddFolderCommand}"</c> a real mouse click did nothing,
    /// while invoking the same command from code opened the dialog and returned a folder — so the
    /// command was fine and the binding was not. This page's <c>BindingContext</c> is a service
    /// provider that <see cref="AppShell"/> passes down (see <see cref="CourseMothPage"/>), not
    /// the ViewModel, which makes a binding on it more fragile than it looks.
    /// </para>
    ///
    /// <para>
    /// The handler reaches the ViewModel through <see cref="HomeViewModel.AddFolderAsync"/>
    /// directly. The command is still generated and still usable; nothing about the ViewModel
    /// changed to accommodate this.
    /// </para>
    /// </summary>
    private async void OnAddFolderClicked(object? sender, EventArgs e)
    {
        if (ViewModel is HomeViewModel viewModel)
        {
            await viewModel.AddFolderAsync();
        }
    }
}

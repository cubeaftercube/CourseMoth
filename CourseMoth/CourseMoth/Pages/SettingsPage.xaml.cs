// SPDX-License-Identifier: AGPL-3.0-or-later

using CourseMoth.ViewModels;

namespace CourseMoth.Pages;

public partial class SettingsPage : CourseMothPage
{
    public SettingsPage(IServiceProvider services)
        : base(services)
    {
        InitializeComponent();

        ViewModel = (SettingsViewModel)BindingContext;
    }

    protected override object ViewModel { get; }
}

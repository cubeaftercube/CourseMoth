// SPDX-License-Identifier: AGPL-3.0-or-later

using CourseMoth.ViewModels;

namespace CourseMoth.Pages;

public partial class DownloadsPage : CourseMothPage
{
    public DownloadsPage(IServiceProvider services)
        : base(services)
    {
        InitializeComponent();

        ViewModel = (DownloadsViewModel)BindingContext;
    }

    protected override object ViewModel { get; }
}

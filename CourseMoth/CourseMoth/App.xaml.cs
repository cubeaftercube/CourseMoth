// SPDX-License-Identifier: AGPL-3.0-or-later

using CourseMoth.Services;
using Microsoft.Extensions.DependencyInjection;

namespace CourseMoth
{
    public partial class App : Application
    {
        private readonly IServiceProvider _services;

        public App(IServiceProvider services)
        {
            _services = services;

            InitializeComponent();
        }

        protected override Window CreateWindow(IActivationState? activationState)
        {
            // Resolved here rather than in the constructor: XAML resources are parsed during
            // InitializeComponent, which runs before the container should be used for anything
            // that depends on them.
            var shell = _services.GetRequiredService<AppShell>();

            // Constructing the theme service applies the persisted light/dark preference to
            // UserAppTheme, so the first window is already themed and the system theme is
            // honoured by default (AppTheme.Unspecified).
            _ = _services.GetRequiredService<IThemeService>();

            return new Window(shell);
        }
    }
}

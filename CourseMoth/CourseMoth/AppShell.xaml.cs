// SPDX-License-Identifier: AGPL-3.0-or-later

using Microsoft.Extensions.DependencyInjection;

namespace CourseMoth
{
    /// <summary>
    /// The application shell. One instance for the life of the app (see MauiProgramExtensions).
    /// </summary>
    public partial class AppShell : Shell
    {
        public AppShell(IServiceProvider services)
        {
            // Pages declared through ContentTemplate are constructed by Shell with
            // Activator.CreateInstance, NOT through the service provider, so constructor
            // injection does not run on that path. Providing the provider as the tabs'
            // BindingContext is what lets each page pull its own ViewModel in its own
            // constructor.
            BindingContext = services;

            InitializeComponent();
        }
    }
}

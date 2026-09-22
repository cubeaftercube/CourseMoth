// SPDX-License-Identifier: AGPL-3.0-or-later

using Microsoft.Extensions.DependencyInjection;

namespace CourseMoth.Pages;

/// <summary>
/// Base for every tab page.
///
/// Shell builds the pages named in a <c>ContentTemplate</c> with <c>Activator.CreateInstance</c>,
/// so constructor injection does not run on them and a page cannot take its ViewModel as a
/// constructor parameter. <see cref="AppShell"/> hands the service provider down as the tabs'
/// BindingContext; each page resolves its ViewModel from there and assigns it to
/// <see cref="BindingContext"/> in the same step, leaving <see cref="ViewModel"/> typed for the
/// code-behind's own use.
///
/// <c>InitializeComponent</c> is called by each derived page, not here: it is generated per page
/// and does not exist on this type.
///
/// The parameterless constructor exists for the XAML designer and throws at runtime — a page
/// built without a provider has no way to reach its ViewModel.
/// </summary>
public abstract class CourseMothPage : ContentPage
{
    protected CourseMothPage(IServiceProvider services)
    {
        BindingContext = PageViewModelFactory.Create(GetType(), services);
    }

    protected CourseMothPage()
    {
        throw new InvalidOperationException(
            $"{GetType().Name} was constructed without a service provider. Pages are built by " +
            "Shell from a ContentTemplate, which does not perform constructor injection; the " +
            "provider is supplied as the BindingContext of the page's tab.");
    }

    /// <summary>The ViewModel this page drives.</summary>
    protected abstract object ViewModel { get; }
}

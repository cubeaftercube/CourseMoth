// SPDX-License-Identifier: AGPL-3.0-or-later

using Microsoft.Extensions.DependencyInjection;

namespace CourseMoth.Pages;

/// <summary>
/// Maps a tab page onto its ViewModel.
///
/// A pair per page, declared in one table rather than scattered as a convention. Everything else
/// in the app resolves through the container; this exists only because Shell constructs
/// ContentTemplate pages outside it, and it keeps that one irregular path in a single place.
/// </summary>
internal static class PageViewModelFactory
{
    private static readonly Dictionary<Type, Type> Map = new()
    {
        [typeof(HomePage)] = typeof(ViewModels.HomeViewModel),
        [typeof(LibraryPage)] = typeof(ViewModels.LibraryViewModel),
        [typeof(TasksPage)] = typeof(ViewModels.TasksViewModel),
        [typeof(DownloadsPage)] = typeof(ViewModels.DownloadsViewModel),
        [typeof(SettingsPage)] = typeof(ViewModels.SettingsViewModel),
    };

    public static object Create(Type pageType, IServiceProvider services)
    {
        if (!Map.TryGetValue(pageType, out var viewModelType))
        {
            throw new InvalidOperationException(
                $"{pageType.Name} has no ViewModel registered in {nameof(PageViewModelFactory)}.");
        }

        return services.GetRequiredService(viewModelType);
    }
}

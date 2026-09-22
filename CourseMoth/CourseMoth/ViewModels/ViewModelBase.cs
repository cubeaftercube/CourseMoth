// SPDX-License-Identifier: AGPL-3.0-or-later

using CommunityToolkit.Mvvm.ComponentModel;

namespace CourseMoth.ViewModels;

/// <summary>
/// Shared state for every tab ViewModel: whether the first load has finished, whether it is
/// still running, and whether the screen should show its empty state.
///
/// The app layer is thin (SystemMap §3): a ViewModel calls a service, shapes the answer for
/// display, and raises a property change. It does not read the file system, does not compute
/// progress and does not parse anything.
/// </summary>
public abstract partial class ViewModelBase : ObservableObject
{
    [ObservableProperty]
    private bool _isBusy;

    /// <summary>
    /// True once <see cref="LoadAsync"/> has completed at least once. Until then the screen
    /// shows a spinner rather than an empty state — "no courses" and "not loaded yet" must not
    /// look the same, or the first frame of every tab claims the library is empty.
    /// </summary>
    [ObservableProperty]
    private bool _isLoaded;

    /// <summary>Set when a service call failed. Null while everything is fine.</summary>
    [ObservableProperty]
    private string? _errorMessage;

    public bool HasError => ErrorMessage is not null;

    /// <summary>Loads whatever this screen displays. Safe to call on every tab appearance.</summary>
    public async Task LoadAsync(CancellationToken ct = default)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        ErrorMessage = null;

        try
        {
            await LoadCoreAsync(ct).ConfigureAwait(false);
            IsLoaded = true;
        }
        catch (OperationCanceledException)
        {
            // Navigating away is not an error and must not surface as one.
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    protected abstract Task LoadCoreAsync(CancellationToken ct);

    partial void OnErrorMessageChanged(string? value) => OnPropertyChanged(nameof(HasError));
}

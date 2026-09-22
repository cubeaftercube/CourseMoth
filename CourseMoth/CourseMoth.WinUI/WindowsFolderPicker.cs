// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using CourseMoth.Services;
using Microsoft.Maui.Platform;
using Windows.Storage.Pickers;

namespace CourseMoth.WinUI;

/// <summary>
/// A real folder picker for Windows.
///
/// <para>
/// <b>Why the WinRT picker and not the shell dialog.</b> An earlier version of this called
/// <c>IFileOpenDialog</c> through hand-written COM declarations and blocked the UI thread while
/// the dialog ran. It looked like it worked — the call returned a folder path — but the dialog
/// was never visible, and the same path came back on every invocation regardless of what the
/// user did. The owner window was obtained with <c>GetActiveWindow()</c>, which says nothing
/// about which window should own a modal, so on a multi-monitor desktop the dialog can be placed
/// on a screen the user is not looking at. A modal that opens off-screen is indistinguishable
/// from a button that does nothing, and it is worse than that: the call still returns, so the
/// app reports success.
/// </para>
///
/// <para>
/// <c>FolderPicker</c> is given the app's own window through <c>IInitializeWithWindow</c>. That
/// is what makes the dialog modal to the right window and positions it over it, and it is the
/// documented way to show a picker from a desktop app that has no package identity.
/// </para>
/// </summary>
public sealed class WindowsFolderPicker : IFolderPicker
{
    public async Task<string?> PickAsync(CancellationToken ct = default)
    {
        var picker = new FolderPicker
        {
            SuggestedStartLocation = PickerLocationId.ComputerFolder,
        };

        // Required, and not obviously so: the picker validates its own state before it will open
        // and refuses to run with an empty filter list, even though a folder picker is not
        // filtering anything. Without this line it throws
        // "Для свойства FileTypeFilters должен быть задан хотя бы один фильтр типа файла."
        // and no dialog is ever created.
        //
        // The wildcard is the standard incantation for this case. It does not restrict what the
        // user can pick, because the picker selects folders regardless.
        picker.FileTypeFilter.Add("*");

        // Without an owner the picker is not modal to anything and Windows places it wherever it
        // likes — on a multi-monitor desktop that is frequently a screen the user is not looking
        // at, which is indistinguishable from the button doing nothing.
        //
        // This is deliberately not guarded by `if (hwnd != IntPtr.Zero)`. Skipping the call
        // silently is how the previous version failed: the dialog was created without an owner
        // and the app reported nothing. A missing handle is a real fault and has to surface.
        var hwnd = GetAppWindowHandle();
        if (hwnd == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                "The app window has no handle yet, so the folder dialog cannot be given an owner. " +
                "This normally means the picker was invoked before the window was shown.");
        }

        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

        var folder = await picker.PickSingleFolderAsync().AsTask(ct).ConfigureAwait(true);

        return folder?.Path;
    }

    /// <summary>
    /// The window handle for the app's main window, or <see cref="IntPtr.Zero"/> when there is
    /// not one yet.
    ///
    /// Resolved through MAUI's current <see cref="Window"/> rather than through a captured handle:
    /// the app can be restarted, and a handle captured at startup is a handle to a dead window.
    /// </summary>
    private static IntPtr GetAppWindowHandle()
    {
        if (Application.Current?.Windows.FirstOrDefault() is not { } window)
        {
            return IntPtr.Zero;
        }

        // The platform view is the route to the handle. It is available by the time a button can
        // be clicked; if it is not, that is a real fault and the caller throws rather than
        // opening a dialog with no owner.
        return window.Handler?.PlatformView is Microsoft.UI.Xaml.Window native
            ? WinRT.Interop.WindowNative.GetWindowHandle(native)
            : IntPtr.Zero;
    }
}

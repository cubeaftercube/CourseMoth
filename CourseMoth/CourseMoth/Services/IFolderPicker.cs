// SPDX-License-Identifier: AGPL-3.0-or-later

namespace CourseMoth.Services;

/// <summary>
/// Asks the user for a folder.
///
/// The "Add folder" action on Home is a platform dialog, which is exactly the kind of thing a
/// ViewModel must not call directly.
///
/// <para>
/// <b>This interface has no implementation in this project, and that is deliberate.</b> The
/// shared project targets <c>net10.0</c>, so it cannot reach a platform API, and every real
/// folder picker <i>is</i> one — MAUI has no cross-platform folder picker. Essentials offers
/// <c>FilePicker</c>, which selects files, not folders.
/// </para>
///
/// <para>
/// An earlier version of this file wrapped <c>FilePicker</c> and returned the picked file's
/// parent directory. That is not a folder picker: it makes the user hunt for a file inside the
/// folder they actually want, and it cannot select a folder that has no files directly in it.
/// It also failed silently, which is why it survived a tab-by-tab check.
/// </para>
///
/// Each head registers the real implementation through
/// <see cref="MauiProgramExtensions.UsePlatformServices"/>. A head that forgets fails loudly at
/// first use rather than quietly doing nothing.
/// </summary>
public interface IFolderPicker
{
    /// <summary>Returns the chosen path, or <c>null</c> when the user cancelled.</summary>
    Task<string?> PickAsync(CancellationToken ct = default);
}

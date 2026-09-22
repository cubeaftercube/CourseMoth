// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CourseMoth.Models;

namespace CourseMoth.ViewModels;

/// <summary>
/// Downloads (UXMap §3.8) — a placeholder, deliberately.
///
/// Downloads are stage 7; until then there is nothing to download and the tab renders its state
/// honestly rather than showing a fake queue. The tab exists now because five tabs are the
/// agreed shape of the shell (UXMap §2); the UXMap notes the tab may be omitted entirely before
/// stage 7 — TODO: confirm with the author whether the tab should be visible this early.
///
/// The counts below are zero by construction, not by coincidence: no download service is
/// injected, so nothing can report a non-zero figure.
/// </summary>
public sealed partial class DownloadsViewModel : ViewModelBase
{
    public string Title => "Downloads";

    /// <summary>Empty until the download manager exists — see the class remarks.</summary>
    public ObservableCollection<TaskItem> Active { get; } = [];

    public bool HasAnything => Active.Count > 0;

    public string EmptyTitle => "Downloads arrive in stage 7";

    public string EmptyMessage =>
        "CourseMoth plays courses from the folder you added, so nothing needs downloading yet. " +
        "Downloading from a server or WebDAV, with a queue, pause and resume, comes later.";

    public string StageNote => "Stage 7 · not implemented yet";

    protected override Task LoadCoreAsync(CancellationToken ct)
    {
        // Nothing to load: the download manager (IDownloadManager, SystemMap §3) is stage 7.
        // TODO: register IDownloadManager once CourseMoth.Downloads lands, then project its
        // queue into Active / Queued / Completed / Errors here.
        Active.Clear();

        OnPropertyChanged(nameof(HasAnything));

        return Task.CompletedTask;
    }
}

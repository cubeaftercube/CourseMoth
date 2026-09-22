// SPDX-License-Identifier: AGPL-3.0-or-later

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CourseMoth.Models;
using CourseMoth.Services;

namespace CourseMoth.ViewModels;

/// <summary>One collapsible group on the Settings screen.</summary>
/// <param name="Name">Group heading, as named in UXMap §3.9.</param>
/// <param name="Rows">The settings inside it.</param>
public sealed record SettingGroup(string Name, IReadOnlyList<SettingRow> Rows);

/// <summary>
/// Settings (UXMap §3.9).
///
/// The rules this screen follows:
/// <list type="bullet">
/// <item>Anything implemented is bound to a real property and persisted.</item>
/// <item>Anything not implemented renders disabled with a short note, rather than being drawn as
/// a working control that silently does nothing.</item>
/// <item>Groups whose entire content is unimplemented (Downloads, Server) keep their heading and
/// say so, so the user can see the shape of what is coming without being misled.</item>
/// </list>
///
/// Only Appearance is live today: the theme preference is presentation state and belongs to the
/// app layer. Every other row here is a domain setting — the daily goal, the streak threshold and
/// the day boundary are <c>DayBoundaryOptions</c> in Core, and the player thresholds belong to
/// Core's completion rules. They are not inventing local copies of those rules (SystemMap §4).
/// </summary>
public sealed partial class SettingsViewModel : ViewModelBase
{
    private const string NotYet = "not implemented yet";

    private readonly IThemeService _theme;

    public SettingsViewModel(IThemeService theme)
    {
        _theme = theme;
        _selectedTheme = theme.Current;

        Groups = BuildGroups();
    }

    public string Title => "Settings";

    public IReadOnlyList<SettingGroup> Groups { get; }

    public IReadOnlyList<ThemePreference> ThemeOptions { get; } =
        [ThemePreference.System, ThemePreference.Light, ThemePreference.Dark];

    /// <summary>The only setting that is actually wired: light / dark / system.</summary>
    [ObservableProperty]
    private ThemePreference _selectedTheme;

    public string VersionText => "CourseMoth 0.0.2";
    public string LicenseText => "AGPL-3.0-or-later";

    protected override Task LoadCoreAsync(CancellationToken ct)
    {
        // The preference is read in the constructor, so the picker is correct on first frame.
        // There is nothing to fetch from a service yet; when user settings land in Core this is
        // where they are read.
        // TODO: load persisted settings from Core's settings service once it exists.
        return Task.CompletedTask;
    }

    partial void OnSelectedThemeChanged(ThemePreference value) => _theme.Apply(value);

    [RelayCommand]
    private async Task OpenRepositoryAsync()
    {
        // TODO: open the project repository (UXMap §3.9, About).
        await Task.CompletedTask.ConfigureAwait(true);
    }

    /// <summary>
    /// The rows, in the order and with the wording of UXMap §3.9. Kept in code rather than XAML
    /// so that the "not implemented yet" notes sit next to the reason they exist.
    /// </summary>
    private static IReadOnlyList<SettingGroup> BuildGroups() =>
    [
        new("Player",
        [
            new SettingRow("Default lesson completion threshold", "90%", NotYet, IsEnabled: false),
            new SettingRow("Autoplay next lesson", "on", NotYet, IsEnabled: false),
            new SettingRow("Default speed", "1.0x", NotYet, IsEnabled: false),
            new SettingRow("Enable subtitles automatically", "off", NotYet, IsEnabled: false),
            new SettingRow("Download over Wi-Fi only", "on", NotYet, IsEnabled: false),
            new SettingRow("Ask on progress conflict", "on", NotYet, IsEnabled: false),
        ]),

        new("Tasks",
        [
            new SettingRow("Enable automatic tasks", "on", NotYet, IsEnabled: false),
            new SettingRow("Daily goal", "2 tasks / 30 minutes", NotYet, IsEnabled: false),
            new SettingRow("Streak threshold", "1 lesson", NotYet, IsEnabled: false),
            new SettingRow("Start of the study day", "04:00", NotYet, IsEnabled: false),
        ]),

        new("Downloads", [new SettingRow("Downloads", "Stage 7", NotYet, IsEnabled: false)]),

        new("Appearance",
        [
            // The one live row. Accent colour is not implemented.
            new SettingRow("Theme"),
            new SettingRow("Accent color", null, NotYet, IsEnabled: false),
        ]),

        new("Data",
        [
            new SettingRow("Export data", null, NotYet, IsEnabled: false),
            new SettingRow("Import data", null, NotYet, IsEnabled: false),
            new SettingRow("Backup", null, NotYet, IsEnabled: false),
            new SettingRow("Clear cache", null, NotYet, IsEnabled: false),
        ]),

        new("Sources",
        [
            // UXMap §3.9: the list of LibrarySource with adding and removing. The stage-1 import
            // path (a folder) is not even wired yet, so there is nothing to list.
            new SettingRow("Library sources", null, NotYet, IsEnabled: false),
        ]),

        new("Server",
        [
            new SettingRow("Enable server", "off", "Stage 6 · not implemented yet", IsEnabled: false),
        ]),

        new("About",
        [
            new SettingRow("Version", "0.0.2"),
            new SettingRow("License", "AGPL-3.0-or-later"),
            new SettingRow("Repository", null, NotYet, IsEnabled: false),
        ]),
    ];
}

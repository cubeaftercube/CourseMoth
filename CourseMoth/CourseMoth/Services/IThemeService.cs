// SPDX-License-Identifier: AGPL-3.0-or-later

namespace CourseMoth.Services;

/// <summary>A user-facing appearance preference. Maps onto <see cref="AppTheme"/>.</summary>
public enum ThemePreference
{
    /// <summary>Follow the operating system.</summary>
    System,

    Light,

    Dark,
}

/// <summary>
/// Reads and writes the app-wide light/dark preference (UXMap §3.9, "Appearance").
///
/// This is presentation state, not domain state, so it lives in the app layer rather than in
/// Core. It is an interface so that a ViewModel never touches <c>Preferences</c> or
/// <c>Application.Current</c> directly.
/// </summary>
public interface IThemeService
{
    ThemePreference Current { get; }

    /// <summary>Applies the preference immediately and persists it.</summary>
    void Apply(ThemePreference preference);
}

/// <summary>Theme preference backed by MAUI's <c>UserAppTheme</c> and <c>Preferences</c>.</summary>
public sealed class ThemeService : IThemeService
{
    private const string PreferenceKey = "CourseMoth.Appearance.Theme";

    private ThemePreference _current;

    public ThemeService()
    {
        _current = Read();
        ApplyToApplication(_current);
    }

    public ThemePreference Current => _current;

    public void Apply(ThemePreference preference)
    {
        _current = preference;
        Preferences.Default.Set(PreferenceKey, preference.ToString());
        ApplyToApplication(preference);
    }

    private static ThemePreference Read()
    {
        var stored = Preferences.Default.Get(PreferenceKey, nameof(ThemePreference.System));

        return Enum.TryParse<ThemePreference>(stored, ignoreCase: true, out var parsed)
            ? parsed
            : ThemePreference.System;
    }

    /// <summary>
    /// <see cref="AppTheme.Unspecified"/> is what "follow the system" means to MAUI — it is not
    /// a synonym for light. Setting the colours by hand here would fight the AppThemeBinding
    /// palette in Resources/Styles.
    /// </summary>
    private static void ApplyToApplication(ThemePreference preference)
    {
        if (Application.Current is not { } application)
        {
            return;
        }

        application.UserAppTheme = preference switch
        {
            ThemePreference.Light => AppTheme.Light,
            ThemePreference.Dark => AppTheme.Dark,
            _ => AppTheme.Unspecified,
        };
    }
}

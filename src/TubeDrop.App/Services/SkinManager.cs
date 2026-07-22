using System.Windows;
using TubeDrop.Core.Settings;
using Wpf.Ui.Appearance;

namespace TubeDrop.App.Services;

public interface ISkinManager
{
    SkinId CurrentSkin { get; }
    ThemeMode CurrentTheme { get; }

    /// <summary>Skins that offer a light variant; Noir is dark-only (§12.1).</summary>
    bool SupportsLight(SkinId skin);

    /// <summary>Applies skin + theme by swapping the merged dictionary — no restart.</summary>
    void Apply(SkinId skin, ThemeMode theme);
}

/// <summary>
/// Runtime skin switching (§12.1): keeps a single "skin slot" in
/// Application.Resources.MergedDictionaries and replaces it in place, so every
/// view bound with DynamicResource restyles live. Also aligns the WPF-UI theme
/// so built-in controls follow.
/// </summary>
public sealed class SkinManager : ISkinManager
{
    private readonly Application _application;
    private ResourceDictionary? _currentSlot;

    public SkinManager(Application application)
    {
        _application = application;
    }

    public SkinId CurrentSkin { get; private set; } = SkinId.Mix;
    public ThemeMode CurrentTheme { get; private set; } = ThemeMode.Dark;

    public bool SupportsLight(SkinId skin) => skin != SkinId.Noir;

    public void Apply(SkinId skin, ThemeMode theme)
    {
        // Noir is dark-only — silently coerce (the UI also hides the toggle).
        if (!SupportsLight(skin))
        {
            theme = ThemeMode.Dark;
        }

        var uri = new Uri($"pack://application:,,,/TubeDrop;component/Skins/{skin}.{theme}.xaml",
            UriKind.Absolute);
        var dictionary = new ResourceDictionary { Source = uri };

        var merged = _application.Resources.MergedDictionaries;
        if (_currentSlot is not null)
        {
            merged.Remove(_currentSlot);
        }

        merged.Add(dictionary);
        _currentSlot = dictionary;

        ApplicationThemeManager.Apply(
            theme == ThemeMode.Dark ? ApplicationTheme.Dark : ApplicationTheme.Light);

        CurrentSkin = skin;
        CurrentTheme = theme;
    }
}

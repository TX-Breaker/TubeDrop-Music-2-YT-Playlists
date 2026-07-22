using System.ComponentModel;
using System.Globalization;
using System.Resources;

namespace TubeDrop.App.Services;

/// <summary>
/// Runtime-switchable i18n (§2, §9). Backed by embedded resx (EN neutral + IT).
/// Exposes a string indexer so XAML can bind <c>{Binding [Key], Source={StaticResource Loc}}</c>;
/// changing the culture raises a blanket indexer change so the whole UI re-reads.
/// </summary>
public sealed class LocalizationService : INotifyPropertyChanged
{
    private readonly ResourceManager _resourceManager = new(
        "TubeDrop.App.Resources.Strings", typeof(LocalizationService).Assembly);

    private CultureInfo _culture = CultureInfo.CurrentUICulture;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Indexer used by XAML bindings; missing keys surface as "!Key!" for visibility.</summary>
    public string this[string key] =>
        _resourceManager.GetString(key, _culture) ?? $"!{key}!";

    public CultureInfo Culture => _culture;

    /// <summary>Resolves "auto"/"it"/"en" to a culture and applies it app-wide.</summary>
    public void SetLanguage(string language)
    {
        var culture = language switch
        {
            "it" => new CultureInfo("it"),
            "en" => new CultureInfo("en"),
            _ => DefaultCulture(),
        };

        _culture = culture;
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.CurrentCulture = culture;
        // Empty name → WPF/CommunityToolkit convention for "all indexer values changed".
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
    }

    /// <summary>OS culture, narrowed to a language we ship (IT), else EN.</summary>
    private static CultureInfo DefaultCulture()
    {
        var os = CultureInfo.InstalledUICulture.TwoLetterISOLanguageName;
        return os == "it" ? new CultureInfo("it") : new CultureInfo("en");
    }
}

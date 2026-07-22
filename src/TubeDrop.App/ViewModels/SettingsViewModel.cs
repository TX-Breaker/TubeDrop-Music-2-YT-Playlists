using CommunityToolkit.Mvvm.ComponentModel;
using TubeDrop.App.Services;
using TubeDrop.Core.Matching;
using TubeDrop.Core.Playlists;
using TubeDrop.Core.Settings;

namespace TubeDrop.App.ViewModels;

/// <summary>
/// Binds every §12 setting. Skin/theme changes apply live via ISkinManager;
/// all changes persist through ISettingsStore. i18n label strings arrive in M9.
/// </summary>
public partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsStore _settings;
    private readonly ISkinManager _skinManager;
    private bool _loading;

    [ObservableProperty] private SkinId _skin;
    [ObservableProperty] private bool _isLightTheme;
    [ObservableProperty] private bool _lightSupported = true;
    [ObservableProperty] private SearchScope _searchScope;
    [ObservableProperty] private double _scoreThreshold;
    [ObservableProperty] private bool _aggressiveMode;
    [ObservableProperty] private bool _onnxRefinerEnabled;
    [ObservableProperty] private bool _cloudRefinerEnabled;
    [ObservableProperty] private string _cloudRefinerApiKey = "";
    [ObservableProperty] private PlaylistPrivacy _defaultPrivacy;
    [ObservableProperty] private RateLimitProfile _rateLimitProfile;
    [ObservableProperty] private UpdateChannel _updateChannel;
    [ObservableProperty] private string _language = "auto";

    public Array Skins { get; } = Enum.GetValues<SkinId>();
    public Array Scopes { get; } = Enum.GetValues<SearchScope>();
    public Array Privacies { get; } = Enum.GetValues<PlaylistPrivacy>();
    public Array RateProfiles { get; } = Enum.GetValues<RateLimitProfile>();
    public Array Channels { get; } = Enum.GetValues<UpdateChannel>();
    public string[] Languages { get; } = ["auto", "it", "en"];

    public SettingsViewModel(ISettingsStore settings, ISkinManager skinManager)
    {
        _settings = settings;
        _skinManager = skinManager;
        LoadFrom(settings.Current);
    }

    private void LoadFrom(AppSettings s)
    {
        _loading = true;
        Skin = s.Skin;
        IsLightTheme = s.Theme == ThemeMode.Light;
        LightSupported = _skinManager.SupportsLight(s.Skin);
        SearchScope = s.SearchScope;
        ScoreThreshold = s.ScoreThreshold;
        AggressiveMode = s.AggressiveMode;
        OnnxRefinerEnabled = s.OnnxRefinerEnabled;
        CloudRefinerEnabled = s.CloudRefinerEnabled;
        CloudRefinerApiKey = s.CloudRefinerApiKey;
        DefaultPrivacy = s.DefaultPrivacy;
        RateLimitProfile = s.RateLimitProfile;
        UpdateChannel = s.UpdateChannel;
        Language = s.Language;
        _loading = false;
    }

    partial void OnSkinChanged(SkinId value)
    {
        LightSupported = _skinManager.SupportsLight(value);
        if (!LightSupported)
        {
            IsLightTheme = false;
        }

        ApplySkin();
        Persist(s => s with { Skin = value, Theme = IsLightTheme ? ThemeMode.Light : ThemeMode.Dark });
    }

    partial void OnIsLightThemeChanged(bool value)
    {
        ApplySkin();
        Persist(s => s with { Theme = value ? ThemeMode.Light : ThemeMode.Dark });
    }

    partial void OnSearchScopeChanged(SearchScope value) => Persist(s => s with { SearchScope = value });
    partial void OnScoreThresholdChanged(double value) => Persist(s => s with { ScoreThreshold = value });
    partial void OnAggressiveModeChanged(bool value) => Persist(s => s with { AggressiveMode = value });
    partial void OnOnnxRefinerEnabledChanged(bool value) => Persist(s => s with { OnnxRefinerEnabled = value });
    partial void OnCloudRefinerEnabledChanged(bool value) => Persist(s => s with { CloudRefinerEnabled = value });
    partial void OnCloudRefinerApiKeyChanged(string value) => Persist(s => s with { CloudRefinerApiKey = value });
    partial void OnDefaultPrivacyChanged(PlaylistPrivacy value) => Persist(s => s with { DefaultPrivacy = value });
    partial void OnRateLimitProfileChanged(RateLimitProfile value) => Persist(s => s with { RateLimitProfile = value });
    partial void OnUpdateChannelChanged(UpdateChannel value) => Persist(s => s with { UpdateChannel = value });
    partial void OnLanguageChanged(string value) => Persist(s => s with { Language = value });

    private void ApplySkin() =>
        _skinManager.Apply(Skin, IsLightTheme ? ThemeMode.Light : ThemeMode.Dark);

    private void Persist(Func<AppSettings, AppSettings> mutate)
    {
        if (!_loading)
        {
            _settings.Update(mutate);
        }
    }
}

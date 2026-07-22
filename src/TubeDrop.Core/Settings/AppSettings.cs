using TubeDrop.Core.Matching;
using TubeDrop.Core.Playlists;

namespace TubeDrop.Core.Settings;

public enum SkinId
{
    Mix,
    Classic,
    Noir,
}

public enum ThemeMode
{
    Dark,
    Light,
}

public enum RateLimitProfile
{
    Gentle,
    Normal,
    Fast,
}

public enum UpdateChannel
{
    Stable,
    Prerelease,
}

/// <summary>
/// All persisted user settings (§12). Defaults match the spec: dark Mix skin,
/// YTM songs scope, 0.75 threshold, private playlists, ONNX capability on but
/// model not downloaded, cloud refiner off.
/// </summary>
public sealed record AppSettings
{
    /// <summary>UI language: "auto" follows OS culture, else "it" / "en".</summary>
    public string Language { get; init; } = "auto";

    public SkinId Skin { get; init; } = SkinId.Mix;
    public ThemeMode Theme { get; init; } = ThemeMode.Dark;

    public SearchScope SearchScope { get; init; } = SearchScope.YtmSongs;
    public double ScoreThreshold { get; init; } = 0.75;
    public bool AggressiveMode { get; init; }

    public bool OnnxRefinerEnabled { get; init; } = true;
    public bool CloudRefinerEnabled { get; init; }
    public string CloudRefinerApiKey { get; init; } = "";
    public string CloudRefinerModel { get; init; } = "claude-sonnet-5";

    /// <summary>Acoustic fingerprint recognition (AcoustID + Chromaprint) to fix poor tags/filenames.</summary>
    public bool AcoustIdEnabled { get; init; }
    public string AcoustIdApiKey { get; init; } = "";

    public PlaylistPrivacy DefaultPrivacy { get; init; } = PlaylistPrivacy.Private;
    public RateLimitProfile RateLimitProfile { get; init; } = RateLimitProfile.Normal;
    public UpdateChannel UpdateChannel { get; init; } = UpdateChannel.Stable;
}

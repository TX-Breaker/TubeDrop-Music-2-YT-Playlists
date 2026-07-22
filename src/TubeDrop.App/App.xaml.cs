using System.IO;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using TubeDrop.App.ViewModels;

namespace TubeDrop.App;

public partial class App : Application
{
    private readonly IHost _host;

    /// <summary>Command-line args captured in Program.Main (WPF does not forward them with a custom entry point).</summary>
    public string[] StartupArgs { get; init; } = [];

    public App()
    {
        _host = Host.CreateDefaultBuilder()
            .UseSerilog((_, loggerConfiguration) =>
            {
                var logDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "TubeDrop", "logs");
                loggerConfiguration
                    .MinimumLevel.Debug()
                    .WriteTo.File(
                        Path.Combine(logDir, "tubedrop-.log"),
                        rollingInterval: RollingInterval.Day,
                        retainedFileCountLimit: 14);
            })
            .ConfigureServices(services =>
            {
                services.AddSingleton<Core.Ingestion.ITagReader, Core.Ingestion.TagReader>();
                services.AddSingleton<Core.Ingestion.IIngestPipeline, Core.Ingestion.IngestPipeline>();

                services.AddSingleton<Services.AuthService>();
                services.AddSingleton<TubeDrop.InnerTube.Auth.ISessionProvider>(
                    sp => sp.GetRequiredService<Services.AuthService>());

                services.AddSingleton(new System.Net.Http.HttpClient());
                services.AddSingleton<TubeDrop.InnerTube.Http.InnerTubeRateLimiter>();
                services.AddSingleton<TubeDrop.InnerTube.Http.InnerTubeTransport>();
                services.AddSingleton<TubeDrop.InnerTube.Search.ISearchClient,
                    TubeDrop.InnerTube.Search.SearchClient>();
                services.AddSingleton<Core.Matching.ICandidateSearcher,
                    TubeDrop.InnerTube.Search.InnerTubeCandidateSearcher>();
                services.AddSingleton<Core.Playlists.IPlaylistClient,
                    TubeDrop.InnerTube.Playlists.PlaylistClient>();

                var journalPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "TubeDrop", "journal.db");
                services.AddSingleton(new Core.Journal.JournalStore(journalPath));
                services.AddSingleton<Core.Journal.JournaledPlaylistService>();
                services.AddSingleton<Core.Journal.UndoService>();

                // Fallback ladder (§8), resolved in registration order:
                // deterministic -> onnx (no-op no-go) -> cloud (opt-in).
                services.AddSingleton<Core.Matching.Refiners.IModelProvider>(
                    _ => new Core.Matching.Refiners.LocalModelProvider());
                // Cover is the prioritized fallback: after the plain title search,
                // it's tried FIRST, before the deterministic/cloud rungs.
                services.AddSingleton<Core.Matching.IQueryRefiner, Core.Matching.Refiners.CoverImageRefiner>();
                services.AddSingleton<Core.Matching.IQueryRefiner, Core.Matching.Refiners.DeterministicRefiner>();
                services.AddSingleton<Core.Matching.IQueryRefiner, Core.Matching.Refiners.OnnxQueryRefiner>();
                services.AddSingleton<Core.Matching.IQueryRefiner, Core.Matching.Refiners.CloudQueryRefiner>();

                services.AddSingleton<Core.Matching.MatchingEngine>(sp => new Core.Matching.MatchingEngine(
                    sp.GetRequiredService<Core.Matching.ICandidateSearcher>(),
                    sp.GetServices<Core.Matching.IQueryRefiner>()));

                services.AddSingleton<Core.Settings.ISettingsStore>(new Core.Settings.SettingsStore());

                // Acoustic fingerprint recognition (AcoustID + Chromaprint) — opt-in.
                services.AddSingleton<Core.Fingerprint.IAudioFingerprinter>(
                    _ => new Core.Fingerprint.FpcalcFingerprinter());
                services.AddSingleton<Core.Fingerprint.IAcoustIdClient, Core.Fingerprint.AcoustIdClient>();
                // Keyless cover reverse-image lookup (Google Lens via the WebView2 session).
                services.AddSingleton<Core.Cover.ICoverImageLookup, Services.WebView2CoverImageLookup>();

                // Audio fingerprint recognition runs before matching (opt-in, weak tags only).
                services.AddSingleton<Core.Fingerprint.IMetadataEnricher, Core.Fingerprint.AcoustIdEnricher>();
                services.AddSingleton<Services.ISkinManager>(_ => new Services.SkinManager(Current));
                services.AddSingleton<Services.LocalizationService>();
                services.AddSingleton<Services.UpdateService>();
                services.AddSingleton<Services.BatchCoordinator>();

                services.AddSingleton<MainWindow>();
                services.AddSingleton<MainWindowViewModel>();
                services.AddSingleton<HomeViewModel>();
                services.AddSingleton<QueueViewModel>();
                services.AddSingleton<ReportViewModel>();
                services.AddSingleton<ActivityViewModel>();
                services.AddSingleton<SettingsViewModel>();
            })
            .Build();
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        await _host.StartAsync();

        // Dev capture mode (§5 fixture-first): set TUBEDROP_CAPTURE=1 to dump every
        // InnerTube response (sanitized) to %LOCALAPPDATA%\TubeDrop\captures for
        // building/hardening parsers against real authenticated traffic.
        if (Environment.GetEnvironmentVariable("TUBEDROP_CAPTURE") is "1" or "true")
        {
            var captureDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "TubeDrop", "captures");
            _host.Services.GetRequiredService<TubeDrop.InnerTube.Http.InnerTubeTransport>()
                .CaptureDirectory = captureDir;
        }

        // Apply the persisted skin + theme before showing the window (§12.1).
        var settingsStore = _host.Services.GetRequiredService<Core.Settings.ISettingsStore>();
        var settings = settingsStore.Current;
        _host.Services.GetRequiredService<Services.ISkinManager>().Apply(settings.Skin, settings.Theme);

        // Localization (§2, §9): expose as "Loc" for XAML, apply persisted language,
        // re-apply live on settings change.
        var loc = _host.Services.GetRequiredService<Services.LocalizationService>();
        loc.SetLanguage(settings.Language);
        Resources["Loc"] = loc;
        settingsStore.Changed += (_, s) => loc.SetLanguage(s.Language);

        // Apply the rate-limit profile (§12) to the shared limiter, and keep it live.
        var limiter = _host.Services.GetRequiredService<TubeDrop.InnerTube.Http.InnerTubeRateLimiter>();
        void ApplyRateProfile(Core.Settings.RateLimitProfile profile) => limiter.SetInterval(profile switch
        {
            Core.Settings.RateLimitProfile.Gentle => TimeSpan.FromMilliseconds(1200),
            Core.Settings.RateLimitProfile.Fast => TimeSpan.FromMilliseconds(400),
            _ => TimeSpan.FromMilliseconds(700),
        });
        ApplyRateProfile(settings.RateLimitProfile);
        settingsStore.Changed += (_, s) => ApplyRateProfile(s.RateLimitProfile);

        var mainWindow = _host.Services.GetRequiredService<MainWindow>();
        mainWindow.Show();

        // "Open with" / file-association: ingest any real paths passed on the command line (§6).
        var droppedPaths = StartupArgs
            .Where(a => File.Exists(a) || Directory.Exists(a))
            .ToList();
        if (droppedPaths.Count > 0)
        {
            _ = _host.Services.GetRequiredService<HomeViewModel>().AddPathsAsync(droppedPaths);
        }

        // Update check on startup (§13) — best-effort, never blocks the UI.
        _ = _host.Services.GetRequiredService<Services.UpdateService>().CheckAsync();

        base.OnStartup(e);
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        await _host.StopAsync();
        _host.Dispose();

        base.OnExit(e);
    }
}

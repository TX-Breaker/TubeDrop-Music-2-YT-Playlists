using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using TubeDrop.App.Services;
using TubeDrop.Core.Playlists;

namespace TubeDrop.App.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly AuthService _authService;
    private readonly BatchCoordinator _coordinator;
    private readonly ILogger<MainWindowViewModel> _logger;

    private readonly UpdateService _updateService;

    [ObservableProperty] private object _currentViewModel;
    [ObservableProperty] private string _currentSection = "Home";
    [ObservableProperty] private bool _isSignedIn;
    [ObservableProperty] private bool _sessionExpiredBannerOpen;
    [ObservableProperty] private bool _updateBannerOpen;
    [ObservableProperty] private string _updateVersion = "";

    public HomeViewModel Home { get; }
    public QueueViewModel Queue { get; }
    public ReportViewModel Report { get; }
    public ActivityViewModel Activity { get; }
    public SettingsViewModel Settings { get; }

    public MainWindowViewModel(
        HomeViewModel home,
        QueueViewModel queue,
        ReportViewModel report,
        ActivityViewModel activity,
        SettingsViewModel settings,
        AuthService authService,
        BatchCoordinator coordinator,
        UpdateService updateService,
        ILogger<MainWindowViewModel> logger)
    {
        Home = home;
        Queue = queue;
        Report = report;
        Activity = activity;
        Settings = settings;
        _authService = authService;
        _coordinator = coordinator;
        _updateService = updateService;
        _logger = logger;
        _currentViewModel = home;

        _authService.SessionChanged += (_, _) => IsSignedIn = _authService.IsSignedIn;
        _authService.SessionExpired += (_, _) => SessionExpiredBannerOpen = true;
        _updateService.UpdateAvailable += (_, version) =>
        {
            UpdateVersion = version;
            UpdateBannerOpen = true;
        };
        Home.BatchRequested += async (_, request) => await RunBatchAsync(request);
    }

    [RelayCommand]
    private async Task ApplyUpdate()
    {
        UpdateBannerOpen = false;
        await _updateService.ApplyAsync();
    }

    [RelayCommand]
    private void Navigate(string section)
    {
        CurrentSection = section;
        CurrentViewModel = section switch
        {
            "Home" => Home,
            "Queue" => Queue,
            "Report" => Report,
            "Activity" => ActivityRefreshed(),
            "Settings" => Settings,
            _ => Home,
        };
    }

    private object ActivityRefreshed()
    {
        Activity.Refresh();
        return Activity;
    }

    [RelayCommand]
    private void SignIn()
    {
        SessionExpiredBannerOpen = false;
        _authService.SignIn(Application.Current.MainWindow);
        IsSignedIn = _authService.IsSignedIn;
    }

    private async Task RunBatchAsync(StartBatchRequest request)
    {
        if (!_authService.IsSignedIn)
        {
            if (!_authService.SignIn(Application.Current.MainWindow))
            {
                _logger.LogInformation("Batch cancelled — user not signed in");
                return;
            }

            IsSignedIn = true;
        }

        var progressItems = request.Ingest.Tracks.Select(t => new TrackProgress(t)).ToList();
        Queue.Reset(progressItems.Count);
        var rows = progressItems.ToDictionary(p => p, p =>
        {
            var vm = new QueueItemViewModel();
            vm.UpdateFrom(p);
            Queue.Items.Add(vm);
            return vm;
        });

        Navigate("Queue");

        var progress = new Progress<TrackProgress>(p =>
        {
            rows[p].UpdateFrom(p);
            Queue.Completed = progressItems.Count(x =>
                x.Phase is TrackPhase.Added or TrackPhase.Unmatched or TrackPhase.Error);
        });

        var target = new BatchTarget(
            request.CreateNew, request.ExistingPlaylistId,
            request.NewTitle, request.NewDescription, request.Privacy);

        try
        {
            var outcome = await _coordinator.RunAsync(request.Ingest, target, progressItems, progress);
            Queue.Finish(outcome.PlaylistId is not null
                ? $"Done — added to playlist {outcome.PlaylistId}"
                : "Done — nothing matched");
            Report.Build(outcome);
            Navigate("Report");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Batch failed");
            Queue.Finish($"Batch failed: {ex.Message}");
        }
    }
}

using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using TubeDrop.App.Services;
using TubeDrop.Core.Ingestion;
using TubeDrop.Core.Playlists;
using TubeDrop.Core.Settings;

namespace TubeDrop.App.ViewModels;

public sealed record TrackRow(string Display, string Detail);

/// <summary>Raised when the user commits a drop and target to start the batch.</summary>
public sealed record StartBatchRequest(
    IngestResult Ingest,
    bool CreateNew,
    string? ExistingPlaylistId,
    string NewTitle,
    string NewDescription,
    PlaylistPrivacy Privacy);

public partial class HomeViewModel : ObservableObject
{
    private readonly IIngestPipeline _ingestPipeline;
    private readonly ISettingsStore _settings;
    private readonly IPlaylistClient _playlistClient;
    private readonly AuthService _authService;
    private readonly ILogger<HomeViewModel> _logger;

    /// <summary>All paths dropped so far this session — drops accumulate (drop songs one by one).</summary>
    private readonly List<string> _accumulatedPaths = [];

    [ObservableProperty] private string _statusText = "Drop audio files or folders here";
    [ObservableProperty] private bool _isScanning;
    [ObservableProperty] private bool _hasFiles;

    // Target picker (§9)
    [ObservableProperty] private bool _createNew = true;
    [ObservableProperty] private string _playlistName = "";
    [ObservableProperty] private string _playlistDescription = "Created with TubeDrop";
    [ObservableProperty] private PlaylistPrivacy _privacy = PlaylistPrivacy.Private;

    // Append target
    [ObservableProperty] private PlaylistSummary? _selectedExistingPlaylist;
    [ObservableProperty] private bool _isLoadingLibrary;
    [ObservableProperty] private string _libraryHint = "";

    // Folder naming (§9)
    [ObservableProperty] private bool _nameFromFolder;
    [ObservableProperty] private bool _folderNamingAvailable;
    [ObservableProperty] private FolderNameMode _folderMode = FolderNameMode.Master;
    [ObservableProperty] private string? _derivedFolderNote;

    public ObservableCollection<TrackRow> Tracks { get; } = [];
    public ObservableCollection<PlaylistSummary> ExistingPlaylists { get; } = [];
    public Array Privacies { get; } = Enum.GetValues<PlaylistPrivacy>();
    public Array FolderModes { get; } = Enum.GetValues<FolderNameMode>();

    public IngestResult? LastResult { get; private set; }
    public IReadOnlyList<string> LastDroppedPaths => _accumulatedPaths;

    public event EventHandler<StartBatchRequest>? BatchRequested;

    public HomeViewModel(
        IIngestPipeline ingestPipeline,
        ISettingsStore settings,
        IPlaylistClient playlistClient,
        AuthService authService,
        ILogger<HomeViewModel> logger)
    {
        _ingestPipeline = ingestPipeline;
        _settings = settings;
        _playlistClient = playlistClient;
        _authService = authService;
        _logger = logger;
        _privacy = settings.Current.DefaultPrivacy;
    }

    public bool CanStart => HasFiles && !IsScanning &&
        (CreateNew || SelectedExistingPlaylist is not null);

    [RelayCommand]
    private async Task HandleDropAsync(IReadOnlyList<string> paths) => await AddPathsAsync(paths);

    /// <summary>Adds paths to the queue and rescans. Shared by drop, Ctrl+V paste, and "Open with" (§6).</summary>
    public async Task AddPathsAsync(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0)
        {
            return;
        }

        // Accumulate so users can drop songs one at a time.
        _accumulatedPaths.AddRange(paths);
        await RescanAsync();
    }

    private async Task RescanAsync()
    {
        IsScanning = true;
        StatusText = "Scanning…";
        try
        {
            var snapshot = _accumulatedPaths.ToList();
            var result = await Task.Run(() => _ingestPipeline.Ingest(snapshot));
            LastResult = result;

            Tracks.Clear();
            foreach (var track in result.Tracks)
            {
                var display = track.Artist.Length > 0 ? $"{track.Artist} — {track.Title}" : track.Title;
                var duration = TimeSpan.FromSeconds(track.DurationSeconds).ToString(@"m\:ss");
                var origin = track.Origin == TrackMetadataOrigin.FilenameHeuristics ? " · from filename" : "";
                Tracks.Add(new TrackRow(display, $"{duration}{origin} · {Path.GetFileName(track.SourcePath)}"));
            }

            HasFiles = Tracks.Count > 0;
            StatusText = BuildSummary(result);
            UpdateFolderNaming();
            _logger.LogInformation(
                "Ingested {Tracks} tracks ({Duplicates} duplicates, {Skipped} skipped, {Missing} missing, {Errors} errors)",
                result.Tracks.Count, result.DuplicateCount, result.SkippedCount, result.MissingCount, result.ErrorCount);
        }
        finally
        {
            IsScanning = false;
            NotifyStartState();
        }
    }

    [RelayCommand]
    private void Reset()
    {
        _accumulatedPaths.Clear();
        Tracks.Clear();
        LastResult = null;
        HasFiles = false;
        NameFromFolder = false;
        FolderNamingAvailable = false;
        DerivedFolderNote = null;
        PlaylistName = "";
        StatusText = "Drop audio files or folders here";
        NotifyStartState();
    }

    partial void OnCreateNewChanged(bool value)
    {
        NotifyStartState();
        if (!value && ExistingPlaylists.Count == 0)
        {
            _ = LoadLibraryAsync();
        }
    }

    partial void OnSelectedExistingPlaylistChanged(PlaylistSummary? value) => NotifyStartState();

    [RelayCommand]
    private async Task LoadLibraryAsync()
    {
        if (!_authService.IsSignedIn)
        {
            LibraryHint = "Sign in to load your playlists.";
            return;
        }

        IsLoadingLibrary = true;
        LibraryHint = "";
        try
        {
            var playlists = await _playlistClient.GetLibraryPlaylistsAsync();
            ExistingPlaylists.Clear();
            foreach (var playlist in playlists)
            {
                ExistingPlaylists.Add(playlist);
            }

            LibraryHint = ExistingPlaylists.Count == 0 ? "No playlists found in your library." : "";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load library playlists");
            LibraryHint = "Couldn't load your playlists — try again.";
        }
        finally
        {
            IsLoadingLibrary = false;
        }
    }

    partial void OnNameFromFolderChanged(bool value) => UpdateFolderNaming();
    partial void OnFolderModeChanged(FolderNameMode value) => UpdateFolderNaming();

    private void UpdateFolderNaming()
    {
        if (LastResult is null)
        {
            return;
        }

        var derived = FolderNameDeriver.Derive(
            _accumulatedPaths, LastResult.Tracks.Select(t => t.SourcePath).ToList(), FolderMode);

        FolderNamingAvailable = derived.Name is not null;
        if (!FolderNamingAvailable && NameFromFolder)
        {
            NameFromFolder = false; // loose files → toggle auto-disables (§9)
            DerivedFolderNote = "No folder to name after — dropped loose files.";
            return;
        }

        if (NameFromFolder && derived.Name is not null)
        {
            PlaylistName = derived.Name;
            DerivedFolderNote = derived.FellBackToMaster
                ? "Tracks span multiple subfolders — used the master folder name."
                : null;
        }
    }

    [RelayCommand(CanExecute = nameof(CanStart))]
    private void StartBatch()
    {
        if (LastResult is null)
        {
            return;
        }

        var title = PlaylistName.Trim().Length > 0 ? PlaylistName.Trim() : "TubeDrop playlist";
        BatchRequested?.Invoke(this, new StartBatchRequest(
            LastResult, CreateNew,
            CreateNew ? null : SelectedExistingPlaylist?.PlaylistId,
            title, PlaylistDescription, Privacy));
    }

    private void NotifyStartState()
    {
        OnPropertyChanged(nameof(CanStart));
        StartBatchCommand.NotifyCanExecuteChanged();
    }

    private static string BuildSummary(IngestResult result)
    {
        if (result.Tracks.Count == 0)
        {
            return "No audio files found in the drop.";
        }

        var parts = new List<string> { $"{result.Tracks.Count} track(s) ready" };
        if (result.DuplicateCount > 0)
        {
            parts.Add($"{result.DuplicateCount} duplicate(s)");
        }

        if (result.SkippedCount > 0)
        {
            parts.Add($"{result.SkippedCount} skipped");
        }

        if (result.MissingCount > 0)
        {
            parts.Add($"{result.MissingCount} missing");
        }

        if (result.ErrorCount > 0)
        {
            parts.Add($"{result.ErrorCount} unreadable");
        }

        return string.Join(" · ", parts);
    }
}

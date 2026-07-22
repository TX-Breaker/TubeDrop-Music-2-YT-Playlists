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

public sealed record TrackRow(string SourcePath, string Display, string Detail, string? Initials);

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
    private readonly LocalizationService _loc;
    private readonly ILogger<HomeViewModel> _logger;

    /// <summary>All paths dropped so far this session — drops accumulate (drop songs one by one).</summary>
    private readonly List<string> _accumulatedPaths = [];

    /// <summary>Source paths the user removed by hand — excluded from the list and the batch.</summary>
    private readonly HashSet<string> _removedPaths = new(StringComparer.OrdinalIgnoreCase);

    [ObservableProperty] private bool _isScanning;
    [ObservableProperty] private bool _hasFiles;
    [ObservableProperty] private string _summary = "";
    [ObservableProperty] private int _trackCount;

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

    private IngestResult? _lastResult;
    public IReadOnlyList<string> LastDroppedPaths => _accumulatedPaths;

    public event EventHandler<StartBatchRequest>? BatchRequested;

    public HomeViewModel(
        IIngestPipeline ingestPipeline,
        ISettingsStore settings,
        IPlaylistClient playlistClient,
        AuthService authService,
        LocalizationService loc,
        ILogger<HomeViewModel> logger)
    {
        _ingestPipeline = ingestPipeline;
        _settings = settings;
        _playlistClient = playlistClient;
        _authService = authService;
        _loc = loc;
        _logger = logger;
        _privacy = settings.Current.DefaultPrivacy;

        // Re-emit localized/computed labels when the UI language changes at runtime.
        _loc.PropertyChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(StartButtonText));
            if (HasFiles)
            {
                RebuildSummary();
            }
        };
    }

    public bool CanStart => HasFiles && !IsScanning &&
        (CreateNew || SelectedExistingPlaylist is not null);

    /// <summary>Localized primary-button label — differs for create vs append.</summary>
    public string StartButtonText => CreateNew ? _loc["Home_Start"] : _loc["Home_StartAppend"];

    [RelayCommand]
    private async Task HandleDropAsync(IReadOnlyList<string> paths) => await AddPathsAsync(paths);

    /// <summary>Adds paths to the queue and rescans. Shared by drop, Ctrl+V paste, and "Open with" (§6).</summary>
    public async Task AddPathsAsync(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0)
        {
            return;
        }

        _accumulatedPaths.AddRange(paths);
        await RescanAsync();
    }

    private async Task RescanAsync()
    {
        IsScanning = true;
        try
        {
            var snapshot = _accumulatedPaths.ToList();
            _lastResult = await Task.Run(() => _ingestPipeline.Ingest(snapshot));
            RebuildTrackList();
            UpdateFolderNaming();
            _logger.LogInformation(
                "Ingested {Tracks} tracks ({Duplicates} duplicates, {Skipped} skipped, {Missing} missing, {Errors} errors)",
                _lastResult.Tracks.Count, _lastResult.DuplicateCount, _lastResult.SkippedCount,
                _lastResult.MissingCount, _lastResult.ErrorCount);
        }
        finally
        {
            IsScanning = false;
            NotifyStartState();
        }
    }

    private void RebuildTrackList()
    {
        Tracks.Clear();
        if (_lastResult is null)
        {
            HasFiles = false;
            TrackCount = 0;
            return;
        }

        foreach (var track in _lastResult.Tracks.Where(t => !_removedPaths.Contains(t.SourcePath)))
        {
            var display = track.Artist.Length > 0 ? $"{track.Artist} — {track.Title}" : track.Title;
            var duration = TimeSpan.FromSeconds(track.DurationSeconds).ToString(@"m\:ss");
            var origin = track.Origin == TrackMetadataOrigin.FilenameHeuristics ? " · from filename" : "";
            Tracks.Add(new TrackRow(track.SourcePath, display,
                $"{duration}{origin} · {Path.GetFileName(track.SourcePath)}", Initials(track)));
        }

        TrackCount = Tracks.Count;
        HasFiles = Tracks.Count > 0;
        RebuildSummary();
    }

    private void RebuildSummary()
    {
        if (_lastResult is null || TrackCount == 0)
        {
            Summary = _loc["Home_NoAudio"];
            return;
        }

        var parts = new List<string> { string.Format(_loc["Home_TracksReady"], TrackCount) };
        if (_lastResult.DuplicateCount > 0)
        {
            parts.Add($"{_lastResult.DuplicateCount} × dup");
        }

        if (_lastResult.SkippedCount > 0)
        {
            parts.Add($"{_lastResult.SkippedCount} skip");
        }

        if (_lastResult.ErrorCount > 0)
        {
            parts.Add($"{_lastResult.ErrorCount} err");
        }

        Summary = string.Join(" · ", parts);
    }

    [RelayCommand]
    private void RemoveTrack(TrackRow? row)
    {
        if (row is null)
        {
            return;
        }

        _removedPaths.Add(row.SourcePath);
        RebuildTrackList();
        UpdateFolderNaming();
        NotifyStartState();
    }

    [RelayCommand]
    private void Reset()
    {
        _accumulatedPaths.Clear();
        _removedPaths.Clear();
        _lastResult = null;
        Tracks.Clear();
        HasFiles = false;
        TrackCount = 0;
        NameFromFolder = false;
        FolderNamingAvailable = false;
        DerivedFolderNote = null;
        PlaylistName = "";
        Summary = "";
        NotifyStartState();
    }

    partial void OnCreateNewChanged(bool value)
    {
        OnPropertyChanged(nameof(StartButtonText));
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
        if (_lastResult is null)
        {
            return;
        }

        var effectivePaths = _lastResult.Tracks
            .Where(t => !_removedPaths.Contains(t.SourcePath))
            .Select(t => t.SourcePath)
            .ToList();
        var derived = FolderNameDeriver.Derive(_accumulatedPaths, effectivePaths, FolderMode);

        FolderNamingAvailable = derived.Name is not null;
        if (!FolderNamingAvailable && NameFromFolder)
        {
            NameFromFolder = false;
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
        if (_lastResult is null)
        {
            return;
        }

        // Honour manual removals: the batch sees only the kept tracks.
        var keptTracks = _lastResult.Tracks.Where(t => !_removedPaths.Contains(t.SourcePath)).ToList();
        if (keptTracks.Count == 0)
        {
            return;
        }

        var effective = _lastResult with { Tracks = keptTracks };
        var title = PlaylistName.Trim().Length > 0 ? PlaylistName.Trim() : "TubeDrop playlist";
        BatchRequested?.Invoke(this, new StartBatchRequest(
            effective, CreateNew,
            CreateNew ? null : SelectedExistingPlaylist?.PlaylistId,
            title, PlaylistDescription, Privacy));
    }

    private void NotifyStartState()
    {
        OnPropertyChanged(nameof(CanStart));
        StartBatchCommand.NotifyCanExecuteChanged();
    }

    private static string Initials(TrackInfo track)
    {
        var source = track.Artist.Length > 0 ? track.Artist : track.Title;
        var words = source.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return words.Length switch
        {
            0 => "♪",
            1 => words[0][..Math.Min(2, words[0].Length)].ToUpperInvariant(),
            _ => $"{char.ToUpperInvariant(words[0][0])}{char.ToUpperInvariant(words[1][0])}",
        };
    }
}

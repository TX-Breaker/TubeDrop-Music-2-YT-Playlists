using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using TubeDrop.App.Services;
using TubeDrop.Core.Journal;
using TubeDrop.Core.Matching;
using TubeDrop.Core.Playlists;

namespace TubeDrop.App.ViewModels;

public sealed record ReportRow(
    string Status,
    string StatusBrushKey,
    string Title,
    string MatchedTitle,
    double Confidence,
    string VideoId,
    string? SetVideoId,
    string Query);

public partial class ReportViewModel : ObservableObject
{
    private readonly JournaledPlaylistService _playlistService;

    [ObservableProperty] private int _addedCount;
    [ObservableProperty] private int _fallbackCount;
    [ObservableProperty] private int _unmatchedCount;
    [ObservableProperty] private int _duplicateCount;
    [ObservableProperty] private int _skippedCount;
    [ObservableProperty] private int _errorCount;
    [ObservableProperty] private string _filter = "All";
    [ObservableProperty] private bool _hasReport;
    [ObservableProperty] private string _statusMessage = "";

    private readonly List<ReportRow> _allRows = [];
    private string? _playlistId;

    public ObservableCollection<ReportRow> Rows { get; } = [];
    public string[] Filters { get; } = ["All", "Added", "Fallback", "Unmatched", "Duplicates", "Skipped", "Errors"];

    public ReportViewModel(JournaledPlaylistService playlistService)
    {
        _playlistService = playlistService;
    }

    public void Build(BatchOutcome outcome)
    {
        _allRows.Clear();
        _playlistId = outcome.PlaylistId;
        AddedCount = FallbackCount = UnmatchedCount = ErrorCount = 0;
        DuplicateCount = outcome.Ingest.DuplicateCount;
        SkippedCount = outcome.Ingest.SkippedCount;
        StatusMessage = "";

        foreach (var item in outcome.Tracks)
        {
            var match = item.Match;
            var best = match?.Best;
            var (status, key) = item.Phase switch
            {
                TrackPhase.Added when match?.Status == MatchStatus.FallbackMatched => ("Fallback", "TubeDrop.Brush.Warn"),
                TrackPhase.Added => ("Added", "TubeDrop.Brush.Good"),
                TrackPhase.Matched when match?.Status == MatchStatus.FallbackMatched => ("Fallback", "TubeDrop.Brush.Warn"),
                TrackPhase.Matched => ("Added", "TubeDrop.Brush.Good"),
                TrackPhase.Unmatched => ("Unmatched", "TubeDrop.Brush.Text2"),
                TrackPhase.Error => ("Error", "TubeDrop.Brush.Bad"),
                _ => ("Unmatched", "TubeDrop.Brush.Text2"),
            };

            if (status == "Added")
            {
                AddedCount++;
            }
            else if (status == "Fallback")
            {
                FallbackCount++;
            }
            else if (status == "Error")
            {
                ErrorCount++;
            }
            else
            {
                UnmatchedCount++;
            }

            _allRows.Add(new ReportRow(
                status, key,
                item.Track.Artist.Length > 0 ? $"{item.Track.Artist} — {item.Track.Title}" : item.Track.Title,
                best?.Candidate.Title ?? "",
                best?.Score ?? 0,
                best?.Candidate.VideoId ?? "",
                item.AddedSetVideoId,
                match?.UsedQuery ?? item.Track.Title));
        }

        HasReport = true;
        ApplyFilter();
    }

    partial void OnFilterChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        IEnumerable<ReportRow> filtered = Filter switch
        {
            "Added" => _allRows.Where(r => r.Status == "Added"),
            "Fallback" => _allRows.Where(r => r.Status == "Fallback"),
            "Unmatched" => _allRows.Where(r => r.Status == "Unmatched"),
            "Errors" => _allRows.Where(r => r.Status == "Error"),
            "Duplicates" => [],
            "Skipped" => [],
            _ => _allRows,
        };

        Rows.Clear();
        foreach (var row in filtered)
        {
            Rows.Add(row);
        }
    }

    [RelayCommand]
    private void OpenVideo(string videoId)
    {
        if (!string.IsNullOrEmpty(videoId))
        {
            Open($"https://www.youtube.com/watch?v={videoId}");
        }
    }

    /// <summary>Opens a manual YouTube search for a row's track (§11).</summary>
    [RelayCommand]
    private void ManualSearch(ReportRow row)
    {
        var query = WebUtility.UrlEncode(row.Query.Length > 0 ? row.Query : row.Title);
        Open($"https://www.youtube.com/results?search_query={query}");
    }

    /// <summary>Removes an added row from the playlist — journaled, so it is itself undoable (§10/§11).</summary>
    [RelayCommand]
    private async Task RemoveRow(ReportRow row)
    {
        if (_playlistId is null || string.IsNullOrEmpty(row.SetVideoId))
        {
            StatusMessage = "This row can't be removed (it was never added).";
            return;
        }

        try
        {
            await _playlistService.RemoveItemsAsync(_playlistId, [new PlaylistItem(row.VideoId, row.SetVideoId)]);
            _allRows.Remove(row);
            Rows.Remove(row);
            if (row.Status == "Added")
            {
                AddedCount--;
            }
            else if (row.Status == "Fallback")
            {
                FallbackCount--;
            }

            StatusMessage = $"Removed \"{row.Title}\" from the playlist (undoable in Activity).";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Remove failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private void ExportCsv()
    {
        var dialog = new SaveFileDialog { Filter = "CSV (*.csv)|*.csv", FileName = "tubedrop-report.csv" };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var sb = new StringBuilder();
        sb.AppendLine("Status,Title,MatchedTitle,Confidence,VideoId,Query");
        foreach (var r in _allRows)
        {
            sb.AppendLine($"{Csv(r.Status)},{Csv(r.Title)},{Csv(r.MatchedTitle)},{r.Confidence:0.00},{Csv(r.VideoId)},{Csv(r.Query)}");
        }

        File.WriteAllText(dialog.FileName, sb.ToString());
    }

    [RelayCommand]
    private void ExportJson()
    {
        var dialog = new SaveFileDialog { Filter = "JSON (*.json)|*.json", FileName = "tubedrop-report.json" };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        File.WriteAllText(dialog.FileName,
            JsonSerializer.Serialize(_allRows, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static void Open(string url) =>
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

    private static string Csv(string value) =>
        value.Contains(',') || value.Contains('"')
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;
}

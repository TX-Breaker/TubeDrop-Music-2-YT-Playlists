using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using TubeDrop.App.Services;

namespace TubeDrop.App.ViewModels;

public partial class QueueItemViewModel : ObservableObject
{
    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string _subtitle = "";
    [ObservableProperty] private string _phaseText = "Pending";
    [ObservableProperty] private string _phaseBrushKey = "TubeDrop.Brush.Text3";

    public void UpdateFrom(TrackProgress progress)
    {
        var track = progress.Track;
        Title = track.Artist.Length > 0 ? $"{track.Artist} — {track.Title}" : track.Title;
        var best = progress.Match?.Best;
        Subtitle = progress.Message
            ?? (best is not null ? $"{best.Candidate.Title} · score {best.Score:0.00}" : "");

        (PhaseText, PhaseBrushKey) = progress.Phase switch
        {
            TrackPhase.Pending => ("Pending", "TubeDrop.Brush.Text3"),
            TrackPhase.Searching => ("Searching…", "TubeDrop.Brush.Info"),
            TrackPhase.Matched => ("Matched", "TubeDrop.Brush.Good"),
            TrackPhase.Adding => ("Adding…", "TubeDrop.Brush.Info"),
            TrackPhase.Added => ("Added", "TubeDrop.Brush.Good"),
            TrackPhase.Unmatched => ("Unmatched", "TubeDrop.Brush.Warn"),
            TrackPhase.Error => ("Error", "TubeDrop.Brush.Bad"),
            _ => ("—", "TubeDrop.Brush.Text3"),
        };
    }
}

public partial class QueueViewModel : ObservableObject
{
    [ObservableProperty] private int _total;
    [ObservableProperty] private int _completed;
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private string _headline = "No batch running";

    public ObservableCollection<QueueItemViewModel> Items { get; } = [];

    public double ProgressPercent => Total == 0 ? 0 : 100.0 * Completed / Total;

    public void Reset(int total)
    {
        Items.Clear();
        Total = total;
        Completed = 0;
        IsRunning = true;
        Headline = $"Processing {total} track(s)…";
        OnPropertyChanged(nameof(ProgressPercent));
    }

    public void Finish(string headline)
    {
        IsRunning = false;
        Headline = headline;
    }

    partial void OnCompletedChanged(int value) => OnPropertyChanged(nameof(ProgressPercent));
}

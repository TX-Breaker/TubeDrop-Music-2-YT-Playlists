using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TubeDrop.App.ViewModels;

namespace TubeDrop.App.Views.Pages;

public partial class HomePage : UserControl
{
    public HomePage()
    {
        InitializeComponent();
    }

    private HomeViewModel? ViewModel => DataContext as HomeViewModel;

    private void DropZone_OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private async void DropZone_OnDrop(object sender, DragEventArgs e)
    {
        if (ViewModel is null)
        {
            return;
        }

        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths && paths.Length > 0)
        {
            await ViewModel.AddPathsAsync(paths);
        }
    }

    private void AppendRadio_OnChecked(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not null)
        {
            ViewModel.CreateNew = false;
        }
    }

    /// <summary>Delete / Backspace removes the selected tracks from the queue.</summary>
    private void TrackList_OnKeyDown(object sender, KeyEventArgs e)
    {
        if ((e.Key != Key.Delete && e.Key != Key.Back) || ViewModel is null || sender is not ListBox list)
        {
            return;
        }

        var selected = list.SelectedItems.Cast<TrackRow>().ToList();
        if (selected.Count > 0)
        {
            ViewModel.RemoveTracks(selected);
            e.Handled = true;
        }
    }

    /// <summary>Ctrl+V paste of a file list from Explorer (§6).</summary>
    private async void HomePage_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.V || (Keyboard.Modifiers & ModifierKeys.Control) == 0 || ViewModel is null)
        {
            return;
        }

        if (Clipboard.ContainsFileDropList())
        {
            var files = Clipboard.GetFileDropList().Cast<string>().ToList();
            if (files.Count > 0)
            {
                await ViewModel.AddPathsAsync(files);
                e.Handled = true;
            }
        }
    }
}

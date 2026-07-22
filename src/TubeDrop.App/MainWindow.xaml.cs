using TubeDrop.App.ViewModels;

namespace TubeDrop.App;

public partial class MainWindow : Wpf.Ui.Controls.FluentWindow
{
    public MainWindow(MainWindowViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
    }
}

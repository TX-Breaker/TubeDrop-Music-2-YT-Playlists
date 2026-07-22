using System.Windows.Controls;
using TubeDrop.App.ViewModels;

namespace TubeDrop.App.Views.Pages;

public partial class SettingsPage : UserControl
{
    public SettingsPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, System.Windows.RoutedEventArgs e)
    {
        // PasswordBox.Password is not bindable; seed it from the current setting.
        if (DataContext is SettingsViewModel vm && CloudKeyBox.Password != vm.CloudRefinerApiKey)
        {
            CloudKeyBox.Password = vm.CloudRefinerApiKey;
        }
    }

    private void CloudKeyBox_OnPasswordChanged(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel vm)
        {
            vm.CloudRefinerApiKey = CloudKeyBox.Password;
        }
    }
}

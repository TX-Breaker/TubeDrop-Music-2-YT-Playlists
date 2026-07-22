using Velopack;

namespace TubeDrop.App;

/// <summary>
/// Entry point. Velopack's hook must run first (§13): during install/update/
/// uninstall the framework invokes the exe with special args, does its work,
/// and exits before any WPF window is created.
/// </summary>
public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        VelopackApp.Build().Run();

        var app = new App { StartupArgs = args };
        app.InitializeComponent();
        app.Run();
    }
}

using PhaseLab.Shell;
using PhaseLab.UI;
using System.Windows;

namespace PhaseLab.Shell;

public partial class App : Application
{
    private void OnStartup(object sender, StartupEventArgs e)
    {
        ThemeService.Initialize();
        MainWindow = new MainWindow();
        MainWindow.Show();
    }

    private void OnExit(object sender, ExitEventArgs e)
    {
        ThemeService.Shutdown();
    }
}

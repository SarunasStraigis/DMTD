using Dmtd.Module;
using JitterMeasurement.Module;
using PhaseLab.Api;
using PhaseLab.Api.Host;
using PhaseLab.Shell.Services;
using PhaseLab.UI;
using System.Windows;
using System.Windows.Threading;

namespace PhaseLab.Shell;

public partial class App : Application
{
    private PhaseLabApiHost? _apiHost;
    private IReadOnlyList<IMeasurementModule> _modules = Array.Empty<IMeasurementModule>();

    private async void OnStartup(object sender, StartupEventArgs e)
    {
        ThemeService.Initialize();

        _modules =
        [
            new DmtdMeasurementModule(),
            new JitterMeasurementModule()
        ];

        var settings = ShellSettingsStore.Load();
        await StartApiHostAsync(settings);

        MainWindow = new MainWindow(_modules, settings);
        MainWindow.Show();

        _ = Dispatcher.InvokeAsync(CheckForUpdatesDeferred, DispatcherPriority.ApplicationIdle);
    }

    private static async void CheckForUpdatesDeferred()
    {
        await UpdateService.CheckForUpdatesAsync();
    }

    private async void OnExit(object sender, ExitEventArgs e)
    {
        foreach (var module in _modules)
        {
            module.Deactivate();
        }

        if (_apiHost is not null)
        {
            await _apiHost.DisposeAsync();
            _apiHost = null;
        }

        ThemeService.Shutdown();
    }

    private async Task StartApiHostAsync(ShellSettings settings)
    {
        if (!settings.ApiEnabled)
        {
            return;
        }

        var apiModules = _modules
            .Select(m => m.Api)
            .OfType<IMeasurementApiModule>()
            .ToList();

        if (apiModules.Count == 0)
        {
            return;
        }

        var registry = new ModuleApiRegistry(apiModules);
        _apiHost = new PhaseLabApiHost();
        await _apiHost.StartAsync(registry, new PhaseLabApiOptions { Port = settings.ApiPort });
    }
}

using PhaseLab.Shell.Services;
using PhaseLab.UI;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;

namespace PhaseLab.Shell;

public partial class MainWindow : Window
{
    private readonly IReadOnlyList<IMeasurementModule> _modules;
    private readonly ShellSettings _settings;
    private IMeasurementModule? _activeModule;

    public MainWindow(IReadOnlyList<IMeasurementModule> modules, ShellSettings settings)
    {
        InitializeComponent();

        _modules = modules;
        _settings = settings;
        ModeComboBox.ItemsSource = _modules;
        ModeComboBox.DisplayMemberPath = nameof(IMeasurementModule.DisplayName);

        var initial = _modules.FirstOrDefault(m => m.Id == settings.ActiveModeId) ?? _modules[0];
        ModeComboBox.SelectedItem = initial;
    }

    private void ModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ModeComboBox.SelectedItem is not IMeasurementModule module)
        {
            return;
        }

        if (ReferenceEquals(module, _activeModule))
        {
            return;
        }

        _activeModule?.Deactivate();
        _activeModule = module;
        _activeModule.Activate();
        ModuleContent.Content = module.View;
        Title = $"PhaseLab — {module.DisplayName}";

        ShellSettingsStore.Save(new ShellSettings
        {
            ActiveModeId = module.Id,
            ApiEnabled = _settings.ApiEnabled,
            ApiPort = _settings.ApiPort
        });
    }

    protected override void OnClosed(EventArgs e)
    {
        _activeModule?.Deactivate();
        base.OnClosed(e);
    }

    private void OpenApiDocs_Click(object sender, RoutedEventArgs e)
    {
        if (!_settings.ApiEnabled)
        {
            MessageBox.Show(
                "The REST API is disabled. Set apiEnabled to true in %AppData%\\PhaseLab\\settings.json and restart PhaseLab.",
                "API Docs",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var url = $"http://127.0.0.1:{_settings.ApiPort}/docs";
        Process.Start(new ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true
        });
    }
}

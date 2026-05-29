using Dmtd.Module;
using JitterMeasurement.Module;
using PhaseLab.Shell.Services;
using PhaseLab.UI;
using System.Windows;
using System.Windows.Controls;

namespace PhaseLab.Shell;

public partial class MainWindow : Window
{
    private readonly IReadOnlyList<IMeasurementModule> _modules =
    [
        new DmtdMeasurementModule(),
        new JitterMeasurementModule()
    ];

    private IMeasurementModule? _activeModule;

    public MainWindow()
    {
        InitializeComponent();

        ModeComboBox.ItemsSource = _modules;
        ModeComboBox.DisplayMemberPath = nameof(IMeasurementModule.DisplayName);

        var settings = ShellSettingsStore.Load();
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

        ShellSettingsStore.Save(new ShellSettings { ActiveModeId = module.Id });
    }

    protected override void OnClosed(EventArgs e)
    {
        _activeModule?.Deactivate();
        base.OnClosed(e);
    }
}

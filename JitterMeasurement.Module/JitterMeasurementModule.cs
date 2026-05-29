using JitterMeasurement.Module.Api;
using JitterMeasurement.Module.ViewModels;
using JitterMeasurement.Module.Views;
using PhaseLab.Api;
using PhaseLab.UI;
using System.Windows.Controls;

namespace JitterMeasurement.Module;

public sealed class JitterMeasurementModule : IMeasurementModule
{
    private MainViewModel? _viewModel;
    private JitterView? _view;
    private JitterApiModule? _api;

    public string Id => "jitter";
    public string DisplayName => "Jitter Measurement";

    public UserControl View => _view ??= new JitterView(ViewModel);

    public IMeasurementApiModule? Api => _api ??= new JitterApiModule(ViewModel);

    private MainViewModel ViewModel => _viewModel ??= new MainViewModel();

    public void Activate()
    {
        // Restore UI from saved settings on show.
    }

    public void Deactivate()
    {
        if (ViewModel.IsCapturing)
        {
            ViewModel.StartStopCommand.Execute(null);
        }
    }
}

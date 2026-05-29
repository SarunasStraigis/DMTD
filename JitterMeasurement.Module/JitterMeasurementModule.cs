using JitterMeasurement.Module.ViewModels;
using JitterMeasurement.Module.Views;
using PhaseLab.UI;
using System.Windows.Controls;

namespace JitterMeasurement.Module;

public sealed class JitterMeasurementModule : IMeasurementModule
{
    private MainViewModel? _viewModel;
    private JitterView? _view;

    public string Id => "jitter";
    public string DisplayName => "Jitter Measurement";

    public UserControl View => _view ??= new JitterView(ViewModel);

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

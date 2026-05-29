using Dmtd.Module.Api;
using Dmtd.Module.ViewModels;
using Dmtd.Module.Views;
using PhaseLab.Api;
using PhaseLab.UI;
using System.Windows.Controls;

namespace Dmtd.Module;

public sealed class DmtdMeasurementModule : IMeasurementModule
{
    private DmtdViewModel? _viewModel;
    private DmtdView? _view;
    private DmtdApiModule? _api;

    public string Id => "dmtd";
    public string DisplayName => "DMTD Phase Analyser";

    public UserControl View => _view ??= new DmtdView(ViewModel);

    public IMeasurementApiModule? Api => _api ??= new DmtdApiModule(ViewModel);

    private DmtdViewModel ViewModel => _viewModel ??= new DmtdViewModel();

    public void Activate() => ViewModel.Activate();

    public void Deactivate() => ViewModel.Deactivate();
}

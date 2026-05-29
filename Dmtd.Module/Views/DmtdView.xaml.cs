using Dmtd.Core;
using Dmtd.Module.ViewModels;
using PhaseLab.UI;
using ScottPlot;
using ScottPlot.Plottables;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace Dmtd.Module.Views;

public partial class DmtdView : UserControl
{
    private const int MaxPhasePoints = 3600;

    private readonly DmtdViewModel _viewModel;
    private readonly List<double> _phaseTimes = new();
    private readonly List<double> _phasePs = new();
    private readonly List<double> _phaseMa = new();
    private SignalXY? _phaseSignal;
    private SignalXY? _maSignal;
    private double _sessionStart;

    public DmtdView(DmtdViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;

        ConfigurePlots();
        _viewModel.LivePointReceived += OnLivePoint;
        _viewModel.PhaseSessionReset += OnPhaseSessionReset;
        _viewModel.SnapshotCaptured += UpdateSnapshotPlot;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        ThemeService.ThemeChanged += OnThemeChanged;
        Unloaded += (_, _) => ThemeService.ThemeChanged -= OnThemeChanged;
    }

    private void ConfigurePlots()
    {
        PhasePlot.Plot.Axes.Bottom.Label.Text = "Time (s)";
        PhasePlot.Plot.Axes.Left.Label.Text = "Δt (ps)";
        PhasePlot.Plot.Title("Differential phase");

        ChannelPlot.Plot.Title("Channels (live)");
        ChannelPlot.Plot.Axes.Bottom.Label.Text = "Time (s)";
        ChannelPlot.Plot.Axes.Left.Label.Text = "Amplitude";

        SnapshotPlot.Plot.Title("Waveform snapshot");
        SnapshotPlot.Plot.Axes.Bottom.Label.Text = "Sample";
        SnapshotPlot.Plot.Axes.Left.Label.Text = "Amplitude";

        ApplyPlotChrome();
    }

    private void OnThemeChanged(AppTheme theme) => Dispatcher.Invoke(ApplyPlotChrome);

    private void ApplyPlotChrome()
    {
        PlotThemeHelper.ApplyChrome(PhasePlot);
        PlotThemeHelper.ApplyChrome(ChannelPlot);
        PlotThemeHelper.ApplyChrome(SnapshotPlot);
        PhasePlot.Refresh();
        ChannelPlot.Refresh();
        SnapshotPlot.Refresh();
    }

    private void OnLivePoint(LivePoint point)
    {
        Dispatcher.Invoke(() =>
        {
            if (_phaseTimes.Count == 0)
            {
                _sessionStart = point.Timestamp.ToUnixTimeMilliseconds() / 1000.0;
            }

            var t = point.Timestamp.ToUnixTimeMilliseconds() / 1000.0 - _sessionStart;
            _phaseTimes.Add(t);
            _phasePs.Add(point.PhaseDiffPs);
            RecomputeMa();

            while (_phaseTimes.Count > MaxPhasePoints)
            {
                _phaseTimes.RemoveAt(0);
                _phasePs.RemoveAt(0);
                _phaseMa.RemoveAt(0);
            }

            UpdatePhasePlot();

            if (_viewModel.ChannelsLiveEnabled)
            {
                UpdateChannelPlot(point);
            }
        });
    }

    private void OnPhaseSessionReset()
    {
        Dispatcher.Invoke(() =>
        {
            _phaseTimes.Clear();
            _phasePs.Clear();
            _phaseMa.Clear();
            _sessionStart = 0;
            UpdatePhasePlot();
        });
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(DmtdViewModel.MaWindow))
        {
            return;
        }

        Dispatcher.Invoke(() =>
        {
            RecomputeMa();
            UpdatePhasePlot();
        });
    }

    private void RecomputeMa()
    {
        var w = Math.Max(1, _viewModel.MaWindow);
        _phaseMa.Clear();
        double sum = 0;
        for (var i = 0; i < _phasePs.Count; i++)
        {
            sum += _phasePs[i];
            if (i >= w)
            {
                sum -= _phasePs[i - w];
            }

            var span = Math.Min(i + 1, w);
            _phaseMa.Add(sum / span);
        }
    }

    private void UpdatePhasePlot()
    {
        PhasePlot.Plot.Clear();
        if (_phaseTimes.Count > 0)
        {
            _phaseSignal = PhasePlot.Plot.Add.SignalXY(_phaseTimes.ToArray(), _phasePs.ToArray());
            StyleSignal(_phaseSignal);
            _phaseSignal.LegendText = "Δt (ps)";

            if (_phaseMa.Count == _phaseTimes.Count)
            {
                _maSignal = PhasePlot.Plot.Add.SignalXY(_phaseTimes.ToArray(), _phaseMa.ToArray());
                _maSignal.Color = PlotThemeHelper.GetSecondarySignalColor();
                _maSignal.LineWidth = 1.5f;
                _maSignal.LegendText = $"Moving average ({_viewModel.MaWindow})";
            }

            PlotThemeHelper.ApplyLegend(PhasePlot.Plot);

            var (yMin, yMax) = PlotScaleHelper.RangeWithPadding(_phasePs.ToArray());
            PhasePlot.Plot.Axes.SetLimitsX(_phaseTimes[0], _phaseTimes[^1]);
            PhasePlot.Plot.Axes.SetLimitsY(yMin, yMax);
        }

        PhasePlot.Refresh();
    }

    private void UpdateChannelPlot(LivePoint point)
    {
        var (left, right) = _viewModel.CaptureService.Snapshot.CopyLatest(1024);
        if (left.Length == 0)
        {
            return;
        }

        var xs = Enumerable.Range(0, left.Length).Select(i => i / (double)_viewModel.CaptureService.SampleRate).ToArray();
        ChannelPlot.Plot.Clear();
        var sigA = ChannelPlot.Plot.Add.SignalXY(xs, left.Select(v => (double)v).ToArray());
        StyleSignal(sigA);
        var sigB = ChannelPlot.Plot.Add.SignalXY(xs, right.Select(v => (double)v).ToArray());
        sigB.Color = PlotThemeHelper.GetSecondarySignalColor();
        sigB.LineWidth = 2;
        ChannelPlot.Plot.Axes.SetLimitsX(xs[0], xs[^1]);
        ChannelPlot.Refresh();
    }

    private void UpdateSnapshotPlot()
    {
        if (!_viewModel.IsCapturing)
        {
            return;
        }

        var (left, right) = _viewModel.CaptureService.Snapshot.CopyLatest(Dmtd.Module.Services.DmtdCaptureService.SnapshotFrames);
        var xs = Enumerable.Range(0, left.Length).Select(i => (double)i).ToArray();
        SnapshotPlot.Plot.Clear();
        var sigA = SnapshotPlot.Plot.Add.SignalXY(xs, left.Select(v => (double)v).ToArray());
        StyleSignal(sigA);
        var sigB = SnapshotPlot.Plot.Add.SignalXY(xs, right.Select(v => (double)v).ToArray());
        sigB.Color = PlotThemeHelper.GetSecondarySignalColor();
        sigB.LineWidth = 1.5f;
        SnapshotPlot.Refresh();
    }

    private static void StyleSignal(SignalXY signal)
    {
        signal.Color = PlotThemeHelper.GetSignalColor();
        signal.LineWidth = 2;
    }
}

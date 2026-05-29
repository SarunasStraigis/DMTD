using Dmtd.Core;
using Dmtd.Module.ViewModels;
using PhaseLab.UI;
using ScottPlot;
using ScottPlot.Plottables;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Dmtd.Module.Views;

public partial class DmtdView : UserControl
{
    private const int MaxPhasePoints = 3600;
    private static readonly TimeSpan PlotRefreshInterval = TimeSpan.FromMilliseconds(100);

    private readonly DmtdViewModel _viewModel;
    private readonly List<double> _phaseTimes = new();
    private readonly List<double> _phasePs = new();
    private readonly List<double> _phaseMa = new();
    private readonly ConcurrentQueue<LivePoint> _pendingPoints = new();
    private LivePoint? _latestPoint;
    private double _sessionStart;
    private bool _uiRefreshScheduled;
    private DateTime _lastPhasePlotRefresh = DateTime.MinValue;
    private DateTime _lastChannelPlotRefresh = DateTime.MinValue;

    public DmtdView(DmtdViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;

        ConfigurePlots();
        _viewModel.LivePointReceived += OnLivePoint;
        _viewModel.PhaseSessionReset += () => Dispatcher.BeginInvoke(DispatcherPriority.Background, OnPhaseSessionReset);
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

        ApplyPlotChrome();
    }

    private void OnThemeChanged(AppTheme theme) =>
        Dispatcher.BeginInvoke(DispatcherPriority.Background, ApplyPlotChrome);

    private void ApplyPlotChrome()
    {
        PlotThemeHelper.ApplyChrome(PhasePlot);
        PlotThemeHelper.ApplyChrome(ChannelPlot);
        PhasePlot.Refresh();
        ChannelPlot.Refresh();
    }

    private void OnLivePoint(LivePoint point)
    {
        _pendingPoints.Enqueue(point);
        ScheduleUiRefresh();
    }

    private void ScheduleUiRefresh()
    {
        if (_uiRefreshScheduled)
        {
            return;
        }

        _uiRefreshScheduled = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, ProcessPendingUiUpdates);
    }

    private void ProcessPendingUiUpdates()
    {
        _uiRefreshScheduled = false;

        var hadPoints = false;
        while (_pendingPoints.TryDequeue(out var point))
        {
            AppendPhasePoint(point);
            hadPoints = true;
        }

        if (!hadPoints)
        {
            return;
        }

        var now = DateTime.UtcNow;
        if (now - _lastPhasePlotRefresh >= PlotRefreshInterval || _pendingPoints.IsEmpty)
        {
            UpdatePhasePlot();
            _lastPhasePlotRefresh = now;
        }
        else
        {
            ScheduleUiRefresh();
        }

        if (_viewModel.ChannelsLiveEnabled &&
            _latestPoint is not null &&
            now - _lastChannelPlotRefresh >= PlotRefreshInterval)
        {
            UpdateChannelPlot();
            _lastChannelPlotRefresh = now;
        }
    }

    private void AppendPhasePoint(LivePoint point)
    {
        _latestPoint = point;

        if (_phaseTimes.Count == 0)
        {
            _sessionStart = point.Timestamp.ToUnixTimeMilliseconds() / 1000.0;
        }

        var t = point.Timestamp.ToUnixTimeMilliseconds() / 1000.0 - _sessionStart;
        _phaseTimes.Add(t);
        _phasePs.Add(point.PhaseDiffPs);
        AppendMovingAverage();

        while (_phaseTimes.Count > MaxPhasePoints)
        {
            _phaseTimes.RemoveAt(0);
            _phasePs.RemoveAt(0);
            _phaseMa.RemoveAt(0);
        }
    }

    private void OnPhaseSessionReset()
    {
        while (_pendingPoints.TryDequeue(out _))
        {
        }

        _phaseTimes.Clear();
        _phasePs.Clear();
        _phaseMa.Clear();
        _sessionStart = 0;
        _latestPoint = null;
        _lastPhasePlotRefresh = DateTime.MinValue;
        _lastChannelPlotRefresh = DateTime.MinValue;
        UpdatePhasePlot();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(DmtdViewModel.MaWindow))
        {
            return;
        }

        Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            RecomputeMa();
            UpdatePhasePlot();
        });
    }

    private void AppendMovingAverage()
    {
        var w = Math.Max(1, _viewModel.MaWindow);
        var count = _phasePs.Count;
        var span = Math.Min(count, w);
        var start = count - span;
        double sum = 0;
        for (var i = start; i < count; i++)
        {
            sum += _phasePs[i];
        }

        _phaseMa.Add(sum / span);
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
        if (_phaseTimes.Count == 0)
        {
            PhasePlot.Refresh();
            return;
        }

        var phaseSignal = PhasePlot.Plot.Add.SignalXY(_phaseTimes.ToArray(), _phasePs.ToArray());
        StyleSignal(phaseSignal);
        phaseSignal.LegendText = "Δt (ps)";

        if (_phaseMa.Count == _phaseTimes.Count)
        {
            var maSignal = PhasePlot.Plot.Add.SignalXY(_phaseTimes.ToArray(), _phaseMa.ToArray());
            maSignal.Color = PlotThemeHelper.GetSecondarySignalColor();
            maSignal.LineWidth = 1.5f;
            maSignal.LegendText = $"Moving average ({_viewModel.MaWindow})";
        }

        PlotThemeHelper.ApplyLegend(PhasePlot.Plot);

        var (yMin, yMax) = PlotScaleHelper.RangeWithPadding(_phasePs.ToArray());
        PhasePlot.Plot.Axes.SetLimitsX(_phaseTimes[0], _phaseTimes[^1]);
        PhasePlot.Plot.Axes.SetLimitsY(yMin, yMax);
        PhasePlot.Refresh();
    }

    private void UpdateChannelPlot()
    {
        var (left, right) = _viewModel.CaptureService.Snapshot.CopyLatest(1024);
        if (left.Length == 0)
        {
            return;
        }

        var sampleRate = _viewModel.CaptureService.SampleRate;
        var xs = new double[left.Length];
        for (var i = 0; i < left.Length; i++)
        {
            xs[i] = i / (double)sampleRate;
        }

        ChannelPlot.Plot.Clear();
        var sigA = ChannelPlot.Plot.Add.SignalXY(xs, left.Select(v => (double)v).ToArray());
        StyleSignal(sigA);
        var sigB = ChannelPlot.Plot.Add.SignalXY(xs, right.Select(v => (double)v).ToArray());
        sigB.Color = PlotThemeHelper.GetSecondarySignalColor();
        sigB.LineWidth = 2;

        var combined = new double[left.Length + right.Length];
        for (var i = 0; i < left.Length; i++)
        {
            combined[i] = left[i];
            combined[left.Length + i] = right[i];
        }

        var (yMin, yMax) = PlotScaleHelper.RangeWithPadding(combined);
        ChannelPlot.Plot.Axes.SetLimitsX(xs[0], xs[^1]);
        ChannelPlot.Plot.Axes.SetLimitsY(yMin, yMax);
        ChannelPlot.Refresh();
    }

    private static void StyleSignal(SignalXY signal)
    {
        signal.Color = PlotThemeHelper.GetSignalColor();
        signal.LineWidth = 2;
    }
}

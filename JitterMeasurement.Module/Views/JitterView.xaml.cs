using JitterMeasurement.Core.Models;
using JitterMeasurement.Module.ViewModels;
using PhaseLab.UI;
using ScottPlot;
using ScottPlot.Plottables;
using System.Windows;
using System.Windows.Controls;

namespace JitterMeasurement.Module.Views;

public partial class JitterView : UserControl
{
    private readonly MainViewModel _viewModel;
    private bool _timeInitialScaled;
    private bool _fftInitialScaled;
    private ScottPlot.IYAxis? _fftRightAxis;

    public JitterView(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        _viewModel.SnapshotUpdated += OnSnapshotUpdated;
        _viewModel.CaptureSessionStarted += ResetPlotScaling;
        _viewModel.FftViewRangeChanged += ResetFftScaling;
        ThemeService.ThemeChanged += OnThemeChanged;
        Unloaded += (_, _) => ThemeService.ThemeChanged -= OnThemeChanged;

        ConfigurePlots();
        UpdateAutoscaleButtonLabels();
    }

    public void DisposeViewModel()
    {
        ThemeService.ThemeChanged -= OnThemeChanged;
        _viewModel.Dispose();
    }

    private void OnThemeChanged(AppTheme theme)
    {
        Dispatcher.Invoke(() =>
        {
            ApplyPlotChrome();
            if (_viewModel.LatestSnapshot is not null)
            {
                UpdatePlots(_viewModel.LatestSnapshot);
            }
            else
            {
                TimePlot.Refresh();
                FftPlot.Refresh();
            }
        });
    }

    private void ResetPlotScaling()
    {
        _timeInitialScaled = false;
        _fftInitialScaled = false;
    }

    private void ResetFftScaling()
    {
        _fftInitialScaled = false;
        if (_viewModel.LatestSnapshot is not null)
        {
            UpdateFftPlot(_viewModel.LatestSnapshot);
        }
    }

    private void ConfigurePlots()
    {
        TimePlot.Plot.Axes.ContinuouslyAutoscale = false;
        TimePlot.Plot.Axes.Bottom.Label.Text = "Time (s)";
        TimePlot.Plot.Axes.Left.Label.Text = "Voltage (V)";

        FftPlot.Plot.Axes.ContinuouslyAutoscale = false;
        FftPlot.Plot.Axes.Bottom.Label.Text = "Frequency (Hz)";
        FftPlot.Plot.Axes.Left.Label.Text = "Magnitude (dB)";
        _fftRightAxis = FftPlot.Plot.Axes.AddRightAxis();
        _fftRightAxis.Label.Text = "Integrated jitter (fs)";

        ApplyPlotChrome();
    }

    private void ApplyPlotChrome()
    {
        PlotThemeHelper.ApplyChrome(TimePlot);
        PlotThemeHelper.ApplyChrome(FftPlot);
        TimePlot.Refresh();
        FftPlot.Refresh();
    }

    private void OnSnapshotUpdated(AnalysisSnapshot snapshot) =>
        Dispatcher.Invoke(() => UpdatePlots(snapshot));

    private void UpdatePlots(AnalysisSnapshot snapshot)
    {
        UpdateTimePlot(snapshot);
        UpdateFftPlot(snapshot);
    }

    private void UpdateTimePlot(AnalysisSnapshot snapshot)
    {
        TimePlot.Plot.Clear();
        if (snapshot.TimeSeconds.Length > 0)
        {
            StyleSignal(TimePlot.Plot.Add.SignalXY(snapshot.TimeSeconds, snapshot.TimeVolts));
        }

        if (snapshot.TimeSeconds.Length > 0)
        {
            if (_viewModel.TimeContinuousAutoscale || !_timeInitialScaled)
            {
                ApplyTimeLimits(snapshot);
                _timeInitialScaled = true;
            }
        }

        TimePlot.Refresh();
    }

    private void UpdateFftPlot(AnalysisSnapshot snapshot)
    {
        FftPlot.Plot.Clear();
        if (_fftRightAxis is null)
        {
            _fftRightAxis = FftPlot.Plot.Axes.AddRightAxis();
            _fftRightAxis.Label.Text = "Integrated jitter (fs)";
        }

        if (snapshot.FftFrequenciesHz.Length > 0)
        {
            AddIntegrationMarkers(FftPlot.Plot, snapshot.IntegrationBandLowHz, snapshot.IntegrationBandHighHz);
            var magnitude = FftPlot.Plot.Add.SignalXY(snapshot.FftFrequenciesHz, snapshot.FftMagnitudeDb);
            StyleSignal(magnitude);
            magnitude.LegendText = "Magnitude (dB)";

            var (cumulativeFreqHz, cumulativeJitterFs) = GetVisibleCumulativeJitter(snapshot);
            if (cumulativeFreqHz.Length > 0 && _fftRightAxis is not null)
            {
                var cumulative = FftPlot.Plot.Add.SignalXY(cumulativeFreqHz, cumulativeJitterFs);
                cumulative.Axes.YAxis = _fftRightAxis;
                cumulative.Color = PlotThemeHelper.GetSecondarySignalColor();
                cumulative.LineWidth = 1.5f;
                cumulative.LegendText = "Integrated jitter (fs)";
            }

            PlotThemeHelper.ApplyLegend(FftPlot.Plot);
        }

        if (snapshot.FftFrequenciesHz.Length > 0)
        {
            if (_viewModel.FftContinuousAutoscale || !_fftInitialScaled)
            {
                ApplyFftLimits(snapshot);
                _fftInitialScaled = true;
            }
        }

        FftPlot.Refresh();
    }

    private static (double[] FrequenciesHz, double[] JitterFs) GetVisibleCumulativeJitter(AnalysisSnapshot snapshot)
    {
        var freqs = snapshot.CumulativeJitterFreqHz;
        var jitterFs = snapshot.CumulativeJitterFs;
        if (freqs.Length == 0 || jitterFs.Length != freqs.Length)
        {
            return (Array.Empty<double>(), Array.Empty<double>());
        }

        var xMax = snapshot.FftViewMaxHz;
        var lowHz = snapshot.IntegrationBandLowHz;
        var count = 0;
        for (var i = 0; i < freqs.Length; i++)
        {
            if (freqs[i] > xMax || freqs[i] < lowHz || jitterFs[i] <= 0)
            {
                continue;
            }

            count++;
        }

        if (count == 0)
        {
            return (Array.Empty<double>(), Array.Empty<double>());
        }

        var xs = new double[count];
        var ys = new double[count];
        var index = 0;
        for (var i = 0; i < freqs.Length; i++)
        {
            if (freqs[i] > xMax || freqs[i] < lowHz || jitterFs[i] <= 0)
            {
                continue;
            }

            xs[index] = freqs[i];
            ys[index] = jitterFs[i];
            index++;
        }

        return (xs, ys);
    }

    private static void StyleSignal(SignalXY signal)
    {
        signal.Color = PlotThemeHelper.GetSignalColor();
        signal.LineWidth = 2;
    }

    private static void AddIntegrationMarkers(Plot plot, double lowHz, double highHz)
    {
        var color = PlotThemeHelper.GetIntegrationLineColor();
        var lowLine = plot.Add.VerticalLine(lowHz);
        lowLine.Color = color;
        lowLine.LineWidth = 1;
        lowLine.LinePattern = LinePattern.Dashed;
        var highLine = plot.Add.VerticalLine(highHz);
        highLine.Color = color;
        highLine.LineWidth = 1;
        highLine.LinePattern = LinePattern.Dashed;
    }

    private void ApplyTimeLimits(AnalysisSnapshot snapshot)
    {
        var (yMin, yMax) = PlotScaleHelper.RangeWithPadding(snapshot.TimeVolts);
        TimePlot.Plot.Axes.SetLimitsX(snapshot.TimeSeconds[0], snapshot.TimeSeconds[^1]);
        TimePlot.Plot.Axes.SetLimitsY(yMin, yMax);
    }

    private void ApplyFftLimits(AnalysisSnapshot snapshot)
    {
        var xMax = snapshot.FftViewMaxHz;
        var visibleMagnitudes = GetVisibleFftMagnitudes(snapshot, xMax);
        var (yMin, yMax) = PlotScaleHelper.RangeWithPadding(visibleMagnitudes);
        FftPlot.Plot.Axes.SetLimitsX(0, xMax);
        FftPlot.Plot.Axes.SetLimitsY(yMin, yMax);

        if (_fftRightAxis is not null)
        {
            var (_, cumulativeJitterFs) = GetVisibleCumulativeJitter(snapshot);
            if (cumulativeJitterFs.Length > 0)
            {
                var (rightMin, rightMax) = PlotScaleHelper.RangeWithPadding(cumulativeJitterFs);
                _fftRightAxis.Min = rightMin;
                _fftRightAxis.Max = rightMax;
            }
        }
    }

    private static double[] GetVisibleFftMagnitudes(AnalysisSnapshot snapshot, double xMaxHz)
    {
        var count = snapshot.FftFrequenciesHz.Count(f => f <= xMaxHz);
        if (count == 0)
        {
            return snapshot.FftMagnitudeDb;
        }

        var values = new double[count];
        var index = 0;
        for (var i = 0; i < snapshot.FftFrequenciesHz.Length; i++)
        {
            if (snapshot.FftFrequenciesHz[i] <= xMaxHz)
            {
                values[index++] = snapshot.FftMagnitudeDb[i];
            }
        }

        return values;
    }

    private void TimeAutoscaleButton_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.TimeContinuousAutoscale = !_viewModel.TimeContinuousAutoscale;
        if (_viewModel.TimeContinuousAutoscale && _viewModel.LatestSnapshot is { TimeSeconds.Length: > 0 } snapshot)
        {
            ApplyTimeLimits(snapshot);
        }

        UpdateAutoscaleButtonLabels();
        TimePlot.Refresh();
    }

    private void FftAutoscaleButton_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.FftContinuousAutoscale = !_viewModel.FftContinuousAutoscale;
        if (_viewModel.FftContinuousAutoscale && _viewModel.LatestSnapshot is { FftFrequenciesHz.Length: > 0 } snapshot)
        {
            ApplyFftLimits(snapshot);
        }

        UpdateAutoscaleButtonLabels();
        FftPlot.Refresh();
    }

    private void UpdateAutoscaleButtonLabels()
    {
        UpdateAutoscaleButton(TimeAutoscaleButton, _viewModel.TimeContinuousAutoscale);
        UpdateAutoscaleButton(FftAutoscaleButton, _viewModel.FftContinuousAutoscale);
    }

    private static void UpdateAutoscaleButton(Button button, bool enabled)
    {
        button.Content = AutoscaleButtonLabel(enabled);
        button.BorderBrush = enabled
            ? (System.Windows.Media.Brush)Application.Current.FindResource("AccentBrush")
            : (System.Windows.Media.Brush)Application.Current.FindResource("SurfaceBorderBrush");
    }

    private static string AutoscaleButtonLabel(bool enabled) =>
        enabled ? "Auto: On" : "Auto: Off";
}

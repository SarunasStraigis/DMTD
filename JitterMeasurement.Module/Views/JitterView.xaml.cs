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
        TimePlot.Plot.Title("Error voltage (time)");
        TimePlot.Plot.Axes.Bottom.Label.Text = "Time (s)";
        TimePlot.Plot.Axes.Left.Label.Text = "Voltage (V)";

        FftPlot.Plot.Axes.ContinuouslyAutoscale = false;
        FftPlot.Plot.Title("Magnitude spectrum");
        FftPlot.Plot.Axes.Bottom.Label.Text = "Frequency (Hz)";
        FftPlot.Plot.Axes.Left.Label.Text = "Magnitude (dB)";

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

        var title = "Error voltage (time)";
        if (snapshot.Calibration?.IsClipping == true || snapshot.Jitter?.IsClipping == true)
        {
            title += " [CLIP]";
        }

        TimePlot.Plot.Title(title);

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
        if (snapshot.FftFrequenciesHz.Length > 0)
        {
            AddIntegrationMarkers(FftPlot.Plot, snapshot.IntegrationBandLowHz, snapshot.IntegrationBandHighHz);
            StyleSignal(FftPlot.Plot.Add.SignalXY(snapshot.FftFrequenciesHz, snapshot.FftMagnitudeDb));
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
        TimeAutoscaleButton.Content = AutoscaleButtonLabel(_viewModel.TimeContinuousAutoscale);
        FftAutoscaleButton.Content = AutoscaleButtonLabel(_viewModel.FftContinuousAutoscale);
    }

    private static string AutoscaleButtonLabel(bool enabled) =>
        enabled ? "Continuous auto: On" : "Continuous auto: Off";
}

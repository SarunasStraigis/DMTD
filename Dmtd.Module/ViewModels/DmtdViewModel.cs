using Dmtd.Core;
using Dmtd.Module.Api;
using Dmtd.Module.Services;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace Dmtd.Module.ViewModels;

public sealed class DmtdViewModel : INotifyPropertyChanged, IDisposable
{
    private const int MaxPhaseHistoryPoints = 3600;

    private readonly DmtdCaptureService _capture = new();
    private readonly List<double> _phasePsHistory = new();
    private readonly object _phaseHistoryLock = new();
    private PhaseHistoryStore? _history;
    private DispatcherTimer? _metricsTimer;
    private DmtdSettings _settings;
    private AudioDeviceInfo? _selectedInputDevice;
    private bool _isCapturing;
    private string _statusText = "Ready";
    private LivePoint? _latestPoint;
    private int _maWindow = 30;
    private bool _channelsLiveEnabled;
    private string _primaryMetricTitle = "Phase Δt";
    private string _primaryMetricValue = "—";
    private string _primaryMetricUnit = "ps";
    private string _secondaryMetricTitle = "Moving average";
    private string _secondaryMetricValue = "—";
    private string _secondaryMetricUnit = "ps";
    private string _stdMetricTitle = "σ (std dev)";
    private string _stdMetricValue = "—";
    private string _stdMetricUnit = "ps";
    private string _detailMetrics = "Start capture to begin live phase measurement.";
    private string _channelBeatFrequencyDisplay = "—";
    private string _channelRmsDisplay = "—";
    private string _channelPhaseAbDisplay = "—";
    private bool _showSlipWarning;
    private string _slipWarningText = string.Empty;
    private DateTimeOffset? _sessionSince;
    private int _exportableRowCount;
    private EnumOption<FreqEstimator>? _selectedFreqEstimator;
    private EnumOption<FreqSource>? _selectedFreqSource;
    private EnumOption<DemodMode>? _selectedDemodMode;
    private EnumOption<IqWindow>? _selectedIqWindow;
    private string _blockDurationMsText = string.Empty;

    public DmtdViewModel()
    {
        _settings = DmtdSettingsStore.Load();
        _blockDurationMsText = FormatBlockDurationMs(_settings.BlockDurationMs);
        InputDevices = new ObservableCollection<AudioDeviceInfo>(AudioDeviceEnumerator.GetInputDevices());
        _selectedInputDevice = ResolveDevice(InputDevices, _settings.DeviceId);

        SampleRates = new[] { 44100, 48000, 96000, 192000 };

        FreqEstimatorOptions =
        [
            new EnumOption<FreqEstimator> { Label = "FFT peak (adaptive)", Value = FreqEstimator.FftPeak },
            new EnumOption<FreqEstimator> { Label = "Fixed (nominal)", Value = FreqEstimator.Fixed }
        ];
        FreqSourceOptions =
        [
            new EnumOption<FreqSource> { Label = "Channel A only", Value = FreqSource.ChA },
            new EnumOption<FreqSource> { Label = "Average of A and B", Value = FreqSource.AvgAb }
        ];
        DemodModeOptions =
        [
            new EnumOption<DemodMode> { Label = "Block IQ (legacy atan2)", Value = DemodMode.BlockIq },
            new EnumOption<DemodMode> { Label = "Block IQ + LPF (noise-reduced)", Value = DemodMode.BlockIqFir },
            new EnumOption<DemodMode> { Label = "PLL tracker (amplitude-robust)", Value = DemodMode.PllTracker }
        ];
        IqWindowOptions =
        [
            new EnumOption<IqWindow> { Label = "Hann (recommended)", Value = IqWindow.Hann },
            new EnumOption<IqWindow> { Label = "None (rectangular)", Value = IqWindow.None }
        ];

        _selectedFreqEstimator = FreqEstimatorOptions.First(o => o.Value == _settings.FreqEstimator);
        _selectedFreqSource = FreqSourceOptions.First(o => o.Value == _settings.FreqSource);
        _selectedDemodMode = DemodModeOptions.First(o => o.Value == _settings.DemodMode);
        _selectedIqWindow = IqWindowOptions.First(o => o.Value == _settings.IqWindow);

        StartStopCommand = new RelayCommand(ToggleCapture);
        SetZeroCommand = new RelayCommand(SetZero);
        ClearZeroCommand = new RelayCommand(ClearZero);
        SaveConfigCommand = new RelayCommand(SaveConfig);
        ResetPhaseCommand = new RelayCommand(ResetPhaseSession);
        DownloadHistoryCommand = new RelayCommand(DownloadHistoryCsv);
        RefreshInputDevicesCommand = new RelayCommand(RefreshInputDevices);

        _capture.LivePointAvailable += point =>
        {
            lock (_phaseHistoryLock)
            {
                RecordPhasePoint(point);
            }

            _latestPoint = point;
            NoteHistoryRowLogged(point);
            LivePointReceived?.Invoke(point);
        };
        _capture.ErrorOccurred += msg => StatusText = msg;

        RefreshExportableRowCount();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action<LivePoint>? LivePointReceived;
    public event Action? PhaseSessionReset;

    public ObservableCollection<AudioDeviceInfo> InputDevices { get; }
    public IReadOnlyList<int> SampleRates { get; }
    public IReadOnlyList<EnumOption<FreqEstimator>> FreqEstimatorOptions { get; }
    public IReadOnlyList<EnumOption<FreqSource>> FreqSourceOptions { get; }
    public IReadOnlyList<EnumOption<DemodMode>> DemodModeOptions { get; }
    public IReadOnlyList<EnumOption<IqWindow>> IqWindowOptions { get; }

    public DmtdSettings Settings => _settings;

    public AudioDeviceInfo? SelectedInputDevice
    {
        get => _selectedInputDevice;
        set
        {
            if (SetField(ref _selectedInputDevice, value))
            {
                _settings.DeviceId = value?.Id;
                PersistSettings();
            }
        }
    }

    public int SelectedSampleRate
    {
        get => _settings.SampleRate;
        set
        {
            if (_settings.SampleRate != value)
            {
                _settings.SampleRate = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(BlockSizeDisplay));
                PersistSettings();
            }
        }
    }

    public string BlockDurationMsText
    {
        get => _blockDurationMsText;
        set
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                ResetBlockDurationMsText();
                return;
            }

            if (!double.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.CurrentCulture, out var parsed) &&
                !double.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out parsed))
            {
                ResetBlockDurationMsText();
                return;
            }

            var clamped = Math.Clamp(parsed, 100, 60_000);
            if (Math.Abs(_settings.BlockDurationMs - clamped) > 0.01)
            {
                _settings.BlockDurationMs = clamped;
                OnPropertyChanged(nameof(BlockSizeDisplay));
                PersistSettings();
            }

            var formatted = FormatBlockDurationMs(clamped);
            if (_blockDurationMsText != formatted)
            {
                _blockDurationMsText = formatted;
                OnPropertyChanged();
            }
        }
    }

    public string BlockSizeDisplay =>
        $"{_settings.ResolveBlockSize(_settings.SampleRate):N0} samples @ {_settings.SampleRate / 1000.0:F0} kHz";

    public double BeatFrequency
    {
        get => _settings.BeatFrequency;
        set
        {
            if (Math.Abs(_settings.BeatFrequency - value) > double.Epsilon)
            {
                _settings.BeatFrequency = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ExpansionFactorDisplay));
                PersistSettings();
            }
        }
    }

    public double RefFrequencyMhz
    {
        get => _settings.RefFrequency / 1_000_000.0;
        set
        {
            var hz = value * 1_000_000.0;
            if (Math.Abs(_settings.RefFrequency - hz) > double.Epsilon)
            {
                _settings.RefFrequency = hz;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ExpansionFactorDisplay));
                PersistSettings();
            }
        }
    }

    public string ExpansionFactorDisplay => _settings.ExpansionFactor.ToString("N0");

    public EnumOption<FreqEstimator>? SelectedFreqEstimator
    {
        get => _selectedFreqEstimator;
        set
        {
            if (SetField(ref _selectedFreqEstimator, value) && value is not null)
            {
                _settings.FreqEstimator = value.Value;
                NotifyConfigVisibility();
                PersistSettings();
            }
        }
    }

    public EnumOption<DemodMode>? SelectedDemodMode
    {
        get => _selectedDemodMode;
        set
        {
            if (SetField(ref _selectedDemodMode, value) && value is not null)
            {
                _settings.DemodMode = value.Value;
                NotifyConfigVisibility();
                PersistSettings();
            }
        }
    }

    public EnumOption<FreqSource>? SelectedFreqSource
    {
        get => _selectedFreqSource;
        set
        {
            if (SetField(ref _selectedFreqSource, value) && value is not null)
            {
                _settings.FreqSource = value.Value;
                PersistSettings();
            }
        }
    }

    public EnumOption<IqWindow>? SelectedIqWindow
    {
        get => _selectedIqWindow;
        set
        {
            if (SetField(ref _selectedIqWindow, value) && value is not null)
            {
                _settings.IqWindow = value.Value;
                PersistSettings();
            }
        }
    }

    public double IqLpfCutoffHz
    {
        get => _settings.IqLpfCutoffHz;
        set
        {
            if (Math.Abs(_settings.IqLpfCutoffHz - value) > double.Epsilon)
            {
                _settings.IqLpfCutoffHz = value;
                OnPropertyChanged();
                PersistSettings();
            }
        }
    }

    public int IqLpfOrder
    {
        get => _settings.IqLpfOrder;
        set
        {
            if (_settings.IqLpfOrder != value)
            {
                _settings.IqLpfOrder = Math.Clamp(value, 1, 12);
                OnPropertyChanged();
                PersistSettings();
            }
        }
    }

    public double IqMinMag
    {
        get => _settings.IqMinMag;
        set
        {
            if (Math.Abs(_settings.IqMinMag - value) > double.Epsilon)
            {
                _settings.IqMinMag = value;
                OnPropertyChanged();
                PersistSettings();
            }
        }
    }

    public double PllKp
    {
        get => _settings.PllKp;
        set
        {
            if (Math.Abs(_settings.PllKp - value) > double.Epsilon)
            {
                _settings.PllKp = value;
                OnPropertyChanged();
                PersistSettings();
            }
        }
    }

    public double PllKi
    {
        get => _settings.PllKi;
        set
        {
            if (Math.Abs(_settings.PllKi - value) > double.Epsilon)
            {
                _settings.PllKi = value;
                OnPropertyChanged();
                PersistSettings();
            }
        }
    }

    public double PllMinMag
    {
        get => _settings.PllMinMag;
        set
        {
            if (Math.Abs(_settings.PllMinMag - value) > double.Epsilon)
            {
                _settings.PllMinMag = value;
                OnPropertyChanged();
                PersistSettings();
            }
        }
    }

    public int HistoryRetentionDays
    {
        get => _settings.HistoryRetentionDays;
        set
        {
            if (_settings.HistoryRetentionDays != value)
            {
                _settings.HistoryRetentionDays = Math.Max(1, value);
                OnPropertyChanged();
                PersistSettings();
            }
        }
    }

    public bool ShowFreqSource => _settings.FreqEstimator == FreqEstimator.FftPeak;
    public bool ShowIqLpfFields => _settings.DemodMode == DemodMode.BlockIqFir;
    public bool ShowIqMinMag => _settings.DemodMode is DemodMode.BlockIq or DemodMode.BlockIqFir;
    public bool ShowPllFields => _settings.DemodMode == DemodMode.PllTracker;

    public bool IsCapturing
    {
        get => _isCapturing;
        private set
        {
            if (SetField(ref _isCapturing, value))
            {
                OnPropertyChanged(nameof(StartStopButtonText));
            }
        }
    }

    public string StartStopButtonText => IsCapturing ? "Stop" : "Start";

    public string StatusText
    {
        get => _statusText;
        private set => SetField(ref _statusText, value);
    }

    public LivePoint? LatestPoint
    {
        get => _latestPoint;
        private set => SetField(ref _latestPoint, value);
    }

    public int MaWindow
    {
        get => _maWindow;
        set
        {
            if (SetField(ref _maWindow, Math.Clamp(value, 2, 600)))
            {
                UpdateMetricDisplay();
            }
        }
    }

    public string PrimaryMetricTitle
    {
        get => _primaryMetricTitle;
        private set => SetField(ref _primaryMetricTitle, value);
    }

    public string PrimaryMetricValue
    {
        get => _primaryMetricValue;
        private set => SetField(ref _primaryMetricValue, value);
    }

    public string PrimaryMetricUnit
    {
        get => _primaryMetricUnit;
        private set => SetField(ref _primaryMetricUnit, value);
    }

    public string SecondaryMetricTitle
    {
        get => _secondaryMetricTitle;
        private set => SetField(ref _secondaryMetricTitle, value);
    }

    public string SecondaryMetricValue
    {
        get => _secondaryMetricValue;
        private set => SetField(ref _secondaryMetricValue, value);
    }

    public string SecondaryMetricUnit
    {
        get => _secondaryMetricUnit;
        private set => SetField(ref _secondaryMetricUnit, value);
    }

    public string StdMetricTitle
    {
        get => _stdMetricTitle;
        private set => SetField(ref _stdMetricTitle, value);
    }

    public string StdMetricValue
    {
        get => _stdMetricValue;
        private set => SetField(ref _stdMetricValue, value);
    }

    public string StdMetricUnit
    {
        get => _stdMetricUnit;
        private set => SetField(ref _stdMetricUnit, value);
    }

    public string DetailMetrics
    {
        get => _detailMetrics;
        private set => SetField(ref _detailMetrics, value);
    }

    public string ChannelBeatFrequencyDisplay
    {
        get => _channelBeatFrequencyDisplay;
        private set => SetField(ref _channelBeatFrequencyDisplay, value);
    }

    public string ChannelRmsDisplay
    {
        get => _channelRmsDisplay;
        private set => SetField(ref _channelRmsDisplay, value);
    }

    public string ChannelPhaseAbDisplay
    {
        get => _channelPhaseAbDisplay;
        private set => SetField(ref _channelPhaseAbDisplay, value);
    }

    public bool ShowSlipWarning
    {
        get => _showSlipWarning;
        private set => SetField(ref _showSlipWarning, value);
    }

    public string SlipWarningText
    {
        get => _slipWarningText;
        private set => SetField(ref _slipWarningText, value);
    }

    public string DownloadCsvToolTip =>
        _sessionSince is null
            ? "Download full phase time-series history as CSV"
            : "Download phase history since the last Reset as CSV";

    public bool EnableHistoryLogging
    {
        get => _settings.EnableHistoryLogging;
        set
        {
            if (_settings.EnableHistoryLogging == value)
            {
                return;
            }

            _settings.EnableHistoryLogging = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShowDownloadCsv));

            if (value)
            {
                EnsureHistoryStore();
                if (IsCapturing)
                {
                    _capture.SetHistoryStore(_history);
                }

                RefreshExportableRowCount();
            }
            else
            {
                _exportableRowCount = 0;
                OnPropertyChanged(nameof(ShowDownloadCsv));
                if (IsCapturing)
                {
                    _capture.SetHistoryStore(null);
                }
            }

            PersistSettings();
        }
    }

    public bool ShowDownloadCsv => _settings.EnableHistoryLogging && _exportableRowCount > 0;

    public bool PhaseZeroActive =>
        Math.Abs(_settings.PhaseZeroOffsetPs) > 1e-12 || Math.Abs(_settings.PhaseZeroOffsetRad) > 1e-12;

    public string PhaseZeroDisplay =>
        PhaseZeroActive
            ? PhaseMetricFormatter.FormatCompact(_settings.PhaseZeroOffsetPs)
            : "Not set";

    public bool ChannelsLiveEnabled
    {
        get => _channelsLiveEnabled;
        set => SetField(ref _channelsLiveEnabled, value);
    }

    public DmtdCaptureService CaptureService => _capture;

    public ICommand StartStopCommand { get; }
    public ICommand SetZeroCommand { get; }
    public ICommand ClearZeroCommand { get; }
    public ICommand SaveConfigCommand { get; }
    public ICommand ResetPhaseCommand { get; }
    public ICommand DownloadHistoryCommand { get; }
    public ICommand RefreshInputDevicesCommand { get; }

    public void Activate()
    {
    }

    public void Deactivate()
    {
        if (IsCapturing)
        {
            StopCapture();
        }
    }

    public DmtdSnapshotData? GetApiMetrics()
    {
        List<double> phaseHistorySnapshot;
        lock (_phaseHistoryLock)
        {
            phaseHistorySnapshot = _phasePsHistory.ToList();
        }

        if (_latestPoint is null)
        {
            return new DmtdSnapshotData
            {
                MaWindow = _maWindow,
                PhaseZeroActive = PhaseZeroActive,
                PhaseZeroOffsetPs = _settings.PhaseZeroOffsetPs
            };
        }

        var latest = _latestPoint;
        var stats = ComputeStats(phaseHistorySnapshot);
        return new DmtdSnapshotData
        {
            PhaseDiffPs = latest.PhaseDiffPs,
            PhaseDiffRad = latest.PhaseDiffRad,
            BeatFreqHz = latest.BeatFreq,
            MovingAveragePs = ComputeMovingAverage(phaseHistorySnapshot),
            StdDevPs = stats?.Std,
            MaWindow = _maWindow,
            PhaseZeroActive = PhaseZeroActive,
            PhaseZeroOffsetPs = _settings.PhaseZeroOffsetPs,
            RmsA = latest.RmsA,
            RmsB = latest.RmsB,
            SlipCount = latest.SlipCount,
            LatestTimestamp = latest.Timestamp
        };
    }

    private void NotifyConfigVisibility()
    {
        OnPropertyChanged(nameof(ShowFreqSource));
        OnPropertyChanged(nameof(ShowIqLpfFields));
        OnPropertyChanged(nameof(ShowIqMinMag));
        OnPropertyChanged(nameof(ShowPllFields));
    }

    private void RefreshInputDevices()
    {
        var previousId = _selectedInputDevice?.Id ?? _settings.DeviceId;
        InputDevices.Clear();
        foreach (var device in AudioDeviceEnumerator.GetInputDevices())
        {
            InputDevices.Add(device);
        }

        SelectedInputDevice = ResolveDevice(InputDevices, previousId);
        StatusText = InputDevices.Count > 0
            ? $"Found {InputDevices.Count} input device(s)."
            : "No input devices found.";
    }

    private void ToggleCapture()
    {
        if (IsCapturing)
        {
            StopCapture();
        }
        else
        {
            StartCapture();
        }
    }

    private void StartCapture()
    {
        try
        {
            PhaseHistoryStore? history = null;
            if (_settings.EnableHistoryLogging)
            {
                EnsureHistoryStore();
                _history!.PruneOldRows(_settings.HistoryRetentionDays);
                history = _history;
            }

            _capture.Start(_settings, history);
            IsCapturing = true;
            StatusText = "Capturing";
            StartMetricsTimer();
            UpdateMetricDisplay();
            RefreshExportableRowCount();
        }
        catch (Exception ex)
        {
            StatusText = ex.Message;
        }
    }

    private void StopCapture()
    {
        _capture.Stop();
        IsCapturing = false;
        StopMetricsTimer();
        StatusText = string.Empty;
        UpdateMetricDisplay();
        RefreshExportableRowCount();
        PersistSettings();
    }

    private void EnsureHistoryStore()
    {
        _history ??= new PhaseHistoryStore(AppDataPaths.DmtdHistoryFile);
    }

    private void StartMetricsTimer()
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            return;
        }

        _metricsTimer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.DataBind, (_, _) =>
        {
            if (!IsCapturing)
            {
                return;
            }

            UpdateMetricDisplay();
        }, dispatcher);

        if (!_metricsTimer.IsEnabled)
        {
            _metricsTimer.Start();
        }
    }

    private void StopMetricsTimer()
    {
        _metricsTimer?.Stop();
    }

    private void SetZero()
    {
        var latest = _capture.GetLatestRawPhase();
        if (latest is null)
        {
            StatusText = "No phase data yet.";
            return;
        }

        _settings.PhaseZeroOffsetRad = -latest.Value.Rad;
        _settings.PhaseZeroOffsetPs = -latest.Value.Ps;
        _capture.SetPhaseZeroOffset(_settings.PhaseZeroOffsetRad, _settings.PhaseZeroOffsetPs);
        NotifyPhaseZeroChanged();
        PersistSettings();
        StatusText = "Phase zero set.";
    }

    private void ClearZero()
    {
        _settings.PhaseZeroOffsetRad = 0;
        _settings.PhaseZeroOffsetPs = 0;
        _settings.SavedUnwrapState = null;
        _capture.SetPhaseZeroOffset(0, 0);
        NotifyPhaseZeroChanged();
        PersistSettings();
        StatusText = "Phase zero cleared.";
    }

    private void NotifyPhaseZeroChanged()
    {
        OnPropertyChanged(nameof(PhaseZeroActive));
        OnPropertyChanged(nameof(PhaseZeroDisplay));
    }

    private void SaveConfig()
    {
        PersistSettings();
        StatusText = "Configuration saved.";
    }

    private void ResetPhaseSession()
    {
        _sessionSince = DateTimeOffset.UtcNow;
        OnPropertyChanged(nameof(DownloadCsvToolTip));
        _phasePsHistory.Clear();
        ShowSlipWarning = false;
        SlipWarningText = string.Empty;
        _exportableRowCount = 0;
        OnPropertyChanged(nameof(ShowDownloadCsv));
        UpdateMetricDisplay();
        PhaseSessionReset?.Invoke();
    }

    private void NoteHistoryRowLogged(LivePoint point)
    {
        if (!_settings.EnableHistoryLogging)
        {
            return;
        }

        if (_sessionSince is not null && point.Timestamp < _sessionSince)
        {
            return;
        }

        _exportableRowCount++;
        OnPropertyChanged(nameof(ShowDownloadCsv));
    }

    private void RefreshExportableRowCount()
    {
        if (!_settings.EnableHistoryLogging)
        {
            _exportableRowCount = 0;
            OnPropertyChanged(nameof(ShowDownloadCsv));
            return;
        }

        PhaseHistoryStore? disposableStore = null;
        try
        {
            var store = _history ?? (disposableStore = new PhaseHistoryStore(AppDataPaths.DmtdHistoryFile));
            _exportableRowCount = store.Count(_sessionSince?.ToString("O"));
        }
        catch
        {
            _exportableRowCount = 0;
        }
        finally
        {
            disposableStore?.Dispose();
        }

        OnPropertyChanged(nameof(ShowDownloadCsv));
    }

    private void DownloadHistoryCsv()
    {
        PhaseHistoryStore? disposableStore = null;
        try
        {
            var store = _history ?? (disposableStore = new PhaseHistoryStore(AppDataPaths.DmtdHistoryFile));
            var since = _sessionSince?.ToString("O");
            var csv = store.ExportCsv(since);
            var rowCount = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length - 1;

            var dialog = new SaveFileDialog
            {
                Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
                DefaultExt = "csv",
                FileName = $"dmtd-phase-{DateTime.Now:yyyyMMdd-HHmmss}.csv"
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            File.WriteAllText(dialog.FileName, csv);
            StatusText = rowCount > 0
                ? $"Exported {rowCount:N0} rows to {dialog.FileName}"
                : $"Saved empty CSV (no rows matched) to {dialog.FileName}";
            RefreshExportableRowCount();
        }
        catch (Exception ex)
        {
            StatusText = $"CSV export failed: {ex.Message}";
        }
        finally
        {
            disposableStore?.Dispose();
        }
    }

    private void RecordPhasePoint(LivePoint point)
    {
        _phasePsHistory.Add(point.PhaseDiffPs);
        while (_phasePsHistory.Count > MaxPhaseHistoryPoints)
        {
            _phasePsHistory.RemoveAt(0);
        }
    }

    private void UpdateMetricDisplay()
    {
        PrimaryMetricTitle = "Latest Δt";
        SecondaryMetricTitle = "Moving average";
        StdMetricTitle = "σ (std dev)";

        List<double> phaseHistorySnapshot;
        lock (_phaseHistoryLock)
        {
            phaseHistorySnapshot = _phasePsHistory.ToList();
        }

        if (_latestPoint is null || phaseHistorySnapshot.Count == 0)
        {
            PrimaryMetricValue = "—";
            PrimaryMetricUnit = "ps";
            SecondaryMetricValue = "—";
            SecondaryMetricUnit = "ps";
            StdMetricValue = "—";
            StdMetricUnit = "ps";
            DetailMetrics = IsCapturing
                ? "Waiting for phase data..."
                : "Start capture to begin live phase measurement.";
            ChannelBeatFrequencyDisplay = "—";
            ChannelRmsDisplay = "—";
            ChannelPhaseAbDisplay = "—";
            return;
        }

        var latest = _latestPoint;
        var (latestValue, latestUnit) = PhaseMetricFormatter.FormatHero(latest.PhaseDiffPs);
        PrimaryMetricValue = latestValue;
        PrimaryMetricUnit = latestUnit;

        var ma = ComputeMovingAverage(phaseHistorySnapshot);
        if (ma is null)
        {
            SecondaryMetricValue = "—";
            SecondaryMetricUnit = "ps";
        }
        else
        {
            var (maValue, maUnit) = PhaseMetricFormatter.FormatHero(ma.Value);
            SecondaryMetricValue = maValue;
            SecondaryMetricUnit = maUnit;
        }

        var diffDeg = PhaseMetricFormatter.PhaseDiffDeg(latest.PhaseDiffRad);
        var stats = ComputeStats(phaseHistorySnapshot);
        var detail = new StringBuilder();
        detail.AppendLine($"Beat-note phase: {diffDeg:F2}°");
        detail.AppendLine($"Window: {_maWindow} samples ({Math.Min(_maWindow, phaseHistorySnapshot.Count)} effective)");

        ChannelBeatFrequencyDisplay = $"{latest.BeatFreq:F2} Hz";
        ChannelRmsDisplay = $"{latest.RmsA:F3} / {latest.RmsB:F3}";
        ChannelPhaseAbDisplay =
            $"{PhaseMetricFormatter.FormatCompact(latest.PhaseAPs)} / {PhaseMetricFormatter.FormatCompact(latest.PhaseBPs)}";

        if (stats is PhaseStats phaseStats)
        {
            var (stdValue, stdUnit) = PhaseMetricFormatter.FormatHero(phaseStats.Std);
            StdMetricValue = stdValue;
            StdMetricUnit = stdUnit;

            detail.AppendLine();
            detail.Append($"mean {PhaseMetricFormatter.FormatCompact(phaseStats.Mean)}");
            detail.AppendLine();
            detail.Append($"min {PhaseMetricFormatter.FormatCompact(phaseStats.Min)}");
            detail.Append($"   max {PhaseMetricFormatter.FormatCompact(phaseStats.Max)}");
            detail.AppendLine();
            detail.Append($"n = {phaseStats.Count:N0}");
        }
        else
        {
            StdMetricValue = "—";
            StdMetricUnit = "ps";
        }

        if (latest.SlipCount > 0)
        {
            detail.AppendLine();
            detail.Append($"Total slips: {latest.SlipCount}");
        }

        DetailMetrics = detail.ToString().TrimEnd();

        if (latest.SlipCount > 0)
        {
            ShowSlipWarning = true;
            SlipWarningText = latest.SlipK != 0
                ? $"PHASE SLIP  k={latest.SlipK}  Δφ={latest.SlipStepRad:F3} rad  (total={latest.SlipCount})"
                : $"PHASE SLIP  total={latest.SlipCount}";
        }
        else
        {
            ShowSlipWarning = false;
            SlipWarningText = string.Empty;
        }
    }

    private double? ComputeMovingAverage(IReadOnlyList<double> phaseHistory)
    {
        if (phaseHistory.Count == 0)
        {
            return null;
        }

        var span = Math.Min(_maWindow, phaseHistory.Count);
        var start = phaseHistory.Count - span;
        double sum = 0;
        for (var i = start; i < phaseHistory.Count; i++)
        {
            sum += phaseHistory[i];
        }

        return sum / span;
    }

    private static PhaseStats? ComputeStats(IReadOnlyList<double> values)
    {
        var count = values.Count;
        if (count == 0)
        {
            return null;
        }

        double sum = 0;
        var min = values[0];
        var max = values[0];
        for (var i = 0; i < count; i++)
        {
            var v = values[i];
            sum += v;
            if (v < min)
            {
                min = v;
            }

            if (v > max)
            {
                max = v;
            }
        }

        var mean = sum / count;
        double sqSum = 0;
        for (var i = 0; i < count; i++)
        {
            var d = values[i] - mean;
            sqSum += d * d;
        }

        return new PhaseStats(mean, Math.Sqrt(sqSum / count), count, min, max);
    }

    private readonly record struct PhaseStats(double Mean, double Std, int Count, double Min, double Max);

    private void PersistSettings() => DmtdSettingsStore.Save(_settings);

    private void ResetBlockDurationMsText()
    {
        var formatted = FormatBlockDurationMs(_settings.BlockDurationMs);
        if (_blockDurationMsText != formatted)
        {
            _blockDurationMsText = formatted;
            OnPropertyChanged(nameof(BlockDurationMsText));
        }
    }

    private static string FormatBlockDurationMs(double ms) =>
        Math.Abs(ms - Math.Round(ms)) < 0.01
            ? Math.Round(ms).ToString("0", CultureInfo.InvariantCulture)
            : ms.ToString("0.##", CultureInfo.InvariantCulture);

    private static AudioDeviceInfo? ResolveDevice(IEnumerable<AudioDeviceInfo> devices, string? id) =>
        string.IsNullOrWhiteSpace(id)
            ? devices.FirstOrDefault()
            : devices.FirstOrDefault(d => d.Id == id) ?? devices.FirstOrDefault();

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(name);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public void Dispose()
    {
        Deactivate();
        StopMetricsTimer();
        _history?.Dispose();
        _capture.Dispose();
        PersistSettings();
    }
}

internal sealed class RelayCommand : ICommand
{
    private readonly Action _execute;
    private readonly Func<bool>? _canExecute;

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged
    {
        add => System.Windows.Input.CommandManager.RequerySuggested += value;
        remove => System.Windows.Input.CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;
    public void Execute(object? parameter) => _execute();
}

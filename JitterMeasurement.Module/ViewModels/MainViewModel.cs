using JitterMeasurement.Core;
using JitterMeasurement.Core.Models;
using JitterMeasurement.Module.Api;
using JitterMeasurement.Module.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace JitterMeasurement.Module.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly AudioCaptureService _capture = new();
    private readonly AudioRingBuffer _ringBuffer;
    private readonly DispatcherTimer _uiTimer;
    private readonly AppSettings _settings = new();
    private readonly SavedSettings _savedSettings;
    private readonly ObservableCollection<AudioDeviceInfo> _devices = new();

    private PhaseDetectorCal? _storedCalibration;
    private AnalysisSnapshot? _latestSnapshot;
    private bool _isCapturing;
    private string _statusText = "Ready";
    private AudioDeviceInfo? _selectedDevice;
    private string _primaryMetricTitle = "Jitter RMS";
    private string _primaryMetricValue = "—";
    private string _primaryMetricUnit = "fs";
    private string _detailMetrics = "Start capture, then Calibrate while unlocked.";
    private bool _showClipWarning;

    public MainViewModel()
    {
        _savedSettings = SettingsStore.Load();
        CopyMeasurementSettings(_savedSettings.Measurement, _settings);

        var ringCapacity = (int)(_settings.SampleRate * (_settings.TimeWindowMs / 1000.0) * 2);
        _ringBuffer = new AudioRingBuffer(Math.Max(ringCapacity, 8192));

        Devices = _devices;
        foreach (var device in AudioCaptureService.GetInputDevices())
        {
            _devices.Add(device);
        }

        _selectedDevice = ResolveDevice(_settings.DeviceId);
        _storedCalibration = _savedSettings.Calibration is { KpdVPerRad: > 0 } cal ? cal : null;

        SampleRates = new[] { 44100, 48000, 88200, 96000, 176400, 192000 };

        StartStopCommand = new RelayCommand(ToggleCapture, () => SelectedDevice is not null);
        CalibrateCommand = new RelayCommand(CalibrateNow, () => IsCapturing);

        _capture.SamplesAvailable += OnSamplesAvailable;
        _capture.ErrorOccurred += message => StatusText = message;

        _uiTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(33)
        };
        _uiTimer.Tick += (_, _) => RefreshAnalysis();
    }

    public bool TimeContinuousAutoscale
    {
        get => _savedSettings.TimeContinuousAutoscale;
        set
        {
            if (_savedSettings.TimeContinuousAutoscale == value)
            {
                return;
            }

            _savedSettings.TimeContinuousAutoscale = value;
            OnPropertyChanged();
            PersistSettings();
        }
    }

    public bool FftContinuousAutoscale
    {
        get => _savedSettings.FftContinuousAutoscale;
        set
        {
            if (_savedSettings.FftContinuousAutoscale == value)
            {
                return;
            }

            _savedSettings.FftContinuousAutoscale = value;
            OnPropertyChanged();
            PersistSettings();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action<AnalysisSnapshot>? SnapshotUpdated;
    public event Action? CaptureSessionStarted;
    public event Action? FftViewRangeChanged;

    public IReadOnlyList<AudioDeviceInfo> Devices { get; }
    public int CaptureSampleRate => _capture.SampleRate;
    public IReadOnlyList<int> SampleRates { get; }
    public IReadOnlyList<int> InputChannels { get; } = new[] { 1, 2 };

    public AudioDeviceInfo? SelectedDevice
    {
        get => _selectedDevice;
        set
        {
            if (SetField(ref _selectedDevice, value))
            {
                _settings.DeviceId = value?.Id;
                PersistSettings();
            }
        }
    }

    public int SelectedInputChannel
    {
        get => _settings.InputChannel;
        set
        {
            var channel = Math.Max(1, value);
            if (_settings.InputChannel == channel)
            {
                return;
            }

            _settings.InputChannel = channel;
            OnPropertyChanged();
            PersistSettings();
        }
    }

    public int SelectedSampleRate
    {
        get => _settings.SampleRate;
        set
        {
            if (_settings.SampleRate == value)
            {
                return;
            }

            _settings.SampleRate = value;
            OnPropertyChanged();
            RebuildRingBuffer();
            PersistSettings();
        }
    }

    public double FundamentalMHz
    {
        get => _settings.FundamentalHz / 1_000_000.0;
        set
        {
            var hz = value * 1_000_000.0;
            if (Math.Abs(_settings.FundamentalHz - hz) > double.Epsilon)
            {
                _settings.FundamentalHz = hz;
                OnPropertyChanged();
                UpdateMetricDisplay();
                PersistSettings();
            }
        }
    }

    public int HarmonicNumber
    {
        get => _settings.HarmonicNumber;
        set
        {
            if (_settings.HarmonicNumber != value)
            {
                _settings.HarmonicNumber = Math.Max(1, value);
                OnPropertyChanged();
                UpdateMetricDisplay();
                PersistSettings();
            }
        }
    }

    public double IntegrationLowHz
    {
        get => _settings.IntegrationBandLowHz;
        set
        {
            if (Math.Abs(_settings.IntegrationBandLowHz - value) > double.Epsilon)
            {
                _settings.IntegrationBandLowHz = value;
                OnPropertyChanged();
                PersistSettings();
            }
        }
    }

    public double IntegrationHighHz
    {
        get => _settings.IntegrationBandHighHz;
        set
        {
            if (Math.Abs(_settings.IntegrationBandHighHz - value) > double.Epsilon)
            {
                _settings.IntegrationBandHighHz = value;
                OnPropertyChanged();
                PersistSettings();
                FftViewRangeChanged?.Invoke();
            }
        }
    }

    public double FftViewMaxKHz
    {
        get => _settings.FftViewMaxHz / 1000.0;
        set
        {
            var hz = value <= 0 ? 0 : value * 1000.0;
            if (Math.Abs(_settings.FftViewMaxHz - hz) > double.Epsilon)
            {
                _settings.FftViewMaxHz = hz;
                OnPropertyChanged();
                PersistSettings();
                FftViewRangeChanged?.Invoke();
            }
        }
    }

    public double TimeWindowMs
    {
        get => _settings.TimeWindowMs;
        set
        {
            if (Math.Abs(_settings.TimeWindowMs - value) > double.Epsilon)
            {
                _settings.TimeWindowMs = value;
                OnPropertyChanged();
                RebuildRingBuffer();
                PersistSettings();
            }
        }
    }

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

    public string DetailMetrics
    {
        get => _detailMetrics;
        private set => SetField(ref _detailMetrics, value);
    }

    public bool ShowClipWarning
    {
        get => _showClipWarning;
        private set => SetField(ref _showClipWarning, value);
    }

    public ICommand StartStopCommand { get; }
    public ICommand CalibrateCommand { get; }

    public AnalysisSnapshot? LatestSnapshot => _latestSnapshot;

    public JitterSnapshotData GetApiMetrics()
    {
        var snapshot = _latestSnapshot;
        var jitter = snapshot?.Jitter;
        var calibration = _storedCalibration ?? snapshot?.Calibration;

        return new JitterSnapshotData
        {
            JitterRmsFs = jitter?.IsValid == true ? jitter.SigmaTFs : null,
            IntegratedFs = jitter?.IsValid == true ? jitter.IntegratedTFs : null,
            SigmaVRms = jitter?.IsValid == true ? jitter.SigmaVRms : null,
            SigmaPhiRad = jitter?.IsValid == true ? jitter.SigmaPhiRad : null,
            HarmonicFrequencyHz = jitter?.IsValid == true ? jitter.HarmonicFrequencyHz : null,
            Calibrated = calibration is { KpdVPerRad: > 0 },
            Vpp = calibration?.Vpp,
            KpdVPerRad = calibration?.KpdVPerRad,
            IsClipping = jitter?.IsClipping ?? calibration?.IsClipping ?? false,
            IsValid = jitter?.IsValid ?? false,
            Message = jitter?.Message
        };
    }

    public void RefreshDevices()
    {
        var previousId = _selectedDevice?.Id ?? _settings.DeviceId;
        _devices.Clear();
        foreach (var device in AudioCaptureService.GetInputDevices())
        {
            _devices.Add(device);
        }

        SelectedDevice = ResolveDevice(previousId);
        StatusText = _devices.Count > 0
            ? $"Found {_devices.Count} input device(s)."
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
        if (SelectedDevice is null)
        {
            StatusText = "Select an input device.";
            return;
        }

        try
        {
            _ringBuffer.Clear();
            _latestSnapshot = null;
            CaptureSessionStarted?.Invoke();
            UpdateMetricDisplay();

            _capture.Start(SelectedDevice.Id, SelectedSampleRate, SelectedInputChannel - 1);
            _uiTimer.Start();
            IsCapturing = true;

            var rateNote = _capture.SampleRate == SelectedSampleRate
                ? $"{_capture.SampleRate} Hz"
                : $"{_capture.SampleRate} Hz (requested {SelectedSampleRate} Hz)";

            StatusText = $"Capturing {SelectedDevice.Name} — input {SelectedInputChannel} at {rateNote}";
        }
        catch (Exception ex)
        {
            StatusText = ex.Message;
        }
    }

    private void StopCapture()
    {
        _uiTimer.Stop();
        _capture.Stop();
        IsCapturing = false;
        StatusText = "Stopped";
    }

    private void CalibrateNow()
    {
        var sampleRate = AnalysisSampleRate;
        var sampleCount = (int)(sampleRate * (_settings.TimeWindowMs / 1000.0));
        var samples = _ringBuffer.CopyLatest(sampleCount);
        if (samples.Length < 64)
        {
            StatusText = "Not enough data to calibrate yet.";
            return;
        }

        var calibration = CalibrationService.CalibrateFromVpp(samples);
        if (calibration.KpdVPerRad <= 0)
        {
            StatusText = calibration.Message ?? "Calibration failed.";
            return;
        }

        _storedCalibration = calibration;
        StatusText = calibration.IsClipping
            ? $"Calibrated Vpp={calibration.Vpp:F4} V (clipping — reduce gain)"
            : $"Calibrated Vpp={calibration.Vpp:F4} V, Kpd={calibration.KpdVPerRad:F4} V/rad";

        PersistSettings();
        RefreshAnalysis();
    }

    private void OnSamplesAvailable(float[] samples)
    {
        _ringBuffer.Write(samples);
    }

    private void RefreshAnalysis()
    {
        var sampleRate = AnalysisSampleRate;
        var sampleCount = (int)(sampleRate * (_settings.TimeWindowMs / 1000.0));
        var samples = _ringBuffer.CopyLatest(sampleCount);
        if (samples.Length < 64)
        {
            return;
        }

        var snapshot = MeasurementEngine.AnalyzeWindow(
            samples,
            CreateAnalysisSettings(sampleRate),
            _storedCalibration);

        _latestSnapshot = snapshot;
        SnapshotUpdated?.Invoke(snapshot);
        UpdateMetricDisplay();
    }

    private void UpdateMetricDisplay()
    {
        PrimaryMetricTitle = "Jitter RMS";
        PrimaryMetricUnit = "fs";

        if (_latestSnapshot is null)
        {
            PrimaryMetricValue = "—";
            DetailMetrics = _storedCalibration is null
                ? "Start capture, then Calibrate while unlocked."
                : FormatCalibrationDetail(_storedCalibration) + "\n\nWaiting for data...";
            ShowClipWarning = false;
            return;
        }

        var jitter = _latestSnapshot.Jitter;
        if (jitter is null || !jitter.IsValid)
        {
            PrimaryMetricValue = "—";
            DetailMetrics = _storedCalibration is null
                ? "Click Calibrate while unlocked to set Vpp."
                : FormatCalibrationDetail(_storedCalibration) + "\n\n" +
                  (jitter?.Message ?? "Waiting for locked signal...");
            ShowClipWarning = _latestSnapshot.Calibration?.IsClipping == true;
            return;
        }

        PrimaryMetricValue = jitter.SigmaTFs.ToString("F1");
        DetailMetrics =
            $"Integrated ({IntegrationLowHz:F0}–{IntegrationHighHz:F0} Hz)\n" +
            $"{jitter.IntegratedTFs:F1} fs\n\n" +
            $"RMS error: {jitter.SigmaVRms * 1000:F3} mV\n" +
            $"Phase: {jitter.SigmaPhiRad * 1000:F2} mrad\n" +
            $"Harmonic: {jitter.HarmonicFrequencyHz / 1e6:F3} MHz\n\n" +
            FormatCalibrationDetail(_storedCalibration) +
            (jitter.Message is not null ? $"\n\n{jitter.Message}" : string.Empty);
        ShowClipWarning = jitter.IsClipping;
    }

    private static string FormatCalibrationDetail(PhaseDetectorCal? calibration)
    {
        if (calibration is null)
        {
            return "Calibration: not set";
        }

        return
            $"Calibration:\n" +
            $"Vpp: {calibration.Vpp:F4} V\n" +
            $"Kpd: {calibration.KpdVPerRad:F4} V/rad";
    }

    private void RebuildRingBuffer()
    {
        _ringBuffer.Clear();
    }

    private int AnalysisSampleRate => IsCapturing ? _capture.SampleRate : _settings.SampleRate;

    private AppSettings CreateAnalysisSettings(int sampleRate) => new()
    {
        FundamentalHz = _settings.FundamentalHz,
        HarmonicNumber = _settings.HarmonicNumber,
        SampleRate = sampleRate,
        IntegrationBandLowHz = _settings.IntegrationBandLowHz,
        IntegrationBandHighHz = _settings.IntegrationBandHighHz,
        TimeWindowMs = _settings.TimeWindowMs,
        DeviceId = _settings.DeviceId,
        InputChannel = _settings.InputChannel,
        FftViewMaxHz = _settings.FftViewMaxHz
    };

    private AudioDeviceInfo? ResolveDevice(string? deviceId)
    {
        if (!string.IsNullOrWhiteSpace(deviceId))
        {
            var match = Devices.FirstOrDefault(d => d.Id == deviceId);
            if (match is not null)
            {
                return match;
            }
        }

        return Devices.FirstOrDefault(d =>
            d.Name.Contains("Focusrite", StringComparison.OrdinalIgnoreCase)) ?? Devices.FirstOrDefault();
    }

    private static void CopyMeasurementSettings(AppSettings source, AppSettings target)
    {
        target.FundamentalHz = source.FundamentalHz;
        target.HarmonicNumber = source.HarmonicNumber;
        target.SampleRate = source.SampleRate;
        target.IntegrationBandLowHz = source.IntegrationBandLowHz;
        target.IntegrationBandHighHz = source.IntegrationBandHighHz;
        target.TimeWindowMs = source.TimeWindowMs;
        target.FftViewMaxHz = source.FftViewMaxHz;
        target.DeviceId = source.DeviceId;
        target.InputChannel = source.InputChannel;
    }

    private void PersistSettings()
    {
        CopyMeasurementSettings(_settings, _savedSettings.Measurement);
        _savedSettings.Calibration = _storedCalibration is { KpdVPerRad: > 0 } ? _storedCalibration : null;
        SettingsStore.Save(_savedSettings);
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public void Dispose()
    {
        _uiTimer.Stop();
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
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;

    public void Execute(object? parameter) => _execute();
}

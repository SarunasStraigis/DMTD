using JitterMeasurement.Core.Models;
using JitterMeasurement.Module.Services;
using JitterMeasurement.Module.ViewModels;
using PhaseLab.Api;
using System.Windows;
using System.Windows.Threading;

namespace JitterMeasurement.Module.Api;

public sealed class JitterApiModule : IMeasurementApiModule
{
    private static readonly string[] Actions = ["calibrate"];

    private readonly MainViewModel _viewModel;

    public JitterApiModule(MainViewModel viewModel) => _viewModel = viewModel;

    public string Id => "jitter";
    public string DisplayName => "Jitter Measurement";

    public ModuleInfoDto GetInfo() => OnUi(() => new ModuleInfoDto
    {
        Id = Id,
        DisplayName = DisplayName,
        Capabilities = BuildCapabilities().ToCapabilityNames(),
        Actions = Actions
    });

    public ModuleStatusDto GetStatus() => OnUi(() =>
    {
        var device = _viewModel.SelectedDevice;
        return new ModuleStatusDto
        {
            ModuleId = Id,
            Capturing = _viewModel.IsCapturing,
            StatusText = _viewModel.StatusText,
            DeviceId = device?.Id,
            DeviceName = device?.Name,
            SampleRate = _viewModel.IsCapturing
                ? _viewModel.CaptureSampleRate
                : _viewModel.SelectedSampleRate
        };
    });

    public ModuleSnapshotDto GetSnapshot() => OnUi(() => new ModuleSnapshotDto
    {
        ModuleId = Id,
        Timestamp = DateTimeOffset.UtcNow,
        Capturing = _viewModel.IsCapturing,
        StatusText = _viewModel.StatusText,
        Data = _viewModel.GetApiMetrics()
    });

    public IReadOnlyList<AudioDeviceDto> ListDevices() => OnUi(() =>
        _viewModel.Devices
            .Select(d => new AudioDeviceDto { Id = d.Id, Name = d.Name })
            .ToList());

    public void RefreshDevices() => OnUi(() => _viewModel.RefreshDevices());

    public void StartCapture(CaptureRequestDto? request) => OnUi(() =>
    {
        if (request?.DeviceId is not null)
        {
            _viewModel.SelectedDevice = _viewModel.Devices
                .FirstOrDefault(d => d.Id == request.DeviceId)
                ?? throw new ModuleApiException($"Device '{request.DeviceId}' not found.", 404);
        }

        if (request?.SampleRate is int sampleRate)
        {
            if (!_viewModel.SampleRates.Contains(sampleRate))
            {
                throw new ModuleApiException($"Sample rate {sampleRate} is not supported.", 409);
            }

            _viewModel.SelectedSampleRate = sampleRate;
        }

        if (request?.InputChannel is int inputChannel)
        {
            _viewModel.SelectedInputChannel = inputChannel;
        }

        if (_viewModel.IsCapturing)
        {
            throw new ModuleApiException("Capture is already running.", 409);
        }

        if (_viewModel.SelectedDevice is null)
        {
            throw new ModuleApiException("No input device selected.", 409);
        }

        _viewModel.StartStopCommand.Execute(null);
        if (!_viewModel.IsCapturing)
        {
            throw new ModuleApiException(_viewModel.StatusText, 409);
        }
    });

    public void StopCapture() => OnUi(() =>
    {
        if (!_viewModel.IsCapturing)
        {
            throw new ModuleApiException("Capture is not running.", 409);
        }

        _viewModel.StartStopCommand.Execute(null);
    });

    public ActionResultDto ExecuteAction(string actionId) => OnUi(() => actionId switch
    {
        "calibrate" => ExecuteCalibrate(),
        _ => throw new ModuleApiException($"Unknown action '{actionId}'.", 404)
    });

    private static ModuleCapabilities BuildCapabilities() =>
        ModuleCapabilities.Capture |
        ModuleCapabilities.Devices |
        ModuleCapabilities.Actions |
        ModuleCapabilities.Calibration;

    private ActionResultDto ExecuteCalibrate()
    {
        if (!_viewModel.IsCapturing)
        {
            throw new ModuleApiException("Capture must be running to calibrate.", 409);
        }

        _viewModel.CalibrateCommand.Execute(null);
        var metrics = _viewModel.GetApiMetrics();
        if (metrics?.Calibrated != true)
        {
            throw new ModuleApiException(_viewModel.StatusText, 409);
        }

        return new ActionResultDto
        {
            Status = "calibrated",
            Message = _viewModel.StatusText
        };
    }

    private static T OnUi<T>(Func<T> action)
    {
        var dispatcher = Application.Current?.Dispatcher
            ?? throw new ModuleApiException("UI thread is not available.", 503);

        return dispatcher.CheckAccess()
            ? action()
            : dispatcher.Invoke(action, DispatcherPriority.Send);
    }

    private static void OnUi(Action action) => OnUi(() =>
    {
        action();
        return true;
    });
}

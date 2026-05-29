using Dmtd.Module.Services;
using Dmtd.Module.ViewModels;
using PhaseLab.Api;
using System.Windows;
using System.Windows.Threading;

namespace Dmtd.Module.Api;

public sealed class DmtdApiModule : IMeasurementApiModule
{
    private static readonly string[] Actions =
    [
        "phase-zero/set",
        "phase-zero/clear",
        "session/reset"
    ];

    private readonly DmtdViewModel _viewModel;

    public DmtdApiModule(DmtdViewModel viewModel) => _viewModel = viewModel;

    public string Id => "dmtd";
    public string DisplayName => "DMTD Phase Analyser";

    public ModuleInfoDto GetInfo() => OnUi(() => new ModuleInfoDto
    {
        Id = Id,
        DisplayName = DisplayName,
        Capabilities = BuildCapabilities().ToCapabilityNames(),
        Actions = Actions
    });

    public ModuleStatusDto GetStatus() => OnUi(() =>
    {
        var device = _viewModel.SelectedInputDevice;
        return new ModuleStatusDto
        {
            ModuleId = Id,
            Capturing = _viewModel.IsCapturing,
            StatusText = _viewModel.StatusText,
            DeviceId = device?.Id,
            DeviceName = device?.Name,
            SampleRate = _viewModel.IsCapturing
                ? _viewModel.CaptureService.SampleRate
                : _viewModel.SelectedSampleRate
        };
    });

    public ModuleSnapshotDto GetSnapshot() => OnUi(() =>
    {
        var metrics = _viewModel.GetApiMetrics();
        return new ModuleSnapshotDto
        {
            ModuleId = Id,
            Timestamp = DateTimeOffset.UtcNow,
            Capturing = _viewModel.IsCapturing,
            StatusText = _viewModel.StatusText,
            Data = metrics
        };
    });

    public IReadOnlyList<AudioDeviceDto> ListDevices() => OnUi(() =>
        _viewModel.InputDevices
            .Select(d => new AudioDeviceDto { Id = d.Id, Name = d.Name })
            .ToList());

    public void RefreshDevices() => OnUi(() => _viewModel.RefreshInputDevicesCommand.Execute(null));

    public void StartCapture(CaptureRequestDto? request) => OnUi(() =>
    {
        if (request?.DeviceId is not null)
        {
            _viewModel.SelectedInputDevice = _viewModel.InputDevices
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

        if (_viewModel.IsCapturing)
        {
            throw new ModuleApiException("Capture is already running.", 409);
        }

        if (_viewModel.SelectedInputDevice is null)
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
        "phase-zero/set" => ExecuteSetZero(),
        "phase-zero/clear" => ExecuteClearZero(),
        "session/reset" => ExecuteResetSession(),
        _ => throw new ModuleApiException($"Unknown action '{actionId}'.", 404)
    });

    private static ModuleCapabilities BuildCapabilities() =>
        ModuleCapabilities.Capture |
        ModuleCapabilities.Devices |
        ModuleCapabilities.Actions |
        ModuleCapabilities.PhaseZero |
        ModuleCapabilities.SessionReset;

    private ActionResultDto ExecuteSetZero()
    {
        if (!_viewModel.IsCapturing)
        {
            throw new ModuleApiException("Capture must be running to set phase zero.", 409);
        }

        _viewModel.SetZeroCommand.Execute(null);
        if (!_viewModel.PhaseZeroActive)
        {
            throw new ModuleApiException(_viewModel.StatusText, 409);
        }

        return new ActionResultDto
        {
            Status = "set",
            Message = _viewModel.StatusText
        };
    }

    private ActionResultDto ExecuteClearZero()
    {
        _viewModel.ClearZeroCommand.Execute(null);
        return new ActionResultDto
        {
            Status = "cleared",
            Message = _viewModel.StatusText
        };
    }

    private ActionResultDto ExecuteResetSession()
    {
        _viewModel.ResetPhaseCommand.Execute(null);
        return new ActionResultDto
        {
            Status = "reset",
            Message = "Phase session reset."
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

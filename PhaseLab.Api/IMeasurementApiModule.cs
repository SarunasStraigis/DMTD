namespace PhaseLab.Api;

public interface IMeasurementApiModule
{
    string Id { get; }
    string DisplayName { get; }

    ModuleInfoDto GetInfo();
    ModuleStatusDto GetStatus();
    ModuleSnapshotDto GetSnapshot();
    IReadOnlyList<AudioDeviceDto> ListDevices();
    void RefreshDevices();
    void StartCapture(CaptureRequestDto? request);
    void StopCapture();
    ActionResultDto ExecuteAction(string actionId);
}

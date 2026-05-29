using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Dmtd.Module.Services;

public sealed class AudioDeviceInfo
{
    public required string Id { get; init; }
    public required string Name { get; init; }

    public override string ToString() => Name;
}

public static class AudioDeviceEnumerator
{
    public static IReadOnlyList<AudioDeviceInfo> GetInputDevices()
    {
        var enumerator = new MMDeviceEnumerator();
        var devices = new List<AudioDeviceInfo>();
        foreach (var endpoint in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
        {
            devices.Add(new AudioDeviceInfo { Id = endpoint.ID, Name = endpoint.FriendlyName });
        }

        return devices;
    }

    public static IReadOnlyList<AudioDeviceInfo> GetOutputDevices()
    {
        var enumerator = new MMDeviceEnumerator();
        var devices = new List<AudioDeviceInfo>();
        foreach (var endpoint in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
        {
            devices.Add(new AudioDeviceInfo { Id = endpoint.ID, Name = endpoint.FriendlyName });
        }

        return devices;
    }
}

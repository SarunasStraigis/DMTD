using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace PhaseLab.UI;

public readonly record struct CaptureMixFormat(int SampleRateHz, int Channels);

public readonly record struct CaptureStartResult(int RequestedSampleRateHz, int ActualSampleRateHz)
{
    public bool HasMismatch => RequestedSampleRateHz != ActualSampleRateHz;
}

public static class WasapiCaptureFormat
{
    public static CaptureMixFormat GetCaptureMixFormat(string? deviceId)
    {
        using var device = ResolveDevice(deviceId);
        var mix = device.AudioClient.MixFormat;
        return new CaptureMixFormat(mix.SampleRate, Math.Max(1, mix.Channels));
    }

    public static bool IsSharedFormatSupported(string? deviceId, int sampleRate, int channels)
    {
        using var device = ResolveDevice(deviceId);
        var format = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels);
        return device.AudioClient.IsFormatSupported(AudioClientShareMode.Shared, format);
    }

    public static MMDevice ResolveDevice(string? deviceId)
    {
        var enumerator = new MMDeviceEnumerator();
        if (!string.IsNullOrWhiteSpace(deviceId))
        {
            try
            {
                return enumerator.GetDevice(deviceId);
            }
            catch
            {
                // Fall through to default.
            }
        }

        return enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia);
    }
}

public static class SampleRateMismatchText
{
    public static string BuildToolTip(int selectedHz, int deviceMixHz) =>
        $"Sample rate mismatch.\n\n" +
        $"The app is set to {selectedHz / 1000.0:F0} kHz but Windows reports this device at {deviceMixHz / 1000.0:F0} kHz.\n\n" +
        "Open Settings → System → Sound → your input device → Properties → Advanced " +
        "(or your interface control panel, e.g. Focusrite Control) and set the same sample rate.\n\n" +
        "Measurements may be wrong or noisy until they match.";

    public static string BuildCaptureStatus(bool isCapturing, CaptureStartResult? startResult)
    {
        if (!isCapturing || startResult is null)
        {
            return string.Empty;
        }

        var actual = startResult.Value.ActualSampleRateHz;
        if (!startResult.Value.HasMismatch)
        {
            return $"Capturing @ {actual / 1000.0:F0} kHz";
        }

        return $"Capturing @ {actual / 1000.0:F0} kHz (requested {startResult.Value.RequestedSampleRateHz / 1000.0:F0} kHz)";
    }
}

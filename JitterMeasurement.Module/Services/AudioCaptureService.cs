using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace JitterMeasurement.Module.Services;

public sealed class AudioDeviceInfo
{
    public required string Id { get; init; }
    public required string Name { get; init; }

    public override string ToString() => Name;
}

public sealed class AudioCaptureService : IDisposable
{
    private WasapiCapture? _capture;
    private int _sampleRate = 192_000;
    private int _inputChannelIndex;

    public event Action<float[]>? SamplesAvailable;
    public event Action<string>? ErrorOccurred;

    public bool IsCapturing => _capture is not null;

    public int SampleRate => _sampleRate;

    public int ChannelCount => _capture?.WaveFormat.Channels ?? 0;

    public static IReadOnlyList<AudioDeviceInfo> GetInputDevices()
    {
        var enumerator = new MMDeviceEnumerator();
        var devices = new List<AudioDeviceInfo>();

        foreach (var endpoint in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
        {
            devices.Add(new AudioDeviceInfo
            {
                Id = endpoint.ID,
                Name = endpoint.FriendlyName
            });
        }

        return devices;
    }

    public void Start(string? deviceId, int requestedSampleRate, int inputChannelIndex)
    {
        Stop();

        _inputChannelIndex = Math.Max(0, inputChannelIndex);
        var device = ResolveDevice(deviceId);
        var mixFormat = device.AudioClient.MixFormat;
        var channelCount = Math.Max(1, mixFormat.Channels);

        if (_inputChannelIndex >= channelCount)
        {
            _inputChannelIndex = 0;
            ErrorOccurred?.Invoke(
                $"Input channel {inputChannelIndex + 1} is not available. Using input 1.");
        }

        _capture = new WasapiCapture(device)
        {
            ShareMode = AudioClientShareMode.Shared
        };

        var desiredFormat = WaveFormat.CreateIeeeFloatWaveFormat(requestedSampleRate, channelCount);
        _capture.WaveFormat = desiredFormat;

        try
        {
            _capture.DataAvailable += OnDataAvailable;
            _capture.RecordingStopped += OnRecordingStopped;
            _capture.StartRecording();
        }
        catch (Exception ex)
        {
            _capture.DataAvailable -= OnDataAvailable;
            _capture.RecordingStopped -= OnRecordingStopped;
            _capture.Dispose();
            _capture = null;

            throw new InvalidOperationException(
                $"Could not open device at {requestedSampleRate} Hz. " +
                $"Set the sample rate in Focusrite Control / Windows Sound, then try again. ({ex.Message})",
                ex);
        }

        _sampleRate = _capture.WaveFormat.SampleRate;

        if (_sampleRate != requestedSampleRate)
        {
            ErrorOccurred?.Invoke(
                $"Capture opened at {_sampleRate} Hz instead of requested {requestedSampleRate} Hz.");
        }
    }

    public void Stop()
    {
        if (_capture is null)
        {
            return;
        }

        _capture.DataAvailable -= OnDataAvailable;
        _capture.RecordingStopped -= OnRecordingStopped;
        _capture.StopRecording();
        _capture.Dispose();
        _capture = null;
    }

    private MMDevice ResolveDevice(string? deviceId)
    {
        var enumerator = new MMDeviceEnumerator();
        if (!string.IsNullOrWhiteSpace(deviceId))
        {
            try
            {
                return enumerator.GetDevice(deviceId);
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke($"Could not open device: {ex.Message}");
            }
        }

        return enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia);
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (e.BytesRecorded == 0 || _capture is null)
        {
            return;
        }

        try
        {
            var floats = ConvertBuffer(e.Buffer, e.BytesRecorded, _capture.WaveFormat, _inputChannelIndex);
            if (floats.Length > 0)
            {
                SamplesAvailable?.Invoke(floats);
            }
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(ex.Message);
        }
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception is not null)
        {
            ErrorOccurred?.Invoke(e.Exception.Message);
        }
    }

    private static float[] ConvertBuffer(byte[] buffer, int bytesRecorded, WaveFormat format, int channelIndex)
    {
        if (format.Encoding == WaveFormatEncoding.IeeeFloat && format.BitsPerSample == 32)
        {
            var frameCount = bytesRecorded / 4 / format.Channels;
            var samples = new float[frameCount];
            var maxChannel = Math.Min(channelIndex, format.Channels - 1);

            for (var frame = 0; frame < frameCount; frame++)
            {
                var offset = frame * 4 * format.Channels + maxChannel * 4;
                samples[frame] = BitConverter.ToSingle(buffer, offset);
            }

            return samples;
        }

        if (format.Encoding == WaveFormatEncoding.Pcm && format.BitsPerSample == 24)
        {
            var frameCount = bytesRecorded / 3 / format.Channels;
            var samples = new float[frameCount];
            var maxChannel = Math.Min(channelIndex, format.Channels - 1);

            for (var frame = 0; frame < frameCount; frame++)
            {
                var offset = frame * 3 * format.Channels + maxChannel * 3;
                var raw = buffer[offset] | (buffer[offset + 1] << 8) | (buffer[offset + 2] << 16);
                if ((raw & 0x800000) != 0)
                {
                    raw |= unchecked((int)0xFF000000);
                }

                samples[frame] = raw / 8388608f;
            }

            return samples;
        }

        if (format.Encoding == WaveFormatEncoding.Pcm && format.BitsPerSample == 16)
        {
            var frameCount = bytesRecorded / 2 / format.Channels;
            var samples = new float[frameCount];
            var maxChannel = Math.Min(channelIndex, format.Channels - 1);

            for (var frame = 0; frame < frameCount; frame++)
            {
                var offset = frame * 2 * format.Channels + maxChannel * 2;
                var raw = (short)(buffer[offset] | (buffer[offset + 1] << 8));
                samples[frame] = raw / 32768f;
            }

            return samples;
        }

        throw new NotSupportedException(
            $"Unsupported capture format: {format.Encoding}, {format.BitsPerSample}-bit.");
    }

    public void Dispose()
    {
        Stop();
    }
}

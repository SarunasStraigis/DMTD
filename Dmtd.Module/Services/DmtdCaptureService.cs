using Dmtd.Core;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using System.Collections.Concurrent;

namespace Dmtd.Module.Services;

public sealed class DmtdCaptureService : IDisposable
{
    public const int SnapshotFrames = 4096;

    private readonly StereoRingBuffer _snapshot = new(SnapshotFrames);
    private readonly ConcurrentQueue<(float[] Left, float[] Right)> _blockQueue = new();
    private WasapiCapture? _capture;
    private CancellationTokenSource? _workerCts;
    private Task? _workerTask;
    private DmtdProcessor? _processor;
    private DmtdSettings? _settings;
    private PhaseHistoryStore? _history;

    private double _phaseZeroOffsetRad;
    private double? _prevPhaseDiffRadRaw;
    private int _slipCount;
    private int _lastSlipK;
    private double _lastSlipStepRad;
    private DateTimeOffset? _lastSlipTime;

    public event Action<LivePoint>? LivePointAvailable;
    public event Action<string>? ErrorOccurred;

    public bool IsCapturing => _capture is not null;
    public int SampleRate { get; private set; } = 192_000;

    public StereoRingBuffer Snapshot => _snapshot;

    public void SetHistoryStore(PhaseHistoryStore? history) => _history = history;

    public void Start(DmtdSettings settings, PhaseHistoryStore? history)
    {
        Stop();
        _settings = settings;
        _history = history;
        _processor = new DmtdProcessor(settings);
        _processor.Reset();
        _phaseZeroOffsetRad = settings.PhaseZeroOffsetRad;
        _prevPhaseDiffRadRaw = null;
        _slipCount = 0;

        var device = ResolveDevice(settings.DeviceId);
        _capture = new WasapiCapture(device) { ShareMode = AudioClientShareMode.Shared };
        var channelCount = Math.Max(2, device.AudioClient.MixFormat.Channels);
        _capture.WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(settings.SampleRate, channelCount);
        _capture.DataAvailable += OnDataAvailable;
        _capture.RecordingStopped += OnRecordingStopped;

        try
        {
            _capture.StartRecording();
        }
        catch (Exception ex)
        {
            _capture.Dispose();
            _capture = null;
            throw new InvalidOperationException($"Could not open capture device: {ex.Message}", ex);
        }

        SampleRate = _capture.WaveFormat.SampleRate;
        _workerCts = new CancellationTokenSource();
        _workerTask = Task.Run(() => WorkerLoop(_workerCts.Token));
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

        _workerCts?.Cancel();
        try
        {
            _workerTask?.Wait(TimeSpan.FromSeconds(2));
        }
        catch
        {
            // Ignore shutdown race.
        }

        _workerCts?.Dispose();
        _workerCts = null;
        _workerTask = null;
        _processor = null;
        while (_blockQueue.TryDequeue(out _))
        {
        }
    }

    public void SetPhaseZeroOffset(double offsetRad, double offsetPs)
    {
        _phaseZeroOffsetRad = offsetRad;
        if (_settings is not null)
        {
            _settings.PhaseZeroOffsetRad = offsetRad;
            _settings.PhaseZeroOffsetPs = offsetPs;
        }
    }

    public (double Rad, double Ps)? GetLatestRawPhase()
    {
        return _prevPhaseDiffRadRaw is null || _settings is null
            ? null
            : (_prevPhaseDiffRadRaw.Value, _prevPhaseDiffRadRaw.Value * PhaseMath.PsPerRad(_settings.RefFrequency));
    }

    private void WorkerLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            if (!_blockQueue.TryDequeue(out var block))
            {
                Thread.Sleep(1);
                continue;
            }

            try
            {
                ProcessBlock(block.Left, block.Right);
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(ex.Message);
            }
        }
    }

    private void ProcessBlock(float[] left, float[] right)
    {
        if (_processor is null || _settings is null)
        {
            return;
        }

        var result = _processor.ProcessBlock(left, right);
        var combinedRad = result.PhaseDiffRad + _phaseZeroOffsetRad;
        var phaseRad = PhaseMath.WrapPrincipal(combinedRad);
        var phasePs = phaseRad * PhaseMath.PsPerRad(_settings.RefFrequency);
        var ts = DateTimeOffset.UtcNow;

        var slipK = 0;
        var slipStepRad = 0.0;
        if (_prevPhaseDiffRadRaw is not null)
        {
            var step = result.PhaseDiffRad - _prevPhaseDiffRadRaw.Value;
            var k = (int)Math.Round(step / (2.0 * Math.PI));
            if (k != 0)
            {
                var residual = step - k * 2.0 * Math.PI;
                if (Math.Abs(residual) < 0.35)
                {
                    slipK = k;
                    slipStepRad = step;
                    _slipCount++;
                    _lastSlipK = k;
                    _lastSlipStepRad = step;
                    _lastSlipTime = ts;
                }
            }
        }

        _prevPhaseDiffRadRaw = result.PhaseDiffRad;
        if (_settings.EnableHistoryLogging)
        {
            _history?.Enqueue(ts.ToString("O"), phaseRad, phasePs, result.BeatFreq);
        }

        LivePointAvailable?.Invoke(new LivePoint
        {
            Timestamp = ts,
            PhaseDiffRad = phaseRad,
            PhaseDiffPs = phasePs,
            BeatFreq = result.BeatFreq,
            PhaseAPs = result.PhaseAPs,
            PhaseBPs = result.PhaseBPs,
            PhaseADeg = result.PhaseADeg,
            PhaseBDeg = result.PhaseBDeg,
            RmsA = result.RmsA,
            RmsB = result.RmsB,
            SlipK = slipK,
            SlipCount = _slipCount,
            SlipStepRad = slipStepRad,
            LastSlipTime = _lastSlipTime
        });
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (_capture is null || e.BytesRecorded == 0)
        {
            return;
        }

        try
        {
            var (left, right) = Deinterleave(e.Buffer, e.BytesRecorded, _capture.WaveFormat);
            if (left.Length == 0)
            {
                return;
            }

            _snapshot.Write(left, right);
            _blockQueue.Enqueue((left, right));
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(ex.Message);
        }
    }

    private static (float[] Left, float[] Right) Deinterleave(byte[] buffer, int bytesRecorded, WaveFormat format)
    {
        if (format.Encoding != WaveFormatEncoding.IeeeFloat || format.BitsPerSample != 32)
        {
            throw new NotSupportedException("Only IEEE float capture is supported.");
        }

        var frameCount = bytesRecorded / 4 / format.Channels;
        var left = new float[frameCount];
        var right = new float[frameCount];
        for (var frame = 0; frame < frameCount; frame++)
        {
            left[frame] = BitConverter.ToSingle(buffer, frame * 4 * format.Channels);
            right[frame] = format.Channels > 1
                ? BitConverter.ToSingle(buffer, frame * 4 * format.Channels + 4)
                : left[frame];
        }

        return (left, right);
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception is not null)
        {
            ErrorOccurred?.Invoke(e.Exception.Message);
        }
    }

    private static MMDevice ResolveDevice(string? deviceId)
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

    public void Dispose() => Stop();
}

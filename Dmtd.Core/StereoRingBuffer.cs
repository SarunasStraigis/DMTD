namespace Dmtd.Core;

public sealed class StereoRingBuffer
{
    private readonly float[] _buffer;
    private readonly int _frameCount;
    private readonly object _lock = new();
    private int _writeIndex;

    public StereoRingBuffer(int frameCount)
    {
        _frameCount = Math.Max(1, frameCount);
        _buffer = new float[_frameCount * 2];
    }

    public int FrameCount => _frameCount;

    public void Write(ReadOnlySpan<float> left, ReadOnlySpan<float> right)
    {
        var n = Math.Min(left.Length, right.Length);
        if (n <= 0)
        {
            return;
        }

        lock (_lock)
        {
            for (var i = 0; i < n; i++)
            {
                var idx = _writeIndex * 2;
                _buffer[idx] = left[i];
                _buffer[idx + 1] = right[i];
                _writeIndex = (_writeIndex + 1) % _frameCount;
            }
        }
    }

    public void WriteInterleaved(ReadOnlySpan<float> interleavedStereo)
    {
        var frames = interleavedStereo.Length / 2;
        if (frames <= 0)
        {
            return;
        }

        lock (_lock)
        {
            for (var i = 0; i < frames; i++)
            {
                var src = i * 2;
                var idx = _writeIndex * 2;
                _buffer[idx] = interleavedStereo[src];
                _buffer[idx + 1] = interleavedStereo[src + 1];
                _writeIndex = (_writeIndex + 1) % _frameCount;
            }
        }
    }

    public (float[] Left, float[] Right) CopyLatest(int frameCount)
    {
        frameCount = Math.Min(frameCount, _frameCount);
        var left = new float[frameCount];
        var right = new float[frameCount];

        lock (_lock)
        {
            for (var i = 0; i < frameCount; i++)
            {
                var ringIndex = (_writeIndex - frameCount + i + _frameCount) % _frameCount;
                var idx = ringIndex * 2;
                left[i] = _buffer[idx];
                right[i] = _buffer[idx + 1];
            }
        }

        return (left, right);
    }

    public void Clear()
    {
        lock (_lock)
        {
            Array.Clear(_buffer);
            _writeIndex = 0;
        }
    }
}

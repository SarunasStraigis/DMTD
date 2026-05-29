namespace JitterMeasurement.Core;

public sealed class AudioRingBuffer
{
    private readonly float[] _buffer;
    private readonly object _lock = new();
    private int _writeIndex;
    private int _count;

    public AudioRingBuffer(int capacity)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }

        _buffer = new float[capacity];
        Capacity = capacity;
    }

    public int Capacity { get; }

    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _count;
            }
        }
    }

    public void Write(ReadOnlySpan<float> samples)
    {
        lock (_lock)
        {
            foreach (var sample in samples)
            {
                _buffer[_writeIndex] = sample;
                _writeIndex = (_writeIndex + 1) % Capacity;
                if (_count < Capacity)
                {
                    _count++;
                }
            }
        }
    }

    public float[] CopyLatest(int sampleCount)
    {
        if (sampleCount <= 0)
        {
            return Array.Empty<float>();
        }

        lock (_lock)
        {
            var count = Math.Min(sampleCount, _count);
            var result = new float[count];
            var start = (_writeIndex - count + Capacity) % Capacity;

            if (start + count <= Capacity)
            {
                Array.Copy(_buffer, start, result, 0, count);
            }
            else
            {
                var firstPart = Capacity - start;
                Array.Copy(_buffer, start, result, 0, firstPart);
                Array.Copy(_buffer, 0, result, firstPart, count - firstPart);
            }

            return result;
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _writeIndex = 0;
            _count = 0;
            Array.Clear(_buffer);
        }
    }
}

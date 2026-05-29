using MathNet.Numerics.IntegralTransforms;
using System.Numerics;

namespace JitterMeasurement.Core;

public static class SignalProcessor
{
    private const double ClipThreshold = 0.99;

    public static bool DetectClipping(ReadOnlySpan<float> samples)
    {
        foreach (var sample in samples)
        {
            if (Math.Abs(sample) >= ClipThreshold)
            {
                return true;
            }
        }

        return false;
    }

    public static double ComputeVpp(ReadOnlySpan<float> samples)
    {
        if (samples.IsEmpty)
        {
            return 0;
        }

        var min = double.PositiveInfinity;
        var max = double.NegativeInfinity;

        foreach (var sample in samples)
        {
            min = Math.Min(min, sample);
            max = Math.Max(max, sample);
        }

        return max - min;
    }

    public static double ComputeRms(ReadOnlySpan<float> samples)
    {
        if (samples.IsEmpty)
        {
            return 0;
        }

        double sumSquares = 0;
        foreach (var sample in samples)
        {
            sumSquares += sample * sample;
        }

        return Math.Sqrt(sumSquares / samples.Length);
    }

    public static double ComputeMean(ReadOnlySpan<float> samples)
    {
        if (samples.IsEmpty)
        {
            return 0;
        }

        double sum = 0;
        foreach (var sample in samples)
        {
            sum += sample;
        }

        return sum / samples.Length;
    }

    public static float[] RemoveDc(float[] samples)
    {
        var mean = ComputeMean(samples);
        var result = new float[samples.Length];
        for (var i = 0; i < samples.Length; i++)
        {
            result[i] = (float)(samples[i] - mean);
        }

        return result;
    }

    public static (double[] frequenciesHz, double[] magnitudeDb) ComputeMagnitudeSpectrum(
        float[] samples,
        double sampleRate,
        bool removeDc = true)
    {
        if (samples.Length < 16)
        {
            return (Array.Empty<double>(), Array.Empty<double>());
        }

        var working = removeDc ? RemoveDc(samples) : samples;
        var fftSize = NextPowerOfTwo(working.Length);
        var windowed = new Complex[fftSize];

        for (var i = 0; i < working.Length; i++)
        {
            var window = 0.5 * (1 - Math.Cos(2 * Math.PI * i / (working.Length - 1)));
            windowed[i] = new Complex(working[i] * window, 0);
        }

        Fourier.Forward(windowed, FourierOptions.Matlab);

        var half = fftSize / 2;
        var frequencies = new double[half];
        var magnitudesDb = new double[half];
        var scale = 2.0 / working.Length;

        for (var i = 0; i < half; i++)
        {
            frequencies[i] = i * sampleRate / fftSize;
            var magnitude = windowed[i].Magnitude * scale;
            magnitudesDb[i] = magnitude > 1e-12
                ? 20 * Math.Log10(magnitude)
                : -200;
        }

        return (frequencies, magnitudesDb);
    }

    public static double FindPeakFrequencyHz(double[] frequenciesHz, double[] magnitudeDb, double minHz = 1)
    {
        if (frequenciesHz.Length == 0)
        {
            return 0;
        }

        var peakIndex = -1;
        var peakValue = double.NegativeInfinity;

        for (var i = 1; i < frequenciesHz.Length; i++)
        {
            if (frequenciesHz[i] < minHz)
            {
                continue;
            }

            if (magnitudeDb[i] > peakValue)
            {
                peakValue = magnitudeDb[i];
                peakIndex = i;
            }
        }

        return peakIndex >= 0 ? frequenciesHz[peakIndex] : 0;
    }

    public static double[] ComputeWelchPsd(
        float[] samples,
        double sampleRate,
        int segmentLength,
        int overlap)
    {
        if (samples.Length < segmentLength || segmentLength < 16)
        {
            return Array.Empty<double>();
        }

        var step = Math.Max(1, segmentLength - overlap);
        var fftSize = NextPowerOfTwo(segmentLength);
        var half = fftSize / 2;
        var psd = new double[half];
        var segmentCount = 0;

        for (var start = 0; start + segmentLength <= samples.Length; start += step)
        {
            var segment = new float[segmentLength];
            Array.Copy(samples, start, segment, 0, segmentLength);
            var ac = RemoveDc(segment);

            var windowed = new Complex[fftSize];
            for (var i = 0; i < ac.Length; i++)
            {
                var window = 0.5 * (1 - Math.Cos(2 * Math.PI * i / (ac.Length - 1)));
                windowed[i] = new Complex(ac[i] * window, 0);
            }

            Fourier.Forward(windowed, FourierOptions.Matlab);

            for (var i = 0; i < half; i++)
            {
                var magnitude = windowed[i].Magnitude / segmentLength;
                psd[i] += magnitude * magnitude;
            }

            segmentCount++;
        }

        if (segmentCount == 0)
        {
            return Array.Empty<double>();
        }

        var normalization = 2.0 / (sampleRate * segmentCount);
        for (var i = 0; i < psd.Length; i++)
        {
            psd[i] *= normalization;
        }

        return psd;
    }

    public static double IntegrateBand(
        double[] psd,
        double sampleRate,
        int fftSize,
        double lowHz,
        double highHz)
    {
        if (psd.Length == 0)
        {
            return 0;
        }

        var binWidth = sampleRate / fftSize;
        double sum = 0;

        for (var i = 0; i < psd.Length; i++)
        {
            var frequency = i * binWidth;
            if (frequency >= lowHz && frequency <= highHz)
            {
                sum += psd[i] * binWidth;
            }
        }

        return Math.Sqrt(Math.Max(0, sum));
    }

    public static double[] BuildTimeAxis(int sampleCount, double sampleRate)
    {
        var axis = new double[sampleCount];
        for (var i = 0; i < sampleCount; i++)
        {
            axis[i] = i / sampleRate;
        }

        return axis;
    }

    public static float[] DecimateForDisplay(float[] samples, int maxPoints)
    {
        if (samples.Length <= maxPoints)
        {
            return samples;
        }

        var step = (double)samples.Length / maxPoints;
        var result = new float[maxPoints];
        for (var i = 0; i < maxPoints; i++)
        {
            var index = (int)(i * step);
            if (index >= samples.Length)
            {
                index = samples.Length - 1;
            }

            result[i] = samples[index];
        }

        return result;
    }

    private static int NextPowerOfTwo(int value)
    {
        var power = 1;
        while (power < value)
        {
            power <<= 1;
        }

        return power;
    }
}

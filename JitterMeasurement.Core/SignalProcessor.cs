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
        var windowSumSq = HannWindowSumSquares(segmentLength);

        for (var start = 0; start + segmentLength <= samples.Length; start += step)
        {
            var segment = new float[segmentLength];
            Array.Copy(samples, start, segment, 0, segmentLength);
            var ac = RemoveDc(segment);

            var windowed = new Complex[fftSize];
            for (var i = 0; i < ac.Length; i++)
            {
                var window = HannWindow(i, ac.Length);
                windowed[i] = new Complex(ac[i] * window, 0);
            }

            Fourier.Forward(windowed, FourierOptions.Matlab);

            for (var i = 0; i < half; i++)
            {
                var magnitude = windowed[i].Magnitude;
                psd[i] += magnitude * magnitude;
            }

            segmentCount++;
        }

        if (segmentCount == 0)
        {
            return Array.Empty<double>();
        }

        // One-sided PSD density (V²/Hz), aligned with scipy.signal.welch(..., scaling='density').
        var scale = 1.0 / (sampleRate * windowSumSq * segmentCount);
        for (var i = 0; i < psd.Length; i++)
        {
            var oneSided = i == 0 || i == psd.Length - 1 ? 1.0 : 2.0;
            psd[i] *= scale * oneSided;
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

    public static (double[] FrequenciesHz, double[] JitterFs) ComputeCumulativeIntegratedJitter(
        double[] psd,
        double sampleRate,
        int fftSize,
        double lowHz,
        double highHz,
        double kpdVPerRad,
        double harmonicHz)
    {
        if (psd.Length == 0 || kpdVPerRad <= 0 || harmonicHz <= 0)
        {
            return (Array.Empty<double>(), Array.Empty<double>());
        }

        var binWidth = sampleRate / fftSize;
        var frequencies = new double[psd.Length];
        var jitterFs = new double[psd.Length];

        for (var i = 0; i < psd.Length; i++)
        {
            frequencies[i] = i * binWidth;
        }

        var lowIndex = 0;
        while (lowIndex < psd.Length && frequencies[lowIndex] < lowHz)
        {
            lowIndex++;
        }

        var highIndex = psd.Length - 1;
        while (highIndex >= lowIndex && frequencies[highIndex] > highHz)
        {
            highIndex--;
        }

        if (highIndex < lowIndex)
        {
            return (Array.Empty<double>(), Array.Empty<double>());
        }

        double sum = 0;
        for (var i = highIndex; i >= lowIndex; i--)
        {
            sum += psd[i] * binWidth;
            jitterFs[i] = VoltageJitterToFemtoseconds(Math.Sqrt(Math.Max(0, sum)), kpdVPerRad, harmonicHz);
        }

        return (frequencies, jitterFs);
    }

    public static double VoltageJitterToFemtoseconds(double integratedVolts, double kpdVPerRad, double harmonicHz)
    {
        var integratedPhiRad = integratedVolts / kpdVPerRad;
        var integratedTSec = integratedPhiRad / (2 * Math.PI * harmonicHz);
        return integratedTSec * 1e15;
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

    public static (double[] TimeSeconds, float[] Values) DecimateForDisplayWithTime(
        float[] samples,
        int maxPoints,
        double sampleRate)
    {
        if (samples.Length == 0)
        {
            return (Array.Empty<double>(), Array.Empty<float>());
        }

        if (sampleRate <= 0)
        {
            sampleRate = 1;
        }

        if (samples.Length <= maxPoints)
        {
            return (BuildTimeAxis(samples.Length, sampleRate), samples);
        }

        var time = new double[maxPoints];
        var values = new float[maxPoints];
        var lastIndex = samples.Length - 1;

        for (var i = 0; i < maxPoints; i++)
        {
            var index = maxPoints == 1
                ? 0
                : (int)Math.Round(i * lastIndex / (double)(maxPoints - 1));
            index = Math.Clamp(index, 0, lastIndex);
            time[i] = index / sampleRate;
            values[i] = samples[index];
        }

        return (time, values);
    }

    private static double HannWindow(int index, int length)
    {
        if (length < 2)
        {
            return 1.0;
        }

        return 0.5 * (1 - Math.Cos(2 * Math.PI * index / (length - 1)));
    }

    private static double HannWindowSumSquares(int length)
    {
        if (length < 2)
        {
            return 1.0;
        }

        double sum = 0;
        for (var i = 0; i < length; i++)
        {
            var w = HannWindow(i, length);
            sum += w * w;
        }

        return sum;
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

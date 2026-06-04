using JitterMeasurement.Core;
using Xunit;

namespace JitterMeasurement.Core.Tests;

public sealed class SignalProcessorWelchTests
{
    private const double SampleRate = 192_000;
    private const int SegmentLength = 4096;
    private const int Overlap = SegmentLength / 2;

    private static int NextPowerOfTwo(int value)
    {
        var power = 1;
        while (power < value)
        {
            power <<= 1;
        }

        return power;
    }

    private static (double TimeRms, double IntegratedRms, int FftSize) AnalyzeAcBand(
        float[] acSamples,
        double lowHz,
        double highHz)
    {
        var fftSize = NextPowerOfTwo(SegmentLength);
        var timeRms = SignalProcessor.ComputeRms(acSamples);
        var psd = SignalProcessor.ComputeWelchPsd(acSamples, SampleRate, SegmentLength, Overlap);
        var integratedRms = SignalProcessor.IntegrateBand(psd, SampleRate, fftSize, lowHz, highHz);
        return (timeRms, integratedRms, fftSize);
    }

    [Fact]
    public void WhiteNoise_Parseval_IntegratedMatchesTimeRmsWithinTolerance()
    {
        const int sampleCount = (int)(SampleRate * 0.25);
        var rng = new Random(42);
        var samples = new float[sampleCount];
        for (var i = 0; i < sampleCount; i++)
        {
            samples[i] = (float)NextGaussian(rng);
        }

        var ac = SignalProcessor.RemoveDc(samples);
        var nyquist = SampleRate / 2.0;
        var (timeRms, integratedRms, _) = AnalyzeAcBand(ac, 0, nyquist);

        Assert.True(timeRms > 0.9 && timeRms < 1.1, $"time RMS expected ~1, got {timeRms}");
        var ratio = integratedRms / timeRms;
        Assert.InRange(ratio, 0.75, 1.35);
    }

    [Fact]
    public void SingleTone_IntegratedCapturesTonePower()
    {
        const double toneHz = 5_000;
        const double amplitude = 0.5;
        const int sampleCount = (int)(SampleRate * 0.25);
        var samples = new float[sampleCount];
        for (var i = 0; i < sampleCount; i++)
        {
            var t = i / SampleRate;
            samples[i] = (float)(amplitude * Math.Sin(2 * Math.PI * toneHz * t));
        }

        var ac = SignalProcessor.RemoveDc(samples);
        var expectedRms = amplitude / Math.Sqrt(2);
        var (timeRms, integratedRms, _) = AnalyzeAcBand(ac, toneHz * 0.5, toneHz * 1.5);

        Assert.InRange(timeRms, expectedRms * 0.98, expectedRms * 1.02);
        var ratio = integratedRms / timeRms;
        Assert.InRange(ratio, 0.85, 1.15);
    }

    private static double NextGaussian(Random rng)
    {
        var u1 = 1.0 - rng.NextDouble();
        var u2 = 1.0 - rng.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
    }
}

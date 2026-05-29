using JitterMeasurement.Core.Models;

namespace JitterMeasurement.Core;

public static class JitterAnalyzer
{
    public static JitterResult Analyze(
        float[] samples,
        double sampleRate,
        PhaseDetectorCal calibration,
        double fundamentalHz,
        int harmonicNumber,
        double integrationLowHz,
        double integrationHighHz)
    {
        if (calibration.KpdVPerRad <= 0)
        {
            return JitterResult.Invalid("Calibrate the phase detector before measuring jitter.");
        }

        if (samples.Length < 64)
        {
            return JitterResult.Invalid("Not enough samples for jitter analysis.");
        }

        var isClipping = SignalProcessor.DetectClipping(samples);
        var acSamples = SignalProcessor.RemoveDc(samples);
        var sigmaV = SignalProcessor.ComputeRms(acSamples);
        var sigmaPhiRad = sigmaV / calibration.KpdVPerRad;
        var harmonicHz = fundamentalHz * harmonicNumber;

        if (harmonicHz <= 0)
        {
            return JitterResult.Invalid("Harmonic frequency must be positive.");
        }

        var sigmaTSec = sigmaPhiRad / (2 * Math.PI * harmonicHz);
        var sigmaTDeg = sigmaPhiRad * (180.0 / Math.PI);
        var sigmaTFs = sigmaTSec * 1e15;

        var segmentLength = Math.Min(4096, NextPowerOfTwo(Math.Max(256, samples.Length / 4)));
        var overlap = segmentLength / 2;
        var fftSize = NextPowerOfTwo(segmentLength);

        var psd = SignalProcessor.ComputeWelchPsd(acSamples, sampleRate, segmentLength, overlap);
        var integratedV = SignalProcessor.IntegrateBand(
            psd,
            sampleRate,
            fftSize,
            integrationLowHz,
            integrationHighHz);

        var integratedPhiRad = integratedV / calibration.KpdVPerRad;
        var integratedTSec = integratedPhiRad / (2 * Math.PI * harmonicHz);
        var integratedTFs = integratedTSec * 1e15;

        var message = isClipping ? "Warning: input is clipping during measurement." : null;

        return new JitterResult
        {
            SigmaVRms = sigmaV,
            SigmaPhiRad = sigmaPhiRad,
            SigmaTDeg = sigmaTDeg,
            SigmaTFs = sigmaTFs,
            IntegratedPhiRad = integratedPhiRad,
            IntegratedTFs = integratedTFs,
            HarmonicFrequencyHz = harmonicHz,
            IsClipping = isClipping,
            IsValid = true,
            Message = message
        };
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

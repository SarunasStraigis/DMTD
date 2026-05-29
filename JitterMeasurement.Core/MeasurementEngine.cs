using JitterMeasurement.Core.Models;

namespace JitterMeasurement.Core;

public static class MeasurementEngine
{
    private const int MaxDisplayPoints = 4000;

    public static AnalysisSnapshot AnalyzeWindow(
        float[] samples,
        AppSettings settings,
        PhaseDetectorCal? existingCalibration)
    {
        var sampleRate = settings.SampleRate;
        var displaySamples = SignalProcessor.DecimateForDisplay(samples, MaxDisplayPoints);
        var timeAxis = SignalProcessor.BuildTimeAxis(displaySamples.Length, sampleRate);
        var displayVolts = displaySamples.Select(s => (double)s).ToArray();

        var (fftFrequencies, fftMagnitudesDb) = SignalProcessor.ComputeMagnitudeSpectrum(samples, sampleRate);

        JitterResult? jitter = null;
        if (existingCalibration is { KpdVPerRad: > 0 })
        {
            jitter = JitterAnalyzer.Analyze(
                samples,
                sampleRate,
                existingCalibration,
                settings.FundamentalHz,
                settings.HarmonicNumber,
                settings.IntegrationBandLowHz,
                settings.IntegrationBandHighHz);
        }

        return new AnalysisSnapshot
        {
            TimeSeconds = timeAxis,
            TimeVolts = displayVolts,
            FftFrequenciesHz = fftFrequencies,
            FftMagnitudeDb = fftMagnitudesDb,
            Calibration = existingCalibration,
            Jitter = jitter,
            SampleRate = sampleRate,
            IntegrationBandLowHz = settings.IntegrationBandLowHz,
            IntegrationBandHighHz = settings.IntegrationBandHighHz,
            FftViewMaxHz = FftViewRange.ComputeMaxHz(
                settings.IntegrationBandHighHz,
                sampleRate,
                settings.FftViewMaxHz)
        };
    }
}

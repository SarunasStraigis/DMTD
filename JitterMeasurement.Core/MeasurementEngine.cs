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
        var (timeAxis, displaySamples) = SignalProcessor.DecimateForDisplayWithTime(
            samples,
            MaxDisplayPoints,
            sampleRate);
        var displayVolts = displaySamples.Select(s => (double)s).ToArray();

        var (fftFrequencies, fftMagnitudesDb) = SignalProcessor.ComputeMagnitudeSpectrum(samples, sampleRate);

        var (integrationLowHz, integrationHighHz) = IntegrationBand.Clamp(
            settings.IntegrationBandLowHz,
            settings.IntegrationBandHighHz,
            sampleRate);

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
            CumulativeJitterFreqHz = jitter?.CumulativeJitterFreqHz ?? Array.Empty<double>(),
            CumulativeJitterFs = jitter?.CumulativeJitterFs ?? Array.Empty<double>(),
            Calibration = existingCalibration,
            Jitter = jitter,
            SampleRate = sampleRate,
            IntegrationBandLowHz = integrationLowHz,
            IntegrationBandHighHz = integrationHighHz,
            FftViewMaxHz = FftViewRange.ComputeMaxHz(
                integrationHighHz,
                sampleRate,
                settings.FftViewMaxHz)
        };
    }
}

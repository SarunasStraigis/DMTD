namespace JitterMeasurement.Core.Models;

public sealed class AnalysisSnapshot
{
    public required double[] TimeSeconds { get; init; }
    public required double[] TimeVolts { get; init; }
    public required double[] FftFrequenciesHz { get; init; }
    public required double[] FftMagnitudeDb { get; init; }
    public PhaseDetectorCal? Calibration { get; init; }
    public JitterResult? Jitter { get; init; }
    public double SampleRate { get; init; }
    public double IntegrationBandLowHz { get; init; }
    public double IntegrationBandHighHz { get; init; }
    public double FftViewMaxHz { get; init; }
}

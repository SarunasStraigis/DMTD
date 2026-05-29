namespace JitterMeasurement.Core;

public static class FftViewRange
{
    /// <summary>
    /// Default lower bound when auto-scaling FFT X axis (typical pro-audio / soundcard rolloff).
    /// </summary>
    public const double AutoFloorHz = 25_000;

    /// <summary>
    /// Multiplier applied to integration high when auto-scaling FFT X axis.
    /// </summary>
    public const double IntegrationMultiplier = 2.5;

    public static double ComputeMaxHz(
        double integrationHighHz,
        double sampleRate,
        double userMaxHz)
    {
        var nyquist = sampleRate / 2.0;

        if (userMaxHz > 0)
        {
            return Math.Min(nyquist, userMaxHz);
        }

        var autoMax = Math.Max(integrationHighHz * IntegrationMultiplier, AutoFloorHz);
        return Math.Min(nyquist, autoMax);
    }
}

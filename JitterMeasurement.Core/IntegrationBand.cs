namespace JitterMeasurement.Core;

public static class IntegrationBand
{
    /// <summary>
    /// Clamps integration limits to [0, Nyquist]. High is never below low.
    /// Values above sampleRate/2 (e.g. 100 kHz at 192 kHz) integrate only to Nyquist.
    /// </summary>
    public static (double LowHz, double HighHz) Clamp(double lowHz, double highHz, double sampleRate)
    {
        if (sampleRate <= 0)
        {
            return (Math.Max(0, lowHz), Math.Max(0, highHz));
        }

        var nyquist = sampleRate / 2.0;
        var low = Math.Max(0, lowHz);
        var high = Math.Min(nyquist, Math.Max(low, highHz));
        return (low, high);
    }
}

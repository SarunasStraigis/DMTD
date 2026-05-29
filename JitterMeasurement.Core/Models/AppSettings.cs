namespace JitterMeasurement.Core.Models;

public sealed class AppSettings
{
    public double FundamentalHz { get; set; } = 65_000_000;
    public int HarmonicNumber { get; set; } = 1;
    public int SampleRate { get; set; } = 192_000;
    public double IntegrationBandLowHz { get; set; } = 10;
    public double IntegrationBandHighHz { get; set; } = 10_000;
    public double TimeWindowMs { get; set; } = 100;
    /// <summary>FFT X-axis max in Hz. 0 = auto from integration band.</summary>
    public double FftViewMaxHz { get; set; }
    public string? DeviceId { get; set; }
    public int InputChannel { get; set; } = 1;
}

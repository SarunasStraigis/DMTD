namespace Dmtd.Core;

public sealed class DmtdSettings
{
    public string? DeviceId { get; set; }
    public int SampleRate { get; set; } = 192_000;
    public double BlockDurationMs { get; set; } = 1000.0;
    public double BeatFrequency { get; set; } = 1000.0;
    public FreqEstimator FreqEstimator { get; set; } = FreqEstimator.FftPeak;
    public DemodMode DemodMode { get; set; } = DemodMode.BlockIq;
    public FreqSource FreqSource { get; set; } = FreqSource.ChA;
    public double IqLpfCutoffHz { get; set; } = 120.0;
    public int IqLpfOrder { get; set; } = 4;
    public double IqMinMag { get; set; } = 1e-4;
    public IqWindow IqWindow { get; set; } = IqWindow.Hann;
    public double PllKp { get; set; } = 0.3;
    public double PllKi { get; set; } = 0.03;
    public double PllMinMag { get; set; } = 1e-4;
    public double RefFrequency { get; set; } = 90_000_000;
    public int HistoryRetentionDays { get; set; } = 30;
    public bool EnableHistoryLogging { get; set; } = true;
    public double PhaseZeroOffsetRad { get; set; }
    public double PhaseZeroOffsetPs { get; set; }
    public DspUnwrapState? SavedUnwrapState { get; set; }

    public double ExpansionFactor =>
        RefFrequency / BeatFrequency;

    public int ResolveBlockSize(int sampleRate)
    {
        if (sampleRate <= 0)
        {
            sampleRate = SampleRate;
        }

        return Math.Clamp(
            (int)Math.Round(sampleRate * BlockDurationMs / 1000.0),
            1024,
            1_920_000);
    }
}

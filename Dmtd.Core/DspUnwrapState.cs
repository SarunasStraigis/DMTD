namespace Dmtd.Core;

public sealed class DspUnwrapState
{
    public int SampleRate { get; set; }
    public FreqEstimator FreqEstimator { get; set; }

    public double? PrevRawA { get; set; }
    public double? PrevRawB { get; set; }
    public double UnwrapOffsetA { get; set; }
    public double UnwrapOffsetB { get; set; }
    public double? LastEstimatedFreq { get; set; }

    public bool Matches(DmtdSettings settings) =>
        SampleRate == settings.SampleRate &&
        FreqEstimator == settings.FreqEstimator;
}

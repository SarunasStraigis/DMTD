namespace Dmtd.Core;

public sealed class DspUnwrapState
{
    public int SampleRate { get; set; }
    public DemodMode DemodMode { get; set; }
    public FreqEstimator FreqEstimator { get; set; }

    public double? PrevRawA { get; set; }
    public double? PrevRawB { get; set; }
    public double UnwrapOffsetA { get; set; }
    public double UnwrapOffsetB { get; set; }
    public double? LastEstimatedFreq { get; set; }
    public double? PllPhaseA { get; set; }
    public double? PllPhaseB { get; set; }
    public double? PllFreqHz { get; set; }

    public bool Matches(DmtdSettings settings) =>
        SampleRate == settings.SampleRate &&
        DemodMode == settings.DemodMode &&
        FreqEstimator == settings.FreqEstimator;
}

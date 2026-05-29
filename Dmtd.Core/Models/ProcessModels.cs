namespace Dmtd.Core;

public sealed class BlockProcessResult
{
    public required double PhaseDiffRad { get; init; }
    public required double PhaseDiffPs { get; init; }
    public required double BeatFreq { get; init; }
    public required double PhaseAPs { get; init; }
    public required double PhaseBPs { get; init; }
    public required double PhaseADeg { get; init; }
    public required double PhaseBDeg { get; init; }
    public required double RmsA { get; init; }
    public required double RmsB { get; init; }
}

public sealed class LivePoint
{
    public required DateTimeOffset Timestamp { get; init; }
    public required double PhaseDiffRad { get; init; }
    public required double PhaseDiffPs { get; init; }
    public required double BeatFreq { get; init; }
    public required double PhaseAPs { get; init; }
    public required double PhaseBPs { get; init; }
    public required double PhaseADeg { get; init; }
    public required double PhaseBDeg { get; init; }
    public required double RmsA { get; init; }
    public required double RmsB { get; init; }
    public int SlipK { get; init; }
    public int SlipCount { get; init; }
    public double SlipStepRad { get; init; }
    public DateTimeOffset? LastSlipTime { get; init; }
}

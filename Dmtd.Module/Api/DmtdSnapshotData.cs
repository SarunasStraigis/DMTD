namespace Dmtd.Module.Api;

public sealed class DmtdSnapshotData
{
    public double? PhaseDiffPs { get; init; }
    public double? PhaseDiffRad { get; init; }
    public double? BeatFreqHz { get; init; }
    public double? MovingAveragePs { get; init; }
    public double? StdDevPs { get; init; }
    public int MaWindow { get; init; }
    public bool PhaseZeroActive { get; init; }
    public double PhaseZeroOffsetPs { get; init; }
    public double? RmsA { get; init; }
    public double? RmsB { get; init; }
    public int SlipCount { get; init; }
    public DateTimeOffset? LatestTimestamp { get; init; }
}

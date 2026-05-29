namespace JitterMeasurement.Module.Api;

public sealed class JitterSnapshotData
{
    public double? JitterRmsFs { get; init; }
    public double? IntegratedFs { get; init; }
    public double? SigmaVRms { get; init; }
    public double? SigmaPhiRad { get; init; }
    public double? HarmonicFrequencyHz { get; init; }
    public bool Calibrated { get; init; }
    public double? Vpp { get; init; }
    public double? KpdVPerRad { get; init; }
    public bool IsClipping { get; init; }
    public bool IsValid { get; init; }
    public string? Message { get; init; }
}

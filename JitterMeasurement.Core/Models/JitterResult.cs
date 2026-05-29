namespace JitterMeasurement.Core.Models;

public sealed class JitterResult
{
    public required double SigmaVRms { get; init; }
    public required double SigmaPhiRad { get; init; }
    public required double SigmaTDeg { get; init; }
    public required double SigmaTFs { get; init; }
    public required double IntegratedPhiRad { get; init; }
    public required double IntegratedTFs { get; init; }
    public required double HarmonicFrequencyHz { get; init; }
    public required bool IsClipping { get; init; }
    public required bool IsValid { get; init; }
    public string? Message { get; init; }

    public static JitterResult Invalid(string message) => new()
    {
        SigmaVRms = 0,
        SigmaPhiRad = 0,
        SigmaTDeg = 0,
        SigmaTFs = 0,
        IntegratedPhiRad = 0,
        IntegratedTFs = 0,
        HarmonicFrequencyHz = 0,
        IsClipping = false,
        IsValid = false,
        Message = message
    };
}

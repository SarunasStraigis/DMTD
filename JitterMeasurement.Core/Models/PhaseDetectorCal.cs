namespace JitterMeasurement.Core.Models;

public sealed class PhaseDetectorCal
{
    public required double Vpp { get; init; }
    public required double Vpk { get; init; }
    public required double KpdVPerRad { get; init; }
    public required double KpdVPerDeg { get; init; }
    public required double BeatFrequencyHz { get; init; }
    public required bool IsClipping { get; init; }
    public required bool IsValid { get; init; }
    public string? Message { get; init; }

    public static PhaseDetectorCal Invalid(string message) => new()
    {
        Vpp = 0,
        Vpk = 0,
        KpdVPerRad = 0,
        KpdVPerDeg = 0,
        BeatFrequencyHz = 0,
        IsClipping = false,
        IsValid = false,
        Message = message
    };
}

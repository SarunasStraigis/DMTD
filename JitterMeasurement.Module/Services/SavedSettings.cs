using JitterMeasurement.Core.Models;

namespace JitterMeasurement.Module.Services;

public sealed class SavedSettings
{
    public AppSettings Measurement { get; set; } = new();
    public PhaseDetectorCal? Calibration { get; set; }
    public bool TimeContinuousAutoscale { get; set; }
    public bool FftContinuousAutoscale { get; set; }
}

namespace PhaseLab.Api;

[Flags]
public enum ModuleCapabilities
{
    None = 0,
    Capture = 1 << 0,
    Devices = 1 << 1,
    Actions = 1 << 2,
    PhaseZero = 1 << 3,
    Calibration = 1 << 4,
    SessionReset = 1 << 5
}

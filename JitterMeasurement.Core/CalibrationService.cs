using JitterMeasurement.Core.Models;

namespace JitterMeasurement.Core;

public static class CalibrationService
{
    public static PhaseDetectorCal Calibrate(float[] samples, double sampleRate, double minBeatHz = 1)
    {
        if (samples.Length < 64)
        {
            return PhaseDetectorCal.Invalid("Not enough samples for calibration.");
        }

        var isClipping = SignalProcessor.DetectClipping(samples);
        var vpp = SignalProcessor.ComputeVpp(samples);
        if (vpp <= 1e-9)
        {
            return PhaseDetectorCal.Invalid("Signal amplitude is too small for calibration.");
        }

        var vpk = vpp / 2.0;
        var kpdVPerRad = vpk;
        var kpdVPerDeg = vpk * (180.0 / Math.PI);

        var (frequencies, magnitudesDb) = SignalProcessor.ComputeMagnitudeSpectrum(samples, sampleRate);
        var beatHz = SignalProcessor.FindPeakFrequencyHz(frequencies, magnitudesDb, minBeatHz);

        var message = isClipping
            ? "Warning: input is clipping. Reduce gain before calibrating."
            : null;

        return new PhaseDetectorCal
        {
            Vpp = vpp,
            Vpk = vpk,
            KpdVPerRad = kpdVPerRad,
            KpdVPerDeg = kpdVPerDeg,
            BeatFrequencyHz = beatHz,
            IsClipping = isClipping,
            IsValid = !isClipping && beatHz > minBeatHz,
            Message = message ?? (beatHz <= minBeatHz ? "No beat tone detected above minimum frequency." : null)
        };
    }

    public static PhaseDetectorCal CalibrateFromVpp(float[] samples)
    {
        if (samples.Length < 64)
        {
            return PhaseDetectorCal.Invalid("Not enough samples for calibration.");
        }

        var isClipping = SignalProcessor.DetectClipping(samples);
        var vpp = SignalProcessor.ComputeVpp(samples);
        if (vpp <= 1e-9)
        {
            return PhaseDetectorCal.Invalid("Signal amplitude is too small for calibration.");
        }

        var vpk = vpp / 2.0;

        return new PhaseDetectorCal
        {
            Vpp = vpp,
            Vpk = vpk,
            KpdVPerRad = vpk,
            KpdVPerDeg = vpk * (180.0 / Math.PI),
            BeatFrequencyHz = 0,
            IsClipping = isClipping,
            IsValid = !isClipping,
            Message = isClipping ? "Warning: input is clipping. Reduce gain before calibrating." : null
        };
    }
}

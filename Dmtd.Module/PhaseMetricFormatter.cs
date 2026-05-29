namespace Dmtd.Module;

internal static class PhaseMetricFormatter
{
    public static (string Value, string Unit) FormatHero(double ps)
    {
        var abs = Math.Abs(ps);
        if (abs < 1)
        {
            return ($"{ps * 1e3:F1}", "fs");
        }

        if (abs < 1e3)
        {
            return ($"{ps:F2}", "ps");
        }

        if (abs < 1e6)
        {
            return ($"{ps / 1e3:F3}", "ns");
        }

        return ($"{ps / 1e6:F3}", "μs");
    }

    public static string FormatCompact(double ps)
    {
        var abs = Math.Abs(ps);
        if (abs < 1)
        {
            return $"{ps * 1e3:F3} fs";
        }

        if (abs < 1e3)
        {
            return $"{ps:F4} ps";
        }

        if (abs < 1e6)
        {
            return $"{ps / 1e3:F3} ns";
        }

        return $"{ps / 1e6:F3} μs";
    }

    public static double PhaseDiffDeg(double phaseDiffRad) =>
        phaseDiffRad * 180.0 / Math.PI;
}

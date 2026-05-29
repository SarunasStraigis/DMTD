namespace Dmtd.Core;

public static class PhaseMath
{
    public static double WrapPrincipal(double phiRad) =>
        Math.Atan2(Math.Sin(phiRad), Math.Cos(phiRad));

    public static double PsPerRad(double refFrequencyHz) =>
        1e12 / (2.0 * Math.PI * refFrequencyHz);
}

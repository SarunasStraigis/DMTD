namespace Dmtd.Core;

internal static class ButterworthFilter
{
    public static double[][] DesignLowPassSos(int sampleRate, double cutoffHz, int order)
    {
        var nyquist = 0.5 * sampleRate;
        var norm = Math.Min(0.99, Math.Max(1e-6, cutoffHz / nyquist));
        var sections = (order + 1) / 2;
        var sos = new double[sections][];

        for (var k = 0; k < sections; k++)
        {
            var theta = Math.PI * (2.0 * k + order + 1) / (2.0 * order);
            var real = -Math.Sin(theta);
            var imag = Math.Cos(theta);

            var alpha = Math.Sin(Math.PI * norm) / (2.0 * imag);
            var b0 = alpha;
            var b1 = 2 * alpha;
            var b2 = alpha;
            var a0 = 1 + alpha;
            var a1 = -2 * Math.Cos(Math.PI * norm);
            var a2 = 1 - alpha;

            sos[k] = new[]
            {
                b0 / a0, b1 / a0, b2 / a0,
                1.0, a1 / a0, a2 / a0
            };
        }

        return sos;
    }

    public static double[] SosFiltFilt(double[][] sos, ReadOnlySpan<double> input)
    {
        if (input.Length == 0)
        {
            return Array.Empty<double>();
        }

        var forward = SosFilter(sos, input);
        var reversed = forward.AsSpan().ToArray();
        Array.Reverse(reversed);
        var backward = SosFilter(sos, reversed);
        Array.Reverse(backward);
        return backward;
    }

    private static double[] SosFilter(double[][] sos, ReadOnlySpan<double> input)
    {
        var output = input.ToArray();
        foreach (var section in sos)
        {
            var b0 = section[0];
            var b1 = section[1];
            var b2 = section[2];
            var a1 = section[4];
            var a2 = section[5];

            double z1 = 0, z2 = 0;
            for (var i = 0; i < output.Length; i++)
            {
                var x = output[i];
                var y = b0 * x + z1;
                z1 = b1 * x - a1 * y + z2;
                z2 = b2 * x - a2 * y;
                output[i] = y;
            }
        }

        return output;
    }
}

namespace PhaseLab.UI;

public static class PlotScaleHelper
{
    public static (double Min, double Max) RangeWithPadding(double[] values, double paddingFraction = 0.1, double minSpan = 1e-6)
    {
        if (values.Length == 0)
        {
            return (0, 1);
        }

        var min = values.Min();
        var max = values.Max();

        if (Math.Abs(max - min) < minSpan)
        {
            var center = (max + min) / 2;
            var half = minSpan / 2;
            return (center - half, center + half);
        }

        var pad = (max - min) * paddingFraction;
        return (min - pad, max + pad);
    }
}

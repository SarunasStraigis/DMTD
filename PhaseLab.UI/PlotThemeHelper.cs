using ScottPlot;
using ScottPlot.WPF;
using System.Windows;

namespace PhaseLab.UI;

public static class PlotThemeHelper
{
    public static void ApplyChrome(WpfPlot plot)
    {
        var figure = GetColor("ChartFigureBackground", "#FFFFFF");
        var data = GetColor("ChartDataBackground", "#FAFAFA");
        var text = GetColor("ChartText", "#333333");
        var grid = GetColor("ChartGrid", "#888888").WithAlpha(60);

        plot.Plot.FigureBackground.Color = figure;
        plot.Plot.DataBackground.Color = data;
        plot.Plot.Axes.Color(text);
        plot.Plot.Grid.MajorLineColor = grid;
    }

    public static Color GetSignalColor() => GetColor("ChartSignalColor", "#1565C0");

    public static Color GetIntegrationLineColor() =>
        GetColor("ChartIntegrationLine", "#1565C0").WithAlpha(140);

    public static Color GetSecondarySignalColor() => GetColor("ChartSecondaryColor", "#7B1FA2");

    public static void ApplyLegend(Plot plot)
    {
        var text = GetColor("ChartText", "#333333");
        var figure = GetColor("ChartFigureBackground", "#FFFFFF");
        plot.Legend.IsVisible = true;
        plot.Legend.Alignment = Alignment.UpperRight;
        plot.Legend.FontColor = text;
        plot.Legend.FontSize = 12;
        plot.Legend.BackgroundColor = figure.WithAlpha(220);
        plot.Legend.OutlineColor = text.WithAlpha(80);
        plot.Legend.ShadowColor = Colors.Transparent;
    }

    private static Color GetColor(string key, string fallbackHex)
    {
        if (Application.Current.TryFindResource(key) is string hex && !string.IsNullOrWhiteSpace(hex))
        {
            return ParseHex(hex, fallbackHex);
        }

        return ParseHex(fallbackHex, fallbackHex);
    }

    private static Color ParseHex(string hex, string fallbackHex)
    {
        try
        {
            var normalized = hex.Trim();
            if (normalized.StartsWith('#'))
            {
                normalized = normalized[1..];
            }

            if (normalized.Length == 8)
            {
                normalized = normalized[2..];
            }

            return Color.FromHex("#" + normalized);
        }
        catch
        {
            return Color.FromHex(fallbackHex.StartsWith('#') ? fallbackHex : "#" + fallbackHex);
        }
    }
}

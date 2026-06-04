using Dmtd.Core;
using System.Text.Json;
using System.Text.Json.Serialization;
using Xunit;

namespace Dmtd.Core.Tests;

public sealed class DmtdGoldenVectorTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) }
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void ProcessBlock_MatchesPythonGoldenVectors(GoldenCase testCase)
    {
        var settings = MapSettings(testCase.Settings);
        var processor = new DmtdProcessor(settings);
        var generator = testCase.Generator;

        for (var blockIndex = 0; blockIndex < generator.Blocks; blockIndex++)
        {
            var (chA, chB) = GoldenSignalGenerator.CreateBlock(generator, blockIndex);
            var actual = processor.ProcessBlock(chA, chB);
            var expected = testCase.Expected[blockIndex];
            const double tolerance = 1e-7;

            AssertEqual(expected.PhaseDiffRad, actual.PhaseDiffRad, tolerance, $"{testCase.Name}[{blockIndex}].phase_diff_rad");
            AssertEqual(expected.PhaseDiffPs, actual.PhaseDiffPs, tolerance * PhaseMath.PsPerRad(settings.RefFrequency), $"{testCase.Name}[{blockIndex}].phase_diff_ps");
            AssertEqual(expected.BeatFreq, actual.BeatFreq, 1e-9, $"{testCase.Name}[{blockIndex}].beat_freq");
            AssertEqual(expected.PhaseAPs, actual.PhaseAPs, tolerance * PhaseMath.PsPerRad(settings.RefFrequency), $"{testCase.Name}[{blockIndex}].phase_a_ps");
            AssertEqual(expected.PhaseBPs, actual.PhaseBPs, tolerance * PhaseMath.PsPerRad(settings.RefFrequency), $"{testCase.Name}[{blockIndex}].phase_b_ps");
            AssertEqual(expected.PhaseADeg, actual.PhaseADeg, tolerance * (180.0 / Math.PI), $"{testCase.Name}[{blockIndex}].phase_a_deg");
            AssertEqual(expected.PhaseBDeg, actual.PhaseBDeg, tolerance * (180.0 / Math.PI), $"{testCase.Name}[{blockIndex}].phase_b_deg");
            AssertEqual(expected.RmsA, actual.RmsA, 1e-6, $"{testCase.Name}[{blockIndex}].rms_a");
            AssertEqual(expected.RmsB, actual.RmsB, 1e-6, $"{testCase.Name}[{blockIndex}].rms_b");
        }
    }

    public static IEnumerable<object[]> Cases()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "GoldenVectors", "dmtd_golden_vectors.json");
        var json = File.ReadAllText(path);
        var document = JsonSerializer.Deserialize<GoldenDocument>(json, JsonOptions)
            ?? throw new InvalidOperationException("Failed to load golden vectors.");

        foreach (var testCase in document.Cases)
        {
            yield return new object[] { testCase };
        }
    }

    private static DmtdSettings MapSettings(GoldenSettings settings) =>
        new()
        {
            SampleRate = settings.SampleRate,
            BlockDurationMs = settings.SampleRate > 0
                ? settings.BlockSize * 1000.0 / settings.SampleRate
                : 1000.0,
            BeatFrequency = settings.BeatFrequency,
            RefFrequency = settings.RefFrequency,
            FreqEstimator = settings.FreqEstimator,
            FreqSource = settings.FreqSource,
            IqMinMag = settings.IqMinMag,
            IqWindow = settings.IqWindow
        };

    private static void AssertEqual(double expected, double actual, double tolerance, string label)
    {
        Assert.True(
            Math.Abs(expected - actual) <= tolerance,
            $"{label}: expected {expected:G17}, actual {actual:G17}, tolerance {tolerance:G17}");
    }
}

public sealed class GoldenDocument
{
    public required List<GoldenCase> Cases { get; init; }
}

public sealed class GoldenCase
{
    public required string Name { get; init; }
    public required GoldenSettings Settings { get; init; }
    public required GoldenGenerator Generator { get; init; }
    public required List<GoldenExpected> Expected { get; init; }
}

public sealed class GoldenSettings
{
    public int SampleRate { get; init; }
    public int BlockSize { get; init; }
    public double BeatFrequency { get; init; }
    public double RefFrequency { get; init; }
    public FreqEstimator FreqEstimator { get; init; } = FreqEstimator.Fixed;
    public FreqSource FreqSource { get; init; } = FreqSource.ChA;
    public double IqMinMag { get; init; } = 1e-4;
    public IqWindow IqWindow { get; init; } = IqWindow.Hann;
    public double BOffsetRad { get; init; }
    public double BDriftRadPerBlock { get; init; }
    public double Amp { get; init; } = 0.45;
}

public sealed class GoldenGenerator
{
    public int SampleRate { get; init; }
    public int N { get; init; }
    public int Blocks { get; init; }
    public double BeatHz { get; init; }
    public double BOffsetRad { get; init; }
    public double BDriftRadPerBlock { get; init; }
    public double Amp { get; init; } = 0.45;
}

public sealed class GoldenExpected
{
    public double PhaseDiffRad { get; init; }
    public double PhaseDiffPs { get; init; }
    public double BeatFreq { get; init; }
    public double PhaseAPs { get; init; }
    public double PhaseBPs { get; init; }
    public double PhaseADeg { get; init; }
    public double PhaseBDeg { get; init; }
    public double RmsA { get; init; }
    public double RmsB { get; init; }
}

internal static class GoldenSignalGenerator
{
    public static (float[] ChA, float[] ChB) CreateBlock(GoldenGenerator generator, int blockIndex)
    {
        var chA = new float[generator.N];
        var chB = new float[generator.N];
        var start = blockIndex * generator.N;
        var bOffset = generator.BOffsetRad + blockIndex * generator.BDriftRadPerBlock;

        for (var i = 0; i < generator.N; i++)
        {
            var t = (start + i) / (double)generator.SampleRate;
            var phaseA = 2.0 * Math.PI * generator.BeatHz * t;
            var phaseB = phaseA + bOffset;
            chA[i] = (float)(generator.Amp * Math.Sin(phaseA));
            chB[i] = (float)(generator.Amp * Math.Sin(phaseB));
        }

        return (chA, chB);
    }
}

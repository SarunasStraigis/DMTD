using Dmtd.Core;
using Xunit;

namespace Dmtd.Core.Tests;

public class DmtdProcessorTests
{
    [Fact]
    public void ProcessBlock_1kHzBeat_ProducesFinitePhase()
    {
        const int sampleRate = 48_000;
        const int n = 4800;
        const double beatHz = 1000.0;
        var settings = new DmtdSettings
        {
            SampleRate = sampleRate,
            BeatFrequency = beatHz,
            RefFrequency = 90_000_000,
            FreqEstimator = FreqEstimator.Fixed,
            DemodMode = DemodMode.BlockIq
        };

        var processor = new DmtdProcessor(settings);
        var chA = new float[n];
        var chB = new float[n];
        for (var i = 0; i < n; i++)
        {
            var t = i / (double)sampleRate;
            var phase = 2.0 * Math.PI * beatHz * t;
            chA[i] = (float)(0.5 * Math.Sin(phase));
            chB[i] = (float)(0.5 * Math.Sin(phase + 0.01));
        }

        var result = processor.ProcessBlock(chA, chB);
        Assert.True(double.IsFinite(result.PhaseDiffPs));
        Assert.InRange(result.RmsA, 0.01, 1.0);
        Assert.InRange(result.RmsB, 0.01, 1.0);
    }

    [Fact]
    public void WrapPrincipal_FoldsToPiRange()
    {
        var wrapped = PhaseMath.WrapPrincipal(4.0);
        Assert.InRange(wrapped, -Math.PI, Math.PI);
    }
}

using JitterMeasurement.Core;
using Xunit;

namespace JitterMeasurement.Core.Tests;

public sealed class IntegrationBandTests
{
    [Fact]
    public void Clamp_CapsHighAtNyquist()
    {
        var (low, high) = IntegrationBand.Clamp(10, 100_000, 192_000);
        Assert.Equal(10, low);
        Assert.Equal(96_000, high);
    }

    [Fact]
    public void Clamp_HighNotBelowLow()
    {
        var (low, high) = IntegrationBand.Clamp(5000, 100, 192_000);
        Assert.Equal(5000, low);
        Assert.Equal(5000, high);
    }
}

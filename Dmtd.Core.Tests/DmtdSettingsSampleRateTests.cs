using Dmtd.Core;
using Xunit;

namespace Dmtd.Core.Tests;

public sealed class DmtdSettingsSampleRateTests
{
    [Fact]
    public void ResolveBlockSize_ScalesWithSampleRate()
    {
        var settings = new DmtdSettings { BlockDurationMs = 1000 };

        var at48k = settings.ResolveBlockSize(48_000);
        var at192k = settings.ResolveBlockSize(192_000);

        Assert.Equal(48_000, at48k);
        Assert.Equal(192_000, at192k);
        Assert.Equal(4, at192k / (double)at48k, 3);
    }
}

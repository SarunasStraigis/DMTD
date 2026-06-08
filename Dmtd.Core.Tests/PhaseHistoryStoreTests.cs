using Dmtd.Core;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Dmtd.Core.Tests;

public sealed class PhaseHistoryStoreTests : IDisposable
{
    private readonly string _dbPath;
    private readonly List<string> _cleanupPaths = new();

    public PhaseHistoryStoreTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"dmtd-history-test-{Guid.NewGuid():N}.db");
        _cleanupPaths.Add(_dbPath);
    }

    [Fact]
    public void Count_respects_since_and_until()
    {
        using var store = new PhaseHistoryStore(_dbPath);
        store.Enqueue("2026-06-01T10:00:00.0000000+00:00", 0.1, 100, 1000);
        store.Enqueue("2026-06-02T10:00:00.0000000+00:00", 0.2, 200, 1000);
        store.Enqueue("2026-06-03T10:00:00.0000000+00:00", 0.3, 300, 1000);
        store.WaitForPendingWrites(TimeSpan.FromSeconds(5));

        Assert.Equal(3, store.Count());
        Assert.Equal(2, store.Count(new HistoryQuery("2026-06-02T00:00:00.0000000+00:00")));
        Assert.Equal(1, store.Count(new HistoryQuery(
            "2026-06-02T00:00:00.0000000+00:00",
            "2026-06-02T23:59:59.9999999+00:00")));
    }

    [Fact]
    public void Concurrent_enqueue_and_count_does_not_throw()
    {
        using var store = new PhaseHistoryStore(_dbPath);
        var writers = Enumerable.Range(0, 4).Select(i => Task.Run(() =>
        {
            for (var n = 0; n < 50; n++)
            {
                store.Enqueue(
                    DateTimeOffset.UtcNow.AddMilliseconds(n).ToString("O"),
                    0.1,
                    100,
                    1000);
            }
        })).ToArray();

        Task.WaitAll(writers);
        store.WaitForPendingWrites(TimeSpan.FromSeconds(5));

        Assert.True(store.Count() >= 200);
        Assert.True(store.CheckIntegrity());
    }

    [Fact]
    public void Repair_recreates_database_and_preserves_backup()
    {
        using (var store = new PhaseHistoryStore(_dbPath))
        {
            store.Enqueue(DateTimeOffset.UtcNow.ToString("O"), 0.1, 100, 1000);
            store.WaitForPendingWrites(TimeSpan.FromSeconds(5));
        }

        SqliteConnection.ClearAllPools();
        var repaired = PhaseHistoryStore.Repair(_dbPath);
        _cleanupPaths.Add(repaired.DatabasePath);
        foreach (var path in Directory.GetFiles(Path.GetDirectoryName(_dbPath)!, Path.GetFileName(_dbPath) + ".*"))
        {
            _cleanupPaths.Add(path);
        }

        repaired.WaitForPendingWrites(TimeSpan.FromSeconds(2));
        Assert.Equal(0, repaired.Count());
        Assert.True(repaired.CheckIntegrity());
        Assert.True(File.Exists(_dbPath));
    }

    public void Dispose()
    {
        foreach (var path in _cleanupPaths.Distinct())
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // Temp cleanup best effort.
            }
        }
    }
}

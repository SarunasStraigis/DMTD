using System.Collections.Concurrent;
using Microsoft.Data.Sqlite;

namespace Dmtd.Core;

public sealed class PhaseHistoryStore : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly BlockingCollection<(string Ts, double PhaseRad, double PhasePs, double BeatFreq)> _queue = new(1024);
    private readonly Thread _writerThread;
    private volatile bool _disposed;

    public PhaseHistoryStore(string dbPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
        _connection = new SqliteConnection($"Data Source={dbPath}");
        _connection.Open();
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS phase_log (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                ts TEXT NOT NULL,
                phase_rad REAL NOT NULL,
                phase_ps REAL NOT NULL,
                beat_freq REAL NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_ts ON phase_log (ts);
            """;
        cmd.ExecuteNonQuery();

        _writerThread = new Thread(WriterLoop) { IsBackground = true, Name = "PhaseHistoryWriter" };
        _writerThread.Start();
    }

    public void Enqueue(string ts, double phaseRad, double phasePs, double beatFreq)
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            _queue.Add((ts, phaseRad, phasePs, beatFreq));
        }
        catch (InvalidOperationException)
        {
            // Store is shutting down.
        }
    }

    public IReadOnlyList<HistoryRow> Query(int limit = 10_000, string? since = null)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT ts, phase_rad, phase_ps, beat_freq FROM phase_log";
        if (!string.IsNullOrWhiteSpace(since))
        {
            cmd.CommandText += " WHERE ts >= $since";
            cmd.Parameters.AddWithValue("$since", since);
        }

        cmd.CommandText += " ORDER BY ts DESC LIMIT $limit";
        cmd.Parameters.AddWithValue("$limit", limit);

        var rows = new List<HistoryRow>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            rows.Add(new HistoryRow(
                reader.GetString(0),
                reader.GetDouble(1),
                reader.GetDouble(2),
                reader.GetDouble(3)));
        }

        rows.Reverse();
        return rows;
    }

    public int Count(string? since = null)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM phase_log";
        if (!string.IsNullOrWhiteSpace(since))
        {
            cmd.CommandText += " WHERE ts >= $since";
            cmd.Parameters.AddWithValue("$since", since);
        }

        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    public void PruneOldRows(int retentionDays)
    {
        var cutoff = DateTimeOffset.UtcNow.AddDays(-retentionDays).ToString("O");
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "DELETE FROM phase_log WHERE ts < $cutoff";
        cmd.Parameters.AddWithValue("$cutoff", cutoff);
        cmd.ExecuteNonQuery();
    }

    public string ExportCsv(string? since = null)
    {
        var rows = Query(limit: 1_000_000, since: since);
        using var writer = new StringWriter();
        writer.WriteLine("ts,phase_rad,phase_ps,beat_freq");
        foreach (var row in rows)
        {
            writer.WriteLine($"{row.Timestamp},{row.PhaseRad},{row.PhasePs},{row.BeatFreq}");
        }

        return writer.ToString();
    }

    private void WriterLoop()
    {
        while (!_disposed)
        {
            try
            {
                if (!_queue.TryTake(out var row, 500))
                {
                    continue;
                }

                var batch = new List<(string, double, double, double)> { row };
                while (batch.Count < 64 && _queue.TryTake(out var extra))
                {
                    batch.Add(extra);
                }

                using var tx = _connection.BeginTransaction();
                using var cmd = _connection.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = "INSERT INTO phase_log (ts, phase_rad, phase_ps, beat_freq) VALUES ($ts, $rad, $ps, $bf)";
                var pTs = cmd.Parameters.Add("$ts", SqliteType.Text);
                var pRad = cmd.Parameters.Add("$rad", SqliteType.Real);
                var pPs = cmd.Parameters.Add("$ps", SqliteType.Real);
                var pBf = cmd.Parameters.Add("$bf", SqliteType.Real);

                foreach (var (ts, phaseRad, phasePs, beatFreq) in batch)
                {
                    pTs.Value = ts;
                    pRad.Value = phaseRad;
                    pPs.Value = phasePs;
                    pBf.Value = beatFreq;
                    cmd.ExecuteNonQuery();
                }

                tx.Commit();
            }
            catch
            {
                // Ignore transient write failures.
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _queue.CompleteAdding();
        _writerThread.Join(TimeSpan.FromSeconds(2));
        _connection.Dispose();
    }
}

public readonly record struct HistoryRow(string Timestamp, double PhaseRad, double PhasePs, double BeatFreq);

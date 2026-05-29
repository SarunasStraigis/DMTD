using Dmtd.Core;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Dmtd.Core;

public static class DmtdSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static DmtdSettings Load()
    {
        try
        {
            if (!File.Exists(AppDataPaths.DmtdSettingsFile))
            {
                return TryMigrateFromRepoConfig() ?? new DmtdSettings();
            }

            var json = File.ReadAllText(AppDataPaths.DmtdSettingsFile);
            return JsonSerializer.Deserialize<DmtdSettings>(json, JsonOptions) ?? new DmtdSettings();
        }
        catch
        {
            return new DmtdSettings();
        }
    }

    public static void Save(DmtdSettings settings)
    {
        Directory.CreateDirectory(AppDataPaths.DmtdSettingsDirectory);
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(AppDataPaths.DmtdSettingsFile, json);
    }

    private static DmtdSettings? TryMigrateFromRepoConfig()
    {
        try
        {
            var repoConfig = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "config.json");
            if (!File.Exists(repoConfig))
            {
                repoConfig = Path.Combine(Directory.GetCurrentDirectory(), "config.json");
            }

            if (!File.Exists(repoConfig))
            {
                return null;
            }

            using var doc = JsonDocument.Parse(File.ReadAllText(repoConfig));
            var root = doc.RootElement;
            var settings = new DmtdSettings();
            if (root.TryGetProperty("sample_rate", out var sr))
            {
                settings.SampleRate = sr.GetInt32();
            }

            if (root.TryGetProperty("block_size", out var bs))
            {
                settings.BlockSize = bs.GetInt32();
            }

            if (root.TryGetProperty("beat_frequency", out var bf))
            {
                settings.BeatFrequency = bf.GetDouble();
            }

            if (root.TryGetProperty("ref_frequency", out var rf))
            {
                settings.RefFrequency = rf.GetDouble();
            }

            if (root.TryGetProperty("freq_estimator", out var fe))
            {
                settings.FreqEstimator = fe.GetString() switch
                {
                    "fixed" => FreqEstimator.Fixed,
                    _ => FreqEstimator.FftPeak
                };
            }

            if (root.TryGetProperty("freq_source", out var fs))
            {
                settings.FreqSource = fs.GetString() switch
                {
                    "avg_ab" => FreqSource.AvgAb,
                    _ => FreqSource.ChA
                };
            }

            if (root.TryGetProperty("demod_mode", out var dm))
            {
                settings.DemodMode = dm.GetString() switch
                {
                    "block_iq_fir" => DemodMode.BlockIqFir,
                    "pll_tracker" => DemodMode.PllTracker,
                    _ => DemodMode.BlockIq
                };
            }

            if (root.TryGetProperty("iq_window", out var iw))
            {
                settings.IqWindow = iw.GetString() == "none" ? IqWindow.None : IqWindow.Hann;
            }

            if (root.TryGetProperty("iq_min_mag", out var imm))
            {
                settings.IqMinMag = imm.GetDouble();
            }

            if (root.TryGetProperty("iq_lpf_cutoff_hz", out var lpf))
            {
                settings.IqLpfCutoffHz = lpf.GetDouble();
            }

            if (root.TryGetProperty("iq_lpf_order", out var lpo))
            {
                settings.IqLpfOrder = lpo.GetInt32();
            }

            if (root.TryGetProperty("pll_kp", out var pk))
            {
                settings.PllKp = pk.GetDouble();
            }

            if (root.TryGetProperty("pll_ki", out var pi))
            {
                settings.PllKi = pi.GetDouble();
            }

            if (root.TryGetProperty("pll_min_mag", out var pmm))
            {
                settings.PllMinMag = pmm.GetDouble();
            }

            Save(settings);
            return settings;
        }
        catch
        {
            return null;
        }
    }
}

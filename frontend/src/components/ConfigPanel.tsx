import { useEffect, useState } from "react";
import { api, computedExpansionFactor, type AppConfig, type DeviceInfo } from "../api/client";

const SAMPLE_RATES = [44100, 48000, 96000, 192000] as const;

function Field({
  label,
  hint,
  children,
}: {
  label: string;
  hint?: string;
  children: React.ReactNode;
}) {
  return (
    <div className="flex flex-col gap-1">
      <label className="text-sm font-medium text-gray-300">{label}</label>
      {hint && <p className="text-xs text-gray-500">{hint}</p>}
      {children}
    </div>
  );
}

const inputClass =
  "rounded-md bg-gray-800 border border-gray-600 px-3 py-1.5 text-sm text-white focus:outline-none focus:ring-2 focus:ring-cyan-500";

export function ConfigPanel() {
  const [cfg, setCfg] = useState<AppConfig | null>(null);
  const [devices, setDevices] = useState<DeviceInfo[]>([]);
  const [saving, setSaving] = useState(false);
  const [saved, setSaved] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    Promise.all([api.getConfig(), api.getDevices()])
      .then(([config, devList]) => {
        setCfg(config);
        setDevices(devList);
      })
      .catch((e) => setError(String(e)));
  }, []);

  const update = <K extends keyof AppConfig>(key: K, value: AppConfig[K]) => {
    setCfg((prev) => (prev ? { ...prev, [key]: value } : prev));
  };

  const save = async () => {
    if (!cfg) return;
    setSaving(true);
    setError(null);
    try {
      await api.putConfig(cfg);
      setSaved(true);
      setTimeout(() => setSaved(false), 2000);
    } catch (e) {
      setError(String(e));
    } finally {
      setSaving(false);
    }
  };

  if (!cfg) {
    return (
      <div className="bg-gray-900 rounded-xl p-4 shadow-lg text-gray-400 text-sm">
        {error ? `Error: ${error}` : "Loading configuration…"}
      </div>
    );
  }

  return (
    <div className="bg-gray-900 rounded-xl p-5 shadow-lg">
      <h2 className="text-amber-400 font-semibold text-lg mb-5">
        Configuration
      </h2>

      {/* Channel wiring note */}
      <div className="mb-4 rounded-lg bg-gray-800 border border-gray-700 px-4 py-3 text-xs text-gray-400 flex gap-6">
        <span>
          <span className="text-cyan-400 font-semibold">Left channel (ch 0)</span>
          {" "}→ DUT 1 beat note
        </span>
        <span>
          <span className="text-violet-400 font-semibold">Right channel (ch 1)</span>
          {" "}→ DUT 2 beat note
        </span>
        <span className="ml-auto text-gray-600">AC-coupled · no phantom power</span>
      </div>

      <div className="grid grid-cols-1 md:grid-cols-2 gap-5">
        {/* Audio device */}
        <Field
          label="Audio Input Device"
          hint="Stereo audio interface (min 24-bit, 192 kSPS recommended)"
        >
          <select
            className={inputClass}
            value={cfg.device_index ?? ""}
            onChange={(e) =>
              update(
                "device_index",
                e.target.value === "" ? null : Number(e.target.value)
              )
            }
          >
            <option value="">System default</option>
            {devices.map((d) => (
              <option key={d.index} value={d.index}>
                [{d.index}] {d.name} ({d.max_input_channels}ch,{" "}
                {d.default_samplerate / 1000}kHz)
              </option>
            ))}
          </select>
        </Field>

        {/* Sample rate */}
        <Field label="Sample Rate" hint="Audio interface sample rate in Hz">
          <select
            className={inputClass}
            value={cfg.sample_rate}
            onChange={(e) =>
              update(
                "sample_rate",
                Number(e.target.value) as AppConfig["sample_rate"]
              )
            }
          >
            {SAMPLE_RATES.map((r) => (
              <option key={r} value={r}>
                {r.toLocaleString()} Hz
              </option>
            ))}
          </select>
        </Field>

        {/* Block size */}
        <Field
          label="Block Size (samples)"
          hint="Larger = lower noise floor, lower time resolution"
        >
          <input
            type="number"
            className={inputClass}
            value={cfg.block_size}
            min={1024}
            max={1920000}
            step={1024}
            onChange={(e) => update("block_size", Number(e.target.value))}
          />
        </Field>

        {/* Beat frequency */}
        <Field
          label="Nominal Beat Frequency (Hz)"
          hint="Expected IF beat note frequency (e.g. 1000)"
        >
          <input
            type="number"
            className={inputClass}
            value={cfg.beat_frequency}
            min={0.1}
            step={0.1}
            onChange={(e) => update("beat_frequency", Number(e.target.value))}
          />
        </Field>

        {/* Frequency estimator */}
        <Field
          label="Frequency Estimator"
          hint="fft_peak: measure actual beat each block; fixed: use nominal value"
        >
          <select
            className={inputClass}
            value={cfg.freq_estimator}
            onChange={(e) =>
              update(
                "freq_estimator",
                e.target.value as AppConfig["freq_estimator"]
              )
            }
          >
            <option value="fft_peak">FFT Peak (adaptive)</option>
            <option value="fixed">Fixed (nominal)</option>
          </select>
        </Field>

        {/* Reference oscillator frequency */}
        <Field
          label="Oscillator Frequency — both DUTs (Hz)"
          hint="Frequency of the two test oscillators (e.g. 90 000 000 for 90 MHz). Also used to convert phase → picoseconds."
        >
          <input
            type="number"
            className={inputClass}
            value={cfg.ref_frequency}
            min={1}
            step={1000}
            onChange={(e) => update("ref_frequency", Number(e.target.value))}
          />
        </Field>

        {/* Expansion factor — derived, read-only */}
        <Field
          label="Expansion Factor (computed)"
          hint="= Oscillator freq / Beat freq — automatically derived, no manual entry needed"
        >
          <div className="rounded-md bg-gray-700 border border-gray-600 px-3 py-1.5 text-sm text-cyan-300 font-mono">
            {computedExpansionFactor(cfg).toLocaleString(undefined, {
              maximumFractionDigits: 1,
            })}
          </div>
        </Field>

        {/* History retention */}
        <Field
          label="History Retention (days)"
          hint="Phase log rows older than this are pruned"
        >
          <input
            type="number"
            className={inputClass}
            value={cfg.history_retention_days}
            min={1}
            max={3650}
            onChange={(e) =>
              update("history_retention_days", Number(e.target.value))
            }
          />
        </Field>
      </div>

      {error && (
        <p className="mt-4 text-red-400 text-sm">Save error: {error}</p>
      )}

      <div className="mt-6 flex items-center gap-4">
        <button
          onClick={save}
          disabled={saving}
          className="px-5 py-2 rounded-lg bg-amber-500 hover:bg-amber-400 text-gray-900 font-semibold text-sm disabled:opacity-50 transition"
        >
          {saving ? "Saving…" : "Save Configuration"}
        </button>
        {saved && (
          <span className="text-green-400 text-sm">Saved successfully</span>
        )}
      </div>
    </div>
  );
}

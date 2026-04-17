import { useEffect, useState } from "react";
import {
  api,
  type OutputDeviceInfo,
  type SigGenConfig,
  type SigGenStatus,
} from "../api/client";

const SAMPLE_RATES = [44100, 48000, 96000, 192000] as const;

const inputClass =
  "rounded-md bg-gray-800 border border-gray-600 px-3 py-1.5 text-sm text-white focus:outline-none focus:ring-2 focus:ring-emerald-500 w-full";

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

export function SigGenPanel() {
  const [status, setStatus] = useState<SigGenStatus | null>(null);
  const [devices, setDevices] = useState<OutputDeviceInfo[]>([]);
  const [cfg, setCfg] = useState<SigGenConfig>({
    output_device_index: null,
    frequency: 1000.0,
    amplitude: 0.5,
    phase_offset_deg: 0.0,
    sample_rate: 48000,
  });
  const [pending, setPending] = useState(false);
  const [error, setError] = useState<string | null>(null);

  // Load initial status and device list
  useEffect(() => {
    Promise.all([api.siggen.getStatus(), api.siggen.getDevices()])
      .then(([s, devs]) => {
        setStatus(s);
        setDevices(devs);
        const { running: _r, ...cfgFields } = s;
        setCfg(cfgFields);
      })
      .catch((e) => setError(String(e)));
  }, []);

  const update = <K extends keyof SigGenConfig>(k: K, v: SigGenConfig[K]) =>
    setCfg((prev) => ({ ...prev, [k]: v }));

  const applyConfig = async () => {
    setPending(true);
    setError(null);
    try {
      if (status?.running) {
        // Update live: backend stops + restarts with new config
        await api.siggen.putConfig(cfg);
      } else {
        await api.siggen.putConfig(cfg);
      }
      setStatus((prev) => (prev ? { ...prev, ...cfg } : null));
    } catch (e) {
      setError(String(e));
    } finally {
      setPending(false);
    }
  };

  const toggleOutput = async () => {
    setPending(true);
    setError(null);
    try {
      if (status?.running) {
        await api.siggen.stop();
        setStatus((prev) => (prev ? { ...prev, running: false } : null));
      } else {
        await api.siggen.start(cfg);
        setStatus((prev) => (prev ? { ...prev, ...cfg, running: true } : null));
      }
    } catch (e) {
      setError(String(e));
    } finally {
      setPending(false);
    }
  };

  const isRunning = status?.running ?? false;

  return (
    <div className="bg-gray-900 rounded-xl p-5 shadow-lg">
      {/* Header */}
      <div className="flex items-center justify-between mb-4 flex-wrap gap-3">
        <div>
          <h2 className="text-emerald-400 font-semibold text-lg">
            Signal Generator
          </h2>
          <p className="text-xs text-gray-500 mt-0.5">
            Outputs two sine waves on L/R. Connect audio outputs → inputs to
            verify phase measurement accuracy.
          </p>
        </div>
        <div className="flex items-center gap-3">
          <span
            className={`text-xs font-medium ${isRunning ? "text-emerald-400" : "text-gray-500"}`}
          >
            {isRunning ? "Outputting" : "Stopped"}
          </span>
          <span
            className={`w-2.5 h-2.5 rounded-full ${isRunning ? "bg-emerald-400 animate-pulse" : "bg-gray-600"}`}
          />
          <button
            onClick={toggleOutput}
            disabled={pending}
            className={`px-5 py-1.5 rounded-lg text-sm font-semibold transition disabled:opacity-50 ${
              isRunning
                ? "bg-red-700 hover:bg-red-600 text-white"
                : "bg-emerald-700 hover:bg-emerald-600 text-white"
            }`}
          >
            {pending ? "…" : isRunning ? "Stop Output" : "Start Output"}
          </button>
        </div>
      </div>

      {/* Self-test wiring hint */}
      <div className="mb-5 rounded-lg bg-gray-800 border border-gray-700 px-4 py-3 text-xs text-gray-400 flex flex-wrap gap-6">
        <span>
          <span className="text-emerald-400 font-semibold">Output L</span>
          {" "}→ Focusrite Output 1 → cable → Focusrite Input 1 →{" "}
          <span className="text-cyan-400">Ch A (DUT 1)</span>
        </span>
        <span>
          <span className="text-emerald-400 font-semibold">Output R</span>
          {" "}→ Focusrite Output 2 → cable → Focusrite Input 2 →{" "}
          <span className="text-violet-400">Ch B (DUT 2)</span>
        </span>
      </div>

      <div className="grid grid-cols-1 md:grid-cols-2 gap-5">
        {/* Output device */}
        <Field
          label="Output Device"
          hint="Stereo output (2+ channels required)"
        >
          <select
            className={inputClass}
            value={cfg.output_device_index ?? ""}
            onChange={(e) =>
              update(
                "output_device_index",
                e.target.value === "" ? null : Number(e.target.value)
              )
            }
          >
            <option value="">System default</option>
            {devices.map((d) => (
              <option key={d.index} value={d.index}>
                [{d.index}] {d.name} ({d.max_output_channels}ch
                {!d.stereo ? " — mono only" : ""})
              </option>
            ))}
          </select>
        </Field>

        {/* Sample rate */}
        <Field label="Output Sample Rate" hint="Should match your interface setting">
          <select
            className={inputClass}
            value={cfg.sample_rate}
            onChange={(e) => update("sample_rate", Number(e.target.value))}
          >
            {SAMPLE_RATES.map((r) => (
              <option key={r} value={r}>
                {r.toLocaleString()} Hz
              </option>
            ))}
          </select>
        </Field>

        {/* Frequency */}
        <Field
          label="Frequency (Hz)"
          hint="Set to match your configured beat frequency (e.g. 1000 Hz)"
        >
          <input
            type="number"
            className={inputClass}
            value={cfg.frequency}
            min={1}
            max={96000}
            step={0.1}
            onChange={(e) => update("frequency", Number(e.target.value))}
          />
        </Field>

        {/* Amplitude */}
        <Field
          label={`Amplitude — ${(cfg.amplitude * 100).toFixed(0)}%`}
          hint="Output level. Keep below 0.8 to stay away from clipping."
        >
          <input
            type="range"
            min={0}
            max={1}
            step={0.01}
            value={cfg.amplitude}
            onChange={(e) => update("amplitude", Number(e.target.value))}
            className="w-full accent-emerald-500"
          />
          <div className="flex justify-between text-xs text-gray-600 -mt-1">
            <span>0%</span>
            <span>50%</span>
            <span>100%</span>
          </div>
        </Field>

        {/* Phase offset — full width */}
        <div className="md:col-span-2">
          <Field
            label={`Phase Offset Ch R vs Ch L — ${cfg.phase_offset_deg.toFixed(1)}°`}
            hint="The DMTD measurement should read this exact phase difference (scaled by expansion factor). Use as a calibration check."
          >
            <div className="flex gap-3 items-center">
              <input
                type="range"
                min={0}
                max={360}
                step={0.1}
                value={cfg.phase_offset_deg}
                onChange={(e) =>
                  update("phase_offset_deg", Number(e.target.value))
                }
                className="flex-1 accent-emerald-500"
              />
              <input
                type="number"
                min={0}
                max={360}
                step={0.1}
                value={cfg.phase_offset_deg}
                onChange={(e) =>
                  update(
                    "phase_offset_deg",
                    Math.min(360, Math.max(0, Number(e.target.value)))
                  )
                }
                className="rounded-md bg-gray-800 border border-gray-600 px-2 py-1.5 text-sm text-white w-24 focus:outline-none focus:ring-2 focus:ring-emerald-500 font-mono"
              />
              <span className="text-gray-400 text-sm">°</span>
            </div>
            <div className="flex justify-between text-xs text-gray-600 mt-1">
              <span>0°</span>
              <span>90°</span>
              <span>180°</span>
              <span>270°</span>
              <span>360°</span>
            </div>

            {/* Expected measurement hint */}
            {cfg.phase_offset_deg !== 0 && (
              <p className="text-xs text-emerald-600 mt-1">
                Expected differential phase reading: ~
                {cfg.phase_offset_deg.toFixed(1)}°
                {" "}(or the equivalent in ps on the Live Phase tab)
              </p>
            )}
          </Field>
        </div>
      </div>

      {error && (
        <p className="mt-4 text-red-400 text-sm">Error: {error}</p>
      )}

      <div className="mt-5 flex items-center gap-4">
        <button
          onClick={applyConfig}
          disabled={pending}
          className="px-5 py-2 rounded-lg bg-gray-700 hover:bg-gray-600 text-white text-sm font-semibold disabled:opacity-50 transition"
        >
          {pending ? "Applying…" : isRunning ? "Apply (restarts output)" : "Save Config"}
        </button>
        <span className="text-xs text-gray-600">
          Config is applied immediately; output restarts seamlessly if running.
        </span>
      </div>
    </div>
  );
}

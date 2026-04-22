import { useEffect, useMemo, useRef, useState } from "react";
import uPlot from "uplot";
import "uplot/dist/uPlot.min.css";
import { api, createLiveSocket, type LivePoint, type PhaseZeroState } from "../api/client";

const MAX_POINTS = 3600;
const DEFAULT_MA_WINDOW = 30;
const MIN_MA_WINDOW = 2;
const MAX_MA_WINDOW = 600;
const AXIS_COLOR = "#d1d5db";
const GRID_COLOR = "#374151";
const TICK_COLOR = "#4b5563";

interface Stats { mean: number; std: number; n: number }

function computeStats(arr: number[]): Stats | null {
  if (arr.length === 0) return null;
  const n = arr.length;
  const mean = arr.reduce((s, v) => s + v, 0) / n;
  const variance = arr.reduce((s, v) => s + (v - mean) ** 2, 0) / n;
  return { mean, std: Math.sqrt(variance), n };
}

/** Auto-scale a picosecond value to the most readable unit. */
function fmtTime(ps: number): string {
  const abs = Math.abs(ps);
  if (abs === 0) return "0 ps";
  if (abs < 1e-9) return `${(ps * 1e12).toExponential(3)} as`; // attoseconds
  if (abs < 1e-6) return `${(ps * 1e9).toExponential(3)} zs`;  // zeptoseconds
  if (abs < 1e-3) return `${(ps * 1e6).toExponential(3)} ys`;  // yoctoseconds
  if (abs < 1)    return `${(ps * 1e3).toFixed(3)} fs`;         // femtoseconds
  if (abs < 1e3)  return `${ps.toFixed(4)} ps`;                 // picoseconds
  if (abs < 1e6)  return `${(ps / 1e3).toFixed(3)} ns`;         // nanoseconds
  return `${(ps / 1e6).toFixed(3)} μs`;
}

/** Format for the uPlot axis ticks (compact). */
function fmtTimeAxis(ps: number): string {
  const abs = Math.abs(ps);
  if (abs === 0) return "0";
  if (abs < 1)    return `${(ps * 1e3).toExponential(1)}fs`;
  if (abs < 1e3)  return `${ps.toFixed(3)}ps`;
  if (abs < 1e6)  return `${(ps / 1e3).toFixed(2)}ns`;
  return `${(ps / 1e6).toFixed(2)}μs`;
}

interface PhaseChartProps {
  /** Shared session-start timestamp (ISO). When non-null, Download CSV is
   *  trimmed to rows at/after this moment and the indicator shows it. */
  sessionSince: string | null;
  /** Called when the user clicks Reset — should bump the shared `sessionSince`
   *  to now so downstream charts (Allan) re-window too. */
  onSessionReset: () => void;
}

export function PhaseChart({ sessionSince, onSessionReset }: PhaseChartProps) {
  const containerRef = useRef<HTMLDivElement>(null);
  const plotRef = useRef<uPlot | null>(null);
  const dataRef = useRef<{
    ts: number[];
    ps: number[];
    ps_ma: number[];
    diff_deg: number[];
  }>({
    ts: [], ps: [], ps_ma: [], diff_deg: [],
  });
  const wsRef = useRef<WebSocket | null>(null);
  const [connected, setConnected] = useState(false);
  const [latest, setLatest] = useState<LivePoint | null>(null);
  const [stats, setStats] = useState<Stats | null>(null);
  const [maValue, setMaValue] = useState<number | null>(null);
  const [maWindow, setMaWindow] = useState<number>(DEFAULT_MA_WINDOW);
  const maWindowRef = useRef<number>(DEFAULT_MA_WINDOW);
  const [phaseZero, setPhaseZero] = useState<PhaseZeroState | null>(null);
  const [zeroPending, setZeroPending] = useState(false);
  const [zeroError, setZeroError] = useState<string | null>(null);

  const clampMaWindow = (w: number): number =>
    Math.max(MIN_MA_WINDOW, Math.min(MAX_MA_WINDOW, Math.round(w)));

  const recomputeAllMa = () => {
    const d = dataRef.current;
    const w = Math.max(1, maWindowRef.current);
    const out = new Array<number>(d.ps.length);
    let sum = 0;
    for (let i = 0; i < d.ps.length; i++) {
      sum += d.ps[i];
      if (i >= w) sum -= d.ps[i - w];
      const span = Math.min(i + 1, w);
      out[i] = sum / span;
    }
    d.ps_ma = out;
  };

  const reset = () => {
    const d = dataRef.current;
    d.ts = []; d.ps = []; d.ps_ma = []; d.diff_deg = [];
    plotRef.current?.setData([[], [], [], []]);
    setStats(null);
    setLatest(null);
    setMaValue(null);
    // Bump the shared session boundary so the Allan chart re-windows
    // and CSV downloads (from either chart) trim to now.
    onSessionReset();
  };

  useEffect(() => {
    if (!containerRef.current) return;

    const opts: uPlot.Options = {
      title: "Beat-Note Phase Difference",
      width: containerRef.current.clientWidth || 900,
      height: 320,
      series: [
        {},
        {
          label: "Phase diff (ps, no expansion scaling)",
          stroke: "#22d3ee",
          width: 1.5,
          scale: "ps",
          value: (_u, v) => v == null ? "—" : fmtTimeAxis(v),
        },
        {
          // Moving-average overlay — browser-side only, not logged.
          label: "Phase diff MA (ps)",
          stroke: "#f472b6",
          width: 2,
          scale: "ps",
          value: (_u, v) => v == null ? "—" : fmtTimeAxis(v),
          points: { show: false },
        },
        {
          label: "Phase diff (°, beat-note)",
          stroke: "#fbbf24",
          width: 1.5,
          scale: "deg",
        },
      ],
      axes: [
        {
          label: "Time",
          stroke: AXIS_COLOR,
          grid: { stroke: GRID_COLOR, width: 1 },
          ticks: { stroke: TICK_COLOR },
          font: "11px sans-serif",
          labelFont: "12px sans-serif",
          labelSize: 20,
          values: (_u, vals) =>
            vals.map((v) => new Date(v * 1000).toLocaleTimeString()),
        },
        {
          scale: "ps",
          label: "ps (beat-note)",
          stroke: "#22d3ee",
          grid: { stroke: GRID_COLOR, width: 1 },
          ticks: { stroke: TICK_COLOR },
          font: "11px sans-serif",
          labelFont: "12px sans-serif",
          labelSize: 20,
          size: 90,
          values: (_u, vals) => vals.map(v => fmtTimeAxis(v ?? 0)),
        },
        {
          scale: "deg",
          label: "° (beat-note)",
          stroke: "#fbbf24",
          side: 1,
          grid: { show: false },
          ticks: { stroke: TICK_COLOR },
          font: "11px sans-serif",
          labelFont: "12px sans-serif",
          labelSize: 24,
          size: 80,
        },
      ],
      scales: {
        x: { time: false },
        ps: {},
        deg: {},
      },
    };

    plotRef.current = new uPlot(
      opts,
      [
        dataRef.current.ts,
        dataRef.current.ps,
        dataRef.current.ps_ma,
        dataRef.current.diff_deg,
      ],
      containerRef.current
    );

    const ro = new ResizeObserver(() => {
      if (containerRef.current && plotRef.current) {
        plotRef.current.setSize({ width: containerRef.current.clientWidth, height: 320 });
      }
    });
    ro.observe(containerRef.current);

    return () => {
      ro.disconnect();
      plotRef.current?.destroy();
      plotRef.current = null;
    };
  }, []);

  useEffect(() => {
    api.phaseZero.get().then(setPhaseZero).catch(() => {
      setZeroError("Unable to read phase zero state");
    });

    function connect() {
      const ws = createLiveSocket(
        (point) => {
          setLatest(point);
          const tSec = Date.parse(point.t) / 1000;
          const d = dataRef.current;
          const diffDeg = point.phase_a_deg - point.phase_b_deg;
          d.ts.push(tSec);
          d.ps.push(point.phase_diff_ps);
          d.diff_deg.push(diffDeg);

          // Incrementally extend the trailing moving-average series.
          const w = Math.max(1, maWindowRef.current);
          const n = d.ps.length;
          const start = Math.max(0, n - w);
          let s = 0;
          for (let i = start; i < n; i++) s += d.ps[i];
          const maNow = s / (n - start);
          d.ps_ma.push(maNow);

          if (d.ts.length > MAX_POINTS) {
            d.ts.shift();
            d.ps.shift();
            d.ps_ma.shift();
            d.diff_deg.shift();
          }
          plotRef.current?.setData([d.ts, d.ps, d.ps_ma, d.diff_deg]);
          setStats(computeStats(d.ps));
          setMaValue(maNow);
        },
        () => {
          setConnected(false);
          setTimeout(connect, 3000);
        }
      );
      ws.onopen = () => setConnected(true);
      wsRef.current = ws;
    }
    connect();
    return () => { wsRef.current?.close(); };
  }, []);

  // Recompute the MA series in place whenever the window size changes.
  useEffect(() => {
    maWindowRef.current = maWindow;
    const d = dataRef.current;
    if (d.ps.length === 0) {
      setMaValue(null);
      return;
    }
    recomputeAllMa();
    plotRef.current?.setData([d.ts, d.ps, d.ps_ma, d.diff_deg]);
    setMaValue(d.ps_ma[d.ps_ma.length - 1] ?? null);
  }, [maWindow]);

  const diffDegLatest = latest
    ? (latest.phase_a_deg - latest.phase_b_deg).toFixed(2)
    : null;

  const maEffectiveSpan = useMemo(() => {
    const n = dataRef.current.ps.length;
    return Math.min(maWindow, n);
  }, [maWindow, latest]);

  const setZero = async () => {
    setZeroPending(true);
    setZeroError(null);
    try {
      const state = await api.phaseZero.set();
      setPhaseZero(state);
    } catch (err) {
      setZeroError(err instanceof Error ? err.message : "Failed to set zero");
    } finally {
      setZeroPending(false);
    }
  };

  const clearZero = async () => {
    setZeroPending(true);
    setZeroError(null);
    try {
      const state = await api.phaseZero.clear();
      setPhaseZero(state);
    } catch (err) {
      setZeroError(err instanceof Error ? err.message : "Failed to clear zero");
    } finally {
      setZeroPending(false);
    }
  };

  return (
    <div className="bg-gray-900 rounded-xl p-4 shadow-lg">
      <div className="flex items-center justify-between mb-2 flex-wrap gap-2">
        <h2 className="text-cyan-400 font-semibold text-lg">
          Beat-Note Phase Difference — Real Time
        </h2>
        <div className="flex items-center gap-3 text-sm flex-wrap">
          {latest && (
            <span className="text-gray-400">
              Zero offset:{" "}
              <span className={`font-mono ${phaseZero?.active ? "text-emerald-300" : "text-gray-300"}`}>
                {fmtTime(phaseZero?.phase_zero_offset_ps ?? 0)}
              </span>
            </span>
          )}
          {sessionSince && (
            <span className="text-gray-500 text-xs">
              session from{" "}
              <span className="font-mono text-gray-300">
                {new Date(sessionSince).toLocaleTimeString()}
              </span>
            </span>
          )}
          <button
            onClick={reset}
            className="px-2.5 py-1 rounded bg-gray-700 hover:bg-gray-600 text-xs text-gray-300"
            title="Clear the visible live trace and re-window the Allan chart + downloads from now"
          >
            Reset
          </button>
          <button
            onClick={() =>
              window.open(
                api.exportHistoryUrl(sessionSince ?? undefined),
                "_blank"
              )
            }
            className="px-2.5 py-1 rounded bg-gray-700 hover:bg-gray-600 text-xs text-gray-200"
            title={
              sessionSince
                ? "Download phase history since session start as CSV"
                : "Download full phase time-series history as CSV"
            }
          >
            Download CSV
          </button>
          <button
            onClick={setZero}
            disabled={zeroPending || !connected}
            className="px-2.5 py-1 rounded bg-cyan-700 hover:bg-cyan-600 text-xs text-white disabled:opacity-50"
            title="Set current live phase as zero reference"
          >
            Set Zero
          </button>
          <button
            onClick={clearZero}
            disabled={zeroPending || !phaseZero?.active}
            className="px-2.5 py-1 rounded bg-gray-700 hover:bg-gray-600 text-xs text-gray-200 disabled:opacity-50"
            title="Clear persistent zero offset"
          >
            Clear Zero
          </button>
          <span className={`w-2 h-2 rounded-full ${connected ? "bg-green-400" : "bg-red-500"}`} />
        </div>
      </div>
      {zeroError && <div className="mb-2 text-xs text-red-400">{zeroError}</div>}

      {/* Primary indicators: latest Δt and moving-average Δt */}
      <div className="flex flex-wrap gap-3 mb-3">
        <div className="flex-1 min-w-[200px] bg-gray-800/60 rounded-lg px-4 py-2 border border-gray-700">
          <div className="text-[10px] uppercase tracking-wider text-gray-500">
            Latest Δt
          </div>
          <div className="text-2xl font-mono text-cyan-300 leading-tight">
            {latest ? fmtTime(latest.phase_diff_ps) : "—"}
          </div>
          <div className="text-xs text-gray-500 mt-0.5">
            beat-note phase:{" "}
            <span className="font-mono text-yellow-300">
              {diffDegLatest ?? "—"}°
            </span>
          </div>
        </div>

        <div className="flex-1 min-w-[220px] bg-gray-800/60 rounded-lg px-4 py-2 border border-gray-700">
          <div className="flex items-center justify-between gap-2">
            <div className="text-[10px] uppercase tracking-wider text-gray-500">
              Moving average (Δt)
            </div>
            <label className="flex items-center gap-1 text-[10px] text-gray-400">
              window
              <input
                type="number"
                min={MIN_MA_WINDOW}
                max={MAX_MA_WINDOW}
                step={1}
                value={maWindow}
                onChange={(e) => {
                  const parsed = Number(e.target.value);
                  if (!Number.isFinite(parsed)) return;
                  setMaWindow(clampMaWindow(parsed));
                }}
                className="w-16 bg-gray-900 border border-gray-700 rounded px-1.5 py-0.5 text-xs text-gray-200 font-mono"
                title={`Window size in samples (${MIN_MA_WINDOW}–${MAX_MA_WINDOW})`}
              />
              <span className="text-gray-600">samples</span>
            </label>
          </div>
          <div className="text-2xl font-mono text-pink-300 leading-tight">
            {maValue != null ? fmtTime(maValue) : "—"}
          </div>
          <div className="text-xs text-gray-500 mt-0.5">
            averaged over{" "}
            <span className="font-mono text-gray-300">{maEffectiveSpan}</span>{" "}
            of {maWindow} samples
          </div>
        </div>
      </div>

      {/* Statistics bar */}
      {stats && (
        <div className="flex gap-6 mb-2 text-xs font-mono flex-wrap">
          <span className="text-gray-500">n={stats.n.toLocaleString()}</span>
          <span className="text-gray-400">
            mean: <span className="text-cyan-300">{fmtTime(stats.mean)}</span>
          </span>
          <span className="text-gray-400">
            σ: <span className="text-yellow-300">{fmtTime(stats.std)}</span>
          </span>
          <span className="text-gray-400">
            min: <span className="text-gray-300">
              {dataRef.current.ps.length ? fmtTime(Math.min(...dataRef.current.ps)) : "—"}
            </span>
          </span>
          <span className="text-gray-400">
            max: <span className="text-gray-300">
              {dataRef.current.ps.length ? fmtTime(Math.max(...dataRef.current.ps)) : "—"}
            </span>
          </span>
        </div>
      )}

      {/* Legend */}
      <div className="flex gap-5 mb-1 text-xs text-gray-500 flex-wrap">
        <span><span className="text-cyan-400">■</span> Beat-note time/phase (left, ps; no expansion-factor division)</span>
        <span><span className="text-pink-400">■</span> Moving average (window {maWindow}, left ps) — not logged</span>
        <span><span className="text-yellow-400">■</span> Beat-note phase diff (right, °) — raw IQ</span>
      </div>

      <div ref={containerRef} className="w-full" />
    </div>
  );
}

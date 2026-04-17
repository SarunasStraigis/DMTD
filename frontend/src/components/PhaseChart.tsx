import { useEffect, useRef, useState } from "react";
import uPlot from "uplot";
import "uplot/dist/uPlot.min.css";
import { createLiveSocket, type LivePoint } from "../api/client";

const MAX_POINTS = 3600;
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

export function PhaseChart() {
  const containerRef = useRef<HTMLDivElement>(null);
  const plotRef = useRef<uPlot | null>(null);
  const dataRef = useRef<{ ts: number[]; ps: number[]; diff_deg: number[] }>({
    ts: [], ps: [], diff_deg: [],
  });
  const wsRef = useRef<WebSocket | null>(null);
  const [connected, setConnected] = useState(false);
  const [latest, setLatest] = useState<LivePoint | null>(null);
  const [stats, setStats] = useState<Stats | null>(null);

  const reset = () => {
    const d = dataRef.current;
    d.ts = []; d.ps = []; d.diff_deg = [];
    plotRef.current?.setData([[], [], []]);
    setStats(null);
    setLatest(null);
  };

  useEffect(() => {
    if (!containerRef.current) return;

    const opts: uPlot.Options = {
      title: "Phase Difference",
      width: containerRef.current.clientWidth || 900,
      height: 320,
      series: [
        {},
        {
          label: "Phase diff (ps)",
          stroke: "#22d3ee",
          width: 1.5,
          scale: "ps",
          value: (_u, v) => v == null ? "—" : fmtTimeAxis(v),
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
          label: "ps",
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
      [dataRef.current.ts, dataRef.current.ps, dataRef.current.diff_deg],
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
          if (d.ts.length > MAX_POINTS) {
            d.ts.shift();
            d.ps.shift();
            d.diff_deg.shift();
          }
          plotRef.current?.setData([d.ts, d.ps, d.diff_deg]);
          setStats(computeStats(d.ps));
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

  const diffDegLatest = latest
    ? (latest.phase_a_deg - latest.phase_b_deg).toFixed(2)
    : null;

  return (
    <div className="bg-gray-900 rounded-xl p-4 shadow-lg">
      <div className="flex items-center justify-between mb-2 flex-wrap gap-2">
        <h2 className="text-cyan-400 font-semibold text-lg">
          Phase Difference — Real Time
        </h2>
        <div className="flex items-center gap-3 text-sm flex-wrap">
          {latest && (
            <>
              <span className="text-gray-400">
                Now: <span className="text-white font-mono">{fmtTime(latest.phase_diff_ps)}</span>
              </span>
              <span className="text-gray-400">
                Beat diff:{" "}
                <span className="text-yellow-300 font-mono">{diffDegLatest}°</span>
              </span>
            </>
          )}
          <button onClick={reset}
            className="px-2.5 py-1 rounded bg-gray-700 hover:bg-gray-600 text-xs text-gray-300">
            Reset
          </button>
          <span className={`w-2 h-2 rounded-full ${connected ? "bg-green-400" : "bg-red-500"}`} />
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
      <div className="flex gap-5 mb-1 text-xs text-gray-500">
        <span><span className="text-cyan-400">■</span> Physical phase diff (left, auto-scaled)</span>
        <span><span className="text-yellow-400">■</span> Beat-note phase diff (right, °) — raw IQ, not divided by expansion factor</span>
      </div>

      <div ref={containerRef} className="w-full" />
    </div>
  );
}

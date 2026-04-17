import { useEffect, useRef, useState } from "react";
import uPlot from "uplot";
import "uplot/dist/uPlot.min.css";
import { createLiveSocket, type LivePoint } from "../api/client";

type Mode = "amplitude" | "phase";

const MAX_POINTS = 3600;

const COLORS = {
  a: "#22d3ee",   // cyan  — Input 1 / Left / DUT 1
  b: "#a78bfa",   // violet — Input 2 / Right / DUT 2
};

const AXIS_COLOR = "#d1d5db";
const GRID_COLOR = "#374151";
const TICK_COLOR = "#4b5563";

function buildOptions(
  width: number,
  mode: Mode
): uPlot.Options {
  const isAmp = mode === "amplitude";
  const axisBase = {
    stroke: AXIS_COLOR,
    grid: { stroke: GRID_COLOR, width: 1 },
    ticks: { stroke: TICK_COLOR },
    font: "11px sans-serif",
    labelFont: "12px sans-serif",
    labelSize: 20,
  };
  return {
    title: isAmp
      ? "Channel Amplitude — RMS (normalised)"
      : "Per-Channel Beat-Note Phase (degrees, raw IQ)",
    width,
    height: 300,
    series: [
      {},
      { label: "Ch A — Input 1 (DUT 1)", stroke: COLORS.a, width: 1.5 },
      { label: "Ch B — Input 2 (DUT 2)", stroke: COLORS.b, width: 1.5 },
    ],
    axes: [
      {
        ...axisBase,
        label: "Time",
        values: (_u, vals) =>
          vals.map((v) => new Date(v * 1000).toLocaleTimeString()),
      },
      {
        ...axisBase,
        label: isAmp ? "RMS (0–1)" : "°",
        size: 80,
      },
    ],
    scales: { x: { time: false } },
  };
}

export function ChannelChart() {
  const containerRef = useRef<HTMLDivElement>(null);
  const plotRef = useRef<uPlot | null>(null);
  const [mode, setMode] = useState<Mode>("amplitude");
  const modeRef = useRef<Mode>("amplitude");

  const dataRef = useRef<{
    ts: number[];
    a_amp: number[];
    b_amp: number[];
    a_deg: number[];
    b_deg: number[];
  }>({ ts: [], a_amp: [], b_amp: [], a_deg: [], b_deg: [] });

  const wsRef = useRef<WebSocket | null>(null);
  const [connected, setConnected] = useState(false);
  const [latest, setLatest] = useState<LivePoint | null>(null);

  const reset = () => {
    const d = dataRef.current;
    d.ts = [];
    d.a_amp = [];
    d.b_amp = [];
    d.a_deg = [];
    d.b_deg = [];
    plotRef.current?.setData([[], [], []]);
    setLatest(null);
  };

  // Keep modeRef in sync so the WebSocket callback can read it without stale closure
  useEffect(() => {
    modeRef.current = mode;
  }, [mode]);

  // Create / recreate the uPlot instance whenever mode changes
  useEffect(() => {
    if (!containerRef.current) return;

    plotRef.current?.destroy();
    const d = dataRef.current;
    const yA = mode === "amplitude" ? d.a_amp : d.a_deg;
    const yB = mode === "amplitude" ? d.b_amp : d.b_deg;

    plotRef.current = new uPlot(
      buildOptions(containerRef.current.clientWidth || 900, mode),
      [d.ts, yA, yB],
      containerRef.current
    );

    const ro = new ResizeObserver(() => {
      if (containerRef.current && plotRef.current) {
        plotRef.current.setSize({
          width: containerRef.current.clientWidth,
          height: 300,
        });
      }
    });
    ro.observe(containerRef.current);

    return () => {
      ro.disconnect();
      plotRef.current?.destroy();
      plotRef.current = null;
    };
  }, [mode]);

  // WebSocket connection — connect once, persist across mode changes
  useEffect(() => {
    function connect() {
      const ws = createLiveSocket(
        (point) => {
          setLatest(point);
          const tSec = Date.parse(point.t) / 1000;
          const d = dataRef.current;
          d.ts.push(tSec);
          d.a_amp.push(point.rms_a);
          d.b_amp.push(point.rms_b);
          d.a_deg.push(point.phase_a_deg);
          d.b_deg.push(point.phase_b_deg);
          if (d.ts.length > MAX_POINTS) {
            d.ts.shift();
            d.a_amp.shift();
            d.b_amp.shift();
            d.a_deg.shift();
            d.b_deg.shift();
          }
          // Update the plot with whichever series is currently active
          const yA =
            modeRef.current === "amplitude" ? d.a_amp : d.a_deg;
          const yB =
            modeRef.current === "amplitude" ? d.b_amp : d.b_deg;
          plotRef.current?.setData([d.ts, yA, yB]);
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
    return () => {
      wsRef.current?.close();
    };
  }, []);

  const chA = mode === "amplitude" ? latest?.rms_a : latest?.phase_a_deg;
  const chB = mode === "amplitude" ? latest?.rms_b : latest?.phase_b_deg;
  const unit = mode === "amplitude" ? "" : "°";
  const fmt = (v: number | undefined) =>
    v == null ? "—" : mode === "amplitude" ? v.toExponential(3) : v.toFixed(4);

  return (
    <div className="bg-gray-900 rounded-xl p-4 shadow-lg">
      <div className="flex items-center justify-between mb-3 flex-wrap gap-2">
        <h2 className="text-white font-semibold text-lg">Channel Monitor</h2>

        <div className="flex items-center gap-3 text-sm flex-wrap">
          {/* Live readouts */}
          {latest && (
            <>
              <span style={{ color: COLORS.a }}>
                A:{" "}
                <span className="font-mono text-white">
                  {fmt(chA)}{unit}
                </span>
              </span>
              <span style={{ color: COLORS.b }}>
                B:{" "}
                <span className="font-mono text-white">
                  {fmt(chB)}{unit}
                </span>
              </span>
            </>
          )}

          <button
            onClick={reset}
            className="px-2.5 py-1 rounded bg-gray-700 hover:bg-gray-600 text-xs text-gray-300"
          >
            Reset
          </button>

          {/* Mode toggle */}
          <div className="flex rounded-md overflow-hidden border border-gray-600 text-xs">
            <button
              onClick={() => setMode("amplitude")}
              className={`px-3 py-1 transition ${
                mode === "amplitude"
                  ? "bg-gray-600 text-white"
                  : "bg-gray-800 text-gray-400 hover:text-white"
              }`}
            >
              Amplitude
            </button>
            <button
              onClick={() => setMode("phase")}
              className={`px-3 py-1 transition ${
                mode === "phase"
                  ? "bg-gray-600 text-white"
                  : "bg-gray-800 text-gray-400 hover:text-white"
              }`}
            >
              Phase
            </button>
          </div>

          <span
            className={`w-2 h-2 rounded-full ${connected ? "bg-green-400" : "bg-red-500"}`}
          />
        </div>
      </div>

      {/* Channel labels */}
      <div className="flex gap-6 mb-2 text-xs text-gray-500">
        <span>
          <span style={{ color: COLORS.a }}>■</span> Input 1 / Left — DUT 1
        </span>
        <span>
          <span style={{ color: COLORS.b }}>■</span> Input 2 / Right — DUT 2
        </span>
        {mode === "amplitude" ? (
          <span className="ml-auto text-gray-600">
            RMS normalised to audio full-scale (0 = silence, 1 = clipping)
          </span>
        ) : (
          <span className="ml-auto text-gray-600">
            Raw 1 kHz beat-note IQ phase — 180° here = 180°/expansion_factor physical oscillator phase
          </span>
        )}
      </div>

      <div ref={containerRef} className="w-full" />
    </div>
  );
}

import { useEffect, useRef, useState } from "react";
import uPlot from "uplot";
import "uplot/dist/uPlot.min.css";
import { api } from "../api/client";

const AXIS_COLOR = "#d1d5db";
const GRID_COLOR = "#374151";
const TICK_COLOR = "#4b5563";

export function WaveformSnapshot() {
  const containerRef = useRef<HTMLDivElement>(null);
  const plotRef = useRef<uPlot | null>(null);
  const autoRef = useRef(false);
  const intervalRef = useRef<ReturnType<typeof setInterval> | null>(null);

  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [autoRefresh, setAutoRefresh] = useState(false);
  const [lastCapture, setLastCapture] = useState<string | null>(null);
  const [info, setInfo] = useState<{ sr: number; n: number } | null>(null);

  // Build or rebuild uPlot whenever the container mounts
  useEffect(() => {
    if (!containerRef.current) return;
    const width = containerRef.current.clientWidth || 900;

    const opts: uPlot.Options = {
      title: "Waveform Snapshot",
      width,
      height: 220,
      series: [
        {},
        { label: "Ch A (Input 1)", stroke: "#22d3ee", width: 1.5 },
        { label: "Ch B (Input 2)", stroke: "#a78bfa", width: 1.5 },
      ],
      axes: [
        {
          label: "Time (ms)",
          stroke: AXIS_COLOR,
          grid: { stroke: GRID_COLOR, width: 1 },
          ticks: { stroke: TICK_COLOR },
          font: "11px sans-serif",
          labelFont: "12px sans-serif",
          labelSize: 20,
        },
        {
          label: "Amplitude",
          stroke: AXIS_COLOR,
          grid: { stroke: GRID_COLOR, width: 1 },
          ticks: { stroke: TICK_COLOR },
          font: "11px sans-serif",
          labelFont: "12px sans-serif",
          labelSize: 20,
          size: 70,
        },
      ],
      scales: { x: { time: false } },
      legend: { show: true },
    };

    plotRef.current = new uPlot(opts, [[], [], []], containerRef.current);

    const ro = new ResizeObserver(() => {
      if (containerRef.current && plotRef.current) {
        plotRef.current.setSize({ width: containerRef.current.clientWidth, height: 220 });
      }
    });
    ro.observe(containerRef.current);

    return () => {
      ro.disconnect();
      plotRef.current?.destroy();
      plotRef.current = null;
    };
  }, []);

  const capture = async () => {
    setLoading(true);
    setError(null);
    try {
      const snap = await api.getSnapshot();
      const n = snap.ch_a.length;
      const msPerSample = 1000 / snap.sample_rate;
      const t = Array.from({ length: n }, (_, i) => i * msPerSample);
      plotRef.current?.setData([t, snap.ch_a, snap.ch_b]);
      setInfo({ sr: snap.sample_rate, n });
      setLastCapture(new Date().toLocaleTimeString());
    } catch (e) {
      setError(String(e));
    } finally {
      setLoading(false);
    }
  };

  // Auto-refresh toggle
  useEffect(() => {
    autoRef.current = autoRefresh;
    if (autoRefresh) {
      capture(); // immediate first capture
      intervalRef.current = setInterval(capture, 2000);
    } else {
      if (intervalRef.current) {
        clearInterval(intervalRef.current);
        intervalRef.current = null;
      }
    }
    return () => {
      if (intervalRef.current) clearInterval(intervalRef.current);
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [autoRefresh]);

  return (
    <div className="bg-gray-900 rounded-xl p-4 shadow-lg">
      <div className="flex items-center justify-between mb-3 flex-wrap gap-2">
        <h2 className="text-white font-semibold text-lg">Waveform Snapshot</h2>
        <div className="flex items-center gap-3 text-sm flex-wrap">
          {info && (
            <span className="text-gray-500 text-xs font-mono">
              {info.n} samples · {info.sr.toLocaleString()} Hz ·{" "}
              {((info.n / info.sr) * 1000).toFixed(1)} ms
            </span>
          )}
          {lastCapture && (
            <span className="text-gray-600 text-xs">at {lastCapture}</span>
          )}

          {/* Auto-refresh toggle */}
          <label className="flex items-center gap-1.5 text-xs text-gray-400 cursor-pointer select-none">
            <input
              type="checkbox"
              checked={autoRefresh}
              onChange={(e) => setAutoRefresh(e.target.checked)}
              className="accent-cyan-500"
            />
            Auto (2s)
          </label>

          <button
            onClick={capture}
            disabled={loading || autoRefresh}
            className="px-3 py-1 rounded bg-gray-700 hover:bg-gray-600 text-white text-xs disabled:opacity-50"
          >
            {loading ? "Capturing…" : "Capture"}
          </button>
        </div>
      </div>

      {error && (
        <p className="text-red-400 text-xs mb-2">
          {error.includes("409") ? "Start capture first to take a snapshot." : error}
        </p>
      )}

      <div className="flex gap-4 mb-2 text-xs text-gray-500">
        <span><span className="text-cyan-400">■</span> Input 1 / Ch A — DUT 1</span>
        <span><span className="text-violet-400">■</span> Input 2 / Ch B — DUT 2</span>
        <span className="ml-auto text-gray-600">
          Y axis: normalised amplitude (±1 = full scale)
        </span>
      </div>

      <div ref={containerRef} className="w-full" />
    </div>
  );
}

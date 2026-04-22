import { useCallback, useEffect, useState } from "react";
import {
  LineChart,
  Line,
  XAxis,
  YAxis,
  CartesianGrid,
  Tooltip,
  ResponsiveContainer,
  Legend,
} from "recharts";
import { api, type HistoryPoint } from "../api/client";

interface AllanPoint {
  tau: number;
  adev: number;
}

/**
 * Overlapping Allan Deviation (OADEV) estimator.
 * Input: array of phase values in seconds (converted from ps).
 * tauStep: the measurement interval in seconds (one block duration).
 */
function computeAllanDev(
  phaseSeconds: number[],
  tau0: number
): AllanPoint[] {
  const n = phaseSeconds.length;
  if (n < 3) return [];

  const result: AllanPoint[] = [];
  const maxM = Math.floor(n / 3);

  for (let m = 1; m <= maxM; m *= 2) {
    const tau = m * tau0;
    let sum = 0;
    let count = 0;
    for (let i = 0; i + 2 * m < n; i++) {
      const diff =
        phaseSeconds[i + 2 * m] - 2 * phaseSeconds[i + m] + phaseSeconds[i];
      sum += diff * diff;
      count++;
    }
    if (count === 0) continue;
    const adev = Math.sqrt(sum / (2 * count * tau * tau));
    result.push({ tau, adev });
  }
  return result;
}

const formatTau = (v: number) => {
  if (v >= 1) return `${v.toFixed(0)}s`;
  return `${(v * 1000).toFixed(0)}ms`;
};

const formatAdev = (v: number) => {
  if (v < 1e-12) return `${(v * 1e15).toFixed(2)}fs`;
  if (v < 1e-9) return `${(v * 1e12).toFixed(2)}ps`;
  return `${(v * 1e9).toFixed(2)}ns`;
};

interface AllanChartProps {
  tau0: number;
  /** Shared session-start ISO timestamp. Controlled by the Live Phase tab's
   *  Reset button; when set, history is trimmed to rows at/after this moment. */
  sessionSince: string | null;
}

export function AllanChart({ tau0, sessionSince }: AllanChartProps) {
  const [allanData, setAllanData] = useState<AllanPoint[]>([]);
  const [loading, setLoading] = useState(false);
  const [lastFetch, setLastFetch] = useState<string>("");

  const refresh = useCallback(async () => {
    setLoading(true);
    try {
      const history: HistoryPoint[] = await api.getHistory(
        10000,
        sessionSince ?? undefined
      );
      if (history.length < 3) {
        setAllanData([]);
        return;
      }
      // phase_ps → seconds
      const phaseSeconds = history.map((p) => p.phase_ps * 1e-12);
      const points = computeAllanDev(phaseSeconds, tau0);
      setAllanData(points);
      setLastFetch(new Date().toLocaleTimeString());
    } catch {
      // ignore fetch errors
    } finally {
      setLoading(false);
    }
  }, [tau0, sessionSince]);

  useEffect(() => {
    refresh();
    const id = setInterval(refresh, 60_000);
    return () => clearInterval(id);
  }, [refresh]);

  return (
    <div className="bg-gray-900 rounded-xl p-4 shadow-lg">
      <div className="flex items-center justify-between mb-3 flex-wrap gap-2">
        <h2 className="text-violet-400 font-semibold text-lg">
          Allan Deviation (OADEV, DUT time error)
        </h2>
        <div className="flex items-center gap-3 text-sm text-gray-400 flex-wrap">
          {sessionSince && (
            <span className="text-gray-500 text-xs">
              since{" "}
              <span className="font-mono text-gray-300">
                {new Date(sessionSince).toLocaleTimeString()}
              </span>
            </span>
          )}
          {lastFetch && <span>Last: {lastFetch}</span>}
          <button
            onClick={refresh}
            disabled={loading}
            className="px-3 py-1 rounded bg-violet-700 hover:bg-violet-600 text-white text-xs disabled:opacity-50"
            title="Force a recompute now (auto-refreshes every 60 s)"
          >
            {loading ? "Loading…" : "Refresh"}
          </button>
        </div>
      </div>

      {allanData.length === 0 ? (
        <div className="h-64 flex items-center justify-center text-gray-500 text-sm">
          {loading
            ? "Computing…"
            : "Not enough history data (need ≥ 3 blocks)"}
        </div>
      ) : (
        <ResponsiveContainer width="100%" height={280}>
          <LineChart
            data={allanData}
            margin={{ top: 4, right: 20, bottom: 20, left: 20 }}
          >
            <CartesianGrid strokeDasharray="3 3" stroke="#374151" />
            <XAxis
              dataKey="tau"
              tickFormatter={formatTau}
              label={{
                value: "Averaging time τ",
                position: "insideBottom",
                offset: -10,
                fill: "#d1d5db",
              }}
              tick={{ fill: "#d1d5db", fontSize: 11 }}
              scale="log"
              type="number"
              domain={["auto", "auto"]}
            />
            <YAxis
              tickFormatter={formatAdev}
              label={{
                value: "σy(τ)",
                angle: -90,
                position: "insideLeft",
                fill: "#d1d5db",
              }}
              tick={{ fill: "#d1d5db", fontSize: 11 }}
              scale="log"
              type="number"
              domain={["auto", "auto"]}
            />
            <Tooltip
              formatter={(v) => formatAdev(Number(v))}
              labelFormatter={(v) => `τ = ${formatTau(Number(v))}`}
              contentStyle={{
                background: "#1f2937",
                border: "1px solid #374151",
                color: "#f9fafb",
              }}
            />
            <Legend wrapperStyle={{ color: "#d1d5db" }} />
            <Line
              type="monotone"
              dataKey="adev"
              name="OADEV"
              stroke="#a78bfa"
              dot={{ r: 3 }}
              strokeWidth={2}
            />
          </LineChart>
        </ResponsiveContainer>
      )}
    </div>
  );
}

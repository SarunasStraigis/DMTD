import { useCallback, useEffect, useState } from "react";
import { AllanChart } from "./components/AllanChart";
import { ChannelChart } from "./components/ChannelChart";
import { ConfigPanel } from "./components/ConfigPanel";
import { PhaseChart } from "./components/PhaseChart";
import { SigGenPanel } from "./components/SigGenPanel";
import { WaveformSnapshot } from "./components/WaveformSnapshot";
import { api, type StatusResponse } from "./api/client";

type Tab = "live" | "channels" | "siggen" | "config";

export default function App() {
  const [tab, setTab] = useState<Tab>("live");
  const [status, setStatus] = useState<StatusResponse | null>(null);
  const [actionPending, setActionPending] = useState(false);
  // Shared "session start" timestamp — a Reset on the Live tab bumps this and
  // it becomes the `since` filter for both the Allan chart and all CSV downloads.
  const [sessionSince, setSessionSince] = useState<string | null>(null);
  const resetSession = useCallback(() => {
    setSessionSince(new Date().toISOString());
  }, []);

  const fetchStatus = useCallback(async () => {
    try {
      setStatus(await api.getStatus());
    } catch {
      // backend may not be up yet
    }
  }, []);

  useEffect(() => {
    fetchStatus();
    const id = setInterval(fetchStatus, 5000);
    return () => clearInterval(id);
  }, [fetchStatus]);

  const toggleCapture = async () => {
    if (!status) return;
    setActionPending(true);
    try {
      if (status.running) {
        await api.stop();
      } else {
        await api.start();
      }
      await fetchStatus();
    } catch (e) {
      alert(String(e));
    } finally {
      setActionPending(false);
    }
  };

  const blockDurationSec = status
    ? status.block_size / status.sample_rate
    : null;

  return (
    <div className="min-h-screen bg-gray-950 text-gray-100 flex flex-col">
      {/* Header */}
      <header className="border-b border-gray-800 px-6 py-3 flex items-center justify-between">
        <div className="flex items-center gap-3">
          <span className="text-cyan-400 font-bold text-xl tracking-tight">
            DMTD Analyser
          </span>
          <span className="text-gray-600 text-xs">
            Dual Mixer Time Difference
          </span>
        </div>

        <div className="flex items-center gap-4 text-sm">
          {status && (
            <>
              <span className="text-gray-500">
                {status.sample_rate.toLocaleString()} Hz ·{" "}
                {blockDurationSec?.toFixed(1)}s blocks
              </span>
              <span className="text-gray-500">
                ×{status.expansion_factor.toLocaleString()}
              </span>
              <span className="text-gray-500">
                WS clients: {status.ws_clients}
              </span>
            </>
          )}

          <button
            onClick={toggleCapture}
            disabled={actionPending || !status}
            className={`px-4 py-1.5 rounded-lg text-sm font-semibold transition ${
              status?.running
                ? "bg-red-700 hover:bg-red-600 text-white"
                : "bg-green-700 hover:bg-green-600 text-white"
            } disabled:opacity-50`}
          >
            {actionPending
              ? "…"
              : status?.running
                ? "Stop"
                : "Start"}
          </button>

          <span
            className={`w-2.5 h-2.5 rounded-full ${status?.running ? "bg-green-400 animate-pulse" : "bg-gray-600"}`}
          />
        </div>
      </header>

      {/* Tab bar */}
      <nav className="border-b border-gray-800 px-6 flex gap-1">
        {(
          [
            { id: "live",     label: "Live Phase" },
            { id: "channels", label: "Channels" },
            { id: "siggen",   label: "Signal Gen" },
            { id: "config",   label: "Configuration" },
          ] as { id: Tab; label: string }[]
        ).map(({ id, label }) => (
          <button
            key={id}
            onClick={() => setTab(id)}
            className={`px-4 py-2.5 text-sm font-medium border-b-2 transition ${
              tab === id
                ? "border-cyan-400 text-cyan-400"
                : "border-transparent text-gray-400 hover:text-gray-200"
            }`}
          >
            {label}
          </button>
        ))}
      </nav>

      {/* Content — all panels stay mounted so WebSocket buffers persist across tab switches */}
      <main className="flex-1 px-6 py-5">
        <div className={tab === "live"     ? "flex flex-col gap-5" : "hidden"}>
          <PhaseChart sessionSince={sessionSince} onSessionReset={resetSession} />
          <AllanChart tau0={blockDurationSec ?? 1} sessionSince={sessionSince} />
        </div>
        <div className={tab === "channels" ? "flex flex-col gap-5" : "hidden"}>
          <ChannelChart />
          <WaveformSnapshot />
        </div>
        <div className={tab === "siggen"   ? "flex flex-col gap-5" : "hidden"}><SigGenPanel /></div>
        <div className={tab === "config"   ? "flex flex-col gap-5" : "hidden"}><ConfigPanel /></div>
      </main>

      {/* Footer */}
      <footer className="border-t border-gray-800 px-6 py-2 text-xs text-gray-600 flex justify-between">
        <span>
          Backend:{" "}
          <a
            href="/docs"
            target="_blank"
            className="text-cyan-700 hover:underline"
          >
            REST API Docs
          </a>
        </span>
        <span>DMTD Phase Analyser</span>
      </footer>
    </div>
  );
}

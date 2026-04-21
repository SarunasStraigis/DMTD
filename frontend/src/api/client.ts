export interface AppConfig {
  device_index: number | null;
  sample_rate: 44100 | 48000 | 96000 | 192000;
  block_size: number;
  beat_frequency: number;
  freq_estimator: "fft_peak" | "fixed";
  demod_mode: "block_iq" | "block_iq_fir" | "pll_tracker";
  freq_source: "ch_a" | "avg_ab";
  iq_lpf_cutoff_hz: number;
  iq_lpf_order: number;
  iq_min_mag: number;
  pll_kp: number;
  pll_ki: number;
  pll_min_mag: number;
  ref_frequency: number;
  history_retention_days: number;
  phase_zero_offset_rad: number;
  phase_zero_offset_ps: number;
}

/** Derived — not stored in config.json */
export function computedExpansionFactor(cfg: AppConfig): number {
  return cfg.ref_frequency / cfg.beat_frequency;
}

export interface StatusResponse {
  running: boolean;
  ws_clients: number;
  sample_rate: number;
  block_size: number;
  device_index: number | null;
  beat_frequency: number;
  expansion_factor: number; // computed by backend: ref_frequency / beat_frequency
  phase_zero_offset_rad: number;
  phase_zero_offset_ps: number;
}

export interface DeviceInfo {
  index: number;
  name: string;
  max_input_channels: number;
  default_samplerate: number;
}

export interface OutputDeviceInfo {
  index: number;
  name: string;
  max_output_channels: number;
  default_samplerate: number;
  stereo: boolean;
}

export interface SigGenConfig {
  output_device_index: number | null;
  frequency: number;
  amplitude: number;
  phase_offset_deg: number;
  sample_rate: number;
}

export interface SigGenStatus extends SigGenConfig {
  running: boolean;
}

export interface HistoryPoint {
  ts: string;
  // Beat-note domain differential phase/time (no expansion-factor division).
  phase_rad: number;
  phase_ps: number;
  beat_freq: number;
}

export interface LivePoint {
  t: string;
  // Beat-note domain differential phase/time (no expansion-factor division).
  phase_diff_rad: number;
  phase_diff_ps: number;
  beat_freq: number;
  phase_a_ps: number;
  phase_b_ps: number;
  phase_a_deg: number;
  phase_b_deg: number;
  rms_a: number;
  rms_b: number;
}

export interface PhaseZeroState {
  phase_zero_offset_rad: number;
  phase_zero_offset_ps: number;
  active: boolean;
}

const BASE = "/api";

async function request<T>(
  path: string,
  options: RequestInit = {}
): Promise<T> {
  const res = await fetch(`${BASE}${path}`, {
    headers: { "Content-Type": "application/json" },
    ...options,
  });
  if (!res.ok) {
    const detail = await res.text();
    throw new Error(`${res.status}: ${detail}`);
  }
  return res.json() as Promise<T>;
}

export const api = {
  getConfig: () => request<AppConfig>("/config"),
  putConfig: (cfg: AppConfig) =>
    request<AppConfig>("/config", { method: "PUT", body: JSON.stringify(cfg) }),
  getStatus: () => request<StatusResponse>("/status"),
  getDevices: () => request<DeviceInfo[]>("/devices"),
  start: () => request<{ status: string }>("/control/start", { method: "POST" }),
  stop: () => request<{ status: string }>("/control/stop", { method: "POST" }),
  getHistory: (limit = 10000, since?: string) => {
    const params = new URLSearchParams({ limit: String(limit) });
    if (since) params.set("since", since);
    return request<HistoryPoint[]>(`/history?${params}`);
  },

  getSnapshot: () =>
    request<{
      sample_rate: number;
      beat_frequency_hz: number | null;
      ch_a: number[];
      ch_b: number[];
    }>("/snapshot"),
  phaseZero: {
    get: () => request<PhaseZeroState>("/phase/zero"),
    set: () => request<PhaseZeroState & { status: string }>("/phase/zero/set", { method: "POST" }),
    clear: () => request<PhaseZeroState & { status: string }>("/phase/zero/clear", { method: "POST" }),
  },

  siggen: {
    getDevices: () => request<OutputDeviceInfo[]>("/siggen/devices"),
    getStatus: () => request<SigGenStatus>("/siggen/status"),
    putConfig: (cfg: SigGenConfig) =>
      request<SigGenConfig>("/siggen/config", {
        method: "PUT",
        body: JSON.stringify(cfg),
      }),
    start: (cfg?: SigGenConfig) =>
      request<{ status: string }>("/siggen/start", {
        method: "POST",
        body: cfg ? JSON.stringify(cfg) : undefined,
      }),
    stop: () => request<{ status: string }>("/siggen/stop", { method: "POST" }),
  },
};

export function createLiveSocket(
  onMessage: (data: LivePoint) => void,
  onClose?: () => void
): WebSocket {
  const protocol = location.protocol === "https:" ? "wss:" : "ws:";
  const ws = new WebSocket(`${protocol}//${location.host}/ws/live`);
  ws.onmessage = (ev) => {
    try {
      onMessage(JSON.parse(ev.data) as LivePoint);
    } catch {
      // ignore parse errors
    }
  };
  ws.onclose = () => onClose?.();
  return ws;
}

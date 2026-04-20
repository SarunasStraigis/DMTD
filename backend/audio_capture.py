"""
Audio capture using sounddevice.
Runs a callback-based InputStream and pushes blocks onto an asyncio queue
that the DSP coroutine drains.
"""

from __future__ import annotations

import asyncio
import threading
from datetime import datetime, timezone
from typing import Any

import numpy as np
import sounddevice as sd

from .config import AppConfig
from .dsp import DMTDProcessor
from .history import enqueue_row
from .ws_manager import manager as ws_manager


SNAPSHOT_SAMPLES = 4096  # ~21 ms at 192 kSPS — enough for ~20 cycles of 1 kHz


class AudioCapture:
    def __init__(self) -> None:
        self._stream: sd.InputStream | None = None
        self._processor: DMTDProcessor | None = None
        self._block_queue: asyncio.Queue[np.ndarray] = asyncio.Queue(maxsize=64)
        self._loop: asyncio.AbstractEventLoop | None = None
        self._worker_task: asyncio.Task | None = None
        self.running = False
        self._sample_rate: int = 192000
        self._phase_zero_offset_rad: float = 0.0
        self._phase_zero_offset_ps: float = 0.0
        self._latest_raw_phase_rad: float | None = None
        self._latest_raw_phase_ps: float | None = None
        # Ring buffer for waveform snapshot — always holds latest SNAPSHOT_SAMPLES frames
        self._snapshot: np.ndarray = np.zeros((SNAPSHOT_SAMPLES, 2), dtype=np.float32)
        self._snapshot_lock = threading.Lock()

    def start(self, cfg: AppConfig, loop: asyncio.AbstractEventLoop) -> None:
        if self.running:
            return
        self._loop = loop
        self._block_queue = asyncio.Queue(maxsize=64)
        self._processor = DMTDProcessor(
            sample_rate=cfg.sample_rate,
            beat_frequency=cfg.beat_frequency,
            expansion_factor=cfg.expansion_factor,   # computed: ref_frequency / beat_frequency
            ref_frequency=cfg.ref_frequency,
            freq_estimator=cfg.freq_estimator,
        )
        self._processor.reset()
        self.set_phase_zero_offset(cfg.phase_zero_offset_rad, cfg.phase_zero_offset_ps)

        self._stream = sd.InputStream(
            device=cfg.device_index,
            channels=2,
            samplerate=cfg.sample_rate,
            blocksize=cfg.block_size,
            dtype="float32",
            callback=self._sd_callback,
            latency="low",
        )
        self._sample_rate = cfg.sample_rate
        self._worker_task = loop.create_task(self._dsp_worker(cfg))
        self._stream.start()
        self.running = True

    def stop(self) -> None:
        if not self.running:
            return
        if self._stream:
            self._stream.stop()
            self._stream.close()
            self._stream = None
        if self._worker_task:
            self._worker_task.cancel()
            self._worker_task = None
        self._processor = None
        self._latest_raw_phase_rad = None
        self._latest_raw_phase_ps = None
        self.running = False

    def get_snapshot(self) -> tuple[np.ndarray, int]:
        """Return a copy of the latest waveform snapshot and the sample rate."""
        with self._snapshot_lock:
            return self._snapshot.copy(), self._sample_rate

    def set_phase_zero_offset(self, offset_rad: float, offset_ps: float) -> None:
        self._phase_zero_offset_rad = float(offset_rad)
        self._phase_zero_offset_ps = float(offset_ps)

    def get_phase_zero_offset(self) -> tuple[float, float]:
        return self._phase_zero_offset_rad, self._phase_zero_offset_ps

    def get_latest_raw_phase(self) -> tuple[float, float] | None:
        if self._latest_raw_phase_rad is None or self._latest_raw_phase_ps is None:
            return None
        return self._latest_raw_phase_rad, self._latest_raw_phase_ps

    def _sd_callback(
        self,
        indata: np.ndarray,
        frames: int,
        time: Any,
        status: sd.CallbackFlags,
    ) -> None:
        """Called from sounddevice's audio thread — must be non-blocking."""
        if self._loop is None:
            return
        block = indata.copy()
        # Update snapshot ring — keep latest SNAPSHOT_SAMPLES frames
        with self._snapshot_lock:
            n = min(frames, SNAPSHOT_SAMPLES)
            self._snapshot = np.roll(self._snapshot, -n, axis=0)
            self._snapshot[-n:] = block[-n:]
        try:
            self._loop.call_soon_threadsafe(self._block_queue.put_nowait, block)
        except asyncio.QueueFull:
            pass  # drop block if consumer is too slow

    async def _dsp_worker(self, cfg: AppConfig) -> None:
        proc = self._processor
        while True:
            try:
                block = await self._block_queue.get()
            except asyncio.CancelledError:
                break

            if proc is None:
                continue

            try:
                block_f64 = block.astype(np.float64)
                (
                    phase_rad_raw, phase_ps_raw, beat_freq,
                    phase_a_ps, phase_b_ps,
                    phase_a_deg, phase_b_deg,
                    rms_a, rms_b,
                ) = proc.process_block(block_f64)
            except Exception:
                continue

            self._latest_raw_phase_rad = phase_rad_raw
            self._latest_raw_phase_ps = phase_ps_raw
            phase_rad = phase_rad_raw + self._phase_zero_offset_rad
            phase_ps = phase_ps_raw + self._phase_zero_offset_ps

            ts = datetime.now(timezone.utc).isoformat()
            enqueue_row(ts, phase_rad, phase_ps, beat_freq)

            payload = {
                "t": ts,
                "phase_diff_rad": phase_rad,
                "phase_diff_ps": phase_ps,
                "beat_freq": beat_freq,
                "phase_a_ps": phase_a_ps,
                "phase_b_ps": phase_b_ps,
                "phase_a_deg": phase_a_deg,
                "phase_b_deg": phase_b_deg,
                "rms_a": rms_a,
                "rms_b": rms_b,
            }
            await ws_manager.broadcast(payload)


def list_input_devices() -> list[dict]:
    """Return all available input devices as a list of dicts."""
    devices = sd.query_devices()
    result = []
    for i, dev in enumerate(devices):
        if dev["max_input_channels"] >= 2:
            result.append(
                {
                    "index": i,
                    "name": dev["name"],
                    "max_input_channels": dev["max_input_channels"],
                    "default_samplerate": dev["default_samplerate"],
                }
            )
    return result


capture = AudioCapture()

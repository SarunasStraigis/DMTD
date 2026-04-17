"""
Stereo sine-wave signal generator using sounddevice.OutputStream.

Outputs two continuous sine waves on L/R channels with a configurable
phase offset between them. Intended for self-test: route the audio
outputs back into the inputs to verify the DMTD phase measurement.

  Ch L (out 0): amplitude * sin(2π·f·t)
  Ch R (out 1): amplitude * sin(2π·f·t + phase_offset_rad)
"""

from __future__ import annotations

import math
from typing import Any

import numpy as np
import sounddevice as sd
from pydantic import BaseModel, Field


class SigGenConfig(BaseModel):
    output_device_index: int | None = Field(
        default=None, description="Output device index (None = system default)"
    )
    frequency: float = Field(
        default=1000.0, gt=0, description="Sine wave frequency in Hz"
    )
    amplitude: float = Field(
        default=0.5, ge=0.0, le=1.0, description="Output amplitude 0–1 (1 = full scale)"
    )
    phase_offset_deg: float = Field(
        default=0.0,
        ge=0.0,
        le=360.0,
        description="Phase offset of Ch R relative to Ch L in degrees",
    )
    sample_rate: int = Field(
        default=48000, description="Output sample rate in Hz"
    )


class SignalGenerator:
    def __init__(self) -> None:
        self._stream: sd.OutputStream | None = None
        self._sample_pos: int = 0
        self._cfg: SigGenConfig = SigGenConfig()
        self.running: bool = False

    # ------------------------------------------------------------------
    # Public interface
    # ------------------------------------------------------------------

    def start(self, cfg: SigGenConfig) -> None:
        if self.running:
            self.stop()
        self._cfg = cfg
        self._sample_pos = 0

        self._stream = sd.OutputStream(
            device=cfg.output_device_index,
            channels=2,
            samplerate=cfg.sample_rate,
            dtype="float32",
            callback=self._callback,
            latency="low",
        )
        self._stream.start()
        self.running = True

    def stop(self) -> None:
        if not self.running:
            return
        if self._stream:
            self._stream.stop()
            self._stream.close()
            self._stream = None
        self.running = False

    @property
    def config(self) -> SigGenConfig:
        return self._cfg

    # ------------------------------------------------------------------
    # sounddevice callback (audio thread — must be non-blocking)
    # ------------------------------------------------------------------

    def _callback(
        self,
        outdata: np.ndarray,
        frames: int,
        time: Any,
        status: sd.CallbackFlags,
    ) -> None:
        cfg = self._cfg
        t = (np.arange(frames, dtype=np.float64) + self._sample_pos) / cfg.sample_rate
        phase_L = 2.0 * math.pi * cfg.frequency * t
        phase_offset_rad = math.radians(cfg.phase_offset_deg)

        outdata[:, 0] = (cfg.amplitude * np.sin(phase_L)).astype(np.float32)
        outdata[:, 1] = (cfg.amplitude * np.sin(phase_L + phase_offset_rad)).astype(
            np.float32
        )
        self._sample_pos += frames


def list_output_devices() -> list[dict]:
    """Return all output-capable devices (at least 2 output channels preferred)."""
    devices = sd.query_devices()
    result = []
    for i, dev in enumerate(devices):
        if dev["max_output_channels"] >= 1:
            result.append(
                {
                    "index": i,
                    "name": dev["name"],
                    "max_output_channels": dev["max_output_channels"],
                    "default_samplerate": dev["default_samplerate"],
                    "stereo": dev["max_output_channels"] >= 2,
                }
            )
    return result


# Module-level singleton
generator = SignalGenerator()

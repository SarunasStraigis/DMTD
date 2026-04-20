"""
Configuration model and persistence.
Settings are stored in config.json at the project root.
"""

import json
import os
from pathlib import Path
from typing import Literal

from pydantic import BaseModel, Field

CONFIG_PATH = Path(__file__).parent.parent / "config.json"

_DEFAULTS = {
    "device_index": None,
    "sample_rate": 192000,
    "block_size": 192000,
    "beat_frequency": 1000.0,
    "freq_estimator": "fft_peak",
    "ref_frequency": 90_000_000,
    "history_retention_days": 30,
    "phase_zero_offset_rad": 0.0,
    "phase_zero_offset_ps": 0.0,
}


class AppConfig(BaseModel):
    device_index: int | None = Field(
        default=None, description="sounddevice input device index (None = system default)"
    )
    sample_rate: Literal[44100, 48000, 96000, 192000] = Field(
        default=192000, description="Audio capture sample rate in Hz"
    )
    block_size: int = Field(
        default=192000,
        ge=1024,
        le=1_920_000,
        description="Samples per DSP block (noise-vs-time-resolution tradeoff)",
    )
    beat_frequency: float = Field(
        default=1000.0, gt=0, description="IF beat-note frequency in Hz (f_osc - f_lo)"
    )
    freq_estimator: Literal["fft_peak", "fixed"] = Field(
        default="fft_peak",
        description="Method to estimate actual beat frequency each block",
    )
    ref_frequency: float = Field(
        default=90_000_000, gt=0, description="Oscillator frequency in Hz (both DUTs)"
    )
    history_retention_days: int = Field(
        default=30, ge=1, description="How many days of phase history to keep in SQLite"
    )
    phase_zero_offset_rad: float = Field(
        default=0.0,
        description="Persistent differential phase zero offset in radians (beat-note domain)",
    )
    phase_zero_offset_ps: float = Field(
        default=0.0,
        description="Persistent differential phase zero offset in picoseconds (beat-note domain)",
    )

    @property
    def expansion_factor(self) -> float:
        """Derived: f_osc / f_beat — the DMTD phase stretching ratio."""
        return self.ref_frequency / self.beat_frequency


def load_config() -> AppConfig:
    if CONFIG_PATH.exists():
        try:
            raw = json.loads(CONFIG_PATH.read_text(encoding="utf-8"))
            return AppConfig(**{**_DEFAULTS, **raw})
        except Exception:
            pass
    return AppConfig()


def save_config(cfg: AppConfig) -> None:
    CONFIG_PATH.write_text(
        json.dumps(cfg.model_dump(), indent=2), encoding="utf-8"
    )

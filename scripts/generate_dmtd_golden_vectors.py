#!/usr/bin/env python3
"""Generate golden-vector expected outputs from backend/dsp.py for Dmtd.Core.Tests."""

from __future__ import annotations

import json
import math
import sys
from pathlib import Path

import numpy as np

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "backend"))

from dsp import DMTDProcessor  # noqa: E402


def make_stereo_block(
    *,
    sample_rate: int,
    n: int,
    block_index: int,
    beat_hz: float,
    b_offset_rad: float,
    b_drift_rad_per_block: float,
    amp: float = 0.45,
) -> np.ndarray:
    """Deterministic stereo block shared with the C# test signal generator."""
    start = block_index * n
    i = np.arange(n, dtype=np.float64) + start
    t = i / sample_rate
    phase_a = 2.0 * math.pi * beat_hz * t
    b_offset = b_offset_rad + block_index * b_drift_rad_per_block
    phase_b = phase_a + b_offset
    block = np.empty((n, 2), dtype=np.float64)
    block[:, 0] = amp * np.sin(phase_a)
    block[:, 1] = amp * np.sin(phase_b)
    return block


def run_case(name: str, settings: dict, *, blocks: int) -> dict:
    proc = DMTDProcessor(
        sample_rate=settings["sample_rate"],
        beat_frequency=settings["beat_frequency"],
        expansion_factor=settings["ref_frequency"] / settings["beat_frequency"],
        ref_frequency=settings["ref_frequency"],
        freq_estimator=settings["freq_estimator"],
        demod_mode=settings["demod_mode"],
        freq_source=settings.get("freq_source", "ch_a"),
        iq_lpf_cutoff_hz=settings.get("iq_lpf_cutoff_hz", 120.0),
        iq_lpf_order=settings.get("iq_lpf_order", 4),
        iq_min_mag=settings.get("iq_min_mag", 1e-4),
        iq_window=settings.get("iq_window", "hann"),
        pll_kp=settings.get("pll_kp", 0.3),
        pll_ki=settings.get("pll_ki", 0.03),
        pll_min_mag=settings.get("pll_min_mag", 1e-4),
    )

    generator = {
        "sample_rate": settings["sample_rate"],
        "n": settings["block_size"],
        "blocks": blocks,
        "beat_hz": settings["beat_frequency"],
        "b_offset_rad": settings.get("b_offset_rad", 0.02),
        "b_drift_rad_per_block": settings.get("b_drift_rad_per_block", 0.0005),
        "amp": settings.get("amp", 0.45),
    }

    expected = []
    for block_index in range(blocks):
        block = make_stereo_block(
            sample_rate=generator["sample_rate"],
            n=generator["n"],
            block_index=block_index,
            beat_hz=generator["beat_hz"],
            b_offset_rad=generator["b_offset_rad"],
            b_drift_rad_per_block=generator["b_drift_rad_per_block"],
            amp=generator["amp"],
        )
        (
            phase_diff_rad,
            phase_diff_ps,
            beat_freq,
            phase_a_ps,
            phase_b_ps,
            phase_a_deg,
            phase_b_deg,
            rms_a,
            rms_b,
        ) = proc.process_block(block)
        expected.append(
            {
                "phase_diff_rad": phase_diff_rad,
                "phase_diff_ps": phase_diff_ps,
                "beat_freq": beat_freq,
                "phase_a_ps": phase_a_ps,
                "phase_b_ps": phase_b_ps,
                "phase_a_deg": phase_a_deg,
                "phase_b_deg": phase_b_deg,
                "rms_a": rms_a,
                "rms_b": rms_b,
            }
        )

    return {"name": name, "settings": settings, "generator": generator, "expected": expected}


def main() -> None:
    base = {
        "sample_rate": 48_000,
        "block_size": 4_800,
        "beat_frequency": 1_000.0,
        "ref_frequency": 90_000_000.0,
        "freq_estimator": "fixed",
        "b_offset_rad": 0.02,
        "b_drift_rad_per_block": 0.0005,
        "amp": 0.45,
    }

    cases = [
        run_case(
            "block_iq_three_blocks",
            {**base, "demod_mode": "block_iq", "iq_window": "hann"},
            blocks=3,
        ),
        run_case(
            "block_iq_fir_two_blocks",
            {
                **base,
                "demod_mode": "block_iq_fir",
                "iq_window": "hann",
                "iq_lpf_cutoff_hz": 120.0,
                "iq_lpf_order": 4,
            },
            blocks=2,
        ),
        run_case(
            "pll_tracker_three_blocks",
            {
                **base,
                "demod_mode": "pll_tracker",
                "iq_window": "hann",
                "pll_kp": 0.3,
                "pll_ki": 0.03,
                "pll_min_mag": 1e-4,
            },
            blocks=3,
        ),
        run_case(
            "block_iq_rect_window",
            {**base, "demod_mode": "block_iq", "iq_window": "none"},
            blocks=2,
        ),
    ]

    out_path = ROOT / "Dmtd.Core.Tests" / "GoldenVectors" / "dmtd_golden_vectors.json"
    out_path.parent.mkdir(parents=True, exist_ok=True)
    out_path.write_text(json.dumps({"cases": cases}, indent=2), encoding="utf-8")
    print(f"Wrote {out_path} ({len(cases)} cases)")


if __name__ == "__main__":
    main()

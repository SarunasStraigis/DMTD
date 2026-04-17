"""
DSP engine: IQ demodulation, phase unwrapping, DMTD differential measurement.

Processing pipeline per block:
  1. Estimate beat frequency (FFT peak or fixed)
  2. IQ demodulate each channel → atan2 → raw phase
  3. Unwrap each channel independently (accumulate across blocks)
  4. Differential subtraction A - B
  5. Scale by expansion factor → physical phase in radians, then picoseconds
"""

from __future__ import annotations

import math
import numpy as np
from numpy.typing import NDArray


class DMTDProcessor:
    def __init__(
        self,
        sample_rate: int,
        beat_frequency: float,
        expansion_factor: float,
        ref_frequency: float,
        freq_estimator: str = "fft_peak",
    ) -> None:
        self.sample_rate = sample_rate
        self.beat_frequency = beat_frequency
        self.expansion_factor = expansion_factor
        self.ref_frequency = ref_frequency
        self.freq_estimator = freq_estimator

        # Accumulated unwrap offsets (updated per block)
        self._prev_raw_A: float | None = None
        self._prev_raw_B: float | None = None
        self._unwrap_offset_A: float = 0.0
        self._unwrap_offset_B: float = 0.0

    def reset(self) -> None:
        self._prev_raw_A = None
        self._prev_raw_B = None
        self._unwrap_offset_A = 0.0
        self._unwrap_offset_B = 0.0

    def process_block(
        self, block: NDArray[np.float64]
    ) -> tuple[float, float, float, float, float, float, float, float, float]:
        """
        Parameters
        ----------
        block : shape (N, 2) float64 stereo samples, normalised to [-1, 1]

        Returns
        -------
        (phase_diff_rad, phase_diff_ps, beat_freq,
         phase_a_ps, phase_b_ps, phase_a_deg, phase_b_deg, rms_a, rms_b)

            phase_diff_rad  – physical differential phase in radians
            phase_diff_ps   – same in picoseconds
            beat_freq       – estimated beat-note frequency used this block
            phase_a_ps      – absolute unwrapped phase of ch A in picoseconds
            phase_b_ps      – absolute unwrapped phase of ch B in picoseconds
            phase_a_deg     – absolute unwrapped phase of ch A in degrees
            phase_b_deg     – absolute unwrapped phase of ch B in degrees
            rms_a           – RMS amplitude of ch A (normalised, 0–1)
            rms_b           – RMS amplitude of ch B (normalised, 0–1)
        """
        ch_a = block[:, 0].astype(np.float64)
        ch_b = block[:, 1].astype(np.float64)
        n = len(ch_a)

        beat_freq = self._estimate_frequency(ch_a, n)

        # Build I/Q reference vectors
        t = np.arange(n, dtype=np.float64) / self.sample_rate
        phase_ref = 2.0 * math.pi * beat_freq * t
        cos_ref = np.cos(phase_ref)
        sin_ref = np.sin(phase_ref)

        raw_A = self._iq_phase(ch_a, cos_ref, sin_ref)
        raw_B = self._iq_phase(ch_b, cos_ref, sin_ref)

        # Unwrap across block boundaries
        unwrapped_A = self._unwrap_step(raw_A, "_A")
        unwrapped_B = self._unwrap_step(raw_B, "_B")

        # Physical differential phase
        phase_diff_expanded = unwrapped_A - unwrapped_B
        phase_diff_rad = phase_diff_expanded / self.expansion_factor

        # Convert to picoseconds: τ = φ / (2π f_ref)
        ps_per_rad = 1e12 / (2.0 * math.pi * self.ref_frequency)
        phase_diff_ps = phase_diff_rad * ps_per_rad

        # Per-channel absolute phase in picoseconds (physical domain, divided by expansion factor)
        phase_a_ps = (unwrapped_A / self.expansion_factor) * ps_per_rad
        phase_b_ps = (unwrapped_B / self.expansion_factor) * ps_per_rad

        # Per-channel phase in degrees — raw beat-note IQ phase (NOT divided by expansion factor).
        # This is what the audio-band signal actually looks like: 180° on the signal generator
        # will appear as ~180° here, making it useful for signal health / self-test diagnostics.
        deg_per_rad = 180.0 / math.pi
        phase_a_deg = unwrapped_A * deg_per_rad
        phase_b_deg = unwrapped_B * deg_per_rad

        # RMS amplitude per channel (normalised audio float, 0–1 range)
        rms_a = float(math.sqrt(float(np.mean(ch_a ** 2))))
        rms_b = float(math.sqrt(float(np.mean(ch_b ** 2))))

        return (
            float(phase_diff_rad),
            float(phase_diff_ps),
            float(beat_freq),
            float(phase_a_ps),
            float(phase_b_ps),
            float(phase_a_deg),
            float(phase_b_deg),
            rms_a,
            rms_b,
        )

    # ------------------------------------------------------------------
    # Internal helpers
    # ------------------------------------------------------------------

    def _estimate_frequency(self, ch: NDArray[np.float64], n: int) -> float:
        if self.freq_estimator == "fixed":
            return self.beat_frequency

        # FFT-based peak near the nominal beat frequency
        fft_mag = np.abs(np.fft.rfft(ch))
        freqs = np.fft.rfftfreq(n, d=1.0 / self.sample_rate)

        # Search window: ±20 % of nominal beat frequency
        lo = self.beat_frequency * 0.80
        hi = self.beat_frequency * 1.20
        mask = (freqs >= lo) & (freqs <= hi)
        if not np.any(mask):
            return self.beat_frequency

        idx_in_window = np.argmax(fft_mag[mask])
        peak_freq = freqs[mask][idx_in_window]
        return float(peak_freq)

    @staticmethod
    def _iq_phase(
        ch: NDArray[np.float64],
        cos_ref: NDArray[np.float64],
        sin_ref: NDArray[np.float64],
    ) -> float:
        i_dc = float(np.mean(ch * cos_ref))
        q_dc = float(np.mean(ch * sin_ref))
        return math.atan2(q_dc, i_dc)

    def _unwrap_step(self, raw: float, suffix: str) -> float:
        """Accumulate a cross-block unwrap offset using a simple ±π jump detector."""
        prev_attr = f"_prev_raw{suffix}"
        offset_attr = f"_unwrap_offset{suffix}"
        prev = getattr(self, prev_attr)
        offset = getattr(self, offset_attr)

        if prev is None:
            setattr(self, prev_attr, raw)
            return raw + offset

        jump = raw - prev
        if jump > math.pi:
            offset -= 2.0 * math.pi
        elif jump < -math.pi:
            offset += 2.0 * math.pi

        setattr(self, prev_attr, raw)
        setattr(self, offset_attr, offset)
        return raw + offset

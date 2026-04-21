"""
DSP engine: IQ demodulation, phase unwrapping, DMTD differential measurement.

Processing pipeline per block:
  1. Estimate beat frequency (FFT peak or fixed)
  2. Demodulate each channel (block IQ or PLL tracker) → phase
  3. For block IQ mode, unwrap each channel independently (accumulate across blocks)
  4. Differential subtraction A - B
  5. Convert differential phase to picoseconds (no expansion-factor division)
"""

from __future__ import annotations

import math
import numpy as np
from numpy.typing import NDArray
from scipy.signal import butter, sosfilt, sosfilt_zi


class DMTDProcessor:
    def __init__(
        self,
        sample_rate: int,
        beat_frequency: float,
        expansion_factor: float,
        ref_frequency: float,
        freq_estimator: str = "fft_peak",
        demod_mode: str = "block_iq",
        freq_source: str = "ch_a",
        iq_lpf_cutoff_hz: float = 120.0,
        iq_lpf_order: int = 4,
        iq_min_mag: float = 1e-4,
        pll_kp: float = 0.3,
        pll_ki: float = 0.03,
        pll_min_mag: float = 1e-4,
    ) -> None:
        self.sample_rate = sample_rate
        self.beat_frequency = beat_frequency
        self.expansion_factor = expansion_factor
        self.ref_frequency = ref_frequency
        self.freq_estimator = freq_estimator
        self.demod_mode = demod_mode
        self.freq_source = freq_source
        self.iq_lpf_cutoff_hz = iq_lpf_cutoff_hz
        self.iq_lpf_order = iq_lpf_order
        self.iq_min_mag = iq_min_mag
        self.pll_kp = pll_kp
        self.pll_ki = pll_ki
        self.pll_min_mag = pll_min_mag

        # Accumulated unwrap offsets (updated per block)
        self._prev_raw_A: float | None = None
        self._prev_raw_B: float | None = None
        self._unwrap_offset_A: float = 0.0
        self._unwrap_offset_B: float = 0.0
        self._last_estimated_freq: float | None = None
        self._pll_phase_A: float | None = None
        self._pll_phase_B: float | None = None
        self._pll_freq_hz: float | None = None
        self._iq_lpf_cache_key: tuple[int, float, int] | None = None
        self._iq_lpf_sos: NDArray[np.float64] | None = None
        self._iq_lpf_zi_ia: NDArray[np.float64] | None = None
        self._iq_lpf_zi_qa: NDArray[np.float64] | None = None
        self._iq_lpf_zi_ib: NDArray[np.float64] | None = None
        self._iq_lpf_zi_qb: NDArray[np.float64] | None = None

    def reset(self) -> None:
        self._prev_raw_A = None
        self._prev_raw_B = None
        self._unwrap_offset_A = 0.0
        self._unwrap_offset_B = 0.0
        self._last_estimated_freq = None
        self._pll_phase_A = None
        self._pll_phase_B = None
        self._pll_freq_hz = None
        self._iq_lpf_zi_ia = None
        self._iq_lpf_zi_qa = None
        self._iq_lpf_zi_ib = None
        self._iq_lpf_zi_qb = None

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

            phase_diff_rad  – beat-note differential phase in radians
            phase_diff_ps   – same in picoseconds, using DUT oscillator frequency
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

        beat_freq = self._estimate_frequency(ch_a, ch_b, n)

        if self.demod_mode == "pll_tracker":
            unwrapped_A, unwrapped_B = self._pll_demodulate(ch_a, ch_b, n, beat_freq)
        elif self.demod_mode == "block_iq_fir":
            unwrapped_A, unwrapped_B = self._block_iq_fir_demodulate(ch_a, ch_b, n, beat_freq)
        else:
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

        # Beat-note differential phase (no expansion-factor division).
        phase_diff_expanded = unwrapped_A - unwrapped_B
        phase_diff_rad = phase_diff_expanded

        # Convert to picoseconds: τ = φ / (2π f_ref)
        ps_per_rad = 1e12 / (2.0 * math.pi * self.ref_frequency)
        phase_diff_ps = phase_diff_rad * ps_per_rad

        # Per-channel absolute phase in picoseconds (same domain as phase_diff_rad above).
        phase_a_ps = unwrapped_A * ps_per_rad
        phase_b_ps = unwrapped_B * ps_per_rad

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

    def _block_iq_fir_demodulate(
        self,
        ch_a: NDArray[np.float64],
        ch_b: NDArray[np.float64],
        n: int,
        beat_freq: float,
    ) -> tuple[float, float]:
        t = np.arange(n, dtype=np.float64) / self.sample_rate
        phase_ref = 2.0 * math.pi * beat_freq * t
        cos_ref = np.cos(phase_ref)
        sin_ref = np.sin(phase_ref)

        i_a = self._lowpass_iq_stream(ch_a * cos_ref, "ia")
        q_a = self._lowpass_iq_stream(ch_a * sin_ref, "qa")
        i_b = self._lowpass_iq_stream(ch_b * cos_ref, "ib")
        q_b = self._lowpass_iq_stream(ch_b * sin_ref, "qb")

        # Use the settled tail to reduce startup transient bias.
        tail = max(32, int(0.2 * n))
        i_dc_a = float(np.mean(i_a[-tail:]))
        q_dc_a = float(np.mean(q_a[-tail:]))
        i_dc_b = float(np.mean(i_b[-tail:]))
        q_dc_b = float(np.mean(q_b[-tail:]))

        raw_a = self._iq_phase_with_mag_gate(i_dc_a, q_dc_a, "_A", self.iq_min_mag)
        raw_b = self._iq_phase_with_mag_gate(i_dc_b, q_dc_b, "_B", self.iq_min_mag)
        return self._unwrap_step(raw_a, "_A"), self._unwrap_step(raw_b, "_B")

    def _lowpass_iq_stream(self, x: NDArray[np.float64], stream: str) -> NDArray[np.float64]:
        key = (self.sample_rate, self.iq_lpf_cutoff_hz, self.iq_lpf_order)
        if self._iq_lpf_sos is None or self._iq_lpf_cache_key != key:
            nyquist = 0.5 * self.sample_rate
            norm_cutoff = min(0.99, self.iq_lpf_cutoff_hz / nyquist)
            self._iq_lpf_sos = butter(self.iq_lpf_order, norm_cutoff, btype="low", output="sos")
            self._iq_lpf_cache_key = key

            # Reset stream filter memories when the filter design changes.
            self._iq_lpf_zi_ia = None
            self._iq_lpf_zi_qa = None
            self._iq_lpf_zi_ib = None
            self._iq_lpf_zi_qb = None

        zi_attr = f"_iq_lpf_zi_{stream}"
        zi = getattr(self, zi_attr)
        if zi is None:
            zi = sosfilt_zi(self._iq_lpf_sos) * float(x[0] if len(x) else 0.0)

        y, zf = sosfilt(self._iq_lpf_sos, x, zi=zi)
        setattr(self, zi_attr, zf)
        return y

    def _iq_phase_with_mag_gate(
        self, i_dc: float, q_dc: float, suffix: str, min_mag: float
    ) -> float:
        mag = math.hypot(i_dc, q_dc)
        if mag < min_mag:
            prev = getattr(self, f"_prev_raw{suffix}")
            if prev is not None:
                return float(prev)
        return math.atan2(q_dc, i_dc)

    def _pll_demodulate(
        self,
        ch_a: NDArray[np.float64],
        ch_b: NDArray[np.float64],
        n: int,
        beat_freq_hint: float,
    ) -> tuple[float, float]:
        if self._pll_phase_A is None:
            self._pll_phase_A = 0.0
        if self._pll_phase_B is None:
            self._pll_phase_B = 0.0
        if self._pll_freq_hz is None:
            self._pll_freq_hz = beat_freq_hint

        phase_a, i_a, q_a = self._pll_channel_measure(
            ch_a, n, self._pll_phase_A, self._pll_freq_hz
        )
        phase_b, i_b, q_b = self._pll_channel_measure(
            ch_b, n, self._pll_phase_B, self._pll_freq_hz
        )

        mag_a = math.hypot(i_a, q_a)
        mag_b = math.hypot(i_b, q_b)
        err_a = math.atan2(q_a, i_a)
        err_b = math.atan2(q_b, i_b)
        common_err = 0.5 * (err_a + err_b)

        block_dt = n / float(self.sample_rate)
        pll_freq_next = self._pll_freq_hz
        good_a = mag_a >= self.pll_min_mag
        good_b = mag_b >= self.pll_min_mag
        if good_a and good_b:
            avg_mag = 0.5 * (mag_a + mag_b)
            gain = avg_mag / (avg_mag + self.pll_min_mag)
            pll_freq_next += self.pll_ki * gain * (
                common_err / (2.0 * math.pi * block_dt)
            )
        pll_freq_next += 0.02 * (beat_freq_hint - pll_freq_next)

        if good_a:
            gain_a = mag_a / (mag_a + self.pll_min_mag)
            phase_a += self.pll_kp * gain_a * err_a
        if good_b:
            gain_b = mag_b / (mag_b + self.pll_min_mag)
            phase_b += self.pll_kp * gain_b * err_b

        self._pll_phase_A = phase_a
        self._pll_phase_B = phase_b
        self._pll_freq_hz = pll_freq_next
        return phase_a, phase_b

    def _pll_channel_measure(
        self,
        ch: NDArray[np.float64],
        n: int,
        phase_state: float,
        shared_freq_hz: float,
    ) -> tuple[float, float, float]:
        t = np.arange(n, dtype=np.float64) / self.sample_rate
        nco_phase = phase_state + (2.0 * math.pi * shared_freq_hz * t)
        cos_ref = np.cos(nco_phase)
        sin_ref = np.sin(nco_phase)

        i_dc = float(np.mean(ch * cos_ref))
        q_dc = float(np.mean(ch * sin_ref))
        block_dt = n / float(self.sample_rate)
        phase_propagated = phase_state + (2.0 * math.pi * shared_freq_hz * block_dt)
        return phase_propagated, i_dc, q_dc

    def _estimate_frequency(
        self, ch_a: NDArray[np.float64], ch_b: NDArray[np.float64], n: int
    ) -> float:
        if self.freq_estimator == "fixed":
            return self.beat_frequency

        ch = ch_a if self.freq_source == "ch_a" else 0.5 * (ch_a + ch_b)
        fft_mag = np.abs(np.fft.rfft(ch))
        freqs = np.fft.rfftfreq(n, d=1.0 / self.sample_rate)
        nyquist = 0.5 * self.sample_rate

        def peak_in_window(lo: float, hi: float) -> tuple[float, float] | None:
            lo_clamped = max(5.0, lo)
            hi_clamped = min(nyquist * 0.98, hi)
            if hi_clamped <= lo_clamped:
                return None
            mask = (freqs >= lo_clamped) & (freqs <= hi_clamped)
            if not np.any(mask):
                return None
            idx_local = int(np.argmax(fft_mag[mask]))
            window_freqs = freqs[mask]
            window_mag = fft_mag[mask]
            return float(window_freqs[idx_local]), float(window_mag[idx_local])

        nominal = self.beat_frequency
        broad = peak_in_window(20.0, min(nyquist * 0.98, 20_000.0))
        if broad is None:
            self._last_estimated_freq = nominal
            return nominal

        # If we already have lock, prioritize continuity strongly to avoid broad-band jumps.
        if self._last_estimated_freq is not None:
            guided = peak_in_window(
                self._last_estimated_freq * 0.80, self._last_estimated_freq * 1.20
            )
            if guided is not None:
                self._last_estimated_freq = guided[0]
                return guided[0]
            self._last_estimated_freq = broad[0]
            return broad[0]

        # First-lock logic:
        # prefer nominal window when it's reasonably strong; otherwise fall back broad.
        nominal_peak = peak_in_window(nominal * 0.80, nominal * 1.20)
        if nominal_peak is not None and nominal_peak[1] >= (0.20 * broad[1]):
            chosen = nominal_peak[0]
        else:
            chosen = broad[0]

        self._last_estimated_freq = chosen
        return chosen

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

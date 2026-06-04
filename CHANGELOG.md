# Changelog

All notable changes to PhaseLab are documented here. Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [Unreleased]

## [1.0.6] - 2026-06-04

### Fixed

- **DMTD:** DSP and block sizing now use the actual WASAPI capture sample rate when it differs from the rate selected in the app.
- **Jitter:** On capture rate mismatch, settings sync to the actual rate for analysis.

### Changed

- **Capture:** Sample rate combo reflects a request to the device; an orange warning appears when it differs from the Windows mix format. Status shows effective capture rate. Selecting a device defaults the combo to the device mix rate when listed.

## [1.0.5] - 2026-06-04

### Fixed

- **UI:** ComboBox closed-state text invisible in dark mode; custom template now applies theme foreground correctly.
- **UI:** ComboBox click target and text clipping (full control opens dropdown; selection no longer hidden beside arrow).

### Changed

- **DMTD:** Single main view with phase plot and live channel waveform; channel metrics (beat, RMS, phase) under phase chart. Right panel uses Session / Configuration tabs. Removed Live checkbox and top-level Channels tab; channel plot always updates while capturing.
- **DMTD:** Compact Session action buttons (2×2 grid).
- **UI:** Side panel tab headers styled as segmented control; selected tab uses accent highlight for clearer state.

## [1.0.4] - 2026-06-04

### Fixed

- **Jitter:** Welch PSD normalization for integrated jitter was ~100× too low versus time-domain RMS on wideband noise. Scaling now matches scipy `welch(..., scaling='density')` with Hann window energy correction and proper one-sided density factors.

### Added

- **Jitter:** `JitterMeasurement.Core.Tests` — Parseval checks (white noise, single tone) and `IntegrationBand` Nyquist clamp tests.
- **Jitter:** Integration limits capped at Nyquist during analysis; hero title and FFT markers use the effective band.
- **Jitter:** Tooltips on integration fields and metrics explaining integrated (√∫ PSD over band) vs full-window RMS.

### Changed

- **DMTD:** Demodulation simplified to Block IQ only (removed PLL demod and Block IQ+LPF path); golden vectors and settings updated accordingly.
- **Jitter:** Integrated jitter is the primary metric; cumulative integrated jitter on FFT (right axis); Refresh devices button; corrected time-axis decimation for plots; autoscale toggle row above charts.

## [1.0.3] - 2026-05-29

### Added

- Application version shown in the shell toolbar.

### Changed

- DMTD and Jitter module UI: channel beat frequency, RMS, and phase metrics; status text during capture; layout cleanup.

## [1.0.0] - 2026-05-29

### Added

- PhaseLab Windows desktop shell with DMTD and Jitter modules (.NET 8).
- Velopack installer, GitHub Actions release workflow, and startup update check.
- Localhost REST API for module snapshots (see [docs/API.md](docs/API.md)).

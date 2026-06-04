# Changelog

All notable changes to PhaseLab are documented here. Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

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

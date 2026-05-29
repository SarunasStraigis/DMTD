### System Architecture: High-Precision Phase Measurement (DMTD)

**Objective:** Measure sub-picosecond, long-term phase drift between two 60–90 MHz continuous-wave oscillators. 

**Core Methodology:** Dual Mixer Time Difference (DMTD) combined with direct digital down-conversion. This architecture stretches the physical phase difference by an expansion factor (e.g., 90,000), translates the measurement to the audio band, and uses intensive mathematical averaging to crush thermal and amplitude noise.

---

### 1. Hardware: RF Front-End
The analog RF stage down-converts the test oscillators to a 1 kHz baseband beat note.

* **Transfer Oscillator (LO):** A signal generator or DDS set to exactly 1 kHz offset from the test oscillators (e.g., 90.001 MHz). 
* **LO Distribution:** Route the LO through an amplifier to achieve roughly +16 dBm. Split this using a 2-way, 0-degree power splitter. Place 6 dB attenuators on both splitter outputs to isolate the mixer LO ports from each other.
* **DUT Inputs:** Route the two 90.000 MHz test oscillators through 3 dB attenuators to absorb reflections.
* **The Mixers:** Feed the LO and DUT signals into two identical Double-Balanced Mixers (DBMs). **Crucial Layout Rule:** The mixers and all trace lengths must be physically identical and thermally coupled (placed directly adjacent) to prevent differential temperature drift.

### 2. Hardware: Baseband & Digitization
The IF output from the mixers (now containing a 1 kHz beat note, 90 MHz bleed-through, and 180 MHz sum frequency) must be conditioned and digitized.

* **Low-Pass Filtering:** Route both IF outputs through passive LC low-pass filters (cutoff < 100 kHz) to strip all high-frequency RF.
* **Impedance Matching:** Place a physical $50\ \Omega$ shunt resistor across the signal and ground lines immediately before the digitization stage to ensure the mixers operate linearly.
* **Digitization Hardware:** Connect the conditioned signals to the Left and Right line inputs of a 24-bit commercial stereo audio interface. 
* **Audio Interface Settings:** AC-coupled line inputs, minimum gain, phantom power **strictly disabled** (to prevent frying the mixer diodes). 

### 3. Software: DSP & Mathematics
The software layer captures the continuous audio stream and extracts the phase difference mathematically, eliminating the need for analog zero-crossing comparators.

* **Data Capture:** Pull continuous, raw 24-bit stereo data from the audio interface at a high sample rate (e.g., 192 kSPS) using a low-latency, bit-perfect audio driver API. 
* **Dynamic LO Matching:** Calculate the actual incoming frequency of the beat notes (which will naturally drift slightly from exactly 1,000.0 Hz) to set the software demodulator frequency.
* **IQ Demodulation (Per Channel):**
    1. Multiply the incoming data array by a software-generated sine wave ($Q$) and cosine wave ($I$).
    2. Apply a digital low-pass filter (or calculate the mean of the data block) to extract the DC components: $I_{DC}$ and $Q_{DC}$.
    3. Calculate the raw phase: `atan2(Q_DC, I_DC)`.
* **Phase Unwrapping:** Apply an unwrapping algorithm to the calculated phase arrays for both channels to remove the artificial $2\pi$ jumps caused by the slight frequency offset between the physical beat note and the software LO.
* **Differential Subtraction:** Subtract the unwrapped phase of Channel B from Channel A. The common-mode frequency error perfectly cancels out.
* **Expansion Factor Scaling:** Divide the resulting phase difference by the expansion factor (e.g., 90,000) to yield the true physical phase drift between the two 90 MHz oscillators.

---

## PhaseLab (Native .NET Desktop App)

**PhaseLab** is the unified Windows desktop application for DMTD phase analysis and jitter measurement. Use the toolbar to switch modes; the last selected mode is remembered in `%AppData%\PhaseLab\settings.json`.

### Install (end users)

Download **`PhaseLab-win-Setup.exe`** from [GitHub Releases](https://github.com/SarunasStraigis/DMTD/releases). No .NET SDK is required — the installer is self-contained.

Windows SmartScreen may warn because the installer is not code-signed yet. Choose **More info → Run anyway** to proceed.

Installed apps check GitHub Releases for updates on startup and prompt before downloading.

### Prerequisites (development)
- Windows 10/11 x64
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

### Run (development)

```powershell
dotnet run --project PhaseLab.Shell/PhaseLab.Shell.csproj
```

### Publish (single-file executable)

```powershell
./publish.ps1
```

Output: `dist/PhaseLab.exe`

### Release installer (maintainers)

Tag a version to build and publish a Velopack installer via GitHub Actions:

```powershell
git tag v1.0.1
git push origin v1.0.1
```

The release workflow publishes `PhaseLab-win-Setup.exe` and update metadata to GitHub Releases.

To test packaging locally before tagging:

```powershell
./scripts/release-pack.ps1 -Version 1.0.1
```

Output: `dist/releases/` (Setup.exe and Velopack update assets).

### REST API

While PhaseLab is running, a localhost REST API is available for scripting and automation:

- **Docs:** [docs/API.md](docs/API.md)
- **Swagger UI:** http://127.0.0.1:8787/docs (when the app is open)
- **Poll metrics:** `GET /api/modules/{dmtd|jitter}/snapshot`

Configure port and enable/disable in `%AppData%\PhaseLab\settings.json` (`apiEnabled`, `apiPort`).

---

## Project Structure

```
DMTD/
├── PhaseLab.Shell/              # WPF app entry point
├── PhaseLab.UI/                 # Shared shell UI and themes
├── PhaseLab.Api/                # REST API contracts
├── PhaseLab.Api.Host/           # Embedded Kestrel host
├── Dmtd.Core/                   # DMTD DSP library
├── Dmtd.Module/                 # DMTD measurement module
├── JitterMeasurement.Core/      # Jitter analysis library
├── JitterMeasurement.Module/    # Jitter measurement module
├── Dmtd.Core.Tests/             # Unit and golden-vector tests
├── docs/API.md                  # REST API reference
├── scripts/release-pack.ps1     # Local Velopack packaging
├── publish.ps1                  # Dev single-file publish
└── .github/workflows/           # CI and release automation
```

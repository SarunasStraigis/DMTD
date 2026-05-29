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

### Prerequisites
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

### REST API

While PhaseLab is running, a localhost REST API is available for scripting and automation:

- **Docs:** [docs/API.md](docs/API.md)
- **Swagger UI:** http://127.0.0.1:8787/docs (when the app is open)
- **Poll metrics:** `GET /api/modules/{dmtd|jitter}/snapshot`

Configure port and enable/disable in `%AppData%\PhaseLab\settings.json` (`apiEnabled`, `apiPort`).

### Legacy Python/React stack

The `python desktop.py` launcher and `backend/` + `frontend/` stack remain in the repo for reference but are **deprecated** in favor of PhaseLab.

---

## Software Setup (Legacy Python/React)

### Prerequisites
- Python 3.11+
- Node.js 18+

### Backend

```bash
cd backend
pip install -r requirements.txt
```

### Frontend

```bash
cd frontend
npm install
npm run build          # build for production (served by FastAPI)
# OR
npm run dev            # dev server with hot reload at http://localhost:5173
```

### Run (Desktop App — deprecated)

```bash
# From the repo root:
python desktop.py
```

Starts the FastAPI server on `http://localhost:8123` and opens a native desktop window. Prefer **PhaseLab** above.

### Run (Server / Headless)

```bash
cd backend
uvicorn main:app --host 0.0.0.0 --port 8000
```

Access the web UI at `http://localhost:8000` from any browser.  
Interactive API docs: `http://localhost:8000/docs`

---

## REST API (PhaseLab)

PhaseLab includes an embedded localhost API. See [docs/API.md](docs/API.md) for endpoints, snapshot fields, and curl examples. Interactive OpenAPI docs: `http://127.0.0.1:8787/docs`.

## Legacy REST API (deprecated Python stack)

| Method | Path | Description |
|--------|------|-------------|
| GET | `/api/config` | Read configuration |
| PUT | `/api/config` | Update & persist configuration |
| GET | `/api/devices` | List audio input devices |
| GET | `/api/status` | Running state, sample rate, WS clients |
| POST | `/api/control/start` | Start audio capture & DSP |
| POST | `/api/control/stop` | Stop capture |
| GET | `/api/history` | Query phase history (SQLite) |
| WS | `/ws/live` | Real-time phase data stream |

---

## Project Structure

```
DMTD/
├── backend/
│   ├── main.py              # FastAPI app
│   ├── audio_capture.py     # sounddevice capture + DSP dispatch
│   ├── dsp.py               # IQ demod, unwrap, DMTD scaling
│   ├── config.py            # Pydantic config, load/save JSON
│   ├── history.py           # aiosqlite phase log
│   └── ws_manager.py        # WebSocket broadcast manager
├── frontend/
│   ├── src/
│   │   ├── App.tsx           # Main layout, tab nav, start/stop
│   │   ├── components/
│   │   │   ├── PhaseChart.tsx    # uPlot real-time chart
│   │   │   ├── AllanChart.tsx    # OADEV chart (recharts)
│   │   │   └── ConfigPanel.tsx   # Settings form
│   │   └── api/client.ts         # REST + WebSocket helpers
│   └── package.json
├── desktop.py               # pywebview desktop launcher
├── config.json              # Persisted settings (auto-created)
├── history.db               # SQLite phase log (auto-created)
└── README.md
```

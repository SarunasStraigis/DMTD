# PhaseLab REST API

PhaseLab exposes a small **localhost-only REST API** while the desktop app is running. Use it to poll live metrics, control capture, and run module-specific actions from scripts or automation tools.

- **Base URL:** `http://127.0.0.1:8787`
- **Interactive docs:** [http://127.0.0.1:8787/docs](http://127.0.0.1:8787/docs)
- **OpenAPI spec:** [http://127.0.0.1:8787/openapi/v1.json](http://127.0.0.1:8787/openapi/v1.json)

The API starts automatically when PhaseLab launches (unless disabled in settings). It binds to **127.0.0.1 only** — no authentication is required.

## Configuration

Settings file: `%AppData%\PhaseLab\settings.json`

```json
{
  "activeModeId": "dmtd",
  "apiEnabled": true,
  "apiPort": 8787
}
```

| Field | Default | Description |
|-------|---------|-------------|
| `apiEnabled` | `true` | Start the embedded API server on launch |
| `apiPort` | `8787` | TCP port (localhost only) |

## Quick start

List modules:

```powershell
curl http://127.0.0.1:8787/api/modules
```

Poll DMTD live metrics (primary endpoint):

```powershell
curl http://127.0.0.1:8787/api/modules/dmtd/snapshot
```

Start capture:

```powershell
curl -X POST http://127.0.0.1:8787/api/modules/dmtd/capture/start `
  -H "Content-Type: application/json" `
  -d "{\"sampleRate\": 192000}"
```

Stop capture:

```powershell
curl -X POST http://127.0.0.1:8787/api/modules/dmtd/capture/stop
```

Set phase zero (DMTD, while capturing):

```powershell
curl -X POST http://127.0.0.1:8787/api/modules/dmtd/actions/phase-zero/set
```

Calibrate jitter (while capturing):

```powershell
curl -X POST http://127.0.0.1:8787/api/modules/jitter/actions/calibrate
```

## Endpoints

| Method | Path | Description |
|--------|------|-------------|
| GET | `/api` | App name, API version, module ids |
| GET | `/api/modules` | All modules with capabilities and actions |
| GET | `/api/modules/{id}` | Single module info |
| GET | `/api/modules/{id}/status` | Capture state, device, sample rate |
| GET | `/api/modules/{id}/snapshot` | **Latest metrics** (poll this) |
| GET | `/api/modules/{id}/devices` | Audio input devices |
| POST | `/api/modules/{id}/devices/refresh` | Re-enumerate devices |
| POST | `/api/modules/{id}/capture/start` | Start capture |
| POST | `/api/modules/{id}/capture/stop` | Stop capture |
| POST | `/api/modules/{id}/actions/{action}` | Module-specific command |

### Capture start body (optional)

```json
{
  "deviceId": "wasapi-device-id",
  "sampleRate": 192000,
  "inputChannel": 1
}
```

`inputChannel` applies to the Jitter module only.

## Snapshot format

Every `/snapshot` response shares the same envelope:

```json
{
  "moduleId": "dmtd",
  "timestamp": "2026-05-29T12:00:00Z",
  "capturing": true,
  "statusText": "Capturing at 192000 Hz",
  "data": { }
}
```

The `data` object is module-specific.

### DMTD (`dmtd`) — `data` fields

| Field | Type | Description |
|-------|------|-------------|
| `phaseDiffPs` | number | Latest differential phase (ps) |
| `phaseDiffRad` | number | Latest differential phase (rad) |
| `beatFreqHz` | number | Estimated beat frequency |
| `movingAveragePs` | number | Session moving average |
| `stdDevPs` | number | Session standard deviation |
| `maWindow` | int | Moving-average window size |
| `phaseZeroActive` | bool | Phase zero offset applied |
| `phaseZeroOffsetPs` | number | Stored zero offset |
| `rmsA`, `rmsB` | number | Channel RMS levels |
| `slipCount` | int | Total phase slips |
| `latestTimestamp` | string | Time of latest point (ISO 8601) |

### Jitter (`jitter`) — `data` fields

| Field | Type | Description |
|-------|------|-------------|
| `jitterRmsFs` | number | RMS jitter (fs) |
| `integratedFs` | number | Band-limited integrated jitter (fs) |
| `sigmaVRms` | number | RMS voltage noise |
| `sigmaPhiRad` | number | Phase noise (rad) |
| `harmonicFrequencyHz` | number | Measured harmonic frequency |
| `calibrated` | bool | Calibration present |
| `vpp` | number | Peak-to-peak voltage |
| `kpdVPerRad` | number | Phase detector gain (V/rad) |
| `isClipping` | bool | Input clipping detected |
| `isValid` | bool | Measurement valid |
| `message` | string | Status or error message |

## Module actions

| Module | Action | Description |
|--------|--------|-------------|
| dmtd | `phase-zero/set` | Zero displayed phase to current reading |
| dmtd | `phase-zero/clear` | Clear phase zero offset |
| dmtd | `session/reset` | Reset session metrics and plot |
| jitter | `calibrate` | Run phase-detector calibration |

Failed preconditions return **409** with a JSON body: `{"detail": "..."}`.

## Polling guidance

| Module | Suggested interval | Notes |
|--------|-------------------|-------|
| DMTD | 100 ms – 1 s | Block duration sets minimum useful interval |
| Jitter | 100–500 ms | Analysis refreshes ~30 Hz internally |

## Extending for new modules

Future PhaseLab modules implement `IMeasurementApiModule` (in `PhaseLab.Api`) and expose it via `IMeasurementModule.Api`. The host registers all non-null API modules automatically — no route changes required in the shell.

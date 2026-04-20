"""
DMTD Analysis Backend — FastAPI application.
"""

from __future__ import annotations

import asyncio
from contextlib import asynccontextmanager
from typing import Any

import io
import csv

from fastapi import FastAPI, HTTPException, WebSocket, WebSocketDisconnect
from fastapi.middleware.cors import CORSMiddleware
from fastapi.staticfiles import StaticFiles
from fastapi.responses import FileResponse, StreamingResponse
from pathlib import Path

from .audio_capture import capture, list_input_devices
from .config import AppConfig, load_config, save_config
from .history import close_db, init_db, prune_old_rows, query_history
from .signal_gen import SigGenConfig, generator as sig_generator, list_output_devices
from .ws_manager import manager as ws_manager

FRONTEND_DIST = Path(__file__).parent.parent / "frontend" / "dist"


@asynccontextmanager
async def lifespan(app: FastAPI):
    await init_db()
    yield
    capture.stop()
    sig_generator.stop()
    await close_db()


app = FastAPI(title="DMTD Phase Analyser", lifespan=lifespan)

app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_methods=["*"],
    allow_headers=["*"],
)


# ---------------------------------------------------------------------------
# Config endpoints
# ---------------------------------------------------------------------------

@app.get("/api/config", response_model=AppConfig)
async def get_config() -> AppConfig:
    return load_config()


@app.put("/api/config", response_model=AppConfig)
async def put_config(cfg: AppConfig) -> AppConfig:
    save_config(cfg)
    if capture.running:
        capture.set_phase_zero_offset(cfg.phase_zero_offset_rad, cfg.phase_zero_offset_ps)
    return cfg


# ---------------------------------------------------------------------------
# Device enumeration
# ---------------------------------------------------------------------------

@app.get("/api/devices")
async def get_devices() -> list[dict]:
    return list_input_devices()


# ---------------------------------------------------------------------------
# Status & control
# ---------------------------------------------------------------------------

@app.get("/api/status")
async def get_status() -> dict[str, Any]:
    cfg = load_config()
    return {
        "running": capture.running,
        "ws_clients": ws_manager.client_count,
        "sample_rate": cfg.sample_rate,
        "block_size": cfg.block_size,
        "device_index": cfg.device_index,
        "beat_frequency": cfg.beat_frequency,
        "expansion_factor": cfg.expansion_factor,
        "phase_zero_offset_rad": cfg.phase_zero_offset_rad,
        "phase_zero_offset_ps": cfg.phase_zero_offset_ps,
    }


@app.post("/api/control/start")
async def start_capture() -> dict:
    if capture.running:
        raise HTTPException(status_code=409, detail="Already running")
    cfg = load_config()
    loop = asyncio.get_event_loop()
    try:
        capture.start(cfg, loop)
    except Exception as exc:
        raise HTTPException(status_code=500, detail=str(exc)) from exc
    return {"status": "started"}


@app.post("/api/control/stop")
async def stop_capture() -> dict:
    if not capture.running:
        raise HTTPException(status_code=409, detail="Not running")
    capture.stop()
    return {"status": "stopped"}


# ---------------------------------------------------------------------------
# Phase zero offset
# ---------------------------------------------------------------------------

@app.get("/api/phase/zero")
async def get_phase_zero() -> dict[str, Any]:
    cfg = load_config()
    return {
        "phase_zero_offset_rad": cfg.phase_zero_offset_rad,
        "phase_zero_offset_ps": cfg.phase_zero_offset_ps,
        "active": abs(cfg.phase_zero_offset_rad) > 0.0 or abs(cfg.phase_zero_offset_ps) > 0.0,
    }


@app.post("/api/phase/zero/set")
async def set_phase_zero() -> dict[str, Any]:
    if not capture.running:
        raise HTTPException(status_code=409, detail="Capture must be running to set zero")
    latest = capture.get_latest_raw_phase()
    if latest is None:
        raise HTTPException(status_code=409, detail="No live phase sample available yet")
    latest_rad, latest_ps = latest
    cfg = load_config()
    cfg.phase_zero_offset_rad = -latest_rad
    cfg.phase_zero_offset_ps = -latest_ps
    save_config(cfg)
    capture.set_phase_zero_offset(cfg.phase_zero_offset_rad, cfg.phase_zero_offset_ps)
    return {
        "status": "set",
        "phase_zero_offset_rad": cfg.phase_zero_offset_rad,
        "phase_zero_offset_ps": cfg.phase_zero_offset_ps,
        "active": True,
    }


@app.post("/api/phase/zero/clear")
async def clear_phase_zero() -> dict[str, Any]:
    cfg = load_config()
    cfg.phase_zero_offset_rad = 0.0
    cfg.phase_zero_offset_ps = 0.0
    save_config(cfg)
    capture.set_phase_zero_offset(0.0, 0.0)
    return {
        "status": "cleared",
        "phase_zero_offset_rad": 0.0,
        "phase_zero_offset_ps": 0.0,
        "active": False,
    }


# ---------------------------------------------------------------------------
# History
# ---------------------------------------------------------------------------

@app.get("/api/history")
async def get_history(limit: int = 10000, since: str | None = None) -> list[dict]:
    cfg = load_config()
    await prune_old_rows(cfg.history_retention_days)
    return await query_history(limit=limit, since=since)


# ---------------------------------------------------------------------------
# Waveform snapshot
# ---------------------------------------------------------------------------

@app.get("/api/snapshot")
async def get_snapshot() -> dict[str, Any]:
    if not capture.running:
        raise HTTPException(status_code=409, detail="Capture not running")
    buf, sr = capture.get_snapshot()
    return {
        "sample_rate": sr,
        "ch_a": buf[:, 0].tolist(),
        "ch_b": buf[:, 1].tolist(),
    }


# ---------------------------------------------------------------------------
# History export (CSV)
# ---------------------------------------------------------------------------

@app.get("/api/history/export")
async def export_history(since: str | None = None) -> StreamingResponse:
    rows = await query_history(limit=1_000_000, since=since)

    def generate():
        buf = io.StringIO()
        writer = csv.writer(buf)
        writer.writerow(["ts", "phase_rad", "phase_ps", "beat_freq_hz"])
        for row in rows:
            writer.writerow([row["ts"], row["phase_rad"], row["phase_ps"], row["beat_freq"]])
        yield buf.getvalue()

    filename = "dmtd_phase_history.csv"
    return StreamingResponse(
        generate(),
        media_type="text/csv",
        headers={"Content-Disposition": f'attachment; filename="{filename}"'},
    )


# ---------------------------------------------------------------------------
# Signal generator
# ---------------------------------------------------------------------------

@app.get("/api/siggen/devices")
async def siggen_devices() -> list[dict]:
    return list_output_devices()


@app.get("/api/siggen/status")
async def siggen_status() -> dict[str, Any]:
    cfg = sig_generator.config
    return {
        "running": sig_generator.running,
        **cfg.model_dump(),
    }


@app.put("/api/siggen/config", response_model=SigGenConfig)
async def siggen_put_config(cfg: SigGenConfig) -> SigGenConfig:
    was_running = sig_generator.running
    if was_running:
        sig_generator.stop()
        sig_generator.start(cfg)
    else:
        sig_generator._cfg = cfg
    return sig_generator.config


@app.post("/api/siggen/start")
async def siggen_start(cfg: SigGenConfig | None = None) -> dict:
    if sig_generator.running:
        raise HTTPException(status_code=409, detail="Signal generator already running")
    effective_cfg = cfg if cfg is not None else sig_generator.config
    try:
        sig_generator.start(effective_cfg)
    except Exception as exc:
        raise HTTPException(status_code=500, detail=str(exc)) from exc
    return {"status": "started"}


@app.post("/api/siggen/stop")
async def siggen_stop() -> dict:
    if not sig_generator.running:
        raise HTTPException(status_code=409, detail="Signal generator not running")
    sig_generator.stop()
    return {"status": "stopped"}


# ---------------------------------------------------------------------------
# WebSocket live feed
# ---------------------------------------------------------------------------

@app.websocket("/ws/live")
async def ws_live(websocket: WebSocket) -> None:
    await ws_manager.connect(websocket)
    try:
        while True:
            # Keep connection alive; data is pushed by the DSP worker
            await websocket.receive_text()
    except WebSocketDisconnect:
        pass
    finally:
        await ws_manager.disconnect(websocket)


# ---------------------------------------------------------------------------
# Serve built frontend (production)
# ---------------------------------------------------------------------------

if FRONTEND_DIST.exists():
    app.mount("/assets", StaticFiles(directory=FRONTEND_DIST / "assets"), name="assets")

    @app.get("/{full_path:path}", include_in_schema=False)
    async def serve_spa(full_path: str) -> FileResponse:
        index = FRONTEND_DIST / "index.html"
        return FileResponse(index)

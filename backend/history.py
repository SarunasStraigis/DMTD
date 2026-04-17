"""
Phase history persistence via SQLite (async, using aiosqlite).
"""

from __future__ import annotations

import asyncio
from datetime import datetime, timedelta, timezone
from pathlib import Path

import aiosqlite

DB_PATH = Path(__file__).parent.parent / "history.db"

_CREATE_TABLE = """
CREATE TABLE IF NOT EXISTS phase_log (
    id          INTEGER PRIMARY KEY AUTOINCREMENT,
    ts          TEXT    NOT NULL,
    phase_rad   REAL    NOT NULL,
    phase_ps    REAL    NOT NULL,
    beat_freq   REAL    NOT NULL
);
CREATE INDEX IF NOT EXISTS idx_ts ON phase_log (ts);
"""

_db_conn: aiosqlite.Connection | None = None
_write_queue: asyncio.Queue[tuple[str, float, float, float]] = asyncio.Queue()
_writer_task: asyncio.Task | None = None


async def init_db() -> None:
    global _db_conn, _writer_task
    _db_conn = await aiosqlite.connect(DB_PATH)
    await _db_conn.executescript(_CREATE_TABLE)
    await _db_conn.commit()
    _writer_task = asyncio.create_task(_writer_loop())


async def close_db() -> None:
    global _db_conn, _writer_task
    if _writer_task:
        _writer_task.cancel()
        _writer_task = None
    if _db_conn:
        await _db_conn.close()
        _db_conn = None


async def _writer_loop() -> None:
    """Drain the write queue in batches for efficiency."""
    while True:
        rows: list[tuple[str, float, float, float]] = []
        try:
            row = await asyncio.wait_for(_write_queue.get(), timeout=2.0)
            rows.append(row)
            # drain any additional buffered items
            while not _write_queue.empty():
                rows.append(_write_queue.get_nowait())
        except asyncio.TimeoutError:
            continue
        except asyncio.CancelledError:
            break

        if _db_conn and rows:
            try:
                await _db_conn.executemany(
                    "INSERT INTO phase_log (ts, phase_rad, phase_ps, beat_freq) VALUES (?,?,?,?)",
                    rows,
                )
                await _db_conn.commit()
            except Exception:
                pass


def enqueue_row(ts: str, phase_rad: float, phase_ps: float, beat_freq: float) -> None:
    """Non-blocking enqueue from DSP thread."""
    try:
        _write_queue.put_nowait((ts, phase_rad, phase_ps, beat_freq))
    except asyncio.QueueFull:
        pass


async def query_history(
    limit: int = 10000,
    since: str | None = None,
) -> list[dict]:
    if _db_conn is None:
        return []
    sql = "SELECT ts, phase_rad, phase_ps, beat_freq FROM phase_log"
    params: list = []
    if since:
        sql += " WHERE ts >= ?"
        params.append(since)
    sql += " ORDER BY ts DESC LIMIT ?"
    params.append(limit)
    async with _db_conn.execute(sql, params) as cursor:
        rows = await cursor.fetchall()
    return [
        {"ts": r[0], "phase_rad": r[1], "phase_ps": r[2], "beat_freq": r[3]}
        for r in reversed(rows)
    ]


async def prune_old_rows(retention_days: int) -> None:
    if _db_conn is None:
        return
    cutoff = (
        datetime.now(timezone.utc) - timedelta(days=retention_days)
    ).isoformat()
    await _db_conn.execute("DELETE FROM phase_log WHERE ts < ?", (cutoff,))
    await _db_conn.commit()

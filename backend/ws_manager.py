"""
WebSocket connection manager.
Broadcasts JSON messages to all currently connected clients.
"""

import asyncio
import json
from typing import Any

from fastapi import WebSocket


class ConnectionManager:
    def __init__(self) -> None:
        self._active: list[WebSocket] = []
        self._lock = asyncio.Lock()

    async def connect(self, ws: WebSocket) -> None:
        await ws.accept()
        async with self._lock:
            self._active.append(ws)

    async def disconnect(self, ws: WebSocket) -> None:
        async with self._lock:
            try:
                self._active.remove(ws)
            except ValueError:
                pass

    async def broadcast(self, data: dict[str, Any]) -> None:
        text = json.dumps(data)
        dead: list[WebSocket] = []
        async with self._lock:
            targets = list(self._active)
        for ws in targets:
            try:
                await ws.send_text(text)
            except Exception:
                dead.append(ws)
        for ws in dead:
            await self.disconnect(ws)

    @property
    def client_count(self) -> int:
        return len(self._active)


manager = ConnectionManager()

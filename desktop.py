"""
Desktop launcher: starts the FastAPI backend in a background thread,
then opens a pywebview window pointing at localhost:8000.

Usage:
    python desktop.py
"""

import threading
import time
import webbrowser

import uvicorn
import webview

PORT = 8000
URL = f"http://localhost:{PORT}"


def _run_server() -> None:
    uvicorn.run(
        "backend.main:app",
        host="127.0.0.1",
        port=PORT,
        log_level="warning",
    )


def _wait_for_server(timeout: float = 10.0) -> bool:
    import urllib.request

    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        try:
            urllib.request.urlopen(f"{URL}/api/status", timeout=1)
            return True
        except Exception:
            time.sleep(0.2)
    return False


def main() -> None:
    server_thread = threading.Thread(target=_run_server, daemon=True)
    server_thread.start()

    if not _wait_for_server():
        print("Backend did not start in time. Opening browser as fallback.")
        webbrowser.open(URL)
        server_thread.join()
        return

    window = webview.create_window(
        title="DMTD Phase Analyser",
        url=URL,
        width=1280,
        height=800,
        min_size=(900, 600),
    )
    webview.start(debug=False)


if __name__ == "__main__":
    main()

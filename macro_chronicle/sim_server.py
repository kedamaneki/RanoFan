"""静的ファイル配信 + シミュレーション API。"""

from __future__ import annotations

import json
import sys
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from urllib.parse import urlparse

ROOT = Path(__file__).resolve().parent
_PKG_ROOT = ROOT.parent
if str(_PKG_ROOT) not in sys.path:
    sys.path.insert(0, str(_PKG_ROOT))


class SimHandler(SimpleHTTPRequestHandler):
    def __init__(self, *args, **kwargs):
        super().__init__(*args, directory=str(ROOT), **kwargs)

    @staticmethod
    def _normalize_path(path: str) -> str:
        if path in ("/macro_chronicle", "/macro_chronicle/"):
            return "/index.html"
        if path.startswith("/macro_chronicle/"):
            return path[len("/macro_chronicle") :] or "/"
        return path

    def log_message(self, format: str, *args) -> None:
        if args and str(args[0]).startswith("4"):
            super().log_message(format, *args)

    def _json(self, code: int, payload: dict) -> None:
        body = json.dumps(payload, ensure_ascii=False).encode("utf-8")
        self.send_response(code)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(body)))
        self.send_header("Access-Control-Allow-Origin", "*")
        self.end_headers()
        self.wfile.write(body)

    def end_headers(self) -> None:
        self.send_header("X-Macro-Chronicle-Server", "sim_server")
        super().end_headers()

    def _read_json(self) -> dict:
        length = int(self.headers.get("Content-Length", 0))
        raw = self.rfile.read(length) if length else b"{}"
        return json.loads(raw.decode("utf-8") or "{}")

    def do_OPTIONS(self) -> None:
        self.send_response(204)
        self.send_header("Access-Control-Allow-Origin", "*")
        self.send_header("Access-Control-Allow-Methods", "GET, POST, OPTIONS")
        self.send_header("Access-Control-Allow-Headers", "Content-Type")
        self.end_headers()

    def do_GET(self) -> None:
        path = self._normalize_path(urlparse(self.path).path)
        if path in ("/", ""):
            self.send_response(302)
            self.send_header("Location", "/index.html")
            self.end_headers()
            return
        if path == "/favicon.ico":
            self.send_response(204)
            self.end_headers()
            return
        if path == "/api/config":
            from macro_chronicle.config import SimulationConfig

            cfg = SimulationConfig.load()
            self._json(200, {"ok": True, "config": cfg.to_dict()})
            return
        if path == "/api/health":
            self._json(200, {"ok": True, "service": "macro_chronicle"})
            return
        self.path = path
        super().do_GET()

    def do_POST(self) -> None:
        path = self._normalize_path(urlparse(self.path).path)
        try:
            if path == "/api/config":
                from macro_chronicle.config import SimulationConfig

                data = self._read_json()
                cfg = SimulationConfig.from_dict(data.get("config", data))
                saved = cfg.save()
                self._json(200, {"ok": True, "config": cfg.to_dict(), "path": str(saved)})
                return

            if path == "/api/simulate":
                from macro_chronicle.config import SimulationConfig
                from macro_chronicle.runner import run_simulation, write_outputs

                data = self._read_json()
                cfg = SimulationConfig.from_dict(data.get("config", data))
                if data.get("save_config", True):
                    cfg.save()
                eng, geo = run_simulation(cfg)
                paths = write_outputs(eng, geo, ROOT)
                final = eng.snapshots[-1]
                self._json(
                    200,
                    {
                        "ok": True,
                        "config": cfg.to_dict(),
                        "summary": {
                            "turns": cfg.macro_turns,
                            "initial_nations": cfg.initial_nations,
                            "alive": final.alive_count,
                            "total_power": round(final.total_human_power, 2),
                        },
                        "paths": paths,
                        "geo": geo,
                    },
                )
                return

            self._json(404, {"ok": False, "error": "not found"})
        except Exception as exc:
            self._json(500, {"ok": False, "error": str(exc)})


def main() -> None:
    port = int(sys.argv[1]) if len(sys.argv) > 1 else 8765
    host = sys.argv[2] if len(sys.argv) > 2 else "127.0.0.1"
    try:
        server = ThreadingHTTPServer((host, port), SimHandler)
    except OSError as exc:
        print(f"ポート {port} を使えません: {exc}", flush=True)
        print("既に起動中のサーバーを Ctrl+C で止めてから再実行してください。", flush=True)
        sys.exit(1)
    print(f"マクロ・クロニクル: http://{host}:{port}/index.html", flush=True)
    print("API: GET/POST /api/config  POST /api/simulate", flush=True)
    print("(python -m http.server では POST は 501 になります)", flush=True)
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        print("\n停止")
        server.server_close()


if __name__ == "__main__":
    main()

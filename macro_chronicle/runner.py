"""シミュレーション実行と成果物出力。"""

from __future__ import annotations

import json
from pathlib import Path
from typing import Dict, Tuple

from .config import SimulationConfig
from .engine import MacroChronicleEngine
from .formatter import format_chronicle
from .geo import export_geo_payload


def run_simulation(config: SimulationConfig) -> Tuple[MacroChronicleEngine, Dict[str, object]]:
    eng = MacroChronicleEngine.create(config)
    eng.run(config.macro_turns)
    return eng, export_geo_payload(eng, config)


def write_outputs(
    eng: MacroChronicleEngine,
    geo_payload: Dict[str, object],
    out_dir: Path,
) -> Dict[str, str]:
    out_dir.mkdir(parents=True, exist_ok=True)
    paths = {
        "log": out_dir / "macro_chronicle_log.json",
        "analytics": out_dir / "macro_analytics.json",
        "report": out_dir / "macro_chronicle_report.md",
        "geo": out_dir / "macro_chronicle_geo.json",
    }
    paths["log"].write_text(
        json.dumps([e.to_dict() for e in eng.logs], ensure_ascii=False, indent=2),
        encoding="utf-8",
    )
    paths["analytics"].write_text(
        json.dumps([s.to_dict() for s in eng.snapshots], ensure_ascii=False, indent=2),
        encoding="utf-8",
    )
    paths["report"].write_text(format_chronicle(eng), encoding="utf-8")
    paths["geo"].write_text(json.dumps(geo_payload, ensure_ascii=False, indent=2), encoding="utf-8")
    return {k: str(v) for k, v in paths.items()}

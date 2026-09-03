"""パス・ファイル名ユーティリティ."""

from __future__ import annotations

from pathlib import Path

from data_loader import NationTurnState

PROJECT_ROOT = Path(__file__).resolve().parent.parent
MICRO_DIR = PROJECT_ROOT / "ミクロ生活史"
DEFAULT_GEO = PROJECT_ROOT.parent / "macro_chronicle" / "macro_chronicle_geo.json"


def nation_dir(nation_id: int) -> Path:
    return MICRO_DIR / f"国家{nation_id:03d}"


def output_filename(state: NationTurnState) -> str:
    return (
        f"{state.name}_ターン{state.turn:03d}_"
        f"叙事年{state.narrative_year:04d}_生活史.md"
    )


def output_path(state: NationTurnState) -> Path:
    return nation_dir(state.nation_id) / output_filename(state)


def ensure_directories() -> None:
    MICRO_DIR.mkdir(parents=True, exist_ok=True)

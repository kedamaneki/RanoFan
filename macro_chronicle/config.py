"""シミュレーション設定の読み書き。"""

from __future__ import annotations

import json
from dataclasses import asdict, dataclass, field
from pathlib import Path
from typing import Any, Dict

DEFAULT_CONFIG_PATH = Path(__file__).resolve().parent / "macro_chronicle_config.json"

BASE_NATIONS = 131
BASE_INITIAL_POWER = 19340.01


@dataclass
class SimulationConfig:
    initial_nations: int = 250
    macro_turns: int = 100
    seed: int = 42
    target_initial_power: float = 0.0  # 0 = 国数に比例して自動
    initial_global_threat: float = 1.232
    years_per_turn: int = 10
    birth_chance: float = 0.14
    placement_min_dist_deg: float = 1.0

    def __post_init__(self) -> None:
        self.initial_nations = max(50, min(500, int(self.initial_nations)))
        self.macro_turns = max(10, min(1000, int(self.macro_turns)))
        self.years_per_turn = max(1, min(50, int(self.years_per_turn)))
        self.birth_chance = max(0.0, min(1.0, float(self.birth_chance)))
        self.placement_min_dist_deg = max(0.5, min(3.0, float(self.placement_min_dist_deg)))

    def effective_initial_power(self) -> float:
        if self.target_initial_power > 0:
            return self.target_initial_power
        return BASE_INITIAL_POWER * (self.initial_nations / BASE_NATIONS)

    def to_dict(self) -> Dict[str, Any]:
        d = asdict(self)
        d["effective_initial_power"] = round(self.effective_initial_power(), 2)
        return d

    @classmethod
    def from_dict(cls, data: Dict[str, Any]) -> SimulationConfig:
        known = {f.name for f in cls.__dataclass_fields__.values()}  # type: ignore[attr-defined]
        return cls(**{k: v for k, v in data.items() if k in known})

    def save(self, path: Path | None = None) -> Path:
        p = path or DEFAULT_CONFIG_PATH
        p.write_text(json.dumps(self.to_dict(), ensure_ascii=False, indent=2), encoding="utf-8")
        return p

    @classmethod
    def load(cls, path: Path | None = None) -> SimulationConfig:
        p = path or DEFAULT_CONFIG_PATH
        if not p.is_file():
            cfg = cls()
            cfg.save(p)
            return cfg
        return cls.from_dict(json.loads(p.read_text(encoding="utf-8")))

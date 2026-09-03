"""マクロ・クロニクル — データモデル."""

from __future__ import annotations

from dataclasses import dataclass, field
from typing import Dict, List, Optional


REGIONS = ("領域・西", "領域・東", "領域・中央", "領域・南", "領域・北")


@dataclass
class Nation:
    """国家オブジェクト。ナンバリングは滅亡後も固定。"""

    nation_id: int
    region: str
    lat: float = 0.0
    lng: float = 0.0
    alive: bool = True
    economy: float = 50.0
    military: float = 50.0
    magic: float = 40.0
    territory: float = 50.0
    barrier_efficiency: float = 1.0
    infrastructure: float = 1.0
    innovation: float = 1.0
    domestic_threat_factor: float = 1.0
    born_turn: int = 0
    died_turn: Optional[int] = None
    death_cause: Optional[str] = None
    # 構造変化（吸収・統合）
    absorbed_by: Optional[int] = None
    structural_change: Optional[str] = None
    structural_turn: Optional[int] = None

    @property
    def name(self) -> str:
        return f"国家{self.nation_id:03d}"

    @property
    def human_power(self) -> float:
        return self.economy + self.military + self.magic

    def clamp(self) -> None:
        for attr in ("economy", "military", "magic"):
            v = getattr(self, attr)
            setattr(self, attr, max(0.0, min(2000.0, v)))
        self.territory = max(0.0, min(500.0, self.territory))
        self.barrier_efficiency = max(0.3, min(2.5, self.barrier_efficiency))
        self.infrastructure = max(0.3, min(2.5, self.infrastructure))
        self.innovation = max(0.3, min(2.5, self.innovation))
        self.domestic_threat_factor = max(0.5, min(2.0, self.domestic_threat_factor))


@dataclass
class RegionState:
    name: str
    threat_level: float = 1.0
    spike: float = 0.0

    def effective_threat(self) -> float:
        raw = self.threat_level + self.spike
        return max(0.08, min(2.5, raw))


@dataclass
class MacroLog:
    turn: int
    event: str
    category: str
    nation_id: Optional[int] = None
    nation_ids: Optional[List[int]] = None
    region: Optional[str] = None
    result: Optional[Dict[str, object]] = None

    def to_dict(self) -> Dict[str, object]:
        d: Dict[str, object] = {
            "turn": self.turn,
            "event": self.event,
            "category": self.category,
        }
        if self.nation_id is not None:
            d["nation_id"] = self.nation_id
        if self.nation_ids:
            d["nation_ids"] = self.nation_ids
        if self.region:
            d["region"] = self.region
        if self.result:
            d["result"] = self.result
        return d


@dataclass
class TurnSnapshot:
    turn: int
    alive_count: int
    total_human_power: float
    avg_threat: float
    global_threat_index: float
    cycle_phase: str

    def to_dict(self) -> Dict[str, object]:
        return {
            "turn": self.turn,
            "narrative_year": self.turn * 10,
            "alive_countries": self.alive_count,
            "total_human_power": round(self.total_human_power, 2),
            "average_monster_threat": round(self.avg_threat, 3),
            "global_threat_index": round(self.global_threat_index, 3),
            "cycle_phase": self.cycle_phase,
        }

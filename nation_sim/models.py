"""国家シミュレーション — データモデル定義."""

from __future__ import annotations

from dataclasses import dataclass, field
from typing import Dict, List, Optional


@dataclass
class NationalPower:
    """国力（経済・軍事）。"""

    economy: float
    military: float

    @property
    def total(self) -> float:
        return self.economy + self.military

    def clamp(self, minimum: float = 0.0, maximum: float = 1000.0) -> None:
        self.economy = max(minimum, min(maximum, self.economy))
        self.military = max(minimum, min(maximum, self.military))


@dataclass
class Region:
    """地域オブジェクト。魔物脅威は地域単位で管理する。"""

    name: str
    threat_level: float  # 地域脅威度係数（0.5〜2.0）

    def clamp(self) -> None:
        self.threat_level = max(0.5, min(2.0, self.threat_level))


@dataclass
class State:
    """国家オブジェクト。"""

    name: str
    power: NationalPower
    magic_level: float
    territory_size: float
    government: str
    region_id: str
    domestic_threat_factor: float  # 国内脅威因子（0.5〜2.0）
    relations: Dict[str, float] = field(default_factory=dict)
    alliances: List[str] = field(default_factory=list)

    def get_relation(self, other_name: str) -> float:
        return self.relations.get(other_name, 0.0)

    def set_relation(self, other_name: str, value: float) -> None:
        self.relations[other_name] = max(-100.0, min(100.0, value))

    def adjust_relation(self, other_name: str, delta: float) -> None:
        self.set_relation(other_name, self.get_relation(other_name) + delta)

    def clamp_stats(self) -> None:
        self.power.clamp()
        self.magic_level = max(0.0, min(1000.0, self.magic_level))
        self.territory_size = max(1.0, min(1000.0, self.territory_size))
        self.domestic_threat_factor = max(0.5, min(2.0, self.domestic_threat_factor))

    def final_threat(self, regions: Dict[str, Region]) -> float:
        """最終脅威度 = 所属地域の地域脅威度 × 国内脅威因子。"""
        region = regions[self.region_id]
        return round(region.threat_level * self.domestic_threat_factor, 2)


@dataclass
class HistoryEntry:
    """履歴ログの1エントリ（事実と数値のみ）。"""

    turn: int
    event: str
    category: str = "general"
    region: Optional[str] = None
    state: Optional[str] = None
    states: Optional[List[str]] = None
    result: Optional[Dict[str, object]] = None

    def to_dict(self) -> Dict[str, object]:
        entry: Dict[str, object] = {
            "turn": self.turn,
            "event": self.event,
            "category": self.category,
        }
        if self.region is not None:
            entry["region"] = self.region
        if self.state is not None:
            entry["state"] = self.state
        if self.states:
            entry["states"] = self.states
        if self.result:
            entry["result"] = self.result
        return entry

    def to_fact_text(self) -> str:
        """LLM入力用の事実テキスト（形容・情景表現なし）。"""
        parts = [f"ターン{self.turn}", f"事象:{self.event}"]
        if self.region:
            parts.append(f"地域:{self.region}")
        if self.state:
            parts.append(f"国家:{self.state}")
        if self.states:
            parts.append(f"関与国家:{','.join(self.states)}")
        if self.result:
            changes = ", ".join(f"{k}={v}" for k, v in self.result.items())
            parts.append(f"結果:{changes}")
        return " | ".join(parts)

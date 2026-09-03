"""シミュレーションコアエンジン."""

from __future__ import annotations

import json
import random
from dataclasses import dataclass, field
from pathlib import Path
from typing import Dict, List, Optional

from .events import EventContext, passive_turn_effects, select_events_for_state, tick_region_threats
from .models import HistoryEntry, Region, State


@dataclass
class SimulationEngine:
    """国家シミュレーションのコアエンジン。

    1ターン = 1ヶ月。魔物脅威は地域×国内因子で計算される。
    すべての出来事は事実と数値変化のみを history_log に記録する。
    """

    states: Dict[str, State]
    regions: Dict[str, Region]
    current_turn: int = 0
    history_log: List[HistoryEntry] = field(default_factory=list)
    seed: Optional[int] = None
    _rng: random.Random = field(default_factory=random.Random, repr=False)

    def __post_init__(self) -> None:
        if self.seed is not None:
            self._rng.seed(self.seed)

    @property
    def state_list(self) -> List[State]:
        return list(self.states.values())

    def _make_context(self) -> EventContext:
        return EventContext(
            turn=self.current_turn,
            states=self.states,
            regions=self.regions,
            rng=self._rng,
            log=self.history_log,
        )

    def process_turn(self) -> List[HistoryEntry]:
        """1ターン（1ヶ月）を進行し、このターンで発生したログを返す。"""
        self.current_turn += 1
        turn_start_len = len(self.history_log)
        ctx = self._make_context()

        tick_region_threats(ctx)

        state_order = self.state_list[:]
        self._rng.shuffle(state_order)

        for state in state_order:
            handlers = select_events_for_state(state, ctx, max_events=2)
            for handler in handlers:
                handler(state, ctx)

        passive_turn_effects(ctx)

        for state in self.states.values():
            state.clamp_stats()
        for region in self.regions.values():
            region.clamp()

        return self.history_log[turn_start_len:]

    def run(self, turns: int) -> List[HistoryEntry]:
        if turns < 1:
            raise ValueError("turns は 1 以上である必要があります。")
        start = len(self.history_log)
        for _ in range(turns):
            self.process_turn()
        return self.history_log[start:]

    def get_history_as_dicts(self) -> List[Dict[str, object]]:
        return [entry.to_dict() for entry in self.history_log]

    def get_history_fact_texts(self) -> List[str]:
        return [entry.to_fact_text() for entry in self.history_log]

    def get_history_json(self, indent: int = 2) -> str:
        return json.dumps(self.get_history_as_dicts(), ensure_ascii=False, indent=indent)

    def save_history(self, path: str | Path) -> None:
        Path(path).write_text(self.get_history_json(), encoding="utf-8")

    def get_turn_logs(self, turn: int) -> List[Dict[str, object]]:
        return [e.to_dict() for e in self.history_log if e.turn == turn]

    def snapshot(self) -> Dict[str, object]:
        return {
            "turn": self.current_turn,
            "regions": {
                rid: {
                    "name": r.name,
                    "threat_level": round(r.threat_level, 2),
                }
                for rid, r in self.regions.items()
            },
            "states": {
                name: {
                    "name": s.name,
                    "region": self.regions[s.region_id].name,
                    "economy": round(s.power.economy, 1),
                    "military": round(s.power.military, 1),
                    "magic_level": round(s.magic_level, 1),
                    "territory_size": round(s.territory_size, 1),
                    "government": s.government,
                    "domestic_threat_factor": round(s.domestic_threat_factor, 2),
                    "final_threat": s.final_threat(self.regions),
                    "alliances": list(s.alliances),
                    "relations": {k: round(v, 1) for k, v in s.relations.items()},
                }
                for name, s in self.states.items()
            },
        }

    def snapshot_json(self, indent: int = 2) -> str:
        return json.dumps(self.snapshot(), ensure_ascii=False, indent=indent)

"""macro_chronicle_geo.json ローダーと時間・周期計算."""

from __future__ import annotations

import json
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any, Dict, Iterator, List, Optional, Tuple

CYCLE_LEN = 24
ACTIVE_HALF = 12


@dataclass(frozen=True)
class NationTurnState:
    """特定ターンにおける一国のスナップショット."""

    nation_id: int
    name: str
    region: str
    lat: float
    lng: float
    alive: bool
    territory: float
    power: float
    economy: float
    military: float
    magic: float
    barrier_efficiency: float
    born_turn: int
    died_turn: Optional[int]
    absorbed_by: Optional[int]
    turn: int
    narrative_year: int
    cycle_phase: str
    cycle_position: int
    global_threat_index: float
    events: List[Dict[str, Any]] = field(default_factory=list)

    @property
    def is_operational(self) -> bool:
        """当該ターンに国家として存続していたか."""
        if self.turn < self.born_turn:
            return False
        if self.died_turn is not None and self.turn > self.died_turn:
            return False
        return self.alive or self.turn == self.died_turn


def narrative_year(turn: int, years_per_turn: int) -> int:
    return turn * years_per_turn


def cycle_position(turn: int) -> int:
    if turn <= 0:
        return 0
    return ((turn - 1) % CYCLE_LEN) + 1


def cycle_phase(turn: int) -> str:
    pos = cycle_position(turn)
    if pos == 0:
        return "active"
    return "active" if pos <= ACTIVE_HALF else "dormant"


class ChronicleDataLoader:
    """geo JSON を読み込み、ターン×国家の状態とログを提供する."""

    def __init__(self, geo_path: Path) -> None:
        self.geo_path = geo_path.resolve()
        self._raw: Dict[str, Any] = {}
        self._snapshots_by_turn: Dict[int, Dict[str, Any]] = {}
        self._logs_by_turn_nation: Dict[Tuple[int, int], List[Dict[str, Any]]] = {}
        self._logs_by_turn: Dict[int, List[Dict[str, Any]]] = {}
        self._nation_ids: set[int] = set()
        self._loaded = False

    def load(self) -> None:
        if self._loaded:
            return
        if not self.geo_path.is_file():
            raise FileNotFoundError(f"geo JSON が見つかりません: {self.geo_path}")

        with self.geo_path.open("r", encoding="utf-8") as f:
            self._raw = json.load(f)

        for snap in self._raw.get("snapshots", []):
            turn = int(snap["turn"])
            self._snapshots_by_turn[turn] = snap
            for nation in snap.get("nations", []):
                self._nation_ids.add(int(nation["id"]))

        for log in self._raw.get("logs", []):
            turn = int(log["turn"])
            self._logs_by_turn.setdefault(turn, []).append(log)
            nid = log.get("nation_id")
            if nid is not None:
                key = (turn, int(nid))
                self._logs_by_turn_nation.setdefault(key, []).append(log)

        self._loaded = True

    @property
    def meta(self) -> Dict[str, Any]:
        self.load()
        return self._raw.get("meta", {})

    @property
    def years_per_turn(self) -> int:
        return int(self.meta.get("years_per_turn", 1))

    @property
    def max_turn(self) -> int:
        return int(self.meta.get("turns", max(self._snapshots_by_turn) if self._snapshots_by_turn else 0))

    @property
    def nation_ids(self) -> List[int]:
        self.load()
        return sorted(self._nation_ids)

    def get_nation_turn(self, nation_id: int, turn: int) -> Optional[NationTurnState]:
        self.load()
        snap = self._snapshots_by_turn.get(turn)
        if snap is None:
            return None

        nation_data: Optional[Dict[str, Any]] = None
        for n in snap.get("nations", []):
            if int(n["id"]) == nation_id:
                nation_data = n
                break
        if nation_data is None:
            return None

        stats = snap.get("stats", {})
        ypt = self.years_per_turn
        phase = cycle_phase(turn)
        pos = cycle_position(turn)

        events = list(self._logs_by_turn_nation.get((turn, nation_id), []))
        global_events = [
            e for e in self._logs_by_turn.get(turn, [])
            if e.get("nation_id") is None and e.get("category") in (
                "threat_environment",
                "cycle_transition",
            )
        ]
        events = global_events + events

        return NationTurnState(
            nation_id=nation_id,
            name=str(nation_data.get("name", f"国家{nation_id:03d}")),
            region=str(nation_data.get("region", "")),
            lat=float(nation_data.get("lat", 0.0)),
            lng=float(nation_data.get("lng", 0.0)),
            alive=bool(nation_data.get("alive", False)),
            territory=float(nation_data.get("territory", 0.0)),
            power=float(nation_data.get("power", 0.0)),
            economy=float(nation_data.get("economy", 0.0)),
            military=float(nation_data.get("military", 0.0)),
            magic=float(nation_data.get("magic", 0.0)),
            barrier_efficiency=float(nation_data.get("barrier_efficiency", 1.0)),
            born_turn=int(nation_data.get("born_turn", 0)),
            died_turn=nation_data.get("died_turn"),
            absorbed_by=nation_data.get("absorbed_by"),
            turn=turn,
            narrative_year=narrative_year(turn, ypt),
            cycle_phase=phase,
            cycle_position=pos,
            global_threat_index=float(stats.get("global_threat_index", 0.0)),
            events=events,
        )

    def iter_jobs(
        self,
        nation_ids: Optional[List[int]] = None,
        start_turn: int = 1,
        end_turn: Optional[int] = None,
        skip_dead_after: bool = True,
    ) -> Iterator[NationTurnState]:
        """生成対象の (国家, ターン) を列挙する."""
        self.load()
        ids = nation_ids if nation_ids is not None else self.nation_ids
        last = end_turn if end_turn is not None else self.max_turn

        for nid in ids:
            for turn in range(start_turn, last + 1):
                state = self.get_nation_turn(nid, turn)
                if state is None:
                    continue
                if skip_dead_after and not state.is_operational:
                    continue
                yield state

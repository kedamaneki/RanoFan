"""マクロ・クロニクル・エンジン（1ターン＝10年、計100ターン）."""

from __future__ import annotations

import random
from dataclasses import dataclass, field
from typing import Dict, List, Optional, Tuple

from .config import SimulationConfig
from .models import MacroLog, Nation, RegionState, REGIONS, TurnSnapshot
from .geo import jitter_near, nation_geo_dict
from .land_mask import snap_to_land
from .world import (
    REGION_THREAT_FACTORS,
    avg_domestic_threat,
    create_initial_world,
    living,
    total_human_power,
)

MACRO_TURNS = 100
YEARS_PER_TURN = 10
CHAPTER_SIZE = 10

# 領域間摩擦・吸収（世界観上は例外的な事象として低頻度）
FRICTION_PAIR_CHANCE = 0.03  # 旧 0.20 → 0.10
ABSORPTION_CHANCE = 0.03  # 摩擦発生時の吸収判定（旧 0.08）

# 周期位相による全球指数の乗数（初期全球脅威を基準に全ターン共通）
ACTIVE_THREAT_MUL = 1.08
DORMANT_THREAT_MUL = 0.62

# 20年周期をマクロ時間に換算: 2ターン＝20年、24ターン＝240年で一巡
CYCLE_LEN = 24
ACTIVE_HALF = 12


@dataclass
class MacroChronicleEngine:
    nations: Dict[int, Nation]
    regions: Dict[str, RegionState]
    config: SimulationConfig
    current_turn: int = 0
    logs: List[MacroLog] = field(default_factory=list)
    snapshots: List[TurnSnapshot] = field(default_factory=list)
    geo_snapshots: List[Dict[str, object]] = field(default_factory=list)
    next_birth_id: int = 0
    _rng: random.Random = field(default_factory=random.Random, repr=False)

    @classmethod
    def create(cls, config: SimulationConfig | None = None, seed: int | None = None) -> MacroChronicleEngine:
        cfg = config or SimulationConfig()
        if seed is not None:
            cfg.seed = seed
        rng = random.Random(cfg.seed)
        nations, regions = create_initial_world(rng, cfg)
        eng = cls(
            nations=nations,
            regions=regions,
            config=cfg,
            next_birth_id=cfg.initial_nations + 1,
            _rng=rng,
        )
        eng._apply_region_threat_levels(0)
        eng.snapshots.append(eng._snapshot(0))
        eng._push_geo_snapshot(0)
        return eng

    def global_threat_base(self) -> float:
        """設定の初期全球脅威を全ターンで基準とする。"""
        return self.config.initial_global_threat

    def global_threat_index(self, turn: int) -> float:
        base = self.global_threat_base()
        if self.cycle_phase(turn) == "active":
            return min(3.0, base * ACTIVE_THREAT_MUL)
        return max(0.05, base * DORMANT_THREAT_MUL)

    def narrative_year(self, turn: int) -> int:
        return turn * self.config.years_per_turn

    def cycle_phase(self, turn: int) -> str:
        if turn <= 0:
            return "active"
        pos = ((turn - 1) % CYCLE_LEN) + 1
        return "active" if pos <= ACTIVE_HALF else "dormant"

    def cycle_position(self, turn: int) -> int:
        if turn <= 0:
            return 0
        return ((turn - 1) % CYCLE_LEN) + 1

    def _apply_region_threat_levels(self, turn: int) -> None:
        gti = self.global_threat_index(turn)
        phase = self.cycle_phase(turn)
        for r_name, r in self.regions.items():
            idx = REGIONS.index(r_name)
            factor = REGION_THREAT_FACTORS[idx]
            r.threat_level = gti * factor * self._rng.uniform(0.95, 1.05)
            if phase == "active":
                r.spike = self._rng.uniform(0, 0.22 * max(0.5, gti))
            else:
                r.spike = self._rng.uniform(-0.12 * max(0.5, gti), 0.06 * max(0.5, gti))

    def _push_geo_snapshot(self, turn: int) -> None:
        snap = self._snapshot(turn)
        self.geo_snapshots.append(
            {
                "turn": turn,
                "narrative_year": turn * self.config.years_per_turn,
                "stats": snap.to_dict(),
                "nations": [nation_geo_dict(n) for n in sorted(self.nations.values(), key=lambda x: x.nation_id)],
            }
        )

    def _snapshot(self, turn: int) -> TurnSnapshot:
        gti = self.global_threat_index(turn)
        return TurnSnapshot(
            turn=turn,
            alive_count=len(living(self.nations)),
            total_human_power=total_human_power(self.nations),
            avg_threat=avg_domestic_threat(self.nations, self.regions),
            global_threat_index=gti,
            cycle_phase=self.cycle_phase(turn),
        )

    def _log(
        self,
        turn: int,
        event: str,
        category: str,
        nation_id: Optional[int] = None,
        nation_ids: Optional[List[int]] = None,
        region: Optional[str] = None,
        result: Optional[Dict[str, object]] = None,
    ) -> None:
        self.logs.append(
            MacroLog(turn, event, category, nation_id, nation_ids, region, result)
        )

    def _tick_regions(self, turn: int) -> None:
        gti = self.global_threat_index(turn)
        phase = self.cycle_phase(turn)
        self._apply_region_threat_levels(turn)
        self._log(
            turn,
            f"世界平均脅威度指数{gti:.3f}、周期位相{phase}（位相{self.cycle_position(turn)}/{CYCLE_LEN}）。",
            "threat_environment",
            result={"global_threat_index": round(gti, 3), "cycle_phase": phase},
        )
        if self.cycle_position(turn) in (1, ACTIVE_HALF + 1):
            if phase == "active":
                self._log(turn, "魔物活性期突入。", "cycle_transition")
            else:
                self._log(
                    turn,
                    "魔物休眠期（復興期）突入。全国家の国力自然増加バフ発動。",
                    "cycle_transition",
                )

    def _domestic_pass(self, turn: int) -> None:
        phase = self.cycle_phase(turn)
        deaths_this_turn = 0
        max_deaths = 5 if turn <= 25 else 4 if turn <= 55 else 3

        for n in living(self.nations):
            reg = self.regions[n.region]
            threat = reg.effective_threat() * n.domestic_threat_factor
            barrier = n.barrier_efficiency * (0.55 + n.magic * 0.0035)
            upkeep = threat * self._rng.uniform(1.2, 3.5)
            n.magic -= upkeep * max(0.35, 1.0 - barrier * 0.35)
            n.infrastructure -= threat * self._rng.uniform(0.01, 0.04)
            n.infrastructure = max(0.35, n.infrastructure)

            if phase == "dormant":
                boost = self._rng.uniform(2.5, 6.5) * n.innovation
                n.economy += boost
                n.military += boost * 0.7
                n.magic += boost * 0.8
                # 休眠期: 国内脅威係数が中立(1.0)へ戻り、滅亡後の平均が回復しやすくなる
                n.domestic_threat_factor += (1.0 - n.domestic_threat_factor) * 0.07

            if turn > 35 and self._rng.random() < 0.10 * n.innovation:
                n.barrier_efficiency += self._rng.uniform(0.02, 0.07)
                n.innovation += self._rng.uniform(0.01, 0.03)
                t_gain = self._rng.uniform(0.2, 0.6) * n.innovation
                n.territory += t_gain
                self._log(
                    turn,
                    f"障壁イノベーション（循環効率上昇、係数{n.barrier_efficiency:.2f}、生存圏+{t_gain:.1f}）。",
                    "innovation",
                    nation_id=n.nation_id,
                    region=n.region,
                )

            if self._rng.random() < min(0.30, threat * 0.085):
                if barrier >= threat * self._rng.uniform(0.75, 1.05):
                    loss = self._rng.uniform(0.5, 3.0) * threat
                    n.military -= loss
                    t_gain = self._rng.uniform(0.1, 0.3) * n.barrier_efficiency
                    n.territory += t_gain
                    n.domestic_threat_factor -= self._rng.uniform(0.008, 0.02)
                    self._log(
                        turn,
                        f"脅威侵攻を障壁網で拒否（軍事-{loss:.1f}、生存圏+{t_gain:.1f}）。",
                        "monster_defense",
                        nation_id=n.nation_id,
                        region=n.region,
                        result={"military_loss": round(loss, 1), "territory_gain": round(t_gain, 1)},
                    )
                else:
                    t_loss = self._rng.uniform(0.8, 4.5) * threat
                    m_loss = self._rng.uniform(1.5, 7.0) * threat
                    e_loss = self._rng.uniform(1.0, 5.5) * threat
                    n.territory -= t_loss
                    n.military -= m_loss
                    n.economy -= e_loss
                    n.domestic_threat_factor += self._rng.uniform(0.01, 0.03)
                    self._log(
                        turn,
                        f"脅威侵攻による生存圏損壊（領-{t_loss:.1f}、軍-{m_loss:.1f}、経-{e_loss:.1f}）。",
                        "monster_attack",
                        nation_id=n.nation_id,
                        region=n.region,
                    )

            n.clamp()
            if deaths_this_turn < max_deaths and (
                n.territory <= 0 or (n.military <= 0 and n.human_power < 25)
            ):
                self._mark_dead(n, turn, "脅威侵攻・障壁崩壊")
                deaths_this_turn += 1

    def _friction_pass(self, turn: int) -> None:
        by_region: Dict[str, List[Nation]] = {r: [] for r in REGIONS}
        for n in living(self.nations):
            by_region[n.region].append(n)

        for region, group in by_region.items():
            if len(group) < 2:
                continue
            pairs = 0
            max_pairs = min(5, len(group) // 3)
            shuffled = group[:]
            self._rng.shuffle(shuffled)
            for i in range(0, len(shuffled) - 1, 2):
                if pairs >= max_pairs:
                    break
                a, b = shuffled[i], shuffled[i + 1]
                if self._rng.random() > FRICTION_PAIR_CHANCE:
                    continue
                pairs += 1
                roll = self._rng.random()
                if roll < ABSORPTION_CHANCE and a.human_power > b.human_power * 1.8:
                    b.alive = False
                    b.died_turn = turn
                    b.death_cause = "領域統合（吸収）"
                    b.absorbed_by = a.nation_id
                    b.structural_change = "吸収統合"
                    b.structural_turn = turn
                    gain = b.human_power * 0.45
                    a.economy += gain * 0.4
                    a.military += gain * 0.35
                    a.magic += gain * 0.25
                    a.territory += b.territory * 0.5
                    self._log(
                        turn,
                        f"領域統合（吸収）。{b.name}→{a.name}へ構造統合。",
                        "absorption",
                        nation_ids=[a.nation_id, b.nation_id],
                        region=region,
                    )
                elif roll < 0.55:
                    winner = a if a.military + a.magic >= b.military + b.magic else b
                    loser = b if winner is a else a
                    l_loss = self._rng.uniform(5, 18)
                    w_loss = self._rng.uniform(2, 8)
                    loser.military -= l_loss
                    loser.territory -= self._rng.uniform(2, 7)
                    winner.military -= w_loss
                    self._log(
                        turn,
                        f"局地衝突（魔力結晶資源・防衛網主導権）。勝者:{winner.name}。",
                        "border_conflict",
                        nation_ids=[a.nation_id, b.nation_id],
                        region=region,
                        result={"winner": winner.nation_id},
                    )
                else:
                    delta = self._rng.uniform(-8, 8)
                    a.economy += delta * 0.3
                    b.economy -= delta * 0.2
                    self._log(
                        turn,
                        "隣接統治体間の摩擦（配給・結界供給路の争奪）。",
                        "friction",
                        nation_ids=[a.nation_id, b.nation_id],
                        region=region,
                    )

    def _mark_dead(self, n: Nation, turn: int, cause: str) -> None:
        if not n.alive:
            return
        n.alive = False
        n.died_turn = turn
        n.death_cause = cause
        self._log(
            turn,
            f"滅亡（{cause}）。歴史上の欠番として固定。",
            "collapse",
            nation_id=n.nation_id,
            region=n.region,
            result={"died_turn": turn, "narrative_year": self.narrative_year(turn)},
        )

    def _birth_pass(self, turn: int) -> None:
        if self._rng.random() > self.config.birth_chance:
            return
        dead = [n for n in self.nations.values() if not n.alive and n.died_turn == turn]
        if not dead:
            dead = [n for n in self.nations.values() if not n.alive]
        if not dead:
            return
        parent_region = self._rng.choice(REGIONS)
        parents = [n for n in living(self.nations) if n.region == parent_region]
        if not parents:
            parents = living(self.nations)
        if not parents:
            return
        parent = self._rng.choice(parents)
        ghost = self._rng.choice(dead) if dead else parent
        nid = self.next_birth_id
        self.next_birth_id += 1
        base = parent.human_power * self._rng.uniform(0.18, 0.32)
        lat, lng = jitter_near(ghost.lat, ghost.lng, self._rng)
        lat, lng = snap_to_land(lat, lng)
        new_n = Nation(
            nation_id=nid,
            region=parent_region,
            lat=lat,
            lng=lng,
            economy=base * 0.36,
            military=base * 0.34,
            magic=base * 0.30,
            territory=self._rng.uniform(18, 42),
            barrier_efficiency=self._rng.uniform(0.9, 1.2),
            infrastructure=self._rng.uniform(0.85, 1.1),
            innovation=self._rng.uniform(0.9, 1.2),
            domestic_threat_factor=self._rng.uniform(0.92, 1.12),
            born_turn=turn,
        )
        self.nations[nid] = new_n
        self._log(
            turn,
            f"新生（残民蜂起・大開拓による新拠点確立）。",
            "birth",
            nation_id=nid,
            region=parent_region,
            result={"parent_nation": parent.nation_id, "narrative_year": self.narrative_year(turn)},
        )

    def _late_era_growth(self, turn: int) -> None:
        if turn < 59:
            return
        mul = 1.0 + (turn - 58) * 0.03
        if turn >= 83:
            mul *= 1.2 + (turn - 82) * 0.022
        for n in living(self.nations):
            n.economy += self._rng.uniform(5.5, 17.0) * mul * n.innovation
            n.military += self._rng.uniform(4.0, 13.0) * mul
            n.magic += self._rng.uniform(5.0, 16.0) * mul
            n.infrastructure += self._rng.uniform(0.01, 0.04)
            n.clamp()

    def process_turn(self) -> List[MacroLog]:
        self.current_turn += 1
        turn = self.current_turn
        start = len(self.logs)

        self._tick_regions(turn)
        self._domestic_pass(turn)
        self._friction_pass(turn)
        self._birth_pass(turn)
        self._late_era_growth(turn)

        for n in living(self.nations):
            n.clamp()
            if n.territory <= 0 or (n.military <= 0 and n.human_power < 20):
                self._mark_dead(n, turn, "内政維持不能")

        snap = self._snapshot(turn)
        self.snapshots.append(snap)
        self._push_geo_snapshot(turn)
        self._log(
            turn,
            f"ターン終了統計：生存{snap.alive_count}、総国力{snap.total_human_power:.2f}、平均脅威{snap.avg_threat:.3f}。",
            "turn_summary",
            result=snap.to_dict(),
        )
        return self.logs[start:]

    def run(self, turns: int | None = None) -> None:
        n = turns if turns is not None else self.config.macro_turns
        for _ in range(n):
            self.process_turn()

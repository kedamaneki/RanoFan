"""可変規模の初期世界構築。"""

from __future__ import annotations

import random
from typing import Dict, List, Tuple

from .config import SimulationConfig
from .geo import assign_all_coords
from .models import Nation, REGIONS, RegionState

# 131カ国時の領域配分比率
_REGION_SHARES = (28, 28, 27, 24, 24)
REGION_THREAT_FACTORS = (1.05, 1.15, 1.0, 0.95, 1.1)


def _region_weights(count: int) -> List[str]:
    total_share = sum(_REGION_SHARES)
    counts = [max(1, round(count * s / total_share)) for s in _REGION_SHARES]
    while sum(counts) < count:
        idx = counts.index(min(counts))
        counts[idx] += 1
    while sum(counts) > count:
        idx = counts.index(max(counts))
        counts[idx] -= 1
    weights: List[str] = []
    for region, n in zip(REGIONS, counts):
        weights.extend([region] * n)
    return weights


def create_initial_world(
    rng: random.Random,
    config: SimulationConfig,
) -> Tuple[Dict[int, Nation], Dict[str, RegionState]]:
    count = config.initial_nations
    threat_base = config.initial_global_threat
    target_power = config.effective_initial_power()

    nations: Dict[int, Nation] = {}
    regions: Dict[str, RegionState] = {
        r: RegionState(name=r, threat_level=threat_base * f)
        for r, f in zip(REGIONS, REGION_THREAT_FACTORS)
    }

    region_assign = _region_weights(count)
    rng.shuffle(region_assign)

    raw_powers: List[float] = []
    top_n = max(3, count // 26)
    for i in range(1, count + 1):
        region = region_assign[i - 1]
        base = rng.uniform(120, 180)
        if i <= top_n:
            base = rng.uniform(220, 320)
        raw_powers.append(base)
        nations[i] = Nation(
            nation_id=i,
            region=region,
            economy=base * rng.uniform(0.32, 0.38),
            military=base * rng.uniform(0.30, 0.36),
            magic=base * rng.uniform(0.28, 0.34),
            territory=rng.uniform(25, 50),
            barrier_efficiency=rng.uniform(0.85, 1.25),
            infrastructure=rng.uniform(0.85, 1.2),
            innovation=rng.uniform(0.8, 1.3),
            domestic_threat_factor=rng.uniform(0.75, 1.35),
            born_turn=0,
        )

    scale = target_power / sum(raw_powers)
    for n in nations.values():
        n.economy *= scale
        n.military *= scale
        n.magic *= scale

    total = sum(n.human_power for n in nations.values())
    if total > 0:
        fix = target_power / total
        for n in nations.values():
            n.economy *= fix
            n.military *= fix
            n.magic *= fix
            n.clamp()

    assign_all_coords(nations, rng, min_dist_deg=config.placement_min_dist_deg)
    return nations, regions


def living(nations: Dict[int, Nation]) -> List[Nation]:
    return [n for n in nations.values() if n.alive]


def total_human_power(nations: Dict[int, Nation]) -> float:
    return sum(n.human_power for n in living(nations))


def avg_domestic_threat(nations: Dict[int, Nation], regions: Dict[str, RegionState]) -> float:
    alive = living(nations)
    if not alive:
        return 0.0
    return sum(regions[n.region].effective_threat() * n.domestic_threat_factor for n in alive) / len(alive)


def next_nation_id(nations: Dict[int, Nation]) -> int:
    return max(nations.keys()) + 1

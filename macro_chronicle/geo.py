"""1925447.jpg（メルカトル世界地図）の投影定数と変換。"""

from __future__ import annotations

import math
import random
from typing import Dict, List, Tuple

from .land_mask import is_on_land, snap_to_land
from .map_projection import CENTRAL_MERIDIAN, LAT_MAX, LAT_MIN, map_meta
from .models import Nation, REGIONS

REGION_ANCHORS: Dict[str, List[Tuple[float, float]]] = {
    "領域・西": [
        (48.0, 2.0),
        (42.0, -3.0),
        (55.0, 12.0),
        (40.0, -75.0),
        (34.0, -118.0),
        (19.0, -99.0),
        (-15.0, -58.0),
        (-23.0, -46.0),
        (51.0, -0.5),
        (38.0, -122.0),
    ],
    "領域・東": [
        (36.0, 138.0),
        (43.0, 141.0),
        (35.5, 127.0),
        (55.0, 135.0),
        (45.0, 142.0),
        (30.0, 120.0),
        (22.0, 114.0),
        (50.0, 143.0),
        (38.0, 128.0),
    ],
    "領域・中央": [
        (30.0, 31.0),
        (35.0, 51.0),
        (40.0, 65.0),
        (25.0, 45.0),
        (15.0, 44.0),
        (33.0, 36.0),
        (45.0, 28.0),
        (20.0, 32.0),
    ],
    "領域・南": [
        (20.0, 78.0),
        (-6.0, 106.0),
        (-25.0, 135.0),
        (-30.0, 25.0),
        (10.0, -5.0),
        (-15.0, -47.0),
        (-33.0, 151.0),
        (5.0, 115.0),
        (-12.0, -77.0),
    ],
    "領域・北": [
        (65.0, 25.0),
        (70.0, 90.0),
        (60.0, -120.0),
        (55.0, -100.0),
        (72.0, 50.0),
        (64.0, -150.0),
        (58.0, 105.0),
        (68.0, 33.0),
    ],
}

# 後方互換（旧定数）
PACIFIC_MAP_BOUNDS = [[LAT_MIN, -40.0], [LAT_MAX, 320.0]]
PACIFIC_CENTER_LNG = CENTRAL_MERIDIAN


def _dist_deg(a: Tuple[float, float], b: Tuple[float, float]) -> float:
    return math.hypot(a[0] - b[0], a[1] - b[1])


def assign_all_coords(
    nations: Dict[int, Nation],
    rng: random.Random,
    min_dist_deg: float = 1.5,
) -> None:
    placed: List[Tuple[float, float]] = []
    for n in sorted(nations.values(), key=lambda x: x.nation_id):
        anchors = REGION_ANCHORS.get(n.region, REGION_ANCHORS["領域・中央"])
        assigned = False
        for _ in range(120):
            lat0, lng0 = rng.choice(anchors)
            lat = max(-55.0, min(75.0, lat0 + rng.uniform(-4.0, 4.0)))
            lng = lng0 + rng.uniform(-5.0, 5.0)
            lat, lng = snap_to_land(lat, lng)
            if not is_on_land(lat, lng):
                continue
            if all(_dist_deg((lat, lng), p) >= min_dist_deg for p in placed):
                n.lat, n.lng = lat, lng
                placed.append((lat, lng))
                assigned = True
                break
        if not assigned:
            for lat0, lng0 in anchors:
                lat, lng = snap_to_land(lat0, lng0, max_radius=180)
                if is_on_land(lat, lng):
                    n.lat, n.lng = lat, lng
                    placed.append((lat, lng))
                    assigned = True
                    break
        if not assigned:
            lat, lng = snap_to_land(anchors[0][0], anchors[0][1], max_radius=220)
            n.lat, n.lng = lat, lng
            placed.append((lat, lng))


def jitter_near(
    lat: float, lng: float, rng: random.Random, spread_lat: float = 3.0, spread_lng: float = 4.0
) -> Tuple[float, float]:
    return (
        max(-55.0, min(75.0, lat + rng.uniform(-spread_lat, spread_lat))),
        lng + rng.uniform(-spread_lng, spread_lng),
    )


def nation_geo_dict(n: Nation) -> Dict[str, object]:
    return {
        "id": n.nation_id,
        "name": n.name,
        "region": n.region,
        "lat": round(n.lat, 4),
        "lng": round(n.lng, 4),
        "alive": n.alive,
        "territory": round(n.territory, 1),
        "power": round(n.human_power, 1),
        "economy": round(n.economy, 1),
        "military": round(n.military, 1),
        "magic": round(n.magic, 1),
        "barrier_efficiency": round(n.barrier_efficiency, 2),
        "died_turn": n.died_turn,
        "born_turn": n.born_turn,
        "absorbed_by": n.absorbed_by,
    }


def export_geo_payload(eng, config=None) -> Dict[str, object]:
    from .config import SimulationConfig

    cfg = config or getattr(eng, "config", None) or SimulationConfig()
    ypt = cfg.years_per_turn
    return {
        "meta": {
            "turns": cfg.macro_turns,
            "years_per_turn": ypt,
            "initial_nations": cfg.initial_nations,
            "seed": cfg.seed,
            "config": cfg.to_dict(),
            "map": map_meta(),
            "regions": list(REGIONS),
        },
        "snapshots": eng.geo_snapshots,
        "logs": [e.to_dict() for e in eng.logs],
    }

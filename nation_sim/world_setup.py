#!/usr/bin/env python3
"""world_setup.json を読み込んで SimulationEngine を構築する。"""

from __future__ import annotations

import json
from pathlib import Path
from typing import Dict, Tuple

from .engine import SimulationEngine
from .models import NationalPower, Region, State


def _init_relations(
    states: Dict[str, State], pairs: Dict[Tuple[str, str], float]
) -> None:
    for (a, b), value in pairs.items():
        states[a].set_relation(b, value)
        states[b].set_relation(a, value)


def load_world_from_json(path: str | Path, seed: int | None = 42) -> SimulationEngine:
    """ビジュアライザーが出力した world_setup.json から世界を構築する。"""
    data = json.loads(Path(path).read_text(encoding="utf-8"))

    regions: Dict[str, Region] = {}
    for r in data.get("regions", []):
        rid = r["id"]
        regions[rid] = Region(name=r["name"], threat_level=float(r.get("threat_level", 1.0)))

    states: Dict[str, State] = {}
    for feat in data.get("geojson", {}).get("features", []):
        props = feat.get("properties", {})
        name = props.get("name") or props.get("id")
        if not name:
            continue
        states[name] = State(
            name=name,
            power=NationalPower(
                economy=float(props.get("economy", 50)),
                military=float(props.get("military", 50)),
            ),
            magic_level=float(props.get("magic_level", 30)),
            territory_size=float(props.get("territory_size", 60)),
            government=str(props.get("government", "君主制")),
            region_id=str(props.get("region_id", "unknown")),
            domestic_threat_factor=float(props.get("domestic_threat_factor", 1.0)),
        )

    return SimulationEngine(states=states, regions=regions, seed=seed)


def create_initial_world(seed: int | None = 42) -> SimulationEngine:
    """魔法文明が広がった5大国・4地域からスタートする世界を生成する（組み込み）。"""
    regions: Dict[str, Region] = {
        "central": Region(name="中央大陸", threat_level=1.4),
        "frontier": Region(name="北方辺境", threat_level=1.7),
        "desert": Region(name="南方砂海", threat_level=1.2),
        "islands": Region(name="西方諸島", threat_level=1.0),
    }

    states: Dict[str, State] = {
        "アルカディア連合": State(
            name="アルカディア連合",
            power=NationalPower(economy=72.0, military=65.0),
            magic_level=78.0,
            territory_size=85.0,
            government="魔導議会制共和国",
            region_id="central",
            domestic_threat_factor=1.1,
        ),
        "鉄血帝国": State(
            name="鉄血帝国",
            power=NationalPower(economy=68.0, military=88.0),
            magic_level=55.0,
            territory_size=92.0,
            government="軍事独裁制",
            region_id="central",
            domestic_threat_factor=1.3,
        ),
        "蒼穹王国": State(
            name="蒼穹王国",
            power=NationalPower(economy=80.0, military=58.0),
            magic_level=70.0,
            territory_size=78.0,
            government="立憲君主制",
            region_id="islands",
            domestic_threat_factor=0.9,
        ),
        "砂海諸邦": State(
            name="砂海諸邦",
            power=NationalPower(economy=75.0, military=52.0),
            magic_level=62.0,
            territory_size=70.0,
            government="都市国家連合",
            region_id="desert",
            domestic_threat_factor=1.0,
        ),
        "緑嶺自治領": State(
            name="緑嶺自治領",
            power=NationalPower(economy=60.0, military=48.0),
            magic_level=85.0,
            territory_size=65.0,
            government="氏族長会議制",
            region_id="frontier",
            domestic_threat_factor=1.2,
        ),
    }

    _init_relations(
        states,
        {
            ("アルカディア連合", "蒼穹王国"): 45.0,
            ("アルカディア連合", "緑嶺自治領"): 35.0,
            ("アルカディア連合", "砂海諸邦"): 20.0,
            ("アルカディア連合", "鉄血帝国"): -15.0,
            ("蒼穹王国", "緑嶺自治領"): 30.0,
            ("蒼穹王国", "砂海諸邦"): 10.0,
            ("蒼穹王国", "鉄血帝国"): -25.0,
            ("砂海諸邦", "緑嶺自治領"): 15.0,
            ("砂海諸邦", "鉄血帝国"): -10.0,
            ("緑嶺自治領", "鉄血帝国"): -35.0,
        },
    )

    return SimulationEngine(states=states, regions=regions, seed=seed)

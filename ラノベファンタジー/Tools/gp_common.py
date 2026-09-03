#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""フェーズG 大国追補 simulationEvents 生成の共通ヘルパー。"""

from __future__ import annotations

import json
import os

from spine_common import prosperity, validate_spine_events

BASE = os.path.join(
    os.path.dirname(os.path.dirname(os.path.abspath(__file__))),
    "Assets",
    "Resources",
    "HistorySimulation",
)
OUT_DIR = os.path.join(BASE, "greatpowers")
SPINE_DIR = os.path.join(BASE, "spine")
NATIONS_DIR = os.path.join(BASE, "nations")
SECTIONS_DIR = os.path.join(BASE, "sections")

GP_META: dict[str, dict] = {
    "G1": {
        "title": "国家001・初期統合の兆し",
        "turns": (1, 20),
        "output": "NATION_001",
        "source": "歴史書その6.txt + その1.md",
    },
    "G2": {
        "title": "国家001・焦土期・絶対防衛圏",
        "turns": (21, 60),
        "output": "NATION_001",
        "source": "歴史書その6.txt + その2-3.md",
    },
    "G3": {
        "title": "国家001・統合王国・大遠征",
        "turns": (61, 100),
        "output": "NATION_001",
        "source": "歴史書その6.txt + その4-5.md",
    },
    "G4": {
        "title": "国家002・障壁防衛網",
        "turns": (1, 20),
        "output": "NATION_002",
        "source": "歴史書その6.txt + その1.md",
    },
    "G5": {
        "title": "国家002・焦土・深層魔導都市",
        "turns": (21, 60),
        "output": "NATION_002",
        "source": "歴史書その6.txt + その2-3.md",
    },
    "G6": {
        "title": "国家002・魔導大国・双極秩序",
        "turns": (61, 100),
        "output": "NATION_002",
        "source": "歴史書その6.txt + その4-5.md",
    },
    "G7": {
        "title": "国家015・分散防衛・第3章",
        "turns": (21, 60),
        "output": "NATION_015",
        "source": "歴史書その8.txt + その3.md",
    },
    "G8": {
        "title": "国家001×002・双極外交",
        "turns": (80, 100),
        "output": "DUAL_001_002",
        "source": "歴史書その4-5.md",
    },
    "G9": {
        "title": "世界・最終データ",
        "turns": (95, 100),
        "output": "WORLD",
        "source": "歴史書その5.md",
    },
    "G10": {
        "title": "世界・欠番・滅亡統計",
        "turns": (1, 100),
        "output": "WORLD",
        "source": "turn_index + 歴史棚",
    },
}

OUTPUT_FILES: dict[str, str] = {
    "NATION_001": "SIM_GP_NATION_001.json",
    "NATION_002": "SIM_GP_NATION_002.json",
    "NATION_015": "SIM_GP_NATION_015.json",
    "DUAL_001_002": "SIM_GP_DUAL_001_002.json",
    "WORLD": "SIM_GP_WORLD.json",
}


def evolution(macro_turn: int) -> str:
    if macro_turn <= 40:
        return "Ritual"
    if macro_turn <= 60:
        return "Inspiration"
    return "Recipe"


def _gp_id(batch: str, target: str, event_type: str, year: int) -> str:
    tid = "WORLD" if target == "WORLD" else target.replace("NATION_", "N")
    return f"SIM_EVT_GP_{batch}_{tid}_{event_type}_{year}"


def gp_nation_evt(
    batch: str,
    nation: str,
    macro_turn: int,
    event_type: str,
    category: str,
    log: str,
    lore: str,
    p1d: str,
    p1b: str,
    p2d: str,
    p2v: str,
    p3d: str,
    p3r: str,
    related: str = "",
    month: int = 6,
    day: int = 15,
    hour: int = 10,
    hist_suffix: str = "",
    alt_flag: str = "",
    alt_log: str = "",
    alt_lore: str = "",
    zone: str = "",
    mmo_flag: str = "",
    level: int = 8,
    prereq_type: str = "ShieldReinforcementCount",
    prereq_val: int = 6,
) -> dict:
    year = macro_turn * 10
    suffix = hist_suffix or event_type.upper()
    num = nation.split("_")[1]
    return {
        "eventId": _gp_id(batch, nation, event_type, year),
        "macroTurn": macro_turn,
        "timestamp": {"year": year, "month": month, "day": day, "hour": hour},
        "targetNationId": nation,
        "relatedNationId": related,
        "eventCategory": category,
        "eventType": event_type,
        "logMessageTemplate": log,
        "isHistoricalCollapseRoute": False,
        "evolutionSystemType": evolution(macro_turn),
        "microLore": lore,
        "prosperityTimeline": prosperity(macro_turn, p1d, p1b, p2d, p2v, p3d, p3r),
        "historicalMode": {
            "timeLockedHourOffset": 12,
            "forcedFlagsOnTrigger": [
                f"HIST_{year:04d}_{month:02d}_{day:02d}_GP_{suffix}"
            ],
        },
        "alternativeMode": {
            "causalPrerequisites": {
                "requiredLogType": prereq_type,
                "requiredValue": prereq_val,
            },
            "unlockRewards": {
                "alternativeFlag": alt_flag or f"ALT_GP_{num}_{suffix}",
                "alternativeLogMessage": alt_log,
                "alternativeLore": alt_lore,
            },
        },
        "mmoMode": {
            "zoneId": zone,
            "recommendedLevel": level,
            "progressTriggerFlag": mmo_flag,
        },
    }


def gp_world_evt(
    batch: str,
    macro_turn: int,
    event_type: str,
    category: str,
    log: str,
    lore: str,
    p1d: str,
    p1b: str,
    p2d: str,
    p2v: str,
    p3d: str,
    p3r: str,
    month: int = 6,
    day: int = 15,
    hour: int = 10,
    hist_suffix: str = "",
    alt_flag: str = "",
    alt_log: str = "",
    alt_lore: str = "",
    zone: str = "",
    mmo_flag: str = "",
    level: int = 8,
    prereq_type: str = "MonsterEradicatedCount",
    prereq_val: int = 5,
) -> dict:
    year = macro_turn * 10
    suffix = hist_suffix or event_type.upper()
    return {
        "eventId": _gp_id(batch, "WORLD", event_type, year),
        "macroTurn": macro_turn,
        "timestamp": {"year": year, "month": month, "day": day, "hour": hour},
        "targetNationId": "WORLD",
        "relatedNationId": "",
        "eventCategory": category,
        "eventType": event_type,
        "logMessageTemplate": log,
        "isHistoricalCollapseRoute": False,
        "evolutionSystemType": evolution(macro_turn),
        "microLore": lore,
        "prosperityTimeline": prosperity(macro_turn, p1d, p1b, p2d, p2v, p3d, p3r),
        "historicalMode": {
            "timeLockedHourOffset": 12,
            "forcedFlagsOnTrigger": [
                f"HIST_{year:04d}_{month:02d}_{day:02d}_GP_{suffix}"
            ],
        },
        "alternativeMode": {
            "causalPrerequisites": {
                "requiredLogType": prereq_type,
                "requiredValue": prereq_val,
            },
            "unlockRewards": {
                "alternativeFlag": alt_flag or f"ALT_GP_WORLD_{suffix}",
                "alternativeLogMessage": alt_log,
                "alternativeLore": alt_lore,
            },
        },
        "mmoMode": {
            "zoneId": zone,
            "recommendedLevel": level,
            "progressTriggerFlag": mmo_flag,
        },
    }


def _collect_pairs_from_dir(dir_path: str) -> set[tuple[str, int]]:
    pairs: set[tuple[str, int]] = set()
    if not os.path.isdir(dir_path):
        return pairs
    for name in os.listdir(dir_path):
        if not name.endswith(".json"):
            continue
        with open(os.path.join(dir_path, name), encoding="utf-8") as f:
            for e in json.load(f).get("simulationEvents", []):
                tid = e.get("targetNationId", "")
                if tid and tid != "WORLD":
                    pairs.add((tid, e["macroTurn"]))
    return pairs


def load_occupied_nation_turns() -> set[tuple[str, int]]:
    """spine / nations / sections の (nation, turn)。gp 自身は除外（再生成可能）。"""
    pairs: set[tuple[str, int]] = set()
    for d in (SPINE_DIR, NATIONS_DIR, SECTIONS_DIR):
        pairs |= _collect_pairs_from_dir(d)
    return pairs


def validate_gp_batch(batch: str, events: list[dict]) -> list[str]:
    errors = list(validate_spine_events(events))
    meta = GP_META.get(batch)
    if not meta:
        errors.append(f"unknown batch {batch}")
        return errors
    lo, hi = meta["turns"]
    for e in events:
        t = e["macroTurn"]
        if t < lo or t > hi:
            errors.append(
                f"{e['eventId']}: macroTurn {t} outside batch range T{lo}-T{hi}"
            )
    nation_turns = [
        (e["targetNationId"], e["macroTurn"])
        for e in events
        if e.get("targetNationId") not in ("", "WORLD")
    ]
    if len(nation_turns) != len(set(nation_turns)):
        errors.append(f"duplicate nation+macroTurn within batch: {nation_turns}")
    occupied = load_occupied_nation_turns()
    for tid, t in nation_turns:
        if (tid, t) in occupied:
            errors.append(
                f"({tid}, T{t}) already in spine/nations/sections/gp — pick another turn"
            )
    return errors


def write_gp_output(output_key: str, events: list[dict]) -> str:
    os.makedirs(OUT_DIR, exist_ok=True)
    fname = OUTPUT_FILES[output_key]
    events = sorted(events, key=lambda e: (e["macroTurn"], e.get("targetNationId", "")))
    path = os.path.join(OUT_DIR, fname)
    with open(path, "w", encoding="utf-8") as f:
        json.dump({"simulationEvents": events}, f, ensure_ascii=False, indent=2)
    print(f"Wrote {path} ({len(events)} events)")
    return path

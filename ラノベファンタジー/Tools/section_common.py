#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""フェーズM 章・節追補 simulationEvents 生成の共通ヘルパー。"""

from __future__ import annotations

import json
import os

from spine_common import prosperity, validate_spine_events

OUT_DIR = os.path.join(
    os.path.dirname(os.path.dirname(os.path.abspath(__file__))),
    "Assets",
    "Resources",
    "HistorySimulation",
    "sections",
)
SPINE_DIR = os.path.join(
    os.path.dirname(OUT_DIR),
    "spine",
)

# 章・節メタデータ（全20節）
SECTION_META: dict[tuple[int, int], dict] = {
    (1, 1): {"title": "高脅威周期の到来と初期生存環境の激変", "turns": (1, 4), "source": "歴史書その1.md"},
    (1, 2): {"title": "領域・東におけるエネルギー障壁防衛戦", "turns": (5, 10), "source": "歴史書その1.md"},
    (1, 3): {"title": "領域・西における統治体の動揺と初期統合への兆し", "turns": (11, 20), "source": "歴史書その1.md"},
    (1, 4): {"title": "第2世紀末における人類総国力の推移と構造淘汰", "turns": (18, 20), "source": "歴史書その1.md"},
    (2, 1): {"title": "百国時代への凋落と生存闘争の過激化", "turns": (21, 25), "source": "歴史書その2.md"},
    (2, 2): {"title": "領域・西における人間同士の領域統合と対立", "turns": (28, 32), "source": "歴史書その2.md"},
    (2, 3): {"title": "領域・東の疲弊と中央海平原における焦土戦術", "turns": (35, 39), "source": "歴史書その2.md"},
    (2, 4): {"title": "第4世紀末における人類の総力と構造的転換", "turns": (38, 40), "source": "歴史書その2.md"},
    (3, 1): {"title": "総国力最底期における文明の構造的沈降", "turns": (41, 50), "source": "歴史書その3.md"},
    (3, 2): {"title": "領域・西における絶対防衛線の維持と多極化", "turns": (45, 54), "source": "歴史書その3.md"},
    (3, 3): {"title": "領域・東の深層魔導都市への移行と防衛構造", "turns": (48, 58), "source": "歴史書その3.md"},
    (3, 4): {"title": "第6世紀末における生存構造の総力戦的限界", "turns": (57, 60), "source": "歴史書その3.md"},
    (4, 1): {"title": "低脅威周期への移行と大開拓時代の開幕", "turns": (61, 65), "source": "歴史書その4.md"},
    (4, 2): {"title": "領域・西における絶対覇権の確立と統合王国の誕生", "turns": (68, 75), "source": "歴史書その4.md"},
    (4, 3): {"title": "領域・東の深層からの解放と魔導工学の新展開", "turns": (63, 70), "source": "歴史書その4.md"},
    (4, 4): {"title": "第8世紀末における世界秩序の再編と構造的集約", "turns": (78, 80), "source": "歴史書その4.md"},
    (5, 1): {"title": "低脅威周期の継続と千年紀末における生存国家の変容", "turns": (81, 85), "source": "歴史書その5.md"},
    (5, 2): {"title": "領域・西における大遠征の敢行と脅威の完全駆逐", "turns": (85, 95), "source": "歴史書その5.md"},
    (5, 3): {"title": "領域・東と各領域の再興、および双極秩序の形成", "turns": (87, 94), "source": "歴史書その5.md"},
    (5, 4): {"title": "第10世紀末におけるシミュレーションログの結末", "turns": (98, 100), "source": "歴史書その5.md"},
}


def evolution(macro_turn: int) -> str:
    if macro_turn <= 40:
        return "Ritual"
    if macro_turn <= 60:
        return "Inspiration"
    return "Recipe"


def _sec_id(chapter: int, section: int, target: str, event_type: str, year: int) -> str:
    tid = "WORLD" if target == "WORLD" else target
    return f"SIM_EVT_SEC_CH{chapter:02d}_S{section:02d}_{tid}_{event_type}_{year}"


def sec_world_evt(
    chapter: int,
    section: int,
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
        "eventId": _sec_id(chapter, section, "WORLD", event_type, year),
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
                f"HIST_{year:04d}_{month:02d}_{day:02d}_SEC_{suffix}"
            ],
        },
        "alternativeMode": {
            "causalPrerequisites": {
                "requiredLogType": prereq_type,
                "requiredValue": prereq_val,
            },
            "unlockRewards": {
                "alternativeFlag": alt_flag or f"ALT_SEC_CH{chapter:02d}_S{section:02d}_{suffix}",
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


def sec_nation_evt(
    chapter: int,
    section: int,
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
        "eventId": _sec_id(chapter, section, nation, event_type, year),
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
                f"HIST_{year:04d}_{month:02d}_{day:02d}_SEC_{suffix}"
            ],
        },
        "alternativeMode": {
            "causalPrerequisites": {
                "requiredLogType": prereq_type,
                "requiredValue": prereq_val,
            },
            "unlockRewards": {
                "alternativeFlag": alt_flag or f"ALT_SEC_{num}_{suffix}",
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


def write_section(chapter: int, section: int, events: list[dict]) -> str:
    os.makedirs(OUT_DIR, exist_ok=True)
    events = sorted(events, key=lambda e: (e["macroTurn"], e.get("targetNationId", "")))
    path = os.path.join(OUT_DIR, f"SIM_CH{chapter:02d}_SEC{section:02d}.json")
    with open(path, "w", encoding="utf-8") as f:
        json.dump({"simulationEvents": events}, f, ensure_ascii=False, indent=2)
    print(f"Wrote {path} ({len(events)} events)")
    return path


def load_spine_nation_turns() -> set[tuple[str, int]]:
    """脊髄 JSON に既にある (targetNationId, macroTurn) を収集（重複回避用）。"""
    pairs: set[tuple[str, int]] = set()
    if not os.path.isdir(SPINE_DIR):
        return pairs
    for name in os.listdir(SPINE_DIR):
        if not name.startswith("SIM_T") or not name.endswith(".json"):
            continue
        with open(os.path.join(SPINE_DIR, name), encoding="utf-8") as f:
            for e in json.load(f).get("simulationEvents", []):
                tid = e.get("targetNationId", "")
                if tid and tid != "WORLD":
                    pairs.add((tid, e["macroTurn"]))
    return pairs


def validate_section_events(events: list[dict], chapter: int, section: int) -> list[str]:
    errors = list(validate_spine_events(events))
    meta = SECTION_META.get((chapter, section))
    if meta:
        lo, hi = meta["turns"]
        for e in events:
            t = e["macroTurn"]
            if t < lo or t > hi:
                errors.append(
                    f"{e['eventId']}: macroTurn {t} outside section range T{lo}-T{hi}"
                )
    nation_turns = [
        (e["targetNationId"], e["macroTurn"])
        for e in events
        if e.get("targetNationId") not in ("", "WORLD")
    ]
    if len(nation_turns) != len(set(nation_turns)):
        errors.append(f"duplicate nation+macroTurn within section: {nation_turns}")
    spine_pairs = load_spine_nation_turns()
    for tid, t in nation_turns:
        if (tid, t) in spine_pairs:
            errors.append(
                f"({tid}, T{t}) already in spine — pick another turn for section event"
            )
    return errors

#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""フェーズN 深掘り simulationEvents 生成の共通ヘルパー。"""

from __future__ import annotations

import json
import os

OUT_DIR = os.path.join(
    os.path.dirname(os.path.dirname(os.path.abspath(__file__))),
    "Assets",
    "Resources",
    "HistorySimulation",
    "nations",
)


def evolution(macro_turn: int) -> str:
    if macro_turn <= 40:
        return "Ritual"
    if macro_turn <= 60:
        return "Inspiration"
    return "Recipe"


def prosperity(
    macro_turn: int,
    p1_dialogue: str,
    p1_buff: str,
    p2_dialogue: str,
    p2_visual: str,
    p3_dialogue: str,
    p3_reward: str,
) -> dict:
    y0 = (macro_turn - 1) * 10 + 1
    y3 = macro_turn * 10
    y1 = y0 + 2
    y2 = y0 + 6
    return {
        "phase1_accumulation": {
            "timeRange": f"叙事{y0}年〜{y1}年（1年目〜3年目）",
            "npcDialogue": p1_dialogue,
            "statusBuff": p1_buff,
        },
        "phase2_pioneering": {
            "timeRange": f"叙事{y1 + 1}年〜{y2}年（4年目〜7年目）",
            "npcDialogue": p2_dialogue,
            "visualChange": p2_visual,
        },
        "phase3_perfection": {
            "timeRange": f"叙事{y2 + 1}年〜{y3}年（8年目〜10年目）",
            "npcDialogue": p3_dialogue,
            "gameplayReward": p3_reward,
        },
    }


def evt(
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
    level: int = 10,
    prereq_type: str = "MonsterEradicatedCount",
    prereq_val: int = 8,
) -> dict:
    year = macro_turn * 10
    suffix = hist_suffix or event_type.upper()
    return {
        "eventId": f"SIM_EVT_{nation}_{event_type}_{year}",
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
                f"HIST_{year:04d}_{month:02d}_{day:02d}_{suffix}"
            ],
        },
        "alternativeMode": {
            "causalPrerequisites": {
                "requiredLogType": prereq_type,
                "requiredValue": prereq_val,
            },
            "unlockRewards": {
                "alternativeFlag": alt_flag or f"ALT_{nation}_{suffix}",
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


def write_nations(nations: dict[str, list[dict]]) -> None:
    os.makedirs(OUT_DIR, exist_ok=True)
    for nation_id, events in nations.items():
        num = nation_id.split("_")[1]
        path = os.path.join(OUT_DIR, f"SIM_NATION_{num}.json")
        events = sorted(events, key=lambda e: e["macroTurn"])
        with open(path, "w", encoding="utf-8") as f:
            json.dump({"simulationEvents": events}, f, ensure_ascii=False, indent=2)
        print(f"Wrote {path} ({len(events)} events)")


def decay(
    collapse_year: int,
    p1_dialogue: str,
    p1_debuff: str,
    p2_dialogue: str,
    p2_visual: str,
    p3_dialogue: str,
    p3_risk: str,
) -> dict:
    y0 = collapse_year - 9
    return {
        "phase1_premonition": {
            "timeRange": f"叙事{collapse_year - 9}年〜{collapse_year - 7}年（滅亡10〜7年前）",
            "npcDialogue": p1_dialogue,
            "statusDebuff": p1_debuff,
        },
        "phase2_poverty": {
            "timeRange": f"叙事{collapse_year - 6}年〜{collapse_year - 3}年（6〜3年前）",
            "npcDialogue": p2_dialogue,
            "visualChange": p2_visual,
        },
        "phase3_critical": {
            "timeRange": f"叙事{collapse_year - 2}年〜{collapse_year}年（2年前〜崩壊）",
            "npcDialogue": p3_dialogue,
            "gameplayRisk": p3_risk,
        },
    }


def collapse_evt(
    nation: str,
    macro_turn: int,
    log: str,
    lore: str,
    p1d: str,
    p1db: str,
    p2d: str,
    p2v: str,
    p3d: str,
    p3r: str,
    related: str = "",
    month: int = 11,
    day: int = 3,
    hour: int = 4,
    hist_suffix: str = "NATION_COLLAPSE",
    alt_flag: str = "",
    alt_log: str = "",
    alt_lore: str = "",
    zone: str = "",
    mmo_flag: str = "",
    level: int = 18,
    prereq_type: str = "ShieldReinforcementCount",
    prereq_val: int = 10,
) -> dict:
    year = macro_turn * 10
    suffix = hist_suffix
    return {
        "eventId": f"SIM_EVT_{nation}_NationCollapse_{year}",
        "macroTurn": macro_turn,
        "timestamp": {"year": year, "month": month, "day": day, "hour": hour},
        "targetNationId": nation,
        "relatedNationId": related,
        "eventCategory": "lifecycle",
        "eventType": "NationCollapse",
        "logMessageTemplate": log,
        "isHistoricalCollapseRoute": True,
        "evolutionSystemType": evolution(macro_turn),
        "microLore": lore,
        "decayTimeline": decay(year, p1d, p1db, p2d, p2v, p3d, p3r),
        "historicalMode": {
            "timeLockedHourOffset": 24,
            "forcedFlagsOnTrigger": [
                f"HIST_{year:04d}_{month:02d}_{day:02d}_{suffix}",
            ],
        },
        "alternativeMode": {
            "causalPrerequisites": {
                "requiredLogType": prereq_type,
                "requiredValue": prereq_val,
            },
            "unlockRewards": {
                "alternativeFlag": alt_flag or f"ALT_{nation}_SHIELD_REINFORCED",
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


def birth_evt(
    nation: str,
    macro_turn: int,
    log: str,
    lore: str,
    p1d: str,
    p1b: str,
    p2d: str,
    p2v: str,
    p3d: str,
    p3r: str,
    related: str = "",
    month: int = 4,
    day: int = 1,
    hour: int = 8,
    hist_suffix: str = "NATION_BIRTH",
    alt_flag: str = "",
    alt_log: str = "",
    alt_lore: str = "",
    zone: str = "",
    mmo_flag: str = "",
    level: int = 12,
    prereq_type: str = "MonsterEradicatedCount",
    prereq_val: int = 5,
) -> dict:
    year = macro_turn * 10
    suffix = hist_suffix
    return {
        "eventId": f"SIM_EVT_{nation}_NationBirth_{year}",
        "macroTurn": macro_turn,
        "timestamp": {"year": year, "month": month, "day": day, "hour": hour},
        "targetNationId": nation,
        "relatedNationId": related,
        "eventCategory": "lifecycle",
        "eventType": "NationBirth",
        "logMessageTemplate": log,
        "isHistoricalCollapseRoute": False,
        "evolutionSystemType": evolution(macro_turn),
        "microLore": lore,
        "prosperityTimeline": prosperity(macro_turn, p1d, p1b, p2d, p2v, p3d, p3r),
        "historicalMode": {
            "timeLockedHourOffset": 12,
            "forcedFlagsOnTrigger": [
                f"HIST_{year:04d}_{month:02d}_{day:02d}_{suffix}",
            ],
        },
        "alternativeMode": {
            "causalPrerequisites": {
                "requiredLogType": prereq_type,
                "requiredValue": prereq_val,
            },
            "unlockRewards": {
                "alternativeFlag": alt_flag or f"ALT_{nation}_BIRTH_SURVIVED",
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

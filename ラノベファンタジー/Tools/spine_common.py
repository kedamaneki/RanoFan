#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""フェーズS タイムライン脊髄 simulationEvents 生成の共通ヘルパー。"""

from __future__ import annotations

import json
import os
import re

OUT_DIR = os.path.join(
    os.path.dirname(os.path.dirname(os.path.abspath(__file__))),
    "Assets",
    "Resources",
    "HistorySimulation",
    "spine",
)
NATIONS_DIR = os.path.join(
    os.path.dirname(OUT_DIR),
    "nations",
)

# 歴史棚欠番・推定生成国（turn_index.json 未収録）
GAP_COLLAPSE_TURNS: dict[int, list[str]] = {
    3: ["NATION_041"],
    5: ["NATION_042"],
    7: ["NATION_043"],
    9: ["NATION_044"],
}


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


def nation_ref_evt(
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
    """脊髄用・大国・地域の象徴イベント（targetNationId は内部IDのみ）。"""
    year = macro_turn * 10
    suffix = hist_suffix or event_type.upper()
    num = nation.split("_")[1]
    return {
        "eventId": f"SIM_EVT_SPINE_{nation}_{event_type}_{year}",
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
                f"HIST_{year:04d}_{month:02d}_{day:02d}_SPINE_{suffix}"
            ],
        },
        "alternativeMode": {
            "causalPrerequisites": {
                "requiredLogType": prereq_type,
                "requiredValue": prereq_val,
            },
            "unlockRewards": {
                "alternativeFlag": alt_flag or f"ALT_SPINE_{num}_{suffix}",
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


def world_evt(
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
    level: int = 5,
    prereq_type: str = "MonsterEradicatedCount",
    prereq_val: int = 5,
) -> dict:
    year = macro_turn * 10
    suffix = hist_suffix or event_type.upper()
    return {
        "eventId": f"SIM_EVT_WORLD_{event_type}_{year}",
        "macroTurn": macro_turn,
        "timestamp": {"year": year, "month": month, "day": day, "hour": hour},
        "targetNationId": "WORLD",
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
                "alternativeFlag": alt_flag or f"ALT_WORLD_{suffix}",
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


def load_nation_collapse_at_turn(macro_turn: int) -> list[dict]:
    """既存 nations JSON から当該 macroTurn の NationCollapse を抽出。"""
    events: list[dict] = []
    if not os.path.isdir(NATIONS_DIR):
        return events
    for name in sorted(os.listdir(NATIONS_DIR)):
        if not name.startswith("SIM_NATION_") or not name.endswith(".json"):
            continue
        path = os.path.join(NATIONS_DIR, name)
        with open(path, encoding="utf-8") as f:
            data = json.load(f)
        for e in data.get("simulationEvents", []):
            if (
                e.get("macroTurn") == macro_turn
                and e.get("eventType") == "NationCollapse"
                and e.get("isHistoricalCollapseRoute")
            ):
                events.append(e)
    return events


def collapse_nations_for_turn(
    macro_turn: int,
    turn_index: dict | None = None,
) -> list[str]:
    """当該ターンに滅亡する国家ID一覧（turn_index + gap補完）。"""
    ids: list[str] = []
    if turn_index:
        bucket = turn_index.get(str(macro_turn), {})
        ids.extend(bucket.get("collapse", []))
        ids.extend(bucket.get("birthCollapse", []))
    ids.extend(GAP_COLLAPSE_TURNS.get(macro_turn, []))
    # 重複除去・順序維持
    seen: set[str] = set()
    out: list[str] = []
    for nid in ids:
        if nid not in seen:
            seen.add(nid)
            out.append(nid)
    return out


def write_spine(macro_turn: int, events: list[dict]) -> str:
    os.makedirs(OUT_DIR, exist_ok=True)
    events = sorted(events, key=lambda e: (e.get("targetNationId", ""), e["macroTurn"]))
    path = os.path.join(OUT_DIR, f"SIM_T{macro_turn:03d}.json")
    with open(path, "w", encoding="utf-8") as f:
        json.dump({"simulationEvents": events}, f, ensure_ascii=False, indent=2)
    print(f"Wrote {path} ({len(events)} events)")
    return path


def validate_spine_events(events: list[dict]) -> list[str]:
    bad_ui = re.compile(r"国家\d{3}|NATION_\d{3}|区域[AB]")
    errors: list[str] = []
    turns = [e["macroTurn"] for e in events]
    if len(turns) != len(set(turns)) and any(
        e.get("targetNationId") == "WORLD" for e in events
    ):
        # 世界イベント複数は同一 macroTurn 可。国家滅亡は国ごとに1件。
        nation_turns = [
            (e["targetNationId"], e["macroTurn"])
            for e in events
            if e.get("targetNationId") not in ("", "WORLD")
        ]
        if len(nation_turns) != len(set(nation_turns)):
            errors.append(f"duplicate nation+macroTurn: {nation_turns}")
    for e in events:
        y = e["timestamp"]["year"]
        if y != e["macroTurn"] * 10:
            errors.append(f"{e['eventId']}: year mismatch")
        collapse = e.get("isHistoricalCollapseRoute", False)
        has_p = "prosperityTimeline" in e
        has_d = "decayTimeline" in e
        if collapse and has_p:
            errors.append(f"{e['eventId']}: collapse has prosperity")
        if not collapse and has_d:
            errors.append(f"{e['eventId']}: non-collapse has decay")
        for field in ["logMessageTemplate", "microLore"]:
            txt = e.get(field, "")
            if e.get("targetNationId") == "WORLD" and bad_ui.search(txt):
                errors.append(f"{e['eventId']}: bad UI in {field}")
        tl = e.get("prosperityTimeline") or e.get("decayTimeline") or {}
        for phase in tl.values():
            for v in phase.values():
                if (
                    isinstance(v, str)
                    and e.get("targetNationId") == "WORLD"
                    and bad_ui.search(v)
                ):
                    errors.append(f"{e['eventId']}: bad UI in timeline")
    return errors

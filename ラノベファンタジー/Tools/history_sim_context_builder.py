#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""world_analytics.json と 歴史棚 から歴史シミュレーション生成用コンテキストを抽出する。"""

from __future__ import annotations

import argparse
import glob
import json
import os
import re
import sys
from typing import Any

# リポジトリルート（ラノベファンタジーの親）
TOOLS_DIR = os.path.dirname(os.path.abspath(__file__))
GAME_ROOT = os.path.dirname(TOOLS_DIR)
REPO_ROOT = os.path.dirname(GAME_ROOT)
HISTORY_DIR = os.path.join(REPO_ROOT, "歴史棚")
ANALYTICS_PATH = os.path.join(REPO_ROOT, "world_analytics.json")

TURNS_PER_ANALYTICS_YEAR = 12
NARRATIVE_YEARS_PER_MACRO = 10
NARRATIVE_SCALE = 10  # 歴史書叙事年 = (analytics年 - BASE) × 10
ANALYTICS_YEAR_BASE = 1000

HEADER_RE = re.compile(
    r"【国家(\d{3})（領域・([^）]+)）(?:：([^】]+)|の千年史)】"
)
COLLAPSE_TURN_RE = re.compile(r"ターン(\d+)")
NARRATIVE_YEAR_RE = re.compile(r"(\d+)年目")
TECH_RE = re.compile(r"「([^」]+)」")

REGION_DISPLAY: dict[str, str] = {
    "西": "【最西端の統合王国】周辺",
    "東": "【東方の魔導生産大国】",
    "中央": "【中央海平原の結節都市】",
    "南": "【険峻な山岳要塞帯】",
    "北": "【極寒針葉樹林の前線】",
}


def macro_to_analytics_turn_range(macro_turn: int) -> tuple[int, int]:
    """1 macroTurn = 1 analytics暦年 = 12 月次ターン。"""
    start = (macro_turn - 1) * TURNS_PER_ANALYTICS_YEAR + 1
    end = macro_turn * TURNS_PER_ANALYTICS_YEAR
    return start, end


def macro_to_analytics_year(macro_turn: int) -> int:
    return ANALYTICS_YEAR_BASE + macro_turn - 1


def macro_to_narrative_year_range(macro_turn: int) -> tuple[int, int]:
    """叙事年。ターンTの区間は (T-1)×10+1 〜 T×10。例: ターン46 → 451〜460年目。"""
    start = (macro_turn - 1) * NARRATIVE_YEARS_PER_MACRO + 1
    end = macro_turn * NARRATIVE_YEARS_PER_MACRO
    return start, end


def narrative_year_to_analytics_year(narrative_year: int) -> int:
    """叙事年 → analytics の year フィールド（10倍圧縮の逆変換）。"""
    return ANALYTICS_YEAR_BASE + (narrative_year - 1) // NARRATIVE_SCALE


def evolution_system_type(macro_turn: int) -> str:
    if macro_turn <= 40:
        return "Ritual"
    if macro_turn <= 60:
        return "Inspiration"
    return "Recipe"


def load_analytics() -> list[dict[str, Any]]:
    if not os.path.isfile(ANALYTICS_PATH):
        return []
    with open(ANALYTICS_PATH, encoding="utf-8") as f:
        data = json.load(f)
    return data if isinstance(data, list) else []


def slice_analytics(
    records: list[dict[str, Any]],
    macro_start: int,
    macro_end: int,
) -> dict[str, Any]:
    if not records:
        return {"available": False, "reason": "world_analytics.json not found or empty"}

    t_start, _ = macro_to_analytics_turn_range(macro_start)
    _, t_end = macro_to_analytics_turn_range(macro_end)

    subset = [r for r in records if t_start <= int(r.get("turn", 0)) <= t_end]
    if not subset:
        return {
            "available": False,
            "macroTurnRange": [macro_start, macro_end],
            "analyticsTurnRange": [t_start, t_end],
            "reason": "no records in range (歴史棚テキストを主ソースにしてください)",
        }

    first, last = subset[0], subset[-1]
    threats = [float(r.get("average_monster_threat", 0)) for r in subset]
    powers = [float(r.get("total_human_power", 0)) for r in subset]
    alive = [int(r.get("alive_countries", 0)) for r in subset]

    return {
        "available": True,
        "macroTurnRange": [macro_start, macro_end],
        "analyticsTurnRange": [t_start, t_end],
        "narrativeYearRange": list(macro_to_narrative_year_range(macro_start))
        + [macro_to_narrative_year_range(macro_end)[1]],
        "analyticsYearRange": [
            macro_to_analytics_year(macro_start),
            macro_to_analytics_year(macro_end),
        ],
        "sampleCount": len(subset),
        "periodStart": {
            "turn": first.get("turn"),
            "year": first.get("year"),
            "month": first.get("month"),
            "alive_countries": first.get("alive_countries"),
            "total_human_power": first.get("total_human_power"),
            "average_monster_threat": first.get("average_monster_threat"),
            "cycle_phase": first.get("cycle_phase"),
        },
        "periodEnd": {
            "turn": last.get("turn"),
            "year": last.get("year"),
            "month": last.get("month"),
            "alive_countries": last.get("alive_countries"),
            "total_human_power": last.get("total_human_power"),
            "average_monster_threat": last.get("average_monster_threat"),
            "cycle_phase": last.get("cycle_phase"),
        },
        "trends": {
            "monster_threat_min": round(min(threats), 4),
            "monster_threat_max": round(max(threats), 4),
            "human_power_delta": round(powers[-1] - powers[0], 2),
            "alive_countries_min": min(alive),
            "alive_countries_max": max(alive),
        },
        "keyFrames": _sample_key_frames(subset),
    }


def _sample_key_frames(subset: list[dict[str, Any]], n: int = 6) -> list[dict[str, Any]]:
    if len(subset) <= n:
        return subset
    step = max(1, len(subset) // (n - 1))
    indices = list(range(0, len(subset), step))[: n - 1]
    if indices[-1] != len(subset) - 1:
        indices.append(len(subset) - 1)
    return [subset[i] for i in indices]


def parse_header(header_line: str) -> dict[str, Any]:
    m = HEADER_RE.match(header_line.strip())
    if not m:
        return {}
    num = int(m.group(1))
    region = m.group(2)
    status_raw = m.group(3) or "生存"
    collapse_turns = [int(x) for x in COLLAPSE_TURN_RE.findall(status_raw)]
    narrative_years = [int(x) for x in NARRATIVE_YEAR_RE.findall(status_raw)]
    survived = "滅亡" not in status_raw and "新生" not in status_raw
    is_birth = "新生" in status_raw
    is_collapse = "滅亡" in status_raw
    return {
        "countryNum": num,
        "nationId": f"NATION_{num:03d}",
        "region": region,
        "regionDisplayName": REGION_DISPLAY.get(region, f"領域・{region}"),
        "statusRaw": status_raw,
        "survived": survived and not is_collapse,
        "isBirthNation": is_birth,
        "isCollapseRoute": is_collapse,
        "collapseMacroTurns": collapse_turns,
        "narrativeYearsMentioned": narrative_years,
    }


def parse_countries() -> dict[int, dict[str, Any]]:
    countries: dict[int, dict[str, Any]] = {}
    paths = sorted(glob.glob(os.path.join(HISTORY_DIR, "*")))
    for path in paths:
        if not os.path.isfile(path):
            continue
        with open(path, encoding="utf-8") as f:
            text = f.read()
        source_file = os.path.basename(path)
        for part in re.split(r"(?=【国家\d{3})", text):
            part = part.strip()
            if not part:
                continue
            header_line = part.split("\n", 1)[0]
            meta = parse_header(header_line)
            if not meta:
                continue
            num = meta["countryNum"]
            body = part.split("\n", 1)[1].strip() if "\n" in part else ""
            techs = TECH_RE.findall(part)
            countries[num] = {
                **meta,
                "sourceFile": source_file,
                "bodyText": body,
                "quotedTechnologies": techs,
                "bodyPreview": body[:400] + ("…" if len(body) > 400 else ""),
            }
    return dict(sorted(countries.items()))


def nation_context(
    countries: dict[int, dict[str, Any]],
    nation_num: int,
    macro_turn: int | None,
    collapse_hint: bool,
) -> dict[str, Any]:
    entry = countries.get(nation_num)
    if not entry:
        return {
            "nationId": f"NATION_{nation_num:03d}",
            "found": False,
            "hint": "歴史棚に該当国家の記述がありません",
        }

    ctx: dict[str, Any] = {
        "found": True,
        "nationId": entry["nationId"],
        "countryNum": nation_num,
        "region": entry["region"],
        "regionDisplayName": entry["regionDisplayName"],
        "survived": entry["survived"],
        "isCollapseRoute": entry["isCollapseRoute"],
        "isBirthNation": entry["isBirthNation"],
        "collapseMacroTurns": entry["collapseMacroTurns"],
        "narrativeYearsMentioned": entry["narrativeYearsMentioned"],
        "quotedTechnologies": entry["quotedTechnologies"],
        "sourceFile": entry["sourceFile"],
        "bodyText": entry["bodyText"],
        "suggestedEvolutionSystemType": evolution_system_type(macro_turn or 1),
    }

    if macro_turn is not None:
        ctx["requestedMacroTurn"] = macro_turn
        ctx["narrativeYearRange"] = list(macro_to_narrative_year_range(macro_turn))
        ctx["isCollapseAtRequestedTurn"] = macro_turn in entry["collapseMacroTurns"]
        if collapse_hint or macro_turn in entry["collapseMacroTurns"]:
            ctx["recommendedIsHistoricalCollapseRoute"] = True
        else:
            ctx["recommendedIsHistoricalCollapseRoute"] = not entry["survived"]

    return ctx


def build_context(
    nation_num: int | None,
    macro_turn: int | None,
    macro_turn_end: int | None,
    collapse_hint: bool,
) -> dict[str, Any]:
    countries = parse_countries()
    analytics = load_analytics()

    m_start = macro_turn or 1
    m_end = macro_turn_end or macro_turn or m_start

    result: dict[str, Any] = {
        "generator": "history_sim_context_builder",
        "dataSources": {
            "analytics": os.path.relpath(ANALYTICS_PATH, REPO_ROOT),
            "historyShelf": os.path.relpath(HISTORY_DIR, REPO_ROOT),
        },
        "turnMapping": {
            "narrativeScale": NARRATIVE_SCALE,
            "rule": "叙事年 = (analytics.year - 1000) × 10 + 月内進行。1 macroTurn = 10叙事年 = 1 analytics暦年 = 12 turn",
            "turnsPerMacroTurn": TURNS_PER_ANALYTICS_YEAR,
            "narrativeYearsPerMacroTurn": NARRATIVE_YEARS_PER_MACRO,
            "analyticsYearBase": ANALYTICS_YEAR_BASE,
            "formulas": {
                "analyticsTurnStart": "(macroTurn - 1) × 12 + 1",
                "analyticsTurnEnd": "macroTurn × 12",
                "analyticsYear": "1000 + macroTurn - 1",
                "narrativeYearEnd": "macroTurn × 10",
                "narrativeYearToAnalyticsYear": "1000 + (叙事年 - 1) // 10",
            },
        },
        "macroTurnRange": [m_start, m_end],
        "worldAnalytics": slice_analytics(analytics, m_start, m_end),
    }

    if nation_num is not None:
        result["nation"] = nation_context(
            countries, nation_num, macro_turn, collapse_hint
        )

    if macro_turn is not None:
        turn_idx = build_turn_index(countries)
        t = macro_turn
        if t in turn_idx:
            result["nationsAtThisTurn"] = turn_idx[t]
        else:
            result["nationsAtThisTurn"] = {"collapse": [], "birthCollapse": []}

    return result


def build_turn_index(countries: dict[int, dict[str, Any]]) -> dict[int, dict[str, Any]]:
    """macroTurn ごとに滅亡・新生が起きる国家を索引化。"""
    index: dict[int, dict[str, Any]] = {}
    for num, c in countries.items():
        for t in c["collapseMacroTurns"]:
            if t not in index:
                index[t] = {"collapse": [], "birthCollapse": []}
            bucket = "birthCollapse" if c["isBirthNation"] else "collapse"
            index[t][bucket].append(c["nationId"])
    return dict(sorted(index.items()))


def nation_deep_plan(entry: dict[str, Any]) -> list[dict[str, Any]]:
    """1国家あたりの深掘り用イベント案（章×時代）。"""
    chapters = [
        (1, 20, "Ritual", "第1章・初期高脅威"),
        (21, 40, "Ritual", "第2章・瓦解と同盟"),
        (41, 60, "Inspiration", "第3章・常夜の底"),
        (61, 80, "Recipe", "第4章・大開拓"),
        (81, 100, "Recipe", "第5章・完全駆逐"),
    ]
    collapse_turns = set(entry["collapseMacroTurns"])
    plan: list[dict[str, Any]] = []
    for start, end, evo, label in chapters:
        if collapse_turns and min(collapse_turns) < start:
            continue
        rep_turn = start + (end - start) // 2
        if any(start <= t <= end for t in collapse_turns):
            route = "decay"
            rep_turn = next(t for t in collapse_turns if start <= t <= end)
        elif entry["isCollapseRoute"] and collapse_turns and max(collapse_turns) < start:
            continue
        else:
            route = "prosperity"
        plan.append({
            "chapter": label,
            "macroTurnRange": [start, end],
            "suggestedMacroTurn": rep_turn,
            "narrativeYear": rep_turn * 10,
            "evolutionSystemType": evo,
            "recommendedRoute": route,
        })
    if entry["isBirthNation"] and entry["collapseMacroTurns"]:
        plan.append({
            "chapter": "新生",
            "macroTurnRange": None,
            "suggestedMacroTurn": entry["collapseMacroTurns"][0],
            "narrativeYear": entry["collapseMacroTurns"][0] * 10,
            "evolutionSystemType": "Recipe",
            "recommendedRoute": "prosperity",
            "note": "NationBirth イベント候補",
        })
    if entry["quotedTechnologies"]:
        plan.append({
            "chapter": "固有技術",
            "suggestedMacroTurn": plan[0]["suggestedMacroTurn"] if plan else 5,
            "eventTypeHint": "ShieldFortification or MagicPioneering",
            "technologies": entry["quotedTechnologies"],
            "recommendedRoute": "prosperity",
        })
    return plan


def list_nations_table(countries: dict[int, dict[str, Any]]) -> str:
    lines = [
        "num\tnationId\tregion\tstatus\tcollapseTurns\tsource",
    ]
    for num, c in countries.items():
        status = "生存" if c["survived"] else ("新生+滅亡" if c["isBirthNation"] else "滅亡")
        turns = ",".join(str(t) for t in c["collapseMacroTurns"]) or "-"
        lines.append(
            f"{num:03d}\t{c['nationId']}\t{c['region']}\t{status}\t{turns}\t{c['sourceFile']}"
        )
    return "\n".join(lines)


def main() -> int:
    parser = argparse.ArgumentParser(
        description="歴史シミュレーション JSON 生成用のコンテキストを抽出"
    )
    parser.add_argument(
        "--nation",
        type=int,
        help="国家番号 (例: 71 → NATION_071)",
    )
    parser.add_argument(
        "--macro-turn",
        type=int,
        help="対象 macroTurn (1-100)",
    )
    parser.add_argument(
        "--macro-turn-end",
        type=int,
        help="macroTurn 範囲の終端（省略時は --macro-turn と同じ）",
    )
    parser.add_argument(
        "--collapse",
        action="store_true",
        help="有事ルート (decayTimeline) を推奨フラグ付きで出力",
    )
    parser.add_argument(
        "--list-nations",
        action="store_true",
        help="歴史棚から抽出した国家一覧を TSV 表示",
    )
    parser.add_argument(
        "--turn-index",
        action="store_true",
        help="macroTurn ごとの滅亡/新生国家索引を JSON 出力",
    )
    parser.add_argument(
        "--nation-deep",
        type=int,
        metavar="N",
        help="国家 N の深掘りイベント計画を出力",
    )
    parser.add_argument(
        "--output",
        "-o",
        help="JSON 出力先ファイル（省略時は stdout）",
    )
    args = parser.parse_args()

    if args.list_nations:
        countries = parse_countries()
        text = list_nations_table(countries)
        if args.output:
            with open(args.output, "w", encoding="utf-8") as f:
                f.write(text)
        else:
            print(text)
        return 0

    if args.turn_index:
        countries = parse_countries()
        payload = {
            "turnIndex": build_turn_index(countries),
            "turnMapping": {
                "narrativeYearEnd": "macroTurn × 10",
                "analyticsYear": "1000 + macroTurn - 1",
            },
        }
        text = json.dumps(payload, ensure_ascii=False, indent=2)
        if args.output:
            with open(args.output, "w", encoding="utf-8") as f:
                f.write(text)
        else:
            print(text)
        return 0

    if args.nation_deep is not None:
        countries = parse_countries()
        entry = countries.get(args.nation_deep)
        if not entry:
            print(json.dumps({"error": "nation not found"}, ensure_ascii=False), file=sys.stderr)
            return 1
        payload = {
            "nationId": entry["nationId"],
            "deepEventPlan": nation_deep_plan(entry),
            "bodyText": entry["bodyText"],
            "quotedTechnologies": entry["quotedTechnologies"],
        }
        text = json.dumps(payload, ensure_ascii=False, indent=2)
        if args.output:
            with open(args.output, "w", encoding="utf-8") as f:
                f.write(text)
        else:
            print(text)
        return 0

    if args.nation is None and args.macro_turn is None:
        parser.error("--nation または --macro-turn のいずれかを指定してください")

    ctx = build_context(
        nation_num=args.nation,
        macro_turn=args.macro_turn,
        macro_turn_end=args.macro_turn_end,
        collapse_hint=args.collapse,
    )

    text = json.dumps(ctx, ensure_ascii=False, indent=2)
    if args.output:
        with open(args.output, "w", encoding="utf-8") as f:
            f.write(text)
    else:
        print(text)
    return 0


if __name__ == "__main__":
    sys.exit(main())

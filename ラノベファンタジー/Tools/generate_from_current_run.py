#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""現行マクロラン（ターン1〜250）から HistorySimulation JSON を再生成する。"""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path
from typing import Dict, List, Optional, Sequence

from geo_event_builder import (
    build_event,
    nation_id_str,
    region_label,
    validate_events,
)
from geo_sim_source import (
    DEFAULT_MAX_TURN,
    GeoIndex,
    RawEvent,
    build_index,
    events_for_nation,
    events_for_turn,
    top_nations_at,
)

TOOLS_DIR = Path(__file__).resolve().parent
OUT_ROOT = TOOLS_DIR.parent / "Assets" / "Resources" / "HistorySimulation"
SPINE_DIR = OUT_ROOT / "spine"
NATIONS_DIR = OUT_ROOT / "nations"
SECTIONS_DIR = OUT_ROOT / "sections"
GP_DIR = OUT_ROOT / "greatpowers"

BATCH_RANGES: Dict[str, tuple[int, int]] = {}
for i in range(6, 30):
    start = (i - 6) * 5 + 1
    BATCH_RANGES[f"{i:02d}"] = (start, start + 4)
BATCH_RANGES["30"] = (121, 131)
BATCH_RANGES["gap"] = (132, 173)


def _uniquify(events: List[dict]) -> List[dict]:
    seen: Dict[str, int] = {}
    out: List[dict] = []
    for e in events:
        base = e["eventId"]
        n = seen.get(base, 0)
        seen[base] = n + 1
        if n:
            e = dict(e)
            e["eventId"] = f"{base}_{n + 1}"
        out.append(e)
    return out


def _dump(path: Path, events: List[dict], years_per_turn: int) -> None:
    events = _uniquify(sorted(events, key=lambda e: (e["macroTurn"], e.get("targetNationId", ""))))
    errors = validate_events(events, years_per_turn)
    if errors:
        raise ValueError(f"{path.name}: " + "; ".join(errors[:8]))
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(
        json.dumps({"simulationEvents": events}, ensure_ascii=False, indent=2),
        encoding="utf-8",
    )
    print(f"Wrote {path} ({len(events)} events)")


def _world_placeholder(index: GeoIndex, turn: int) -> RawEvent:
    stats = index.stats_by_turn.get(turn) or index.stats_by_turn.get(turn - 1) or {}
    threat = float(stats.get("global_threat_index") or 0.0)
    alive = stats.get("alive_countries", "")
    phase = stats.get("cycle_phase", "")
    return RawEvent(
        nation_id=None,
        turn=turn,
        event_type="Innovation" if phase == "dormant" else "MonsterAttack",
        category="domestic" if phase == "dormant" else "monster",
        region="",
        message=(
            f"全球観測：生存統治体 {alive}、脅威指数 {threat:.3f}、周期位相 {phase or '不明'}。"
        ),
        metrics={"global_threat_index": threat},
    )


def generate_nations(
    index: GeoIndex,
    start: int = 1,
    end: Optional[int] = None,
) -> int:
    last = end or index.initial_nations
    count = 0
    for nid in range(start, last + 1):
        raws = events_for_nation(index, nid)
        events = [build_event(index, r, prefix="SIM_EVT") for r in raws]
        _dump(NATIONS_DIR / f"SIM_NATION_{nid:03d}.json", events, index.years_per_turn)
        count += 1
    return count


def generate_spine(index: GeoIndex) -> int:
    for turn in range(1, index.max_turn + 1):
        raws = [e for e in events_for_turn(index, turn) if e.nation_id is None or e.is_collapse]
        if not raws:
            raws = [_world_placeholder(index, turn)]
        elif not any(e.nation_id is None for e in raws):
            raws = [_world_placeholder(index, turn)] + raws
        events = [
            build_event(index, r, prefix="SIM_EVT_SPINE" if r.nation_id is None else "SIM_EVT")
            for r in raws
        ]
        _dump(SPINE_DIR / f"SIM_T{turn:03d}.json", events, index.years_per_turn)
    return index.max_turn


def _section_bounds(max_turn: int) -> Dict[tuple[int, int], tuple[int, int]]:
    chapter_size = max_turn // 5
    spans = (13, 12, 13, 12)
    meta: Dict[tuple[int, int], tuple[int, int]] = {}
    for ch in range(1, 6):
        start = (ch - 1) * chapter_size + 1
        end = ch * chapter_size if ch < 5 else max_turn
        t = start
        remaining = end - start + 1
        for sec in range(1, 5):
            if sec == 4:
                lo, hi = t, end
            else:
                n = spans[sec - 1]
                n = min(n, max(1, remaining - (4 - sec)))
                lo, hi = t, min(end, t + n - 1)
            meta[(ch, sec)] = (lo, hi)
            t = hi + 1
            remaining = end - t + 1
    return meta


def generate_sections(index: GeoIndex) -> int:
    bounds = _section_bounds(index.max_turn)
    written = 0
    for (ch, sec), (lo, hi) in bounds.items():
        raws: List[RawEvent] = []
        start_stats = index.stats_by_turn.get(lo) or {}
        end_stats = index.stats_by_turn.get(hi) or {}
        raws.append(
            RawEvent(
                nation_id=None,
                turn=lo,
                event_type="MonsterAttack"
                if (start_stats.get("cycle_phase") == "active")
                else "Innovation",
                category="domestic",
                region="",
                message=(
                    f"第{ch}章第{sec}節の観測開始。生存 {start_stats.get('alive_countries', '?')}、"
                    f"総国力 {start_stats.get('total_human_power', 0):.0f}、"
                    f"脅威 {float(start_stats.get('global_threat_index') or 0):.3f}。"
                ),
            )
        )
        collapses = [
            e
            for e in index.events
            if e.is_collapse and lo <= e.turn <= hi
        ]
        if collapses:
            raws.append(
                RawEvent(
                    nation_id=None,
                    turn=collapses[-1].turn,
                    event_type="NationCollapse",
                    category="lifecycle",
                    region="",
                    message=(
                        f"この節で {len(collapses)} の統治体が欠番化した。"
                        "結界崩壊は番号を再利用せず、無主地として記録される。"
                    ),
                    is_collapse=True,
                )
            )
        attacks = [
            e
            for e in index.events
            if e.event_type == "MonsterAttack" and e.nation_id and lo <= e.turn <= hi
        ]
        if attacks:
            worst = max(attacks, key=lambda e: e.metrics.get("territory_loss", 0.0))
            raws.append(worst)
        raws.append(
            RawEvent(
                nation_id=None,
                turn=hi,
                event_type="Innovation",
                category="domestic",
                region="",
                message=(
                    f"第{ch}章第{sec}節の観測終了。生存 {end_stats.get('alive_countries', '?')}、"
                    f"総国力 {end_stats.get('total_human_power', 0):.0f}、"
                    f"脅威 {float(end_stats.get('global_threat_index') or 0):.3f}。"
                ),
            )
        )
        events = [build_event(index, r, prefix=f"SIM_EVT_SEC_CH{ch:02d}_S{sec:02d}") for r in raws]
        for e in events:
            if e.get("isHistoricalCollapseRoute") and e.get("targetNationId") == "WORLD":
                e["eventType"] = "MonsterAttack"
                e["isHistoricalCollapseRoute"] = False
                e.pop("decayTimeline", None)
                if "prosperityTimeline" not in e:
                    from geo_event_builder import _prosperity

                    e["prosperityTimeline"] = _prosperity(
                        e["macroTurn"],
                        index.years_per_turn,
                        "「欠番の地図が増えている。番号は戻らない。」",
                        "世界規模の淘汰圧。無主地の拡大。",
                        "「隣の結界都市へ避難する列が、街道を埋めている。」",
                        "放棄都市と明滅する残骸結界。",
                        "「生き残った輪を太くする以外に、手はない。」",
                        "世界観測ログの更新。",
                    )
        _dump(
            SECTIONS_DIR / f"SIM_CH{ch:02d}_SEC{sec:02d}.json",
            events,
            index.years_per_turn,
        )
        written += 1
    return written


def _clear_dir_json(directory: Path, keep: set[str]) -> None:
    if not directory.is_dir():
        return
    for path in directory.glob("*.json"):
        if path.name not in keep:
            path.unlink()
            meta = path.with_suffix(".json.meta")
            if meta.is_file():
                meta.unlink()
            print(f"Removed stale {path.name}")


def generate_greatpowers(index: GeoIndex, top_n: int = 3) -> int:
    ranked = top_nations_at(index, index.max_turn, n=max(5, top_n))
    keep: set[str] = set()
    written = 0
    for nid, st in ranked[:top_n]:
        raws = events_for_nation(index, nid)
        profile = index.profiles[nid]
        place = region_label(profile.region)
        raws.append(
            RawEvent(
                nation_id=nid,
                turn=index.max_turn,
                event_type="Innovation",
                category="domestic",
                region=profile.region,
                message=(
                    f"叙事{index.max_turn}年時点で【{place}】の有力統治体が国力 {st.power:.0f} を記録し、"
                    "観測対象の大国として確定した。"
                ),
                metrics={"power": st.power, "barrier": st.barrier},
            )
        )
        events = [build_event(index, r, prefix="SIM_EVT_GP") for r in raws]
        name = f"SIM_GP_NATION_{nid:03d}.json"
        _dump(GP_DIR / name, events, index.years_per_turn)
        keep.add(name)
        written += 1

    if len(ranked) >= 2:
        a, sa = ranked[0]
        b, sb = ranked[1]
        pa, pb = index.profiles[a], index.profiles[b]
        dual = RawEvent(
            nation_id=None,
            turn=index.max_turn,
            event_type="Innovation",
            category="conflict",
            region="",
            message=(
                f"叙事{index.max_turn}年、【{region_label(pa.region)}】と【{region_label(pb.region)}】の"
                f"有力統治体が双極の観測対象となった（国力 {sa.power:.0f} / {sb.power:.0f}）。"
            ),
            related_id=b,
        )
        evt = build_event(index, dual, prefix="SIM_EVT_GP_DUAL")
        evt["relatedNationId"] = nation_id_str(b)
        evt["targetNationId"] = "WORLD"
        name = f"SIM_GP_DUAL_{a:03d}_{b:03d}.json"
        _dump(GP_DIR / name, [evt], index.years_per_turn)
        keep.add(name)
        written += 1

    start = index.stats_by_turn.get(1) or {}
    end = index.stats_by_turn.get(index.max_turn) or {}
    world_raws = [
        RawEvent(
            nation_id=None,
            turn=1,
            event_type="MonsterAttack",
            category="monster",
            region="",
            message=(
                f"現行マクロラン開始。初期統治体 {index.initial_nations}、"
                f"総国力 {start.get('total_human_power', 0):.0f}、"
                f"脅威 {float(start.get('global_threat_index') or 0):.3f}。"
            ),
        ),
        RawEvent(
            nation_id=None,
            turn=index.max_turn,
            event_type="Innovation",
            category="domestic",
            region="",
            message=(
                f"叙事{index.max_turn}年で正史データを確定。生存 {end.get('alive_countries', '?')}、"
                f"総国力 {end.get('total_human_power', 0):.0f}、"
                f"脅威 {float(end.get('global_threat_index') or 0):.3f}。"
            ),
        ),
    ]
    world_events = [build_event(index, r, prefix="SIM_EVT_GP_WORLD") for r in world_raws]
    _dump(GP_DIR / "SIM_GP_WORLD.json", world_events, index.years_per_turn)
    keep.add("SIM_GP_WORLD.json")
    written += 1
    _clear_dir_json(GP_DIR, keep)
    return written


def generate_all(index: GeoIndex, nation_start: int = 1, nation_end: Optional[int] = None) -> None:
    generate_nations(index, nation_start, nation_end)
    generate_spine(index)
    generate_sections(index)
    generate_greatpowers(index)


def build_parser() -> argparse.ArgumentParser:
    p = argparse.ArgumentParser(description="現行マクロランから HistorySimulation JSON を生成")
    p.add_argument("--max-turn", type=int, default=DEFAULT_MAX_TURN)
    p.add_argument("--no-cache", action="store_true")
    p.add_argument("--nations-only", action="store_true")
    p.add_argument("--spine-only", action="store_true")
    p.add_argument("--sections-only", action="store_true")
    p.add_argument("--greatpowers-only", action="store_true")
    p.add_argument("--start", type=int, default=1)
    p.add_argument("--end", type=int, default=None)
    p.add_argument("--batch", type=str, default=None, help="06..30 or gap")
    return p


def main(argv: Optional[Sequence[str]] = None) -> int:
    args = build_parser().parse_args(argv)
    start, end = args.start, args.end
    if args.batch:
        key = args.batch if args.batch == "gap" else f"{int(args.batch):02d}"
        if key not in BATCH_RANGES:
            print(f"unknown batch: {args.batch}", file=sys.stderr)
            return 2
        start, end = BATCH_RANGES[key]
    print("Loading geo index...", flush=True)
    index = build_index(max_turn=args.max_turn, use_cache=not args.no_cache)
    print(
        f"Ready: initial={index.initial_nations} turns=1..{index.max_turn} "
        f"events={len(index.events)} ypt={index.years_per_turn}",
        flush=True,
    )
    if args.nations_only or args.batch:
        generate_nations(index, start, end)
    elif args.spine_only:
        generate_spine(index)
    elif args.sections_only:
        generate_sections(index)
    elif args.greatpowers_only:
        generate_greatpowers(index)
    else:
        generate_all(index, start, end)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

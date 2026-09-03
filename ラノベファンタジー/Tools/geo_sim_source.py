#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""現行マクロラン（geo.json + log.json）からターン1〜250の抽出用インデックスを構築する。"""

from __future__ import annotations

import json
import re
import sys
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any, Dict, Iterable, List, Optional, Tuple

TOOLS_DIR = Path(__file__).resolve().parent
REPO_ROOT = TOOLS_DIR.parent.parent
GEO_PATH = REPO_ROOT / "macro_chronicle" / "macro_chronicle_geo.json"
LOG_PATH = REPO_ROOT / "macro_chronicle" / "macro_chronicle_log.json"
CACHE_PATH = TOOLS_DIR / ".cache_geo_t250.json"

DEFAULT_MAX_TURN = 250
BARRIER_DROP_MIN = 0.02
ATTACK_WINDOW = 50
MAX_ATTACKS_PER_NATION = 5
MAX_INNOVATIONS_PER_NATION = 4
MAX_BARRIER_DROPS_PER_NATION = 3
BREACH_TERRITORY_LOSS = 8.0

ATTACK_RE = re.compile(r"領-([0-9.]+).*?軍-([0-9.]+).*?経-([0-9.]+)")
INNOV_RE = re.compile(r"係数([0-9.]+).*?生存圏\+([0-9.]+)")


@dataclass
class NationProfile:
    nation_id: int
    name: str
    region: str
    born_turn: int = 0
    died_turn: Optional[int] = None
    absorbed_by: Optional[int] = None
    lat: float = 0.0
    lng: float = 0.0


@dataclass
class TurnNationStats:
    alive: bool
    power: float
    territory: float
    economy: float
    military: float
    magic: float
    barrier: float


@dataclass
class RawEvent:
    nation_id: Optional[int]
    turn: int
    event_type: str
    category: str
    region: str
    message: str
    related_id: Optional[int] = None
    metrics: Dict[str, float] = field(default_factory=dict)
    is_collapse: bool = False


@dataclass
class GeoIndex:
    max_turn: int
    years_per_turn: int
    initial_nations: int
    profiles: Dict[int, NationProfile]
    stats_by_turn: Dict[int, Dict[str, Any]]
    nation_stats: Dict[Tuple[int, int], TurnNationStats]
    events: List[RawEvent]


def _num(value: Any, default: float = 0.0) -> float:
    if value is None:
        return default
    return float(value)


def _maybe_int(value: Any) -> Optional[int]:
    if value is None:
        return None
    return int(value)


def _plain(value: Any) -> Any:
    if isinstance(value, dict):
        return {str(k): _plain(v) for k, v in value.items()}
    if isinstance(value, (list, tuple)):
        return [_plain(v) for v in value]
    if isinstance(value, bool):
        return value
    if isinstance(value, (int, float, str)) or value is None:
        return value
    try:
        return float(value)
    except (TypeError, ValueError):
        return str(value)


def _load_json(path: Path) -> Any:
    with path.open("r", encoding="utf-8") as f:
        return json.load(f)


def _snapshot_stream(geo_path: Path, max_turn: int) -> Iterable[dict]:
    try:
        import ijson  # type: ignore
    except ImportError:
        ijson = None
    if ijson is not None:
        with geo_path.open("rb") as f:
            for snap in ijson.items(f, "snapshots.item"):
                turn = int(snap.get("turn", 0))
                if turn > max_turn:
                    break
                yield snap
        return
    data = _load_json(geo_path)
    for snap in data.get("snapshots", []):
        turn = int(snap.get("turn", 0))
        if turn > max_turn:
            break
        yield snap


def _parse_attack(text: str) -> Dict[str, float]:
    m = ATTACK_RE.search(text or "")
    if not m:
        return {}
    return {
        "territory_loss": float(m.group(1)),
        "military_loss": float(m.group(2)),
        "economy_loss": float(m.group(3)),
    }


def _parse_innov(text: str) -> Dict[str, float]:
    m = INNOV_RE.search(text or "")
    if not m:
        return {}
    return {
        "barrier_coeff": float(m.group(1)),
        "territory_gain": float(m.group(2)),
    }


def _pick_windowed(
    items: List[RawEvent],
    score_fn,
    window: int,
    limit: int,
) -> List[RawEvent]:
    if not items:
        return []
    best: Dict[int, RawEvent] = {}
    for ev in items:
        bucket = (ev.turn - 1) // window
        prev = best.get(bucket)
        if prev is None or score_fn(ev) > score_fn(prev):
            best[bucket] = ev
    ranked = sorted(best.values(), key=score_fn, reverse=True)
    extra = [ev for ev in items if ev not in ranked and score_fn(ev) >= 10.0]
    merged = ranked + extra
    merged.sort(key=lambda e: e.turn)
    return merged[:limit]


def build_index(
    max_turn: int = DEFAULT_MAX_TURN,
    geo_path: Path = GEO_PATH,
    log_path: Path = LOG_PATH,
    use_cache: bool = True,
) -> GeoIndex:
    if use_cache and CACHE_PATH.is_file() and geo_path.is_file() and log_path.is_file():
        cache_mtime = CACHE_PATH.stat().st_mtime
        if cache_mtime >= geo_path.stat().st_mtime and cache_mtime >= log_path.stat().st_mtime:
            cached = _load_json(CACHE_PATH)
            if int(cached.get("max_turn", 0)) == max_turn:
                return _index_from_cache(cached)

    if not geo_path.is_file():
        raise FileNotFoundError(f"geo JSON が見つかりません: {geo_path}")
    if not log_path.is_file():
        raise FileNotFoundError(f"log JSON が見つかりません: {log_path}")

    years_per_turn = 1
    initial_nations = 173
    try:
        with geo_path.open("r", encoding="utf-8") as f:
            head = f.read(2500)
        if '"years_per_turn"' in head:
            import re as _re

            m = _re.search(r'"years_per_turn"\s*:\s*(\d+)', head)
            if m:
                years_per_turn = int(m.group(1))
            m = _re.search(r'"initial_nations"\s*:\s*(\d+)', head)
            if m:
                initial_nations = int(m.group(1))
    except OSError:
        pass

    profiles: Dict[int, NationProfile] = {}
    nation_stats: Dict[Tuple[int, int], TurnNationStats] = {}
    stats_by_turn: Dict[int, Dict[str, Any]] = {}
    prev_barrier: Dict[int, float] = {}
    barrier_events: List[RawEvent] = []

    for snap in _snapshot_stream(geo_path, max_turn):
        turn = int(snap.get("turn", 0))
        stats_by_turn[turn] = _plain(snap.get("stats") or {})
        for n in snap.get("nations") or []:
            nid = int(n["id"])
            if nid < 1 or nid > initial_nations:
                continue
            if nid not in profiles:
                profiles[nid] = NationProfile(
                    nation_id=nid,
                    name=str(n.get("name", f"国家{nid:03d}")),
                    region=str(n.get("region", "")),
                    born_turn=int(n.get("born_turn") or 0),
                    died_turn=_maybe_int(n.get("died_turn")),
                    absorbed_by=_maybe_int(n.get("absorbed_by")),
                    lat=_num(n.get("lat")),
                    lng=_num(n.get("lng")),
                )
            else:
                died = _maybe_int(n.get("died_turn"))
                if died is not None:
                    profiles[nid].died_turn = died
                absorbed = _maybe_int(n.get("absorbed_by"))
                if absorbed is not None:
                    profiles[nid].absorbed_by = absorbed
            ts = TurnNationStats(
                alive=bool(n.get("alive", False)),
                power=_num(n.get("power")),
                territory=_num(n.get("territory")),
                economy=_num(n.get("economy")),
                military=_num(n.get("military")),
                magic=_num(n.get("magic")),
                barrier=_num(n.get("barrier_efficiency"), 1.0),
            )
            nation_stats[(turn, nid)] = ts
            if turn >= 1:
                prev = prev_barrier.get(nid)
                if prev is not None:
                    drop = prev - ts.barrier
                    died_now = profiles[nid].died_turn == turn
                    if drop >= BARRIER_DROP_MIN and not died_now:
                        barrier_events.append(
                            RawEvent(
                                nation_id=nid,
                                turn=turn,
                                event_type="BarrierDrop",
                                category="domestic",
                                region=profiles[nid].region,
                                message=(
                                    f"防衛障壁の循環効率が{prev:.2f}から{ts.barrier:.2f}へ急落した。"
                                ),
                                metrics={
                                    "barrier_before": float(prev),
                                    "barrier_after": float(ts.barrier),
                                    "drop": float(drop),
                                },
                            )
                        )
            prev_barrier[nid] = ts.barrier

    logs = _load_json(log_path)
    log_events: List[RawEvent] = []
    world_events: List[RawEvent] = []
    for entry in logs:
        turn = int(entry.get("turn") or 0)
        if turn < 1 or turn > max_turn:
            continue
        cat = str(entry.get("category") or "")
        text = str(entry.get("event") or "")
        region = str(entry.get("region") or "")
        nid = entry.get("nation_id")
        nids = entry.get("nation_ids") or []

        if cat == "collapse" and nid is not None:
            nid = int(nid)
            if 1 <= nid <= initial_nations:
                related = profiles.get(nid).absorbed_by if nid in profiles else None
                log_events.append(
                    RawEvent(
                        nation_id=nid,
                        turn=turn,
                        event_type="NationCollapse",
                        category="lifecycle",
                        region=region or (profiles[nid].region if nid in profiles else ""),
                        message=text,
                        related_id=related,
                        is_collapse=True,
                    )
                )
        elif cat == "absorption" and len(nids) >= 2:
            winner, loser = int(nids[0]), int(nids[1])
            if 1 <= loser <= initial_nations:
                log_events.append(
                    RawEvent(
                        nation_id=loser,
                        turn=turn,
                        event_type="NationCollapse",
                        category="lifecycle",
                        region=region,
                        message=text,
                        related_id=winner,
                        is_collapse=True,
                    )
                )
        elif cat == "innovation" and nid is not None:
            nid = int(nid)
            if 1 <= nid <= initial_nations:
                log_events.append(
                    RawEvent(
                        nation_id=nid,
                        turn=turn,
                        event_type="Innovation",
                        category="domestic",
                        region=region,
                        message=text,
                        metrics=_parse_innov(text),
                    )
                )
        elif cat == "monster_attack" and nid is not None:
            nid = int(nid)
            if 1 <= nid <= initial_nations:
                metrics = _parse_attack(text)
                log_events.append(
                    RawEvent(
                        nation_id=nid,
                        turn=turn,
                        event_type="MonsterAttack",
                        category="monster",
                        region=region,
                        message=text,
                        metrics=metrics,
                    )
                )
                if metrics.get("territory_loss", 0.0) >= BREACH_TERRITORY_LOSS:
                    log_events.append(
                        RawEvent(
                            nation_id=nid,
                            turn=turn,
                            event_type="BarrierDrop",
                            category="domestic",
                            region=region,
                            message=(
                                f"脅威侵攻に伴い防衛障壁が破綻し、生存圏が"
                                f"{metrics['territory_loss']:.1f}縮小した。"
                            ),
                            metrics={
                                "drop": metrics["territory_loss"] / 50.0,
                                "territory_loss": metrics["territory_loss"],
                            },
                        )
                    )
        elif cat in ("cycle_transition", "threat_environment"):
            world_events.append(
                RawEvent(
                    nation_id=None,
                    turn=turn,
                    event_type="MonsterAttack" if "活性" in text else "Innovation",
                    category="monster" if cat == "cycle_transition" else "domestic",
                    region="",
                    message=text,
                    metrics={
                        "global_threat_index": _num(
                            (entry.get("result") or {}).get("global_threat_index")
                        )
                    },
                )
            )

    collapse_keys = {(e.nation_id, e.turn) for e in log_events if e.is_collapse}
    selected: List[RawEvent] = []
    by_nation: Dict[int, List[RawEvent]] = {}
    for ev in log_events:
        if ev.nation_id is None:
            continue
        by_nation.setdefault(ev.nation_id, []).append(ev)

    barrier_by_nation: Dict[int, List[RawEvent]] = {}
    for ev in barrier_events:
        if ev.nation_id is None:
            continue
        if (ev.nation_id, ev.turn) in collapse_keys:
            continue
        barrier_by_nation.setdefault(ev.nation_id, []).append(ev)

    for nid in range(1, initial_nations + 1):
        group = by_nation.get(nid, [])
        collapses = [e for e in group if e.event_type == "NationCollapse"]
        innovs = [e for e in group if e.event_type == "Innovation"]
        attacks = [e for e in group if e.event_type == "MonsterAttack"]
        drops = [e for e in group if e.event_type == "BarrierDrop"]
        drops.extend(barrier_by_nation.get(nid, []))

        selected.extend(collapses)
        selected.extend(
            _pick_windowed(
                innovs,
                lambda e: e.metrics.get("territory_gain", 0.0) + e.metrics.get("barrier_coeff", 0.0),
                ATTACK_WINDOW,
                MAX_INNOVATIONS_PER_NATION,
            )
        )
        selected.extend(
            _pick_windowed(
                attacks,
                lambda e: e.metrics.get("territory_loss", 0.0)
                + e.metrics.get("military_loss", 0.0) * 0.4,
                ATTACK_WINDOW,
                MAX_ATTACKS_PER_NATION,
            )
        )
        selected.extend(
            _pick_windowed(
                drops,
                lambda e: e.metrics.get("drop", 0.0),
                ATTACK_WINDOW,
                MAX_BARRIER_DROPS_PER_NATION,
            )
        )
        if nid not in by_nation and nid not in barrier_by_nation and nid in profiles:
            selected.append(
                RawEvent(
                    nation_id=nid,
                    turn=max(1, profiles[nid].born_turn or 1),
                    event_type="Innovation",
                    category="domestic",
                    region=profiles[nid].region,
                    message="初期結界の定常運用が開始され、生存圏の維持が記録された。",
                )
            )

    world_keep: List[RawEvent] = []
    for ev in world_events:
        if "突入" in ev.message or ev.turn == 1 or ev.turn % 10 == 0 or ev.turn == max_turn:
            world_keep.append(ev)
    selected.extend(world_keep)
    selected.sort(key=lambda e: (e.turn, e.nation_id or 0, e.event_type))

    index = GeoIndex(
        max_turn=max_turn,
        years_per_turn=years_per_turn,
        initial_nations=initial_nations,
        profiles=profiles,
        stats_by_turn=stats_by_turn,
        nation_stats=nation_stats,
        events=selected,
    )
    _write_cache(index)
    return index


def _index_from_cache(cached: dict) -> GeoIndex:
    profiles = {
        int(k): NationProfile(**v) for k, v in cached["profiles"].items()
    }
    nation_stats = {
        (int(t), int(nid)): TurnNationStats(**vals)
        for t, nmap in cached["nation_stats"].items()
        for nid, vals in nmap.items()
    }
    events = [RawEvent(**e) for e in cached["events"]]
    return GeoIndex(
        max_turn=int(cached["max_turn"]),
        years_per_turn=int(cached["years_per_turn"]),
        initial_nations=int(cached["initial_nations"]),
        profiles=profiles,
        stats_by_turn={int(k): v for k, v in cached["stats_by_turn"].items()},
        nation_stats=nation_stats,
        events=events,
    )


def _write_cache(index: GeoIndex) -> None:
    payload = {
        "max_turn": index.max_turn,
        "years_per_turn": index.years_per_turn,
        "initial_nations": index.initial_nations,
        "profiles": {str(k): vars(v) for k, v in index.profiles.items()},
        "stats_by_turn": {str(k): v for k, v in index.stats_by_turn.items()},
        "nation_stats": {},
        "events": [],
    }
    ns: Dict[str, Dict[str, Any]] = {}
    for (turn, nid), st in index.nation_stats.items():
        ns.setdefault(str(turn), {})[str(nid)] = vars(st)
    payload["nation_stats"] = ns
    payload["events"] = [_plain(vars(e)) for e in index.events]
    CACHE_PATH.write_text(json.dumps(payload, ensure_ascii=False), encoding="utf-8")


def events_for_nation(index: GeoIndex, nation_id: int) -> List[RawEvent]:
    return [e for e in index.events if e.nation_id == nation_id]


def events_for_turn(index: GeoIndex, turn: int) -> List[RawEvent]:
    return [e for e in index.events if e.turn == turn]


def top_nations_at(index: GeoIndex, turn: int, n: int = 5) -> List[Tuple[int, TurnNationStats]]:
    ranked: List[Tuple[int, TurnNationStats]] = []
    for nid in range(1, index.initial_nations + 1):
        st = index.nation_stats.get((turn, nid))
        if st is None or not st.alive:
            continue
        ranked.append((nid, st))
    ranked.sort(key=lambda x: x[1].power, reverse=True)
    return ranked[:n]


def main() -> int:
    idx = build_index()
    print(
        f"index: nations={idx.initial_nations} max_turn={idx.max_turn} "
        f"events={len(idx.events)} ypt={idx.years_per_turn}",
        file=sys.stderr,
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

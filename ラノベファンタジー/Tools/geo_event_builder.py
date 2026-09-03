#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""geo 抽出イベントを JsonUtility 互換 simulationEvents に変換する。"""

from __future__ import annotations

import re
from pathlib import Path
from typing import Optional

from geo_sim_source import GeoIndex, RawEvent, TurnNationStats

TOOLS_DIR = Path(__file__).resolve().parent
REPO_ROOT = TOOLS_DIR.parent.parent
MICRO_DIR = REPO_ROOT / "千年史サバイバルログ" / "ミクロ生活史"

REGION_LABELS = {
    "領域・西": "最西端の大陸帯",
    "領域・東": "東方の結晶生産圏",
    "領域・中央": "中央海平原の結節帯",
    "領域・南": "南方の山岳・海洋前線",
    "領域・北": "極寒針葉樹林の前線",
}

ZONE_BY_REGION = {
    "領域・西": "ZONE_WEST_FRONT",
    "領域・東": "ZONE_EAST_CRYSTAL",
    "領域・中央": "ZONE_CENTRAL_PLAIN",
    "領域・南": "ZONE_SOUTH_FRONT",
    "領域・北": "ZONE_NORTH_TAIGA",
}

QUOTE_RE = re.compile(r"「([^」]{8,80})」")
BAD_UI_RE = re.compile(r"国家\d{3}|NATION_\d{3}|区域[AB]")


def evolution(macro_turn: int) -> str:
    if macro_turn <= 100:
        return "Ritual"
    if macro_turn <= 150:
        return "Inspiration"
    return "Recipe"


def region_label(region: str) -> str:
    return REGION_LABELS.get(region, "未知の辺境帯")


def zone_id(region: str) -> str:
    return ZONE_BY_REGION.get(region, "ZONE_FRONTIER")


def nation_id_str(nation_id: int) -> str:
    return f"NATION_{nation_id:03d}"


def condition_flag(nation_id: Optional[int], turn: int, event_type: str) -> str:
    if nation_id is None:
        return f"HIST_WORLD_GEO_TURN_{turn:03d}_{event_type.upper()}"
    return f"HIST_NATION_{nation_id:03d}_GEO_TURN_{turn:03d}_{event_type.upper()}"


def _scrub(text: str) -> str:
    text = BAD_UI_RE.sub("この統治体", text)
    text = re.sub(r"国家(\d{3})", r"この統治体", text)
    return text


def _load_micro(nation_id: int, turn: int, years_per_turn: int) -> str:
    year = turn * years_per_turn
    path = (
        MICRO_DIR
        / f"国家{nation_id:03d}"
        / f"国家{nation_id:03d}_ターン{turn:03d}_叙事年{year:04d}_生活史.md"
    )
    if not path.is_file():
        return ""
    raw = path.read_text(encoding="utf-8")
    raw = re.sub(r"<!--.*?-->", "", raw, flags=re.S).strip()
    raw = BAD_UI_RE.sub("この土地", raw)
    memo = re.search(r"##\s*この年の風俗メモ\s*(.+?)(?:\n##|\Z)", raw, re.S)
    body = memo.group(1).strip() if memo else raw
    body = re.sub(r"^#+\s+.*$", "", body, flags=re.M).strip()
    body = re.sub(r"\n{3,}", "\n\n", body)
    return body[:520]


def _quotes_from(text: str, fallback: str) -> str:
    found = QUOTE_RE.findall(text or "")
    if found:
        return f"「{found[0]}」"
    return fallback


def _stats(index: GeoIndex, nation_id: Optional[int], turn: int) -> Optional[TurnNationStats]:
    if nation_id is None:
        return None
    return index.nation_stats.get((turn, nation_id))


def _prosperity(turn: int, ypt: int, p1d: str, p1b: str, p2d: str, p2v: str, p3d: str, p3r: str) -> dict:
    year = turn * ypt
    return {
        "phase1_accumulation": {
            "timeRange": f"叙事{year}年・年初（ターン{turn:03d}）",
            "npcDialogue": p1d,
            "statusBuff": p1b,
        },
        "phase2_pioneering": {
            "timeRange": f"叙事{year}年・年央（ターン{turn:03d}）",
            "npcDialogue": p2d,
            "visualChange": p2v,
        },
        "phase3_perfection": {
            "timeRange": f"叙事{year}年・年末（ターン{turn:03d}）",
            "npcDialogue": p3d,
            "gameplayReward": p3r,
        },
    }


def _decay(turn: int, ypt: int, p1d: str, p1db: str, p2d: str, p2v: str, p3d: str, p3r: str) -> dict:
    year = turn * ypt
    y0 = max(1, year - 9)
    return {
        "phase1_premonition": {
            "timeRange": f"叙事{y0}年〜{max(y0, year - 7)}年（滅亡10〜7年前）",
            "npcDialogue": p1d,
            "statusDebuff": p1db,
        },
        "phase2_poverty": {
            "timeRange": f"叙事{max(1, year - 6)}年〜{max(1, year - 3)}年（6〜3年前）",
            "npcDialogue": p2d,
            "visualChange": p2v,
        },
        "phase3_critical": {
            "timeRange": f"叙事{max(1, year - 2)}年〜{year}年（2年前〜崩壊）",
            "npcDialogue": p3d,
            "gameplayRisk": p3r,
        },
    }


def _copy_for_event(raw: RawEvent, place: str, micro: str) -> tuple[str, str, str, str, str, str, str, str]:
    et = raw.event_type
    if et == "NationCollapse":
        log = f"【{place}】の防衛結界が崩壊し、統治体は歴史上の欠番として固定された。"
        lore = micro or (
            f"{place}では障壁網が脅威個体群の圧力に抗しきれず、生存圏と配給網が同時に潰えた。"
            "残民は隣接する結界都市へと散り、番号は再利用されない。"
        )
        return (
            log,
            lore,
            "「結界の灯が週ごとに短くなっている。配給も、夜の見回りも、間に合わない。」",
            "国内脅威上昇。工房猶予の短縮。物資配給の不安定化。",
            "「外縁の集落から煙が途絶えた。道は異形の entangle で塞がれている。」".replace(" entangle", "粘糸"),
            "耕作地の放棄、結界の明滅、無主地化の進行。",
            "「最後の結晶炉が落ちる。逃げるなら今だ。」",
            "無主地化。工房猶予 100s→20s。異形の定着。",
        )
    if et == "BarrierDrop":
        drop = raw.metrics.get("drop", 0.0)
        log = f"【{place}】で防衛障壁の循環効率が急落した（低下幅 {drop:.2f}）。"
        lore = micro or (
            f"{place}の障壁網は魔力循環の乱れから出力を落とし、外周監視が間引きされた。"
            "市民は灯火管制と配給削減に晒された。"
        )
        return (
            log,
            lore,
            "「障壁の唸りが低い。結晶を足しても、膜が薄くなる。」",
            "障壁効率低下。夜間警戒の強化。",
            "「外周の光帯に穴が開いた。見回りを倍にしろ。」",
            "結界の明滅、避難民の流入、市場の閉鎖時間延長。",
            "「循環を戻せ。戻らなければ、内側の輪だけでも守れ。」",
            "障壁修復クエスト。Ritual 系の循環補修が解禁候補。",
        )
    if et == "Innovation":
        gain = raw.metrics.get("territory_gain", 0.0)
        coeff = raw.metrics.get("barrier_coeff", 0.0)
        extra = f"循環係数 {coeff:.2f}、生存圏 +{gain:.1f}。" if coeff or gain else ""
        log = f"【{place}】で障壁イノベーションが発生した。{extra}".strip()
        lore = micro or (
            f"{place}の技術層は結界の循環経路を組み直し、限られた魔力でより広い生存圏を維持し始めた。"
        )
        return (
            log,
            lore,
            "「循環を折り返せ。同じ結晶量で膜を厚くできる。」",
            "障壁効率上昇。生存圏の微増。",
            "「新しい結晶柱が点灯した。外周の影が少し遠のいた。」",
            "防衛線の青白い光帯が外側へ伸びる。",
            "「この循環なら、次の活性期も内側の市井は守れる。」",
            "障壁強化ボーナス。進化系統の解禁候補。",
        )
    loss = raw.metrics.get("territory_loss", 0.0)
    log = f"【{place}】が脅威個体群の侵攻を受け、生存圏が損壊した。"
    if loss:
        log = f"【{place}】が脅威侵攻を受け、生存圏が {loss:.1f} 縮小した。"
    lore = micro or (
        f"{place}の前線は異形の波に押し込まれ、耕作地と配給路が寸断された。"
        "人型の敵ではなく、環境と一体化した魔獣が結界を削っている。"
    )
    return (
        log,
        lore,
        "「膜の向こうで土がうねっている。今夜は外出禁止を徹底しろ。」",
        "領土・軍事・経済の低下。異形出没会話の増加。",
        "「外周の灯が三つ消えた。避難民が内門に殺到している。」",
        "放棄された耕作地、粘液痕、明滅する結界。",
        "「内側の輪まで下がる。結晶は監視塔へ回せ。」",
        "防衛失敗ペナルティ。掃討クエスト発生。",
    )


def build_event(
    index: GeoIndex,
    raw: RawEvent,
    prefix: str = "SIM_EVT",
) -> dict:
    ypt = index.years_per_turn
    year = raw.turn * ypt
    nid = raw.nation_id
    target = "WORLD" if nid is None else nation_id_str(nid)
    region = raw.region
    if not region and nid in index.profiles:
        region = index.profiles[nid].region
    place = region_label(region) if region else "全大陸観測網"
    micro = _load_micro(nid, raw.turn, ypt) if nid else ""
    log, lore, p1, p1b, p2, p2v, p3, p3r = _copy_for_event(raw, place, micro)
    if nid is None and raw.message:
        log = raw.message
    log, lore = _scrub(log), _scrub(lore)
    if micro:
        lore = _scrub(micro)
        p1 = _quotes_from(micro, p1)
        p2 = _quotes_from(micro, p2)
        p3 = _quotes_from(micro, p3)

    flag = condition_flag(nid, raw.turn, raw.event_type)
    related = ""
    if raw.related_id:
        related = nation_id_str(raw.related_id)

    month = min(12, max(1, ((raw.turn - 1) % 12) + 1))
    day = min(28, max(1, 1 + (raw.turn * 3) % 27))
    hour = 4 if raw.is_collapse else 10
    st = _stats(index, nid, raw.turn)
    level = 6
    if st:
        level = max(4, min(22, int(8 + st.power / 400)))

    event_id_target = "WORLD" if nid is None else target
    evt = {
        "eventId": f"{prefix}_{event_id_target}_{raw.event_type}_{year}",
        "macroTurn": raw.turn,
        "timestamp": {"year": year, "month": month, "day": day, "hour": hour},
        "targetNationId": target,
        "relatedNationId": related,
        "eventCategory": raw.category,
        "eventType": raw.event_type,
        "logMessageTemplate": log,
        "isHistoricalCollapseRoute": bool(raw.is_collapse),
        "evolutionSystemType": evolution(raw.turn),
        "microLore": lore,
        "conditionFlag": flag,
        "historicalMode": {
            "timeLockedHourOffset": 24 if raw.is_collapse else 12,
            "forcedFlagsOnTrigger": [flag],
        },
        "alternativeMode": {
            "causalPrerequisites": {
                "requiredLogType": "ShieldReinforcementCount" if raw.is_collapse else "MonsterEradicatedCount",
                "requiredValue": 10 if raw.is_collapse else 8,
            },
            "unlockRewards": {
                "alternativeFlag": (
                    f"ALT_WORLD_{raw.event_type.upper()}_T{raw.turn:03d}"
                    if nid is None
                    else f"ALT_NATION_{nid:03d}_{raw.event_type.upper()}_T{raw.turn:03d}"
                ),
                "alternativeLogMessage": _scrub(
                    f"【{place}】で史実の被害を事前掃討により軽減できた。"
                    if not raw.is_collapse
                    else f"【{place}】の結界崩壊を回避し、欠番化を食い止めた。"
                ),
                "alternativeLore": _scrub(
                    f"改変世界線では、{place}の防衛循環が維持され、市井の配給と夜間監視が途切れなかった。"
                ),
            },
        },
        "mmoMode": {
            "zoneId": zone_id(region) if region else "ZONE_WORLD_CYCLE",
            "recommendedLevel": level,
            "progressTriggerFlag": (
                f"MMO_ZONE_WORLD_T{raw.turn:03d}"
                if nid is None
                else f"MMO_{zone_id(region)}_N{nid:03d}_T{raw.turn:03d}"
            ),
        },
    }
    if raw.is_collapse:
        evt["decayTimeline"] = _decay(raw.turn, ypt, p1, p1b, p2, p2v, p3, p3r)
    else:
        evt["prosperityTimeline"] = _prosperity(raw.turn, ypt, p1, p1b, p2, p2v, p3, p3r)
    return evt


def validate_events(events: list[dict], years_per_turn: int = 1) -> list[str]:
    errors: list[str] = []
    seen_ids: set[str] = set()
    for e in events:
        eid = e.get("eventId", "")
        if eid in seen_ids:
            errors.append(f"duplicate eventId: {eid}")
        seen_ids.add(eid)
        year = e.get("timestamp", {}).get("year")
        if year != e.get("macroTurn", 0) * years_per_turn:
            errors.append(f"{eid}: year mismatch")
        collapse = e.get("isHistoricalCollapseRoute", False)
        has_p = "prosperityTimeline" in e
        has_d = "decayTimeline" in e
        if collapse and has_p:
            errors.append(f"{eid}: collapse has prosperity")
        if not collapse and has_d:
            errors.append(f"{eid}: non-collapse has decay")
        flag = e.get("conditionFlag", "")
        if not str(flag).startswith("HIST_"):
            errors.append(f"{eid}: conditionFlag missing HIST_ prefix")
        for field in ("logMessageTemplate", "microLore"):
            if BAD_UI_RE.search(str(e.get(field, ""))):
                errors.append(f"{eid}: bad UI in {field}")
    return errors

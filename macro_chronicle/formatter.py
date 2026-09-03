"""千年史レポートの整形出力."""

from __future__ import annotations

from typing import Dict, List

from .engine import CHAPTER_SIZE, MacroChronicleEngine
from .models import MacroLog, Nation

# 1章あたりの叙事年数（100ターン×10年/ターン ＝ 1000ターン×1年/ターン と整合）
CHAPTER_YEARS = CHAPTER_SIZE * 10
MAX_NATION_LINES_PER_CHAPTER = 80

CHAPTER_TITLES = {
    1: "初期高脅威周期と世界秩序の崩壊",
    2: "生存淘汰と防衛体制の再編",
    3: "中世期・国力底と局地防衛の極限",
    4: "統合圧力と広域防衛網の萌芽",
    5: "魔導工学転換と復興バフの累積",
    6: "覇権競争と構造統合の加速",
    7: "休眠周期支配下の国力再構築",
    8: "物流網復旧と総国力の上昇転換",
    9: "千年末期・淘汰統計の確定",
    10: "最終局面・人類総国力極大値",
}


def _chapter_turn_size(years_per_turn: int) -> int:
    return max(1, CHAPTER_YEARS // years_per_turn)


def _chapter_count(max_turn: int, years_per_turn: int) -> int:
    total_years = max_turn * years_per_turn
    return max(1, (total_years + CHAPTER_YEARS - 1) // CHAPTER_YEARS)


def _year_range(chapter: int, years_per_turn: int, max_turn: int) -> tuple[int, int]:
    y_lo = (chapter - 1) * CHAPTER_YEARS + 1
    y_hi = min(chapter * CHAPTER_YEARS, max_turn * years_per_turn)
    return y_lo, y_hi


def _turn_range(chapter: int, years_per_turn: int, max_turn: int) -> tuple[int, int]:
    step = _chapter_turn_size(years_per_turn)
    t_lo = (chapter - 1) * step + 1
    t_hi = min(chapter * step, max_turn)
    return t_lo, t_hi


def _chapter_title(chapter: int) -> str:
    return CHAPTER_TITLES.get(chapter, f"第{chapter}百年区間")


def _macro_summary_lines(eng: MacroChronicleEngine, t_lo: int, t_hi: int) -> List[str]:
    t_lo = min(max(0, t_lo), len(eng.snapshots) - 1)
    t_hi = min(max(0, t_hi), len(eng.snapshots) - 1)
    snap_lo = eng.snapshots[t_lo]
    snap_hi = eng.snapshots[t_hi]
    return [
        f"- 生存国家数: {snap_lo.alive_count} → {snap_hi.alive_count}",
        f"- 人類総国力ポイント: {snap_lo.total_human_power:,.2f} → {snap_hi.total_human_power:,.2f}",
        f"- 世界平均脅威度指数: {snap_lo.avg_threat:.3f} → {snap_hi.avg_threat:.3f}",
        f"- 全球脅威環境指数: {snap_lo.global_threat_index:.3f} → {snap_hi.global_threat_index:.3f}",
    ]


def _chapter_factor_summary(logs: List[MacroLog], t_lo: int, t_hi: int) -> List[str]:
    cats: Dict[str, int] = {}
    for log in logs:
        if t_lo <= log.turn <= t_hi:
            cats[log.category] = cats.get(log.category, 0) + 1
    lines = []
    labels = {
        "threat_environment": "脅威環境判定",
        "cycle_transition": "活性・休眠周期遷移",
        "monster_attack": "脅威侵攻（突破）",
        "monster_defense": "障壁防衛（拒否）",
        "innovation": "障壁イノベーション",
        "border_conflict": "局地衝突",
        "absorption": "領域統合（吸収）",
        "friction": "隣接摩擦",
        "collapse": "滅亡",
        "birth": "新生",
        "turn_summary": "ターン統計",
    }
    for cat, count in sorted(cats.items(), key=lambda x: -x[1]):
        label = labels.get(cat, cat)
        if cat != "turn_summary":
            lines.append(f"- {label}: {count}件")
    return lines or ["- 該当期間のシステム因子は脅威環境・内政・摩擦の全カテゴリで記録済み。"]


def _nation_event_lines(
    nations: Dict[int, Nation],
    logs: List[MacroLog],
    t_lo: int,
    t_hi: int,
    years_per_turn: int,
) -> List[str]:
    lines: List[str] = []
    seen: set[tuple[int, str]] = set()
    step = _chapter_turn_size(years_per_turn)

    for log in logs:
        if not (t_lo <= log.turn <= t_hi):
            continue
        if log.category not in ("collapse", "birth", "absorption", "innovation"):
            continue
        if log.nation_id is None:
            continue
        key = (log.nation_id, log.category)
        if key in seen and log.category == "innovation":
            continue
        seen.add(key)
        n = nations.get(log.nation_id)
        if not n:
            continue
        year = log.turn * years_per_turn
        ch = (log.turn - 1) // step + 1

        if log.category == "collapse":
            lines.append(
                f"【{n.name}（{n.region}）：滅亡（第{ch}章：{year}年目 / ターン{log.turn}にて崩壊）】"
            )
        elif log.category == "birth":
            lines.append(
                f"【{n.name}（{n.region}）：新生（第{ch}章：{year}年目誕生）】"
            )
        elif log.category == "absorption":
            lines.append(
                f"【{n.name}（{n.region}）：構造変化・吸収統合（第{ch}章：{year}年目 / ターン{log.turn}）】"
            )
        elif log.category == "innovation" and n.nation_id in (1, 2, 15):
            lines.append(
                f"【{n.name}（{n.region}）：障壁技術革新（第{ch}章：{year}年目 / ターン{log.turn}）】"
            )

    for n in nations.values():
        if n.structural_change and n.structural_turn and t_lo <= n.structural_turn <= t_hi:
            year = n.structural_turn * years_per_turn
            ch = (n.structural_turn - 1) // step + 1
            absorber = nations.get(n.absorbed_by) if n.absorbed_by else None
            ab_name = absorber.name if absorber else "不明"
            lines.append(
                f"【{n.name}（{n.region}）：構造変化・{n.structural_change}（第{ch}章：{year}年目 / 統合先{ab_name}）】"
            )

    unique = sorted(set(lines))
    if len(unique) <= MAX_NATION_LINES_PER_CHAPTER:
        return unique
    omitted = len(unique) - MAX_NATION_LINES_PER_CHAPTER
    return unique[:MAX_NATION_LINES_PER_CHAPTER] + [f"- …他 {omitted} 件の個別記録（省略）"]


def format_chronicle(eng: MacroChronicleEngine) -> str:
    ypt = eng.config.years_per_turn
    max_turn = eng.config.macro_turns
    total_years = max_turn * ypt
    max_ch = _chapter_count(max_turn, ypt)
    init_n = eng.config.initial_nations

    parts: List[str] = [
        "# マクロ・クロニクル — 真の千年史サバイバルデータログ",
        "",
        "記録方式: 数理ルール・確率システムのみに基づく客観ログ。情緒的記述・個人エピソードは含まない。",
        f"初期統治体数: {init_n} / 最終ターン: {max_turn} / 1ターン={ypt}年 / 叙事総年数: {total_years}年",
        f"章構成: {max_ch}章（各{CHAPTER_YEARS}年区間）",
        "",
    ]

    for ch in range(1, max_ch + 1):
        y_lo, y_hi = _year_range(ch, ypt, max_turn)
        t_lo, t_hi = _turn_range(ch, ypt, max_turn)
        if t_lo > max_turn:
            break
        parts.append(f"## 【第{ch}章：{y_lo}年目〜{y_hi}年目（ターン{t_lo}〜{t_hi}）】")
        parts.append(f"### {_chapter_title(ch)}")
        parts.append("")
        parts.append("### 1. マクロ情勢統計")
        parts.extend(_macro_summary_lines(eng, t_lo - 1 if t_lo > 0 else 0, t_hi))
        parts.append("")
        parts.append(f"### 2. この{CHAPTER_YEARS}年間に自然発生したマクロ動乱・システム因子の要約")
        parts.extend(_chapter_factor_summary(eng.logs, t_lo, t_hi))
        parts.append("")
        parts.append("### 3. 滅亡・新生・構造変化の個別システムサバイバルログ")
        nation_lines = _nation_event_lines(eng.nations, eng.logs, t_lo, t_hi, ypt)
        if nation_lines:
            parts.extend(nation_lines)
        else:
            parts.append(f"- 該当{CHAPTER_YEARS}年間に滅亡・新生・主要構造変化の確定記録なし。")
        parts.append("")
        parts.append("---")
        parts.append("")

    final = eng.snapshots[-1]
    parts.append(f"## 【{total_years}年紀統計確定】")
    parts.append(f"- 最終生存国家数: {final.alive_count}")
    parts.append(f"- 最終人類総国力: {final.total_human_power:,.2f}")
    parts.append(f"- 最終平均脅威度: {final.avg_threat:.3f}")
    parts.append(
        f"- 滅亡による歴史欠番数: "
        f"{sum(1 for n in eng.nations.values() if not n.alive and n.nation_id <= init_n)}"
    )
    parts.append(
        f"- 新規発行国家数（{init_n + 1}以降）: "
        f"{sum(1 for n in eng.nations.values() if n.nation_id > init_n)}"
    )
    parts.append("")
    return "\n".join(parts)

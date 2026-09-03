"""動的プロンプト組み立て."""

from __future__ import annotations

from typing import Optional

from constitution import WORLD_CONSTITUTION
from data_loader import NationTurnState


def _territory_guidance(territory: float) -> str:
    if territory <= 5:
        return (
            "生存圏は極限まで逼迫している。都市への人口過密、地下集落への潜行、"
            "障壁の限界による閉塞感、不動産不足、スラム化が日常の中心テーマ。"
            "窮屈さ・窒息感・奪い合いを必ず描写する。"
        )
    if territory <= 15:
        return (
            "生存圏は狭い。居住空間の争奪、結界内での縦割り階層、"
            "限られた水・食料配分への不満が市民の口癖になっている。"
        )
    if territory <= 30:
        return (
            "生存圏は中程度。都市と農地のバランスは保たれているが、"
            "魔物周期によって前線からの避難民が周期的に流入する。"
        )
    if territory <= 50:
        return (
            "生存圏に余裕がある。複数の結界都市と農地帯が連携し、"
            "地域ごとの風土・祭礼・職人文化が育っている。"
        )
    return (
        "広大な生存圏を持つ。辺境の新開地、交易路、複数の結界拠点が"
        "国家の誇りとして語り継がれている。"
    )


def _stat_guidance(state: NationTurnState) -> str:
    econ = state.economy
    mil = state.military
    mag = state.magic
    barrier = state.barrier_efficiency

    econ_text = (
        "経済は活況。魔力結晶の流通・市場・職人ギルドが活発。"
        if econ >= 600
        else "経済は安定。配給と物々交換が主。"
        if econ >= 300
        else "経済は停滞。結晶配給の切り詰め、物価高、失業者の増加。"
    )
    mil_text = (
        "防衛隊の練度は高い。市民も避難訓練に慣れ、夜間パトロールが日常。"
        if mil >= 600
        else "防衛隊は及第点。志願兵の入退が激しく、訓練は周期に左右される。"
        if mil >= 300
        else "防衛隊は疲弊。欠員・傷兵・装備不足が市民の不安の種。"
    )
    mag_text = (
        "魔力・結界技術は安定。結晶炉の出力は十分で、障壁技師は自信を持っている。"
        if mag >= 600
        else "魔力は平均的。結界維持に必要最低限の結晶しか回らない。"
        if mag >= 300
        else "魔力は不足。結界の補修が追いつかず、技師たちは徹夜が続いている。"
    )
    barrier_text = (
        "障壁効率は良好。夜も結界の光が安定し、市民は比較的安心して眠れる。"
        if barrier >= 1.0
        else "障壁効率は低下。結界の明滅、防魔出力の不安定さ、夜間への恐怖が市民心理を支配。"
        if barrier >= 0.7
        else "障壁効率は危機的。結界が断続的に消え、避難警報と献祭的結晶投入が常態化。"
    )

    return f"- 経済: {econ_text}\n- 軍事: {mil_text}\n- 魔力: {mag_text}\n- 障壁: {barrier_text}"


def _cycle_guidance(state: NationTurnState) -> str:
    if state.cycle_phase == "active":
        return (
            f"現在は魔物**活性期**（位相 {state.cycle_position}/24）。"
            "結晶配給制限、夜間外出禁止令、前線からの避難、"
            "障壁増強工事、祈りと監視の日常が強調される。"
            "緊迫した空気、配給所の行列、灯火管制を必ず含める。"
        )
    return (
        f"現在は魔物**休眠期**（位相 {state.cycle_position}/24）。"
        "インフラ修復、資源採掘の再開、市場の活気、"
        "束の間の安堵と祭り・結婚・新築の話題が増える。"
        "ただし休眠期の終わりへの不安も根底に残る。"
    )


def _events_block(state: NationTurnState) -> str:
    if not state.events:
        return "（当ターンの記録された国家イベントなし）"
    lines = []
    for ev in state.events:
        cat = ev.get("category", "")
        text = ev.get("event", "")
        lines.append(f"- [{cat}] {text}")
    return "\n".join(lines)


def _life_context(state: NationTurnState) -> str:
    if not state.alive and state.died_turn == state.turn:
        return (
            "**このターンで国家は滅亡する。** 絶望、崩壊、避難の混乱、"
            "結界の崩落、市民の散り散りを強調する。"
        )
    if state.died_turn is not None and state.turn >= state.died_turn - 3:
        return (
            f"滅亡が近い（died_turn={state.died_turn}）。"
            "インフラ劣化、指導部の動揺、希望と諦念が入り混じる。"
        )
    if state.born_turn == state.turn and state.born_turn > 0:
        return "新成立国家の初ターン。建国の熱と不安が共存する。"
    return "通常の存続期。前ターンからの連続性を意識した日常描写。"


def build_system_prompt() -> str:
    return (
        "あなたは架空ファンタジー世界の「一般市民の生活史」を執筆する専門作家である。\n"
        "文字数制限はない。可能な限り詳細に、五感・会話・風俗・社会制度まで描く。\n"
        "出力は Markdown のみ（見出し・段落・箇条書き可）。メタ発言や前置きは不要。\n\n"
        f"{WORLD_CONSTITUTION}"
    )


def build_user_prompt(
    state: NationTurnState,
    custom_instruction: Optional[str] = None,
) -> str:
    territory_guide = _territory_guidance(state.territory)
    stat_guide = _stat_guidance(state)
    cycle_guide = _cycle_guidance(state)
    events = _events_block(state)
    life_ctx = _life_context(state)

    custom_block = ""
    if custom_instruction:
        custom_block = f"\n\n## 追加指示（最優先で反映）\n{custom_instruction.strip()}\n"

    return f"""以下のシミュレーションデータに基づき、**{state.name}** の一般市民の生活史を執筆せよ。

## 時間・周期
- ターン: {state.turn}
- 叙事年: {state.narrative_year}
- 魔物周期: {state.cycle_phase}（位相 {state.cycle_position}/24）
- 全球脅威指数: {state.global_threat_index:.3f}

## 国家ステータス
- 領域: {state.region}
- 存続: {"存続" if state.alive else "滅亡/消滅"}
- 生存圏 (territory): {state.territory:.1f}
- 国力 (power): {state.power:.1f}
- 経済: {state.economy:.1f} / 軍事: {state.military:.1f} / 魔力: {state.magic:.1f}
- 障壁効率: {state.barrier_efficiency:.2f}
- 成立ターン: {state.born_turn} / 滅亡ターン: {state.died_turn or "—"}
- 吸収元: {state.absorbed_by or "—"}

## 執筆ガイド（必須反映）
### 生存圏
{territory_guide}

### 国力・障壁
{stat_guide}

### 魔物周期
{cycle_guide}

### 状況コンテキスト
{life_ctx}

## 当ターンのイベントログ
{events}
{custom_block}
## 出力形式
Markdown ファイル本文として、以下の構成を推奨（見出し名は調整可）:

# {state.name} — 叙事年{state.narrative_year:04d}（ターン{state.turn:03d}）生活史

## 朝の結界
## 市井の営み
## 結晶と配給
## 夜の不安
## この年の風俗メモ

具体的人物（架空名）、地名（架空）、結界技術の細部、食事、信仰、口癖を厚く書くこと。
現実の国名・史実・現代文明用語は一切使わないこと。
"""

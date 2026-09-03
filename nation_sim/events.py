"""イベント種別・確率計算・イベントハンドラ（事実ログのみ）."""

from __future__ import annotations

import random
from dataclasses import dataclass
from enum import Enum
from typing import Callable, Dict, List, Optional

from .models import HistoryEntry, Region, State
from .threat import (
    attack_probability,
    conflict_allowed,
    conflict_weight_multiplier,
    final_threat,
)


class EventCategory(str, Enum):
    MONSTER = "monster"
    DIPLOMACY = "diplomacy"
    DOMESTIC = "domestic"
    CONFLICT = "conflict"
    SYSTEM = "system"


@dataclass
class EventContext:
    turn: int
    states: Dict[str, State]
    regions: Dict[str, Region]
    rng: random.Random
    log: List[HistoryEntry]


def _log(
    ctx: EventContext,
    event: str,
    category: str,
    *,
    region: Optional[str] = None,
    state: Optional[str] = None,
    states: Optional[List[str]] = None,
    result: Optional[Dict[str, object]] = None,
) -> None:
    ctx.log.append(
        HistoryEntry(
            turn=ctx.turn,
            event=event,
            category=category,
            region=region,
            state=state,
            states=states,
            result=result,
        )
    )


def _round(v: float, n: int = 1) -> float:
    return round(v, n)


def _other_states(ctx: EventContext, name: str) -> List[State]:
    return [s for s in ctx.states.values() if s.name != name]


def _same_region_states(state: State, ctx: EventContext) -> List[State]:
    return [
        s
        for s in _other_states(ctx, state.name)
        if s.region_id == state.region_id
    ]


def _pick_partner(
    state: State,
    ctx: EventContext,
    candidates: Optional[List[State]] = None,
    min_relation: float = -20.0,
) -> Optional[State]:
    pool = candidates if candidates is not None else _other_states(ctx, state.name)
    pool = [s for s in pool if state.get_relation(s.name) >= min_relation]
    if not pool:
        return None
    weights = [max(1.0, state.get_relation(s.name) + 50) for s in pool]
    return ctx.rng.choices(pool, weights=weights, k=1)[0]


def _symmetric_relation(a: State, b: State, delta: float) -> None:
    a.adjust_relation(b.name, delta)
    b.adjust_relation(a.name, delta)


# ---------------------------------------------------------------------------
# 対魔物イベント
# ---------------------------------------------------------------------------


def monster_attack_check(state: State, ctx: EventContext) -> None:
    """最終脅威度に基づく魔物襲撃判定。"""
    region = ctx.regions[state.region_id]
    ft = final_threat(state, ctx.regions)
    prob = attack_probability(ft)

    if ctx.rng.random() > prob:
        return

    defense = (
        state.power.military * 0.4
        + state.magic_level * 0.35
        + state.territory_size * 0.05
    )
    attack_power = ft * 30.0 * ctx.rng.uniform(0.85, 1.15)
    repelled = defense >= attack_power

    old_economy = state.power.economy
    old_military = state.power.military
    old_territory = state.territory_size
    old_region_threat = region.threat_level
    old_state_factor = state.domestic_threat_factor

    if repelled:
        mil_loss = ctx.rng.uniform(2, 8)
        state.power.military -= mil_loss
        region.threat_level -= ctx.rng.uniform(0.02, 0.08)
    else:
        territory_loss = ctx.rng.uniform(3, 12)
        economy_loss = ctx.rng.uniform(5, 15)
        military_loss = ctx.rng.uniform(8, 20)
        state.territory_size -= territory_loss
        state.power.economy -= economy_loss
        state.power.military -= military_loss
        region.threat_level += ctx.rng.uniform(0.02, 0.06)
        state.domestic_threat_factor += ctx.rng.uniform(0.02, 0.05)

    region.clamp()
    state.clamp_stats()

    _log(
        ctx,
        "魔物襲撃発生" if not repelled else "魔物襲撃発生_撃退",
        EventCategory.MONSTER.value,
        region=region.name,
        state=state.name,
        result={
            "region_threat": _round(region.threat_level),
            "state_factor": _round(state.domestic_threat_factor),
            "final_threat": ft,
            "attack_probability": _round(prob, 2),
            "repelled": repelled,
            "economy_change": _round(state.power.economy - old_economy),
            "military_change": _round(state.power.military - old_military),
            "territory_change": _round(state.territory_size - old_territory),
            "region_threat_change": _round(region.threat_level - old_region_threat, 2),
            "state_factor_change": _round(
                state.domestic_threat_factor - old_state_factor, 2
            ),
        },
    )


def regional_joint_hunt(state: State, ctx: EventContext) -> None:
    """同一地域の国同士による共同討伐（地域脅威度低下）。"""
    partner = _pick_partner(
        state, ctx, candidates=_same_region_states(state, ctx), min_relation=0.0
    )
    if partner is None:
        return

    region = ctx.regions[state.region_id]
    ft = (final_threat(state, ctx.regions) + final_threat(partner, ctx.regions)) / 2
    combined = (
        state.power.military
        + partner.power.military
        + state.magic_level
        + partner.magic_level
    )
    success = combined >= ft * 35.0 * ctx.rng.uniform(0.9, 1.2)

    old_region_threat = region.threat_level
    old_mil_a = state.power.military
    old_mil_b = partner.power.military
    old_magic_a = state.magic_level
    old_magic_b = partner.magic_level
    old_rel = state.get_relation(partner.name)

    if success:
        region.threat_level -= ctx.rng.uniform(0.08, 0.18)
        magic_gain = ctx.rng.uniform(2, 5)
        state.magic_level += magic_gain
        partner.magic_level += magic_gain
        rel_gain = ctx.rng.uniform(5, 12)
        _symmetric_relation(state, partner, rel_gain)
    else:
        mil_loss = ctx.rng.uniform(4, 10)
        state.power.military -= mil_loss
        partner.power.military -= mil_loss
        region.threat_level += ctx.rng.uniform(0.02, 0.06)
        _symmetric_relation(state, partner, ctx.rng.uniform(1, 4))

    region.clamp()
    state.clamp_stats()
    partner.clamp_stats()

    _log(
        ctx,
        "地域共同討伐_成功" if success else "地域共同討伐_失敗",
        EventCategory.MONSTER.value,
        region=region.name,
        states=[state.name, partner.name],
        result={
            "region_threat_change": _round(region.threat_level - old_region_threat, 2),
            "region_threat": _round(region.threat_level),
            f"{state.name}_military_change": _round(state.power.military - old_mil_a),
            f"{partner.name}_military_change": _round(partner.power.military - old_mil_b),
            f"{state.name}_magic_change": _round(state.magic_level - old_magic_a),
            f"{partner.name}_magic_change": _round(partner.magic_level - old_magic_b),
            "relation_change": _round(state.get_relation(partner.name) - old_rel),
        },
    )


# ---------------------------------------------------------------------------
# 内政イベント
# ---------------------------------------------------------------------------


def barrier_reinforcement(state: State, ctx: EventContext) -> None:
    """結界強化（国内脅威因子低下）。"""
    region = ctx.regions[state.region_id]
    old_factor = state.domestic_threat_factor
    old_economy = state.power.economy
    old_magic = state.magic_level

    factor_reduction = ctx.rng.uniform(0.05, 0.15)
    economy_cost = ctx.rng.uniform(3, 8)
    magic_cost = ctx.rng.uniform(1, 4)

    state.domestic_threat_factor -= factor_reduction
    state.power.economy -= economy_cost
    state.magic_level -= magic_cost
    state.clamp_stats()

    _log(
        ctx,
        "結界強化",
        EventCategory.DOMESTIC.value,
        region=region.name,
        state=state.name,
        result={
            "state_factor_change": _round(state.domestic_threat_factor - old_factor, 2),
            "state_factor": _round(state.domestic_threat_factor),
            "region_threat": _round(region.threat_level),
            "final_threat": final_threat(state, ctx.regions),
            "economy_change": _round(state.power.economy - old_economy),
            "magic_change": _round(state.magic_level - old_magic),
        },
    )


def magic_colonization(state: State, ctx: EventContext) -> None:
    region = ctx.regions[state.region_id]
    old_territory = state.territory_size
    old_economy = state.power.economy
    old_magic = state.magic_level
    old_factor = state.domestic_threat_factor

    territory_gain = ctx.rng.uniform(2, 7)
    economy_cost = ctx.rng.uniform(2, 6)
    magic_gain = ctx.rng.uniform(0.5, 2.0)
    factor_increase = ctx.rng.uniform(0.02, 0.06)

    state.territory_size += territory_gain
    state.power.economy -= economy_cost
    state.magic_level += magic_gain
    state.domestic_threat_factor += factor_increase
    state.clamp_stats()

    _log(
        ctx,
        "魔法開拓",
        EventCategory.DOMESTIC.value,
        region=region.name,
        state=state.name,
        result={
            "territory_change": _round(state.territory_size - old_territory),
            "economy_change": _round(state.power.economy - old_economy),
            "magic_change": _round(state.magic_level - old_magic),
            "state_factor_change": _round(state.domestic_threat_factor - old_factor, 2),
            "final_threat": final_threat(state, ctx.regions),
        },
    )


def magic_mining(state: State, ctx: EventContext) -> None:
    region = ctx.regions[state.region_id]
    old_magic = state.magic_level
    old_economy = state.power.economy

    magic_gain = ctx.rng.uniform(3, 8)
    economy_gain = ctx.rng.uniform(2, 5)
    state.magic_level += magic_gain
    state.power.economy += economy_gain
    state.clamp_stats()

    _log(
        ctx,
        "魔力資源採掘",
        EventCategory.DOMESTIC.value,
        region=region.name,
        state=state.name,
        result={
            "magic_change": _round(state.magic_level - old_magic),
            "economy_change": _round(state.power.economy - old_economy),
        },
    )


def military_buildup(state: State, ctx: EventContext) -> None:
    region = ctx.regions[state.region_id]
    old_military = state.power.military
    old_economy = state.power.economy

    mil_gain = ctx.rng.uniform(4, 10)
    eco_cost = ctx.rng.uniform(2, 5)
    state.power.military += mil_gain
    state.power.economy -= eco_cost
    state.clamp_stats()

    _log(
        ctx,
        "軍備増強",
        EventCategory.DOMESTIC.value,
        region=region.name,
        state=state.name,
        result={
            "military_change": _round(state.power.military - old_military),
            "economy_change": _round(state.power.economy - old_economy),
        },
    )


# ---------------------------------------------------------------------------
# 外交イベント
# ---------------------------------------------------------------------------


def form_alliance(state: State, ctx: EventContext) -> None:
    partner = _pick_partner(state, ctx, min_relation=25.0)
    if partner is None or partner.name in state.alliances:
        return

    old_rel = state.get_relation(partner.name)
    state.alliances.append(partner.name)
    partner.alliances.append(state.name)
    rel_gain = ctx.rng.uniform(12, 22)
    _symmetric_relation(state, partner, rel_gain)

    _log(
        ctx,
        "同盟締結",
        EventCategory.DIPLOMACY.value,
        states=[state.name, partner.name],
        result={
            "relation_change": _round(state.get_relation(partner.name) - old_rel),
        },
    )


def joint_magic_research(state: State, ctx: EventContext) -> None:
    partner = _pick_partner(state, ctx, min_relation=0.0)
    if partner is None:
        return

    old_magic_a = state.magic_level
    old_magic_b = partner.magic_level
    old_rel = state.get_relation(partner.name)

    gain_a = ctx.rng.uniform(2, 6)
    gain_b = ctx.rng.uniform(2, 5)
    state.magic_level += gain_a
    partner.magic_level += gain_b
    rel_gain = ctx.rng.uniform(3, 8)
    _symmetric_relation(state, partner, rel_gain)
    state.clamp_stats()
    partner.clamp_stats()

    _log(
        ctx,
        "魔導技術共同研究",
        EventCategory.DIPLOMACY.value,
        states=[state.name, partner.name],
        result={
            f"{state.name}_magic_change": _round(state.magic_level - old_magic_a),
            f"{partner.name}_magic_change": _round(partner.magic_level - old_magic_b),
            "relation_change": _round(state.get_relation(partner.name) - old_rel),
        },
    )


def improve_relations(state: State, ctx: EventContext) -> None:
    partner = _pick_partner(state, ctx, min_relation=-50.0)
    if partner is None:
        return

    old_rel = state.get_relation(partner.name)
    gain = ctx.rng.uniform(4, 12)
    _symmetric_relation(state, partner, gain)

    _log(
        ctx,
        "関係改善",
        EventCategory.DIPLOMACY.value,
        states=[state.name, partner.name],
        result={"relation_change": _round(state.get_relation(partner.name) - old_rel)},
    )


def territorial_dispute(state: State, ctx: EventContext) -> None:
    partner = _pick_partner(state, ctx, min_relation=-100.0)
    if partner is None or state.get_relation(partner.name) > -15:
        return

    old_rel = state.get_relation(partner.name)
    loss = ctx.rng.uniform(5, 12)
    _symmetric_relation(state, partner, -loss)

    _log(
        ctx,
        "領土不和",
        EventCategory.DIPLOMACY.value,
        states=[state.name, partner.name],
        result={"relation_change": _round(state.get_relation(partner.name) - old_rel)},
    )


# ---------------------------------------------------------------------------
# 紛争イベント（両国の最終脅威度が低い時のみ）
# ---------------------------------------------------------------------------


def _worst_enemy(state: State, ctx: EventContext) -> Optional[State]:
    others = _other_states(ctx, state.name)
    if not others:
        return None
    return min(others, key=lambda s: state.get_relation(s.name))


def declare_war(state: State, ctx: EventContext) -> None:
    enemy = _worst_enemy(state, ctx)
    if enemy is None or state.get_relation(enemy.name) > -45:
        return
    if not conflict_allowed(state, enemy, ctx.regions):
        return

    old_mil_a = state.power.military
    old_mil_b = enemy.power.military
    old_rel = state.get_relation(enemy.name)

    loss_a = ctx.rng.uniform(5, 12)
    loss_b = ctx.rng.uniform(5, 12)
    state.power.military -= loss_a
    enemy.power.military -= loss_b
    rel_loss = ctx.rng.uniform(8, 16)
    _symmetric_relation(state, enemy, -rel_loss)
    state.clamp_stats()
    enemy.clamp_stats()

    _log(
        ctx,
        "宣戦布告",
        EventCategory.CONFLICT.value,
        states=[state.name, enemy.name],
        result={
            f"{state.name}_military_change": _round(state.power.military - old_mil_a),
            f"{enemy.name}_military_change": _round(enemy.power.military - old_mil_b),
            "relation_change": _round(state.get_relation(enemy.name) - old_rel),
            f"{state.name}_final_threat": final_threat(state, ctx.regions),
            f"{enemy.name}_final_threat": final_threat(enemy, ctx.regions),
        },
    )


def local_battle(state: State, ctx: EventContext) -> None:
    enemy = _worst_enemy(state, ctx)
    if enemy is None or state.get_relation(enemy.name) > -30:
        return
    if not conflict_allowed(state, enemy, ctx.regions):
        return

    winner = state if ctx.rng.random() < 0.5 else enemy
    loser = enemy if winner is state else state

    old_territory_w = winner.territory_size
    old_territory_l = loser.territory_size
    old_mil_w = winner.power.military
    old_mil_l = loser.power.military
    old_rel = state.get_relation(enemy.name)

    shift = ctx.rng.uniform(1, 4)
    winner.territory_size += shift * 0.5
    loser.territory_size -= shift
    loser.power.military -= ctx.rng.uniform(3, 7)
    winner.power.military -= ctx.rng.uniform(1, 4)
    _symmetric_relation(state, enemy, -ctx.rng.uniform(4, 10))
    winner.clamp_stats()
    loser.clamp_stats()

    _log(
        ctx,
        "局地戦",
        EventCategory.CONFLICT.value,
        states=[state.name, enemy.name],
        result={
            "winner": winner.name,
            f"{winner.name}_territory_change": _round(
                winner.territory_size - old_territory_w
            ),
            f"{loser.name}_territory_change": _round(loser.territory_size - old_territory_l),
            f"{winner.name}_military_change": _round(winner.power.military - old_mil_w),
            f"{loser.name}_military_change": _round(loser.power.military - old_mil_l),
            "relation_change": _round(state.get_relation(enemy.name) - old_rel),
        },
    )


# ---------------------------------------------------------------------------
# 確率計算
# ---------------------------------------------------------------------------

EventHandler = Callable[[State, EventContext], None]


@dataclass
class WeightedEvent:
    handler: EventHandler
    weight_fn: Callable[[State, EventContext], float]


def _monster_check_weight(state: State, ctx: EventContext) -> float:
    ft = final_threat(state, ctx.regions)
    return 6.0 + ft * 8.0


def _regional_hunt_weight(state: State, ctx: EventContext) -> float:
    region = ctx.regions[state.region_id]
    same = _same_region_states(state, ctx)
    if not same:
        return 0.0
    return 5.0 + region.threat_level * 6.0


def _barrier_weight(state: State, ctx: EventContext) -> float:
    return 4.0 + max(0.0, state.domestic_threat_factor - 1.0) * 10.0


def build_event_table(ctx: EventContext) -> List[WeightedEvent]:
    return [
        WeightedEvent(monster_attack_check, _monster_check_weight),
        WeightedEvent(regional_joint_hunt, _regional_hunt_weight),
        WeightedEvent(barrier_reinforcement, _barrier_weight),
        WeightedEvent(form_alliance, lambda s, c: 3.0),
        WeightedEvent(joint_magic_research, lambda s, c: 6.0),
        WeightedEvent(improve_relations, lambda s, c: 8.0),
        WeightedEvent(territorial_dispute, lambda s, c: 4.0),
        WeightedEvent(magic_colonization, lambda s, c: 6.0),
        WeightedEvent(magic_mining, lambda s, c: 8.0),
        WeightedEvent(military_buildup, lambda s, c: 7.0),
        WeightedEvent(
            declare_war,
            lambda s, c: (
                max(0.0, (-s.get_relation(_worst_enemy(s, c).name) - 35) * 0.25)
                * conflict_weight_multiplier(s, c.regions)
                if (enemy := _worst_enemy(s, c)) is not None
                and conflict_allowed(s, enemy, c.regions)
                else 0.0
            ),
        ),
        WeightedEvent(
            local_battle,
            lambda s, c: (
                max(0.0, (-s.get_relation(_worst_enemy(s, c).name) - 20) * 0.35)
                * conflict_weight_multiplier(s, c.regions)
                if (enemy := _worst_enemy(s, c)) is not None
                and conflict_allowed(s, enemy, c.regions)
                else 0.0
            ),
        ),
    ]


def select_events_for_state(
    state: State, ctx: EventContext, max_events: int = 2
) -> List[EventHandler]:
    table = build_event_table(ctx)
    handlers: List[EventHandler] = []
    attempts = 0
    while len(handlers) < max_events and attempts < max_events * 4:
        attempts += 1
        weights = [max(0.0, e.weight_fn(state, ctx)) for e in table]
        total = sum(weights)
        if total <= 0:
            break
        chosen = ctx.rng.choices(table, weights=weights, k=1)[0]
        if chosen.handler not in handlers:
            handlers.append(chosen.handler)
    return handlers


def tick_region_threats(ctx: EventContext) -> None:
    """ターン開始時の地域脅威度の自然変動。"""
    for region in ctx.regions.values():
        old = region.threat_level
        delta = ctx.rng.uniform(-0.06, 0.08)
        if ctx.rng.random() < 0.12:
            delta += ctx.rng.uniform(0.05, 0.12)
        region.threat_level += delta
        region.clamp()

        if abs(delta) >= 0.04:
            _log(
                ctx,
                "地域脅威度変動",
                EventCategory.SYSTEM.value,
                region=region.name,
                result={
                    "region_threat_change": _round(region.threat_level - old, 2),
                    "region_threat": _round(region.threat_level),
                },
            )


def passive_turn_effects(ctx: EventContext) -> None:
    for state in ctx.states.values():
        state.power.economy += ctx.rng.uniform(0.5, 2.0)
        state.clamp_stats()

    names = list(ctx.states.keys())
    for i, a_name in enumerate(names):
        for b_name in names[i + 1 :]:
            a = ctx.states[a_name]
            b = ctx.states[b_name]
            _symmetric_relation(a, b, ctx.rng.uniform(-0.4, 0.4))

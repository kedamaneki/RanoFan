"""脅威度計算ユーティリティ."""

from __future__ import annotations

from typing import Dict

from .models import Region, State

# 人間同士の紛争が発生しやすくなる最終脅威度の上限
PEACEFUL_THREAT_THRESHOLD = 1.0


def final_threat(state: State, regions: Dict[str, Region]) -> float:
    return state.final_threat(regions)


def attack_probability(final: float) -> float:
    """最終脅威度に基づく襲撃発生確率。"""
    return min(0.85, 0.08 + final * 0.22)


def conflict_allowed(state_a: State, state_b: State, regions: Dict[str, Region]) -> bool:
    """両国の最終脅威度がともに低い場合のみ紛争可能。"""
    return (
        final_threat(state_a, regions) < PEACEFUL_THREAT_THRESHOLD
        and final_threat(state_b, regions) < PEACEFUL_THREAT_THRESHOLD
    )


def conflict_weight_multiplier(state: State, regions: Dict[str, Region]) -> float:
    """最終脅威度が低いほど紛争確率が上がる。"""
    ft = final_threat(state, regions)
    if ft >= PEACEFUL_THREAT_THRESHOLD:
        return 0.0
    if ft >= 0.8:
        return 0.2
    if ft >= 0.6:
        return 0.5
    return 1.0

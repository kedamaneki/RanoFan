"""デモ実行エントリポイント。

Usage:
    python -m nation_sim
    python -m nation_sim --turns 24 --seed 42 --output history.json
"""

from __future__ import annotations

import argparse
import json
from pathlib import Path

from .engine import SimulationEngine
from .world_setup import create_initial_world


def _print_turn_summary(engine: SimulationEngine, turn: int) -> None:
    logs = engine.get_turn_logs(turn)
    print(f"\n{'=' * 60}")
    print(f"ターン {turn}")
    print("-" * 60)
    for entry in logs:
        line = json.dumps(entry, ensure_ascii=False)
        print(f"  {line}")


def main() -> None:
    parser = argparse.ArgumentParser(
        description="魔法IF地球 国家シミュレーション コアエンジン"
    )
    parser.add_argument("--turns", type=int, default=12, help="シミュレーションターン数（月数）")
    parser.add_argument("--seed", type=int, default=42, help="乱数シード")
    parser.add_argument(
        "--output",
        type=str,
        default="history_log.json",
        help="履歴ログの出力先JSONファイル",
    )
    parser.add_argument("--quiet", action="store_true", help="ターン毎の表示を抑制")
    args = parser.parse_args()

    engine = create_initial_world(seed=args.seed)

    print("=== 魔法IF地球 国家シミュレーション ===")
    print("地域:")
    for region in engine.regions.values():
        print(f"  {region.name} | 地域脅威度:{region.threat_level:.2f}")
    print("\n国家:")
    for state in engine.state_list:
        region_name = engine.regions[state.region_id].name
        ft = state.final_threat(engine.regions)
        print(
            f"  {state.name} | 地域:{region_name} | "
            f"経済:{state.power.economy:.0f} 軍事:{state.power.military:.0f} | "
            f"魔力:{state.magic_level:.0f} | 領土:{state.territory_size:.0f} | "
            f"国内因子:{state.domestic_threat_factor:.2f} | 最終脅威:{ft:.2f}"
        )
    print(f"\n{args.turns}ターンのシミュレーションを開始...\n")

    for _ in range(args.turns):
        engine.process_turn()
        if not args.quiet:
            _print_turn_summary(engine, engine.current_turn)

    output_path = Path(args.output)
    engine.save_history(output_path)

    print(f"\n{'=' * 60}")
    print("シミュレーション完了")
    print(f"総ログ件数: {len(engine.history_log)}")
    print(f"履歴ログ保存先: {output_path.resolve()}")
    print("\n最終世界状態:")
    print(json.dumps(engine.snapshot(), ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()

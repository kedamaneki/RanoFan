"""マクロ・クロニクル CLI."""

from __future__ import annotations

import argparse
from pathlib import Path

from .config import SimulationConfig
from .engine import MACRO_TURNS
from .runner import run_simulation, write_outputs


def main() -> None:
    parser = argparse.ArgumentParser(description="1000年史マクロ・クロニクル・シミュレーター")
    parser.add_argument("--turns", type=int, default=None, help="マクロターン数")
    parser.add_argument("--nations", type=int, default=None, help="初期国家数（50〜500）")
    parser.add_argument("--seed", type=int, default=None, help="乱数シード")
    parser.add_argument(
        "-o",
        "--output-dir",
        type=Path,
        default=Path("."),
        help="出力ディレクトリ",
    )
    parser.add_argument(
        "--config",
        type=Path,
        default=None,
        help="設定JSON（macro_chronicle_config.json）",
    )
    args = parser.parse_args()

    if args.config and args.config.is_file():
        cfg = SimulationConfig.load(args.config)
    else:
        cfg = SimulationConfig.load()

    if args.turns is not None:
        cfg.macro_turns = args.turns
    if args.nations is not None:
        cfg.initial_nations = args.nations
    if args.seed is not None:
        cfg.seed = args.seed

    eng, geo = run_simulation(cfg)
    paths = write_outputs(eng, geo, args.output_dir)

    final = eng.snapshots[-1]
    print(
        f"完了: {cfg.macro_turns}ターン / 初期{cfg.initial_nations}国 / "
        f"生存{final.alive_count}国 / 総国力{final.total_human_power:,.2f}"
    )
    print(f"ログ: {paths['log']}")
    print(f"レポート: {paths['report']}")
    print(f"分析: {paths['analytics']}")
    print(f"地図データ: {paths['geo']}")


if __name__ == "__main__":
    main()

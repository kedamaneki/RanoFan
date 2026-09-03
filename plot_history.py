import argparse
import json
import os
import sys
from pathlib import Path

import matplotlib.pyplot as plt
import pandas as pd
from matplotlib import font_manager, rcParams


def configure_console_encoding() -> None:
    for stream in (sys.stdout, sys.stderr):
        reconfigure = getattr(stream, "reconfigure", None)
        if reconfigure is not None:
            try:
                reconfigure(encoding="utf-8")
            except (AttributeError, OSError, ValueError):
                pass


def configure_japanese_font() -> None:
    rcParams["axes.unicode_minus"] = False

    if sys.platform == "win32":
        fonts_dir = Path(os.environ.get("WINDIR", r"C:\Windows")) / "Fonts"
        for fname in ("YuGothR.ttc", "YuGothM.ttc", "meiryo.ttc", "msgothic.ttc"):
            font_path = fonts_dir / fname
            if not font_path.exists():
                continue
            font_manager.fontManager.addfont(str(font_path))
            prop = font_manager.FontProperties(fname=str(font_path))
            font_name = prop.get_name()
            rcParams["font.family"] = font_name
            rcParams["font.sans-serif"] = [font_name, "DejaVu Sans"]
            return

    candidates = [
        "Yu Gothic",
        "YuGothic",
        "Meiryo",
        "MS Gothic",
        "Noto Sans CJK JP",
        "Hiragino Sans",
        "IPAGothic",
    ]
    available = {font.name for font in font_manager.fontManager.ttflist}
    for name in candidates:
        if name in available:
            rcParams["font.family"] = name
            rcParams["font.sans-serif"] = [name, "DejaVu Sans"]
            return


def load_analytics(json_path: Path) -> pd.DataFrame:
    with json_path.open("r", encoding="utf-8") as f:
        records = json.load(f)

    if not isinstance(records, list) or len(records) == 0:
        raise ValueError("world_analytics.json が空、または配列形式ではありません。")

    df = pd.DataFrame(records)
    required = [
        "turn",
        "year",
        "month",
        "alive_countries",
        "total_human_power",
        "average_monster_threat",
    ]
    missing = [col for col in required if col not in df.columns]
    if missing:
        raise ValueError(f"必須キーが不足しています: {', '.join(missing)}")

    df = df.sort_values("turn").reset_index(drop=True)
    return df


def plot_history(df: pd.DataFrame, output_path: Path) -> None:
    configure_japanese_font()
    plt.style.use("seaborn-v0_8-darkgrid")
    configure_japanese_font()
    fig, axes = plt.subplots(3, 1, figsize=(13, 10), sharex=True)

    x = df["turn"]

    axes[0].plot(x, df["alive_countries"], color="#2E86C1", linewidth=2)
    axes[0].set_title("国の数の推移 (Alive Countries)")
    axes[0].set_ylabel("国数")

    axes[1].plot(x, df["total_human_power"], color="#27AE60", linewidth=2)
    axes[1].set_title("人類総国力の推移 (Total Human Power)")
    axes[1].set_ylabel("総国力")

    axes[2].plot(x, df["average_monster_threat"], color="#C0392B", linewidth=2)
    axes[2].set_title("魔物平均脅威度の推移 (Average Monster Threat)")
    axes[2].set_xlabel("ターン")
    axes[2].set_ylabel("脅威度")

    for ax in axes:
        ax.margins(x=0.01)

    fig.suptitle("世界シミュレーション推移", fontsize=14)
    fig.tight_layout(rect=(0, 0, 1, 0.97))
    fig.savefig(output_path, dpi=180)
    if os.environ.get("MPLBACKEND", "").lower() != "agg":
        plt.show()


def main() -> None:
    configure_console_encoding()
    parser = argparse.ArgumentParser(
        description="world_analytics.json を読み込み、履歴グラフを表示・保存します。"
    )
    parser.add_argument(
        "--input",
        default="world_analytics.json",
        help="入力JSONファイルのパス (default: world_analytics.json)",
    )
    parser.add_argument(
        "--output",
        default="simulation_trend.png",
        help="出力画像ファイルのパス (default: simulation_trend.png)",
    )
    args = parser.parse_args()

    input_path = Path(args.input)
    output_path = Path(args.output)

    if not input_path.exists():
        raise FileNotFoundError(f"入力ファイルが見つかりません: {input_path}")

    df = load_analytics(input_path)
    plot_history(df, output_path)
    print(f"Saved: {output_path.resolve()}")


if __name__ == "__main__":
    main()

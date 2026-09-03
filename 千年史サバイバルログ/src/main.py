#!/usr/bin/env python3
"""
千年史サバイバルログ — ローカルLLM日常描写量産システム

macro_chronicle_geo.json を読み込み、国家×ターンごとに
Ollama 経由で一般市民の生活史 Markdown を生成する。
"""

from __future__ import annotations

import argparse
import logging
import sys
import time
from pathlib import Path
from typing import List, Optional

from data_loader import ChronicleDataLoader, NationTurnState
from ollama_client import OllamaClient, OllamaError
from paths import DEFAULT_GEO, ensure_directories, output_path
from prompt_engine import build_system_prompt, build_user_prompt

logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s [%(levelname)s] %(message)s",
    datefmt="%H:%M:%S",
)
logger = logging.getLogger("micro_chronicle")


def parse_nation_ids(raw: Optional[str]) -> Optional[List[int]]:
    if not raw:
        return None
    ids: List[int] = []
    for part in raw.replace(" ", "").split(","):
        if not part:
            continue
        if part.startswith("国家"):
            part = part.replace("国家", "")
        ids.append(int(part))
    return ids


def write_markdown(path: Path, content: str, state: NationTurnState) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    header = (
        f"<!-- generated: nation={state.nation_id} turn={state.turn} "
        f"narrative_year={state.narrative_year} cycle={state.cycle_phase} -->\n\n"
    )
    path.write_text(header + content + "\n", encoding="utf-8")


def process_one(
    state: NationTurnState,
    client: OllamaClient,
    force: bool,
    custom_instruction: Optional[str],
    dry_run: bool,
    temperature: float = 0.8,
) -> str:
    """
    1件処理。戻り値: 'skipped' | 'ok' | 'error'
    """
    out = output_path(state)

    if out.exists() and not force:
        logger.debug("スキップ（既存）: %s", out)
        return "skipped"

    system = build_system_prompt()
    user = build_user_prompt(state, custom_instruction=custom_instruction)

    if dry_run:
        logger.info("[DRY-RUN] 生成予定: %s", out)
        return "ok"

    try:
        content = client.generate(system, user, temperature=temperature)
        write_markdown(out, content, state)
        logger.info("保存完了: %s (%d 文字)", out, len(content))
        return "ok"
    except OllamaError as exc:
        logger.error("生成失敗 %s: %s", out, exc)
        return "error"


def build_parser() -> argparse.ArgumentParser:
    p = argparse.ArgumentParser(
        description="架空世界千年史 — 一般市民生活史の自動執筆（Ollama）",
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog="""
使用例:
  # 全国家・全ターン（既存ファイルはスキップ）
  python main.py

  # 国家112のみ、ターン1〜100
  python main.py --country 112 --start-turn 1 --end-turn 100

  # 特定ターンを強制上書き＋カスタム指示
  python main.py --country 112 --turn 46 --force \\
    --custom_instruction "前回はのんきすぎました。滅亡が近い絶望感を強調。"

  # 接続確認のみ
  python main.py --check-ollama
        """,
    )
    p.add_argument(
        "--geo",
        type=Path,
        default=DEFAULT_GEO,
        help=f"macro_chronicle_geo.json のパス (既定: {DEFAULT_GEO})",
    )
    p.add_argument(
        "--country",
        "--countries",
        dest="countries",
        type=str,
        default=None,
        help="対象国家ID（カンマ区切り。例: 1,112 または 国家001,国家112）",
    )
    p.add_argument("--start-turn", type=int, default=1, help="開始ターン (既定: 1)")
    p.add_argument("--end-turn", type=int, default=None, help="終了ターン (既定: meta.turns)")
    p.add_argument(
        "--turn",
        type=int,
        default=None,
        help="単一ターン指定（--start-turn/--end-turn より優先）",
    )
    p.add_argument(
        "--force",
        action="store_true",
        help="既存ファイルがあっても上書き再生成",
    )
    p.add_argument(
        "--custom_instruction",
        type=str,
        default=None,
        help="再執筆用の追加指示（矛盾修正・トーン調整など）",
    )
    p.add_argument(
        "--include-dead",
        action="store_true",
        help="滅亡後ターンも含める（通常は存続期のみ）",
    )
    p.add_argument(
        "--ollama-url",
        default="http://localhost:11434",
        help="Ollama API ベースURL",
    )
    p.add_argument(
        "--model",
        default="qwen2.5:14b",
        help="Ollama モデル名 (例: qwen2.5:7b, qwen2.5:14b)",
    )
    p.add_argument("--timeout", type=int, default=600, help="1リクエストのタイムアウト秒")
    p.add_argument("--retries", type=int, default=3, help="API リトライ回数")
    p.add_argument("--temperature", type=float, default=0.8, help="生成温度")
    p.add_argument(
        "--dry-run",
        action="store_true",
        help="Ollama を呼ばず、処理対象のみ表示",
    )
    p.add_argument(
        "--check-ollama",
        action="store_true",
        help="Ollama 接続確認して終了",
    )
    p.add_argument(
        "--limit",
        type=int,
        default=None,
        help="処理件数上限（テスト用）",
    )
    p.add_argument("-v", "--verbose", action="store_true", help="詳細ログ")
    return p


def main(argv: Optional[List[str]] = None) -> int:
    args = build_parser().parse_args(argv)

    if args.verbose:
        logging.getLogger().setLevel(logging.DEBUG)

    ensure_directories()

    client = OllamaClient(
        base_url=args.ollama_url,
        model=args.model,
        timeout=args.timeout,
        max_retries=args.retries,
    )

    if args.check_ollama:
        ok = client.health_check()
        if ok:
            logger.info("Ollama 接続 OK: %s (model=%s)", args.ollama_url, args.model)
            return 0
        logger.error("Ollama に接続できません: %s", args.ollama_url)
        return 1

    try:
        loader = ChronicleDataLoader(args.geo)
        loader.load()
    except FileNotFoundError as exc:
        logger.error("%s", exc)
        return 1
    except (KeyError, ValueError, TypeError) as exc:
        logger.error("geo JSON の解析に失敗: %s", exc)
        return 1

    logger.info(
        "データ読込: turns=%d, nations=%d, years_per_turn=%d, geo=%s",
        loader.max_turn,
        len(loader.nation_ids),
        loader.years_per_turn,
        args.geo,
    )

    nation_ids = parse_nation_ids(args.countries)

    start_turn = args.start_turn
    end_turn = args.end_turn if args.end_turn is not None else loader.max_turn
    if args.turn is not None:
        start_turn = end_turn = args.turn

    if not args.dry_run and not client.health_check():
        logger.error(
            "Ollama が起動していません。`ollama serve` を実行するか --dry-run を使用してください。"
        )
        return 1

    stats = {"ok": 0, "skipped": 0, "error": 0}
    t0 = time.time()
    count = 0

    for state in loader.iter_jobs(
        nation_ids=nation_ids,
        start_turn=start_turn,
        end_turn=end_turn,
        skip_dead_after=not args.include_dead,
    ):
        if args.limit is not None and count >= args.limit:
            break

        result = process_one(
            state=state,
            client=client,
            force=args.force,
            custom_instruction=args.custom_instruction,
            dry_run=args.dry_run,
            temperature=args.temperature,
        )
        stats[result] = stats.get(result, 0) + 1
        count += 1

        if count % 10 == 0:
            logger.info("進捗: %d 件処理 (ok=%d skip=%d err=%d)", count, stats["ok"], stats["skipped"], stats["error"])

    elapsed = time.time() - t0
    logger.info(
        "完了: 処理=%d, 生成=%d, スキップ=%d, エラー=%d, 経過=%.1fs",
        count,
        stats["ok"],
        stats["skipped"],
        stats["error"],
        elapsed,
    )
    return 1 if stats["error"] and stats["ok"] == 0 else 0


if __name__ == "__main__":
    sys.exit(main())

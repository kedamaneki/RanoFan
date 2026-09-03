#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""現行マクロラン（ターン1〜250）から spine/SIM_T{NNN}.json を生成する。

用法:
  python generate_spine.py
  python generate_spine.py --macro-turn 1 --macro-turn-end 250
"""

from __future__ import annotations

import argparse
import sys

from generate_from_current_run import generate_spine
from geo_sim_source import DEFAULT_MAX_TURN, build_index


def main() -> int:
    parser = argparse.ArgumentParser(description="現行マクロラン spine JSON 生成")
    parser.add_argument("--macro-turn", type=int, default=1)
    parser.add_argument("--macro-turn-end", type=int, default=None)
    parser.add_argument("--max-turn", type=int, default=DEFAULT_MAX_TURN)
    parser.add_argument("--no-cache", action="store_true")
    args = parser.parse_args()
    index = build_index(max_turn=args.max_turn, use_cache=not args.no_cache)
    generate_spine(index)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

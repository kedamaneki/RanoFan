#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""現行マクロラン（ターン1〜250）から sections/SIM_CH{NN}_SEC{NN}.json を生成する。

用法:
  python generate_sections.py
  python generate_sections.py --list
"""

from __future__ import annotations

import argparse

from generate_from_current_run import _section_bounds, generate_sections
from geo_sim_source import DEFAULT_MAX_TURN, build_index


def main() -> int:
    parser = argparse.ArgumentParser(description="現行マクロラン 章・節 JSON 生成")
    parser.add_argument("--chapter", type=int, default=None)
    parser.add_argument("--section", type=int, default=None)
    parser.add_argument("--section-end", type=int, default=None)
    parser.add_argument("--list", action="store_true")
    parser.add_argument("--max-turn", type=int, default=DEFAULT_MAX_TURN)
    parser.add_argument("--no-cache", action="store_true")
    args = parser.parse_args()

    if args.list:
        for (ch, sec), (lo, hi) in _section_bounds(args.max_turn).items():
            print(f"CH{ch:02d} SEC{sec:02d}  T{lo:03d}-T{hi:03d}")
        return 0

    index = build_index(max_turn=args.max_turn, use_cache=not args.no_cache)
    generate_sections(index)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

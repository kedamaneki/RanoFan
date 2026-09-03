#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""現行マクロラン（ターン250時点）から greatpowers/SIM_GP_*.json を生成する。

用法:
  python generate_greatpowers.py
  python generate_greatpowers.py --all
  python generate_greatpowers.py --list
"""

from __future__ import annotations

import argparse

from generate_from_current_run import generate_greatpowers
from geo_event_builder import region_label
from geo_sim_source import DEFAULT_MAX_TURN, build_index, top_nations_at


def main() -> int:
    parser = argparse.ArgumentParser(description="現行マクロラン 大国追補 JSON 生成")
    parser.add_argument("--batch", type=str, default=None)
    parser.add_argument("--batch-end", type=str, default=None)
    parser.add_argument("--all", action="store_true")
    parser.add_argument("--list", action="store_true")
    parser.add_argument("--max-turn", type=int, default=DEFAULT_MAX_TURN)
    parser.add_argument("--no-cache", action="store_true")
    args = parser.parse_args()

    index = build_index(max_turn=args.max_turn, use_cache=not args.no_cache)
    if args.list:
        for nid, st in top_nations_at(index, index.max_turn, n=5):
            region = index.profiles[nid].region
            print(
                f"NATION_{nid:03d}  power={st.power:.1f}  "
                f"barrier={st.barrier:.2f}  {region_label(region)}"
            )
        return 0
    generate_greatpowers(index)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())

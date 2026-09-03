#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""現行マクロラン（ターン1〜250）から nations/SIM_NATION_{NNN}.json を生成する。

用法:
  python generate_nations.py
  python generate_nations.py --start 1 --end 173
  python generate_nations.py --batch 06
"""

from __future__ import annotations

import sys

from generate_from_current_run import main as run_main


def main() -> int:
    argv = ["--nations-only", *sys.argv[1:]]
    return run_main(argv)


if __name__ == "__main__":
    raise SystemExit(main())

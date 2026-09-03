#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""現行マクロラン（ターン1〜250）: 国家006-010 の simulationEvents を生成する。"""

from __future__ import annotations

from generate_from_current_run import main

if __name__ == "__main__":
    raise SystemExit(main(["--nations-only", "--batch", "07"]))

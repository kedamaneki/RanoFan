"""pipeline_job.json の生成・読取・プロンプト推論。"""
from __future__ import annotations

import argparse
import json
import os
import re
import sys
from typing import Any

JOB_FILENAME = "pipeline_job.json"

DEFAULT_JOB: dict[str, Any] = {
    "schemaVersion": 1,
    "jobType": "magiChronicleLoop",
    "startTurn": 1001,
    "endTurn": 1050,
    "generationSpan": 50,
    "branchCount": 7,
    "commitPolicy": "skuldPruningMax",
    "preferThemes": ["学術", "街道開拓", "復興"],
    "exportCandidates": True,
    "promptSummary": "",
}


def job_path(project_root: str) -> str:
    return os.path.join(project_root, JOB_FILENAME)


def write_job(project_root: str, job: dict[str, Any]) -> str:
    path = job_path(project_root)
    merged = dict(DEFAULT_JOB)
    merged.update(job or {})
    themes = merged.get("preferThemes") or []
    if isinstance(themes, str):
        themes = [t for t in themes.split(",") if t.strip()]
        merged["preferThemes"] = themes
    merged["preferThemesCsv"] = ",".join(themes)
    with open(path, "w", encoding="utf-8") as f:
        json.dump(merged, f, ensure_ascii=False, indent=2)
        f.write("\n")
    return path


def load_job(project_root: str) -> dict[str, Any]:
    path = job_path(project_root)
    if not os.path.isfile(path):
        return dict(DEFAULT_JOB)
    with open(path, "r", encoding="utf-8") as f:
        data = json.load(f)
    merged = dict(DEFAULT_JOB)
    merged.update(data or {})
    return merged


def _extract_turn_numbers(prompt: str) -> list[int]:
    """プロンプトからシミュ年代らしい数値（1000〜2999）を抽出。"""
    nums = []
    for m in re.finditer(r"(?<![0-9])(1\d{3}|2\d{3})(?![0-9])", prompt or ""):
        nums.append(int(m.group(1)))
    return nums


def infer_job_from_prompt(prompt: str) -> dict[str, Any]:
    """自然言語指示から job を推定（Safe-Fail: 不明なら復興50年相当）。"""
    job = dict(DEFAULT_JOB)
    text = prompt or ""
    job["promptSummary"] = text[:120]

    turns = _extract_turn_numbers(text)
    span_m = re.search(r"(\d+)\s*年\s*世代|世代.*?(\d+)\s*年|span\s*=\s*(\d+)", text)
    if span_m:
        for g in span_m.groups():
            if g:
                job["generationSpan"] = max(1, int(g))
                break

    # テーマヒント
    themes = []
    for key, label in (
        ("学術", "学術"),
        ("魔導", "学術"),
        ("街道", "街道開拓"),
        ("開拓", "街道開拓"),
        ("交易", "交易"),
        ("共生", "共生"),
        ("結界", "結界"),
        ("復興", "復興"),
        ("文明", "復興"),
    ):
        if key in text and label not in themes:
            themes.append(label)
    if themes:
        job["preferThemes"] = themes
    job["preferThemesCsv"] = ",".join(job.get("preferThemes") or [])

    if "剪定" in text or "スクルド" in text or "可能性" in text:
        job["commitPolicy"] = "skuldPruningMax"

    if turns:
        start = min(turns)
        end = max(turns)
        if end > start:
            job["startTurn"] = start
            job["endTurn"] = end

    # フレーズ別 jobType
    if re.search(r"検証|verify|MAGI検証", text, re.I) and not re.search(
        r"連続|進行|コミット|量産", text
    ):
        job["jobType"] = "magiVerify"
        return job

    span_years = job["endTurn"] - job["startTurn"] + 1
    if span_years <= 55 and job["startTurn"] <= 1001 and job["endTurn"] <= 1051:
        job["jobType"] = "civilizationRevival50"
        job["startTurn"] = max(job["startTurn"], 1001)
        job["endTurn"] = max(job["endTurn"], 1050)
    else:
        job["jobType"] = "magiChronicleLoop"
        # 「1050から1250」「第22〜25世代」→ 次世代開始を 1051 に正規化
        if job["startTurn"] == 1050 and job["endTurn"] >= 1250:
            job["startTurn"] = 1051
            job["endTurn"] = 1250
            job["generationSpan"] = 50
        elif (job["endTurn"] - job["startTurn"] + 1) >= 100:
            # 連続進行
            if job["generationSpan"] < 10:
                job["generationSpan"] = 50

    return job


def apply_cli_overrides(job: dict[str, Any], args: argparse.Namespace) -> dict[str, Any]:
    out = dict(job)
    if getattr(args, "job_type", None):
        out["jobType"] = args.job_type
    if getattr(args, "start", None) is not None:
        out["startTurn"] = int(args.start)
    if getattr(args, "end", None) is not None:
        out["endTurn"] = int(args.end)
    if getattr(args, "span", None) is not None:
        out["generationSpan"] = int(args.span)
    if getattr(args, "branches", None) is not None:
        out["branchCount"] = int(args.branches)
    return out


def build_arg_parser() -> argparse.ArgumentParser:
    p = argparse.ArgumentParser(add_help=False)
    p.add_argument("--job-type", dest="job_type", default=None)
    p.add_argument("--start", type=int, default=None)
    p.add_argument("--end", type=int, default=None)
    p.add_argument("--span", type=int, default=None)
    p.add_argument("--branches", type=int, default=None)
    return p


def prepare_job_from_argv(project_root: str, argv: list[str]) -> tuple[dict[str, Any], str, str]:
    """
    argv から CLI オプションとプロンプトを分離し job を書いて返す。
    Returns: (job, job_path, prompt)
    """
    parser = build_arg_parser()
    args, rest = parser.parse_known_args(argv)
    prompt = " ".join(rest).strip()
    job = infer_job_from_prompt(prompt)
    job = apply_cli_overrides(job, args)
    path = write_job(project_root, job)
    return job, path, prompt


if __name__ == "__main__":
    root = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
    job, path, prompt = prepare_job_from_argv(root, sys.argv[1:])
    print(json.dumps(job, ensure_ascii=False, indent=2))
    print(f"wrote: {path}")

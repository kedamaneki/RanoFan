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


def _extract_explicit_turn_range(prompt: str) -> tuple[int | None, int | None]:
    """
    「ターン1001〜3000」「T1001からT3000まで」など明示レンジを優先抽出。
    3000 以上も許可（旧実装は 1000〜2999 のみで 3000 を落としていた）。
    """
    text = prompt or ""
    patterns = (
        r"(?:ターン|turn|T)\s*(\d{3,5})\s*[〜~\-–—から]+\s*(?:ターン|turn|T)?\s*(\d{3,5})",
        r"(?:ターン|turn|T)\s*(\d{3,5})\s*まで",
    )
    for pat in patterns:
        m = re.search(pat, text, re.I)
        if not m:
            continue
        nums = [int(g) for g in m.groups() if g]
        if len(nums) >= 2:
            return min(nums), max(nums)
        if len(nums) == 1:
            return None, nums[0]
    return None, None


def _extract_turn_numbers(prompt: str) -> list[int]:
    """
    プロンプトからシミュ年代らしい数値を抽出。
    - 「ターンN」「TN」を最優先
    - フォールバックは 1000〜9999（「2000年間」など期間表現は除外）
    """
    text = prompt or ""
    nums: list[int] = []
    for m in re.finditer(r"(?:ターン|turn|T)\s*(\d{3,5})(?!\d)", text, re.I):
        nums.append(int(m.group(1)))
    if nums:
        return nums

    # 期間・史・刻みはターン扱いしない
    cleaned = re.sub(
        r"\d+\s*年\s*(?:間|史|刻み|世代|スパン)",
        " ",
        text,
    )
    for m in re.finditer(r"(?<![0-9])([1-9]\d{3,4})(?![0-9])", cleaned):
        nums.append(int(m.group(1)))
    return nums


def _infer_generation_span(prompt: str, start: int, end: int) -> int:
    """
    世代刻み（generationSpan）を推定。
    「3000年史」「2000年間」を span に誤認しない。
    明示がなければ長区間は 50 年刻みを既定とする。
    """
    text = prompt or ""
    total = max(1, end - start + 1)

    # 明示指定のみ採用（年史・年間は除外）
    explicit = re.search(
        r"(?:"
        r"(\d+)\s*年\s*(?:刻み|世代|スパン)"
        r"|世代\s*(?:スパン|幅|刻み)?\s*[:=]?\s*(\d+)"
        r"|generationSpan\s*=\s*(\d+)"
        r"|span\s*=\s*(\d+)"
        r")",
        text,
        re.I,
    )
    if explicit:
        for g in explicit.groups():
            if g:
                span = max(1, int(g))
                # 区間全体以上の span は一括化バグなので 50 に落とす
                if span >= total and total >= 100:
                    return 50
                return span

    # 「第21〜60世代」など複数世代言及 → 50年刻み
    if re.search(r"第\s*\d+\s*[〜~\-–—到至]+\s*\d+\s*世代", text):
        return 50
    if re.search(r"\d+\s*世代", text) and total >= 100:
        return 50

    # 長区間の連続進行はデフォルト 50
    if total >= 100:
        return 50
    return int(DEFAULT_JOB["generationSpan"])


def infer_job_from_prompt(prompt: str) -> dict[str, Any]:
    """自然言語指示から job を推定（Safe-Fail: 不明なら復興50年相当）。"""
    job = dict(DEFAULT_JOB)
    text = prompt or ""
    job["promptSummary"] = text[:120]

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

    # ターン範囲
    start_ex, end_ex = _extract_explicit_turn_range(text)
    turns = _extract_turn_numbers(text)
    if start_ex is not None:
        job["startTurn"] = start_ex
    if end_ex is not None:
        job["endTurn"] = end_ex
    if turns:
        if start_ex is None:
            job["startTurn"] = min(turns)
        if end_ex is None:
            # 明示 end が無いときだけ max(turns)。単一指定なら start のみ更新
            if len(turns) >= 2 or start_ex is not None:
                job["endTurn"] = max(turns) if end_ex is None else end_ex
                if start_ex is None:
                    job["startTurn"] = min(turns)
            elif len(turns) == 1 and start_ex is None:
                job["startTurn"] = turns[0]

    if job["endTurn"] < job["startTurn"]:
        job["startTurn"], job["endTurn"] = job["endTurn"], job["startTurn"]

    job["generationSpan"] = _infer_generation_span(
        text, int(job["startTurn"]), int(job["endTurn"])
    )

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
        job["generationSpan"] = min(int(job["generationSpan"]), 50)
    else:
        job["jobType"] = "magiChronicleLoop"
        # 「1050から1250」「第22〜25世代」→ 次世代開始を 1051 に正規化
        if job["startTurn"] == 1050 and job["endTurn"] >= 1250:
            job["startTurn"] = 1051
            job["endTurn"] = 1250
            job["generationSpan"] = 50
        # 一括化防止: span が区間全体以上なら 50 年刻みへ
        total = job["endTurn"] - job["startTurn"] + 1
        if total >= 100 and int(job["generationSpan"]) >= total:
            job["generationSpan"] = 50
        elif total >= 100 and int(job["generationSpan"]) < 10:
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

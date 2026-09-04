import os
import sys
import subprocess
import anthropic

# Windows コンソールの文字化け対策
if sys.platform == "win32":
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    sys.stderr.reconfigure(encoding="utf-8", errors="replace")

# 1. Load API Key from .env
env_path = os.path.join(os.path.dirname(__file__), ".env")
if os.path.exists(env_path):
    with open(env_path, "r", encoding="utf-8") as f:
        for line in f:
            if line.startswith("ANTHROPIC_API_KEY="):
                os.environ["ANTHROPIC_API_KEY"] = line.strip().split("=", 1)[1]

api_key = os.environ.get("ANTHROPIC_API_KEY")
if not api_key:
    print("Error: ANTHROPIC_API_KEY is missing in Tools/.env file.", file=sys.stderr)
    sys.exit(1)

client = anthropic.Anthropic(api_key=api_key)

# 2. System Instruction for MAGI-1: Verdandi
VERDANDI_SYSTEM_PROMPT = """
あなたは異世界歴史シミュレーターの合議システムにおける審議ユニット【MAGI-1：ヴェルダンディ】です。
提案された複数の歴史分岐（IF枝）の概要テキストを解析し、定性的な「人間ドラマ・ロマン・波乱度」を評価してください。

【評価規則】
- 評価スコアは -400 点から +400 点の範囲で算出すること。
- 人間同士の争い（王位継承、派閥抗争、内乱）、前線の過酷な防衛戦、禁忌・異端魔導の解禁を含む展開を高評価 (+100〜+400) すること。
- 平和すぎる展開や過去ログと変化のない展開にはマンネリデバフ (-200〜-400) を叩き込むこと。
- レスポンスは必ず以下の純粋な JSON フォーマットのみで返答すること (Markdown記法や解説テキストは禁止):

{
  "branchEvaluations": [
    {
      "branchId": "文字列",
      "verdandiScore": 数値 (-400〜400),
      "reasoning": "推薦または減点の短い日本語理由 (1〜2文)"
    }
  ],
  "topRecommendedBranchId": "最も推奨するbranchId"
}
"""


def auto_git_commit_eval():
    try:
        project_root = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
        print(f"📦 Git への自動コミットおよびプッシュを実行中... (対象: {project_root})")

        subprocess.run(["git", "add", "."], check=True, cwd=project_root)

        result = subprocess.run(
            ["git", "status", "--porcelain"],
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
            cwd=project_root,
        )

        if not result.stdout or not result.stdout.strip():
            print("ℹ️ 変更がないため Git コミットをスキップしました。")
            return

        subprocess.run(
            ["git", "commit", "-m", "auto: updated MAGI Verdandi evaluation result"],
            check=True,
            cwd=project_root,
        )
        push = subprocess.run(
            ["git", "push", "origin", "main"],
            cwd=project_root,
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
        )
        if push.returncode == 0:
            print("✅ MAGI 審議結果の Git プッシュが完了しました！")
        else:
            print("⚠️ Git push はスキップ/失敗しました（ローカルコミットは成功している可能性があります）。")
            if push.stderr:
                print(push.stderr.strip()[-500:], file=sys.stderr)
    except Exception as e:
        print(f"⚠️ Git 自動処理をスキップしました: {e}", file=sys.stderr)


def evaluate_branches(input_json_path):
    if not os.path.exists(input_json_path):
        print(f"Error: Target JSON file not found at {input_json_path}", file=sys.stderr)
        print(
            "Unity BatchMode (MagiSystemDecisionEngineMenu.BatchVerifyMagiAndQuit) 実行後に "
            "リポジトリ直下へ CurrentBranchCandidates.json が生成されます。",
            file=sys.stderr,
        )
        sys.exit(1)

    with open(input_json_path, "r", encoding="utf-8") as f:
        branch_data = f.read()

    print("Querying Claude 3.5 Sonnet for MAGI-1 (Verdandi) evaluation...")

    response = client.messages.create(
        model="claude-3-5-sonnet-20241022",
        max_tokens=1000,
        temperature=0.3,
        system=VERDANDI_SYSTEM_PROMPT,
        messages=[
            {
                "role": "user",
                "content": f"以下の歴史分岐リストを審議してください:\n{branch_data}",
            }
        ],
    )

    result_text = response.content[0].text.strip()

    output_path = os.path.join(os.path.dirname(__file__), "..", "VerdandiEvaluationResult.json")
    with open(output_path, "w", encoding="utf-8") as f:
        f.write(result_text)

    print(f"✅ MAGI-1 (Verdandi) Evaluation Complete. Output saved to: {output_path}")


if __name__ == "__main__":
    target_file = (
        sys.argv[1]
        if len(sys.argv) > 1
        else os.path.join(os.path.dirname(__file__), "..", "CurrentBranchCandidates.json")
    )
    evaluate_branches(target_file)
    auto_git_commit_eval()

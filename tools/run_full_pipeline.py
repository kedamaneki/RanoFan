import os
import sys
import subprocess


def run_step(description, command, cwd=None, log_file=None):
    print(f"\n🚀 [{description}] を実行中...")
    try:
        result = subprocess.run(
            command,
            check=True,
            cwd=cwd,
            text=True,
            capture_output=True,
            encoding="utf-8",
            errors="replace",
        )
        print(f"✅ [{description}] 完了")
        if result.stdout and result.stdout.strip():
            print(result.stdout.strip()[:500])
        return True
    except subprocess.CalledProcessError as e:
        print(f"❌ [{description}] でエラーが発生しました (exit={e.returncode}):")
        combined = ((e.stdout or "") + "\n" + (e.stderr or "")).strip()
        if "another Unity instance is running" in combined or "Multiple Unity instances" in combined:
            print(
                "⚠️ 原因: 同じプロジェクトを Unity エディタが開いたままです。\n"
                "   → Unity を完全に閉じてから再実行してください（Hub は開いたままでも可）。"
            )
        if e.stdout and e.stdout.strip():
            print("--- stdout ---")
            print(e.stdout.strip()[-2000:])
        if e.stderr and e.stderr.strip():
            print("--- stderr ---")
            print(e.stderr.strip()[-2000:])
        if log_file and os.path.isfile(log_file):
            print(f"--- log tail ({log_file}) ---")
            try:
                with open(log_file, "r", encoding="utf-8", errors="replace") as f:
                    lines = f.readlines()
                print("".join(lines[-40:]).rstrip())
            except OSError as read_err:
                print(f"(ログ読取失敗: {read_err})")
        if not (e.stdout or e.stderr or (log_file and os.path.isfile(log_file))):
            print("(詳細出力なし。コマンド・パス・依存ファイルを確認してください)")
        return False
    except FileNotFoundError as e:
        print(f"❌ [{description}] 実行ファイルが見つかりません: {e}")
        return False


def find_unity_editor_holding_project(unity_project):
    """同一プロジェクトを開いている Unity.exe のコマンドラインを列挙。"""
    holders = []
    norm = os.path.normcase(os.path.abspath(unity_project)).replace("/", "\\")
    try:
        probe = subprocess.run(
            [
                "powershell",
                "-NoProfile",
                "-Command",
                "Get-CimInstance Win32_Process -Filter \"Name='Unity.exe'\" | "
                "Select-Object -ExpandProperty CommandLine",
            ],
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
        )
        for line in (probe.stdout or "").splitlines():
            raw = line.strip()
            if not raw:
                continue
            if norm in os.path.normcase(raw).replace("/", "\\"):
                holders.append(raw)
    except Exception:
        pass

    lock_path = os.path.join(unity_project, "Temp", "UnityLockfile")
    lock_exists = os.path.isfile(lock_path)
    return holders, lock_exists


def main():
    project_root = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
    unity_project = os.path.join(project_root, "ラノベファンタジー")
    prompt = " ".join(sys.argv[1:]) if len(sys.argv) > 1 else "自動世代進行とMAGI審議の実行"

    if not os.path.isdir(os.path.join(unity_project, "ProjectSettings")):
        print(f"❌ Unity プロジェクトが見つかりません: {unity_project}")
        sys.exit(1)

    # Step 1: Gemini API で指示書(CursorInstruction.md)を自動生成
    success = run_step(
        "1/3 Gemini による設計・指示書自動生成",
        [sys.executable, "Tools/generate_instruction.py", prompt],
        cwd=project_root,
    )
    if not success:
        sys.exit(1)

    # Step 2 事前チェック: エディタ多重起動ロック
    holders, lock_exists = find_unity_editor_holding_project(unity_project)
    if holders or lock_exists:
        print("\n⛔ Step 2 を開始できません: Unity プロジェクトがロックされています。")
        print(f"   プロジェクト: {unity_project}")
        if holders:
            print(f"   検出: Unity.exe がこのプロジェクトを開いています（{len(holders)} 件）")
        if lock_exists:
            print("   検出: Temp/UnityLockfile が存在します")
        print(
            "\n対処:\n"
            "  1. Unity エディタ（ラノベファンタジー）を保存して閉じる\n"
            "  2. タスクマネージャで Unity.exe が残っていれば終了\n"
            "  3. このパイプラインを再実行\n"
            "（Unity Hub はそのままで問題ありません）"
        )
        sys.exit(1)

    # Step 2: Unity BatchMode
    unity_path = r"C:\Program Files\Unity\Hub\Editor\6000.5.0f1\Editor\Unity.exe"
    if not os.path.isfile(unity_path):
        hub_editor = r"C:\Program Files\Unity\Hub\Editor"
        if os.path.isdir(hub_editor):
            versions = sorted(os.listdir(hub_editor), reverse=True)
            for ver in versions:
                candidate = os.path.join(hub_editor, ver, "Editor", "Unity.exe")
                if os.path.isfile(candidate):
                    unity_path = candidate
                    print(f"ℹ️ Unity パスをフォールバック: {unity_path}")
                    break

    logs_dir = os.path.join(project_root, "Logs")
    os.makedirs(logs_dir, exist_ok=True)
    log_file = os.path.join(logs_dir, "pipeline_execution.log")

    unity_cmd = [
        unity_path,
        "-batchmode",
        "-nographics",
        "-projectPath", unity_project,
        # 現状: 文明復興50年（T1001-1050）。T1050-1250 連続バッチは未接続。
        "-executeMethod", "MagiSystemDecisionEngineMenu.BatchRunCivilizationRevival50YearsAndQuit",
        "-logFile", log_file,
        "-quit",
    ]

    success = run_step(
        "2/3 Unity サイレント実行 & MAGI自動審議",
        unity_cmd,
        cwd=project_root,
        log_file=log_file,
    )
    if not success:
        print("\n⛔ Step 2 失敗のためパイプラインを中断します。")
        sys.exit(1)

    # Step 3: Claude MAGI 評価 & Git 自動同期
    candidates = os.path.join(project_root, "CurrentBranchCandidates.json")
    if not os.path.isfile(candidates):
        print(
            f"❌ [3/3] 入力がありません: {candidates}\n"
            "   Unity 側が IF 枝 JSON を書き出すか、手動で配置してください。"
        )
        sys.exit(1)

    success = run_step(
        "3/3 Claude MAGI 評価 & Git 自動同期",
        [sys.executable, "Tools/eval_verdandi_claude.py", candidates],
        cwd=project_root,
    )
    if not success:
        print("\n⛔ Step 3 失敗のためパイプラインを中断します。")
        sys.exit(1)

    print("\n🎉 一連の全自動パイプライン処理がすべて完了しました！")


if __name__ == "__main__":
    main()

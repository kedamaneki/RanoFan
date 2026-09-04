import os
import sys
import subprocess
import time

def run_step(description, command, cwd=None):
    print(f"\n🚀 [{description}] を実行中...")
    try:
        result = subprocess.run(command, check=True, cwd=cwd, text=True, capture_output=True, encoding='utf-8', errors='replace')
        print(f"✅ [{description}] 完了")
        if result.stdout:
            print(result.stdout.strip()[:300] + "...") # ログの先頭部分を表示
        return True
    except subprocess.CalledProcessError as e:
        print(f"❌ [{description}] でエラーが発生しました:")
        print(e.stderr)
        return False

def main():
    project_root = os.path.abspath(os.path.join(os.path.dirname(__file__), '..'))
    prompt = " ".join(sys.argv[1:]) if len(sys.argv) > 1 else "自動世代進行とMAGI審議の実行"

    # Step 1: Gemini API で指示書(CursorInstruction.md)を自動生成
    success = run_step(
        "1/3 Gemini による設計・指示書自動生成",
        [sys.executable, "Tools/generate_instruction.py", prompt],
        cwd=project_root
    )
    if not success: return

    # Step 2: Unity BatchMode での自動ビルド・MAGI合議テストのバックグラウンド実行
    unity_path = r"C:\Program Files\Unity\Hub\Editor\6000.5.0f1\Editor\Unity.exe" # 環境に合わせて調整
    log_file = os.path.join(project_root, "Logs", "pipeline_execution.log")
    
    unity_cmd = [
        unity_path,
        "-batchmode",
        "-nographics",
        "-projectPath", project_root,
        "-executeMethod", "TimelineGenerationLoopEngineMenu.BatchVerifyGen4AndQuit",
        "-logFile", log_file,
        "-quit"
    ]
    
    run_step("2/3 Unity サイレント実行 & MAGI自動審議", unity_cmd, cwd=project_root)

    # Step 3: MAGI審議結果(Claude API)の実行と Git 自動同期
    run_step("3/3 Claude MAGI 評価 & Git 自動同期", [sys.executable, "Tools/eval_verdandi_claude.py"], cwd=project_root)

    print("\n🎉 一連の全自動パイプライン処理がすべて完了しました！")

if __name__ == "__main__":
    main()
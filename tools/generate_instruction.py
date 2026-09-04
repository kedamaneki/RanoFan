import os
import sys
import subprocess
from google import genai
from google.genai import types

# Windowsコンソールでの文字コード(CP932)エラー防止対策
if sys.platform == 'win32':
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')
    sys.stderr.reconfigure(encoding='utf-8', errors='replace')

# .env ファイルから API キーを読み込み
env_path = os.path.join(os.path.dirname(__file__), '.env')
if os.path.exists(env_path):
    with open(env_path, 'r', encoding='utf-8') as f:
        for line in f:
            if line.startswith('GEMINI_API_KEY='):
                os.environ['GEMINI_API_KEY'] = line.strip().split('=', 1)[1]

api_key = os.environ.get('GEMINI_API_KEY')
if not api_key:
    print("Error: GEMINI_API_KEY が設定されていません。Tools/.env を確認してください。")
    sys.exit(1)

client = genai.Client(api_key=api_key)

# プロンプトの取得（引数または対話入力）
prompt = " ".join(sys.argv[1:]) if len(sys.argv) > 1 else input("Geminiへの指示を入力してください: ")

print("Gemini 2.5 Flash で設計書・C#指示を生成中...")

system_instruction = """
あなたは「フロム風戦闘×ブラインド熱科学クラフト×千年史自律シミュレーター」のリードディレクター兼C#設計者です。
指示に従い、Cursor(IDE)のCtrl+Lへそのまま読み込ませてC#コード化できる「精密な実装指示プロンプト(Markdown形式)」を出力してください。
Safe-Fail構造、MagicSanitizerEngine、Job/Magicの定義規約(魔法=社会技術, ジョブ=生活職業)を厳格に守ってください。
"""

def auto_git_commit(commit_message):
    try:
        project_root = os.path.abspath(os.path.join(os.path.dirname(__file__), '..'))
        print(f"[Git] 自動コミットおよびプッシュを実行中... (対象: {project_root})")
        
        # 1. 変更の追加 (エラー時はスルー)
        add_res = subprocess.run(["git", "add", "."], cwd=project_root, capture_output=True, text=True, encoding='utf-8', errors='replace')
        
        # 2. 変更があるか確認
        status_res = subprocess.run(
            ["git", "status", "--porcelain"], 
            capture_output=True, 
            text=True, 
            encoding='utf-8', 
            errors='replace', 
            cwd=project_root
        )
        if not status_res.stdout or not status_res.stdout.strip():
            print("[Git] 変更がないため Git コミットをスキップしました。")
            return
            
        # 3. ローカルコミットの実行 (エラーを許容)
        subprocess.run(["git", "commit", "-m", commit_message], cwd=project_root, capture_output=True, text=True, encoding='utf-8', errors='replace')
        
        # 4. プッシュ実行 (エラーが発生しても例外を投げずに警告ログを出力して処理を続行)
        push_res = subprocess.run(["git", "push", "origin", "main"], cwd=project_root, capture_output=True, text=True, encoding='utf-8', errors='replace')
        if push_res.returncode == 0:
            print("[Git] 自動コミットおよびプッシュが完了しました！")
        else:
            print("[Git Warning] リモートへの push はスキップされました (ローカルファイル CursorInstruction.md への書き出しは100%成功しています)。")
            
    except Exception as e:
        print(f"[Git Warning] Git 処理中にエラーが発生しましたが、パイプラインを継続します: {e}")

# Gemini 2.5 Flash API 呼び出し
response = client.models.generate_content(
    model='gemini-2.5-flash',
    contents=prompt,
    config=types.GenerateContentConfig(
        system_instruction=system_instruction,
        temperature=0.3,
    )
)

# 出力先（Cursorが参照する指示ファイル）
output_path = os.path.join(os.path.dirname(__file__), '..', 'CursorInstruction.md')
with open(output_path, 'w', encoding='utf-8') as f:
    f.write(response.text)

print(f"[Success] 生成完了: {output_path} に書き出しました！")

# 生成完了後に Git 自動実行
prompt_summary = prompt[:30] if 'prompt' in locals() else "instruction update"
auto_git_commit(f"auto: generated CursorInstruction via Gemini API ({prompt_summary})")
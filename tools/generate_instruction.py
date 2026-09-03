import os
import sys
import subprocess
from google import genai
from google.genai import types

# .env ファイルから API キーを簡易読み込み
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

print("Gemini 2.5 flash で設計書・C#指示を生成中...")

system_instruction = """
あなたは「フロム風戦闘×ブラインド熱科学クラフト×千年史自律シミュレーター」のリードディレクター兼C#設計者です。
指示に従い、Cursor(IDE)のCtrl+Lへそのまま読み込ませてC#コード化できる「精密な実装指示プロンプト(Markdown形式)」を出力してください。
Safe-Fail構造、MagicSanitizerEngine、Job/Magicの定義規約(魔法=社会技術, ジョブ=生活職業)を厳格に守ってください。
"""

def auto_git_commit(commit_message):
    try:
        # プロジェクトルートの絶対パスを取得
        project_root = os.path.abspath(os.path.join(os.path.dirname(__file__), '..'))
        print(f"📦 Git への自動コミットおよびプッシュを実行中... (対象: {project_root})")
        
        # 1. 変更の追加
        subprocess.run(["git", "add", "."], check=True, cwd=project_root)
        
        # 2. 変更があるか確認（UTF-8指定・デコードエラー自動置換で保護）
        result = subprocess.run(
            ["git", "status", "--porcelain"], 
            capture_output=True, 
            text=True, 
            encoding='utf-8', 
            errors='replace', 
            cwd=project_root
        )
        
        if not result.stdout or not result.stdout.strip():
            print("ℹ️ 変更がないため Git コミットをスキップしました。")
            return
            
        # 3. コミット＆プッシュ実行
        subprocess.run(["git", "commit", "-m", commit_message], check=True, cwd=project_root)
        subprocess.run(["git", "push", "origin", "main"], check=True, cwd=project_root)
        print("✅ Git への自動コミットおよびプッシュが完了しました！")
    except Exception as e:
        print(f"⚠️ Git 自動処理中にエラーが発生しました: {e}")

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

print(f"✅ 生成完了: {output_path} に書き出しました！")

# 🎯 生成完了後に Git 自動実行
prompt_summary = prompt[:30] if 'prompt' in locals() else "instruction update"
auto_git_commit(f"auto: generated CursorInstruction via Gemini API ({prompt_summary})")
# 千年史サバイバルログ — ローカルLLM日常描写量産システム

架空世界1000年史シミュレーション（`macro_chronicle_geo.json`）を読み込み、各国家・各ターンの「一般市民の日常・社会・風俗」をローカルLLM（Ollama + Qwen 2.5 等）で自動執筆し、Markdown としてモジュール管理する Python システムです。

## ディレクトリ構成

```text
千年史サバイバルログ/
├── README.md
├── requirements.txt
├── src/
│   ├── main.py              # CLI エントリポイント
│   ├── data_loader.py       # geo JSON 読込・時間/周期計算
│   ├── prompt_engine.py     # 動的プロンプト組み立て
│   ├── ollama_client.py     # Ollama API 通信
│   ├── constitution.py      # 世界観憲法（プロンプト注入）
│   └── paths.py             # 出力パス管理
└── ミクロ生活史/
    ├── 国家001/
    │   ├── 国家001_ターン001_叙事年0001_生活史.md
    │   └── ...
    └── 国家112/
```

## 前提条件

- Python 3.10 以上
- [Ollama](https://ollama.com/) がインストール済み
- シミュレーションデータ: `../macro_chronicle/macro_chronicle_geo.json`（`--geo` で変更可）

## セットアップ

### 1. Ollama のインストールと起動

```bash
# モデル取得（例: Qwen 2.5 14B）
ollama pull qwen2.5:14b

# サーバー起動（別ターミナルで常時実行）
ollama serve
```

軽量モデルを使う場合:

```bash
ollama pull qwen2.5:7b
```

### 2. Python 依存関係

```bash
cd 千年史サバイバルログ
pip install -r requirements.txt
```

## 実行方法

`src/` ディレクトリで `main.py` を実行します。

```bash
cd src
```

### 接続確認

```bash
python main.py --check-ollama
```

### 全件生成（既存ファイルはスキップ）

```bash
python main.py
```

データ規模が大きいため、初回は `--country` と `--end-turn` で範囲を絞ることを推奨します。

```bash
# 国家001〜010、ターン1〜100 のみ
python main.py --countries 1,2,3,4,5,6,7,8,9,10 --end-turn 100
```

### 単一国家・単一ターンのピンポイント再執筆

矛盾を発見した場合、システム全体を再実行せず上書きできます。

```bash
python main.py --country 112 --turn 46 --force \
  --custom_instruction "前回はのんきすぎました。次のターンで滅亡することが確定しているため、都市全体に絶望感が漂い、インフラが崩壊していく様子を強調して再執筆してください。"
```

### ドライラン（生成対象の確認のみ）

```bash
python main.py --country 1 --start-turn 1 --end-turn 5 --dry-run
```

## 主要 CLI オプション

| オプション | 説明 |
|-----------|------|
| `--geo PATH` | geo JSON パス |
| `--country / --countries` | 対象国家（例: `112` または `1,2,3`） |
| `--start-turn` / `--end-turn` | ターン範囲 |
| `--turn` | 単一ターン（範囲指定より優先） |
| `--force` | 既存 Markdown を上書き |
| `--custom_instruction` | 再執筆用の追加指示 |
| `--include-dead` | 滅亡後ターンも含める |
| `--model` | Ollama モデル名（既定: `qwen2.5:14b`） |
| `--ollama-url` | API URL（既定: `http://localhost:11434`） |
| `--limit N` | 処理件数上限（テスト用） |
| `--dry-run` | LLM を呼ばず対象のみ表示 |

## データ連動ルール

システムは geo JSON と以下の式で 100% 連動します。

- **叙事年** = `turn × meta.years_per_turn`
- **周期位相** = `((turn - 1) % 24) + 1`
  - 位相 1〜12 → `active`（活性期）
  - 位相 13〜24 → `dormant`（休眠期）

各ターンの `territory` / `economy` / `military` / `magic` / `barrier_efficiency` / `cycle_phase` と、当ターンの `logs` が動的プロンプトに自動反映されます。

## 世界観憲法

`src/constitution.py` に執筆 AI 向けの絶対前提が定義されています。

- 架空国家のみ（現実の国名・史実禁止）
- 魔力結晶・防衛障壁・結界依存の中世〜近世ファンタジー
- 環境一体型異形（魔獣）が最大の脅威（人型亜種なし）
- 国家番号の欠番は詰めない
- 24ターン周期の活性期/休眠期

矛盾を発見した場合は `--force --custom_instruction` で該当ファイルのみ再生成してください。生成ファイル先頭の HTML コメントに `nation` / `turn` / `cycle` メタデータが付与されます。

## トラブルシューティング

| 症状 | 対処 |
|------|------|
| `Ollama に接続できません` | `ollama serve` を起動 |
| タイムアウト | `--timeout 900` やより小さい `--model qwen2.5:7b` |
| メモリ不足 | 7B モデル、または `--limit 1` で試行 |
| geo JSON が見つからない | `--geo` で正しいパスを指定 |

## ライセンス

プロジェクト内データ・コードに従います。

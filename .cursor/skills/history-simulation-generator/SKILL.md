---
name: history-simulation-generator
description: >-
  Generates Unity JsonUtility-compatible history simulation event JSON from
  world_analytics.json and 歴史棚 narrative files. Use when the user asks to
  mass-produce history flags, simulation events, macro/micro lore, HIST_/ALT_/MMO_
  flags, or mentions world_analytics.json or 歴史棚.
---

# 歴史シミュレーション JSON 量産

## 必須入力（リポジトリ内）

| ファイル | パス（リポジトリルート基準） |
|---------|---------------------------|
| マクロ分析ログ | `world_analytics.json` |
| 歴史ナラティブ | `歴史棚/歴史書その*.md`, `歴史棚/歴史書その*.txt` |

## 作業開始手順

1. **コンテキスト抽出**（推奨・最初に実行）:

```bash
python ラノベファンタジー/Tools/history_sim_context_builder.py --nation 71 --macro-turn 5
python ラノベファンタジー/Tools/history_sim_context_builder.py --nation 72 --macro-turn 46 --collapse
```

2. 出力された `context` と `歴史棚` の該当段落を読む
3. [schema-reference.md](schema-reference.md) に従い `simulationEvents` を生成
4. 出力先: `ラノベファンタジー/Assets/Resources/HistorySimulation/`（未存在なら作成）

## ターン対応（重要・10倍スケール）

| 概念 | 意味 |
|------|------|
| **analytics 暦**（`world_analytics.json` の `year`） | 実シミュレーションの **100年分**（1000〜1099） |
| **叙事年**（歴史書の「〇〇年目」） | analytics を **10倍に引き伸ばした1000年史** |
| **macroTurn**（出力スキーマ） | 歴史書の「ターン」。**1ターン = 10叙事年 = 1 analytics暦年** |
| **analytics turn** | 月次。**12 turn = 1暦年 = 1 macroTurn** |

### 変換式

```
叙事年（区間終端）  = macroTurn × 10          例: ターン46 → 460年目
叙事年（区間）      = (macroTurn-1)×10+1 〜 macroTurn×10
analytics.year      = 1000 + macroTurn - 1    例: ターン46 → year 1045
analytics turn      = (macroTurn-1)×12+1 〜 macroTurn×12
```

`world_analytics.json` の **100暦年は叙事1000年・macroTurn 1〜100 全体** に対応する。  
国家個別イベントは analytics に無いため、**叙事の解釈は歴史棚テキストが主**、数値トレンドは analytics と突合する。

## 絶対規約（要約）

詳細は [schema-reference.md](schema-reference.md)。

- UI文字列（`logMessageTemplate`, `npcDialogue`, `alternativeLore`）に **国家番号・NATION_xxx・区域A** を出さない → 地理・工学的呼称へ言い換え
- `isHistoricalCollapseRoute: true` → **`decayTimeline` のみ**（`prosperityTimeline` キー禁止）
- `isHistoricalCollapseRoute: false` → **`prosperityTimeline` のみ**
- フラグ: `HIST_` / `ALT_` / `MMO_` 命名規則厳守
- `evolutionSystemType`: macroTurn 1-40=Ritual, 41-60=Inspiration, 61-100=Recipe
- 敵は魔獣・異形のみ（人型亜種禁止。ティア10擬態のみ例外）
- 設定上のエネルギーは **魔力** と表記

## 国家ID → 表示用呼称（内部IDは targetNationId のみ）

歴史棚の「領域・X」を UI 向けに変換する例:

| 領域 | 表示用呼称例 |
|------|------------|
| 領域・西 | 【最西端の統合王国】周辺 |
| 領域・東 | 【東方の魔導生産大国】 |
| 領域・中央 | 【中央海平原の結節都市】 |
| 領域・南 | 【険峻な山岳要塞帯】 |
| 領域・北 | 【極寒針葉樹林の前線】 |

国家ごとの固有技術名（「激流回帰障壁」「焦土エネルギー戦術」等）は **歴史棚の原文** から抽出して `microLore` / タイムラインに反映。

## 量産バッチ例

```bash
# 国家071〜075の索引確認
python ラノベファンタジー/Tools/history_sim_context_builder.py --list-nations

# バッチ用コンテキスト（-o でファイル保存推奨・日本語はファイル出力が安全）
python ラノベファンタジー/Tools/history_sim_context_builder.py --nation 71 --macro-turn 1 --macro-turn-end 10 -o context_T71.json
```

## 追加リソース

- JSON Schema 全文・チェックリスト: [schema-reference.md](schema-reference.md)
- データソース詳細・マッピング: [data-sources.md](data-sources.md)
- 新チャット用依頼文テンプレ: [new-chat-prompt.md](new-chat-prompt.md)
- **全131カ国バッチ依頼文**: [batch-prompts-all.md](batch-prompts-all.md)
- **1年目〜1000年目・全30ファイルの進行手順**: [full-production-roadmap.md](full-production-roadmap.md)
- **深掘り版（チャット無制限・約241回）**: [deep-production-roadmap.md](deep-production-roadmap.md)

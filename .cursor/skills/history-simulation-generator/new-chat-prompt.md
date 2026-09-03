# 新チャット用 — コピペ依頼テンプレート

以下を **そのまま新しいチャットの最初のメッセージ** に貼り付けてください。  
`@` でファイルを添付すると精度が上がります。

---

## ★ おすすめ：汎用依頼文（まずこれを使う）

```
@.cursor/skills/history-simulation-generator/SKILL.md
@.cursor/skills/history-simulation-generator/schema-reference.md
@.cursor/skills/history-simulation-generator/data-sources.md
@world_analytics.json
@歴史棚

【依頼】history-simulation-generator スキルに従い、歴史シミュレーション JSON を生成してください。

## 時間スケールの前提（必読・矛盾なく扱うこと）

- `world_analytics.json` は **analytics 100暦年**（year 1000〜1099、turn 1〜1200・月次）
- `歴史棚` の歴史書は **叙事 1000年**（歴史書は analytics を **10倍に引き伸ばした** ナラティブ）
- **macroTurn**（歴史書の「ターン」）が両者の共通インデックス:
  - 1 macroTurn = 10叙事年 = 1 analytics暦年 = 12 analytics turn
  - 叙事年（区間終端）= macroTurn × 10（例: ターン46 → 460年目）
  - analytics.year = 1000 + macroTurn - 1（例: ターン46 → year 1045）
- 出力 JSON の `timestamp.year` は **叙事年** を使う（analytics の year をそのまま入れない）

## データの役割分担

- **世界指標**（生存国数・脅威度・総国力など）→ `world_analytics.json` と突合
- **国家固有の出来事・技術名・滅亡経緯** → `歴史棚` テキストが主ソース
- analytics に国家別イベントは無い

## 今回の生成条件（★ここを書き換える）

- 対象国: NATION_072
- macroTurn: 46（叙事年 451〜460、analytics year 1045、turn 541〜552）
- ルート: 有事（isHistoricalCollapseRoute: true → decayTimeline のみ）
- eventType: NationCollapse
- 件数: 1イベント

## 作業手順

1. `python ラノベファンタジー/Tools/history_sim_context_builder.py --nation 72 --macro-turn 46 --collapse -o context.json` を実行
2. context.json の `turnMapping` / `worldAnalytics` / `nation.bodyText` を読む
3. schema-reference.md の排他ルール・フラグ命名に従い `simulationEvents` を組み立てる
4. UI文字列（logMessageTemplate / npcDialogue / alternativeLore）に国家番号・NATION_xxx を出さない

## 出力

- ファイル: ラノベファンタジー/Assets/Resources/HistorySimulation/SIM_EVT_NATION_072_T46.json
- ルートは `{ "simulationEvents": [ ... ] }` でラップ
```

---

## テンプレート A: 単一国・平時（生存国）

```
@.cursor/skills/history-simulation-generator/SKILL.md
@schema-reference.md
@world_analytics.json
@歴史棚

【依頼】歴史シミュレーション JSON を1件生成してください。

■ 時間スケール: analytics 100年 → 叙事1000年（10倍）。1 macroTurn = 10叙事年 = 1 analytics暦年。
■ 対象: NATION_071 / macroTurn 5（叙事41〜50年目、analytics year 1004）
■ ルート: 平時（isHistoricalCollapseRoute: false → prosperityTimeline のみ）
■ timestamp.year は叙事年（41〜50のいずれか）

手順:
1. python ラノベファンタジー/Tools/history_sim_context_builder.py --nation 71 --macro-turn 5 -o context.json
2. 歴史棚の国家071本文と analytics 世界指標を突合
3. simulationEvents JSON を出力（UIに国家番号禁止）

出力先: ラノベファンタジー/Assets/Resources/HistorySimulation/SIM_EVT_NATION_071_T05.json
```

---

## テンプレート B: 滅亡イベント（有事ルート）

```
@.cursor/skills/history-simulation-generator/SKILL.md
@schema-reference.md
@data-sources.md
@world_analytics.json
@歴史棚/歴史書その19.txt

【依頼】国家072の滅亡イベント JSON を生成してください。

■ 10倍スケール前提:
  - 歴史書「ターン46 / 460年目」= macroTurn 46
  - analytics: year 1045, turn 541〜552
  - 出力 timestamp.year = 460（叙事年）

■ 条件:
  - targetNationId: NATION_072（内部IDのみ。UI文字列では地理呼称へ変換）
  - isHistoricalCollapseRoute: true
  - decayTimeline の3フェーズすべて充填（prosperityTimeline キー禁止）
  - eventType: NationCollapse
  - evolutionSystemType: Inspiration（macroTurn 41〜60）

手順:
1. python ラノベファンタジー/Tools/history_sim_context_builder.py --nation 72 --macro-turn 46 --collapse -o context.json
2. 歴史棚の「焦土」「大国001の強制供出」等を microLore に反映
3. forcedFlags: HIST_[叙事年]_[月]_[日]_NATION_COLLAPSE 形式

出力先: ラノベファンタジー/Assets/Resources/HistorySimulation/SIM_EVT_NATION_072_COLLAPSE_T46.json
```

---

## テンプレート C: 複数国バッチ

```
@.cursor/skills/history-simulation-generator/SKILL.md
@schema-reference.md
@world_analytics.json
@歴史棚

【依頼】歴史シミュレーション JSON をバッチ生成してください。

■ 10倍スケール: macroTurn T → 叙事 (T-1)×10+1〜T×10年目 / analytics year 1000+T-1

■ 対象（各1イベント）:
  - NATION_071: macroTurn 5（生存・平時）
  - NATION_072: macroTurn 46（滅亡・有事）
  - NATION_073: macroTurn 5（生存・平時）
  - NATION_074: macroTurn 5（生存・平時）
  - NATION_075: macroTurn 5（生存・平時）

■ ルール:
  - 滅亡国 → decayTimeline のみ
  - 生存国 → prosperityTimeline のみ
  - timestamp.year は叙事年
  - UI文字列に国家番号禁止

手順:
1. python ラノベファンタジー/Tools/history_sim_context_builder.py --list-nations
2. 各国について context_builder でコンテキスト抽出
3. 1ファイルに simulationEvents 配列でまとめて出力

出力先: ラノベファンタジー/Assets/Resources/HistorySimulation/SIM_BATCH_NATION_071_075.json
```

---

## テンプレート D: 章単位（歴史書 MD）

```
@.cursor/skills/history-simulation-generator/SKILL.md
@schema-reference.md
@歴史棚/歴史書その1.md
@world_analytics.json

【依頼】第1章（macroTurn 1〜20）の主要イベントを simulationEvents 10件で生成してください。

■ 時間対応（10倍スケール）:
  - macroTurn 1〜20 = 叙事 1〜200年目 = analytics year 1000〜1019
  - 世界指標は world_analytics と突合、国家固有叙事は歴史書その1.md が主

■ 含めたい題材:
  - 国家002の障壁戦術
  - 国家001の統合
  - 初期脅威度上昇（macroTurn 1 付近 analytics 脅威 ≈ 1.232）

■ 規約: 排他タイムライン / フラグ命名 / UI国家番号禁止 / 敵は魔獣・異形のみ

出力先: ラノベファンタジー/Assets/Resources/HistorySimulation/SIM_CHAPTER01_EVENTS.json
```

---

## 添付ファイル早見表

| 添付 | 用途 |
|------|------|
| `@.cursor/skills/history-simulation-generator/SKILL.md` | 作業手順・10倍スケール規約（必須） |
| `@schema-reference.md` | JSON Schema・チェックリスト |
| `@data-sources.md` | ターン変換式の詳細 |
| `@world_analytics.json` | 世界全体の turn/脅威度/生存国数（100暦年） |
| `@歴史棚` または個別 txt/md | 国家別ナラティブ（1000年史） |

## 変換早見（コピペ用）

| macroTurn | 叙事年（終端） | analytics year | analytics turn |
|-----------|--------------|----------------|----------------|
| 1 | 10年目 | 1000 | 1〜12 |
| 5 | 50年目 | 1004 | 49〜60 |
| 13 | 130年目 | 1012 | 145〜156 |
| 46 | 460年目 | 1045 | 541〜552 |
| 100 | 1000年目 | 1099 | 1189〜1200 |

## Cursor で Skill を自動適用させるキーワード

- 「歴史シミュレーション JSON を量産」
- 「world_analytics と 歴史棚 から HIST_ フラグを生成」
- 「history-simulation-generator スキルに従って」
- 「10倍スケールの叙事年で timestamp を書いて」

## 全131カ国バッチ依頼文

5国ずつ **全25ファイル分** のコピペ依頼文は [batch-prompts-all.md](batch-prompts-all.md) を参照。

## 1年目〜1000年目・全歴史書を通す場合

| 目的 | ドキュメント | チャット数 |
|------|-------------|-----------|
| 標準（全体を通す） | [full-production-roadmap.md](full-production-roadmap.md) | 約30 |
| **深掘り（歴史を厚く）** | [deep-production-roadmap.md](deep-production-roadmap.md) | **約241〜282** |

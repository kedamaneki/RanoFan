# 全史量産ロードマップ（1年目〜1000年目・全歴史書）

叙事 **1年目（macroTurn 1）から 1000年目（macroTurn 100）** まで、歴史棚 **全30ファイル** を使って simulationEvents を量産する手順書です。

---

## 全体像

| フェーズ | ファイル | チャット数 | 内容 | 目安イベント数 |
|---------|---------|-----------|------|--------------|
| **A: 世界史（章）** | 歴史書その1〜5.md | **5** | 大国001/002等の世界規模イベント | 各章 8〜15件 → 計40〜75件 |
| **B: 国家史（個別）** | 歴史書その6〜30.txt | **25** | 131カ国それぞれの代表イベント | 131件（+α） |
| **合計** | 全30ファイル | **30チャット** | | 約170〜200件 |

```
叙事年 1 ───────────────────────────────────────────── 1000
         │第1章│第2章│第3章│第4章│第5章│
         T1-20 T21-40 T41-60 T61-80 T81-100
              ↑ フェーズA（その1-5.md）
         
         国家001〜131（その6-30.txt、5〜6国ずつ）
              ↑ フェーズB（batch-prompts-all.md）
```

**推奨順序**: **フェーズA（第1章→第5章）→ フェーズB（その6→その30）**  
世界の流れを先に固めてから、各国イベントを生成すると矛盾が少ないです。

---

## 出力フォルダ構成（先に作っておく）

```
ラノベファンタジー/Assets/Resources/HistorySimulation/
├── chapters/
│   ├── SIM_CHAPTER01_T01-20.json
│   ├── SIM_CHAPTER02_T21-40.json
│   ├── SIM_CHAPTER03_T41-60.json
│   ├── SIM_CHAPTER04_T61-80.json
│   └── SIM_CHAPTER05_T81-100.json
└── nations/
    ├── batch06/   … NATION_001〜005
    ├── batch07/   … NATION_006〜010
    …
    └── batch30/   … NATION_126〜131
```

---

## ★ マスター依頼文（全工程の最初の1チャット用）

全30ファイル・千年史を通しで進めるとき、**最初に1回だけ** 以下を送り、方針を固定してください。

```
@.cursor/skills/history-simulation-generator/SKILL.md
@.cursor/skills/history-simulation-generator/schema-reference.md
@.cursor/skills/history-simulation-generator/data-sources.md
@.cursor/skills/history-simulation-generator/full-production-roadmap.md
@world_analytics.json
@歴史棚

【依頼】ラノベファンタジー千年史の simulationEvents を、1年目（叙事）から1000年目まで全歴史書分つくります。

## 時間スケール（全フェーズ共通・厳守）
- analytics 100暦年（year 1000〜1099）→ 叙事1000年（10倍引き伸ばし）
- 1 macroTurn = 10叙事年 = 1 analytics暦年 = 12 analytics turn
- 叙事年（終端）= macroTurn × 10
- analytics.year = 1000 + macroTurn - 1
- 出力 timestamp.year は叙事年のみ（analytics year を入れない）

## 進行計画（この順で実施）
1. フェーズA: 歴史書その1.md〜その5.md（第1章〜第5章、macroTurn 1〜100 の世界史）
2. フェーズB: 歴史書その6.txt〜その30.txt（131カ国、batch-prompts-all.md 参照）

## 今回のスコープ
フェーズA・第1章のみ（macroTurn 1〜20、叙事 1〜200年目）の simulationEvents を 10件生成してください。
次のチャット以降は章・バッチを順番に進めます。

## 規約
- 滅亡/有事 → decayTimeline のみ / 平時 → prosperityTimeline のみ
- UI文字列に国家番号・NATION_xxx 禁止（大国は「最西端の統合王国」「東方の魔導大国」等）
- world_analytics と macroTurn 帯を突合して脅威度・総国力・生存国数を反映
- 存在しない GameMaster ID は捏造しない

出力: ラノベファンタジー/Assets/Resources/HistorySimulation/chapters/SIM_CHAPTER01_T01-20.json
```

---

## フェーズA — 章別依頼文（5チャット）

### A-1 第1章（その1.md）macroTurn 1〜20 / 叙事 1〜200年目

```
@.cursor/skills/history-simulation-generator/SKILL.md
@schema-reference.md
@world_analytics.json
@歴史棚/歴史書その1.md

【依頼】第1章（macroTurn 1〜20、叙事1〜200年目）の世界史 simulationEvents を10件生成。

■ 10倍スケール厳守 / timestamp.year は叙事年
■ 題材例: 脅威度1.232の初期大崩壊、国家002障壁防衛、国家001統合の兆し、総国力減少
■ macroTurn 1〜20 は analytics year 1000〜1019 と突合可能
■ 平時・有事は内容に応じて選択。UI国家番号禁止

出力: HistorySimulation/chapters/SIM_CHAPTER01_T01-20.json
```

### A-2 第2章（その2.md）macroTurn 21〜40 / 叙事 201〜400年目

```
@.cursor/skills/history-simulation-generator/SKILL.md
@schema-reference.md
@world_analytics.json
@歴史棚/歴史書その2.md

【依頼】第2章（macroTurn 21〜40、叙事201〜400年目）の世界史 simulationEvents を10件生成。

■ analytics year 1020〜1039 と突合
■ 題材例: 百国時代の凋落、鉄血同盟、焦土エネルギー戦術の萌芽
■ 10倍スケール / UI国家番号禁止

出力: HistorySimulation/chapters/SIM_CHAPTER02_T21-40.json
```

### A-3 第3章（その3.md）macroTurn 41〜60 / 叙事 401〜600年目

```
@.cursor/skills/history-simulation-generator/SKILL.md
@schema-reference.md
@world_analytics.json
@歴史棚/歴史書その3.md

【依頼】第3章（macroTurn 41〜60、叙事401〜600年目）の世界史 simulationEvents を10件生成。

■ analytics year 1040〜1059 と突合
■ 題材例: 最底の世紀、国家001内周収縮、国家002深層魔導都市、生存国88→80
■ evolutionSystemType: Inspiration（macroTurn 41〜60）
■ 10倍スケール / UI国家番号禁止

出力: HistorySimulation/chapters/SIM_CHAPTER03_T41-60.json
```

### A-4 第4章（その4.md）macroTurn 61〜80 / 叙事 601〜800年目

```
@.cursor/skills/history-simulation-generator/SKILL.md
@schema-reference.md
@world_analytics.json
@歴史棚/歴史書その4.md

【依頼】第4章（macroTurn 61〜80、叙事601〜800年目）の世界史 simulationEvents を10件生成。

■ analytics year 1060〜1079 と突合
■ 題材例: 低脅威周期突入、大開拓、魔導工学誕生、統合王国完成（叙事800年目）
■ evolutionSystemType: Recipe（macroTurn 61〜100）
■ 10倍スケール / UI国家番号禁止

出力: HistorySimulation/chapters/SIM_CHAPTER04_T61-80.json
```

### A-5 第5章（その5.md）macroTurn 81〜100 / 叙事 801〜1000年目

```
@.cursor/skills/history-simulation-generator/SKILL.md
@schema-reference.md
@world_analytics.json
@歴史棚/歴史書その5.md

【依頼】第5章（macroTurn 81〜100、叙事801〜1000年目）の世界史 simulationEvents を10件生成。

■ analytics year 1080〜1099 と突合
■ 題材例: 大遠征、脅威完全駆逐、双極秩序（統合王国×魔導大国）、最終データ（生存68国・総国力226151）
■ macroTurn 100 = 叙事1000年目 = シミュレーション停止点
■ 10倍スケール / UI国家番号禁止

出力: HistorySimulation/chapters/SIM_CHAPTER05_T81-100.json
```

---

## フェーズB — 国家別（25チャット）

**その6.txt からその30.txt まで** は、既存の一覧をそのまま使います。

→ **[batch-prompts-all.md](batch-prompts-all.md)** のバッチ01〜25を、**フェーズA完了後**に順番に実行。

出力先を `HistorySimulation/nations/batch06/` 〜 `batch30/` に変更する場合は、各バッチ文の出力先を書き換えてください。

---

## 全工程チェックリスト（30チャット）

### フェーズA — 世界史（叙事 1〜1000年を章で網羅）

- [ ] A-1 その1.md  第1章  T01-20  （1〜200年目）
- [ ] A-2 その2.md  第2章  T21-40  （201〜400年目）
- [ ] A-3 その3.md  第3章  T41-60  （401〜600年目）
- [ ] A-4 その4.md  第4章  T61-80  （601〜800年目）
- [ ] A-5 その5.md  第5章  T81-100 （801〜1000年目）

### フェーズB — 国家史（131カ国）

- [ ] B-01 その6.txt   NATION_001〜005
- [ ] B-02 その7.txt   NATION_006〜010
- [ ] B-03 その8.txt   NATION_011〜015
- [ ] B-04 その9.txt   NATION_016〜020
- [ ] B-05 その10.txt  NATION_021〜025
- [ ] B-06 その11.txt  NATION_026〜030
- [ ] B-07 その12.txt  NATION_031〜035
- [ ] B-08 その13.txt  NATION_036〜040
- [ ] B-09 その14.txt  NATION_046〜050
- [ ] B-10 その15.txt  NATION_051〜055
- [ ] B-11 その16.txt  NATION_056〜060
- [ ] B-12 その17.txt  NATION_061〜065
- [ ] B-13 その18.txt  NATION_066〜070
- [ ] B-14 その19.txt  NATION_071〜075
- [ ] B-15 その20.txt  NATION_076〜080
- [ ] B-16 その21.txt  NATION_081〜085
- [ ] B-17 その22.txt  NATION_086〜090
- [ ] B-18 その23.txt  NATION_091〜095
- [ ] B-19 その24.txt  NATION_096〜100
- [ ] B-20 その25.txt  NATION_101〜105
- [ ] B-21 その26.txt  NATION_106〜110
- [ ] B-22 その27.txt  NATION_111〜115
- [ ] B-23 その28.txt  NATION_116〜120
- [ ] B-24 その29.txt  NATION_121〜125
- [ ] B-25 その30.txt  NATION_126〜131

---

## 深さのレベル（どこまでやるか）

| レベル | 内容 | チャット数 | 向いている人 |
|--------|------|-----------|-------------|
| **L1 標準** | 章10件×5 + 国1件×131 | 30 | まず全体を通す |
| **L2 深掘り（推奨）** | ターン100 + 国深掘り131 + 大国10 | **約241** | 歴史を厚くしたい |
| **L3 完全追補** | L2 + 節追補20 | **約261** | 教科書レベルまで |
| L4 叙事年単位 | 1000チャット超 | 非推奨 | — |

**チャット回数を増やして深くしたい場合** → [deep-production-roadmap.md](deep-production-roadmap.md)

---

## 各チャットの定型手順

1. 該当の依頼文をコピペ（`@` 添付）
2. 必要なら `python ラノベファンタジー/Tools/history_sim_context_builder.py --nation N --macro-turn T -o context.json`
3. 生成 JSON を所定フォルダに保存
4. チェックリストにチェック
5. **新しいチャット** で次の章/バッチへ（1チャット1ファイル推奨）

---

## 整合性の取り方

| 確認項目 | 方法 |
|---------|------|
| 叙事年と macroTurn | 叙事終端 = macroTurn × 10 |
| 世界指標 | `worldAnalytics` in context.json と章の記述を突合 |
| 大国の呼称 | 001→最西端の統合王国、002→東方の魔導大国（章JSONと国JSONで統一） |
| 滅亡タイミング | 国JSONの macroTurn は歴史棚ヘッダーの「ターンXX」と一致させる |
| 重複 | 章JSON＝世界イベント、国JSON＝国家固有。同じ年の出来事は視点を分ける |

---

## 欠番

国家 **041〜045** の歴史棚 txt はありません。`--list-nations` で確認し、必要なら別途補完してください。

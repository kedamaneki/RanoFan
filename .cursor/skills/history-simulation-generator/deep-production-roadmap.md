# 深掘り全史ロードマップ（チャット回数無制限版）

叙事 **1年目〜1000年目** を可能な限り厚くするための進行手順です。  
**標準版（30チャット）** は [full-production-roadmap.md](full-production-roadmap.md) を参照。

---

## 深掘りの3層構造

```
層1 タイムライン脊髄 … macroTurn 1〜100（100チャット）
      ↓ 各ターンの世界指標 + 滅亡国 + 章テキスト
層2 国家プロファイル … 131カ国（131チャット）
      ↓ 各国5〜7イベント（章ごと + 滅亡/新生 + 固有技術）
層3 大国・節の追補 … 約25チャット（任意）
      ↓ 001/002/015 等 + 歴史書の「第N節」単位
```

| フェーズ | チャット数 | 1チャットの出力 | 目安イベント数 |
|---------|-----------|----------------|--------------|
| **S: タイムライン脊髄** | **100** | macroTurn 1件分 | 2〜8件/ターン → **400〜600件** |
| **N: 国家深掘り** | **131** | 国家1件分 | 5〜7件/国 → **655〜917件** |
| **G: 大国追補** | **10** | 大国1件×章 | 3〜5件/回 → **30〜50件** |
| **M: 章の節** | **〜20** | 節1つ | 3〜5件/節 → **60〜100件** |
| **合計** | **約261〜282** | | **約1,150〜1,670件** |

---

## 推奨進行順序

```
1. フェーズ S（T01 → T100）  … 千年の骨格を時系列で固定
2. フェーズ N（001 → 131）  … 各国の肉付け（Sと矛盾しないよう参照）
3. フェーズ G（大国追補）    … 001/002 等をさらに厚く
4. フェーズ M（節追補）      … 章MDの抜けを埋める
```

**1チャット = 1スコープ** を厳守。1チャットに複数ターン・複数国を詰めない。

---

## 出力フォルダ

```
HistorySimulation/
├── spine/           SIM_T001.json … SIM_T100.json
├── nations/         SIM_NATION_001.json … SIM_NATION_131.json
├── greatpowers/     SIM_GP_NATION_001.json 等
└── sections/        SIM_CH01_SEC02.json 等
```

---

## 事前準備（1回だけ）

```bash
# 全国家一覧
python ラノベファンタジー/Tools/history_sim_context_builder.py --list-nations -o nations.tsv

# macroTurn ごとの滅亡・新生索引（フェーズSで毎回参照）
python ラノベファンタジー/Tools/history_sim_context_builder.py --turn-index -o turn_index.json
```

---

## ★ 深掘りマスター依頼文（最初の1チャット）

```
@.cursor/skills/history-simulation-generator/SKILL.md
@.cursor/skills/history-simulation-generator/schema-reference.md
@.cursor/skills/history-simulation-generator/deep-production-roadmap.md
@world_analytics.json
@歴史棚

【依頼】千年史 simulationEvents の深掘り量産を開始します。チャット回数は無制限です。

## 時間スケール（厳守）
- 1 macroTurn = 10叙事年 = 1 analytics暦年 = 12 analytics turn
- 叙事年（終端）= macroTurn × 10
- analytics.year = 1000 + macroTurn - 1
- timestamp.year は叙事年のみ

## 深掘り方針
- フェーズS: macroTurn 1件ずつ100チャット（脊髄）
- フェーズN: 国家1件ずつ131チャット（各5〜7イベント）
- 既存 spine JSON と矛盾させない

## 今回のスコープ（フェーズS・ターン1のみ）
macroTurn 1（叙事1〜10年目、analytics year 1000）の simulationEvents を生成してください。

含めること:
1. 世界規模イベント1〜2件（脅威度1.232、131国、総国力19340付近）— 歴史書その1.md 第1節
2. このターンに滅亡する国家があれば各国1件（turn_index 参照）
3. 平時は prosperityTimeline / 有事は decayTimeline の排他

手順:
python ラノベファンタジー/Tools/history_sim_context_builder.py --macro-turn 1 -o context_T01.json

出力: HistorySimulation/spine/SIM_T001.json
```

---

## フェーズ S — タイムライン脊髄（100チャット）

### 定型依頼文（T{NN} に置換）

```
@.cursor/skills/history-simulation-generator/SKILL.md
@schema-reference.md
@world_analytics.json
@歴史棚/歴史書その1.md
@歴史棚/歴史書その2.md
@歴史棚/歴史書その3.md
@歴史棚/歴史書その4.md
@歴史棚/歴史書その5.md

【依頼】フェーズS・macroTurn {NN} の simulationEvents を深掘り生成してください。

■ 叙事: {(NN-1)*10+1}〜{NN*10}年目
■ analytics: year {1000+NN-1}, turn {(NN-1)*12+1}〜{NN*12}
■ evolutionSystemType: {NN<=40→Ritual, NN<=60→Inspiration, それ以外→Recipe}

手順:
1. python ラノベファンタジー/Tools/history_sim_context_builder.py --macro-turn {NN} -o context_T{NN:03d}.json
2. context の worldAnalytics / nationsAtThisTurn を確認
3. 歴史書その{章}.md で「ターン{NN}」「{NN*10}年目」を検索し該当節を反映

生成内容（このターンに応じて調整）:
- 世界イベント: 1〜3件（MonsterAttack / MagicPioneering / 国力変動など）
- 滅亡国: nationsAtThisTurn.collapse の各国に NationCollapse + decayTimeline 1件ずつ
- 新生滅亡国: nationsAtThisTurn.birthCollapse は滅亡側のみ（誕生は別ターンで）
- 大国の動き（001/002）が章テキストにあれば追加

件数目安: 滅亡国が多いターン（T47等）は8件まで可。平常ターンは2〜4件。
UI国家番号禁止。

出力: HistorySimulation/spine/SIM_T{NN:03d}.json
```

### 章と macroTurn の対応（添付する md を絞る）

| macroTurn | 叙事年 | 主に読む md |
|-----------|--------|------------|
| 1〜20 | 1〜200 | その1.md |
| 21〜40 | 201〜400 | その2.md |
| 41〜60 | 401〜600 | その3.md |
| 61〜80 | 601〜800 | その4.md |
| 81〜100 | 801〜1000 | その5.md |

### 滅亡が集中するターン（多めのイベントを想定）

| macroTurn | 叙事年 | 備考 |
|-----------|--------|------|
| 12〜19 | 120〜190 | 北方の早期滅亡が多い |
| 13〜16 | 130〜160 | 同上 |
| 31, 33, 34, 36 | 310〜360 | 第2章の淘汰 |
| 42〜49 | 420〜490 | 第3章・暗黒期の大量滅亡 |
| 46〜48 | 460〜480 | 特に西・中央 |
| 51, 53, 55, 58 | 510〜580 | 第3章後半 |
| 71〜79 | 710〜790 | 新生国の誕生と短命滅亡 |
| 100 | 1000 | 最終ターン・完全駆逐 |

`turn_index.json` で正確な国リストを毎回確認してください。

### フェーズ S チェックリスト

- [ ] T001 … T010（第1章前半）
- [ ] T011 … T020（第1章後半）
- [ ] T021 … T030（第2章前半）
- [ ] T031 … T040（第2章後半）
- [ ] T041 … T050（第3章前半）
- [ ] T051 … T060（第3章後半）
- [ ] T061 … T070（第4章前半）
- [ ] T071 … T080（第4章後半）
- [ ] T081 … T090（第5章前半）
- [ ] T091 … T100（第5章後半・千年の結末）

※ 細かく進めたい場合は **1ターン1チェック**（100行）で管理。

---

## フェーズ N — 国家深掘り（131チャット）

### 定型依頼文（国家 {NNN} に置換）

```
@.cursor/skills/history-simulation-generator/SKILL.md
@schema-reference.md
@world_analytics.json
@歴史棚/歴史書その{ファイル番号}.txt

【依頼】フェーズN・NATION_{NNN} の深掘り simulationEvents を生成してください。

手順:
1. python ラノベファンタジー/Tools/history_sim_context_builder.py --nation-deep {N} -o plan_{NNN}.json
2. plan の deepEventPlan に従い、**5〜7イベント**を生成
3. 既存 spine/SIM_T*.json と macroTurn・叙事年が矛盾しないこと

## 国家 {NNN} のイベント構成（deepEventPlan 準拠）

| # | 時代 | macroTurn目安 | ルート | eventType 例 |
|---|------|--------------|--------|-------------|
| 1 | 第1章 | 5〜10 | prosperity | ShieldFortification / MonsterRepelled |
| 2 | 第2章 | 25〜30 | prosperity or decay | LocalConflict / MonsterAttack |
| 3 | 第3章 | 45〜50 | 暗黒期 | MonsterAttack or 有事 decay |
| 4 | 第4章 | 65〜75 | prosperity（生存時） | MagicPioneering / NationBirth |
| 5 | 第5章 | 85〜95 | prosperity（生存時） | 復興・交易 |
| 6 | 滅亡時 | ヘッダーのターン | decay のみ | NationCollapse |
| 7 | 固有技術 | 本文の「」技術 | prosperity | ShieldFortification or MagicPioneering |

- 滅亡国: 滅亡前2〜3件は prosperity、最終1件は decayTimeline フル
- 新生滅亡国: NationBirth（誕生ターン）+ 短命存続 + NationCollapse
- 生存国: 5章すべて prosperity + 固有技術1件
- 各イベントに historicalMode / alternativeMode / mmoMode のいずれかを付与（3モードをバランス）

UI国家番号禁止。技術名は歴史棚の「」から引用。

出力: HistorySimulation/nations/SIM_NATION_{NNN}.json（1ファイルに simulationEvents 配列）
```

### 国家番号 → 歴史棚 txt

| 国家 | txt | 国家 | txt |
|------|-----|------|-----|
| 001-005 | その6 | 066-070 | その18 |
| 006-010 | その7 | 071-075 | その19 |
| 011-015 | その8 | 076-080 | その20 |
| 016-020 | その9 | 081-085 | その21 |
| 021-025 | その10 | 086-090 | その22 |
| 026-030 | その11 | 091-095 | その23 |
| 031-035 | その12 | 096-100 | その24 |
| 036-040 | その13 | 101-105 | その25 |
| 046-050 | その14 | 106-110 | その26 |
| 051-055 | その15 | 111-115 | その27 |
| 056-060 | その16 | 116-120 | その28 |
| 061-065 | その17 | 121-125 | その29 |
| | | 126-131 | その30 |

※ 041-045 は欠番。

### フェーズ N チェックリスト（10国ずつ）

- [ ] NATION_001〜010（その6-7）
- [ ] NATION_011〜020（その8-9）
- [ ] NATION_021〜030（その10-11）
- [ ] NATION_031〜040（その12-13）
- [ ] NATION_046〜055（その14-15）※041-045欠番
- [ ] NATION_056〜065（その16-17）
- [ ] NATION_066〜075（その18-19）
- [ ] NATION_076〜085（その20-21）
- [ ] NATION_086〜095（その22-23）
- [ ] NATION_096〜105（その24-25）
- [ ] NATION_106〜115（その26-27）
- [ ] NATION_116〜125（その28-29）
- [ ] NATION_126〜131（その30）

---

## フェーズ G — 大国追補（10チャット・任意）

章の総括で繰り返し登場する国家を、**さらに3〜5イベント**追加。

| チャット | 対象 | 添付 | 題材 |
|---------|------|------|------|
| G1 | 国家001 | その6.txt + その1-5.md | 初期統合の兆し（T1-20） |
| G2 | 国家001 | 同上 | 焦土戦術・絶対防衛圏（T21-60） |
| G3 | 国家001 | 同上 | 統合王国完成・大遠征（T61-100） |
| G4 | 国家002 | その6.txt + その1-5.md | 障壁防衛網（T1-20） |
| G5 | 国家002 | 同上 | 焦土・深層魔導都市（T21-60） |
| G6 | 国家002 | 同上 | 魔導大国・双極秩序（T61-100） |
| G7 | 国家015 | その8.txt + その3.md | 分散防衛・第3章 |
| G8 | 国家001×002 | その1-5.md | 双極対立・外交（T80-100） |
| G9 | 世界 | その5.md | 最終データ（68国・226151・脅威0.5） |
| G10 | 世界 | turn_index | 欠番・滅亡統計の整理イベント |

---

## フェーズ M — 章の節追補（〜20チャット・任意）

歴史書 md の **第N節** ごとに3〜5イベント。フェーズSで薄かった節を補う。

例（その1.md）:
- M01: 第1節 高脅威周期の到来（T1-4）
- M02: 第2節 国家002障壁戦（T5-10）
- M03: 第3節 国家001統合の兆し（T11-20）
- … 各章同様

---

## 品質を保つコツ

1. **常に context_builder を先に実行**（数値と滅亡リストの取り違え防止）
2. **フェーズSを先に完了**してからフェーズN（時系列の正本を spine に）
3. **1チャットの上限**: イベント8件まで。超えるならターンを分割
4. **decayTimeline は滅亡1件に1フルセット**（3フェーズ省略禁止）
5. **同じ叙事年に同国2イベント**を作らない
6. 定期的に `turn_index.json` と spine を突合

---

## さらに深くする場合（上級）

| 手法 | 追加チャット | 内容 |
|------|-------------|------|
| 叙事年単位 | +1000 | 1年1イベント（非推奨・管理不能） |
| alternativeMode 専用 | +131 | 各国に改変成功ルート1件 |
| mmoMode 専用 | +50 | 区域ごとのゾーン進行 |
| 領域別 | +5 | 東西南北中央の地域史 |

現実的な上限は **フェーズ S+N+G ≈ 241チャット・1,200イベント前後** です。

---

## 関連ファイル

- 標準（30チャット）: [full-production-roadmap.md](full-production-roadmap.md)
- 5国バッチ短文: [batch-prompts-all.md](batch-prompts-all.md)
- 単発依頼: [new-chat-prompt.md](new-chat-prompt.md)

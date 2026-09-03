# 5国バッチ依頼文 — 全ファイル一覧

歴史棚 `歴史書その6.txt` 〜 `その30.txt` の **25ファイル**（計131カ国）に対応する、新チャット用コピペ依頼文です。  
※ その30のみ **6カ国**（126〜131）。その1〜5（.md）は章概説のため対象外。

**推奨進行**: その6 → その7 → … → その30 の順。1チャット = 1ファイル = 5〜6イベント。

---

## 共通ルール（全バッチ共通・毎回書いておくと安全）

```
■ 10倍スケール:
  - analytics 100暦年（year 1000〜1099）→ 叙事1000年
  - 1 macroTurn = 10叙事年 = 1 analytics暦年 = 12 analytics turn
  - 叙事年（終端）= macroTurn × 10
  - analytics.year = 1000 + macroTurn - 1
  - 出力 timestamp.year は叙事年（analytics year をそのまま入れない）

■ ルート:
  - 滅亡・新生滅亡 → isHistoricalCollapseRoute: true + decayTimeline のみ
  - 生存 → isHistoricalCollapseRoute: false + prosperityTimeline のみ

■ UI文字列に国家番号・NATION_xxx 禁止
■ 出力: 国別 JSON 5〜6ファイル、または1ファイルに simulationEvents 配列
■ 出力先: ラノベファンタジー/Assets/Resources/HistorySimulation/
```

---

## 索引一覧

| # | ファイル | 国家 | 滅亡/新生（macroTurn） |
|---|---------|------|------------------------|
| 1 | 歴史書その6.txt | 001〜005 | 全員生存（千年史形式） |
| 2 | 歴史書その7.txt | 006〜010 | 全員生存（千年史形式） |
| 3 | 歴史書その8.txt | 011〜015 | 全員生存（千年史形式） |
| 4 | 歴史書その9.txt | 016〜020 | 019 滅亡 T51 |
| 5 | 歴史書その10.txt | 021〜025 | 022 T13, 024 T34 |
| 6 | 歴史書その11.txt | 026〜030 | 027 T45, 029 T19, 030 新生滅亡 T71→73 |
| 7 | 歴史書その12.txt | 031〜035 | 032 T49, 034 T22 |
| 8 | 歴史書その13.txt | 036〜040 | 037 T58, 039 T16 |
| 9 | 歴史書その14.txt | 046〜050 | 047 T36, 049 T47 |
| 10 | 歴史書その15.txt | 051〜055 | 051 T42, 054 T31 |
| 11 | 歴史書その16.txt | 056〜060 | 057 T48, 059 T18 |
| 12 | 歴史書その17.txt | 061〜065 | 062 T55, 063 新生滅亡 T72→75, 065 T17 |
| 13 | 歴史書その18.txt | 066〜070 | 067 T39, 069 新生滅亡 T74→77 |
| 14 | 歴史書その19.txt | 071〜075 | 072 T46, 074 T14, 075 新生滅亡 T75→79 |
| 15 | 歴史書その20.txt | 076〜080 | 077 T33, 079 T53, 080 新生滅亡 T73→76 |
| 16 | 歴史書その21.txt | 081〜085 | 082 T44, 084 T13, 085 新生滅亡 T76→78 |
| 17 | 歴史書その22.txt | 086〜090 | 087 T47, 089 T12, 090 新生滅亡 T77→79 |
| 18 | 歴史書その23.txt | 091〜095 | 092 T49, 094 T15, 095 新生滅亡 T75→78 |
| 19 | 歴史書その24.txt | 096〜100 | 097 T48, 099 T16, 100 新生滅亡 T76→79 |
| 20 | 歴史書その25.txt | 101〜105 | 102 T47, 104 T14, 105 新生滅亡 T77→79 |
| 21 | 歴史書その26.txt | 106〜110 | 107 T46, 109 T13, 110 新生滅亡 T75→78 |
| 22 | 歴史書その27.txt | 111〜115 | 112 T47, 114 T14, 115 新生滅亡 T77→79 |
| 23 | 歴史書その28.txt | 116〜120 | 117 T46, 119 T13, 120 新生滅亡 T75→78 |
| 24 | 歴史書その29.txt | 121〜125 | 122 T47, 124 T14, 125 新生滅亡 T76→79 |
| 25 | 歴史書その30.txt | 126〜131（6国） | 127 T47, 129 T14, 130 新生滅亡 T76→79 |

---

## バッチ 01 — 歴史書その6.txt（国家001〜005）

```
@.cursor/skills/history-simulation-generator/SKILL.md
@.cursor/skills/history-simulation-generator/schema-reference.md
@world_analytics.json
@歴史棚/歴史書その6.txt

【依頼】history-simulation-generator スキルに従い、歴史書その6.txt の5カ国分の simulationEvents を生成してください。

■ 対象: NATION_001, NATION_002, NATION_003, NATION_004, NATION_005
■ 10倍スケール厳守（叙事年 = macroTurn×10、analytics.year = 1000+macroTurn-1、timestamp.year は叙事年）
■ 各国1イベント。全員生存 → isHistoricalCollapseRoute: false + prosperityTimeline のみ
■ 生存国の macroTurn は本文の重要期（第1章なら T1〜10、第2章なら T11〜20 等）から代表を選定
■ 国家002（東方魔導大国）・国家001（西方統合）の大局は本文の技術名を microLore に反映
■ UI文字列に国家番号禁止

手順:
1. 各国: python ラノベファンタジー/Tools/history_sim_context_builder.py --nation N --macro-turn T -o context_N.json
2. simulationEvents を国別5ファイルで出力

出力先: ラノベファンタジー/Assets/Resources/HistorySimulation/batch06/
```

---

## バッチ 02 — 歴史書その7.txt（国家006〜010）

```
@.cursor/skills/history-simulation-generator/SKILL.md
@schema-reference.md
@world_analytics.json
@歴史棚/歴史書その7.txt

【依頼】歴史書その7.txt の NATION_006〜010 について、各国1イベントの simulationEvents JSON を生成してください。

■ 10倍スケール厳守 / timestamp.year は叙事年
■ 全員生存（千年史形式）→ prosperityTimeline のみ
■ 各国の「」内技術名を歴史棚から抽出して反映
■ UI国家番号禁止

手順: 各国 context_builder 実行後、5ファイル出力
出力先: ラノベファンタジー/Assets/Resources/HistorySimulation/batch07/
```

---

## バッチ 03 — 歴史書その8.txt（国家011〜015）

```
@.cursor/skills/history-simulation-generator/SKILL.md
@schema-reference.md
@world_analytics.json
@歴史棚/歴史書その8.txt

【依頼】歴史書その8.txt の NATION_011〜015 について、各国1イベントの simulationEvents JSON を生成してください。

■ 10倍スケール厳守 / 生存国は prosperityTimeline のみ
■ UI国家番号禁止

出力先: ラノベファンタジー/Assets/Resources/HistorySimulation/batch08/
```

---

## バッチ 04 — 歴史書その9.txt（国家016〜020）

```
@.cursor/skills/history-simulation-generator/SKILL.md
@schema-reference.md
@world_analytics.json
@歴史棚/歴史書その9.txt

【依頼】歴史書その9.txt の NATION_016〜020 について、各国1イベントを生成してください。

■ 10倍スケール厳守
■ NATION_019: 滅亡 T51（叙事510年目、analytics year 1050）→ decayTimeline のみ、eventType: NationCollapse
■ 016, 017, 018, 020: 生存 → prosperityTimeline のみ
■ UI国家番号禁止

手順:
1. python ラノベファンタジー/Tools/history_sim_context_builder.py --nation 19 --macro-turn 51 --collapse -o context_019.json
2. 他4国も context_builder で代表 macroTurn を選定

出力先: ラノベファンタジー/Assets/Resources/HistorySimulation/batch09/
```

---

## バッチ 05 — 歴史書その10.txt（国家021〜025）

```
@.cursor/skills/history-simulation-generator/SKILL.md
@schema-reference.md
@world_analytics.json
@歴史棚/歴史書その10.txt

【依頼】歴史書その10.txt の NATION_021〜025 について、各国1イベントを生成してください。

■ NATION_022: 滅亡 T13（叙事130年目）→ decayTimeline
■ NATION_024: 滅亡 T34（叙事340年目）→ decayTimeline
■ NATION_021, 023, 025: 生存 → prosperityTimeline
■ 10倍スケール / UI国家番号禁止

出力先: ラノベファンタジー/Assets/Resources/HistorySimulation/batch10/
```

---

## バッチ 06 — 歴史書その11.txt（国家026〜030）

```
@.cursor/skills/history-simulation-generator/SKILL.md
@schema-reference.md
@world_analytics.json
@歴史棚/歴史書その11.txt

【依頼】歴史書その11.txt の NATION_026〜030 について、各国1イベントを生成してください。

■ NATION_027: 滅亡 T45 → decayTimeline
■ NATION_029: 滅亡 T19 → decayTimeline
■ NATION_030: 新生滅亡 T71誕生〜T73崩壊 → decayTimeline（有事）、叙事710〜730年目
■ NATION_026, 028: 生存 → prosperityTimeline
■ 10倍スケール / UI国家番号禁止

出力先: ラノベファンタジー/Assets/Resources/HistorySimulation/batch11/
```

---

## バッチ 07 — 歴史書その12.txt（国家031〜035）

```
@.cursor/skills/history-simulation-generator/SKILL.md
@schema-reference.md
@world_analytics.json
@歴史棚/歴史書その12.txt

【依頼】歴史書その12.txt の NATION_031〜035 について、各国1イベントを生成してください。

■ NATION_032: 滅亡 T49 → decayTimeline
■ NATION_034: 滅亡 T22 → decayTimeline
■ NATION_031, 033, 035: 生存 → prosperityTimeline
■ 10倍スケール / UI国家番号禁止

出力先: ラノベファンタジー/Assets/Resources/HistorySimulation/batch12/
```

---

## バッチ 08 — 歴史書その13.txt（国家036〜040）

```
@.cursor/skills/history-simulation-generator/SKILL.md
@schema-reference.md
@world_analytics.json
@歴史棚/歴史書その13.txt

【依頼】歴史書その13.txt の NATION_036〜040 について、各国1イベントを生成してください。

■ NATION_037: 滅亡 T58 → decayTimeline
■ NATION_039: 滅亡 T16 → decayTimeline
■ NATION_036, 038, 040: 生存 → prosperityTimeline
■ 10倍スケール / UI国家番号禁止

出力先: ラノベファンタジー/Assets/Resources/HistorySimulation/batch13/
```

---

## バッチ 09 — 歴史書その14.txt（国家046〜050）

```
@.cursor/skills/history-simulation-generator/SKILL.md
@schema-reference.md
@world_analytics.json
@歴史棚/歴史書その14.txt

【依頼】歴史書その14.txt の NATION_046〜050 について、各国1イベントを生成してください。

■ NATION_047: 滅亡 T36 → decayTimeline
■ NATION_049: 滅亡 T47 → decayTimeline
■ NATION_046, 048, 050: 生存 → prosperityTimeline
■ 10倍スケール / UI国家番号禁止

出力先: ラノベファンタジー/Assets/Resources/HistorySimulation/batch14/
```

---

## バッチ 10 — 歴史書その15.txt（国家051〜055）

```
@.cursor/skills/history-simulation-generator/SKILL.md
@schema-reference.md
@world_analytics.json
@歴史棚/歴史書その15.txt

【依頼】歴史書その15.txt の NATION_051〜055 について、各国1イベントを生成してください。

■ NATION_051: 滅亡 T42 → decayTimeline
■ NATION_054: 滅亡 T31 → decayTimeline
■ NATION_052, 053, 055: 生存 → prosperityTimeline
■ 10倍スケール / UI国家番号禁止

出力先: ラノベファンタジー/Assets/Resources/HistorySimulation/batch15/
```

---

## バッチ 11 — 歴史書その16.txt（国家056〜060）

```
@.cursor/skills/history-simulation-generator/SKILL.md
@schema-reference.md
@world_analytics.json
@歴史棚/歴史書その16.txt

【依頼】歴史書その16.txt の NATION_056〜060 について、各国1イベントを生成してください。

■ NATION_057: 滅亡 T48 → decayTimeline
■ NATION_059: 滅亡 T18 → decayTimeline
■ NATION_056, 058, 060: 生存 → prosperityTimeline
■ 10倍スケール / UI国家番号禁止

出力先: ラノベファンタジー/Assets/Resources/HistorySimulation/batch16/
```

---

## バッチ 12 — 歴史書その17.txt（国家061〜065）

```
@.cursor/skills/history-simulation-generator/SKILL.md
@schema-reference.md
@world_analytics.json
@歴史棚/歴史書その17.txt

【依頼】歴史書その17.txt の NATION_061〜065 について、各国1イベントを生成してください。

■ NATION_062: 滅亡 T55 → decayTimeline
■ NATION_063: 新生滅亡 T72〜T75 → decayTimeline
■ NATION_065: 滅亡 T17 → decayTimeline
■ NATION_061, 064: 生存 → prosperityTimeline
■ 10倍スケール / UI国家番号禁止

出力先: ラノベファンタジー/Assets/Resources/HistorySimulation/batch17/
```

---

## バッチ 13 — 歴史書その18.txt（国家066〜070）

```
@.cursor/skills/history-simulation-generator/SKILL.md
@schema-reference.md
@world_analytics.json
@歴史棚/歴史書その18.txt

【依頼】歴史書その18.txt の NATION_066〜070 について、各国1イベントを生成してください。

■ NATION_067: 滅亡 T39 → decayTimeline
■ NATION_069: 新生滅亡 T74〜T77 → decayTimeline
■ NATION_066, 068, 070: 生存 → prosperityTimeline
■ 10倍スケール / UI国家番号禁止

出力先: ラノベファンタジー/Assets/Resources/HistorySimulation/batch18/
```

---

## バッチ 14 — 歴史書その19.txt（国家071〜075）

```
@.cursor/skills/history-simulation-generator/SKILL.md
@schema-reference.md
@data-sources.md
@world_analytics.json
@歴史棚/歴史書その19.txt

【依頼】歴史書その19.txt の NATION_071〜075 について、各国1イベントを生成してください。

■ NATION_072: 滅亡 T46（叙事460年目、analytics year 1045）→ decayTimeline、eventType: NationCollapse
■ NATION_074: 滅亡 T14 → decayTimeline
■ NATION_075: 新生滅亡 T75〜T79 → decayTimeline
■ NATION_071, 073: 生存 → prosperityTimeline
■ 10倍スケール / timestamp.year は叙事年 / UI国家番号禁止

手順:
1. python ラノベファンタジー/Tools/history_sim_context_builder.py --nation 72 --macro-turn 46 --collapse -o context_072.json
2. 他国も同様に context 抽出

出力先: ラノベファンタジー/Assets/Resources/HistorySimulation/batch19/
```

---

## バッチ 15 — 歴史書その20.txt（国家076〜080）

```
@.cursor/skills/history-simulation-generator/SKILL.md
@schema-reference.md
@world_analytics.json
@歴史棚/歴史書その20.txt

【依頼】歴史書その20.txt の NATION_076〜080 について、各国1イベントを生成してください。

■ NATION_077: 滅亡 T33 → decayTimeline
■ NATION_079: 滅亡 T53 → decayTimeline
■ NATION_080: 新生滅亡 T73〜T76 → decayTimeline
■ NATION_076, 078: 生存 → prosperityTimeline
■ 10倍スケール / UI国家番号禁止

出力先: ラノベファンタジー/Assets/Resources/HistorySimulation/batch20/
```

---

## バッチ 16 — 歴史書その21.txt（国家081〜085）

```
@.cursor/skills/history-simulation-generator/SKILL.md
@schema-reference.md
@world_analytics.json
@歴史棚/歴史書その21.txt

【依頼】歴史書その21.txt の NATION_081〜085 について、各国1イベントを生成してください。

■ NATION_082: 滅亡 T44 → decayTimeline
■ NATION_084: 滅亡 T13 → decayTimeline
■ NATION_085: 新生滅亡 T76〜T78 → decayTimeline
■ NATION_081, 083: 生存 → prosperityTimeline
■ 10倍スケール / UI国家番号禁止

出力先: ラノベファンタジー/Assets/Resources/HistorySimulation/batch21/
```

---

## バッチ 17 — 歴史書その22.txt（国家086〜090）

```
@.cursor/skills/history-simulation-generator/SKILL.md
@schema-reference.md
@world_analytics.json
@歴史棚/歴史書その22.txt

【依頼】歴史書その22.txt の NATION_086〜090 について、各国1イベントを生成してください。

■ NATION_087: 滅亡 T47 → decayTimeline
■ NATION_089: 滅亡 T12 → decayTimeline
■ NATION_090: 新生滅亡 T77〜T79 → decayTimeline
■ NATION_086, 088: 生存 → prosperityTimeline
■ 10倍スケール / UI国家番号禁止

出力先: ラノベファンタジー/Assets/Resources/HistorySimulation/batch22/
```

---

## バッチ 18 — 歴史書その23.txt（国家091〜095）

```
@.cursor/skills/history-simulation-generator/SKILL.md
@schema-reference.md
@world_analytics.json
@歴史棚/歴史書その23.txt

【依頼】歴史書その23.txt の NATION_091〜095 について、各国1イベントを生成してください。

■ NATION_092: 滅亡 T49 → decayTimeline
■ NATION_094: 滅亡 T15 → decayTimeline
■ NATION_095: 新生滅亡 T75〜T78 → decayTimeline
■ NATION_091, 093: 生存 → prosperityTimeline
■ 10倍スケール / UI国家番号禁止

出力先: ラノベファンタジー/Assets/Resources/HistorySimulation/batch23/
```

---

## バッチ 19 — 歴史書その24.txt（国家096〜100）

```
@.cursor/skills/history-simulation-generator/SKILL.md
@schema-reference.md
@world_analytics.json
@歴史棚/歴史書その24.txt

【依頼】歴史書その24.txt の NATION_096〜100 について、各国1イベントを生成してください。

■ NATION_097: 滅亡 T48 → decayTimeline
■ NATION_099: 滅亡 T16 → decayTimeline
■ NATION_100: 新生滅亡 T76〜T79 → decayTimeline
■ NATION_096, 098: 生存 → prosperityTimeline
■ 10倍スケール / UI国家番号禁止

出力先: ラノベファンタジー/Assets/Resources/HistorySimulation/batch24/
```

---

## バッチ 20 — 歴史書その25.txt（国家101〜105）

```
@.cursor/skills/history-simulation-generator/SKILL.md
@schema-reference.md
@world_analytics.json
@歴史棚/歴史書その25.txt

【依頼】歴史書その25.txt の NATION_101〜105 について、各国1イベントを生成してください。

■ NATION_102: 滅亡 T47 → decayTimeline
■ NATION_104: 滅亡 T14 → decayTimeline
■ NATION_105: 新生滅亡 T77〜T79 → decayTimeline
■ NATION_101, 103: 生存 → prosperityTimeline
■ 10倍スケール / UI国家番号禁止

出力先: ラノベファンタジー/Assets/Resources/HistorySimulation/batch25/
```

---

## バッチ 21 — 歴史書その26.txt（国家106〜110）

```
@.cursor/skills/history-simulation-generator/SKILL.md
@schema-reference.md
@world_analytics.json
@歴史棚/歴史書その26.txt

【依頼】歴史書その26.txt の NATION_106〜110 について、各国1イベントを生成してください。

■ NATION_107: 滅亡 T46 → decayTimeline
■ NATION_109: 滅亡 T13 → decayTimeline
■ NATION_110: 新生滅亡 T75〜T78 → decayTimeline
■ NATION_106, 108: 生存 → prosperityTimeline
■ 10倍スケール / UI国家番号禁止

出力先: ラノベファンタジー/Assets/Resources/HistorySimulation/batch26/
```

---

## バッチ 22 — 歴史書その27.txt（国家111〜115）

```
@.cursor/skills/history-simulation-generator/SKILL.md
@schema-reference.md
@world_analytics.json
@歴史棚/歴史書その27.txt

【依頼】歴史書その27.txt の NATION_111〜115 について、各国1イベントを生成してください。

■ NATION_112: 滅亡 T47 → decayTimeline
■ NATION_114: 滅亡 T14 → decayTimeline
■ NATION_115: 新生滅亡 T77〜T79 → decayTimeline
■ NATION_111, 113: 生存 → prosperityTimeline
■ 10倍スケール / UI国家番号禁止

出力先: ラノベファンタジー/Assets/Resources/HistorySimulation/batch27/
```

---

## バッチ 23 — 歴史書その28.txt（国家116〜120）

```
@.cursor/skills/history-simulation-generator/SKILL.md
@schema-reference.md
@world_analytics.json
@歴史棚/歴史書その28.txt

【依頼】歴史書その28.txt の NATION_116〜120 について、各国1イベントを生成してください。

■ NATION_117: 滅亡 T46 → decayTimeline
■ NATION_119: 滅亡 T13 → decayTimeline
■ NATION_120: 新生滅亡 T75〜T78 → decayTimeline
■ NATION_116, 118: 生存 → prosperityTimeline
■ 10倍スケール / UI国家番号禁止

出力先: ラノベファンタジー/Assets/Resources/HistorySimulation/batch28/
```

---

## バッチ 24 — 歴史書その29.txt（国家121〜125）

```
@.cursor/skills/history-simulation-generator/SKILL.md
@schema-reference.md
@world_analytics.json
@歴史棚/歴史書その29.txt

【依頼】歴史書その29.txt の NATION_121〜125 について、各国1イベントを生成してください。

■ NATION_122: 滅亡 T47 → decayTimeline
■ NATION_124: 滅亡 T14 → decayTimeline
■ NATION_125: 新生滅亡 T76〜T79 → decayTimeline
■ NATION_121, 123: 生存 → prosperityTimeline
■ 10倍スケール / UI国家番号禁止

出力先: ラノベファンタジー/Assets/Resources/HistorySimulation/batch29/
```

---

## バッチ 25 — 歴史書その30.txt（国家126〜131・6カ国）

```
@.cursor/skills/history-simulation-generator/SKILL.md
@schema-reference.md
@world_analytics.json
@歴史棚/歴史書その30.txt

【依頼】歴史書その30.txt の NATION_126〜131（6カ国）について、各国1イベントを生成してください。

■ NATION_127: 滅亡 T47 → decayTimeline
■ NATION_129: 滅亡 T14 → decayTimeline
■ NATION_130: 新生滅亡 T76〜T79 → decayTimeline
■ NATION_126, 128, 131: 生存 → prosperityTimeline
■ 10倍スケール / UI国家番号禁止

出力先: ラノベファンタジー/Assets/Resources/HistorySimulation/batch30/
```

---

## 進行チェックリスト

- [ ] バッチ01 その6.txt  001-005
- [ ] バッチ02 その7.txt  006-010
- [ ] バッチ03 その8.txt  011-015
- [ ] バッチ04 その9.txt  016-020
- [ ] バッチ05 その10.txt 021-025
- [ ] バッチ06 その11.txt 026-030
- [ ] バッチ07 その12.txt 031-035
- [ ] バッチ08 その13.txt 036-040
- [ ] バッチ09 その14.txt 046-050
- [ ] バッチ10 その15.txt 051-055
- [ ] バッチ11 その16.txt 056-060
- [ ] バッチ12 その17.txt 061-065
- [ ] バッチ13 その18.txt 066-070
- [ ] バッチ14 その19.txt 071-075
- [ ] バッチ15 その20.txt 076-080
- [ ] バッチ16 その21.txt 081-085
- [ ] バッチ17 その22.txt 086-090
- [ ] バッチ18 その23.txt 091-095
- [ ] バッチ19 その24.txt 096-100
- [ ] バッチ20 その25.txt 101-105
- [ ] バッチ21 その26.txt 106-110
- [ ] バッチ22 その27.txt 111-115
- [ ] バッチ23 その28.txt 116-120
- [ ] バッチ24 その29.txt 121-125
- [ ] バッチ25 その30.txt 126-131

**合計: 25チャット（131カ国）**

## 欠番について

国家041〜045の txt は存在しません（その13が036-040、その14が046-050）。欠番国があれば `history_sim_context_builder.py --list-nations` で確認してください。

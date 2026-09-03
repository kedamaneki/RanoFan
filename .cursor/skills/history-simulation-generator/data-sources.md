# データソース仕様

## ファイル配置

```
F:/制作物/その6/
├── world_analytics.json          # マクロ分析（月次ターン）
├── 歴史棚/
│   ├── 歴史書その1.md            # 第1章（macroTurn 1-20 概略）
│   ├── 歴史書その2.md … その4.md
│   ├── 歴史書その5.md            # 第5章・総括含む
│   ├── 歴史書その6.txt … その30.txt  # 国家別ブロック（5国ずつ等）
├── nation_sim/                   # シミュレーションエンジン（参考）
└── ラノベファンタジー/Tools/history_sim_context_builder.py
```

## world_analytics.json

### レコード構造（1ターン = 約1ヶ月）

```json
{
  "turn": 1,
  "year": 1000,
  "month": 1,
  "cycle_phase": "active",
  "cycle_position": 1,
  "alive_countries": 131,
  "total_human_power": 19340.01,
  "average_monster_threat": 1.232
}
```

### カバー範囲（現行ファイル）

- `turn`: 1 〜 1200（月次）
- `year`: 1000 〜 1099（**analytics 100暦年**）
- 叙事換算: **0〜990年目付近**（`year 1099` の終端 ≒ 叙事 990〜1000）
- **国家個別イベントは含まない**（世界全体指標のみ）

### 10倍スケールと macroTurn の対応

歴史書は analytics を **10倍に時間伸長** したナラティブである。

| analytics | 叙事（歴史書） | macroTurn |
|-----------|--------------|-----------|
| year 1000（turn 1-12） | 1〜10年目 | 1 |
| year 1004（turn 49-60） | 41〜50年目 | 5 |
| year 1045（turn 541-552） | 451〜460年目 | 46 |
| year 1099（turn 1189-1200） | 991〜1000年目 | 100 |

```
analyticsTurnStart  = (macroTurn - 1) × 12 + 1
analyticsTurnEnd    = macroTurn × 12
analyticsYear       = 1000 + macroTurn - 1
叙事年Start         = (macroTurn - 1) × 10 + 1
叙事年End           = macroTurn × 10
叙事年 → analytics  = year ≈ 1000 + (叙事年 - 1) // 10
```

例: macroTurn 5 → analytics turn 49-60, year 1004, 叙事年 41〜50  
例: macroTurn 46 → analytics turn 541-552, year 1045, 叙事年 451〜460（滅亡は460年目）

**macroTurn 1〜100 はすべて analytics と突合可能**（世界指標）。  
国家固有の出来事・技術名・滅亡経緯は **歴史棚が主ソース**。

## 歴史棚（ナラティブ）

### ファイル種別

| 種別 | 内容 |
|------|------|
| `歴史書その1.md` 等 | 章単位の教科書体（領域・国家002/001の大局） |
| `歴史書その6.txt` 以降 | **国家ブロック**（`【国家071（領域・中央）：生存】` 形式） |

### 国家エントリの典型パターン

```
【国家072（領域・西）：滅亡（第3章：460年目 / ターン46にて崩壊）】
…本文（固有技術名・大国001との関係・滅亡経緯）…
```

### 抽出すべき情報

- 生存 / 滅亡 / 新生
- 滅亡 macroTurn・叙事年（例: ターン46 = 460年目）
- 領域（東西南北中央）
- 固有技術・戦術名（「」で囲まれた語）
- 関連大国（内部参照のみ。UIでは「最西端の統合王国」等へ変換）

### 叙事と analytics の年号（10倍スケール）

- 歴史書「10年目」≈ analytics `year: 1000` の終わり / `year: 1001` の始まり
- 歴史書「460年目」≈ analytics `year: 1045`（macroTurn 46）
- **叙事年 Y** → analytics 検索: `year = 1000 + (Y - 1) // 10`
- 出力 JSON の `timestamp.year` は **叙事年** を使う（analytics の year をそのまま入れない）

## 既存ゲーム連携 ID（unlockRewards 用）

`ラノベファンタジー/Assets/Resources/GameMasters/GameMasters_Sample.json` 参照:

- Job: `JOB_HISTORIAN`, `JOB_BLACKSMITH`, `JOB_MIMICRY_HUNTER`, …
- Magic: `MAGIC_CRYSTAL_LANCE`, `MAGIC_FIRE_SPARK`, `MAGIC_STALKER_GLINT`
- Recipe: `WEAPON_CHITIN_EDGE`, `POTION_HIGH_REMEDY`（`CraftRecipeIds`）
- Skill: `SKILL_HISTORY_ANALYSIS`, `SKILL_COMBAT_SWORD`, …

**存在しない ID は捏造しない。**

## world_history.json（任意）

`history_visualizer` からダウンロード可能な詳細イベントログ。リポジトリに無い場合は `nation_sim` 出力の `history_log.json` を代替参考にできる。

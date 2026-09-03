# JSON Schema リファレンス（History Simulation）

## ルート構造（JsonUtility 用）

```json
{
  "simulationEvents": [ { "...": "..." } ]
}
```

## 1イベントの必須フィールド

| フィールド | 型 | 説明 |
|-----------|-----|------|
| `eventId` | string | `SIM_EVT_[NATION_ID]_[EVENT_TYPE]_[YEAR]` |
| `macroTurn` | int | 歴史書ターン（1〜100） |
| `timestamp` | object | `year`, `month`, `day`, `hour` |
| `targetNationId` | string | 例: `NATION_071`（システム内部のみ） |
| `relatedNationId` | string | 隣接・関連国。無ければ `""` |
| `eventCategory` | string | `monster` / `domestic` / `conflict` / `lifecycle` |
| `eventType` | string | 下表7種のいずれか |
| `logMessageTemplate` | string | UI表示用（国家番号禁止） |
| `isHistoricalCollapseRoute` | bool | 有事/平時の排他スイッチ |
| `evolutionSystemType` | string | `Ritual` / `Inspiration` / `Recipe` |

### eventType 一覧

`MonsterAttack` | `MonsterRepelled` | `MagicPioneering` | `ShieldFortification` | `LocalConflict` | `NationCollapse` | `NationBirth`

## 排他タイムライン

### 有事 `isHistoricalCollapseRoute: true`

`decayTimeline` **必須**。3フェーズすべて充填:

- `phase1_premonition` — 滅亡10〜7年前
- `phase2_poverty` — 6〜3年前
- `phase3_critical` — 2年前〜数日前（工房猶予100s→20s等）

`prosperityTimeline` キーは **出力しない**。

### 平時 `isHistoricalCollapseRoute: false`

`prosperityTimeline` **必須**。3フェーズすべて充填:

- `phase1_accumulation` — 1〜3年目
- `phase2_pioneering` — 4〜7年目
- `phase3_perfection` — 8〜10年目

`decayTimeline` キーは **出力しない**。

## 3大モード

### historicalMode

```json
"historicalMode": {
  "timeLockedHourOffset": 24,
  "forcedFlagsOnTrigger": ["HIST_0840_11_03_NATION_COLLAPSE"]
}
```

### alternativeMode

```json
"alternativeMode": {
  "causalPrerequisites": {
    "requiredLogType": "MonsterEradicatedCount",
    "requiredValue": 10
  },
  "unlockRewards": {
    "alternativeFlag": "ALT_NATION_071_SHIELD_REINFORCED",
    "alternativeLogMessage": "改変成功ログ（国家番号禁止）",
    "alternativeLore": "改変後世界線（国家番号禁止）"
  }
}
```

### mmoMode

```json
"mmoMode": {
  "zoneId": "ZONE_EAST_FORGE_BELT",
  "recommendedLevel": 8,
  "progressTriggerFlag": "MMO_ZONE_EAST_FORGE_STEP_02"
}
```

## フラグ命名

| モード | 形式 |
|--------|------|
| 史実 | `HIST_[YEAR]_[MONTH]_[DAY]_[EVENT_TYPE]` |
| 改変 | `ALT_[NATION_ID]_[PARAM]_[STATUS]` |
| MMO | `MMO_[ZONE_ID]_[STEP_ID]` |

## evolutionSystemType 選択

| macroTurn | 章 | 値 |
|-----------|-----|-----|
| 1〜40 | 第1〜2章 | `Ritual` |
| 41〜60 | 第3章 | `Inspiration` |
| 61〜100 | 第4〜5章 | `Recipe` |

## イベント種別 → パラメータ影響

| eventType | ゲーム内影響の目安 |
|-----------|------------------|
| MonsterAttack | 領土・軍事・経済↓、異形出没会話 |
| MonsterRepelled | 軍事微減、防衛成功 |
| MagicPioneering | 領土+1〜5、開拓緑化 |
| ShieldFortification | 国内脅威因子↓ |
| LocalConflict | 隣国戦、敗者領土・軍事↓ |
| NationCollapse | 無主地化、wilderness |
| NationBirth | 新生国ログ |

## 出力前チェックリスト

- [ ] decay / prosperity 排他
- [ ] UI文字列に国家番号なし
- [ ] eventType が7種のいずれか
- [ ] forcedFlags / alternativeFlag / progressTriggerFlag が命名規約どおり
- [ ] 敵言及は魔獣・異形のみ
- [ ] `simulationEvents` ラップあり

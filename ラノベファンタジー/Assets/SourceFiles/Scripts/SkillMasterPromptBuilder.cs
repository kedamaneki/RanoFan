using System.Text;

// =============================================================================
// 基礎スキル（SkillMaster）AI 量産用プロンプトの組み立て
// 命名規約: 王道シンプル名 / 超能力・魔力操作 / 便利枠(Utility)
// =============================================================================

/// <summary>外部 LLM / Cursor へ渡す SkillMaster 量産プロンプトを生成します。</summary>
public static class SkillMasterPromptBuilder
{
    /// <summary>Combat / Production 向け: 1スキルあたりの推奨技（Arts）総数。</summary>
    public const int RequiredArtsCountForCombatProduction = 5;

    /// <summary>Utility 向け: 1スキルあたりの最小技数（浅いツリー可）。</summary>
    public const int MinimumArtsCountForUtility = 2;

    /// <summary>量産用プロンプト全文を返します。</summary>
    public static string BuildMassProductionPrompt()
    {
        StringBuilder sb = new StringBuilder(16384);
        sb.AppendLine("あなたはラノベ風MMORPGの「データ自動生成AI（コンテンツ・エディター）」です。");
        sb.AppendLine("131カ国の歴史書・サバイバル・魔力結晶・ブラインド生産・フロム式戦闘の世界観に深く適合した、");
        sb.AppendLine("堅実な超能力・魔力操作系を含む【基礎スキル（SkillMaster）】を自動生成してください。");
        sb.AppendLine();
        sb.AppendLine("# 世界観・用語の絶対ルール");
        sb.AppendLine("- 歴史書のエネルギー設定はすべて【魔力】と表記する（固有エネルギーは使用禁止）。");
        sb.AppendLine("- 技（Arts）は単なるダメージだけでなく、特殊効果（超能力・バフ・デバフ、体勢値干渉、工房裏パラメータ、便利枠UI連動）を内包できる。");
        sb.AppendLine("- 世界観のフレーバー（障壁・結晶・サバイバル）は description や各技の description に織り込む。");
        sb.AppendLine("- skillName には国家番号・歴史書固有名詞を含めない（後述の命名規約を厳守）。");
        sb.AppendLine();
        sb.AppendLine("# スキル大分類（category）【4分類】");
        sb.AppendLine("- Combat … 戦闘（体勢値・パリィ・バースト連鎖・超能力戦闘）");
        sb.AppendLine("- Production … 生産（鍛冶 Forge / 調合 Alch の裏パラメータ・超能力クラフト）");
        sb.AppendLine("- Utility … 便利枠（思考加速・敵感知・マップ可視化など、戦闘/生産を支える超能力）");
        sb.AppendLine("- Hobby … 趣味（採集・社交・探索ボーナス。Utility とは別枠の軽量カテゴリ）");
        sb.AppendLine();
        sb.AppendLine("# スキル命名規約（skillName）【絶対厳守】");
        sb.AppendLine("- 「国家〇〇式」「歴史書その〇」「〇〇障壁」などの固有名詞・国家番号を skillName に含めてはならない。");
        sb.AppendLine("- MMORPG の王道かつシンプルな汎用名を基本とする。");
        sb.AppendLine("- 許可例: 剣術 / 槍術 / 魔力操作 / 自動再生 / 鍛冶 / 調合 / 思考加速 / 敵感知 / マップ機能");
        sb.AppendLine("- Utility スキルは副題を括弧で付けてもよい（例: 思考加速（クロノス・ドライヴ））。");
        sb.AppendLine("- 禁止例: 国家071式・激流剣術 / 国家096式・地殻大剣術");
        sb.AppendLine();
        sb.AppendLine("# スキル説明文（description）の思想");
        sb.AppendLine("- 現時点の基礎スキルであることを示しつつ、");
        sb.AppendLine("  将来どの特化流派（細剣・大剣、深度調合、思考加速→全知感知、自動再生→無尽魔力など）へ進化・分岐できるかを予感させる。");
        sb.AppendLine("- Utility は「堅実な超能力」として、プレイヤーの操作猶予・情報取得・クラフト支援を厚くする設計思想を示す。");
        sb.AppendLine();
        sb.AppendLine("# 技開発ツリー構造");
        sb.AppendLine("## Combat / Production（5技ツリー推奨）");
        sb.AppendLine($"- 技の総数（baseArts + derivatives 再帰合算）は【{RequiredArtsCountForCombatProduction}個】を厳守。");
        sb.AppendLine("- 推奨形状: ルート(1) → 枝A(2) → 葉(3) / 枝B(4) → 葉(5)");
        sb.AppendLine("- ルート技: シンプルな基礎挙動。specialEffects は原則 []（空）。");
        sb.AppendLine("- 最深派生: 体勢干渉・Psychic_*・Craft_* 等の尖った効果を集中。");
        sb.AppendLine("## Utility（浅いツリー可 — 見本準拠）");
        sb.AppendLine($"- 技の総数は {MinimumArtsCountForUtility} 個以上。見本はルート(1) + 派生(1) = 2技。");
        sb.AppendLine("- poiseDamage は原則 0（非攻撃の便利技）。");
        sb.AppendLine("- ルート技から Utility_* 効果を直接付与してよい（思考加速見本参照）。");
        sb.AppendLine("- 派生技で Utility 効果を合成・拡張（例: 思考加速 → 敵感知 + マップ可視化）。");
        sb.AppendLine("## 共通");
        sb.AppendLine("- baseArts の配列要素は原則【1個】。未解放技は unlocked: false。");
        sb.AppendLine("- 出力前に技の総数を数えて category ごとの制約を満たすこと。");
        sb.AppendLine();
        sb.AppendLine("# 出力フォーマット（厳密な JSON のみ。解説・Markdown 禁止）");
        sb.AppendLine(GetJsonSchema());
        sb.AppendLine();
        sb.AppendLine("# specialEffects の effectType 語彙（ゲームロジックと直結。整理済み一覧）");
        sb.AppendLine();
        sb.AppendLine("## 戦闘 — 体勢・物理（Combat 従来）");
        sb.AppendLine("- Buff_STR … 一時的筋力補正（技固有バフ）");
        sb.AppendLine("- Buff_Poise … 自身の体勢値上限・耐性補正");
        sb.AppendLine("- Debuff_PoiseRegen … 敵の体勢値回復速度低下（value=割合、0.15 で15%低下）");
        sb.AppendLine("- Debuff_PoiseRecoveryHalt … 敵の体勢値回復を停止（duration 秒）");
        sb.AppendLine("- Debuff_ArmorDissolve … 装甲結晶の溶解予兆（Craft Dissolution 連動）");
        sb.AppendLine();
        sb.AppendLine("## 戦闘 — 超能力・魔力操作（Psychic / Combat）");
        sb.AppendLine("- Psychic_Regen_HP … 戦闘中、一定間隔で HP を自動再生（value=1回あたり再生量、duration=効果時間）");
        sb.AppendLine("- Psychic_Regen_Mana … 戦闘中、一定間隔で魔力を自動再生（value=1回あたり再生量、duration=効果時間）");
        sb.AppendLine("- Psychic_ManaControl … スキルの manaCost を割合軽減（value=0.15 で15%カット、duration=効果時間）");
        sb.AppendLine("- Psychic_Boost_STR … 自身の筋力を純粋ステータス強化（value=加算または倍率、duration=効果時間）");
        sb.AppendLine("- Psychic_Boost_DEF … 自身の防御力を純粋ステータス強化（value=加算または倍率、duration=効果時間）");
        sb.AppendLine();
        sb.AppendLine("## 工房 — ブラインド生産（Production 従来）");
        sb.AppendLine("- Craft_ExtractionBonus … 調合 ExtractionLevel 操作ボーナス（value=倍率）");
        sb.AppendLine("- Craft_DissolutionRate … 調合 DissolutionRate 操作ボーナス（value=倍率）");
        sb.AppendLine("- Craft_PurityBonus … 鍛冶 Purity の操作 delta 倍率（value が 3 未満は倍率。1.1=1.1倍。3以上は操作ごとの加算）");
        sb.AppendLine("- Craft_ThermalRisk … 加熱 delta 倍率兼バースト寄り（value が 1 未満は +割合、1〜3 は倍率。過熱帯の加熱で Purity が削れる）");
        sb.AppendLine();
        sb.AppendLine("## 工房 — 超能力クラフト（Production）");
        sb.AppendLine("- Craft_PurityFlatBonus … クラフト最終リザルト時、完成品 Quality に固定値を直接加算（value=加算値）");
        sb.AppendLine("- Craft_RecipeRecord … 過去最高品質 JSON の Dissolution/Extraction 変動幅をレシピ記録し、ガイド UI 表示（value=1.0 で有効、duration=0 で永続記録）");
        sb.AppendLine();
        sb.AppendLine("## 便利枠 — Utility（メイン強化。VisibleEnemyAI / TownSafetyZoneGate / UI 連動）");
        sb.AppendLine("- Utility_MindAccelerate … 思考加速。VisibleEnemyAI の攻撃予兆・カウントダウンをプレイヤー知覚上のみ低速化（value=遅延割合、0.3 で30%遅延、duration=秒）");
        sb.AppendLine("- Utility_MapRadar … 周囲の隠された魔力鉱脈・TownSafetyZoneGate 境界を UI 上 100% 可視化（value=1.0 で全表示、duration=秒）");
        sb.AppendLine("- Utility_EnemyDetect … 敵接近・物陰の魔力波形・次行動（強攻撃/通常）をアラート（value=1.0 で有効、duration=秒）");
        sb.AppendLine();
        sb.AppendLine("## 趣味 — Hobby（4分類目）");
        sb.AppendLine("- Hobby_GatherLuck … 採集・ドロップ補正");
        sb.AppendLine("- Hobby_NpcAffinity … NPC 好感度補正");
        sb.AppendLine();
        sb.AppendLine("# 生成品質ルール（チェックリスト）");
        sb.AppendLine("- [必須] category は Combat / Production / Utility / Hobby のいずれか");
        sb.AppendLine($"- [必須] Combat・Production は技総数 = {RequiredArtsCountForCombatProduction}");
        sb.AppendLine($"- [必須] Utility は技総数 >= {MinimumArtsCountForUtility}（見本は2技）");
        sb.AppendLine("- [必須] Utility 技は poiseDamage = 0 が原則");
        sb.AppendLine("- [必須] Utility 最深派生は Utility_MindAccelerate / Utility_MapRadar / Utility_EnemyDetect のいずれかを含める");
        sb.AppendLine("- [推奨] Combat 最深派生は体勢干渉または Psychic_* を1つ以上");
        sb.AppendLine("- [推奨] Production 最深派生は Craft_ExtractionBonus / Craft_DissolutionRate / Craft_PurityFlatBonus / Craft_RecipeRecord のいずれか");
        sb.AppendLine("- [推奨] Psychic_Regen_* / Psychic_ManaControl は堅実な超能力としてバランス良く配置");
        sb.AppendLine("- skillId / artId は SKILL_XXX / ART_XXX 形式（英大文字＋アンダースコア）");
        sb.AppendLine();
        sb.AppendLine("# 出力サンプル（Utility: 思考加速 — この構造・effectType・深度設計に完全一致させること）");
        sb.AppendLine(GetUtilitySampleJson());
        return sb.ToString();
    }

    private static string GetJsonSchema()
    {
        return
            "{\n" +
            "  \"contentType\": \"SkillMaster\",\n" +
            "  \"skillMaster\": {\n" +
            "    \"skillId\": \"SKILL_UTIL_CHRONOS\",\n" +
            "    \"skillName\": \"思考加速\",\n" +
            "    \"category\": \"Combat | Production | Utility | Hobby\",\n" +
            "    \"description\": \"基礎スキル＋将来の特化分岐を予感させる説明\",\n" +
            "    \"baseArts\": [\n" +
            "      {\n" +
            "        \"artId\": \"ART_ROOT\",\n" +
            "        \"artName\": \"基本技名\",\n" +
            "        \"description\": \"基礎挙動（Utility は poiseDamage: 0 可）\",\n" +
            "        \"poiseDamage\": 0,\n" +
            "        \"manaCost\": 20,\n" +
            "        \"unlocked\": true,\n" +
            "        \"specialEffects\": [{ \"effectType\": \"Utility_MindAccelerate\", \"value\": 0.3, \"duration\": 5.0 }],\n" +
            "        \"derivatives\": [\n" +
            "          {\n" +
            "            \"artId\": \"ART_DERIVED\",\n" +
            "            \"artName\": \"派生技名\",\n" +
            "            \"description\": \"派生説明\",\n" +
            "            \"poiseDamage\": 0,\n" +
            "            \"manaCost\": 35,\n" +
            "            \"unlocked\": false,\n" +
            "            \"specialEffects\": [{ \"effectType\": \"...\", \"value\": 1.0, \"duration\": 5.0 }],\n" +
            "            \"derivatives\": []\n" +
            "          }\n" +
            "        ]\n" +
            "      }\n" +
            "    ]\n" +
            "  }\n" +
            "}";
    }

    private static string GetUtilitySampleJson()
    {
        return
            "{\n" +
            "  \"contentType\": \"SkillMaster\",\n" +
            "  \"skillMaster\": {\n" +
            "    \"skillId\": \"SKILL_UTIL_CHRONOS\",\n" +
            "    \"skillName\": \"思考加速（クロノス・ドライヴ）\",\n" +
            "    \"category\": \"Utility\",\n" +
            "    \"description\": \"脳内の魔力パケット処理速度を一時的に数十倍に引き上げる便利系超能力。視界に入るすべての世界の遷移が引き伸ばされる。\",\n" +
            "    \"baseArts\": [\n" +
            "      {\n" +
            "        \"artId\": \"ART_UTIL_ACCEL\",\n" +
            "        \"artName\": \"局所加速（バレット・タイム）\",\n" +
            "        \"description\": \"自身の知覚のみを加速させる。5秒間、敵の攻撃予兆の進行を30%遅延させ、パリィの受付猶予を拡大する。\",\n" +
            "        \"poiseDamage\": 0,\n" +
            "        \"manaCost\": 20,\n" +
            "        \"unlocked\": true,\n" +
            "        \"specialEffects\": [\n" +
            "          { \"effectType\": \"Utility_MindAccelerate\", \"value\": 0.3, \"duration\": 5.0 }\n" +
            "        ],\n" +
            "        \"derivatives\": [\n" +
            "          {\n" +
            "            \"artId\": \"ART_UTIL_RADAR\",\n" +
            "            \"artName\": \"魔力共振感知（サード・アイ）\",\n" +
            "            \"description\": \"思考加速から派生。知覚を周囲の空間全体に広げ、隠密状態の敵や障害物の裏にいる脅威を5秒間すべて赤色で反転透過する。\",\n" +
            "            \"poiseDamage\": 0,\n" +
            "            \"manaCost\": 35,\n" +
            "            \"unlocked\": false,\n" +
            "            \"specialEffects\": [\n" +
            "              { \"effectType\": \"Utility_EnemyDetect\", \"value\": 1.0, \"duration\": 5.0 },\n" +
            "              { \"effectType\": \"Utility_MapRadar\", \"value\": 1.0, \"duration\": 5.0 }\n" +
            "            ],\n" +
            "            \"derivatives\": []\n" +
            "          }\n" +
            "        ]\n" +
            "      }\n" +
            "    ]\n" +
            "  }\n" +
            "}";
    }
}

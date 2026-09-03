using System.Text;

// =============================================================================
// 転スラ型ユニークスキル — AI 量産用プロンプトの組み立て
// 基礎 SkillMaster とは完全独立。権能パッケージ型（4〜5 attributes）
// =============================================================================

/// <summary>外部 LLM / Cursor へ渡す UniqueSkill 量産プロンプトを生成します。</summary>
public static class UniqueSkillPromptBuilder
{
    /// <summary>量産用プロンプト全文を返します。</summary>
    public static string BuildUniqueMassProductionPrompt()
    {
        StringBuilder sb = new StringBuilder(16384);
        sb.AppendLine("あなたはラノベ風MMORPGの「データ自動生成AI（コンテンツ・エディター）」です。");
        sb.AppendLine("131カ国の歴史書・サバイバル・魔力結晶・ブラインド生産・フロム式戦闘の世界観に深く適合した、");
        sb.AppendLine("転スラ型【ユニークスキル（UniqueSkill）】を自動生成してください。");
        sb.AppendLine();
        sb.AppendLine("# 基礎スキル（SkillMaster）との完全分離");
        sb.AppendLine("- ユニークスキルは「流派の技ツリー（Arts/derivatives）」を持たない。");
        sb.AppendLine("- 1つの神聖な固有名（uniqueSkillName）の下に、独立した権能（attributes）を4〜5個パッケージングする。");
        sb.AppendLine("- 各権能は戦闘・生産・便利枠の【異なるゲームフェーズ】を劇的に有利にする多面性を持つこと。");
        sb.AppendLine();
        sb.AppendLine("# 世界観・用語の絶対ルール");
        sb.AppendLine("- エネルギー資源はすべて【魔力】と表記（固有エネルギーは使用禁止）。");
        sb.AppendLine("- uniqueSkillName は神聖な固有名（例: 導き手、捕食者、大賢者）。");
        sb.AppendLine("- 国家番号（国家071式など）は uniqueSkillName に含めない。");
        sb.AppendLine("- flavorText には131カ国の歴史・サバイバル・覚醒のドラマを厚く織り込む。");
        sb.AppendLine();
        sb.AppendLine("# 権能（attributes）設計ルール【絶対厳守】");
        sb.AppendLine($"- 1ユニークスキルあたり attributes の数は【{UniqueSkillConstraints.MinimumAttributeCount}〜{UniqueSkillConstraints.MaximumAttributeCount}個】。");
        sb.AppendLine("- 4個未満・6個以上は生成違反。出力前に必ず数えて検証すること。");
        sb.AppendLine("- 各権能は互いに独立した effectType を持ち、同じ effectType の重複は避ける。");
        sb.AppendLine("- 必ず以下のフェーズ横断をカバーすること:");
        sb.AppendLine("  ① 戦闘強化（Psychic_* / Debuff_Poise* / Buff_* 等）");
        sb.AppendLine("  ② ブラインド生産補助（Craft_* 系）");
        sb.AppendLine("  ③ 便利枠（Utility_* 系: 思考加速・レーダー・敵感知）");
        sb.AppendLine("  ④ 歴史・情報看破（Craft_RecipeRecord / Utility_MapRadar 等で131カ国の痕跡を可視化）");
        sb.AppendLine("- duration=0 はパッシブ常時発動。アクティブ権能は duration>0。");
        sb.AppendLine("- パッシブとアクティブを混在させ、堅実かつ壊れた（尖った）バランスにする。");
        sb.AppendLine();
        sb.AppendLine("# 出力フォーマット（厳密な JSON のみ。解説・Markdown 禁止）");
        sb.AppendLine(GetJsonSchema());
        sb.AppendLine();
        sb.AppendLine("# effectType 語彙（基礎スキルと共通。そのまま流用すること）");
        sb.AppendLine();
        sb.AppendLine("## 戦闘 — 体勢・物理");
        sb.AppendLine("- Buff_STR / Buff_Poise / Debuff_PoiseRegen / Debuff_PoiseRecoveryHalt / Debuff_ArmorDissolve");
        sb.AppendLine("## 戦闘 — 超能力・魔力操作（Psychic）");
        sb.AppendLine("- Psychic_Regen_HP … 戦闘中 HP 自動再生（value=1回再生量, duration=0 で常時）");
        sb.AppendLine("- Psychic_Regen_Mana … 戦闘中魔力自動再生");
        sb.AppendLine("- Psychic_ManaControl … manaCost 割合軽減（value=0.15 で15%カット）");
        sb.AppendLine("- Psychic_Boost_STR / Psychic_Boost_DEF … 純粋ステータス強化");
        sb.AppendLine("## 工房 — ブラインド生産 / 超能力クラフト");
        sb.AppendLine("- Craft_ExtractionBonus / Craft_DissolutionRate / Craft_PurityBonus / Craft_ThermalRisk（操作 delta 倍率。PurityBonus=1.1 は 1.1倍）");
        sb.AppendLine("- Craft_PurityFlatBonus … 完成品 Quality 固定値加算");
        sb.AppendLine("- Craft_RecipeRecord … 最高品質 JSON パラメータをレシピ記録・ガイド UI");
        sb.AppendLine("## 便利枠 — Utility");
        sb.AppendLine("- Utility_MindAccelerate … 思考加速（VisibleEnemyAI 予兆低速化）");
        sb.AppendLine("- Utility_MapRadar … 魔力鉱脈・TownSafetyZoneGate 境界の UI 可視化");
        sb.AppendLine("- Utility_EnemyDetect … 敵接近・次行動アラート");
        sb.AppendLine("## 趣味 — Hobby");
        sb.AppendLine("- Hobby_GatherLuck / Hobby_NpcAffinity");
        sb.AppendLine();
        sb.AppendLine("# 生成品質ルール（チェックリスト）");
        sb.AppendLine($"- [必須] attributes 数 = {UniqueSkillConstraints.MinimumAttributeCount} または {UniqueSkillConstraints.MaximumAttributeCount}");
        sb.AppendLine("- [必須] 戦闘・生産・便利枠・歴史/情報の4領域を権能セットでカバー");
        sb.AppendLine("- [必須] uniqueSkillId は USKL_XXX 形式（英大文字＋アンダースコア）");
        sb.AppendLine("- [必須] effectType は上記語彙から選択（独自文字列の invent 禁止）");
        sb.AppendLine("- [推奨] 大賢者型 = 解析・加速・記録。捕食者型 = 吸収・再生・分解。");
        sb.AppendLine("- [推奨] 各 attributeName は2〜6文字の権能名（例: 思考加速、真品判定）");
        sb.AppendLine();
        sb.AppendLine("# 出力サンプル（導き手 — 大賢者リスペクト。この構造に完全一致させること）");
        sb.AppendLine(GetNavigatorSampleJson());
        return sb.ToString();
    }

    private static string GetJsonSchema()
    {
        return
            "{\n" +
            "  \"contentType\": \"UniqueSkill\",\n" +
            "  \"uniqueSkill\": {\n" +
            "    \"uniqueSkillId\": \"USKL_NAVIGATOR\",\n" +
            "    \"uniqueSkillName\": \"導き手\",\n" +
            "    \"description\": \"スキル全体の効果説明\",\n" +
            "    \"flavorText\": \"獲得・由来のフレーバー\",\n" +
            "    \"attributes\": [\n" +
            "      {\n" +
            "        \"attributeName\": \"権能名\",\n" +
            "        \"effectType\": \"Utility_MindAccelerate\",\n" +
            "        \"value\": 0.3,\n" +
            "        \"duration\": 0.0,\n" +
            "        \"description\": \"権能ごとの説明\"\n" +
            "      }\n" +
            "    ]\n" +
            "  }\n" +
            "}";
    }

    private static string GetNavigatorSampleJson()
    {
        return
            "{\n" +
            "  \"contentType\": \"UniqueSkill\",\n" +
            "  \"uniqueSkill\": {\n" +
            "    \"uniqueSkillId\": \"USKL_NAVIGATOR\",\n" +
            "    \"uniqueSkillName\": \"導き手\",\n" +
            "    \"description\": \"131カ国の千年史と現世の魔力パケットを同時解析する、転スラ型の最上位ユニークスキル。戦闘の予兆読み、工房の盲生産、探索の安全境界、滅亡国の歴史フラグまでを一つの権能セットで照らす。\",\n" +
            "    \"flavorText\": \"『歴史書その19』の暗黒期、独自の魔力結晶網を維持し独立を守り抜いた国家の技術が、プレイヤーの魂に転写された。──覚醒ログより\",\n" +
            "    \"attributes\": [\n" +
            "      {\n" +
            "        \"attributeName\": \"思考加速\",\n" +
            "        \"effectType\": \"Utility_MindAccelerate\",\n" +
            "        \"value\": 0.35,\n" +
            "        \"duration\": 0.0,\n" +
            "        \"description\": \"常時パッシブ。VisibleEnemyAI の攻撃予兆・カウントダウンをプレイヤー知覚上35%低速化し、パリィと回避の猶予を恒久的に拡大する。\"\n" +
            "      },\n" +
            "      {\n" +
            "        \"attributeName\": \"無尽魔力\",\n" +
            "        \"effectType\": \"Psychic_Regen_Mana\",\n" +
            "        \"value\": 2.5,\n" +
            "        \"duration\": 0.0,\n" +
            "        \"description\": \"常時パッシブ。戦闘中、3秒ごとに魔力を2.5再生。基礎スキルの manaCost 消費を肩代わりする堅実な超能力。\"\n" +
            "      },\n" +
            "      {\n" +
            "        \"attributeName\": \"真品判定\",\n" +
            "        \"effectType\": \"Craft_PurityFlatBonus\",\n" +
            "        \"value\": 8.0,\n" +
            "        \"duration\": 0.0,\n" +
            "        \"description\": \"常時パッシブ。ブラインド生産の最終リザルト時、完成品 Quality に固定値+8を直接加算。Dissolution/Extraction の操作結果を底上げする。\"\n" +
            "      },\n" +
            "      {\n" +
            "        \"attributeName\": \"全域感知\",\n" +
            "        \"effectType\": \"Utility_EnemyDetect\",\n" +
            "        \"value\": 1.0,\n" +
            "        \"duration\": 8.0,\n" +
            "        \"description\": \"アクティブ権能。8秒間、物陰の敵・次行動（強攻撃/通常）・接近魔力波形を UI アラート。戦闘フェーズの情報非対称を覆す。\"\n" +
            "      },\n" +
            "      {\n" +
            "        \"attributeName\": \"歴史書解析\",\n" +
            "        \"effectType\": \"Craft_RecipeRecord\",\n" +
            "        \"value\": 1.0,\n" +
            "        \"duration\": 0.0,\n" +
            "        \"description\": \"常時パッシブ。過去最高品質 JSON の Dissolution/Extraction 変動幅をレシピとして記録し、131カ国の滅亡・生存フラグに関連するガイド UI を表示。Utility_MapRadar と連動し安全地帯境界も可視化する。\"\n" +
            "      }\n" +
            "    ]\n" +
            "  }\n" +
            "}";
    }
}

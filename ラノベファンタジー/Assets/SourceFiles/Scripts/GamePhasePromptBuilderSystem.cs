using System;
using System.Collections.Generic;
using System.Text;

#if UNITY_5_3_OR_NEWER
using UnityEngine;
#endif

// =============================================================================
// GamePhase 対応型 AI プロンプト自動組み立て
// GamePhasePromptBuilderSystemTest.Main() で検証。
// 連携: AIGeneratorConnector / PlayerHistoryLog / SkillData / PromptBuilder
// =============================================================================

/// <summary>AI 指示を切り替えるゲームフェーズ（大前提モード）。</summary>
public enum GamePhase
{
    Training,
    Fusion,
    Crafting,
    Crisis
}

/// <summary>
/// 融合（Fusion）によって生まれる合成スキルのメリット・デメリット構造。
/// </summary>
public sealed class CompositeSkillData
{
    public string SkillName { get; set; } = string.Empty;
    public string MeritEffect { get; set; } = string.Empty;
    public string DemeritEffect { get; set; } = string.Empty;

    public void AppendTradeoffSection(StringBuilder sb)
    {
        sb.AppendLine("【合成スキル・トレードオフ構造（必須）】");
        sb.AppendLine($"- スキル名: {SkillName}");
        sb.AppendLine($"- メリット効果: {MeritEffect}");
        sb.AppendLine($"- デメリット効果: {DemeritEffect}");
    }
}

/// <summary>フェーズごとの制約命令と JSON スキーマ定義。</summary>
public readonly struct GamePhasePromptDefinition
{
    public string PhaseLabel { get; }
    public string ConstraintInstruction { get; }
    public string JsonSchema { get; }

    public GamePhasePromptDefinition(string phaseLabel, string constraintInstruction, string jsonSchema)
    {
        PhaseLabel = phaseLabel ?? string.Empty;
        ConstraintInstruction = constraintInstruction ?? string.Empty;
        JsonSchema = jsonSchema ?? string.Empty;
    }
}

/// <summary>各 GamePhase のプロンプト文脈と JSON スキーマを保持します。</summary>
public static class GamePhasePromptRegistry
{
    private static readonly IReadOnlyDictionary<GamePhase, GamePhasePromptDefinition> Definitions =
        BuildDefinitions();

    public static GamePhasePromptDefinition Get(GamePhase phase)
    {
        if (Definitions.TryGetValue(phase, out GamePhasePromptDefinition definition))
        {
            return definition;
        }

        throw new ArgumentOutOfRangeException(nameof(phase), phase, null);
    }

    private static IReadOnlyDictionary<GamePhase, GamePhasePromptDefinition> BuildDefinitions()
    {
        return new Dictionary<GamePhase, GamePhasePromptDefinition>
        {
            [GamePhase.Training] = new GamePhasePromptDefinition(
                "Training（技術修練）",
                "対応する基礎スキルが未習得であるため、プレイヤーの蓄積履歴にふさわしい" +
                "【基礎スキル（BaseSkill）】を1つ目覚めさせるデータ（JSON）を生成せよ。" +
                "現実の職人技の工程やファンタジーの基礎理論を、それらしい専門用語で flavorText に反映すること。",
                TrainingJsonSchema),

            [GamePhase.Fusion] = new GamePhasePromptDefinition(
                "Fusion（術式融合）",
                "捧げられたスキルを融合した【合成スキル】を生成せよ。" +
                "安易な合成を防ぐため、特定の条件下で超強力になるメリットと、" +
                "別の条件下で致命的な弱体化が入るデメリット（例：他人の武器を使うと極端に弱くなる等）のペアを" +
                "必ず数値付きで考案せよ。meritEffect / demeritEffect は条件と数値を明示すること。",
                FusionJsonSchema),

            [GamePhase.Crafting] = new GamePhasePromptDefinition(
                "Crafting（生産・解体）",
                "複雑な工程は省き、純粋に素材の組み合わせと品質（Quality）のみを参照してアイテムを生成せよ。" +
                "レシピ要望モードの場合は、足りないレシピを補う新しいレシピデータを生成せよ。",
                CraftingJsonSchema),

            [GamePhase.Crisis] = new GamePhasePromptDefinition(
                "Crisis（ピンチ覚醒）",
                "死線を超えた瞬間にのみ発動する緊急モードとして、ユニークスキル獲得用データを生成せよ。" +
                "過去の行動履歴を最大級にリスペクトし、既存の枠を破壊する唯一無二の絶対効果を考案すること。" +
                "スキル名は転スラ風の【漢字3〜4文字（読みカタカナ）】を厳守し、有名作品の丸パクリは禁止。",
                CrisisJsonSchema)
        };
    }

    private const string TrainingJsonSchema =
        "{\n" +
        "  \"generationType\": \"baseSkill\",\n" +
        "  \"phase\": \"Training\",\n" +
        "  \"baseSkill\": {\n" +
        "    \"skillID\": \"skill_muddy_thrust\",\n" +
        "    \"skillName\": \"泥臭き連突の型\",\n" +
        "    \"description\": \"何百回もの踏み込みが形になった突きの基礎理論\",\n" +
        "    \"targetBonus\": \"speedBonus\",\n" +
        "    \"bonusValue\": 3\n" +
        "  }\n" +
        "}";

    private const string FusionJsonSchema =
        "{\n" +
        "  \"generationType\": \"skill\",\n" +
        "  \"phase\": \"Fusion\",\n" +
        "  \"compositeSkill\": {\n" +
        "    \"skillName\": \"合成スキル名\",\n" +
        "    \"meritEffect\": \"自身が作成した武器のダメージ+50%\",\n" +
        "    \"demeritEffect\": \"他人が作成した武器を使用時、攻撃倍率-70%\",\n" +
        "    \"flavorText\": \"融合の経緯と呪いの説明\",\n" +
        "    \"probability\": 1.0,\n" +
        "    \"effectParameters\": [\n" +
        "      { \"key\": \"selfCraftedWeaponDamageBonus\", \"value\": 0.5 },\n" +
        "      { \"key\": \"foreignWeaponDamagePenalty\", \"value\": -0.7 }\n" +
        "    ],\n" +
        "    \"fusedFromSkillIds\": [\"skill_a\", \"skill_b\"]\n" +
        "  }\n" +
        "}";

    private const string CraftingJsonSchema =
        "{\n" +
        "  \"generationType\": \"craft\",\n" +
        "  \"phase\": \"Crafting\",\n" +
        "  \"craft\": {\n" +
        "    \"itemName\": \"生成アイテム名\",\n" +
        "    \"quality\": 0.85,\n" +
        "    \"materials\": [\"鉄鉱石\", \"獣骨\"],\n" +
        "    \"qualityBonusOnSuccess\": 0.1\n" +
        "  },\n" +
        "  \"recipeRequest\": {\n" +
        "    \"isNewRecipe\": false,\n" +
        "    \"recipeName\": \"（要望時のみ）新レシピ名\",\n" +
        "    \"requiredMaterials\": [\"素材A\", \"素材B\"],\n" +
        "    \"outputItemName\": \"完成品名\"\n" +
        "  }\n" +
        "}";

    private const string CrisisJsonSchema =
        "{\n" +
        "  \"generationType\": \"skill\",\n" +
        "  \"phase\": \"Crisis\",\n" +
        "  \"skill\": {\n" +
        "    \"skillName\": \"漢字3〜4文字（読みカタカナ）\",\n" +
        "    \"flavorText\": \"唯一無二の絶対効果の説明\",\n" +
        "    \"probability\": 0.00001,\n" +
        "    \"effectParameters\": [\n" +
        "      { \"key\": \"ignoreDefense\", \"value\": 1.0 },\n" +
        "      { \"key\": \"haltFleshDegradation\", \"value\": 1.0 }\n" +
        "    ]\n" +
        "  }\n" +
        "}";
}

/// <summary>生産フェーズ用の追加入力。</summary>
public sealed class GamePhaseCraftingInput
{
    public IReadOnlyList<string> Materials { get; set; } = Array.Empty<string>();
    public bool RequestNewRecipe { get; set; }
    public string RecipeWishDescription { get; set; } = string.Empty;

    /// <summary>職人実験ハブから渡される決定論的評価レポート（任意）。</summary>
    public CraftExperimentReport ExperimentReport { get; set; }

    public void AppendCraftingSection(StringBuilder sb, float craftQuality)
    {
        sb.AppendLine("【生産・解体コンテキスト】");
        sb.AppendLine($"- 品質（Quality）: {craftQuality:F2}");
        sb.AppendLine("- 素材リスト:");

        if (Materials == null || Materials.Count == 0)
        {
            sb.AppendLine("  - （素材未指定）");
        }
        else
        {
            for (int i = 0; i < Materials.Count; i++)
            {
                sb.AppendLine($"  - {Materials[i]}");
            }
        }

        sb.AppendLine($"- レシピ要望モード: {(RequestNewRecipe ? "ON（新レシピ生成）" : "OFF（既存レシピに基づく生成）")}");

        if (RequestNewRecipe && !string.IsNullOrWhiteSpace(RecipeWishDescription))
        {
            sb.AppendLine($"- レシピ要望内容: {RecipeWishDescription.Trim()}");
        }

        ExperimentReport?.AppendExperimentSection(sb);
    }
}

/// <summary>技術修練フェーズ用：未習得の基礎スキル判定ヒント。</summary>
public sealed class BaseSkillAwakeningContext
{
    public IReadOnlyList<string> UnlearnedBaseSkillCategories { get; set; } = Array.Empty<string>();
    public int TotalProductionActions { get; set; }

    public void AppendAwakeningSection(StringBuilder sb)
    {
        sb.AppendLine("【基礎スキル目覚め判定】");
        sb.AppendLine("- 対応する基礎スキル: 未習得（この履歴から1つ目覚めさせる）");
        sb.AppendLine($"- 累計生産・解体行動数: {TotalProductionActions}");

        sb.AppendLine("- 未習得の基礎スキル候補カテゴリ:");
        if (UnlearnedBaseSkillCategories == null || UnlearnedBaseSkillCategories.Count == 0)
        {
            sb.AppendLine("  - （カテゴリ未指定 — 履歴から推論せよ）");
        }
        else
        {
            for (int i = 0; i < UnlearnedBaseSkillCategories.Count; i++)
            {
                sb.AppendLine($"  - {UnlearnedBaseSkillCategories[i]}");
            }
        }
    }
}

/// <summary>
/// ゲームフェーズに応じて AI 指示を完全に切り替えるプロンプトビルダー。
/// </summary>
public sealed class GamePhasePromptBuilder
{
    public const string SystemHeader =
        "あなたはゲームのコアシステムを司るAIです。現在のゲームフェーズに従い、" +
        "指定されたJSONフォーマットのみでゲームデータを生成してください。";

    public const string StrictFooter =
        "雑談や解説、前置きは一切禁止。出力はC#でパース可能な純粋なJSONオブジェクト1件のみとすること。" +
        "マークダウンの ```json のような囲みも不要（またはパースの邪魔にならない形式）とすること";

    /// <summary>
    /// フェーズ・履歴・対価スキル・品質から最終プロンプトを組み立てます。
    /// sacrificeSkills は Fusion フェーズで使用（仕様上の List&lt;SkillData&gt;）。
    /// </summary>
    public string Build(
        GamePhase phase,
        PlayerHistoryLog history,
        List<SkillData> sacrificeSkills = null,
        float craftQuality = 0f,
        GamePhaseCraftingInput craftingInput = null,
        BaseSkillAwakeningContext awakeningContext = null,
        CurrentBattleContext crisisContext = null,
        string additionalIntent = null)
    {
        if (history == null)
        {
            throw new ArgumentNullException(nameof(history));
        }

        GamePhasePromptDefinition phaseDefinition = GamePhasePromptRegistry.Get(phase);
        StringBuilder sb = new StringBuilder();

        sb.AppendLine("=== GamePhase AI 生成プロンプト ===");
        sb.AppendLine(SystemHeader);
        sb.AppendLine();
        sb.AppendLine($"【現在フェーズ】 {phaseDefinition.PhaseLabel}");
        sb.AppendLine();

        AppendPhaseSpecificContext(
            sb,
            phase,
            history,
            sacrificeSkills,
            craftQuality,
            craftingInput,
            awakeningContext,
            crisisContext);

        sb.AppendLine();
        sb.AppendLine("【フェーズ別制約命令】");
        sb.AppendLine(phaseDefinition.ConstraintInstruction);

        if (!string.IsNullOrWhiteSpace(additionalIntent))
        {
            sb.AppendLine();
            sb.AppendLine("【追加意図】");
            sb.AppendLine(additionalIntent.Trim());
        }

        sb.AppendLine();
        sb.AppendLine("【期待JSONスキーマ】");
        sb.AppendLine(phaseDefinition.JsonSchema);
        sb.AppendLine();
        sb.AppendLine("【出力厳格化フッター】");
        sb.AppendLine(StrictFooter);

        return sb.ToString().TrimEnd();
    }

    private static void AppendPhaseSpecificContext(
        StringBuilder sb,
        GamePhase phase,
        PlayerHistoryLog history,
        List<SkillData> sacrificeSkills,
        float craftQuality,
        GamePhaseCraftingInput craftingInput,
        BaseSkillAwakeningContext awakeningContext,
        CurrentBattleContext crisisContext)
    {
        switch (phase)
        {
            case GamePhase.Training:
                AppendTrainingContext(sb, history, awakeningContext);
                break;

            case GamePhase.Fusion:
                AppendFusionContext(sb, history, sacrificeSkills);
                break;

            case GamePhase.Crafting:
                AppendCraftingContext(sb, history, craftQuality, craftingInput);
                break;

            case GamePhase.Crisis:
                AppendCrisisContext(sb, history, crisisContext);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(phase), phase, null);
        }
    }

    private static void AppendTrainingContext(
        StringBuilder sb,
        PlayerHistoryLog history,
        BaseSkillAwakeningContext awakeningContext)
    {
        sb.AppendLine("【履歴ログ（戦闘・生産）】");
        history.AppendHistorySection(sb);
        sb.AppendLine();

        awakeningContext ??= new BaseSkillAwakeningContext();
        awakeningContext.AppendAwakeningSection(sb);
    }

    private static void AppendFusionContext(
        StringBuilder sb,
        PlayerHistoryLog history,
        List<SkillData> sacrificeSkills)
    {
        sb.AppendLine("【プレイヤー履歴（参考）】");
        history.AppendHistorySection(sb);
        sb.AppendLine();
        sb.AppendLine("【捧げられたスキル（対価）】");

        if (sacrificeSkills == null || sacrificeSkills.Count == 0)
        {
            sb.AppendLine("- （対価スキル未指定）");
        }
        else
        {
            for (int i = 0; i < sacrificeSkills.Count; i++)
            {
                SkillData skill = sacrificeSkills[i];
                if (skill == null)
                {
                    continue;
                }

                string category = skill.category.ToString();
                sb.AppendLine($"- {skill.skillName} (id: {skill.skillID}, category: {category}, Lv.{skill.skillLevel})");
            }
        }

        sb.AppendLine();
        CompositeSkillData tradeoffExample = new CompositeSkillData
        {
            SkillName = "（AIが命名）",
            MeritEffect = "自身が作成した武器のダメージ+50%",
            DemeritEffect = "他人が作成した武器を使用時、攻撃倍率-70%"
        };
        tradeoffExample.AppendTradeoffSection(sb);
    }

    private static void AppendCraftingContext(
        StringBuilder sb,
        PlayerHistoryLog history,
        float craftQuality,
        GamePhaseCraftingInput craftingInput)
    {
        sb.AppendLine("【プレイヤー履歴（参考）】");
        history.AppendHistorySection(sb);
        sb.AppendLine();

        craftingInput ??= new GamePhaseCraftingInput();
        craftingInput.AppendCraftingSection(sb, craftQuality);
    }

    private static void AppendCrisisContext(
        StringBuilder sb,
        PlayerHistoryLog history,
        CurrentBattleContext crisisContext)
    {
        sb.AppendLine("【履歴ログ】");
        history.AppendHistorySection(sb);
        sb.AppendLine();

        crisisContext ??= new CurrentBattleContext { IsCrisisAwakeningTriggered = true };
        sb.AppendLine("【状況・ピンチ判定】");
        crisisContext.AppendCurrentContextSection(sb);

        if (crisisContext.IsCrisisAwakeningTriggered)
        {
            sb.AppendLine();
            sb.AppendLine(PromptAssemblySections.CrisisAwakeningInstruction);
        }
    }
}

/// <summary>GamePhase ビルダーと既存 API の結合ヘルパー。</summary>
public static class GamePhasePromptBridge
{
    public static string BuildTrainingAwakeningPrompt(
        PlayerHistoryLog history,
        BaseSkillAwakeningContext awakeningContext = null,
        string additionalIntent = null)
    {
        return new GamePhasePromptBuilder().Build(
            GamePhase.Training,
            history,
            sacrificeSkills: null,
            craftQuality: 0f,
            craftingInput: null,
            awakeningContext: awakeningContext,
            crisisContext: null,
            additionalIntent: additionalIntent);
    }

    public static string BuildFusionTradeoffPrompt(
        PlayerHistoryLog history,
        List<SkillData> sacrificeSkills,
        string additionalIntent = null)
    {
        return new GamePhasePromptBuilder().Build(
            GamePhase.Fusion,
            history,
            sacrificeSkills,
            additionalIntent: additionalIntent);
    }

    public static string BuildCraftingPrompt(
        PlayerHistoryLog history,
        float craftQuality,
        GamePhaseCraftingInput craftingInput,
        string additionalIntent = null)
    {
        return new GamePhasePromptBuilder().Build(
            GamePhase.Crafting,
            history,
            craftQuality: craftQuality,
            craftingInput: craftingInput,
            additionalIntent: additionalIntent);
    }
}

/// <summary>モック実行・フェーズ別プロンプト検証用エントリポイント。</summary>
public static class GamePhasePromptBuilderSystemTest
{
    public static void Main()
    {
        RunAllTests(Console.WriteLine);
    }

    public static void RunAllTests(Action<string> log)
    {
        if (log == null)
        {
            log = _ => { };
        }

        log("===== GamePhasePromptBuilderSystemTest 開始 =====");
        RunTrainingAwakeningPromptTest(log);
        RunFusionTradeoffPromptTest(log);
        log("===== GamePhasePromptBuilderSystemTest 完了 =====");
    }

    private static void RunTrainingAwakeningPromptTest(Action<string> log)
    {
        log(string.Empty);
        log("--- 検証：Training モード（基礎スキルの目覚め）---");

        PlayerHistoryLog history = new PlayerHistoryLog
        {
            TotalSlashHits = 850,
            TotalStrikeHits = 120,
            TotalThrustHits = 40,
            TotalPerfectEvades = 12,
            PreferredStatAllocation = "STR重視"
        };

        BaseSkillAwakeningContext awakening = new BaseSkillAwakeningContext
        {
            TotalProductionActions = 230,
            UnlearnedBaseSkillCategories = new List<string> { "鍛冶基礎", "片手剣基礎" }
        };

        GamePhasePromptBuilder builder = new GamePhasePromptBuilder();
        string prompt = builder.Build(
            GamePhase.Training,
            history,
            sacrificeSkills: null,
            craftQuality: 0f,
            awakeningContext: awakening,
            additionalIntent: "斬撃の蓄積が突出しているため、刃の研ぎ方に関する基礎スキルを優先せよ。");

        log("--- 組み立てられた最終プロンプト（Training）---");
        log(prompt);
        log("--- プロンプト終端 ---");
        log(string.Empty);

        AssertScenario(log, "Trainingフェーズ", prompt.Contains("Training（技術修練）"), "フェーズラベル");
        AssertScenario(log, "目覚め命令", prompt.Contains("基礎スキル（BaseSkill）】を1つ目覚めさせる"), "基礎スキル目覚め");
        AssertScenario(log, "未習得判定", prompt.Contains("未習得") && prompt.Contains("鍛冶基礎"), "目覚め判定コンテキスト");
        AssertScenario(log, "生産履歴", prompt.Contains("累計生産・解体行動数: 230"), "生産行動数");
        AssertScenario(log, "Trainingスキーマ", prompt.Contains("\"phase\": \"Training\""), "JSONスキーマ");
    }

    private static void RunFusionTradeoffPromptTest(Action<string> log)
    {
        log(string.Empty);
        log("--- 検証：Fusion モード（呪いとロマンの合成スキル）---");

        PlayerHistoryLog history = new PlayerHistoryLog
        {
            TotalSlashHits = 400,
            TotalStrikeHits = 200,
            TotalThrustHits = 60,
            TotalPerfectEvades = 8,
            PreferredStatAllocation = "バランス型"
        };

        List<SkillData> sacrifices = new List<SkillData>
        {
            new SkillData
            {
                skillID = "skill_fire_spark",
                skillName = "火の粉",
                category = SkillCategory.Magic,
                skillLevel = 3
            },
            new SkillData
            {
                skillID = "skill_one_hand_sword",
                skillName = "片手剣術",
                category = SkillCategory.Attack,
                skillLevel = 5
            }
        };

        GamePhasePromptBuilder builder = new GamePhasePromptBuilder();
        string prompt = builder.Build(
            GamePhase.Fusion,
            history,
            sacrificeSkills: sacrifices,
            additionalIntent: "火と刃を融合し、自造武器との相性で威力が変わる呪われた剣技とせよ。");

        log("--- 組み立てられた最終プロンプト（Fusion）---");
        log(prompt);
        log("--- プロンプト終端 ---");
        log(string.Empty);

        AssertScenario(log, "Fusionフェーズ", prompt.Contains("Fusion（術式融合）"), "フェーズラベル");
        AssertScenario(log, "対価スキル", prompt.Contains("火の粉") && prompt.Contains("片手剣術"), "捧げスキル");
        AssertScenario(log, "トレードオフ命令", prompt.Contains("メリット") && prompt.Contains("デメリット"), "メリデメ命令");
        AssertScenario(log, "トレードオフ例", prompt.Contains("自身が作成した武器のダメージ+50%"), "CompositeSkillData例");
        AssertScenario(log, "Fusionスキーマ", prompt.Contains("\"compositeSkill\"") && prompt.Contains("meritEffect"), "JSONスキーマ");
        AssertScenario(log, "厳格フッター", prompt.Contains(GamePhasePromptBuilder.StrictFooter), "純粋JSONのみ");
    }

    private static void AssertScenario(Action<string> log, string label, bool condition, string detail)
    {
#if UNITY_5_3_OR_NEWER
        log(condition ? $"  ✓ [{label} OK] {detail}" : $"  ✗ [{label} NG] {detail}");
#else
        if (!condition)
        {
            throw new InvalidOperationException($"{label} failed: {detail}");
        }

        log($"  ✓ [{label} OK] {detail}");
#endif
    }

#if UNITY_5_3_OR_NEWER
    public static void RunInUnity()
    {
        RunAllTests(Debug.Log);
    }
#endif
}

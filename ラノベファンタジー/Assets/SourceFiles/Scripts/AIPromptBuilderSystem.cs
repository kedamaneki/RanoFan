using System;
using System.Collections.Generic;
using System.Text;

#if UNITY_5_3_OR_NEWER
using UnityEngine;
#endif

// =============================================================================
// AI プロンプト自動組み立て — 4大カテゴリ × 蓄積履歴 × ピンチ覚醒
// AIPromptBuilderSystemTest.Main() で検証。
// 連携: AIGeneratorConnector / SkillFusionTransactionSystem / SacrificeSkillData
// =============================================================================

/// <summary>スキル生成の4大カテゴリ。</summary>
public enum SkillGenerationMode
{
    BaseSkill,
    NormalSkill,
    CompositeSkill,
    UniqueSkill
}

/// <summary>プレイヤーが過去から積み上げてきた行動履歴（統計データ）。</summary>
public sealed class PlayerHistoryLog
{
    public int TotalSlashHits { get; set; }
    public int TotalStrikeHits { get; set; }
    public int TotalThrustHits { get; set; }
    public int TotalPerfectEvades { get; set; }
    public string PreferredStatAllocation { get; set; } = "未設定";

    public void AppendHistorySection(StringBuilder sb)
    {
        sb.AppendLine("プレイヤーの過去の戦闘スタイル履歴：");
        sb.AppendLine($"- 累計斬撃ヒット数: {TotalSlashHits}");
        sb.AppendLine($"- 累計打撃ヒット数: {TotalStrikeHits}");
        sb.AppendLine($"- 累計突刺ヒット数: {TotalThrustHits}");
        sb.AppendLine($"- 累計ジャスト回避回数: {TotalPerfectEvades}");
        sb.AppendLine($"- ステータス割り振り傾向: {PreferredStatAllocation}");
    }

    public static PlayerHistoryLog FromAccumulatedStats(AccumulatedStatsLog source)
    {
        if (source == null)
        {
            return new PlayerHistoryLog();
        }

        return new PlayerHistoryLog
        {
            TotalSlashHits = source.TotalSlashHits,
            TotalStrikeHits = source.TotalStrikeHits,
            TotalThrustHits = source.TotalThrustHits,
            TotalPerfectEvades = source.TotalPerfectEvades,
            PreferredStatAllocation = source.PreferredStatAllocation
        };
    }
}

/// <summary>モードごとの制約命令と JSON スキーマ。</summary>
public readonly struct SkillGenerationModeDefinition
{
    public string ModeLabel { get; }
    public string ConstraintInstruction { get; }
    public string JsonSchema { get; }

    public SkillGenerationModeDefinition(string modeLabel, string constraintInstruction, string jsonSchema)
    {
        ModeLabel = modeLabel ?? string.Empty;
        ConstraintInstruction = constraintInstruction ?? string.Empty;
        JsonSchema = jsonSchema ?? string.Empty;
    }
}

/// <summary>4大カテゴリの制約・スキーマ定義を保持します。</summary>
public static class SkillGenerationModeRegistry
{
    private static readonly IReadOnlyDictionary<SkillGenerationMode, SkillGenerationModeDefinition> Definitions =
        BuildDefinitions();

    public static SkillGenerationModeDefinition Get(SkillGenerationMode mode)
    {
        if (Definitions.TryGetValue(mode, out SkillGenerationModeDefinition definition))
        {
            return definition;
        }

        throw new ArgumentOutOfRangeException(nameof(mode), mode, null);
    }

    private static IReadOnlyDictionary<SkillGenerationMode, SkillGenerationModeDefinition> BuildDefinitions()
    {
        return new Dictionary<SkillGenerationMode, SkillGenerationModeDefinition>
        {
            [SkillGenerationMode.BaseSkill] = new SkillGenerationModeDefinition(
                "① 基礎スキル (BaseSkill)",
                "人間の技術・鍛錬・魔力操作などの『基礎』として設計すること。現実の職人技の工程や、" +
                "ファンタジーの基礎理論を、それらしい専門用語で解説する flavorText を必ず含めること。",
                BaseSkillJsonSchema),

            [SkillGenerationMode.NormalSkill] = new SkillGenerationModeDefinition(
                "② 通常スキル (NormalSkill)",
                "システムから付与される超常能力（例: 火の粉）として設計すること。一般的なファンタジーらしい能力概要と、" +
                "戦闘/生産に直結する effectParameters（倍率・コスト等）を整合的に設定すること。",
                NormalSkillJsonSchema),

            [SkillGenerationMode.CompositeSkill] = new SkillGenerationModeDefinition(
                "③ 合成スキル (CompositeSkill)",
                "複数のスキルを掛け合わせた上位スキルとして設計すること。対価（生贄）に捧げられた各スキルの特徴を" +
                "論理的に融合し、単体より明確に強化された数値・効果にすること。",
                CompositeSkillJsonSchema),

            [SkillGenerationMode.UniqueSkill] = new SkillGenerationModeDefinition(
                "④ ユニークスキル (UniqueSkill)",
                "ガチャより渋い確率で手に入る究極能力として設計すること。" +
                "★スキル名は転スラ風の【漢字3〜4文字（読みカタカナ）】を厳守（例: 爆炎之刃（バクエンノヤイバ））。" +
                "既存の有名作品の完全な丸パクリ（グラトニー等）は絶対に避けること。" +
                "ゲームシステムや既存ルールを破壊・無視する唯一無二の絶対効果（防御無視、肉質劣化停止など）を考案すること。",
                UniqueSkillJsonSchema)
        };
    }

    private const string BaseSkillJsonSchema =
        "{\n" +
        "  \"generationType\": \"skill\",\n" +
        "  \"skill\": {\n" +
        "    \"skillName\": \"基礎技法名\",\n" +
        "    \"flavorText\": \"職人技/魔力基礎理論の解説\",\n" +
        "    \"probability\": 1.0,\n" +
        "    \"effectParameters\": [\n" +
        "      { \"key\": \"craftQualityBonus\", \"value\": 0.0 },\n" +
        "      { \"key\": \"theoryDepth\", \"value\": 1.0 }\n" +
        "    ]\n" +
        "  }\n" +
        "}";

    private const string NormalSkillJsonSchema =
        "{\n" +
        "  \"generationType\": \"skill\",\n" +
        "  \"skill\": {\n" +
        "    \"skillName\": \"通常スキル名\",\n" +
        "    \"flavorText\": \"一般的ファンタジー能力の概要\",\n" +
        "    \"probability\": 1.0,\n" +
        "    \"effectParameters\": [\n" +
        "      { \"key\": \"damageMultiplier\", \"value\": 1.0 },\n" +
        "      { \"key\": \"staminaCostMultiplier\", \"value\": 1.0 }\n" +
        "    ]\n" +
        "  }\n" +
        "}";

    private const string CompositeSkillJsonSchema =
        "{\n" +
        "  \"generationType\": \"skill\",\n" +
        "  \"skill\": {\n" +
        "    \"skillName\": \"合成スキル名\",\n" +
        "    \"flavorText\": \"生贄スキル融合の説明\",\n" +
        "    \"probability\": 1.0,\n" +
        "    \"effectParameters\": [\n" +
        "      { \"key\": \"damageMultiplier\", \"value\": 1.5 },\n" +
        "      { \"key\": \"fusionPowerScale\", \"value\": 1.2 }\n" +
        "    ],\n" +
        "    \"fusedFromSkillIds\": [\"skill_a\", \"skill_b\"]\n" +
        "  }\n" +
        "}";

    private const string UniqueSkillJsonSchema =
        "{\n" +
        "  \"generationType\": \"skill\",\n" +
        "  \"skill\": {\n" +
        "    \"skillName\": \"漢字3〜4文字（読みカタカナ）\",\n" +
        "    \"flavorText\": \"唯一無二の絶対効果の説明\",\n" +
        "    \"probability\": 0.00001,\n" +
        "    \"effectParameters\": [\n" +
        "      { \"key\": \"damageMultiplier\", \"value\": 2.5 },\n" +
        "      { \"key\": \"ignoreDefense\", \"value\": 1.0 },\n" +
        "      { \"key\": \"haltFleshDegradation\", \"value\": 1.0 }\n" +
        "    ]\n" +
        "  }\n" +
        "}";
}

/// <summary>プロンプト組み立ての共通ヘッダー・フッター・ピンチ割り込み。</summary>
public static class PromptAssemblySections
{
    public const string SystemHeader =
        "あなたはゲームのコアシステムを司るAIです。以下のプレイヤーの歴史と状況から、" +
        "指定されたJSONフォーマットに従ってゲームデータを生成してください。";

    public const string CrisisAwakeningInstruction =
        "【緊急警告・極限状態】プレイヤーは今、死線の間際に立たされており、ピンチ覚醒フラグが点灯しています。" +
        "過去の行動履歴を最大級にリスペクトした上で、既存の枠を破壊する強力なスキル" +
        "（ユニークスキルの場合は獲得確率の動的算出）を考案してください";

    public const string StrictFooter =
        "雑談や解説、前置きは一切禁止。出力はC#でパース可能な純粋なJSONオブジェクト1件のみとすること。" +
        "マークダウンの ```json のような囲みも不要（またはパースの邪魔にならない形式）とすること";

    public static void AppendCrisisInjectionIfNeeded(StringBuilder sb, CurrentBattleContext context)
    {
        if (context == null || !context.IsCrisisAwakeningTriggered)
        {
            return;
        }

        sb.AppendLine();
        sb.AppendLine(CrisisAwakeningInstruction);
    }
}

/// <summary>
/// ゲーム内ログ・統計から LLM 送信テキストを動的生成するプロンプトビルダー。
/// </summary>
public sealed class GameAIPromptBuilder
{
    /// <summary>
    /// AI へ送信する最終プロンプトを組み立てます。
    /// sacrificeSkills は CompositeSkill モードで使用します（仕様上の SkillData リストに相当）。
    /// </summary>
    public string Build(
        SkillGenerationMode mode,
        PlayerHistoryLog history,
        CurrentBattleContext context,
        IReadOnlyList<SacrificeSkillData> sacrificeSkills = null,
        string additionalIntent = null)
    {
        if (history == null)
        {
            throw new ArgumentNullException(nameof(history));
        }

        if (context == null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        SkillGenerationModeDefinition modeDefinition = SkillGenerationModeRegistry.Get(mode);
        StringBuilder sb = new StringBuilder();

        sb.AppendLine("=== AI 生成プロンプト ===");
        sb.AppendLine(PromptAssemblySections.SystemHeader);
        sb.AppendLine();

        sb.AppendLine("【履歴ログ埋め込み】");
        history.AppendHistorySection(sb);
        sb.AppendLine();

        sb.AppendLine("【状況・ピンチ判定】");
        context.AppendCurrentContextSection(sb);
        PromptAssemblySections.AppendCrisisInjectionIfNeeded(sb, context);
        sb.AppendLine();

        sb.AppendLine("【カテゴリ別制約・フォーマット】");
        sb.AppendLine($"生成モード: {modeDefinition.ModeLabel}");
        sb.AppendLine(modeDefinition.ConstraintInstruction);

        if (mode == SkillGenerationMode.CompositeSkill)
        {
            AppendSacrificeSkillsSection(sb, sacrificeSkills);
        }

        if (!string.IsNullOrWhiteSpace(additionalIntent))
        {
            sb.AppendLine();
            sb.AppendLine("【追加意図】");
            sb.AppendLine(additionalIntent.Trim());
        }

        sb.AppendLine();
        sb.AppendLine("【期待JSONスキーマ】");
        sb.AppendLine(modeDefinition.JsonSchema);
        sb.AppendLine();
        sb.AppendLine("【出力厳格化フッター】");
        sb.AppendLine(PromptAssemblySections.StrictFooter);

        return sb.ToString().TrimEnd();
    }

    private static void AppendSacrificeSkillsSection(StringBuilder sb, IReadOnlyList<SacrificeSkillData> sacrificeSkills)
    {
        sb.AppendLine();
        sb.AppendLine("【対価（生贄）スキル一覧】");

        if (sacrificeSkills == null || sacrificeSkills.Count == 0)
        {
            sb.AppendLine("- （生贄スキル未指定）");
            return;
        }

        for (int i = 0; i < sacrificeSkills.Count; i++)
        {
            SacrificeSkillData skill = sacrificeSkills[i];
            if (skill != null)
            {
                sb.AppendLine($"- {skill.SkillName} (id: {skill.SkillId})");
            }
        }
    }
}

/// <summary>
/// 融合トランザクション・API コネクターとの結合ヘルパー。
/// </summary>
public static class AIPromptFusionBridge
{
    public static string BuildCompositeFusionPrompt(
        IReadOnlyList<SacrificeSkillData> sacrificeSkills,
        PlayerHistoryLog history = null,
        CurrentBattleContext context = null,
        bool crisisAwakening = false)
    {
        history ??= new PlayerHistoryLog();
        context ??= new CurrentBattleContext();

        if (crisisAwakening)
        {
            context.IsCrisisAwakeningTriggered = true;
        }

        return new GameAIPromptBuilder().Build(
            SkillGenerationMode.CompositeSkill,
            history,
            context,
            sacrificeSkills);
    }

    public static string BuildUniqueSkillPrompt(
        PlayerHistoryLog history,
        CurrentBattleContext context,
        string additionalIntent = null)
    {
        return new GameAIPromptBuilder().Build(
            SkillGenerationMode.UniqueSkill,
            history,
            context,
            sacrificeSkills: null,
            additionalIntent: additionalIntent);
    }
}

/// <summary>モック実行・プロンプト組み立て検証用エントリポイント。</summary>
public static class AIPromptBuilderSystemTest
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

        log("===== AIPromptBuilderSystemTest 開始 =====");
        RunCrisisUniqueSkillPromptTest(log);
        log("===== AIPromptBuilderSystemTest 完了 =====");
    }

    private static void RunCrisisUniqueSkillPromptTest(Action<string> log)
    {
        log(string.Empty);
        log("--- 検証：ユニークスキル要求（ピンチ覚醒フラグ ON）---");

        PlayerHistoryLog history = new PlayerHistoryLog
        {
            TotalSlashHits = 1200,
            TotalStrikeHits = 320,
            TotalThrustHits = 180,
            TotalPerfectEvades = 45,
            PreferredStatAllocation = "MND極振り"
        };

        CurrentBattleContext context = new CurrentBattleContext
        {
            EnemyRank = EnemyRank.Boss,
            IsCrisisAwakeningTriggered = true
        };

        GameAIPromptBuilder builder = new GameAIPromptBuilder();
        string prompt = builder.Build(
            SkillGenerationMode.UniqueSkill,
            history,
            context,
            sacrificeSkills: null,
            additionalIntent: "斬撃と精神の極意を融合した、ボス専用の覚醒ユニークスキルを設計せよ。");

        log("--- 組み立てられた最終プロンプト ---");
        log(prompt);
        log("--- プロンプト終端 ---");
        log(string.Empty);

        AssertScenario(log, "システムヘッダー", prompt.Contains(PromptAssemblySections.SystemHeader), "基本ヘッダー");
        AssertScenario(log, "蓄積履歴", prompt.Contains("累計斬撃ヒット数: 1200") && prompt.Contains("MND極振り"), "履歴ログ");
        AssertScenario(log, "Bossコンテキスト", prompt.Contains("対峙している敵のランク: Boss"), "敵ランク");
        AssertScenario(
            log,
            "ピンチ割り込み",
            prompt.Contains(PromptAssemblySections.CrisisAwakeningInstruction),
            "緊急警告命令");
        AssertScenario(
            log,
            "ユニーク制約",
            prompt.Contains("漢字3〜4文字") && prompt.Contains("丸パクリ"),
            "カテゴリ④制約");
        AssertScenario(
            log,
            "JSONスキーマ",
            prompt.Contains("\"probability\": 0.00001") && prompt.Contains("ignoreDefense"),
            "ユニーク用スキーマ");
        AssertScenario(log, "厳格フッター", prompt.Contains(PromptAssemblySections.StrictFooter), "純粋JSONのみ");

        CurrentBattleContext calm = new CurrentBattleContext
        {
            EnemyRank = EnemyRank.Boss,
            IsCrisisAwakeningTriggered = false
        };

        string calmPrompt = builder.Build(SkillGenerationMode.UniqueSkill, history, calm);
        AssertScenario(
            log,
            "フラグOFF時割り込みなし",
            !calmPrompt.Contains(PromptAssemblySections.CrisisAwakeningInstruction),
            "平常時は緊急ブロックなし");
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

using System;
using System.Collections.Generic;
using System.Text;

#if UNITY_5_3_OR_NEWER
using UnityEngine;
#endif

// =============================================================================
// 蓄積ログ × ピンチ覚醒フラグ — AI プロンプト自動組み立て
// AccumulatedLogPromptBuilderTest.Main() でモック検証。
// =============================================================================

/// <summary>プレイヤーが過去から積み上げてきた行動履歴（統計データ）。</summary>
public sealed class AccumulatedStatsLog
{
    public int TotalSlashHits { get; set; }
    public int TotalStrikeHits { get; set; }
    public int TotalThrustHits { get; set; }
    public int TotalPerfectEvades { get; set; }
    public string PreferredStatAllocation { get; set; } = "未設定";

    public void AppendAccumulatedHistorySection(StringBuilder sb)
    {
        sb.AppendLine("プレイヤーの過去の戦闘スタイル履歴：");
        sb.AppendLine($"- 累計斬撃ヒット数: {TotalSlashHits}");
        sb.AppendLine($"- 累計打撃ヒット数: {TotalStrikeHits}");
        sb.AppendLine($"- 累計突刺ヒット数: {TotalThrustHits}");
        sb.AppendLine($"- 累計ジャスト回避回数: {TotalPerfectEvades}");
        sb.AppendLine($"- ステータス割り振り傾向: {PreferredStatAllocation}");
    }
}

/// <summary>瞬間的な戦闘状況コンテキスト。</summary>
public sealed class CurrentBattleContext
{
    public EnemyRank EnemyRank { get; set; } = EnemyRank.Normal;
    public bool IsCrisisAwakeningTriggered { get; set; }

    public void AppendCurrentContextSection(StringBuilder sb)
    {
        sb.AppendLine("現在の状況：");
        sb.AppendLine($"- 対峙している敵のランク: {EnemyRank}");
    }
}

/// <summary>
/// ピンチ覚醒時のみ AI へ割り込むシステム命令。
/// ON のときだけプロンプトの性格が激変するようカプセル化しています。
/// </summary>
public static class CrisisAwakeningPromptInjection
{
    public const string SystemInstruction = PromptAssemblySections.CrisisAwakeningInstruction;

    public static void AppendIfTriggered(StringBuilder sb, CurrentBattleContext context)
    {
        PromptAssemblySections.AppendCrisisInjectionIfNeeded(sb, context);
    }

    public static bool ShouldInject(CurrentBattleContext context)
    {
        return context != null && context.IsCrisisAwakeningTriggered;
    }
}

/// <summary>
/// AI へ渡すプロンプトの基礎組み立て（JSON フォーマット指定など）。
/// </summary>
public class PromptBuilder
{
    protected const string JsonFormatInstruction =
        "応答は必ず以下の JSON 形式のみで返してください（枕詞や解説文は禁止）。\n" +
        "スキル名は転スラ風の漢字3〜4文字の重厚な名前にしてください（例: 虚空之眼、万物融解、爆炎之刃）。\n" +
        "{\n" +
        "  \"generationType\": \"skill\",\n" +
        "  \"skill\": {\n" +
        "    \"skillName\": \"漢字3〜4文字のスキル名\",\n" +
        "    \"flavorText\": \"スキルの世界観説明\",\n" +
        "    \"probability\": 0.00001,\n" +
        "    \"effectParameters\": [\n" +
        "      { \"key\": \"damageMultiplier\", \"value\": 1.0 },\n" +
        "      { \"key\": \"staminaCostMultiplier\", \"value\": 1.0 }\n" +
        "    ]\n" +
        "  }\n" +
        "}";

    /// <summary>生成要求の種別ラベル（派生クラスで上書き可能）。</summary>
    protected virtual string GenerationRequestTitle => "ユニークスキル生成要求";

    /// <summary>基礎プロンプト（履歴・状況なし）を組み立てます。</summary>
    public virtual string Build(string userIntent = null)
    {
        StringBuilder sb = new StringBuilder();
        AppendHeader(sb);
        AppendUserIntent(sb, userIntent);
        AppendJsonFormatSection(sb);
        return sb.ToString().TrimEnd();
    }

    protected virtual void AppendHeader(StringBuilder sb)
    {
        sb.AppendLine("=== AI 生成プロンプト ===");
        sb.AppendLine($"【{GenerationRequestTitle}】");
        sb.AppendLine();
    }

    protected void AppendUserIntent(StringBuilder sb, string userIntent)
    {
        if (!string.IsNullOrWhiteSpace(userIntent))
        {
            sb.AppendLine("【プレイヤー意図】");
            sb.AppendLine(userIntent.Trim());
            sb.AppendLine();
        }
    }

    protected void AppendJsonFormatSection(StringBuilder sb)
    {
        sb.AppendLine("【出力フォーマット指定】");
        sb.AppendLine(JsonFormatInstruction);
    }

    /// <summary>
    /// 4大カテゴリ対応の統合プロンプト組み立て（AIPromptBuilderSystem へ委譲）。
    /// </summary>
    public static string Build(
        SkillGenerationMode mode,
        PlayerHistoryLog history,
        CurrentBattleContext context,
        IReadOnlyList<SacrificeSkillData> sacrificeSkills = null,
        string additionalIntent = null)
    {
        return new GameAIPromptBuilder().Build(
            mode,
            history,
            context,
            sacrificeSkills,
            additionalIntent);
    }

    /// <summary>
    /// GamePhase 対応型プロンプト組み立て（GamePhasePromptBuilderSystem へ委譲）。
    /// </summary>
    public static string Build(
        GamePhase phase,
        PlayerHistoryLog history,
        List<SkillData> sacrificeSkills = null,
        float craftQuality = 0f,
        GamePhaseCraftingInput craftingInput = null,
        BaseSkillAwakeningContext awakeningContext = null,
        CurrentBattleContext crisisContext = null,
        string additionalIntent = null)
    {
        return new GamePhasePromptBuilder().Build(
            phase,
            history,
            sacrificeSkills,
            craftQuality,
            craftingInput,
            awakeningContext,
            crisisContext,
            additionalIntent);
    }
}

/// <summary>
/// 蓄積ログとピンチ覚醒フラグを分離して AI インプットを組み立てる拡張 PromptBuilder。
/// </summary>
public sealed class AccumulatedLogPromptBuilder : PromptBuilder
{
    protected override string GenerationRequestTitle => "ユニークスキル要求（蓄積履歴・戦闘コンテキスト統合）";

    /// <summary>
    /// 蓄積統計・現在コンテキスト・（任意）追加意図から最終プロンプトを組み立てます。
    /// </summary>
    public string Build(
        AccumulatedStatsLog accumulatedStats,
        CurrentBattleContext battleContext,
        string additionalUserIntent = null)
    {
        if (accumulatedStats == null)
        {
            throw new ArgumentNullException(nameof(accumulatedStats));
        }

        if (battleContext == null)
        {
            throw new ArgumentNullException(nameof(battleContext));
        }

        return PromptBuilder.Build(
            SkillGenerationMode.UniqueSkill,
            PlayerHistoryLog.FromAccumulatedStats(accumulatedStats),
            battleContext,
            sacrificeSkills: null,
            additionalIntent: additionalUserIntent);
    }
}

/// <summary>モック実行・プロンプト組み立て検証用エントリポイント。</summary>
public static class AccumulatedLogPromptBuilderTest
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

        log("===== AccumulatedLogPromptBuilderTest 開始 =====");
        RunCrisisAwakeningUniqueSkillPromptTest(log);
        log("===== AccumulatedLogPromptBuilderTest 完了 =====");
    }

    /// <summary>
    /// 検証：ピンチ覚醒フラグ ON のユニークスキル要求プロンプト。
    /// </summary>
    private static void RunCrisisAwakeningUniqueSkillPromptTest(Action<string> log)
    {
        log(string.Empty);
        log("--- 検証：ユニークスキル要求（ピンチ覚醒フラグ ON）のプロンプト組み立て ---");

        AccumulatedStatsLog accumulated = new AccumulatedStatsLog
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

        AccumulatedLogPromptBuilder builder = new AccumulatedLogPromptBuilder();
        string prompt = builder.Build(
            accumulated,
            context,
            additionalUserIntent: "斬撃と精神の極意を融合した、ボス専用の覚醒ユニークスキルを設計せよ。");

        log("--- 組み立てられた最終プロンプト ---");
        log(prompt);
        log("--- プロンプト終端 ---");
        log(string.Empty);

        AssertScenario(
            log,
            "蓄積履歴の埋め込み",
            prompt.Contains("累計斬撃ヒット数: 1200") &&
            prompt.Contains("累計ジャスト回避回数: 45") &&
            prompt.Contains("MND極振り"),
            "泥臭い過去履歴が含まれる");

        AssertScenario(
            log,
            "現在コンテキスト",
            prompt.Contains("対峙している敵のランク: Boss"),
            "Boss 戦コンテキストが含まれる");

        AssertScenario(
            log,
            "ピンチ覚醒割り込み",
            CrisisAwakeningPromptInjection.ShouldInject(context) &&
            prompt.Contains(CrisisAwakeningPromptInjection.SystemInstruction),
            "緊急警告システム命令が割り込まれる");

        AssertScenario(
            log,
            "JSONフォーマット指定",
            prompt.Contains("\"generationType\": \"skill\"") &&
            prompt.Contains("漢字3〜4文字") &&
            prompt.Contains("effectParameters"),
            "転スラ風漢字指定と JSON スキーマが結合");

        AssertScenario(
            log,
            "割り込み条件の一致",
            CrisisAwakeningPromptInjection.ShouldInject(context) &&
            prompt.Contains(CrisisAwakeningPromptInjection.SystemInstruction),
            "フラグ ON と割り込み命令の発火が一致");

        // フラグ OFF 時は割り込みが入らないことも確認
        CurrentBattleContext calmContext = new CurrentBattleContext
        {
            EnemyRank = EnemyRank.Boss,
            IsCrisisAwakeningTriggered = false
        };

        string calmPrompt = builder.Build(accumulated, calmContext);
        AssertScenario(
            log,
            "フラグOFF時は割り込みなし",
            !calmPrompt.Contains(CrisisAwakeningPromptInjection.SystemInstruction),
            "平常時は緊急警告ブロックが入らない");
    }

    private static void AssertScenario(Action<string> log, string label, bool condition, string detail)
    {
#if UNITY_5_3_OR_NEWER
        log(condition
            ? $"  ✓ [{label} OK] {detail}"
            : $"  ✗ [{label} NG] {detail}");
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

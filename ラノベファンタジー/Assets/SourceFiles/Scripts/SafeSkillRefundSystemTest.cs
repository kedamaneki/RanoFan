using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

#if UNITY_5_3_OR_NEWER
using UnityEngine;
#endif

// =============================================================================
// スキル融合トランザクション × AI パース失敗時の安全返還（ロールバック）
// SafeSkillRefundSystemTest.Main() でモック検証。
// 連携: AIGeneratorConnector / AIResponseParser / GeneratedSkillData
// ※ 既存 SkillData.cs（スキル枠）とは別の SacrificeSkillData を使用します。
// =============================================================================

/// <summary>
/// 融合の対価となるプレイヤー所持スキル（仕様上の SkillData に相当）。
/// 既存の <see cref="SkillData"/> スキル枠クラスとは別概念です。
/// </summary>
public sealed class SacrificeSkillData
{
    public string SkillId { get; }
    public string SkillName { get; }

    public SacrificeSkillData(string skillId, string skillName)
    {
        SkillId = skillId ?? string.Empty;
        SkillName = skillName ?? string.Empty;
    }

    public override string ToString()
    {
        return $"{SkillName} ({SkillId})";
    }
}

/// <summary>融合処理の結果種別。</summary>
public enum SkillFusionOutcome
{
    Success,
    Refunded,
    InvalidRequest
}

/// <summary>TrySkillFusion の結果。</summary>
public sealed class SkillFusionResult
{
    public SkillFusionOutcome Outcome { get; }
    public GeneratedSkillData GrantedSkill { get; }
    public IReadOnlyList<SacrificeSkillData> RefundedSkills { get; }
    public string Message { get; }

    public SkillFusionResult(
        SkillFusionOutcome outcome,
        GeneratedSkillData grantedSkill,
        IReadOnlyList<SacrificeSkillData> refundedSkills,
        string message)
    {
        Outcome = outcome;
        GrantedSkill = grantedSkill;
        RefundedSkills = refundedSkills ?? Array.Empty<SacrificeSkillData>();
        Message = message ?? string.Empty;
    }
}

/// <summary>
/// プレイヤーの所持スキルと融合仮預かり（エスクロー）を管理します。
/// </summary>
public sealed class PlayerSkillInventory
{
    private readonly List<SacrificeSkillData> ownedSkills = new List<SacrificeSkillData>();
    private readonly List<SacrificeSkillData> escrowSkills = new List<SacrificeSkillData>();
    private readonly List<GeneratedSkillData> fusedUniqueSkills = new List<GeneratedSkillData>();

    public IReadOnlyList<SacrificeSkillData> OwnedSkills => ownedSkills;
    public IReadOnlyList<SacrificeSkillData> EscrowSkills => escrowSkills;
    public IReadOnlyList<GeneratedSkillData> FusedUniqueSkills => fusedUniqueSkills;

    public void AddOwnedSkill(SacrificeSkillData skill)
    {
        if (skill != null)
        {
            ownedSkills.Add(skill);
        }
    }

    public void AddOwnedSkills(IEnumerable<SacrificeSkillData> skills)
    {
        if (skills == null)
        {
            return;
        }

        foreach (SacrificeSkillData skill in skills)
        {
            AddOwnedSkill(skill);
        }
    }

    /// <summary>対価スキルを所持リストから仮預かりへ移動します。</summary>
    public bool TryHoldSacrificesForFusion(IReadOnlyList<SacrificeSkillData> sacrificeSkills)
    {
        if (sacrificeSkills == null || sacrificeSkills.Count == 0)
        {
            return false;
        }

        if (escrowSkills.Count > 0)
        {
            return false;
        }

        for (int i = 0; i < sacrificeSkills.Count; i++)
        {
            SacrificeSkillData sacrifice = sacrificeSkills[i];
            int index = FindOwnedIndex(sacrifice.SkillId);
            if (index < 0)
            {
                RollbackPartialHold();
                return false;
            }

            escrowSkills.Add(ownedSkills[index]);
            ownedSkills.RemoveAt(index);
        }

        return true;
    }

    /// <summary>融合成功時：仮預かりを完全消費（消滅）。</summary>
    public void ConsumeEscrow()
    {
        escrowSkills.Clear();
    }

    /// <summary>融合失敗時：仮預かりを所持リストへ返還。</summary>
    public void RefundEscrow()
    {
        for (int i = 0; i < escrowSkills.Count; i++)
        {
            ownedSkills.Add(escrowSkills[i]);
        }

        escrowSkills.Clear();
    }

    public void GrantFusedSkill(GeneratedSkillData skill)
    {
        if (skill != null)
        {
            fusedUniqueSkills.Add(skill);
        }
    }

    public bool ContainsOwnedSkill(string skillId)
    {
        return FindOwnedIndex(skillId) >= 0;
    }

    public int CountOwnedSkill(string skillId)
    {
        int count = 0;
        for (int i = 0; i < ownedSkills.Count; i++)
        {
            if (ownedSkills[i].SkillId == skillId)
            {
                count++;
            }
        }

        return count;
    }

    private int FindOwnedIndex(string skillId)
    {
        for (int i = 0; i < ownedSkills.Count; i++)
        {
            if (ownedSkills[i].SkillId == skillId)
            {
                return i;
            }
        }

        return -1;
    }

    private void RollbackPartialHold()
    {
        RefundEscrow();
    }

    public string BuildInventoryLog()
    {
        StringBuilder sb = new StringBuilder();
        sb.Append("所持: ");
        AppendSkillList(sb, ownedSkills);
        sb.Append(" | 仮預かり: ");
        AppendSkillList(sb, escrowSkills);
        sb.Append(" | 融合ユニーク: ");
        if (fusedUniqueSkills.Count == 0)
        {
            sb.Append("なし");
        }
        else
        {
            for (int i = 0; i < fusedUniqueSkills.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append(", ");
                }

                sb.Append(fusedUniqueSkills[i].SkillName);
            }
        }

        return sb.ToString();
    }

    private static void AppendSkillList(StringBuilder sb, List<SacrificeSkillData> list)
    {
        if (list.Count == 0)
        {
            sb.Append("なし");
            return;
        }

        for (int i = 0; i < list.Count; i++)
        {
            if (i > 0)
            {
                sb.Append(", ");
            }

            sb.Append(list[i].SkillName);
        }
    }
}

/// <summary>融合専用モック API（成功時は『爆炎之刃』を返す）。</summary>
public sealed class FusionMockAIGenerationBackend : IAIGenerationBackend
{
    public const string FusionSuccessToken = "[FUSION_SUCCESS]";
    public const string FusionFailureToken = "[FUSION_FAILURE]";

    public Task<string> FetchRawResponseAsync(string playerLogInput, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        bool failure = playerLogInput != null &&
                       playerLogInput.IndexOf(FusionFailureToken, StringComparison.Ordinal) >= 0;

        string response = failure
            ? MockAIGenerationBackend.BuildBrokenResponse()
            : BuildBlazeBladeSuccessResponse();

        return Task.FromResult(response);
    }

    public static string BuildBlazeBladeSuccessResponse()
    {
        return
            "融合術式、成立しました。\n" +
            "```json\n" +
            "{\n" +
            "  \"generationType\": \"skill\",\n" +
            "  \"skill\": {\n" +
            "    \"skillName\": \"爆炎之刃\",\n" +
            "    \"flavorText\": \"火の粉と通常斬りが交わり、刃先から紅蓮が滾る転移者だけの剣技。\",\n" +
            "    \"probability\": 0.00003,\n" +
            "    \"effectParameters\": [\n" +
            "      { \"key\": \"damageMultiplier\", \"value\": 2.8 },\n" +
            "      { \"key\": \"staminaCostMultiplier\", \"value\": 0.85 }\n" +
            "    ]\n" +
            "  }\n" +
            "}\n" +
            "```";
    }
}

/// <summary>
/// スキル融合のトランザクション管理。
/// パース失敗時はフォールバックスキルを付与せず、必ず返還します。
/// </summary>
public sealed class SkillFusionTransactionSystem
{
    public const string FusionFailureWarning =
        "融合術式の構築に失敗！捧げられたスキルが逆流します";

    private readonly AIGeneratorConnector aiConnector;
    private readonly PlayerSkillInventory inventory;

    public SkillFusionTransactionSystem(
        PlayerSkillInventory inventory,
        AIGeneratorConnector aiConnector = null)
    {
        this.inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
        this.aiConnector = aiConnector ?? new AIGeneratorConnector(new FusionMockAIGenerationBackend());
    }

    /// <summary>
    /// 対価スキルを捧げ、AI 融合を試みます。
    /// 異常系では必ず RefundEscrow が実行されます。
    /// </summary>
    public async Task<SkillFusionResult> TrySkillFusionAsync(
        IReadOnlyList<SacrificeSkillData> sacrificeSkills,
        bool simulateAiSuccess,
        Action<string> log = null,
        CancellationToken cancellationToken = default)
    {
        string fusionPrompt = BuildFusionPrompt(sacrificeSkills, simulateAiSuccess);
        return await TrySkillFusionWithPromptAsync(
            fusionPrompt,
            sacrificeSkills,
            log,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 任意のプロンプト（GamePhase ビルダー等）で融合を試みます。実 API 連携用。
    /// </summary>
    public async Task<SkillFusionResult> TrySkillFusionWithPromptAsync(
        string fusionPrompt,
        IReadOnlyList<SacrificeSkillData> sacrificeSkills,
        Action<string> log = null,
        CancellationToken cancellationToken = default,
        Func<string, AIGenerationResult> parseFunc = null)
    {
        if (sacrificeSkills == null || sacrificeSkills.Count == 0)
        {
            return new SkillFusionResult(
                SkillFusionOutcome.InvalidRequest,
                null,
                Array.Empty<SacrificeSkillData>(),
                "対価スキルが指定されていません。");
        }

        if (!inventory.TryHoldSacrificesForFusion(sacrificeSkills))
        {
            return new SkillFusionResult(
                SkillFusionOutcome.InvalidRequest,
                null,
                Array.Empty<SacrificeSkillData>(),
                "対価スキルを仮預かりできませんでした（未所持またはエスクロー競合）。");
        }

        log?.Invoke($"[Fusion] 対価を仮預かり: {FormatSacrificeList(sacrificeSkills)}");
        log?.Invoke($"[Fusion] {inventory.BuildInventoryLog()}");

        try
        {
            AIGenerationResult aiResult = await aiConnector
                .RequestAIGenerationAsync(fusionPrompt, cancellationToken, parseFunc)
                .ConfigureAwait(false);

            bool fusionSucceeded = aiResult.ParseStatus == AIParseStatus.Success &&
                                   aiResult.Skill != null &&
                                   !aiResult.Skill.IsFallback;

            if (fusionSucceeded)
            {
                inventory.ConsumeEscrow();
                inventory.GrantFusedSkill(aiResult.Skill);

                log?.Invoke(
                    $"[Fusion] 奇跡の融合成功！新ユニークスキル『{aiResult.Skill.SkillName}』を獲得。");
                log?.Invoke($"[Fusion] 対価スキルは術式に消費され消滅しました。");
                log?.Invoke($"[Fusion] {inventory.BuildInventoryLog()}");

                return new SkillFusionResult(
                    SkillFusionOutcome.Success,
                    aiResult.Skill,
                    Array.Empty<SacrificeSkillData>(),
                    "融合成功");
            }

            return RefundAfterFailure(sacrificeSkills, aiResult.DiagnosticMessage, log);
        }
        catch (Exception exception)
        {
            log?.Invoke($"[Fusion] 例外捕捉: {exception.Message}");
            return RefundAfterFailure(sacrificeSkills, exception.Message, log);
        }
    }

    private SkillFusionResult RefundAfterFailure(
        IReadOnlyList<SacrificeSkillData> originalSacrifices,
        string reason,
        Action<string> log)
    {
        log?.Invoke($"<color=#FF7043><b>【警告】{FusionFailureWarning}</b></color>");
        log?.Invoke($"[Fusion] 失敗理由: {reason}");

        inventory.RefundEscrow();

        List<SacrificeSkillData> refunded = new List<SacrificeSkillData>(originalSacrifices);
        log?.Invoke($"[Fusion] 返還完了: {FormatSacrificeList(refunded)}");
        log?.Invoke($"[Fusion] {inventory.BuildInventoryLog()}");

        return new SkillFusionResult(
            SkillFusionOutcome.Refunded,
            null,
            refunded,
            FusionFailureWarning);
    }

    private static string BuildFusionPrompt(
        IReadOnlyList<SacrificeSkillData> sacrificeSkills,
        bool simulateAiSuccess)
    {
        string mockToken = simulateAiSuccess
            ? FusionMockAIGenerationBackend.FusionSuccessToken
            : FusionMockAIGenerationBackend.FusionFailureToken;

        string compositePrompt = AIPromptFusionBridge.BuildCompositeFusionPrompt(sacrificeSkills);
        return mockToken + Environment.NewLine + compositePrompt;
    }

    private static string FormatSacrificeList(IReadOnlyList<SacrificeSkillData> skills)
    {
        if (skills == null || skills.Count == 0)
        {
            return "なし";
        }

        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < skills.Count; i++)
        {
            if (i > 0)
            {
                sb.Append(", ");
            }

            sb.Append(skills[i].SkillName);
        }

        return sb.ToString();
    }
}

/// <summary>モック実行・融合返還検証用エントリポイント。</summary>
public static class SafeSkillRefundSystemTest
{
    public static void Main()
    {
        RunAllTestsAsync().GetAwaiter().GetResult();
    }

    public static async Task RunAllTestsAsync(Action<string> log = null)
    {
        log ??= Console.WriteLine;

        log("===== SafeSkillRefundSystemTest 開始 =====");
        await RunSuccessFusionCaseAsync(log);
        await RunRefundOnAiErrorCaseAsync(log);
        log("===== SafeSkillRefundSystemTest 完了 =====");
    }

    private static async Task RunSuccessFusionCaseAsync(Action<string> log)
    {
        log(string.Empty);
        log("--- 検証1：正常系（奇跡の融合成功） ---");

        PlayerSkillInventory inventory = new PlayerSkillInventory();
        inventory.AddOwnedSkill(new SacrificeSkillData(ActionIds.FireSpark, "火の粉"));
        inventory.AddOwnedSkill(new SacrificeSkillData(ActionIds.BasicSlash, "通常斬り"));

        List<SacrificeSkillData> sacrifices = new List<SacrificeSkillData>
        {
            new SacrificeSkillData(ActionIds.FireSpark, "火の粉"),
            new SacrificeSkillData(ActionIds.BasicSlash, "通常斬り")
        };

        SkillFusionTransactionSystem fusion = new SkillFusionTransactionSystem(inventory);
        SkillFusionResult result = await fusion.TrySkillFusionAsync(
            sacrifices,
            simulateAiSuccess: true,
            log);

        AssertScenario(
            log,
            "融合成功",
            result.Outcome == SkillFusionOutcome.Success &&
            result.GrantedSkill != null &&
            result.GrantedSkill.SkillName == "爆炎之刃" &&
            !inventory.ContainsOwnedSkill(ActionIds.FireSpark) &&
            !inventory.ContainsOwnedSkill(ActionIds.BasicSlash) &&
            inventory.EscrowSkills.Count == 0 &&
            inventory.FusedUniqueSkills.Count == 1,
            "火の粉・通常斬りが消滅し『爆炎之刃』を獲得");
    }

    private static async Task RunRefundOnAiErrorCaseAsync(Action<string> log)
    {
        log(string.Empty);
        log("--- 検証2：異常系（AIエラーによる安全返還） ---");

        const string rareId1 = "skill_rare_phoenix_flash";
        const string rareId2 = "skill_rare_void_mirror";

        PlayerSkillInventory inventory = new PlayerSkillInventory();
        inventory.AddOwnedSkill(new SacrificeSkillData(rareId1, "鳳凰の閃撃"));
        inventory.AddOwnedSkill(new SacrificeSkillData(rareId2, "虚空鏡写の極意"));

        List<SacrificeSkillData> sacrifices = new List<SacrificeSkillData>
        {
            new SacrificeSkillData(rareId1, "鳳凰の閃撃"),
            new SacrificeSkillData(rareId2, "虚空鏡写の極意")
        };

        log($"[Setup] 融合前インベントリ: {inventory.BuildInventoryLog()}");

        SkillFusionTransactionSystem fusion = new SkillFusionTransactionSystem(inventory);
        SkillFusionResult result = await fusion.TrySkillFusionAsync(
            sacrifices,
            simulateAiSuccess: false,
            log);

        AssertScenario(
            log,
            "AIエラー返還",
            result.Outcome == SkillFusionOutcome.Refunded &&
            inventory.CountOwnedSkill(rareId1) == 1 &&
            inventory.CountOwnedSkill(rareId2) == 1 &&
            inventory.EscrowSkills.Count == 0 &&
            inventory.FusedUniqueSkills.Count == 0,
            "貴重レアスキル2つが1つも失われず返還");

        AssertScenario(
            log,
            "汎用スキル非付与",
            result.GrantedSkill == null,
            "フォールバック汎用スキルは付与されない");
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
        RunAllTestsAsync(Debug.Log).GetAwaiter().GetResult();
    }
#endif
}

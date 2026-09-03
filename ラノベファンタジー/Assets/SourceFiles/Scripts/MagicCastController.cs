using System;
using UnityEngine;

/// <summary>
/// MasterDataManager の MagicMasterData を基に、戦闘中の魔法発動（manaCost 消費・威力伝播）を制御します。
/// PlayerMagicLibrary（MagicTechData）とは独立し、JSON マスター配線専用です。
/// </summary>
public static class MagicCastController
{
    /// <summary>MP 消費に成功し戦闘へ渡す直前に発火します。</summary>
    public static event Action<MagicCastResult> MagicCastSucceeded;

    private const float MpCostReductionPerMagicBonus = 0.5f;
    private const float MinimumManaCost = 1f;

    /// <summary>
    /// 指定魔法 ID を発動します。MP が足りなければ false、成功時は true を返します。
    /// </summary>
    /// <param name="magicId">MagicMasterData.id（例: MAGIC_FIRE_SPARK）</param>
    /// <param name="casterStats">発動者の CombatStats（同一 GameObject の PlayerStatusManager と連携）</param>
    public static bool TryCastMagic(string magicId, CombatStats casterStats)
    {
        if (casterStats == null)
        {
            Debug.LogWarning("[MagicCastController] casterStats が null です。発動を中止します。");
            return false;
        }

        if (string.IsNullOrWhiteSpace(magicId))
        {
            Debug.LogWarning("[MagicCastController] 魔法 ID が空です。発動を中止します。");
            LogCombatWarning("魔法 ID が未指定のため発動できません。");
            return false;
        }

        MasterDataManager masterData = MasterDataManager.Instance;
        if (masterData == null)
        {
            Debug.LogWarning("[MagicCastController] MasterDataManager が未初期化です。");
            LogCombatWarning("マスターデータ未読込のため魔法を発動できません。");
            return false;
        }

        if (!masterData.TryGetMagic(magicId, out MagicMasterData magic) || magic == null)
        {
            Debug.LogWarning(
                $"[MagicCastController] 未登録または無効な魔法 ID: '{magicId}'。でっち上げ ID の可能性があります。");
            LogCombatWarning($"未知の魔法 ID '{magicId}' — 発動をスキップしました。");
            return false;
        }

        PlayerStatusManager status = ResolvePlayerStatus(casterStats);
        if (status == null)
        {
            Debug.LogWarning("[MagicCastController] PlayerStatusManager が見つかりません。");
            LogCombatWarning($"『{magic.name}』を発動できません（MP 管理コンポーネント未検出）。");
            return false;
        }

        ResolvedMagicParameters resolved = MagicCustomizer.Resolve(magic, status, casterStats);
        float effectiveManaCost = resolved.EffectiveManaCost;
        if (!status.TryUseMP(effectiveManaCost))
        {
            Debug.LogWarning(
                $"[MagicCastController] MP 不足: 『{magic.name}』 必要 {effectiveManaCost:F0} / 現在 {status.CurrentMP:F0}");
            MagicMasterCastLog.LogCastFailed(magic, effectiveManaCost, status.CurrentMP);
            LogCombatWarning(
                $"MP 不足のため『{magic.name}』は展開できません。（必要 {effectiveManaCost:F0} / 現在 {status.CurrentMP:F0}）");
            return false;
        }

        MagicCastResult result = MagicCustomizer.BuildCastResult(
            magic,
            resolved,
            effectiveManaCost,
            status.CurrentMP,
            casterStats);

        MagicMasterCastLog.LogCastSucceeded(result);
        MagicCastSucceeded?.Invoke(result);

        CombatActionFeedbackManager combat = CombatActionFeedbackManager.Instance;
        if (combat != null)
        {
            combat.TryApplyPlayerMagicHit(result);
        }

        return true;
    }

    /// <summary>
    /// magicBonus とユニークスキル（Psychic_ManaControl）を反映した実効 manaCost を返します。
    /// </summary>
    public static float CalculateEffectiveManaCost(
        int baseManaCost,
        PlayerStatusManager status,
        CombatStats casterStats = null)
    {
        if (baseManaCost <= 0)
        {
            return 0f;
        }

        float cost = baseManaCost;

        if (status != null)
        {
            float reduction = status.MagicBonus * MpCostReductionPerMagicBonus;
            cost = Mathf.Max(MinimumManaCost, cost - reduction);
        }

        UniqueSkillRuntimeExecutor executor = ResolveUniqueSkillExecutor(casterStats, status);
        if (executor != null)
        {
            cost = executor.GetModifiedManaCost(cost);
        }

        return Mathf.Max(0f, cost);
    }

    private static PlayerStatusManager ResolvePlayerStatus(CombatStats casterStats)
    {
        if (casterStats == null)
        {
            return PlayerStatusManager.Instance;
        }

        PlayerStatusManager onCaster = casterStats.GetComponent<PlayerStatusManager>();
        if (onCaster != null)
        {
            return onCaster;
        }

        return PlayerStatusManager.Instance;
    }

    private static UniqueSkillRuntimeExecutor ResolveUniqueSkillExecutor(
        CombatStats casterStats,
        PlayerStatusManager status)
    {
        if (casterStats != null)
        {
            UniqueSkillRuntimeExecutor onCaster = casterStats.GetComponent<UniqueSkillRuntimeExecutor>();
            if (onCaster != null)
            {
                return onCaster;
            }
        }

        if (status != null)
        {
            UniqueSkillRuntimeExecutor onStatus = status.GetComponent<UniqueSkillRuntimeExecutor>();
            if (onStatus != null)
            {
                return onStatus;
            }
        }

        return UniqueSkillRuntimeExecutor.Instance;
    }

    private static void LogCombatWarning(string message)
    {
        CombatActionFeedbackManager combat = CombatActionFeedbackManager.Instance;
        if (combat != null)
        {
            combat.LogMagicCastWarning(message);
            return;
        }

        Debug.LogWarning($"[MagicCastController] {message}");
    }
}

/// <summary>MagicMasterData 発動ログ（MagicTechLog とは色分け）。</summary>
public static class MagicMasterCastLog
{
    private const string CastColor = "#CE93D8";
    private const string FailColor = "#FF8A80";

    public static void LogCastSucceeded(MagicCastResult result)
    {
        string traitLabel = string.IsNullOrWhiteSpace(result.CustomTraitCode)
            ? "—"
            : result.CustomTraitCode;
        Debug.Log(
            $"<color={CastColor}><b>【魔導発動】MP {result.ManaCostSpent:F0} 消費 — " +
            $"『{result.MagicName}』（{result.Element} / 威力 {result.Power:F0} / " +
            $"速度×{result.CastSpeedModifier:F2} / 範囲 {result.AreaRange:F1} / 特性 {traitLabel}）</b></color> " +
            $"(残MP {result.RemainingMp:F0})");
    }

    public static void LogCastFailed(MagicMasterData magic, float requiredMp, float currentMp)
    {
        string name = magic != null ? magic.name : "不明な術式";
        Debug.Log(
            $"<color={FailColor}><b>【魔導失敗】MP不足 — 『{name}』" +
            $"（必要 {requiredMp:F0} / 現在 {currentMp:F0}）</b></color>");
    }
}

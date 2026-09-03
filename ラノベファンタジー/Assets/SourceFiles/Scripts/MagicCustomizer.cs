using System;
using UnityEngine;

/// <summary>
/// MagicCustomizer が算出した魔法の最終実数値（戦闘ロジックへ渡すペイロード）。
/// </summary>
public readonly struct ResolvedMagicParameters
{
    public float Power { get; }
    public float CastSpeedModifier { get; }
    public float AreaRange { get; }
    public string CustomTraitCode { get; }
    public float EffectiveManaCost { get; }

    public ResolvedMagicParameters(
        float power,
        float castSpeedModifier,
        float areaRange,
        string customTraitCode,
        float effectiveManaCost)
    {
        Power = Mathf.Max(0f, power);
        CastSpeedModifier = castSpeedModifier <= 0f ? 1f : castSpeedModifier;
        AreaRange = Mathf.Max(0f, areaRange);
        CustomTraitCode = customTraitCode ?? string.Empty;
        EffectiveManaCost = Mathf.Max(0f, effectiveManaCost);
    }
}

/// <summary>
/// MagicMasterData の静的パラメータへ、装備・ユニークスキル・ステータス補正を乗算し、
/// 魔法ごとの個性（castSpeed / areaRange / customTraitCode）を保持した最終値を算出します。
/// </summary>
public static class MagicCustomizer
{
    private const float PowerPerMagicBonus = 0.5f;
    private const float CastSpeedPerTechnicalBonus = 0.02f;
    private const float AreaRangePerMagicBonus = 0.05f;

    /// <summary>
    /// マスター定義とプレイヤー状態から、発動時の最終魔法パラメータを解決します。
    /// </summary>
    public static ResolvedMagicParameters Resolve(
        MagicMasterData master,
        PlayerStatusManager status,
        CombatStats casterStats = null)
    {
        if (master == null)
        {
            return new ResolvedMagicParameters(0f, 1f, 0f, string.Empty, 0f);
        }

        float power = master.power;
        float castSpeed = master.castSpeedModifier;
        float areaRange = master.areaRange;
        string traitCode = master.customTraitCode;
        float manaCost = MagicCastController.CalculateEffectiveManaCost(
            master.manaCost,
            status,
            casterStats);

        if (status != null)
        {
            // 表ボーナス「魔」は威力と範囲へ、技は発動速度へ反映
            power += status.MagicBonus * PowerPerMagicBonus;
            areaRange += status.MagicBonus * AreaRangePerMagicBonus;
            castSpeed += status.TechnicalBonus * CastSpeedPerTechnicalBonus;
        }

        UniqueSkillRuntimeExecutor executor = ResolveUniqueSkillExecutor(casterStats, status);
        if (executor != null)
        {
            manaCost = executor.GetModifiedManaCost(manaCost);
        }

        ApplyEraModifiers(master.eraSettings, ref manaCost, ref castSpeed);

        castSpeed = Mathf.Max(0.1f, castSpeed);
        return new ResolvedMagicParameters(power, castSpeed, areaRange, traitCode, manaCost);
    }

    /// <summary>解決済みパラメータから MagicCastResult を組み立てます。</summary>
    public static MagicCastResult BuildCastResult(
        MagicMasterData master,
        ResolvedMagicParameters resolved,
        float manaCostSpent,
        float remainingMp,
        CombatStats casterStats)
    {
        return new MagicCastResult(
            master.id,
            master.name,
            master.element,
            resolved.Power,
            manaCostSpent,
            remainingMp,
            master,
            casterStats,
            resolved.CastSpeedModifier,
            resolved.AreaRange,
            resolved.CustomTraitCode);
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

    /// <summary>
    /// 古代化期は 1.5〜2.5 倍。eraSettings 未定義・例外時は 1.0 倍のまま（Safe-Fail）。
    /// </summary>
    private static void ApplyEraModifiers(MagicEraSettings eraSettings, ref float manaCost, ref float castSpeed)
    {
        try
        {
            EraModifier era = EraContextResolver.ResolveMagicModifier(eraSettings);
            manaCost *= era.ManaCostMultiplier;
            castSpeed *= era.CastSpeedMultiplier;
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[MagicCustomizer] 時代補正の解決に失敗したため 1.0 倍へフォールバックします。\n{exception}");
        }
    }
}

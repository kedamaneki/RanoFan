using UnityEngine;

/// <summary>
/// マスターデータ由来の魔法発動結果（カスタマイズ適用後の最終実数値を保持）。
/// </summary>
public readonly struct MagicCastResult
{
    public string MagicId { get; }
    public string MagicName { get; }
    public string Element { get; }
    public float Power { get; }
    public float ManaCostSpent { get; }
    public float RemainingMp { get; }
    public MagicMasterData SourceData { get; }
    public CombatStats CasterStats { get; }

    /// <summary>発動速度倍率（MagicCustomizer 適用後）。</summary>
    public float CastSpeedModifier { get; }

    /// <summary>効果範囲・軌道半径（MagicCustomizer 適用後）。</summary>
    public float AreaRange { get; }

    /// <summary>固有特殊挙動コード（TRACKING / PIERCING / REBOUND 等）。</summary>
    public string CustomTraitCode { get; }

    public MagicCastResult(
        string magicId,
        string magicName,
        string element,
        float power,
        float manaCostSpent,
        float remainingMp,
        MagicMasterData sourceData,
        CombatStats casterStats,
        float castSpeedModifier = 1f,
        float areaRange = 0f,
        string customTraitCode = null)
    {
        MagicId = magicId ?? string.Empty;
        MagicName = magicName ?? string.Empty;
        Element = element ?? string.Empty;
        Power = power;
        ManaCostSpent = manaCostSpent;
        RemainingMp = remainingMp;
        SourceData = sourceData;
        CasterStats = casterStats;
        CastSpeedModifier = castSpeedModifier <= 0f ? 1f : castSpeedModifier;
        AreaRange = Mathf.Max(0f, areaRange);
        CustomTraitCode = customTraitCode ?? string.Empty;
    }
}

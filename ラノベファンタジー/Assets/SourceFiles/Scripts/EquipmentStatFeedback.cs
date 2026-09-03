using UnityEngine;

// =============================================================================
// 成果物武器の裏パラメータ → 戦闘ステータス還元式
// 連携: ItemData / CombatStats / InventoryManager
// =============================================================================

/// <summary>装備還元の算出結果（STR 加算と倍率。欠損時は恒等）。</summary>
public readonly struct EquipmentModifierValues
{
    public readonly int StrengthBonus;
    public readonly float PoiseDamageMultiplier;
    public readonly float StaminaCostMultiplier;
    public readonly bool Applied;
    public readonly string ItemId;
    public readonly float Purity;
    public readonly float Density;

    public EquipmentModifierValues(
        int strengthBonus,
        float poiseDamageMultiplier,
        float staminaCostMultiplier,
        bool applied,
        string itemId,
        float purity,
        float density)
    {
        StrengthBonus = strengthBonus;
        PoiseDamageMultiplier = poiseDamageMultiplier;
        StaminaCostMultiplier = staminaCostMultiplier;
        Applied = applied;
        ItemId = itemId ?? string.Empty;
        Purity = purity;
        Density = density;
    }

    public static EquipmentModifierValues Identity => new EquipmentModifierValues(
        0, 1f, 1f, false, string.Empty, 0f, 0f);
}

/// <summary>
/// クラフト成果物の Purity / Density から戦闘補正を決定論的に算出します。
/// 未指定・欠損は 1.0 倍 / STR+0 へ Safe-Fail します。
/// </summary>
public static class EquipmentStatFeedback
{
    /// <summary>STR 加算 = Purity × この係数。</summary>
    public const float PurityToStrengthFactor = 0.25f;

    /// <summary>体勢削り倍率 = 1 + Density × この係数。</summary>
    public const float DensityToPoiseFactor = 0.005f;

    /// <summary>攻撃スタミナ倍率 = 1 + Density × この係数。</summary>
    public const float DensityToStaminaFactor = 0.004f;

    public const float PoiseMultiplierMin = 0.75f;
    public const float PoiseMultiplierMax = 2f;
    public const float StaminaMultiplierMin = 0.85f;
    public const float StaminaMultiplierMax = 1.75f;
    public const float HiddenParamAbsMax = 100000f;
    public const int StrengthBonusMax = 9999;

    /// <summary>ItemData から補正を算出します。null / 非装備 / 裏パラメータ欠損は恒等を返します。</summary>
    public static EquipmentModifierValues Calculate(ItemData weapon)
    {
        if (weapon == null || !weapon.IsValid() || !weapon.TryGetCraftHiddenParams(out float purity, out float density))
        {
            return EquipmentModifierValues.Identity;
        }

        return Calculate(weapon.id, true, purity, density);
    }

    /// <summary>
    /// STR+ = round(Purity × 0.25)
    /// 体勢倍率 = clamp(1 + Density × 0.005, 0.75, 2.0)
    /// スタミナ倍率 = clamp(1 + Density × 0.004, 0.85, 1.75)
    /// </summary>
    public static EquipmentModifierValues Calculate(
        string itemId,
        bool hasCraftHiddenParams,
        float purity,
        float density)
    {
        if (!hasCraftHiddenParams)
        {
            return EquipmentModifierValues.Identity;
        }

        if (float.IsNaN(purity) || float.IsNaN(density) || float.IsInfinity(purity) || float.IsInfinity(density))
        {
            return EquipmentModifierValues.Identity;
        }

        float safePurity = Mathf.Clamp(purity, -HiddenParamAbsMax, HiddenParamAbsMax);
        float safeDensity = Mathf.Clamp(density, -HiddenParamAbsMax, HiddenParamAbsMax);
        int strBonus = Mathf.Clamp(
            Mathf.RoundToInt(safePurity * PurityToStrengthFactor),
            0,
            StrengthBonusMax);
        float poiseMul = Mathf.Clamp(1f + safeDensity * DensityToPoiseFactor, PoiseMultiplierMin, PoiseMultiplierMax);
        float staminaMul = Mathf.Clamp(1f + safeDensity * DensityToStaminaFactor, StaminaMultiplierMin, StaminaMultiplierMax);

        return new EquipmentModifierValues(
            strBonus,
            poiseMul,
            staminaMul,
            true,
            itemId ?? string.Empty,
            purity,
            density);
    }

    /// <summary>既知入力で式が壊れていないかを検証し、結果をコンソールへ出します。</summary>
    public static bool RunFormulaSelfCheck()
    {
        EquipmentModifierValues missing = Calculate(null);
        EquipmentModifierValues sample = Calculate("WEAPON_CHITIN_EDGE", true, 80f, 40f);
        bool pass = !missing.Applied &&
                    missing.StrengthBonus == 0 &&
                    Mathf.Approximately(missing.PoiseDamageMultiplier, 1f) &&
                    Mathf.Approximately(missing.StaminaCostMultiplier, 1f) &&
                    sample.Applied &&
                    sample.StrengthBonus == 20 &&
                    Mathf.Abs(sample.PoiseDamageMultiplier - 1.2f) < 0.001f &&
                    Mathf.Abs(sample.StaminaCostMultiplier - 1.16f) < 0.001f;

        if (pass)
        {
            Debug.Log(
                "<color=#A5D6A7>【装備還元・検証】式セルフチェック PASS " +
                "（欠損=恒等 / Purity80→STR+20 / Density40→体勢×1.20 スタミナ×1.16）</color>");
        }
        else
        {
            Debug.LogError(
                "[装備還元・検証] 式セルフチェック FAIL " +
                $"missing STR={missing.StrengthBonus} sample STR={sample.StrengthBonus} " +
                $"poise={sample.PoiseDamageMultiplier:F3} stam={sample.StaminaCostMultiplier:F3}");
        }

        return pass;
    }
}

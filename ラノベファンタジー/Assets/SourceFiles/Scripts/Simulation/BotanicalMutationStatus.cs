using UnityEngine;

// =============================================================================
// 植物の魔導変異 — 伐採場の樹木強化・魔力果実
// =============================================================================

/// <summary>
/// 伐採場 1 箇所の樹木変異と魔力果実ノード。欠損時は通常材へ Safe-Fail します。
/// </summary>
[System.Serializable]
public class BotanicalMutationStatus
{
    public const float TreeMutationManaThreshold = 55f;
    public const float FruitManaThreshold = 70f;
    public const float QualityBonusMin = 1.5f;
    public const float QualityBonusMax = 2.0f;
    public const float MaxFruitStock = 3f;

    public bool IsManaMutated;
    public bool HasManaFruitNode;
    public string FruitNodeId = string.Empty;
    [Range(0f, 3f)] public float FruitStock;
    [Range(1f, 2f)] public float TimberQualityMultiplier = 1f;

    public bool HasEdibleFruit => HasManaFruitNode && FruitStock > 0.05f;

    public void ClampAll()
    {
        if (float.IsNaN(FruitStock) || float.IsInfinity(FruitStock))
        {
            FruitStock = 0f;
        }

        FruitStock = Mathf.Clamp(FruitStock, 0f, MaxFruitStock);
        if (float.IsNaN(TimberQualityMultiplier) || float.IsInfinity(TimberQualityMultiplier) ||
            TimberQualityMultiplier < 1f)
        {
            TimberQualityMultiplier = 1f;
        }

        TimberQualityMultiplier = Mathf.Clamp(TimberQualityMultiplier, 1f, QualityBonusMax);
        FruitNodeId = FruitNodeId ?? string.Empty;
    }

    /// <summary>環境魔力から変異・果実・品質倍率を更新します。</summary>
    public void EvaluateFromMana(float manaEcologyLevel, string hostSpotId)
    {
        ClampAll();
        float mana = NatureEnvironmentStatus.ClampMana(manaEcologyLevel);

        if (mana >= TreeMutationManaThreshold)
        {
            IsManaMutated = true;
            float t = Mathf.InverseLerp(TreeMutationManaThreshold, 100f, mana);
            TimberQualityMultiplier = Mathf.Lerp(QualityBonusMin, QualityBonusMax, t);
        }
        else
        {
            IsManaMutated = false;
            TimberQualityMultiplier = 1f;
        }

        if (mana > FruitManaThreshold)
        {
            HasManaFruitNode = true;
            if (string.IsNullOrWhiteSpace(FruitNodeId))
            {
                FruitNodeId = BuildFruitNodeId(hostSpotId);
            }

            float grow = Mathf.Lerp(0.35f, 0.85f, (mana - FruitManaThreshold) / 30f);
            FruitStock = Mathf.Min(MaxFruitStock, FruitStock + grow);
        }
        else
        {
            HasManaFruitNode = false;
            FruitStock = Mathf.Max(0f, FruitStock - 0.45f);
            if (FruitStock <= 0.05f)
            {
                FruitStock = 0f;
                FruitNodeId = string.Empty;
            }
        }

        ClampAll();
    }

    public float ConsumeFruit(float amount)
    {
        ClampAll();
        float take = Mathf.Min(FruitStock, Mathf.Max(0f, amount));
        FruitStock -= take;
        if (FruitStock <= 0.05f)
        {
            FruitStock = 0f;
            HasManaFruitNode = false;
        }

        return take;
    }

    /// <summary>変異データ欠損時は通常材（倍率 1.0）。</summary>
    public static float SafeQualityMultiplier(BotanicalMutationStatus botany)
    {
        if (botany == null || !botany.IsManaMutated)
        {
            return 1f;
        }

        botany.ClampAll();
        if (botany.TimberQualityMultiplier < QualityBonusMin)
        {
            return QualityBonusMin;
        }

        return Mathf.Clamp(botany.TimberQualityMultiplier, QualityBonusMin, QualityBonusMax);
    }

    public static string BuildFruitNodeId(string spotId)
    {
        string id = string.IsNullOrWhiteSpace(spotId) ? "FOREST" : spotId.Trim();
        return "MANA_FRUIT_" + id;
    }
}

/// <summary>木こり伐採の結果（森林効率・品質・副産物）。</summary>
public struct WoodcutterHarvestResult
{
    public float ForestEfficiency;
    public float QualityMultiplier;
    public float ManaResin;
    public float MutatedLeaves;
    public bool IsManaMutated;
    public bool UsedSafeFail;
    public string Label;

    public static WoodcutterHarvestResult NormalTimber(float efficiency)
    {
        return new WoodcutterHarvestResult
        {
            ForestEfficiency = Mathf.Max(NatureEnvironmentStatus.MinimumProductivity, efficiency),
            QualityMultiplier = 1f,
            Label = "通常材"
        };
    }
}

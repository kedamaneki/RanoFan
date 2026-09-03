using UnityEngine;

// =============================================================================
// 自然環境ステータス — 森林・野生動物・環境魔力
// =============================================================================

/// <summary>
/// 村周辺の自然資源。絶滅時も 0 で停止せず、最低生産性を返します（Safe-Fail）。
/// </summary>
[System.Serializable]
public class NatureEnvironmentStatus
{
    public const float MinForestDensity = 0f;
    public const float MaxForestDensity = 100f;
    public const int MinWildlifePopulation = 0;
    public const int MaxWildlifePopulation = 200;
    public const float MinManaEcology = 0f;
    public const float MaxManaEcology = 100f;
    public const float ForestStressThreshold = 20f;
    public const float MinimumProductivity = 0.10f;

    [Range(0f, 100f)] public float ForestDensity = 72f;
    [Range(0, 200)] public int WildlifePopulation = 118;
    [Range(0f, 100f)] public float ManaEcologyLevel = 32f;

    public bool ForestDepleted => ForestDensity <= 0.05f;
    public bool WildlifeExtinct => WildlifePopulation <= 0;
    public bool ForestStressed => ForestDensity <= ForestStressThreshold;

    public void ClampAll()
    {
        ForestDensity = ClampForest(ForestDensity);
        WildlifePopulation = ClampWildlife(WildlifePopulation);
        ManaEcologyLevel = ClampMana(ManaEcologyLevel);
    }

    /// <summary>森林密度に比例した野生動物の環境収容数（0〜200）。</summary>
    public int CarryingCapacity
    {
        get
        {
            float ratio = ClampForest(ForestDensity) / MaxForestDensity;
            return Mathf.Clamp(Mathf.RoundToInt(ratio * MaxWildlifePopulation), MinWildlifePopulation, MaxWildlifePopulation);
        }
    }

    /// <summary>伐採・狩猟の実効効率。枯渇時は 10% を維持。</summary>
    public float ResolveHarvestEfficiency()
    {
        ClampAll();
        if (ForestDepleted || WildlifeExtinct)
        {
            return MinimumProductivity;
        }

        if (ForestStressed)
        {
            float t = ForestDensity / ForestStressThreshold;
            return Mathf.Lerp(MinimumProductivity, 0.55f, Mathf.Clamp01(t));
        }

        return 1f;
    }

    public float ApplyForestDelta(float delta)
    {
        float before = ForestDensity;
        ForestDensity = ClampForest(ForestDensity + delta);
        return ForestDensity - before;
    }

    public int ApplyWildlifeDelta(int delta)
    {
        int before = WildlifePopulation;
        WildlifePopulation = ClampWildlife(WildlifePopulation + delta);
        return WildlifePopulation - before;
    }

    public float ApplyManaDelta(float delta)
    {
        float before = ManaEcologyLevel;
        ManaEcologyLevel = ClampMana(ManaEcologyLevel + delta);
        return ManaEcologyLevel - before;
    }

    public int ConsumeWildlife(int requested)
    {
        if (requested <= 0)
        {
            return 0;
        }

        int have = Mathf.Max(0, WildlifePopulation);
        int taken = Mathf.Min(have, requested);
        WildlifePopulation = ClampWildlife(have - taken);
        return taken;
    }

    public string FormatSnapshot()
    {
        return $"森林 {ForestDensity:F1}% / 野生動物 {WildlifePopulation} / 環境魔力 {ManaEcologyLevel:F1}";
    }

    public static float ClampForest(float value)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
        {
            return MinForestDensity;
        }

        return Mathf.Clamp(value, MinForestDensity, MaxForestDensity);
    }

    public static int ClampWildlife(int value)
    {
        return Mathf.Clamp(value, MinWildlifePopulation, MaxWildlifePopulation);
    }

    public static float ClampMana(float value)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
        {
            return MinManaEcology;
        }

        return Mathf.Clamp(value, MinManaEcology, MaxManaEcology);
    }
}

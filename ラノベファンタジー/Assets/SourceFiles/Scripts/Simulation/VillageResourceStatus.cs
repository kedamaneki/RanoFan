using UnityEngine;

// =============================================================================
// 村の一次産業ストック — 位相ごとの加算・消費
// =============================================================================

/// <summary>
/// 村の蓄積資源。枯渇時は例外を出さず、消費効率だけ落とします（Safe-Fail）。
/// </summary>
[System.Serializable]
public class VillageResourceStatus
{
    public const float DepletionEfficiency = 0.35f;
    public const float MinStock = 0f;

    public float Wood = 40f;
    public float Food = 52f;
    public float Ore = 18f;
    public float ManaCrystal = 14f;

    /// <summary>要求量に対する実効効率。足りなければ DepletionEfficiency。</summary>
    public float ResolveConsumeEfficiency(float wood, float food, float ore, float manaCrystal)
    {
        bool shortage =
            (wood > 0.001f && Wood < wood) ||
            (food > 0.001f && Food < food) ||
            (ore > 0.001f && Ore < ore) ||
            (manaCrystal > 0.001f && ManaCrystal < manaCrystal);
        return shortage ? DepletionEfficiency : 1f;
    }

    /// <summary>デルタを適用します。負値は在庫の範囲で消費し、下限は 0。</summary>
    public void Apply(VillageResourceDelta delta, float consumeScale)
    {
        float scale = Mathf.Clamp(consumeScale, 0.05f, 1f);
        Wood = ClampStock(Wood + ScaleSigned(delta.wood, scale));
        Food = ClampStock(Food + ScaleSigned(delta.food, scale));
        Ore = ClampStock(Ore + ScaleSigned(delta.ore, scale));
        ManaCrystal = ClampStock(ManaCrystal + ScaleSigned(delta.manaCrystal, scale));
    }

    /// <summary>消費したい正の量だけ引き、実際に引けた量を返します。</summary>
    public float ConsumeUpTo(ref float stock, float requested)
    {
        if (requested <= 0f)
        {
            return 0f;
        }

        float have = Mathf.Max(0f, stock);
        float taken = Mathf.Min(have, requested);
        stock = ClampStock(have - taken);
        return taken;
    }

    public string FormatSnapshot()
    {
        return $"Wood {Wood:F1} / Food {Food:F1} / Ore {Ore:F1} / ManaCrystal {ManaCrystal:F1}";
    }

    private static float ScaleSigned(float value, float consumeScale)
    {
        if (value >= 0f)
        {
            return value;
        }

        return value * consumeScale;
    }

    private static float ClampStock(float value)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
        {
            return MinStock;
        }

        return Mathf.Max(MinStock, value);
    }
}

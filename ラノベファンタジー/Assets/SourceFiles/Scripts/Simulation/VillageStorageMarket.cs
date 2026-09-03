using UnityEngine;

// =============================================================================
// 村共有倉庫 — Food / Timber / Ore / ManaCrystal の循環
// =============================================================================

/// <summary>
/// 仕事完了で入庫、食事・結界維持で出庫します。在庫 0 でも例外は出しません。
/// </summary>
[System.Serializable]
public class VillageStorageMarket
{
    public const float DepletionEfficiency = 0.35f;

    public float Food = 48f;
    public float Timber = 36f;
    public float Ore = 16f;
    public float ManaCrystal = 12f;
    public float ManaResin;
    public float MutatedLeaves;

    public bool HasFood => Food > 0.05f;
    public bool HasCrystal => ManaCrystal > 0.05f;
    public bool FoodDepleted => Food <= 0.05f;

    public void Deposit(float food, float timber, float ore, float manaCrystal)
    {
        Food = Clamp(Food + Mathf.Max(0f, food));
        Timber = Clamp(Timber + Mathf.Max(0f, timber));
        Ore = Clamp(Ore + Mathf.Max(0f, ore));
        ManaCrystal = Clamp(ManaCrystal + Mathf.Max(0f, manaCrystal));
    }

    /// <summary>魔導変異の副産物。欠損・負値は無視します（Safe-Fail）。</summary>
    public void DepositByproduct(float manaResin, float mutatedLeaves)
    {
        ManaResin = Clamp(ManaResin + Mathf.Max(0f, manaResin));
        MutatedLeaves = Clamp(MutatedLeaves + Mathf.Max(0f, mutatedLeaves));
    }

    /// <summary>要求量まで消費し、実際に引けた量を返します。</summary>
    public float Withdraw(ref float stock, float requested)
    {
        if (requested <= 0f)
        {
            return 0f;
        }

        float have = Mathf.Max(0f, stock);
        float taken = Mathf.Min(have, requested);
        stock = Clamp(have - taken);
        return taken;
    }

    public float WithdrawFood(float requested)
    {
        return Withdraw(ref Food, requested);
    }

    public float WithdrawTimber(float requested)
    {
        return Withdraw(ref Timber, requested);
    }

    public float WithdrawOre(float requested)
    {
        return Withdraw(ref Ore, requested);
    }

    public float WithdrawCrystal(float requested)
    {
        return Withdraw(ref ManaCrystal, requested);
    }

    public float ConsumeEfficiency(float needFood, float needTimber, float needOre, float needCrystal)
    {
        bool shortFood = needFood > 0.001f && Food < needFood;
        bool shortTimber = needTimber > 0.001f && Timber < needTimber;
        bool shortOre = needOre > 0.001f && Ore < needOre;
        bool shortCrystal = needCrystal > 0.001f && ManaCrystal < needCrystal;
        return (shortFood || shortTimber || shortOre || shortCrystal) ? DepletionEfficiency : 1f;
    }

    public void SyncToVillageResources(VillageResourceStatus resources)
    {
        if (resources == null)
        {
            return;
        }

        resources.Food = Food;
        resources.Wood = Timber;
        resources.Ore = Ore;
        resources.ManaCrystal = ManaCrystal;
    }

    public string FormatSnapshot()
    {
        string extra = string.Empty;
        if (ManaResin > 0.01f || MutatedLeaves > 0.01f)
        {
            extra = $" / Resin {ManaResin:F1} / Leaves {MutatedLeaves:F1}";
        }

        return $"Food {Food:F1} / Timber {Timber:F1} / Ore {Ore:F1} / ManaCrystal {ManaCrystal:F1}{extra}";
    }

    private static float Clamp(float value)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
        {
            return 0f;
        }

        return Mathf.Max(0f, value);
    }
}

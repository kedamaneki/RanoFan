using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

// =============================================================================
// 領域・西 — 資材ネットワーク・環境魔力波及・交易集計
// =============================================================================

/// <summary>LOD 段階。</summary>
public enum WestRegionLodLevel
{
    Lod0FullMicro = 0,
    Lod1MacroAbstract = 1
}

/// <summary>領域内 1 カ国のランタイム状態（LOD0/LOD1 兼用）。</summary>
[Serializable]
public sealed class WestRegionNationRuntime
{
    public MacroChronicleNationSnapshot snapshot;
    public WestRegionLodLevel lod = WestRegionLodLevel.Lod1MacroAbstract;
    public float distanceDeg;
    public bool alive;

    public RegionalNationMicroCell microCell;
    public float abstractFood;
    public float abstractTimber;
    public float abstractOre;
    public float abstractCrystal;

    public MacroGeoNationWriteback writeback;
    public NationBiasData bias;
}

/// <summary>領域・西の共有市場・脅威・交易ネットワーク。</summary>
[Serializable]
public sealed class RegionalMarketAndThreat
{
    public const string WestRegionName = "領域・西";
    public const float Lod0DistanceDeg = 3.5f;
    public const float TradeFoodReserve = 18f;
    public const float TradeTimberReserve = 12f;
    public const float TradeOreReserve = 6f;

    /// <summary>LOD1 抽象国の位相ごと食料生産係数。</summary>
    public const float Lod1FoodProduction = 0.75f;
    /// <summary>LOD1 抽象国の位相ごと食料維持費係数。</summary>
    public const float Lod1FoodUpkeepBase = 1.85f;
    /// <summary>LOD1 抽象国の位相固定維持費。</summary>
    public const float Lod1FoodUpkeepFlat = 0.22f;
    /// <summary>LOD1 抽象国のエスカレーション期追加消費。</summary>
    public const float Lod1EscalationFoodDrain = 0.48f;

    [Range(0f, 100f)] public float regionalManaEcologyLevel = 32f;
    public float regionalThreatIndex = 2.7f;
    public float aggregateFoodSurplus;
    public float aggregateFoodDeficit;
    public float aggregateTimberSurplus;
    public float aggregateOreSurplus;

    public List<RegionalTradeLogEntry> regionalTrades = new List<RegionalTradeLogEntry>();

    public void ResetNetwork()
    {
        regionalManaEcologyLevel = 32f;
        regionalThreatIndex = 2.7f;
        aggregateFoodSurplus = 0f;
        aggregateFoodDeficit = 0f;
        aggregateTimberSurplus = 0f;
        aggregateOreSurplus = 0f;
        regionalTrades.Clear();
    }

  public static float DistanceDeg(float latA, float lngA, float latB, float lngB)
    {
        return Mathf.Sqrt((latA - latB) * (latA - latB) + (lngA - lngB) * (lngA - lngB));
    }

    public void ClassifyLod(
        List<WestRegionNationRuntime> nations,
        int focalNationId,
        float focalLat,
        float focalLng)
    {
        if (nations == null)
        {
            return;
        }

        for (int i = 0; i < nations.Count; i++)
        {
            WestRegionNationRuntime runtime = nations[i];
            if (runtime?.snapshot == null)
            {
                continue;
            }

            runtime.distanceDeg = DistanceDeg(
                focalLat,
                focalLng,
                runtime.snapshot.lat,
                runtime.snapshot.lng);
            bool lod0 = runtime.snapshot.id == focalNationId ||
                        runtime.distanceDeg <= Lod0DistanceDeg;
            runtime.lod = lod0 ? WestRegionLodLevel.Lod0FullMicro : WestRegionLodLevel.Lod1MacroAbstract;
        }
    }

    public void AggregateSurplusDeficit(
        List<WestRegionNationRuntime> nations,
        VillageStorageMarket primaryMarket)
    {
        aggregateFoodSurplus = 0f;
        aggregateFoodDeficit = 0f;
        aggregateTimberSurplus = 0f;
        aggregateOreSurplus = 0f;

        if (primaryMarket != null)
        {
            aggregateFoodSurplus += Mathf.Max(0f, primaryMarket.Food - TradeFoodReserve);
            aggregateTimberSurplus += Mathf.Max(0f, primaryMarket.Timber - TradeTimberReserve);
            aggregateOreSurplus += Mathf.Max(0f, primaryMarket.Ore - TradeOreReserve);
            if (primaryMarket.FoodDepleted)
            {
                aggregateFoodDeficit += TradeFoodReserve - primaryMarket.Food;
            }
        }

        if (nations == null)
        {
            return;
        }

        for (int i = 0; i < nations.Count; i++)
        {
            WestRegionNationRuntime runtime = nations[i];
            if (runtime == null || !runtime.alive || runtime.snapshot == null)
            {
                continue;
            }

            if (runtime.snapshot.id == MicroToMacroAggregator.DefaultNationId)
            {
                continue;
            }

            float food;
            float timber;
            float ore;
            if (runtime.lod == WestRegionLodLevel.Lod0FullMicro && runtime.microCell?.market != null)
            {
                food = runtime.microCell.market.Food;
                timber = runtime.microCell.market.Timber;
                ore = runtime.microCell.market.Ore;
            }
            else
            {
                food = runtime.abstractFood;
                timber = runtime.abstractTimber;
                ore = runtime.abstractOre;
            }

            float foodSur = Mathf.Max(0f, food - TradeFoodReserve);
            float timberSur = Mathf.Max(0f, timber - TradeTimberReserve);
            float oreSur = Mathf.Max(0f, ore - TradeOreReserve);
            aggregateFoodSurplus += foodSur;
            aggregateTimberSurplus += timberSur;
            aggregateOreSurplus += oreSur;
            if (food < 8f)
            {
                aggregateFoodDeficit += 8f - food;
            }
        }
    }

    public void ProcessRegionalTrade(
        List<WestRegionNationRuntime> nations,
        int phase,
        VillageStorageMarket primaryMarket)
    {
        if (nations == null || nations.Count == 0)
        {
            return;
        }

        List<WestRegionNationRuntime> donors = new List<WestRegionNationRuntime>();
        List<WestRegionNationRuntime> receivers = new List<WestRegionNationRuntime>();
        for (int i = 0; i < nations.Count; i++)
        {
            WestRegionNationRuntime runtime = nations[i];
            if (runtime == null || !runtime.alive)
            {
                continue;
            }

            float food = ResolveFood(runtime, primaryMarket);
            if (food > TradeFoodReserve + 2f)
            {
                donors.Add(runtime);
            }
            else if (food < 8f)
            {
                receivers.Add(runtime);
            }
        }

        for (int r = 0; r < receivers.Count; r++)
        {
            WestRegionNationRuntime receiver = receivers[r];
            float need = 8f - ResolveFood(receiver, primaryMarket);
            if (need <= 0.05f)
            {
                continue;
            }

            for (int d = 0; d < donors.Count && need > 0.05f; d++)
            {
                WestRegionNationRuntime donor = donors[d];
                float donorFood = ResolveFood(donor, primaryMarket);
                float surplus = donorFood - TradeFoodReserve;
                if (surplus <= 1f)
                {
                    continue;
                }

                float amount = Mathf.Min(surplus * 0.25f, need);
                if (amount <= 0.05f)
                {
                    continue;
                }

                WithdrawFood(donor, primaryMarket, amount);
                DepositFood(receiver, primaryMarket, amount);
                need -= amount;

                regionalTrades.Add(new RegionalTradeLogEntry
                {
                    fromNationId = donor.snapshot.id,
                    toNationId = receiver.snapshot.id,
                    phase = phase,
                    resource = "Food",
                    amount = amount,
                    note = "領域交易"
                });
            }
        }
    }

    public void PropagateManaEcology(
        List<WestRegionNationRuntime> nations,
        int phase)
    {
        if (nations == null)
        {
            return;
        }

        float barrierStress = 0f;
        int aliveCount = 0;
        for (int i = 0; i < nations.Count; i++)
        {
            WestRegionNationRuntime runtime = nations[i];
            if (runtime == null || !runtime.alive || runtime.snapshot == null)
            {
                continue;
            }

            aliveCount++;
            float barrierNorm = runtime.snapshot.barrierEfficiency;
            if (runtime.lod == WestRegionLodLevel.Lod0FullMicro && runtime.microCell?.barrier != null)
            {
                barrierNorm = runtime.microCell.barrier.Efficiency / 100f;
            }

            barrierStress += Mathf.Clamp01(1f - barrierNorm);
        }

        if (aliveCount > 0)
        {
            barrierStress /= aliveCount;
        }

        float manaPulse = barrierStress * 4.2f + regionalThreatIndex * 0.15f;
        regionalManaEcologyLevel = Mathf.Clamp(regionalManaEcologyLevel + manaPulse - 0.35f, 0f, 100f);

        for (int i = 0; i < nations.Count; i++)
        {
            WestRegionNationRuntime runtime = nations[i];
            if (runtime == null || !runtime.alive)
            {
                continue;
            }

            float weight = runtime.lod == WestRegionLodLevel.Lod0FullMicro ? 1.2f : 0.35f;
            float localMana = manaPulse * weight * (1f - Mathf.Clamp01(runtime.distanceDeg / 40f));
            if (runtime.lod == WestRegionLodLevel.Lod0FullMicro && runtime.microCell?.nature != null)
            {
                runtime.microCell.nature.ApplyManaDelta(localMana);
            }
        }

        try
        {
            NatureEnvironmentStatus ecology = NaturalEcologyEngine.EnsureInstance().Environment;
            if (ecology != null)
            {
                ecology.ApplyManaDelta(manaPulse * 0.65f);
            }
        }
        catch (Exception)
        {
            // Safe-Fail
        }
    }

    private static float ResolveFood(WestRegionNationRuntime runtime, VillageStorageMarket primaryMarket)
    {
        if (runtime?.snapshot == null)
        {
            return 0f;
        }

        if (runtime.snapshot.id == MicroToMacroAggregator.DefaultNationId && primaryMarket != null)
        {
            return primaryMarket.Food;
        }

        if (runtime.lod == WestRegionLodLevel.Lod0FullMicro && runtime.microCell?.market != null)
        {
            return runtime.microCell.market.Food;
        }

        return runtime.abstractFood;
    }

    private static void WithdrawFood(
        WestRegionNationRuntime donor,
        VillageStorageMarket primaryMarket,
        float amount)
    {
        if (donor?.snapshot == null)
        {
            return;
        }

        if (donor.snapshot.id == MicroToMacroAggregator.DefaultNationId && primaryMarket != null)
        {
            primaryMarket.WithdrawFood(amount);
            return;
        }

        if (donor.lod == WestRegionLodLevel.Lod0FullMicro && donor.microCell?.market != null)
        {
            donor.microCell.market.WithdrawFood(amount);
            return;
        }

        donor.abstractFood = Mathf.Max(0f, donor.abstractFood - amount);
    }

    private static void DepositFood(
        WestRegionNationRuntime receiver,
        VillageStorageMarket primaryMarket,
        float amount)
    {
        if (receiver?.snapshot == null)
        {
            return;
        }

        if (receiver.snapshot.id == MicroToMacroAggregator.DefaultNationId && primaryMarket != null)
        {
            receiver.abstractFood = primaryMarket.Food;
            return;
        }

        if (receiver.lod == WestRegionLodLevel.Lod0FullMicro && receiver.microCell?.market != null)
        {
            receiver.microCell.market.Deposit(amount, 0f, 0f, 0f);
            return;
        }

        receiver.abstractFood += amount;
    }

    public int CountLod(List<WestRegionNationRuntime> nations, WestRegionLodLevel lod)
    {
        int count = 0;
        if (nations == null)
        {
            return 0;
        }

        for (int i = 0; i < nations.Count; i++)
        {
            WestRegionNationRuntime runtime = nations[i];
            if (runtime != null && runtime.alive && runtime.lod == lod)
            {
                count++;
            }
        }

        return count;
    }
}

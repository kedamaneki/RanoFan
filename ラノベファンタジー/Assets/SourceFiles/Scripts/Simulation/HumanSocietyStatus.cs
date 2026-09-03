using System;
using System.Globalization;
using UnityEngine;

// =============================================================================
// 人間社会摩擦ステータス — 食料・派閥・技術独占
// =============================================================================

/// <summary>対人間抗争の社会要因（0〜100%）。</summary>
[Serializable]
public sealed class HumanSocietyStatus
{
    [Range(0f, 100f)] public float foodScarcity = 0f;
    [Range(0f, 100f)] public float factionTension = 22f;
    [Range(0f, 100f)] public float techMonopolyLevel = 28f;

    public int phasesSampled = 0;
    public float lastFoodStock = 0f;
    public float lastBarrierPercent = 100f;

    public void ClampAll()
    {
        foodScarcity = Mathf.Clamp(foodScarcity, 0f, 100f);
        factionTension = Mathf.Clamp(factionTension, 0f, 100f);
        techMonopolyLevel = Mathf.Clamp(techMonopolyLevel, 0f, 100f);
    }

    public string FormatSnapshot()
    {
        return $"食料危機{foodScarcity.ToString("F1", CultureInfo.InvariantCulture)}% " +
               $"派閥{factionTension.ToString("F1", CultureInfo.InvariantCulture)}% " +
               $"技術独占{techMonopolyLevel.ToString("F1", CultureInfo.InvariantCulture)}%";
    }

    /// <summary>倉庫・領域ネットワークから食料危機度を更新します。</summary>
    public void RefreshFoodScarcity(VillageStorageMarket market, float regionalFoodDeficit)
    {
        float scarcity = 0f;
        if (market != null)
        {
            lastFoodStock = market.Food;
            if (market.FoodDepleted)
            {
                scarcity = 100f;
            }
            else
            {
                float reserve = RegionalMarketAndThreat.TradeFoodReserve;
                float gap = Mathf.Max(0f, reserve - market.Food);
                scarcity = Mathf.Max(scarcity, gap / reserve * 85f);
                scarcity = Mathf.Max(scarcity, Mathf.Clamp(100f - market.Food * 2.8f, 0f, 100f));
            }
        }

        if (regionalFoodDeficit > 0.5f)
        {
            scarcity = Mathf.Max(scarcity, Mathf.Min(100f, regionalFoodDeficit * 4.5f));
        }

        foodScarcity = scarcity;
        ClampAll();
    }

    /// <summary>位相・村人・技術状況から派閥対立度を更新します。</summary>
    public void RefreshFactionTension(
        VariableTimelineSeason season,
        int inquisitiveVillagerCount,
        bool dormantResearchAttempt)
    {
        float delta = season == VariableTimelineSeason.Active ? 2.8f :
            season == VariableTimelineSeason.Escalation ? 3.6f :
            season == VariableTimelineSeason.Deescalation ? 1.2f :
            season == VariableTimelineSeason.Dormant ? 0.8f : 0.5f;

        factionTension += delta + inquisitiveVillagerCount * 1.5f;
        if (dormantResearchAttempt)
        {
            factionTension += 6f;
        }

        if (season == VariableTimelineSeason.Dormant)
        {
            factionTension += 2.5f;
        }

        ClampAll();
    }

    /// <summary>工房・結晶・新技術解禁から技術独占度を更新します。</summary>
    public void RefreshTechMonopoly(
        VillageStorageMarket market,
        int workshopLevel,
        bool innovationFlagActive,
        bool magicInheritedThisPhase)
    {
        float monopoly = techMonopolyLevel;
        monopoly = Mathf.Max(monopoly, workshopLevel * 14f);
        if (market != null)
        {
            float crystalBias = Mathf.Clamp(market.ManaCrystal * 3.5f, 0f, 35f);
            monopoly = Mathf.Max(monopoly, crystalBias);
        }

        if (innovationFlagActive)
        {
            monopoly = Mathf.Max(monopoly, 72f);
        }

        if (magicInheritedThisPhase)
        {
            monopoly = Mathf.Max(monopoly, 58f);
        }

        techMonopolyLevel = monopoly + factionTension * 0.18f;
        ClampAll();
    }

    public void RefreshBarrierContext(float barrierPercent)
    {
        lastBarrierPercent = barrierPercent;
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

// =============================================================================
// 領域・西 全35カ国 — geo / 歴史ログから個性バイアスを注入
// =============================================================================

/// <summary>国家の地理・歴史由来アーキタイプ。</summary>
public enum NationBiasArchetype
{
    Neutral = 0,
    MountainHighland = 1,
    PlainsAgricultural = 2,
    DisasterRecovery = 3,
    StableAffluent = 4
}

/// <summary>国家個別の生産・行動バイアス。</summary>
[Serializable]
public sealed class NationBiasData
{
    public int nationId;
    public string nationName = string.Empty;
    public NationBiasArchetype primaryArchetype = NationBiasArchetype.Neutral;
    public string label = "標準";
    public float elevationNorm;
    public bool recentDamage;

    public float foodYieldMul = 1f;
    public float oreYieldMul = 1f;
    public float crystalYieldMul = 1f;
    public float timberYieldMul = 1f;
    public float facilityUpkeepMul = 1f;
    public float repairBarrierPriorityMul = 1f;
    public float dormantResearchMul = 1f;
    public float dormantFacilityUpgradeMul = 1f;

    public string FormatShort()
    {
        return $"{label} elev={elevationNorm.ToString("F2", CultureInfo.InvariantCulture)} " +
               $"F×{foodYieldMul:F2} Ore×{oreYieldMul:F2} Cry×{crystalYieldMul:F2} " +
               $"維持×{facilityUpkeepMul:F2} 修復結界×{repairBarrierPriorityMul:F2} " +
               $"休眠研究×{dormantResearchMul:F2} 施設UP×{dormantFacilityUpgradeMul:F2}";
    }
}

/// <summary>
/// geo.json・macro_chronicle_log.json から各国家のバイアスを算出し、
/// NPC Utility AI / LOD1 抽象計算 / ミクロセルへ適用します。
/// </summary>
public static class NationBiasInjector
{
    public const float MountainElevationThreshold = 0.55f;
    public const float PlainsElevationThreshold = 0.35f;
    public const float MountainOreCrystalMul = 1.5f;
    public const float MountainFoodMul = 0.7f;
    public const float PlainsFoodMul = 1.6f;
    public const float PlainsUpkeepMul = 1.2f;
    public const float DisasterRepairBarrierMul = 2f;
    public const float StableDormantMul = 1.8f;
    public const float StableEconomyThreshold = 300f;
    public const float DormantResearchBaseChance = 0.1f;
    public const float DormantFacilityUpgradeBaseChance = 0.07f;

    public static float NpcRepairBarrierPriorityMul { get; private set; } = 1f;
    public static float NpcDormantResearchMul { get; private set; } = 1f;
    public static float NpcDormantFacilityUpgradeMul { get; private set; } = 1f;

    public static void ClearNpcUtilityContext()
    {
        NpcRepairBarrierPriorityMul = 1f;
        NpcDormantResearchMul = 1f;
        NpcDormantFacilityUpgradeMul = 1f;
    }

    public static void ApplyNpcUtilityContext(NationBiasData bias)
    {
        if (bias == null)
        {
            ClearNpcUtilityContext();
            return;
        }

        NpcRepairBarrierPriorityMul = Mathf.Max(1f, bias.repairBarrierPriorityMul);
        NpcDormantResearchMul = Mathf.Max(1f, bias.dormantResearchMul);
        NpcDormantFacilityUpgradeMul = Mathf.Max(1f, bias.dormantFacilityUpgradeMul);
    }

    public static NationBiasData ComputeForNation(
        MacroChronicleNationSnapshot snap,
        int turn,
        List<MacroChronicleLogEvent> turnEvents)
    {
        NationBiasData bias = new NationBiasData
        {
            nationId = snap?.id ?? 0,
            nationName = snap?.name ?? string.Empty
        };

        if (snap == null)
        {
            return bias;
        }

        bias.elevationNorm = SampleElevationNorm(snap.lat, snap.lng);
        bias.recentDamage = HasRecentDamage(snap.id, turnEvents);

        bool mountain = bias.elevationNorm >= MountainElevationThreshold;
        bool plains = bias.elevationNorm <= PlainsElevationThreshold;
        bool stable = !bias.recentDamage && snap.alive && snap.economy >= StableEconomyThreshold;

        if (mountain)
        {
            bias.foodYieldMul *= MountainFoodMul;
            bias.oreYieldMul *= MountainOreCrystalMul;
            bias.crystalYieldMul *= MountainOreCrystalMul;
            bias.primaryArchetype = NationBiasArchetype.MountainHighland;
            bias.label = "山岳高地";
        }

        if (plains)
        {
            bias.foodYieldMul *= PlainsFoodMul;
            bias.facilityUpkeepMul *= PlainsUpkeepMul;
            if (bias.primaryArchetype == NationBiasArchetype.Neutral)
            {
                bias.primaryArchetype = NationBiasArchetype.PlainsAgricultural;
                bias.label = "平野農業";
            }
            else
            {
                bias.label += "+平野";
            }
        }

        if (bias.recentDamage)
        {
            bias.repairBarrierPriorityMul *= DisasterRepairBarrierMul;
            bias.primaryArchetype = NationBiasArchetype.DisasterRecovery;
            bias.label = string.IsNullOrEmpty(bias.label) || bias.label == "標準"
                ? "被災復興"
                : bias.label + "+被災";
        }

        if (stable)
        {
            bias.dormantResearchMul *= StableDormantMul;
            bias.dormantFacilityUpgradeMul *= StableDormantMul;
            if (bias.primaryArchetype == NationBiasArchetype.Neutral)
            {
                bias.primaryArchetype = NationBiasArchetype.StableAffluent;
                bias.label = "安定富裕";
            }
            else if (!bias.label.Contains("安定"))
            {
                bias.label += "+安定";
            }
        }

        if (bias.primaryArchetype == NationBiasArchetype.Neutral)
        {
            bias.label = "標準均衡";
        }

        return bias;
    }

    public static void InjectBatch(
        List<WestRegionNationRuntime> nations,
        int turn,
        string logPath)
    {
        if (nations == null)
        {
            return;
        }

        List<MacroChronicleLogEvent> turnEvents = null;
        try
        {
            turnEvents = MacroChronicleJsonScan.ReadTurnEvents(logPath, turn);
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[NationBiasInjector] ログ読込 Safe-Fail: {exception.Message}");
            turnEvents = new List<MacroChronicleLogEvent>();
        }

        for (int i = 0; i < nations.Count; i++)
        {
            WestRegionNationRuntime runtime = nations[i];
            if (runtime?.snapshot == null)
            {
                continue;
            }

            runtime.bias = ComputeForNation(runtime.snapshot, turn, turnEvents);
            if (runtime.microCell != null)
            {
                runtime.microCell.bias = runtime.bias;
            }
        }
    }

    public static string TryDormantStableAffluentRoll(
        NationBiasData bias,
        int nationId,
        int turn,
        VariableTimelineSeason season)
    {
        if (season != VariableTimelineSeason.Dormant || bias == null || bias.dormantResearchMul <= 1.01f)
        {
            return string.Empty;
        }

        float chance = DormantResearchBaseChance * bias.dormantResearchMul;
        if (UnityEngine.Random.value > chance)
        {
            return string.Empty;
        }

        try
        {
            int workshopLevel = 2;
            try
            {
                VillageInfrastructureEngine infra = VillageInfrastructureEngine.EnsureInstance();
                for (int i = 0; i < infra.BuildingCount; i++)
                {
                    VillageBuildingData b = infra.Buildings[i];
                    if (b != null && b.Type == BuildingType.Workshop)
                    {
                        workshopLevel = Mathf.Max(workshopLevel, b.Level);
                    }
                }
            }
            catch (Exception)
            {
                workshopLevel = 2;
            }

            DynamicMasterGenerationResult result =
                DynamicMasterGenerationEngine.NotifyInnovation(workshopLevel, nationId, turn);
            if (result != null && result.success)
            {
                return $"休眠研究発火({bias.label}) {result.message}";
            }

            return $"休眠研究試行({bias.label}) 工房Lv{workshopLevel}";
        }
        catch (Exception exception)
        {
            return $"休眠研究 Safe-Fail: {exception.Message}";
        }
    }

    public static string TryDormantFacilityUpgradeRoll(NationBiasData bias, VariableTimelineSeason season)
    {
        if (season != VariableTimelineSeason.Dormant || bias == null || bias.dormantFacilityUpgradeMul <= 1.01f)
        {
            return string.Empty;
        }

        float chance = DormantFacilityUpgradeBaseChance * bias.dormantFacilityUpgradeMul;
        if (UnityEngine.Random.value > chance)
        {
            return string.Empty;
        }

        try
        {
            VillageInfrastructureEngine infra = VillageInfrastructureEngine.EnsureInstance();
            VillageBuildingData target = infra.FindMostDamagedRepairable();
            if (target == null)
            {
                return string.Empty;
            }

            float before = target.Durability;
            target.Durability = Mathf.Clamp(target.Durability + 18f, 0f, 100f);
            if (target.Durability >= VillageInfrastructureEngine.RepairThreshold)
            {
                target.IsRepairing = false;
            }

            return $"施設強化({bias.label}) {VillageBuildingData.TypeLabel(target.Type)} " +
                   $"{before:F0}%→{target.Durability:F0}%";
        }
        catch (Exception exception)
        {
            return $"施設強化 Safe-Fail: {exception.Message}";
        }
    }

    public static float SampleElevationNorm(float lat, float lng)
    {
        try
        {
            float refLat = ProceduralTerrainGenerator.ReferenceLatitude;
            float refLng = ProceduralTerrainGenerator.ReferenceLongitude;
            Vector2 village = ProceduralTerrainGenerator.VillageNormalized;
            float nx = Mathf.Clamp01(village.x + (lng - refLng) * 0.045f);
            float nz = Mathf.Clamp01(village.y + (lat - refLat) * 0.055f);
            return Mathf.Clamp01(ProceduralTerrainGenerator.SampleNormalizedHeight(nx, nz));
        }
        catch (Exception)
        {
            return 0.45f;
        }
    }

    private static bool HasRecentDamage(int nationId, List<MacroChronicleLogEvent> turnEvents)
    {
        if (turnEvents == null || nationId < 1)
        {
            return false;
        }

        for (int i = 0; i < turnEvents.Count; i++)
        {
            MacroChronicleLogEvent evt = turnEvents[i];
            if (evt == null || !evt.ConcernsNation(nationId))
            {
                continue;
            }

            if (evt.militaryLoss > 0.5f || evt.territoryLoss > 0.5f || evt.economyLoss > 0.5f)
            {
                return true;
            }

            string category = evt.category ?? string.Empty;
            if (category.IndexOf("attack", StringComparison.OrdinalIgnoreCase) >= 0 ||
                category.IndexOf("defense", StringComparison.OrdinalIgnoreCase) >= 0 ||
                category.IndexOf("disaster", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            string text = evt.eventText ?? string.Empty;
            if (text.Contains("被害") || text.Contains("損失") || text.Contains("破壊"))
            {
                return true;
            }
        }

        return false;
    }
}

using UnityEngine;

// =============================================================================
// Utility AI — 位相と個人状態から生活行動を選ぶ
// =============================================================================

/// <summary>社会生活の自律行動。</summary>
public enum NpcLifeAction
{
    Work = 0,
    Eat = 1,
    Rest = 2,
    Flee = 3,
    MaintainBarrier = 4,
    RepairBuilding = 5,
    EmergencyRepair = 6
}

/// <summary>スコア最大の行動とその理由。</summary>
public struct NpcUtilityDecision
{
    public NpcLifeAction Action;
    public float Score;
    public string Reason;
}

/// <summary>
/// Hunger / Danger / 活性・休眠 / 施設耐久に応じて行動を選びます。
/// </summary>
public static class NpcUtilityAI
{
    /// <summary>結界が危険域とみなす維持率（%）。</summary>
    public const float BarrierDangerPercent = 42f;

    /// <summary>
    /// JobId に対応する近隣作業スポットを取得します。
    /// 未配置時は (0,0,0) フォールバック（VillageWorkSpotManager）。
    /// </summary>
    public static WorkSpotData GetOptimalWorkSpot(string jobId, Vector3 npcPosition)
    {
        return VillageWorkSpotManager.ResolveOptimalWorkSpot(jobId, npcPosition);
    }

    public static NpcUtilityDecision Decide(
        NpcIndividualStatus npc,
        VariableTimelineSeason season,
        float barrierPercent,
        bool storageHasFood,
        bool storageHasCrystal)
    {
        float lowestBuilding = 100f;
        bool hasTimberOrOre = true;
        try
        {
            VillageInfrastructureEngine infra = VillageInfrastructureEngine.EnsureInstance();
            lowestBuilding = infra.LowestRepairableDurability();
            VillageStorageMarket storage = NpcCivilizationEngine.EnsureInstance()?.Storage;
            hasTimberOrOre = storage == null || storage.Timber > 0.05f || storage.Ore > 0.05f;
        }
        catch (System.Exception)
        {
            lowestBuilding = 100f;
            hasTimberOrOre = true;
        }

        return Decide(
            npc,
            season,
            barrierPercent,
            storageHasFood,
            storageHasCrystal,
            lowestBuilding,
            hasTimberOrOre);
    }

    public static NpcUtilityDecision Decide(
        NpcIndividualStatus npc,
        VariableTimelineSeason season,
        float barrierPercent,
        bool storageHasFood,
        bool storageHasCrystal,
        float lowestBuildingDurability,
        bool storageHasRepairMats)
    {
        NpcIndividualStatus safe = npc ?? new NpcIndividualStatus();
        safe.ClampVitals();

        try
        {
            float work = ScoreWork(safe, season);
            float eat = ScoreEat(safe, storageHasFood);
            float rest = ScoreRest(safe, season);
            float flee = ScoreFlee(safe, season, barrierPercent);
            float barrier = ScoreMaintainBarrier(safe, season, barrierPercent, storageHasCrystal);
            float repair = ScoreRepairBuilding(safe, lowestBuildingDurability, storageHasRepairMats);
            float emergency = ScoreEmergencyRepair(safe);

            NpcLifeAction action = NpcLifeAction.Work;
            float best = work;
            string reason = BuildWorkReason(safe, season);

            best = TakeIfBetter(eat, NpcLifeAction.Eat, BuildEatReason(safe, storageHasFood), ref action, ref reason, best);
            best = TakeIfBetter(rest, NpcLifeAction.Rest, BuildRestReason(safe), ref action, ref reason, best);
            best = TakeIfBetter(flee, NpcLifeAction.Flee, BuildFleeReason(season, barrierPercent), ref action, ref reason, best);
            best = TakeIfBetter(barrier, NpcLifeAction.MaintainBarrier, BuildBarrierReason(safe, barrierPercent), ref action, ref reason, best);
            best = TakeIfBetter(
                repair,
                NpcLifeAction.RepairBuilding,
                $"施設耐久 {lowestBuildingDurability:F0}%",
                ref action,
                ref reason,
                best);
            best = TakeIfBetter(
                emergency,
                NpcLifeAction.EmergencyRepair,
                BuildEmergencyReason(safe),
                ref action,
                ref reason,
                best);

            return new NpcUtilityDecision
            {
                Action = action,
                Score = best,
                Reason = reason
            };
        }
        catch (System.Exception exception)
        {
            Debug.LogWarning($"[NpcUtilityAI] 行動選択 Safe-Fail: {exception.Message}");
            return new NpcUtilityDecision
            {
                Action = NpcLifeAction.Rest,
                Score = 1f,
                Reason = "判定例外・休息フォールバック"
            };
        }
    }

    public static string ActionLabel(NpcLifeAction action)
    {
        switch (action)
        {
            case NpcLifeAction.Eat:
                return "食事";
            case NpcLifeAction.Rest:
                return "休息";
            case NpcLifeAction.Flee:
                return "避難";
            case NpcLifeAction.MaintainBarrier:
                return "結界維持";
            case NpcLifeAction.RepairBuilding:
                return "施設修復";
            case NpcLifeAction.EmergencyRepair:
                return "緊急修復";
            default:
                return "仕事";
        }
    }

    private static float ScoreWork(NpcIndividualStatus npc, VariableTimelineSeason season)
    {
        float score = 28f + npc.JobProficiency * 8f;
        if (season == VariableTimelineSeason.Dormant)
        {
            score += 38f;
        }
        else if (season == VariableTimelineSeason.Deescalation)
        {
            score += 22f;
        }
        else if (season == VariableTimelineSeason.Active)
        {
            score -= 18f;
        }

        if (npc.Trait == NpcTrait.Diligent)
        {
            score += 14f;
        }

        if (npc.Trait == NpcTrait.Inquisitive && season == VariableTimelineSeason.Dormant)
        {
            score += 6f;
        }

        score -= npc.Hunger * 0.22f;
        if (npc.IsStarving)
        {
            score -= 24f;
        }

        return score;
    }

    private static float ScoreEat(NpcIndividualStatus npc, bool storageHasFood)
    {
        float score = npc.Hunger * 0.85f;
        if (npc.Hunger >= 70f)
        {
            score += 28f;
        }

        if (!storageHasFood)
        {
            score *= 0.35f;
            score += npc.IsStarving ? 12f : 0f;
        }

        return score;
    }

    private static float ScoreRest(NpcIndividualStatus npc, VariableTimelineSeason season)
    {
        float score = (1f - npc.HpRatio) * 42f + (1f - npc.MpRatio) * 18f;
        if (season == VariableTimelineSeason.Dormant)
        {
            score += 8f;
        }

        if (npc.Hunger > 75f)
        {
            score -= 10f;
        }

        return score;
    }

    private static float ScoreFlee(NpcIndividualStatus npc, VariableTimelineSeason season, float barrierPercent)
    {
        bool hot = season == VariableTimelineSeason.Active || season == VariableTimelineSeason.Escalation;
        if (!hot && barrierPercent > BarrierDangerPercent)
        {
            return npc.Trait == NpcTrait.Cautious ? 6f : 0f;
        }

        float score = hot ? 48f : 12f;
        if (barrierPercent < BarrierDangerPercent)
        {
            score += (BarrierDangerPercent - barrierPercent) * 1.1f;
        }

        if (npc.Trait == NpcTrait.Cautious)
        {
            score += 22f;
        }

        if (npc.CivicJob == NpcCivicJob.BarrierKeeper)
        {
            score -= 16f;
        }

        return score;
    }

    private static float ScoreMaintainBarrier(
        NpcIndividualStatus npc,
        VariableTimelineSeason season,
        float barrierPercent,
        bool storageHasCrystal)
    {
        float need = Mathf.Max(0f, 100f - barrierPercent);
        float score = need * 0.55f;
        if (npc.CivicJob == NpcCivicJob.BarrierKeeper)
        {
            score += 36f;
        }
        else if (npc.KnowsMagic("MAG_BARRIER_SEAL"))
        {
            score += 14f;
        }
        else
        {
            score *= 0.35f;
        }

        if (season == VariableTimelineSeason.Active || season == VariableTimelineSeason.Escalation)
        {
            score += 20f;
        }

        if (barrierPercent < 28f)
        {
            score += 30f;
        }

        if (!storageHasCrystal)
        {
            score *= 0.4f;
        }

        score *= NationBiasInjector.NpcRepairBarrierPriorityMul;
        return score;
    }

    /// <summary>大工・鍛冶は施設耐久低下で修復スコアが跳ね上がります。</summary>
    private static float ScoreRepairBuilding(
        NpcIndividualStatus npc,
        float lowestDurability,
        bool hasMats)
    {
        bool canRepair = npc.CivicJob == NpcCivicJob.Carpenter ||
                         npc.CivicJob == NpcCivicJob.Blacksmith;
        if (!canRepair)
        {
            return -5f;
        }

        if (lowestDurability >= VillageInfrastructureEngine.RepairThreshold)
        {
            return 4f;
        }

        float deficit = VillageInfrastructureEngine.RepairThreshold - lowestDurability;
        float score = 20f + deficit * 1.35f;
        if (npc.CivicJob == NpcCivicJob.Carpenter)
        {
            score += 18f;
        }
        else
        {
            score += 10f;
        }

        if (lowestDurability < 35f)
        {
            score += 30f;
        }

        if (!hasMats)
        {
            score *= 0.45f;
            score += 8f;
        }

        score *= NationBiasInjector.NpcRepairBarrierPriorityMul;
        return score;
    }

    /// <summary>Breach Point 発生時、結界守の緊急修復を他行動より優先します。</summary>
    private static float ScoreEmergencyRepair(NpcIndividualStatus npc)
    {
        bool breached = false;
        try
        {
            VillageBarrierBreachEngine engine = VillageBarrierBreachEngine.Instance;
            breached = engine != null && engine.HasActiveBreach;
        }
        catch (System.Exception)
        {
            return -20f;
        }

        if (!breached)
        {
            return -20f;
        }

        if (npc.CivicJob == NpcCivicJob.BarrierKeeper)
        {
            return 2500f;
        }

        return 12f;
    }

    private static float TakeIfBetter(
        float candidate,
        NpcLifeAction action,
        string reason,
        ref NpcLifeAction chosen,
        ref string chosenReason,
        float best)
    {
        if (candidate > best)
        {
            chosen = action;
            chosenReason = reason;
            return candidate;
        }

        return best;
    }

    private static string BuildWorkReason(NpcIndividualStatus npc, VariableTimelineSeason season)
    {
        string job = NpcJobProfile.JobLabel(npc.CivicJob);
        return season == VariableTimelineSeason.Dormant
            ? $"休眠期・{job}の評価が高い"
            : $"{job}継続";
    }

    private static string BuildEatReason(NpcIndividualStatus npc, bool hasFood)
    {
        return !hasFood ? "空腹だが倉庫食料なし" : $"Hunger {npc.Hunger:F0}";
    }

    private static string BuildRestReason(NpcIndividualStatus npc)
    {
        return $"HP比 {npc.HpRatio:P0}";
    }

    private static string BuildFleeReason(VariableTimelineSeason season, float barrierPercent)
    {
        return $"危険（{season} / 結界 {barrierPercent:F0}%）";
    }

    private static string BuildBarrierReason(NpcIndividualStatus npc, float barrierPercent)
    {
        return npc.CivicJob == NpcCivicJob.BarrierKeeper
            ? $"結界守・維持率 {barrierPercent:F0}%"
            : $"結界低下 {barrierPercent:F0}%";
    }

    private static string BuildEmergencyReason(NpcIndividualStatus npc)
    {
        string spotId = string.Empty;
        try
        {
            spotId = VillageBarrierBreachEngine.Instance != null
                ? VillageBarrierBreachEngine.Instance.BreachSpotId
                : string.Empty;
        }
        catch (System.Exception)
        {
            spotId = string.Empty;
        }

        return npc.CivicJob == NpcCivicJob.BarrierKeeper
            ? $"緊急修復 → {spotId}"
            : "裂け目警戒";
    }
}

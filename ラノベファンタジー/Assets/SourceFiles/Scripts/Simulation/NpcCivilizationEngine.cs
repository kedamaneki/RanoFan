using System.Collections.Generic;
using System.Text;
using UnityEngine;
#if UNITY_EDITOR
using System.IO;
using UnityEditor;
#endif

// =============================================================================
// NPC 自律生活シミュレーション — 個別ステータス + Utility AI + 倉庫物流
// 連携: NpcIndividualStatus / NpcUtilityAI / VillageStorageMarket
//       VillageBarrierCore / VillageAutonomyEngine / MicroPhaseSimUI
// =============================================================================

/// <summary>
/// プレイヤー介在なしで、NPC が仕事・食事・休息・避難・結界維持・技術伝承を行います。
/// </summary>
[DefaultExecutionOrder(51)]
public class NpcCivilizationEngine : MonoBehaviour
{
    public static NpcCivilizationEngine Instance { get; private set; }

    [SerializeField] private List<NpcIndividualStatus> villagers = new List<NpcIndividualStatus>();
    [SerializeField] private VillageStorageMarket storage = new VillageStorageMarket();
    [SerializeField] private int lastTickedPhase = -1;

    public IReadOnlyList<NpcIndividualStatus> Villagers => villagers;
    public VillageStorageMarket Storage => storage ?? (storage = new VillageStorageMarket());
    public int LastTickedPhase => lastTickedPhase;

    /// <summary>歴史主要人物を村人リストへ登録します（同一 ID は上書き）。</summary>
    public bool TryRegisterHistoricalNpc(NpcIndividualStatus npc)
    {
        try
        {
            EnsureDefaults();
            if (npc == null || string.IsNullOrWhiteSpace(npc.NpcId))
            {
                return false;
            }

            villagers ??= new List<NpcIndividualStatus>();
            for (int i = 0; i < villagers.Count; i++)
            {
                if (villagers[i] != null &&
                    string.Equals(villagers[i].NpcId, npc.NpcId, System.StringComparison.OrdinalIgnoreCase))
                {
                    villagers[i] = npc;
                    return true;
                }
            }

            villagers.Add(npc);
            return true;
        }
        catch (System.Exception exception)
        {
            Debug.LogWarning($"[NpcCivilizationEngine] TryRegisterHistoricalNpc Safe-Fail: {exception.Message}");
            return false;
        }
    }

    public static NpcCivilizationEngine EnsureInstance()
    {
        if (Instance != null)
        {
            return Instance;
        }

        NpcCivilizationEngine existing = Object.FindAnyObjectByType<NpcCivilizationEngine>();
        if (existing != null)
        {
            Instance = existing;
            return existing;
        }

        GameObject hub = GameObject.Find("DebugSystemsHub");
        GameObject host = hub != null ? hub : new GameObject(nameof(NpcCivilizationEngine));
        NpcCivilizationEngine engine = host.GetComponent<NpcCivilizationEngine>();
        if (engine == null)
        {
            engine = host.AddComponent<NpcCivilizationEngine>();
        }

        Instance = engine;
        return Instance;
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        EnsureDefaults();
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    /// <summary>1 位相分の生活サイクルです。</summary>
    public void TickPhase(int phase, VariableTimelineSeason season)
    {
        try
        {
            EnsureDefaults();
            VillageAutonomyEngine village = VillageAutonomyEngine.EnsureInstance();
            VillageBarrierCore barrier = village.Barrier;
            VillageStorageMarket market = Storage;
            VillageInfrastructureEngine infra = VillageInfrastructureEngine.EnsureInstance();
            int normalized = ProceduralMapPopulator.NormalizePhase(phase);
            float tech = VillageAutonomyEngine.ResolveMagicTechMultiplier();

            if (!StoryTimelineManager.TryPreparePhaseTick(normalized, season))
            {
                lastTickedPhase = normalized;
                if (StoryTimelineManager.EnsureInstance().YearCompleted)
                {
                    TryTickSatellitePhases(normalized, season, market, barrier);
                }

                return;
            }

            MicroToMacroAggregator.EnsureInstance().BeginPhaseBarrierTracking();
            barrier.ApplySeasonPressure(season);
            infra.ApplySeasonPressure(season, barrier.BarrierDropped);

            VillageBarrierBreachEngine breach = VillageBarrierBreachEngine.EnsureInstance();
            breach.Evaluate(normalized, season, barrier);

            if (villagers == null || CountLiving() == 0)
            {
                Debug.LogWarning(
                    "[NpcCivilizationEngine] NPC が存在しません。生活シミュレーションをスキップします（Safe-Fail）。");
                breach.NotifyDefenseLineCollapse("結界守が存在しない");
                try
                {
                    NaturalEcologyEngine.EnsureInstance().TickPhase(normalized, season);
                }
                catch (System.Exception)
                {
                    // 自然生態は独立継続
                }

                lastTickedPhase = normalized;
                StoryTimelineManager.TryEmitPhaseDrama(normalized, season);
                HumanConflictEngine.TryTickHumanSocietyPhase(normalized, season, market, barrier);
                WestRegionSimulationManager.TryTickWestRegionPhase(normalized, season);
                RegionalExpansionEngine.TryTickRegionalPhase(normalized, season);
                GeographicNaturalDisasterEngine.TryTickPhase(normalized, season, market, barrier);
                TryAggregateMacro(normalized, season);
                return;
            }

            if (market.FoodDepleted)
            {
                Debug.LogWarning(
                    "<color=#FFCC80><b>【飢餓警告】</b></color> 倉庫の食料が 0 です。食事は失敗し、作業効率が低下します。");
            }

            float restMul = infra.ResolveRestRecoveryMultiplier();
            float hungerMul = infra.ResolveHungerGrowthMultiplier();
            float lowestBuilding = infra.LowestRepairableDurability();
            bool hasRepairMats = market.Timber > 0.05f || market.Ore > 0.05f;

            NationBiasData nationBias = WestRegionSimulationManager.TryGetNationBias(
                MicroToMacroAggregator.DefaultNationId);
            NationBiasInjector.ApplyNpcUtilityContext(nationBias);

            StringBuilder log = new StringBuilder();
            log.Append("<color=#CE93D8><b>【NPC文明】</b></color> ");
            log.Append($"位相 {normalized}/24 {season.ToString().ToUpperInvariant()} ");
            log.Append($"結界 {barrier.Efficiency:F1}% 倉庫 {market.FormatSnapshot()} ");
            log.Append(infra.FormatSnapshot());

            for (int i = 0; i < villagers.Count; i++)
            {
                NpcIndividualStatus npc = villagers[i];
                if (npc == null)
                {
                    continue;
                }

                npc.ClampVitals();
                NpcUtilityDecision decision = NpcUtilityAI.Decide(
                    npc,
                    season,
                    barrier.Efficiency,
                    market.HasFood,
                    market.HasCrystal,
                    lowestBuilding,
                    hasRepairMats);

                string detail = ExecuteAction(npc, decision.Action, season, tech, market, barrier, infra, restMul);
                npc.Hunger = Mathf.Clamp(
                    npc.Hunger + HungerDrift(decision.Action) * hungerMul,
                    0f,
                    100f);
                npc.ClampVitals();

                log.AppendLine();
                log.Append(
                    $"  {npc.Name}（{npc.JobId}/{NpcJobProfile.JobLabel(npc.CivicJob)} 熟練{npc.JobProficiency:F1} {npc.Trait}）");
                log.Append($" → {NpcUtilityAI.ActionLabel(decision.Action)} [{decision.Reason}] {detail}");
                if (!string.IsNullOrEmpty(npc.AssignedSpotId))
                {
                    log.Append($" @ {npc.AssignedSpotId}");
                }

                log.Append($" | {npc.FormatVitals()}");
            }

            breach.FinalizePhase(CountLivingKeepers(), market.HasCrystal, barrier);

            if (season == VariableTimelineSeason.Active || season == VariableTimelineSeason.Escalation)
            {
                if (!barrier.BarrierDropped && barrier.Efficiency > NpcUtilityAI.BarrierDangerPercent)
                {
                    StoryTimelineManager.TryNotifyDefenseSuccess(
                        MicroToMacroAggregator.DefaultNationId,
                        normalized,
                        $"結界{barrier.Efficiency:F1}%を維持");
                }
            }

            try
            {
                NaturalEcologyEngine.EnsureInstance().TickPhase(normalized, season);
            }
            catch (System.Exception natureException)
            {
                Debug.LogWarning($"[NpcCivilizationEngine] 自然生態ティックをスキップ: {natureException.Message}");
            }

            if (season == VariableTimelineSeason.Dormant)
            {
                string inherit = TryInheritMagic();
                if (!string.IsNullOrEmpty(inherit))
                {
                    log.AppendLine();
                    log.Append($"  <b>伝承</b> {inherit}");
                    HumanConflictEngine.NotifyMagicInheritedThisPhase();
                }

                string research = NationBiasInjector.TryDormantStableAffluentRoll(
                    nationBias,
                    MicroToMacroAggregator.DefaultNationId,
                    MicroHistoryTimelineTimelineEngine.DefaultTurn,
                    season);
                if (!string.IsNullOrEmpty(research))
                {
                    log.AppendLine();
                    log.Append($"  <b>休眠研究</b> {research}");
                }

                string facility = NationBiasInjector.TryDormantFacilityUpgradeRoll(nationBias, season);
                if (!string.IsNullOrEmpty(facility))
                {
                    log.AppendLine();
                    log.Append($"  <b>施設強化</b> {facility}");
                }
            }

            NationBiasInjector.ClearNpcUtilityContext();

            HumanConflictEngine.TryTickHumanSocietyPhase(normalized, season, market, barrier);

            market.SyncToVillageResources(village.Resources);
            lastTickedPhase = normalized;
            log.AppendLine();
            log.Append($"  期末倉庫 {market.FormatSnapshot()} / 結界 {barrier.Efficiency:F1}%");
            if (barrier.BarrierDropped)
            {
                log.Append(" / BarrierDrop");
            }

            Debug.Log(log.ToString());

            StoryTimelineManager.TryEmitPhaseDrama(normalized, season);

            WestRegionSimulationManager.TryTickWestRegionPhase(normalized, season);
            RegionalExpansionEngine.TryTickRegionalPhase(normalized, season);

            GeographicNaturalDisasterEngine.TryTickPhase(normalized, season, market, barrier);

            TryAggregateMacro(normalized, season);
        }
        catch (System.Exception exception)
        {
            Debug.LogWarning($"[NpcCivilizationEngine] TickPhase Safe-Fail: {exception.Message}");
        }
    }

    /// <summary>方針C: 年次完了後も自然・社会・広域サテライトを継続します。</summary>
    private static void TryTickSatellitePhases(
        int normalized,
        VariableTimelineSeason season,
        VillageStorageMarket market,
        VillageBarrierCore barrier)
    {
        try
        {
            NaturalEcologyEngine.EnsureInstance().TickPhase(normalized, season);
        }
        catch (System.Exception)
        {
            // 自然生態は独立継続
        }

        HumanConflictEngine.TryTickHumanSocietyPhase(normalized, season, market, barrier);
        WestRegionSimulationManager.TryTickWestRegionPhase(normalized, season);
        RegionalExpansionEngine.TryTickRegionalPhase(normalized, season);
        GeographicNaturalDisasterEngine.TryTickPhase(normalized, season, market, barrier);
    }

    private static void TryAggregateMacro(int normalized, VariableTimelineSeason season)
    {
        try
        {
            MicroToMacroAggregator aggregator = MicroToMacroAggregator.EnsureInstance();
            aggregator.AggregatePhase(normalized, season);
            if (normalized >= ProceduralMapPopulator.PhaseCount)
            {
                aggregator.AggregateYearEnd(logMacro: true);
                WestRegionSimulationManager.TryCompleteYearEnd();
            }
        }
        catch (System.Exception aggException)
        {
            Debug.LogWarning($"[NpcCivilizationEngine] マクロ還元位相をスキップ: {aggException.Message}");
        }
    }

    public void SimulateFullDay(VariablePhaseDistribution distribution)
    {
        VariablePhaseDistribution dist = distribution ?? ProceduralMapPopulator.DefaultNation001Turn1Distribution();
        try
        {
            MicroToMacroAggregator.EnsureInstance().BeginYearBaseline();
        }
        catch (System.Exception)
        {
            // Safe-Fail
        }

        for (int phase = 1; phase <= ProceduralMapPopulator.PhaseCount; phase++)
        {
            VariableTimelineSeason season = ProceduralMapPopulator.ResolveSeason(phase, dist);
            TickPhase(phase, season);
        }

        try
        {
            MicroToMacroAggregator.EnsureInstance().AggregateYearEnd(logMacro: true);
        }
        catch (System.Exception exception)
        {
            Debug.LogWarning($"[NpcCivilizationEngine] 年次マクロ還元 Safe-Fail: {exception.Message}");
        }
    }

    private string ExecuteAction(
        NpcIndividualStatus npc,
        NpcLifeAction action,
        VariableTimelineSeason season,
        float tech,
        VillageStorageMarket market,
        VillageBarrierCore barrier,
        VillageInfrastructureEngine infra,
        float restMul)
    {
        switch (action)
        {
            case NpcLifeAction.Eat:
                return ExecuteEat(npc, market);
            case NpcLifeAction.Rest:
                return ExecuteRest(npc, restMul);
            case NpcLifeAction.Flee:
                return ExecuteFlee(npc, season);
            case NpcLifeAction.MaintainBarrier:
                return ExecuteMaintainBarrier(npc, tech, market, barrier, season);
            case NpcLifeAction.RepairBuilding:
                return ExecuteRepairBuilding(npc, market, infra);
            case NpcLifeAction.EmergencyRepair:
                return ExecuteEmergencyRepair(npc, tech, market, barrier, season);
            default:
                return ExecuteWork(npc, season, tech, market);
        }
    }

    private static string ExecuteRepairBuilding(
        NpcIndividualStatus npc,
        VillageStorageMarket market,
        VillageInfrastructureEngine infra)
    {
        if (infra == null)
        {
            infra = VillageInfrastructureEngine.EnsureInstance();
        }

        VillageBuildingData target = infra.FindMostDamagedRepairable();
        if (target == null)
        {
            return "修復対象なし（耐久十分）";
        }

        if (target.SceneAnchor != null)
        {
            npc.WorldPosition = target.SceneAnchor.position;
        }

        npc.AssignedSpotId = string.IsNullOrEmpty(target.LinkedSpotId)
            ? target.BuildingId
            : target.LinkedSpotId;
        npc.CurrentHP = Mathf.Max(1f, npc.CurrentHP - 3f);
        npc.CurrentMP = Mathf.Max(0f, npc.CurrentMP - 4f);
        return infra.TryRepair(target, npc, market);
    }

    private static WorkSpotData AssignAndMoveToSpot(NpcIndividualStatus npc, bool preferBarrier)
    {
        Vector3 from = npc.WorldPosition;
        if (from.sqrMagnitude < 0.01f)
        {
            from = VillageWorkSpotManager.EnsureInstance().ResolveVillageCenterOrFallback();
            npc.WorldPosition = from;
        }

        string jobKey = preferBarrier
            ? nameof(NpcCivicJob.BarrierKeeper)
            : npc.JobId;
        WorkSpotData spot = NpcUtilityAI.GetOptimalWorkSpot(jobKey, from);
        if (spot == null)
        {
            spot = WorkSpotData.CreateFallback(VillageWorkSpotManager.FallbackWorldPosition);
        }

        npc.WorldPosition = spot.WorldPosition;
        npc.AssignedSpotId = spot.SpotId ?? string.Empty;
        return spot;
    }

    private static int ResolveWorkshopLevelForSpot(WorkSpotData spot)
    {
        try
        {
            VillageInfrastructureEngine infra = VillageInfrastructureEngine.EnsureInstance();
            if (infra?.Buildings == null)
            {
                return 1;
            }

            string spotId = spot != null ? spot.SpotId : string.Empty;
            int best = 1;
            for (int i = 0; i < infra.Buildings.Count; i++)
            {
                VillageBuildingData b = infra.Buildings[i];
                if (b == null || b.Type != BuildingType.Workshop)
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(spotId) &&
                    string.Equals(b.LinkedSpotId, spotId, System.StringComparison.OrdinalIgnoreCase))
                {
                    return Mathf.Clamp(b.Level, 1, 5);
                }

                best = Mathf.Max(best, b.Level);
            }

            return best;
        }
        catch (System.Exception)
        {
            return 1;
        }
    }

    private static string ExecuteEat(NpcIndividualStatus npc, VillageStorageMarket market)
    {
        const float meal = 1.4f;
        float taken = market.WithdrawFood(meal);
        if (taken < 0.05f)
        {
            npc.CurrentHP = Mathf.Max(1f, npc.CurrentHP - 4f);
            npc.Hunger = Mathf.Min(100f, npc.Hunger + 6f);
            return "食事失敗（倉庫食料0・飢餓）";
        }

        float fill = 28f * (taken / meal);
        npc.Hunger = Mathf.Max(0f, npc.Hunger - fill);
        npc.CurrentHP = Mathf.Min(npc.MaxHP, npc.CurrentHP + 6f);
        return $"食料消費 {taken:F1}";
    }

    private static string ExecuteRest(NpcIndividualStatus npc)
    {
        return ExecuteRest(npc, 1f);
    }

    private static string ExecuteRest(NpcIndividualStatus npc, float restMul)
    {
        float mul = restMul > 0.01f && !float.IsNaN(restMul)
            ? Mathf.Clamp(restMul, VillageInfrastructureEngine.RestMulMin, VillageInfrastructureEngine.RestMulMax)
            : VillageInfrastructureEngine.DefaultRestMul;
        float hpGain = 12f * mul;
        float staminaGain = 10f * mul; // NPC は MP をスタミナ相当として回復
        npc.CurrentHP = Mathf.Min(npc.MaxHP, npc.CurrentHP + hpGain);
        npc.CurrentMP = Mathf.Min(npc.MaxMP, npc.CurrentMP + staminaGain);
        return $"体力・スタミナ回復（家屋補正×{mul:F2} HP+{hpGain:F0} Stam+{staminaGain:F0}）";
    }

    private static string ExecuteFlee(NpcIndividualStatus npc, VariableTimelineSeason season)
    {
        npc.CurrentHP = Mathf.Min(npc.MaxHP, npc.CurrentHP + 4f);
        npc.CurrentMP = Mathf.Max(0f, npc.CurrentMP - 3f);
        return season == VariableTimelineSeason.Active ? "工房へ退避" : "安全域へ移動";
    }

    private static string ExecuteMaintainBarrier(
        NpcIndividualStatus npc,
        float tech,
        VillageStorageMarket market,
        VillageBarrierCore barrier,
        VariableTimelineSeason season)
    {
        WorkSpotData spot = AssignAndMoveToSpot(npc, preferBarrier: true);
        float spotMul = spot != null ? Mathf.Max(0.25f, spot.YieldEfficiency) : 0.35f;
        float want = (0.5f + npc.JobProficiency * 0.12f) * spotMul;
        if (spot != null && spot.ResourceYield.manaCrystal < 0f)
        {
            want = Mathf.Max(want, Mathf.Abs(spot.ResourceYield.manaCrystal) * spotMul);
        }

        float spent = market.WithdrawCrystal(want);
        bool shortage = spent + 0.001f < want;
        float recovered = barrier.RestoreFromKeeper(spent, tech, shortage);
        npc.CurrentMP = Mathf.Max(0f, npc.CurrentMP - 8f);
        npc.CurrentHP = Mathf.Max(1f, npc.CurrentHP - 2f);

        try
        {
            MicroToMacroAggregator.EnsureInstance()
                .NotifyBarrierHeroRepair(season, barrier.Efficiency);
        }
        catch (System.Exception)
        {
            // Safe-Fail
        }

        string spotLabel = spot != null
            ? $"{WorkSpotData.TypeLabel(spot.Type)}@{spot.SpotId}"
            : "FALLBACK(0,0,0)";
        return shortage
            ? $"[{spotLabel}] 結晶不足 {spent:F2} 結界+{recovered:F1}%→{barrier.Efficiency:F1}%"
            : $"[{spotLabel}] 結晶 {spent:F2} 結界+{recovered:F1}%→{barrier.Efficiency:F1}%";
    }

    private static string ExecuteEmergencyRepair(
        NpcIndividualStatus npc,
        float tech,
        VillageStorageMarket market,
        VillageBarrierCore barrier,
        VariableTimelineSeason season)
    {
        VillageBarrierBreachEngine breach = VillageBarrierBreachEngine.EnsureInstance();
        WorkSpotData spot = breach.ActiveBreachSpot;
        if (spot == null)
        {
            spot = AssignAndMoveToSpot(npc, preferBarrier: true);
        }
        else
        {
            npc.WorldPosition = spot.WorldPosition;
            npc.AssignedSpotId = spot.SpotId ?? string.Empty;
        }

        if (npc.CivicJob != NpcCivicJob.BarrierKeeper)
        {
            return $"[EmergencyRepair] JobId={npc.JobId} は結界守ではないため杭警戒のみ";
        }

        const float crystalNeed = 2.2f;
        float spent = market != null ? market.WithdrawCrystal(crystalNeed) : 0f;
        bool depleted = spent <= 0.05f;
        if (depleted)
        {
            breach.NotifyDefenseLineCollapse("ManaCrystal が枯渇している");
            breach.MarkRepairAttempt(recovered: false);
            string loc = spot != null ? spot.SpotId : "FALLBACK";
            return $"[{loc}] 結晶を持たず現地到着（修復不能）";
        }

        float recoverPct = Mathf.Clamp(15f + npc.JobProficiency * 3.7f, 15f, 25f);
        if (tech > 1f)
        {
            recoverPct = Mathf.Min(25f, recoverPct + (tech - 1f) * 2f);
        }

        float recovered = barrier.RestoreDirectPercent(recoverPct);
        npc.CurrentMP = Mathf.Max(0f, npc.CurrentMP - 12f);
        npc.CurrentHP = Mathf.Max(1f, npc.CurrentHP - 3f);
        breach.MarkRepairAttempt(recovered: recovered > 0.01f);
        breach.PlayKeeperRepairPulse(spot);

        try
        {
            MicroToMacroAggregator.EnsureInstance()
                .NotifyBarrierHeroRepair(season, barrier.Efficiency);
        }
        catch (System.Exception)
        {
            // Safe-Fail
        }

        string spotLabel = spot != null
            ? $"{WorkSpotData.TypeLabel(spot.Type)}@{spot.SpotId}"
            : "FALLBACK";
        return $"[{spotLabel}] 結晶を抱えて現地修復 Crystal-{spent:F1} 結界+{recovered:F1}%→{barrier.Efficiency:F1}%";
    }

    private static string ExecuteWork(
        NpcIndividualStatus npc,
        VariableTimelineSeason season,
        float tech,
        VillageStorageMarket market)
    {
        WorkSpotData spot = AssignAndMoveToSpot(npc, preferBarrier: false);
        float hungerMul = npc.IsStarving ? VillageStorageMarket.DepletionEfficiency : (npc.IsHungry ? 0.7f : 1f);
        float outdoor = season == VariableTimelineSeason.Active ? 0.45f : (season == VariableTimelineSeason.Dormant ? 1f : 0.75f);
        float skill = npc.JobProficiency * tech * hungerMul;
        float spotMul = spot != null ? Mathf.Max(0.25f, spot.YieldEfficiency) : 0.35f;
        bool usedFail = hungerMul < 0.99f || (spot != null && spot.IsFallback);
        float food = 0f;
        float timber = 0f;
        float ore = 0f;
        float crystal = 0f;
        string label;

        WorkSpotResourceYield yield = spot != null
            ? spot.ResourceYield
            : WorkSpotResourceYield.ForType(WorkSpotType.Farmland);

        switch (npc.CivicJob)
        {
            case NpcCivicJob.Woodcutter:
                timber = Mathf.Max(0f, yield.timber) * skill * outdoor * spotMul;
                crystal = Mathf.Max(0f, yield.manaCrystal) * skill * outdoor * spotMul;
                WoodcutterHarvestResult harvest = NaturalEcologyEngine.EnsureInstance()
                    .HarvestAtCamp(spot, npc.JobProficiency, market);
                timber *= harvest.ForestEfficiency * harvest.QualityMultiplier;
                crystal *= harvest.ForestEfficiency;
                usedFail = usedFail || harvest.UsedSafeFail || harvest.ForestEfficiency < 0.99f;
                label = string.IsNullOrEmpty(harvest.Label) ? "伐採" : harvest.Label;
                break;
            case NpcCivicJob.Rancher:
                food = Mathf.Max(0.8f, yield.food > 0f ? yield.food : 1.2f) * skill * outdoor * spotMul;
                timber = yield.timber < 0f ? yield.timber : -0.06f;
                float ranchMul = NaturalEcologyEngine.EnsureInstance()
                    .ResolveWildlifeFoodMultiplier(isRancher: true);
                food *= ranchMul;
                usedFail = usedFail || ranchMul <= NatureEnvironmentStatus.MinimumProductivity + 0.001f;
                label = ranchMul <= 0.3f ? "畜産（野生枯渇・効率低下）" : "畜産";
                break;
            case NpcCivicJob.Blacksmith:
                float needT = yield.timber < 0f ? Mathf.Abs(yield.timber) : 0.4f;
                float needO = 0.22f;
                float forgeEff = market.ConsumeEfficiency(0f, needT, needO, 0f) * spotMul;
                market.WithdrawTimber(needT * forgeEff);
                market.WithdrawOre(needO * (1f - forgeEff * 0.2f));
                ore = Mathf.Max(0.15f, yield.ore) * skill * forgeEff;
                usedFail = usedFail || forgeEff < 0.99f;
                label = forgeEff < 0.99f ? "精錬（資材不足・効率低下）" : "精錬";
                if (!usedFail)
                {
                    int workshopLevel = ResolveWorkshopLevelForSpot(spot);
                    try
                    {
                        // 精錬成功を武器1・防具1相当として軍事還元
                        MicroToMacroAggregator.EnsureInstance()
                            .NotifySmithProduction(weapon: true, armor: true, workshopLevel: workshopLevel);
                    }
                    catch (System.Exception)
                    {
                        // Safe-Fail
                    }
                }

                break;
            case NpcCivicJob.BarrierKeeper:
                crystal = 0.08f * skill * spotMul;
                label = "結界資材の選別";
                break;
            case NpcCivicJob.Carpenter:
                float needWood = 0.3f;
                float carpEff = market.ConsumeEfficiency(0f, needWood, 0f, 0f) * spotMul;
                market.WithdrawTimber(needWood * carpEff);
                timber = 0.25f * skill * carpEff;
                usedFail = usedFail || carpEff < 0.99f;
                label = carpEff < 0.99f ? "建材加工（材木不足）" : "建材加工";
                break;
            default:
                food = Mathf.Max(0.8f, yield.food > 0f ? yield.food : 1.3f) * skill * outdoor * spotMul;
                crystal = Mathf.Max(0f, yield.manaCrystal) * skill * spotMul;
                float farmMul = NaturalEcologyEngine.EnsureInstance()
                    .ResolveWildlifeFoodMultiplier(isRancher: false);
                food *= farmMul;
                label = "農耕";
                break;
        }

        if (timber < 0f)
        {
            market.WithdrawTimber(-timber);
            timber = 0f;
        }

        market.Deposit(food, timber, ore, crystal);
        npc.CurrentHP = Mathf.Max(1f, npc.CurrentHP - (season == VariableTimelineSeason.Active ? 5f : 2f));
        npc.CurrentMP = Mathf.Max(0f, npc.CurrentMP - 3f);
        string spotLabel = spot != null
            ? $"{WorkSpotData.TypeLabel(spot.Type)}@{spot.SpotId}{(spot.IsFallback ? "(FB)" : string.Empty)}"
            : "FALLBACK";
        string extra = usedFail ? " / 効率低下" : string.Empty;
        return $"[{spotLabel}] {label} 納品 +F{food:F1} +T{timber:F1} +O{ore:F1} +C{crystal:F2}{extra}";
    }

    private static float HungerDrift(NpcLifeAction action)
    {
        switch (action)
        {
            case NpcLifeAction.Eat:
                return 0f;
            case NpcLifeAction.Rest:
                return 3f;
            case NpcLifeAction.Flee:
                return 5f;
            case NpcLifeAction.MaintainBarrier:
                return 4f;
            case NpcLifeAction.RepairBuilding:
                return 5f;
            case NpcLifeAction.EmergencyRepair:
                return 6f;
            default:
                return 7f;
        }
    }

    private string TryInheritMagic()
    {
        NpcIndividualStatus teacher = null;
        NpcIndividualStatus pupil = null;
        for (int i = 0; i < villagers.Count; i++)
        {
            NpcIndividualStatus npc = villagers[i];
            if (npc == null)
            {
                continue;
            }

            if (npc.LearnedMagicIds != null && npc.LearnedMagicIds.Count > 0)
            {
                if (teacher == null || npc.JobProficiency > teacher.JobProficiency)
                {
                    teacher = npc;
                }
            }

            if (pupil == null || npc.JobProficiency < pupil.JobProficiency)
            {
                pupil = npc;
            }
        }

        if (teacher == null || pupil == null || teacher == pupil)
        {
            return string.Empty;
        }

        if (teacher.JobProficiency <= pupil.JobProficiency)
        {
            return string.Empty;
        }

        for (int i = 0; i < teacher.LearnedMagicIds.Count; i++)
        {
            string magicId = teacher.LearnedMagicIds[i];
            if (pupil.TryLearnMagic(magicId))
            {
                pupil.JobProficiency = Mathf.Min(3f, pupil.JobProficiency + 0.04f);
                return $"{teacher.Name} → {pupil.Name} : {magicId}（熟練 {pupil.JobProficiency:F2}）";
            }
        }

        return string.Empty;
    }

    private int CountLiving()
    {
        int n = 0;
        for (int i = 0; i < villagers.Count; i++)
        {
            if (villagers[i] != null)
            {
                n++;
            }
        }

        return n;
    }

    public int CountLivingKeepers()
    {
        int n = 0;
        if (villagers == null)
        {
            return 0;
        }

        for (int i = 0; i < villagers.Count; i++)
        {
            NpcIndividualStatus npc = villagers[i];
            if (npc != null && npc.CivicJob == NpcCivicJob.BarrierKeeper)
            {
                n++;
            }
        }

        return n;
    }

    private void EnsureDefaults()
    {
        if (storage == null)
        {
            storage = new VillageStorageMarket();
        }

        if (villagers != null && villagers.Count > 0)
        {
            return;
        }

        villagers = new List<NpcIndividualStatus>
        {
            new NpcIndividualStatus(
                "npc_farmer_a", "ハル", 31, NpcCivicJob.Farmer, 2.2f, NpcTrait.Diligent,
                "MAG_SOIL_WARD", "MAG_HARVEST_FLOW"),
            new NpcIndividualStatus(
                "npc_farmer_b", "ナギ", 19, NpcCivicJob.Farmer, 1.1f, NpcTrait.Inquisitive),
            new NpcIndividualStatus(
                "npc_wood_a", "トガ", 44, NpcCivicJob.Woodcutter, 2.4f, NpcTrait.Cautious,
                "MAG_WOOD_BIND"),
            new NpcIndividualStatus(
                "npc_ranch_a", "シキ", 27, NpcCivicJob.Rancher, 1.8f, NpcTrait.Diligent,
                "MAG_HERD_CALM"),
            new NpcIndividualStatus(
                "npc_smith_a", "カネ", 38, NpcCivicJob.Blacksmith, 2.1f, NpcTrait.Diligent,
                "MAG_FURNACE_FLOW"),
            new NpcIndividualStatus(
                "npc_carpenter_a", "ソウ", 33, NpcCivicJob.Carpenter, 2.3f, NpcTrait.Diligent,
                "MAG_WOOD_BIND"),
            new NpcIndividualStatus(
                "npc_keeper_a", "ミサ", 52, NpcCivicJob.BarrierKeeper, 2.7f, NpcTrait.Cautious,
                "MAG_BARRIER_SEAL", "MAG_CRYSTAL_TUNE")
        };

        NpcStatusManagerRegistry.EnsureAllFromVillagers();
    }
}

public static class NpcCivilizationEngineBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoAttach()
    {
        NpcCivilizationEngine.EnsureInstance();
    }
}

#if UNITY_EDITOR
public static class NpcCivilizationEngineMenu
{
    private const string VerifyLogPath = "Logs/npc_civilization_verify.txt";

    [MenuItem("Tools/Procedural Map/Run Npc Civilization Day")]
    public static void RunCivilizationDayFromMenu()
    {
        NpcCivilizationEngine engine = NpcCivilizationEngine.EnsureInstance();
        engine.SimulateFullDay(ProceduralMapPopulator.DefaultNation001Turn1Distribution());
        string line =
            $"PASS phase={engine.LastTickedPhase} villagers={engine.Villagers.Count} {engine.Storage.FormatSnapshot()}";
        try
        {
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
            string path = Path.Combine(projectRoot, VerifyLogPath);
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? projectRoot);
            File.WriteAllText(path, line + "\n", Encoding.UTF8);
        }
        catch (System.Exception exception)
        {
            Debug.LogWarning($"[NpcCivilizationEngine] 検証ログをスキップ: {exception.Message}");
        }

        Debug.Log($"<color=#CE93D8><b>【NPC文明・1日完了】</b></color> {line}");
        EditorUtility.DisplayDialog("NPC Civilization", line, "OK");
    }
}
#endif

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

// =============================================================================
// 社会人間ドラマ・抗争 — 内乱・派閥・継承・国境資源争奪
// =============================================================================

/// <summary>対人間抗争イベント種別。</summary>
public enum HumanConflictEventKind
{
    CivilWarFoodRiot = 0,
    AcademyResearchRivalry = 1,
    SuccessionCrisisBarrier = 2,
    BorderResourceWar = 3
}

/// <summary>発火した対人間イベント 1 件。</summary>
[Serializable]
public sealed class HumanConflictEventLog
{
    public int turn;
    public int phase;
    public HumanConflictEventKind kind;
    public string eventCode = string.Empty;
    public string title = string.Empty;
    public string narrative = string.Empty;
    public string historyFlag = string.Empty;
    public string jobUnlockHint = string.Empty;

    public string FormatLine()
    {
        return $"位相{phase}/24 [{title}] ({eventCode}) {narrative} FLAG={historyFlag} JOB={jobUnlockHint}";
    }
}

/// <summary>検証結果。</summary>
[Serializable]
public sealed class HumanConflictVerifyResult
{
    public bool success;
    public string message = string.Empty;
    public int eventCount;
    public string societySnapshot = string.Empty;
}

/// <summary>
/// 資源枯渇・技術独占・派閥対立から対人間ドラマを動的発生させます。
/// </summary>
[DefaultExecutionOrder(47)]
public class HumanConflictEngine : MonoBehaviour
{
    public const string LogTag = "【社会人間ドラマ・抗争】";
    public const float FoodRiotScarcityThreshold = 70f;
    public const float AcademyTensionThreshold = 60f;
    public const float SuccessionBarrierPercentMax = 52f;
    public const float TechMonopolySuccessionThreshold = 55f;
    public const int DefaultAdjacentNationId = 64;

    public const string FlagCivilWar = "HIST_NATION_001_GEO_TURN_001_CIVILWAR";
    public const string FlagAcademyRivalry = "HIST_NATION_001_GEO_TURN_001_ACADEMY_RIVALRY";
    public const string FlagSuccession = "HIST_NATION_001_GEO_TURN_001_SUCCESSION";
    public const string FlagBorderWar = "HIST_NATION_001_GEO_TURN_001_BORDERWAR";
    public const string FlagRebelLeader = "HIST_NATION_001_GEO_TURN_001_REBEL_LEADER";
    public const string FlagHistorianCivil = "HIST_NATION_001_GEO_TURN_001_HISTORIAN_CIVIL";

    public static HumanConflictEngine Instance { get; private set; }

    [SerializeField] private int nationId = MicroToMacroAggregator.DefaultNationId;
    [SerializeField] private int turn = MicroHistoryTimelineTimelineEngine.DefaultTurn;
    [SerializeField] private HumanSocietyStatus society = new HumanSocietyStatus();
    [SerializeField] private List<HumanConflictEventLog> eventLog = new List<HumanConflictEventLog>();
    [SerializeField] private bool civilWarFired;
    [SerializeField] private bool academyRivalryFired;
    [SerializeField] private bool successionFired;
    [SerializeField] private bool borderWarFired;
    [SerializeField] private bool magicInheritedThisPhase;
    [SerializeField] private int lastTickedPhase = -1;

    public HumanSocietyStatus Society => society;
    public IReadOnlyList<HumanConflictEventLog> EventLog => eventLog;
    public int LastTickedPhase => lastTickedPhase;

    public static HumanConflictEngine EnsureInstance()
    {
        if (Instance != null)
        {
            return Instance;
        }

        HumanConflictEngine existing = FindAnyObjectByType<HumanConflictEngine>();
        if (existing != null)
        {
            Instance = existing;
            return existing;
        }

        GameObject hub = GameObject.Find("DebugSystemsHub");
        GameObject host = hub != null ? hub : new GameObject(nameof(HumanConflictEngine));
        HumanConflictEngine engine = host.GetComponent<HumanConflictEngine>();
        return engine != null ? engine : host.AddComponent<HumanConflictEngine>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        ResetYearState();
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    public void ResetYearState()
    {
        society = new HumanSocietyStatus
        {
            factionTension = 24f,
            techMonopolyLevel = 30f
        };
        eventLog.Clear();
        civilWarFired = false;
        academyRivalryFired = false;
        successionFired = false;
        borderWarFired = false;
        magicInheritedThisPhase = false;
        lastTickedPhase = -1;
        HistoryFlagRegistry.EnsureWired();
        HistoryCausalValidator.EnsureInstance().ResetYearTracking();
    }

    public static void TryTickHumanSocietyPhase(
        int phase,
        VariableTimelineSeason season,
        VillageStorageMarket market,
        VillageBarrierCore barrier)
    {
        try
        {
            HumanConflictEngine engine = EnsureInstance();
            engine.TickHumanSocietyPhase(phase, season, market, barrier);
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[HumanConflictEngine] Tick Safe-Fail: {exception.Message}");
        }
    }

    public static void NotifyMagicInheritedThisPhase()
    {
        try
        {
            HumanConflictEngine engine = EnsureInstance();
            engine.magicInheritedThisPhase = true;
        }
        catch (Exception)
        {
            // Safe-Fail
        }
    }

    public void TickHumanSocietyPhase(
        int phase,
        VariableTimelineSeason season,
        VillageStorageMarket market,
        VillageBarrierCore barrier)
    {
        int normalized = ProceduralMapPopulator.NormalizePhase(phase);
        lastTickedPhase = normalized;

        float regionalDeficit = ResolveRegionalFoodDeficit();
        int inquisitive = CountInquisitiveVillagers();
        bool innovationActive = HasInnovationUnlock();
        int workshopLevel = ResolveWorkshopLevel();

        society.RefreshFoodScarcity(market, regionalDeficit);
        society.RefreshFactionTension(season, inquisitive, HasDormantResearchAttempt(season));
        society.RefreshTechMonopoly(market, workshopLevel, innovationActive, magicInheritedThisPhase);
        society.RefreshBarrierContext(barrier != null ? barrier.Efficiency : society.lastBarrierPercent);
        society.phasesSampled++;

        TryEmitConflictEvents(normalized, season, market, barrier);
        magicInheritedThisPhase = false;

        HistoryCausalValidator.TryRecordPhaseSnapshot(normalized, society, society.lastBarrierPercent);

        if (eventLog.Count == 0 && normalized % 6 == 0)
        {
            Debug.Log(
                $"<color=#E57373><b>{LogTag}</b></color> 平穏継続 位相{normalized}/24 " +
                society.FormatSnapshot() + "（日常生産継続）");
        }
    }

    private void TryEmitConflictEvents(
        int phase,
        VariableTimelineSeason season,
        VillageStorageMarket market,
        VillageBarrierCore barrier)
    {
        if (!civilWarFired && society.foodScarcity > FoodRiotScarcityThreshold)
        {
            if (EmitEvent(
                phase,
                HumanConflictEventKind.CivilWarFoodRiot,
                "CIVIL_WAR_FOOD_RIOT",
                "食糧暴動・内乱",
                "倉庫前で配給待ちの列が崩れ、飢えた住民が結界守と衝突する。",
                BuildHistoryFlag("CIVILWAR"),
                FlagRebelLeader))
            {
                civilWarFired = true;
            }
        }

        if (!academyRivalryFired &&
            season == VariableTimelineSeason.Dormant &&
            society.factionTension > AcademyTensionThreshold)
        {
            if (EmitEvent(
                phase,
                HumanConflictEventKind.AcademyResearchRivalry,
                "ACADEMY_RESEARCH_RIVALRY",
                "魔導学院の派閥抗争",
                "紀録派と実用派が古代魔導の解読権を巡り、研究室を占拠する。",
                BuildHistoryFlag("ACADEMY_RIVALRY"),
                FlagHistorianCivil))
            {
                academyRivalryFired = true;
            }
        }

        float barrierPct = barrier != null ? barrier.Efficiency : society.lastBarrierPercent;
        bool newTech = HasInnovationUnlock() || magicInheritedThisPhase;
        if (!successionFired &&
            barrierPct < SuccessionBarrierPercentMax &&
            society.techMonopolyLevel >= TechMonopolySuccessionThreshold &&
            newTech)
        {
            if (EmitEvent(
                phase,
                HumanConflictEventKind.SuccessionCrisisBarrier,
                "SUCCESSION_CRISIS_BARRIER",
                "結界継承権争い",
                "結界守の座を巡り、秘伝の結界術式を独占する派閥が宮廷を揺らす。",
                BuildHistoryFlag("SUCCESSION"),
                "JOB_CHRONO_ARCHITECT"))
            {
                successionFired = true;
            }
        }

        if (!borderWarFired && EvaluateAdjacentDisasterHigh(DefaultAdjacentNationId))
        {
            if (EmitEvent(
                phase,
                HumanConflictEventKind.BorderResourceWar,
                "BORDER_RESOURCE_WAR",
                "国境資源争奪戦",
                $"国家{DefaultAdjacentNationId:D3}の被災地から流れる難民と、木材・食料の配分を巡る国境摩擦が激化する。",
                BuildHistoryFlag("BORDERWAR"),
                FlagRebelLeader))
            {
                borderWarFired = true;
            }
        }
    }

    private bool EmitEvent(
        int phase,
        HumanConflictEventKind kind,
        string eventCode,
        string title,
        string narrative,
        string historyFlag,
        string jobUnlockHint)
    {
        HumanConflictEventLog entry = new HumanConflictEventLog
        {
            turn = turn,
            phase = phase,
            kind = kind,
            eventCode = eventCode,
            title = title,
            narrative = narrative,
            historyFlag = historyFlag,
            jobUnlockHint = jobUnlockHint
        };

        float barrierPct = society.lastBarrierPercent;
        CausalEventProposal proposal = HistoryCausalValidator.BuildFromHumanConflict(
            entry,
            society,
            barrierPct,
            turn);
        if (proposal == null)
        {
            return false;
        }

        proposal.season = ResolveSeasonForPhase(phase);
        if (!HistoryCausalValidator.TryEmitValidatedEvent(proposal))
        {
            return false;
        }

        eventLog.Add(entry);
        return true;
    }

    private static VariableTimelineSeason ResolveSeasonForPhase(int phase)
    {
        try
        {
            VariablePhaseDistribution dist = ProceduralMapPopulator.DefaultNation001Turn1Distribution();
            return ProceduralMapPopulator.ResolveSeason(phase, dist);
        }
        catch (Exception)
        {
            return VariableTimelineSeason.Active;
        }
    }

    private string BuildHistoryFlag(string suffix)
    {
        return $"HIST_NATION_{nationId:D3}_GEO_TURN_{turn:D3}_{suffix}";
    }

    private static bool HasDormantResearchAttempt(VariableTimelineSeason season)
    {
        return season == VariableTimelineSeason.Dormant;
    }

    private static bool HasInnovationUnlock()
    {
        return HistoryFlagRegistry.IsUnlocked(MicroToMacroAggregator.FlagInnovation) ||
               HistoryFlagRegistry.IsUnlocked(MicroToMacroAggregator.FlagHero);
    }

    private static float ResolveRegionalFoodDeficit()
    {
        try
        {
            WestRegionSimulationManager west = WestRegionSimulationManager.Instance;
            if (west?.RegionalNetwork != null)
            {
                return west.RegionalNetwork.aggregateFoodDeficit;
            }
        }
        catch (Exception)
        {
            // Safe-Fail
        }

        return 0f;
    }

    private static int CountInquisitiveVillagers()
    {
        try
        {
            NpcCivilizationEngine civ = NpcCivilizationEngine.Instance;
            if (civ?.Villagers == null)
            {
                return 0;
            }

            int count = 0;
            for (int i = 0; i < civ.Villagers.Count; i++)
            {
                NpcIndividualStatus npc = civ.Villagers[i];
                if (npc != null && npc.Trait == NpcTrait.Inquisitive)
                {
                    count++;
                }
            }

            return count;
        }
        catch (Exception)
        {
            return 0;
        }
    }

    private static int ResolveWorkshopLevel()
    {
        try
        {
            VillageInfrastructureEngine infra = VillageInfrastructureEngine.EnsureInstance();
            int max = 1;
            for (int i = 0; i < infra.BuildingCount; i++)
            {
                VillageBuildingData b = infra.Buildings[i];
                if (b != null && b.Type == BuildingType.Workshop)
                {
                    max = Mathf.Max(max, b.Level);
                }
            }

            return max;
        }
        catch (Exception)
        {
            return 1;
        }
    }

    private bool EvaluateAdjacentDisasterHigh(int adjacentNationId)
    {
        try
        {
            WestRegionSimulationManager west = WestRegionSimulationManager.Instance ??
                                             WestRegionSimulationManager.EnsureInstance();
            west.TryInitializeWestRegion();
            WestRegionNationRuntime adjacent = west.FindRuntime(adjacentNationId);
            if (adjacent?.bias == null)
            {
                MacroChronicleNationSnapshot snap = MacroChronicleJsonScan.ReadNationAtTurn(
                    MicroHistoryTimelineTimelineEngine.ResolveGeoPath(),
                    turn,
                    adjacentNationId);
                if (snap == null)
                {
                    return false;
                }

                NationBiasData bias = NationBiasInjector.ComputeForNation(
                    snap,
                    turn,
                    MacroChronicleJsonScan.ReadTurnEvents(
                        MicroHistoryTimelineTimelineEngine.ResolveLogPath(),
                        turn));
                return bias.recentDamage;
            }

            bool disaster = adjacent.bias.recentDamage;
            float adjacentFood = adjacent.abstractFood;
            if (adjacent.lod == WestRegionLodLevel.Lod0FullMicro && adjacent.microCell?.market != null)
            {
                adjacentFood = adjacent.microCell.market.Food;
            }

            return disaster && adjacentFood < 50f;
        }
        catch (Exception)
        {
            return adjacentNationId == DefaultAdjacentNationId;
        }
    }

    public static HumanConflictVerifyResult RunDayVerification()
    {
        HumanConflictVerifyResult result = new HumanConflictVerifyResult();
        try
        {
            HumanConflictEngine engine = EnsureInstance();
            engine.ResetYearState();
            engine.turn = MicroHistoryTimelineTimelineEngine.DefaultTurn;
            SimulationVerifyBootstrap.PrepareFreshStoryDay();

            NpcCivilizationEngine civ = NpcCivilizationEngine.EnsureInstance();
            VariablePhaseDistribution dist = ProceduralMapPopulator.DefaultNation001Turn1Distribution();
            MicroToMacroAggregator.EnsureInstance().BeginYearBaseline();
            WestRegionSimulationManager.EnsureInstance().ResetWestRegionYear();

            for (int phase = 1; phase <= ProceduralMapPopulator.PhaseCount; phase++)
            {
                VariableTimelineSeason season = ProceduralMapPopulator.ResolveSeason(phase, dist);
                civ.TickPhase(phase, season);
            }

            result.eventCount = engine.eventLog.Count;
            result.societySnapshot = engine.society.FormatSnapshot();
            bool hasFoodOrBorder = engine.civilWarFired || engine.borderWarFired;
            bool hasAcademyOrSuccession = engine.academyRivalryFired || engine.successionFired;
            result.success = result.eventCount >= 2 && hasFoodOrBorder && hasAcademyOrSuccession;
            result.message =
                $"events={result.eventCount} civil={engine.civilWarFired} academy={engine.academyRivalryFired} " +
                $"succession={engine.successionFired} border={engine.borderWarFired} " +
                $"{result.societySnapshot} phase={engine.lastTickedPhase}";
        }
        catch (Exception exception)
        {
            result.success = false;
            result.message = $"Safe-Fail: {exception.Message}";
        }

        return result;
    }
}

public static class HumanConflictEngineBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoAttach()
    {
        HumanConflictEngine.EnsureInstance();
    }
}

#if UNITY_EDITOR
public static class HumanConflictEngineMenu
{
    private const string VerifyLogPath = "Logs/human_conflict_verify.txt";

    [MenuItem("Tools/Procedural Map/Run Human Conflict Day")]
    public static void RunHumanConflictDay()
    {
        HumanConflictVerifyResult result = HumanConflictEngine.RunDayVerification();
        WriteLog(result);
        Debug.Log(
            result.success
                ? $"<color=#E57373><b>{HumanConflictEngine.LogTag}・検証】PASS</b></color> {result.message}"
                : $"<color=#FF8A80><b>{HumanConflictEngine.LogTag}・検証】FAIL</b></color> {result.message}");
        EditorUtility.DisplayDialog("Human Conflict", result.message, "OK");
    }

    /// <summary>Unity バッチ: -executeMethod HumanConflictEngineMenu.BatchVerifyAndQuit</summary>
    public static void BatchVerifyAndQuit()
    {
        HumanConflictVerifyResult result = HumanConflictEngine.RunDayVerification();
        WriteLog(result);
        Debug.Log(
            result.success
                ? $"<color=#E57373><b>{HumanConflictEngine.LogTag}・検証】PASS</b></color> {result.message}"
                : $"<color=#FF8A80><b>{HumanConflictEngine.LogTag}・検証】FAIL</b></color> {result.message}");
        EditorApplication.Exit(0);
    }

    private static void WriteLog(HumanConflictVerifyResult result)
    {
        try
        {
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
            string path = Path.Combine(projectRoot, VerifyLogPath);
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? projectRoot);
            File.WriteAllText(path, result.message + "\n", Encoding.UTF8);
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[HumanConflictEngine] 検証ログをスキップ: {exception.Message}");
        }
    }
}
#endif

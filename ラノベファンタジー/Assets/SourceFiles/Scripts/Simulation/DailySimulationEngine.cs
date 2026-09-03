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
// 24 位相 → 365 日デイリータイムライン補間・1 日単位ミクロ進行
// 連携: MicroHistoryTimelineTimelineEngine / VillageStorageMarket / VillageBarrierCore
//       NaturalEcologyEngine / VillageBarrierBreachEngine / MicroToMacroAggregator
// =============================================================================

/// <summary>プレイヤー介入の翌日以降への影響。</summary>
[Serializable]
public sealed class DailyInterventionState
{
    public int remainingDays = 0;
    public float threatReduction = 0f;
    public bool collapseBlocked = false;
    public float lastBarrierRestorePercent = 0f;
    public string lastInjectedItemId = string.Empty;
}

/// <summary>1 日進行の結果。</summary>
public sealed class DailyAdvanceResult
{
    public bool success;
    public bool yearCompleted;
    public int dayOfYear;
    public int turn;
    public string lifeLog = string.Empty;
    public float barrierPercent;
    public float threatLevel;
    public float manaEcologyLevel;
    public string storageSnapshot = string.Empty;
    public string message = string.Empty;
}

/// <summary>クラフト成果物の村納品結果。</summary>
public sealed class CraftVillageInjectionResult
{
    public bool success;
    public float barrierRestoredPercent;
    public float crystalDeposited;
    public float threatReductionApplied;
    public int interventionDays;
    public string message = string.Empty;
}

/// <summary>バッチ検証の成否。</summary>
public sealed class DailySimulationVerifyResult
{
    public bool success;
    public string message = string.Empty;
}

/// <summary>
/// 国家001・ターン1の 24 位相を 365 日へ展開し、1 日単位で村状態を更新します。
/// </summary>
[DefaultExecutionOrder(49)]
public class DailySimulationEngine : MonoBehaviour
{
    public const string LogTag = "【デイリーシミュレーション】";
    public const string InterventionLogTag = "【デイリー介入】";
    public const int DefaultNationId = MicroHistoryTimelineTimelineEngine.DefaultNationId;
    public const int DefaultTurn = MicroHistoryTimelineTimelineEngine.DefaultTurn;

    public static DailySimulationEngine Instance { get; private set; }

    [SerializeField] private int nationId = DefaultNationId;
    [SerializeField] private int turn = DefaultTurn;
    [SerializeField] private int currentDayOfYear = 1;
    [SerializeField] private DailyInterventionState intervention = new DailyInterventionState();

    private MicroHistoryTimeline timeline;
    private MicroPhaseSlot[] dailySlots = Array.Empty<MicroPhaseSlot>();
    private readonly List<string> recentLifeLogs = new List<string>(32);
    private float displayedThreatLevel = 1f;
    private bool yearCompletedThisSession;

    public int NationId => nationId;
    public int Turn => turn;
    public int CurrentDayOfYear => currentDayOfYear;
    public float CurrentThreatLevel => displayedThreatLevel;
    public DailyInterventionState Intervention => intervention ?? (intervention = new DailyInterventionState());
    public IReadOnlyList<string> RecentLifeLogs => recentLifeLogs;
    public MicroHistoryTimeline Timeline => timeline;

    public static DailySimulationEngine EnsureInstance()
    {
        if (Instance != null)
        {
            return Instance;
        }

        DailySimulationEngine existing = UnityEngine.Object.FindAnyObjectByType<DailySimulationEngine>();
        if (existing != null)
        {
            Instance = existing;
            return existing;
        }

        GameObject hub = GameObject.Find("DebugSystemsHub");
        GameObject host = hub != null ? hub : new GameObject(nameof(DailySimulationEngine));
        DailySimulationEngine engine = host.GetComponent<DailySimulationEngine>();
        return engine != null ? engine : host.AddComponent<DailySimulationEngine>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        try
        {
            RebuildTimeline(forceYearBaseline: true);
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[DailySimulationEngine] Awake Safe-Fail: {exception.Message}");
        }
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    /// <summary>検証用: 日次カウンタをリセットします。</summary>
    public void ResetSessionForVerification()
    {
        currentDayOfYear = 1;
        turn = DefaultTurn;
        yearCompletedThisSession = false;
        recentLifeLogs.Clear();
        RumorLogGenerator.ClearRecent();
        RebuildTimeline(forceYearBaseline: true);
    }

    /// <summary>タイムラインを再構築し、365 日スロットへ展開します。</summary>
    public void RebuildTimeline(bool forceYearBaseline = false)
    {
        timeline = MicroHistoryTimelineTimelineEngine.BuildTimeline(nationId, turn);
        dailySlots = MicroHistoryTimelineTimelineEngine.ExpandToDays(timeline);
        if (dailySlots == null || dailySlots.Length == 0)
        {
            dailySlots = BuildEvenDailyFallback();
        }
        else if (dailySlots.Length < MicroHistoryTimelineTimelineEngine.DaysPerYear)
        {
            dailySlots = PadDailySlotsToFullYear(dailySlots);
        }

        if (forceYearBaseline)
        {
            try
            {
                MicroToMacroAggregator.EnsureInstance().BeginYearBaseline();
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[DailySimulationEngine] BeginYearBaseline Safe-Fail: {exception.Message}");
            }
        }

        yearCompletedThisSession = false;
        Debug.Log(
            $"<color=#80DEEA><b>{LogTag}</b></color> " +
            $"国家{nationId:D3} ターン{turn} → {dailySlots.Length} 日展開 " +
            $"配分 A{timeline?.distribution?.activePhases ?? 0}/" +
            $"D{timeline?.distribution?.deescalationPhases ?? 0}/" +
            $"Z{timeline?.distribution?.dormantPhases ?? 0}/" +
            $"E{timeline?.distribution?.escalationPhases ?? 0}");
    }

    /// <summary>1 日進め、デイリー処理と生活ログを生成します。</summary>
    public DailyAdvanceResult AdvanceOneDay()
    {
        DailyAdvanceResult result = new DailyAdvanceResult
        {
            dayOfYear = currentDayOfYear,
            turn = turn
        };

        try
        {
            if (yearCompletedThisSession && currentDayOfYear > MicroHistoryTimelineTimelineEngine.DaysPerYear)
            {
                result.success = true;
                result.message = "年次完了済み。AdvanceOneDay はスキップされました。";
                return result;
            }

            if (currentDayOfYear > MicroHistoryTimelineTimelineEngine.DaysPerYear)
            {
                return CompleteYearSafeFail(result);
            }

            MicroPhaseSlot slot = ResolveSlotForDay(currentDayOfYear);
            VariableTimelineSeason season = slot.season;
            int phase = slot.phaseIndex > 0 ? slot.phaseIndex : ResolvePhaseForDay(currentDayOfYear);

            VillageAutonomyEngine village = VillageAutonomyEngine.EnsureInstance();
            VillageBarrierCore barrier = village.Barrier;
            VillageStorageMarket market = NpcCivilizationEngine.EnsureInstance().Storage;
            NatureEnvironmentStatus nature = NaturalEcologyEngine.EnsureInstance().Environment;
            nature.ClampAll();

            float barrierBefore = barrier.Efficiency;
            ApplyDailyBarrierDecay(barrier, slot, season);
            float manaDelta = ApplyDailyManaEcology(nature, season, slot);
            DailyProductionOutcome production = ApplyDailyStorageCycle(market, season, phase);
            string lifeLog = BuildDailyLifeLog(currentDayOfYear, season, production, barrierBefore, barrier.Efficiency);
            AppendLifeLog(lifeLog);
            NpcStatusManagerRegistry.EnsureAllFromVillagers();

            displayedThreatLevel = ComputeThreatLevel(slot, barrier);
            RumorLogGenerator.TryEmitDailyRumor(
                currentDayOfYear,
                season,
                phase,
                barrierBefore,
                barrier.Efficiency,
                displayedThreatLevel,
                nature.ManaEcologyLevel,
                market);

            EvaluateDailyDefense(phase, season, barrier, market);

            GeographicNaturalDisasterEngine.TryTickDay(
                currentDayOfYear,
                season,
                phase,
                market,
                barrier);

            if (intervention.remainingDays > 0)
            {
                intervention.remainingDays--;
                if (intervention.remainingDays <= 0)
                {
                    intervention.collapseBlocked = false;
                    intervention.threatReduction = 0f;
                }
            }

            try
            {
                ProceduralMapPopulator populator = ResolvePopulator();
                if (populator != null)
                {
                    populator.BindTimeline(timeline);
                    populator.UpdateEnvironmentForDay(currentDayOfYear);
                }
            }
            catch (Exception mapException)
            {
                Debug.LogWarning($"[DailySimulationEngine] マップ日次更新スキップ: {mapException.Message}");
            }

            market.SyncToVillageResources(village.Resources);

            result.success = true;
            result.lifeLog = lifeLog;
            result.barrierPercent = barrier.Efficiency;
            result.threatLevel = displayedThreatLevel;
            result.manaEcologyLevel = nature.ManaEcologyLevel;
            result.storageSnapshot = market.FormatSnapshot();
            result.message = $"日次処理完了 結界{barrier.Efficiency:F1}% 脅威{displayedThreatLevel:F2}";

            Debug.Log(
                $"<color=#4DD0E1><b>{LogTag}</b></color> " +
                $"{currentDayOfYear}日目 {season} 位相{phase} " +
                $"結界{barrierBefore:F1}→{barrier.Efficiency:F1}% " +
                $"魔力{nature.ManaEcologyLevel:F1} 脅威{displayedThreatLevel:F2} / {lifeLog}");

            currentDayOfYear++;
            if (currentDayOfYear > MicroHistoryTimelineTimelineEngine.DaysPerYear)
            {
                return CompleteYearSafeFail(result);
            }

            return result;
        }
        catch (Exception exception)
        {
            result.success = false;
            result.message = $"Safe-Fail: {exception.Message}";
            Debug.LogWarning($"[DailySimulationEngine] AdvanceOneDay Safe-Fail: {exception.Message}");
            currentDayOfYear = Mathf.Min(currentDayOfYear + 1, MicroHistoryTimelineTimelineEngine.DaysPerYear + 1);
            return result;
        }
    }

    /// <summary>工房クラフト成果物を共有倉庫へ納品し、結界を即時回復します。</summary>
    public static CraftVillageInjectionResult InjectCraftResultToVillage(ItemData item)
    {
        CraftVillageInjectionResult result = new CraftVillageInjectionResult();
        if (item == null || string.IsNullOrWhiteSpace(item.id))
        {
            result.message = "成果物 null のためスキップ";
            return result;
        }

        try
        {
            DailySimulationEngine engine = EnsureInstance();
            return engine.InjectCraftResultInternal(item);
        }
        catch (Exception exception)
        {
            result.message = $"Safe-Fail: {exception.Message}";
            Debug.LogWarning($"[DailySimulationEngine] InjectCraft Safe-Fail: {exception.Message}");
            return result;
        }
    }

    /// <summary>品質スコア付きで納品（デモジャッジ連携）。</summary>
    public static CraftVillageInjectionResult InjectCraftResultToVillage(
        string itemId,
        float purity,
        float density,
        string displayName = null)
    {
        ItemData item = new ItemData
        {
            id = itemId ?? string.Empty,
            itemName = displayName ?? itemId ?? "成果物",
            itemType = "Material",
            weight = 1f,
            maxStack = 99
        };
        item.BindCraftHiddenParams(purity, density);
        return InjectCraftResultToVillage(item);
    }

    private CraftVillageInjectionResult InjectCraftResultInternal(ItemData item)
    {
        CraftVillageInjectionResult result = new CraftVillageInjectionResult();
        VillageAutonomyEngine village = VillageAutonomyEngine.EnsureInstance();
        VillageBarrierCore barrier = village.Barrier;
        VillageStorageMarket market = NpcCivilizationEngine.EnsureInstance().Storage;

        float purity = 40f;
        float density = 40f;
        bool hasHidden = item.TryGetCraftHiddenParams(out purity, out density);
        string id = item.id ?? string.Empty;

        bool crystalLike =
            id.IndexOf("CRYSTAL", StringComparison.OrdinalIgnoreCase) >= 0 ||
            id.IndexOf("REMEDY", StringComparison.OrdinalIgnoreCase) >= 0 ||
            id.IndexOf("POTION", StringComparison.OrdinalIgnoreCase) >= 0 ||
            (hasHidden && purity >= 50f);

        float crystalDeposit = crystalLike
            ? Mathf.Clamp(purity * 0.18f + density * 0.04f, 1f, 24f)
            : Mathf.Clamp(density * 0.05f, 0f, 4f);
        float timberDeposit = id.StartsWith("WEAPON_", StringComparison.OrdinalIgnoreCase) ? 0f : 0.5f;
        float foodDeposit = id.IndexOf("FOOD", StringComparison.OrdinalIgnoreCase) >= 0 ? 2f : 0f;

        market.Deposit(foodDeposit, timberDeposit, 0f, crystalDeposit);
        market.SyncToVillageResources(village.Resources);

        float restorePct = crystalLike
            ? Mathf.Clamp(purity * 0.25f, 4f, 25f)
            : Mathf.Clamp(purity * 0.08f, 1f, 8f);
        float restored = barrier.RestoreDirectPercent(restorePct);

        intervention.lastInjectedItemId = id;
        intervention.lastBarrierRestorePercent = restored;
        intervention.remainingDays = crystalLike ? 7 : 3;
        intervention.threatReduction = Mathf.Clamp(purity / 100f * 0.42f, 0.04f, 0.45f);
        intervention.collapseBlocked = purity >= 70f && barrier.Efficiency >= 45f;

        MicroPhaseSlot slot = ResolveSlotForDay(currentDayOfYear);
        int phase = slot.phaseIndex > 0 ? slot.phaseIndex : ResolvePhaseForDay(currentDayOfYear);
        VillageBarrierBreachEngine breach = VillageBarrierBreachEngine.EnsureInstance();
        breach.Evaluate(phase, slot.season, barrier);
        breach.MarkRepairAttempt(restored > 0.01f);

        displayedThreatLevel = ComputeThreatLevel(slot, barrier);

        result.success = true;
        result.barrierRestoredPercent = restored;
        result.crystalDeposited = crystalDeposit;
        result.threatReductionApplied = intervention.threatReduction;
        result.interventionDays = intervention.remainingDays;
        result.message =
            $"納品 {item.itemName} Purity{purity:F0} → 結界+{restored:F1}% 結晶+{crystalDeposit:F1} " +
            $"脅威軽減{intervention.threatReduction:F2} ({intervention.remainingDays}日)";

        Debug.Log(
            $"<color=#CE93D8><b>{InterventionLogTag}</b></color> " +
            $"{currentDayOfYear}日目 {item.itemName}({id}) Purity{purity:F0} " +
            $"結界 {barrier.Efficiency:F1}% (+{restored:F1}%) 倉庫{market.FormatSnapshot()} " +
            $"翌日以降脅威-{intervention.threatReduction:F2} 滅亡回避={intervention.collapseBlocked}");

        if (turn >= Generation2TransitionEngine.Generation2StartTurn && crystalLike)
        {
            Generation2DailyIfEngine.TrySpawnBranchFromCraftIntervention(
                turn,
                nationId,
                purity,
                item.itemName);
        }

        return result;
    }

    private DailyAdvanceResult CompleteYearSafeFail(DailyAdvanceResult result)
    {
        try
        {
            yearCompletedThisSession = true;
            AppendLifeLog($"{MicroHistoryTimelineTimelineEngine.DaysPerYear}日目: 年次集計をマクロへ還元");

            if (turn >= Generation2TransitionEngine.Generation2StartTurn)
            {
                Generation2DailyIfEngine.TrySpawnBranchFromYearCompletion(turn, nationId);
            }

            TurnTransitionResult transition = TurnTransitionEngine.AdvanceToNextYear(fromDailyCompletion: true);
            result.success = transition.success;
            result.yearCompleted = true;
            result.turn = transition.newTurn;
            result.dayOfYear = currentDayOfYear;
            result.message = transition.message;

            if (transition.advanced)
            {
                yearCompletedThisSession = false;
            }

            return result;
        }
        catch (Exception exception)
        {
            result.success = false;
            result.yearCompleted = true;
            result.message = $"年次 Safe-Fail: {exception.Message}";
            Debug.LogWarning($"[DailySimulationEngine] CompleteYear Safe-Fail: {exception.Message}");
            return result;
        }
    }

    /// <summary>1000年史バッチ向け — ターン 1〜1000 まで許容します。</summary>
    public bool TryPrepareForChronicleTurn(int newTurn)
    {
        try
        {
            turn = EraContextResolver.ClampSimulationTurn(newTurn);
            currentDayOfYear = 1;
            yearCompletedThisSession = false;
            intervention = new DailyInterventionState();
            recentLifeLogs.Clear();
            RumorLogGenerator.ClearRecent();
            RebuildTimeline(forceYearBaseline: true);
            return true;
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[DailySimulationEngine] TryPrepareForChronicleTurn Safe-Fail: {exception.Message}");
            return false;
        }
    }

    /// <summary>年代進行後のターンへ 365 日シミュレーションを再初期化します。</summary>
    public bool TryPrepareForTurn(int newTurn)
    {
        try
        {
            turn = Mathf.Clamp(newTurn, EraContextResolver.MinTurn, EraContextResolver.MaxTurn);
            currentDayOfYear = 1;
            yearCompletedThisSession = false;
            intervention = new DailyInterventionState();
            recentLifeLogs.Clear();
            RumorLogGenerator.ClearRecent();
            RebuildTimeline(forceYearBaseline: true);
            return true;
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[DailySimulationEngine] TryPrepareForTurn Safe-Fail: {exception.Message}");
            return false;
        }
    }

    private void ApplyDailyBarrierDecay(
        VillageBarrierCore barrier,
        MicroPhaseSlot slot,
        VariableTimelineSeason season)
    {
        if (barrier == null)
        {
            return;
        }

        if (season != VariableTimelineSeason.Active &&
            season != VariableTimelineSeason.Escalation)
        {
            return;
        }

        int phaseDays = Mathf.Max(1, slot.dayEnd - slot.dayStart + 1);
        float phaseDecay = season == VariableTimelineSeason.Active
            ? VillageBarrierCore.ActiveDecayPerPhase
            : VillageBarrierCore.ActiveDecayPerPhase * 0.55f;
        float dailyDecay = phaseDecay / phaseDays;
        float maintenance = Mathf.Clamp(slot.barrierMaintenanceRate, 0.45f, 1.35f);
        barrier.Efficiency = Mathf.Clamp(
            barrier.Efficiency - dailyDecay * maintenance,
            VillageBarrierCore.MinEfficiency,
            VillageBarrierCore.MaxEfficiency);
    }

    private float ApplyDailyManaEcology(
        NatureEnvironmentStatus nature,
        VariableTimelineSeason season,
        MicroPhaseSlot slot)
    {
        float delta = season == VariableTimelineSeason.Dormant ? 0.12f : -0.06f;
        if (season == VariableTimelineSeason.Escalation)
        {
            delta -= 0.04f;
        }

        delta += (slot.threatLevel - 1f) * 0.02f;
        if (intervention.threatReduction > 0f)
        {
            delta += intervention.threatReduction * 0.05f;
        }

        return nature.ApplyManaDelta(delta);
    }

    private DailyProductionOutcome ApplyDailyStorageCycle(
        VillageStorageMarket market,
        VariableTimelineSeason season,
        int phase)
    {
        DailyProductionOutcome outcome = new DailyProductionOutcome();
        NpcCivilizationEngine civ = NpcCivilizationEngine.EnsureInstance();
        int population = Mathf.Max(1, civ.Villagers?.Count ?? 4);
        float seasonFoodMul = season == VariableTimelineSeason.Dormant ? 0.82f : 1f;
        float seasonOutdoorMul = season == VariableTimelineSeason.Active ? 0.55f :
            season == VariableTimelineSeason.Dormant ? 1f : 0.78f;

        float foodNeed = population * 0.17f * seasonFoodMul;
        float timberNeed = population * 0.06f * seasonFoodMul;
        market.WithdrawFood(foodNeed);
        market.WithdrawTimber(timberNeed);

        NatureEnvironmentStatus nature = NaturalEcologyEngine.EnsureInstance().Environment;
        float harvestEff = nature.ResolveHarvestEfficiency() * seasonOutdoorMul;

        WorkSpotData spot = ResolveWorkSpotForDailyJob(NpcCivicJob.Woodcutter, phase);
        float timberGain = Mathf.Max(0.5f, 1.1f * harvestEff * seasonOutdoorMul);
        float foodGain = Mathf.Max(0.4f, 0.75f * harvestEff * seasonFoodMul);

        if (spot != null && spot.Type == WorkSpotType.WoodcutterCamp)
        {
            WoodcutterHarvestResult harvest = NaturalEcologyEngine.EnsureInstance()
                .HarvestAtCamp(spot, 1.2f, market);
            timberGain = Mathf.Max(timberGain, harvest.ForestEfficiency * 1.4f * harvest.QualityMultiplier);
            outcome.woodcutterLabel = harvest.Label;
            outcome.spotLabel = ResolveSpotDisplayName(spot);
            outcome.timberGained = timberGain;
            outcome.jobLabel = "木こり";
        }
        else
        {
            outcome.spotLabel = ResolveSpotDisplayName(spot);
            outcome.timberGained = timberGain;
            outcome.jobLabel = "木こり";
        }

        market.Deposit(foodGain, timberGain, 0f, 0.04f * harvestEff);
        outcome.foodGained = foodGain;
        outcome.spot = spot;
        outcome.civicJob = NpcCivicJob.Woodcutter;
        return outcome;
    }

    private void EvaluateDailyDefense(
        int phase,
        VariableTimelineSeason season,
        VillageBarrierCore barrier,
        VillageStorageMarket market)
    {
        VillageBarrierBreachEngine breach = VillageBarrierBreachEngine.EnsureInstance();
        breach.Evaluate(phase, season, barrier);

        if (intervention.collapseBlocked && breach.HasActiveBreach && barrier.Efficiency >= 50f)
        {
            breach.MarkRepairAttempt(true);
        }

        int keepers = CountLivingKeepers();
        breach.FinalizePhase(keepers, market.HasCrystal, barrier);
    }

    private static int CountLivingKeepers()
    {
        try
        {
            NpcCivilizationEngine civ = NpcCivilizationEngine.EnsureInstance();
            if (civ?.Villagers == null)
            {
                return 0;
            }

            int count = 0;
            for (int i = 0; i < civ.Villagers.Count; i++)
            {
                NpcIndividualStatus npc = civ.Villagers[i];
                if (npc != null && npc.CivicJob == NpcCivicJob.BarrierKeeper && npc.CurrentHP > 0.01f)
                {
                    count++;
                }
            }

            return count;
        }
        catch (Exception)
        {
            return 1;
        }
    }

    private float ComputeThreatLevel(MicroPhaseSlot slot, VillageBarrierCore barrier)
    {
        float baseThreat = Mathf.Max(0.1f, slot.threatLevel);
        float barrierFactor = Mathf.Clamp(barrier.Efficiency / 100f, 0.05f, 1f);
        float threat = baseThreat * (1.35f - barrierFactor * 0.65f);
        threat -= intervention.threatReduction;
        if (intervention.collapseBlocked && barrier.Efficiency >= 45f)
        {
            threat *= 0.55f;
        }

        displayedThreatLevel = Mathf.Clamp(threat, 0.08f, 3.5f);
        return displayedThreatLevel;
    }

    private string BuildDailyLifeLog(
        int day,
        VariableTimelineSeason season,
        DailyProductionOutcome production,
        float barrierBefore,
        float barrierAfter)
    {
        float barrierDelta = barrierAfter - barrierBefore;
        if (barrierDelta <= -0.4f)
        {
            float drop = Mathf.Abs(barrierDelta);
            return $"{day}日目: 結界杭のエネルギーが{drop.ToString("F0", CultureInfo.InvariantCulture)}%低下";
        }

        if (production.timberGained >= 0.8f && production.jobLabel == "木こり")
        {
            string locale = string.IsNullOrWhiteSpace(production.spotLabel) ? "村外れ" : production.spotLabel;
            return $"{day}日目: {locale}で木こりが木材{production.timberGained.ToString("F0", CultureInfo.InvariantCulture)}を調達";
        }

        if (season == VariableTimelineSeason.Dormant)
        {
            return $"{day}日目: 休眠期の静穏な暮らし（食料+{production.foodGained:F1}）";
        }

        return $"{day}日目: 市民が日常生産を継続（食料+{production.foodGained:F1} 材木+{production.timberGained:F1}）";
    }

    private void AppendLifeLog(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        recentLifeLogs.Add(line);
        if (recentLifeLogs.Count > 24)
        {
            recentLifeLogs.RemoveAt(0);
        }
    }

    private MicroPhaseSlot ResolveSlotForDay(int day)
    {
        int index = Mathf.Clamp(day - 1, 0, dailySlots.Length - 1);
        if (dailySlots.Length == 0)
        {
            return new MicroPhaseSlot
            {
                phaseIndex = ResolvePhaseForDay(day),
                season = VariableTimelineSeason.Active,
                dayStart = day,
                dayEnd = day,
                barrierMaintenanceRate = 1f,
                threatLevel = 1f
            };
        }

        return dailySlots[index];
    }

    private static int ResolvePhaseForDay(int day)
    {
        return Mathf.Clamp(
            Mathf.CeilToInt(day * MicroHistoryTimelineTimelineEngine.PhaseCount /
                            (float)MicroHistoryTimelineTimelineEngine.DaysPerYear),
            1,
            MicroHistoryTimelineTimelineEngine.PhaseCount);
    }

    private static WorkSpotData ResolveWorkSpotForDailyJob(NpcCivicJob job, int phase)
    {
        try
        {
            VillageWorkSpotManager spots = VillageWorkSpotManager.EnsureInstance();
            if (job == NpcCivicJob.Woodcutter)
            {
                return spots.FindNearestWoodcutterCamp(Vector3.zero);
            }

            return spots.GetSpotsOfType(WorkSpotType.Farmland)?.Count > 0
                ? spots.GetSpotsOfType(WorkSpotType.Farmland)[0]
                : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string ResolveSpotDisplayName(WorkSpotData spot)
    {
        if (spot == null)
        {
            return "村外れ";
        }

        if (!string.IsNullOrWhiteSpace(spot.SpotId) &&
            spot.SpotId.IndexOf("WOOD", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "西山麓";
        }

        switch (spot.Type)
        {
            case WorkSpotType.WoodcutterCamp:
                return "西山麓";
            case WorkSpotType.Farmland:
                return "南畑";
            case WorkSpotType.BarrierAnchor:
                return "結界杭";
            case WorkSpotType.BarrierCore:
                return "結界核";
            case WorkSpotType.Workshop:
                return "中央工房";
            default:
                return WorkSpotData.TypeLabel(spot.Type);
        }
    }

    private static ProceduralMapPopulator ResolvePopulator()
    {
        ProceduralMapPopulator populator = UnityEngine.Object.FindAnyObjectByType<ProceduralMapPopulator>();
        return populator;
    }

    private static MicroPhaseSlot[] PadDailySlotsToFullYear(MicroPhaseSlot[] source)
    {
        MicroPhaseSlot[] padded = BuildEvenDailyFallback();
        int copy = Mathf.Min(source.Length, padded.Length);
        for (int i = 0; i < copy; i++)
        {
            if (source[i] != null)
            {
                padded[i] = source[i];
                padded[i].dayStart = i + 1;
                padded[i].dayEnd = i + 1;
            }
        }

        Debug.LogWarning(
            $"[DailySimulationEngine] 日次スロット {source.Length} 件 → {padded.Length} 日へ Safe-Fail 補完");
        return padded;
    }

    private static MicroPhaseSlot[] BuildEvenDailyFallback()
    {
        MicroPhaseSlot[] slots = new MicroPhaseSlot[MicroHistoryTimelineTimelineEngine.DaysPerYear];
        VariablePhaseDistribution dist = ProceduralMapPopulator.DefaultNation001Turn1Distribution();
        for (int day = 1; day <= slots.Length; day++)
        {
            int phase = ResolvePhaseForDay(day);
            slots[day - 1] = new MicroPhaseSlot
            {
                phaseIndex = phase,
                season = ProceduralMapPopulator.ResolveSeason(phase, dist),
                dayStart = day,
                dayEnd = day,
                barrierMaintenanceRate = 1f,
                threatLevel = 1f
            };
        }

        return slots;
    }

    /// <summary>検証: 数日進行 + 介入クランプ。</summary>
    public static DailySimulationVerifyResult RunVerification()
    {
        DailySimulationVerifyResult verify = new DailySimulationVerifyResult();
        StringBuilder log = new StringBuilder();
        try
        {
            DailySimulationEngine engine = EnsureInstance();
            engine.currentDayOfYear = 1;
            engine.turn = DefaultTurn;
            engine.RebuildTimeline(forceYearBaseline: true);

            DailyAdvanceResult day1 = engine.AdvanceOneDay();
            bool dayPass = day1.success && !string.IsNullOrWhiteSpace(day1.lifeLog);
            log.AppendLine($"day1: {day1.lifeLog} pass={dayPass}");

            CraftVillageInjectionResult inject = InjectCraftResultToVillage(
                CraftRecipeIds.CrystalShard,
                80f,
                55f,
                "高純度魔力結晶");
            bool injectPass = inject.success && inject.barrierRestoredPercent > 0f;
            log.AppendLine(
                $"inject: restore={inject.barrierRestoredPercent:F1} threat-{inject.threatReductionApplied:F2} pass={injectPass}");

            DailyAdvanceResult day2 = engine.AdvanceOneDay();
            bool threatPass = day2.success && day2.threatLevel < day1.threatLevel + 0.5f;
            log.AppendLine($"day2: threat={day2.threatLevel:F2} barrier={day2.barrierPercent:F1} pass={threatPass}");

            verify.success = dayPass && injectPass && threatPass;
            verify.message = log.ToString().TrimEnd();
        }
        catch (Exception exception)
        {
            verify.success = false;
            verify.message = exception.Message;
        }

        WriteVerifyLog(verify);
        return verify;
    }

    private static void WriteVerifyLog(DailySimulationVerifyResult verify)
    {
        try
        {
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
            string path = Path.Combine(projectRoot, "Logs", "daily_simulation_verify.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? projectRoot);
            File.WriteAllText(
                path,
                $"{DateTime.UtcNow:O} success={verify.success}\n{verify.message}\n",
                Encoding.UTF8);
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[DailySimulationEngine] 検証ログスキップ: {exception.Message}");
        }
    }

    private sealed class DailyProductionOutcome
    {
        public WorkSpotData spot;
        public NpcCivicJob civicJob;
        public string jobLabel = string.Empty;
        public string spotLabel = string.Empty;
        public string woodcutterLabel = string.Empty;
        public float foodGained;
        public float timberGained;
    }
}

public static class DailySimulationBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoAttach()
    {
        DailySimulationEngine.EnsureInstance();
        DailySimulationPresenter.EnsureInstance();
    }
}

#if UNITY_EDITOR
public static class DailySimulationEngineMenu
{
    [MenuItem("Tools/Procedural Map/Verify Daily Simulation")]
    public static void VerifyFromMenu()
    {
        DailySimulationVerifyResult result = DailySimulationEngine.RunVerification();
        Debug.Log(
            result.success
                ? $"<color=#A5D6A7><b>【デイリーシミュレーション検証】PASS</b></color>\n{result.message}"
                : $"<color=#FF8A80><b>【デイリーシミュレーション検証】FAIL</b></color>\n{result.message}");
        EditorUtility.DisplayDialog("Daily Simulation", result.message, "OK");
    }

    /// <summary>Unity バッチ: -executeMethod DailySimulationEngineMenu.BatchVerifyAndQuit</summary>
    public static void BatchVerifyAndQuit()
    {
        DailySimulationVerifyResult result = DailySimulationEngine.RunVerification();
        Debug.Log(
            result.success
                ? $"<color=#A5D6A7><b>【デイリーシミュレーション検証】PASS</b></color>\n{result.message}"
                : $"<color=#FF8A80><b>【デイリーシミュレーション検証】FAIL</b></color>\n{result.message}");
        EditorApplication.Exit(result.success ? 0 : 1);
    }
}
#endif

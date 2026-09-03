using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

// =============================================================================
// 複数年年代進行 — 365 日完了 / 手動年越し → 翌ターン (T+1) へ拡張
// 連携: DailySimulationEngine / MicroToMacroAggregator / HistoryBranchManager
//       WestRegionSimulationManager / TimelineCharacterSelector / EraContextResolver
// =============================================================================

/// <summary>ターン移行結果。</summary>
public sealed class TurnTransitionResult
{
    public bool success;
    public bool advanced;
    public bool cappedAtMaxTurn;
    public bool loopMode;
    public int previousTurn = 1;
    public int newTurn = 1;
    public int dayOfYear = 1;
    public float power;
    public float barrier;
    public float threat;
    public bool survival;
    public int propagatedNations;
    public string message = string.Empty;
}

/// <summary>検証結果。</summary>
public sealed class TurnTransitionVerifyResult
{
    public bool success;
    public string message = string.Empty;
}

/// <summary>
/// デイリー 365 日完了時または手動年越しで、ミクロ成果と ALT_ 改変を引き継ぎ翌ターンへ進めます。
/// </summary>
[DefaultExecutionOrder(47)]
public class TurnTransitionEngine : MonoBehaviour
{
    public const string LogTag = "【年代進行】";
    public const int DefaultNationId = MicroToMacroAggregator.DefaultNationId;

    public static TurnTransitionEngine Instance { get; private set; }

    [SerializeField] private bool enableLoopModeAtMaxTurn = false;

    public static TurnTransitionEngine EnsureInstance()
    {
        if (Instance != null)
        {
            return Instance;
        }

        TurnTransitionEngine existing = UnityEngine.Object.FindAnyObjectByType<TurnTransitionEngine>();
        if (existing != null)
        {
            Instance = existing;
            return existing;
        }

        GameObject hub = GameObject.Find("DebugSystemsHub");
        GameObject host = hub != null ? hub : new GameObject(nameof(TurnTransitionEngine));
        TurnTransitionEngine engine = host.GetComponent<TurnTransitionEngine>();
        return engine != null ? engine : host.AddComponent<TurnTransitionEngine>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    /// <summary>当年成果を集計し、翌ターン (T+1) へ進行します。</summary>
    public static TurnTransitionResult AdvanceToNextYear(bool fromDailyCompletion = false)
    {
        TurnTransitionResult result = new TurnTransitionResult();
        try
        {
            TurnTransitionEngine engine = EnsureInstance();
            int nationId = DefaultNationId;
            int previousTurn = ResolveCurrentTurn();
            result.previousTurn = previousTurn;

            MicroToMacroAggregator aggregator = MicroToMacroAggregator.EnsureInstance();
            HistoryBranchManager.EnsureInstance();
            HistoryFlagRegistry.EnsureWired();

            if (!aggregator.YearEndApplied)
            {
                aggregator.AggregateYearEnd(logMacro: true);
            }

            WestRegionSimulationManager.TryCompleteYearEnd();

            MacroStats stats = aggregator.Stats;
            float closingPower = stats.Power;
            float closingBarrier = stats.BarrierEfficiency;
            HistoryBranchManager.TryApplyToMacroStats(stats, previousTurn, nationId);
            closingPower = stats.Power;
            closingBarrier = stats.BarrierEfficiency;

            int nextTurn = previousTurn + 1;
            if (nextTurn > EraContextResolver.MaxTurn)
            {
                return engine.HandleMaxTurnSafeFail(result, previousTurn, closingPower, closingBarrier);
            }

            MicroHistoryTimeline nextTimeline =
                MicroHistoryTimelineTimelineEngine.BuildTimeline(nationId, nextTurn);
            if (nextTimeline == null || nextTimeline.usedSafeFailFallback ||
                nextTimeline.phases == null || nextTimeline.phases.Length == 0)
            {
                result.success = true;
                result.advanced = false;
                result.newTurn = previousTurn;
                result.power = closingPower;
                result.barrier = closingBarrier;
                result.message = "次ターン・タイムライン欠損 — 年次集計のみで Safe-Fail 停止";
                Debug.LogWarning($"[TurnTransitionEngine] {result.message}");
                return result;
            }

            float basePower = closingPower;
            float geoBarrierPrevious = aggregator.ResolveGeoBarrierBaseline(previousTurn);
            float geoBarrierNext = aggregator.ResolveGeoBarrierBaseline(nextTurn);
            float baseBarrier = Mathf.Max(
                MicroToMacroAggregator.NormalizeCanonicalBarrier(closingBarrier),
                geoBarrierPrevious,
                geoBarrierNext);
            float adjustedPower = HistoryBranchManager.GetAdjustedPower(basePower, nextTurn, nationId);
            float adjustedBarrier = HistoryBranchManager.GetAdjustedBarrier(baseBarrier, nextTurn, nationId);
            adjustedBarrier = Mathf.Clamp01(Mathf.Max(adjustedBarrier, geoBarrierNext));
            bool adjustedSurvival = HistoryBranchManager.GetAdjustedSurvival(true, nextTurn, nationId);
            float adjustedThreat = HistoryBranchManager.GetAdjustedThreat(1f, nextTurn, nationId);

            aggregator.ApplyOpeningMacroForTurn(adjustedPower, adjustedBarrier);
            adjustedPower = aggregator.Stats.Power;
            adjustedBarrier = aggregator.Stats.BarrierEfficiency;
            if (!adjustedSurvival)
            {
                MacroStats statsAfterSurvival = aggregator.Stats;
                statsAfterSurvival.Economy *= 0.85f;
                statsAfterSurvival.Military *= 0.9f;
                statsAfterSurvival.RecalculatePower();
                adjustedPower = statsAfterSurvival.Power;
            }

            float ripplePreviousBarrier = Mathf.Max(
                MicroToMacroAggregator.NormalizeCanonicalBarrier(closingBarrier),
                geoBarrierPrevious);
            float ripplePreviousPower = closingPower;

            EraContextResolver.TrySetCurrentTurn(nextTurn);

            aggregator.PrepareTurnTransition(nextTurn, applyBranchAdjustments: false);
            aggregator.BeginYearBaseline();

            GameMode mode = StoryTimelineManager.EnsureInstance().CurrentMode;
            StoryTimelineManager.EnsureInstance().TryBootstrapStoryYear(nextTurn, mode);

            DailySimulationEngine daily = DailySimulationEngine.EnsureInstance();
            daily.TryPrepareForTurn(nextTurn);

            WestRegionSimulationManager west = WestRegionSimulationManager.EnsureInstance();
            west.ReinitializeForTurn(nextTurn);
            int propagated = west.PropagateFocalNationInfluence(
                nextTurn,
                adjustedPower,
                adjustedBarrier,
                adjustedThreat,
                ripplePreviousBarrier,
                ripplePreviousPower);
            west.ResetYearEndWritebackFlag();

            try
            {
                HistoricalNpcCaster.SpawnHistoricalKeyCharacters(nextTurn);
            }
            catch (Exception spawnException)
            {
                Debug.LogWarning($"[TurnTransitionEngine] 歴史主要人物スポーン省略: {spawnException.Message}");
            }

            TimelineCharacterSelector.EnsureInstance().AutoPossessForCurrentMode();

            result.success = true;
            result.advanced = true;
            result.newTurn = nextTurn;
            result.dayOfYear = daily.CurrentDayOfYear;
            result.power = adjustedPower;
            result.barrier = adjustedBarrier;
            result.threat = adjustedThreat;
            result.survival = adjustedSurvival;
            result.propagatedNations = propagated;
            result.message =
                $"国力={adjustedPower.ToString("F0", CultureInfo.InvariantCulture)} " +
                $"結界={adjustedBarrier.ToString("F3", CultureInfo.InvariantCulture)} " +
                $"脅威={adjustedThreat.ToString("F2", CultureInfo.InvariantCulture)} " +
                $"LOD波及={propagated}国 " +
                $"Day={result.dayOfYear}";

            Debug.Log(
                $"<color=#A5D6A7><b>{LogTag}</b></color> ターン{previousTurn} ➔ ターン{nextTurn} へ移行しました！ " +
                $"(国力:{adjustedPower.ToString("F0", CultureInfo.InvariantCulture)}, " +
                $"結界:{adjustedBarrier.ToString("F3", CultureInfo.InvariantCulture)}) " +
                (fromDailyCompletion ? "[365日完了]" : "[手動年越し]"));

            return result;
        }
        catch (Exception exception)
        {
            result.success = false;
            result.message = $"Safe-Fail: {exception.Message}";
            Debug.LogWarning($"[TurnTransitionEngine] AdvanceToNextYear Safe-Fail: {exception.Message}");
            return result;
        }
    }

    public static TurnTransitionVerifyResult RunVerification()
    {
        TurnTransitionVerifyResult verify = new TurnTransitionVerifyResult();
        StringBuilder log = new StringBuilder();
        try
        {
            SimulationVerifyBootstrap.PrepareFreshStoryDay(1, GameMode.StoryMode);
            HistoryFlagRegistry.EnsureWired();
            MasterDataManager.EnsureInstance();
            HistoryBranchManager.EnsureInstance().ClearRegisteredBranches();

            MacroParamDelta delta = new MacroParamDelta
            {
                powerMultiplier = 1.12f,
                barrierEfficiencyDelta = 0.05f,
                threatMultiplier = 0.92f
            };
            HistoryBranchManager.RegisterHistoryAlteration(
                1,
                DefaultNationId,
                "HIST_NATION_001_GEO_TURN_001_HERO",
                createNewBranch: true,
                deltaOverride: delta);
            HistoryBranchManager.SelectTimelineBranch(
                HistoryBranchManager.EnsureInstance().CurrentActiveBranch.branchId);

            DailySimulationEngine daily = DailySimulationEngine.EnsureInstance();
            daily.ResetSessionForVerification();
            MicroToMacroAggregator agg = MicroToMacroAggregator.EnsureInstance();
            agg.BeginYearBaseline();

            float geoBarrierT1 = agg.ResolveGeoBarrierBaseline(1);
            TurnTransitionResult manual = AdvanceToNextYear(fromDailyCompletion: false);
            bool turnPass = manual.advanced && manual.newTurn == 2 && daily.Turn == 2;
            bool dayPass = daily.CurrentDayOfYear == 1;
            float expectedBarrierT2 = HistoryBranchManager.GetAdjustedBarrier(
                Mathf.Max(geoBarrierT1, 0.95f),
                2,
                DefaultNationId);
            bool altPass = manual.power >= 1000f &&
                           manual.barrier >= expectedBarrierT2 - 0.02f;
            bool ripplePass = manual.propagatedNations > 0;

            log.AppendLine(
                $"manual-advance: T{manual.previousTurn}->{manual.newTurn} " +
                $"power={manual.power:F0} barrier={manual.barrier:F3} expectedBarrier={expectedBarrierT2:F3} " +
                $"propagated={manual.propagatedNations}");
            log.AppendLine(
                $"turn={turnPass} day1={dayPass} alt={altPass} ripple={ripplePass} " +
                $"pass={turnPass && dayPass && altPass && ripplePass}");

            SimulationVerifyBootstrap.PrepareFreshStoryDay(EraContextResolver.MaxTurn, GameMode.StoryMode);
            daily.ResetSessionForVerification();
            daily.TryPrepareForTurn(EraContextResolver.MaxTurn);
            agg.PrepareTurnTransition(EraContextResolver.MaxTurn, applyBranchAdjustments: false);
            agg.BeginYearBaseline();
            EraContextResolver.TrySetCurrentTurn(EraContextResolver.MaxTurn);
            TurnTransitionResult cap = AdvanceToNextYear(fromDailyCompletion: false);
            bool capPass = cap.cappedAtMaxTurn && cap.newTurn == EraContextResolver.MaxTurn && !cap.advanced;
            log.AppendLine($"max-turn-cap: turn={cap.newTurn} capped={cap.cappedAtMaxTurn} pass={capPass}");

            verify.success = turnPass && dayPass && altPass && ripplePass && capPass;
            verify.message = log.ToString().TrimEnd();
        }
        catch (Exception exception)
        {
            verify.success = false;
            verify.message = $"Safe-Fail: {exception.Message}";
        }

        WriteVerifyLog(verify);
        return verify;
    }

    private TurnTransitionResult HandleMaxTurnSafeFail(
        TurnTransitionResult result,
        int previousTurn,
        float power,
        float barrier)
    {
        result.success = true;
        result.advanced = false;
        result.cappedAtMaxTurn = true;
        result.newTurn = previousTurn;
        result.power = power;
        result.barrier = barrier;

        if (enableLoopModeAtMaxTurn)
        {
            result.loopMode = true;
            EraContextResolver.TrySetCurrentTurn(1);
            MicroToMacroAggregator.EnsureInstance().PrepareTurnTransition(1);
            DailySimulationEngine.EnsureInstance().TryPrepareForTurn(1);
            WestRegionSimulationManager.EnsureInstance().ReinitializeForTurn(1);
            result.newTurn = 1;
            result.advanced = true;
            result.message = $"ターン{EraContextResolver.MaxTurn}到達 — 周回モードで T1 へ Safe-Fail 復帰";
        }
        else
        {
            result.message = $"ターン{EraContextResolver.MaxTurn}到達 — 最終ターン維持（Safe-Fail）";
        }

        Debug.LogWarning(
            $"<color=#FFCC80><b>{LogTag}</b></color> {result.message} " +
            $"(国力:{power:F0}, 結界:{barrier:F3})");
        return result;
    }

    private static int ResolveCurrentTurn()
    {
        DailySimulationEngine daily = DailySimulationEngine.Instance;
        if (daily != null && daily.Turn > 0)
        {
            return daily.Turn;
        }

        MicroToMacroAggregator agg = MicroToMacroAggregator.Instance;
        if (agg != null && agg.Turn > 0)
        {
            return agg.Turn;
        }

        return EraContextResolver.CurrentTurn > 0 ? EraContextResolver.CurrentTurn : 1;
    }

    private static void WriteVerifyLog(TurnTransitionVerifyResult verify)
    {
        try
        {
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
            string path = Path.Combine(projectRoot, "Logs", "turn_transition_verify.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? projectRoot);
            File.WriteAllText(
                path,
                $"{DateTime.UtcNow:O} success={verify.success}\n{verify.message}\n",
                Encoding.UTF8);
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[TurnTransitionEngine] 検証ログスキップ: {exception.Message}");
        }
    }
}

public static class TurnTransitionEngineBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoAttach()
    {
        TurnTransitionEngine.EnsureInstance();
    }
}

#if UNITY_EDITOR
public static class TurnTransitionEngineMenu
{
    [MenuItem("Tools/Procedural Map/Verify Turn Transition Engine")]
    public static void VerifyFromMenu()
    {
        TurnTransitionVerifyResult result = TurnTransitionEngine.RunVerification();
        Debug.Log(
            result.success
                ? $"<color=#A5D6A7><b>【年代進行検証】PASS</b></color>\n{result.message}"
                : $"<color=#FF8A80><b>【年代進行検証】FAIL</b></color>\n{result.message}");
        EditorUtility.DisplayDialog("Turn Transition Engine", result.message, "OK");
    }

    [MenuItem("Tools/Procedural Map/Advance To Next Year (Turn Transition)")]
    public static void AdvanceFromMenu()
    {
        TurnTransitionResult result = TurnTransitionEngine.AdvanceToNextYear();
        Debug.Log(
            result.success
                ? $"<color=#A5D6A7><b>{TurnTransitionEngine.LogTag}</b></color> {result.message}"
                : $"<color=#FFCC80><b>{TurnTransitionEngine.LogTag}</b></color> {result.message}");
        EditorUtility.DisplayDialog(
            "Turn Transition",
            result.success
                ? $"T{result.previousTurn} → T{result.newTurn}\n{result.message}"
                : result.message,
            "OK");
    }
}
#endif

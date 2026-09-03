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
// 領域・西 全35カ国 — LOD統合・個性バイアス広域シミュレーション
// =============================================================================

/// <summary>領域・西 統合検証結果。</summary>
[Serializable]
public sealed class WestRegionVerifyResult
{
    public bool success;
    public string message = string.Empty;
    public int totalNations;
    public int lod0Count;
    public int lod1Count;
    public int writebackOk;
    public int regionalTrades;
}

/// <summary>
/// 領域・西の全国家を LOD 制御のもと並行駆動し、領域ネットワークと geo 書き戻しを行います。
/// 国家001の geo 書き戻しは MicroToMacroAggregator に委譲します。
/// </summary>
[DefaultExecutionOrder(56)]
public class WestRegionSimulationManager : MonoBehaviour
{
    public const int DefaultTurn = 1;
    public const int DefaultFocalNationId = 1;
    public const int ExpectedLod0Count = 3;
    public const int MinimumWestNations = 35;
    public const int WestRegionNationCountCap = 35;
    public const string LogTag = "【全35カ国広域シミュレーション】";

    public static WestRegionSimulationManager Instance { get; private set; }

    [SerializeField] private int turn = DefaultTurn;
    [SerializeField] private int focalNationId = DefaultFocalNationId;
    [SerializeField] private bool westRegionActive = true;
    [SerializeField] private RegionalMarketAndThreat regionalNetwork = new RegionalMarketAndThreat();
    [SerializeField] private List<WestRegionNationRuntime> westNations = new List<WestRegionNationRuntime>();
    [SerializeField] private int lastTickedPhase = -1;
    [SerializeField] private bool yearEndWritebackDone;
    [SerializeField] private int lastWritebackOkCount;

    public bool IsWestRegionActive => westRegionActive && westNations != null && westNations.Count > 0;
    public int LastWritebackOkCount => lastWritebackOkCount;
    public RegionalMarketAndThreat RegionalNetwork => regionalNetwork;
    public IReadOnlyList<WestRegionNationRuntime> WestNations => westNations;
    public int FocalNationId => focalNationId;
    public int Lod0Count => regionalNetwork != null ? regionalNetwork.CountLod(westNations, WestRegionLodLevel.Lod0FullMicro) : 0;
    public int Lod1Count => regionalNetwork != null ? regionalNetwork.CountLod(westNations, WestRegionLodLevel.Lod1MacroAbstract) : 0;

    public static WestRegionSimulationManager EnsureInstance()
    {
        if (Instance != null)
        {
            return Instance;
        }

        WestRegionSimulationManager existing = FindAnyObjectByType<WestRegionSimulationManager>();
        if (existing != null)
        {
            Instance = existing;
            return Instance;
        }

        GameObject hub = GameObject.Find("DebugSystemsHub");
        GameObject host = hub != null ? hub : new GameObject(nameof(WestRegionSimulationManager));
        WestRegionSimulationManager mgr = host.GetComponent<WestRegionSimulationManager>();
        return mgr != null ? mgr : host.AddComponent<WestRegionSimulationManager>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        TryInitializeWestRegion();
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    public static void TryTickWestRegionPhase(int phase, VariableTimelineSeason season)
    {
        try
        {
            WestRegionSimulationManager mgr = EnsureInstance();
            if (!mgr.westRegionActive)
            {
                return;
            }

            mgr.TickWestRegionPhase(phase, season);
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[WestRegionSimulationManager] Tick Safe-Fail: {exception.Message}");
        }
    }

    public static void TryCompleteYearEnd()
    {
        try
        {
            WestRegionSimulationManager mgr = EnsureInstance();
            mgr.CompleteYearEndWriteback();
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[WestRegionSimulationManager] YearEnd Safe-Fail: {exception.Message}");
        }
    }

    /// <summary>年代進行後に広域 LOD を新ターンで再構築します。</summary>
    public void ReinitializeForTurn(int newTurn)
    {
        turn = Mathf.Clamp(newTurn, EraContextResolver.MinTurn, EraContextResolver.MaxTurn);
        yearEndWritebackDone = false;
        lastTickedPhase = -1;
        lastWritebackOkCount = 0;
        if (westNations != null)
        {
            westNations.Clear();
        }

        regionalNetwork?.ResetNetwork();
        TryInitializeWestRegion();
    }

    public void ResetYearEndWritebackFlag()
    {
        yearEndWritebackDone = false;
    }

    /// <summary>国家001の国力・結界変化を LOD0 近接国 / LOD1 隣接国へ波及させます。</summary>
    public int PropagateFocalNationInfluence(
        int atTurn,
        float focalPower,
        float focalBarrierNorm,
        float focalThreat,
        float previousBarrierNorm,
        float previousPower)
    {
        if (!TryInitializeWestRegion())
        {
            return 0;
        }

        turn = Mathf.Clamp(atTurn, EraContextResolver.MinTurn, EraContextResolver.MaxTurn);
        SyncNation001FromAggregator();

        WestRegionNationRuntime focal = FindRuntime(focalNationId) ?? FindRuntime(DefaultFocalNationId);
        float barrierChange = focalBarrierNorm - previousBarrierNorm;
        float powerChange = focalPower - previousPower;

        if (focal?.snapshot != null)
        {
            focal.snapshot.power = focalPower;
            focal.snapshot.barrierEfficiency = Mathf.Clamp01(focalBarrierNorm);
            focal.snapshot.economy = Mathf.Max(0f, focal.snapshot.economy + powerChange * 0.35f);
            focal.snapshot.power = focalPower;
        }

        if (regionalNetwork != null)
        {
            regionalNetwork.regionalThreatIndex = Mathf.Max(
                0.5f,
                focalThreat + Mathf.Max(0f, -barrierChange) * 3.5f + Mathf.Max(0f, powerChange) * 0.0008f);
        }

        const float lod1NeighborRadiusDeg = 20f;
        const float minBarrierChange = 0.001f;
        const float minPowerChange = 0.5f;
        int affected = 0;
        for (int i = 0; i < westNations.Count; i++)
        {
            WestRegionNationRuntime runtime = westNations[i];
            if (runtime?.snapshot == null)
            {
                continue;
            }

            if (runtime.snapshot.id == focalNationId || runtime.snapshot.id == DefaultFocalNationId)
            {
                continue;
            }

            float distance = runtime.distanceDeg;
            if (distance <= 0.01f && focal?.snapshot != null)
            {
                distance = RegionalMarketAndThreat.DistanceDeg(
                    focal.snapshot.lat,
                    focal.snapshot.lng,
                    runtime.snapshot.lat,
                    runtime.snapshot.lng);
                runtime.distanceDeg = distance;
            }

            bool isLod0Neighbor =
                runtime.lod == WestRegionLodLevel.Lod0FullMicro &&
                distance <= RegionalMarketAndThreat.Lod0DistanceDeg + 0.01f;
            bool isLod1Neighbor =
                runtime.lod == WestRegionLodLevel.Lod1MacroAbstract &&
                distance <= lod1NeighborRadiusDeg;
            if (!isLod0Neighbor && !isLod1Neighbor)
            {
                continue;
            }

            if (!runtime.alive && !isLod1Neighbor)
            {
                continue;
            }

            if (Mathf.Abs(barrierChange) < minBarrierChange && Mathf.Abs(powerChange) < minPowerChange)
            {
                continue;
            }

            float weightRadius = isLod0Neighbor
                ? RegionalMarketAndThreat.Lod0DistanceDeg
                : lod1NeighborRadiusDeg;
            float weight = 1f - Mathf.Clamp01(distance / weightRadius);
            float barrierRippleMul = isLod0Neighbor ? 0.12f : 0.08f;
            if (barrierChange < 0f)
            {
                float threatRipple = -barrierChange * 0.15f * weight;
                runtime.snapshot.barrierEfficiency = Mathf.Clamp01(
                    runtime.snapshot.barrierEfficiency - threatRipple * 0.35f);
                runtime.snapshot.military = Mathf.Max(0f, runtime.snapshot.military + threatRipple * 45f);
            }
            else if (barrierChange > 0f)
            {
                runtime.snapshot.barrierEfficiency = Mathf.Clamp01(
                    runtime.snapshot.barrierEfficiency + barrierChange * barrierRippleMul * weight);
            }

            runtime.snapshot.economy = Mathf.Max(0f, runtime.snapshot.economy + powerChange * 0.03f * weight);
            runtime.snapshot.power = runtime.snapshot.economy + runtime.snapshot.military + runtime.snapshot.magic;
            runtime.writeback = BuildWritebackFromSnapshot(runtime.snapshot, turn);
            runtime.writeback.source = "WestRegionSimulationManager:TurnTransitionRipple";
            affected++;

            if (runtime.snapshot.id == 64)
            {
                Debug.Log(
                    $"<color=#4DD0E1><b>{LogTag} 隣接波及</b></color> " +
                    $"国家{runtime.snapshot.id:D3} Δbarrier={barrierChange:F3} Δpower={powerChange:F1} " +
                    $"→ barrier={runtime.snapshot.barrierEfficiency:F3} power={runtime.snapshot.power:F1}");
            }
        }

        return affected;
    }

    public void SetFocusedNation(int nationId)
    {
        focalNationId = Mathf.Max(1, nationId);
        WestRegionNationRuntime focal = FindRuntime(focalNationId);
        if (focal?.snapshot == null)
        {
            Debug.LogWarning(
                $"[WestRegionSimulationManager] フォーカス国家{nationId:D3} が領域内に見つかりません（Safe-Fail）。");
            return;
        }

        regionalNetwork.ClassifyLod(
            westNations,
            focalNationId,
            focal.snapshot.lat,
            focal.snapshot.lng);
        BootstrapLod0MicroCells();

        Debug.Log(
            $"<color=#4DD0E1><b>{LogTag} LOD Focus Switch</b></color> " +
            $"フォーカス=国家{focalNationId:D3} LOD0:{Lod0Count} LOD1:{Lod1Count}");
    }

    public bool TryInitializeWestRegion()
    {
        if (westNations != null && westNations.Count > 0)
        {
            return true;
        }

        try
        {
            string geoPath = MicroHistoryTimelineTimelineEngine.ResolveGeoPath();
            string logPath = MicroHistoryTimelineTimelineEngine.ResolveLogPath();
            List<MacroChronicleNationSnapshot> snaps =
                MacroChronicleJsonScan.ReadNationsInRegionAtTurn(
                    geoPath,
                    turn,
                    RegionalMarketAndThreat.WestRegionName);
            snaps = FilterAndCapWestNations(snaps, WestRegionNationCountCap);
            if (snaps == null || snaps.Count == 0)
            {
                Debug.LogWarning($"{LogTag} 領域・西 国家0件 — Safe-Fail フォールバック。");
                return false;
            }

            westNations = new List<WestRegionNationRuntime>(snaps.Count);
            for (int i = 0; i < snaps.Count; i++)
            {
                MacroChronicleNationSnapshot snap = snaps[i];
                if (snap == null)
                {
                    continue;
                }

                WestRegionNationRuntime runtime = CreateRuntimeFromSnapshot(snap);
                westNations.Add(runtime);
            }

            NationBiasInjector.InjectBatch(westNations, turn, logPath);
            LogNationBiasTable();

            WestRegionNationRuntime focal = FindRuntime(focalNationId) ?? FindRuntime(DefaultFocalNationId);
            if (focal?.snapshot != null)
            {
                focalNationId = focal.snapshot.id;
                regionalNetwork.ResetNetwork();
                regionalNetwork.ClassifyLod(
                    westNations,
                    focalNationId,
                    focal.snapshot.lat,
                    focal.snapshot.lng);
                BootstrapLod0MicroCells();
            }

            yearEndWritebackDone = false;
            Debug.Log(
                $"<color=#4DD0E1><b>{LogTag}</b></color> 初期化 " +
                $"全{westNations.Count}カ国 LOD0:{Lod0Count} LOD1:{Lod1Count} " +
                $"フォーカス=国家{focalNationId:D3} {SummarizeArchetypeCounts()}");
            return westNations.Count > 0;
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[WestRegionSimulationManager] 初期化 Safe-Fail: {exception.Message}");
            return false;
        }
    }

    private WestRegionNationRuntime CreateRuntimeFromSnapshot(MacroChronicleNationSnapshot snap)
    {
        WestRegionNationRuntime runtime = new WestRegionNationRuntime
        {
            snapshot = snap,
            alive = snap.alive,
            abstractFood = 36f * Mathf.Clamp(snap.territory / 30f, 0.5f, 1.5f),
            abstractTimber = 28f * Mathf.Clamp(snap.territory / 30f, 0.5f, 1.5f),
            abstractOre = 12f * Mathf.Clamp(snap.territory / 30f, 0.5f, 1.5f),
            abstractCrystal = 8f * Mathf.Clamp(snap.magic / 300f, 0.4f, 1.4f)
        };
        runtime.writeback = BuildWritebackFromSnapshot(snap, turn);
        return runtime;
    }

    private static List<MacroChronicleNationSnapshot> FilterAndCapWestNations(
        List<MacroChronicleNationSnapshot> source,
        int cap)
    {
        if (source == null || source.Count == 0)
        {
            return new List<MacroChronicleNationSnapshot>();
        }

        List<MacroChronicleNationSnapshot> alive = new List<MacroChronicleNationSnapshot>();
        List<MacroChronicleNationSnapshot> dead = new List<MacroChronicleNationSnapshot>();
        for (int i = 0; i < source.Count; i++)
        {
            MacroChronicleNationSnapshot snap = source[i];
            if (snap == null)
            {
                continue;
            }

            if (snap.alive)
            {
                alive.Add(snap);
            }
            else
            {
                dead.Add(snap);
            }
        }

        alive.Sort((a, b) => a.id.CompareTo(b.id));
        dead.Sort((a, b) => a.id.CompareTo(b.id));

        List<MacroChronicleNationSnapshot> result = new List<MacroChronicleNationSnapshot>(cap);
        for (int i = 0; i < alive.Count && result.Count < cap; i++)
        {
            result.Add(alive[i]);
        }

        for (int i = 0; i < dead.Count && result.Count < cap; i++)
        {
            result.Add(dead[i]);
        }

        return result;
    }

    private void LogNationBiasTable()
    {
        if (westNations == null)
        {
            return;
        }

        for (int i = 0; i < westNations.Count; i++)
        {
            WestRegionNationRuntime runtime = westNations[i];
            if (runtime?.snapshot == null || runtime.bias == null)
            {
                continue;
            }

            string lod = runtime.lod == WestRegionLodLevel.Lod0FullMicro ? "LOD0" : "LOD1";
            string aliveTag = runtime.alive ? "生存" : "滅亡";
            Debug.Log(
                $"<color=#4DD0E1><b>{LogTag}</b></color> バイアス注入 " +
                $"国家{runtime.snapshot.id:D3} {runtime.snapshot.name} {lod} {aliveTag} " +
                runtime.bias.FormatShort());
        }
    }

    private string SummarizeArchetypeCounts()
    {
        if (westNations == null || westNations.Count == 0)
        {
            return string.Empty;
        }

        int mountain = 0;
        int plains = 0;
        int disaster = 0;
        int stable = 0;
        int neutral = 0;
        for (int i = 0; i < westNations.Count; i++)
        {
            NationBiasData bias = westNations[i]?.bias;
            if (bias == null)
            {
                continue;
            }

            switch (bias.primaryArchetype)
            {
                case NationBiasArchetype.MountainHighland:
                    mountain++;
                    break;
                case NationBiasArchetype.PlainsAgricultural:
                    plains++;
                    break;
                case NationBiasArchetype.DisasterRecovery:
                    disaster++;
                    break;
                case NationBiasArchetype.StableAffluent:
                    stable++;
                    break;
                default:
                    neutral++;
                    break;
            }
        }

        return $"山岳{mountain} 平野{plains} 被災{disaster} 安定{stable} 標準{neutral}";
    }

    private void BootstrapLod0MicroCells()
    {
        if (westNations == null)
        {
            return;
        }

        for (int i = 0; i < westNations.Count; i++)
        {
            WestRegionNationRuntime runtime = westNations[i];
            if (runtime == null || !runtime.alive || runtime.snapshot == null)
            {
                continue;
            }

            if (runtime.lod != WestRegionLodLevel.Lod0FullMicro)
            {
                runtime.microCell = null;
                continue;
            }

            if (runtime.snapshot.id == MicroToMacroAggregator.DefaultNationId)
            {
                continue;
            }

            if (runtime.microCell == null || !runtime.microCell.initialized ||
                runtime.microCell.nationId != runtime.snapshot.id)
            {
                runtime.microCell = WestRegionMicroCellOps.BootstrapMicroCell(runtime.snapshot, runtime.bias);
            }
            else if (runtime.bias != null)
            {
                runtime.microCell.bias = runtime.bias;
            }
        }
    }

    public void TickWestRegionPhase(int phase, VariableTimelineSeason season)
    {
        if (!TryInitializeWestRegion())
        {
            return;
        }

        int normalized = ProceduralMapPopulator.NormalizePhase(phase);
        VillageStorageMarket primaryMarket = ResolvePrimaryMarket();

        for (int i = 0; i < westNations.Count; i++)
        {
            WestRegionNationRuntime runtime = westNations[i];
            if (runtime == null || !runtime.alive || runtime.snapshot == null)
            {
                continue;
            }

            if (runtime.snapshot.id == MicroToMacroAggregator.DefaultNationId)
            {
                continue;
            }

            if (runtime.lod == WestRegionLodLevel.Lod0FullMicro && runtime.microCell != null)
            {
                WestRegionMicroCellOps.TickMicroCell(runtime.microCell, season);
            }
            else
            {
                TickAbstractNation(runtime, season);
            }
        }

        regionalNetwork.AggregateSurplusDeficit(westNations, primaryMarket);
        int tradeCountBefore = regionalNetwork.regionalTrades.Count;
        regionalNetwork.ProcessRegionalTrade(westNations, normalized, primaryMarket);
        regionalNetwork.PropagateManaEcology(westNations, normalized);
        for (int t = tradeCountBefore; t < regionalNetwork.regionalTrades.Count; t++)
        {
            RegionalTradeLogEntry entry = regionalNetwork.regionalTrades[t];
            Debug.Log(
                $"<color=#4DD0E1><b>{LogTag}</b></color> RegionalTrade " +
                entry.FormatLine());
        }

        lastTickedPhase = normalized;
        Debug.Log(
            $"<color=#4DD0E1><b>{LogTag}</b></color> " +
            $"位相{normalized}/24 {season.ToString().ToUpperInvariant()} " +
            $"LOD0:{Lod0Count} LOD1:{Lod1Count} {SummarizeArchetypeCounts()} " +
            $"領域魔力{regionalNetwork.regionalManaEcologyLevel:F1} " +
            $"食料余剰{regionalNetwork.aggregateFoodSurplus:F1} 不足{regionalNetwork.aggregateFoodDeficit:F1} " +
            $"領域交易{regionalNetwork.regionalTrades.Count}件");

        if (normalized == 1 || normalized == 12 || normalized == ProceduralMapPopulator.PhaseCount)
        {
            LogLodNationDigest(normalized);
        }
    }

    private void LogLodNationDigest(int phase)
    {
        if (westNations == null)
        {
            return;
        }

        for (int i = 0; i < westNations.Count; i++)
        {
            WestRegionNationRuntime runtime = westNations[i];
            if (runtime == null || !runtime.alive || runtime.snapshot == null || runtime.bias == null)
            {
                continue;
            }

            if (runtime.snapshot.id == MicroToMacroAggregator.DefaultNationId)
            {
                continue;
            }

            string lod = runtime.lod == WestRegionLodLevel.Lod0FullMicro ? "LOD0" : "LOD1";
            float food;
            float ore;
            float crystal;
            if (runtime.lod == WestRegionLodLevel.Lod0FullMicro && runtime.microCell?.market != null)
            {
                food = runtime.microCell.market.Food;
                ore = runtime.microCell.market.Ore;
                crystal = runtime.microCell.market.ManaCrystal;
            }
            else
            {
                food = runtime.abstractFood;
                ore = runtime.abstractOre;
                crystal = runtime.abstractCrystal;
            }

            Debug.Log(
                $"<color=#4DD0E1><b>{LogTag}</b></color> 駆動ダイジェスト 位相{phase} " +
                $"国家{runtime.snapshot.id:D3} {lod} {runtime.bias.label} " +
                $"Food{food:F1} Ore{ore:F1} Cry{crystal:F1} " +
                $"F×{runtime.bias.foodYieldMul:F2} Ore×{runtime.bias.oreYieldMul:F2}");
        }
    }

    private static void TickAbstractNation(WestRegionNationRuntime runtime, VariableTimelineSeason season)
    {
        MacroChronicleNationSnapshot snap = runtime.snapshot;
        NationBiasData bias = runtime.bias;
        float foodMul = bias != null ? bias.foodYieldMul : 1f;
        float oreMul = bias != null ? bias.oreYieldMul : 1f;
        float crystalMul = bias != null ? bias.crystalYieldMul : 1f;
        float timberMul = bias != null ? bias.timberYieldMul : 1f;
        float upkeepMul = bias != null ? bias.facilityUpkeepMul : 1f;
        float seasonProd = season == VariableTimelineSeason.Active ? 1f :
            season == VariableTimelineSeason.Escalation ? 0.75f :
            season == VariableTimelineSeason.Deescalation ? 0.55f : 0.35f;
        float seasonDecay = season == VariableTimelineSeason.Active ? 0.004f :
            season == VariableTimelineSeason.Escalation ? 0.006f :
            season == VariableTimelineSeason.Deescalation ? 0.002f : 0.001f;

        runtime.abstractFood += RegionalMarketAndThreat.Lod1FoodProduction * seasonProd * foodMul;
        runtime.abstractFood -= (RegionalMarketAndThreat.Lod1FoodUpkeepBase * seasonProd +
                                 RegionalMarketAndThreat.Lod1FoodUpkeepFlat) * upkeepMul;
        if (season == VariableTimelineSeason.Escalation)
        {
            runtime.abstractFood -= RegionalMarketAndThreat.Lod1EscalationFoodDrain;
        }

        runtime.abstractTimber += 0.5f * seasonProd * timberMul;
        runtime.abstractOre += 0.25f * seasonProd * oreMul;
        runtime.abstractCrystal += 0.08f * seasonProd * crystalMul;
        runtime.abstractFood = Mathf.Max(0f, runtime.abstractFood);
        runtime.abstractTimber = Mathf.Max(0f, runtime.abstractTimber);
        runtime.abstractOre = Mathf.Max(0f, runtime.abstractOre);
        runtime.abstractCrystal = Mathf.Max(0f, runtime.abstractCrystal);

        snap.barrierEfficiency = Mathf.Clamp01(snap.barrierEfficiency - seasonDecay);
        if (season == VariableTimelineSeason.Escalation)
        {
            snap.military = Mathf.Max(0f, snap.military - 0.35f);
        }

        if (season == VariableTimelineSeason.Dormant && bias != null)
        {
            string research = NationBiasInjector.TryDormantStableAffluentRoll(
                bias,
                snap.id,
                DefaultTurn,
                season);
            if (!string.IsNullOrEmpty(research))
            {
                Debug.Log($"<color=#4DD0E1><b>{LogTag}</b></color> {research}");
            }
        }

        float economyDelta = (runtime.abstractFood - 20f) * 0.04f + runtime.abstractTimber * 0.01f;
        snap.economy = Mathf.Max(0f, snap.economy + economyDelta * seasonProd);
        snap.magic = Mathf.Max(0f, snap.magic + runtime.abstractCrystal * 0.02f * seasonProd);
        snap.power = snap.economy + snap.military + snap.magic;
        runtime.writeback = BuildWritebackFromSnapshot(snap, DefaultTurn);
        runtime.writeback.source = "WestRegionSimulationManager:LOD1";
    }

    public void CompleteYearEndWriteback()
    {
        if (!TryInitializeWestRegion() || yearEndWritebackDone)
        {
            return;
        }

        SyncNation001FromAggregator();
        for (int i = 0; i < westNations.Count; i++)
        {
            WestRegionNationRuntime runtime = westNations[i];
            if (runtime == null || !runtime.alive)
            {
                continue;
            }

            if (runtime.lod == WestRegionLodLevel.Lod0FullMicro &&
                runtime.microCell != null &&
                runtime.snapshot.id != MicroToMacroAggregator.DefaultNationId)
            {
                runtime.writeback = BuildWritebackFromMicroCell(runtime);
            }
        }

        int ok = 0;
        string geoPath = MicroHistoryTimelineTimelineEngine.ResolveGeoPath();
        MicroToMacroAggregator aggregator = MicroToMacroAggregator.EnsureInstance();
        for (int i = 0; i < westNations.Count; i++)
        {
            WestRegionNationRuntime runtime = westNations[i];
            if (runtime?.writeback == null || !runtime.alive)
            {
                continue;
            }

            if (runtime.writeback.id == MicroToMacroAggregator.DefaultNationId)
            {
                if (aggregator.YearEndApplied && aggregator.GeoWritebackFileSucceeded)
                {
                    ok++;
                }
                else
                {
                    Debug.LogWarning(
                        "[WestRegionSimulationManager] 国家001は MicroToMacroAggregator 未完了 — geo 書き戻しをスキップ。");
                }

                continue;
            }

            if (MacroChronicleJsonScan.TryWriteNationAtTurn(
                    geoPath,
                    turn,
                    runtime.writeback.id,
                    runtime.writeback,
                    out string error))
            {
                ok++;
            }
            else
            {
                Debug.LogWarning(
                    $"[WestRegionSimulationManager] geo 書き戻し Safe-Fail 国家{runtime.writeback.id:D3}: {error}");
            }
        }

        lastWritebackOkCount = ok;
        yearEndWritebackDone = true;
        Debug.Log(
            $"<color=#4DD0E1><b>{LogTag} 書き戻し完了</b></color> " +
            $"geo={geoPath} 成功={ok}/{westNations.Count} LOD0:{Lod0Count} LOD1:{Lod1Count}");
    }

    private void SyncNation001FromAggregator()
    {
        WestRegionNationRuntime runtime = FindRuntime(MicroToMacroAggregator.DefaultNationId);
        if (runtime?.snapshot == null)
        {
            return;
        }

        try
        {
            MicroToMacroAggregator agg = MicroToMacroAggregator.EnsureInstance();
            MacroGeoNationWriteback dto = MacroGeoNationWriteback.FromStats(
                agg.Stats,
                runtime.snapshot,
                MicroToMacroAggregator.DefaultNationId,
                turn);
            dto.source = "WestRegionSimulationManager:LOD0+MicroToMacro";
            runtime.writeback = dto;
            runtime.snapshot.power = dto.power;
            runtime.snapshot.economy = dto.economy;
            runtime.snapshot.military = dto.military;
            runtime.snapshot.magic = dto.magic;
            runtime.snapshot.barrierEfficiency = dto.barrier_efficiency;
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[WestRegionSimulationManager] 国家001同期 Safe-Fail: {exception.Message}");
            runtime.writeback = BuildWritebackFromSnapshot(runtime.snapshot, turn);
        }
    }

    private static MacroGeoNationWriteback BuildWritebackFromMicroCell(WestRegionNationRuntime runtime)
    {
        RegionalNationMicroCell cell = runtime.microCell;
        MacroChronicleNationSnapshot snap = runtime.snapshot;
        float barrierNorm = cell?.barrier != null
            ? Mathf.Clamp01(cell.barrier.Efficiency / 100f)
            : snap.barrierEfficiency;
        float food = cell?.market != null ? cell.market.Food : runtime.abstractFood;
        float economy = snap.economy + (food - 20f) * 0.08f;
        float military = snap.military;
        float magic = snap.magic + (cell?.market?.ManaCrystal ?? 0f) * 0.05f;
        float power = economy + military + magic;

        MacroGeoNationWriteback dto = BuildWritebackFromSnapshot(snap, DefaultTurn);
        dto.economy = economy;
        dto.military = military;
        dto.magic = magic;
        dto.power = power;
        dto.barrier_efficiency = barrierNorm;
        dto.source = "WestRegionSimulationManager:LOD0";
        return dto;
    }

    private static MacroGeoNationWriteback BuildWritebackFromSnapshot(MacroChronicleNationSnapshot snap, int turnValue)
    {
        return new MacroGeoNationWriteback
        {
            id = snap.id,
            name = snap.name,
            region = snap.region,
            lat = snap.lat,
            lng = snap.lng,
            alive = snap.alive,
            territory = snap.territory,
            power = snap.power,
            economy = snap.economy,
            military = snap.military,
            magic = snap.magic,
            barrier_efficiency = Mathf.Clamp01(snap.barrierEfficiency),
            born_turn = snap.bornTurn,
            died_turn = snap.diedTurn,
            turn = turnValue,
            source = "WestRegionSimulationManager"
        };
    }

    public WestRegionNationRuntime FindRuntime(int nationId)
    {
        if (westNations == null)
        {
            return null;
        }

        for (int i = 0; i < westNations.Count; i++)
        {
            WestRegionNationRuntime runtime = westNations[i];
            if (runtime?.snapshot != null && runtime.snapshot.id == nationId)
            {
                return runtime;
            }
        }

        return null;
    }

    public static NationBiasData TryGetNationBias(int nationId)
    {
        try
        {
            WestRegionSimulationManager mgr = Instance ?? EnsureInstance();
            if (mgr == null)
            {
                return null;
            }

            mgr.TryInitializeWestRegion();
            WestRegionNationRuntime runtime = mgr.FindRuntime(nationId);
            return runtime?.bias;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static VillageStorageMarket ResolvePrimaryMarket()
    {
        try
        {
            return NpcCivilizationEngine.EnsureInstance().Storage;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public void ResetWestRegionYear()
    {
        westNations.Clear();
        regionalNetwork?.ResetNetwork();
        lastTickedPhase = -1;
        yearEndWritebackDone = false;
        lastWritebackOkCount = 0;
        TryInitializeWestRegion();
    }

    /// <summary>Regional Expansion 検証など、35 国排他を一時解除します。</summary>
    public void SetWestRegionActiveForVerification(bool active)
    {
        westRegionActive = active;
        if (active)
        {
            return;
        }

        westNations?.Clear();
        regionalNetwork?.ResetNetwork();
        lastTickedPhase = -1;
        yearEndWritebackDone = false;
        lastWritebackOkCount = 0;
    }

    public static WestRegionVerifyResult RunDayVerification()
    {
        WestRegionVerifyResult result = new WestRegionVerifyResult();
        try
        {
            WestRegionSimulationManager mgr = EnsureInstance();
            mgr.ResetWestRegionYear();
            SimulationVerifyBootstrap.PrepareFreshStoryDay();
            NpcCivilizationEngine civ = NpcCivilizationEngine.EnsureInstance();
            VariablePhaseDistribution dist = ProceduralMapPopulator.DefaultNation001Turn1Distribution();
            MicroToMacroAggregator.EnsureInstance().BeginYearBaseline();

            for (int phase = 1; phase <= ProceduralMapPopulator.PhaseCount; phase++)
            {
                VariableTimelineSeason season = ProceduralMapPopulator.ResolveSeason(phase, dist);
                civ.TickPhase(phase, season);
            }

            if (!mgr.yearEndWritebackDone)
            {
                mgr.CompleteYearEndWriteback();
            }

            result.totalNations = mgr.westNations?.Count ?? 0;
            result.lod0Count = mgr.Lod0Count;
            result.lod1Count = mgr.Lod1Count;
            result.regionalTrades = mgr.regionalNetwork?.regionalTrades?.Count ?? 0;
            result.writebackOk = mgr.LastWritebackOkCount;
            int expectedLod1 = result.totalNations - result.lod0Count;
            result.success = result.totalNations == WestRegionNationCountCap &&
                            result.lod0Count == ExpectedLod0Count &&
                            result.lod1Count == expectedLod1 &&
                            result.regionalTrades > 0 &&
                            result.writebackOk >= result.totalNations &&
                            mgr.lastTickedPhase >= ProceduralMapPopulator.PhaseCount;
            result.message =
                $"nations={result.totalNations} LOD0={result.lod0Count} LOD1={result.lod1Count} " +
                $"trades={result.regionalTrades} writeback={result.writebackOk}/{result.totalNations} " +
                $"phase={mgr.lastTickedPhase}";
        }
        catch (Exception exception)
        {
            result.success = false;
            result.message = $"Safe-Fail: {exception.Message}";
        }

        return result;
    }
}

/// <summary>LOD0 ミクロセルの生成・位相ティック（国家001以外）。</summary>
public static class WestRegionMicroCellOps
{
    public const float DamageResourcePenalty = 0.8f;

    public static RegionalNationMicroCell BootstrapMicroCell(
        MacroChronicleNationSnapshot snap,
        NationBiasData bias = null)
    {
        RegionalNationMicroCell cell = new RegionalNationMicroCell
        {
            nationId = snap.id,
            nationName = snap.name,
            region = snap.region,
            lat = snap.lat,
            lng = snap.lng,
            territory = snap.territory,
            macroPower = snap.power,
            market = BuildMarket(snap, bias),
            barrier = new VillageBarrierCore
            {
                Efficiency = Mathf.Clamp(snap.barrierEfficiency * 100f, 0f, 100f),
                BarrierDropped = snap.barrierEfficiency <= 0.01f
            },
            nature = BuildNature(snap),
            phaseStartBarrierPercent = snap.barrierEfficiency * 100f,
            bias = bias,
            initialized = true
        };
        ResolvePlacement(cell);
        cell.workSpots = BuildWorkSpots(cell);
        EnsureSceneRoot(cell);
        return cell;
    }

    public static void TickMicroCell(RegionalNationMicroCell cell, VariableTimelineSeason season)
    {
        if (cell?.barrier == null || cell.market == null)
        {
            return;
        }

        cell.phaseStartBarrierPercent = cell.barrier.Efficiency;
        cell.barrier.ApplySeasonPressure(season);
        ApplyUpkeep(cell, season);
        Produce(cell, season);
        MaintainBarrier(cell);
        ApplyNature(cell.nature, season);
    }

    private static VillageStorageMarket BuildMarket(MacroChronicleNationSnapshot snap, NationBiasData bias)
    {
        float penalty = snap.id == 64 ? DamageResourcePenalty : 0.92f;
        float foodMul = bias != null ? bias.foodYieldMul : 1f;
        float oreMul = bias != null ? bias.oreYieldMul : 1f;
        float crystalMul = bias != null ? bias.crystalYieldMul : 1f;
        return new VillageStorageMarket
        {
            Food = 48f * penalty * foodMul,
            Timber = 36f * penalty,
            Ore = 16f * penalty * oreMul,
            ManaCrystal = 12f * penalty * crystalMul
        };
    }

    private static NatureEnvironmentStatus BuildNature(MacroChronicleNationSnapshot snap)
    {
        NatureEnvironmentStatus nature = new NatureEnvironmentStatus
        {
            ForestDensity = 68f,
            WildlifePopulation = 92,
            ManaEcologyLevel = Mathf.Clamp(snap.magic / 10f, 18f, 72f)
        };
        nature.ClampAll();
        return nature;
    }

    private static void ResolvePlacement(RegionalNationMicroCell cell)
    {
        float refLat = ProceduralTerrainGenerator.ReferenceLatitude;
        float refLng = ProceduralTerrainGenerator.ReferenceLongitude;
        Vector2 village = ProceduralTerrainGenerator.VillageNormalized;
        cell.normalizedX = Mathf.Clamp01(village.x + (cell.lng - refLng) * 0.045f);
        cell.normalizedZ = Mathf.Clamp01(village.y + (cell.lat - refLat) * 0.055f);
        cell.sampledElevationNorm = ProceduralTerrainGenerator.SampleNormalizedHeight(
            cell.normalizedX,
            cell.normalizedZ);
    }

    private static List<WorkSpotData> BuildWorkSpots(RegionalNationMicroCell cell)
    {
        Terrain terrain = FindTerrain();
        List<WorkSpotData> spots = new List<WorkSpotData>(5);
        Vector2 center = new Vector2(cell.normalizedX, cell.normalizedZ);
        int id = cell.nationId;
        spots.Add(CreateSpot(terrain, $"SPOT_{id:D3}_BARRIER_CORE", WorkSpotType.BarrierCore,
            center + new Vector2(0.01f, 0.01f), nameof(NpcCivicJob.BarrierKeeper)));
        spots.Add(CreateSpot(terrain, $"SPOT_{id:D3}_WOODCUTTER_CAMP", WorkSpotType.WoodcutterCamp,
            center + new Vector2(-0.02f, -0.02f), nameof(NpcCivicJob.Woodcutter)));
        spots.Add(CreateSpot(terrain, $"SPOT_{id:D3}_FARMLAND", WorkSpotType.Farmland,
            center + new Vector2(0.03f, -0.01f), nameof(NpcCivicJob.Farmer)));
        spots.Add(CreateSpot(terrain, $"SPOT_{id:D3}_MINING_SHAFT", WorkSpotType.MiningShaft,
            center + new Vector2(-0.01f, 0.03f), WorkSpotData.DefaultJobForType(WorkSpotType.MiningShaft)));
        spots.Add(CreateSpot(terrain, $"SPOT_{id:D3}_WORKSHOP", WorkSpotType.Workshop,
            center + new Vector2(0.02f, 0.02f), nameof(NpcCivicJob.Blacksmith)));
        return spots;
    }

    private static WorkSpotData CreateSpot(
        Terrain terrain,
        string spotId,
        WorkSpotType type,
        Vector2 normalized,
        string jobId)
    {
        Vector3 world = ProceduralTerrainGenerator.NormalizedToWorld(terrain, normalized.x, normalized.y);
        return new WorkSpotData
        {
            SpotId = spotId,
            Type = type,
            WorldPosition = world,
            TargetJobId = jobId,
            ResourceYield = WorkSpotResourceYield.ForType(type),
            Durability = 78f,
            Integrity = 72f
        };
    }

    private static Terrain FindTerrain()
    {
        GameObject root = GameObject.Find(Nation001ProceduralMapBuilder.RootName);
        return root != null ? root.GetComponentInChildren<Terrain>() : null;
    }

    private static void EnsureSceneRoot(RegionalNationMicroCell cell)
    {
        if (cell.sceneRoot == null)
        {
            cell.sceneRoot = new GameObject($"Nation{cell.nationId:D3}_RegionalCell");
            GameObject hub = GameObject.Find("DebugSystemsHub");
            if (hub != null)
            {
                cell.sceneRoot.transform.SetParent(hub.transform, false);
            }
        }

        if (cell.spotManager == null)
        {
            cell.spotManager = cell.sceneRoot.GetComponent<VillageWorkSpotManager>();
            if (cell.spotManager == null)
            {
                cell.spotManager = cell.sceneRoot.AddComponent<VillageWorkSpotManager>();
            }
        }

        cell.spotManager.ClearAndRegister(cell.workSpots);
    }

    private static void ApplyUpkeep(RegionalNationMicroCell cell, VariableTimelineSeason season)
    {
        NationBiasData bias = cell.bias;
        float upkeepMul = bias != null ? bias.facilityUpkeepMul : 1f;
        float repairMul = bias != null ? bias.repairBarrierPriorityMul : 1f;
        float pressure = season == VariableTimelineSeason.Active ? 1.15f :
            season == VariableTimelineSeason.Escalation ? 1.35f :
            season == VariableTimelineSeason.Deescalation ? 0.85f : 0.55f;
        cell.market.WithdrawFood(pressure * 1.4f * upkeepMul);
        cell.market.WithdrawTimber(pressure * 0.35f * upkeepMul);
        cell.market.WithdrawOre(pressure * 0.2f * upkeepMul);

        if (repairMul > 1.01f && season != VariableTimelineSeason.Dormant)
        {
            float crystalSpend = 0.15f * repairMul;
            cell.market.WithdrawCrystal(crystalSpend);
            cell.barrier.RestoreFromKeeper(crystalSpend, 1.1f * repairMul, cell.market.ManaCrystal <= 0.05f);
        }
    }

    private static void Produce(RegionalNationMicroCell cell, VariableTimelineSeason season)
    {
        NationBiasData bias = cell.bias;
        float foodMul = bias != null ? bias.foodYieldMul : 1f;
        float oreMul = bias != null ? bias.oreYieldMul : 1f;
        float crystalMul = bias != null ? bias.crystalYieldMul : 1f;
        float timberMul = bias != null ? bias.timberYieldMul : 1f;
        float seasonMul = season == VariableTimelineSeason.Active ? 1f :
            season == VariableTimelineSeason.Escalation ? 0.82f :
            season == VariableTimelineSeason.Deescalation ? 0.65f : 0.45f;
        float harvestMul = cell.nature.ResolveHarvestEfficiency();
        for (int i = 0; i < cell.workSpots.Count; i++)
        {
            WorkSpotData spot = cell.workSpots[i];
            if (spot == null)
            {
                continue;
            }

            float eff = spot.YieldEfficiency * seasonMul * harvestMul;
            WorkSpotResourceYield y = spot.ResourceYield;
            cell.market.Deposit(
                Mathf.Max(0f, y.food * eff * foodMul),
                Mathf.Max(0f, y.timber * eff * timberMul),
                Mathf.Max(0f, y.ore * eff * oreMul),
                Mathf.Max(0f, y.manaCrystal * eff * crystalMul));
        }
    }

    private static void MaintainBarrier(RegionalNationMicroCell cell)
    {
        float crystals = cell.market.WithdrawCrystal(0.35f);
        if (crystals > 0.01f)
        {
            cell.barrier.RestoreFromKeeper(crystals, 1.15f, cell.market.ManaCrystal <= 0.05f);
        }
        else
        {
            cell.barrier.RestoreFromKeeper(0f, 1f, true);
        }
    }

    private static void ApplyNature(NatureEnvironmentStatus nature, VariableTimelineSeason season)
    {
        float forestDelta = season == VariableTimelineSeason.Active ? -0.08f :
            season == VariableTimelineSeason.Escalation ? -0.14f :
            season == VariableTimelineSeason.Deescalation ? 0.05f : 0.12f;
        float manaDelta = season == VariableTimelineSeason.Active ? 2.4f :
            season == VariableTimelineSeason.Escalation ? 3.6f :
            season == VariableTimelineSeason.Deescalation ? -0.8f : -1.6f;
        nature.ApplyForestDelta(forestDelta);
        nature.ApplyManaDelta(manaDelta);
        nature.ClampAll();
    }
}

public static class WestRegionSimulationManagerBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoAttach()
    {
        WestRegionSimulationManager.EnsureInstance();
    }
}

#if UNITY_EDITOR
public static class WestRegionSimulationManagerMenu
{
    private const string VerifyLogPath = "Logs/west_region_verify.txt";

    [MenuItem("Tools/Procedural Map/Run West Region Day")]
    public static void RunWestRegionDay()
    {
        WestRegionVerifyResult result = WestRegionSimulationManager.RunDayVerification();
        WriteVerifyLog(result);
        LogVerifyResult(result);
        EditorUtility.DisplayDialog("West Region", result.message, "OK");
    }

    /// <summary>Unity バッチ: -executeMethod WestRegionSimulationManagerMenu.BatchVerifyAndQuit</summary>
    public static void BatchVerifyAndQuit()
    {
        WestRegionVerifyResult result = WestRegionSimulationManager.RunDayVerification();
        WriteVerifyLog(result);
        LogVerifyResult(result);
        EditorApplication.Exit(0);
    }

    private static void WriteVerifyLog(WestRegionVerifyResult result)
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
            Debug.LogWarning($"[WestRegionSimulationManager] 検証ログをスキップ: {exception.Message}");
        }
    }

    private static void LogVerifyResult(WestRegionVerifyResult result)
    {
        Debug.Log(
            result.success
                ? $"<color=#4DD0E1><b>{WestRegionSimulationManager.LogTag}・検証】PASS</b></color> {result.message}"
                : $"<color=#FF8A80><b>{WestRegionSimulationManager.LogTag}・検証】FAIL</b></color> {result.message}");
    }
}
#endif

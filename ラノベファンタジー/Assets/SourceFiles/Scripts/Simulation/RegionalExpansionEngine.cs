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
// 広域セル展開 — 隣接国家のミクロセル並行駆動・2国間物流・境界摩擦
// 連携: NpcCivilizationEngine / NaturalEcologyEngine / macro_chronicle_geo.json
// =============================================================================

/// <summary>国境貿易 1 件のログ。</summary>
[Serializable]
public sealed class RegionalTradeLogEntry
{
    public int fromNationId;
    public int toNationId;
    public int phase;
    public string resource;
    public float amount;
    public string note;

    public string FormatLine()
    {
        return $"位相{phase} 国家{fromNationId:D3}→{toNationId:D3} {resource} {amount.ToString("F1", CultureInfo.InvariantCulture)} ({note})";
    }
}

/// <summary>隣接国家 1 件のミクロ自律セル（倉庫・結界・自然・作業スポット）。</summary>
[Serializable]
public sealed class RegionalNationMicroCell
{
    public int nationId;
    public string nationName = string.Empty;
    public string region = string.Empty;
    public float lat;
    public float lng;
    public float territory;
    public float macroPower;
    public float normalizedX;
    public float normalizedZ;
    public float sampledElevationNorm;

    public VillageStorageMarket market = new VillageStorageMarket();
    public VillageBarrierCore barrier = new VillageBarrierCore();
    public NatureEnvironmentStatus nature = new NatureEnvironmentStatus();
    public List<WorkSpotData> workSpots = new List<WorkSpotData>();

    public float phaseStartBarrierPercent = 87f;
    public int lastTickedPhase = -1;
    public bool initialized;

    public GameObject sceneRoot;
    public VillageWorkSpotManager spotManager;
    public NationBiasData bias;
}

/// <summary>
/// 国家001に隣接する国家064など、広域ミクロセルを展開し2国間相互作用を処理します。
/// </summary>
[DefaultExecutionOrder(55)]
public class RegionalExpansionEngine : MonoBehaviour
{
    public const int DefaultPrimaryNationId = 1;
    public const int DefaultAdjacentNationId = 64;
    public const int DefaultTurn = 1;

    public const float PlacementMinDistDeg = 1.2f;
    public const float AdjacentDistanceDeg = 1.272f;
    public const float DamageResourcePenalty = 0.8f;
    public const float TradeFoodReserve = 20f;
    public const float TradeTimberReserve = 15f;
    public const float TradeOreReserve = 8f;
    public const float BorderManaPerBarrierDrop = 3.2f;
    public const float BorderThreatPerBarrierDrop = 2.5f;

    public static RegionalExpansionEngine Instance { get; private set; }

    [SerializeField] private int primaryNationId = DefaultPrimaryNationId;
    [SerializeField] private int adjacentNationId = DefaultAdjacentNationId;
    [SerializeField] private int turn = DefaultTurn;
    [SerializeField] private bool enableRegionalCells = true;
    [SerializeField] private RegionalNationMicroCell adjacentCell = new RegionalNationMicroCell();
    [SerializeField] private List<RegionalTradeLogEntry> tradeLog = new List<RegionalTradeLogEntry>();
    [SerializeField] private float nation001SouthwestThreatBonus = 0f;
    [SerializeField] private int lastRegionalPhase = -1;

    public RegionalNationMicroCell AdjacentCell => adjacentCell;
    public IReadOnlyList<RegionalTradeLogEntry> TradeLog => tradeLog;
    public float Nation001SouthwestThreatBonus => nation001SouthwestThreatBonus;

    public static RegionalExpansionEngine EnsureInstance()
    {
        if (Instance != null)
        {
            return Instance;
        }

        RegionalExpansionEngine existing = FindAnyObjectByType<RegionalExpansionEngine>();
        if (existing != null)
        {
            Instance = existing;
            return Instance;
        }

        GameObject hub = GameObject.Find("DebugSystemsHub");
        GameObject host = hub != null ? hub : new GameObject(nameof(RegionalExpansionEngine));
        RegionalExpansionEngine engine = host.GetComponent<RegionalExpansionEngine>();
        return engine != null ? engine : host.AddComponent<RegionalExpansionEngine>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        TryBootstrapAdjacentCell();
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    /// <summary>NpcCivilizationEngine 位相終了後に呼び出します（Safe-Fail）。</summary>
    public static void TryTickRegionalPhase(int phase, VariableTimelineSeason season)
    {
        try
        {
            if (WestRegionSimulationManager.Instance != null &&
                WestRegionSimulationManager.Instance.IsWestRegionActive)
            {
                return;
            }

            RegionalExpansionEngine engine = EnsureInstance();
            if (!engine.enableRegionalCells)
            {
                return;
            }

            engine.TickRegionalPhase(phase, season);
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[RegionalExpansionEngine] TryTickRegionalPhase Safe-Fail: {exception.Message}");
        }
    }

    /// <summary>NaturalEcologyEngine から境界摩擦の環境魔力加算を適用します。</summary>
    public static void TryApplyBorderEcologyPressure(NatureEnvironmentStatus nature, StringBuilder log)
    {
        try
        {
            RegionalExpansionEngine engine = Instance;
            if (engine == null || nature == null || engine.nation001SouthwestThreatBonus <= 0.01f)
            {
                return;
            }

            float manaGain = engine.nation001SouthwestThreatBonus * 0.85f;
            float applied = nature.ApplyManaDelta(manaGain);
            if (Mathf.Abs(applied) > 0.01f && log != null)
            {
                log.AppendLine();
                log.Append(
                    $"  <color=#FFAB91><b>【広域境界摩擦】</b></color> 南西外周 環境魔力+{applied:F1} " +
                    $"(国家{engine.adjacentNationId:D3}結界低下 Threat+{engine.nation001SouthwestThreatBonus:F1})");
            }
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[RegionalExpansionEngine] BorderEcology Safe-Fail: {exception.Message}");
        }
    }

    public void TickRegionalPhase(int phase, VariableTimelineSeason season)
    {
        if (!enableRegionalCells)
        {
            return;
        }

        int normalized = ProceduralMapPopulator.NormalizePhase(phase);
        if (!TryBootstrapAdjacentCell())
        {
            return;
        }

        adjacentCell.phaseStartBarrierPercent = adjacentCell.barrier != null
            ? adjacentCell.barrier.Efficiency
            : adjacentCell.phaseStartBarrierPercent;

        TickAdjacentAutonomy(normalized, season);
        ProcessBiNationalTrade(normalized);
        ProcessBorderFriction(normalized);

        adjacentCell.lastTickedPhase = normalized;
        lastRegionalPhase = normalized;
        DecaySouthwestThreat();
    }

    private bool TryBootstrapAdjacentCell()
    {
        if (adjacentCell != null && adjacentCell.initialized)
        {
            return true;
        }

        try
        {
            MacroChronicleNationSnapshot snap = LoadNationSnapshot(adjacentNationId, turn);
            if (snap == null)
            {
                Debug.LogWarning(
                    $"[RegionalExpansionEngine] 国家{adjacentNationId:D3} geo 未検出。国家001単体へ Safe-Fail フォールバック。");
                return false;
            }

            adjacentCell = adjacentCell ?? new RegionalNationMicroCell();
            adjacentCell.nationId = snap.id;
            adjacentCell.nationName = snap.name;
            adjacentCell.region = snap.region;
            adjacentCell.lat = snap.lat;
            adjacentCell.lng = snap.lng;
            adjacentCell.territory = snap.territory;
            adjacentCell.macroPower = snap.power;
            adjacentCell.market = BuildDamagedMarket();
            adjacentCell.barrier = new VillageBarrierCore
            {
                Efficiency = Mathf.Clamp(snap.barrierEfficiency * 100f, 0f, 100f),
                BarrierDropped = snap.barrierEfficiency <= 0.01f
            };
            adjacentCell.nature = BuildRegionalNature(snap);
            ResolveNormalizedPlacement(adjacentCell);
            adjacentCell.workSpots = BuildWorkSpots(adjacentCell);
            EnsureSceneCell(adjacentCell);
            adjacentCell.phaseStartBarrierPercent = adjacentCell.barrier.Efficiency;
            adjacentCell.initialized = true;

            Debug.Log(
                "<color=#80DEEA><b>【広域シミュレーション】</b></color> " +
                $"国家{adjacentCell.nationId:D3} セル展開 " +
                $"({adjacentCell.region} lat={adjacentCell.lat:F2} lng={adjacentCell.lng:F2} " +
                $"標高norm={adjacentCell.sampledElevationNorm:F3}) " +
                $"Power={adjacentCell.macroPower:F1} 生存圏={adjacentCell.territory:F1} " +
                $"結界={adjacentCell.barrier.Efficiency:F1}% 倉庫 {adjacentCell.market.FormatSnapshot()}");
            return true;
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[RegionalExpansionEngine] Bootstrap Safe-Fail: {exception.Message}");
            return false;
        }
    }

    private static MacroChronicleNationSnapshot LoadNationSnapshot(int nationId, int turnValue)
    {
        try
        {
            string geoPath = MicroHistoryTimelineTimelineEngine.ResolveGeoPath();
            return MacroChronicleJsonScan.ReadNationAtTurn(geoPath, turnValue, nationId);
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[RegionalExpansionEngine] geo 読込 Safe-Fail: {exception.Message}");
            return null;
        }
    }

    private static VillageStorageMarket BuildDamagedMarket()
    {
        VillageStorageMarket market = new VillageStorageMarket
        {
            Food = 48f * DamageResourcePenalty,
            Timber = 36f * DamageResourcePenalty,
            Ore = 16f * DamageResourcePenalty,
            ManaCrystal = 12f * DamageResourcePenalty
        };
        return market;
    }

    private static NatureEnvironmentStatus BuildRegionalNature(MacroChronicleNationSnapshot snap)
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

    private static void ResolveNormalizedPlacement(RegionalNationMicroCell cell)
    {
        float refLat = ProceduralTerrainGenerator.ReferenceLatitude;
        float refLng = ProceduralTerrainGenerator.ReferenceLongitude;
        float dLat = cell.lat - refLat;
        float dLng = cell.lng - refLng;

        Vector2 village = ProceduralTerrainGenerator.VillageNormalized;
        float lngScale = 0.045f;
        float latScale = 0.055f;
        cell.normalizedX = Mathf.Clamp01(village.x + dLng * lngScale);
        cell.normalizedZ = Mathf.Clamp01(village.y + dLat * latScale);
        cell.sampledElevationNorm = ProceduralTerrainGenerator.SampleNormalizedHeight(cell.normalizedX, cell.normalizedZ);
    }

    private static List<WorkSpotData> BuildWorkSpots(RegionalNationMicroCell cell)
    {
        Terrain terrain = FindNation001Terrain();
        List<WorkSpotData> spots = new List<WorkSpotData>(8);
        Vector2 center = new Vector2(cell.normalizedX, cell.normalizedZ);

        spots.Add(CreateSpot(
            terrain, $"SPOT_{cell.nationId:D3}_BARRIER_CORE", WorkSpotType.BarrierCore,
            center + new Vector2(0.01f, 0.01f), nameof(NpcCivicJob.BarrierKeeper)));
        spots.Add(CreateSpot(
            terrain, $"SPOT_{cell.nationId:D3}_WOODCUTTER_CAMP", WorkSpotType.WoodcutterCamp,
            center + new Vector2(-0.02f, -0.02f), nameof(NpcCivicJob.Woodcutter)));
        spots.Add(CreateSpot(
            terrain, $"SPOT_{cell.nationId:D3}_FARMLAND", WorkSpotType.Farmland,
            center + new Vector2(0.03f, -0.01f), nameof(NpcCivicJob.Farmer)));
        spots.Add(CreateSpot(
            terrain, $"SPOT_{cell.nationId:D3}_MINING_SHAFT", WorkSpotType.MiningShaft,
            center + new Vector2(-0.01f, 0.03f), WorkSpotData.DefaultJobForType(WorkSpotType.MiningShaft)));
        spots.Add(CreateSpot(
            terrain, $"SPOT_{cell.nationId:D3}_WORKSHOP", WorkSpotType.Workshop,
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
        WorkSpotData spot = new WorkSpotData
        {
            SpotId = spotId,
            Type = type,
            WorldPosition = world,
            TargetJobId = jobId,
            ResourceYield = WorkSpotResourceYield.ForType(type),
            Durability = 78f,
            Integrity = 72f
        };
        return spot;
    }

    private static Terrain FindNation001Terrain()
    {
        GameObject root = GameObject.Find(Nation001ProceduralMapBuilder.RootName);
        if (root == null)
        {
            return null;
        }

        return root.GetComponentInChildren<Terrain>();
    }

    private void EnsureSceneCell(RegionalNationMicroCell cell)
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

    private void TickAdjacentAutonomy(int normalized, VariableTimelineSeason season)
    {
        if (adjacentCell?.barrier == null || adjacentCell.market == null || adjacentCell.nature == null)
        {
            return;
        }

        adjacentCell.barrier.ApplySeasonPressure(season);
        ApplyRegionalUpkeep(adjacentCell, season);
        ProduceFromWorkSpots(adjacentCell, season);
        MaintainBarrier(adjacentCell);
        ApplyRegionalNature(adjacentCell.nature, season);

        Debug.Log(
            "<color=#80DEEA><b>【広域シミュレーション】</b></color> " +
            $"国家{adjacentCell.nationId:D3} 位相{normalized}/24 {season.ToString().ToUpperInvariant()} " +
            $"結界 {adjacentCell.barrier.Efficiency:F1}% 倉庫 {adjacentCell.market.FormatSnapshot()} " +
            $"自然 {adjacentCell.nature.FormatSnapshot()}");
    }

    private static void ApplyRegionalUpkeep(RegionalNationMicroCell cell, VariableTimelineSeason season)
    {
        float pressure = season == VariableTimelineSeason.Active ? 1.15f :
            season == VariableTimelineSeason.Escalation ? 1.35f :
            season == VariableTimelineSeason.Deescalation ? 0.85f : 0.55f;
        cell.market.WithdrawFood(pressure * 1.4f);
        cell.market.WithdrawTimber(pressure * 0.35f);
        cell.market.WithdrawOre(pressure * 0.2f);
    }

    private static void ProduceFromWorkSpots(RegionalNationMicroCell cell, VariableTimelineSeason season)
    {
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
                Mathf.Max(0f, y.food * eff),
                Mathf.Max(0f, y.timber * eff),
                Mathf.Max(0f, y.ore * eff),
                Mathf.Max(0f, y.manaCrystal * eff));
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

    private static void ApplyRegionalNature(NatureEnvironmentStatus nature, VariableTimelineSeason season)
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

    private void ProcessBiNationalTrade(int normalized)
    {
        VillageStorageMarket donor = ResolvePrimaryMarket();
        VillageStorageMarket receiver = adjacentCell?.market;
        if (donor == null || receiver == null)
        {
            return;
        }

        TryTradeResource(normalized, donor, receiver, "Food", donor.Food, receiver.Food, TradeFoodReserve, 8f);
        TryTradeResource(normalized, donor, receiver, "Timber", donor.Timber, receiver.Timber, TradeTimberReserve, 6f);
        TryTradeResource(normalized, donor, receiver, "Ore", donor.Ore, receiver.Ore, TradeOreReserve, 4f);
    }

    private void TryTradeResource(
        int normalized,
        VillageStorageMarket donor,
        VillageStorageMarket receiver,
        string label,
        float donorStock,
        float receiverStock,
        float donorReserve,
        float receiverNeedThreshold)
    {
        float surplus = donorStock - donorReserve;
        bool receiverLow = label == "Food"
            ? receiver.FoodDepleted || receiverStock < receiverNeedThreshold
            : receiverStock < receiverNeedThreshold;
        if (surplus <= 1f || !receiverLow)
        {
            return;
        }

        float need = Mathf.Max(0f, receiverNeedThreshold - receiverStock);
        float amount = Mathf.Min(surplus * 0.35f, Mathf.Max(need, 2f));
        if (amount <= 0.05f)
        {
            return;
        }

        switch (label)
        {
            case "Food":
                donor.WithdrawFood(amount);
                receiver.Deposit(amount, 0f, 0f, 0f);
                break;
            case "Timber":
                donor.WithdrawTimber(amount);
                receiver.Deposit(0f, amount, 0f, 0f);
                break;
            case "Ore":
                donor.WithdrawOre(amount);
                receiver.Deposit(0f, 0f, amount, 0f);
                break;
        }

        RegionalTradeLogEntry entry = new RegionalTradeLogEntry
        {
            fromNationId = primaryNationId,
            toNationId = adjacentNationId,
            phase = normalized,
            resource = label,
            amount = amount,
            note = "国境物流"
        };
        tradeLog.Add(entry);

        Debug.Log(
            "<color=#80DEEA><b>【広域シミュレーション】</b></color> TradeLog " + entry.FormatLine());
    }

    private VillageStorageMarket ResolvePrimaryMarket()
    {
        try
        {
            NpcCivilizationEngine civ = NpcCivilizationEngine.Instance;
            if (civ != null)
            {
                return civ.Storage;
            }

            return NpcCivilizationEngine.EnsureInstance().Storage;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private void ProcessBorderFriction(int normalized)
    {
        if (adjacentCell?.barrier == null)
        {
            return;
        }

        float before = adjacentCell.phaseStartBarrierPercent;
        float after = adjacentCell.barrier.Efficiency;
        bool edgeDrop = after <= VillageBarrierCore.BreachThresholdPercent &&
                        before > VillageBarrierCore.BreachThresholdPercent + 0.5f;
        bool severe = after <= 0.01f || adjacentCell.barrier.BarrierDropped;
        if (!edgeDrop && !severe && after >= before - 2f)
        {
            return;
        }

        float severity = Mathf.Clamp01((before - after) / 100f + (severe ? 0.35f : 0.1f));
        float manaPulse = BorderManaPerBarrierDrop * severity;
        float threatPulse = BorderThreatPerBarrierDrop * severity;
        nation001SouthwestThreatBonus = Mathf.Max(nation001SouthwestThreatBonus, threatPulse);
        ApplySouthwestSpotPressure(threatPulse);

        try
        {
            NatureEnvironmentStatus ecology = NaturalEcologyEngine.EnsureInstance().Environment;
            if (ecology != null)
            {
                ecology.ApplyManaDelta(manaPulse);
            }
        }
        catch (Exception)
        {
            // Safe-Fail
        }

        Debug.LogWarning(
            "<color=#FFAB91><b>【広域シミュレーション】</b></color> 境界摩擦 " +
            $"国家{adjacentNationId:D3}結界 {before:F1}%→{after:F1}% " +
            $"国家{primaryNationId:D3}南西外周 環境魔力+{manaPulse:F1} ThreatLevel+{threatPulse:F1} " +
            $"(WoodcutterCamp付近)");
    }

    private void ApplySouthwestSpotPressure(float threatPulse)
    {
        try
        {
            VillageWorkSpotManager manager = VillageWorkSpotManager.EnsureInstance();
            if (manager == null || manager.Spots == null)
            {
                return;
            }

            for (int i = 0; i < manager.Spots.Count; i++)
            {
                WorkSpotData spot = manager.Spots[i];
                if (spot == null || string.IsNullOrEmpty(spot.SpotId))
                {
                    continue;
                }

                bool southwest =
                    spot.SpotId.Contains("WOODCUTTER", StringComparison.OrdinalIgnoreCase) ||
                    spot.SpotId.Contains("WOODCUTTER_CAMP", StringComparison.OrdinalIgnoreCase) ||
                    (spot.Type == WorkSpotType.WoodcutterCamp && IsSouthwestSpot(spot));
                if (!southwest)
                {
                    continue;
                }

                spot.Integrity = Mathf.Clamp(spot.Integrity - threatPulse * 1.5f, 35f, 100f);
                BotanicalMutationStatus botany = spot.ResolveBotany();
                if (botany != null)
                {
                    if (threatPulse >= 2f)
                    {
                        botany.IsManaMutated = true;
                    }

                    botany.TimberQualityMultiplier = Mathf.Clamp(
                        botany.TimberQualityMultiplier + threatPulse * 0.04f,
                        1f,
                        BotanicalMutationStatus.QualityBonusMax);
                }
            }
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[RegionalExpansionEngine] 南西スポット圧力 Safe-Fail: {exception.Message}");
        }
    }

    private static bool IsSouthwestSpot(WorkSpotData spot)
    {
        Vector2 village = ProceduralTerrainGenerator.VillageNormalized;
        Vector2 spotN = WorldToNormalized(spot.WorldPosition);
        return spotN.x <= village.x + 0.02f && spotN.y <= village.y + 0.02f;
    }

    private static Vector2 WorldToNormalized(Vector3 world)
    {
        float nx = world.x / ProceduralTerrainGenerator.TerrainWidthMeters;
        float nz = world.z / ProceduralTerrainGenerator.TerrainLengthMeters;
        return new Vector2(Mathf.Clamp01(nx), Mathf.Clamp01(nz));
    }

    private void DecaySouthwestThreat()
    {
        if (nation001SouthwestThreatBonus > 0.01f)
        {
            nation001SouthwestThreatBonus = Mathf.Max(0f, nation001SouthwestThreatBonus - 0.35f);
        }
    }

    public void ResetRegionalYear()
    {
        tradeLog.Clear();
        nation001SouthwestThreatBonus = 0f;
        lastRegionalPhase = -1;
        if (adjacentCell != null)
        {
            adjacentCell.initialized = false;
            adjacentCell.lastTickedPhase = -1;
        }
    }

    public static RegionalExpansionVerifyResult RunDayVerification()
    {
        RegionalExpansionVerifyResult result = new RegionalExpansionVerifyResult();
        try
        {
            RegionalExpansionEngine engine = EnsureInstance();
            engine.ResetRegionalYear();
            SimulationVerifyBootstrap.PrepareFreshStoryDay();
            SimulationVerifyBootstrap.DeactivateWestRegionForRegionalVerification();
            engine.TryBootstrapAdjacentCell();

            NpcCivilizationEngine civ = NpcCivilizationEngine.EnsureInstance();
            VariablePhaseDistribution dist = ProceduralMapPopulator.DefaultNation001Turn1Distribution();
            MicroToMacroAggregator.EnsureInstance().BeginYearBaseline();

            for (int phase = 1; phase <= ProceduralMapPopulator.PhaseCount; phase++)
            {
                VariableTimelineSeason season = ProceduralMapPopulator.ResolveSeason(phase, dist);
                civ.TickPhase(phase, season);
            }

            result.adjacentInitialized = engine.adjacentCell != null && engine.adjacentCell.initialized;
            result.tradeCount = engine.tradeLog.Count;
            result.lastPhase = engine.lastRegionalPhase;
            result.adjacentBarrier = engine.adjacentCell?.barrier?.Efficiency ?? 0f;
            result.southwestThreat = engine.nation001SouthwestThreatBonus;
            result.success = result.adjacentInitialized && result.lastPhase >= ProceduralMapPopulator.PhaseCount;
            result.message =
                $"init={result.adjacentInitialized} phase={result.lastPhase} trades={result.tradeCount} " +
                $"064Barrier={result.adjacentBarrier:F1}% threatBonus={result.southwestThreat:F1}";
        }
        catch (Exception exception)
        {
            result.success = false;
            result.message = $"Safe-Fail: {exception.Message}";
        }

        return result;
    }
}

[Serializable]
public sealed class RegionalExpansionVerifyResult
{
    public bool success;
    public string message = string.Empty;
    public bool adjacentInitialized;
    public int tradeCount;
    public int lastPhase;
    public float adjacentBarrier;
    public float southwestThreat;
}

public static class RegionalExpansionEngineBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoAttach()
    {
        RegionalExpansionEngine.EnsureInstance();
    }
}

#if UNITY_EDITOR
public static class RegionalExpansionEngineMenu
{
    private const string VerifyLogPath = "Logs/regional_expansion_verify.txt";

    [MenuItem("Tools/Procedural Map/Run Regional Expansion Day")]
    public static void RunRegionalDay()
    {
        RegionalExpansionVerifyResult result = RegionalExpansionEngine.RunDayVerification();
        try
        {
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
            string path = Path.Combine(projectRoot, VerifyLogPath);
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? projectRoot);
            File.WriteAllText(path, result.message + "\n", Encoding.UTF8);
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[RegionalExpansionEngine] 検証ログをスキップ: {exception.Message}");
        }

        Debug.Log(
            result.success
                ? $"<color=#80DEEA><b>【広域シミュレーション・検証】PASS</b></color> {result.message}"
                : $"<color=#FF8A80><b>【広域シミュレーション・検証】FAIL</b></color> {result.message}");
        EditorUtility.DisplayDialog("Regional Expansion", result.message, "OK");
    }
}
#endif

using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

// =============================================================================
// ミクロ村成果 → マクロ国家パラメータ / 歴史フラグ還元
// 連携: VillageStorageMarket / VillageBarrierCore / VillageInfrastructureEngine
//       HistoryFlagRegistry / MacroChronicleNationSnapshot / NpcCivilizationEngine
// =============================================================================

/// <summary>24 位相完了時の村インフラ・倉庫スナップショット（集計ログ用）。</summary>
[Serializable]
public sealed class MicroYearFacilitySnapshot
{
    public int palisadeLevel = 1;
    public int workshopLevel = 1;
    public float housingDurabilityAvg = 100f;
    public float foodStock;
    public float timberStock;
    public float crystalStock;
    public float barrierPercent;

    public string FormatShort()
    {
        return $"防壁Lv{palisadeLevel} 工房Lv{workshopLevel} 住居{housingDurabilityAvg:F0}% " +
               $"倉庫F{foodStock:F1}/T{timberStock:F1}/C{crystalStock:F1} 結界{barrierPercent:F1}%";
    }
}

/// <summary>バッチ検証の成否。</summary>
public sealed class MicroToMacroVerifyResult
{
    public bool success;
    public string message = string.Empty;
    public float power;
    public float economy;
    public float military;
    public float magic;
    public float barrierEfficiency;
    public bool yearEndApplied;
    public bool writebackFileWritten;
}

/// <summary>国家マクロ統計（Economy / Military / Magic / Barrier / Power）。</summary>
[Serializable]
public class MacroStats
{
    public float Economy = 400f;
    public float Military = 300f;
    public float Magic = 300f;
    /// <summary>0.0〜1.0（村結界維持率と同期）。</summary>
    public float BarrierEfficiency = 0.95f;
    public float Power = 1000f;

    public void RecalculatePower()
    {
        Power = Economy + Military + Magic;
    }

    public static MacroStats CreateSafeDefaults()
    {
        MacroStats stats = new MacroStats
        {
            Economy = 400f,
            Military = 300f,
            Magic = 300f,
            BarrierEfficiency = 0.95f,
            Power = 1000f
        };
        stats.RecalculatePower();
        return stats;
    }
}

/// <summary>
/// macro_chronicle_geo.json の nations[] 1 件と同じキーで書き戻せるスナップショット。
/// </summary>
[Serializable]
public class MacroGeoNationWriteback
{
    public int id = 1;
    public string name = "国家001";
    public string region = string.Empty;
    public float lat;
    public float lng;
    public bool alive = true;
    public float territory = 30f;
    public float power = 1000f;
    public float economy = 400f;
    public float military = 300f;
    public float magic = 300f;
    public float barrier_efficiency = 0.95f;
    public int born_turn;
    public int died_turn = -1;
    public int turn = 1;
    public string source = "MicroToMacroAggregator";

    public static MacroGeoNationWriteback FromStats(
        MacroStats stats,
        MacroChronicleNationSnapshot baseSnap,
        int nationId,
        int turn)
    {
        MacroStats safe = stats ?? MacroStats.CreateSafeDefaults();
        MacroGeoNationWriteback dto = new MacroGeoNationWriteback
        {
            id = nationId,
            turn = turn,
            economy = safe.Economy,
            military = safe.Military,
            magic = safe.Magic,
            barrier_efficiency = Mathf.Clamp01(safe.BarrierEfficiency),
            power = safe.Power,
            alive = true,
            source = "MicroToMacroAggregator"
        };

        if (baseSnap != null)
        {
            dto.name = string.IsNullOrEmpty(baseSnap.name) ? dto.name : baseSnap.name;
            dto.region = baseSnap.region ?? string.Empty;
            dto.lat = baseSnap.lat;
            dto.lng = baseSnap.lng;
            dto.territory = baseSnap.territory > 0f ? baseSnap.territory : dto.territory;
            dto.born_turn = baseSnap.bornTurn;
            dto.died_turn = baseSnap.diedTurn;
            dto.alive = baseSnap.alive;
            if (string.IsNullOrEmpty(dto.name) || dto.name == "国家001")
            {
                // 表示名は番号を出さない方針だが geo 原典が番号名の場合はそのまま保持
                dto.name = baseSnap.name;
            }
        }

        return dto;
    }

    public string ToJsonObject()
    {
        StringBuilder sb = new StringBuilder(256);
        sb.Append('{');
        sb.Append("\"id\":").Append(id).Append(',');
        sb.Append("\"name\":\"").Append(Escape(name)).Append("\",");
        sb.Append("\"region\":\"").Append(Escape(region)).Append("\",");
        sb.Append("\"lat\":").Append(lat.ToString("0.###")).Append(',');
        sb.Append("\"lng\":").Append(lng.ToString("0.####")).Append(',');
        sb.Append("\"alive\":").Append(alive ? "true" : "false").Append(',');
        sb.Append("\"territory\":").Append(territory.ToString("0.#")).Append(',');
        sb.Append("\"power\":").Append(power.ToString("0.####", CultureInfo.InvariantCulture)).Append(',');
        sb.Append("\"economy\":").Append(economy.ToString("0.####", CultureInfo.InvariantCulture)).Append(',');
        sb.Append("\"military\":").Append(military.ToString("0.####", CultureInfo.InvariantCulture)).Append(',');
        sb.Append("\"magic\":").Append(magic.ToString("0.####", CultureInfo.InvariantCulture)).Append(',');
        sb.Append("\"barrier_efficiency\":").Append(barrier_efficiency.ToString("0.####", CultureInfo.InvariantCulture)).Append(',');
        sb.Append("\"born_turn\":").Append(born_turn).Append(',');
        sb.Append("\"died_turn\":").Append(died_turn < 0 ? "null" : died_turn.ToString()).Append(',');
        sb.Append("\"turn\":").Append(turn).Append(',');
        sb.Append("\"source\":\"").Append(Escape(source)).Append('\"');
        sb.Append('}');
        return sb.ToString();
    }

    private static string Escape(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}

/// <summary>
/// 村シミュレーション結果を国家001・ターン1の MacroStats / conditionFlag へ還元します。
/// </summary>
[DefaultExecutionOrder(60)]
public class MicroToMacroAggregator : MonoBehaviour
{
    public const int DefaultNationId = 1;
    public const float CanonicalStoryBarrierFloor = 0.95f;
    public const float DegradedBarrierThreshold = 0.5f;
    public const int DefaultTurn = 1;
    public const float DefaultPowerFallback = 1000f;

    public const string FlagBarrierDrop = "HIST_NATION_001_GEO_TURN_001_BARRIERDROP";
    public const string FlagInnovation = "HIST_NATION_001_GEO_TURN_001_INNOVATION";
    public const string FlagHero = "HIST_NATION_001_GEO_TURN_001_HERO";

    public const float FoodReserve = 20f;
    public const float TimberReserve = 15f;
    public const float EconomySurplusFactor = 0.1f;
    public const float MilitaryFactor = 0.2f;
    public const int InnovationWorkshopLevelMin = 3;

    public static MicroToMacroAggregator Instance { get; private set; }

    [SerializeField] private int nationId = DefaultNationId;
    [SerializeField] private int turn = DefaultTurn;
    [SerializeField] private MacroStats stats = MacroStats.CreateSafeDefaults();
    [SerializeField] private MacroGeoNationWriteback lastWriteback;
    [SerializeField] private int yearWeaponProduction;
    [SerializeField] private int yearArmorProduction;
    [SerializeField] private float yearStartFood;
    [SerializeField] private float yearStartTimber;
    [SerializeField] private bool yearBaselineCaptured;
    [SerializeField] private bool flagBarrierDropIssued;
    [SerializeField] private bool flagInnovationIssued;
    [SerializeField] private bool flagHeroIssued;
    [SerializeField] private int lastAggregatedPhase = -1;
    [SerializeField] private bool yearEndApplied;
    [SerializeField] private MicroYearFacilitySnapshot lastFacilitySnapshot;
    [SerializeField] private float phaseStartBarrierNorm = 0.95f;
    [SerializeField] private float lastSyncedBarrierNorm = 0.95f;
    [SerializeField] private bool geoWritebackFileSucceeded;
    [SerializeField] private bool productionNeedNotifiedThisYear;
    [SerializeField] private bool barrierNeedNotifiedThisYear;

    public MacroStats Stats => stats ?? (stats = MacroStats.CreateSafeDefaults());
    public MacroGeoNationWriteback LastWriteback => lastWriteback;
    public MicroYearFacilitySnapshot LastFacilitySnapshot => lastFacilitySnapshot;
    public int LastAggregatedPhase => lastAggregatedPhase;
    public bool YearEndApplied => yearEndApplied;
    public bool GeoWritebackFileSucceeded => geoWritebackFileSucceeded;
    public bool FlagBarrierDropIssued => flagBarrierDropIssued;
    public int Turn => turn;
    public int NationId => nationId;

    /// <summary>翌ターン開始用に年次フラグをリセットし、必要なら ALT_ 補正を再適用します。</summary>
    public void PrepareTurnTransition(int newTurn, bool applyBranchAdjustments = true)
    {
        turn = EraContextResolver.ClampSimulationTurn(newTurn);
        yearEndApplied = false;
        yearBaselineCaptured = false;
        EnsureSafeStats();
        if (applyBranchAdjustments)
        {
            HistoryBranchManager.TryApplyToMacroStats(stats, turn, nationId);
        }
    }

    /// <summary>ミクロ還元で劣化した結界値を正史フロアへ戻します（0〜1）。</summary>
    public static float NormalizeCanonicalBarrier(float rawNorm)
    {
        if (float.IsNaN(rawNorm) || float.IsInfinity(rawNorm))
        {
            return CanonicalStoryBarrierFloor;
        }

        float norm = rawNorm;
        if (norm > 1.01f)
        {
            norm /= 100f;
        }

        norm = Mathf.Clamp01(norm);
        return norm < DegradedBarrierThreshold ? CanonicalStoryBarrierFloor : norm;
    }

    /// <summary>指定ターンの geo 正史ベース結界（0〜1）を返します。</summary>
    public float ResolveGeoBarrierBaseline(int atTurn)
    {
        try
        {
            MicroHistoryTimeline timeline = MicroHistoryTimelineTimelineEngine.BuildTimeline(nationId, atTurn);
            MacroChronicleNationSnapshot snap = timeline?.geoAtTurn;
            if (snap != null && snap.barrierEfficiency > 0.01f)
            {
                return NormalizeCanonicalBarrier(snap.barrierEfficiency);
            }
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[MicroToMacroAggregator] ResolveGeoBarrierBaseline Safe-Fail: {exception.Message}");
        }

        return stats != null && stats.BarrierEfficiency > 0.01f
            ? NormalizeCanonicalBarrier(stats.BarrierEfficiency)
            : CanonicalStoryBarrierFloor;
    }

    /// <summary>マクロ結界を村結界へ書き戻し、翌年1日目の低結界引き継ぎを防ぎます。</summary>
    public void TrySyncVillageBarrierFromMacro(float barrierNorm)
    {
        try
        {
            VillageAutonomyEngine village = VillageAutonomyEngine.EnsureInstance();
            if (village?.Barrier == null)
            {
                return;
            }

            float percent = Mathf.Clamp(barrierNorm * 100f, VillageBarrierCore.MinEfficiency, VillageBarrierCore.MaxEfficiency);
            village.Barrier.Efficiency = percent;
            lastSyncedBarrierNorm = Mathf.Clamp01(barrierNorm);
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[MicroToMacroAggregator] TrySyncVillageBarrierFromMacro Safe-Fail: {exception.Message}");
        }
    }

    /// <summary>ターン開始時マクロ値（国力・結界）を確定し、村結界へ同期します。</summary>
    public void ApplyOpeningMacroForTurn(float power, float barrierNorm)
    {
        EnsureSafeStats();
        stats.BarrierEfficiency = Mathf.Clamp01(barrierNorm);

        if (power > 0.01f && stats.Power > 0.01f)
        {
            float ratio = power / stats.Power;
            stats.Economy = Mathf.Max(0f, stats.Economy * ratio);
            stats.Military = Mathf.Max(0f, stats.Military * ratio);
            stats.Magic = Mathf.Max(0f, stats.Magic * ratio);
        }

        stats.RecalculatePower();
        if (power > stats.Power)
        {
            stats.Power = power;
        }

        TrySyncVillageBarrierFromMacro(barrierNorm);
    }

    public static MicroToMacroAggregator EnsureInstance()
    {
        if (Instance != null)
        {
            return Instance;
        }

        MicroToMacroAggregator existing = FindAnyObjectByType<MicroToMacroAggregator>();
        if (existing != null)
        {
            Instance = existing;
            return Instance;
        }

        GameObject hub = GameObject.Find("DebugSystemsHub");
        GameObject host = hub != null ? hub : new GameObject(nameof(MicroToMacroAggregator));
        MicroToMacroAggregator agg = host.GetComponent<MicroToMacroAggregator>();
        return agg != null ? agg : host.AddComponent<MicroToMacroAggregator>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            return;
        }

        Instance = this;
        EnsureSafeStats();
        TryLoadBaselineFromGeo();
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    /// <summary>1 年（24 位相）開始時の倉庫基準を記録します。</summary>
    public void BeginYearBaseline()
    {
        try
        {
            EnsureSafeStats();
            VillageStorageMarket market = ResolveMarket();
            yearStartFood = market != null ? market.Food : FoodReserve;
            yearStartTimber = market != null ? market.Timber : TimberReserve;
            yearWeaponProduction = 0;
            yearArmorProduction = 0;
            yearBaselineCaptured = true;
            flagBarrierDropIssued = false;
            flagInnovationIssued = false;
            flagHeroIssued = false;
            yearEndApplied = false;
            lastAggregatedPhase = 0;
            productionNeedNotifiedThisYear = false;
            barrierNeedNotifiedThisYear = false;
            geoWritebackFileSucceeded = false;
            phaseStartBarrierNorm = ResolveCurrentBarrierNorm();
            lastSyncedBarrierNorm = phaseStartBarrierNorm;
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[MicroToMacroAggregator] BeginYearBaseline Safe-Fail: {exception.Message}");
            yearBaselineCaptured = false;
            EnsureSafeStats();
        }
    }

    /// <summary>位相開始時の結界維持率を記録（エッジトリガー用）。</summary>
    public void BeginPhaseBarrierTracking()
    {
        try
        {
            phaseStartBarrierNorm = ResolveCurrentBarrierNorm();
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[MicroToMacroAggregator] BeginPhaseBarrierTracking Safe-Fail: {exception.Message}");
        }
    }

    /// <summary>位相ごと：結界同期・フラグ判定・軍事の微増分。</summary>
    public void AggregatePhase(int phase, VariableTimelineSeason season)
    {
        try
        {
            EnsureSafeStats();
            if (!yearBaselineCaptured)
            {
                BeginYearBaseline();
            }

            SyncBarrierFromVillage();
            EvaluateDynamicFlags(season);
            TryNotifyDynamicMasters();
            stats.RecalculatePower();
            HistoryBranchManager.TryApplyToMacroStats(stats, turn, nationId);
            lastAggregatedPhase = ProceduralMapPopulator.NormalizePhase(phase);
            lastWriteback = BuildWriteback();
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[MicroToMacroAggregator] AggregatePhase Safe-Fail: {exception.Message}");
            EnsureSafeStats();
        }
    }

    /// <summary>24 位相完了時：余剰資材・防壁レベルを国家パラメータへ本還元。</summary>
    public void AggregateYearEnd(bool logMacro = true)
    {
        if (yearEndApplied)
        {
            if (logMacro && lastWriteback != null)
            {
                Debug.Log(
                    $"<color=#90CAF9><b>【マクロ還元】</b></color>（再集計スキップ） " +
                    $"Power={stats.Power:F1} Economy={stats.Economy:F1} Military={stats.Military:F1} " +
                    $"BarrierEfficiency={stats.BarrierEfficiency:F3}");
            }

            return;
        }

        yearEndApplied = true;
        try
        {
            EnsureSafeStats();
            VillageStorageMarket market = ResolveMarket();
            VillageInfrastructureEngine infra = null;
            try
            {
                infra = VillageInfrastructureEngine.EnsureInstance();
            }
            catch (Exception)
            {
                infra = null;
            }

            float foodNow = market != null ? market.Food : yearStartFood;
            float timberNow = market != null ? market.Timber : yearStartTimber;
            float foodSurplus = Mathf.Max(0f, foodNow - Mathf.Max(yearStartFood, FoodReserve));
            float timberSurplus = Mathf.Max(0f, timberNow - Mathf.Max(yearStartTimber, TimberReserve));
            if (foodSurplus <= 0.01f && market != null)
            {
                foodSurplus = Mathf.Max(0f, market.Food - FoodReserve);
            }

            if (timberSurplus <= 0.01f && market != null)
            {
                timberSurplus = Mathf.Max(0f, market.Timber - TimberReserve);
            }

            float economyGain = (foodSurplus + timberSurplus) * EconomySurplusFactor;
            stats.Economy += economyGain;

            int palisadeLevel = ResolveMaxPalisadeLevel(infra);
            int workshopLevel = ResolveMaxWorkshopLevel(infra);
            float militaryGain =
                (yearWeaponProduction + yearArmorProduction) * MilitaryFactor +
                palisadeLevel * MilitaryFactor;
            stats.Military += militaryGain;

            float crystal = market != null ? market.ManaCrystal : 0f;
            stats.Magic += Mathf.Max(0f, crystal * 0.05f);

            SyncBarrierFromVillage();
            stats.RecalculatePower();
            HistoryBranchManager.TryApplyToMacroStats(stats, turn, nationId);
            lastFacilitySnapshot = CaptureFacilitySnapshot(market, infra);
            lastWriteback = BuildWriteback();
            bool geoFileOk = TryWriteGeoNationSnapshot(lastWriteback);
            geoWritebackFileSucceeded = geoFileOk;
            TryWriteSnapshotFile(lastWriteback, lastFacilitySnapshot);

            if (logMacro)
            {
                string facilityLine = lastFacilitySnapshot != null
                    ? lastFacilitySnapshot.FormatShort()
                    : $"防壁Lv{palisadeLevel} 工房Lv{workshopLevel}";
                Debug.Log(
                    "<color=#90CAF9><b>【マクロ還元】</b></color> " +
                    $"国家{nationId:D3} ターン{turn} " +
                    $"Power={stats.Power.ToString("F1", CultureInfo.InvariantCulture)} " +
                    $"Economy={stats.Economy.ToString("F1", CultureInfo.InvariantCulture)}(+{economyGain.ToString("F1", CultureInfo.InvariantCulture)}) " +
                    $"Military={stats.Military.ToString("F1", CultureInfo.InvariantCulture)}(+{militaryGain.ToString("F1", CultureInfo.InvariantCulture)}) " +
                    $"Magic={stats.Magic.ToString("F1", CultureInfo.InvariantCulture)} " +
                    $"BarrierEfficiency={stats.BarrierEfficiency.ToString("F4", CultureInfo.InvariantCulture)} " +
                    $"余剰 F{foodSurplus:F1}/T{timberSurplus:F1} 武具生産{yearWeaponProduction}+{yearArmorProduction} " +
                    $"{facilityLine}");

                string geoPath = MicroHistoryTimelineTimelineEngine.ResolveGeoPath();
                Debug.Log(
                    "<color=#90CAF9><b>【マクロ還元・書き戻し完了】</b></color> " +
                    $"geo={geoPath} " +
                    $"power={lastWriteback.power.ToString("F1", CultureInfo.InvariantCulture)} " +
                    $"economy={lastWriteback.economy.ToString("F1", CultureInfo.InvariantCulture)} " +
                    $"military={lastWriteback.military.ToString("F1", CultureInfo.InvariantCulture)} " +
                    $"magic={lastWriteback.magic.ToString("F1", CultureInfo.InvariantCulture)} " +
                    $"barrier_efficiency={lastWriteback.barrier_efficiency.ToString("F4", CultureInfo.InvariantCulture)} " +
                    $"fileOk={geoFileOk}");
            }
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[MicroToMacroAggregator] AggregateYearEnd Safe-Fail: {exception.Message}");
            EnsureSafeStats();
            if (logMacro)
            {
                Debug.Log(
                    $"<color=#90CAF9><b>【マクロ還元】</b></color> Safe-Fail 既定値 Power={stats.Power:F0} " +
                    $"E={stats.Economy:F0} M={stats.Military:F0} Mag={stats.Magic:F0} " +
                    $"Barrier={stats.BarrierEfficiency:F2}");
            }
        }
    }

    /// <summary>鍛冶の武器／防具生産を軍事還元用に計上します。</summary>
    public void NotifySmithProduction(bool weapon, bool armor, int workshopLevel)
    {
        try
        {
            if (weapon)
            {
                yearWeaponProduction++;
            }

            if (armor)
            {
                yearArmorProduction++;
            }

            if (!flagInnovationIssued && workshopLevel >= InnovationWorkshopLevelMin)
            {
                IssueFlag(FlagInnovation, "鍛冶が高レベル施設で生産");
                flagInnovationIssued = true;
            }
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[MicroToMacroAggregator] NotifySmithProduction Safe-Fail: {exception.Message}");
        }
    }

    /// <summary>結界守が休眠期に結界を満タン付近まで戻したとき。</summary>
    public void NotifyBarrierHeroRepair(VariableTimelineSeason season, float barrierPercent)
    {
        try
        {
            if (flagHeroIssued)
            {
                return;
            }

            if (season != VariableTimelineSeason.Dormant)
            {
                return;
            }

            if (barrierPercent < 99.5f)
            {
                return;
            }

            IssueFlag(FlagHero, "結界守が休眠期に結界を満タン修復");
            flagHeroIssued = true;
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[MicroToMacroAggregator] NotifyBarrierHeroRepair Safe-Fail: {exception.Message}");
        }
    }

    public MacroGeoNationWriteback BuildWriteback()
    {
        EnsureSafeStats();
        MacroChronicleNationSnapshot baseSnap = null;
        try
        {
            MicroHistoryTimeline timeline = MicroHistoryTimelineTimelineEngine.BuildTimeline(nationId, turn);
            baseSnap = timeline?.geoAtTurn;
        }
        catch (Exception)
        {
            baseSnap = null;
        }

        return MacroGeoNationWriteback.FromStats(stats, baseSnap, nationId, turn);
    }

    private void SyncBarrierFromVillage()
    {
        try
        {
            VillageAutonomyEngine village = VillageAutonomyEngine.EnsureInstance();
            if (village?.Barrier == null)
            {
                return;
            }

            float newNorm = Mathf.Clamp01(village.Barrier.Efficiency / 100f);
            bool nowAtZero = village.Barrier.Efficiency <= 0.01f || village.Barrier.BarrierDropped;
            bool edgeDrop = nowAtZero && phaseStartBarrierNorm > 0.01f;

            stats.BarrierEfficiency = newNorm;
            lastSyncedBarrierNorm = newNorm;

            if (edgeDrop)
            {
                TryIssueBarrierDrop();
            }
        }
        catch (Exception)
        {
            // 村未配置時は既存 BarrierEfficiency を維持
        }
    }

    private float ResolveCurrentBarrierNorm()
    {
        try
        {
            VillageAutonomyEngine village = VillageAutonomyEngine.EnsureInstance();
            if (village?.Barrier != null)
            {
                return Mathf.Clamp01(village.Barrier.Efficiency / 100f);
            }
        }
        catch (Exception)
        {
            // Safe-Fail
        }

        return Mathf.Clamp01(stats != null ? stats.BarrierEfficiency : 0.95f);
    }

    private void EvaluateDynamicFlags(VariableTimelineSeason season)
    {
        _ = season;
    }

    private void TryIssueBarrierDrop()
    {
        if (flagBarrierDropIssued)
        {
            return;
        }

        IssueFlag(FlagBarrierDrop, "結界維持率 0%");
        flagBarrierDropIssued = true;
    }

    private static void IssueFlag(string flagKey, string reason)
    {
        try
        {
            HistoryFlagRegistry.EnsureWired();
            bool neu = HistoryFlagRegistry.Unlock(flagKey);
            if (neu)
            {
                Debug.Log(
                    $"<color=#FFE082><b>【歴史フラグ】</b></color> {flagKey} 解禁（{reason}）");
                DynamicMasterGenerationEngine.NotifyHistoryFlag(flagKey, reason);
            }
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[MicroToMacroAggregator] フラグ登録 Safe-Fail ({flagKey}): {exception.Message}");
        }
    }

    private void TryNotifyDynamicMasters()
    {
        try
        {
            float barrierPercent = ResolveCurrentBarrierNorm() * 100f;
            VillageStorageMarket market = ResolveMarket();
            bool foodDepleted = market != null && market.FoodDepleted;

            if (foodDepleted && !productionNeedNotifiedThisYear)
            {
                productionNeedNotifiedThisYear = true;
                DynamicMasterGenerationEngine.NotifyVillageState(barrierPercent, true);
                return;
            }

            if (!barrierNeedNotifiedThisYear && barrierPercent <= 50f)
            {
                barrierNeedNotifiedThisYear = true;
                DynamicMasterGenerationEngine.NotifyVillageState(barrierPercent, false);
            }
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[MicroToMacroAggregator] 動的マスター通知 Safe-Fail: {exception.Message}");
        }
    }

    private bool TryWriteGeoNationSnapshot(MacroGeoNationWriteback dto)
    {
        if (dto == null)
        {
            return false;
        }

        try
        {
            string geoPath = MicroHistoryTimelineTimelineEngine.ResolveGeoPath();
            if (MacroChronicleJsonScan.TryWriteNationAtTurn(geoPath, turn, nationId, dto, out string error))
            {
                return true;
            }

            Debug.LogWarning(
                $"[MicroToMacroAggregator] geo 書き戻し Safe-Fail（メモリのみ更新）: {error}");
            return false;
        }
        catch (Exception exception)
        {
            Debug.LogWarning(
                $"[MicroToMacroAggregator] geo 書き戻し Safe-Fail（メモリのみ更新）: {exception.Message}");
            return false;
        }
    }

    private void EnsureSafeStats()
    {
        if (stats == null)
        {
            stats = MacroStats.CreateSafeDefaults();
        }

        if (float.IsNaN(stats.Economy) || float.IsInfinity(stats.Economy))
        {
            stats.Economy = 400f;
        }

        if (float.IsNaN(stats.Military) || float.IsInfinity(stats.Military))
        {
            stats.Military = 300f;
        }

        if (float.IsNaN(stats.Magic) || float.IsInfinity(stats.Magic))
        {
            stats.Magic = 300f;
        }

        if (float.IsNaN(stats.BarrierEfficiency) || float.IsInfinity(stats.BarrierEfficiency))
        {
            stats.BarrierEfficiency = 0.95f;
        }

        stats.BarrierEfficiency = Mathf.Clamp01(stats.BarrierEfficiency);
        stats.RecalculatePower();
        if (stats.Power <= 0.01f || float.IsNaN(stats.Power))
        {
            stats.Power = DefaultPowerFallback;
        }
    }

    private void TryLoadBaselineFromGeo()
    {
        try
        {
            MicroHistoryTimeline timeline = MicroHistoryTimelineTimelineEngine.BuildTimeline(nationId, turn);
            MacroChronicleNationSnapshot snap = timeline?.geoAtTurn;
            if (snap == null)
            {
                EnsureSafeStats();
                return;
            }

            stats.Economy = snap.economy > 0f ? snap.economy : 400f;
            stats.Military = snap.military > 0f ? snap.military : 300f;
            stats.Magic = snap.magic > 0f ? snap.magic : 300f;
            stats.BarrierEfficiency = snap.barrierEfficiency > 0f
                ? Mathf.Clamp01(snap.barrierEfficiency)
                : 0.95f;
            stats.RecalculatePower();
            if (stats.Power < 1f)
            {
                stats.Power = DefaultPowerFallback;
            }
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[MicroToMacroAggregator] geo 読込 Safe-Fail → Power {DefaultPowerFallback}: {exception.Message}");
            stats = MacroStats.CreateSafeDefaults();
        }
    }

    private static VillageStorageMarket ResolveMarket()
    {
        try
        {
            return NpcCivilizationEngine.EnsureInstance()?.Storage;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static int ResolveMaxPalisadeLevel(VillageInfrastructureEngine infra)
    {
        return ResolveMaxBuildingLevel(infra, BuildingType.Palisade);
    }

    private static int ResolveMaxWorkshopLevel(VillageInfrastructureEngine infra)
    {
        return ResolveMaxBuildingLevel(infra, BuildingType.Workshop);
    }

    private static int ResolveMaxBuildingLevel(VillageInfrastructureEngine infra, BuildingType type)
    {
        if (infra?.Buildings == null)
        {
            return 1;
        }

        int max = 1;
        for (int i = 0; i < infra.Buildings.Count; i++)
        {
            VillageBuildingData b = infra.Buildings[i];
            if (b != null && b.Type == type)
            {
                max = Mathf.Max(max, b.Level);
            }
        }

        return max;
    }

    private MicroYearFacilitySnapshot CaptureFacilitySnapshot(
        VillageStorageMarket market,
        VillageInfrastructureEngine infra)
    {
        MicroYearFacilitySnapshot snap = new MicroYearFacilitySnapshot
        {
            palisadeLevel = ResolveMaxPalisadeLevel(infra),
            workshopLevel = ResolveMaxWorkshopLevel(infra),
            housingDurabilityAvg = infra != null ? infra.AverageHousingDurability() : 100f,
            foodStock = market != null ? market.Food : FoodReserve,
            timberStock = market != null ? market.Timber : TimberReserve,
            crystalStock = market != null ? market.ManaCrystal : 0f
        };

        try
        {
            VillageAutonomyEngine village = VillageAutonomyEngine.EnsureInstance();
            if (village?.Barrier != null)
            {
                snap.barrierPercent = village.Barrier.Efficiency;
            }
            else
            {
                snap.barrierPercent = stats.BarrierEfficiency * 100f;
            }
        }
        catch (Exception)
        {
            snap.barrierPercent = stats.BarrierEfficiency * 100f;
        }

        return snap;
    }

    private void TryWriteSnapshotFile(MacroGeoNationWriteback dto, MicroYearFacilitySnapshot facilities)
    {
        if (dto == null)
        {
            return;
        }

        try
        {
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
            string dir = Path.Combine(projectRoot, "Logs");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, "macro_nation001_turn001_writeback.json");
            string facilityJson = facilities != null
                ? string.Format(
                    CultureInfo.InvariantCulture,
                    ",\"facilities\":{{\"palisade_level\":{0},\"workshop_level\":{1},\"housing_durability_avg\":{2},\"food\":{3},\"timber\":{4},\"barrier_percent\":{5}}}",
                    facilities.palisadeLevel,
                    facilities.workshopLevel,
                    facilities.housingDurabilityAvg.ToString("F1", CultureInfo.InvariantCulture),
                    facilities.foodStock.ToString("F1", CultureInfo.InvariantCulture),
                    facilities.timberStock.ToString("F1", CultureInfo.InvariantCulture),
                    facilities.barrierPercent.ToString("F1", CultureInfo.InvariantCulture))
                : string.Empty;
            string body =
                "{\n  \"meta\": { \"note\": \"MicroToMacro writeback for nation 001 turn 1\"" + facilityJson + " },\n" +
                "  \"snapshot\": " + dto.ToJsonObject() + "\n}\n";
            File.WriteAllText(path, body, Encoding.UTF8);
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[MicroToMacroAggregator] JSON 書き戻しをスキップ: {exception.Message}");
        }
    }

    /// <summary>24 位相シミュレーションを実行し、マクロ還元が成立したか検証します。</summary>
    public static MicroToMacroVerifyResult RunYearVerification()
    {
        MicroToMacroVerifyResult result = new MicroToMacroVerifyResult();
        try
        {
            HistoryFlagRegistry.EnsureWired();
            NpcCivilizationEngine civ = NpcCivilizationEngine.EnsureInstance();
            MicroToMacroAggregator agg = MicroToMacroAggregator.EnsureInstance();
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
            agg.BeginYearBaseline();
            civ.SimulateFullDay(ProceduralMapPopulator.DefaultNation001Turn1Distribution());

            MacroStats s = agg.Stats;
            result.power = s.Power;
            result.economy = s.Economy;
            result.military = s.Military;
            result.magic = s.Magic;
            result.barrierEfficiency = s.BarrierEfficiency;
            result.yearEndApplied = agg.YearEndApplied;
            result.writebackFileWritten = agg.GeoWritebackFileSucceeded || File.Exists(
                Path.Combine(projectRoot, "Logs", "macro_nation001_turn001_writeback.json"));

            bool statsOk = s.Power > 100f && s.Economy > 0f && s.Military > 0f;
            result.success = statsOk && result.yearEndApplied;
            result.message =
                $"Power={s.Power:F1} E={s.Economy:F1} M={s.Military:F1} Mag={s.Magic:F1} " +
                $"Barrier={s.BarrierEfficiency:F4} yearEnd={result.yearEndApplied} " +
                $"geoWriteback={agg.GeoWritebackFileSucceeded} barrierDropOnce={agg.FlagBarrierDropIssued}";
        }
        catch (Exception exception)
        {
            result.success = false;
            result.message = $"Safe-Fail: {exception.Message}";
        }

        return result;
    }
}

public static class MicroToMacroAggregatorBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoAttach()
    {
        MicroToMacroAggregator.EnsureInstance();
    }
}

#if UNITY_EDITOR
public static class MicroToMacroAggregatorMenu
{
    private const string VerifyLogPath = "Logs/micro_to_macro_verify.txt";

    [MenuItem("Tools/Procedural Map/Run Micro-to-Macro Year")]
    public static void RunYearAggregation()
    {
        MicroToMacroVerifyResult result = MicroToMacroAggregator.RunYearVerification();
        string line = result.message;
        WriteVerifyLog(result);
        Debug.Log(
            result.success
                ? $"<color=#90CAF9><b>【マクロ還元・検証】PASS</b></color> {line}"
                : $"<color=#FF8A80><b>【マクロ還元・検証】FAIL</b></color> {line}");
        EditorUtility.DisplayDialog("MicroToMacro", line, "OK");
    }

    /// <summary>Unity バッチ: -executeMethod MicroToMacroAggregatorMenu.BatchVerifyAndQuit</summary>
    public static void BatchVerifyAndQuit()
    {
        MicroToMacroVerifyResult result = MicroToMacroAggregator.RunYearVerification();
        WriteVerifyLog(result);
        Debug.Log(
            result.success
                ? $"<color=#A5D6A7><b>【マクロ還元検証】PASS</b></color> {result.message}"
                : $"<color=#FF8A80><b>【マクロ還元検証】FAIL</b></color> {result.message}");
        EditorApplication.Exit(result.success ? 0 : 1);
    }

    private static void WriteVerifyLog(MicroToMacroVerifyResult result)
    {
        try
        {
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
            string path = Path.Combine(projectRoot, VerifyLogPath);
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? projectRoot);
            File.WriteAllText(
                path,
                $"{DateTime.UtcNow:O} success={result.success} {result.message}\n",
                Encoding.UTF8);
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[MicroToMacroAggregatorMenu] 検証ログをスキップ: {exception.Message}");
        }
    }
}
#endif

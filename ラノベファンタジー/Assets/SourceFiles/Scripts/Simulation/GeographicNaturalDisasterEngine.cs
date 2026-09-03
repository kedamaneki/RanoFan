using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

// =============================================================================
// 地理依存天候・自然災害 — 標高/水域/地質に基づく発火制御
// 連携: GeographicFeatureDetector / VillageBarrierCore / VillageStorageMarket
//       VillageInfrastructureEngine / NpcCivilizationEngine / DailySimulationEngine
// =============================================================================

/// <summary>天候状態。</summary>
public enum GeographicWeather
{
    Clear = 0,
    Cloudy = 1,
    Rain = 2,
    HeavyRain = 3,
    Ashfall = 4
}

/// <summary>自然災害種別。</summary>
public enum GeographicDisasterKind
{
    Flood = 0,
    Eruption = 1,
    Landslide = 2,
    Earthquake = 3
}

/// <summary>1 件の災害イベント結果。</summary>
public sealed class GeographicDisasterEvent
{
    public GeographicDisasterKind kind;
    public int phase;
    public int dayOfYear;
    public float barrierDelta;
    public float foodLoss;
    public float timberLoss;
    public float facilityDamage;
    public string reason = string.Empty;
    public string message = string.Empty;
}

/// <summary>検証結果。</summary>
public sealed class GeographicDisasterVerifyResult
{
    public bool success;
    public string message = string.Empty;
}

/// <summary>
/// 地形条件に合致した天候推移と自然災害（洪水・噴火・地震・土砂崩れ）を駆動します。
/// </summary>
[DefaultExecutionOrder(48)]
public class GeographicNaturalDisasterEngine : MonoBehaviour
{
    public const string LogTag = "【自然災害】";
    public const int HeavyRainStreakForFlood = 2;
    public const float CrustStressEruptionThreshold = 0.72f;
    public const float ManaEruptionThreshold = 68f;

    public static GeographicNaturalDisasterEngine Instance { get; private set; }

    [SerializeField] private GeographicWeather currentWeather = GeographicWeather.Clear;
    [SerializeField] private int heavyRainStreak;
    [SerializeField] private float crustStress;
    [SerializeField] private int lastTickedPhase = -1;
    [SerializeField] private bool earthquakeThisPhase;
    [SerializeField] private bool featuresCached;
    [SerializeField] private GeographicFeatureFlags featureFlags = new GeographicFeatureFlags();

    private readonly List<GeographicDisasterEvent> recentEvents = new List<GeographicDisasterEvent>(16);

    public GeographicWeather CurrentWeather => currentWeather;
    public GeographicFeatureFlags FeatureFlags => featureFlags ?? (featureFlags = new GeographicFeatureFlags());
    public IReadOnlyList<GeographicDisasterEvent> RecentEvents => recentEvents;
    public float CrustStress => crustStress;

    public static GeographicNaturalDisasterEngine EnsureInstance()
    {
        if (Instance != null)
        {
            return Instance;
        }

        GeographicNaturalDisasterEngine existing =
            UnityEngine.Object.FindAnyObjectByType<GeographicNaturalDisasterEngine>();
        if (existing != null)
        {
            Instance = existing;
            return existing;
        }

        GameObject hub = GameObject.Find("DebugSystemsHub");
        GameObject host = hub != null ? hub : new GameObject(nameof(GeographicNaturalDisasterEngine));
        GeographicNaturalDisasterEngine engine = host.GetComponent<GeographicNaturalDisasterEngine>();
        return engine != null ? engine : host.AddComponent<GeographicNaturalDisasterEngine>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        RefreshFeatureFlags();
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    /// <summary>24 位相ティック（NpcCivilizationEngine から呼出）。</summary>
    public static void TryTickPhase(
        int phase,
        VariableTimelineSeason season,
        VillageStorageMarket market,
        VillageBarrierCore barrier)
    {
        try
        {
            GeographicNaturalDisasterEngine engine = EnsureInstance();
            engine.TickPhaseInternal(phase, season, market, barrier, dayOfYear: -1);
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[GeographicNaturalDisasterEngine] TryTickPhase Safe-Fail: {exception.Message}");
        }
    }

    /// <summary>365 日デイリーティック（DailySimulationEngine から呼出）。</summary>
    public static void TryTickDay(
        int dayOfYear,
        VariableTimelineSeason season,
        int phase,
        VillageStorageMarket market,
        VillageBarrierCore barrier)
    {
        try
        {
            GeographicNaturalDisasterEngine engine = EnsureInstance();
            engine.TickPhaseInternal(phase, season, market, barrier, dayOfYear);
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[GeographicNaturalDisasterEngine] TryTickDay Safe-Fail: {exception.Message}");
        }
    }

    public void RefreshFeatureFlags()
    {
        Terrain terrain = ResolveTerrain();
        featureFlags = GeographicFeatureDetector.Detect(terrain);
        featuresCached = true;
    }

    private void TickPhaseInternal(
        int phase,
        VariableTimelineSeason season,
        VillageStorageMarket market,
        VillageBarrierCore barrier,
        int dayOfYear)
    {
        if (!featuresCached)
        {
            RefreshFeatureFlags();
        }

        int normalized = ProceduralMapPopulator.NormalizePhase(phase);
        lastTickedPhase = normalized;
        earthquakeThisPhase = false;

        AdvanceWeather(season, normalized, dayOfYear);
        UpdateCrustStress(season, normalized);

        TryTriggerEarthquake(normalized, dayOfYear, market, barrier);
        TryTriggerFlood(normalized, dayOfYear, market, barrier);
        TryTriggerEruption(normalized, dayOfYear, market, barrier);
        TryTriggerLandslide(normalized, dayOfYear, market, barrier);
    }

    private void AdvanceWeather(VariableTimelineSeason season, int phase, int dayOfYear)
    {
        int seed = dayOfYear > 0 ? dayOfYear * 17 + phase : phase * 31 + (int)season;
        System.Random rng = new System.Random(seed);

        GeographicWeather next = currentWeather;
        switch (season)
        {
            case VariableTimelineSeason.Active:
                next = RollWeather(rng, clear: 0.35f, cloudy: 0.25f, rain: 0.28f, heavy: 0.12f, ash: 0f);
                break;
            case VariableTimelineSeason.Deescalation:
                next = RollWeather(rng, clear: 0.40f, cloudy: 0.30f, rain: 0.22f, heavy: 0.08f, ash: 0f);
                break;
            case VariableTimelineSeason.Dormant:
                next = RollWeather(rng, clear: 0.55f, cloudy: 0.30f, rain: 0.12f, heavy: 0.03f, ash: 0f);
                break;
            case VariableTimelineSeason.Escalation:
                next = RollWeather(rng, clear: 0.18f, cloudy: 0.22f, rain: 0.30f, heavy: 0.18f,
                    ash: featureFlags.HasVolcanicFeature ? 0.12f : 0f);
                break;
        }

        if (featureFlags.HasVolcanicFeature && crustStress > 0.55f && rng.NextDouble() < 0.08)
        {
            next = GeographicWeather.Ashfall;
        }

        currentWeather = next;
        heavyRainStreak = currentWeather == GeographicWeather.HeavyRain
            ? heavyRainStreak + 1
            : 0;
    }

    private static GeographicWeather RollWeather(
        System.Random rng,
        float clear,
        float cloudy,
        float rain,
        float heavy,
        float ash)
    {
        double roll = rng.NextDouble();
        if (roll < clear)
        {
            return GeographicWeather.Clear;
        }

        roll -= clear;
        if (roll < cloudy)
        {
            return GeographicWeather.Cloudy;
        }

        roll -= cloudy;
        if (roll < rain)
        {
            return GeographicWeather.Rain;
        }

        roll -= rain;
        if (roll < heavy)
        {
            return GeographicWeather.HeavyRain;
        }

        return ash > 0f && rng.NextDouble() < ash / (1f - clear - cloudy - rain - heavy + 0.001f)
            ? GeographicWeather.Ashfall
            : GeographicWeather.Cloudy;
    }

    private void UpdateCrustStress(VariableTimelineSeason season, int phase)
    {
        NatureEnvironmentStatus nature = NaturalEcologyEngine.EnsureInstance().Environment;
        float mana = nature != null ? nature.ManaEcologyLevel : 50f;

        float gain = season == VariableTimelineSeason.Escalation ? 0.045f : 0.018f;
        if (featureFlags.HasVolcanicFeature)
        {
            gain *= 1.35f;
        }

        if (currentWeather == GeographicWeather.Ashfall)
        {
            gain += 0.06f;
        }

        crustStress = Mathf.Clamp01(crustStress + gain - 0.012f);
        if (mana >= ManaEruptionThreshold)
        {
            crustStress = Mathf.Clamp01(crustStress + 0.02f);
        }
    }

    private void TryTriggerFlood(int phase, int dayOfYear, VillageStorageMarket market, VillageBarrierCore barrier)
    {
        if (!featureFlags.HasWaterBody)
        {
            return;
        }

        if (heavyRainStreak < HeavyRainStreakForFlood)
        {
            return;
        }

        ApplyDisaster(
            GeographicDisasterKind.Flood,
            phase,
            dayOfYear,
            market,
            barrier,
            barrierDrop: 12f,
            foodLoss: 8f,
            timberLoss: 4f,
            facilityDamage: 6f,
            "地形条件クリア（低地・水域）+ HeavyRain 連続");
    }

    private void TryTriggerEruption(int phase, int dayOfYear, VillageStorageMarket market, VillageBarrierCore barrier)
    {
        if (!featureFlags.HasVolcanicFeature)
        {
            return;
        }

        NatureEnvironmentStatus nature = NaturalEcologyEngine.EnsureInstance().Environment;
        float mana = nature != null ? nature.ManaEcologyLevel : 0f;
        bool stressHigh = crustStress >= CrustStressEruptionThreshold;
        bool manaHigh = mana >= ManaEruptionThreshold;
        if (!stressHigh && !manaHigh)
        {
            return;
        }

        if (currentWeather != GeographicWeather.Ashfall && crustStress < 0.85f && !manaHigh)
        {
            return;
        }

        ApplyDisaster(
            GeographicDisasterKind.Eruption,
            phase,
            dayOfYear,
            market,
            barrier,
            barrierDrop: 18f,
            foodLoss: 5f,
            timberLoss: 6f,
            facilityDamage: 14f,
            $"地形条件クリア（火山性）地殻ストレス{crustStress:F2} 魔力{mana:F0}");
        crustStress = Mathf.Max(0f, crustStress - 0.35f);
    }

    private void TryTriggerLandslide(int phase, int dayOfYear, VillageStorageMarket market, VillageBarrierCore barrier)
    {
        if (!featureFlags.HasSteepSlope)
        {
            return;
        }

        bool trigger = currentWeather == GeographicWeather.HeavyRain || earthquakeThisPhase;
        if (!trigger)
        {
            return;
        }

        string reason = earthquakeThisPhase
            ? "地形条件クリア（急斜面）+ 地震余波"
            : "地形条件クリア（急斜面）+ 大雨";

        ApplyDisaster(
            GeographicDisasterKind.Landslide,
            phase,
            dayOfYear,
            market,
            barrier,
            barrierDrop: 10f,
            foodLoss: 3f,
            timberLoss: 9f,
            facilityDamage: 11f,
            reason);
    }

    private void TryTriggerEarthquake(int phase, int dayOfYear, VillageStorageMarket market, VillageBarrierCore barrier)
    {
        int seed = dayOfYear > 0 ? dayOfYear * 13 + phase : phase * 47;
        System.Random rng = new System.Random(seed);
        float chance = featureFlags.HasSteepSlope ? 0.09f : 0.05f;
        if (seasonEscalationBoost())
        {
            chance += 0.03f;
        }

        if (rng.NextDouble() >= chance)
        {
            return;
        }

        earthquakeThisPhase = true;
        float mountainMul = featureFlags.HasSteepSlope ? 1.45f : 1f;
        ApplyDisaster(
            GeographicDisasterKind.Earthquake,
            phase,
            dayOfYear,
            market,
            barrier,
            barrierDrop: 8f * mountainMul,
            foodLoss: 2f * mountainMul,
            timberLoss: 3f * mountainMul,
            facilityDamage: 9f * mountainMul,
            featureFlags.HasSteepSlope
                ? "全域発生・山岳部で被害拡大"
                : "全域発生");
        crustStress = Mathf.Clamp01(crustStress + 0.08f);
    }

    private bool seasonEscalationBoost()
    {
        return currentWeather == GeographicWeather.Ashfall ||
               crustStress > 0.4f;
    }

    private void ApplyDisaster(
        GeographicDisasterKind kind,
        int phase,
        int dayOfYear,
        VillageStorageMarket market,
        VillageBarrierCore barrier,
        float barrierDrop,
        float foodLoss,
        float timberLoss,
        float facilityDamage,
        string reason)
    {
        float barrierBefore = barrier?.Efficiency ?? 0f;
        if (barrier != null)
        {
            barrier.ApplyExternalDamage(barrierDrop);
        }

        if (market != null)
        {
            market.WithdrawFood(foodLoss);
            market.WithdrawTimber(timberLoss);
        }

        string facilityLog = string.Empty;
        try
        {
            VillageInfrastructureEngine infra = VillageInfrastructureEngine.EnsureInstance();
            facilityLog = infra.ApplyInvasionDamage(facilityDamage, housingAndWorkshopOnly: false, KindLabel(kind));
        }
        catch (Exception exception)
        {
            facilityLog = $"施設 Safe-Fail: {exception.Message}";
        }

        GeographicDisasterEvent evt = new GeographicDisasterEvent
        {
            kind = kind,
            phase = phase,
            dayOfYear = dayOfYear,
            barrierDelta = barrierBefore - (barrier?.Efficiency ?? 0f),
            foodLoss = foodLoss,
            timberLoss = timberLoss,
            facilityDamage = facilityDamage,
            reason = reason,
            message =
                $"{KindLabel(kind)}が発生！ 理由: {reason} " +
                $"結界維持率-{barrierDrop:F0}% 食料-{foodLoss:F0} 木材-{timberLoss:F0} " +
                $"天候={currentWeather} {facilityLog}"
        };

        recentEvents.Add(evt);
        if (recentEvents.Count > 24)
        {
            recentEvents.RemoveAt(0);
        }

        Debug.Log(
            $"<color=#FF8A65><b>{LogTag}</b></color> [{KindLabel(kind)}]が発生！ " +
            $"理由: {reason} 結界維持率-{barrierDrop:F0}% " +
            $"({barrierBefore:F1}%→{barrier?.Efficiency:F1}%) " +
            $"食料-{foodLoss:F0} 木材-{timberLoss:F0} 天候={currentWeather} " +
            $"位相{phase}" + (dayOfYear > 0 ? $" 日{dayOfYear}" : string.Empty));
    }

    private static string KindLabel(GeographicDisasterKind kind)
    {
        switch (kind)
        {
            case GeographicDisasterKind.Flood:
                return "洪水";
            case GeographicDisasterKind.Eruption:
                return "噴火";
            case GeographicDisasterKind.Landslide:
                return "土砂崩れ";
            case GeographicDisasterKind.Earthquake:
                return "地震";
            default:
                return kind.ToString();
        }
    }

    private static Terrain ResolveTerrain()
    {
        ProceduralMapPopulator populator = UnityEngine.Object.FindAnyObjectByType<ProceduralMapPopulator>();
        if (populator != null && populator.BoundTerrain != null)
        {
            return populator.BoundTerrain;
        }

        return Terrain.activeTerrain;
    }

    /// <summary>検証: 地形判定 + 強制天候で災害発火。</summary>
    public static GeographicDisasterVerifyResult RunVerification()
    {
        GeographicDisasterVerifyResult verify = new GeographicDisasterVerifyResult();
        StringBuilder log = new StringBuilder();

        try
        {
            GeographicNaturalDisasterEngine engine = EnsureInstance();
            engine.RefreshFeatureFlags();
            engine.heavyRainStreak = 0;
            engine.crustStress = 0.75f;
            engine.recentEvents.Clear();

            GeographicFeatureFlags flags = engine.FeatureFlags;
            log.AppendLine($"features: {flags.Summary}");
            log.AppendLine(
                $"  water={flags.HasWaterBody} volcanic={flags.HasVolcanicFeature} slope={flags.HasSteepSlope}");

            VillageAutonomyEngine village = VillageAutonomyEngine.EnsureInstance();
            VillageBarrierCore barrier = village.Barrier;
            VillageStorageMarket market = NpcCivilizationEngine.EnsureInstance().Storage;
            NatureEnvironmentStatus nature = NaturalEcologyEngine.EnsureInstance().Environment;
            nature.ManaEcologyLevel = 72f;

            float barrierStart = barrier.Efficiency;

            engine.currentWeather = GeographicWeather.HeavyRain;
            engine.heavyRainStreak = 2;
            engine.TryTriggerFlood(6, 32, market, barrier);
            bool floodOk = !flags.HasWaterBody || engine.recentEvents.Exists(e => e.kind == GeographicDisasterKind.Flood);
            log.AppendLine($"flood: fired={floodOk} skip-ok={!flags.HasWaterBody || floodOk}");

            engine.currentWeather = GeographicWeather.Ashfall;
            engine.TryTriggerEruption(12, 145, market, barrier);
            bool eruptionOk = !flags.HasVolcanicFeature ||
                              engine.recentEvents.Exists(e => e.kind == GeographicDisasterKind.Eruption);
            log.AppendLine($"eruption: fired={eruptionOk}");

            engine.earthquakeThisPhase = true;
            engine.currentWeather = GeographicWeather.HeavyRain;
            engine.TryTriggerLandslide(14, 200, market, barrier);
            bool slideOk = !flags.HasSteepSlope ||
                           engine.recentEvents.Exists(e => e.kind == GeographicDisasterKind.Landslide);
            log.AppendLine($"landslide: fired={slideOk}");

            engine.earthquakeThisPhase = false;
            for (int p = 1; p <= 40 && !engine.recentEvents.Exists(e => e.kind == GeographicDisasterKind.Earthquake); p++)
            {
                engine.TryTriggerEarthquake(p, p * 9, market, barrier);
            }

            bool quakeOk = engine.recentEvents.Exists(e => e.kind == GeographicDisasterKind.Earthquake);
            log.AppendLine($"earthquake: fired={quakeOk} barrier={barrierStart:F1}->{barrier.Efficiency:F1}");

            bool safeSkip = true;
            bool savedWater = flags.HasWaterBody;
            engine.featureFlags.HasWaterBody = false;
            engine.heavyRainStreak = 5;
            int floodCountBefore = engine.recentEvents.FindAll(e => e.kind == GeographicDisasterKind.Flood).Count;
            engine.TryTriggerFlood(99, 99, market, barrier);
            int floodCountAfter = engine.recentEvents.FindAll(e => e.kind == GeographicDisasterKind.Flood).Count;
            safeSkip = floodCountAfter == floodCountBefore;
            engine.featureFlags.HasWaterBody = savedWater;

            log.AppendLine($"safe-skip-no-water: {safeSkip}");

            verify.success = floodOk && eruptionOk && slideOk && quakeOk && safeSkip;
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

    private static void WriteVerifyLog(GeographicDisasterVerifyResult verify)
    {
        try
        {
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
            string path = Path.Combine(projectRoot, "Logs", "geographic_disaster_verify.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? projectRoot);
            File.WriteAllText(
                path,
                $"{DateTime.UtcNow:O} success={verify.success}\n{verify.message}\n",
                Encoding.UTF8);
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[GeographicNaturalDisasterEngine] 検証ログスキップ: {exception.Message}");
        }
    }
}

public static class GeographicNaturalDisasterBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoAttach()
    {
        GeographicNaturalDisasterEngine.EnsureInstance();
    }
}

#if UNITY_EDITOR
public static class GeographicNaturalDisasterMenu
{
    [MenuItem("Tools/Procedural Map/Verify Geographic Natural Disasters")]
    public static void VerifyFromMenu()
    {
        GeographicDisasterVerifyResult result = GeographicNaturalDisasterEngine.RunVerification();
        Debug.Log(
            result.success
                ? $"<color=#A5D6A7><b>【地理自然災害検証】PASS</b></color>\n{result.message}"
                : $"<color=#FF8A80><b>【地理自然災害検証】FAIL</b></color>\n{result.message}");
        EditorUtility.DisplayDialog("Geographic Natural Disasters", result.message, "OK");
    }

    /// <summary>Unity バッチ: -executeMethod GeographicNaturalDisasterMenu.BatchVerifyAndQuit</summary>
    public static void BatchVerifyAndQuit()
    {
        GeographicDisasterVerifyResult result = GeographicNaturalDisasterEngine.RunVerification();
        Debug.Log(
            result.success
                ? $"<color=#A5D6A7><b>【地理自然災害検証】PASS</b></color>\n{result.message}"
                : $"<color=#FF8A80><b>【地理自然災害検証】FAIL</b></color>\n{result.message}");
        EditorApplication.Exit(result.success ? 0 : 1);
    }
}
#endif

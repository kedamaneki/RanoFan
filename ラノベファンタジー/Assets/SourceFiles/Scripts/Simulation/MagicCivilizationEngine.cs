using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

// =============================================================================
// 異端魔導 — 民生化・失伝（古代魔法化）400年周期推移
// 連携: EnvironmentBiorhythmEngine / EraContextResolver / HistoryFlagRegistry
// =============================================================================

/// <summary>異端魔導技術の文明化状態。</summary>
[Serializable]
public sealed class HereticMagicTechState
{
    public string techId = string.Empty;
    public string branchId = string.Empty;
    public string displayLabel = string.Empty;
    public int canonizedTurn = 200;
    public bool isCivilized;
    public bool isLostTech;
    public string civilizationFlag = string.Empty;
    public string lostTechFlag = string.Empty;
}

/// <summary>1 ターンの推移結果。</summary>
public sealed class MagicCivilizationTickResult
{
    public bool success;
    public int civilizedCount;
    public int lostTechCount;
    public string message = string.Empty;
}

/// <summary>
/// 第4〜5世代で正統化した異端魔導 (HERETIC_ARCANA / HERETIC_ORTHODOXY) の
/// 400年周期平穏期における民生化・失伝ルールを管理します。
/// </summary>
[DefaultExecutionOrder(47)]
public class MagicCivilizationEngine : MonoBehaviour
{
    public const string LogTag = "【異端魔導文明化】";
    public const string LostTechCycleLogTag = "【400年周期・技術失伝発火】";
    public const string HereticArcanaTechId = "MAGIC_HERETIC_ARCANA";
    public const string HereticOrthodoxyTechId = "MAGIC_HERETIC_ORTHODOXY";
    public const string CivilizedSuffix = "_CIVILIZED";
    public const string LostTechSuffix = "_IS_LOST_TECH";
    public const int DormantCivilizationMinTurn = 251;
    /// <summary>休眠期長期平穏における失伝閾値（PositionInCycle）。</summary>
    public const int LostTechDormantPositionThreshold = 280;
    /// <summary>Rising / Peak および前線放棄連動の失伝閾値（PositionInCycle）。</summary>
    public const int LostTechRisingPositionThreshold = 250;
    public const float FrontlineAbandonmentThreshold = 0.35f;

    public static MagicCivilizationEngine Instance { get; private set; }

    [SerializeField] private List<HereticMagicTechState> hereticTechs = new List<HereticMagicTechState>();
    [SerializeField] private int hereticTechnicianDeathCount;
    [SerializeField] private bool frontlineFacilityAbandoned;
    [SerializeField] private int lastProcessedTurn;

    public IReadOnlyList<HereticMagicTechState> HereticTechs => hereticTechs;

    public static MagicCivilizationEngine EnsureInstance()
    {
        if (Instance != null)
        {
            return Instance;
        }

        MagicCivilizationEngine existing = UnityEngine.Object.FindAnyObjectByType<MagicCivilizationEngine>();
        if (existing != null)
        {
            Instance = existing;
            return existing;
        }

        GameObject hub = GameObject.Find("DebugSystemsHub");
        GameObject host = hub != null ? hub : new GameObject(nameof(MagicCivilizationEngine));
        MagicCivilizationEngine engine = host.GetComponent<MagicCivilizationEngine>();
        return engine != null ? engine : host.AddComponent<MagicCivilizationEngine>();
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

    /// <summary>250年正史幹から Gen4/Gen5 異端魔導をバインドします。</summary>
    public static int BindHereticTechFromCanonLineage(IReadOnlyList<CanonTimelineLineageNode> lineage)
    {
        MagicCivilizationEngine engine = EnsureInstance();
        engine.hereticTechs.Clear();

        if (lineage == null || lineage.Count == 0)
        {
            engine.hereticTechs.Add(BuildDefaultState(
                HereticArcanaTechId,
                TimelineGenerationLoopEngine.ManualGen4BranchId,
                "異端魔導の覚醒",
                200));
            engine.hereticTechs.Add(BuildDefaultState(
                HereticOrthodoxyTechId,
                TimelineGenerationLoopEngine.ManualGen5BranchId,
                "異端魔法の正統化",
                250));
            return engine.hereticTechs.Count;
        }

        for (int i = 0; i < lineage.Count; i++)
        {
            CanonTimelineLineageNode node = lineage[i];
            if (node == null || string.IsNullOrWhiteSpace(node.branchId))
            {
                continue;
            }

            if (node.branchId.IndexOf("HERETIC_ARCANA", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                engine.hereticTechs.Add(BuildDefaultState(
                    HereticArcanaTechId,
                    node.branchId,
                    node.label,
                    node.committedAtTurn > 0 ? node.committedAtTurn : 200));
            }
            else if (node.branchId.IndexOf("HERETIC_ORTHODOXY", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                engine.hereticTechs.Add(BuildDefaultState(
                    HereticOrthodoxyTechId,
                    node.branchId,
                    node.label,
                    node.committedAtTurn > 0 ? node.committedAtTurn : 250));
            }
        }

        if (engine.hereticTechs.Count == 0)
        {
            return BindHereticTechFromCanonLineage(null);
        }

        return engine.hereticTechs.Count;
    }

    /// <summary>400年周期の休眠期における民生化/失伝ルールを適用します。</summary>
    public static MagicCivilizationTickResult TickCivilizationRules(int turn)
    {
        MagicCivilizationTickResult result = new MagicCivilizationTickResult { success = true };
        try
        {
            MagicCivilizationEngine engine = EnsureInstance();
            engine.lastProcessedTurn = turn;
            if (engine.hereticTechs == null || engine.hereticTechs.Count == 0)
            {
                BindHereticTechFromCanonLineage(null);
            }

            EnvironmentBiorhythmSnapshot biorhythm = EnvironmentBiorhythmEngine.ResolveMidCyclePhase(turn);
            bool dormant = biorhythm.Phase == EnvironmentMidCyclePhase.Dormant;
            EraTag era = EraContextResolver.ResolveEraTag(turn);
            HistoryFlagRegistry.EnsureWired();

            for (int i = 0; i < engine.hereticTechs.Count; i++)
            {
                HereticMagicTechState tech = engine.hereticTechs[i];
                if (tech == null || tech.isLostTech)
                {
                    continue;
                }

                if (dormant &&
                    turn >= DormantCivilizationMinTurn &&
                    turn > tech.canonizedTurn &&
                    !tech.isCivilized)
                {
                    ApplyCivilization(engine, tech, turn);
                    result.civilizedCount++;
                }

                if (tech.isCivilized && !tech.isLostTech)
                {
                    TryApplyLostAncientTech(
                        engine,
                        tech,
                        turn,
                        era,
                        biorhythm,
                        ref result.lostTechCount);
                }
            }

            result.message =
                $"T{turn} phase={biorhythm.Phase} civilized={result.civilizedCount} " +
                $"lost={result.lostTechCount} techs={engine.hereticTechs.Count}";
            return result;
        }
        catch (Exception exception)
        {
            result.success = false;
            result.message = $"Safe-Fail: {exception.Message}";
            Debug.LogWarning($"[MagicCivilizationEngine] {result.message}");
            return result;
        }
    }

    public static void NotifyHereticTechnicianDeath(string npcId = null, int? turnOverride = null)
    {
        MagicCivilizationEngine engine = EnsureInstance();
        engine.hereticTechnicianDeathCount++;
        Debug.Log(
            $"<color=#FFD54F><b>{LogTag}</b></color> 異端魔導技術者の喪失 " +
            $"(count={engine.hereticTechnicianDeathCount} id={npcId ?? "—"})");
        TryApplyImmediateLostTechFromDeath(turnOverride);
    }

    public static void NotifyFrontlineFacilityAbandoned(int nationId, float occupancyRatio)
    {
        MagicCivilizationEngine engine = EnsureInstance();
        if (occupancyRatio <= FrontlineAbandonmentThreshold)
        {
            engine.frontlineFacilityAbandoned = true;
            Debug.Log(
                $"<color=#FFD54F><b>{LogTag}</b></color> 前線施設廃絶 国家{nationId:000} " +
                $"occupancy={occupancyRatio.ToString("F2", CultureInfo.InvariantCulture)}");
        }
    }

    public static void ResetForVerification()
    {
        MagicCivilizationEngine engine = EnsureInstance();
        engine.hereticTechs.Clear();
        engine.hereticTechnicianDeathCount = 0;
        engine.frontlineFacilityAbandoned = false;
        engine.lastProcessedTurn = 0;
    }

    private static HereticMagicTechState BuildDefaultState(
        string techId,
        string branchId,
        string label,
        int canonTurn)
    {
        return new HereticMagicTechState
        {
            techId = techId,
            branchId = branchId,
            displayLabel = label ?? techId,
            canonizedTurn = canonTurn,
            civilizationFlag = techId + CivilizedSuffix,
            lostTechFlag = techId + LostTechSuffix
        };
    }

    private static void ApplyCivilization(
        MagicCivilizationEngine engine,
        HereticMagicTechState tech,
        int turn)
    {
        tech.isCivilized = true;
        HistoryFlagRegistry.Unlock(tech.civilizationFlag);
        HistoryFlagRegistry.Unlock(DynamicMasterGenerationEngine.MagicForgeSpirit);
        HistoryFlagRegistry.Unlock(DynamicMasterGenerationEngine.MagicBarrierRestore);
        Debug.Log(
            $"<color=#A5D6A7><b>{LogTag}</b></color> T{turn} {tech.displayLabel} → 民生化/インフラ化 " +
            $"(過熱高炉・結界増幅触媒) flag={tech.civilizationFlag}");
    }

    private static void TryApplyLostAncientTech(
        MagicCivilizationEngine engine,
        HereticMagicTechState tech,
        int turn,
        EraTag era,
        EnvironmentBiorhythmSnapshot biorhythm,
        ref int lostTechCount)
    {
        if (engine == null || tech == null || tech.isLostTech || !tech.isCivilized)
        {
            return;
        }

        bool technicianGone = engine.hereticTechnicianDeathCount > 0;
        bool frontlineGone = engine.frontlineFacilityAbandoned;
        bool risingOrPeak = biorhythm.Phase == EnvironmentMidCyclePhase.Rising ||
                            biorhythm.Phase == EnvironmentMidCyclePhase.PeakCatastrophe;
        bool risingPositionReady = biorhythm.PositionInCycle >= LostTechRisingPositionThreshold;
        bool dormantLongPeace = biorhythm.Phase == EnvironmentMidCyclePhase.Dormant &&
                                biorhythm.PositionInCycle >= LostTechDormantPositionThreshold;

        bool shouldLose = false;
        if (technicianGone)
        {
            shouldLose = true;
        }
        else if (frontlineGone && (risingOrPeak || risingPositionReady))
        {
            shouldLose = true;
        }
        else if (dormantLongPeace && frontlineGone)
        {
            shouldLose = true;
        }

        if (!shouldLose)
        {
            return;
        }

        ApplyLostAncientTech(engine, tech, turn, era);
        lostTechCount++;
    }

    private static void TryApplyImmediateLostTechFromDeath(int? turnOverride = null)
    {
        try
        {
            MagicCivilizationEngine engine = EnsureInstance();
            if (engine.hereticTechs == null || engine.hereticTechs.Count == 0)
            {
                return;
            }

            int turn = turnOverride ?? engine.lastProcessedTurn;
            if (turn <= 0)
            {
                turn = TimelineScriptEvaluator.ResolveCurrentEvaluationTurn();
            }

            EraTag era = EraContextResolver.ResolveEraTag(turn);
            int lost = 0;
            for (int i = 0; i < engine.hereticTechs.Count; i++)
            {
                HereticMagicTechState tech = engine.hereticTechs[i];
                if (tech == null || !tech.isCivilized || tech.isLostTech)
                {
                    continue;
                }

                ApplyLostAncientTech(engine, tech, turn, era);
                lost++;
            }

            if (lost > 0)
            {
                Debug.Log(
                    $"<color=#BCAAA4><b>{LostTechCycleLogTag}</b></color> " +
                    $"ターン{turn}: 技術者喪失により {lost} 系統が即時失伝しました。");
            }
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[MagicCivilizationEngine] TryApplyImmediateLostTechFromDeath Safe-Fail: {exception.Message}");
        }
    }

    private static void ApplyLostAncientTech(
        MagicCivilizationEngine engine,
        HereticMagicTechState tech,
        int turn,
        EraTag era)
    {
        tech.isLostTech = true;
        HistoryFlagRegistry.Unlock(tech.lostTechFlag);
        if (era == EraTag.Late || turn > EraContextResolver.MaxTurn)
        {
            HistoryFlagRegistry.Unlock(EraContextResolver.EraAncientFlag);
        }

        Debug.Log(
            $"<color=#BCAAA4><b>{LostTechCycleLogTag}</b></color> " +
            $"ターン{turn}: 異端魔導 {tech.techId} が失伝し、古代魔法(IsLostTech)へ移行しました。");
    }
}

public static class MagicCivilizationEngineBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoAttach()
    {
        MagicCivilizationEngine.EnsureInstance();
    }
}

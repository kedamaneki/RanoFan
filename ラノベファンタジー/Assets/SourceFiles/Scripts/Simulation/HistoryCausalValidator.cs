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
// 歴史的因果律検証 — 対魔獣・対人間イベントの時代整合チェック
// =============================================================================

/// <summary>検証対象イベントの提案。</summary>
[Serializable]
public sealed class CausalEventProposal
{
    public string sourceEngine = string.Empty;
    public int turn = 1;
    public int phase;
    public VariableTimelineSeason season;
    public string eventCode = string.Empty;
    public string title = string.Empty;
    public string narrative = string.Empty;
    public string historyFlag = string.Empty;
    public string secondaryFlag = string.Empty;
    public float barrierPercent = 100f;
    public float foodScarcity = 0f;
    public float factionTension = 0f;
    public float techMonopolyLevel = 0f;
    public bool requiresCivilPrecursor = false;
    public bool requiresAdvancedEraTech = false;
    public bool impliesCityDestruction = false;
}

/// <summary>因果律検証結果。</summary>
[Serializable]
public sealed class CausalValidationResult
{
    public bool accepted;
    public string reason = string.Empty;
}

/// <summary>位相ごとの前兆ステータススナップショット。</summary>
[Serializable]
public sealed class PhasePrecursorSnapshot
{
    public int phase;
    public float foodScarcity;
    public float factionTension;
    public float barrierPercent;
    public float techMonopolyLevel;
}

/// <summary>検証サマリー。</summary>
[Serializable]
public sealed class HistoryCausalVerifyResult
{
    public bool success;
    public string message = string.Empty;
    public int accepted;
    public int rejected;
}

/// <summary>
/// 発火前に時代タグ・歴史フラグ・前兆ステータスを検証し、
/// 整合したイベントのみ歴史フラグ連鎖を許可します。
/// </summary>
[DefaultExecutionOrder(46)]
public class HistoryCausalValidator : MonoBehaviour
{
    public const string LogTag = "【因果律整合イベント】";
    public const string FallbackLogTag = "【因果律・平穏継続】";
    public const float CivilPrecursorThreshold = 50f;
    public const float StrongDefenseBarrierPercent = 80f;
    public const float ImmediateFoodScarcityThreshold = 70f;

    public static HistoryCausalValidator Instance { get; private set; }

    [SerializeField] private int trackedTurn = MicroHistoryTimelineTimelineEngine.DefaultTurn;
    [SerializeField] private List<PhasePrecursorSnapshot> phaseHistory = new List<PhasePrecursorSnapshot>();
    [SerializeField] private int acceptedEventCount = 0;
    [SerializeField] private int rejectedEventCount = 0;

    public int AcceptedEventCount => acceptedEventCount;
    public int RejectedEventCount => rejectedEventCount;

    public static HistoryCausalValidator EnsureInstance()
    {
        if (Instance != null)
        {
            return Instance;
        }

        HistoryCausalValidator existing = FindAnyObjectByType<HistoryCausalValidator>();
        if (existing != null)
        {
            Instance = existing;
            return existing;
        }

        GameObject hub = GameObject.Find("DebugSystemsHub");
        GameObject host = hub != null ? hub : new GameObject(nameof(HistoryCausalValidator));
        HistoryCausalValidator validator = host.GetComponent<HistoryCausalValidator>();
        return validator != null ? validator : host.AddComponent<HistoryCausalValidator>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        ResetYearTracking();
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    public void ResetYearTracking()
    {
        phaseHistory.Clear();
        acceptedEventCount = 0;
        rejectedEventCount = 0;
        trackedTurn = EraContextResolver.CurrentTurn;
        HistoryFlagRegistry.EnsureWired();
    }

    public static void TryRecordPhaseSnapshot(
        int phase,
        HumanSocietyStatus society,
        float barrierPercent)
    {
        try
        {
            HistoryCausalValidator validator = EnsureInstance();
            validator.RecordPhaseSnapshot(phase, society, barrierPercent);
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[HistoryCausalValidator] Snapshot Safe-Fail: {exception.Message}");
        }
    }

    public void RecordPhaseSnapshot(int phase, HumanSocietyStatus society, float barrierPercent)
    {
        int normalized = ProceduralMapPopulator.NormalizePhase(phase);
        PhasePrecursorSnapshot snap = new PhasePrecursorSnapshot
        {
            phase = normalized,
            foodScarcity = society?.foodScarcity ?? 0f,
            factionTension = society?.factionTension ?? 0f,
            barrierPercent = barrierPercent,
            techMonopolyLevel = society?.techMonopolyLevel ?? 0f
        };

        for (int i = phaseHistory.Count - 1; i >= 0; i--)
        {
            if (phaseHistory[i]?.phase == normalized)
            {
                phaseHistory.RemoveAt(i);
            }
        }

        phaseHistory.Add(snap);
    }

    /// <summary>因果律チェックを通過した場合のみ歴史フラグを発行しログ出力します。</summary>
    public static bool TryEmitValidatedEvent(CausalEventProposal proposal)
    {
        try
        {
            HistoryCausalValidator validator = EnsureInstance();
            return validator.EmitValidatedEvent(proposal);
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[HistoryCausalValidator] Emit Safe-Fail: {exception.Message}");
            LogPeacefulFallback(proposal, "検証例外フォールバック");
            return false;
        }
    }

    public bool EmitValidatedEvent(CausalEventProposal proposal)
    {
        if (proposal == null)
        {
            LogPeacefulFallback(null, "提案null");
            rejectedEventCount++;
            return false;
        }

        CausalValidationResult validation = Validate(proposal);
        if (!validation.accepted)
        {
            rejectedEventCount++;
            LogPeacefulFallback(proposal, validation.reason);
            return false;
        }

        if (!string.IsNullOrWhiteSpace(proposal.historyFlag))
        {
            HistoryFlagRegistry.Unlock(proposal.historyFlag);
        }

        if (!string.IsNullOrWhiteSpace(proposal.secondaryFlag))
        {
            HistoryFlagRegistry.Unlock(proposal.secondaryFlag);
        }

        acceptedEventCount++;
        Debug.Log(
            $"<color=#81C784><b>{LogTag}</b></color> " +
            $"位相{proposal.phase}/24 {proposal.season.ToString().ToUpperInvariant()} " +
            $"[{proposal.title}] ({proposal.eventCode}) {proposal.narrative} " +
            $"FLAG={proposal.historyFlag} ERA={EraContextResolver.FormatEraLabel(EraContextResolver.CurrentEra)} " +
            $"結界{proposal.barrierPercent.ToString("F1", CultureInfo.InvariantCulture)}% " +
            $"出典={proposal.sourceEngine}");
        DynamicMasterGenerationEngine.NotifyFromCausalProposal(proposal);
        return true;
    }

    public CausalValidationResult Validate(CausalEventProposal proposal)
    {
        CausalValidationResult result = new CausalValidationResult { accepted = true };

        if (proposal.requiresAdvancedEraTech && !IsAdvancedEraTechAllowed(proposal))
        {
            result.accepted = false;
            result.reason = $"時代技術不整合（{EraContextResolver.FormatEraLabel(EraContextResolver.CurrentEra)}）";
            return result;
        }

        if (proposal.impliesCityDestruction && proposal.barrierPercent > StrongDefenseBarrierPercent)
        {
            result.accepted = false;
            result.reason =
                $"被害ログ不整合（結界{proposal.barrierPercent:F1}%>80%で都市全滅系イベント不可）";
            return result;
        }

        if (proposal.requiresCivilPrecursor && !HasCivilUnrestPrecursor(proposal))
        {
            result.accepted = false;
            result.reason = "因果前兆不足（直前位相 FoodScarcity/FactionTension 未達）";
            return result;
        }

        if (ImpliesOverpoweredWeapon(proposal) && EraContextResolver.CurrentEra == EraTag.Early)
        {
            result.accepted = false;
            result.reason = "時代技術不整合（普遍期に高度魔導兵器戦は未解禁）";
            return result;
        }

        return result;
    }

    private static bool IsAdvancedEraTechAllowed(CausalEventProposal proposal)
    {
        EraTag era = EraContextResolver.CurrentEra;
        if (era == EraTag.Mid || era == EraTag.Late)
        {
            return true;
        }

        if (HistoryFlagRegistry.IsUnlocked(MicroToMacroAggregator.FlagInnovation))
        {
            return true;
        }

        if (proposal.techMonopolyLevel >= 55f)
        {
            return true;
        }

        return false;
    }

    private bool HasCivilUnrestPrecursor(CausalEventProposal proposal)
    {
        if (proposal.foodScarcity >= ImmediateFoodScarcityThreshold)
        {
            return true;
        }

        PhasePrecursorSnapshot prior = FindSnapshot(proposal.phase - 1);
        if (prior != null)
        {
            if (prior.foodScarcity > CivilPrecursorThreshold || prior.factionTension > CivilPrecursorThreshold)
            {
                return true;
            }
        }

        if (proposal.factionTension > CivilPrecursorThreshold + 10f)
        {
            return true;
        }

        return false;
    }

    private static bool ImpliesOverpoweredWeapon(CausalEventProposal proposal)
    {
        string blob = (proposal.eventCode ?? string.Empty) + proposal.title + proposal.narrative;
        return blob.Contains("高度魔導兵器") ||
               blob.Contains("古代魔導兵器") ||
               blob.Contains("魔導兵器戦");
    }

    private PhasePrecursorSnapshot FindSnapshot(int phase)
    {
        for (int i = phaseHistory.Count - 1; i >= 0; i--)
        {
            PhasePrecursorSnapshot snap = phaseHistory[i];
            if (snap != null && snap.phase == phase)
            {
                return snap;
            }
        }

        return null;
    }

    private static void LogPeacefulFallback(CausalEventProposal proposal, string reason)
    {
        int phase = proposal?.phase ?? 0;
        VariableTimelineSeason season = proposal?.season ?? VariableTimelineSeason.Active;
        Debug.Log(
            $"<color=#B0BEC5><b>{FallbackLogTag}</b></color> " +
            $"位相{phase}/24 {season.ToString().ToUpperInvariant()} " +
            $"拒絶={reason} — 日常生産・防衛維持を継続");
    }

    public static CausalEventProposal BuildFromSideQuest(SideQuestFlag flag, VariableTimelineSeason season)
    {
        if (flag == null)
        {
            return null;
        }

        CausalEventProposal proposal = new CausalEventProposal
        {
            sourceEngine = nameof(StoryTimelineManager),
            turn = flag.turn,
            phase = flag.phase,
            season = season,
            eventCode = flag.dramaKind.ToString(),
            title = flag.dramaTitle,
            narrative = flag.narrative,
            historyFlag = flag.flagKey,
            barrierPercent = ResolveCurrentBarrierPercent(),
            impliesCityDestruction = flag.narrative.Contains("全滅") || flag.narrative.Contains("崩壊")
        };

        switch (flag.dramaKind)
        {
            case SideQuestDramaKind.AncientTechAwakening:
            case SideQuestDramaKind.OverforgeDiscovery:
                proposal.requiresAdvancedEraTech = true;
                break;
            case SideQuestDramaKind.EmergencySupply:
            case SideQuestDramaKind.VictimSupport:
                proposal.requiresCivilPrecursor = true;
                break;
        }

        return proposal;
    }

    public static CausalEventProposal BuildFromHumanConflict(
        HumanConflictEventLog entry,
        HumanSocietyStatus society,
        float barrierPercent,
        int turn)
    {
        if (entry == null)
        {
            return null;
        }

        CausalEventProposal proposal = new CausalEventProposal
        {
            sourceEngine = nameof(HumanConflictEngine),
            turn = turn,
            phase = entry.phase,
            eventCode = entry.eventCode,
            title = entry.title,
            narrative = entry.narrative,
            historyFlag = entry.historyFlag,
            secondaryFlag = entry.jobUnlockHint,
            barrierPercent = barrierPercent,
            foodScarcity = society?.foodScarcity ?? 0f,
            factionTension = society?.factionTension ?? 0f,
            techMonopolyLevel = society?.techMonopolyLevel ?? 0f
        };

        switch (entry.kind)
        {
            case HumanConflictEventKind.CivilWarFoodRiot:
                proposal.requiresCivilPrecursor = true;
                break;
            case HumanConflictEventKind.SuccessionCrisisBarrier:
                proposal.requiresAdvancedEraTech = true;
                break;
            case HumanConflictEventKind.BorderResourceWar:
                break;
        }

        return proposal;
    }

    private static float ResolveCurrentBarrierPercent()
    {
        try
        {
            return VillageAutonomyEngine.EnsureInstance().Barrier.Efficiency;
        }
        catch (Exception)
        {
            return 100f;
        }
    }

    public static HistoryCausalVerifyResult RunDayVerification()
    {
        HistoryCausalVerifyResult result = new HistoryCausalVerifyResult();
        try
        {
            HistoryCausalValidator validator = EnsureInstance();
            validator.ResetYearTracking();

            NpcCivilizationEngine civ = NpcCivilizationEngine.EnsureInstance();
            VariablePhaseDistribution dist = ProceduralMapPopulator.DefaultNation001Turn1Distribution();
            MicroToMacroAggregator.EnsureInstance().BeginYearBaseline();
            WestRegionSimulationManager.EnsureInstance().ResetWestRegionYear();
            StoryTimelineManager.EnsureInstance().TryBootstrapStoryYear(1, GameMode.StoryMode);

            for (int phase = 1; phase <= ProceduralMapPopulator.PhaseCount; phase++)
            {
                VariableTimelineSeason season = ProceduralMapPopulator.ResolveSeason(phase, dist);
                civ.TickPhase(phase, season);
            }

            result.accepted = validator.AcceptedEventCount;
            result.rejected = validator.RejectedEventCount;
            result.success = result.accepted >= 3 &&
                             validator.GetLastRecordedPhase() >= ProceduralMapPopulator.PhaseCount;
            result.message =
                $"accepted={result.accepted} rejected={result.rejected} phase={validator.GetLastRecordedPhase()}";
        }
        catch (Exception exception)
        {
            result.success = false;
            result.message = $"Safe-Fail: {exception.Message}";
        }

        return result;
    }

    public int GetLastRecordedPhase()
    {
        try
        {
            int civPhase = NpcCivilizationEngine.EnsureInstance().LastTickedPhase;
            if (civPhase > 0)
            {
                return civPhase;
            }
        }
        catch (Exception)
        {
            // Safe-Fail
        }

        return lastStoryPhaseFromHistory();
    }

    private int lastStoryPhaseFromHistory()
    {
        int max = 0;
        for (int i = 0; i < phaseHistory.Count; i++)
        {
            if (phaseHistory[i] != null && phaseHistory[i].phase > max)
            {
                max = phaseHistory[i].phase;
            }
        }

        return max;
    }
}

public static class HistoryCausalValidatorBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoAttach()
    {
        HistoryCausalValidator.EnsureInstance();
    }
}

#if UNITY_EDITOR
public static class HistoryCausalValidatorMenu
{
    private const string VerifyLogPath = "Logs/history_causal_verify.txt";

    [MenuItem("Tools/Procedural Map/Run Causal Validator Day")]
    public static void RunCausalValidatorDay()
    {
        HistoryCausalVerifyResult result = HistoryCausalValidator.RunDayVerification();
        try
        {
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
            string path = Path.Combine(projectRoot, VerifyLogPath);
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? projectRoot);
            File.WriteAllText(path, result.message + "\n", Encoding.UTF8);
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[HistoryCausalValidator] 検証ログをスキップ: {exception.Message}");
        }

        Debug.Log(
            result.success
                ? $"<color=#81C784><b>{HistoryCausalValidator.LogTag}・検証】PASS</b></color> {result.message}"
                : $"<color=#FF8A80><b>{HistoryCausalValidator.LogTag}・検証】FAIL</b></color> {result.message}");
        EditorUtility.DisplayDialog("Causal Validator", result.message, "OK");
    }
}
#endif

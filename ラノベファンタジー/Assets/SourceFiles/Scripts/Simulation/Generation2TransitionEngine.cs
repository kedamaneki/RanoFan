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
// 第2世代移行 — T50 確定メイン歴史軸を引き継ぎ T51 へ進行・新主人公スロットイン
// 連携: TurnTransitionEngine / TimelineCharacterSelector / HistoryBranchManager
//       AutoBranchGenerator / HistoricalNpcCaster / HistoryFlagRegistry
// =============================================================================

/// <summary>第2世代移行結果。</summary>
public sealed class Generation2TransitionResult
{
    public bool success;
    public bool usedFallback;
    public int previousTurn = 50;
    public int newTurn = 51;
    public string mainStoryBranchId = string.Empty;
    public string mainStoryBranchName = string.Empty;
    public string protagonistNpcId = string.Empty;
    public string protagonistName = string.Empty;
    public string protagonistJobId = string.Empty;
    public float power;
    public float barrier;
    public int verifiedFlagCount;
    public string message = string.Empty;
}

/// <summary>検証結果。</summary>
public sealed class Generation2TransitionVerifyResult
{
    public bool success;
    public string message = string.Empty;
}

/// <summary>
/// 第1世代（T50）で確定したメイン歴史軸を引き継ぎ、第2世代（T51〜）へ進行し新主人公へ操作権を付与します。
/// </summary>
[DefaultExecutionOrder(46)]
public class Generation2TransitionEngine : MonoBehaviour
{
    public const string LogTag = "【第2世代開始】";
    public const int Generation1EndTurn = 50;
    public const int Generation2StartTurn = 51;
    public const int Generation2EndTurn = 100;
    public const int Generation3StartTurn = 101;
    public const int Generation3EndTurn = 150;
    public const int Generation4StartTurn = 151;
    public const int Generation4EndTurn = 200;
    public const int Generation5StartTurn = 201;
    public const int Generation5EndTurn = 250;
    public const int DefaultGeneration1PlanIndex = 2;
    public const int DefaultNationId = MicroToMacroAggregator.DefaultNationId;

    public static Generation2TransitionEngine Instance { get; private set; }

    public static Generation2TransitionEngine EnsureInstance()
    {
        if (Instance != null)
        {
            return Instance;
        }

        Generation2TransitionEngine existing = UnityEngine.Object.FindAnyObjectByType<Generation2TransitionEngine>();
        if (existing != null)
        {
            Instance = existing;
            return existing;
        }

        GameObject hub = GameObject.Find("DebugSystemsHub");
        GameObject host = hub != null ? hub : new GameObject(nameof(Generation2TransitionEngine));
        Generation2TransitionEngine engine = host.GetComponent<Generation2TransitionEngine>();
        return engine != null ? engine : host.AddComponent<Generation2TransitionEngine>();
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

    /// <summary>確定済みメイン歴史軸を引き継ぎ T51 へ進行し、新主人公へ操作権を付与します。</summary>
    public static Generation2TransitionResult BeginGeneration2(
        int fromTurn = Generation1EndTurn,
        int targetTurn = Generation2StartTurn)
    {
        Generation2TransitionResult result = new Generation2TransitionResult
        {
            previousTurn = Mathf.Clamp(fromTurn, EraContextResolver.MinTurn, EraContextResolver.MaxTurn),
            newTurn = Mathf.Clamp(targetTurn, EraContextResolver.MinTurn, EraContextResolver.MaxTurn)
        };

        try
        {
            Generation2TransitionEngine.EnsureInstance();
            TurnTransitionEngine.EnsureInstance();
            TimelineCharacterSelector.EnsureInstance();
            HistoryBranchManager mgr = HistoryBranchManager.EnsureInstance();
            HistoryFlagRegistry.EnsureWired();
            MasterDataManager.EnsureInstance();

            if (!mgr.HasCommittedMainStory)
            {
                result.usedFallback = true;
                AutoBranchGenerationResult fallbackPlan = AutoBranchGenerator.GenerateAndCommitPlan(
                    1,
                    result.previousTurn,
                    DefaultGeneration1PlanIndex);
                if (!fallbackPlan.success)
                {
                    result.message = "メイン歴史軸未確定かつ案2フォールバック生成に失敗";
                    return result;
                }
            }

            result.mainStoryBranchId = mgr.MainStoryBranchId;
            HistoryTimelineBranch mainBranch = mgr.FindRegisteredBranchForVerification(result.mainStoryBranchId);
            result.mainStoryBranchName = TimelineScriptEvaluator.ResolveBranchDisplayName(mainBranch);

            SyncToTurn(result.previousTurn, applyBranchAdjustments: true);

            int currentTurn = ResolveCurrentTurn();
            TurnTransitionResult transition = null;
            if (currentTurn < result.newTurn)
            {
                MicroToMacroAggregator aggregator = MicroToMacroAggregator.EnsureInstance();
                if (!aggregator.YearEndApplied)
                {
                    aggregator.AggregateYearEnd(logMacro: false);
                }

                transition = TurnTransitionEngine.AdvanceToNextYear(fromDailyCompletion: false);
                if (!transition.success || !transition.advanced || transition.newTurn != result.newTurn)
                {
                    DailySimulationEngine daily = DailySimulationEngine.EnsureInstance();
                    daily.TryPrepareForTurn(result.newTurn);
                    SyncToTurn(result.newTurn, applyBranchAdjustments: true);
                    HistoricalNpcCaster.SpawnHistoricalKeyCharacters(result.newTurn);
                    result.usedFallback = true;
                }
                else
                {
                    result.power = transition.power;
                    result.barrier = transition.barrier;
                }
            }
            else
            {
                DailySimulationEngine.EnsureInstance().TryPrepareForTurn(result.newTurn);
                SyncToTurn(result.newTurn, applyBranchAdjustments: true);
                HistoricalNpcCaster.SpawnHistoricalKeyCharacters(result.newTurn);
            }

            result.newTurn = ResolveCurrentTurn();
            MacroStats stats = MicroToMacroAggregator.EnsureInstance().Stats;
            HistoryBranchManager.TryApplyToMacroStats(stats, result.newTurn, DefaultNationId);
            result.power = stats.Power;
            result.barrier = stats.BarrierEfficiency;

            result.verifiedFlagCount = VerifyCommittedBranchFlags(mainBranch, result.newTurn, out string flagSummary);
            float canonBarrier = HistoryBranchManager.GetAdjustedBarrier(0.95f, result.newTurn, DefaultNationId);
            float canonPower = HistoryBranchManager.GetAdjustedPower(1000f, result.newTurn, DefaultNationId);
            bool macroInherited = result.barrier > 0.01f &&
                                  (Mathf.Abs(result.barrier - canonBarrier) > 0.001f ||
                                   Mathf.Abs(result.power - canonPower) > 1f ||
                                   mgr.IsMainStoryBaselineActive);

            TimelineCharacterSelector selector = TimelineCharacterSelector.EnsureInstance();
            bool possessed = selector.TryPossessGeneration2Protagonist(result.newTurn);
            NpcStatusManager protagonist = selector.PossessedNpc;
            if (protagonist != null)
            {
                result.protagonistNpcId = protagonist.NpcId;
                result.protagonistName = protagonist.DisplayName;
                result.protagonistJobId = protagonist.JobId;
            }
            else if (selector.IsUsingSafeFallback)
            {
                result.usedFallback = true;
                result.protagonistName = "標準仮アバター";
                result.protagonistJobId = DynamicJobBuilder.FallbackJobId;
            }

            result.success = result.newTurn >= Generation2StartTurn &&
                             !string.IsNullOrWhiteSpace(result.mainStoryBranchId) &&
                             macroInherited &&
                             possessed;
            result.message =
                $"turn={result.previousTurn}->{result.newTurn} branch={result.mainStoryBranchId} " +
                $"power={result.power.ToString("F0", CultureInfo.InvariantCulture)} " +
                $"barrier={result.barrier.ToString("F3", CultureInfo.InvariantCulture)} " +
                $"flags={result.verifiedFlagCount} {flagSummary} " +
                $"hero={result.protagonistName} job={result.protagonistJobId}";

            Debug.Log(
                $"<color=#A5D6A7><b>{LogTag}</b></color> " +
                $"ターン{result.newTurn} へ移行しました！ " +
                $"主人公: 「{result.protagonistName}」 ({result.protagonistJobId}) / " +
                $"確定歴史軸: 「{result.mainStoryBranchId}」");

            return result;
        }
        catch (Exception exception)
        {
            result.success = false;
            result.usedFallback = true;
            result.message = $"Safe-Fail: {exception.Message}";
            Debug.LogWarning($"[Generation2TransitionEngine] BeginGeneration2 Safe-Fail: {exception.Message}");

            try
            {
                TimelineCharacterSelector.EnsureInstance().FallbackToSafeAvatar(exception.Message);
            }
            catch (Exception fallbackException)
            {
                result.message += $" / fallback failed: {fallbackException.Message}";
            }

            return result;
        }
    }

    public static Generation2TransitionVerifyResult RunVerification()
    {
        Generation2TransitionVerifyResult verify = new Generation2TransitionVerifyResult();
        StringBuilder log = new StringBuilder();
        GameObject puppetObject = null;

        try
        {
            SimulationVerifyBootstrap.PrepareFreshStoryDay(1, GameMode.StoryMode);
            HistoryFlagRegistry.EnsureWired();
            HistoryFlagRegistry.ClearForTesting();
            HistoryBranchManager.EnsureInstance().ClearRegisteredBranches();
            MasterDataManager.EnsureInstance();

            AutoBranchGenerationResult plan = AutoBranchGenerator.GenerateAndCommitPlan(
                1,
                Generation1EndTurn,
                DefaultGeneration1PlanIndex);
            log.AppendLine(
                $"gen1-plan2: success={plan.success} branch={plan.committedBranchId} score={plan.bestScore}");

            TimelineCharacterSelector selector = TimelineCharacterSelector.EnsureInstance();
            puppetObject = new GameObject("Generation2TransitionVerifyPuppet");
            CombatStats puppetCombat = puppetObject.AddComponent<CombatStats>();
            PlayerStats puppetStamina = puppetObject.AddComponent<PlayerStats>();
            PlayerStatusManager puppetStatus = puppetObject.AddComponent<PlayerStatusManager>();
            selector.BindPlayerPuppet(puppetCombat, puppetStamina, puppetStatus);

            Generation2TransitionResult transition = BeginGeneration2(
                Generation1EndTurn,
                Generation2StartTurn);
            log.AppendLine($"transition: success={transition.success} {transition.message}");

            HistoryBranchManager mgr = HistoryBranchManager.EnsureInstance();
            bool turnPass = transition.newTurn == Generation2StartTurn;
            bool branchPass = mgr.HasCommittedMainStory &&
                              string.Equals(
                                  mgr.MainStoryBranchId,
                                  plan.committedBranchId,
                                  StringComparison.OrdinalIgnoreCase);
            bool eraPass = EraContextResolver.ResolveEraTag(Generation2StartTurn) == EraTag.Mid;

            HistoryTimelineBranch mainBranch = mgr.FindRegisteredBranchForVerification(mgr.MainStoryBranchId);
            int flagCount = VerifyCommittedBranchFlags(mainBranch, Generation2StartTurn, out string flagSummary);
            bool flagPass = flagCount > 0;
            log.AppendLine($"inherit: turn={turnPass} branch={branchPass} era={eraPass} flags={flagCount} {flagSummary}");

            NpcStatusManager protagonist = selector.PossessedNpc;
            bool possessPass = protagonist != null || selector.IsUsingSafeFallback;
            bool jobSync = protagonist != null &&
                           string.Equals(
                               puppetStatus.MainJob,
                               protagonist.JobId,
                               StringComparison.OrdinalIgnoreCase);
            bool hpSync = protagonist != null &&
                          puppetCombat.MaxHp == protagonist.Combat.MaxHp &&
                          puppetCombat.CurrentHp == protagonist.Combat.CurrentHp;
            bool staSync = protagonist != null &&
                           Mathf.Approximately(puppetStamina.maxStamina, protagonist.StaminaLayer.maxStamina);
            bool mpSync = protagonist != null &&
                          Mathf.Abs(puppetStatus.MaxMP - protagonist.MaxMP) < 1f;

            log.AppendLine(
                $"protagonist: possess={possessPass} name={transition.protagonistName} " +
                $"job={transition.protagonistJobId} hp={hpSync} sta={staSync} mp={mpSync} jobSync={jobSync}");

            verify.success = plan.success && transition.success && turnPass && branchPass &&
                             eraPass && flagPass && possessPass && jobSync && hpSync && staSync && mpSync;
            verify.message = log.ToString().TrimEnd();
        }
        catch (Exception exception)
        {
            verify.success = false;
            verify.message = $"Safe-Fail: {exception.Message}";
        }
        finally
        {
            if (puppetObject != null)
            {
#if UNITY_EDITOR
                if (!Application.isPlaying)
                {
                    UnityEngine.Object.DestroyImmediate(puppetObject);
                }
                else
#endif
                {
                    UnityEngine.Object.Destroy(puppetObject);
                }
            }
        }

        WriteVerifyLog(verify);
        return verify;
    }

    private static void SyncToTurn(int turn, bool applyBranchAdjustments)
    {
        EraContextResolver.TrySetCurrentTurn(turn);
        DailySimulationEngine daily = DailySimulationEngine.EnsureInstance();
        daily.TryPrepareForTurn(turn);
        MicroToMacroAggregator aggregator = MicroToMacroAggregator.EnsureInstance();
        aggregator.PrepareTurnTransition(turn, applyBranchAdjustments);
        aggregator.BeginYearBaseline();
        HistoryBranchManager.TryApplyToMacroStats(aggregator.Stats, turn, DefaultNationId);
        StoryTimelineManager.EnsureInstance().TryBootstrapStoryYear(turn, GameMode.StoryMode);
    }

    private static int ResolveCurrentTurn()
    {
        DailySimulationEngine daily = DailySimulationEngine.Instance;
        if (daily != null && daily.Turn > 0)
        {
            return daily.Turn;
        }

        return EraContextResolver.CurrentTurn > 0 ? EraContextResolver.CurrentTurn : 1;
    }

    private static int VerifyCommittedBranchFlags(
        HistoryTimelineBranch branch,
        int turn,
        out string summary)
    {
        summary = string.Empty;
        if (branch?.nodes == null || branch.nodes.Count == 0)
        {
            return 0;
        }

        HistoryFlagRegistry.EnsureWired();
        StringBuilder sb = new StringBuilder();
        int verified = 0;
        for (int i = 0; i < branch.nodes.Count; i++)
        {
            HistoryBranchNode node = branch.nodes[i];
            if (node == null || node.turn > turn)
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(node.outcomeFlag))
            {
                bool unlocked = HistoryFlagRegistry.IsUnlocked(node.outcomeFlag);
                bool resolved = SkillEvolutionLinker.ConditionFlagResolver != null &&
                                SkillEvolutionLinker.ConditionFlagResolver(node.outcomeFlag);
                if (unlocked || resolved)
                {
                    verified++;
                    sb.Append('[').Append(node.outcomeFlag).Append(']');
                }
            }
        }

        string[] inheritedUnlockFlags =
        {
            "MAGIC_DYN_ACADEMY_SYNTHESIS",
            "JOB_ACADEMY_SCHOLAR",
            "MAG_FURNACE_FLOW"
        };
        for (int i = 0; i < inheritedUnlockFlags.Length; i++)
        {
            string flag = inheritedUnlockFlags[i];
            if (!HistoryFlagRegistry.IsUnlocked(flag))
            {
                continue;
            }

            verified++;
            sb.Append('[').Append(flag).Append(']');
        }

        summary = sb.ToString();
        return verified;
    }

    private static void WriteVerifyLog(Generation2TransitionVerifyResult verify)
    {
        try
        {
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
            string path = Path.Combine(projectRoot, "Logs", "generation2_transition_verify.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? projectRoot);
            File.WriteAllText(
                path,
                $"{DateTime.UtcNow:O} success={verify.success}\n{verify.message}\n",
                Encoding.UTF8);
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[Generation2TransitionEngine] 検証ログスキップ: {exception.Message}");
        }
    }
}

public static class Generation2TransitionEngineBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoAttach()
    {
        Generation2TransitionEngine.EnsureInstance();
    }
}

#if UNITY_EDITOR
public static class Generation2TransitionEngineMenu
{
    [MenuItem("Tools/Procedural Map/Begin Generation 2 (T50 to T51)")]
    public static void BeginFromMenu()
    {
        Generation2TransitionResult result = Generation2TransitionEngine.BeginGeneration2();
        EditorUtility.DisplayDialog("Generation 2 Transition", result.message, "OK");
    }

    [MenuItem("Tools/Procedural Map/Verify Generation 2 Transition")]
    public static void VerifyFromMenu()
    {
        Generation2TransitionVerifyResult result = Generation2TransitionEngine.RunVerification();
        Debug.Log(
            result.success
                ? $"<color=#A5D6A7><b>【第2世代移行検証】PASS</b></color>\n{result.message}"
                : $"<color=#FF8A80><b>【第2世代移行検証】FAIL</b></color>\n{result.message}");
        EditorUtility.DisplayDialog("Generation 2 Transition", result.message, result.success ? "OK" : "FAIL");
    }
}
#endif

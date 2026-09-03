using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

// =============================================================================
// 第2世代デイリー IF 創出 — T51 クラフト/防衛介入から ALT_ 子枝を動的生成
// 連携: DailySimulationEngine / HistoryBranchManager / Generation2TransitionEngine
// =============================================================================

/// <summary>第2世代 IF 創出結果。</summary>
public sealed class Generation2IfSpawnResult
{
    public bool success;
    public bool alreadySpawned;
    public string branchId = string.Empty;
    public string parentBranchId = string.Empty;
    public string message = string.Empty;
}

/// <summary>検証結果。</summary>
public sealed class Generation2DailyIfVerifyResult
{
    public bool success;
    public string message = string.Empty;
}

/// <summary>
/// ターン51以降のデイリー介入（クラフト納品・防衛）から、第1世代確定軸の子 IF 枝を創出します。
/// </summary>
[DefaultExecutionOrder(45)]
public class Generation2DailyIfEngine : MonoBehaviour
{
    public const string LogTag = "【第2世代IF創出】";
    public const float HighPurityCraftThreshold = 70f;
    public const string Gen2BranchSuffix = "NEW_TECH";
    public const string Gen2OutcomeFlag = "ALT_NATION_001_TURN_051_NEW_TECH";

    private static string spawnedBranchIdForSession = string.Empty;

    public static Generation2DailyIfEngine EnsureInstance()
    {
        Generation2DailyIfEngine existing = UnityEngine.Object.FindAnyObjectByType<Generation2DailyIfEngine>();
        if (existing != null)
        {
            return existing;
        }

        GameObject hub = GameObject.Find("DebugSystemsHub");
        GameObject host = hub != null ? hub : new GameObject(nameof(Generation2DailyIfEngine));
        Generation2DailyIfEngine engine = host.GetComponent<Generation2DailyIfEngine>();
        return engine != null ? engine : host.AddComponent<Generation2DailyIfEngine>();
    }

    public static void ResetSessionForVerification()
    {
        spawnedBranchIdForSession = string.Empty;
    }

    /// <summary>高純度クラフト納品から第2世代 IF 枝を創出します。</summary>
    public static Generation2IfSpawnResult TrySpawnBranchFromCraftIntervention(
        int turn,
        int nationId,
        float purity,
        string itemLabel = null)
    {
        Generation2IfSpawnResult result = new Generation2IfSpawnResult();
        try
        {
            EnsureInstance();
            if (turn < Generation2TransitionEngine.Generation2StartTurn)
            {
                result.message = $"T{turn} は第2世代開始前のためスキップ";
                return result;
            }

            if (purity < HighPurityCraftThreshold)
            {
                result.message = $"純度{purity:F0} が閾値 {HighPurityCraftThreshold:F0} 未満";
                return result;
            }

            if (!string.IsNullOrWhiteSpace(spawnedBranchIdForSession))
            {
                result.alreadySpawned = true;
                result.branchId = spawnedBranchIdForSession;
                result.success = true;
                result.message = $"既に創出済み branch={spawnedBranchIdForSession}";
                return result;
            }

            HistoryBranchManager mgr = HistoryBranchManager.EnsureInstance();
            if (!mgr.HasCommittedMainStory)
            {
                result.message = "MainStoryBranchId 未確定";
                return result;
            }

            string keyEventId = $"HIST_NATION_{nationId:D3}_GEO_TURN_{turn:D3}_INNOVATION";
            GenerateBranchFromStateResult generated = HistoryBranchManager.GenerateBranchFromCurrentState(
                turn,
                nationId,
                keyEventId,
                Gen2OutcomeFlag,
                new MacroParamDelta
                {
                    powerMultiplier = 1.12f,
                    barrierEfficiencyDelta = 0.06f,
                    threatMultiplier = 0.88f
                },
                mgr.MainStoryBranchId,
                Gen2BranchSuffix);

            if (!generated.success)
            {
                result.message = generated.message;
                return result;
            }

            HistoryFlagRegistry.EnsureWired();
            HistoryFlagRegistry.Unlock("MAGIC_DYN_FORGE_SPIRIT");
            HistoryFlagRegistry.Unlock("JOB_ACADEMY_SCHOLAR");

            spawnedBranchIdForSession = generated.branchId;
            result.success = true;
            result.branchId = generated.branchId;
            result.parentBranchId = generated.parentBranchId;
            result.message = generated.message;

            string label = string.IsNullOrWhiteSpace(itemLabel) ? "高純度魔力結晶" : itemLabel;
            Debug.Log(
                $"<color=#A5D6A7><b>{LogTag}</b></color> " +
                $"ターン{turn} にて新分岐「{generated.branchId}」が発生しました！ " +
                $"(親軸: {generated.parentBranchId}) 納品={label} Purity{purity:F0}");
            return result;
        }
        catch (Exception exception)
        {
            result.message = $"Safe-Fail: {exception.Message}";
            Debug.LogWarning($"[Generation2DailyIfEngine] TrySpawnBranchFromCraftIntervention Safe-Fail: {exception.Message}");
            return result;
        }
    }

    /// <summary>365 日走破時に未創出なら防衛成果ベースで Safe-Fail 創出します。</summary>
    public static Generation2IfSpawnResult TrySpawnBranchFromYearCompletion(int turn, int nationId)
    {
        if (!string.IsNullOrWhiteSpace(spawnedBranchIdForSession))
        {
            return new Generation2IfSpawnResult
            {
                success = true,
                alreadySpawned = true,
                branchId = spawnedBranchIdForSession,
                message = "介入創出済み"
            };
        }

        if (turn < Generation2TransitionEngine.Generation2StartTurn)
        {
            return new Generation2IfSpawnResult { message = "第2世代前" };
        }

        HistoryBranchManager mgr = HistoryBranchManager.EnsureInstance();
        if (!mgr.HasCommittedMainStory)
        {
            return new Generation2IfSpawnResult { message = "MainStoryBranchId 未確定" };
        }

        string keyEventId = $"HIST_NATION_{nationId:D3}_GEO_TURN_{turn:D3}_HERO";
        GenerateBranchFromStateResult generated = HistoryBranchManager.GenerateBranchFromCurrentState(
            turn,
            nationId,
            keyEventId,
            Gen2OutcomeFlag,
            new MacroParamDelta
            {
                powerMultiplier = 1.08f,
                barrierEfficiencyDelta = 0.04f,
                threatMultiplier = 0.92f
            },
            mgr.MainStoryBranchId,
            Gen2BranchSuffix);

        if (!generated.success)
        {
            return new Generation2IfSpawnResult { message = generated.message };
        }

        spawnedBranchIdForSession = generated.branchId;
        Debug.Log(
            $"<color=#A5D6A7><b>{LogTag}</b></color> " +
            $"ターン{turn} にて新分岐「{generated.branchId}」が発生しました！ " +
            $"(親軸: {generated.parentBranchId}) [365日走破 Safe-Fail]");
        return new Generation2IfSpawnResult
        {
            success = true,
            branchId = generated.branchId,
            parentBranchId = generated.parentBranchId,
            message = generated.message
        };
    }

    public static Generation2DailyIfVerifyResult RunVerification()
    {
        Generation2DailyIfVerifyResult verify = new Generation2DailyIfVerifyResult();
        StringBuilder log = new StringBuilder();
        try
        {
            ResetSessionForVerification();
            SimulationVerifyBootstrap.PrepareFreshStoryDay(1, GameMode.StoryMode);
            HistoryFlagRegistry.EnsureWired();
            HistoryFlagRegistry.ClearForTesting();
            HistoryBranchManager.EnsureInstance().ClearRegisteredBranches();
            MasterDataManager.EnsureInstance();

            Generation2TransitionResult gen2 = Generation2TransitionEngine.BeginGeneration2();
            log.AppendLine($"gen2-setup: success={gen2.success} branch={gen2.mainStoryBranchId} turn={gen2.newTurn}");

            DailySimulationEngine daily = DailySimulationEngine.EnsureInstance();
            daily.ResetSessionForVerification();
            daily.TryPrepareForTurn(Generation2TransitionEngine.Generation2StartTurn);
            daily.RebuildTimeline(forceYearBaseline: true);

            TimelineCharacterSelector selector = TimelineCharacterSelector.EnsureInstance();
            selector.TryPossessGeneration2Protagonist(Generation2TransitionEngine.Generation2StartTurn);
            string heroName = selector.PossessedNpc != null
                ? selector.PossessedNpc.DisplayName
                : "SafeFallback";

            DailyAdvanceResult day1 = daily.AdvanceOneDay();
            bool dayPass = day1.success && !string.IsNullOrWhiteSpace(day1.lifeLog);
            log.AppendLine($"day1: log={day1.lifeLog} barrier={day1.barrierPercent:F1} pass={dayPass}");

            CraftVillageInjectionResult inject = DailySimulationEngine.InjectCraftResultToVillage(
                CraftRecipeIds.CrystalShard,
                85f,
                60f,
                "高純度魔力結晶");
            bool injectPass = inject.success && inject.barrierRestoredPercent > 0f;
            log.AppendLine(
                $"inject: restore={inject.barrierRestoredPercent:F1} threat-{inject.threatReductionApplied:F2} " +
                $"pass={injectPass}");

            DailyAdvanceResult day2 = daily.AdvanceOneDay();
            bool threatPass = day2.success && day2.threatLevel <= day1.threatLevel + 0.05f;
            log.AppendLine(
                $"day2: threat={day2.threatLevel:F2} barrier={day2.barrierPercent:F1} pass={threatPass}");

            HistoryBranchManager mgr = HistoryBranchManager.EnsureInstance();
            string mainStoryId = mgr.MainStoryBranchId;
            string newBranchId = spawnedBranchIdForSession;
            bool branchSpawnPass = !string.IsNullOrWhiteSpace(newBranchId);
            bool parentPass = false;
            bool childLinkPass = false;
            if (branchSpawnPass)
            {
                HistoryTimelineBranch child = mgr.FindRegisteredBranchForVerification(newBranchId);
                parentPass = child != null &&
                             string.Equals(child.parentBranchId, mainStoryId, StringComparison.OrdinalIgnoreCase) &&
                             child.forkTurn == Generation2TransitionEngine.Generation2StartTurn;
                childLinkPass = mgr.TryGetChildBranchIds(mainStoryId, out System.Collections.Generic.List<string> childIds) &&
                                childIds.Contains(newBranchId);
            }

            log.AppendLine(
                $"if-branch: id={newBranchId} parent={mainStoryId} spawn={branchSpawnPass} " +
                $"parentNode={parentPass} childLink={childLinkPass}");

            bool flagPass = HistoryFlagRegistry.IsUnlocked(Gen2OutcomeFlag) &&
                            SkillEvolutionLinker.ConditionFlagResolver != null &&
                            SkillEvolutionLinker.ConditionFlagResolver(Gen2OutcomeFlag);
            log.AppendLine($"flags: outcome={flagPass} hero={heroName}");

            bool yearPass = false;
            int daysAdvanced = 2;
            while (daysAdvanced < MicroHistoryTimelineTimelineEngine.DaysPerYear)
            {
                DailyAdvanceResult step = daily.AdvanceOneDay();
                daysAdvanced++;
                if (step.yearCompleted)
                {
                    yearPass = true;
                    log.AppendLine($"year-complete: day={daysAdvanced} turn={step.turn}");
                    break;
                }
            }

            if (!branchSpawnPass && yearPass)
            {
                Generation2IfSpawnResult fallback = TrySpawnBranchFromYearCompletion(
                    Generation2TransitionEngine.Generation2StartTurn,
                    DailySimulationEngine.DefaultNationId);
                branchSpawnPass = fallback.success;
                newBranchId = fallback.branchId;
                log.AppendLine($"year-fallback-spawn: success={fallback.success} id={newBranchId}");
            }

            verify.success = gen2.success && dayPass && injectPass && threatPass &&
                             branchSpawnPass && parentPass && childLinkPass && flagPass && yearPass;
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

    private static void WriteVerifyLog(Generation2DailyIfVerifyResult verify)
    {
        try
        {
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
            string path = Path.Combine(projectRoot, "Logs", "generation2_daily_if_verify.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? projectRoot);
            File.WriteAllText(
                path,
                $"{DateTime.UtcNow:O} success={verify.success}\n{verify.message}\n",
                Encoding.UTF8);
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[Generation2DailyIfEngine] 検証ログスキップ: {exception.Message}");
        }
    }
}

public static class Generation2DailyIfEngineBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoAttach()
    {
        Generation2DailyIfEngine.EnsureInstance();
    }
}

#if UNITY_EDITOR
public static class Generation2DailyIfEngineMenu
{
    [MenuItem("Tools/Procedural Map/Run Generation 2 Daily IF (Turn 51)")]
    public static void RunFromMenu()
    {
        Generation2DailyIfVerifyResult result = Generation2DailyIfEngine.RunVerification();
        EditorUtility.DisplayDialog(
            "Generation 2 Daily IF",
            result.message,
            result.success ? "OK" : "FAIL");
    }

    [MenuItem("Tools/Procedural Map/Verify Generation 2 Daily IF")]
    public static void VerifyFromMenu()
    {
        Generation2DailyIfVerifyResult result = Generation2DailyIfEngine.RunVerification();
        Debug.Log(
            result.success
                ? $"<color=#A5D6A7><b>【第2世代デイリーIF検証】PASS</b></color>\n{result.message}"
                : $"<color=#FF8A80><b>【第2世代デイリーIF検証】FAIL</b></color>\n{result.message}");
        EditorUtility.DisplayDialog(
            "Generation 2 Daily IF",
            result.message,
            result.success ? "OK" : "FAIL");
    }
}
#endif

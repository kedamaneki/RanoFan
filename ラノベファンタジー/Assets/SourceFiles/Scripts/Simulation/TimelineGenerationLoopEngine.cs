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
// タイムライン世代ループ — 第2世代 IF 評価確定 → 第3世代 (T101) 移行
// 連携: AutoBranchGenerator / TimelineScriptEvaluator / Generation2TransitionEngine
//       TurnTransitionEngine / TimelineCharacterSelector / Generation2DailyIfEngine
// =============================================================================

/// <summary>世代ループ実行結果。</summary>
public sealed class TimelineGenerationLoopResult
{
    public bool success;
    public bool usedFallback;
    public bool manualCommit;
    public string gen2BestBranchId = string.Empty;
    public string gen2BestBranchName = string.Empty;
    public int gen2BestScore;
    public string previousMainStoryBranchId = string.Empty;
    public string newMainStoryBranchId = string.Empty;
    public int previousTurn = 100;
    public int newTurn = 101;
    public string protagonistName = string.Empty;
    public string protagonistJobId = string.Empty;
    public string dailyChildBranchId = string.Empty;
    public int evaluatedBranchCount;
    public string message = string.Empty;
    public List<TimelineScriptEvaluationResult> rankings = new List<TimelineScriptEvaluationResult>();
}

/// <summary>検証結果。</summary>
public sealed class TimelineGenerationLoopVerifyResult
{
    public bool success;
    public string message = string.Empty;
}

/// <summary>一括世代生成・評価バッチ結果。</summary>
public sealed class BatchGenerationEraResult
{
    public bool success;
    public bool usedFallback;
    public bool awaitingManualSelection;
    public int targetStartTurn = 101;
    public int targetEndTurn = 150;
    public string parentBranchId = string.Empty;
    public string protagonistName = string.Empty;
    public string protagonistJobId = string.Empty;
    public int generatedBranchCount;
    public int evaluatedBranchCount;
    public string message = string.Empty;
    public List<TimelineScriptEvaluationResult> rankings = new List<TimelineScriptEvaluationResult>();
}

/// <summary>第3世代確定 ➔ 第4世代一括バッチ結果。</summary>
public sealed class Gen4TransitionBatchResult
{
    public bool success;
    public bool usedFallback;
    public bool awaitingManualSelection;
    public string committedGen3BranchId = string.Empty;
    public string committedGen3BranchLabel = string.Empty;
    public int committedGen3Score;
    public int previousTurn = 150;
    public int newTurn = 151;
    public int targetStartTurn = 151;
    public int targetEndTurn = 200;
    public string protagonistName = string.Empty;
    public string protagonistJobId = string.Empty;
    public int generatedBranchCount;
    public int evaluatedBranchCount;
    public string message = string.Empty;
    public List<TimelineScriptEvaluationResult> rankings = new List<TimelineScriptEvaluationResult>();
}

/// <summary>第4世代確定 ➔ 第5世代一括バッチ結果。</summary>
public sealed class Gen5TransitionBatchResult
{
    public bool success;
    public bool usedFallback;
    public bool awaitingManualSelection;
    public string committedGen4BranchId = string.Empty;
    public string committedGen4BranchLabel = string.Empty;
    public int committedGen4Score;
    public int previousTurn = 200;
    public int newTurn = 201;
    public int targetStartTurn = 201;
    public int targetEndTurn = 250;
    public string protagonistName = string.Empty;
    public string protagonistJobId = string.Empty;
    public int generatedBranchCount;
    public int evaluatedBranchCount;
    public string message = string.Empty;
    public List<TimelineScriptEvaluationResult> rankings = new List<TimelineScriptEvaluationResult>();
}

/// <summary>第5世代確定 ➔ 250年正史完結結果。</summary>
public sealed class Gen5FinalizationResult
{
    public bool success;
    public bool usedFallback;
    public string committedGen5BranchId = string.Empty;
    public string committedGen5BranchLabel = string.Empty;
    public int committedGen5Score;
    public int finalTurn = 250;
    public int midCycleTurns = EnvironmentBiorhythmEngine.MidCycleTurns;
    public bool biorhythmApplied;
    public bool exportOk;
    public bool geoWritebackOk;
    public bool gameMastersWritebackOk;
    public string mainStoryLineageSummary = string.Empty;
    public string message = string.Empty;
    public List<CanonTimelineLineageNode> lineage = new List<CanonTimelineLineageNode>();
}

/// <summary>1000年史 MAGI バッチ — 1 世代分の進行結果。</summary>
public sealed class MagiChroniclePipelineStepResult
{
    public int generation;
    public int startTurn;
    public int endTurn;
    public int branchCount;
    public MagiDeliberationStatus status = MagiDeliberationStatus.Disagreed;
    public string selectedBranchId = string.Empty;
    public bool continued;
    public string biorhythmPhase = string.Empty;
    public int positionInCycle;
    public string message = string.Empty;
}

/// <summary>1000年史 MAGI 自動合議バッチ結果。</summary>
public sealed class MagiChroniclePipelineResult
{
    public bool success;
    public bool completedToTurn1000;
    public bool haltedForManualSelection;
    public int generationsProcessed;
    public int finalTurn;
    public string finalMainStoryBranchId = string.Empty;
    public string message = string.Empty;
    public List<MagiChroniclePipelineStepResult> steps = new List<MagiChroniclePipelineStepResult>();
}

/// <summary>文明復興フェーズ（T1001〜1050）一括生成・合議・正史コミット結果。</summary>
public sealed class CivilizationRevivalBatchResult
{
    public bool success;
    public int startTurn = TimelineGenerationLoopEngine.CivilizationRevivalStartTurn;
    public int endTurn = TimelineGenerationLoopEngine.CivilizationRevivalEndTurn;
    public int nextTurn = TimelineGenerationLoopEngine.CivilizationRevivalNextTurn;
    public int generatedBranchCount;
    public string bestBranchId = string.Empty;
    public string bestBranchName = string.Empty;
    public int skuldPruningScore;
    public MagiDeliberationStatus deliberationStatus = MagiDeliberationStatus.Disagreed;
    public bool candidatesExported;
    public string candidatesPath = string.Empty;
    public string message = string.Empty;
}

/// <summary>
/// 第2世代 (T51〜100) の IF 分岐評価・確定と第3世代 (T101) への移行を一括 orchestrate します。
/// </summary>
[DefaultExecutionOrder(44)]
public class TimelineGenerationLoopEngine : MonoBehaviour
{
    public const string LogTag = "【世代進行成功】";
    public const string ManualCommitLogTag = "【第2世代手動確定】";
    public const string BatchCompleteLogTag = "【一括世代生成完了】";
    public const string Gen4BatchCompleteLogTag = "【第3世代確定 ➔ 第4世代一括生成完了】";
    public const string Gen5BatchCompleteLogTag = "【第4世代確定 ➔ 第5世代一括生成完了】";
    public const string ManualGen2BranchId = "ALT_NATION_001_TURN_058_ARCANE_REVIVAL";
    public const string ManualGen2BranchLabel = "魔導復興";
    public const int ManualGen2ExpectedScore = 379;
    public const int ManualGen2CommitTurn = 100;
    public const int DefaultGen2BranchCount = 5;
    public const int DefaultGen3BranchCount = 5;
    public const int DefaultGen4BranchCount = 5;
    public const string ManualGen3BranchId = "ALT_NATION_001_TURN_111_DEMON_FRONT";
    public const string ManualGen3BranchLabel = "魔王軍前線";
    public const int ManualGen3ExpectedScore = 379;
    public const int ManualGen3CommitTurn = 150;
    public const int DefaultGen5BranchCount = 5;
    public const string ManualGen4BranchId = "ALT_NATION_001_TURN_158_HERETIC_ARCANA";
    public const string ManualGen4BranchLabel = "異端魔導の覚醒";
    public const int ManualGen4ExpectedScore = 385;
    public const int ManualGen4CommitTurn = 200;
    public const string ManualGen5BranchId = "ALT_NATION_001_TURN_208_HERETIC_ORTHODOXY";
    public const string ManualGen5BranchLabel = "異端魔法の正統化";
    public const int ManualGen5ExpectedScore = 377;
    public const int ManualGen5CommitTurn = 250;
    public const string Gen5FinalCompleteLogTag = "【250年正史完結・400年周期適用】";
    public const string MagiChronicleBatchLogTag = "【1000年史MAGI自動バッチ進行】";
    public const string CivilizationRevivalBatchLogTag = "【文明復興50年一括完走】";
    public const int PostCanonStartTurn = 251;
    public const int ChronicleCompletionTurn = 1000;
    public const int ChronicleGenerationSpan = 50;
    public const int MinPostCanonBranchCount = 5;
    public const int MaxPostCanonBranchCount = 8;
    /// <summary>第21世代・文明復興フェーズ開始（T≥1001）。</summary>
    public const int CivilizationRevivalStartTurn = EraContextResolver.ChronicleMaxTurn + 1;
    /// <summary>文明復興50年区間の終端。</summary>
    public const int CivilizationRevivalEndTurn = CivilizationRevivalStartTurn + 49;
    /// <summary>復興一括完走後の移行先ターン。</summary>
    public const int CivilizationRevivalNextTurn = CivilizationRevivalEndTurn + 1;

    public static BatchGenerationEraResult LastBatchResult { get; private set; }
    public static Gen4TransitionBatchResult LastGen4BatchResult { get; private set; }
    public static Gen5TransitionBatchResult LastGen5BatchResult { get; private set; }
    public static Gen5FinalizationResult LastGen5FinalizationResult { get; private set; }
    public static MagiChroniclePipelineResult LastMagiChroniclePipelineResult { get; private set; }
    public static CivilizationRevivalBatchResult LastCivilizationRevivalBatchResult { get; private set; }
    public static bool AwaitingManualBranchSelection { get; private set; }
    public static List<TimelineScriptEvaluationResult> PendingBranchRankings { get; private set; }
        = new List<TimelineScriptEvaluationResult>();
    public static string PendingParentBranchId { get; private set; } = string.Empty;
    public static int PendingStartTurn { get; private set; }
    public static int PendingEndTurn { get; private set; }

    /// <summary>MAGI 合議結果を手動選定待ち状態へ反映します。</summary>
    public static void SyncMagiDeliberationState(
        bool awaitingManualSelection,
        IReadOnlyList<TimelineScriptEvaluationResult> rankings = null)
    {
        AwaitingManualBranchSelection = awaitingManualSelection;
        if (rankings != null)
        {
            PendingBranchRankings = new List<TimelineScriptEvaluationResult>(rankings);
        }
    }

    public static TimelineGenerationLoopEngine Instance { get; private set; }

    public static TimelineGenerationLoopEngine EnsureInstance()
    {
        if (Instance != null)
        {
            return Instance;
        }

        TimelineGenerationLoopEngine existing = UnityEngine.Object.FindAnyObjectByType<TimelineGenerationLoopEngine>();
        if (existing != null)
        {
            Instance = existing;
            return existing;
        }

        GameObject hub = GameObject.Find("DebugSystemsHub");
        GameObject host = hub != null ? hub : new GameObject(nameof(TimelineGenerationLoopEngine));
        TimelineGenerationLoopEngine engine = host.GetComponent<TimelineGenerationLoopEngine>();
        return engine != null ? engine : host.AddComponent<TimelineGenerationLoopEngine>();
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

    /// <summary>第2世代 IF 群を評価・Commit し、第3世代 T101 へ進行して新主人公をスロットインします。</summary>
    public static TimelineGenerationLoopResult RunGeneration2CommitAndAdvanceToGen3(
        bool bootstrapIfNeeded = true)
    {
        TimelineGenerationLoopResult result = new TimelineGenerationLoopResult
        {
            previousTurn = Generation2TransitionEngine.Generation2EndTurn,
            newTurn = Generation2TransitionEngine.Generation3StartTurn
        };

        try
        {
            TimelineGenerationLoopEngine.EnsureInstance();
            AutoBranchGenerator.EnsureInstance();
            TimelineScriptEvaluator.EnsureInstance();
            HistoryBranchManager mgr = HistoryBranchManager.EnsureInstance();
            HistoryFlagRegistry.EnsureWired();
            MasterDataManager.EnsureInstance();

            if (bootstrapIfNeeded)
            {
                BootstrapGeneration2Baseline(out string childBranchId);
                result.dailyChildBranchId = childBranchId;
            }
            else
            {
                result.dailyChildBranchId = Generation2DailyIfEngine.EnsureInstance() != null
                    ? FindDailyChildBranchId(mgr)
                    : string.Empty;
            }

            result.previousMainStoryBranchId = mgr.MainStoryBranchId;
            if (string.IsNullOrWhiteSpace(result.previousMainStoryBranchId))
            {
                result.usedFallback = true;
                AutoBranchGenerator.GenerateAndCommitPlan(1, Generation2TransitionEngine.Generation1EndTurn, 2);
                Generation2TransitionEngine.BeginGeneration2();
                result.previousMainStoryBranchId = mgr.MainStoryBranchId;
            }

            AutoBranchGenerationResult gen2 = AutoBranchGenerator.GenerateAndEvaluateBranches(
                Generation2TransitionEngine.Generation2StartTurn,
                Generation2TransitionEngine.Generation2EndTurn,
                DefaultGen2BranchCount);

            result.rankings = gen2.rankings ?? new List<TimelineScriptEvaluationResult>();
            result.evaluatedBranchCount = result.rankings.Count;
            result.gen2BestBranchId = gen2.committedBranchId;
            result.gen2BestBranchName = gen2.committedBranchName;
            result.gen2BestScore = gen2.bestScore;
            result.newMainStoryBranchId = mgr.MainStoryBranchId;
            result.usedFallback = result.usedFallback || gen2.usedFallbackBranch;

            if (!gen2.success || string.IsNullOrWhiteSpace(gen2.committedBranchId))
            {
                result.message = $"第2世代 IF 評価失敗: {gen2.message}";
                return result;
            }

            SyncToTurn(Generation2TransitionEngine.Generation2EndTurn);
            MicroToMacroAggregator aggregator = MicroToMacroAggregator.EnsureInstance();
            if (!aggregator.YearEndApplied)
            {
                aggregator.AggregateYearEnd(logMacro: false);
            }

            return FinalizeAdvanceToGen3(result);
        }
        catch (Exception exception)
        {
            result.success = false;
            result.usedFallback = true;
            result.message = $"Safe-Fail: {exception.Message}";
            Debug.LogWarning($"[TimelineGenerationLoopEngine] RunGeneration2CommitAndAdvanceToGen3 Safe-Fail: {exception.Message}");

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

    /// <summary>
    /// 第2世代 IF 群を生成・評価したうえで、指定 branchId を手動 Commit し第3世代 T101 へ進行します。
    /// </summary>
    public static TimelineGenerationLoopResult RunGeneration2ManualCommitAndAdvanceToGen3(
        string branchId = ManualGen2BranchId,
        int? knownScore = ManualGen2ExpectedScore,
        int evaluationTurn = ManualGen2CommitTurn,
        bool bootstrapIfNeeded = true)
    {
        TimelineGenerationLoopResult result = new TimelineGenerationLoopResult
        {
            previousTurn = Generation2TransitionEngine.Generation2EndTurn,
            newTurn = Generation2TransitionEngine.Generation3StartTurn,
            manualCommit = true
        };

        try
        {
            TimelineGenerationLoopEngine.EnsureInstance();
            AutoBranchGenerator.EnsureInstance();
            TimelineScriptEvaluator.EnsureInstance();
            HistoryBranchManager mgr = HistoryBranchManager.EnsureInstance();
            HistoryFlagRegistry.EnsureWired();
            MasterDataManager.EnsureInstance();

            if (bootstrapIfNeeded)
            {
                BootstrapGeneration2Baseline(out string childBranchId);
                result.dailyChildBranchId = childBranchId;
            }

            result.previousMainStoryBranchId = mgr.MainStoryBranchId;
            if (string.IsNullOrWhiteSpace(result.previousMainStoryBranchId))
            {
                result.usedFallback = true;
                AutoBranchGenerator.GenerateAndCommitPlan(1, Generation2TransitionEngine.Generation1EndTurn, 2);
                Generation2TransitionEngine.BeginGeneration2();
                result.previousMainStoryBranchId = mgr.MainStoryBranchId;
            }

            AutoBranchGenerationResult gen2Eval = AutoBranchGenerator.GenerateBranchesForEvaluation(
                Generation2TransitionEngine.Generation2StartTurn,
                Generation2TransitionEngine.Generation2EndTurn,
                DefaultGen2BranchCount);

            result.rankings = gen2Eval.rankings ?? new List<TimelineScriptEvaluationResult>();
            result.evaluatedBranchCount = result.rankings.Count;

            if (!gen2Eval.success || gen2Eval.generatedCount <= 0)
            {
                result.message = $"第2世代 IF 生成失敗: {gen2Eval.message}";
                return result;
            }

            string resolvedBranchId = ResolveManualBranchId(branchId, mgr, result.rankings);
            if (string.IsNullOrWhiteSpace(resolvedBranchId))
            {
                result.usedFallback = true;
                resolvedBranchId = SelectTopAlt(result.rankings)?.branchId ?? string.Empty;
            }

            int commitScore = ResolveManualBranchScore(
                resolvedBranchId,
                evaluationTurn,
                knownScore,
                mgr,
                result.rankings);

            MainStoryCommitResult commit = HistoryBranchManager.CommitBranchAsMainStory(
                resolvedBranchId,
                commitScore,
                evaluationTurn);

            if (!commit.success)
            {
                result.message = $"手動 Commit 失敗: {commit.message}";
                return result;
            }

            result.gen2BestBranchId = commit.branchId;
            result.gen2BestBranchName = string.IsNullOrWhiteSpace(commit.branchName)
                ? ManualGen2BranchLabel
                : commit.branchName;
            result.gen2BestScore = commit.totalScore > 0 ? commit.totalScore : commitScore;
            result.newMainStoryBranchId = mgr.MainStoryBranchId;

            SyncToTurn(Generation2TransitionEngine.Generation2EndTurn);
            MicroToMacroAggregator aggregator = MicroToMacroAggregator.EnsureInstance();
            if (!aggregator.YearEndApplied)
            {
                aggregator.AggregateYearEnd(logMacro: false);
            }

            result = FinalizeAdvanceToGen3(result);

            Debug.Log(
                $"<color=#CE93D8><b>{ManualCommitLogTag}</b></color> " +
                $"メイン軸を「{ManualGen2BranchLabel}」(スコア:{result.gen2BestScore}) に確定し、 " +
                $"第3世代(ターン{result.newTurn})へ移行しました！ 新主人公: 「{result.protagonistName}」");

            return result;
        }
        catch (Exception exception)
        {
            result.success = false;
            result.usedFallback = true;
            result.message = $"Safe-Fail: {exception.Message}";
            Debug.LogWarning(
                $"[TimelineGenerationLoopEngine] RunGeneration2ManualCommitAndAdvanceToGen3 Safe-Fail: {exception.Message}");

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

    private static TimelineGenerationLoopResult FinalizeAdvanceToGen3(TimelineGenerationLoopResult result)
    {
        HistoryBranchManager mgr = HistoryBranchManager.EnsureInstance();

        TurnTransitionResult transition = TurnTransitionEngine.AdvanceToNextYear(fromDailyCompletion: false);
        if (!transition.success || !transition.advanced ||
            transition.newTurn != Generation2TransitionEngine.Generation3StartTurn)
        {
            result.usedFallback = true;
            DailySimulationEngine.EnsureInstance().TryPrepareForTurn(Generation2TransitionEngine.Generation3StartTurn);
            SyncToTurn(Generation2TransitionEngine.Generation3StartTurn);
        }
        else
        {
            result.newTurn = transition.newTurn;
        }

        HistoricalNpcCaster.SpawnHistoricalKeyCharacters(result.newTurn);
        TimelineCharacterSelector selector = TimelineCharacterSelector.EnsureInstance();
        bool possessed = selector.TryPossessGeneration3Protagonist(result.newTurn);
        if (selector.PossessedNpc != null)
        {
            result.protagonistName = selector.PossessedNpc.DisplayName;
            result.protagonistJobId = selector.PossessedNpc.JobId;
        }
        else if (selector.IsUsingSafeFallback)
        {
            result.usedFallback = true;
            result.protagonistName = "標準仮アバター";
            result.protagonistJobId = DynamicJobBuilder.FallbackJobId;
        }

        result.newMainStoryBranchId = mgr.MainStoryBranchId;
        result.success = mgr.HasCommittedMainStory &&
                         !string.IsNullOrWhiteSpace(result.gen2BestBranchId) &&
                         string.Equals(
                             mgr.MainStoryBranchId,
                             result.gen2BestBranchId,
                             StringComparison.OrdinalIgnoreCase) &&
                         result.newTurn == Generation2TransitionEngine.Generation3StartTurn &&
                         possessed;

        if (!result.manualCommit)
        {
            result.message =
                $"gen2={result.gen2BestBranchId} score={result.gen2BestScore} " +
                $"main={result.newMainStoryBranchId} turn={result.previousTurn}->{result.newTurn} " +
                $"hero={result.protagonistName} evaluated={result.evaluatedBranchCount} " +
                $"child={result.dailyChildBranchId}";

            Debug.Log(
                $"<color=#A5D6A7><b>{LogTag}</b></color> " +
                $"第2世代メイン軸「{result.gen2BestBranchName}」(スコア:{result.gen2BestScore}) を確定し、 " +
                $"第3世代(ターン{result.newTurn})へ移行しました！ 新主人公: 「{result.protagonistName}」");
        }
        else
        {
            result.message =
                $"manual={result.gen2BestBranchId} score={result.gen2BestScore} turn=T{ManualGen2CommitTurn} " +
                $"main={result.newMainStoryBranchId} hero={result.protagonistName} " +
                $"evaluated={result.evaluatedBranchCount}";
        }

        return result;
    }

    private static string ResolveManualBranchId(
        string branchId,
        HistoryBranchManager mgr,
        List<TimelineScriptEvaluationResult> rankings)
    {
        if (!string.IsNullOrWhiteSpace(branchId) &&
            mgr.FindRegisteredBranchForVerification(branchId) != null)
        {
            return branchId;
        }

        for (int i = 0; i < rankings?.Count; i++)
        {
            TimelineScriptEvaluationResult row = rankings[i];
            if (row != null &&
                !string.IsNullOrWhiteSpace(row.branchId) &&
                row.branchId.IndexOf("ARCANE_REVIVAL", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return row.branchId;
            }
        }

        IReadOnlyList<HistoryTimelineBranch> branches = mgr.RegisteredBranches;
        for (int i = 0; i < branches.Count; i++)
        {
            HistoryTimelineBranch branch = branches[i];
            if (branch != null &&
                !string.IsNullOrWhiteSpace(branch.displayName) &&
                branch.displayName.IndexOf("魔導復興", StringComparison.OrdinalIgnoreCase) >= 0 ||
                branch.displayName.IndexOf("魔導復", StringComparison.OrdinalIgnoreCase) >= 0 ||
                branch.branchId.IndexOf("ARCANE_REVIVAL", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return branch.branchId;
            }
        }

        return branchId;
    }

    private static int ResolveManualBranchScore(
        string branchId,
        int evaluationTurn,
        int? knownScore,
        HistoryBranchManager mgr,
        List<TimelineScriptEvaluationResult> rankings)
    {
        TimelineScriptEvaluationResult ranked = FindEvaluationResult(rankings, branchId);
        if (ranked != null && ranked.totalScore > 0)
        {
            return ranked.totalScore;
        }

        HistoryTimelineBranch branch = mgr.FindRegisteredBranchForVerification(branchId);
        if (branch != null)
        {
            TimelineScriptEvaluationResult evaluated =
                TimelineScriptEvaluator.EvaluateTimelineBranch(branch, evaluationTurn);
            if (evaluated.totalScore > 0)
            {
                return evaluated.totalScore;
            }
        }

        return knownScore ?? TimelineScriptEvaluator.SafeFailTotalScore;
    }

    private static TimelineScriptEvaluationResult FindEvaluationResult(
        List<TimelineScriptEvaluationResult> ranked,
        string branchId)
    {
        if (ranked == null || string.IsNullOrWhiteSpace(branchId))
        {
            return null;
        }

        for (int i = 0; i < ranked.Count; i++)
        {
            TimelineScriptEvaluationResult row = ranked[i];
            if (row != null && string.Equals(row.branchId, branchId, StringComparison.OrdinalIgnoreCase))
            {
                return row;
            }
        }

        return null;
    }

    /// <summary>
    /// 指定世代へ年次移行・新主人公スロットイン・IF 分岐生成と評価を一括実行します（Commit なし・手動選定待ち）。
    /// </summary>
    public void RunBatchGenerationAndEvaluateNextEra(
        int targetStartTurn,
        int targetEndTurn,
        string parentBranchId)
    {
        LastBatchResult = RunBatchGenerationAndEvaluateNextEraCore(
            targetStartTurn,
            targetEndTurn,
            parentBranchId);
    }

    /// <summary>
    /// 指定世代へ年次移行・新主人公スロットイン・IF 分岐生成と評価を一括実行します（Commit なし・手動選定待ち）。
    /// </summary>
    public static BatchGenerationEraResult RunBatchGenerationAndEvaluateNextEraCore(
        int targetStartTurn,
        int targetEndTurn,
        string parentBranchId,
        bool bootstrapIfNeeded = false)
    {
        BatchGenerationEraResult result = new BatchGenerationEraResult
        {
            targetStartTurn = Mathf.Clamp(
                targetStartTurn,
                EraContextResolver.MinTurn,
                EraContextResolver.MaxTurn),
            targetEndTurn = Mathf.Clamp(
                targetEndTurn,
                EraContextResolver.MinTurn,
                EraContextResolver.MaxTurn)
        };

        if (result.targetEndTurn < result.targetStartTurn)
        {
            int swap = result.targetStartTurn;
            result.targetStartTurn = result.targetEndTurn;
            result.targetEndTurn = swap;
        }

        AwaitingManualBranchSelection = false;
        PendingBranchRankings = new List<TimelineScriptEvaluationResult>();
        PendingParentBranchId = string.Empty;
        PendingStartTurn = result.targetStartTurn;
        PendingEndTurn = result.targetEndTurn;

        try
        {
            TimelineGenerationLoopEngine.EnsureInstance();
            TurnTransitionEngine.EnsureInstance();
            AutoBranchGenerator.EnsureInstance();
            TimelineScriptEvaluator.EnsureInstance();
            HistoryBranchManager mgr = HistoryBranchManager.EnsureInstance();
            HistoryFlagRegistry.EnsureWired();
            MasterDataManager.EnsureInstance();

            if (bootstrapIfNeeded)
            {
                RunGeneration2ManualCommitAndAdvanceToGen3(bootstrapIfNeeded: true);
            }

            result.parentBranchId = ResolveBatchParentBranchId(parentBranchId, mgr);
            if (string.IsNullOrWhiteSpace(result.parentBranchId))
            {
                result.usedFallback = true;
                if (!mgr.HasCommittedMainStory)
                {
                    AutoBranchGenerator.GenerateAndCommitPlan(
                        1,
                        Generation2TransitionEngine.Generation1EndTurn,
                        Generation2TransitionEngine.DefaultGeneration1PlanIndex);
                    Generation2TransitionEngine.BeginGeneration2();
                }

                result.parentBranchId = mgr.MainStoryBranchId;
            }

            string mainBeforeEval = mgr.MainStoryBranchId;
            bool advanced = AdvanceToTargetTurn(result.targetStartTurn, out bool advanceFallback);
            if (advanceFallback)
            {
                result.usedFallback = true;
            }

            TimelineCharacterSelector selector = TimelineCharacterSelector.EnsureInstance();
            bool possessed = TryPossessEraProtagonist(selector, result.targetStartTurn);
            if (selector.PossessedNpc != null)
            {
                result.protagonistName = selector.PossessedNpc.DisplayName;
                result.protagonistJobId = selector.PossessedNpc.JobId;
            }
            else if (selector.IsUsingSafeFallback)
            {
                result.usedFallback = true;
                result.protagonistName = "標準仮アバター";
                result.protagonistJobId = DynamicJobBuilder.FallbackJobId;
            }

            AutoBranchGenerationResult eval = AutoBranchGenerator.GenerateBranchesForEvaluation(
                result.targetStartTurn,
                result.targetEndTurn,
                ResolveDefaultBranchCount(result.targetStartTurn),
                result.parentBranchId);

            result.rankings = eval.rankings ?? new List<TimelineScriptEvaluationResult>();
            result.generatedBranchCount = eval.generatedCount;
            result.evaluatedBranchCount = result.rankings.Count;
            if (eval.usedFallbackBranch)
            {
                result.usedFallback = true;
            }

            PendingBranchRankings = new List<TimelineScriptEvaluationResult>(result.rankings);
            PendingParentBranchId = result.parentBranchId;
            AwaitingManualBranchSelection = eval.success && result.evaluatedBranchCount > 0;
            result.awaitingManualSelection = AwaitingManualBranchSelection;

            string pendingList = FormatPendingRankingsList(result.rankings);
            result.message =
                $"turn=T{result.targetStartTurn}-T{result.targetEndTurn} parent={result.parentBranchId} " +
                $"generated={result.generatedBranchCount} evaluated={result.evaluatedBranchCount} " +
                $"hero={result.protagonistName} advanced={advanced} mainUnchanged=" +
                $"{string.Equals(mainBeforeEval, mgr.MainStoryBranchId, StringComparison.OrdinalIgnoreCase)}";

            Debug.Log(
                $"<color=#80DEEA><b>{BatchCompleteLogTag}</b></color> " +
                $"ターン{result.targetStartTurn}〜{result.targetEndTurn} の IF 分岐評価が完了しました。選定待ち一覧:\n" +
                pendingList);

            result.success = eval.success &&
                             result.generatedBranchCount > 0 &&
                             result.evaluatedBranchCount > 0 &&
                             advanced &&
                             possessed &&
                             string.Equals(
                                 mainBeforeEval,
                                 mgr.MainStoryBranchId,
                                 StringComparison.OrdinalIgnoreCase);

            LastBatchResult = result;
            return result;
        }
        catch (Exception exception)
        {
            result.success = false;
            result.usedFallback = true;
            result.message = $"Safe-Fail: {exception.Message}";
            Debug.LogWarning(
                $"[TimelineGenerationLoopEngine] RunBatchGenerationAndEvaluateNextEra Safe-Fail: {exception.Message}");

            try
            {
                TimelineCharacterSelector.EnsureInstance().FallbackToSafeAvatar(exception.Message);
            }
            catch (Exception fallbackException)
            {
                result.message += $" / fallback failed: {fallbackException.Message}";
            }

            LastBatchResult = result;
            return result;
        }
    }

    /// <summary>
    /// 第3世代「魔王軍前線」を手動 Commit し、第4世代 T151 移行と T151〜200 IF 評価を一括実行します。
    /// </summary>
    public void RunGen3DemonFrontCommitAndBatchGen4Evaluation(bool bootstrapIfNeeded = true)
    {
        LastGen4BatchResult = RunGen3DemonFrontCommitAndBatchGen4EvaluationCore(bootstrapIfNeeded);
    }

    /// <summary>
    /// 第3世代「魔王軍前線」を手動 Commit し、第4世代 T151 移行と T151〜200 IF 評価を一括実行します。
    /// </summary>
    public static Gen4TransitionBatchResult RunGen3DemonFrontCommitAndBatchGen4EvaluationCore(
        bool bootstrapIfNeeded = true)
    {
        Gen4TransitionBatchResult result = new Gen4TransitionBatchResult
        {
            previousTurn = Generation2TransitionEngine.Generation3EndTurn,
            newTurn = Generation2TransitionEngine.Generation4StartTurn,
            targetStartTurn = Generation2TransitionEngine.Generation4StartTurn,
            targetEndTurn = Generation2TransitionEngine.Generation4EndTurn,
            committedGen3BranchId = ManualGen3BranchId,
            committedGen3BranchLabel = ManualGen3BranchLabel,
            committedGen3Score = ManualGen3ExpectedScore
        };

        AwaitingManualBranchSelection = false;
        PendingBranchRankings = new List<TimelineScriptEvaluationResult>();
        PendingParentBranchId = ManualGen3BranchId;
        PendingStartTurn = result.targetStartTurn;
        PendingEndTurn = result.targetEndTurn;

        try
        {
            TimelineGenerationLoopEngine.EnsureInstance();
            TurnTransitionEngine.EnsureInstance();
            AutoBranchGenerator.EnsureInstance();
            TimelineScriptEvaluator.EnsureInstance();
            HistoryBranchManager mgr = HistoryBranchManager.EnsureInstance();
            HistoryFlagRegistry.EnsureWired();
            MasterDataManager.EnsureInstance();

            if (bootstrapIfNeeded)
            {
                EnsureGen3DemonFrontBranchAvailable(mgr);
            }
            else if (mgr.FindRegisteredBranchForVerification(ManualGen3BranchId) == null)
            {
                EnsureGen3DemonFrontBranchAvailable(mgr);
            }

            List<TimelineScriptEvaluationResult> gen3Rankings =
                PendingBranchRankings != null && PendingBranchRankings.Count > 0
                    ? PendingBranchRankings
                    : TimelineScriptEvaluator.EvaluateAllBranchesAtCurrentTurn(ManualGen3CommitTurn);

            SyncToTurn(ManualGen3CommitTurn);

            string resolvedBranchId = ResolveGen3ManualBranchId(ManualGen3BranchId, mgr, gen3Rankings);
            if (string.IsNullOrWhiteSpace(resolvedBranchId))
            {
                result.usedFallback = true;
                resolvedBranchId = ManualGen3BranchId;
            }

            int commitScore = ResolveManualBranchScore(
                resolvedBranchId,
                ManualGen3CommitTurn,
                ManualGen3ExpectedScore,
                mgr,
                gen3Rankings);

            MainStoryCommitResult commit = HistoryBranchManager.CommitBranchAsMainStory(
                resolvedBranchId,
                commitScore,
                ManualGen3CommitTurn);

            if (!commit.success)
            {
                result.message = $"第3世代手動 Commit 失敗: {commit.message}";
                LastGen4BatchResult = result;
                return result;
            }

            result.committedGen3BranchId = commit.branchId;
            result.committedGen3BranchLabel = string.IsNullOrWhiteSpace(commit.branchName)
                ? ManualGen3BranchLabel
                : commit.branchName;
            result.committedGen3Score = commit.totalScore > 0 ? commit.totalScore : commitScore;

            SyncToTurn(ManualGen3CommitTurn);
            MicroToMacroAggregator aggregator = MicroToMacroAggregator.EnsureInstance();
            if (!aggregator.YearEndApplied)
            {
                aggregator.AggregateYearEnd(logMacro: false);
            }

            bool advanced = AdvanceToTargetTurn(result.newTurn, out bool advanceFallback);
            if (advanceFallback)
            {
                result.usedFallback = true;
            }

            result.newTurn = ResolveCurrentTurn();
            HistoricalNpcCaster.SpawnHistoricalKeyCharacters(result.newTurn);

            TimelineCharacterSelector selector = TimelineCharacterSelector.EnsureInstance();
            bool possessed = selector.TryPossessGeneration4Protagonist(result.newTurn);
            if (selector.PossessedNpc != null)
            {
                result.protagonistName = selector.PossessedNpc.DisplayName;
                result.protagonistJobId = selector.PossessedNpc.JobId;
            }
            else if (selector.IsUsingSafeFallback)
            {
                result.usedFallback = true;
                result.protagonistName = "標準仮アバター";
                result.protagonistJobId = DynamicJobBuilder.FallbackJobId;
            }

            AutoBranchGenerationResult gen4Eval = AutoBranchGenerator.GenerateBranchesForEvaluation(
                result.targetStartTurn,
                result.targetEndTurn,
                DefaultGen4BranchCount,
                result.committedGen3BranchId);

            result.rankings = gen4Eval.rankings ?? new List<TimelineScriptEvaluationResult>();
            result.generatedBranchCount = gen4Eval.generatedCount;
            result.evaluatedBranchCount = result.rankings.Count;
            if (gen4Eval.usedFallbackBranch)
            {
                result.usedFallback = true;
            }

            PendingBranchRankings = new List<TimelineScriptEvaluationResult>(result.rankings);
            PendingParentBranchId = result.committedGen3BranchId;
            AwaitingManualBranchSelection = gen4Eval.success && result.evaluatedBranchCount > 0;
            result.awaitingManualSelection = AwaitingManualBranchSelection;

            string pendingList = FormatPendingRankingsList(result.rankings);
            EraTag eraTag = EraContextResolver.ResolveEraTag(result.newTurn);
            result.message =
                $"gen3={result.committedGen3BranchId} score={result.committedGen3Score} " +
                $"turn=T{result.previousTurn}->T{result.newTurn} era={eraTag} " +
                $"gen4=T{result.targetStartTurn}-T{result.targetEndTurn} generated={result.generatedBranchCount} " +
                $"evaluated={result.evaluatedBranchCount} hero={result.protagonistName}";

            Debug.Log(
                $"<color=#FFAB91><b>{Gen4BatchCompleteLogTag}</b></color> " +
                $"第3世代メイン軸: 「{result.committedGen3BranchLabel}」({result.committedGen3Score}) を確定！ " +
                $"ターン{result.targetStartTurn}〜{result.targetEndTurn} の IF 評価が完了しました。選定待ち一覧:\n" +
                pendingList);

            result.success = commit.success &&
                             mgr.HasCommittedMainStory &&
                             string.Equals(
                                 mgr.MainStoryBranchId,
                                 result.committedGen3BranchId,
                                 StringComparison.OrdinalIgnoreCase) &&
                             result.newTurn >= Generation2TransitionEngine.Generation4StartTurn &&
                             eraTag == EraTag.Late &&
                             gen4Eval.success &&
                             result.generatedBranchCount > 0 &&
                             result.evaluatedBranchCount > 0 &&
                             advanced &&
                             possessed;

            LastGen4BatchResult = result;
            return result;
        }
        catch (Exception exception)
        {
            result.success = false;
            result.usedFallback = true;
            result.message = $"Safe-Fail: {exception.Message}";
            Debug.LogWarning(
                $"[TimelineGenerationLoopEngine] RunGen3DemonFrontCommitAndBatchGen4Evaluation Safe-Fail: {exception.Message}");

            try
            {
                TimelineCharacterSelector.EnsureInstance().FallbackToSafeAvatar(exception.Message);
            }
            catch (Exception fallbackException)
            {
                result.message += $" / fallback failed: {fallbackException.Message}";
            }

            LastGen4BatchResult = result;
            return result;
        }
    }

    /// <summary>
    /// 第4世代「異端魔導の覚醒」を手動 Commit し、第5世代 T201 移行と T201〜250 IF 評価を一括実行します。
    /// </summary>
    public void RunGen4HereticArcanaCommitAndBatchGen5Evaluation(bool bootstrapIfNeeded = true)
    {
        LastGen5BatchResult = RunGen4HereticArcanaCommitAndBatchGen5EvaluationCore(bootstrapIfNeeded);
    }

    /// <summary>
    /// 第4世代「異端魔導の覚醒」を手動 Commit し、第5世代 T201 移行と T201〜250 IF 評価を一括実行します。
    /// </summary>
    public static Gen5TransitionBatchResult RunGen4HereticArcanaCommitAndBatchGen5EvaluationCore(
        bool bootstrapIfNeeded = true)
    {
        Gen5TransitionBatchResult result = new Gen5TransitionBatchResult
        {
            previousTurn = Generation2TransitionEngine.Generation4EndTurn,
            newTurn = Generation2TransitionEngine.Generation5StartTurn,
            targetStartTurn = Generation2TransitionEngine.Generation5StartTurn,
            targetEndTurn = Generation2TransitionEngine.Generation5EndTurn,
            committedGen4BranchId = ManualGen4BranchId,
            committedGen4BranchLabel = ManualGen4BranchLabel,
            committedGen4Score = ManualGen4ExpectedScore
        };

        AwaitingManualBranchSelection = false;
        PendingBranchRankings = new List<TimelineScriptEvaluationResult>();
        PendingParentBranchId = ManualGen4BranchId;
        PendingStartTurn = result.targetStartTurn;
        PendingEndTurn = result.targetEndTurn;

        try
        {
            TimelineGenerationLoopEngine.EnsureInstance();
            TurnTransitionEngine.EnsureInstance();
            AutoBranchGenerator.EnsureInstance();
            TimelineScriptEvaluator.EnsureInstance();
            HistoryBranchManager mgr = HistoryBranchManager.EnsureInstance();
            HistoryFlagRegistry.EnsureWired();
            MasterDataManager.EnsureInstance();

            if (bootstrapIfNeeded)
            {
                EnsureGen4HereticArcanaBranchAvailable(mgr);
            }
            else if (mgr.FindRegisteredBranchForVerification(ManualGen4BranchId) == null)
            {
                EnsureGen4HereticArcanaBranchAvailable(mgr);
            }

            List<TimelineScriptEvaluationResult> gen4Rankings =
                PendingBranchRankings != null && PendingBranchRankings.Count > 0
                    ? PendingBranchRankings
                    : TimelineScriptEvaluator.EvaluateAllBranchesAtCurrentTurn(ManualGen4CommitTurn);

            SyncToTurn(ManualGen4CommitTurn);

            string resolvedBranchId = ResolveGen4ManualBranchId(ManualGen4BranchId, mgr, gen4Rankings);
            if (string.IsNullOrWhiteSpace(resolvedBranchId))
            {
                result.usedFallback = true;
                resolvedBranchId = ManualGen4BranchId;
            }

            int commitScore = ResolveManualBranchScore(
                resolvedBranchId,
                ManualGen4CommitTurn,
                ManualGen4ExpectedScore,
                mgr,
                gen4Rankings);

            MainStoryCommitResult commit = HistoryBranchManager.CommitBranchAsMainStory(
                resolvedBranchId,
                commitScore,
                ManualGen4CommitTurn);

            if (!commit.success)
            {
                result.message = $"第4世代手動 Commit 失敗: {commit.message}";
                LastGen5BatchResult = result;
                return result;
            }

            result.committedGen4BranchId = commit.branchId;
            result.committedGen4BranchLabel = string.IsNullOrWhiteSpace(commit.branchName)
                ? ManualGen4BranchLabel
                : commit.branchName;
            result.committedGen4Score = commit.totalScore > 0 ? commit.totalScore : commitScore;

            SyncToTurn(ManualGen4CommitTurn);
            MicroToMacroAggregator aggregator = MicroToMacroAggregator.EnsureInstance();
            if (!aggregator.YearEndApplied)
            {
                aggregator.AggregateYearEnd(logMacro: false);
            }

            bool advanced = AdvanceToTargetTurn(result.newTurn, out bool advanceFallback);
            if (advanceFallback)
            {
                result.usedFallback = true;
            }

            result.newTurn = ResolveCurrentTurn();
            HistoricalNpcCaster.SpawnHistoricalKeyCharacters(result.newTurn);

            TimelineCharacterSelector selector = TimelineCharacterSelector.EnsureInstance();
            bool possessed = selector.TryPossessGeneration5Protagonist(result.newTurn);
            if (selector.PossessedNpc != null)
            {
                result.protagonistName = selector.PossessedNpc.DisplayName;
                result.protagonistJobId = selector.PossessedNpc.JobId;
            }
            else if (selector.IsUsingSafeFallback)
            {
                result.usedFallback = true;
                result.protagonistName = "標準仮アバター";
                result.protagonistJobId = DynamicJobBuilder.FallbackJobId;
            }

            AutoBranchGenerationResult gen5Eval = AutoBranchGenerator.GenerateBranchesForEvaluation(
                result.targetStartTurn,
                result.targetEndTurn,
                DefaultGen5BranchCount,
                result.committedGen4BranchId);

            result.rankings = gen5Eval.rankings ?? new List<TimelineScriptEvaluationResult>();
            result.generatedBranchCount = gen5Eval.generatedCount;
            result.evaluatedBranchCount = result.rankings.Count;
            if (gen5Eval.usedFallbackBranch)
            {
                result.usedFallback = true;
            }

            PendingBranchRankings = new List<TimelineScriptEvaluationResult>(result.rankings);
            PendingParentBranchId = result.committedGen4BranchId;
            AwaitingManualBranchSelection = gen5Eval.success && result.evaluatedBranchCount > 0;
            result.awaitingManualSelection = AwaitingManualBranchSelection;

            string pendingList = FormatPendingRankingsList(result.rankings);
            bool ancientEra = IsAncientEraAtTurn(result.newTurn);
            result.message =
                $"gen4={result.committedGen4BranchId} score={result.committedGen4Score} " +
                $"turn=T{result.previousTurn}->T{result.newTurn} ancient={ancientEra} " +
                $"gen5=T{result.targetStartTurn}-T{result.targetEndTurn} generated={result.generatedBranchCount} " +
                $"evaluated={result.evaluatedBranchCount} hero={result.protagonistName}";

            Debug.Log(
                $"<color=#CE93D8><b>{Gen5BatchCompleteLogTag}</b></color> " +
                $"第4世代メイン軸: 「{result.committedGen4BranchLabel}」({result.committedGen4Score}) を確定！ " +
                $"ターン{result.targetStartTurn}〜{result.targetEndTurn} の IF 評価が完了しました。選定待ち一覧:\n" +
                pendingList);

            result.success = commit.success &&
                             mgr.HasCommittedMainStory &&
                             string.Equals(
                                 mgr.MainStoryBranchId,
                                 result.committedGen4BranchId,
                                 StringComparison.OrdinalIgnoreCase) &&
                             result.newTurn >= Generation2TransitionEngine.Generation5StartTurn &&
                             ancientEra &&
                             gen5Eval.success &&
                             result.generatedBranchCount > 0 &&
                             result.evaluatedBranchCount > 0 &&
                             advanced &&
                             possessed;

            LastGen5BatchResult = result;
            return result;
        }
        catch (Exception exception)
        {
            result.success = false;
            result.usedFallback = true;
            result.message = $"Safe-Fail: {exception.Message}";
            Debug.LogWarning(
                $"[TimelineGenerationLoopEngine] RunGen4HereticArcanaCommitAndBatchGen5Evaluation Safe-Fail: {exception.Message}");

            try
            {
                TimelineCharacterSelector.EnsureInstance().FallbackToSafeAvatar(exception.Message);
            }
            catch (Exception fallbackException)
            {
                result.message += $" / fallback failed: {fallbackException.Message}";
            }

            LastGen5BatchResult = result;
            return result;
        }
    }

    /// <summary>
    /// 第5世代「異端魔法の正統化」を手動 Commit し、400年周期適用と250年正史ツリーを確定します。
    /// </summary>
    public void RunGen5HereticOrthodoxyCommitAndFinalize250YearHistory(bool bootstrapIfNeeded = true)
    {
        LastGen5FinalizationResult = RunGen5HereticOrthodoxyCommitAndFinalize250YearHistoryCore(bootstrapIfNeeded);
    }

    /// <summary>
    /// 第5世代「異端魔法の正統化」を手動 Commit し、400年周期適用と250年正史ツリーを確定します。
    /// </summary>
    public static Gen5FinalizationResult RunGen5HereticOrthodoxyCommitAndFinalize250YearHistoryCore(
        bool bootstrapIfNeeded = true)
    {
        Gen5FinalizationResult result = new Gen5FinalizationResult
        {
            finalTurn = ManualGen5CommitTurn,
            midCycleTurns = EnvironmentBiorhythmEngine.MidCycleTurns,
            committedGen5BranchId = ManualGen5BranchId,
            committedGen5BranchLabel = ManualGen5BranchLabel,
            committedGen5Score = ManualGen5ExpectedScore
        };

        AwaitingManualBranchSelection = false;
        PendingBranchRankings = new List<TimelineScriptEvaluationResult>();

        try
        {
            TimelineGenerationLoopEngine.EnsureInstance();
            TurnTransitionEngine.EnsureInstance();
            AutoBranchGenerator.EnsureInstance();
            TimelineScriptEvaluator.EnsureInstance();
            HistoryBranchManager mgr = HistoryBranchManager.EnsureInstance();
            HistoryFlagRegistry.EnsureWired();
            MasterDataManager.EnsureInstance();
            EnvironmentBiorhythmEngine.EnsureInstance();

            result.biorhythmApplied = EnvironmentBiorhythmEngine.ApplyMidCycle400YearModel(persistConfig: true);
            EraContextResolver.SyncEnvironmentBiorhythm(ManualGen5CommitTurn);

            if (bootstrapIfNeeded)
            {
                EnsureGen5HereticOrthodoxyBranchAvailable(mgr);
            }
            else if (mgr.FindRegisteredBranchForVerification(ManualGen5BranchId) == null)
            {
                EnsureGen5HereticOrthodoxyBranchAvailable(mgr);
            }

            List<TimelineScriptEvaluationResult> gen5Rankings =
                PendingBranchRankings != null && PendingBranchRankings.Count > 0
                    ? PendingBranchRankings
                    : TimelineScriptEvaluator.EvaluateAllBranchesAtCurrentTurn(ManualGen5CommitTurn);

            SyncToTurn(ManualGen5CommitTurn);

            string resolvedBranchId = ResolveGen5ManualBranchId(ManualGen5BranchId, mgr, gen5Rankings);
            if (string.IsNullOrWhiteSpace(resolvedBranchId))
            {
                result.usedFallback = true;
                resolvedBranchId = ManualGen5BranchId;
            }

            int commitScore = ResolveManualBranchScore(
                resolvedBranchId,
                ManualGen5CommitTurn,
                ManualGen5ExpectedScore,
                mgr,
                gen5Rankings);

            MainStoryCommitResult commit = HistoryBranchManager.CommitBranchAsMainStory(
                resolvedBranchId,
                commitScore,
                ManualGen5CommitTurn);

            if (!commit.success)
            {
                result.message = $"第5世代手動 Commit 失敗: {commit.message}";
                LastGen5FinalizationResult = result;
                return result;
            }

            result.committedGen5BranchId = commit.branchId;
            result.committedGen5BranchLabel = string.IsNullOrWhiteSpace(commit.branchName)
                ? ManualGen5BranchLabel
                : commit.branchName;
            result.committedGen5Score = commit.totalScore > 0 ? commit.totalScore : commitScore;

            SyncToTurn(ManualGen5CommitTurn);
            MicroToMacroAggregator aggregator = MicroToMacroAggregator.EnsureInstance();
            if (!aggregator.YearEndApplied)
            {
                aggregator.AggregateYearEnd(logMacro: false);
            }

            CanonTimelineExportResult export = HistoryBranchManager.TryExportCanonTimeline250(
                ManualGen5CommitTurn,
                EnvironmentBiorhythmEngine.MidCycleTurns,
                result.committedGen5Score);
            result.exportOk = export.success;
            result.geoWritebackOk = export.geoWritebackOk;
            result.gameMastersWritebackOk = export.gameMastersWritebackOk;
            result.lineage = export.lineage ?? new List<CanonTimelineLineageNode>();
            result.mainStoryLineageSummary = FormatLineageSummary(result.lineage);

            bool lineagePass = ValidateExpectedLineage(result.lineage);
            bool turnPass = ResolveCurrentTurn() >= ManualGen5CommitTurn &&
                            EraContextResolver.ResolveEraTag(ManualGen5CommitTurn) == EraTag.Late;
            bool biorhythmPass = result.biorhythmApplied &&
                                 EnvironmentBiorhythmEngine.MidCycleTurns == 400;

            result.message =
                $"gen5={result.committedGen5BranchId} score={result.committedGen5Score} " +
                $"turn=T{ManualGen5CommitTurn} midCycle={result.midCycleTurns} " +
                $"export={result.exportOk} geo={result.geoWritebackOk} masters={result.gameMastersWritebackOk} " +
                $"lineage={result.lineage.Count} lineageValidate={lineagePass} turnPass={turnPass}";

            Debug.Log(
                $"<color=#A5D6A7><b>{Gen5FinalCompleteLogTag}</b></color> " +
                $"第5世代メイン軸: 「{result.committedGen5BranchLabel}」({result.committedGen5Score}) を確定！ " +
                $"中周期:{result.midCycleTurns}年スケールにて250年史全域のタイムラインツリー（T1〜250）が正常にコミットされました。\n" +
                result.mainStoryLineageSummary);

            result.success = commit.success &&
                             mgr.HasCommittedMainStory &&
                             string.Equals(
                                 mgr.MainStoryBranchId,
                                 result.committedGen5BranchId,
                                 StringComparison.OrdinalIgnoreCase) &&
                             lineagePass &&
                             turnPass &&
                             biorhythmPass &&
                             result.exportOk &&
                             result.gameMastersWritebackOk;

            if (result.success)
            {
                AwaitingManualBranchSelection = false;
                PendingBranchRankings = new List<TimelineScriptEvaluationResult>();
                PendingParentBranchId = result.committedGen5BranchId;
            }

            LastGen5FinalizationResult = result;
            return result;
        }
        catch (Exception exception)
        {
            result.success = false;
            result.usedFallback = true;
            result.message = $"Safe-Fail: {exception.Message}";
            Debug.LogWarning(
                $"[TimelineGenerationLoopEngine] RunGen5HereticOrthodoxyCommitAndFinalize250YearHistory Safe-Fail: {exception.Message}");

            try
            {
                TimelineCharacterSelector.EnsureInstance().FallbackToSafeAvatar(exception.Message);
            }
            catch (Exception fallbackException)
            {
                result.message += $" / fallback failed: {fallbackException.Message}";
            }

            LastGen5FinalizationResult = result;
            return result;
        }
    }

    /// <summary>
    /// 250年正史を引き継ぎ、T251〜T1000 を 50 ターン世代 × MAGI 合議で自動進行します。
    /// 全会一致時は自動 Commit、審議割れ時は手動選定待ちで停止します。
    /// </summary>
    public static MagiChroniclePipelineResult Run1000YearMagiChroniclePipeline(
        bool bootstrapCanon250 = true,
        int branchCountOverride = 0)
    {
        MagiChroniclePipelineResult pipeline = new MagiChroniclePipelineResult();
        StringBuilder log = new StringBuilder();

        try
        {
            EnsureInstance();
            MagiSystemDecisionEngine.EnsureInstance();
            AutoBranchGenerator.EnsureInstance();
            TimelineScriptEvaluator.EnsureInstance();
            HistoryBranchManager mgr = HistoryBranchManager.EnsureInstance();
            HistoryFlagRegistry.EnsureWired();
            EnvironmentBiorhythmEngine.EnsureInstance();
            MagicCivilizationEngine.EnsureInstance();
            SingularBeastManager.EnsureInstance();

            AwaitingManualBranchSelection = false;
            PendingBranchRankings = new List<TimelineScriptEvaluationResult>();

            if (bootstrapCanon250)
            {
                Gen5FinalizationResult canon =
                    RunGen5HereticOrthodoxyCommitAndFinalize250YearHistoryCore(bootstrapIfNeeded: true);
                if (!canon.success)
                {
                    pipeline.success = false;
                    pipeline.message = $"250年正史 bootstrap 失敗: {canon.message}";
                    log.AppendLine(pipeline.message);
                    WriteMagiChroniclePipelineLog(pipeline, log.ToString());
                    LastMagiChroniclePipelineResult = pipeline;
                    return pipeline;
                }
            }
            else if (!mgr.HasCommittedMainStory)
            {
                pipeline.success = false;
                pipeline.message = "Safe-Fail: 250年正史が未確定 — bootstrapCanon250=true が必要です";
                log.AppendLine(pipeline.message);
                WriteMagiChroniclePipelineLog(pipeline, log.ToString());
                LastMagiChroniclePipelineResult = pipeline;
                return pipeline;
            }

            EnvironmentBiorhythmEngine.ApplyMidCycle400YearModel(persistConfig: false);
            ChronicleDensityExpansionEngine.SetupIntegratedAPlusBPipeline(logCompletion: true);

            SyncToChronicleTurn(ManualGen5CommitTurn);
            pipeline.finalMainStoryBranchId = mgr.MainStoryBranchId ?? string.Empty;
            pipeline.finalTurn = ManualGen5CommitTurn;

            int generation = 0;
            for (int startTurn = PostCanonStartTurn;
                 startTurn <= ChronicleCompletionTurn;
                 startTurn += ChronicleGenerationSpan)
            {
                generation++;
                int endTurn = Mathf.Min(startTurn + ChronicleGenerationSpan - 1, ChronicleCompletionTurn);

                SyncToChronicleTurn(startTurn);
                ChronicleDensityExpansionEngine.ApplyDensityRulesForChronicleGeneration(startTurn, endTurn, log);

                int branchCount = branchCountOverride > 0
                    ? Mathf.Clamp(branchCountOverride, MinPostCanonBranchCount, MaxPostCanonBranchCount)
                    : ResolvePostCanonBranchCount(generation);

                AutoBranchGenerationResult gen = AutoBranchGenerator.GenerateBranchesForChronicleEvaluation(
                    startTurn,
                    endTurn,
                    branchCount,
                    mgr.MainStoryBranchId);

                if (!gen.success || gen.generatedCount == 0)
                {
                    pipeline.success = false;
                    pipeline.generationsProcessed = generation;
                    pipeline.finalTurn = endTurn;
                    pipeline.message = $"T{endTurn} IF 生成失敗: {gen.message}";
                    log.AppendLine(pipeline.message);
                    LogMagiChronicleBatchStep(endTurn, "SafeFailFallback", string.Empty);
                    WriteMagiChroniclePipelineLog(pipeline, log.ToString());
                    LastMagiChroniclePipelineResult = pipeline;
                    return pipeline;
                }

                List<HistoryTimelineBranch> candidates =
                    CollectGeneratedBranchCandidates(gen.generatedBranchIds, mgr);
                if (candidates.Count == 0)
                {
                    pipeline.success = false;
                    pipeline.generationsProcessed = generation;
                    pipeline.finalTurn = endTurn;
                    pipeline.message = $"T{endTurn} Safe-Fail: MAGI 審議候補 0 件";
                    log.AppendLine(pipeline.message);
                    LogMagiChronicleBatchStep(endTurn, "SafeFailFallback", string.Empty);
                    WriteMagiChroniclePipelineLog(pipeline, log.ToString());
                    LastMagiChroniclePipelineResult = pipeline;
                    return pipeline;
                }

                MagiDeliberationResult deliberation =
                    MagiSystemDecisionEngine.EvaluateAndProposeBranch(candidates, endTurn);

                EnvironmentBiorhythmSnapshot endBio =
                    EnvironmentBiorhythmEngine.ResolveMidCyclePhase(endTurn);
                MagiChroniclePipelineStepResult step = new MagiChroniclePipelineStepResult
                {
                    generation = generation,
                    startTurn = startTurn,
                    endTurn = endTurn,
                    branchCount = branchCount,
                    status = deliberation.status,
                    selectedBranchId = ResolveSelectedBranchLabel(deliberation, mgr),
                    continued = deliberation.status == MagiDeliberationStatus.AllAgreed,
                    biorhythmPhase = endBio.Phase.ToString(),
                    positionInCycle = endBio.PositionInCycle,
                    message = deliberation.message ?? string.Empty
                };
                pipeline.steps.Add(step);

                string statusLabel = FormatMagiStatusLabel(deliberation.status);
                LogMagiChronicleBatchStep(endTurn, statusLabel, step.selectedBranchId);
                log.AppendLine(
                    $"gen={generation} T{startTurn}-T{endTurn} branches={branchCount} " +
                    $"status={statusLabel} selected={step.selectedBranchId} " +
                    $"bio={step.biorhythmPhase} pos={step.positionInCycle} msg={step.message}");

                if (deliberation.status == MagiDeliberationStatus.Disagreed)
                {
                    pipeline.success = true;
                    pipeline.haltedForManualSelection = true;
                    pipeline.generationsProcessed = generation;
                    pipeline.finalTurn = endTurn;
                    pipeline.finalMainStoryBranchId = mgr.MainStoryBranchId ?? string.Empty;
                    pipeline.message =
                        $"T{endTurn} 審議割れ — 手動選定待ち (AwaitingManualBranchSelection=true)";
                    WriteMagiChroniclePipelineLog(pipeline, log.ToString());
                    LastMagiChroniclePipelineResult = pipeline;
                    return pipeline;
                }

                if (deliberation.status == MagiDeliberationStatus.SafeFailFallback ||
                    !deliberation.success)
                {
                    pipeline.success = false;
                    pipeline.generationsProcessed = generation;
                    pipeline.finalTurn = endTurn;
                    pipeline.message = $"T{endTurn} MAGI Safe-Fail: {deliberation.message}";
                    WriteMagiChroniclePipelineLog(pipeline, log.ToString());
                    LastMagiChroniclePipelineResult = pipeline;
                    return pipeline;
                }

                SyncToChronicleTurn(endTurn);
                ChronicleDensityExpansionEngine.ApplyDensityRulesForChronicleGeneration(endTurn, endTurn, log);
                pipeline.finalMainStoryBranchId = mgr.MainStoryBranchId ?? string.Empty;
                pipeline.finalTurn = endTurn;
                pipeline.generationsProcessed = generation;

                if (endTurn >= ChronicleCompletionTurn)
                {
                    pipeline.success = true;
                    pipeline.completedToTurn1000 = true;
                    pipeline.message =
                        $"1000年史完成域到達 — 最終正史枝={pipeline.finalMainStoryBranchId}";
                    LogMagiChronicleBatchStep(
                        ChronicleCompletionTurn,
                        "Complete",
                        pipeline.finalMainStoryBranchId);
                    log.AppendLine(pipeline.message);
                    WriteMagiChroniclePipelineLog(pipeline, log.ToString());
                    LastMagiChroniclePipelineResult = pipeline;
                    return pipeline;
                }
            }

            pipeline.success = true;
            pipeline.message = $"パイプライン完了 T{pipeline.finalTurn}";
            WriteMagiChroniclePipelineLog(pipeline, log.ToString());
            LastMagiChroniclePipelineResult = pipeline;
            return pipeline;
        }
        catch (Exception exception)
        {
            pipeline.success = false;
            pipeline.message = $"Safe-Fail: {exception.Message}";
            log.AppendLine(pipeline.message);
            Debug.LogWarning(
                $"[TimelineGenerationLoopEngine] Run1000YearMagiChroniclePipeline Safe-Fail: {exception.Message}");
            WriteMagiChroniclePipelineLog(pipeline, log.ToString());
            LastMagiChroniclePipelineResult = pipeline;
            return pipeline;
        }
    }

    /// <summary>
    /// ターン1001〜1050（第21世代・文明復興）の IF 枝を一括生成し、
    /// MAGI（スクルド剪定理論）で合議 → 最高スコア枝を正史コミット → T1051 へ進行します。
    /// </summary>
    public static CivilizationRevivalBatchResult RunCivilizationRevival50YearsAndCommit()
    {
        CivilizationRevivalBatchResult result = new CivilizationRevivalBatchResult();
        try
        {
            EnsureInstance();
            MagiSystemDecisionEngine.EnsureInstance();
            HistoryBranchManager mgr = HistoryBranchManager.EnsureInstance();
            HistoryFlagRegistry.EnsureWired();
            EnvironmentBiorhythmEngine.EnsureInstance();
            EnvironmentBiorhythmEngine.ResetRevivalTransitionLogForVerification();
            TurnTransitionEngine.EnsureInstance();
            GameTimeManager timeMgr = GameTimeManager.EnsureInstance();
            SkuldPruningTheoryManager.EnsureInstance();

            AwaitingManualBranchSelection = false;
            PendingBranchRankings = new List<TimelineScriptEvaluationResult>();

            int startTurn = CivilizationRevivalStartTurn;
            int endTurn = CivilizationRevivalEndTurn;
            int nextTurn = CivilizationRevivalNextTurn;
            result.startTurn = startTurn;
            result.endTurn = endTurn;
            result.nextTurn = nextTurn;

            timeMgr.SetYear(startTurn);
            EnvironmentBiorhythmEngine.TryLogCivilizationRevivalTransition(startTurn);

            string parentId = mgr.MainStoryBranchId;
            if (string.IsNullOrWhiteSpace(parentId) || HistoryBranchManager.IsCanonBranchKey(parentId))
            {
                HistoryTimelineBranch anchor = BuildCivilizationRevivalAnchorBranch(startTurn - 1);
                mgr.UpsertRegisteredBranchForVerification(anchor);
                HistoryBranchManager.CommitBranchAsMainStory(anchor.branchId, 200, startTurn - 1);
                parentId = anchor.branchId;
            }

            List<HistoryTimelineBranch> candidates =
                BuildCivilizationRevivalBranchBlueprints(parentId, startTurn, endTurn);
            for (int i = 0; i < candidates.Count; i++)
            {
                mgr.UpsertRegisteredBranchForVerification(candidates[i]);
            }

            result.generatedBranchCount = candidates.Count;
            if (candidates.Count == 0)
            {
                result.success = false;
                result.message = "Safe-Fail: 文明復興 IF 枝が 0 件";
                LastCivilizationRevivalBatchResult = result;
                WriteCivilizationRevivalPipelineLog(result);
                return result;
            }

            MagiDeliberationResult deliberation =
                MagiSystemDecisionEngine.EvaluateAndProposeBranch(candidates, endTurn);
            result.deliberationStatus = deliberation.status;

            MagiUnitVote skuldVote = FindSkuldVote(deliberation);
            string bestId = ResolveSelectedBranchLabel(deliberation, mgr);
            int bestScore = skuldVote != null ? skuldVote.score : 0;

            if (string.IsNullOrWhiteSpace(bestId))
            {
                bestId = candidates[0].branchId;
            }

            HistoryTimelineBranch bestBranch = mgr.FindRegisteredBranchForVerification(bestId)
                                              ?? FindCandidateById(candidates, bestId);
            if (bestBranch != null)
            {
                SkuldPossibilityAnalysis analysis =
                    SkuldEvaluator.AnalyzePossibilityTree(bestBranch, endTurn);
                bestScore = Mathf.Max(bestScore, analysis.totalScore);
                bestScore = Mathf.Clamp(bestScore, 0, SkuldEvaluator.MaxPossibilityScore);
            }

            bool alreadyCommitted =
                deliberation.status == MagiDeliberationStatus.AllAgreed &&
                deliberation.commitResult != null &&
                deliberation.commitResult.success &&
                string.Equals(
                    deliberation.commitResult.branchId,
                    bestId,
                    StringComparison.OrdinalIgnoreCase);

            MainStoryCommitResult commit = alreadyCommitted
                ? deliberation.commitResult
                : HistoryBranchManager.CommitBranchAsMainStory(bestId, bestScore, endTurn);

            if (!commit.success)
            {
                result.success = false;
                result.bestBranchId = bestId;
                result.skuldPruningScore = bestScore;
                result.message = $"Safe-Fail: 正史コミット失敗 — {commit.message}";
                LastCivilizationRevivalBatchResult = result;
                WriteCivilizationRevivalPipelineLog(result);
                return result;
            }

            result.bestBranchId = commit.branchId;
            result.bestBranchName = bestBranch?.displayName ?? commit.branchId;
            result.skuldPruningScore = commit.totalScore > 0 ? commit.totalScore : bestScore;
            AwaitingManualBranchSelection = false;

            TurnTransitionResult transition =
                TurnTransitionEngine.AdvanceCivilizationRevivalYear(nextTurn);
            result.nextTurn = transition.newTurn > 0 ? transition.newTurn : nextTurn;

            string exportPath;
            result.candidatesExported =
                MagiSystemDecisionEngine.ExportBranchCandidatesForPipeline(
                    candidates,
                    result.nextTurn,
                    out exportPath);
            result.candidatesPath = exportPath ?? string.Empty;

            result.success = true;
            result.message =
                $"{CivilizationRevivalBatchLogTag} ターン{startTurn}〜{endTurn}の復興IF枝を量産・合議完了！ " +
                $"正史確定軸: 「{result.bestBranchId}」 (剪定理論スコア: {result.skuldPruningScore}) " +
                $"➔ ターン{result.nextTurn}へ移行しました。";

            Debug.Log($"<color=#80DEEA><b>{result.message}</b></color>");
            WriteCivilizationRevivalPipelineLog(result);
            LastCivilizationRevivalBatchResult = result;
            return result;
        }
        catch (Exception exception)
        {
            result.success = false;
            result.message = $"Safe-Fail: {exception.Message}";
            Debug.LogWarning(
                $"[TimelineGenerationLoopEngine] RunCivilizationRevival50YearsAndCommit Safe-Fail: {exception.Message}");
            WriteCivilizationRevivalPipelineLog(result);
            LastCivilizationRevivalBatchResult = result;
            return result;
        }
    }

    public static TimelineGenerationLoopVerifyResult RunFinalize250YearVerification()
    {
        TimelineGenerationLoopVerifyResult verify = new TimelineGenerationLoopVerifyResult();
        StringBuilder log = new StringBuilder();
        GameObject puppetObject = null;

        try
        {
            SimulationVerifyBootstrap.PrepareFreshStoryDay(1, GameMode.StoryMode);
            HistoryFlagRegistry.EnsureWired();
            HistoryBranchManager.EnsureInstance().ClearRegisteredBranches();
            Generation2DailyIfEngine.ResetSessionForVerification();
            MasterDataManager.EnsureInstance();

            TimelineCharacterSelector selector = TimelineCharacterSelector.EnsureInstance();
            puppetObject = new GameObject("TimelineFinalize250VerifyPuppet");
            CombatStats puppetCombat = puppetObject.AddComponent<CombatStats>();
            PlayerStats puppetStamina = puppetObject.AddComponent<PlayerStats>();
            PlayerStatusManager puppetStatus = puppetObject.AddComponent<PlayerStatusManager>();
            selector.BindPlayerPuppet(puppetCombat, puppetStamina, puppetStatus);

            Gen5FinalizationResult finalization =
                RunGen5HereticOrthodoxyCommitAndFinalize250YearHistoryCore(bootstrapIfNeeded: true);
            log.AppendLine($"finalize: success={finalization.success} {finalization.message}");

            HistoryBranchManager mgr = HistoryBranchManager.EnsureInstance();
            bool commitPass = mgr.HasCommittedMainStory &&
                              string.Equals(
                                  mgr.MainStoryBranchId,
                                  ManualGen5BranchId,
                                  StringComparison.OrdinalIgnoreCase);
            bool scorePass = finalization.committedGen5Score >= ManualGen5ExpectedScore - 15;
            bool biorhythmPass = finalization.biorhythmApplied &&
                                 EnvironmentBiorhythmEngine.MidCycleTurns == 400;
            bool lineageCountPass = finalization.lineage != null && finalization.lineage.Count >= 5;
            bool lineageValidatePass = ValidateExpectedLineage(finalization.lineage);
            bool exportPass = finalization.exportOk && finalization.gameMastersWritebackOk;
            bool awaitingClearPass = !AwaitingManualBranchSelection;
            bool turnPass = EraContextResolver.CurrentTurn >= ManualGen5CommitTurn;

            log.AppendLine(
                $"checks: commit={commitPass} score={scorePass} biorhythm={biorhythmPass} " +
                $"lineageCount={lineageCountPass} lineageValidate={lineageValidatePass} " +
                $"export={exportPass} awaiting={awaitingClearPass} turn={turnPass}");
            log.AppendLine($"lineage:\n{finalization.mainStoryLineageSummary}");
            log.AppendLine($"main={mgr.MainStoryBranchId} midCycle={EnvironmentBiorhythmEngine.MidCycleTurns}");

            verify.success = finalization.success && commitPass && scorePass && biorhythmPass &&
                             lineageCountPass && lineageValidatePass && exportPass &&
                             awaitingClearPass && turnPass;
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

        WriteFinalize250VerifyLog(verify);
        return verify;
    }

    public static TimelineGenerationLoopVerifyResult RunBatchGen3Verification()
    {
        TimelineGenerationLoopVerifyResult verify = new TimelineGenerationLoopVerifyResult();
        StringBuilder log = new StringBuilder();
        GameObject puppetObject = null;

        try
        {
            SimulationVerifyBootstrap.PrepareFreshStoryDay(1, GameMode.StoryMode);
            HistoryFlagRegistry.EnsureWired();
            HistoryFlagRegistry.ClearForTesting();
            HistoryBranchManager.EnsureInstance().ClearRegisteredBranches();
            Generation2DailyIfEngine.ResetSessionForVerification();
            MasterDataManager.EnsureInstance();

            TimelineCharacterSelector selector = TimelineCharacterSelector.EnsureInstance();
            puppetObject = new GameObject("TimelineBatchGen3VerifyPuppet");
            CombatStats puppetCombat = puppetObject.AddComponent<CombatStats>();
            PlayerStats puppetStamina = puppetObject.AddComponent<PlayerStats>();
            PlayerStatusManager puppetStatus = puppetObject.AddComponent<PlayerStatusManager>();
            selector.BindPlayerPuppet(puppetCombat, puppetStamina, puppetStatus);

            RunGeneration2ManualCommitAndAdvanceToGen3(bootstrapIfNeeded: true);

            BatchGenerationEraResult batch = RunBatchGenerationAndEvaluateNextEraCore(
                Generation2TransitionEngine.Generation3StartTurn,
                Generation2TransitionEngine.Generation3EndTurn,
                ManualGen2BranchId);

            log.AppendLine($"batch: success={batch.success} {batch.message}");

            HistoryBranchManager mgr = HistoryBranchManager.EnsureInstance();
            TimelineScriptEvaluationResult topAlt = SelectTopAlt(batch.rankings);
            bool genPass = batch.generatedBranchCount >= DefaultGen3BranchCount;
            bool evalPass = batch.evaluatedBranchCount >= 6;
            bool awaitingPass = AwaitingManualBranchSelection && batch.awaitingManualSelection;
            bool mainUnchangedPass = mgr.HasCommittedMainStory &&
                                     string.Equals(
                                         mgr.MainStoryBranchId,
                                         ManualGen2BranchId,
                                         StringComparison.OrdinalIgnoreCase);
            bool scorePass = topAlt != null && topAlt.totalScore > TimelineScriptEvaluator.SafeFailTotalScore;
            bool possessPass = selector.PossessedNpc != null || selector.IsUsingSafeFallback;
            bool parentPass = string.Equals(
                batch.parentBranchId,
                ManualGen2BranchId,
                StringComparison.OrdinalIgnoreCase);

            log.AppendLine(
                $"checks: gen={genPass} eval={evalPass} awaiting={awaitingPass} main={mainUnchangedPass} " +
                $"score={scorePass} possess={possessPass} parent={parentPass}");
            log.AppendLine(
                $"top={topAlt?.branchId} score={topAlt?.totalScore} main={mgr.MainStoryBranchId} " +
                $"hero={batch.protagonistName} pending={PendingBranchRankings?.Count ?? 0}");

            verify.success = batch.success && genPass && evalPass && awaitingPass &&
                             mainUnchangedPass && scorePass && possessPass && parentPass;
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

        WriteBatchVerifyLog(verify);
        return verify;
    }

    public static TimelineGenerationLoopVerifyResult RunBatchGen4Verification()
    {
        TimelineGenerationLoopVerifyResult verify = new TimelineGenerationLoopVerifyResult();
        StringBuilder log = new StringBuilder();
        GameObject puppetObject = null;

        try
        {
            SimulationVerifyBootstrap.PrepareFreshStoryDay(1, GameMode.StoryMode);
            HistoryFlagRegistry.EnsureWired();
            HistoryFlagRegistry.ClearForTesting();
            HistoryBranchManager.EnsureInstance().ClearRegisteredBranches();
            Generation2DailyIfEngine.ResetSessionForVerification();
            MasterDataManager.EnsureInstance();

            TimelineCharacterSelector selector = TimelineCharacterSelector.EnsureInstance();
            puppetObject = new GameObject("TimelineBatchGen4VerifyPuppet");
            CombatStats puppetCombat = puppetObject.AddComponent<CombatStats>();
            PlayerStats puppetStamina = puppetObject.AddComponent<PlayerStats>();
            PlayerStatusManager puppetStatus = puppetObject.AddComponent<PlayerStatusManager>();
            selector.BindPlayerPuppet(puppetCombat, puppetStamina, puppetStatus);

            Gen4TransitionBatchResult batch = RunGen3DemonFrontCommitAndBatchGen4EvaluationCore(bootstrapIfNeeded: true);
            log.AppendLine($"gen4: success={batch.success} {batch.message}");

            HistoryBranchManager mgr = HistoryBranchManager.EnsureInstance();
            TimelineScriptEvaluationResult topAlt = SelectTopAlt(batch.rankings);
            bool commitPass = mgr.HasCommittedMainStory &&
                              string.Equals(
                                  mgr.MainStoryBranchId,
                                  ManualGen3BranchId,
                                  StringComparison.OrdinalIgnoreCase);
            bool scorePass = batch.committedGen3Score >= ManualGen3ExpectedScore - 15;
            bool turnPass = batch.newTurn >= Generation2TransitionEngine.Generation4StartTurn &&
                            EraContextResolver.ResolveEraTag(batch.newTurn) == EraTag.Late;
            bool genPass = batch.generatedBranchCount >= DefaultGen4BranchCount;
            bool evalPass = batch.evaluatedBranchCount >= 6;
            bool awaitingPass = AwaitingManualBranchSelection && batch.awaitingManualSelection;
            bool possessPass = selector.PossessedNpc != null || selector.IsUsingSafeFallback;
            bool parentPass = string.Equals(PendingParentBranchId, ManualGen3BranchId, StringComparison.OrdinalIgnoreCase);

            log.AppendLine(
                $"checks: commit={commitPass} score={scorePass} turn={turnPass} gen={genPass} eval={evalPass} " +
                $"awaiting={awaitingPass} possess={possessPass} parent={parentPass}");
            log.AppendLine(
                $"main={mgr.MainStoryBranchId} gen3Score={batch.committedGen3Score} top={topAlt?.branchId} " +
                $"topScore={topAlt?.totalScore} hero={batch.protagonistName} job={batch.protagonistJobId}");

            verify.success = batch.success && commitPass && scorePass && turnPass && genPass &&
                             evalPass && awaitingPass && possessPass && parentPass;
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

        WriteBatchGen4VerifyLog(verify);
        return verify;
    }

    public static TimelineGenerationLoopVerifyResult RunBatchGen5Verification()
    {
        TimelineGenerationLoopVerifyResult verify = new TimelineGenerationLoopVerifyResult();
        StringBuilder log = new StringBuilder();
        GameObject puppetObject = null;

        try
        {
            SimulationVerifyBootstrap.PrepareFreshStoryDay(1, GameMode.StoryMode);
            HistoryFlagRegistry.EnsureWired();
            HistoryFlagRegistry.ClearForTesting();
            HistoryBranchManager.EnsureInstance().ClearRegisteredBranches();
            Generation2DailyIfEngine.ResetSessionForVerification();
            MasterDataManager.EnsureInstance();

            TimelineCharacterSelector selector = TimelineCharacterSelector.EnsureInstance();
            puppetObject = new GameObject("TimelineBatchGen5VerifyPuppet");
            CombatStats puppetCombat = puppetObject.AddComponent<CombatStats>();
            PlayerStats puppetStamina = puppetObject.AddComponent<PlayerStats>();
            PlayerStatusManager puppetStatus = puppetObject.AddComponent<PlayerStatusManager>();
            selector.BindPlayerPuppet(puppetCombat, puppetStamina, puppetStatus);

            Gen5TransitionBatchResult batch = RunGen4HereticArcanaCommitAndBatchGen5EvaluationCore(bootstrapIfNeeded: true);
            log.AppendLine($"gen5: success={batch.success} {batch.message}");

            HistoryBranchManager mgr = HistoryBranchManager.EnsureInstance();
            TimelineScriptEvaluationResult topAlt = SelectTopAlt(batch.rankings);
            bool commitPass = mgr.HasCommittedMainStory &&
                              string.Equals(
                                  mgr.MainStoryBranchId,
                                  ManualGen4BranchId,
                                  StringComparison.OrdinalIgnoreCase);
            bool scorePass = batch.committedGen4Score >= ManualGen4ExpectedScore - 15;
            bool turnPass = batch.newTurn >= Generation2TransitionEngine.Generation5StartTurn &&
                            IsAncientEraAtTurn(batch.newTurn);
            bool genPass = batch.generatedBranchCount >= DefaultGen5BranchCount;
            bool evalPass = batch.evaluatedBranchCount >= 6;
            bool awaitingPass = AwaitingManualBranchSelection && batch.awaitingManualSelection;
            bool possessPass = selector.PossessedNpc != null || selector.IsUsingSafeFallback;
            bool parentPass = string.Equals(PendingParentBranchId, ManualGen4BranchId, StringComparison.OrdinalIgnoreCase);
            bool endTurnPass = batch.targetEndTurn == Generation2TransitionEngine.Generation5EndTurn;

            log.AppendLine(
                $"checks: commit={commitPass} score={scorePass} turn={turnPass} gen={genPass} eval={evalPass} " +
                $"awaiting={awaitingPass} possess={possessPass} parent={parentPass} endTurn={endTurnPass}");
            log.AppendLine(
                $"main={mgr.MainStoryBranchId} gen4Score={batch.committedGen4Score} top={topAlt?.branchId} " +
                $"topScore={topAlt?.totalScore} hero={batch.protagonistName} job={batch.protagonistJobId}");

            verify.success = batch.success && commitPass && scorePass && turnPass && genPass &&
                             evalPass && awaitingPass && possessPass && parentPass && endTurnPass;
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

        WriteBatchGen5VerifyLog(verify);
        return verify;
    }

    public static TimelineGenerationLoopVerifyResult RunVerification()
    {
        TimelineGenerationLoopVerifyResult verify = new TimelineGenerationLoopVerifyResult();
        StringBuilder log = new StringBuilder();
        GameObject puppetObject = null;

        try
        {
            SimulationVerifyBootstrap.PrepareFreshStoryDay(1, GameMode.StoryMode);
            HistoryFlagRegistry.EnsureWired();
            HistoryFlagRegistry.ClearForTesting();
            HistoryBranchManager.EnsureInstance().ClearRegisteredBranches();
            Generation2DailyIfEngine.ResetSessionForVerification();
            MasterDataManager.EnsureInstance();

            TimelineCharacterSelector selector = TimelineCharacterSelector.EnsureInstance();
            puppetObject = new GameObject("TimelineGenerationLoopVerifyPuppet");
            CombatStats puppetCombat = puppetObject.AddComponent<CombatStats>();
            PlayerStats puppetStamina = puppetObject.AddComponent<PlayerStats>();
            PlayerStatusManager puppetStatus = puppetObject.AddComponent<PlayerStatusManager>();
            selector.BindPlayerPuppet(puppetCombat, puppetStamina, puppetStatus);

            TimelineGenerationLoopResult loop = RunGeneration2CommitAndAdvanceToGen3(bootstrapIfNeeded: true);
            log.AppendLine($"loop: success={loop.success} {loop.message}");

            HistoryBranchManager mgr = HistoryBranchManager.EnsureInstance();
            TimelineScriptEvaluationResult topAlt = SelectTopAlt(loop.rankings);
            bool gen2EvalPass = loop.gen2BestScore > TimelineScriptEvaluator.SafeFailTotalScore &&
                                loop.evaluatedBranchCount >= 7;
            bool childIncludedPass = BranchListed(loop.rankings, loop.dailyChildBranchId);
            bool commitPass = mgr.HasCommittedMainStory &&
                              string.Equals(
                                  mgr.MainStoryBranchId,
                                  loop.gen2BestBranchId,
                                  StringComparison.OrdinalIgnoreCase);
            bool topAltPass = topAlt != null &&
                              string.Equals(
                                  topAlt.branchId,
                                  loop.gen2BestBranchId,
                                  StringComparison.OrdinalIgnoreCase);
            bool turnPass = loop.newTurn == Generation2TransitionEngine.Generation3StartTurn &&
                            EraContextResolver.ResolveEraTag(loop.newTurn) == EraTag.Mid;
            bool possessPass = selector.PossessedNpc != null || selector.IsUsingSafeFallback;
            bool jobSync = selector.PossessedNpc != null &&
                           string.Equals(
                               puppetStatus.MainJob,
                               selector.PossessedNpc.JobId,
                               StringComparison.OrdinalIgnoreCase);
            bool hpSync = selector.PossessedNpc != null &&
                          puppetCombat.MaxHp == selector.PossessedNpc.Combat.MaxHp;
            bool mpSync = selector.PossessedNpc != null &&
                          Mathf.Abs(puppetStatus.MaxMP - selector.PossessedNpc.MaxMP) < 1f;

            log.AppendLine(
                $"checks: eval={gen2EvalPass} child={childIncludedPass} commit={commitPass} " +
                $"topAlt={topAltPass} turn={turnPass} possess={possessPass} job={jobSync} hp={hpSync} mp={mpSync}");
            log.AppendLine(
                $"top={topAlt?.branchId} score={topAlt?.totalScore} main={mgr.MainStoryBranchId} " +
                $"hero={loop.protagonistName} job={loop.protagonistJobId}");

            verify.success = loop.success && gen2EvalPass && childIncludedPass && commitPass &&
                             topAltPass && turnPass && possessPass && jobSync && hpSync && mpSync;
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

    public static TimelineGenerationLoopVerifyResult RunManualCommitVerification()
    {
        TimelineGenerationLoopVerifyResult verify = new TimelineGenerationLoopVerifyResult();
        StringBuilder log = new StringBuilder();
        GameObject puppetObject = null;

        try
        {
            SimulationVerifyBootstrap.PrepareFreshStoryDay(1, GameMode.StoryMode);
            HistoryFlagRegistry.EnsureWired();
            HistoryFlagRegistry.ClearForTesting();
            HistoryBranchManager.EnsureInstance().ClearRegisteredBranches();
            Generation2DailyIfEngine.ResetSessionForVerification();
            MasterDataManager.EnsureInstance();

            TimelineCharacterSelector selector = TimelineCharacterSelector.EnsureInstance();
            puppetObject = new GameObject("TimelineManualCommitVerifyPuppet");
            CombatStats puppetCombat = puppetObject.AddComponent<CombatStats>();
            PlayerStats puppetStamina = puppetObject.AddComponent<PlayerStats>();
            PlayerStatusManager puppetStatus = puppetObject.AddComponent<PlayerStatusManager>();
            selector.BindPlayerPuppet(puppetCombat, puppetStamina, puppetStatus);

            TimelineGenerationLoopResult loop = RunGeneration2ManualCommitAndAdvanceToGen3();
            log.AppendLine($"manual: success={loop.success} {loop.message}");

            HistoryBranchManager mgr = HistoryBranchManager.EnsureInstance();
            bool commitPass = mgr.HasCommittedMainStory &&
                              string.Equals(
                                  mgr.MainStoryBranchId,
                                  ManualGen2BranchId,
                                  StringComparison.OrdinalIgnoreCase);
            bool scorePass = loop.gen2BestScore >= ManualGen2ExpectedScore - 15;
            bool notAutoWinnerPass = !string.Equals(
                loop.gen2BestBranchId,
                "ALT_NATION_001_TURN_057_FACTION_WAR",
                StringComparison.OrdinalIgnoreCase);
            bool turnPass = loop.newTurn == Generation2TransitionEngine.Generation3StartTurn;
            bool possessPass = selector.PossessedNpc != null || selector.IsUsingSafeFallback;
            bool jobSync = selector.PossessedNpc != null &&
                           string.Equals(
                               puppetStatus.MainJob,
                               selector.PossessedNpc.JobId,
                               StringComparison.OrdinalIgnoreCase);
            bool hpSync = selector.PossessedNpc != null &&
                          puppetCombat.MaxHp == selector.PossessedNpc.Combat.MaxHp;
            bool mpSync = selector.PossessedNpc != null &&
                          Mathf.Abs(puppetStatus.MaxMP - selector.PossessedNpc.MaxMP) < 1f;

            log.AppendLine(
                $"checks: commit={commitPass} score={scorePass} notAuto1st={notAutoWinnerPass} " +
                $"turn={turnPass} possess={possessPass} job={jobSync} hp={hpSync} mp={mpSync}");
            log.AppendLine(
                $"main={mgr.MainStoryBranchId} score={loop.gen2BestScore} hero={loop.protagonistName} " +
                $"job={loop.protagonistJobId}");

            verify.success = loop.success && commitPass && scorePass && notAutoWinnerPass &&
                             turnPass && possessPass && jobSync && hpSync && mpSync;
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

        WriteManualVerifyLog(verify);
        return verify;
    }

    private static void BootstrapGeneration2Baseline(out string childBranchId)
    {
        childBranchId = string.Empty;
        AutoBranchGenerator.GenerateAndCommitPlan(
            1,
            Generation2TransitionEngine.Generation1EndTurn,
            Generation2TransitionEngine.DefaultGeneration1PlanIndex);
        Generation2TransitionEngine.BeginGeneration2();

        Generation2IfSpawnResult spawn = Generation2DailyIfEngine.TrySpawnBranchFromCraftIntervention(
            Generation2TransitionEngine.Generation2StartTurn,
            Generation2TransitionEngine.DefaultNationId,
            85f,
            "高純度魔力結晶");
        if (spawn.success)
        {
            childBranchId = spawn.branchId;
        }
    }

    private static string FindDailyChildBranchId(HistoryBranchManager mgr)
    {
        IReadOnlyList<HistoryTimelineBranch> branches = mgr.RegisteredBranches;
        for (int i = 0; i < branches.Count; i++)
        {
            HistoryTimelineBranch branch = branches[i];
            if (branch != null &&
                branch.branchId != null &&
                branch.branchId.IndexOf("NEW_TECH", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return branch.branchId;
            }
        }

        return string.Empty;
    }

    private static MagiUnitVote FindSkuldVote(MagiDeliberationResult deliberation)
    {
        if (deliberation?.votes == null)
        {
            return null;
        }

        for (int i = 0; i < deliberation.votes.Count; i++)
        {
            MagiUnitVote vote = deliberation.votes[i];
            if (vote != null &&
                string.Equals(vote.unitId, SkuldEvaluator.Id, StringComparison.OrdinalIgnoreCase))
            {
                return vote;
            }
        }

        return null;
    }

    private static HistoryTimelineBranch FindCandidateById(
        List<HistoryTimelineBranch> candidates,
        string branchId)
    {
        if (candidates == null || string.IsNullOrWhiteSpace(branchId))
        {
            return null;
        }

        for (int i = 0; i < candidates.Count; i++)
        {
            if (candidates[i] != null &&
                string.Equals(candidates[i].branchId, branchId, StringComparison.OrdinalIgnoreCase))
            {
                return candidates[i];
            }
        }

        return null;
    }

    private static HistoryTimelineBranch BuildCivilizationRevivalAnchorBranch(int turn)
    {
        int nationId = MicroToMacroAggregator.DefaultNationId;
        return new HistoryTimelineBranch
        {
            branchId = "ALT_REVIVAL_ANCHOR_T1000",
            parentBranchId = string.Empty,
            displayName = "IF_Line: 千年史完結アンカー",
            baseStartTurn = Mathf.Max(1, turn - 1),
            primaryNationId = nationId,
            nodes = new List<HistoryBranchNode>
            {
                new HistoryBranchNode
                {
                    turn = turn,
                    nationId = nationId,
                    keyEventId = "HIST_NATION_001_GEO_TURN_001_INNOVATION",
                    outcomeFlag = "ALT_NATION_001_OUTCOME_INNOVATION",
                    delta = new MacroParamDelta
                    {
                        powerMultiplier = 1.05f,
                        barrierEfficiencyDelta = 0.03f,
                        threatMultiplier = 0.95f
                    }
                }
            }
        };
    }

    /// <summary>文明復興 IF ブループリント（5〜8 本）を構築します。</summary>
    private static List<HistoryTimelineBranch> BuildCivilizationRevivalBranchBlueprints(
        string parentBranchId,
        int startTurn,
        int endTurn)
    {
        int nationId = MicroToMacroAggregator.DefaultNationId;
        string parent = parentBranchId ?? string.Empty;
        int midA = startTurn + 14;
        int midB = startTurn + 29;

        return new List<HistoryTimelineBranch>
        {
            BuildRevivalBranch(
                "ALT_REVIVAL_NEW_ARCANE_ACADEMIA", parent, "IF_Line: 新魔導学術",
                nationId, startTurn, midA, midB, endTurn,
                "HIST_NATION_001_GEO_TURN_001_INNOVATION", "ALT_NATION_001_OUTCOME_INNOVATION",
                "HIST_NATION_001_GEO_TURN_001_ACADEMY_RIVALRY", "ALT_NATION_001_OUTCOME_ACADEMY_SCHISM",
                1.16f, 0.08f, 0.88f, 1.20f),
            BuildRevivalBranch(
                "ALT_REVIVAL_ANCIENT_BARRIER_DECODE", parent, "IF_Line: 古代結界陣解読",
                nationId, startTurn, midA, midB, endTurn,
                "HIST_NATION_001_GEO_TURN_001_HERO", "ALT_NATION_001_OUTCOME_LOST_TECH_REVIVAL",
                "HIST_NATION_001_GEO_TURN_001_INNOVATION", "ALT_NATION_001_OUTCOME_INNOVATION",
                1.12f, 0.12f, 0.90f, 1.15f),
            BuildRevivalBranch(
                "ALT_REVIVAL_ROAD_AND_BEAST_TERRITORY", parent, "IF_Line: 街道開拓と魔獣の縄張り衝突",
                nationId, startTurn, midA, midB, endTurn,
                "HIST_NATION_001_GEO_TURN_001_MONSTER_DEFENSE", "ALT_NATION_001_OUTCOME_MONSTER_DEFENSE",
                "HIST_NATION_001_GEO_TURN_001_SUCCESSION", "ALT_NATION_001_OUTCOME_FRONTIER_EXPANSION",
                1.08f, 0.04f, 1.05f, 1.25f),
            BuildRevivalBranch(
                "ALT_REVIVAL_NEW_TRADE_SPHERE", parent, "IF_Line: 新交易圏形成",
                nationId, startTurn, midA, midB, endTurn,
                "HIST_NATION_001_GEO_TURN_001_SUCCESSION", "ALT_NATION_001_OUTCOME_FRONTIER_EXPANSION",
                "HIST_NATION_001_GEO_TURN_001_INNOVATION", "ALT_NATION_001_OUTCOME_INNOVATION",
                1.14f, 0.06f, 0.92f, 1.18f),
            BuildRevivalBranch(
                "ALT_REVIVAL_SHARED_INFRASTRUCTURE", parent, "IF_Line: 復興インフラ共有",
                nationId, startTurn, midA, midB, endTurn,
                "HIST_NATION_001_GEO_TURN_001_INNOVATION", "ALT_NATION_001_OUTCOME_INNOVATION",
                "HIST_NATION_001_GEO_TURN_001_HERO", "ALT_NATION_001_OUTCOME_LOST_TECH_REVIVAL",
                1.10f, 0.09f, 0.94f, 1.22f),
            BuildRevivalBranch(
                "ALT_REVIVAL_ACADEMY_GUILD_UNION", parent, "IF_Line: 学術ギルド連合",
                nationId, startTurn, midA, midB, endTurn,
                "HIST_NATION_001_GEO_TURN_001_ACADEMY_RIVALRY", "ALT_NATION_001_OUTCOME_ACADEMY_SCHISM",
                "HIST_NATION_001_GEO_TURN_001_INNOVATION", "ALT_NATION_001_OUTCOME_INNOVATION",
                1.13f, 0.07f, 0.91f, 1.28f),
            BuildRevivalBranch(
                "ALT_REVIVAL_BEAST_COEXISTENCE_PACT", parent, "IF_Line: 魔獣共生条約",
                nationId, startTurn, midA, midB, endTurn,
                "HIST_NATION_001_GEO_TURN_001_MONSTER_DEFENSE", "ALT_NATION_001_OUTCOME_MONSTER_DEFENSE",
                "HIST_NATION_001_GEO_TURN_001_HERO", "ALT_NATION_001_OUTCOME_LOST_TECH_REVIVAL",
                1.06f, 0.05f, 0.85f, 1.30f)
        };
    }

    private static HistoryTimelineBranch BuildRevivalBranch(
        string branchId,
        string parentBranchId,
        string displayName,
        int nationId,
        int t0,
        int t1,
        int t2,
        int t3,
        string keyA,
        string flagA,
        string keyB,
        string flagB,
        float power,
        float barrier,
        float threat,
        float branchingBias)
    {
        float p2 = power * (0.96f + 0.04f * branchingBias);
        float b2 = barrier * branchingBias;
        float th2 = threat / Mathf.Max(0.85f, branchingBias * 0.9f);

        return new HistoryTimelineBranch
        {
            branchId = branchId,
            parentBranchId = parentBranchId ?? string.Empty,
            displayName = displayName,
            baseStartTurn = t0,
            primaryNationId = nationId,
            nodes = new List<HistoryBranchNode>
            {
                new HistoryBranchNode
                {
                    turn = t0,
                    nationId = nationId,
                    keyEventId = keyA,
                    outcomeFlag = flagA,
                    delta = new MacroParamDelta
                    {
                        powerMultiplier = power,
                        barrierEfficiencyDelta = barrier,
                        threatMultiplier = threat
                    }
                },
                new HistoryBranchNode
                {
                    turn = t1,
                    nationId = nationId,
                    keyEventId = keyB,
                    outcomeFlag = flagB,
                    delta = new MacroParamDelta
                    {
                        powerMultiplier = p2,
                        barrierEfficiencyDelta = b2 * 0.85f,
                        threatMultiplier = th2
                    }
                },
                new HistoryBranchNode
                {
                    turn = t2,
                    nationId = nationId,
                    keyEventId = keyA,
                    outcomeFlag = flagA,
                    delta = new MacroParamDelta
                    {
                        powerMultiplier = power * 1.02f,
                        barrierEfficiencyDelta = barrier + 0.02f,
                        threatMultiplier = Mathf.Max(0.80f, threat * 0.95f)
                    }
                },
                new HistoryBranchNode
                {
                    turn = t3,
                    nationId = nationId,
                    keyEventId = keyB,
                    outcomeFlag = flagB,
                    delta = new MacroParamDelta
                    {
                        powerMultiplier = p2 * 1.03f,
                        barrierEfficiencyDelta = b2,
                        threatMultiplier = Mathf.Max(0.78f, th2 * 0.96f)
                    }
                }
            }
        };
    }

    private static void WriteCivilizationRevivalPipelineLog(CivilizationRevivalBatchResult result)
    {
        if (result == null)
        {
            return;
        }

        try
        {
            string unityProjectRoot = Directory.GetParent(Application.dataPath)?.FullName
                                      ?? Application.dataPath;
            string repoRoot = Directory.GetParent(unityProjectRoot)?.FullName ?? unityProjectRoot;
            string line =
                $"{DateTime.Now:yyyy-MM-ddTHH:mm:ss} success={result.success}\n{result.message}\n";

            string[] paths =
            {
                Path.Combine(repoRoot, "Logs", "pipeline_execution.log"),
                Path.Combine(unityProjectRoot, "Logs", "pipeline_execution.log")
            };

            for (int i = 0; i < paths.Length; i++)
            {
                string path = paths[i];
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                File.AppendAllText(path, line, Encoding.UTF8);
            }
        }
        catch (Exception exception)
        {
            Debug.LogWarning(
                $"[TimelineGenerationLoopEngine] 文明復興ログ書き出し Safe-Fail: {exception.Message}");
        }
    }

    private static void SyncToChronicleTurn(int turn)
    {
        int safeTurn = EraContextResolver.ClampSimulationTurn(turn);
        DailySimulationEngine.EnsureInstance().TryPrepareForChronicleTurn(safeTurn);
        EraContextResolver.TrySetCurrentTurn(Mathf.Min(safeTurn, EraContextResolver.MaxTurn));
        MicroToMacroAggregator aggregator = MicroToMacroAggregator.EnsureInstance();
        aggregator.PrepareTurnTransition(safeTurn, applyBranchAdjustments: true);
        aggregator.BeginYearBaseline();
        HistoryBranchManager.TryApplyToMacroStats(
            aggregator.Stats,
            safeTurn,
            Generation2TransitionEngine.DefaultNationId);
        StoryTimelineManager.EnsureInstance().TryBootstrapStoryYear(
            Mathf.Min(safeTurn, EraContextResolver.MaxTurn),
            GameMode.StoryMode);
    }

    private static int ResolvePostCanonBranchCount(int generationIndex)
    {
        int span = MaxPostCanonBranchCount - MinPostCanonBranchCount + 1;
        return MinPostCanonBranchCount + ((generationIndex - 1) % span);
    }

    private static List<HistoryTimelineBranch> CollectGeneratedBranchCandidates(
        IReadOnlyList<string> generatedBranchIds,
        HistoryBranchManager mgr)
    {
        List<HistoryTimelineBranch> candidates = new List<HistoryTimelineBranch>();
        if (generatedBranchIds == null || mgr == null)
        {
            return candidates;
        }

        HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < generatedBranchIds.Count; i++)
        {
            string branchId = generatedBranchIds[i];
            if (string.IsNullOrWhiteSpace(branchId) || !seen.Add(branchId))
            {
                continue;
            }

            HistoryTimelineBranch branch = mgr.FindRegisteredBranchForVerification(branchId);
            if (branch != null &&
                !HistoryBranchManager.IsCanonBranchKey(branch.branchId))
            {
                candidates.Add(branch);
            }
        }

        return candidates;
    }

    private static string ResolveSelectedBranchLabel(
        MagiDeliberationResult deliberation,
        HistoryBranchManager mgr)
    {
        if (deliberation == null)
        {
            return string.Empty;
        }

        if (deliberation.status == MagiDeliberationStatus.AllAgreed &&
            !string.IsNullOrWhiteSpace(deliberation.agreedBranchId))
        {
            return deliberation.agreedBranchId;
        }

        if (deliberation.commitResult != null &&
            !string.IsNullOrWhiteSpace(deliberation.commitResult.branchId))
        {
            return deliberation.commitResult.branchId;
        }

        if (deliberation.votes != null && deliberation.votes.Count > 0)
        {
            MagiUnitVote skuldVote = null;
            for (int i = 0; i < deliberation.votes.Count; i++)
            {
                if (string.Equals(
                        deliberation.votes[i]?.unitId,
                        SkuldEvaluator.Id,
                        StringComparison.OrdinalIgnoreCase))
                {
                    skuldVote = deliberation.votes[i];
                    break;
                }
            }

            skuldVote ??= deliberation.votes[0];
            return skuldVote?.recommendedBranchId ?? string.Empty;
        }

        return mgr?.MainStoryBranchId ?? string.Empty;
    }

    private static string FormatMagiStatusLabel(MagiDeliberationStatus status)
    {
        return status switch
        {
            MagiDeliberationStatus.AllAgreed => "AllAgreed",
            MagiDeliberationStatus.Disagreed => "Disagreed",
            _ => "SafeFailFallback"
        };
    }

    private static void LogMagiChronicleBatchStep(int currentTurn, string status, string selectedBranchId)
    {
        Debug.Log(
            $"<color=#B39DDB><b>{MagiChronicleBatchLogTag}</b></color> " +
            $"ターン{currentTurn}: [判定: {status}] 正史枝: {selectedBranchId ?? string.Empty}");
    }

    private static void WriteMagiChroniclePipelineLog(
        MagiChroniclePipelineResult pipeline,
        string detailLog)
    {
        try
        {
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
            string path = Path.Combine(projectRoot, "Logs", "magi_1000year_chronicle_pipeline.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? projectRoot);
            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"{DateTime.UtcNow:O} success={pipeline.success}");
            sb.AppendLine(
                $"completed1000={pipeline.completedToTurn1000} haltedManual={pipeline.haltedForManualSelection} " +
                $"gens={pipeline.generationsProcessed} finalTurn={pipeline.finalTurn} " +
                $"main={pipeline.finalMainStoryBranchId}");
            sb.AppendLine(pipeline.message ?? string.Empty);
            sb.AppendLine(detailLog ?? string.Empty);
            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        }
        catch (Exception exception)
        {
            Debug.LogWarning(
                $"[TimelineGenerationLoopEngine] 1000年史パイプラインログ書き込み Safe-Fail: {exception.Message}");
        }
    }

    private static void SyncToTurn(int turn)
    {
        EraContextResolver.TrySetCurrentTurn(turn);
        DailySimulationEngine daily = DailySimulationEngine.EnsureInstance();
        daily.TryPrepareForTurn(turn);
        MicroToMacroAggregator aggregator = MicroToMacroAggregator.EnsureInstance();
        aggregator.PrepareTurnTransition(turn, applyBranchAdjustments: true);
        aggregator.BeginYearBaseline();
        HistoryBranchManager.TryApplyToMacroStats(aggregator.Stats, turn, Generation2TransitionEngine.DefaultNationId);
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

    private static bool AdvanceToTargetTurn(int targetTurn, out bool usedFallback)
    {
        usedFallback = false;
        targetTurn = Mathf.Clamp(targetTurn, EraContextResolver.MinTurn, EraContextResolver.MaxTurn);
        int currentTurn = ResolveCurrentTurn();

        if (currentTurn == targetTurn)
        {
            SyncToTurn(targetTurn);
            HistoricalNpcCaster.SpawnHistoricalKeyCharacters(targetTurn);
            return true;
        }

        if (currentTurn > targetTurn)
        {
            usedFallback = true;
            DailySimulationEngine.EnsureInstance().TryPrepareForTurn(targetTurn);
            SyncToTurn(targetTurn);
            HistoricalNpcCaster.SpawnHistoricalKeyCharacters(targetTurn);
            return true;
        }

        int safety = 0;
        while (currentTurn < targetTurn && safety++ < 200)
        {
            SyncToTurn(currentTurn);
            MicroToMacroAggregator aggregator = MicroToMacroAggregator.EnsureInstance();
            if (!aggregator.YearEndApplied)
            {
                aggregator.AggregateYearEnd(logMacro: false);
            }

            TurnTransitionResult transition = TurnTransitionEngine.AdvanceToNextYear(fromDailyCompletion: false);
            if (transition.success && transition.advanced)
            {
                currentTurn = transition.newTurn;
            }
            else
            {
                usedFallback = true;
                currentTurn = Mathf.Min(currentTurn + 1, targetTurn);
                DailySimulationEngine.EnsureInstance().TryPrepareForTurn(currentTurn);
                SyncToTurn(currentTurn);
            }

            if (currentTurn >= targetTurn)
            {
                break;
            }
        }

        if (ResolveCurrentTurn() != targetTurn)
        {
            usedFallback = true;
            SyncToTurn(targetTurn);
        }

        HistoricalNpcCaster.SpawnHistoricalKeyCharacters(targetTurn);
        return ResolveCurrentTurn() >= targetTurn || usedFallback;
    }

    private static string ResolveBatchParentBranchId(string parentBranchId, HistoryBranchManager mgr)
    {
        if (!string.IsNullOrWhiteSpace(parentBranchId) &&
            mgr.FindRegisteredBranchForVerification(parentBranchId) != null)
        {
            return parentBranchId;
        }

        if (mgr.HasCommittedMainStory && !string.IsNullOrWhiteSpace(mgr.MainStoryBranchId))
        {
            return mgr.MainStoryBranchId;
        }

        return parentBranchId ?? string.Empty;
    }

    private static int ResolveDefaultBranchCount(int startTurn)
    {
        if (startTurn >= Generation2TransitionEngine.Generation5StartTurn)
        {
            return DefaultGen5BranchCount;
        }

        if (startTurn >= Generation2TransitionEngine.Generation4StartTurn)
        {
            return DefaultGen4BranchCount;
        }

        if (startTurn >= Generation2TransitionEngine.Generation3StartTurn)
        {
            return DefaultGen3BranchCount;
        }

        if (startTurn >= Generation2TransitionEngine.Generation2StartTurn)
        {
            return DefaultGen2BranchCount;
        }

        return AutoBranchGenerator.DefaultBranchCount;
    }

    private static void EnsureGen3DemonFrontBranchAvailable(HistoryBranchManager mgr)
    {
        if (mgr.FindRegisteredBranchForVerification(ManualGen3BranchId) != null)
        {
            return;
        }

        if (!mgr.HasCommittedMainStory ||
            !string.Equals(mgr.MainStoryBranchId, ManualGen2BranchId, StringComparison.OrdinalIgnoreCase))
        {
            RunGeneration2ManualCommitAndAdvanceToGen3(bootstrapIfNeeded: true);
        }

        RunBatchGenerationAndEvaluateNextEraCore(
            Generation2TransitionEngine.Generation3StartTurn,
            Generation2TransitionEngine.Generation3EndTurn,
            ManualGen2BranchId);
    }

    private static string ResolveGen3ManualBranchId(
        string branchId,
        HistoryBranchManager mgr,
        List<TimelineScriptEvaluationResult> rankings)
    {
        if (!string.IsNullOrWhiteSpace(branchId) &&
            mgr.FindRegisteredBranchForVerification(branchId) != null)
        {
            return branchId;
        }

        for (int i = 0; i < rankings?.Count; i++)
        {
            TimelineScriptEvaluationResult row = rankings[i];
            if (row != null &&
                !string.IsNullOrWhiteSpace(row.branchId) &&
                row.branchId.IndexOf("DEMON_FRONT", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return row.branchId;
            }
        }

        IReadOnlyList<HistoryTimelineBranch> branches = mgr.RegisteredBranches;
        for (int i = 0; i < branches.Count; i++)
        {
            HistoryTimelineBranch branch = branches[i];
            if (branch != null &&
                !string.IsNullOrWhiteSpace(branch.displayName) &&
                branch.displayName.IndexOf("魔王軍前線", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return branch.branchId;
            }
        }

        return branchId;
    }

    private static void EnsureGen4HereticArcanaBranchAvailable(HistoryBranchManager mgr)
    {
        if (mgr.FindRegisteredBranchForVerification(ManualGen4BranchId) != null)
        {
            return;
        }

        if (mgr.FindRegisteredBranchForVerification(ManualGen3BranchId) == null ||
            !string.Equals(mgr.MainStoryBranchId, ManualGen3BranchId, StringComparison.OrdinalIgnoreCase))
        {
            RunGen3DemonFrontCommitAndBatchGen4EvaluationCore(bootstrapIfNeeded: true);
        }
        else
        {
            RunBatchGenerationAndEvaluateNextEraCore(
                Generation2TransitionEngine.Generation4StartTurn,
                Generation2TransitionEngine.Generation4EndTurn,
                ManualGen3BranchId);
        }
    }

    private static string ResolveGen4ManualBranchId(
        string branchId,
        HistoryBranchManager mgr,
        List<TimelineScriptEvaluationResult> rankings)
    {
        if (!string.IsNullOrWhiteSpace(branchId) &&
            mgr.FindRegisteredBranchForVerification(branchId) != null)
        {
            return branchId;
        }

        for (int i = 0; i < rankings?.Count; i++)
        {
            TimelineScriptEvaluationResult row = rankings[i];
            if (row != null &&
                !string.IsNullOrWhiteSpace(row.branchId) &&
                row.branchId.IndexOf("HERETIC_ARCANA", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return row.branchId;
            }
        }

        IReadOnlyList<HistoryTimelineBranch> branches = mgr.RegisteredBranches;
        for (int i = 0; i < branches.Count; i++)
        {
            HistoryTimelineBranch branch = branches[i];
            if (branch != null &&
                !string.IsNullOrWhiteSpace(branch.displayName) &&
                branch.displayName.IndexOf("異端魔導", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return branch.branchId;
            }
        }

        return branchId;
    }

    private static void EnsureGen5HereticOrthodoxyBranchAvailable(HistoryBranchManager mgr)
    {
        if (mgr.FindRegisteredBranchForVerification(ManualGen5BranchId) != null)
        {
            return;
        }

        if (mgr.FindRegisteredBranchForVerification(ManualGen4BranchId) == null ||
            !string.Equals(mgr.MainStoryBranchId, ManualGen4BranchId, StringComparison.OrdinalIgnoreCase))
        {
            RunGen4HereticArcanaCommitAndBatchGen5EvaluationCore(bootstrapIfNeeded: true);
        }
        else if (PendingBranchRankings == null || PendingBranchRankings.Count == 0)
        {
            AutoBranchGenerationResult gen5Eval = AutoBranchGenerator.GenerateBranchesForEvaluation(
                Generation2TransitionEngine.Generation5StartTurn,
                Generation2TransitionEngine.Generation5EndTurn,
                DefaultGen5BranchCount,
                ManualGen4BranchId);
            PendingBranchRankings = gen5Eval.rankings ?? new List<TimelineScriptEvaluationResult>();
            PendingParentBranchId = ManualGen4BranchId;
        }
    }

    private static string ResolveGen5ManualBranchId(
        string branchId,
        HistoryBranchManager mgr,
        List<TimelineScriptEvaluationResult> rankings)
    {
        if (!string.IsNullOrWhiteSpace(branchId) &&
            mgr.FindRegisteredBranchForVerification(branchId) != null)
        {
            return branchId;
        }

        for (int i = 0; i < rankings?.Count; i++)
        {
            TimelineScriptEvaluationResult row = rankings[i];
            if (row != null &&
                !string.IsNullOrWhiteSpace(row.branchId) &&
                row.branchId.IndexOf("HERETIC_ORTHODOXY", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return row.branchId;
            }
        }

        IReadOnlyList<HistoryTimelineBranch> branches = mgr.RegisteredBranches;
        for (int i = 0; i < branches.Count; i++)
        {
            HistoryTimelineBranch branch = branches[i];
            if (branch != null &&
                !string.IsNullOrWhiteSpace(branch.displayName) &&
                branch.displayName.IndexOf("異端魔法の正統化", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return branch.branchId;
            }
        }

        return branchId;
    }

    private static string FormatLineageSummary(List<CanonTimelineLineageNode> lineage)
    {
        if (lineage == null || lineage.Count == 0)
        {
            return "(正史幹なし)";
        }

        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < lineage.Count; i++)
        {
            CanonTimelineLineageNode node = lineage[i];
            sb.Append("  Gen").Append(node.generation).Append(": ")
                .Append(node.label).Append(" (").Append(node.branchId).Append(") T")
                .Append(node.committedAtTurn);
            if (node.score > 0)
            {
                sb.Append(" score=").Append(node.score);
            }

            if (i < lineage.Count - 1)
            {
                sb.Append(" ➔");
            }

            sb.AppendLine();
        }

        return sb.ToString().TrimEnd();
    }

    private static bool ValidateExpectedLineage(List<CanonTimelineLineageNode> lineage)
    {
        if (lineage == null || lineage.Count < 5)
        {
            return false;
        }

        (string labelHint, string branchSuffix)[] markers =
        {
            ("魔導革新", "INNOVATION"),
            ("魔導復", "ARCANE_REVIVAL"),
            ("魔王軍前線", "DEMON_FRONT"),
            ("異端魔導", "HERETIC_ARCANA"),
            ("異端魔法", "HERETIC_ORTHODOXY")
        };

        int markerIndex = 0;
        for (int i = 0; i < lineage.Count && markerIndex < markers.Length; i++)
        {
            CanonTimelineLineageNode node = lineage[i];
            string label = node.label ?? string.Empty;
            string branchId = node.branchId ?? string.Empty;
            (string labelHint, string branchSuffix) marker = markers[markerIndex];
            if (label.IndexOf(marker.labelHint, StringComparison.OrdinalIgnoreCase) >= 0 ||
                branchId.IndexOf(marker.branchSuffix, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                markerIndex++;
            }
        }

        return markerIndex >= markers.Length &&
               string.Equals(
                   lineage[lineage.Count - 1].branchId,
                   ManualGen5BranchId,
                   StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAncientEraAtTurn(int turn)
    {
        EraContextResolver.TrySetCurrentTurn(turn);
        return turn >= Generation2TransitionEngine.Generation5StartTurn &&
               EraContextResolver.ResolveEraTag(turn) == EraTag.Late &&
               EraContextResolver.IsEraConditionFlag(EraContextResolver.EraAncientFlag);
    }

    private static bool TryPossessEraProtagonist(TimelineCharacterSelector selector, int turn)
    {
        if (turn >= Generation2TransitionEngine.Generation5StartTurn)
        {
            return selector.TryPossessGeneration5Protagonist(turn);
        }

        if (turn >= Generation2TransitionEngine.Generation4StartTurn)
        {
            return selector.TryPossessGeneration4Protagonist(turn);
        }

        if (turn >= Generation2TransitionEngine.Generation3StartTurn)
        {
            return selector.TryPossessGeneration3Protagonist(turn);
        }

        if (turn >= Generation2TransitionEngine.Generation2StartTurn)
        {
            return selector.TryPossessGeneration2Protagonist(turn);
        }

        return selector.TryPossessHistoricalHero(turn);
    }

    private static string FormatPendingRankingsList(List<TimelineScriptEvaluationResult> ranked)
    {
        if (ranked == null || ranked.Count == 0)
        {
            return "  (評価結果なし)";
        }

        StringBuilder sb = new StringBuilder();
        int rank = 0;
        for (int i = 0; i < ranked.Count; i++)
        {
            TimelineScriptEvaluationResult row = ranked[i];
            if (row == null || row.isCanon)
            {
                continue;
            }

            if (string.Equals(
                    row.branchName,
                    AutoBranchGenerator.FallbackBranchDisplayName,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            rank++;
            sb.Append("  ")
                .Append(rank)
                .Append(". ")
                .Append(row.branchName)
                .Append(" (")
                .Append(row.branchId)
                .Append(") — ")
                .Append(row.totalScore)
                .Append("点");
            if (row.concentrationBonus > 0)
            {
                sb.Append(" [集中+").Append(row.concentrationBonus).Append(']');
            }

            sb.AppendLine();
        }

        if (rank == 0)
        {
            sb.AppendLine("  (ALT 分岐なし — フォールバック軸のみ)");
        }

        return sb.ToString().TrimEnd();
    }

    private static TimelineScriptEvaluationResult SelectTopAlt(List<TimelineScriptEvaluationResult> ranked)
    {
        if (ranked == null)
        {
            return null;
        }

        TimelineScriptEvaluationResult best = null;
        for (int i = 0; i < ranked.Count; i++)
        {
            TimelineScriptEvaluationResult row = ranked[i];
            if (row == null || row.isCanon || string.IsNullOrWhiteSpace(row.branchId))
            {
                continue;
            }

            if (string.Equals(row.branchName, AutoBranchGenerator.FallbackBranchDisplayName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (best == null || row.totalScore > best.totalScore)
            {
                best = row;
            }
        }

        return best;
    }

    private static bool BranchListed(List<TimelineScriptEvaluationResult> ranked, string branchId)
    {
        if (ranked == null || string.IsNullOrWhiteSpace(branchId))
        {
            return false;
        }

        for (int i = 0; i < ranked.Count; i++)
        {
            TimelineScriptEvaluationResult row = ranked[i];
            if (row != null && string.Equals(row.branchId, branchId, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static void WriteVerifyLog(TimelineGenerationLoopVerifyResult verify)
    {
        try
        {
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
            string path = Path.Combine(projectRoot, "Logs", "timeline_generation_loop_verify.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? projectRoot);
            File.WriteAllText(
                path,
                $"{DateTime.UtcNow:O} success={verify.success}\n{verify.message}\n",
                Encoding.UTF8);
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[TimelineGenerationLoopEngine] 検証ログスキップ: {exception.Message}");
        }
    }

    private static void WriteManualVerifyLog(TimelineGenerationLoopVerifyResult verify)
    {
        try
        {
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
            string path = Path.Combine(projectRoot, "Logs", "timeline_generation_loop_manual_verify.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? projectRoot);
            File.WriteAllText(
                path,
                $"{DateTime.UtcNow:O} success={verify.success}\n{verify.message}\n",
                Encoding.UTF8);
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[TimelineGenerationLoopEngine] 手動確定検証ログスキップ: {exception.Message}");
        }
    }

    private static void WriteBatchVerifyLog(TimelineGenerationLoopVerifyResult verify)
    {
        try
        {
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
            string path = Path.Combine(projectRoot, "Logs", "timeline_batch_gen3_verify.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? projectRoot);
            File.WriteAllText(
                path,
                $"{DateTime.UtcNow:O} success={verify.success}\n{verify.message}\n",
                Encoding.UTF8);
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[TimelineGenerationLoopEngine] バッチ検証ログスキップ: {exception.Message}");
        }
    }

    private static void WriteBatchGen4VerifyLog(TimelineGenerationLoopVerifyResult verify)
    {
        try
        {
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
            string path = Path.Combine(projectRoot, "Logs", "timeline_batch_gen4_verify.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? projectRoot);
            File.WriteAllText(
                path,
                $"{DateTime.UtcNow:O} success={verify.success}\n{verify.message}\n",
                Encoding.UTF8);
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[TimelineGenerationLoopEngine] 第4世代バッチ検証ログスキップ: {exception.Message}");
        }
    }

    private static void WriteBatchGen5VerifyLog(TimelineGenerationLoopVerifyResult verify)
    {
        try
        {
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
            string path = Path.Combine(projectRoot, "Logs", "timeline_batch_gen5_verify.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? projectRoot);
            File.WriteAllText(
                path,
                $"{DateTime.UtcNow:O} success={verify.success}\n{verify.message}\n",
                Encoding.UTF8);
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[TimelineGenerationLoopEngine] 第5世代バッチ検証ログスキップ: {exception.Message}");
        }
    }

    private static void WriteFinalize250VerifyLog(TimelineGenerationLoopVerifyResult verify)
    {
        try
        {
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
            string path = Path.Combine(projectRoot, "Logs", "timeline_finalize_250_verify.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? projectRoot);
            File.WriteAllText(
                path,
                $"{DateTime.UtcNow:O} success={verify.success}\n{verify.message}\n",
                Encoding.UTF8);
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[TimelineGenerationLoopEngine] 250年完結検証ログスキップ: {exception.Message}");
        }
    }
}

public static class TimelineGenerationLoopEngineBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoAttach()
    {
        TimelineGenerationLoopEngine.EnsureInstance();
    }
}

#if UNITY_EDITOR
public static class TimelineGenerationLoopEngineMenu
{
    [MenuItem("Tools/Procedural Map/Run Generation 2 Manual Commit To Gen 3 (Arcane Revival)")]
    public static void RunManualCommitFromMenu()
    {
        TimelineGenerationLoopResult result =
            TimelineGenerationLoopEngine.RunGeneration2ManualCommitAndAdvanceToGen3();
        EditorUtility.DisplayDialog("Generation 2 Manual Commit", result.message, "OK");
    }

    [MenuItem("Tools/Procedural Map/Verify Generation 2 Manual Commit")]
    public static void VerifyManualCommitFromMenu()
    {
        TimelineGenerationLoopVerifyResult result = TimelineGenerationLoopEngine.RunManualCommitVerification();
        Debug.Log(
            result.success
                ? $"<color=#A5D6A7><b>【第2世代手動確定検証】PASS</b></color>\n{result.message}"
                : $"<color=#FF8A80><b>【第2世代手動確定検証】FAIL</b></color>\n{result.message}");
        EditorUtility.DisplayDialog(
            "Generation 2 Manual Commit",
            result.message,
            result.success ? "OK" : "FAIL");
    }

    [MenuItem("Tools/Procedural Map/Run Generation 2 Loop To Gen 3 (T51-101)")]
    public static void RunFromMenu()
    {
        TimelineGenerationLoopResult result = TimelineGenerationLoopEngine.RunGeneration2CommitAndAdvanceToGen3();
        EditorUtility.DisplayDialog("Timeline Generation Loop", result.message, "OK");
    }

    [MenuItem("Tools/Procedural Map/Verify Timeline Generation Loop")]
    public static void VerifyFromMenu()
    {
        TimelineGenerationLoopVerifyResult result = TimelineGenerationLoopEngine.RunVerification();
        Debug.Log(
            result.success
                ? $"<color=#A5D6A7><b>【タイムライン世代ループ検証】PASS</b></color>\n{result.message}"
                : $"<color=#FF8A80><b>【タイムライン世代ループ検証】FAIL</b></color>\n{result.message}");
        EditorUtility.DisplayDialog("Timeline Generation Loop", result.message, result.success ? "OK" : "FAIL");
    }

    [MenuItem("Tools/Procedural Map/Run Batch Gen3 (T101-150) Evaluation")]
    public static void RunBatchGen3FromMenu()
    {
        HistoryBranchManager mgr = HistoryBranchManager.EnsureInstance();
        int currentTurn = DailySimulationEngine.Instance != null && DailySimulationEngine.Instance.Turn > 0
            ? DailySimulationEngine.Instance.Turn
            : EraContextResolver.CurrentTurn;

        if (!mgr.HasCommittedMainStory ||
            currentTurn < Generation2TransitionEngine.Generation3StartTurn)
        {
            TimelineGenerationLoopEngine.RunGeneration2ManualCommitAndAdvanceToGen3();
        }

        TimelineGenerationLoopEngine engine = TimelineGenerationLoopEngine.EnsureInstance();
        engine.RunBatchGenerationAndEvaluateNextEra(
            Generation2TransitionEngine.Generation3StartTurn,
            Generation2TransitionEngine.Generation3EndTurn,
            TimelineGenerationLoopEngine.ManualGen2BranchId);

        BatchGenerationEraResult result = TimelineGenerationLoopEngine.LastBatchResult;
        EditorUtility.DisplayDialog(
            "Batch Gen3 Evaluation",
            result?.message ?? "バッチ結果なし",
            "OK");
    }

    [MenuItem("Tools/Procedural Map/Verify Batch Gen3 (T101-150) Evaluation")]
    public static void VerifyBatchGen3FromMenu()
    {
        TimelineGenerationLoopVerifyResult result = TimelineGenerationLoopEngine.RunBatchGen3Verification();
        Debug.Log(
            result.success
                ? $"<color=#A5D6A7><b>【第3世代バッチ評価検証】PASS</b></color>\n{result.message}"
                : $"<color=#FF8A80><b>【第3世代バッチ評価検証】FAIL</b></color>\n{result.message}");
        EditorUtility.DisplayDialog(
            "Batch Gen3 Evaluation",
            result.message,
            result.success ? "OK" : "FAIL");
    }

    /// <summary>Unity バッチ: -executeMethod TimelineGenerationLoopEngineMenu.BatchVerifyGen3AndQuit</summary>
    public static void BatchVerifyGen3AndQuit()
    {
        TimelineGenerationLoopVerifyResult result = TimelineGenerationLoopEngine.RunBatchGen3Verification();
        Debug.Log(
            result.success
                ? $"<color=#A5D6A7><b>【第3世代バッチ評価検証】PASS</b></color>\n{result.message}"
                : $"<color=#FF8A80><b>【第3世代バッチ評価検証】FAIL</b></color>\n{result.message}");
        EditorApplication.Exit(result.success ? 0 : 1);
    }

    [MenuItem("Tools/Procedural Map/Run Gen3 Demon Front Commit + Batch Gen4 (T151-200)")]
    public static void RunGen4BatchFromMenu()
    {
        TimelineGenerationLoopEngine engine = TimelineGenerationLoopEngine.EnsureInstance();
        engine.RunGen3DemonFrontCommitAndBatchGen4Evaluation(bootstrapIfNeeded: true);

        Gen4TransitionBatchResult result = TimelineGenerationLoopEngine.LastGen4BatchResult;
        EditorUtility.DisplayDialog(
            "Gen4 Batch Evaluation",
            result?.message ?? "バッチ結果なし",
            "OK");
    }

    [MenuItem("Tools/Procedural Map/Verify Gen3 Demon Front Commit + Batch Gen4")]
    public static void VerifyGen4BatchFromMenu()
    {
        TimelineGenerationLoopVerifyResult result = TimelineGenerationLoopEngine.RunBatchGen4Verification();
        Debug.Log(
            result.success
                ? $"<color=#A5D6A7><b>【第4世代バッチ評価検証】PASS</b></color>\n{result.message}"
                : $"<color=#FF8A80><b>【第4世代バッチ評価検証】FAIL</b></color>\n{result.message}");
        EditorUtility.DisplayDialog(
            "Gen4 Batch Evaluation",
            result.message,
            result.success ? "OK" : "FAIL");
    }

    /// <summary>Unity バッチ: -executeMethod TimelineGenerationLoopEngineMenu.BatchVerifyGen4AndQuit</summary>
    public static void BatchVerifyGen4AndQuit()
    {
        TimelineGenerationLoopVerifyResult result = TimelineGenerationLoopEngine.RunBatchGen4Verification();
        Debug.Log(
            result.success
                ? $"<color=#A5D6A7><b>【第4世代バッチ評価検証】PASS</b></color>\n{result.message}"
                : $"<color=#FF8A80><b>【第4世代バッチ評価検証】FAIL</b></color>\n{result.message}");
        EditorApplication.Exit(result.success ? 0 : 1);
    }

    [MenuItem("Tools/Procedural Map/Run Gen4 Heretic Arcana Commit + Batch Gen5 (T201-250)")]
    public static void RunGen5BatchFromMenu()
    {
        TimelineGenerationLoopEngine engine = TimelineGenerationLoopEngine.EnsureInstance();
        engine.RunGen4HereticArcanaCommitAndBatchGen5Evaluation(bootstrapIfNeeded: true);

        Gen5TransitionBatchResult result = TimelineGenerationLoopEngine.LastGen5BatchResult;
        EditorUtility.DisplayDialog(
            "Gen5 Batch Evaluation",
            result?.message ?? "バッチ結果なし",
            "OK");
    }

    [MenuItem("Tools/Procedural Map/Verify Gen4 Heretic Arcana Commit + Batch Gen5")]
    public static void VerifyGen5BatchFromMenu()
    {
        TimelineGenerationLoopVerifyResult result = TimelineGenerationLoopEngine.RunBatchGen5Verification();
        Debug.Log(
            result.success
                ? $"<color=#A5D6A7><b>【第5世代バッチ評価検証】PASS</b></color>\n{result.message}"
                : $"<color=#FF8A80><b>【第5世代バッチ評価検証】FAIL</b></color>\n{result.message}");
        EditorUtility.DisplayDialog(
            "Gen5 Batch Evaluation",
            result.message,
            result.success ? "OK" : "FAIL");
    }

    /// <summary>Unity バッチ: -executeMethod TimelineGenerationLoopEngineMenu.BatchVerifyGen5AndQuit</summary>
    public static void BatchVerifyGen5AndQuit()
    {
        TimelineGenerationLoopVerifyResult result = TimelineGenerationLoopEngine.RunBatchGen5Verification();
        Debug.Log(
            result.success
                ? $"<color=#A5D6A7><b>【第5世代バッチ評価検証】PASS</b></color>\n{result.message}"
                : $"<color=#FF8A80><b>【第5世代バッチ評価検証】FAIL</b></color>\n{result.message}");
        EditorApplication.Exit(result.success ? 0 : 1);
    }

    [MenuItem("Tools/Procedural Map/Run Gen5 Heretic Orthodoxy Commit + Finalize 250y Canon")]
    public static void RunFinalize250FromMenu()
    {
        TimelineGenerationLoopEngine engine = TimelineGenerationLoopEngine.EnsureInstance();
        engine.RunGen5HereticOrthodoxyCommitAndFinalize250YearHistory(bootstrapIfNeeded: true);

        Gen5FinalizationResult result = TimelineGenerationLoopEngine.LastGen5FinalizationResult;
        EditorUtility.DisplayDialog(
            "250年正史完結",
            result?.message ?? "結果なし",
            "OK");
    }

    [MenuItem("Tools/Procedural Map/Verify Gen5 Heretic Orthodoxy Commit + Finalize 250y Canon")]
    public static void VerifyFinalize250FromMenu()
    {
        TimelineGenerationLoopVerifyResult result = TimelineGenerationLoopEngine.RunFinalize250YearVerification();
        Debug.Log(
            result.success
                ? $"<color=#A5D6A7><b>【250年正史完結検証】PASS</b></color>\n{result.message}"
                : $"<color=#FF8A80><b>【250年正史完結検証】FAIL</b></color>\n{result.message}");
        EditorUtility.DisplayDialog(
            "250年正史完結",
            result.message,
            result.success ? "OK" : "FAIL");
    }

    /// <summary>Unity バッチ: -executeMethod TimelineGenerationLoopEngineMenu.BatchVerifyFinalize250AndQuit</summary>
    public static void BatchVerifyFinalize250AndQuit()
    {
        TimelineGenerationLoopVerifyResult result = TimelineGenerationLoopEngine.RunFinalize250YearVerification();
        Debug.Log(
            result.success
                ? $"<color=#A5D6A7><b>【250年正史完結検証】PASS</b></color>\n{result.message}"
                : $"<color=#FF8A80><b>【250年正史完結検証】FAIL</b></color>\n{result.message}");
        EditorApplication.Exit(result.success ? 0 : 1);
    }

    [MenuItem("Tools/Procedural Map/Run 1000 Year MAGI Chronicle Pipeline")]
    [MenuItem("Tools/Procedural Map/Run 1000y MAGI Chronicle Pipeline (T251-T1000)")]
    public static void Run1000YearMagiChroniclePipelineFromMenu()
    {
        MagiChroniclePipelineResult result =
            TimelineGenerationLoopEngine.Run1000YearMagiChroniclePipeline(bootstrapCanon250: true);
        string summary =
            $"success={result.success}\n" +
            $"completed1000={result.completedToTurn1000}\n" +
            $"haltedManual={result.haltedForManualSelection}\n" +
            $"gens={result.generationsProcessed} finalTurn={result.finalTurn}\n" +
            $"main={result.finalMainStoryBranchId}\n\n" +
            result.message;
        Debug.Log(
            result.completedToTurn1000
                ? $"<color=#A5D6A7><b>【1000年史MAGIパイプライン】COMPLETE</b></color>\n{summary}"
                : result.haltedForManualSelection
                    ? $"<color=#FFB74D><b>【1000年史MAGIパイプライン】手動待ち</b></color>\n{summary}"
                    : $"<color=#FF8A80><b>【1000年史MAGIパイプライン】STOP</b></color>\n{summary}");
        EditorUtility.DisplayDialog("1000y MAGI Chronicle Pipeline", summary, "OK");
    }

    /// <summary>Unity バッチ: -executeMethod TimelineGenerationLoopEngineMenu.BatchRun1000YearMagiPipelineAndQuit</summary>
    public static void BatchRun1000YearMagiPipelineAndQuit()
    {
        MagiChroniclePipelineResult result =
            TimelineGenerationLoopEngine.Run1000YearMagiChroniclePipeline(bootstrapCanon250: true);
        bool pass = result.success &&
                    result.haltedForManualSelection &&
                    result.generationsProcessed >= 4 &&
                    TimelineGenerationLoopEngine.AwaitingManualBranchSelection;
        EditorApplication.Exit(pass ? 0 : 1);
    }
}
#endif

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

// =============================================================================
// 自動 IF 分岐ジェネレーター — 指定期間の ALT_ パターン一括生成・評価・Commit
// 連携: HistoryBranchManager / TimelineScriptEvaluator / TurnTransitionEngine
// =============================================================================

/// <summary>自動 IF 分岐生成・評価・確定の結果。</summary>
public sealed class AutoBranchGenerationResult
{
    public bool success;
    public int startTurn = 1;
    public int endTurn = 50;
    public int requestedCount;
    public int generatedCount;
    public string committedBranchId = string.Empty;
    public string committedBranchName = string.Empty;
    public int bestScore;
    public bool usedFallbackBranch;
    public string message = string.Empty;
    public List<TimelineScriptEvaluationResult> rankings = new List<TimelineScriptEvaluationResult>();
    public List<string> generatedBranchIds = new List<string>();
}

/// <summary>検証結果。</summary>
public sealed class AutoBranchGeneratorVerifyResult
{
    public bool success;
    public string message = string.Empty;
}

/// <summary>
/// 指定期間に複数の IF 介入パターン（ALT_）を生成し、最高スコア軸をメインストーリーへ確定します。
/// </summary>
[DefaultExecutionOrder(48)]
public class AutoBranchGenerator : MonoBehaviour
{
    public const string LogTag = "【本番歴史選定】";
    public const string FallbackBranchDisplayName = "IF_Line: 標準フォールバック";
    public const int DefaultBranchCount = 5;
    /// <summary>1位と2位の総合スコアが完全一致した場合のみ拮抗とみなし Safe-Fail します。</summary>
    public const int ScoreTieThreshold = 0;

    public static AutoBranchGenerator Instance { get; private set; }

    public static AutoBranchGenerator EnsureInstance()
    {
        if (Instance != null)
        {
            return Instance;
        }

        AutoBranchGenerator existing = UnityEngine.Object.FindAnyObjectByType<AutoBranchGenerator>();
        if (existing != null)
        {
            Instance = existing;
            return existing;
        }

        GameObject hub = GameObject.Find("DebugSystemsHub");
        GameObject host = hub != null ? hub : new GameObject(nameof(AutoBranchGenerator));
        AutoBranchGenerator generator = host.GetComponent<AutoBranchGenerator>();
        return generator != null ? generator : host.AddComponent<AutoBranchGenerator>();
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

    /// <summary>指定期間の IF 分岐群を生成・評価し、最高スコア軸を Commit します。</summary>
    public static AutoBranchGenerationResult GenerateAndEvaluateBranches(
        int startTurn = 1,
        int endTurn = 50,
        int branchCount = DefaultBranchCount)
    {
        AutoBranchGenerationResult result = new AutoBranchGenerationResult
        {
            startTurn = Mathf.Clamp(startTurn, EraContextResolver.MinTurn, EraContextResolver.MaxTurn),
            endTurn = Mathf.Clamp(endTurn, EraContextResolver.MinTurn, EraContextResolver.MaxTurn),
            requestedCount = Mathf.Max(1, branchCount)
        };

        if (result.endTurn < result.startTurn)
        {
            int swap = result.startTurn;
            result.startTurn = result.endTurn;
            result.endTurn = swap;
        }

        try
        {
            AutoBranchGenerator.EnsureInstance();
            TimelineScriptEvaluator.EnsureInstance();
            HistoryBranchManager mgr = HistoryBranchManager.EnsureInstance();
            HistoryFlagRegistry.EnsureWired();

            List<IfBranchBlueprint> blueprints = result.startTurn >= Generation2TransitionEngine.Generation2StartTurn
                ? BuildGeneration2BlueprintPool()
                : BuildBlueprintPool();
            bool useParentAxis = result.startTurn >= Generation2TransitionEngine.Generation2StartTurn &&
                                 mgr.HasCommittedMainStory;
            List<string> generatedIds = useParentAxis
                ? GenerateBranchesFromParent(
                    result.startTurn,
                    result.endTurn,
                    result.requestedCount,
                    blueprints,
                    mgr,
                    mgr.MainStoryBranchId)
                : GenerateBranches(
                    result.startTurn,
                    result.endTurn,
                    result.requestedCount,
                    blueprints,
                    mgr);
            result.generatedCount = generatedIds.Count;

            if (generatedIds.Count == 0)
            {
                string fallbackId = EnsureFallbackBranch(result.startTurn, result.endTurn, mgr);
                generatedIds.Add(fallbackId);
                result.generatedCount = 1;
                result.usedFallbackBranch = true;
            }

            SyncSimulationTurn(result.endTurn);

            List<TimelineScriptEvaluationResult> ranked =
                TimelineScriptEvaluator.EvaluateAllBranchesAtCurrentTurn(result.endTurn);
            result.rankings = ranked;

            TimelineScriptEvaluationResult bestAlt = SelectBestAltBranch(ranked, out bool scoreTied);
            MainStoryCommitResult commit;
            if (bestAlt == null)
            {
                string fallbackId = EnsureFallbackBranch(result.startTurn, result.endTurn, mgr);
                result.usedFallbackBranch = true;
                bestAlt = TimelineScriptEvaluator.EvaluateTimelineBranch(
                    mgr.FindRegisteredBranchForVerification(fallbackId),
                    result.endTurn);
                ranked = TimelineScriptEvaluator.EvaluateAllBranchesAtCurrentTurn(result.endTurn);
                result.rankings = ranked;
                commit = HistoryBranchManager.CommitBranchAsMainStory(
                    bestAlt.branchId,
                    bestAlt.totalScore,
                    result.endTurn);
            }
            else if (scoreTied)
            {
                Debug.LogWarning(
                    $"{LogTag} 生成 ALT_ 軸の最高スコアが完全一致のため Safe-Fail フォールバックを採用します。");
                string fallbackId = EnsureFallbackBranch(result.startTurn, result.endTurn, mgr);
                result.usedFallbackBranch = true;
                bestAlt = TimelineScriptEvaluator.EvaluateTimelineBranch(
                    mgr.FindRegisteredBranchForVerification(fallbackId),
                    result.endTurn);
                ranked = TimelineScriptEvaluator.EvaluateAllBranchesAtCurrentTurn(result.endTurn);
                result.rankings = ranked;
                commit = HistoryBranchManager.CommitBranchAsMainStory(
                    bestAlt.branchId,
                    bestAlt.totalScore,
                    result.endTurn);
            }
            else
            {
                commit = TimelineScriptEvaluator.CommitBestBranchAtCurrentTurn(result.endTurn);
                ranked = TimelineScriptEvaluator.EvaluateAllBranchesAtCurrentTurn(result.endTurn);
                result.rankings = ranked;
                bestAlt = FindEvaluationResult(ranked, commit.branchId) ?? bestAlt;
            }

            result.success = commit.success && result.generatedCount > 0;
            result.committedBranchId = commit.branchId;
            result.committedBranchName = string.IsNullOrWhiteSpace(commit.branchName)
                ? TimelineScriptEvaluator.ResolveBranchDisplayName(
                    mgr.FindRegisteredBranchForVerification(commit.branchId))
                : commit.branchName;
            result.bestScore = commit.totalScore > 0 ? commit.totalScore : bestAlt?.totalScore ?? 0;
            result.message = BuildSummaryMessage(result, ranked);

            Debug.Log(
                $"<color=#A5D6A7><b>{LogTag}</b></color> " +
                $"ターン{result.startTurn}〜{result.endTurn} 間で {result.generatedCount} 本の IF 軸を生成・評価しました！ " +
                $"採択軸: 「{result.committedBranchName}」 (総合スコア: {result.bestScore})");

            LogRankings(result.endTurn, ranked);
            return result;
        }
        catch (Exception exception)
        {
            result.success = false;
            result.message = $"Safe-Fail: {exception.Message}";
            Debug.LogWarning($"[AutoBranchGenerator] GenerateAndEvaluateBranches Safe-Fail: {exception.Message}");

            try
            {
                HistoryBranchManager mgr = HistoryBranchManager.EnsureInstance();
                string fallbackId = EnsureFallbackBranch(result.startTurn, result.endTurn, mgr);
                SyncSimulationTurn(result.endTurn);
                MainStoryCommitResult commit = HistoryBranchManager.CommitBranchAsMainStory(
                    fallbackId,
                    TimelineScriptEvaluator.SafeFailTotalScore,
                    result.endTurn);
                result.usedFallbackBranch = true;
                result.committedBranchId = commit.branchId;
                result.committedBranchName = FallbackBranchDisplayName;
                result.bestScore = commit.totalScore;
                result.success = commit.success;
            }
            catch (Exception fallbackException)
            {
                result.message += $" / fallback failed: {fallbackException.Message}";
            }

            return result;
        }
    }

    /// <summary>指定期間の IF 分岐群を生成・評価のみ行い、Commit は行いません。</summary>
    public static AutoBranchGenerationResult GenerateBranchesForEvaluation(
        int startTurn = 1,
        int endTurn = 50,
        int branchCount = DefaultBranchCount)
    {
        return GenerateBranchesForEvaluation(startTurn, endTurn, branchCount, null);
    }

    /// <summary>親歴史軸を指定して IF 分岐群を生成・評価のみ行い、Commit は行いません。</summary>
    public static AutoBranchGenerationResult GenerateBranchesForEvaluation(
        int startTurn,
        int endTurn,
        int branchCount,
        string parentBranchId)
    {
        AutoBranchGenerationResult result = new AutoBranchGenerationResult
        {
            startTurn = Mathf.Clamp(startTurn, EraContextResolver.MinTurn, EraContextResolver.MaxTurn),
            endTurn = Mathf.Clamp(endTurn, EraContextResolver.MinTurn, EraContextResolver.MaxTurn),
            requestedCount = Mathf.Max(1, branchCount)
        };

        if (result.endTurn < result.startTurn)
        {
            int swap = result.startTurn;
            result.startTurn = result.endTurn;
            result.endTurn = swap;
        }

        try
        {
            AutoBranchGenerator.EnsureInstance();
            TimelineScriptEvaluator.EnsureInstance();
            HistoryBranchManager mgr = HistoryBranchManager.EnsureInstance();
            HistoryFlagRegistry.EnsureWired();

            List<IfBranchBlueprint> blueprints = ResolveBlueprintPool(result.startTurn);
            string resolvedParentId = ResolveParentBranchId(parentBranchId, mgr);
            bool useParentAxis = result.startTurn >= Generation2TransitionEngine.Generation2StartTurn &&
                                 !string.IsNullOrWhiteSpace(resolvedParentId);
            List<string> generatedIds = useParentAxis
                ? GenerateBranchesFromParent(
                    result.startTurn,
                    result.endTurn,
                    result.requestedCount,
                    blueprints,
                    mgr,
                    resolvedParentId)
                : GenerateBranches(
                    result.startTurn,
                    result.endTurn,
                    result.requestedCount,
                    blueprints,
                    mgr);
            result.generatedCount = generatedIds.Count;

            if (generatedIds.Count == 0)
            {
                string fallbackId = EnsureFallbackBranch(result.startTurn, result.endTurn, mgr);
                generatedIds.Add(fallbackId);
                result.generatedCount = 1;
                result.usedFallbackBranch = true;
            }

            SyncSimulationTurn(result.endTurn);
            result.rankings = TimelineScriptEvaluator.EvaluateAllBranchesAtCurrentTurn(result.endTurn);
            result.success = result.generatedCount > 0;
            result.message = BuildSummaryMessage(result, result.rankings);
            LogRankings(result.endTurn, result.rankings);
            return result;
        }
        catch (Exception exception)
        {
            result.success = false;
            result.message = $"Safe-Fail: {exception.Message}";
            Debug.LogWarning($"[AutoBranchGenerator] GenerateBranchesForEvaluation Safe-Fail: {exception.Message}");
            return result;
        }
    }

    /// <summary>251〜1000年史向け — IF 分岐群を生成・評価のみ（Commit なし）。</summary>
    public static AutoBranchGenerationResult GenerateBranchesForChronicleEvaluation(
        int startTurn,
        int endTurn,
        int branchCount,
        string parentBranchId)
    {
        AutoBranchGenerationResult result = new AutoBranchGenerationResult
        {
            startTurn = EraContextResolver.ClampSimulationTurn(startTurn),
            endTurn = EraContextResolver.ClampSimulationTurn(endTurn),
            requestedCount = Mathf.Clamp(branchCount, 5, 8)
        };

        if (result.endTurn < result.startTurn)
        {
            int swap = result.startTurn;
            result.startTurn = result.endTurn;
            result.endTurn = swap;
        }

        try
        {
            AutoBranchGenerator.EnsureInstance();
            TimelineScriptEvaluator.EnsureInstance();
            HistoryBranchManager mgr = HistoryBranchManager.EnsureInstance();
            HistoryFlagRegistry.EnsureWired();

            List<IfBranchBlueprint> blueprints = BuildPostCanonBlueprintPool();
            string resolvedParentId = ResolveParentBranchId(parentBranchId, mgr);
            List<string> generatedIds = string.IsNullOrWhiteSpace(resolvedParentId)
                ? GenerateBranches(
                    result.startTurn,
                    result.endTurn,
                    result.requestedCount,
                    blueprints,
                    mgr)
                : GenerateBranchesFromParent(
                    result.startTurn,
                    result.endTurn,
                    result.requestedCount,
                    blueprints,
                    mgr,
                    resolvedParentId);
            result.generatedCount = generatedIds.Count;

            if (generatedIds.Count == 0)
            {
                string fallbackId = EnsureChronicleFallbackBranch(result.startTurn, result.endTurn, mgr);
                generatedIds.Add(fallbackId);
                result.generatedCount = 1;
                result.usedFallbackBranch = true;
            }

            result.generatedBranchIds = new List<string>(generatedIds);
            SyncChronicleSimulationTurn(result.endTurn);
            result.rankings = TimelineScriptEvaluator.EvaluateAllBranchesAtCurrentTurn(result.endTurn);
            result.success = result.generatedCount > 0;
            result.message = BuildSummaryMessage(result, result.rankings);
            LogRankings(result.endTurn, result.rankings);
            return result;
        }
        catch (Exception exception)
        {
            result.success = false;
            result.message = $"Safe-Fail: {exception.Message}";
            Debug.LogWarning(
                $"[AutoBranchGenerator] GenerateBranchesForChronicleEvaluation Safe-Fail: {exception.Message}");
            return result;
        }
    }

    private static void SyncChronicleSimulationTurn(int endTurn)
    {
        int safeTurn = EraContextResolver.ClampSimulationTurn(endTurn);
        DailySimulationEngine.EnsureInstance().TryPrepareForChronicleTurn(safeTurn);
        EraContextResolver.TrySetCurrentTurn(Mathf.Min(safeTurn, EraContextResolver.MaxTurn));
        MicroToMacroAggregator.EnsureInstance().PrepareTurnTransition(safeTurn, applyBranchAdjustments: true);
    }

    private static string EnsureChronicleFallbackBranch(int startTurn, int endTurn, HistoryBranchManager mgr)
    {
        string fallbackId = EnsureFallbackBranch(startTurn, endTurn, mgr);
        HistoryTimelineBranch branch = mgr.FindRegisteredBranchForVerification(fallbackId);
        if (branch != null)
        {
            branch.displayName = "IF_Line: 1000年史フォールバック";
            mgr.UpsertRegisteredBranchForVerification(branch);
        }

        return fallbackId;
    }

    private static List<IfBranchBlueprint> BuildPostCanonBlueprintPool()
    {
        int nation = MicroToMacroAggregator.DefaultNationId;
        return new List<IfBranchBlueprint>
        {
            new IfBranchBlueprint
            {
                displayName = "IF_Line: 大活性期・魔獣群災害",
                branchSuffix = "BEAST_CATASTROPHE",
                events = new[]
                {
                    Spec(0.20f, nation, "HIST_NATION_001_GEO_TURN_001_MONSTER_DEFENSE",
                        new MacroParamDelta { powerMultiplier = 0.88f, barrierEfficiencyDelta = -0.08f, threatMultiplier = 1.45f }),
                    Spec(0.55f, nation, "HIST_NATION_001_GEO_TURN_001_BARRIERDROP",
                        new MacroParamDelta { powerMultiplier = 0.92f, barrierEfficiencyDelta = -0.12f, threatMultiplier = 1.62f }),
                    Spec(0.85f, nation, "HIST_NATION_001_GEO_TURN_001_MONSTER_DEFENSE",
                        new MacroParamDelta { powerMultiplier = 1.02f, barrierEfficiencyDelta = 0.02f, threatMultiplier = 1.18f })
                }
            },
            new IfBranchBlueprint
            {
                displayName = "IF_Line: 失伝技術の再発見",
                branchSuffix = "LOST_TECH_REVIVAL",
                events = new[]
                {
                    Spec(0.18f, nation, "HIST_NATION_001_GEO_TURN_001_INNOVATION",
                        new MacroParamDelta { powerMultiplier = 1.14f, barrierEfficiencyDelta = 0.06f, threatMultiplier = 0.92f },
                        "MAGIC_HERETIC_ARCANA_CIVILIZED"),
                    Spec(0.52f, nation, "HIST_NATION_001_GEO_TURN_001_HERO",
                        new MacroParamDelta { powerMultiplier = 1.08f, barrierEfficiencyDelta = 0.04f, threatMultiplier = 0.88f }),
                    Spec(0.82f, nation, "HIST_NATION_001_GEO_TURN_001_INNOVATION",
                        new MacroParamDelta { powerMultiplier = 1.06f, barrierEfficiencyDelta = 0.05f, threatMultiplier = 0.90f })
                }
            },
            new IfBranchBlueprint
            {
                displayName = "IF_Line: 文明インフラ衰退",
                branchSuffix = "CIVILIZATION_DECAY",
                events = new[]
                {
                    Spec(0.22f, nation, "HIST_NATION_001_GEO_TURN_001_BARRIERDROP",
                        new MacroParamDelta { powerMultiplier = 0.94f, barrierEfficiencyDelta = -0.10f, threatMultiplier = 1.22f }),
                    Spec(0.58f, nation, "HIST_NATION_001_GEO_TURN_001_CIVILWAR",
                        new MacroParamDelta { powerMultiplier = 0.90f, barrierEfficiencyDelta = -0.06f, threatMultiplier = 1.28f }),
                    Spec(0.88f, nation, "HIST_NATION_001_GEO_TURN_001_SUCCESSION",
                        new MacroParamDelta { powerMultiplier = 0.96f, barrierEfficiencyDelta = -0.02f, threatMultiplier = 1.10f })
                }
            },
            new IfBranchBlueprint
            {
                displayName = "IF_Line: 異端学派分裂",
                branchSuffix = "HERETIC_SCHISM",
                events = new[]
                {
                    Spec(0.16f, nation, "HIST_NATION_001_GEO_TURN_001_ACADEMY_RIVALRY",
                        new MacroParamDelta { powerMultiplier = 1.04f, barrierEfficiencyDelta = 0.02f, threatMultiplier = 1.12f },
                        "MAGIC_HERETIC_ORTHODOXY_CIVILIZED"),
                    Spec(0.48f, nation, "HIST_NATION_001_GEO_TURN_001_CIVILWAR",
                        new MacroParamDelta { powerMultiplier = 0.98f, barrierEfficiencyDelta = -0.01f, threatMultiplier = 1.18f }),
                    Spec(0.78f, nation, "HIST_NATION_001_GEO_TURN_001_INNOVATION",
                        new MacroParamDelta { powerMultiplier = 1.10f, barrierEfficiencyDelta = 0.04f, threatMultiplier = 1.02f })
                }
            },
            new IfBranchBlueprint
            {
                displayName = "IF_Line: 辺境拡大",
                branchSuffix = "FRONTIER_EXPANSION",
                events = new[]
                {
                    Spec(0.25f, nation, "HIST_NATION_001_GEO_TURN_001_SUCCESSION",
                        new MacroParamDelta { powerMultiplier = 1.12f, barrierEfficiencyDelta = 0.05f, threatMultiplier = 1.06f }),
                    Spec(0.60f, nation, "HIST_NATION_001_GEO_TURN_001_HERO",
                        new MacroParamDelta { powerMultiplier = 1.08f, barrierEfficiencyDelta = 0.03f, threatMultiplier = 0.95f }),
                    Spec(0.90f, nation, "HIST_NATION_001_GEO_TURN_001_INNOVATION",
                        new MacroParamDelta { powerMultiplier = 1.05f, barrierEfficiencyDelta = 0.04f, threatMultiplier = 0.98f })
                }
            },
            new IfBranchBlueprint
            {
                displayName = "IF_Line: 魔王残党再燃",
                branchSuffix = "DEMON_RESURGENCE",
                events = new[]
                {
                    Spec(0.20f, nation, "HIST_NATION_001_GEO_TURN_001_MONSTER_DEFENSE",
                        new MacroParamDelta { powerMultiplier = 1.06f, barrierEfficiencyDelta = -0.04f, threatMultiplier = 1.35f }),
                    Spec(0.55f, nation, "HIST_NATION_001_GEO_TURN_001_BARRIERDROP",
                        new MacroParamDelta { powerMultiplier = 1.02f, barrierEfficiencyDelta = -0.08f, threatMultiplier = 1.48f }),
                    Spec(0.85f, nation, "HIST_NATION_001_GEO_TURN_001_CIVILWAR",
                        new MacroParamDelta { powerMultiplier = 0.94f, barrierEfficiencyDelta = -0.02f, threatMultiplier = 1.25f })
                }
            },
            new IfBranchBlueprint
            {
                displayName = "IF_Line: 学院改革",
                branchSuffix = "ACADEMY_REFORM",
                events = new[]
                {
                    Spec(0.18f, nation, "HIST_NATION_001_GEO_TURN_001_ACADEMY_RIVALRY",
                        new MacroParamDelta { powerMultiplier = 1.10f, barrierEfficiencyDelta = 0.06f, threatMultiplier = 0.96f },
                        "JOB_ACADEMY_SCHOLAR"),
                    Spec(0.50f, nation, "HIST_NATION_001_GEO_TURN_001_INNOVATION",
                        new MacroParamDelta { powerMultiplier = 1.14f, barrierEfficiencyDelta = 0.07f, threatMultiplier = 0.90f }),
                    Spec(0.80f, nation, "HIST_NATION_001_GEO_TURN_001_HERO",
                        new MacroParamDelta { powerMultiplier = 1.06f, barrierEfficiencyDelta = 0.04f, threatMultiplier = 0.92f })
                }
            },
            new IfBranchBlueprint
            {
                displayName = "IF_Line: 結界総崩壊危機",
                branchSuffix = "BARRIER_CRISIS",
                events = new[]
                {
                    Spec(0.15f, nation, "HIST_NATION_001_GEO_TURN_001_BARRIERDROP",
                        new MacroParamDelta { powerMultiplier = 0.86f, barrierEfficiencyDelta = -0.18f, threatMultiplier = 1.72f }),
                    Spec(0.45f, nation, "HIST_NATION_001_GEO_TURN_001_MONSTER_DEFENSE",
                        new MacroParamDelta { powerMultiplier = 0.94f, barrierEfficiencyDelta = -0.06f, threatMultiplier = 1.38f }),
                    Spec(0.75f, nation, "HIST_NATION_001_GEO_TURN_001_HERO",
                        new MacroParamDelta { powerMultiplier = 1.00f, barrierEfficiencyDelta = 0.02f, threatMultiplier = 1.08f },
                        "JOB_BARRIER_REPAIRER")
                }
            }
        };
    }

    /// <summary>指定プラン（案1〜5）の IF 軸のみ生成し、その軸をメインストーリーとして確定します。</summary>
    public static AutoBranchGenerationResult GenerateAndCommitPlan(
        int startTurn = 1,
        int endTurn = 50,
        int planIndexOneBased = 2)
    {
        AutoBranchGenerationResult result = new AutoBranchGenerationResult
        {
            startTurn = Mathf.Clamp(startTurn, EraContextResolver.MinTurn, EraContextResolver.MaxTurn),
            endTurn = Mathf.Clamp(endTurn, EraContextResolver.MinTurn, EraContextResolver.MaxTurn),
            requestedCount = 1
        };

        if (result.endTurn < result.startTurn)
        {
            int swap = result.startTurn;
            result.startTurn = result.endTurn;
            result.endTurn = swap;
        }

        try
        {
            AutoBranchGenerator.EnsureInstance();
            TimelineScriptEvaluator.EnsureInstance();
            HistoryBranchManager mgr = HistoryBranchManager.EnsureInstance();
            HistoryFlagRegistry.EnsureWired();

            List<IfBranchBlueprint> blueprints = BuildBlueprintPool();
            int planIndex = Mathf.Clamp(planIndexOneBased - 1, 0, blueprints.Count - 1);
            IfBranchBlueprint blueprint = blueprints[planIndex];
            int span = Mathf.Max(1, result.endTurn - result.startTurn);
            string branchId = GenerateSingleBranch(
                blueprint,
                result.startTurn,
                result.endTurn,
                span,
                MicroToMacroAggregator.DefaultNationId,
                mgr);

            if (string.IsNullOrWhiteSpace(branchId))
            {
                string fallbackId = EnsureFallbackBranch(result.startTurn, result.endTurn, mgr);
                result.usedFallbackBranch = true;
                result.generatedCount = 1;
                branchId = fallbackId;
            }
            else
            {
                result.generatedCount = 1;
            }

            SyncSimulationTurn(result.endTurn);

            HistoryTimelineBranch branch = mgr.FindRegisteredBranchForVerification(branchId);
            TimelineScriptEvaluationResult evaluation =
                TimelineScriptEvaluator.EvaluateTimelineBranch(branch, result.endTurn);
            MainStoryCommitResult commit = HistoryBranchManager.CommitBranchAsMainStory(
                branchId,
                evaluation.totalScore,
                result.endTurn);

            result.rankings = TimelineScriptEvaluator.EvaluateAllBranchesAtCurrentTurn(result.endTurn);
            result.success = commit.success && result.generatedCount > 0;
            result.committedBranchId = commit.branchId;
            result.committedBranchName = string.IsNullOrWhiteSpace(commit.branchName)
                ? TimelineScriptEvaluator.ResolveBranchDisplayName(branch)
                : commit.branchName;
            result.bestScore = commit.totalScore > 0 ? commit.totalScore : evaluation.totalScore;
            result.message = BuildSummaryMessage(result, result.rankings);

            Debug.Log(
                $"<color=#A5D6A7><b>{LogTag}</b></color> " +
                $"案{planIndexOneBased}「{result.committedBranchName}」を T{result.endTurn} で確定しました " +
                $"(総合スコア: {result.bestScore})");
            return result;
        }
        catch (Exception exception)
        {
            result.success = false;
            result.message = $"Safe-Fail: {exception.Message}";
            Debug.LogWarning($"[AutoBranchGenerator] GenerateAndCommitPlan Safe-Fail: {exception.Message}");
            return result;
        }
    }

    public static AutoBranchGeneratorVerifyResult RunVerification()
    {
        AutoBranchGeneratorVerifyResult verify = new AutoBranchGeneratorVerifyResult();
        StringBuilder log = new StringBuilder();
        try
        {
            SimulationVerifyBootstrap.PrepareFreshStoryDay(1, GameMode.StoryMode);
            HistoryFlagRegistry.EnsureWired();
            HistoryFlagRegistry.ClearForTesting();
            HistoryBranchManager.EnsureInstance().ClearRegisteredBranches();

            AutoBranchGenerationResult result = GenerateAndEvaluateBranches(1, 50, 5);
            log.AppendLine(
                $"generate: success={result.success} count={result.generatedCount} " +
                $"committed={result.committedBranchId} score={result.bestScore}");
            log.AppendLine($"fallback={result.usedFallbackBranch} turn={result.startTurn}-{result.endTurn}");

            TimelineScriptEvaluationResult topGeneratedAlt = SelectBestAltBranch(result.rankings, out _);
            bool commitPass = result.success &&
                              !string.IsNullOrWhiteSpace(result.committedBranchId) &&
                              HistoryBranchManager.EnsureInstance().HasCommittedMainStory;
            bool countPass = result.generatedCount >= 5;
            bool scorePass = result.bestScore > TimelineScriptEvaluator.SafeFailTotalScore;
            bool rankingPass = result.rankings != null && result.rankings.Count >= 6;
            bool topAltPass = !result.usedFallbackBranch &&
                              topGeneratedAlt != null &&
                              string.Equals(
                                  result.committedBranchId,
                                  topGeneratedAlt.branchId,
                                  StringComparison.OrdinalIgnoreCase);
            log.AppendLine(
                $"commit={commitPass} count={countPass} score={scorePass} rankings={rankingPass} " +
                $"topAlt={topAltPass} top={topGeneratedAlt?.branchId} " +
                $"committedScore={result.bestScore} rankedScore={topGeneratedAlt?.totalScore}");

            verify.success = commitPass && countPass && scorePass && rankingPass && topAltPass;
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

    private static List<string> GenerateBranches(
        int startTurn,
        int endTurn,
        int branchCount,
        List<IfBranchBlueprint> blueprints,
        HistoryBranchManager mgr)
    {
        List<string> branchIds = new List<string>();
        int nationId = MicroToMacroAggregator.DefaultNationId;
        int span = Mathf.Max(1, endTurn - startTurn);

        for (int i = 0; i < branchCount; i++)
        {
            IfBranchBlueprint blueprint = blueprints[i % blueprints.Count];
            string branchId = GenerateSingleBranch(blueprint, startTurn, endTurn, span, nationId, mgr);
            if (!string.IsNullOrWhiteSpace(branchId))
            {
                branchIds.Add(branchId);
            }
        }

        return branchIds;
    }

    private static List<string> GenerateBranchesFromParent(
        int startTurn,
        int endTurn,
        int branchCount,
        List<IfBranchBlueprint> blueprints,
        HistoryBranchManager mgr,
        string parentBranchId)
    {
        List<string> branchIds = new List<string>();
        if (string.IsNullOrWhiteSpace(parentBranchId))
        {
            return branchIds;
        }

        int nationId = MicroToMacroAggregator.DefaultNationId;
        int span = Mathf.Max(1, endTurn - startTurn);
        for (int i = 0; i < branchCount; i++)
        {
            IfBranchBlueprint blueprint = blueprints[i % blueprints.Count];
            string branchId = GenerateSingleBranchFromParent(
                blueprint,
                startTurn,
                endTurn,
                span,
                nationId,
                mgr,
                parentBranchId);
            if (!string.IsNullOrWhiteSpace(branchId))
            {
                branchIds.Add(branchId);
            }
        }

        return branchIds;
    }

    private static string GenerateSingleBranchFromParent(
        IfBranchBlueprint blueprint,
        int startTurn,
        int endTurn,
        int span,
        int defaultNationId,
        HistoryBranchManager mgr,
        string parentBranchId)
    {
        if (blueprint?.events == null || blueprint.events.Length == 0 ||
            string.IsNullOrWhiteSpace(parentBranchId))
        {
            return string.Empty;
        }

        IfBranchEventSpec firstSpec = blueprint.events[0];
        int firstTurn = startTurn + Mathf.RoundToInt(span * firstSpec.turnRatio);
        firstTurn = Mathf.Clamp(firstTurn, startTurn, endTurn);
        int nation = firstSpec.nationId > 0 ? firstSpec.nationId : defaultNationId;
        string suffix = string.IsNullOrWhiteSpace(blueprint.branchSuffix)
            ? $"GEN2_{firstTurn:D3}"
            : blueprint.branchSuffix;

        GenerateBranchFromStateResult created = HistoryBranchManager.GenerateBranchFromCurrentState(
            firstTurn,
            nation,
            firstSpec.keyEventId,
            firstSpec.outcomeFlag,
            firstSpec.delta,
            parentBranchId,
            suffix);
        if (!created.success)
        {
            return string.Empty;
        }

        if (!string.IsNullOrWhiteSpace(firstSpec.extraUnlockFlag))
        {
            HistoryFlagRegistry.Unlock(firstSpec.extraUnlockFlag);
        }

        string branchId = created.branchId;
        for (int i = 1; i < blueprint.events.Length; i++)
        {
            IfBranchEventSpec spec = blueprint.events[i];
            int turn = startTurn + Mathf.RoundToInt(span * spec.turnRatio);
            turn = Mathf.Clamp(turn, startTurn, endTurn);
            nation = spec.nationId > 0 ? spec.nationId : defaultNationId;
            HistoryAlterationResult reg = HistoryBranchManager.RegisterHistoryAlteration(
                turn,
                nation,
                spec.keyEventId,
                spec.outcomeFlag,
                spec.delta,
                createNewBranch: false);
            if (!reg.success)
            {
                continue;
            }

            branchId = reg.branchId;
            if (!string.IsNullOrWhiteSpace(spec.extraUnlockFlag))
            {
                HistoryFlagRegistry.Unlock(spec.extraUnlockFlag);
            }
        }

        HistoryTimelineBranch branch = mgr.FindRegisteredBranchForVerification(branchId);
        if (branch != null)
        {
            branch.displayName = blueprint.displayName;
            mgr.UpsertRegisteredBranchForVerification(branch);
        }

        return branchId;
    }

    private static string GenerateSingleBranch(
        IfBranchBlueprint blueprint,
        int startTurn,
        int endTurn,
        int span,
        int defaultNationId,
        HistoryBranchManager mgr)
    {
        if (blueprint?.events == null || blueprint.events.Length == 0)
        {
            return string.Empty;
        }

        string branchId = string.Empty;
        for (int i = 0; i < blueprint.events.Length; i++)
        {
            IfBranchEventSpec spec = blueprint.events[i];
            int turn = startTurn + Mathf.RoundToInt(span * spec.turnRatio);
            turn = Mathf.Clamp(turn, startTurn, endTurn);
            int nation = spec.nationId > 0 ? spec.nationId : defaultNationId;

            HistoryAlterationResult reg = HistoryBranchManager.RegisterHistoryAlteration(
                turn,
                nation,
                spec.keyEventId,
                spec.outcomeFlag,
                spec.delta,
                createNewBranch: i == 0);

            if (!reg.success)
            {
                continue;
            }

            branchId = reg.branchId;
            if (!string.IsNullOrWhiteSpace(spec.extraUnlockFlag))
            {
                HistoryFlagRegistry.Unlock(spec.extraUnlockFlag);
            }
        }

        if (string.IsNullOrWhiteSpace(branchId))
        {
            return string.Empty;
        }

        HistoryTimelineBranch branch = mgr.FindRegisteredBranchForVerification(branchId);
        if (branch != null)
        {
            branch.displayName = blueprint.displayName;
            mgr.UpsertRegisteredBranchForVerification(branch);
        }

        return branchId;
    }

    private static string EnsureFallbackBranch(int startTurn, int endTurn, HistoryBranchManager mgr)
    {
        foreach (HistoryTimelineBranch existing in mgr.RegisteredBranches)
        {
            if (existing != null &&
                string.Equals(existing.displayName, FallbackBranchDisplayName, StringComparison.OrdinalIgnoreCase))
            {
                return existing.branchId;
            }
        }

        int turn = Mathf.Clamp(startTurn + Mathf.Max(1, (endTurn - startTurn) / 3), startTurn, endTurn);
        HistoryAlterationResult reg = HistoryBranchManager.RegisterHistoryAlteration(
            turn,
            MicroToMacroAggregator.DefaultNationId,
            MicroToMacroAggregator.FlagHero,
            $"ALT_NATION_{MicroToMacroAggregator.DefaultNationId:D3}_OUTCOME_T{turn:D3}_FALLBACK_HERO",
            new MacroParamDelta
            {
                powerMultiplier = 1.06f,
                barrierEfficiencyDelta = 0.04f,
                threatMultiplier = 0.90f
            },
            createNewBranch: true);

        HistoryTimelineBranch branch = mgr.FindRegisteredBranchForVerification(reg.branchId);
        if (branch != null)
        {
            branch.displayName = FallbackBranchDisplayName;
            mgr.UpsertRegisteredBranchForVerification(branch);
        }

        return reg.branchId;
    }

    private static void SyncSimulationTurn(int endTurn)
    {
        EraContextResolver.TrySetCurrentTurn(endTurn);
        DailySimulationEngine daily = DailySimulationEngine.EnsureInstance();
        daily.TryPrepareForTurn(endTurn);
        MicroToMacroAggregator.EnsureInstance().PrepareTurnTransition(endTurn, applyBranchAdjustments: false);
    }

    private static bool IsFallbackBranch(TimelineScriptEvaluationResult row)
    {
        return row != null &&
               string.Equals(row.branchName, FallbackBranchDisplayName, StringComparison.OrdinalIgnoreCase);
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

    private static TimelineScriptEvaluationResult SelectBestAltBranch(
        List<TimelineScriptEvaluationResult> ranked,
        out bool scoreTied)
    {
        scoreTied = false;
        TimelineScriptEvaluationResult best = null;
        TimelineScriptEvaluationResult second = null;

        for (int i = 0; i < ranked.Count; i++)
        {
            TimelineScriptEvaluationResult row = ranked[i];
            if (row == null || row.isCanon || string.IsNullOrWhiteSpace(row.branchId) || IsFallbackBranch(row))
            {
                continue;
            }

            if (best == null || row.totalScore > best.totalScore)
            {
                second = best;
                best = row;
                continue;
            }

            if (second == null || row.totalScore > second.totalScore)
            {
                second = row;
            }
        }

        if (best != null && second != null &&
            Mathf.Abs(best.totalScore - second.totalScore) <= ScoreTieThreshold)
        {
            scoreTied = true;
        }

        return best;
    }

    private static string BuildSummaryMessage(
        AutoBranchGenerationResult result,
        List<TimelineScriptEvaluationResult> ranked)
    {
        StringBuilder sb = new StringBuilder();
        sb.Append($"generated={result.generatedCount} committed={result.committedBranchId} ");
        sb.Append($"score={result.bestScore} fallback={result.usedFallbackBranch}");
        if (ranked != null && ranked.Count > 0)
        {
            sb.Append(" rankings=");
            int limit = Mathf.Min(ranked.Count, 6);
            for (int i = 0; i < limit; i++)
            {
                TimelineScriptEvaluationResult row = ranked[i];
                if (row == null)
                {
                    continue;
                }

                sb.Append('[').Append(row.branchName).Append('=').Append(row.totalScore).Append(']');
            }
        }

        return sb.ToString();
    }

    private static void LogRankings(int endTurn, List<TimelineScriptEvaluationResult> ranked)
    {
        if (ranked == null || ranked.Count == 0)
        {
            return;
        }

        StringBuilder sb = new StringBuilder();
        sb.AppendLine($"<color=#B39DDB><b>{LogTag}</b></color> T{endTurn} 評価一覧:");
        for (int i = 0; i < ranked.Count; i++)
        {
            TimelineScriptEvaluationResult row = ranked[i];
            if (row == null)
            {
                continue;
            }

            string tag = row.isCanon ? "正史" : "ALT";
            sb.AppendLine(
                $"  {i + 1}. [{tag}] {row.branchName} Total={row.totalScore} " +
                $"(Dyn={row.dynamismScore} Human={row.humanConflictScore} " +
                $"Tech={row.techGrowthScore} Cont={row.continuityScore} bonus=+{row.concentrationBonus})");
        }

        Debug.Log(sb.ToString().TrimEnd());
    }

    private static List<IfBranchBlueprint> BuildBlueprintPool()
    {
        int nation = MicroToMacroAggregator.DefaultNationId;
        return new List<IfBranchBlueprint>
        {
            new IfBranchBlueprint
            {
                displayName = "IF_Line: 王位継承と西境戦火",
                events = new[]
                {
                    Spec(0.16f, nation, "HIST_NATION_001_GEO_TURN_001_SUCCESSION",
                        new MacroParamDelta { powerMultiplier = 1.08f, barrierEfficiencyDelta = 0.04f, threatMultiplier = 1.05f }),
                    Spec(0.42f, nation, "HIST_NATION_001_GEO_TURN_001_CIVILWAR",
                        new MacroParamDelta { powerMultiplier = 0.96f, barrierEfficiencyDelta = -0.02f, threatMultiplier = 1.12f }),
                    Spec(0.72f, nation, "HIST_NATION_001_GEO_TURN_001_MONSTER_DEFENSE",
                        "ALT_NATION_001_OUTCOME_T014_MONSTER_DEFENSE",
                        new MacroParamDelta { powerMultiplier = 1.06f, barrierEfficiencyDelta = 0.03f, threatMultiplier = 0.92f })
                }
            },
            new IfBranchBlueprint
            {
                displayName = "IF_Line: 魔導革新の年代",
                events = new[]
                {
                    Spec(0.10f, nation, "HIST_NATION_001_GEO_TURN_001_INNOVATION",
                        "ALT_NATION_001_OUTCOME_T005_INNOVATION",
                        new MacroParamDelta { powerMultiplier = 1.14f, barrierEfficiencyDelta = 0.05f, threatMultiplier = 0.94f },
                        "MAGIC_DYN_ACADEMY_SYNTHESIS"),
                    Spec(0.55f, nation, "HIST_NATION_001_GEO_TURN_001_ACADEMY_RIVALRY",
                        new MacroParamDelta { powerMultiplier = 1.06f, barrierEfficiencyDelta = 0.02f, threatMultiplier = 1.05f },
                        "JOB_ACADEMY_SCHOLAR"),
                    Spec(0.78f, 2, "HIST_NATION_002_GEO_TURN_001_ACADEMY_RIVALRY",
                        new MacroParamDelta { powerMultiplier = 1.02f, barrierEfficiencyDelta = 0.01f })
                }
            },
            new IfBranchBlueprint
            {
                displayName = "IF_Line: 結界防衛と英雄伝",
                events = new[]
                {
                    Spec(0.22f, nation, "HIST_NATION_001_GEO_TURN_001_HERO",
                        new MacroParamDelta { powerMultiplier = 1.10f, barrierEfficiencyDelta = 0.06f, threatMultiplier = 0.82f }),
                    Spec(0.50f, nation, "HIST_NATION_001_GEO_TURN_001_DEFENSE",
                        "ALT_NATION_001_DEFENSE_T001",
                        new MacroParamDelta { powerMultiplier = 1.07f, barrierEfficiencyDelta = 0.05f, threatMultiplier = 0.88f }),
                    Spec(0.80f, nation, "HIST_NATION_001_GEO_TURN_001_INNOVATION",
                        new MacroParamDelta { powerMultiplier = 1.08f, barrierEfficiencyDelta = 0.03f, threatMultiplier = 0.95f })
                }
            },
            new IfBranchBlueprint
            {
                displayName = "IF_Line: 結界喪失の冬",
                events = new[]
                {
                    Spec(0.30f, nation, "HIST_NATION_001_GEO_TURN_001_BARRIERDROP",
                        new MacroParamDelta
                        {
                            powerMultiplier = 0.82f,
                            barrierEfficiencyDelta = -0.18f,
                            threatMultiplier = 1.55f,
                            isSurvivalOverridden = true,
                            survivalValue = false
                        })
                }
            },
            new IfBranchBlueprint
            {
                displayName = "IF_Line: 派閥均衡と技術発展",
                events = new[]
                {
                    Spec(0.20f, nation, "HIST_NATION_001_GEO_TURN_001_SUCCESSION",
                        new MacroParamDelta { powerMultiplier = 1.05f, barrierEfficiencyDelta = 0.03f }),
                    Spec(0.45f, nation, "HIST_NATION_001_GEO_TURN_001_INNOVATION",
                        new MacroParamDelta { powerMultiplier = 1.10f, barrierEfficiencyDelta = 0.04f, threatMultiplier = 0.96f }),
                    Spec(0.70f, nation, "HIST_NATION_001_GEO_TURN_001_HERO",
                        new MacroParamDelta { powerMultiplier = 1.06f, barrierEfficiencyDelta = 0.03f, threatMultiplier = 0.90f })
                }
            }
        };
    }

    private static List<IfBranchBlueprint> ResolveBlueprintPool(int startTurn)
    {
        if (startTurn >= Generation2TransitionEngine.Generation5StartTurn)
        {
            return BuildGeneration5BlueprintPool();
        }

        if (startTurn >= Generation2TransitionEngine.Generation4StartTurn)
        {
            return BuildGeneration4BlueprintPool();
        }

        if (startTurn >= Generation2TransitionEngine.Generation3StartTurn)
        {
            return BuildGeneration3BlueprintPool();
        }

        if (startTurn >= Generation2TransitionEngine.Generation2StartTurn)
        {
            return BuildGeneration2BlueprintPool();
        }

        return BuildBlueprintPool();
    }

    private static string ResolveParentBranchId(string parentBranchId, HistoryBranchManager mgr)
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

    private static List<IfBranchBlueprint> BuildGeneration2BlueprintPool()
    {
        int nation = MicroToMacroAggregator.DefaultNationId;
        return new List<IfBranchBlueprint>
        {
            new IfBranchBlueprint
            {
                displayName = "IF_Line: 派閥対立激化",
                branchSuffix = "FACTION_WAR",
                events = new[]
                {
                    Spec(0.12f, nation, "HIST_NATION_001_GEO_TURN_001_SUCCESSION",
                        new MacroParamDelta { powerMultiplier = 1.06f, barrierEfficiencyDelta = 0.03f, threatMultiplier = 1.08f }),
                    Spec(0.38f, nation, "HIST_NATION_001_GEO_TURN_001_CIVILWAR",
                        new MacroParamDelta { powerMultiplier = 0.94f, barrierEfficiencyDelta = -0.03f, threatMultiplier = 1.15f }),
                    Spec(0.70f, nation, "HIST_NATION_001_GEO_TURN_001_ACADEMY_RIVALRY",
                        new MacroParamDelta { powerMultiplier = 1.05f, barrierEfficiencyDelta = 0.02f, threatMultiplier = 1.04f },
                        "JOB_ACADEMY_SCHOLAR")
                }
            },
            new IfBranchBlueprint
            {
                displayName = "IF_Line: 失伝技術復元",
                branchSuffix = "LOST_TECH",
                events = new[]
                {
                    Spec(0.15f, nation, "HIST_NATION_001_GEO_TURN_001_INNOVATION",
                        new MacroParamDelta { powerMultiplier = 1.16f, barrierEfficiencyDelta = 0.06f, threatMultiplier = 0.90f },
                        "MAGIC_DYN_ACADEMY_SYNTHESIS"),
                    Spec(0.55f, nation, "HIST_NATION_001_GEO_TURN_001_HERO",
                        new MacroParamDelta { powerMultiplier = 1.10f, barrierEfficiencyDelta = 0.05f, threatMultiplier = 0.85f }),
                    Spec(0.85f, nation, "HIST_NATION_001_GEO_TURN_001_INNOVATION",
                        new MacroParamDelta { powerMultiplier = 1.08f, barrierEfficiencyDelta = 0.04f, threatMultiplier = 0.92f },
                        "MAGIC_DYN_FORGE_SPIRIT")
                }
            },
            new IfBranchBlueprint
            {
                displayName = "IF_Line: 防衛線拡張",
                branchSuffix = "DEFENSE_LINE",
                events = new[]
                {
                    Spec(0.20f, nation, "HIST_NATION_001_GEO_TURN_001_HERO",
                        new MacroParamDelta { powerMultiplier = 1.09f, barrierEfficiencyDelta = 0.07f, threatMultiplier = 0.80f }),
                    Spec(0.50f, nation, "HIST_NATION_001_GEO_TURN_001_MONSTER_DEFENSE",
                        "ALT_NATION_001_OUTCOME_T014_MONSTER_DEFENSE",
                        new MacroParamDelta { powerMultiplier = 1.08f, barrierEfficiencyDelta = 0.05f, threatMultiplier = 0.88f }),
                    Spec(0.80f, nation, "HIST_NATION_001_GEO_TURN_001_DEFENSE",
                        new MacroParamDelta { powerMultiplier = 1.07f, barrierEfficiencyDelta = 0.04f, threatMultiplier = 0.86f })
                }
            },
            new IfBranchBlueprint
            {
                displayName = "IF_Line: 学院分裂",
                branchSuffix = "ACADEMY_SPLIT",
                events = new[]
                {
                    Spec(0.18f, nation, "HIST_NATION_001_GEO_TURN_001_ACADEMY_RIVALRY",
                        new MacroParamDelta { powerMultiplier = 1.04f, barrierEfficiencyDelta = 0.02f, threatMultiplier = 1.06f }),
                    Spec(0.48f, nation, "HIST_NATION_001_GEO_TURN_001_INNOVATION",
                        new MacroParamDelta { powerMultiplier = 1.12f, barrierEfficiencyDelta = 0.05f, threatMultiplier = 0.93f }),
                    Spec(0.78f, 2, "HIST_NATION_002_GEO_TURN_001_ACADEMY_RIVALRY",
                        new MacroParamDelta { powerMultiplier = 1.03f, barrierEfficiencyDelta = 0.02f })
                }
            },
            new IfBranchBlueprint
            {
                displayName = "IF_Line: 魔導復興",
                branchSuffix = "ARCANE_REVIVAL",
                events = new[]
                {
                    Spec(0.14f, nation, "HIST_NATION_001_GEO_TURN_001_INNOVATION",
                        new MacroParamDelta { powerMultiplier = 1.15f, barrierEfficiencyDelta = 0.05f, threatMultiplier = 0.91f }),
                    Spec(0.44f, nation, "HIST_NATION_001_GEO_TURN_001_SUCCESSION",
                        new MacroParamDelta { powerMultiplier = 1.07f, barrierEfficiencyDelta = 0.03f, threatMultiplier = 1.02f }),
                    Spec(0.74f, nation, "HIST_NATION_001_GEO_TURN_001_HERO",
                        new MacroParamDelta { powerMultiplier = 1.08f, barrierEfficiencyDelta = 0.04f, threatMultiplier = 0.87f })
                }
            }
        };
    }

    private static List<IfBranchBlueprint> BuildGeneration3BlueprintPool()
    {
        int nation = MicroToMacroAggregator.DefaultNationId;
        return new List<IfBranchBlueprint>
        {
            new IfBranchBlueprint
            {
                displayName = "IF_Line: 古代遺跡調査",
                branchSuffix = "ANCIENT_RUINS",
                events = new[]
                {
                    Spec(0.16f, nation, "HIST_NATION_001_GEO_TURN_001_INNOVATION",
                        new MacroParamDelta { powerMultiplier = 1.14f, barrierEfficiencyDelta = 0.04f, threatMultiplier = 0.94f }),
                    Spec(0.46f, nation, "HIST_NATION_001_GEO_TURN_001_HERO",
                        new MacroParamDelta { powerMultiplier = 1.11f, barrierEfficiencyDelta = 0.05f, threatMultiplier = 0.89f }),
                    Spec(0.76f, nation, "HIST_NATION_001_GEO_TURN_001_SUCCESSION",
                        new MacroParamDelta { powerMultiplier = 1.09f, barrierEfficiencyDelta = 0.03f, threatMultiplier = 0.95f })
                }
            },
            new IfBranchBlueprint
            {
                displayName = "IF_Line: 多国連合",
                branchSuffix = "MULTI_NATION",
                events = new[]
                {
                    Spec(0.18f, nation, "HIST_NATION_001_GEO_TURN_001_DEFENSE",
                        new MacroParamDelta { powerMultiplier = 1.08f, barrierEfficiencyDelta = 0.06f, threatMultiplier = 0.82f }),
                    Spec(0.48f, 2, "HIST_NATION_002_GEO_TURN_001_ACADEMY_RIVALRY",
                        new MacroParamDelta { powerMultiplier = 1.06f, barrierEfficiencyDelta = 0.04f }),
                    Spec(0.78f, nation, "HIST_NATION_001_GEO_TURN_001_MONSTER_DEFENSE",
                        new MacroParamDelta { powerMultiplier = 1.07f, barrierEfficiencyDelta = 0.05f, threatMultiplier = 0.88f })
                }
            },
            new IfBranchBlueprint
            {
                displayName = "IF_Line: 魔王軍前線",
                branchSuffix = "DEMON_FRONT",
                events = new[]
                {
                    Spec(0.20f, nation, "HIST_NATION_001_GEO_TURN_001_MONSTER_DEFENSE",
                        "ALT_NATION_001_OUTCOME_T014_MONSTER_DEFENSE",
                        new MacroParamDelta { powerMultiplier = 1.12f, barrierEfficiencyDelta = 0.04f, threatMultiplier = 1.08f }),
                    Spec(0.50f, nation, "HIST_NATION_001_GEO_TURN_001_DEFENSE",
                        new MacroParamDelta { powerMultiplier = 1.10f, barrierEfficiencyDelta = 0.05f, threatMultiplier = 0.90f }),
                    Spec(0.80f, nation, "HIST_NATION_001_GEO_TURN_001_HERO",
                        new MacroParamDelta { powerMultiplier = 1.09f, barrierEfficiencyDelta = 0.03f, threatMultiplier = 0.92f })
                }
            },
            new IfBranchBlueprint
            {
                displayName = "IF_Line: 秘術継承",
                branchSuffix = "SECRET_ARTS",
                events = new[]
                {
                    Spec(0.14f, nation, "HIST_NATION_001_GEO_TURN_001_INNOVATION",
                        new MacroParamDelta { powerMultiplier = 1.16f, barrierEfficiencyDelta = 0.05f, threatMultiplier = 0.93f },
                        "MAGIC_DYN_ACADEMY_SYNTHESIS"),
                    Spec(0.44f, nation, "HIST_NATION_001_GEO_TURN_001_HERO",
                        new MacroParamDelta { powerMultiplier = 1.08f, barrierEfficiencyDelta = 0.04f, threatMultiplier = 0.87f }),
                    Spec(0.74f, nation, "HIST_NATION_001_GEO_TURN_001_SUCCESSION",
                        new MacroParamDelta { powerMultiplier = 1.06f, barrierEfficiencyDelta = 0.03f, threatMultiplier = 0.96f })
                }
            },
            new IfBranchBlueprint
            {
                displayName = "IF_Line: 結界超拡張",
                branchSuffix = "BARRIER_MEGA",
                events = new[]
                {
                    Spec(0.17f, nation, "HIST_NATION_001_GEO_TURN_001_DEFENSE",
                        new MacroParamDelta { powerMultiplier = 1.07f, barrierEfficiencyDelta = 0.08f, threatMultiplier = 0.84f }),
                    Spec(0.47f, nation, "HIST_NATION_001_GEO_TURN_001_INNOVATION",
                        new MacroParamDelta { powerMultiplier = 1.10f, barrierEfficiencyDelta = 0.06f, threatMultiplier = 0.91f }),
                    Spec(0.77f, nation, "HIST_NATION_001_GEO_TURN_001_MONSTER_DEFENSE",
                        new MacroParamDelta { powerMultiplier = 1.08f, barrierEfficiencyDelta = 0.05f, threatMultiplier = 0.86f })
                }
            }
        };
    }

    private static List<IfBranchBlueprint> BuildGeneration4BlueprintPool()
    {
        int nation = MicroToMacroAggregator.DefaultNationId;
        return new List<IfBranchBlueprint>
        {
            new IfBranchBlueprint
            {
                displayName = "IF_Line: 失伝技術の再掘削",
                branchSuffix = "LOST_TECH_REEXCAVATION",
                events = new[]
                {
                    Spec(0.15f, nation, "HIST_NATION_001_GEO_TURN_001_INNOVATION",
                        new MacroParamDelta { powerMultiplier = 1.13f, barrierEfficiencyDelta = 0.04f, threatMultiplier = 0.93f }),
                    Spec(0.45f, nation, "HIST_NATION_001_GEO_TURN_001_HERO",
                        new MacroParamDelta { powerMultiplier = 1.10f, barrierEfficiencyDelta = 0.05f, threatMultiplier = 0.88f }),
                    Spec(0.75f, nation, "HIST_NATION_001_GEO_TURN_001_SUCCESSION",
                        new MacroParamDelta { powerMultiplier = 1.08f, barrierEfficiencyDelta = 0.03f, threatMultiplier = 0.94f })
                }
            },
            new IfBranchBlueprint
            {
                displayName = "IF_Line: 結界全壊防衛戦",
                branchSuffix = "BARRIER_TOTAL_WAR",
                events = new[]
                {
                    Spec(0.18f, nation, "HIST_NATION_001_GEO_TURN_001_DEFENSE",
                        new MacroParamDelta { powerMultiplier = 1.09f, barrierEfficiencyDelta = 0.09f, threatMultiplier = 1.05f }),
                    Spec(0.48f, nation, "HIST_NATION_001_GEO_TURN_001_MONSTER_DEFENSE",
                        "ALT_NATION_001_OUTCOME_T014_MONSTER_DEFENSE",
                        new MacroParamDelta { powerMultiplier = 1.11f, barrierEfficiencyDelta = 0.06f, threatMultiplier = 0.92f }),
                    Spec(0.78f, nation, "HIST_NATION_001_GEO_TURN_001_HERO",
                        new MacroParamDelta { powerMultiplier = 1.08f, barrierEfficiencyDelta = 0.05f, threatMultiplier = 0.87f })
                }
            },
            new IfBranchBlueprint
            {
                displayName = "IF_Line: 広域同盟結成",
                branchSuffix = "WIDE_ALLIANCE",
                events = new[]
                {
                    Spec(0.16f, nation, "HIST_NATION_001_GEO_TURN_001_DEFENSE",
                        new MacroParamDelta { powerMultiplier = 1.07f, barrierEfficiencyDelta = 0.06f, threatMultiplier = 0.83f }),
                    Spec(0.46f, 2, "HIST_NATION_002_GEO_TURN_001_ACADEMY_RIVALRY",
                        new MacroParamDelta { powerMultiplier = 1.05f, barrierEfficiencyDelta = 0.04f }),
                    Spec(0.76f, nation, "HIST_NATION_001_GEO_TURN_001_INNOVATION",
                        new MacroParamDelta { powerMultiplier = 1.09f, barrierEfficiencyDelta = 0.05f, threatMultiplier = 0.90f })
                }
            },
            new IfBranchBlueprint
            {
                displayName = "IF_Line: 異端魔導の覚醒",
                branchSuffix = "HERETIC_ARCANA",
                events = new[]
                {
                    Spec(0.14f, nation, "HIST_NATION_001_GEO_TURN_001_INNOVATION",
                        new MacroParamDelta { powerMultiplier = 1.17f, barrierEfficiencyDelta = 0.03f, threatMultiplier = 1.04f },
                        "MAGIC_DYN_ACADEMY_SYNTHESIS"),
                    Spec(0.44f, nation, "HIST_NATION_001_GEO_TURN_001_ACADEMY_RIVALRY",
                        new MacroParamDelta { powerMultiplier = 1.06f, barrierEfficiencyDelta = 0.02f, threatMultiplier = 1.08f }),
                    Spec(0.74f, nation, "HIST_NATION_001_GEO_TURN_001_HERO",
                        new MacroParamDelta { powerMultiplier = 1.07f, barrierEfficiencyDelta = 0.04f, threatMultiplier = 0.91f })
                }
            },
            new IfBranchBlueprint
            {
                displayName = "IF_Line: 要塞都市化",
                branchSuffix = "FORTRESS_CITY",
                events = new[]
                {
                    Spec(0.17f, nation, "HIST_NATION_001_GEO_TURN_001_DEFENSE",
                        new MacroParamDelta { powerMultiplier = 1.10f, barrierEfficiencyDelta = 0.07f, threatMultiplier = 0.85f }),
                    Spec(0.47f, nation, "HIST_NATION_001_GEO_TURN_001_MONSTER_DEFENSE",
                        new MacroParamDelta { powerMultiplier = 1.09f, barrierEfficiencyDelta = 0.06f, threatMultiplier = 0.89f }),
                    Spec(0.77f, nation, "HIST_NATION_001_GEO_TURN_001_SUCCESSION",
                        new MacroParamDelta { powerMultiplier = 1.06f, barrierEfficiencyDelta = 0.05f, threatMultiplier = 0.93f })
                }
            }
        };
    }

    private static List<IfBranchBlueprint> BuildGeneration5BlueprintPool()
    {
        int nation = MicroToMacroAggregator.DefaultNationId;
        return new List<IfBranchBlueprint>
        {
            new IfBranchBlueprint
            {
                displayName = "IF_Line: 古代秘術の解読・復元",
                branchSuffix = "ANCIENT_ARCANA_RESTORE",
                events = new[]
                {
                    Spec(0.14f, nation, "HIST_NATION_001_GEO_TURN_001_INNOVATION",
                        new MacroParamDelta { powerMultiplier = 1.15f, barrierEfficiencyDelta = 0.04f, threatMultiplier = 0.92f }),
                    Spec(0.44f, nation, "HIST_NATION_001_GEO_TURN_001_HERO",
                        new MacroParamDelta { powerMultiplier = 1.11f, barrierEfficiencyDelta = 0.05f, threatMultiplier = 0.88f }),
                    Spec(0.74f, nation, "HIST_NATION_001_GEO_TURN_001_SUCCESSION",
                        new MacroParamDelta { powerMultiplier = 1.09f, barrierEfficiencyDelta = 0.03f, threatMultiplier = 0.94f })
                }
            },
            new IfBranchBlueprint
            {
                displayName = "IF_Line: 封印結界の再編",
                branchSuffix = "SEAL_BARRIER_REORG",
                events = new[]
                {
                    Spec(0.16f, nation, "HIST_NATION_001_GEO_TURN_001_DEFENSE",
                        new MacroParamDelta { powerMultiplier = 1.08f, barrierEfficiencyDelta = 0.10f, threatMultiplier = 0.86f }),
                    Spec(0.46f, nation, "HIST_NATION_001_GEO_TURN_001_MONSTER_DEFENSE",
                        new MacroParamDelta { powerMultiplier = 1.10f, barrierEfficiencyDelta = 0.07f, threatMultiplier = 0.90f }),
                    Spec(0.76f, nation, "HIST_NATION_001_GEO_TURN_001_INNOVATION",
                        new MacroParamDelta { powerMultiplier = 1.08f, barrierEfficiencyDelta = 0.06f, threatMultiplier = 0.91f })
                }
            },
            new IfBranchBlueprint
            {
                displayName = "IF_Line: 異端魔法の正統化",
                branchSuffix = "HERETIC_ORTHODOXY",
                events = new[]
                {
                    Spec(0.15f, nation, "HIST_NATION_001_GEO_TURN_001_INNOVATION",
                        new MacroParamDelta { powerMultiplier = 1.16f, barrierEfficiencyDelta = 0.03f, threatMultiplier = 1.02f },
                        "MAGIC_DYN_ACADEMY_SYNTHESIS"),
                    Spec(0.45f, nation, "HIST_NATION_001_GEO_TURN_001_ACADEMY_RIVALRY",
                        new MacroParamDelta { powerMultiplier = 1.07f, barrierEfficiencyDelta = 0.02f, threatMultiplier = 1.04f }),
                    Spec(0.75f, nation, "HIST_NATION_001_GEO_TURN_001_HERO",
                        new MacroParamDelta { powerMultiplier = 1.08f, barrierEfficiencyDelta = 0.04f, threatMultiplier = 0.90f })
                }
            },
            new IfBranchBlueprint
            {
                displayName = "IF_Line: 新魔導文明の開化",
                branchSuffix = "NEW_ARCANA_CIVILIZATION",
                events = new[]
                {
                    Spec(0.17f, nation, "HIST_NATION_001_GEO_TURN_001_INNOVATION",
                        new MacroParamDelta { powerMultiplier = 1.14f, barrierEfficiencyDelta = 0.05f, threatMultiplier = 0.93f }),
                    Spec(0.47f, 2, "HIST_NATION_002_GEO_TURN_001_ACADEMY_RIVALRY",
                        new MacroParamDelta { powerMultiplier = 1.06f, barrierEfficiencyDelta = 0.04f }),
                    Spec(0.77f, nation, "HIST_NATION_001_GEO_TURN_001_DEFENSE",
                        new MacroParamDelta { powerMultiplier = 1.07f, barrierEfficiencyDelta = 0.05f, threatMultiplier = 0.88f })
                }
            },
            new IfBranchBlueprint
            {
                displayName = "IF_Line: 全土伝承完了",
                branchSuffix = "FULL_LORE_COMPLETION",
                events = new[]
                {
                    Spec(0.18f, nation, "HIST_NATION_001_GEO_TURN_001_SUCCESSION",
                        new MacroParamDelta { powerMultiplier = 1.06f, barrierEfficiencyDelta = 0.04f, threatMultiplier = 0.95f }),
                    Spec(0.48f, nation, "HIST_NATION_001_GEO_TURN_001_HERO",
                        new MacroParamDelta { powerMultiplier = 1.09f, barrierEfficiencyDelta = 0.05f, threatMultiplier = 0.87f }),
                    Spec(0.78f, nation, "HIST_NATION_001_GEO_TURN_001_INNOVATION",
                        new MacroParamDelta { powerMultiplier = 1.10f, barrierEfficiencyDelta = 0.04f, threatMultiplier = 0.92f })
                }
            }
        };
    }

    private static IfBranchEventSpec Spec(
        float turnRatio,
        int nationId,
        string keyEventId,
        MacroParamDelta delta,
        string extraUnlockFlag = null)
    {
        return Spec(turnRatio, nationId, keyEventId, null, delta, extraUnlockFlag);
    }

    private static IfBranchEventSpec Spec(
        float turnRatio,
        int nationId,
        string keyEventId,
        string outcomeFlag,
        MacroParamDelta delta,
        string extraUnlockFlag = null)
    {
        return new IfBranchEventSpec
        {
            turnRatio = turnRatio,
            nationId = nationId,
            keyEventId = keyEventId,
            outcomeFlag = outcomeFlag,
            delta = delta,
            extraUnlockFlag = extraUnlockFlag
        };
    }

    private static void WriteVerifyLog(AutoBranchGeneratorVerifyResult verify)
    {
        try
        {
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
            string path = Path.Combine(projectRoot, "Logs", "auto_branch_generator_verify.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? projectRoot);
            File.WriteAllText(
                path,
                $"{DateTime.UtcNow:O} success={verify.success}\n{verify.message}\n",
                Encoding.UTF8);
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[AutoBranchGenerator] 検証ログスキップ: {exception.Message}");
        }
    }

    private sealed class IfBranchBlueprint
    {
        public string displayName = string.Empty;
        public string branchSuffix = string.Empty;
        public IfBranchEventSpec[] events = Array.Empty<IfBranchEventSpec>();
    }

    private sealed class IfBranchEventSpec
    {
        public float turnRatio;
        public int nationId;
        public string keyEventId = string.Empty;
        public string outcomeFlag;
        public MacroParamDelta delta;
        public string extraUnlockFlag;
    }
}

public static class AutoBranchGeneratorBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoAttach()
    {
        AutoBranchGenerator.EnsureInstance();
    }
}

#if UNITY_EDITOR
public static class AutoBranchGeneratorMenu
{
    [MenuItem("Tools/Procedural Map/Generate And Evaluate IF Branches (T51-100)")]
    public static void GenerateGen2FromMenu()
    {
        AutoBranchGenerationResult result = AutoBranchGenerator.GenerateAndEvaluateBranches(51, 100, 5);
        EditorUtility.DisplayDialog("Auto Branch Generator Gen2", result.message, "OK");
    }

    [MenuItem("Tools/Procedural Map/Generate And Evaluate IF Branches (T1-50)")]
    public static void GenerateFromMenu()
    {
        AutoBranchGenerationResult result = AutoBranchGenerator.GenerateAndEvaluateBranches(1, 50, 5);
        EditorUtility.DisplayDialog("Auto Branch Generator", result.message, "OK");
    }

    [MenuItem("Tools/Procedural Map/Verify Auto Branch Generator")]
    public static void VerifyFromMenu()
    {
        AutoBranchGeneratorVerifyResult result = AutoBranchGenerator.RunVerification();
        Debug.Log(
            result.success
                ? $"<color=#A5D6A7><b>【自動IF分岐検証】PASS</b></color>\n{result.message}"
                : $"<color=#FF8A80><b>【自動IF分岐検証】FAIL</b></color>\n{result.message}");
        EditorUtility.DisplayDialog("Auto Branch Generator", result.message, "OK");
    }
}
#endif

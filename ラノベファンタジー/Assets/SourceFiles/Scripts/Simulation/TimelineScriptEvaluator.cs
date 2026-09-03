using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

// =============================================================================
// 歴史台本評価エンジン — ALT_ 分岐ラインのストーリー魅力スコアリング
// 連携: HistoryTimelineBranch / HistoryBranchManager / HistoryFlagRegistry
//       HumanConflictEngine / DynamicMasterGenerationEngine
// =============================================================================

/// <summary>歴史台本 4 軸評価結果（各 100 点満点、合計 400 点）。</summary>
public sealed class TimelineScriptEvaluationResult
{
    public bool success;
    public string branchId = string.Empty;
    public string branchName = string.Empty;
    public int dynamismScore;
    public int humanConflictScore;
    public int techGrowthScore;
    public int continuityScore;
    public int totalScore;
    public int evaluatedTurn = 1;
    public bool isCanon;
    public bool usedSafeFailFallback;
    public int peakNationId;
    public float peakSpikeScore;
    public int concentrationBonus;
    public string message = string.Empty;
}

/// <summary>メイン歴史軸確定結果。</summary>
public sealed class MainStoryCommitResult
{
    public bool success;
    public string branchId = string.Empty;
    public string branchName = string.Empty;
    public int totalScore;
    public string message = string.Empty;
}

/// <summary>検証結果。</summary>
public sealed class TimelineScriptEvaluatorVerifyResult
{
    public bool success;
    public string message = string.Empty;
}

/// <summary>
/// 生成済み ALT_ 歴史ラインを 4 指標で評価し、メインストーリー軸の採択を支援します。
/// </summary>
[DefaultExecutionOrder(46)]
public class TimelineScriptEvaluator : MonoBehaviour
{
    public const string LogTag = "【歴史台本評価】";
    public const string AdoptLogTag = "【歴史軸採択】";
    public const int ScoreMin = 0;
    public const int ScoreMax = 100;
    public const int SafeFailPerAxisScore = 50;
    public const int SafeFailTotalScore = 200;
    public const float PeakBlendAverageWeight = 0.30f;
    public const float PeakBlendPeakWeight = 0.70f;
    public const int MaxConcentrationBonus = 30;
    public const string PeakBonusLogTag = "【台本評価】";

    public static TimelineScriptEvaluator Instance { get; private set; }

    public static TimelineScriptEvaluator EnsureInstance()
    {
        if (Instance != null)
        {
            return Instance;
        }

        TimelineScriptEvaluator existing = UnityEngine.Object.FindAnyObjectByType<TimelineScriptEvaluator>();
        if (existing != null)
        {
            Instance = existing;
            return existing;
        }

        GameObject hub = GameObject.Find("DebugSystemsHub");
        GameObject host = hub != null ? hub : new GameObject(nameof(TimelineScriptEvaluator));
        TimelineScriptEvaluator evaluator = host.GetComponent<TimelineScriptEvaluator>();
        return evaluator != null ? evaluator : host.AddComponent<TimelineScriptEvaluator>();
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

    /// <summary>現在の進行ターンを Daily / Aggregator / Era から解決します。</summary>
    public static int ResolveCurrentEvaluationTurn()
    {
        DailySimulationEngine daily = DailySimulationEngine.Instance;
        if (daily != null && daily.Turn > 0)
        {
            return daily.Turn;
        }

        MicroToMacroAggregator aggregator = MicroToMacroAggregator.Instance;
        if (aggregator != null && aggregator.Turn > 0)
        {
            return aggregator.Turn;
        }

        return EraContextResolver.CurrentTurn > 0 ? EraContextResolver.CurrentTurn : EraContextResolver.MinTurn;
    }

    /// <summary>評価ターンを 1〜1000 に正規化します。</summary>
    public static int ClampEvaluationTurn(int turn)
    {
        return EraContextResolver.ClampSimulationTurn(turn);
    }

    /// <summary>ALT_ / 正史ラインを targetTurn 時点で 4 指標評価（未指定時は現在進行ターン）。</summary>
    public static TimelineScriptEvaluationResult EvaluateTimelineBranch(
        HistoryTimelineBranch branch,
        int? targetTurn = null)
    {
        TimelineScriptEvaluationResult result = new TimelineScriptEvaluationResult();
        int probeTurn = ClampEvaluationTurn(targetTurn ?? ResolveCurrentEvaluationTurn());
        result.evaluatedTurn = probeTurn;

        try
        {
            HistoryFlagRegistry.EnsureWired();
            if (branch == null || string.IsNullOrWhiteSpace(branch.branchId))
            {
                return BuildSafeFailResult(result, string.Empty, probeTurn, "branch が null / branchId 欠損");
            }

            result.branchId = branch.branchId;
            result.branchName = ResolveBranchDisplayName(branch);
            result.isCanon = HistoryBranchManager.IsCanonBranchKey(branch.branchId);

            if (branch.nodes == null || branch.nodes.Count == 0)
            {
                if (result.isCanon)
                {
                    return BuildSafeFailResult(result, branch.branchId, probeTurn, "正史ノードなし — 標準スコアへフォールバック");
                }

                return BuildSafeFailResult(result, branch.branchId, probeTurn, "ノード 0 件 — 標準スコアへフォールバック");
            }

            List<HistoryBranchNode> nodesInRange = branch.GetNodesUpToTurn(probeTurn);
            if (ShouldUseSparseSafeFail(probeTurn, nodesInRange))
            {
                return BuildSafeFailResult(
                    result,
                    branch.branchId,
                    probeTurn,
                    $"T{probeTurn} 時点で実データ不足 — 標準スコアへフォールバック");
            }

            MacroParamDelta terminalDelta = branch.AccumulateParamDeltaUpToTurn(probeTurn, branch.primaryNationId);
            float societyFactionTension = TryResolveFactionTension();

            NationSpikeScoreBundle spike = ComputeSpikeAwareScores(
                branch,
                nodesInRange,
                societyFactionTension,
                terminalDelta);
            result.dynamismScore = spike.dynamismScore;
            result.humanConflictScore = spike.humanConflictScore;
            result.peakNationId = spike.topNationId;
            result.peakSpikeScore = spike.maxSpikeScore;
            result.concentrationBonus = spike.concentrationBonus;
            if (spike.concentrationBonus > 0 && spike.topNationId > 0)
            {
                Debug.Log(
                    $"<color=#CE93D8><b>{PeakBonusLogTag}</b></color> " +
                    $"集中度ボーナス適用: 対象国家{spike.topNationId:D3} (ピークスコア: {spike.maxSpikeScore:F1})");
            }

            result.techGrowthScore = ScoreTechGrowth(branch, nodesInRange, terminalDelta);
            result.continuityScore = ScoreContinuity(branch, nodesInRange, probeTurn, terminalDelta);
            result.totalScore = result.dynamismScore + result.humanConflictScore +
                                result.techGrowthScore + result.continuityScore;
            result.success = true;
            result.message =
                $"T{probeTurn} {result.branchName} Dyn={result.dynamismScore} Human={result.humanConflictScore} " +
                $"Tech={result.techGrowthScore} Cont={result.continuityScore} Total={result.totalScore} " +
                $"peak=N{result.peakNationId:D3} bonus=+{result.concentrationBonus} nodes={nodesInRange.Count}";
            return result;
        }
        catch (Exception exception)
        {
            result.message = $"Safe-Fail: {exception.Message}";
            Debug.LogWarning($"[TimelineScriptEvaluator] EvaluateTimelineBranch Safe-Fail: {exception.Message}");
            return BuildSafeFailResult(result, branch?.branchId ?? string.Empty, probeTurn, result.message);
        }
    }

    /// <summary>正史 + 全 ALT_ 分岐を現在ターン時点で評価し、総合スコア降順で返します。</summary>
    public static List<TimelineScriptEvaluationResult> EvaluateAllBranchesAtCurrentTurn(int? targetTurn = null)
    {
        int probeTurn = ClampEvaluationTurn(targetTurn ?? ResolveCurrentEvaluationTurn());
        List<TimelineScriptEvaluationResult> ranked = new List<TimelineScriptEvaluationResult>();

        HistoryTimelineBranch canonBranch = new HistoryTimelineBranch
        {
            branchId = HistoryBranchManager.CanonBranchId,
            displayName = "正史 (HIST_)"
        };
        ranked.Add(EvaluateTimelineBranch(canonBranch, probeTurn));

        HistoryBranchManager mgr = HistoryBranchManager.EnsureInstance();
        IReadOnlyList<HistoryTimelineBranch> branches = mgr.RegisteredBranches;
        if (branches != null)
        {
            for (int i = 0; i < branches.Count; i++)
            {
                HistoryTimelineBranch branch = branches[i];
                if (branch == null || string.IsNullOrWhiteSpace(branch.branchId))
                {
                    continue;
                }

                ranked.Add(EvaluateTimelineBranch(branch, probeTurn));
            }
        }

        ranked.Sort((a, b) => b.totalScore.CompareTo(a.totalScore));
        return ranked;
    }

    /// <summary>登録済み ALT_ 分岐を現在ターンで評価し、総合スコア降順で返します。</summary>
    public static List<TimelineScriptEvaluationResult> EvaluateAllRegisteredBranches(int? targetTurn = null)
    {
        List<TimelineScriptEvaluationResult> ranked = new List<TimelineScriptEvaluationResult>();
        HistoryBranchManager mgr = HistoryBranchManager.EnsureInstance();
        IReadOnlyList<HistoryTimelineBranch> branches = mgr.RegisteredBranches;
        if (branches == null)
        {
            return ranked;
        }

        int probeTurn = ClampEvaluationTurn(targetTurn ?? ResolveCurrentEvaluationTurn());

        for (int i = 0; i < branches.Count; i++)
        {
            HistoryTimelineBranch branch = branches[i];
            if (branch == null || string.IsNullOrWhiteSpace(branch.branchId))
            {
                continue;
            }

            ranked.Add(EvaluateTimelineBranch(branch, probeTurn));
        }

        ranked.Sort((a, b) => b.totalScore.CompareTo(a.totalScore));
        return ranked;
    }

    /// <summary>現在ターン時点の最高スコア ALT_ 軸を自動選定して Commit します。</summary>
    public static MainStoryCommitResult CommitBestBranchAtCurrentTurn(int? targetTurn = null)
    {
        int probeTurn = ClampEvaluationTurn(targetTurn ?? ResolveCurrentEvaluationTurn());
        List<TimelineScriptEvaluationResult> ranked = EvaluateAllBranchesAtCurrentTurn(probeTurn);
        TimelineScriptEvaluationResult bestAlt = null;
        for (int i = 0; i < ranked.Count; i++)
        {
            TimelineScriptEvaluationResult row = ranked[i];
            if (row == null || row.isCanon || string.IsNullOrWhiteSpace(row.branchId))
            {
                continue;
            }

            if (bestAlt == null || row.totalScore > bestAlt.totalScore)
            {
                bestAlt = row;
            }
        }

        if (bestAlt == null)
        {
            return new MainStoryCommitResult
            {
                success = false,
                message = $"T{probeTurn} 時点で Commit 可能な ALT_ 分岐がありません"
            };
        }

        MainStoryCommitResult commit = HistoryBranchManager.CommitBranchAsMainStory(
            bestAlt.branchId,
            bestAlt.totalScore,
            probeTurn);
        Debug.Log(
            $"<color=#CE93D8><b>{LogTag}</b></color> T{probeTurn} 自動選定 → {bestAlt.branchName} " +
            $"(Total={bestAlt.totalScore}) Commit={commit.success}");
        return commit;
    }

    public static string ResolveBranchDisplayName(HistoryTimelineBranch branch)
    {
        if (branch == null)
        {
            return "ALT_?";
        }

        if (!string.IsNullOrWhiteSpace(branch.displayName))
        {
            return branch.displayName.Trim();
        }

        return branch.branchId;
    }

    private static TimelineScriptEvaluationResult BuildSafeFailResult(
        TimelineScriptEvaluationResult result,
        string branchId,
        int evaluatedTurn,
        string reason)
    {
        result.branchId = branchId ?? string.Empty;
        result.branchName = string.IsNullOrWhiteSpace(branchId) ? "ALT_?" : branchId;
        result.evaluatedTurn = evaluatedTurn;
        result.dynamismScore = SafeFailPerAxisScore;
        result.humanConflictScore = SafeFailPerAxisScore;
        result.techGrowthScore = SafeFailPerAxisScore;
        result.continuityScore = SafeFailPerAxisScore;
        result.totalScore = SafeFailTotalScore;
        result.usedSafeFailFallback = true;
        result.success = true;
        result.message = $"{reason} → fallback Total={SafeFailTotalScore} T{evaluatedTurn}";
        return result;
    }

    private static bool ShouldUseSparseSafeFail(int targetTurn, List<HistoryBranchNode> nodesInRange)
    {
        if (targetTurn <= 1)
        {
            return nodesInRange == null || nodesInRange.Count == 0;
        }

        return false;
    }

    private sealed class NationDramaProfile
    {
        public int nationId;
        public readonly List<HistoryBranchNode> nodes = new List<HistoryBranchNode>();
        public readonly HashSet<string> dramaCategories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public bool nationCollapsed;
        public int humanDramaFlagHits;
    }

    private struct NationSpikeScoreBundle
    {
        public int dynamismScore;
        public int humanConflictScore;
        public int topNationId;
        public float maxSpikeScore;
        public int concentrationBonus;
    }

  private static NationSpikeScoreBundle ComputeSpikeAwareScores(
        HistoryTimelineBranch branch,
        List<HistoryBranchNode> nodesInRange,
        float factionTension,
        MacroParamDelta terminalDelta)
    {
        NationSpikeScoreBundle bundle = new NationSpikeScoreBundle();
        if (nodesInRange == null || nodesInRange.Count == 0)
        {
            bundle.dynamismScore = ScoreDynamism(branch, nodesInRange, terminalDelta);
            bundle.humanConflictScore = ScoreHumanConflict(branch, nodesInRange, factionTension);
            return bundle;
        }

        Dictionary<int, NationDramaProfile> profiles = BuildNationProfiles(nodesInRange);
        List<float> healthyDynamism = new List<float>();
        List<float> healthyHuman = new List<float>();
        int topNationId = 0;
        float maxSpike = 0f;

        foreach (KeyValuePair<int, NationDramaProfile> pair in profiles)
        {
            NationDramaProfile profile = pair.Value;
            if (profile.nationCollapsed)
            {
                continue;
            }

            float nationDyn = ScoreNationDynamismRaw(profile.nodes);
            float nationHuman = ScoreNationHumanConflictRaw(profile.nodes, factionTension);
            healthyDynamism.Add(nationDyn);
            healthyHuman.Add(nationHuman);

            float spike = nationDyn + nationHuman;
            if (spike > maxSpike)
            {
                maxSpike = spike;
                topNationId = profile.nationId;
            }
        }

        if (healthyDynamism.Count == 0)
        {
            bundle.dynamismScore = ScoreDynamism(branch, nodesInRange, terminalDelta);
            bundle.humanConflictScore = ScoreHumanConflict(branch, nodesInRange, factionTension);
            return bundle;
        }

        float avgDyn = Average(healthyDynamism);
        float avgHuman = Average(healthyHuman);
        float peakDyn = Max(healthyDynamism);
        float peakHuman = Max(healthyHuman);
        float blendedDyn = avgDyn * PeakBlendAverageWeight + peakDyn * PeakBlendPeakWeight;
        float blendedHuman = avgHuman * PeakBlendAverageWeight + peakHuman * PeakBlendPeakWeight;

        int concentrationBonus = ComputeConcentrationBonus(profiles);
        if (concentrationBonus > 0 && topNationId <= 0)
        {
            topNationId = FindTopConcentrationNation(profiles);
            maxSpike = Mathf.Max(maxSpike, peakDyn + peakHuman + concentrationBonus);
        }

        bundle.dynamismScore = ClampScore(blendedDyn);
        bundle.humanConflictScore = ClampScore(blendedHuman + concentrationBonus);
        bundle.topNationId = topNationId;
        bundle.maxSpikeScore = maxSpike + concentrationBonus;
        bundle.concentrationBonus = concentrationBonus;
        return bundle;
    }

    private static Dictionary<int, NationDramaProfile> BuildNationProfiles(List<HistoryBranchNode> nodesInRange)
    {
        Dictionary<int, NationDramaProfile> profiles = new Dictionary<int, NationDramaProfile>();
        for (int i = 0; i < nodesInRange.Count; i++)
        {
            HistoryBranchNode node = nodesInRange[i];
            if (node == null || node.nationId <= 0)
            {
                continue;
            }

            if (!profiles.TryGetValue(node.nationId, out NationDramaProfile profile))
            {
                profile = new NationDramaProfile { nationId = node.nationId };
                profiles.Add(node.nationId, profile);
            }

            profile.nodes.Add(node);
            if (IsNationCollapsed(node))
            {
                profile.nationCollapsed = true;
            }

            CountHumanDramaSignals(node, profile);
        }

        return profiles;
    }

    private static void CountHumanDramaSignals(HistoryBranchNode node, NationDramaProfile profile)
    {
        string blob = BuildEventBlob(node);
        if (ContainsAny(blob, "SUCCESSION"))
        {
            profile.dramaCategories.Add("SUCCESSION");
            profile.humanDramaFlagHits++;
        }

        if (ContainsAny(blob, "CIVIL", "CIVILWAR", "REBEL", "FACTION"))
        {
            profile.dramaCategories.Add("CIVIL");
            profile.humanDramaFlagHits++;
        }

        if (ContainsAny(blob, "HERO", "PROXY", "DEFENSE"))
        {
            profile.dramaCategories.Add("HERO");
            profile.humanDramaFlagHits++;
        }

        if (ContainsAny(blob, "BORDER", "ACADEMY", "RIVALRY", "NOBLE", "KING", "QUEEN"))
        {
            profile.dramaCategories.Add("POLITICS");
            profile.humanDramaFlagHits++;
        }
    }

    private static bool IsNationCollapsed(HistoryBranchNode node)
    {
        if (node == null)
        {
            return false;
        }

        string blob = BuildEventBlob(node);
        if (ContainsAny(blob, "NATIONCOLLAPSE", "COLLAPSE", "EXTINCTION", "ANNIHILATION", "BARRIERDROP", "BARRIER_DROP"))
        {
            if (node.delta != null && node.delta.isSurvivalOverridden && !node.delta.survivalValue)
            {
                return true;
            }

            if (ContainsAny(blob, "NATIONCOLLAPSE", "COLLAPSE", "EXTINCTION", "ANNIHILATION"))
            {
                return true;
            }
        }

        if (node.delta != null && node.delta.isSurvivalOverridden && !node.delta.survivalValue)
        {
            return true;
        }

        if (node.delta != null &&
            node.delta.powerMultiplier < 0.85f &&
            node.delta.barrierEfficiencyDelta < -0.15f)
        {
            return true;
        }

        return false;
    }

    private static int ComputeConcentrationBonus(Dictionary<int, NationDramaProfile> profiles)
    {
        List<NationDramaProfile> concentrated = new List<NationDramaProfile>();
        foreach (KeyValuePair<int, NationDramaProfile> pair in profiles)
        {
            NationDramaProfile profile = pair.Value;
            if (profile.nationCollapsed)
            {
                continue;
            }

            if (profile.humanDramaFlagHits >= 2 && profile.dramaCategories.Count >= 2)
            {
                concentrated.Add(profile);
            }
        }

        if (concentrated.Count == 0 || concentrated.Count > 2)
        {
            return 0;
        }

        int bonus = 0;
        for (int i = 0; i < concentrated.Count; i++)
        {
            NationDramaProfile profile = concentrated[i];
            bonus += profile.dramaCategories.Count * 8 + profile.humanDramaFlagHits * 3;
        }

        return Mathf.Clamp(bonus, 0, MaxConcentrationBonus);
    }

    private static int FindTopConcentrationNation(Dictionary<int, NationDramaProfile> profiles)
    {
        int topId = 0;
        int topHits = -1;
        foreach (KeyValuePair<int, NationDramaProfile> pair in profiles)
        {
            NationDramaProfile profile = pair.Value;
            if (profile.nationCollapsed)
            {
                continue;
            }

            int weight = profile.humanDramaFlagHits * 10 + profile.dramaCategories.Count;
            if (weight > topHits)
            {
                topHits = weight;
                topId = profile.nationId;
            }
        }

        return topId;
    }

    private static float ScoreNationDynamismRaw(List<HistoryBranchNode> nationNodes)
    {
        if (nationNodes == null || nationNodes.Count == 0)
        {
            return 40f;
        }

        MacroParamDelta accumulated = MacroParamDelta.Identity;
        for (int i = 0; i < nationNodes.Count; i++)
        {
            HistoryBranchNode node = nationNodes[i];
            if (node?.delta == null)
            {
                continue;
            }

            MacroParamDelta nodeDelta = node.delta.Clone();
            nodeDelta.SanitizeInPlace();
            accumulated.Accumulate(nodeDelta);
            accumulated.SanitizeInPlace();
        }

        return ScoreDynamismRawFromDelta(accumulated, nationNodes.Count);
    }

    private static float ScoreNationHumanConflictRaw(List<HistoryBranchNode> nationNodes, float factionTension)
    {
        if (nationNodes == null || nationNodes.Count == 0)
        {
            return 28f;
        }

        float score = 28f;
        bool hasHumanDrama = false;
        bool hasMonsterBeat = false;
        for (int i = 0; i < nationNodes.Count; i++)
        {
            HistoryBranchNode node = nationNodes[i];
            if (node == null)
            {
                continue;
            }

            string blob = BuildEventBlob(node);
            if (ContainsAny(blob, "HERO", "DEFENSE", "PROXY"))
            {
                score += 14f;
                hasHumanDrama = true;
            }

            if (ContainsAny(blob, "CIVIL", "REBEL", "SUCCESSION", "BORDER", "ACADEMY", "RIVALRY"))
            {
                score += 12f;
                hasHumanDrama = true;
            }

            if (ContainsAny(blob, "BARRIERDROP", "BARRIER_DROP", "EXTINCTION", "ANNIHILATION", "NATIONCOLLAPSE"))
            {
                score -= 18f;
            }

            float monsterSynergy = ScoreMonsterHumanSynergy(blob, HasHumanDramaKeyword(blob));
            score += monsterSynergy;
            if (monsterSynergy > 0.01f)
            {
                hasMonsterBeat = true;
            }

            if (!string.IsNullOrWhiteSpace(node.outcomeFlag) &&
                HistoryFlagRegistry.IsUnlocked(node.outcomeFlag))
            {
                score += 4f;
            }
        }

        if (factionTension >= 40f && factionTension <= 78f)
        {
            score += 6f;
        }

        if (hasHumanDrama && hasMonsterBeat)
        {
            score += 10f;
        }

        return score;
    }

    private static float ScoreDynamismRawFromDelta(MacroParamDelta terminal, int nodeCount)
    {
        float score = 52f;
        if (terminal.isSurvivalOverridden && !terminal.survivalValue)
        {
            score -= 45f;
        }

        if (terminal.powerMultiplier < 0.88f)
        {
            score -= 28f;
        }
        else if (terminal.powerMultiplier >= 1.02f && terminal.powerMultiplier <= 1.28f)
        {
            score += 16f;
        }
        else if (terminal.powerMultiplier > 1.45f)
        {
            score -= 12f;
        }

        if (terminal.barrierEfficiencyDelta < -0.12f)
        {
            score -= 30f;
        }
        else if (terminal.barrierEfficiencyDelta >= -0.03f && terminal.barrierEfficiencyDelta <= 0.14f)
        {
            score += 14f;
        }

        if (terminal.threatMultiplier > 1.35f)
        {
            score -= 22f;
        }
        else if (terminal.threatMultiplier >= 0.82f && terminal.threatMultiplier <= 1.18f)
        {
            score += 14f;
        }

        score += Mathf.Min(nodeCount * 3.5f, 14f);
        return score;
    }

    private static float Average(List<float> values)
    {
        if (values == null || values.Count == 0)
        {
            return 0f;
        }

        float sum = 0f;
        for (int i = 0; i < values.Count; i++)
        {
            sum += values[i];
        }

        return sum / values.Count;
    }

    private static float Max(List<float> values)
    {
        if (values == null || values.Count == 0)
        {
            return 0f;
        }

        float max = values[0];
        for (int i = 1; i < values.Count; i++)
        {
            if (values[i] > max)
            {
                max = values[i];
            }
        }

        return max;
    }

    private static float TryResolveFactionTension()
    {
        try
        {
            HumanConflictEngine conflict = HumanConflictEngine.EnsureInstance();
            return conflict?.Society?.factionTension ?? 0f;
        }
        catch (Exception)
        {
            return 0f;
        }
    }

    private static int ScoreDynamism(
        HistoryTimelineBranch branch,
        List<HistoryBranchNode> nodesInRange,
        MacroParamDelta terminal)
    {
        return ClampScore(ScoreDynamismRawFromDelta(terminal, nodesInRange?.Count ?? 0));
    }

    private static int ScoreHumanConflict(
        HistoryTimelineBranch branch,
        List<HistoryBranchNode> nodesInRange,
        float factionTension)
    {
        float score = 28f;
        bool hasHumanDrama = false;
        bool hasMonsterBeat = false;

        if (nodesInRange == null)
        {
            return ClampScore(score);
        }

        for (int i = 0; i < nodesInRange.Count; i++)
        {
            HistoryBranchNode node = nodesInRange[i];
            if (node == null)
            {
                continue;
            }

            string blob = BuildEventBlob(node);
            if (ContainsAny(blob, "HERO", "DEFENSE", "PROXY"))
            {
                score += 14f;
                hasHumanDrama = true;
            }

            if (ContainsAny(blob, "CIVIL", "REBEL", "SUCCESSION", "BORDER", "ACADEMY", "RIVALRY"))
            {
                score += 12f;
                hasHumanDrama = true;
            }

            if (ContainsAny(blob, "BARRIERDROP", "BARRIER_DROP", "EXTINCTION", "ANNIHILATION"))
            {
                if (node.delta != null && node.delta.isSurvivalOverridden && !node.delta.survivalValue)
                {
                    score -= 38f;
                }
                else
                {
                    score -= 18f;
                }
            }

            float monsterSynergy = ScoreMonsterHumanSynergy(blob, hasHumanDramaInBlob: HasHumanDramaKeyword(blob));
            score += monsterSynergy;
            if (monsterSynergy > 0.01f)
            {
                hasMonsterBeat = true;
            }

            if (!string.IsNullOrWhiteSpace(node.outcomeFlag) &&
                HistoryFlagRegistry.IsUnlocked(node.outcomeFlag))
            {
                score += 4f;
            }
        }

        if (factionTension >= 40f && factionTension <= 78f)
        {
            score += 12f;
        }
        else if (factionTension > 78f)
        {
            score += 4f;
        }

        if (hasHumanDrama && hasMonsterBeat)
        {
            score += 10f;
        }

        return ClampScore(score);
    }

    private static int ScoreTechGrowth(
        HistoryTimelineBranch branch,
        List<HistoryBranchNode> nodesInRange,
        MacroParamDelta terminal)
    {
        float score = 34f;
        int magicHits = 0;
        int jobHits = 0;

        if (nodesInRange == null)
        {
            return ClampScore(score);
        }

        for (int i = 0; i < nodesInRange.Count; i++)
        {
            HistoryBranchNode node = nodesInRange[i];
            if (node == null)
            {
                continue;
            }

            string blob = BuildEventBlob(node);
            if (ContainsAny(blob, "INNOVATION", "CRAFT", "ACADEMY", "FORGE", "MAGIC"))
            {
                score += 13f;
                magicHits++;
            }

            if (ContainsAny(blob, "JOB_", "SMITH", "SCHOLAR", "STEWARD", "HISTORIAN", "ARCHITECT"))
            {
                score += 11f;
                jobHits++;
            }

            if (!string.IsNullOrWhiteSpace(node.outcomeFlag))
            {
                string flag = node.outcomeFlag.ToUpperInvariant();
                if (flag.IndexOf("MAGIC", StringComparison.Ordinal) >= 0)
                {
                    magicHits++;
                    score += 6f;
                }

                if (flag.IndexOf("JOB_", StringComparison.Ordinal) >= 0)
                {
                    jobHits++;
                    score += 8f;
                }
            }
        }

        if (terminal.powerMultiplier >= 1.04f && terminal.barrierEfficiencyDelta >= -0.02f)
        {
            score += 8f;
        }

        if (magicHits == 0 && jobHits == 0)
        {
            score += 4f;
        }

        return ClampScore(score);
    }

    private static int ScoreContinuity(
        HistoryTimelineBranch branch,
        List<HistoryBranchNode> nodesInRange,
        int targetTurn,
        MacroParamDelta terminal)
    {
        float score = 42f;

        if (terminal.isSurvivalOverridden && !terminal.survivalValue)
        {
            score -= 42f;
        }

        if (terminal.barrierEfficiencyDelta < -0.15f || terminal.powerMultiplier < 0.82f)
        {
            score -= 20f;
        }

        int minTurn = int.MaxValue;
        int maxTurn = int.MinValue;
        bool hasProtagonistHandoff = false;

        if (nodesInRange != null)
        {
            for (int i = 0; i < nodesInRange.Count; i++)
            {
                HistoryBranchNode node = nodesInRange[i];
                if (node == null)
                {
                    continue;
                }

                minTurn = Mathf.Min(minTurn, node.turn);
                maxTurn = Mathf.Max(maxTurn, node.turn);
                string blob = BuildEventBlob(node);
                if (ContainsAny(blob, "HERO", "SUCCESSION", "PROXY", "TRAINEE", "CHRONO"))
                {
                    hasProtagonistHandoff = true;
                }
            }
        }

        if (hasProtagonistHandoff)
        {
            score += 20f;
        }

        if (nodesInRange != null && nodesInRange.Count >= 2)
        {
            score += 10f;
        }

        if (minTurn != int.MaxValue && maxTurn > minTurn + 4)
        {
            score += 12f;
        }

        if (targetTurn > 0 && targetTurn < EraContextResolver.MaxTurn - 5)
        {
            score += 8f;
        }

        if (branch.isCommittedMainStory)
        {
            score += 6f;
        }

        return ClampScore(score);
    }

    private static float ScoreMonsterHumanSynergy(string blob, bool hasHumanDramaInBlob)
    {
        int tier = InferMonsterIntelligenceTier(blob);
        if (tier <= 0)
        {
            return 0f;
        }

        if (tier >= 4 && hasHumanDramaInBlob)
        {
            return 18f;
        }

        if (tier >= 3 && hasHumanDramaInBlob)
        {
            return 15f;
        }

        if (tier >= 2)
        {
            return hasHumanDramaInBlob ? 11f : 3f;
        }

        return hasHumanDramaInBlob ? 7f : 1f;
    }

    private static int InferMonsterIntelligenceTier(string blob)
    {
        if (string.IsNullOrWhiteSpace(blob))
        {
            return 0;
        }

        if (ContainsAny(blob, "SINGULAR", "SINGULARITY", "特異点", "WORLD_BOSS", "ARCHFIEND"))
        {
            return 4;
        }

        if (ContainsAny(blob, "ELITE", "SENTIENT", "ARCH", "OVERLORD", "MONSTER_DEFENSE"))
        {
            return 3;
        }

        if (ContainsAny(blob, "MONSTER_ATTACK", "MONSTER", "RAID", "BEAST", "HORDE"))
        {
            return 2;
        }

        if (ContainsAny(blob, "SWARM", "FODDER", "PEST"))
        {
            return 1;
        }

        return 0;
    }

    private static bool HasHumanDramaKeyword(string blob)
    {
        return ContainsAny(
            blob,
            "HERO",
            "CIVIL",
            "REBEL",
            "SUCCESSION",
            "BORDER",
            "ACADEMY",
            "RIVALRY",
            "DEFENSE",
            "PROXY",
            "KING",
            "QUEEN",
            "NOBLE");
    }

    private static string BuildEventBlob(HistoryBranchNode node)
    {
        return $"{node.keyEventId}|{node.outcomeFlag}".ToUpperInvariant();
    }

    private static bool ContainsAny(string blob, params string[] keywords)
    {
        if (string.IsNullOrWhiteSpace(blob) || keywords == null)
        {
            return false;
        }

        for (int i = 0; i < keywords.Length; i++)
        {
            if (blob.IndexOf(keywords[i], StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    private static int ClampScore(float raw)
    {
        return Mathf.Clamp(Mathf.RoundToInt(raw), ScoreMin, ScoreMax);
    }

    public static TimelineScriptEvaluatorVerifyResult RunVerification()
    {
        TimelineScriptEvaluatorVerifyResult verify = new TimelineScriptEvaluatorVerifyResult();
        StringBuilder log = new StringBuilder();
        try
        {
            SimulationVerifyBootstrap.PrepareFreshStoryDay(1, GameMode.StoryMode);
            HistoryFlagRegistry.EnsureWired();
            HistoryFlagRegistry.ClearForTesting();
            HistoryBranchManager mgr = HistoryBranchManager.EnsureInstance();
            mgr.ClearRegisteredBranches();

            int nationId = MicroToMacroAggregator.DefaultNationId;
            HistoryAlterationResult despair = HistoryBranchManager.RegisterHistoryAlteration(
                6,
                nationId,
                "HIST_NATION_001_GEO_TURN_001_BARRIERDROP",
                null,
                new MacroParamDelta
                {
                    powerMultiplier = 0.82f,
                    barrierEfficiencyDelta = -0.18f,
                    threatMultiplier = 1.55f,
                    isSurvivalOverridden = true,
                    survivalValue = false
                });
            string despairId = despair.branchId;

            HistoryAlterationResult drama = HistoryBranchManager.RegisterHistoryAlteration(
                8,
                nationId,
                "HIST_NATION_001_GEO_TURN_001_SUCCESSION",
                null,
                new MacroParamDelta
                {
                    powerMultiplier = 1.08f,
                    barrierEfficiencyDelta = 0.04f,
                    threatMultiplier = 1.05f
                },
                createNewBranch: true);
            HistoryTimelineBranch dramaBranch = mgr.FindRegisteredBranchForVerification(drama.branchId);
            if (dramaBranch != null)
            {
                dramaBranch.displayName = "IF_Line: 王位継承ドラマ";
                mgr.UpsertRegisteredBranchForVerification(dramaBranch);
            }

            HistoryBranchManager.RegisterHistoryAlteration(
                10,
                nationId,
                "HIST_NATION_001_GEO_TURN_001_CIVILWAR",
                null,
                new MacroParamDelta
                {
                    powerMultiplier = 0.96f,
                    barrierEfficiencyDelta = -0.02f,
                    threatMultiplier = 1.12f
                });
            HistoryBranchManager.RegisterHistoryAlteration(
                14,
                nationId,
                "HIST_NATION_001_GEO_TURN_001_MONSTER_DEFENSE",
                "ALT_NATION_001_OUTCOME_T014_MONSTER_DEFENSE",
                new MacroParamDelta
                {
                    powerMultiplier = 1.06f,
                    barrierEfficiencyDelta = 0.03f,
                    threatMultiplier = 0.92f
                });

            HistoryAlterationResult tech = HistoryBranchManager.RegisterHistoryAlteration(
                5,
                nationId,
                "HIST_NATION_001_GEO_TURN_001_INNOVATION",
                "ALT_NATION_001_OUTCOME_T005_INNOVATION",
                new MacroParamDelta
                {
                    powerMultiplier = 1.14f,
                    barrierEfficiencyDelta = 0.05f,
                    threatMultiplier = 0.94f
                },
                createNewBranch: true);
            HistoryTimelineBranch techBranch = mgr.FindRegisteredBranchForVerification(tech.branchId);
            if (techBranch != null)
            {
                techBranch.displayName = "IF_Line: 魔導革新";
                mgr.UpsertRegisteredBranchForVerification(techBranch);
            }

            HistoryBranchManager.RegisterHistoryAlteration(
                9,
                nationId,
                "HIST_NATION_001_GEO_TURN_001_ACADEMY_RIVALRY",
                null,
                new MacroParamDelta { powerMultiplier = 1.06f, barrierEfficiencyDelta = 0.02f });
            HistoryBranchManager.RegisterHistoryAlteration(
                11,
                2,
                "HIST_NATION_002_GEO_TURN_001_ACADEMY_RIVALRY",
                null,
                new MacroParamDelta { powerMultiplier = 1.02f, barrierEfficiencyDelta = 0.01f });
            HistoryFlagRegistry.Unlock("ALT_NATION_001_OUTCOME_T005_INNOVATION");
            HistoryFlagRegistry.Unlock("JOB_ACADEMY_SCHOLAR");
            HistoryFlagRegistry.Unlock("MAGIC_DYN_ACADEMY_SYNTHESIS");

            int turnT1 = TimelineScriptEvaluator.ResolveCurrentEvaluationTurn();
            List<TimelineScriptEvaluationResult> allT1 =
                TimelineScriptEvaluator.EvaluateAllBranchesAtCurrentTurn(turnT1);
            TimelineScriptEvaluationResult canonT1 = allT1.Find(r => r.isCanon);
            bool earlySparsePass = canonT1 != null &&
                                   canonT1.usedSafeFailFallback &&
                                   canonT1.evaluatedTurn == 1;
            log.AppendLine(
                $"dynamic-T1: turn={turnT1} canonTotal={canonT1?.totalScore ?? 0} " +
                $"fallback={canonT1?.usedSafeFailFallback == true} pass={earlySparsePass}");

            const int probeTurn = 15;
            EraContextResolver.TrySetCurrentTurn(probeTurn);
            DailySimulationEngine daily = DailySimulationEngine.EnsureInstance();
            daily.TryPrepareForTurn(probeTurn);
            MicroToMacroAggregator.EnsureInstance().PrepareTurnTransition(probeTurn, applyBranchAdjustments: false);

            TimelineScriptEvaluationResult despairEval =
                EvaluateTimelineBranch(mgr.FindRegisteredBranchForVerification(despairId), probeTurn);
            TimelineScriptEvaluationResult dramaEval =
                EvaluateTimelineBranch(mgr.FindRegisteredBranchForVerification(drama.branchId), probeTurn);
            TimelineScriptEvaluationResult techEval =
                EvaluateTimelineBranch(mgr.FindRegisteredBranchForVerification(tech.branchId), probeTurn);

            log.AppendLine(
                $"dynamic-T{probeTurn}: turn={despairEval.evaluatedTurn} despair={despairEval.totalScore} " +
                $"drama={dramaEval.totalScore} tech={techEval.totalScore}");
            log.AppendLine(
                $"despair: total={despairEval.totalScore} dyn={despairEval.dynamismScore} " +
                $"human={despairEval.humanConflictScore} tech={despairEval.techGrowthScore} " +
                $"cont={despairEval.continuityScore}");
            log.AppendLine(
                $"drama: total={dramaEval.totalScore} dyn={dramaEval.dynamismScore} " +
                $"human={dramaEval.humanConflictScore} tech={dramaEval.techGrowthScore} " +
                $"cont={dramaEval.continuityScore}");
            log.AppendLine(
                $"tech: total={techEval.totalScore} dyn={techEval.dynamismScore} " +
                $"human={techEval.humanConflictScore} tech={techEval.techGrowthScore} " +
                $"cont={techEval.continuityScore}");

            bool rankingPass = dramaEval.totalScore > despairEval.totalScore &&
                               techEval.totalScore > despairEval.totalScore &&
                               despairEval.evaluatedTurn == probeTurn;
            log.AppendLine($"ranking-pass: drama>despair & tech>despair & probeTurn={probeTurn} = {rankingPass}");

            bool peakBonusPass = dramaEval.peakNationId == nationId &&
                                 dramaEval.concentrationBonus > 0 &&
                                 despairEval.concentrationBonus == 0 &&
                                 dramaEval.totalScore > techEval.totalScore;
            log.AppendLine(
                $"peak-bonus: dramaNation={dramaEval.peakNationId:D3} bonus=+{dramaEval.concentrationBonus} " +
                $"spike={dramaEval.peakSpikeScore:F1} despairBonus={despairEval.concentrationBonus} pass={peakBonusPass}");

            MainStoryCommitResult commit = CommitBestBranchAtCurrentTurn(probeTurn);
            log.AppendLine(
                $"commit: success={commit.success} branch={commit.branchId} score={commit.totalScore} " +
                $"main={mgr.MainStoryBranchId} active={mgr.ActiveBranchId} turn={probeTurn}");
            bool commitPass = commit.success &&
                              !string.IsNullOrWhiteSpace(mgr.MainStoryBranchId) &&
                              string.Equals(mgr.ActiveBranchId, mgr.MainStoryBranchId, StringComparison.OrdinalIgnoreCase) &&
                              !mgr.IsCanonMode &&
                              (dramaEval.totalScore >= techEval.totalScore
                                  ? string.Equals(commit.branchId, drama.branchId, StringComparison.OrdinalIgnoreCase)
                                  : string.Equals(commit.branchId, tech.branchId, StringComparison.OrdinalIgnoreCase));

            TimelineScriptEvaluationResult missing = EvaluateTimelineBranch(null, probeTurn);
            bool safeFailPass = missing.usedSafeFailFallback && missing.totalScore == SafeFailTotalScore;
            log.AppendLine($"safe-fail: total={missing.totalScore} fallback={missing.usedSafeFailFallback} pass={safeFailPass}");

            verify.success = earlySparsePass && rankingPass && peakBonusPass && commitPass && safeFailPass;
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

    private static void WriteVerifyLog(TimelineScriptEvaluatorVerifyResult verify)
    {
        try
        {
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
            string path = Path.Combine(projectRoot, "Logs", "timeline_script_evaluator_verify.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? projectRoot);
            File.WriteAllText(
                path,
                $"{DateTime.UtcNow:O} success={verify.success}\n{verify.message}\n",
                Encoding.UTF8);
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[TimelineScriptEvaluator] 検証ログスキップ: {exception.Message}");
        }
    }
}

public static class TimelineScriptEvaluatorBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoAttach()
    {
        TimelineScriptEvaluator.EnsureInstance();
    }
}

#if UNITY_EDITOR
public static class TimelineScriptEvaluatorMenu
{
    [MenuItem("Tools/Procedural Map/Verify Timeline Script Evaluator")]
    public static void VerifyFromMenu()
    {
        TimelineScriptEvaluatorVerifyResult result = TimelineScriptEvaluator.RunVerification();
        Debug.Log(
            result.success
                ? $"<color=#A5D6A7><b>【歴史台本評価検証】PASS</b></color>\n{result.message}"
                : $"<color=#FF8A80><b>【歴史台本評価検証】FAIL</b></color>\n{result.message}");
        EditorUtility.DisplayDialog("Timeline Script Evaluator", result.message, "OK");
    }

    [MenuItem("Tools/Procedural Map/Evaluate All Timeline Branches")]
    public static void EvaluateAllFromMenu()
    {
        int turn = TimelineScriptEvaluator.ResolveCurrentEvaluationTurn();
        List<TimelineScriptEvaluationResult> ranked =
            TimelineScriptEvaluator.EvaluateAllBranchesAtCurrentTurn(turn);
        StringBuilder sb = new StringBuilder();
        sb.AppendLine($"=== T{turn} 時点スコア ===");
        for (int i = 0; i < ranked.Count; i++)
        {
            TimelineScriptEvaluationResult row = ranked[i];
            string tag = row.isCanon ? "[正史]" : "[ALT]";
            sb.AppendLine($"{i + 1}. {tag} {row.branchName} Total={row.totalScore} ({row.message})");
        }

        string body = sb.Length > 0 ? sb.ToString() : "評価対象の歴史軸がありません。";
        Debug.Log($"<color=#CE93D8><b>{TimelineScriptEvaluator.LogTag}</b></color>\n{body}");
        EditorUtility.DisplayDialog("Timeline Evaluation", body, "OK");
    }

    [MenuItem("Tools/Procedural Map/Commit Best Timeline Branch As Main Story")]
    public static void CommitBestFromMenu()
    {
        MainStoryCommitResult commit = TimelineScriptEvaluator.CommitBestBranchAtCurrentTurn();
        EditorUtility.DisplayDialog(
            "Commit Main Story",
            commit.success ? commit.message : commit.message,
            "OK");
    }
}
#endif

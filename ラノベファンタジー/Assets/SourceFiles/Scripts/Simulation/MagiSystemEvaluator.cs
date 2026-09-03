using System;
using System.Collections.Generic;

// =============================================================================
// MAGI 三位一体 — 評価ユニット共通基盤（エヴァンゲリオン / 北欧三女神）
// 連携: TimelineScriptEvaluator / HistoryBranchManager / MagiSystemDecisionEngine
// =============================================================================

/// <summary>MAGI 合議の最終判定。</summary>
public enum MagiDeliberationStatus
{
    AllAgreed = 0,
    Disagreed = 1,
    SafeFailFallback = 2
}

/// <summary>1 ユニットの推薦票。</summary>
public sealed class MagiUnitVote
{
    public string unitId = string.Empty;
    public string unitDisplayName = string.Empty;
    public string recommendedBranchId = string.Empty;
    public string recommendedBranchName = string.Empty;
    public int score;
    public bool vetoTriggered;
    public string reason = string.Empty;
}

/// <summary>枝ごとの MAGI スコア比較行。</summary>
public sealed class MagiBranchComparisonRow
{
    public string branchId = string.Empty;
    public string branchName = string.Empty;
    public int verdandiScore;
    public int urdScore;
    public int skuldScore;
    public bool urdVeto;
    public int scriptTotalScore;
}

/// <summary>MAGI 合議結果。</summary>
public sealed class MagiDeliberationResult
{
    public bool success;
    public MagiDeliberationStatus status = MagiDeliberationStatus.Disagreed;
    public string agreedBranchId = string.Empty;
    public MainStoryCommitResult commitResult;
    public List<MagiUnitVote> votes = new List<MagiUnitVote>();
    public List<MagiBranchComparisonRow> comparison = new List<MagiBranchComparisonRow>();
    public bool awaitingManualSelection;
    public bool usedSafeFailFallback;
    public string message = string.Empty;
}

/// <summary>MAGI 検証結果。</summary>
public sealed class MagiSystemDecisionVerifyResult
{
    public bool success;
    public bool allAgreedPass;
    public bool disagreedPass;
    public bool safeFailPass;
    public bool stalenessDisagreedPass;
    public bool skuldPruningPass;
    public string message = string.Empty;
}

/// <summary>3 女神評価ユニットの抽象基底。</summary>
public abstract class MagiSystemEvaluator
{
    public abstract string UnitId { get; }
    public abstract string UnitDisplayName { get; }

    /// <summary>1 分岐枝へのスコアリング（拒否権は Urd が使用）。</summary>
    public abstract MagiUnitVote ScoreBranch(
        HistoryTimelineBranch branch,
        TimelineScriptEvaluationResult scriptResult,
        int evaluationTurn,
        HistoryTimelineBranch mainStoryAnchor);

    /// <summary>候補群から当該ユニットの推薦枝を選びます。</summary>
    public virtual MagiUnitVote SelectRecommendation(
        IReadOnlyList<HistoryTimelineBranch> branches,
        IReadOnlyDictionary<string, TimelineScriptEvaluationResult> scriptById,
        int evaluationTurn,
        HistoryTimelineBranch mainStoryAnchor)
    {
        MagiUnitVote best = null;
        if (branches == null || branches.Count == 0)
        {
            return BuildEmptyVote("候補枝なし");
        }

        for (int i = 0; i < branches.Count; i++)
        {
            HistoryTimelineBranch branch = branches[i];
            if (branch == null || string.IsNullOrWhiteSpace(branch.branchId))
            {
                continue;
            }

            if (!scriptById.TryGetValue(branch.branchId, out TimelineScriptEvaluationResult script) ||
                script == null)
            {
                script = TimelineScriptEvaluator.EvaluateTimelineBranch(branch, evaluationTurn);
            }

            MagiUnitVote vote = ScoreBranch(branch, script, evaluationTurn, mainStoryAnchor);
            if (vote.vetoTriggered)
            {
                continue;
            }

            if (best == null || vote.score > best.score)
            {
                best = vote;
            }
        }

        return best ?? BuildEmptyVote("有効候補なし（全枝拒否）");
    }

    protected MagiUnitVote BuildVote(
        HistoryTimelineBranch branch,
        int score,
        string reason,
        bool veto = false)
    {
        return new MagiUnitVote
        {
            unitId = UnitId,
            unitDisplayName = UnitDisplayName,
            recommendedBranchId = branch?.branchId ?? string.Empty,
            recommendedBranchName = branch != null
                ? TimelineScriptEvaluator.ResolveBranchDisplayName(branch)
                : string.Empty,
            score = score,
            vetoTriggered = veto,
            reason = reason ?? string.Empty
        };
    }

    protected MagiUnitVote BuildEmptyVote(string reason)
    {
        return new MagiUnitVote
        {
            unitId = UnitId,
            unitDisplayName = UnitDisplayName,
            score = int.MinValue,
            reason = reason ?? string.Empty
        };
    }

    /// <summary>分岐 ID / ノードから傾向タグを抽出します。</summary>
    protected static string ExtractTendencyTag(HistoryTimelineBranch branch)
    {
        if (branch == null)
        {
            return string.Empty;
        }

        string blob = (branch.branchId ?? string.Empty) + "|" + (branch.displayName ?? string.Empty);
        if (branch.nodes != null)
        {
            for (int i = 0; i < branch.nodes.Count; i++)
            {
                HistoryBranchNode node = branch.nodes[i];
                blob += "|" + (node?.keyEventId ?? string.Empty);
                blob += "|" + (node?.outcomeFlag ?? string.Empty);
            }
        }

        blob = blob.ToUpperInvariant();
        string[] tags =
        {
            "BEAST_CATASTROPHE", "HERETIC_ORTHODOXY", "HERETIC_SCHISM", "HERETIC",
            "DEMON_FRONT", "DEMON", "CIVILWAR", "CIVIL", "ARCANE_REVIVAL",
            "LOST_TECH", "INNOVATION", "MONSTER", "SUCCESSION", "ACADEMY", "BEAST", "CATASTROPHE"
        };

        for (int i = 0; i < tags.Length; i++)
        {
            if (blob.Contains(tags[i], StringComparison.Ordinal))
            {
                return tags[i];
            }
        }

        return string.Empty;
    }

    protected static bool ContainsKeyword(string blob, params string[] keywords)
    {
        if (string.IsNullOrEmpty(blob) || keywords == null)
        {
            return false;
        }

        for (int i = 0; i < keywords.Length; i++)
        {
            if (!string.IsNullOrEmpty(keywords[i]) &&
                blob.Contains(keywords[i], StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    protected static string BuildBranchBlob(HistoryTimelineBranch branch)
    {
        if (branch == null)
        {
            return string.Empty;
        }

        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        sb.Append(branch.branchId).Append('|').Append(branch.displayName);
        if (branch.nodes != null)
        {
            for (int i = 0; i < branch.nodes.Count; i++)
            {
                HistoryBranchNode node = branch.nodes[i];
                sb.Append('|').Append(node?.keyEventId).Append('|').Append(node?.outcomeFlag);
            }
        }

        return sb.ToString();
    }
}

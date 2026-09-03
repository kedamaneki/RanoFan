using System;
using System.Collections.Generic;
using UnityEngine;

// =============================================================================
// MAGI-1 ヴェルダンディ — 人間ドラマ・波乱度（-400〜+400 可変スケール）
// =============================================================================

/// <summary>
/// 波乱度・人間ドラマを +0〜+400 で加点し、
/// 直近同系統連続確定には -200 / -400 のマンネリ減点を適用します。
/// </summary>
public sealed class VerdandiEvaluator : MagiSystemEvaluator
{
    public const string Id = "MAGI-1";
    public const string DisplayName = "ヴェルダンディ";
    public const string DecisionUpdateLogTag = "【ヴェルダンディ判定更新】";

    public const int MinScore = -400;
    public const int MaxScore = 400;
    public const int StalenessPenaltyTwoGeneration = 200;
    public const int StalenessPenaltyThreeGeneration = 400;
    public const int ForceAlternateStreakThreshold = 3;

    public override string UnitId => Id;
    public override string UnitDisplayName => DisplayName;

    public override MagiUnitVote SelectRecommendation(
        IReadOnlyList<HistoryTimelineBranch> branches,
        IReadOnlyDictionary<string, TimelineScriptEvaluationResult> scriptById,
        int evaluationTurn,
        HistoryTimelineBranch mainStoryAnchor)
    {
        if (branches == null || branches.Count == 0)
        {
            return BuildEmptyVote("候補枝なし");
        }

        HistoryBranchManager mgr = HistoryBranchManager.EnsureInstance();
        int streak = CountConsecutiveSameTendencyStreak(mainStoryAnchor, mgr);
        string streakTag = mainStoryAnchor != null ? ExtractTendencyTag(mainStoryAnchor) : string.Empty;
        bool staleContext = streak >= 2 && !string.IsNullOrWhiteSpace(streakTag);

        if (streak >= ForceAlternateStreakThreshold && !string.IsNullOrWhiteSpace(streakTag))
        {
            HistoryTimelineBranch alternate = FindBestTurmoilAlternateByBonus(
                branches,
                scriptById,
                evaluationTurn,
                streakTag);
            if (alternate != null)
            {
                if (!scriptById.TryGetValue(alternate.branchId, out TimelineScriptEvaluationResult script) ||
                    script == null)
                {
                    script = TimelineScriptEvaluator.EvaluateTimelineBranch(alternate, evaluationTurn);
                }

                int bonus = ComputeTurmoilBonus(alternate, script);
                int score = ClampScore(bonus);
                MagiUnitVote forced = BuildVote(
                    alternate,
                    score,
                    $"脱マンネリ強制推薦 波乱加点=+{bonus} 連続{streak}世代 tag={streakTag}");
                LogDecisionUpdate(score, isStale: true, alternate.branchId);
                return forced;
            }
        }

        MagiUnitVote best = null;
        int bestScore = int.MinValue;
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

            if (best == null || vote.score > bestScore)
            {
                best = vote;
                bestScore = vote.score;
            }
        }

        if (best != null)
        {
            LogDecisionUpdate(best.score, staleContext, best.recommendedBranchId);
        }

        return best ?? BuildEmptyVote("有効候補なし（全枝拒否）");
    }

    public override MagiUnitVote ScoreBranch(
        HistoryTimelineBranch branch,
        TimelineScriptEvaluationResult scriptResult,
        int evaluationTurn,
        HistoryTimelineBranch mainStoryAnchor)
    {
        if (branch == null || scriptResult == null)
        {
            return BuildEmptyVote("評価対象なし");
        }

        int bonus = ComputeTurmoilBonus(branch, scriptResult);
        HistoryBranchManager mgr = HistoryBranchManager.EnsureInstance();
        int streak = CountConsecutiveSameTendencyStreak(mainStoryAnchor, mgr);
        int staleness = ComputeStalenessPenalty(branch, mainStoryAnchor, streak);
        int score = ClampScore(bonus - staleness);

        string stalenessNote = staleness > 0
            ? $" マンネリ減点=-{staleness}(連続{streak}世代)"
            : string.Empty;

        return BuildVote(
            branch,
            score,
            $"波乱加点=+{bonus}{stalenessNote} ➔ 合計={score}");
    }

    /// <summary>波乱度ボーナス（+0〜+400）を算出します。</summary>
    public static int ComputeTurmoilBonus(
        HistoryTimelineBranch branch,
        TimelineScriptEvaluationResult scriptResult)
    {
        if (branch == null || scriptResult == null)
        {
            return 0;
        }

        int bonus = 0;
        bonus += Mathf.Clamp(scriptResult.humanConflictScore, 0, 120);
        bonus += Mathf.Clamp(scriptResult.dynamismScore * 70 / 100, 0, 90);

        string blob = BuildBranchBlob(branch);

        if (ContainsKeyword(blob, "CIVIL", "CIVILWAR", "REBEL", "FACTION", "SUCCESSION"))
        {
            bonus += 95;
        }

        if (ContainsKeyword(blob, "HERETIC", "HERESY", "ORTHODOXY", "SCHISM"))
        {
            bonus += 105;
        }

        if (ContainsKeyword(blob, "LOST_TECH", "REVIVAL", "REEXCAVATION", "ANCIENT"))
        {
            bonus += 90;
        }

        if (ContainsKeyword(blob, "ACADEMY", "RIVALRY", "REFORM"))
        {
            bonus += 80;
        }

        if (ContainsKeyword(blob, "FORGE", "SMITH", "INNOVATION"))
        {
            bonus += 75;
        }

        if (ContainsKeyword(blob, "BARRIERDROP", "BARRIER", "CONFLICT", "CRISIS"))
        {
            bonus += 60;
        }

        if (ContainsKeyword(blob, "DEMON", "FRONT", "MONSTER", "BEAST", "WAR", "CATASTROPHE"))
        {
            bonus += 50;
        }

        bonus += Mathf.Clamp(scriptResult.concentrationBonus, 0, 45);
        return Mathf.Clamp(bonus, 0, MaxScore);
    }

    public static int ClampScore(int score)
    {
        return Mathf.Clamp(score, MinScore, MaxScore);
    }

    public static void LogDecisionUpdate(int score, bool isStale, string branchId)
    {
        Debug.Log(
            $"<color=#FFB74D><b>{DecisionUpdateLogTag}</b></color> " +
            $"加点/減点: {score}点 (マンネリ判定: {isStale}) ➔ 推薦枝: {branchId ?? string.Empty}");
    }

    public static int CountConsecutiveSameTendencyStreak(
        HistoryTimelineBranch anchor,
        HistoryBranchManager mgr)
    {
        if (anchor == null || mgr == null)
        {
            return 0;
        }

        string streakTag = ExtractTendencyTag(anchor);
        if (string.IsNullOrWhiteSpace(streakTag))
        {
            return 1;
        }

        int count = 1;
        string cursor = anchor.parentBranchId;
        int guard = 0;
        while (!string.IsNullOrWhiteSpace(cursor) && guard++ < 32)
        {
            HistoryTimelineBranch parent = mgr.FindRegisteredBranchForVerification(cursor);
            if (parent == null)
            {
                break;
            }

            string parentTag = ExtractTendencyTag(parent);
            if (!TendencyTagsMatch(streakTag, parentTag))
            {
                break;
            }

            count++;
            cursor = parent.parentBranchId;
        }

        return count;
    }

    private static int ComputeStalenessPenalty(
        HistoryTimelineBranch candidate,
        HistoryTimelineBranch mainStoryAnchor,
        int consecutiveStreak)
    {
        if (mainStoryAnchor == null || candidate == null || consecutiveStreak < 2)
        {
            return 0;
        }

        string mainTag = ExtractTendencyTag(mainStoryAnchor);
        string candTag = ExtractTendencyTag(candidate);
        if (string.IsNullOrEmpty(mainTag) || string.IsNullOrEmpty(candTag))
        {
            return 0;
        }

        if (!TendencyTagsMatch(mainTag, candTag))
        {
            return 0;
        }

        if (consecutiveStreak >= ForceAlternateStreakThreshold)
        {
            return StalenessPenaltyThreeGeneration;
        }

        return StalenessPenaltyTwoGeneration;
    }

    private static HistoryTimelineBranch FindBestTurmoilAlternateByBonus(
        IReadOnlyList<HistoryTimelineBranch> branches,
        IReadOnlyDictionary<string, TimelineScriptEvaluationResult> scriptById,
        int evaluationTurn,
        string streakTag)
    {
        HistoryTimelineBranch best = null;
        int bestBonus = int.MinValue;

        for (int i = 0; i < branches.Count; i++)
        {
            HistoryTimelineBranch branch = branches[i];
            if (branch == null || string.IsNullOrWhiteSpace(branch.branchId))
            {
                continue;
            }

            string candTag = ExtractTendencyTag(branch);
            if (TendencyTagsMatch(streakTag, candTag))
            {
                continue;
            }

            if (!IsTurmoilAlternateBranch(branch))
            {
                continue;
            }

            if (!scriptById.TryGetValue(branch.branchId, out TimelineScriptEvaluationResult script) ||
                script == null)
            {
                script = TimelineScriptEvaluator.EvaluateTimelineBranch(branch, evaluationTurn);
            }

            int bonus = ComputeTurmoilBonus(branch, script);
            if (best == null || bonus > bestBonus)
            {
                best = branch;
                bestBonus = bonus;
            }
        }

        return best;
    }

    private static bool IsTurmoilAlternateBranch(HistoryTimelineBranch branch)
    {
        string blob = BuildBranchBlob(branch);
        return ContainsKeyword(
            blob,
            "CIVIL",
            "CIVILWAR",
            "REBEL",
            "FACTION",
            "SUCCESSION",
            "ACADEMY",
            "RIVALRY",
            "SCHISM",
            "REFORM",
            "FORGE",
            "SMITH",
            "BARRIER",
            "CONFLICT",
            "INNOVATION",
            "LOST_TECH",
            "REVIVAL",
            "REEXCAVATION",
            "HERETIC",
            "DECAY",
            "RESURGENCE");
    }

    private static bool TendencyTagsMatch(string left, string right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        if (string.Equals(left, right, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return string.Equals(GetTendencyFamily(left), GetTendencyFamily(right), StringComparison.OrdinalIgnoreCase);
    }

    private static string GetTendencyFamily(string tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return string.Empty;
        }

        string upper = tag.ToUpperInvariant();
        if (upper.Contains("BEAST") ||
            upper.Contains("CATASTROPHE") ||
            string.Equals(upper, "MONSTER"))
        {
            return "DISASTER";
        }

        if (upper.StartsWith("HERETIC"))
        {
            return "HERETIC";
        }

        if (upper.StartsWith("CIVIL"))
        {
            return "CIVIL";
        }

        if (upper.Contains("DEMON"))
        {
            return "DEMON";
        }

        return upper;
    }
}

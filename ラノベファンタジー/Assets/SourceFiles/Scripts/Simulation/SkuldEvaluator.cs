using System;
using System.Collections.Generic;
using UnityEngine;

// =============================================================================
// MAGI-3 スクルド — 過去正史互換（T≤1000）／剪定理論モデル（T≥1001）
// =============================================================================

/// <summary>剪定理論モデル（PossibilityTreeScore）の内訳。</summary>
public sealed class SkuldPossibilityAnalysis
{
    public int branchingFactorScore;
    public int potentialUnlocksScore;
    public int civilizationPlasticityScore;
    public int totalScore;
    public int branchCount;
    public int unlockCount;
}

/// <summary>
/// T≤1000: TimelineScriptEvaluator の totalScore（過去正史互換）。
/// T≥1001: 未来の可能性の太さ（BranchingFactor / PotentialUnlocks / CivilizationPlasticity）0〜+400。
/// 上限は維持しつつ、加点・閾値を厳しくして天井到達を稀にする。
/// </summary>
public sealed class SkuldEvaluator : MagiSystemEvaluator
{
    public const string Id = "MAGI-3";
    public const string DisplayName = "スクルド";
    public const string PruningLogTag = "【スクルド剪定評価発動】";
    public const int PruningModelStartTurn = EraContextResolver.ChronicleMaxTurn + 1;
    public const int MaxPossibilityScore = 400;
    public const int MaxBranchingFactorScore = 200;
    public const int MaxPotentialUnlocksScore = 100;
    public const int MaxCivilizationPlasticityScore = 100;
    public const int MaxRevivalEraBonus = 60;
    public const int ConvergenceLockedScoreCap = 80;

    private static readonly string[] PostCanonBlueprintSuffixes =
    {
        "BEAST_CATASTROPHE",
        "LOST_TECH_REVIVAL",
        "CIVILIZATION_DECAY",
        "HERETIC_SCHISM",
        "FRONTIER_EXPANSION",
        "DEMON_RESURGENCE",
        "ACADEMY_REFORM",
        "BARRIER_CRISIS"
    };

    private static readonly (string suffix, string unlockFlag)[] BlueprintUnlockCatalog =
    {
        ("LOST_TECH_REVIVAL", "MAGIC_HERETIC_ARCANA_CIVILIZED"),
        ("HERETIC_SCHISM", "MAGIC_HERETIC_ORTHODOXY_CIVILIZED"),
        ("ACADEMY_REFORM", "JOB_ACADEMY_SCHOLAR"),
        ("BARRIER_CRISIS", "JOB_BARRIER_REPAIRER")
    };

    public override string UnitId => Id;
    public override string UnitDisplayName => DisplayName;

    public override MagiUnitVote SelectRecommendation(
        IReadOnlyList<HistoryTimelineBranch> branches,
        IReadOnlyDictionary<string, TimelineScriptEvaluationResult> scriptById,
        int evaluationTurn,
        HistoryTimelineBranch mainStoryAnchor)
    {
        MagiUnitVote best = base.SelectRecommendation(
            branches,
            scriptById,
            evaluationTurn,
            mainStoryAnchor);

        if (evaluationTurn < PruningModelStartTurn ||
            best == null ||
            best.score <= int.MinValue + 1 ||
            string.IsNullOrWhiteSpace(best.recommendedBranchId) ||
            branches == null)
        {
            return best;
        }

        EnvironmentBiorhythmEngine.TryLogCivilizationRevivalTransition(evaluationTurn);

        HistoryTimelineBranch picked = null;
        for (int i = 0; i < branches.Count; i++)
        {
            HistoryTimelineBranch branch = branches[i];
            if (branch != null &&
                string.Equals(branch.branchId, best.recommendedBranchId, StringComparison.OrdinalIgnoreCase))
            {
                picked = branch;
                break;
            }
        }

        if (picked != null)
        {
            SkuldPossibilityAnalysis analysis = AnalyzePossibilityTree(picked, evaluationTurn);
            LogPruningEvaluation(evaluationTurn, analysis, best.recommendedBranchId);
        }

        return best;
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

        if (evaluationTurn >= PruningModelStartTurn)
        {
            SkuldPossibilityAnalysis analysis = AnalyzePossibilityTree(branch, evaluationTurn);
            int latent = HistoryBranchManager.GetLatentEnergyBonusForBranch(branch.branchId);
            int total = analysis.totalScore + Mathf.Max(0, latent);
            string latentLabel = latent > 0 ? $" Latent=+{latent}" : string.Empty;
            return BuildVote(
                branch,
                total,
                $"剪定 BF={analysis.branchingFactorScore} PU={analysis.potentialUnlocksScore} " +
                $"CP={analysis.civilizationPlasticityScore} " +
                $"枝={analysis.branchCount} 潜在={analysis.unlockCount}{latentLabel} ➔ 合計={total}");
        }

        int compatLatent = HistoryBranchManager.GetLatentEnergyBonusForBranch(branch.branchId);
        int compatScore = scriptResult.totalScore + Mathf.Max(0, compatLatent);
        return BuildVote(
            branch,
            compatScore,
            $"正史互換 total={scriptResult.totalScore} " +
            $"(Dyn={scriptResult.dynamismScore} Human={scriptResult.humanConflictScore} " +
            $"Tech={scriptResult.techGrowthScore} Cont={scriptResult.continuityScore})" +
            (compatLatent > 0 ? $" Latent=+{compatLatent}" : string.Empty));
    }

    /// <summary>剪定理論モデルで枝の未来可能性を分析します。</summary>
    public static SkuldPossibilityAnalysis AnalyzePossibilityTree(
        HistoryTimelineBranch branch,
        int evaluationTurn)
    {
        SkuldPossibilityAnalysis analysis = new SkuldPossibilityAnalysis();
        if (branch == null)
        {
            return analysis;
        }

        string blob = BuildBranchBlob(branch);
        int nationId = branch.primaryNationId > 0
            ? branch.primaryNationId
            : MicroToMacroAggregator.DefaultNationId;
        MacroParamDelta terminal = branch.AccumulateParamDeltaUpToTurn(evaluationTurn, nationId);
        bool convergenceLocked = IsDisasterConvergenceLocked(blob, terminal);

        HashSet<string> accessible = CountAccessibleBlueprints(blob, terminal, convergenceLocked, evaluationTurn);
        analysis.branchCount = accessible.Count;
        analysis.branchingFactorScore = ScoreBranchingFactor(accessible.Count);

        analysis.unlockCount = CountPotentialUnlocks(accessible, blob, convergenceLocked);
        analysis.potentialUnlocksScore = ScorePotentialUnlocks(analysis.unlockCount);

        analysis.civilizationPlasticityScore =
            ScoreCivilizationPlasticity(terminal, blob, convergenceLocked);

        int revivalBonus = ApplyRevivalEraBonus(blob, terminal, accessible, evaluationTurn);
        analysis.totalScore = Mathf.Clamp(
            analysis.branchingFactorScore +
            analysis.potentialUnlocksScore +
            analysis.civilizationPlasticityScore +
            revivalBonus,
            0,
            MaxPossibilityScore);

        if (evaluationTurn >= PruningModelStartTurn && convergenceLocked)
        {
            analysis.totalScore = Mathf.Min(analysis.totalScore, ConvergenceLockedScoreCap);
        }

        return analysis;
    }

    public static void LogPruningEvaluation(int turn, SkuldPossibilityAnalysis analysis, string branchId)
    {
        if (analysis == null)
        {
            return;
        }

        Debug.Log(
            $"<color=#CE93D8><b>{PruningLogTag}</b></color> " +
            $"ターン{turn}: 未来の可能性の太さ={analysis.totalScore}点 " +
            $"(可能IF枝数:{analysis.branchCount}, 潜在フラグ:{analysis.unlockCount}) " +
            $"➔ 推薦: {branchId ?? string.Empty}");
    }

    private static HashSet<string> CountAccessibleBlueprints(
        string blob,
        MacroParamDelta terminal,
        bool convergenceLocked,
        int evaluationTurn)
    {
        bool revivalEra = evaluationTurn >= PruningModelStartTurn;
        HashSet<string> accessible = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < PostCanonBlueprintSuffixes.Length; i++)
        {
            string suffix = PostCanonBlueprintSuffixes[i];
            if (IsBlueprintAccessible(suffix, blob, terminal, convergenceLocked, revivalEra))
            {
                accessible.Add(suffix);
            }
        }

        return accessible;
    }

    private static bool IsBlueprintAccessible(
        string suffix,
        string blob,
        MacroParamDelta terminal,
        bool convergenceLocked,
        bool revivalEra)
    {
        switch (suffix)
        {
            case "BEAST_CATASTROPHE":
                if (revivalEra)
                {
                    return false;
                }

                return ContainsKeyword(blob, "MONSTER", "BEAST", "CATASTROPHE") ||
                       terminal.threatMultiplier >= 1.05f;
            case "BARRIER_CRISIS":
                if (revivalEra)
                {
                    return !convergenceLocked &&
                           (ContainsKeyword(blob, "BARRIER", "BARRIERDROP") ||
                            terminal.barrierEfficiencyDelta < -0.05f ||
                            terminal.threatMultiplier >= 1.20f);
                }

                return ContainsKeyword(blob, "BARRIER", "BARRIERDROP") ||
                       terminal.barrierEfficiencyDelta < 0f ||
                       terminal.threatMultiplier >= 1.18f;
            case "DEMON_RESURGENCE":
                if (revivalEra)
                {
                    return ContainsKeyword(blob, "DEMON", "WAR") || terminal.powerMultiplier >= 1.15f;
                }

                return ContainsKeyword(blob, "DEMON", "MONSTER", "BEAST", "WAR") ||
                       terminal.threatMultiplier >= 1.12f ||
                       terminal.powerMultiplier >= 1.12f;
            case "LOST_TECH_REVIVAL":
                if (convergenceLocked)
                {
                    return false;
                }

                return ContainsKeyword(blob, "INNOVATION", "LOST_TECH", "HERO", "ARCANE", "REVIVAL", "TECH") ||
                       (terminal.powerMultiplier >= 1.0f && terminal.barrierEfficiencyDelta >= -0.06f) ||
                       (revivalEra && HasRevivalHumanSocietySignals(blob, terminal));
            case "HERETIC_SCHISM":
                if (convergenceLocked)
                {
                    return false;
                }

                return ContainsKeyword(blob, "HERETIC", "ACADEMY", "SCHISM", "ORTHODOXY", "INNOVATION", "POLITICS") ||
                       terminal.powerMultiplier >= 0.98f ||
                       (revivalEra && HasRevivalHumanSocietySignals(blob, terminal));
            case "ACADEMY_REFORM":
                if (convergenceLocked)
                {
                    return false;
                }

                return (ContainsKeyword(blob, "ACADEMY", "INNOVATION", "RIVALRY", "REFORM", "HERO") ||
                        terminal.powerMultiplier >= 1.02f ||
                        (revivalEra && HasRevivalHumanSocietySignals(blob, terminal))) &&
                       terminal.barrierEfficiencyDelta >= -0.14f;
            case "FRONTIER_EXPANSION":
                if (convergenceLocked)
                {
                    return false;
                }

                return ContainsKeyword(blob, "SUCCESSION", "HERO", "INNOVATION", "FRONTIER", "EXPANSION", "TRADE", "COMMERCE") ||
                       terminal.powerMultiplier >= 1.04f ||
                       (revivalEra && HasRevivalHumanSocietySignals(blob, terminal));
            case "CIVILIZATION_DECAY":
                if (convergenceLocked)
                {
                    return ContainsKeyword(blob, "CIVIL", "DECAY", "SUCCESSION", "CIVILWAR");
                }

                return true;
            default:
                return false;
        }
    }

    private static bool IsDisasterConvergenceLocked(string blob, MacroParamDelta terminal)
    {
        if (terminal == null)
        {
            return false;
        }

        bool disasterDominant = ContainsKeyword(
            blob,
            "BEAST",
            "CATASTROPHE",
            "MONSTER_DEFENSE",
            "BARRIERDROP");
        int disasterHits = CountDisasterKeywordHits(blob);
        bool resourceTrap = terminal.barrierEfficiencyDelta <= -0.08f &&
                            terminal.threatMultiplier >= 1.22f;
        bool lowFlex = terminal.powerMultiplier <= 0.94f;
        bool revivalDisasterTrap = terminal.threatMultiplier >= 1.35f &&
                                   terminal.barrierEfficiencyDelta <= -0.06f;
        return disasterDominant && disasterHits >= 2 && (resourceTrap || lowFlex || revivalDisasterTrap);
    }

    private static bool HasRevivalHumanSocietySignals(string blob, MacroParamDelta terminal)
    {
        if (!ContainsKeyword(
                blob,
                "INNOVATION",
                "ACADEMY",
                "HERETIC",
                "CIVIL",
                "TRADE",
                "COMMERCE",
                "SUCCESSION",
                "FRONTIER",
                "POLITICS",
                "REFORM",
                "LOST_TECH",
                "TECH"))
        {
            return false;
        }

        return terminal == null ||
               (terminal.powerMultiplier >= 0.96f &&
                terminal.barrierEfficiencyDelta >= -0.06f &&
                terminal.threatMultiplier <= 1.18f);
    }

    private static int CountDisasterKeywordHits(string blob)
    {
        string[] keywords = { "BEAST", "CATASTROPHE", "MONSTER", "BARRIERDROP", "BARRIER" };
        int hits = 0;
        for (int i = 0; i < keywords.Length; i++)
        {
            if (ContainsKeyword(blob, keywords[i]))
            {
                hits++;
            }
        }

        return hits;
    }

    private static int ScoreBranchingFactor(int accessibleCount)
    {
        // 旧より中間帯を抑え、満点(200)はほぼ全ブループリント開放時のみ。
        switch (accessibleCount)
        {
            case <= 1:
                return 0;
            case 2:
                return 20;
            case 3:
                return 40;
            case 4:
                return 65;
            case 5:
                return 90;
            case 6:
                return 115;
            case 7:
                return 145;
            case 8:
                return 175;
            default:
                return MaxBranchingFactorScore;
        }
    }

    private static int CountPotentialUnlocks(
        HashSet<string> accessible,
        string blob,
        bool convergenceLocked)
    {
        if (accessible == null || accessible.Count == 0)
        {
            return 0;
        }

        int count = 0;
        for (int i = 0; i < BlueprintUnlockCatalog.Length; i++)
        {
            (string suffix, string unlockFlag) = BlueprintUnlockCatalog[i];
            if (accessible.Contains(suffix) && !ContainsKeyword(blob, unlockFlag))
            {
                count++;
            }
        }

        if (accessible.Contains("LOST_TECH_REVIVAL") && !ContainsKeyword(blob, "INNOVATION", "LOST_TECH"))
        {
            count++;
        }

        if (accessible.Contains("ACADEMY_REFORM") && !ContainsKeyword(blob, "ACADEMY", "REFORM"))
        {
            count++;
        }

        if (accessible.Contains("HERETIC_SCHISM") && !ContainsKeyword(blob, "HERETIC", "SCHISM"))
        {
            count++;
        }

        if (accessible.Contains("FRONTIER_EXPANSION") &&
            !ContainsKeyword(blob, "SUCCESSION", "FRONTIER", "EXPANSION"))
        {
            count++;
        }

        if (!convergenceLocked &&
            !ContainsKeyword(blob, "COLOSSUS", "OBSERVED", "SINGULARITY", "SINGULAR"))
        {
            count++;
        }

        return count;
    }

    private static int ScorePotentialUnlocks(int unlockCount)
    {
        // 旧×18 だと数件で上限100に到達するため、単価を下げて到達を難しくする。
        return Mathf.Clamp(unlockCount * 10, 0, MaxPotentialUnlocksScore);
    }

    private static int ScoreCivilizationPlasticity(
        MacroParamDelta terminal,
        string blob,
        bool convergenceLocked)
    {
        if (convergenceLocked || terminal == null)
        {
            return 0;
        }

        int score = 0;
        // 閾値を引き上げ、満点は「力・結界・脅威・テーマ」が揃ったときのみ。
        if (terminal.powerMultiplier >= 1.12f)
        {
            score += 35;
        }
        else if (terminal.powerMultiplier >= 1.05f)
        {
            score += 15;
        }
        else if (terminal.powerMultiplier >= 1.0f)
        {
            score += 6;
        }

        if (terminal.barrierEfficiencyDelta >= 0.10f)
        {
            score += 35;
        }
        else if (terminal.barrierEfficiencyDelta >= 0.04f)
        {
            score += 18;
        }
        else if (terminal.barrierEfficiencyDelta >= 0f)
        {
            score += 6;
        }

        if (terminal.threatMultiplier <= 0.95f)
        {
            score += 30;
        }
        else if (terminal.threatMultiplier <= 1.05f)
        {
            score += 14;
        }
        else if (terminal.threatMultiplier <= 1.15f)
        {
            score += 6;
        }

        if (ContainsKeyword(blob, "INNOVATION", "ACADEMY", "LOST_TECH", "HERO", "FRONTIER"))
        {
            score += 10;
        }

        if (terminal.barrierEfficiencyDelta < -0.12f &&
            terminal.threatMultiplier > 1.35f &&
            !ContainsKeyword(blob, "INNOVATION", "ACADEMY", "LOST_TECH"))
        {
            score = Mathf.Max(0, score - 40);
        }

        return Mathf.Clamp(score, 0, MaxCivilizationPlasticityScore);
    }

    private static int ApplyRevivalEraBonus(
        string blob,
        MacroParamDelta terminal,
        HashSet<string> accessible,
        int evaluationTurn)
    {
        if (evaluationTurn < PruningModelStartTurn || accessible == null)
        {
            return 0;
        }

        int bonus = 0;
        // 件数・キーワード加点を抑え、ボーナスだけで合計を押し上げにくくする。
        if (accessible.Count >= 7)
        {
            bonus += 15;
        }
        else if (accessible.Count >= 5)
        {
            bonus += 8;
        }

        if (accessible.Count >= 8)
        {
            bonus += 10;
        }

        if (ContainsKeyword(blob, "CIVIL", "SUCCESSION", "POLITICS", "REFORM", "CIVILWAR"))
        {
            bonus += 10;
        }

        if (ContainsKeyword(blob, "INNOVATION", "LOST_TECH", "ACADEMY", "HERETIC", "TECH"))
        {
            bonus += 12;
        }

        if (ContainsKeyword(blob, "TRADE", "COMMERCE", "FRONTIER", "EXPANSION", "MARKET"))
        {
            bonus += 10;
        }

        if (terminal != null &&
            terminal.threatMultiplier <= 0.95f &&
            terminal.powerMultiplier >= 1.08f &&
            terminal.barrierEfficiencyDelta >= 0.04f)
        {
            bonus += 8;
        }

        if (EnvironmentBiorhythmEngine.IsCivilizationRevivalTurn(evaluationTurn))
        {
            bonus += 5;
        }

        return Mathf.Clamp(bonus, 0, MaxRevivalEraBonus);
    }
}

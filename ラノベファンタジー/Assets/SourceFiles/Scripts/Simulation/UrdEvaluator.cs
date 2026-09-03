using System;

// =============================================================================
// MAGI-2 ウルズ — 因果律・歴史整合性・EraTag 合致（拒否権）
// =============================================================================

/// <summary>
/// 因果律と時代水準との整合を評価し、大暴走枝には拒否権（Veto）を発動します。
/// </summary>
public sealed class UrdEvaluator : MagiSystemEvaluator
{
    public const string Id = "MAGI-2";
    public const string DisplayName = "ウルズ";

    public override string UnitId => Id;
    public override string UnitDisplayName => DisplayName;

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

        string vetoReason = TryBuildVetoReason(branch, evaluationTurn);
        if (!string.IsNullOrEmpty(vetoReason))
        {
            MagiUnitVote veto = BuildVote(branch, int.MinValue, vetoReason, veto: true);
            veto.vetoTriggered = true;
            return veto;
        }

        int score = scriptResult.continuityScore + (scriptResult.techGrowthScore / 2);
        EraTag era = EraContextResolver.ResolveEraTag(evaluationTurn);
        score += ScoreEraAlignment(branch, era);

        MacroParamDelta terminal = branch.AccumulateParamDeltaUpToTurn(
            evaluationTurn,
            branch.primaryNationId > 0 ? branch.primaryNationId : 1);

        if (terminal != null)
        {
            if (terminal.barrierEfficiencyDelta >= -0.05f && terminal.powerMultiplier >= 0.95f)
            {
                score += 12;
            }

            if (terminal.isSurvivalOverridden && !terminal.survivalValue)
            {
                score -= 18;
            }
        }

        return BuildVote(
            branch,
            score,
            $"整合={scriptResult.continuityScore} 技術={scriptResult.techGrowthScore} " +
            $"ERA={EraContextResolver.FormatEraLabel(era)}");
    }

    private static int ScoreEraAlignment(HistoryTimelineBranch branch, EraTag era)
    {
        string blob = BuildBranchBlob(branch);
        bool advancedMagic =
            ContainsKeyword(blob, "HERETIC_ORTHODOXY", "HERETIC", "ARCANE_REVIVAL");
        bool earlyInnovation = ContainsKeyword(blob, "INNOVATION") && era == EraTag.Early;

        switch (era)
        {
            case EraTag.Early:
                if (advancedMagic)
                {
                    return -25;
                }

                return earlyInnovation ? 10 : 0;
            case EraTag.Mid:
                if (ContainsKeyword(blob, "HERETIC", "DEMON", "CIVIL"))
                {
                    return 14;
                }

                return 6;
            case EraTag.Late:
                if (advancedMagic || ContainsKeyword(blob, "ORTHODOXY", "DEMON"))
                {
                    return 18;
                }

                return 4;
            default:
                return 0;
        }
    }

    private static string TryBuildVetoReason(HistoryTimelineBranch branch, int evaluationTurn)
    {
        MacroParamDelta terminal = branch.AccumulateParamDeltaUpToTurn(
            evaluationTurn,
            branch.primaryNationId > 0 ? branch.primaryNationId : 1);

        if (terminal == null)
        {
            return null;
        }

        string blob = BuildBranchBlob(branch);
        EraTag era = EraContextResolver.ResolveEraTag(evaluationTurn);

        if (terminal.isSurvivalOverridden &&
            !terminal.survivalValue &&
            terminal.powerMultiplier > 1.15f)
        {
            return "拒否: 滅亡上書きと国力急増が矛盾";
        }

        if (terminal.barrierEfficiencyDelta < -0.28f &&
            ContainsKeyword(blob, "INNOVATION") &&
            !ContainsKeyword(blob, "BARRIERDROP", "RECOVERY"))
        {
            return "拒否: 結界大崩壊と無条件イノベーションの因果不整合";
        }

        if (era == EraTag.Early &&
            ContainsKeyword(blob, "HERETIC_ORTHODOXY", "ORTHODOXY") &&
            evaluationTurn <= EraContextResolver.EarlyEraMaxTurn)
        {
            return "拒否: 普遍期に異端正統化は時代不整合";
        }

        if (terminal.powerMultiplier < 0.55f &&
            terminal.threatMultiplier > 2.2f &&
            !terminal.isSurvivalOverridden)
        {
            return "拒否: 国力崩壊×脅威急騰の大暴走枝";
        }

        return null;
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;

// =============================================================================
// プレイヤー戦歴ログ × evolutionCriteria による進化候補の動的フィルタ
// =============================================================================

/// <summary>ログスキャン結果 1 件。</summary>
public sealed class EvolutionLogScanResult
{
    public string targetSkillId;
    public string targetSkillName;
    public string description;
    public SkillEvolutionData gate;
    public bool isGenericFallback;
    public bool criteriaMet;
    public bool levelMet;
    public bool flagMet;
}

/// <summary>
/// PlayerHistoryTracker を参照し、evolutionCriteria を満たす発展スキルのみを抽出します。
/// </summary>
public static class EvolutionLogScanner
{
    /// <summary>
    /// 外部フック（PlayerHistoryTracker 未配置時のフォールバック）。
    /// SkillEvolutionSystemBootstrap が PlayerHistoryTracker へ接続します。
    /// </summary>
    public static Func<string, int> LogCountResolver { get; set; }

    /// <summary>
    /// 候補 SkillMaster 群から、ログ・レベル・フラグを満たすものだけを返します。
    /// 満たさない候補はリストに含めません。
    /// </summary>
    public static List<EvolutionLogScanResult> FilterEligibleBranches(
        IReadOnlyList<SkillMaster> candidates,
        int currentSkillLevel,
        PlayerHistoryTracker historyTracker = null)
    {
        List<EvolutionLogScanResult> results = new List<EvolutionLogScanResult>();
        if (candidates == null || candidates.Count == 0)
        {
            return results;
        }

        for (int i = 0; i < candidates.Count; i++)
        {
            SkillMaster candidate = candidates[i];
            if (candidate == null || string.IsNullOrWhiteSpace(candidate.skillId))
            {
                continue;
            }

            SkillEvolutionData gate = candidate.evolutionData ?? new SkillEvolutionData();
            gate.Sanitize(candidate.skillId);

            bool levelMet = currentSkillLevel >= gate.requiredLevel;
            bool flagMet = ResolveConditionFlag(gate.conditionFlag);
            bool criteriaMet = ResolveEvolutionCriteria(gate.evolutionCriteria, historyTracker);

            if (!levelMet || !flagMet || !criteriaMet)
            {
                continue;
            }

            results.Add(new EvolutionLogScanResult
            {
                targetSkillId = candidate.skillId,
                targetSkillName = candidate.skillName,
                description = candidate.description,
                gate = gate,
                isGenericFallback = IsGenericFallbackId(candidate.skillId),
                criteriaMet = criteriaMet,
                levelMet = levelMet,
                flagMet = flagMet
            });
        }

        return results;
    }

    /// <summary>単一 criteria が満たされているか。</summary>
    public static bool IsCriteriaMet(
        SkillEvolutionCriteria criteria,
        PlayerHistoryTracker historyTracker = null)
    {
        return ResolveEvolutionCriteria(criteria, historyTracker);
    }

    /// <summary>requiredLogType の現在値を取得します。</summary>
    public static int ResolveLogCount(string logType, PlayerHistoryTracker historyTracker)
    {
        if (!string.IsNullOrWhiteSpace(logType) && historyTracker != null)
        {
            return historyTracker.GetCount(logType);
        }

        if (LogCountResolver != null)
        {
            try
            {
                return LogCountResolver(logType);
            }
            catch (Exception exception)
            {
                Debug.LogError($"[EvolutionLogScanner] LogCountResolver 例外: {logType}\n{exception}");
            }
        }

        return 0;
    }

    private static bool ResolveEvolutionCriteria(
        SkillEvolutionCriteria criteria,
        PlayerHistoryTracker historyTracker)
    {
        criteria ??= new SkillEvolutionCriteria();
        criteria.Sanitize();

        if (criteria.IsGeneralPrerequisite())
        {
            return true;
        }

        int count = ResolveLogCount(criteria.requiredLogType, historyTracker);
        return count >= criteria.requiredValue;
    }

    private static bool ResolveConditionFlag(string conditionFlag)
    {
        if (string.IsNullOrWhiteSpace(conditionFlag))
        {
            return true;
        }

        if (SkillEvolutionLinker.ConditionFlagResolver == null)
        {
            return false;
        }

        try
        {
            return SkillEvolutionLinker.ConditionFlagResolver(conditionFlag);
        }
        catch (Exception exception)
        {
            Debug.LogError($"[EvolutionLogScanner] conditionFlag 例外: {conditionFlag}\n{exception}");
            return false;
        }
    }

    private static bool IsGenericFallbackId(string skillId)
    {
        return !string.IsNullOrWhiteSpace(skillId) &&
               skillId.EndsWith("_ADV", StringComparison.OrdinalIgnoreCase);
    }
}

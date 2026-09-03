using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

// =============================================================================
// 基礎スキル → 発展スキル（Evolution）分岐の動的解決
// skillId 規約: SKL_{BASE_SUFFIX}_C###（国家流派） / SKL_{BASE_SUFFIX}_ADV（汎用上位）
// =============================================================================

/// <summary>進化候補 1 件分の表示・実行用データ。</summary>
public sealed class EvolutionBranchOption
{
    public string targetSkillId;
    public string targetSkillName;
    public string description;
    public SkillEvolutionData gate;
    public bool isGenericFallback;
}

/// <summary>
/// 戦歴ログ・クラフトログから発展スキル候補を列挙します。
/// SkillMasterRepository に登録済みの SKL_* マスターを走査します。
/// </summary>
public static class EvolutionSkillBranchResolver
{
    private static readonly Regex CountryBranchIdPattern =
        new Regex(@"^SKL_(.+)_C(\d{3})$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex GenericAdvancedIdPattern =
        new Regex(@"^SKL_(.+)_ADV$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// ログ累計値の取得フック。
    /// 例: (logType) => PlayerBattleHistory.GetCount(logType)
    /// </summary>
    public static Func<string, int> LogCountResolver { get; set; }

    /// <summary>基礎スキル ID から進化候補を条件順に列挙します。</summary>
    public static List<EvolutionBranchOption> ResolveBranches(
        string baseSkillId,
        int currentSkillLevel,
        SkillMasterRepository repository = null)
    {
        List<EvolutionBranchOption> options = new List<EvolutionBranchOption>();
        if (string.IsNullOrWhiteSpace(baseSkillId))
        {
            return options;
        }

        repository ??= SkillMasterRepository.Instance ?? UnityEngine.Object.FindAnyObjectByType<SkillMasterRepository>();
        if (repository == null)
        {
            Debug.LogWarning("[EvolutionSkillBranchResolver] SkillMasterRepository が未初期化です。");
            return options;
        }

        string baseSuffix = StripSkillPrefix(baseSkillId);
        if (string.IsNullOrWhiteSpace(baseSuffix))
        {
            return options;
        }

        IReadOnlyList<SkillMaster> all = repository.GetAll();
        EvolutionBranchOption genericFallback = null;

        for (int i = 0; i < all.Count; i++)
        {
            SkillMaster candidate = all[i];
            if (candidate == null || string.IsNullOrWhiteSpace(candidate.skillId))
            {
                continue;
            }

            if (!TryMatchBaseSuffix(candidate.skillId, baseSuffix, out bool isGeneric))
            {
                continue;
            }

            SkillEvolutionData gate = candidate.evolutionData ?? new SkillEvolutionData();
            gate.Sanitize(candidate.skillId);

            if (!IsBranchUnlocked(gate, currentSkillLevel))
            {
                continue;
            }

            EvolutionBranchOption option = new EvolutionBranchOption
            {
                targetSkillId = candidate.skillId,
                targetSkillName = candidate.skillName,
                description = candidate.description,
                gate = gate,
                isGenericFallback = isGeneric
            };

            if (isGeneric)
            {
                genericFallback = option;
            }
            else
            {
                options.Add(option);
            }
        }

        options.Sort(CompareCountryFirst);
        if (genericFallback != null)
        {
            options.Add(genericFallback);
        }

        return options;
    }

    /// <summary>baseSkillId から直接進化先を 1 件実行します（候補が 1 件のとき等）。</summary>
    public static bool TryExecuteBranch(
        string baseSkillId,
        string targetEvolutionSkillId,
        int currentSkillLevel,
        PlayerSkillSlotManager skillSlots = null)
    {
        if (string.IsNullOrWhiteSpace(baseSkillId) || string.IsNullOrWhiteSpace(targetEvolutionSkillId))
        {
            return false;
        }

        List<EvolutionBranchOption> branches = ResolveBranches(baseSkillId, currentSkillLevel);
        for (int i = 0; i < branches.Count; i++)
        {
            EvolutionBranchOption branch = branches[i];
            if (!string.Equals(branch.targetSkillId, targetEvolutionSkillId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return SkillEvolutionLinker.CheckAndExecuteEvolutionToTarget(
                baseSkillId,
                targetEvolutionSkillId,
                currentSkillLevel,
                skillSlots);
        }

        Debug.LogWarning(
            $"[EvolutionSkillBranchResolver] 進化先が候補にありません: {baseSkillId} -> {targetEvolutionSkillId}");
        return false;
    }

    private static bool TryMatchBaseSuffix(string evolutionSkillId, string baseSuffix, out bool isGeneric)
    {
        isGeneric = false;
        Match country = CountryBranchIdPattern.Match(evolutionSkillId);
        if (country.Success)
        {
            return string.Equals(country.Groups[1].Value, baseSuffix, StringComparison.OrdinalIgnoreCase);
        }

        Match generic = GenericAdvancedIdPattern.Match(evolutionSkillId);
        if (generic.Success && string.Equals(generic.Groups[1].Value, baseSuffix, StringComparison.OrdinalIgnoreCase))
        {
            isGeneric = true;
            return true;
        }

        // Utility: SKL_UTIL_RADAR_DEEP / _PEAK
        if (evolutionSkillId.StartsWith("SKL_", StringComparison.OrdinalIgnoreCase) &&
            evolutionSkillId.IndexOf(baseSuffix, StringComparison.OrdinalIgnoreCase) >= 0 &&
            (evolutionSkillId.EndsWith("_DEEP", StringComparison.OrdinalIgnoreCase) ||
             evolutionSkillId.EndsWith("_PEAK", StringComparison.OrdinalIgnoreCase)))
        {
            isGeneric = true;
            return true;
        }

        return false;
    }

    private static string StripSkillPrefix(string skillId)
    {
        if (string.IsNullOrWhiteSpace(skillId))
        {
            return string.Empty;
        }

        return skillId.StartsWith("SKILL_", StringComparison.OrdinalIgnoreCase)
            ? skillId.Substring("SKILL_".Length)
            : skillId;
    }

    private static bool IsBranchUnlocked(SkillEvolutionData gate, int currentSkillLevel)
    {
        if (gate == null)
        {
            return false;
        }

        if (currentSkillLevel < gate.requiredLevel)
        {
            return false;
        }

        if (!ResolveConditionFlagSatisfied(gate.conditionFlag))
        {
            return false;
        }

        return ResolveEvolutionCriteriaSatisfied(gate.evolutionCriteria);
    }

    private static bool ResolveConditionFlagSatisfied(string conditionFlag)
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
            Debug.LogError($"[EvolutionSkillBranchResolver] conditionFlag 判定例外: {conditionFlag}\n{exception}");
            return false;
        }
    }

    private static bool ResolveEvolutionCriteriaSatisfied(SkillEvolutionCriteria criteria)
    {
        return EvolutionLogScanner.IsCriteriaMet(criteria, PlayerHistoryTracker.Instance);
    }

    private static int CompareCountryFirst(EvolutionBranchOption a, EvolutionBranchOption b)
    {
        if (a.isGenericFallback != b.isGenericFallback)
        {
            return a.isGenericFallback ? 1 : -1;
        }

        return string.Compare(a.targetSkillName, b.targetSkillName, StringComparison.OrdinalIgnoreCase);
    }
}

using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

// =============================================================================
// AI 量産 SkillMaster JSON のランタイム整合性検証
// =============================================================================

/// <summary>検証結果 1 件。</summary>
public sealed class GeneratedDataValidationIssue
{
    public string skillId;
    public string artId;
    public string message;

    public GeneratedDataValidationIssue(string skillId, string artId, string message)
    {
        this.skillId = skillId ?? string.Empty;
        this.artId = artId ?? string.Empty;
        this.message = message ?? string.Empty;
    }

    public override string ToString()
    {
        if (string.IsNullOrWhiteSpace(artId))
        {
            return $"[{skillId}] {message}";
        }

        return $"[{skillId}/{artId}] {message}";
    }
}

/// <summary>SkillMaster バッチ検証の集計結果。</summary>
public sealed class GeneratedDataValidationReport
{
    public bool IsValid => issues.Count == 0;
    public int SkillsChecked { get; private set; }
    public IReadOnlyList<GeneratedDataValidationIssue> Issues => issues;

    private readonly List<GeneratedDataValidationIssue> issues = new List<GeneratedDataValidationIssue>();

    public void AddIssue(string skillId, string artId, string message)
    {
        issues.Add(new GeneratedDataValidationIssue(skillId, artId, message));
    }

    public void SetSkillsChecked(int count)
    {
        SkillsChecked = count;
    }

    public string BuildSummary()
    {
        StringBuilder sb = new StringBuilder(256);
        sb.Append("GeneratedDataValidator: ");
        sb.Append(IsValid ? "OK" : $"NG ({issues.Count} issues)");
        sb.Append($", skills={SkillsChecked}");
        return sb.ToString();
    }

    public void LogAllIssues()
    {
        for (int i = 0; i < issues.Count; i++)
        {
            Debug.LogError($"[GeneratedDataValidator] {issues[i]}");
        }
    }
}

/// <summary>
/// JsonUtility でロード済みの SkillMaster が進化スキル規約を満たすか検証します。
/// </summary>
public static class GeneratedDataValidator
{
    public const int MaxArtsCombatProduction = 5;
    public const int MaxArtsUtility = 3;

    /// <summary>単一 SkillMaster を検証し、問題があれば report に追記します。</summary>
    public static void ValidateSkill(SkillMaster skill, GeneratedDataValidationReport report)
    {
        if (skill == null)
        {
            report?.AddIssue("(null)", string.Empty, "SkillMaster が null です。");
            return;
        }

        if (!skill.IsValid())
        {
            report?.AddIssue(skill.skillId ?? "(invalid)", string.Empty, "skillId / skillName / category が不正です。");
            return;
        }

        ValidateEvolutionSchema(skill, report);
        ValidateArtsTree(skill, report);
    }

    /// <summary>複数スキルを検証します。</summary>
    public static GeneratedDataValidationReport ValidateSkills(IReadOnlyList<SkillMaster> skills)
    {
        GeneratedDataValidationReport report = new GeneratedDataValidationReport();
        if (skills == null)
        {
            report.AddIssue("(batch)", string.Empty, "skills リストが null です。");
            return report;
        }

        report.SetSkillsChecked(skills.Count);
        for (int i = 0; i < skills.Count; i++)
        {
            ValidateSkill(skills[i], report);
        }

        return report;
    }

    /// <summary>検証し、失敗時は Debug.LogError で詳細を出力します。</summary>
    public static bool ValidateAndLog(SkillMaster skill)
    {
        GeneratedDataValidationReport report = new GeneratedDataValidationReport();
        report.SetSkillsChecked(1);
        ValidateSkill(skill, report);
        if (!report.IsValid)
        {
            report.LogAllIssues();
        }

        return report.IsValid;
    }

    private static void ValidateEvolutionSchema(SkillMaster skill, GeneratedDataValidationReport report)
    {
        if (skill.evolutionData == null)
        {
            report.AddIssue(skill.skillId, string.Empty,
                "evolutionData が null です（nextSkillId フィールドを含むオブジェクトが必須）。");
            return;
        }

        // JsonUtility 未パース時は null になり得るため、ランタイムでは空文字へ正規化して存在を担保
        if (skill.evolutionData.nextSkillId == null)
        {
            report.AddIssue(skill.skillId, string.Empty,
                "evolutionData.nextSkillId が未定義です（空文字 \"\" は許容）。");
        }

        skill.evolutionData.Sanitize(skill.skillId);
        skill.evolutionData.evolutionCriteria?.Sanitize();
    }

    private static void ValidateArtsTree(SkillMaster skill, GeneratedDataValidationReport report)
    {
        if (skill.baseArts == null || skill.baseArts.Count == 0)
        {
            report.AddIssue(skill.skillId, string.Empty, "baseArts が空です。");
            return;
        }

        int maxArts = ResolveMaxArts(skill.category);
        int totalArts = 0;
        for (int i = 0; i < skill.baseArts.Count; i++)
        {
            ArtsData root = skill.baseArts[i];
            if (root == null)
            {
                continue;
            }

            CountAndValidateArtsRecursive(
                skill,
                root,
                depth: 0,
                ref totalArts,
                maxArts,
                report);
        }

        if (totalArts > maxArts)
        {
            report.AddIssue(skill.skillId, string.Empty,
                $"技総数 {totalArts} がカテゴリ上限 {maxArts} を超過しています。");
        }

        if (IsCombatCategory(skill.category))
        {
            if (IsGenericAdvancedCombat(skill) && totalArts != MaxArtsCombatProduction)
            {
                report.AddIssue(skill.skillId, string.Empty,
                    $"汎用上位戦闘スキルは技総数 {MaxArtsCombatProduction} 必須です（実際: {totalArts}）。");
            }
        }
    }

    private static void CountAndValidateArtsRecursive(
        SkillMaster skill,
        ArtsData art,
        int depth,
        ref int totalArts,
        int maxArts,
        GeneratedDataValidationReport report)
    {
        if (art == null)
        {
            return;
        }

        totalArts++;
        if (totalArts > maxArts)
        {
            return;
        }

        ValidateSpecialEffectPlacement(skill, art, depth, report);

        if (art.derivatives == null)
        {
            return;
        }

        for (int i = 0; i < art.derivatives.Count; i++)
        {
            CountAndValidateArtsRecursive(
                skill,
                art.derivatives[i],
                depth + 1,
                ref totalArts,
                maxArts,
                report);
        }
    }

    private static void ValidateSpecialEffectPlacement(
        SkillMaster skill,
        ArtsData art,
        int depth,
        GeneratedDataValidationReport report)
    {
        if (!IsCombatCategory(skill.category))
        {
            return;
        }

        bool hasEffects = art.specialEffects != null && art.specialEffects.Count > 0;
        if (!hasEffects)
        {
            return;
        }

        if (IsGenericAdvancedCombat(skill))
        {
            report.AddIssue(skill.skillId, art.artId,
                "汎用上位戦闘スキルは全技の specialEffects が空である必要があります。");
            return;
        }

        // 国家流派など: 根（depth 0）と直下の枝（depth 1）は効果なし
        if (depth <= 1)
        {
            report.AddIssue(skill.skillId, art.artId,
                $"戦闘スキル: 根・枝（depth<={depth}）に specialEffects が付与されています（メリハリ違反）。");
        }
    }

    private static int ResolveMaxArts(string category)
    {
        if (string.Equals(category, SkillMasterCategories.Utility, StringComparison.OrdinalIgnoreCase))
        {
            return MaxArtsUtility;
        }

        if (string.Equals(category, SkillMasterCategories.Combat, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(category, SkillMasterCategories.Production, StringComparison.OrdinalIgnoreCase))
        {
            return MaxArtsCombatProduction;
        }

        return MaxArtsCombatProduction;
    }

    private static bool IsCombatCategory(string category)
    {
        return string.Equals(category, SkillMasterCategories.Combat, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsGenericAdvancedCombat(SkillMaster skill)
    {
        if (skill == null || string.IsNullOrWhiteSpace(skill.skillId))
        {
            return false;
        }

        if (skill.skillId.EndsWith("_ADV", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(skill.skillName) &&
               skill.skillName.StartsWith("上位", StringComparison.Ordinal);
    }
}

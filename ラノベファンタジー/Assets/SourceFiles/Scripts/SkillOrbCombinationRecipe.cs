using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 2つのスキルオーブを掛け合わせて新スキル（器）を生成するレシピ。
/// 専門職 NPC による「スキル合成」で使用します。
/// </summary>
[Serializable]
public class SkillOrbCombinationRecipe
{
    [Tooltip("素材オーブ A のスキル ID")]
    public string skillIdA;

    [Tooltip("素材オーブ B のスキル ID")]
    public string skillIdB;

    [Tooltip("合成で誕生する新スキル ID")]
    public string resultSkillId;

    [Tooltip("合成スキルの表示名")]
    public string resultSkillName;

    [Tooltip("合成スキルのカテゴリー")]
    public SkillCategory resultCategory = SkillCategory.Attack;

    /// <summary>レシピとして有効か</summary>
    public bool IsValid()
    {
        return !string.IsNullOrWhiteSpace(skillIdA) &&
               !string.IsNullOrWhiteSpace(skillIdB) &&
               !string.IsNullOrWhiteSpace(resultSkillId) &&
               !string.IsNullOrWhiteSpace(resultSkillName);
    }

    /// <summary>2つのスキル ID ペアに合致するか（順不同）</summary>
    public bool Matches(string skillIdX, string skillIdY)
    {
        return (skillIdA == skillIdX && skillIdB == skillIdY) ||
               (skillIdA == skillIdY && skillIdB == skillIdX);
    }

    /// <summary>デフォルト：片手剣術 × 舞踏術 → 剣舞融合術</summary>
    public static SkillOrbCombinationRecipe CreateBladeDanceFusionRecipe()
    {
        return new SkillOrbCombinationRecipe
        {
            skillIdA = SkillIds.OneHandSword,
            skillIdB = SkillIds.DanceArt,
            resultSkillId = SkillIds.BladeDanceFusion,
            resultSkillName = "剣舞融合術",
            resultCategory = SkillCategory.Attack
        };
    }
}

/// <summary>スキルオーブ合成レシピ一覧（JSON 用ラッパー）。</summary>
[Serializable]
public class SkillOrbCombinationRecipeList
{
    public List<SkillOrbCombinationRecipe> recipes = new List<SkillOrbCombinationRecipe>();

    public static SkillOrbCombinationRecipeList CreateDefault()
    {
        return new SkillOrbCombinationRecipeList
        {
            recipes = new List<SkillOrbCombinationRecipe>
            {
                SkillOrbCombinationRecipe.CreateBladeDanceFusionRecipe()
            }
        };
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 能動的・技開発レシピ。
/// 「素材スキル × 素材技」→ 完成 ActionData の組み合わせを定義します。
/// JsonUtility シリアライズに対応しています。
/// </summary>
[Serializable]
public class ActionDevelopmentRecipe
{
    [Tooltip("素材となるスキル ID（プレイヤーが所持している必要あり）")]
    public string materialSkillId;

    [Tooltip("素材となる技 ID（いずれかのスキル内に装着されている必要あり）")]
    public string materialActionId;

    [Tooltip("完成技を付与する先スキル ID")]
    public string targetSkillId;

    [Tooltip("開発成功時に生成される新技")]
    public ActionData resultAction;

    /// <summary>レシピとして最低限有効か</summary>
    public bool IsValid()
    {
        return !string.IsNullOrWhiteSpace(materialSkillId) &&
               !string.IsNullOrWhiteSpace(materialActionId) &&
               !string.IsNullOrWhiteSpace(targetSkillId) &&
               resultAction != null &&
               resultAction.IsValid();
    }

    /// <summary>指定の素材ペアに合致するか</summary>
    public bool Matches(string skillId, string actionId)
    {
        return materialSkillId == skillId && materialActionId == actionId;
    }

    public ActionDevelopmentRecipe Clone()
    {
        return FromJson(ToJson());
    }

    public static ActionDevelopmentRecipe FromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            ActionDevelopmentRecipe recipe = JsonUtility.FromJson<ActionDevelopmentRecipe>(json);
            return recipe != null && recipe.IsValid() ? recipe : null;
        }
        catch (Exception exception)
        {
            Debug.LogError($"[ActionDevelopmentRecipe] JSON パース失敗: {exception.Message}");
            return null;
        }
    }

    public string ToJson(bool prettyPrint = false)
    {
        return JsonUtility.ToJson(this, prettyPrint);
    }

    /// <summary>舞踏術 × 通常斬り → 剣の舞（デフォルトサンプルレシピ）</summary>
    public static ActionDevelopmentRecipe CreateDanceBladeRecipe()
    {
        return new ActionDevelopmentRecipe
        {
            materialSkillId = SkillIds.DanceArt,
            materialActionId = ActionIds.BasicSlash,
            targetSkillId = SkillIds.OneHandSword,
            resultAction = new ActionData
            {
                actionID = ActionIds.DanceBlade,
                actionName = "剣の舞",
                damageMultiplier = 1.6f,
                staminaCost = 24f,
                activeDetectionTime = 0.38f,
                invincibilityTime = 0f,
                isDerived = true,
                inspirationSource = "【舞踏術】と【通常斬り】を安全地帯で能動的に融合して開発した術理"
            }
        };
    }
}

/// <summary>
/// 技開発レシピ一覧（JSON 保存・サーバー配信用ラッパー）。
/// </summary>
[Serializable]
public class ActionDevelopmentRecipeList
{
    public List<ActionDevelopmentRecipe> recipes = new List<ActionDevelopmentRecipe>();

    public static ActionDevelopmentRecipeList CreateDefault()
    {
        return new ActionDevelopmentRecipeList
        {
            recipes = new List<ActionDevelopmentRecipe>
            {
                ActionDevelopmentRecipe.CreateDanceBladeRecipe()
            }
        };
    }

    public static ActionDevelopmentRecipeList FromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonUtility.FromJson<ActionDevelopmentRecipeList>(json);
        }
        catch (Exception exception)
        {
            Debug.LogError($"[ActionDevelopmentRecipeList] JSON パース失敗: {exception.Message}");
            return null;
        }
    }

    public string ToJson(bool prettyPrint = false)
    {
        return JsonUtility.ToJson(this, prettyPrint);
    }
}

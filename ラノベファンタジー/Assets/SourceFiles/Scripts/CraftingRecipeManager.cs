using System;
using System.Collections.Generic;
using UnityEngine;

// =============================================================================
// 工房クラフトレシピ解決 — 投入素材 ID の組み合わせから成果物 ID を決定
// 連携: CraftingStatusManager / MasterDataManager / CraftingQualityPacketEmitter
// =============================================================================

/// <summary>レシピ解決で使用する既定成果物・フォールバック ID。</summary>
public static class CraftRecipeIds
{
    public const string CraftFailure = "MAT_CRAFT_FAILURE";
    public const string SlimeGel = "MAT_SLIME_GEL";
    public const string BeetleChitin = "MAT_BEETLE_CHITIN";
    public const string CrystalShard = "MAT_CRYSTAL_SHARD";
    public const string ChitinEdge = "WEAPON_CHITIN_EDGE";
    public const string HighRemedy = "POTION_HIGH_REMEDY";
}

/// <summary>JsonUtility で将来ロード可能な簡易レシピ1件（必要素材 → 成果物）。</summary>
[Serializable]
public class CraftingRecipeEntry
{
    [Tooltip("完成品 ItemMasterData.id")]
    public string resultItemId;

    [Tooltip("この ID をすべて1個以上含めば成立（順不同）")]
    public string[] requiredIngredientIds;

    [Tooltip("true のとき投入素材が全て同一 ID である必要があります")]
    public bool requireAllSameIngredient;
}

/// <summary>レシピ解決結果の表示用スナップショット。</summary>
public readonly struct CraftingResultItemSnapshot
{
    public string ResultItemId { get; }
    public string ResultItemName { get; }
    public string ResultItemDescription { get; }
    public bool UsedFallback { get; }

    public CraftingResultItemSnapshot(
        string resultItemId,
        string resultItemName,
        string resultItemDescription,
        bool usedFallback)
    {
        ResultItemId = resultItemId ?? string.Empty;
        ResultItemName = resultItemName ?? string.Empty;
        ResultItemDescription = resultItemDescription ?? string.Empty;
        UsedFallback = usedFallback;
    }
}

/// <summary>
/// 投入素材リストから完成品 ItemMasterData.id をジャッジするルールエンジン。
/// </summary>
public static class CraftingRecipeManager
{
    private static readonly CraftingRecipeEntry[] BuiltinRecipes =
    {
        // 全スロット同一: スライムゲル ➔ 高密度のスライム塊（同一 ID・品質ブーストは別系統）
        new CraftingRecipeEntry
        {
            resultItemId = CraftRecipeIds.SlimeGel,
            requiredIngredientIds = new[] { CraftRecipeIds.SlimeGel },
            requireAllSameIngredient = true
        },
        // 異素材: 骸甲虫の重分子殻 + 原生異形の原液ジェル ➔ キチン・鋸大刃
        new CraftingRecipeEntry
        {
            resultItemId = CraftRecipeIds.ChitinEdge,
            requiredIngredientIds = new[] { CraftRecipeIds.BeetleChitin, CraftRecipeIds.SlimeGel },
            requireAllSameIngredient = false
        },
        // 異素材: スライムゲル + 結晶破片 ➔ ハイ・結晶レメディ
        new CraftingRecipeEntry
        {
            resultItemId = CraftRecipeIds.HighRemedy,
            requiredIngredientIds = new[] { CraftRecipeIds.SlimeGel, CraftRecipeIds.CrystalShard },
            requireAllSameIngredient = false
        }
    };

    /// <summary>
    /// 投入素材 ID リストから成果物 ID を解決します。
    /// 不正 ID 混入時・該当レシピなし時は MAT_CRAFT_FAILURE へ Safe-Fail します。
    /// </summary>
    public static string ResolveCraftingResult(List<string> ingredientItemIds)
    {
        CraftingResultItemSnapshot snapshot = ResolveCraftingResultDetailed(ingredientItemIds);
        return snapshot.ResultItemId;
    }

    /// <summary>成果物 ID とマスター表示情報をまとめて解決します。</summary>
    public static CraftingResultItemSnapshot ResolveCraftingResultDetailed(List<string> ingredientItemIds)
    {
        if (ingredientItemIds == null || ingredientItemIds.Count == 0)
        {
            return BuildSnapshot(CraftRecipeIds.CraftFailure, true);
        }

        List<string> normalized = new List<string>(ingredientItemIds.Count);
        for (int i = 0; i < ingredientItemIds.Count; i++)
        {
            string rawId = ingredientItemIds[i];
            if (string.IsNullOrWhiteSpace(rawId))
            {
                continue;
            }

            string itemId = rawId.Trim();
            if (!IsValidIngredient(itemId))
            {
                Debug.LogError(
                    $"[CraftingRecipeManager] 不正素材 ID '{itemId}' が検出されました。" +
                    "歪んだ結晶鉄くず（MAT_CRAFT_FAILURE）へフォールバックします。");
                return BuildSnapshot(CraftRecipeIds.CraftFailure, true);
            }

            normalized.Add(itemId);
        }

        if (normalized.Count == 0)
        {
            return BuildSnapshot(CraftRecipeIds.CraftFailure, true);
        }

        for (int r = 0; r < BuiltinRecipes.Length; r++)
        {
            CraftingRecipeEntry recipe = BuiltinRecipes[r];
            if (recipe == null || string.IsNullOrWhiteSpace(recipe.resultItemId))
            {
                continue;
            }

            if (MatchesRecipe(normalized, recipe))
            {
                Debug.Log(
                    $"[CraftingRecipeManager] レシピ成立: [{string.Join(", ", normalized)}] ➔ {recipe.resultItemId}");
                return BuildSnapshot(recipe.resultItemId, false);
            }
        }

        Debug.LogWarning(
            $"[CraftingRecipeManager] 未登録の素材組み合わせ: [{string.Join(", ", normalized)}]。" +
            "失敗作（MAT_CRAFT_FAILURE）へフォールバックします。");
        return BuildSnapshot(CraftRecipeIds.CraftFailure, true);
    }

    /// <summary>マスターに登録済みの Material 素材かを検証します。</summary>
    public static bool IsValidIngredient(string itemId)
    {
        return CraftingMaterialRegistry.IsRegisteredMaterial(itemId);
    }

    /// <summary>成果物 ID から ItemMasterData の名称・説明を引いたスナップショットを生成します。</summary>
    public static CraftingResultItemSnapshot BuildSnapshot(string resultItemId, bool usedFallback)
    {
        string safeId = string.IsNullOrWhiteSpace(resultItemId)
            ? CraftRecipeIds.CraftFailure
            : resultItemId.Trim();

        MasterDataManager masterData = MasterDataManager.Instance;
        if (masterData != null && masterData.TryGetItem(safeId, out ItemMasterData item) && item != null)
        {
            return new CraftingResultItemSnapshot(item.id, item.name, item.description, usedFallback);
        }

        return new CraftingResultItemSnapshot(safeId, safeId, string.Empty, usedFallback);
    }

    /// <summary>レシピ定義と投入リストが一致するかを判定します（順不同・重複投入可）。</summary>
    private static bool MatchesRecipe(List<string> ingredients, CraftingRecipeEntry recipe)
    {
        if (recipe.requiredIngredientIds == null || recipe.requiredIngredientIds.Length == 0)
        {
            return false;
        }

        if (recipe.requireAllSameIngredient)
        {
            string expected = recipe.requiredIngredientIds[0];
            for (int i = 0; i < ingredients.Count; i++)
            {
                if (!string.Equals(ingredients[i], expected, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            return ingredients.Count > 0;
        }

        for (int r = 0; r < recipe.requiredIngredientIds.Length; r++)
        {
            string required = recipe.requiredIngredientIds[r];
            if (!ContainsIngredient(ingredients, required))
            {
                return false;
            }
        }

        return true;
    }

    private static bool ContainsIngredient(List<string> ingredients, string requiredId)
    {
        if (string.IsNullOrWhiteSpace(requiredId))
        {
            return false;
        }

        for (int i = 0; i < ingredients.Count; i++)
        {
            if (string.Equals(ingredients[i], requiredId, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}

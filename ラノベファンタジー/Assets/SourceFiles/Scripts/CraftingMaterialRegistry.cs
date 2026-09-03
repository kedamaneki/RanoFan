using System;
using UnityEngine;

// =============================================================================
// ItemMasterData.craftValue / itemType -> 工房品質計算への仲介
// 連携: CraftingStatusManager / CraftingQualityPacketEmitter
// =============================================================================

/// <summary>
/// 工房フェーズで投入素材のマスターメタデータを品質係数へ変換する静的レジストリ。
/// </summary>
public static class CraftingMaterialRegistry
{
    public const string ItemTypeMaterial = "Material";
    public const string ItemTypeEquipment = "Equipment";
    public const string ItemTypeConsumable = "Consumable";

    private const float FallbackCraftValue = 1f;

    /// <summary>
    /// 素材 ID から品質影響係数（craftValue）を返します。
    /// itemType が Material でない、または未登録 ID の場合は 1.0f（等倍・安全フォールバック）を返します。
    /// </summary>
    public static float GetMaterialCraftValue(string itemId)
    {
        if (string.IsNullOrWhiteSpace(itemId))
        {
            return FallbackCraftValue;
        }

        MasterDataManager masterData = MasterDataManager.Instance;
        if (masterData == null)
        {
            Debug.LogWarning("[CraftingMaterialRegistry] MasterDataManager 未初期化。craftValue=1.0 で続行します。");
            return FallbackCraftValue;
        }

        ItemMasterData itemMaster = masterData.GetItem(itemId);
        if (itemMaster == null || !itemMaster.IsValid())
        {
            Debug.LogWarning(
                $"[CraftingMaterialRegistry] 未登録 itemId '{itemId}'。craftValue=1.0 で続行します。");
            return FallbackCraftValue;
        }

        if (!IsMaterialType(itemMaster.itemType))
        {
            return FallbackCraftValue;
        }

        return Mathf.Max(0f, itemMaster.craftValue);
    }

    /// <summary>
    /// 工程スロット実行時にインベントリから素材を1個消費し、品質係数を返します。
    /// 消費対象が無い／素材でない場合は 1.0f を返し、インベントリは変更しません。
    /// </summary>
    /// <param name="craftSlot">統一工程スロット（1〜5）</param>
    /// <param name="craftType">Forge / Alch</param>
    /// <param name="consumedItemId">実際に消費された素材 ID（無ければ null）</param>
    public static float TryConsumeMaterialForCraftSlot(
        int craftSlot,
        string craftType,
        out string consumedItemId)
    {
        consumedItemId = null;

        if (!ShouldSlotConsumeMaterial(craftSlot, craftType))
        {
            return FallbackCraftValue;
        }

        InventoryManager inventory = InventoryManager.Instance;
        if (inventory == null)
        {
            return FallbackCraftValue;
        }

        if (!TryPickConsumableMaterial(inventory, out string itemId))
        {
            return FallbackCraftValue;
        }

        // 消費前にマスター存在と Material 種別を再確認（安全失敗）
        if (!IsRegisteredMaterial(itemId))
        {
            return FallbackCraftValue;
        }

        float craftValue = GetMaterialCraftValue(itemId);

        if (!inventory.TryConsumeItem(itemId, 1))
        {
            return FallbackCraftValue;
        }

        consumedItemId = itemId;
        Debug.Log(
            $"<color=#B39DDB>【工房素材】{itemId} を1個消費 — 品質係数 ×{craftValue:F2}</color>");
        return craftValue;
    }

    /// <summary>itemType が Equipment（大文字小文字無視）かを判定します。</summary>
    public static bool IsEquipmentType(string itemType)
    {
        return string.Equals(itemType, ItemTypeEquipment, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>itemType が Material（大文字小文字無視）かを判定します。</summary>
    public static bool IsMaterialType(string itemType)
    {
        return string.Equals(itemType, ItemTypeMaterial, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>マスターに登録済みかつ Material 種別のアイテムかを返します。</summary>
    public static bool IsRegisteredMaterial(string itemId)
    {
        MasterDataManager masterData = MasterDataManager.Instance;
        if (masterData == null || string.IsNullOrWhiteSpace(itemId))
        {
            return false;
        }

        ItemMasterData itemMaster = masterData.GetItem(itemId);
        return itemMaster != null && itemMaster.IsValid() && IsMaterialType(itemMaster.itemType);
    }

    /// <summary>素材投入が想定される工程スロットかを判定します。</summary>
    public static bool ShouldSlotConsumeMaterial(int craftSlot, string craftType)
    {
        if (craftSlot < 1 || craftSlot > CraftingStatusManager.UnifiedCraftSlotCount)
        {
            return false;
        }

        if (string.Equals(craftType, CraftingStatusManager.CraftTypeForge, StringComparison.OrdinalIgnoreCase))
        {
            // 鍛冶: 2=魔物素材投入, 4=研磨（触媒素材）
            return craftSlot == 2 || craftSlot == 4;
        }

        if (string.Equals(craftType, CraftingStatusManager.CraftTypeAlch, StringComparison.OrdinalIgnoreCase))
        {
            // 調合: 2=丸ごと投入, 3=薬草すり潰し, 5=特殊素材投入
            return craftSlot == 2 || craftSlot == 3 || craftSlot == 5;
        }

        return false;
    }

    /// <summary>インベントリ内からマスター登録済み Material を1件選びます（先頭優先）。</summary>
    private static bool TryPickConsumableMaterial(InventoryManager inventory, out string itemId)
    {
        itemId = null;
        if (inventory?.slots == null)
        {
            return false;
        }

        for (int i = 0; i < inventory.slots.Count; i++)
        {
            InventorySlot slot = inventory.slots[i];
            if (slot?.item == null || slot.count <= 0)
            {
                continue;
            }

            if (!IsRegisteredMaterial(slot.item.id))
            {
                continue;
            }

            itemId = slot.item.id;
            return true;
        }

        return false;
    }
}

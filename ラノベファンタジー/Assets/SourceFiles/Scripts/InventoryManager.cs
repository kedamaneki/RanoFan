using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

// =============================================================================
// 重量制限付きインベントリ / 職人ツール連動 / 超重時アクション制限
// 連携: CraftingExperimentHub / CraftingExceptionCollector / PlayerController
// =============================================================================

/// <summary>重量を持つインベントリアイテムの定義。</summary>
[Serializable]
public class ItemData
{
    /// <summary>一意 ID（HeavyAnvil 等）。</summary>
    public string id;

    /// <summary>表示名。</summary>
    public string itemName;

    /// <summary>1個あたりの重量（kg）。</summary>
    public float weight;

    /// <summary>1スロットの最大スタック数。</summary>
    public int maxStack;

    /// <summary>マスター itemType（Material / Consumable / Equipment）。</summary>
    public string itemType = string.Empty;

    /// <summary>工房セッションから焼き込んだ純度。hasCraftHiddenParams が false なら未指定。</summary>
    public float purity;

    /// <summary>工房セッションから焼き込んだ密度。hasCraftHiddenParams が false なら未指定。</summary>
    public float density;

    /// <summary>Purity / Density が成果物として有効に焼き込まれているか。</summary>
    public bool hasCraftHiddenParams;

    /// <summary>装備武器として戦闘還元の対象か。</summary>
    public bool IsEquipmentWeapon
    {
        get
        {
            if (string.Equals(itemType, "Equipment", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return !string.IsNullOrWhiteSpace(id) &&
                   id.StartsWith("WEAPON_", StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>焼き込み済みの裏パラメータを取り出します。欠損時は false。</summary>
    public bool TryGetCraftHiddenParams(out float purityValue, out float densityValue)
    {
        if (!hasCraftHiddenParams || float.IsNaN(purity) || float.IsNaN(density) ||
            float.IsInfinity(purity) || float.IsInfinity(density))
        {
            purityValue = 0f;
            densityValue = 0f;
            return false;
        }

        purityValue = purity;
        densityValue = density;
        return true;
    }

    /// <summary>工房セッションの Purity / Density を成果物へ焼き込みます。</summary>
    public void BindCraftHiddenParams(float purityValue, float densityValue)
    {
        if (float.IsNaN(purityValue) || float.IsNaN(densityValue) ||
            float.IsInfinity(purityValue) || float.IsInfinity(densityValue))
        {
            hasCraftHiddenParams = false;
            purity = 0f;
            density = 0f;
            return;
        }

        purity = Mathf.Max(0f, purityValue);
        density = Mathf.Max(0f, densityValue);
        hasCraftHiddenParams = true;
    }

    /// <summary>有効な定義かどうか。</summary>
    public bool IsValid()
    {
        return !string.IsNullOrWhiteSpace(id) &&
               !string.IsNullOrWhiteSpace(itemName) &&
               weight >= 0f &&
               maxStack > 0;
    }

    /// <summary>指定個数分の総重量。</summary>
    public float GetStackWeight(int count)
    {
        return weight * Mathf.Max(0, count);
    }
}

/// <summary>インベントリ内の1スロット（アイテム＋スタック数）。</summary>
[Serializable]
public class InventorySlot
{
    /// <summary>格納アイテム定義。</summary>
    public ItemData item;

    /// <summary>スタック数。</summary>
    public int count;

    public InventorySlot(ItemData item, int count)
    {
        this.item = item;
        this.count = count;
    }
}

/// <summary>ゲーム内で使う既定アイテム ID。</summary>
public static class InventoryItemIds
{
    /// <summary>携帯用魔力炉（鍛冶 hasHeavyTool 連動）。</summary>
    public const string HeavyAnvil = "HeavyAnvil";

    /// <summary>例外行動テスト用触媒。</summary>
    public const string CharmPowder = "charm_powder";

    /// <summary>即席クラフト素材：グリフォンの骨。</summary>
    public const string GryphonBone = InstantCraftMaterialIds.GryphonBone;

    /// <summary>即席クラフト素材：鉄鉱石。</summary>
    public const string IronOre = InstantCraftMaterialIds.IronOre;
}

/// <summary>デバッグ・例外ルート用の既定 ItemData ファクトリ。</summary>
public static class InventoryItemCatalog
{
    /// <summary>携帯用魔力炉（35kg）。</summary>
    public static ItemData CreatePortableManaFurnace()
    {
        return new ItemData
        {
            id = InventoryItemIds.HeavyAnvil,
            itemName = "携帯用魔力炉",
            weight = 35f,
            maxStack = 1
        };
    }

    /// <summary>魅了の粉末（0.1kg）。</summary>
    public static ItemData CreateCharmPowder()
    {
        return new ItemData
        {
            id = InventoryItemIds.CharmPowder,
            itemName = "魅了の粉末",
            weight = 0.1f,
            maxStack = 99
        };
    }

    /// <summary>例外ルートの歪な廃棄物。</summary>
    public static ItemData CreateWarpedScrap()
    {
        return new ItemData
        {
            id = CraftingExceptionCollector.LootWarpedScrapId,
            itemName = "歪な廃棄物",
            weight = 3.5f,
            maxStack = 99
        };
    }

    /// <summary>例外ルートの未知の黒色物質。</summary>
    public static ItemData CreateUnknownBlackMatter()
    {
        return new ItemData
        {
            id = CraftingExceptionCollector.LootUnknownBlackMatterId,
            itemName = "未知の黒色物質",
            weight = 2.5f,
            maxStack = 99
        };
    }

    /// <summary>グリフォンの骨（即席クラフト素材・0.6kg）。</summary>
    public static ItemData CreateGryphonBone()
    {
        return new ItemData
        {
            id = InventoryItemIds.GryphonBone,
            itemName = "グリフォンの骨",
            weight = 0.6f,
            maxStack = 20
        };
    }

    /// <summary>鉄鉱石（即席クラフト素材・1.2kg）。</summary>
    public static ItemData CreateIronOre()
    {
        return new ItemData
        {
            id = InventoryItemIds.IronOre,
            itemName = "鉄鉱石",
            weight = 1.2f,
            maxStack = 30
        };
    }

    /// <summary>例外ルート成果物から ItemData を生成します。</summary>
    public static ItemData FromExceptionLoot(CraftExceptionLootItem loot)
    {
        if (loot == null)
        {
            return null;
        }

        if (string.Equals(loot.itemId, CraftingExceptionCollector.LootUnknownBlackMatterId, StringComparison.Ordinal))
        {
            return CreateUnknownBlackMatter();
        }

        return CreateWarpedScrap();
    }
}

/// <summary>
/// 重量制限付きインベントリを管理し、職人ツール所持と超重デバフへ配線します。
/// </summary>
public class InventoryManager : MonoBehaviour
{
    public const float DefaultMaxWeightCapacity = 50f;

    public static InventoryManager Instance { get; private set; }

    [Header("重量制限")]
    public float maxWeightCapacity = DefaultMaxWeightCapacity;

    public List<InventorySlot> slots { get; private set; } = new List<InventorySlot>();
    private bool overweightWarningEmitted;
    private ItemData equippedCraftedWeapon;

    /// <summary>最大所持重量（kg）。</summary>
    public float MaxWeightCapacity => maxWeightCapacity;

    /// <summary>現在の総重量（kg）。</summary>
    public float currentTotalWeight { get; private set; }

    /// <summary>ローリング回避が重量超過で封じられているか。</summary>
    public bool IsRollingDodgeLocked => IsOverweight();

    /// <summary>現在の重量負荷率（1.0 = 上限ちょうど、1.0超 = 重量超過）。</summary>
    public float WeightLoadRatio => currentTotalWeight / Mathf.Max(0.01f, maxWeightCapacity);

    /// <summary>重量超過時に適用する移動速度の下限倍率。</summary>
    public const float MinOverweightMoveSpeedMultiplier = 0.4f;

    /// <summary>上限ギリギリ（100%）のときの移動速度倍率。</summary>
    public const float EncumberedMoveSpeedAtCapacity = 0.75f;

    /// <summary>工房成果物として装備中の武器（未装備時は null）。</summary>
    public ItemData EquippedCraftedWeapon => equippedCraftedWeapon;

    /// <summary>成果物武器を戦闘還元スロットへ装備します。無効なら安全に外します。</summary>
    public bool EquipCraftedWeapon(ItemData item)
    {
        if (item == null || !item.IsValid() || !item.IsEquipmentWeapon)
        {
            UnequipCraftedWeapon();
            return false;
        }

        equippedCraftedWeapon = CloneItemDefinition(item);
        Debug.Log(
            $"<color=#80CBC4>【装備】成果物武器 {equippedCraftedWeapon.itemName}（{equippedCraftedWeapon.id}）を装備しました。</color>");
        return true;
    }

    /// <summary>成果物武器の装備を外します。</summary>
    public void UnequipCraftedWeapon()
    {
        equippedCraftedWeapon = null;
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[InventoryManager] 重複インスタンスを検出しました。");
            return;
        }

        Instance = this;
        RecalculateTotalWeight();
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    /// <summary>シーンに無い場合は動的生成して返します。</summary>
    public static InventoryManager EnsureInstance()
    {
        if (Instance != null)
        {
            return Instance;
        }

        GameObject player = GameObject.Find("PlayerRobot");
        if (player != null)
        {
            InventoryManager onPlayer = player.GetComponent<InventoryManager>();
            if (onPlayer != null)
            {
                return onPlayer;
            }

            return player.AddComponent<InventoryManager>();
        }

        GameObject hub = GameObject.Find("DebugSystemsHub");
        if (hub != null)
        {
            InventoryManager onHub = hub.GetComponent<InventoryManager>();
            return onHub != null ? onHub : hub.AddComponent<InventoryManager>();
        }

        return new GameObject(nameof(InventoryManager)).AddComponent<InventoryManager>();
    }

    /// <summary>アイテムをインベントリへ追加し、総重量と職人ツールフラグを同期します。</summary>
    public bool AddItem(ItemData item, int amount)
    {
        if (item == null || !item.IsValid() || amount <= 0)
        {
            Debug.LogWarning("[InventoryManager] 無効な AddItem 呼び出しです。");
            return false;
        }

        int remaining = amount;
        for (int i = 0; i < slots.Count && remaining > 0; i++)
        {
            InventorySlot slot = slots[i];
            if (slot.item == null || !string.Equals(slot.item.id, item.id, StringComparison.Ordinal))
            {
                continue;
            }

            int canAdd = Mathf.Max(0, slot.item.maxStack - slot.count);
            int addCount = Mathf.Min(canAdd, remaining);
            if (addCount <= 0)
            {
                continue;
            }

            slot.count += addCount;
            remaining -= addCount;
        }

        while (remaining > 0)
        {
            int stackCount = Mathf.Min(item.maxStack, remaining);
            slots.Add(new InventorySlot(CloneItemDefinition(item), stackCount));
            remaining -= stackCount;
        }

        RecalculateTotalWeight();
        SyncCraftingHeavyToolFlag();
        return true;
    }

    /// <summary>被弾バースト時にインベントリ内の全アイテムを消滅させます。</summary>
    public void RemoveAllItemsOnBurst()
    {
        if (slots.Count == 0)
        {
            return;
        }

        int lostKinds = slots.Count;
        slots.Clear();
        UnequipCraftedWeapon();
        RecalculateTotalWeight();
        SyncCraftingHeavyToolFlag();

        Debug.Log(
            $"<color=#FF4444><b>[InventoryManager] バースト全ロス！</b> " +
            $"所持アイテム {lostKinds} 種類が消滅しました（{currentTotalWeight:F1}/{maxWeightCapacity:F1} kg）</color>");
    }

    /// <summary>総重量が上限を超えているか。</summary>
    public bool IsOverweight()
    {
        return currentTotalWeight > maxWeightCapacity;
    }

    /// <summary>
    /// 重量負荷率に応じた移動速度倍率を返します。
    /// 100%未満は 1.0→0.75 へ線形低下、超過時は最低 40% まで落ちます。
    /// </summary>
    public float GetMovementSpeedMultiplier()
    {
        float ratio = WeightLoadRatio;
        if (ratio <= 1f)
        {
            return Mathf.Lerp(1f, EncumberedMoveSpeedAtCapacity, ratio);
        }

        float overload = ratio - 1f;
        float drop = EncumberedMoveSpeedAtCapacity - MinOverweightMoveSpeedMultiplier;
        return Mathf.Max(MinOverweightMoveSpeedMultiplier, EncumberedMoveSpeedAtCapacity - overload * drop);
    }

    /// <summary>ゼンゼロ風 Rich Text のインベントリ表示文を生成します。</summary>
    public string BuildZenlessInventoryDisplayLog()
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("<color=#00D4FF><b>【インベントリ】</b></color> " +
                        $"<color=#7DF9FF>{currentTotalWeight:F1} / {maxWeightCapacity:F1} kg</color>");

        if (IsOverweight())
        {
            sb.AppendLine(
                "<color=#FF3333><b>【重量超過警告】装備や素材が重すぎてステップ回避が実行できない！</b></color> " +
                $"<color=#FF8A80>移動速度 {GetMovementSpeedMultiplier():P0}</color>");
        }

        if (slots.Count == 0)
        {
            sb.AppendLine("<color=#AAAAAA>  （空）</color>");
            return sb.ToString();
        }

        for (int i = 0; i < slots.Count; i++)
        {
            InventorySlot slot = slots[i];
            if (slot?.item == null || slot.count <= 0)
            {
                continue;
            }

            float lineWeight = slot.item.GetStackWeight(slot.count);
            sb.AppendLine(
                $"<color=#B388FF>  - {slot.item.itemName}</color> " +
                $"<color=#FFFFFF>x{slot.count}</color> " +
                $"<color=#4DE8FF>({lineWeight:F1} kg)</color>");
        }

        return sb.ToString();
    }

    /// <summary>即席クラフト用素材を優先順に1個消費します。</summary>
    /// <param name="consumedMaterialId">実際に消費された素材 ID</param>
    /// <returns>いずれかの素材を消費できた場合 true</returns>
    public bool TryConsumeInstantCraftMaterial(out string consumedMaterialId)
    {
        for (int i = 0; i < InstantCraftWeaponRegistry.MaterialConsumePriority.Length; i++)
        {
            string candidateId = InstantCraftWeaponRegistry.MaterialConsumePriority[i];
            if (TryConsumeItem(candidateId, 1))
            {
                consumedMaterialId = candidateId;
                return true;
            }
        }

        consumedMaterialId = null;
        return false;
    }

    /// <summary>即席クラフトに使える素材を1つ以上所持しているか。</summary>
    public bool ContainsAnyInstantCraftMaterial()
    {
        for (int i = 0; i < InstantCraftWeaponRegistry.MaterialConsumePriority.Length; i++)
        {
            if (ContainsItem(InstantCraftWeaponRegistry.MaterialConsumePriority[i]))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>指定 ID のアイテムを指定個数消費します。</summary>
    /// <param name="itemId">消費対象 ID</param>
    /// <param name="amount">消費個数</param>
    /// <returns>十分な所持があり全量消費できた場合 true</returns>
    public bool TryConsumeItem(string itemId, int amount)
    {
        if (string.IsNullOrWhiteSpace(itemId) || amount <= 0)
        {
            return false;
        }

        int owned = CountItem(itemId);
        if (owned < amount)
        {
            return false;
        }

        int remaining = amount;
        for (int i = slots.Count - 1; i >= 0 && remaining > 0; i--)
        {
            InventorySlot slot = slots[i];
            if (slot?.item == null ||
                slot.count <= 0 ||
                !string.Equals(slot.item.id, itemId, StringComparison.Ordinal))
            {
                continue;
            }

            int take = Mathf.Min(slot.count, remaining);
            slot.count -= take;
            remaining -= take;

            if (slot.count <= 0)
            {
                slots.RemoveAt(i);
            }
        }

        if (remaining > 0)
        {
            return false;
        }

        RecalculateTotalWeight();
        SyncCraftingHeavyToolFlag();
        return true;
    }

    /// <summary>指定 ID の所持個数を返します。</summary>
    public int CountItem(string itemId)
    {
        if (string.IsNullOrWhiteSpace(itemId))
        {
            return 0;
        }

        int total = 0;
        for (int i = 0; i < slots.Count; i++)
        {
            InventorySlot slot = slots[i];
            if (slot?.item != null &&
                slot.count > 0 &&
                string.Equals(slot.item.id, itemId, StringComparison.Ordinal))
            {
                total += slot.count;
            }
        }

        return total;
    }

    /// <summary>指定 ID を1個以上所持しているか。</summary>
    public bool ContainsItem(string itemId)
    {
        if (string.IsNullOrWhiteSpace(itemId))
        {
            return false;
        }

        for (int i = 0; i < slots.Count; i++)
        {
            InventorySlot slot = slots[i];
            if (slot?.item != null &&
                slot.count > 0 &&
                string.Equals(slot.item.id, itemId, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private void RecalculateTotalWeight()
    {
        float total = 0f;
        for (int i = 0; i < slots.Count; i++)
        {
            InventorySlot slot = slots[i];
            if (slot?.item == null || slot.count <= 0)
            {
                continue;
            }

            total += slot.item.GetStackWeight(slot.count);
        }

        bool wasOverweight = currentTotalWeight > maxWeightCapacity;
        currentTotalWeight = total;
        bool isOverweight = IsOverweight();

        if (isOverweight && !wasOverweight)
        {
            EmitOverweightWarning();
        }
        else if (!isOverweight)
        {
            overweightWarningEmitted = false;
        }
    }

    private void EmitOverweightWarning()
    {
        if (overweightWarningEmitted)
        {
            return;
        }

        overweightWarningEmitted = true;
        Debug.Log(
            "<color=#FF1744><b>【警告】重量制限超過！ローリング回避が封じられた！</b></color> " +
            $"<color=#FF8A80>({currentTotalWeight:F1}/{maxWeightCapacity:F1} kg)</color>");
    }

    private void SyncCraftingHeavyToolFlag()
    {
        bool hasHeavyTool = ContainsItem(InventoryItemIds.HeavyAnvil);
        CraftingExperimentHub hub = CraftingExperimentHub.Instance;
        if (hub == null)
        {
            return;
        }

        hub.SetHeavyToolFromInventory(hasHeavyTool);
    }

    private static ItemData CloneItemDefinition(ItemData source)
    {
        return new ItemData
        {
            id = source.id,
            itemName = source.itemName,
            weight = source.weight,
            maxStack = source.maxStack,
            itemType = source.itemType ?? string.Empty,
            purity = source.purity,
            density = source.density,
            hasCraftHiddenParams = source.hasCraftHiddenParams
        };
    }
}

/// <summary>Play 開始時に Inventory 系コンポーネントを配置します。</summary>
public static class InventoryBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AttachInventoryManager()
    {
        InventoryManager.EnsureInstance();
    }
}

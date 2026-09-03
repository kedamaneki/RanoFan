using System;
using UnityEngine;

// =============================================================================
// EnemyMasterData.dropItems -> InventoryManager 安全配線
// 存在しない itemId（AI ハルシネーション）はインベントリ追加をスキップして Safe-Fail
// =============================================================================

/// <summary>
/// 戦闘撃破時に敵マスターのドロップテーブルを確率判定し、報酬をインベントリへ配布します。
/// </summary>
public static class EnemyDropExecutor
{
    /// <summary>
    /// 指定敵 ID のドロップテーブルを処理します。BattlePhase 撃破確定時に呼び出してください。
    /// </summary>
    /// <param name="enemyId">EnemyMasterData.id（例: ENEMY_FOREST_SLIME）</param>
    public static void ProcessEnemyDeathRewards(string enemyId)
    {
        if (string.IsNullOrWhiteSpace(enemyId))
        {
            Debug.LogWarning("[EnemyDropExecutor] enemyId が空のためドロップ処理をスキップします。");
            return;
        }

        MasterDataManager masterData = MasterDataManager.Instance;
        if (masterData == null)
        {
            Debug.LogError("[EnemyDropExecutor] MasterDataManager が未初期化です。ドロップを中止します。");
            return;
        }

        EnemyMasterData enemy = masterData.GetEnemy(enemyId);
        if (enemy == null)
        {
            Debug.LogError(
                $"[EnemyDropExecutor] 未登録の敵 ID '{enemyId}'。ドロップテーブルを解決できません。");
            LogCombatDropWarning($"未知の敵 ID '{enemyId}' — ドロップ報酬は付与されませんでした。");
            return;
        }

        if (enemy.dropItems == null || enemy.dropItems.Count == 0)
        {
            Debug.Log($"[EnemyDropExecutor] {enemy.name}（{enemyId}）にドロップ定義がありません。");
            return;
        }

        InventoryManager inventory = InventoryManager.EnsureInstance();
        if (inventory == null)
        {
            Debug.LogError("[EnemyDropExecutor] InventoryManager を解決できません。ドロップを中止します。");
            return;
        }

        int grantedCount = 0;
        for (int i = 0; i < enemy.dropItems.Count; i++)
        {
            DropItemData drop = enemy.dropItems[i];
            if (drop == null || !drop.IsValid())
            {
                Debug.LogWarning(
                    $"[EnemyDropExecutor] {enemyId} drop[{i}] が無効です。スキップします。");
                continue;
            }

            if (UnityEngine.Random.value > drop.dropChance)
            {
                continue;
            }

            if (!TryGrantDropItem(masterData, inventory, enemy, drop, out string grantLabel))
            {
                continue;
            }

            grantedCount++;
            Debug.Log(
                $"<color=#A5D6A7><b>【ドロップ獲得】{enemy.name} から {grantLabel} を入手！</b></color>");
        }

        if (grantedCount > 0)
        {
            Debug.Log(inventory.BuildZenlessInventoryDisplayLog());
        }
    }

    /// <summary>表示名から EnemyMasterData.id を逆引きします（マスター ID 未設定時のフォールバック）。</summary>
    public static string TryResolveEnemyIdByName(string enemyName)
    {
        if (string.IsNullOrWhiteSpace(enemyName))
        {
            return null;
        }

        MasterDataManager masterData = MasterDataManager.Instance;
        if (masterData == null)
        {
            return null;
        }

        foreach (EnemyMasterData candidate in masterData.EnemyRegistry.Values)
        {
            if (candidate != null &&
                string.Equals(candidate.name, enemyName, StringComparison.OrdinalIgnoreCase))
            {
                return candidate.id;
            }
        }

        return null;
    }

    /// <summary>
    /// ItemMasterData の存在を検証し、ToItemData() で ItemData へ変換してからインベントリへ追加します。
    /// </summary>
    private static bool TryGrantDropItem(
        MasterDataManager masterData,
        InventoryManager inventory,
        EnemyMasterData enemy,
        DropItemData drop,
        out string grantLabel)
    {
        grantLabel = drop.itemId;

        // ① マスター存在チェック（ハルシネーション ID はここで遮断）
        ItemMasterData itemMaster = masterData.GetItem(drop.itemId);
        if (itemMaster == null || !itemMaster.IsValid())
        {
            Debug.LogError(
                $"[EnemyDropExecutor] 存在しない itemId '{drop.itemId}' が " +
                $"敵 '{enemy.id}' のドロップテーブルに含まれています。インベントリ追加をスキップします。");
            LogCombatDropWarning(
                $"ドロップテーブル不正: itemId '{drop.itemId}' はマスター未登録のためスキップしました。");
            return false;
        }

        // ② JSON マスター → ランタイム ItemData へ型安全変換
        ItemData runtimeItem = itemMaster.ToItemData();
        if (runtimeItem == null || !runtimeItem.IsValid())
        {
            Debug.LogError(
                $"[EnemyDropExecutor] itemId '{drop.itemId}' の ToItemData() 結果が無効です。スキップします。");
            return false;
        }

        // ③ InventoryManager へプッシュ（重量・スタック上限は ItemData 側で管理）
        if (!inventory.AddItem(runtimeItem, 1))
        {
            Debug.LogWarning(
                $"[EnemyDropExecutor] AddItem 失敗: {runtimeItem.itemName}（{runtimeItem.id}）");
            return false;
        }

        grantLabel = $"{runtimeItem.itemName}（{runtimeItem.id}）";
        return true;
    }

    /// <summary>
    /// itemId 単体をドロップ経路へ流します。未登録 ID はハルシネーション防水で false（追加なし）。
    /// </summary>
    public static bool TryProcessStandaloneItemId(string itemId)
    {
        if (string.IsNullOrWhiteSpace(itemId))
        {
            Debug.LogWarning("[EnemyDropExecutor] itemId が空のためドロップ処理をスキップします。");
            return false;
        }

        MasterDataManager masterData = MasterDataManager.Instance ?? MasterDataManager.EnsureInstance();
        if (masterData == null)
        {
            Debug.LogError("[EnemyDropExecutor] MasterDataManager が未初期化です。ドロップを中止します。");
            return false;
        }

        InventoryManager inventory = InventoryManager.EnsureInstance();
        if (inventory == null)
        {
            Debug.LogError("[EnemyDropExecutor] InventoryManager を解決できません。ドロップを中止します。");
            return false;
        }

        EnemyMasterData probeEnemy = new EnemyMasterData
        {
            id = "PROBE_STANDALONE_DROP",
            name = "StandaloneDropProbe"
        };
        DropItemData drop = new DropItemData
        {
            itemId = itemId.Trim(),
            dropChance = 1f
        };
        return TryGrantDropItem(masterData, inventory, probeEnemy, drop, out _);
    }

    private static void LogCombatDropWarning(string message)
    {
        CombatActionFeedbackManager combat = CombatActionFeedbackManager.Instance;
        if (combat != null)
        {
            combat.LogDropTableWarning(message);
            return;
        }

        Debug.LogWarning($"[EnemyDropExecutor] {message}");
    }
}

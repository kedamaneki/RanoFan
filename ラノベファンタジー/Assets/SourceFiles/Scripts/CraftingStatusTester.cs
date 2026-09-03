using UnityEngine;

// =============================================================================
// CraftingStatusManager デバッグ検証 — 統一スロット1〜5への配管
// =============================================================================

/// <summary>
/// こだわり工程操作を CraftingStatusManager の統一スロット API へ委譲するテスター。
/// 入力検知は DemoTimeLineManager の中央 Update が担当します。
/// </summary>
public class CraftingStatusTester : MonoBehaviour
{
    [Header("参照")]
    [SerializeField] private CraftingStatusManager statusManager;
    [SerializeField] private DetailedCraftingProcessManager processManager;

    private void Awake()
    {
        statusManager ??= CraftingStatusManager.EnsureInstance();
        processManager ??= DetailedCraftingProcessManager.EnsureInstance();
    }

    /// <summary>鍛冶スロット1 — インゴット精製。</summary>
    public void ExecuteForgeRefineIngot() => ExecuteUnifiedSlot(1);

    /// <summary>鍛冶スロット2 — 魔物合金投入。</summary>
    public void ExecuteForgeMonsterAlloy() => ExecuteUnifiedSlot(2);

    /// <summary>鍛冶スロット3 — 大槌打鍛。</summary>
    public void ExecuteForgeHeavyHammer() => ExecuteUnifiedSlot(3);

    /// <summary>鍛冶スロット4 — 研磨。</summary>
    public void ExecuteForgeWhetstonePolish() => ExecuteUnifiedSlot(4);

    /// <summary>鍛冶スロット5 — 魔力行使。</summary>
    public void ExecuteForgeMagicInfusion() => ExecuteUnifiedSlot(5);

    /// <summary>調合スロット1 — 茎除去。</summary>
    public void ExecuteAlchRemoveStem() => ExecuteUnifiedSlot(1);

    /// <summary>調合スロット2 — 丸ごと投入。</summary>
    public void ExecuteAlchWholeIngredient() => ExecuteUnifiedSlot(2);

    /// <summary>調合スロット3 — すり潰し。</summary>
    public void ExecuteAlchGrindHerb() => ExecuteUnifiedSlot(3);

    /// <summary>調合スロット4 — 強火沸騰。</summary>
    public void ExecuteAlchRapidBoil() => ExecuteUnifiedSlot(4);

    /// <summary>調合スロット5 — 冷水特殊素材投入。</summary>
    public void ExecuteAlchColdSpecialMaterial() => ExecuteUnifiedSlot(5);

    /// <summary>統一スロット1〜5を現在の職種に応じて実行します。</summary>
    /// <param name="slot1To5">Alpha1=1 〜 Alpha5=5</param>
    public void ExecuteUnifiedSlot(int slot1To5)
    {
        statusManager ??= CraftingStatusManager.EnsureInstance();
        processManager ??= DetailedCraftingProcessManager.EnsureInstance();
        if (statusManager == null || processManager == null)
        {
            return;
        }

        statusManager.TryExecuteUnifiedCraftSlot(slot1To5, processManager.currentCraftType, processManager);
    }

    /// <summary>現在の裏ステータスをコンソールへスタイリッシュに出力します。</summary>
    /// <param name="craftType">Forge / Alch</param>
    public void LogStyledStatus(string craftType)
    {
        statusManager ??= CraftingStatusManager.EnsureInstance();
        Debug.Log(statusManager.BuildStatusDisplayRichText(craftType));
    }
}

/// <summary>Play 開始時に CraftingStatusTester を DebugSystemsHub へ自動配置します。</summary>
public static class CraftingStatusTesterBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoAttach()
    {
        AttachToDebugSystemsHub();
    }

    /// <summary>DebugSystemsHub に CraftingStatusTester が無ければ追加します。</summary>
    public static void AttachToDebugSystemsHub()
    {
        EnsureTesterOnHub();
    }

    /// <summary>DebugSystemsHub 上の CraftingStatusTester を保証して返します。</summary>
    public static CraftingStatusTester EnsureTesterOnHub()
    {
        GameObject hub = GameObject.Find("DebugSystemsHub");
        if (hub == null)
        {
            Debug.LogWarning("[CraftingStatusTesterBootstrap] DebugSystemsHub が見つかりません。");
            return null;
        }

        CraftingStatusBootstrap.AttachToDebugSystemsHub();

        CraftingStatusTester existing = hub.GetComponent<CraftingStatusTester>();
        if (existing != null)
        {
            return existing;
        }

        CraftingStatusTester created = hub.AddComponent<CraftingStatusTester>();
        Debug.Log(
            "<color=#4DD0E1><b>[CraftingStatusTesterBootstrap]</b> 拡張型生産ステータス検証を配置しました。</color>");
        return created;
    }
}

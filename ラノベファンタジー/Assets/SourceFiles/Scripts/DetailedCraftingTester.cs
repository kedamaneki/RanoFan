using UnityEngine;

// =============================================================================
// 職人のこだわり工程デバッグテスター（DebugSystemsHub 自動配置）
// 入力検知は DetailedCraftingProcessManager が担当します。
// =============================================================================

/// <summary>
/// DetailedCraftingProcessManager の起動ログと参照解決を担う薄いテスター。
/// 1〜5 / Enter のホットキーは DemoTimeLineManager の中央 Update が処理します。
/// </summary>
[DefaultExecutionOrder(850)]
public class DetailedCraftingTester : MonoBehaviour
{
    [Header("参照")]
    [SerializeField] private DetailedCraftingProcessManager processManager;
    [SerializeField] private CraftingExperimentHub craftingHub;
    [SerializeField] private CraftingStatusTester statusTester;

    private void Awake()
    {
        processManager ??= DetailedCraftingProcessManager.EnsureInstance();
        craftingHub ??= CraftingExperimentHub.EnsureInstance();
        statusTester ??= CraftingStatusTesterBootstrap.EnsureTesterOnHub();
    }

    private void Start()
    {
        Debug.Log(
            "<color=#4DD0E1><b>[DetailedCraftingTester]</b> 準備完了</color>\n" +
            "  撃破後の CraftingPhase で <b>1〜5</b> こだわり工程 → <b>Enter</b> でパケット回収（DemoTimeLineManager 統治）\n" +
            "  <color=#FFAB40>鍛冶:</color> 1=精製 2=魔物合金 3=大槌 4=研磨 5=魔力行使\n" +
            "  <color=#CE93D8>調合:</color> 1=茎除去 2=丸投入 3=すり潰し 4=強火沸騰 5=冷水投入\n" +
            "  <color=#80DEEA>各キーで CraftingStatusManager の裏パラメータが変動します。</color>");
    }
}

/// <summary>Play 開始時に DetailedCraftingTester を DebugSystemsHub へ自動配置します。</summary>
public static class DetailedCraftingTesterBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AttachToDebugSystemsHub()
    {
        GameObject hub = GameObject.Find("DebugSystemsHub");
        if (hub == null)
        {
            Debug.LogWarning("[DetailedCraftingTesterBootstrap] DebugSystemsHub が見つかりません。");
            return;
        }

        DetailedCraftingProcessBootstrap.AttachToDebugSystemsHub();
        CraftingStatusTesterBootstrap.AttachToDebugSystemsHub();

        if (hub.GetComponent<DetailedCraftingTester>() != null)
        {
            return;
        }

        hub.AddComponent<DetailedCraftingTester>();
        Debug.Log(
            "<color=#4DD0E1><b>[DetailedCraftingTesterBootstrap]</b> 自動配置完了。</color>");
    }
}

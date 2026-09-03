using UnityEngine;

// =============================================================================
// デモ進行の補助（DebugSystemsHub 自動配置）— 入力は DemoTimeLineManager が統治
// =============================================================================

/// <summary>
/// DemoTimeLineManager の全自動リレーをコンテキストメニューから起動する薄い補助コンポーネント。
/// キー入力検知は行いません（Play 開始で自動 BattlePhase へ遷移）。
/// </summary>
public class DemoSceneTester : MonoBehaviour
{
    [Header("参照")]
    [SerializeField] private DemoTimeLineManager demoTimeline;

    private void Awake()
    {
        demoTimeline ??= DemoTimeLineManager.EnsureInstance();
    }

    private void Start()
    {
        Debug.Log(
            "<color=#81D4FA><b>[DemoSceneTester]</b> 準備完了</color>\n" +
            "  Play 開始で自動 BattlePhase → 撃破後 CraftingPhase → Enter で ResultPhase\n" +
            "  全自動リレーは ContextMenu または DemoTimeLineManager.StartFullAutoDemoRelay() から起動");
    }

    /// <summary>全自動デモリレー（戦闘→工房→例外パケット）を手動起動します。</summary>
    [ContextMenu("Start Full Auto Demo Relay")]
    public void StartFullAutoDemoRelayFromMenu()
    {
        demoTimeline ??= DemoTimeLineManager.EnsureInstance();
        if (demoTimeline == null)
        {
            return;
        }

        if (demoTimeline.IsAutoRelayActive)
        {
            Debug.Log(
                "<color=#FFD54F>[DemoSceneTester] デモリレーは既に進行中です。</color>");
            return;
        }

        Debug.Log(
            "<color=#FFF176><b>【DemoSceneTester】全自動デモリレーを起動！</b></color>");
        demoTimeline.StartFullAutoDemoRelay();
    }
}

/// <summary>Play 開始時に DemoSceneTester を DebugSystemsHub へ自動配置します。</summary>
public static class DemoSceneTesterBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AttachToDebugSystemsHub()
    {
        GameObject hub = GameObject.Find("DebugSystemsHub");
        if (hub == null)
        {
            Debug.LogWarning("[DemoSceneTesterBootstrap] DebugSystemsHub が見つかりません。");
            return;
        }

        if (hub.GetComponent<DemoSceneTester>() != null)
        {
            return;
        }

        hub.AddComponent<DemoSceneTester>();
        Debug.Log(
            "<color=#81D4FA><b>[DemoSceneTesterBootstrap]</b> 自動配置完了。入力は DemoTimeLineManager が統治します。</color>");
    }
}

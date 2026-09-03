using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// CraftingExperimentHub のデバッグテスター。DebugSystemsHub に自動配置されます。
/// C=鍛冶(街) / P=調合(街) / /=屋外 / K・L・,=介入 / .=設備フラグ
/// Y=例外ルート強制 / ;=被弾
/// </summary>
public class CraftingExperimentTester : MonoBehaviour
{
    [Header("参照")]
    [SerializeField] private CraftingExperimentHub craftingHub;
    [SerializeField] private PlayerStatusManager statusManager;

    [Header("屋外開始時の職種（I キー）")]
    [SerializeField] private string outdoorCraftType = "Forge";

    [Header("例外ルート検証用触媒（Y キー）")]
    [SerializeField] private List<string> debugExceptionCatalysts = new List<string>
    {
        "test_void_catalyst_alpha",
        "test_unstable_ether_residue"
    };

    [Header("ホットキー")]
    [SerializeField] private Key forgeTownToggleKey = Key.C;
    [SerializeField] private Key alchTownToggleKey = Key.P;
    [SerializeField] private Key outdoorToggleKey = Key.Slash;
    [SerializeField] private Key action1Key = Key.LeftBracket;
    [SerializeField] private Key action2Key = Key.L;
    [SerializeField] private Key action3Key = Key.Comma;
    [SerializeField] private Key upgradeFlagsKey = Key.Period;
    [SerializeField] private Key forceUnknownCraftKey = Key.Y;
    [SerializeField] private Key enemyInterruptKey = Key.Semicolon;

    private string lastStartedCraftType = "Forge";

    private void Awake()
    {
        craftingHub ??= CraftingExperimentHub.EnsureInstance();
        statusManager ??= PlayerStatusManager.Instance ?? FindAnyObjectByType<PlayerStatusManager>();
    }

    private void Start()
    {
        Debug.Log(
            "<color=#87CEEB><b>[CraftingExperimentTester]</b> 準備完了\n" +
            "  C=鍛冶(街) P=調合(街) /=屋外トグル終了\n" +
            "  \\=町の中判定切替（屋外だと C/P も携帯キット制限）\n" +
            "  [=介入① L=小槌 ,=高度干渉 .=設備フラグ Y=例外ルート強制 ;=被弾\n" +
            "  Q=戦歴ギャラリー（画面右） / 生産UIは完全ブラインド</color>");
    }

    private void Update()
    {
        craftingHub ??= CraftingExperimentHub.EnsureInstance();

        if (DebugHotkeyUtility.WasPressed(forgeTownToggleKey))
        {
            ToggleSession("Forge", isSafetyZone: true, "C");
            return;
        }

        if (DebugHotkeyUtility.WasPressed(alchTownToggleKey))
        {
            ToggleSession("Alch", isSafetyZone: true, "P");
            return;
        }

        if (DebugHotkeyUtility.WasPressed(outdoorToggleKey))
        {
            ToggleSession(outdoorCraftType, isSafetyZone: false, "I");
            return;
        }

        if (DebugHotkeyUtility.WasPressed(upgradeFlagsKey))
        {
            craftingHub.ToggleDebugUpgradeFlags();
            return;
        }

        if (DebugHotkeyUtility.WasPressed(forceUnknownCraftKey))
        {
            ForceTriggerUnknownCraft();
            return;
        }

        if (!craftingHub.IsActive)
        {
            return;
        }

        if (DebugHotkeyUtility.WasPressed(action1Key))
        {
            craftingHub.ExecuteCraftAction(1);
        }

        if (DebugHotkeyUtility.WasPressed(action2Key))
        {
            craftingHub.ExecuteCraftAction(2);
        }

        if (DebugHotkeyUtility.WasPressed(action3Key))
        {
            craftingHub.ExecuteCraftAction(3);
        }

        if (DebugHotkeyUtility.WasPressed(enemyInterruptKey))
        {
            SimulateEnemyInterruption();
        }
    }

    /// <summary>指定モードで生産を開始、または既存セッションを終了します。</summary>
    private void ToggleSession(string craftType, bool isSafetyZone, string keyLabel)
    {
        if (craftingHub.IsActive)
        {
            FinishCraftingSession();
            return;
        }

        lastStartedCraftType = craftType;
        outdoorCraftType = craftType;
        craftingHub.StartCrafting(craftType, isSafetyZone);

        string location = isSafetyZone ? "街の工房" : "屋外（携帯キット）";
        Debug.Log(
            $"<color=#87CEEB><b>[CraftingExperimentTester]</b> {keyLabel} キー: " +
            $"【{craftType}】{location} で生産開始</color>");
        Debug.Log(craftingHub.FormatParameterStatusLog());
    }

    [ContextMenu("Finish Crafting Session")]
    public void FinishCraftingSession()
    {
        CraftFinishResult result = craftingHub.FinishCrafting(statusManager, Debug.Log);
        if (!result.Succeeded)
        {
            Debug.LogWarning($"[CraftingExperimentTester] 終了失敗: {result.Message}");
        }
    }

    [ContextMenu("Simulate Enemy Interruption (;)")]
    public void SimulateEnemyInterruption()
    {
        if (!craftingHub.IsActive)
        {
            Debug.LogWarning("[CraftingExperimentTester] 生産未開始のため被弾シミュレート不可。");
            return;
        }

        CombatActionFeedbackManager combat = CombatActionFeedbackManager.EnsureInstance();
        if (combat.InterruptByDamage())
        {
            return;
        }

        if (craftingHub.IsSafetyZone)
        {
            Debug.Log(
                "<color=#FFD54F>[CraftingExperimentTester] 街モード：被弾しても継続。/ キーで屋外開始してから ; を試してください。</color>");
        }
        else if (craftingHub.SessionRequestedTownSafety)
        {
            Debug.Log(
                "<color=#FFD54F>[CraftingExperimentTester] 街モード要求中ですが屋外判定です。\\ キーで町の中に入るか、/ キーで屋外生産を開始してください。</color>");
        }

        craftingHub.InterruptByDamage();
    }

    /// <summary>Y キー: 未知ルート状態を強制してクラフト終了し、例外パケット化を検証します。</summary>
    [ContextMenu("Force Trigger Unknown Craft (Y)")]
    public void ForceTriggerUnknownCraft()
    {
        if (!craftingHub.IsActive)
        {
            craftingHub.StartCrafting(lastStartedCraftType, isSafetyZone: true);
            Debug.Log(
                "<color=#FF7043>[CraftingExperimentTester] 生産未開始のため Forge(街) を自動開始しました。</color>");
        }

        if (!craftingHub.ApplyDebugExceptionCraftState(debugExceptionCatalysts))
        {
            Debug.LogWarning("[CraftingExperimentTester] 例外状態の適用に失敗しました。");
            return;
        }

        Debug.Log(
            "<color=#FF5722><b>[CraftingExperimentTester] Yキー: 未知ルート強制 → FinishCrafting へ</b></color>");
        FinishCraftingSession();
    }
}

/// <summary>Play 開始時に DebugSystemsHub へテスターを自動配置します。</summary>
public static class CraftingExperimentBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AttachToDebugSystemsHub()
    {
        GameObject hub = GameObject.Find("DebugSystemsHub");
        if (hub == null)
        {
            Debug.LogWarning("[CraftingExperimentBootstrap] DebugSystemsHub が見つかりません。");
            return;
        }

        if (hub.GetComponent<CraftingExperimentHub>() == null)
        {
            hub.AddComponent<CraftingExperimentHub>();
        }

        if (hub.GetComponent<CraftingExperimentTester>() != null)
        {
            return;
        }

        hub.AddComponent<CraftingExperimentTester>();
        CraftingExceptionBootstrap.AttachCollectorIfNeeded(hub);
        Debug.Log(
            "<color=#87CEEB><b>[CraftingExperimentBootstrap]</b> 自動配置完了。C / P / / キーで生産実験を開始できます。</color>");
    }
}

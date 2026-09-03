using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// InventoryManager のデバッグテスター。DebugSystemsHub に自動配置されます。
/// R キー=表示（= / Shift+- / テンキー= も可） / J=携帯用魔力炉 / K=魅了の粉末
/// U=グリフォンの骨 / I=鉄鉱石（WASD 移動と非競合）
/// </summary>
public class InventoryTester : MonoBehaviour
{
    [Header("参照")]
    [SerializeField] private InventoryManager inventoryManager;

    [Header("ホットキー")]
    [SerializeField] private Key toggleDisplayKey = Key.R;
    [SerializeField] private Key addHeavyToolKey = Key.J;
    [SerializeField] private Key addCatalystKey = Key.K;
    [SerializeField] private Key addGryphonBoneKey = Key.U;
    [SerializeField] private Key addIronOreKey = Key.I;

    [Header("追加量")]
    [SerializeField] private int charmPowderStackAmount = 5;
    [SerializeField] private int instantCraftMaterialStackAmount = 2;

    private void Reset()
    {
        toggleDisplayKey = Key.R;
        addHeavyToolKey = Key.J;
        addCatalystKey = Key.K;
        addGryphonBoneKey = Key.U;
        addIronOreKey = Key.I;
    }

    private void Awake()
    {
        inventoryManager ??= InventoryManager.EnsureInstance();
    }

    private void Start()
    {
        Debug.Log(
            "<color=#7DF9FF><b>[InventoryTester]</b> 準備完了\n" +
            "  R=インベントリ表示（JIS: Shift+- または テンキー= でも可）\n" +
            "  J=携帯用魔力炉(+35kg) / K=魅了の粉末(軽量スタック)\n" +
            "  U=グリフォンの骨 / I=鉄鉱石（即席クラフト用素材）</color>");
    }

    private void Update()
    {
        inventoryManager ??= InventoryManager.EnsureInstance();

        if (WasInventoryDisplayPressed())
        {
            LogInventoryDisplay();
            return;
        }

        if (DebugHotkeyUtility.WasPressed(addHeavyToolKey))
        {
            AddPortableManaFurnace();
            return;
        }

        if (DebugHotkeyUtility.WasPressed(addCatalystKey))
        {
            AddCharmPowderSample();
            return;
        }

        if (DebugHotkeyUtility.WasPressed(addGryphonBoneKey))
        {
            AddGryphonBoneSample();
            return;
        }

        if (DebugHotkeyUtility.WasPressed(addIronOreKey))
        {
            AddIronOreSample();
        }
    }

    /// <summary>インベントリ表示キー（R / = 系）が押されたか。</summary>
    private bool WasInventoryDisplayPressed()
    {
        return DebugHotkeyUtility.WasPressed(toggleDisplayKey) ||
               DebugHotkeyUtility.WasEqualsLikePressed();
    }

    /// <summary>R キー（または = 系）: 所持状況と総重量をコンソールへ表示します。</summary>
    [ContextMenu("Toggle Inventory Display (R)")]
    public void LogInventoryDisplay()
    {
        Debug.Log(inventoryManager.BuildZenlessInventoryDisplayLog());
    }

    /// <summary>J キー: 超重量の携帯用魔力炉を1個追加します。</summary>
    [ContextMenu("Add Heavy Tool (J)")]
    public void AddPortableManaFurnace()
    {
        ItemData furnace = InventoryItemCatalog.CreatePortableManaFurnace();
        inventoryManager.AddItem(furnace, 1);
        Debug.Log(
            $"<color=#FFB74D><b>[InventoryTester]</b> Jキー: {furnace.itemName} を追加 " +
            $"（hasHeavyTool 同期 / 総重量 {inventoryManager.currentTotalWeight:F1} kg）</color>");
        LogInventoryDisplay();
    }

    /// <summary>K キー: 軽量触媒「魅了の粉末」をスタック追加します。</summary>
    [ContextMenu("Add Sample Catalyst (K)")]
    public void AddCharmPowderSample()
    {
        ItemData powder = InventoryItemCatalog.CreateCharmPowder();
        inventoryManager.AddItem(powder, charmPowderStackAmount);
        Debug.Log(
            $"<color=#CE93D8><b>[InventoryTester]</b> Kキー: {powder.itemName} x{charmPowderStackAmount} 追加</color>");
        LogInventoryDisplay();
    }

    /// <summary>U キー: 即席クラフト素材「グリフォンの骨」を追加します。</summary>
    [ContextMenu("Add Gryphon Bone (U)")]
    public void AddGryphonBoneSample()
    {
        ItemData bone = InventoryItemCatalog.CreateGryphonBone();
        inventoryManager.AddItem(bone, instantCraftMaterialStackAmount);
        Debug.Log(
            $"<color=#A5D6A7><b>[InventoryTester]</b> Uキー: {bone.itemName} x{instantCraftMaterialStackAmount} 追加</color>");
        LogInventoryDisplay();
    }

    /// <summary>I キー: 即席クラフト素材「鉄鉱石」を追加します。</summary>
    [ContextMenu("Add Iron Ore (I)")]
    public void AddIronOreSample()
    {
        ItemData ore = InventoryItemCatalog.CreateIronOre();
        inventoryManager.AddItem(ore, instantCraftMaterialStackAmount);
        Debug.Log(
            $"<color=#90CAF9><b>[InventoryTester]</b> Iキー: {ore.itemName} x{instantCraftMaterialStackAmount} 追加</color>");
        LogInventoryDisplay();
    }
}

/// <summary>Play 開始時に InventoryTester を DebugSystemsHub へ配置します。</summary>
public static class InventoryTesterBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AttachToDebugSystemsHub()
    {
        GameObject hub = GameObject.Find("DebugSystemsHub");
        if (hub == null)
        {
            return;
        }

        if (hub.GetComponent<InventoryTester>() == null)
        {
            hub.AddComponent<InventoryTester>();
        }
    }
}

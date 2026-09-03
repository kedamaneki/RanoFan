using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// AI 基礎スキル（BaseSkill）目覚めのデバッグテスター。DebugSystemsHub に配置してください。
/// B キー: Training フェーズ固定 → 戦歴注入 → Gemini 実通信 → スキル枠へ実装着。
/// </summary>
public class BaseSkillAwakeningTester : MonoBehaviour
{
    [Header("参照")]
    [SerializeField] private GamePhaseEventBridge eventBridge;
    [SerializeField] private PlayerSkillSlotManager skillSlotManager;

    [Header("デバッグ戦歴注入（B キー時）")]
    [SerializeField] private int injectThrustHits = 10;
    [SerializeField] private int injectSlashHits;
    [SerializeField] private int injectStrikeHits;
    [SerializeField] private int injectPerfectEvades = 2;
    [SerializeField] private string injectStatAllocationLabel = "敏捷・突刺寄り";

    [Header("ホットキー")]
    [SerializeField] private Key baseSkillTestKey = Key.B;

    private void Awake()
    {
        if (eventBridge == null)
        {
            eventBridge = GamePhaseEventBridge.Instance ?? FindAnyObjectByType<GamePhaseEventBridge>();
        }

        if (skillSlotManager == null)
        {
            skillSlotManager = FindAnyObjectByType<PlayerSkillSlotManager>();
        }
    }

    private void Update()
    {
        if (!DebugHotkeyUtility.WasPressed(baseSkillTestKey))
        {
            return;
        }

        RunBaseSkillAwakeningTest();
    }

    /// <summary>
    /// Training フェーズで AI 基礎スキル目覚めパイプラインを手動実行します。
    /// </summary>
    [ContextMenu("Run BaseSkill Awakening Test (B)")]
    public void RunBaseSkillAwakeningTest()
    {
        if (eventBridge == null)
        {
            Debug.LogError("[BaseSkillAwakeningTester] GamePhaseEventBridge が見つかりません。");
            return;
        }

        if (skillSlotManager == null)
        {
            skillSlotManager = FindAnyObjectByType<PlayerSkillSlotManager>();
        }

        int ownedBefore = skillSlotManager != null ? skillSlotManager.GetOwnedSkillCountForTesting() : -1;

        eventBridge.ForceTrainingPhase();
        eventBridge.InjectDebugTrainingHistory(
            injectThrustHits,
            injectSlashHits,
            injectStrikeHits,
            injectPerfectEvades,
            injectStatAllocationLabel);

        Debug.Log(
            $"<color=#7CFC00><b>[BaseSkillAwakeningTester]</b> B キー: Training 固定 / " +
            $"戦歴注入 突{eventBridge.runtimeHistoryLog.TotalThrustHits} " +
            $"斬{eventBridge.runtimeHistoryLog.TotalSlashHits} " +
            $"打{eventBridge.runtimeHistoryLog.TotalStrikeHits} " +
            $"回避{eventBridge.runtimeHistoryLog.TotalPerfectEvades} / " +
            $"所持スキル数(前): {ownedBefore}</color>");

        eventBridge.TriggerRuntimeAIPipeline(message => Debug.Log(message));
    }
}

using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// GamePhaseEventBridge のデバッグテスター。DebugSystemsHub に配置してください。
/// P キー: フェーズ評価 → AI パイプライン実通信トリガー。
/// </summary>
public class GamePhaseBridgeTester : MonoBehaviour
{
    [Header("参照")]
    [SerializeField] private GamePhaseEventBridge eventBridge;
    [SerializeField] private PlayerStatusManager statusManager;

    [Header("フェーズ評価用フラグ（P キー時に使用）")]
    [SerializeField] private bool simulateCrafting;
    [SerializeField] private bool simulateSafetyZone;

    [Header("ホットキー")]
    [SerializeField] private Key pipelineTestKey = Key.P;

    private void Awake()
    {
        if (eventBridge == null)
        {
            eventBridge = GamePhaseEventBridge.Instance ?? FindAnyObjectByType<GamePhaseEventBridge>();
        }

        if (statusManager == null)
        {
            statusManager = PlayerStatusManager.Instance ?? FindAnyObjectByType<PlayerStatusManager>();
        }
    }

    private void Update()
    {
        if (!DebugHotkeyUtility.WasPressed(pipelineTestKey))
        {
            return;
        }

        RunPhaseEvaluationAndPipelineTest();
    }

    /// <summary>
    /// EvaluateCurrentPhase → TriggerAIPipeline の一連フローを手動実行します。
    /// </summary>
    [ContextMenu("Run Phase Evaluation And AI Pipeline (P)")]
    public void RunPhaseEvaluationAndPipelineTest()
    {
        if (eventBridge == null)
        {
            Debug.LogError("[GamePhaseBridgeTester] GamePhaseEventBridge が見つかりません。");
            return;
        }

        eventBridge.SyncFromPlayerActionLogger();
        eventBridge.EvaluateCurrentPhase(statusManager, simulateCrafting, simulateSafetyZone);

        GamePhase phase = eventBridge.currentPhase;
        Debug.Log(GamePhaseEventBridge.FormatPhaseLog(
            phase,
            $"P キー: フェーズ評価完了 → {phase} / 履歴: 斬{eventBridge.runtimeHistoryLog.TotalSlashHits} " +
            $"打{eventBridge.runtimeHistoryLog.TotalStrikeHits} " +
            $"突{eventBridge.runtimeHistoryLog.TotalThrustHits} " +
            $"回避{eventBridge.runtimeHistoryLog.TotalPerfectEvades}"));

        eventBridge.TriggerAIPipeline(message => Debug.Log(message));
    }
}

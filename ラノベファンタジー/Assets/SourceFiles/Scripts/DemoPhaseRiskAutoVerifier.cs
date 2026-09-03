using System.Collections;
using UnityEngine;

// =============================================================================
// デモ3大リスク自動検証 — ステップ4-3仕様（Transition / Crafting入場 / Resultロック）
// 連携: DemoTimeLineManager / DemoInputGate / CraftingQualityPacketEmitter
// =============================================================================

/// <summary>
/// Play 開始時または ContextMenu から、デモフェーズ遷移の3大リスクをコルーチンで自動検証します。
/// </summary>
[DefaultExecutionOrder(1100)]
public class DemoPhaseRiskAutoVerifier : MonoBehaviour
{
    private const float TransitionWaitTimeoutSeconds = 12f;
    private const float CraftingWaitTimeoutSeconds = 12f;

    [Header("参照")]
    [SerializeField] private DemoTimeLineManager timeline;

    [Header("実行設定")]
    [SerializeField] private bool runOnPlayStart;
    [Tooltip("ON のとき Play 開始時の自動 Battle ウェーブを待ってから検証します")]
    [SerializeField] private bool waitForAutoBattleSetup = true;

    private int passCount;
    private int failCount;
    private bool isRunningAll;

    private void Awake()
    {
        timeline ??= DemoTimeLineManager.Instance ?? DemoTimeLineManager.EnsureInstance();
    }

    private void Start()
    {
        if (!runOnPlayStart)
        {
            Debug.Log(
                "<color=#90A4AE>[DemoPhaseRiskAutoVerifier] runOnPlayStart=OFF。" +
                " ContextMenu または Tools メニューから実行できます。</color>");
            return;
        }

        StartCoroutine(RunAllRiskVerificationsAfterSceneReady());
    }

    [ContextMenu("Run All Demo Phase Risk Verifications")]
    public void RunAllFromContextMenu()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("[DemoPhaseRiskAutoVerifier] Play モード中のみ実行できます。");
            return;
        }

        StartCoroutine(RunAllRiskVerificationsAfterSceneReady());
    }

    private IEnumerator RunAllRiskVerificationsAfterSceneReady()
    {
        if (isRunningAll)
        {
            Debug.LogWarning("[DemoPhaseRiskAutoVerifier] 検証が既に実行中です。");
            yield break;
        }

        isRunningAll = true;

        yield return null;
        yield return new WaitForEndOfFrame();

        timeline ??= DemoTimeLineManager.Instance ?? DemoTimeLineManager.EnsureInstance();
        if (timeline == null)
        {
            LogFail("セットアップ", "DemoTimeLineManager が見つかりません。");
            isRunningAll = false;
            yield break;
        }

        timeline.RiskTest_SetVerificationSuppressing(true);

        try
        {
            if (waitForAutoBattleSetup)
            {
                yield return new WaitForSecondsRealtime(1.5f);
            }

            timeline.RiskTest_StabilizeForVerification();
            yield return null;
            yield return new WaitForSecondsRealtime(0.2f);

            passCount = 0;
            failCount = 0;
            DemoInputTestInjector.ClearAll();
            CraftingQualityPacketEmitter.ResetEmitTelemetry();

            LogHeader("デモ3大リスク自動検証を開始");

            yield return Verify_Transition_DoubleFire_Is_Blocked();
            yield return Verify_Crafting_Entrance_Stats_Reset();
            yield return Verify_ResultPhase_Input_Ignored();

            LogHeader($"検証完了 — 成功: {passCount} / 失敗: {failCount}");
        }
        finally
        {
            timeline.RiskTest_SetVerificationSuppressing(false);
            isRunningAll = false;
        }
    }

    /// <summary>
    /// リスク1: 撃破直後の暗転中に戦闘入力を猛連打しても、二重遷移・被弾エラーが起きないこと。
    /// </summary>
    private IEnumerator Verify_Transition_DoubleFire_Is_Blocked()
    {
        const string testName = "Verify_Transition_DoubleFire_Is_Blocked";

        yield return PrepareBattlePhaseForTest();

        int hpBeforeSpam = ResolvePlayerHp();
        timeline.RiskTest_TriggerCombatClearTransition();

        if (timeline.CurrentState != DemoState.TransitionPhase)
        {
            LogFail(testName, $"即時 TransitionPhase になっていません（現在: {timeline.CurrentState}）。");
            yield break;
        }

        if (!DemoInputGate.IsPlayerInputFullyBlocked())
        {
            LogFail(testName, "TransitionPhase 中に DemoInputGate が入力遮断を返していません。");
            yield break;
        }

        if (DemoInputGate.IsBattleCombatInputAllowed())
        {
            LogFail(testName, "TransitionPhase 中なのに IsBattleCombatInputAllowed()==true です。");
            yield break;
        }

        timeline.RiskTest_TriggerCombatClearTransition();
        if (!timeline.IsBattleToCraftTransitionRunning)
        {
            LogFail(testName, "移行コルーチンが起動していません。");
            yield break;
        }

        DemoInputTestInjector.QueueLeftClick(12);
        DemoInputTestInjector.QueueStepEvade(12);
        for (int i = 0; i < 6; i++)
        {
            timeline.RiskTest_TickBattlePhaseInputOnce();
            timeline.RiskTest_SpamDirectCombatActions(2);
            yield return null;
        }

        yield return timeline.RiskTest_WaitForState(DemoState.CraftingPhase, TransitionWaitTimeoutSeconds);

        if (timeline.CurrentState != DemoState.CraftingPhase)
        {
            LogFail(testName, $"CraftingPhase 着地に失敗（現在: {timeline.CurrentState}）。");
            yield break;
        }

        if (timeline.IsBattleToCraftTransitionRunning)
        {
            LogFail(testName, "CraftingPhase 着地後も暗転コルーチンが残っています（二重起動の疑い）。");
            yield break;
        }

        int hpAfter = ResolvePlayerHp();
        LogPass(
            testName,
            $"暗転中の猛連打後も CraftingPhase へ安全着地（HP {hpBeforeSpam}→{hpAfter}、コルーチン単発）。");
    }

    /// <summary>
    /// リスク2: 瀕死・スタミナゼロの状態から工房入場した際、HP/スタミナが満タンに回復すること。
    /// </summary>
    private IEnumerator Verify_Crafting_Entrance_Stats_Reset()
    {
        const string testName = "Verify_Crafting_Entrance_Stats_Reset";

        yield return PrepareBattlePhaseForTest();
        DrainPlayerVitalsForTest();

        int hpBeforeClear = ResolvePlayerHp();
        int maxHp = ResolvePlayerMaxHp();
        float staminaBeforeClear = ResolvePlayerStamina();

        if (hpBeforeClear >= maxHp * 0.5f)
        {
            LogFail(testName, $"瀕死セットアップ失敗（HP {hpBeforeClear}/{maxHp}）。");
            yield break;
        }

        if (staminaBeforeClear > 0.01f)
        {
            LogFail(testName, $"スタミナゼロセットアップ失敗（{staminaBeforeClear:F1}）。");
            yield break;
        }

        timeline.RiskTest_TriggerCombatClearTransition();
        yield return timeline.RiskTest_WaitForState(DemoState.CraftingPhase, CraftingWaitTimeoutSeconds);

        if (timeline.CurrentState != DemoState.CraftingPhase)
        {
            LogFail(testName, $"CraftingPhase へ遷移できませんでした（現在: {timeline.CurrentState}）。");
            yield break;
        }

        int hpAfter = ResolvePlayerHp();
        int maxHpAfter = ResolvePlayerMaxHp();
        float staminaAfter = ResolvePlayerStamina();
        float maxStamina = ResolvePlayerMaxStamina();

        bool hpFull = hpAfter == maxHpAfter;
        bool staminaFull = Mathf.Approximately(staminaAfter, maxStamina);

        if (hpFull && staminaFull)
        {
            LogPass(
                testName,
                $"EnterCraftingPhaseSetup 後 HP {hpAfter}/{maxHpAfter}、スタミナ {staminaAfter:F0}/{maxStamina:F0}。");
        }
        else
        {
            LogFail(
                testName,
                $"回復不足: HP {hpAfter}/{maxHpAfter}, STA {staminaAfter:F1}/{maxStamina:F0}。");
        }
    }

    /// <summary>
    /// リスク3: ResultPhase 着地後の工程キー・Enter 連打で JSON 多重射出や工程追加が起きないこと。
    /// </summary>
    private IEnumerator Verify_ResultPhase_Input_Ignored()
    {
        const string testName = "Verify_ResultPhase_Input_Ignored";

        yield return PrepareCraftingPhaseForTest();

        DetailedCraftingProcessManager process = DetailedCraftingProcessManager.Instance
            ?? DetailedCraftingProcessManager.EnsureInstance();
        CraftingStatusManager status = CraftingStatusManager.EnsureInstance();

        if (process == null || status == null)
        {
            LogFail(testName, "DetailedCraftingProcessManager / CraftingStatusManager が見つかりません。");
            yield break;
        }

        CraftingQualityPacketEmitter.ResetEmitTelemetry();

        DemoInputTestInjector.QueueDigitKey(1);
        timeline.RiskTest_TickCraftingPhaseInputOnce();
        yield return null;

        if (process.currentProcessLogs.Count == 0)
        {
            LogFail(testName, "検証用の工程ログが1件も追加されていません。");
            yield break;
        }

        DemoInputTestInjector.QueueEnterKey(1);
        timeline.RiskTest_TickCraftingPhaseInputOnce();
        yield return null;

        if (timeline.CurrentState != DemoState.ResultPhase)
        {
            LogFail(testName, $"ResultPhase へ遷移できませんでした（現在: {timeline.CurrentState}）。");
            yield break;
        }

        int emitsAfterFirstEnter = CraftingQualityPacketEmitter.TotalEmitCount;
        if (emitsAfterFirstEnter != 1)
        {
            LogFail(testName, $"初回 Enter 後の JSON 射出回数が 1 ではありません（{emitsAfterFirstEnter}）。");
            yield break;
        }

        yield return null;

        int logsBaseline = process.currentProcessLogs.Count;
        bool sessionClosedAfterResult = !process.IsSessionActive;

        DemoInputTestInjector.ClearAll();
        DemoInputTestInjector.QueueEnterKey(8);
        for (int slot = 1; slot <= 5; slot++)
        {
            DemoInputTestInjector.QueueDigitKey(slot, 4);
        }

        for (int frame = 0; frame < 12; frame++)
        {
            timeline.RiskTest_TickCraftingPhaseInputOnce();
            yield return null;
        }

        int emitsAfterSpam = CraftingQualityPacketEmitter.TotalEmitCount;
        int logsAfterSpam = process.currentProcessLogs.Count;

        if (emitsAfterSpam == 1 &&
            logsAfterSpam == logsBaseline &&
            !process.IsSessionActive &&
            sessionClosedAfterResult)
        {
            LogPass(
                testName,
                $"ResultPhase 連打後も JSON={emitsAfterSpam}回・工程ログ={logsAfterSpam}件（不変）・セッション終了。");
        }
        else
        {
            LogFail(
                testName,
                $"多重発火検知: JSON {emitsAfterFirstEnter}->{emitsAfterSpam}, " +
                $"logs {logsBaseline}->{logsAfterSpam}, sessionActive={process.IsSessionActive}。");
        }
    }

    private IEnumerator PrepareBattlePhaseForTest()
    {
        DemoInputTestInjector.ClearAll();
        CombatActionFeedbackManager combat = CombatActionFeedbackManager.EnsureInstance();
        combat?.UnfreezeCombatSystemsForBattle();
        timeline.RiskTest_ForceReenterBattlePhase();

        yield return null;
    }

    private IEnumerator PrepareCraftingPhaseForTest()
    {
        DemoInputTestInjector.ClearAll();

        if (timeline.CurrentState == DemoState.TransitionPhase)
        {
            yield return timeline.RiskTest_WaitForState(DemoState.CraftingPhase, CraftingWaitTimeoutSeconds);
        }

        if (timeline.CurrentState == DemoState.CraftingPhase)
        {
            timeline.RiskTest_RefreshCraftingSession();
            yield return null;
            yield break;
        }

        yield return PrepareBattlePhaseForTest();

        if (timeline.CurrentState == DemoState.BattlePhase)
        {
            timeline.RiskTest_TriggerCombatClearTransition();
            yield return timeline.RiskTest_WaitForState(DemoState.CraftingPhase, CraftingWaitTimeoutSeconds);
        }

        if (timeline.CurrentState != DemoState.CraftingPhase)
        {
            timeline.TransitionTo(DemoState.CraftingPhase);
            yield return null;
        }
    }

    private void DrainPlayerVitalsForTest()
    {
        CombatStats hp = ResolvePlayerCombatStats();
        PlayerStats stamina = ResolvePlayerStats();

        if (hp != null)
        {
            hp.SetHpForTesting(Mathf.Max(1, hp.MaxHp / 10));
        }

        stamina?.SetStaminaForTesting(0f);
    }

    private static CombatStats ResolvePlayerCombatStats()
    {
        GameObject player = GameObject.Find("PlayerRobot") ?? GameObject.Find("PlayerRobot ");
        if (player == null)
        {
            return null;
        }

        CombatStats stats = player.GetComponent<CombatStats>();
        return stats != null ? stats : player.GetComponentInChildren<CombatStats>(true);
    }

    private static PlayerStats ResolvePlayerStats()
    {
        GameObject player = GameObject.Find("PlayerRobot") ?? GameObject.Find("PlayerRobot ");
        if (player == null)
        {
            return null;
        }

        PlayerStats stats = player.GetComponent<PlayerStats>();
        return stats != null ? stats : player.GetComponentInChildren<PlayerStats>(true);
    }

    private static int ResolvePlayerHp()
    {
        CombatStats stats = ResolvePlayerCombatStats();
        return stats != null ? stats.CurrentHp : -1;
    }

    private static int ResolvePlayerMaxHp()
    {
        CombatStats stats = ResolvePlayerCombatStats();
        return stats != null ? stats.MaxHp : -1;
    }

    private static float ResolvePlayerStamina()
    {
        PlayerStats stats = ResolvePlayerStats();
        return stats != null ? stats.CurrentStamina : -1f;
    }

    private static float ResolvePlayerMaxStamina()
    {
        PlayerStats stats = ResolvePlayerStats();
        return stats != null ? stats.maxStamina : -1f;
    }

    private void LogPass(string testName, string detail)
    {
        passCount++;
        Debug.Log($"<color=#69F0AE>[RISK TEST PASS]</color> {testName} — {detail}");
    }

    private void LogFail(string testName, string detail)
    {
        failCount++;
        Debug.LogWarning($"<color=#FF5252>[RISK TEST FAIL]</color> {testName} — {detail}");
    }

    private void LogHeader(string message)
    {
        Debug.Log($"<color=#81D4FA><b>[DemoPhaseRiskAutoVerifier]</b></color> {message}");
    }
}

/// <summary>Play 開始時に DemoPhaseRiskAutoVerifier を DebugSystemsHub へ配置します。</summary>
public static class DemoPhaseRiskAutoVerifierBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AttachToDebugSystemsHub()
    {
        GameObject hub = GameObject.Find("DebugSystemsHub");
        if (hub == null)
        {
            return;
        }

        if (hub.GetComponent<DemoPhaseRiskAutoVerifier>() != null)
        {
            return;
        }

        hub.AddComponent<DemoPhaseRiskAutoVerifier>();
    }
}

#if UNITY_EDITOR
/// <summary>エディタメニューから3大リスク検証を起動します。</summary>
public static class DemoPhaseRiskAutoVerifierMenu
{
    [UnityEditor.MenuItem("Tools/Demo/Run Phase Risk Verifications (Play Mode)")]
    private static void RunFromMenu()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("[DemoPhaseRiskAutoVerifier] Play モード中のみ実行できます。");
            return;
        }

        DemoPhaseRiskAutoVerifier verifier = Object.FindAnyObjectByType<DemoPhaseRiskAutoVerifier>();
        if (verifier == null)
        {
            GameObject hub = GameObject.Find("DebugSystemsHub");
            verifier = hub != null
                ? hub.AddComponent<DemoPhaseRiskAutoVerifier>()
                : new GameObject("DemoPhaseRiskAutoVerifier").AddComponent<DemoPhaseRiskAutoVerifier>();
        }

        verifier.RunAllFromContextMenu();
    }
}
#endif

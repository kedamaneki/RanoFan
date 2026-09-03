#if UNITY_EDITOR

using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEditor;

// =============================================================================
// AIデバッグ・ファジングエミュレータ — 超高速ランダムプレイで仕様矛盾を検知
// 連携: DemoTimeLineManager / DemoInputTestInjector / CraftingStatusManager
// =============================================================================

/// <summary>
/// 人間の代わりに超高速・ランダムでデモ全フェーズを数千回エミュレートし、
/// スコア矛盾・バースト不発・デッドロック・例外を自動報告します。
/// </summary>
[DefaultExecutionOrder(1200)]
public class DemoAIDebugFuzzer : MonoBehaviour
{
    private const int DemoSuccessScoreMin = 60;
    private const int DemoSuccessScoreMax = 100;
    private const int ThermalBurstScore = 10;

    [Header("参照")]
    [SerializeField] private DemoTimeLineManager timeline;

    [Header("実行設定")]
    [SerializeField] private int hyperBurstSessionsPerFrame = 32;
    [SerializeField] private int hyperBurstTotalTarget = 3000;
    [SerializeField] private int acceleratedSessionTarget = 500;
    [SerializeField] private float acceleratedTimeScale = 100f;
    [SerializeField] private int maxStepsPerSession = 600;
    [SerializeField] private int deadlockStepThreshold = 80;
    [SerializeField] private int randomSeed = 0;

    private System.Random rng;
    private bool isRunning;
    private bool pauseOnBug;
    private float restoreTimeScale = 1f;
    private int completedSessions;
    private int detectedBugs;
    private Coroutine activeRunCoroutine;
    private static int lastHotkeyToggleFrame = -1;
    private readonly List<string> sessionLog = new List<string>(32);

    private void Awake()
    {
        timeline ??= DemoTimeLineManager.Instance ?? DemoTimeLineManager.EnsureInstance();
    }

    private void OnEnable()
    {
        EditorApplication.update += EditorPollHotkey;
    }

    private void OnDisable()
    {
        EditorApplication.update -= EditorPollHotkey;
        if (isRunning)
        {
            StopFuzzRun();
        }
    }

    private void Update()
    {
        PollHotkeyToggle();
    }

    private static void EditorPollHotkey()
    {
        if (!Application.isPlaying)
        {
            return;
        }

        PollHotkeyToggle();
    }

    private static void PollHotkeyToggle()
    {
        if (Time.frameCount == lastHotkeyToggleFrame || !IsShiftF10Pressed())
        {
            return;
        }

        lastHotkeyToggleFrame = Time.frameCount;

        DemoAIDebugFuzzer fuzzer = FindAnyObjectByType<DemoAIDebugFuzzer>();
        if (fuzzer == null)
        {
            return;
        }

        if (fuzzer.isRunning)
        {
            fuzzer.StopFuzzRun();
        }
        else
        {
            fuzzer.StartHyperBurstFuzz();
        }
    }

    private static bool IsShiftF10Pressed()
    {
        if (!DebugHotkeyUtility.TryGetKeyboard(out Keyboard keyboard))
        {
            return false;
        }

        return keyboard.shiftKey.isPressed && keyboard.f10Key.wasPressedThisFrame;
    }

    [ContextMenu("Run Hyper-Burst Fuzz (1 Frame Batch)")]
    public void StartHyperBurstFuzz()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("[DemoAIDebugFuzzer] Play モード中のみ実行できます。");
            return;
        }

        if (isRunning)
        {
            StopFuzzRun();
            return;
        }

        activeRunCoroutine = StartCoroutine(RunHyperBurstFuzzCoroutine());
    }

    [ContextMenu("Run Accelerated Fuzz (TimeScale x100)")]
    public void StartAcceleratedFuzz()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("[DemoAIDebugFuzzer] Play モード中のみ実行できます。");
            return;
        }

        if (isRunning)
        {
            StopFuzzRun();
            return;
        }

        activeRunCoroutine = StartCoroutine(RunAcceleratedFuzzCoroutine());
    }

    public void StopFuzzRun()
    {
        isRunning = false;
        pauseOnBug = false;

        if (activeRunCoroutine != null)
        {
            StopCoroutine(activeRunCoroutine);
            activeRunCoroutine = null;
        }

        StopAllCoroutines();
        DemoInputTestInjector.ClearAll();
        Time.timeScale = restoreTimeScale > 0f ? restoreTimeScale : 1f;
        timeline?.Fuzz_SetFuzzRunActive(false);
        Debug.Log(
            $"<color=#90A4AE>[DemoAIDebugFuzzer] 停止 — 完了セッション: {completedSessions}, 検知バグ: {detectedBugs}</color>");
    }

    private bool ShouldContinueFuzz()
    {
        return isRunning && !pauseOnBug;
    }

    private IEnumerator RunHyperBurstFuzzCoroutine()
    {
        if (!PrepareFuzzRun())
        {
            yield break;
        }

        Debug.Log(
            $"<color=#CE93D8><b>[DemoAIDebugFuzzer]</b> Hyper-Burst 開始 — " +
            $"1フレームあたり最大 {hyperBurstSessionsPerFrame} セッション</color>");

        while (ShouldContinueFuzz() && completedSessions < Mathf.Max(1, hyperBurstTotalTarget))
        {
            int batch = Mathf.Max(1, hyperBurstSessionsPerFrame);
            for (int i = 0; i < batch && ShouldContinueFuzz(); i++)
            {
                RunSingleSessionHyperBurst();
                completedSessions++;
                yield return null;
            }
        }

        activeRunCoroutine = null;
        FinalizeFuzzRun();
    }

    private IEnumerator RunAcceleratedFuzzCoroutine()
    {
        if (!PrepareFuzzRun())
        {
            yield break;
        }

        restoreTimeScale = Time.timeScale;
        Time.timeScale = Mathf.Max(1f, acceleratedTimeScale);

        Debug.Log(
            $"<color=#CE93D8><b>[DemoAIDebugFuzzer]</b> Accelerated 開始 — " +
            $"目標 {acceleratedSessionTarget} セッション / timeScale={Time.timeScale:F0}</color>");

        while (ShouldContinueFuzz() && completedSessions < Mathf.Max(1, acceleratedSessionTarget))
        {
            yield return RunSingleSessionCoroutine(instantTransitions: false);
            if (!ShouldContinueFuzz())
            {
                break;
            }

            completedSessions++;
        }

        Time.timeScale = restoreTimeScale > 0f ? restoreTimeScale : 1f;
        activeRunCoroutine = null;
        FinalizeFuzzRun();
    }

    private bool PrepareFuzzRun()
    {
        timeline ??= DemoTimeLineManager.Instance ?? DemoTimeLineManager.EnsureInstance();
        if (timeline == null)
        {
            Debug.LogError("[DemoAIDebugFuzzer] DemoTimeLineManager が見つかりません。");
            return false;
        }

        rng = randomSeed != 0 ? new System.Random(randomSeed) : new System.Random(Environment.TickCount);
        isRunning = true;
        pauseOnBug = false;
        completedSessions = 0;
        detectedBugs = 0;
        timeline.Fuzz_SetFuzzRunActive(true);
        timeline.RiskTest_StabilizeForVerification();
        CombatActionFeedbackManager.EnsureInstance()?.UnfreezeCombatSystemsForBattle();
        DemoInputTestInjector.ClearAll();
        return true;
    }

    private void FinalizeFuzzRun()
    {
        isRunning = false;
        timeline?.Fuzz_SetFuzzRunActive(false);
        Debug.Log(
            $"<color=#69F0AE><b>[DemoAIDebugFuzzer] 完了</b></color> " +
            $"セッション {completedSessions} / バグ {detectedBugs}");
    }

    private void RunSingleSessionHyperBurst()
    {
        if (!ShouldContinueFuzz())
        {
            return;
        }

        sessionLog.Clear();
        sessionLog.Add("[SessionStart]");

        int steps = 0;
        DemoState lastState = timeline.CurrentState;
        int unchangedStateSteps = 0;

        try
        {
            timeline.RiskTest_StabilizeForVerification();
            sessionLog.Add("[BattlePhase]");

            RunBattleFuzzPhase(ref steps, ref lastState, ref unchangedStateSteps, instantTransitions: true);

            string craftType = RollCraftType();
            timeline.Fuzz_PrepareCraftingSession(craftType);
            sessionLog.Add(craftType == DetailedCraftingProcessManager.CraftTypeAlch ? "[Alch選択]" : "[Forge選択]");

            RunCraftingFuzzPhase(craftType, ref steps, ref lastState, ref unchangedStateSteps);

            timeline.Fuzz_ResetForNextSession();
        }
        catch (Exception ex)
        {
            EmitBugReport(
                timeline.CurrentState,
                "未処理例外が発生しました",
                ex.ToString());
        }
    }

    private IEnumerator RunSingleSessionCoroutine(bool instantTransitions)
    {
        sessionLog.Clear();
        sessionLog.Add("[SessionStart]");

        int steps = 0;
        DemoState lastState = timeline.CurrentState;
        int unchangedStateSteps = 0;

        timeline.RiskTest_StabilizeForVerification();
        sessionLog.Add("[BattlePhase]");

        if (!TryRunBattleAndCraftingSetup(
                ref steps,
                ref lastState,
                ref unchangedStateSteps,
                instantTransitions,
                out string craftType))
        {
            yield break;
        }

        if (!instantTransitions && timeline.CurrentState == DemoState.TransitionPhase)
        {
            yield return timeline.RiskTest_WaitForState(DemoState.CraftingPhase, 3f);
        }

        if (timeline.CurrentState != DemoState.CraftingPhase)
        {
            timeline.Fuzz_InstantBattleClearToCrafting();
        }

        timeline.Fuzz_PrepareCraftingSession(craftType);
        sessionLog.Add(craftType == DetailedCraftingProcessManager.CraftTypeAlch ? "[Alch選択]" : "[Forge選択]");

        if (!TryRunCraftingAndReset(ref steps, ref lastState, ref unchangedStateSteps, craftType))
        {
            yield break;
        }

        yield return null;
    }

    private bool TryRunBattleAndCraftingSetup(
        ref int steps,
        ref DemoState lastState,
        ref int unchangedStateSteps,
        bool instantTransitions,
        out string craftType)
    {
        craftType = null;

        try
        {
            RunBattleFuzzPhase(ref steps, ref lastState, ref unchangedStateSteps, instantTransitions);
            craftType = RollCraftType();
            return true;
        }
        catch (Exception ex)
        {
            EmitBugReport(
                timeline.CurrentState,
                "未処理例外が発生しました",
                ex.ToString());
            return false;
        }
    }

    private bool TryRunCraftingAndReset(
        ref int steps,
        ref DemoState lastState,
        ref int unchangedStateSteps,
        string craftType)
    {
        try
        {
            RunCraftingFuzzPhase(craftType, ref steps, ref lastState, ref unchangedStateSteps);
            timeline.Fuzz_ResetForNextSession();
            return true;
        }
        catch (Exception ex)
        {
            EmitBugReport(
                timeline.CurrentState,
                "未処理例外が発生しました",
                ex.ToString());
            return false;
        }
    }

    private void RunBattleFuzzPhase(
        ref int steps,
        ref DemoState lastState,
        ref int unchangedStateSteps,
        bool instantTransitions)
    {
        CombatActionFeedbackManager combat = CombatActionFeedbackManager.EnsureInstance();
        combat?.UnfreezeCombatSystemsForBattle();

        int battleActions = NextInt(24, 96);
        for (int i = 0; i < battleActions; i++)
        {
            if (!ShouldContinueFuzz())
            {
                return;
            }

            InjectRandomBattleInput();
            SafeTickBattleInput();
            steps++;
            TrackStateProgress(ref lastState, ref unchangedStateSteps, steps);

            if (combat != null && combat.IsEnemyDefeated)
            {
                sessionLog.Add("[BattleCleared]");
                break;
            }
        }

        MaybeInjectInventoryDebugItems();

        if (combat != null && !combat.IsEnemyDefeated)
        {
            sessionLog.Add("[ForceCombatClear]");
        }

        if (instantTransitions)
        {
            timeline.Fuzz_InstantBattleClearToCrafting();
        }
        else
        {
            timeline.RiskTest_TriggerCombatClearTransition();
        }

        unchangedStateSteps = 0;
        lastState = timeline.CurrentState;
    }

    private void RunCraftingFuzzPhase(
        string craftType,
        ref int steps,
        ref DemoState lastState,
        ref int unchangedStateSteps)
    {
        if (timeline.CurrentState != DemoState.CraftingPhase)
        {
            EmitBugReport(
                timeline.CurrentState,
                "生産フェーズへ遷移できませんでした",
                $"現在ステート: {timeline.CurrentState}");
            return;
        }

        DetailedCraftingProcessManager process = DetailedCraftingProcessManager.Instance
            ?? DetailedCraftingProcessManager.EnsureInstance();
        CraftingStatusManager status = CraftingStatusManager.EnsureInstance();

        int actionCount = NextInt(3, 10);
        int slot4Presses = 0;

        for (int i = 0; i < actionCount; i++)
        {
            if (!ShouldContinueFuzz())
            {
                return;
            }

            int slot = NextInt(1, 5);
            if (slot == 4 &&
                string.Equals(craftType, DetailedCraftingProcessManager.CraftTypeAlch, StringComparison.OrdinalIgnoreCase))
            {
                slot4Presses++;
            }

            DemoInputTestInjector.QueueDigitKey(slot);
            sessionLog.Add($"[Key{slot}]");
            SafeTickCraftingInput();
            steps++;
            TrackStateProgress(ref lastState, ref unchangedStateSteps, steps);

            if (timeline.CurrentState == DemoState.ResultPhase)
            {
                ValidateResultPhase(status, process, craftType, slot4Presses, autoFinalize: true);
                return;
            }
        }

        int logCount = process != null ? process.currentProcessLogs.Count : 0;

        DemoInputTestInjector.QueueEnterKey(1);
        sessionLog.Add("[Enter]");
        SafeTickCraftingInput();
        steps++;
        TrackStateProgress(ref lastState, ref unchangedStateSteps, steps);

        ValidateResultPhase(status, process, craftType, slot4Presses, autoFinalize: false);
    }

    private void ValidateResultPhase(
        CraftingStatusManager status,
        DetailedCraftingProcessManager process,
        string craftType,
        int slot4Presses,
        bool autoFinalize)
    {
        int logCount = process != null ? process.currentProcessLogs.Count : 0;
        bool expectedBurst = EvaluateExpectedBurst(status, craftType, logCount);

        if (timeline.CurrentState != DemoState.ResultPhase)
        {
            if (expectedBurst)
            {
                EmitBugReport(
                    timeline.CurrentState,
                    "バースト条件を満たしているのに ResultPhase へ即時着地しませんでした",
                    BuildBurstDiagnostics(status, craftType, logCount, slot4Presses));
            }
            else
            {
                EmitBugReport(
                    timeline.CurrentState,
                    "Enter 後に ResultPhase へ遷移できませんでした",
                    $"autoFinalize={autoFinalize}");
            }

            return;
        }

        DemoCraftingJudgmentResult judgment = status != null
            ? status.EvaluateDemoCraftingJudgment(logCount)
            : null;

        ValidateJudgmentConsistency(
            DemoState.ResultPhase,
            judgment,
            expectedBurst,
            craftType,
            autoFinalize ? "自動ジャッジ" : "Enter後");
    }

    private void ValidateJudgmentConsistency(
        DemoState state,
        DemoCraftingJudgmentResult judgment,
        bool shouldBurst,
        string craftType,
        string phaseLabel)
    {
        if (judgment == null)
        {
            EmitBugReport(state, $"{phaseLabel}: ジャッジ結果が null です", string.Empty);
            return;
        }

        if (shouldBurst)
        {
            if (!judgment.IsThermalBurstFailure)
            {
                EmitBugReport(
                    state,
                    $"バースト条件を満たしているのに通常成功({judgment.FinalScore}点)で計算されました",
                    $"職種={craftType} / Raw={judgment.RawScore} / BurstReason={judgment.BurstReason}");
                return;
            }

            if (judgment.FinalScore != ThermalBurstScore)
            {
                EmitBugReport(
                    state,
                    $"熱科学バーストなのに最終スコアが {judgment.FinalScore} 点です（期待 {ThermalBurstScore}）",
                    judgment.BurstReason);
            }

            return;
        }

        if (judgment.IsThermalBurstFailure)
        {
            EmitBugReport(
                state,
                "バースト条件未達なのに熱科学バースト判定になっています",
                judgment.BurstReason);
            return;
        }

        if (judgment.FinalScore < DemoSuccessScoreMin || judgment.FinalScore > DemoSuccessScoreMax)
        {
            EmitBugReport(
                state,
                $"通常成功なのに最終スコアがレンジ外です（{judgment.FinalScore}点）",
                $"期待 {DemoSuccessScoreMin}〜{DemoSuccessScoreMax} / Raw={judgment.RawScore}");
            return;
        }

        int expectedClamp = Mathf.Clamp(judgment.RawScore, DemoSuccessScoreMin, DemoSuccessScoreMax);
        if (judgment.FinalScore != expectedClamp)
        {
            EmitBugReport(
                state,
                "Mathf.Clamp 漏れの疑い（RawScore と FinalScore が不一致）",
                $"Raw={judgment.RawScore} / Final={judgment.FinalScore} / 期待Clamp={expectedClamp}");
        }
    }

    private bool EvaluateExpectedBurst(
        CraftingStatusManager status,
        string craftType,
        int actionCount)
    {
        if (status == null)
        {
            return false;
        }

        if (string.Equals(craftType, DetailedCraftingProcessManager.CraftTypeForge, StringComparison.OrdinalIgnoreCase))
        {
            return status.IsForgeThermalBurstFailure();
        }

        if (string.Equals(craftType, DetailedCraftingProcessManager.CraftTypeAlch, StringComparison.OrdinalIgnoreCase))
        {
            return status.IsAlchThermalBurstFailure(actionCount);
        }

        return false;
    }

    private string BuildBurstDiagnostics(
        CraftingStatusManager status,
        string craftType,
        int actionCount,
        int slot4Presses)
    {
        if (status == null)
        {
            return "CraftingStatusManager=null";
        }

        if (string.Equals(craftType, DetailedCraftingProcessManager.CraftTypeForge, StringComparison.OrdinalIgnoreCase))
        {
            return $"Forge PeakTemp={status.PeakForgeTemperature:F1}℃ / IsForgeBurst={status.IsForgeThermalBurstFailure()}";
        }

        float pot = status.GetParam(
            DetailedCraftingProcessManager.CraftTypeAlch,
            CraftingStatusManager.ParamPotTemperature);
        float extraction = status.GetParam(
            DetailedCraftingProcessManager.CraftTypeAlch,
            CraftingStatusManager.ParamExtractionLevel);
        return
            $"Alch actions={actionCount} / Key4x{slot4Presses} / Pot={pot:F1}℃ / Extraction={extraction:F1} / " +
            $"IsAlchBurst={status.IsAlchThermalBurstFailure(actionCount)}";
    }

    private void InjectRandomBattleInput()
    {
        int roll = NextInt(0, 99);
        if (roll < 30)
        {
            DemoInputTestInjector.QueueLeftClick(NextInt(1, 3));
            sessionLog.Add("[LMB]");
        }
        else if (roll < 45)
        {
            DemoInputTestInjector.QueueParry(1);
            sessionLog.Add("[Parry]");
        }
        else if (roll < 60)
        {
            DemoInputTestInjector.QueueStepEvade(1);
            sessionLog.Add("[Step]");
        }
        else if (roll < 75)
        {
            int dir = NextInt(0, 1) == 0 ? 1 : -1;
            DemoInputTestInjector.QueueScroll(dir, NextInt(1, 2));
            sessionLog.Add(dir > 0 ? "[WheelUp]" : "[WheelDown]");
        }
        else
        {
            DemoInputTestInjector.QueueLeftClick(1);
            DemoInputTestInjector.QueueParry(1);
            sessionLog.Add("[Combo]");
        }
    }

    private void MaybeInjectInventoryDebugItems()
    {
        int roll = NextInt(0, 99);
        if (roll >= 35)
        {
            return;
        }

        InventoryManager inventory = InventoryManager.EnsureInstance();
        if (inventory == null)
        {
            return;
        }

        if (roll < 17)
        {
            inventory.AddItem(InventoryItemCatalog.CreateGryphonBone(), NextInt(1, 4));
            sessionLog.Add("[U:Bone]");
        }
        else
        {
            inventory.AddItem(InventoryItemCatalog.CreateIronOre(), NextInt(1, 4));
            sessionLog.Add("[I:Ore]");
        }

        if (roll < 8)
        {
            inventory.AddItem(InventoryItemCatalog.CreatePortableManaFurnace(), 1);
            sessionLog.Add("[HeavyTool]");
        }
    }

    private void SafeTickBattleInput()
    {
        try
        {
            if (DemoInputGate.IsBattleCombatInputAllowed())
            {
                timeline.RiskTest_TickBattlePhaseInputOnce();
            }
        }
        catch (Exception ex)
        {
            EmitBugReport(timeline.CurrentState, "戦闘入力ティック中に例外", ex.ToString());
        }
    }

    private void SafeTickCraftingInput()
    {
        try
        {
            if (DemoInputGate.IsCraftingHotkeyInputAllowed())
            {
                timeline.RiskTest_TickCraftingPhaseInputOnce();
            }
        }
        catch (Exception ex)
        {
            EmitBugReport(timeline.CurrentState, "生産入力ティック中に例外", ex.ToString());
        }
    }

    private void TrackStateProgress(
        ref DemoState lastState,
        ref int unchangedStateSteps,
        int steps)
    {
        if (timeline.CurrentState == lastState)
        {
            unchangedStateSteps++;
        }
        else
        {
            unchangedStateSteps = 0;
            lastState = timeline.CurrentState;
        }

        if (steps > maxStepsPerSession)
        {
            EmitBugReport(
                timeline.CurrentState,
                "1セッションの最大ステップ数を超過しました（デッドロック疑い）",
                $"steps={steps}");
            return;
        }

        if (unchangedStateSteps >= deadlockStepThreshold &&
            IsStateStagnationSuspicious(timeline.CurrentState))
        {
            EmitBugReport(
                timeline.CurrentState,
                $"ステートが {deadlockStepThreshold} ステップ以上遷移しませんでした（デッドロック）",
                $"停滞ステート={timeline.CurrentState}");
        }
    }

    /// <summary>
    /// 同一ステートに留まり続けることが異常とみなすフェーズかを判定します。
    /// BattlePhase はランダム攻撃のたびにステート遷移しないため除外します。
    /// </summary>
    private static bool IsStateStagnationSuspicious(DemoState state)
    {
        switch (state)
        {
            case DemoState.BattlePhase:
                return false;
            case DemoState.TransitionPhase:
            case DemoState.CraftingPhase:
            case DemoState.ResultPhase:
                return true;
            default:
                return true;
        }
    }

    private string RollCraftType()
    {
        return NextInt(0, 1) == 0
            ? DetailedCraftingProcessManager.CraftTypeForge
            : DetailedCraftingProcessManager.CraftTypeAlch;
    }

    private int NextInt(int minInclusive, int maxInclusive)
    {
        return rng.Next(minInclusive, maxInclusive + 1);
    }

    private void EmitBugReport(DemoState state, string summary, string detail)
    {
        detectedBugs++;
        pauseOnBug = true;
        isRunning = false;
        Time.timeScale = restoreTimeScale > 0f ? restoreTimeScale : 1f;
        timeline?.Fuzz_SetFuzzRunActive(false);

        StringBuilder processLine = new StringBuilder();
        for (int i = 0; i < sessionLog.Count; i++)
        {
            if (i > 0)
            {
                processLine.Append(" -> ");
            }

            processLine.Append(sessionLog[i]);
        }

        Debug.LogWarning(
            "\n<color=#FF5252><b>--------------------------------------------------</b></color>\n" +
            "<color=#FF5252><b>【AI_DEBUG_BUG_REPORT】致命的な仕様矛盾を検知！</b></color>\n" +
            $"<color=#FF8A80>- 発生ステート: [{state}]</color>\n" +
            $"<color=#FF8A80>- 異常内容: {summary}</color>\n" +
            $"<color=#FFCCBC>- 再現プロセスログ: {processLine}</color>\n" +
            $"<color=#FFAB91>- エラー詳細: {detail}</color>\n" +
            "<color=#FF5252><b>--------------------------------------------------</b></color>");
    }
}

/// <summary>Play 開始時に DemoAIDebugFuzzer を DebugSystemsHub へ配置します。</summary>
public static class DemoAIDebugFuzzerBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AttachToDebugSystemsHub()
    {
        GameObject hub = GameObject.Find("DebugSystemsHub");
        if (hub == null)
        {
            return;
        }

        if (hub.GetComponent<DemoAIDebugFuzzer>() != null)
        {
            return;
        }

        hub.AddComponent<DemoAIDebugFuzzer>();
    }
}

/// <summary>エディタメニューから AI ファジングを起動します。</summary>
public static class DemoAIDebugFuzzerMenu
{
    [MenuItem("Tools/Demo/AI Debug Fuzzer/Hyper-Burst (Play Mode)")]
    private static void RunHyperBurst()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("[DemoAIDebugFuzzer] Play モード中のみ実行できます。");
            return;
        }

        ResolveFuzzer()?.StartHyperBurstFuzz();
    }

    [MenuItem("Tools/Demo/AI Debug Fuzzer/Accelerated x100 (Play Mode)")]
    private static void RunAccelerated()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("[DemoAIDebugFuzzer] Play モード中のみ実行できます。");
            return;
        }

        ResolveFuzzer()?.StartAcceleratedFuzz();
    }

    [MenuItem("Tools/Demo/AI Debug Fuzzer/Stop")]
    private static void StopFuzz()
    {
        if (!Application.isPlaying)
        {
            return;
        }

        DemoAIDebugFuzzer[] fuzzers = UnityEngine.Object.FindObjectsByType<DemoAIDebugFuzzer>(
            FindObjectsInactive.Include);
        if (fuzzers.Length == 0)
        {
            Debug.LogWarning("[DemoAIDebugFuzzer] 実行中のファザーが見つかりません。");
            return;
        }

        for (int i = 0; i < fuzzers.Length; i++)
        {
            fuzzers[i].StopFuzzRun();
        }
    }

    private static DemoAIDebugFuzzer ResolveFuzzer()
    {
        DemoAIDebugFuzzer fuzzer = UnityEngine.Object.FindAnyObjectByType<DemoAIDebugFuzzer>();
        if (fuzzer != null)
        {
            return fuzzer;
        }

        GameObject hub = GameObject.Find("DebugSystemsHub");
        return hub != null
            ? hub.AddComponent<DemoAIDebugFuzzer>()
            : new GameObject(nameof(DemoAIDebugFuzzer)).AddComponent<DemoAIDebugFuzzer>();
    }
}

#endif

#if UNITY_EDITOR

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;

// =============================================================================
// 高速大量 AI デバッガー — 正史 250 年・敵 AI・装備還流・ドロップを超高速ファジング
// エディタ専用（本番ビルドには含まれません）
// =============================================================================

/// <summary>ファザーが検知した異常パケット（JsonUtility 互換）。</summary>
[Serializable]
public class AutoFuzzerErrorPacket
{
    public string fuzzerTestId;
    public string errorType;
    public int targetTurn;
    public string[] lastActionHistory;
    public string errorMessage;
}

/// <summary>
/// 1 秒間に数千回の境界値・不正入力を流し、Safe-Fail が例外なく成立するかを検証します。
/// Play 中は Shift+F12 / Context Menu で開始・停止。Tools メニューから同期バーストも実行できます。
/// </summary>
[DefaultExecutionOrder(1210)]
public class AutoFuzzerTester : MonoBehaviour
{
    public const int DefaultBurstIterations = 1200;
    public const int HistoryTurnCount = 250;

    private static readonly string[] BehaviorPatterns =
    {
        "PATTERN_BASIC_SLIME",
        "PATTERN_AGILE_STALKER",
        "PATTERN_HEAVY_TITAN",
        "PATTERN_FEINT_DANCER"
    };

    private static readonly int[] InvalidTurns = { -5, 9999, 0, -1, 251, 100000 };
    private static readonly string[] InvalidFlagKeys =
    {
        null,
        string.Empty,
        "   ",
        "FLAG_DOES_NOT_EXIST",
        "HIST_HALLUCINATED_FLAG",
        "@@@invalid@@@",
        "ERA_NOT_A_REAL_TAG",
        "A_VERY_LONG_INVALID_FLAG_KEY_THAT_SHOULD_SAFE_FAIL_WITHOUT_THROWING"
    };

    private static readonly string[] InvalidEnemyIds =
    {
        null,
        string.Empty,
        "ENEMY_DOES_NOT_EXIST",
        "ENEMY_HALLUCINATED_WYRM",
        "SLIME???"
    };

    private static readonly string[] InvalidItemIds =
    {
        null,
        string.Empty,
        "ITEM_HALLUCINATED_CORE",
        "MAT_DOES_NOT_EXIST",
        "WEAPON_???"
    };

    private static readonly float[] ExtremePurity =
    {
        -999f, -1f, 0f, 80f, 1e7f, 1e20f, float.MaxValue, float.NaN, float.NegativeInfinity, float.PositiveInfinity
    };

    private static readonly float[] ExtremeDensity =
    {
        -999f, -0.5f, 0f, 40f, 1e8f, float.MaxValue, float.NaN, float.PositiveInfinity
    };

    [Header("実行設定")]
    [SerializeField] private int operationsPerFrame = 400;
    [SerializeField] private int burstIterationTarget = DefaultBurstIterations;
    [SerializeField] private bool muteLogsDuringBurst = true;
    [SerializeField] private bool stopOnFirstBug;

    private bool isRunning;
    private int completedIterations;
    private int detectedBugs;
    private int safeFailHits;
    private bool canonSweepDone;
    private Coroutine activeRunCoroutine;
    private CombatStats fuzzCombatStats;
    private readonly List<string> actionHistory = new List<string>(24);
    private static int lastHotkeyToggleFrame = -1;

    public int CompletedIterations => completedIterations;
    public int DetectedBugs => detectedBugs;
    public int SafeFailHits => safeFailHits;

    private void Awake()
    {
        EnsureCombatStatsHost();
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
        if (Time.frameCount == lastHotkeyToggleFrame || !IsShiftF12Pressed())
        {
            return;
        }

        lastHotkeyToggleFrame = Time.frameCount;
        AutoFuzzerTester fuzzer = FindAnyObjectByType<AutoFuzzerTester>();
        if (fuzzer == null)
        {
            fuzzer = EnsureInstance();
        }

        fuzzer.ToggleFuzzRun();
    }

    private static bool IsShiftF12Pressed()
    {
        if (!DebugHotkeyUtility.TryGetKeyboard(out Keyboard keyboard))
        {
            return false;
        }

        return keyboard.shiftKey.isPressed && keyboard.f12Key.wasPressedThisFrame;
    }

    /// <summary>シーンに無い場合は DebugSystemsHub へ生成します。</summary>
    public static AutoFuzzerTester EnsureInstance()
    {
        AutoFuzzerTester existing = FindAnyObjectByType<AutoFuzzerTester>();
        if (existing != null)
        {
            return existing;
        }

        GameObject hub = GameObject.Find("DebugSystemsHub");
        GameObject host = hub != null ? hub : new GameObject(nameof(AutoFuzzerTester));
        AutoFuzzerTester onHost = host.GetComponent<AutoFuzzerTester>();
        return onHost != null ? onHost : host.AddComponent<AutoFuzzerTester>();
    }

    [ContextMenu("Toggle Auto Fuzzer (Shift+F12)")]
    public void ToggleFuzzRun()
    {
        if (isRunning)
        {
            StopFuzzRun();
            return;
        }

        StartFuzzRun();
    }

    [ContextMenu("Run 1200 Burst Now")]
    public void StartFuzzRun()
    {
        if (isRunning)
        {
            StopFuzzRun();
            return;
        }

        if (Application.isPlaying)
        {
            activeRunCoroutine = StartCoroutine(RunPlayModeBurstCoroutine());
            return;
        }

        RunSynchronousBurst(Mathf.Max(1, burstIterationTarget));
    }

    public void StopFuzzRun()
    {
        isRunning = false;
        if (activeRunCoroutine != null)
        {
            StopCoroutine(activeRunCoroutine);
            activeRunCoroutine = null;
        }

        Debug.Log(
            $"<color=#90A4AE>[AutoFuzzerTester] 停止 — 完了 {completedIterations} / バグ {detectedBugs} / Safe-Fail {safeFailHits}</color>");
    }

    /// <summary>
    /// コルーチン無しで指定回数を一気に実行します（Edit Mode メニュー / Play バーストの本体）。
    /// </summary>
    public int RunSynchronousBurst(int iterations)
    {
        PrepareRun();
        isRunning = true;
        bool restoreLog = Debug.unityLogger.logEnabled;
        if (muteLogsDuringBurst)
        {
            Debug.unityLogger.logEnabled = false;
        }

        try
        {
            RunCanonHistorySweep();
            int target = Mathf.Max(1, iterations);
            for (int i = 0; i < target && isRunning; i++)
            {
                RunOneIteration(i);
                completedIterations++;
            }
        }
        catch (Exception exception)
        {
            ReportBug("Exception", "RunSynchronousBurst", exception.Message, exception);
        }
        finally
        {
            Debug.unityLogger.logEnabled = restoreLog;
            isRunning = false;
            LogSummary("同期バースト");
        }

        return detectedBugs;
    }

    private IEnumerator RunPlayModeBurstCoroutine()
    {
        PrepareRun();
        isRunning = true;
        int target = Mathf.Max(1, burstIterationTarget);
        int perFrame = Mathf.Max(32, operationsPerFrame);
        bool restoreLog = Debug.unityLogger.logEnabled;

        Debug.Log(
            $"<color=#CE93D8><b>[AutoFuzzerTester]</b> 開始 — 目標 {target} 回 / 1フレーム {perFrame} 操作 " +
            $"（Shift+F12 で停止）</color>");

        if (muteLogsDuringBurst)
        {
            Debug.unityLogger.logEnabled = false;
        }

        RunCanonHistorySweep();

        while (isRunning && completedIterations < target)
        {
            int batch = Mathf.Min(perFrame, target - completedIterations);
            for (int i = 0; i < batch && isRunning; i++)
            {
                RunOneIteration(completedIterations);
                completedIterations++;
                if (stopOnFirstBug && detectedBugs > 0)
                {
                    isRunning = false;
                    break;
                }
            }

            yield return null;
        }

        Debug.unityLogger.logEnabled = restoreLog;
        activeRunCoroutine = null;
        isRunning = false;
        LogSummary("Play バースト");
    }

    private void PrepareRun()
    {
        completedIterations = 0;
        detectedBugs = 0;
        safeFailHits = 0;
        canonSweepDone = false;
        actionHistory.Clear();
        HistoryFlagRegistry.EnsureWired();
        EraContextResolver.EnsureInstance();
        MasterDataManager.EnsureInstance();
        InventoryManager.EnsureInstance();
        EnsureCombatStatsHost();
    }

    private void EnsureCombatStatsHost()
    {
        fuzzCombatStats = GetComponent<CombatStats>();
        if (fuzzCombatStats == null)
        {
            fuzzCombatStats = gameObject.AddComponent<CombatStats>();
        }
    }

    private void RunCanonHistorySweep()
    {
        if (canonSweepDone)
        {
            return;
        }

        RecordAction("CanonSweep(T1-250)");
        for (int turn = 1; turn <= HistoryTurnCount && isRunning; turn++)
        {
            FuzzHistoryTurn(turn, importFlags: true);
        }

        canonSweepDone = true;
        safeFailHits++;
    }

    private void RunOneIteration(int index)
    {
        int turn = 1 + (index % HistoryTurnCount);
        if (index % 11 == 0)
        {
            turn = InvalidTurns[index % InvalidTurns.Length];
        }

        FuzzHistoryTurn(turn, importFlags: index % 7 == 0);
        FuzzInvalidFlags(index);
        FuzzEnemyProfiles(index);
        FuzzEquipment(index);
        FuzzDrops(index);
    }

    private void FuzzHistoryTurn(int turn, bool importFlags)
    {
        RecordAction($"TrySetCurrentTurn({turn})");
        try
        {
            bool accepted = EraContextResolver.TrySetCurrentTurn(turn);
            if (!accepted)
            {
                safeFailHits++;
            }

            EraTag era = EraContextResolver.ResolveEraTag(turn);
            if (!EraContextResolver.IsValidTurn(turn) && era != EraTag.Early)
            {
                ReportBug(
                    "StateContradiction",
                    $"ResolveEraTag({turn})",
                    "無効ターンなのに普遍期へフォールバックしていません");
            }

            if (importFlags)
            {
                RecordAction($"ImportHistoryFlagsForTurn({turn})");
                List<string> flags = HistorySimulationLoader.ImportHistoryFlagsForTurn(turn);
                if (flags == null)
                {
                    ReportBug("StateContradiction", $"ImportHistoryFlagsForTurn({turn})", "戻り値が null です");
                }
                else if (!EraContextResolver.IsValidTurn(turn) && flags.Count == 0)
                {
                    safeFailHits++;
                }
            }

            Func<string, bool> resolver = SkillEvolutionLinker.ConditionFlagResolver;
            if (resolver != null)
            {
                resolver("ERA_EARLY");
                resolver("ERA_ANCIENT");
                resolver("HIST_INITIAL_DECODING_COMPLETE");
            }

            MasterDataManager master = MasterDataManager.Instance;
            if (master != null && master.TryGetMagic("MAGIC_FIRE_SPARK", out MagicMasterData spark))
            {
                ResolvedMagicParameters resolved = MagicCustomizer.Resolve(spark, null);
                if (float.IsNaN(resolved.EffectiveManaCost) || float.IsInfinity(resolved.EffectiveManaCost) ||
                    resolved.EffectiveManaCost < 0f)
                {
                    ReportBug(
                        "Overflow",
                        "MagicCustomizer.Resolve",
                        $"manaCost が不正です: {resolved.EffectiveManaCost}");
                }
            }
        }
        catch (Exception exception)
        {
            ReportBug("Exception", $"FuzzHistoryTurn({turn})", exception.Message, exception);
        }
    }

    private void FuzzInvalidFlags(int index)
    {
        string flag = InvalidFlagKeys[index % InvalidFlagKeys.Length];
        RecordAction($"ConditionFlagResolver({flag ?? "null"})");
        try
        {
            bool satisfied = HistoryFlagRegistry.IsSatisfied(flag);
            Func<string, bool> resolver = SkillEvolutionLinker.ConditionFlagResolver;
            if (resolver != null)
            {
                resolver(flag);
            }

            HistoryFlagRegistry.Unlock(flag);

            if (string.IsNullOrWhiteSpace(flag) && !satisfied)
            {
                ReportBug(
                    "StateContradiction",
                    "HistoryFlagRegistry.IsSatisfied",
                    "空フラグは Safe-Fail で true であるべきです");
            }
            else
            {
                safeFailHits++;
            }
        }
        catch (Exception exception)
        {
            ReportBug("Exception", $"Unlock/IsSatisfied({flag})", exception.Message, exception);
        }
    }

    private void FuzzEnemyProfiles(int index)
    {
        string pattern = BehaviorPatterns[index % BehaviorPatterns.Length];
        RecordAction($"LoadBehavior({pattern})");
        try
        {
            EnemyBehaviorProfile profile = EnemyBehaviorProfileManager.LoadFromResources(pattern);
            if (profile == null || !profile.IsValid())
            {
                ReportBug("StateContradiction", $"LoadFromResources({pattern})", "4種プロファイルのロードに失敗しました");
                return;
            }

            EnemyAttackActionData initial = EnemyBehaviorProfileManager.ChooseInitialAttack(profile);
            if (initial == null)
            {
                ReportBug("StateContradiction", $"ChooseInitialAttack({pattern})", "1段目抽選が null です");
                return;
            }

            string bogusId = index % 2 == 0 ? "ATTACK_DOES_NOT_EXIST" : $"ATTACK_HALLUCINATION_{index}";
            RecordAction($"FindActionById({bogusId})");
            EnemyAttackActionData missing = EnemyBehaviorProfileManager.FindActionById(profile, bogusId);
            if (missing != null)
            {
                ReportBug("StateContradiction", $"FindActionById({bogusId})", "未登録 actionId がヒットしました");
            }
            else
            {
                safeFailHits++;
            }

            EnemyBehaviorProfileManager.FindActionById(profile, null);
            EnemyBehaviorProfileManager.FindActionById(null, initial.actionId);
            WalkComboWithForcedLoop(profile, initial);
        }
        catch (Exception exception)
        {
            ReportBug("Exception", $"FuzzEnemyProfiles({pattern})", exception.Message, exception);
        }
    }

    private void WalkComboWithForcedLoop(EnemyBehaviorProfile profile, EnemyAttackActionData start)
    {
        RecordAction($"ComboWalkInterrupt({profile.patternId})");
        HashSet<string> visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string currentId = start.actionId;
        string loopTarget = start.actionId;
        const int maxSteps = 32;

        for (int step = 0; step < maxSteps; step++)
        {
            if (string.IsNullOrWhiteSpace(currentId))
            {
                safeFailHits++;
                return;
            }

            if (!visited.Add(currentId))
            {
                safeFailHits++;
                return;
            }

            EnemyAttackActionData action = EnemyBehaviorProfileManager.FindActionById(profile, currentId);
            if (action == null)
            {
                safeFailHits++;
                return;
            }

            string nextId = action.nextComboActionId;
            if (string.IsNullOrWhiteSpace(nextId))
            {
                nextId = step >= 2 ? loopTarget : "ATTACK_FORCED_INVALID";
            }

            currentId = nextId;
        }

        safeFailHits++;
    }

    private void FuzzEquipment(int index)
    {
        float purity = ExtremePurity[index % ExtremePurity.Length];
        float density = ExtremeDensity[index % ExtremeDensity.Length];
        RecordAction($"ApplyEquipmentModifiers(Purity:{FormatFloat(purity)},Density:{FormatFloat(density)})");
        try
        {
            EnsureCombatStatsHost();
            ItemData weapon = new ItemData
            {
                id = "WEAPON_FUZZ_EDGE",
                itemName = "Fuzz Edge",
                weight = 1f,
                maxStack = 1,
                itemType = "Equipment",
                hasCraftHiddenParams = true,
                purity = purity,
                density = density
            };

            fuzzCombatStats.ApplyEquipmentModifiers(weapon);
            int str = fuzzCombatStats.Strength;
            int bonus = fuzzCombatStats.EquipmentStrengthBonus;
            float poise = fuzzCombatStats.EquipmentPoiseDamageMultiplier;
            float stamina = fuzzCombatStats.EquipmentStaminaCostMultiplier;

            if (str < 0 || bonus < 0 || bonus > EquipmentStatFeedback.StrengthBonusMax)
            {
                ReportBug(
                    "Overflow",
                    "ApplyEquipmentModifiers",
                    $"STR 補正が破綻しています STR={str} bonus={bonus}");
                return;
            }

            if (float.IsNaN(poise) || float.IsInfinity(poise) || float.IsNaN(stamina) || float.IsInfinity(stamina))
            {
                ReportBug("Overflow", "ApplyEquipmentModifiers", $"倍率が非有限です poise={poise} stamina={stamina}");
                return;
            }

            if (poise < EquipmentStatFeedback.PoiseMultiplierMin - 0.001f ||
                poise > EquipmentStatFeedback.PoiseMultiplierMax + 0.001f ||
                stamina < EquipmentStatFeedback.StaminaMultiplierMin - 0.001f ||
                stamina > EquipmentStatFeedback.StaminaMultiplierMax + 0.001f)
            {
                if (fuzzCombatStats.EquipmentStrengthBonus > 0 || weapon.TryGetCraftHiddenParams(out _, out _))
                {
                    ReportBug(
                        "StateContradiction",
                        "ApplyEquipmentModifiers",
                        $"クランプ範囲外 poise={poise:F3} stamina={stamina:F3}");
                    return;
                }
            }

            safeFailHits++;
        }
        catch (OverflowException overflow)
        {
            ReportBug("Overflow", "ApplyEquipmentModifiers", overflow.Message, overflow);
        }
        catch (Exception exception)
        {
            ReportBug("Exception", "ApplyEquipmentModifiers", exception.Message, exception);
        }
    }

    private void FuzzDrops(int index)
    {
        string enemyId = InvalidEnemyIds[index % InvalidEnemyIds.Length];
        string itemId = InvalidItemIds[index % InvalidItemIds.Length];
        RecordAction($"ProcessEnemyDeathRewards({enemyId ?? "null"})");
        try
        {
            EnemyDropExecutor.ProcessEnemyDeathRewards(enemyId);
            RecordAction($"TryProcessStandaloneItemId({itemId ?? "null"})");
            bool granted = EnemyDropExecutor.TryProcessStandaloneItemId(itemId);
            if (granted && IsHallucinatedId(itemId))
            {
                ReportBug(
                    "StateContradiction",
                    $"TryProcessStandaloneItemId({itemId})",
                    "未登録 itemId がインベントリへ追加されました");
                return;
            }

            safeFailHits++;
        }
        catch (Exception exception)
        {
            ReportBug("Exception", "EnemyDropExecutor", exception.Message, exception);
        }
    }

    private static bool IsHallucinatedId(string id)
    {
        return !string.IsNullOrWhiteSpace(id) &&
               (id.IndexOf("HALLUCIN", StringComparison.OrdinalIgnoreCase) >= 0 ||
                id.IndexOf("DOES_NOT_EXIST", StringComparison.OrdinalIgnoreCase) >= 0 ||
                id.IndexOf("???", StringComparison.OrdinalIgnoreCase) >= 0);
    }

    private void RecordAction(string action)
    {
        if (actionHistory.Count >= 16)
        {
            actionHistory.RemoveAt(0);
        }

        actionHistory.Add(action);
    }

    private void ReportBug(string errorType, string action, string message, Exception exception = null)
    {
        detectedBugs++;
        RecordAction(action);
        AutoFuzzerErrorPacket packet = new AutoFuzzerErrorPacket
        {
            fuzzerTestId = Guid.NewGuid().ToString(),
            errorType = errorType,
            targetTurn = EraContextResolver.CurrentTurn,
            lastActionHistory = actionHistory.ToArray(),
            errorMessage = string.IsNullOrWhiteSpace(message)
                ? action
                : exception != null
                    ? $"{message} | {exception.GetType().Name}"
                    : message
        };

        string json = JsonUtility.ToJson(packet, true);
        bool loggerWasEnabled = Debug.unityLogger.logEnabled;
        Debug.unityLogger.logEnabled = true;
        Debug.LogError(json);
        if (muteLogsDuringBurst && isRunning)
        {
            Debug.unityLogger.logEnabled = false;
        }
        else
        {
            Debug.unityLogger.logEnabled = loggerWasEnabled;
        }
        if (stopOnFirstBug)
        {
            isRunning = false;
        }
    }

    private void LogSummary(string mode)
    {
        string color = detectedBugs == 0 ? "#69F0AE" : "#FF5252";
        Debug.Log(
            $"<color={color}><b>[AutoFuzzerTester] {mode} 完了</b></color> " +
            $"iterations={completedIterations} / exceptions+bugs={detectedBugs} / safe-fail={safeFailHits} " +
            $"{(detectedBugs == 0 ? "→ 0 exceptions / safe-fail 成功" : "→ 要調査")}");
    }

    private static string FormatFloat(float value)
    {
        if (float.IsNaN(value))
        {
            return "NaN";
        }

        if (float.IsPositiveInfinity(value))
        {
            return "+Inf";
        }

        if (float.IsNegativeInfinity(value))
        {
            return "-Inf";
        }

        return value.ToString("G6");
    }
}

/// <summary>Play 開始時に AutoFuzzerTester を DebugSystemsHub へ配置します。</summary>
public static class AutoFuzzerTesterBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AttachToDebugSystemsHub()
    {
        GameObject hub = GameObject.Find("DebugSystemsHub");
        GameObject host = hub != null ? hub : new GameObject(nameof(AutoFuzzerTester));
        if (host.GetComponent<AutoFuzzerTester>() == null)
        {
            host.AddComponent<AutoFuzzerTester>();
        }
    }
}

/// <summary>エディタメニューから高速ファジングを起動します。</summary>
public static class AutoFuzzerTesterMenu
{
    private const string ResultFileName = "Logs/auto_fuzzer_result.txt";

    [MenuItem("Tools/Demo/Auto Fuzzer Tester/Run 1200 Burst Now")]
    private static void RunBurstNow()
    {
        RunBurstInternal(quitEditor: false);
    }

    /// <summary>Unity バッチモード用: -executeMethod AutoFuzzerTesterMenu.BatchRunBurstAndQuit</summary>
    public static void BatchRunBurstAndQuit()
    {
        int bugs = RunBurstInternal(quitEditor: false);
        EditorApplication.Exit(bugs == 0 ? 0 : 1);
    }

    private static int RunBurstInternal(bool quitEditor)
    {
        AutoFuzzerTester fuzzer = AutoFuzzerTester.EnsureInstance();
        int bugs = fuzzer.RunSynchronousBurst(AutoFuzzerTester.DefaultBurstIterations);
        string line = bugs == 0
            ? $"PASS iterations={fuzzer.CompletedIterations} bugs=0 safe-fail OK"
            : $"FAIL iterations={fuzzer.CompletedIterations} bugs={bugs}";
        try
        {
            string dir = System.IO.Path.GetDirectoryName(ResultFileName);
            if (!string.IsNullOrEmpty(dir))
            {
                System.IO.Directory.CreateDirectory(dir);
            }

            System.IO.File.WriteAllText(ResultFileName, line + Environment.NewLine);
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[AutoFuzzerTester] 結果ファイル書き込み失敗: {exception.Message}");
        }

        if (bugs == 0)
        {
            Debug.Log($"<color=#69F0AE>[AutoFuzzerTester] 1200 Burst PASS（0 exceptions / safe-fail 成功） {line}</color>");
        }

        if (quitEditor)
        {
            EditorApplication.Exit(bugs == 0 ? 0 : 1);
        }

        return bugs;
    }

    [MenuItem("Tools/Demo/Auto Fuzzer Tester/Toggle Play Loop (Shift+F12)")]
    private static void TogglePlayLoop()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("[AutoFuzzerTester] Play ループは Play モード中のみトグルできます。同期バーストは Run 1200 Burst Now を使ってください。");
            return;
        }

        AutoFuzzerTester.EnsureInstance().ToggleFuzzRun();
    }

    [MenuItem("Tools/Demo/Auto Fuzzer Tester/Stop")]
    private static void Stop()
    {
        AutoFuzzerTester[] fuzzers = UnityEngine.Object.FindObjectsByType<AutoFuzzerTester>(FindObjectsInactive.Include);
        for (int i = 0; i < fuzzers.Length; i++)
        {
            fuzzers[i].StopFuzzRun();
        }
    }
}

#endif

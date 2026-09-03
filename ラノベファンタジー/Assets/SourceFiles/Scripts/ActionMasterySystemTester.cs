#if UNITY_EDITOR

using System.Collections;
using UnityEngine;
using UnityEditor;

/// <summary>
/// 技の熟練度（Mastery）システムの自動検証コンポーネント（エディタ専用）。
/// </summary>
[DefaultExecutionOrder(1001)]
public class ActionMasterySystemTester : MonoBehaviour
{
    private const string TestActionId = "test_mastery_auto_action";

    [Header("参照（未設定ならシーンから自動検索）")]
    [SerializeField] private ActionMasteryManager masteryManager;
    [SerializeField] private PlayerCombatInspirationBridge inspirationBridge;
    [SerializeField] private PlayerActionLogger actionLogger;

    [Header("実行設定")]
    [SerializeField] private bool runTestsOnStart;
    [Tooltip("ON のとき DemoTimeLineManager 存在中は自動テストを実行しません")]
    [SerializeField] private bool deferToDemoTimeline = true;

    private int passCount;
    private int failCount;

    private void Start()
    {
        if (!runTestsOnStart)
        {
            return;
        }

        if (deferToDemoTimeline && DemoTimeLineManager.Instance != null)
        {
            return;
        }

        RunAllTestsNow();
    }

    /// <summary>手動またはメニューから全テストを実行します。</summary>
    public void RunAllTestsNow()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("[ActionMasterySystemTester] Play モード中のみ実行できます。");
            return;
        }

        StartCoroutine(RunAllTestsAfterSceneReady());
    }

    /// <summary>通常斬りに極意を付与します（技開発 F5 の準備用）。</summary>
    public void GrantBasicSlashMasteryForTesting()
    {
        CacheReferences();
        if (masteryManager == null)
        {
            Debug.LogWarning("[ActionMasterySystemTester] ActionMasteryManager が見つかりません。");
            return;
        }

        masteryManager.GrantMasteryForTesting(ActionIds.BasicSlash);
        Debug.Log("<color=#B388FF>[ActionMasterySystemTester] 通常斬りに極意を付与しました（F5 技開発の準備完了）</color>");
    }

    [ContextMenu("Run All Mastery Tests Now")]
    private void RunAllTestsFromContextMenu()
    {
        RunAllTestsNow();
    }

    private IEnumerator RunAllTestsAfterSceneReady()
    {
        yield return null;
        yield return new WaitForEndOfFrame();

        CacheReferences();

        if (!ValidateReferences())
        {
            yield break;
        }

        passCount = 0;
        failCount = 0;

        LogHeader("技の熟練度（Mastery）システム自動テスト開始");

        RunHitIncrementTest();
        RunMasteryThresholdTest();
        RunNoIncrementAfterMasteredTest();
        RunEmptyActionIdIgnoredTest();
        RunBridgeNotifyTest();
        RunAttackLogDoesNotIncrementTest();
        RunTryGetMasteryStateTest();
        RunGetRequiredMasteryTest();

        LogHeader($"テスト完了 — 成功: {passCount} / 失敗: {failCount}");
    }

    private void CacheReferences()
    {
        if (masteryManager == null)
        {
            masteryManager = ActionMasteryManager.Instance;
        }

        if (masteryManager == null)
        {
            masteryManager = FindAnyObjectByType<ActionMasteryManager>();
        }

        if (inspirationBridge == null)
        {
            inspirationBridge = PlayerCombatInspirationBridge.Instance;
        }

        if (inspirationBridge == null)
        {
            inspirationBridge = FindAnyObjectByType<PlayerCombatInspirationBridge>();
        }

        if (actionLogger == null)
        {
            actionLogger = PlayerActionLogger.Instance;
        }

        EnsureInspirationBridge();
    }

    /// <summary>
    /// シーン内の PlayerCombatInspirationBridge を検索して参照をキャッシュします。
    /// 未配置の場合はテスト5（ブリッジ経由）のみスキップし、自動追加は行いません。
    /// </summary>
    private void EnsureInspirationBridge()
    {
        if (inspirationBridge == null)
        {
            inspirationBridge = PlayerCombatInspirationBridge.Instance;
        }

        if (inspirationBridge == null)
        {
            inspirationBridge = FindAnyObjectByType<PlayerCombatInspirationBridge>();
        }
    }

    private bool ValidateReferences()
    {
        if (masteryManager == null)
        {
            LogFail("セットアップ", "ActionMasteryManager が見つかりません。シーンまたは PlayerRobot にアタッチしてください。");
            return false;
        }

        return true;
    }

    private void PrepareCleanState()
    {
        masteryManager.ResetAllMasteryForTesting();
        masteryManager.InitializeDefaultConfigs();
    }

    // -------------------------------------------------------------------------
    // テスト1：敵ヒットで熟練度 +1
    // -------------------------------------------------------------------------
    private void RunHitIncrementTest()
    {
        const string testName = "敵ヒットで熟練度+1";

        PrepareCleanState();
        masteryManager.AddMasteryOnEnemyHit(ActionIds.BasicSlash);

        if (masteryManager.TryGetMasteryState(
                ActionIds.BasicSlash, out int current, out bool isMastered) &&
            current == 1 && !isMastered)
        {
            LogPass(testName, "通常斬りの currentMastery が 1 になりました。");
        }
        else
        {
            LogFail(testName, $"期待: current=1, isMastered=false / 実際: current={current}, isMastered={isMastered}");
        }
    }

    // -------------------------------------------------------------------------
    // テスト2：規定ヒット数で極意到達（isMastered = true）
    // -------------------------------------------------------------------------
    private void RunMasteryThresholdTest()
    {
        const string testName = "極意到達（isMastered）";

        PrepareCleanState();

        int required = masteryManager.GetRequiredMastery(ActionIds.BasicSlash);
        bool reachedOnLastHit = false;

        for (int i = 0; i < required; i++)
        {
            reachedOnLastHit = masteryManager.AddMasteryOnEnemyHit(ActionIds.BasicSlash);
        }

        bool isMastered = masteryManager.IsActionMastered(ActionIds.BasicSlash);
        masteryManager.TryGetMasteryState(ActionIds.BasicSlash, out int current, out bool stateFlag);

        if (reachedOnLastHit &&
            isMastered &&
            stateFlag &&
            current == required)
        {
            LogPass(testName,
                $"通常斬りで {required} ヒット後に isMastered=true（current={current}/{required}）。");
        }
        else
        {
            LogFail(testName,
                $"reachedOnLastHit={reachedOnLastHit}, isMastered={isMastered}, current={current}, required={required}");
        }
    }

    // -------------------------------------------------------------------------
    // テスト3：極意到達後は加算しない
    // -------------------------------------------------------------------------
    private void RunNoIncrementAfterMasteredTest()
    {
        const string testName = "極意到達後は加算停止";

        PrepareCleanState();

        int required = masteryManager.GetRequiredMastery(ActionIds.StrongStrike);
        for (int i = 0; i < required; i++)
        {
            masteryManager.AddMasteryOnEnemyHit(ActionIds.StrongStrike);
        }

        bool extraHitResult = masteryManager.AddMasteryOnEnemyHit(ActionIds.StrongStrike);
        masteryManager.TryGetMasteryState(ActionIds.StrongStrike, out int current, out bool isMastered);

        if (!extraHitResult && isMastered && current == required)
        {
            LogPass(testName, "極意到達後の追加ヒットは熟練度を増やしませんでした。");
        }
        else
        {
            LogFail(testName,
                $"extraHitResult={extraHitResult}, current={current}, required={required}, isMastered={isMastered}");
        }
    }

    // -------------------------------------------------------------------------
    // テスト4：空 actionID は無視
    // -------------------------------------------------------------------------
    private void RunEmptyActionIdIgnoredTest()
    {
        const string testName = "空 actionID 無視";

        PrepareCleanState();

        bool resultNull = masteryManager.AddMasteryOnEnemyHit(null);
        bool resultEmpty = masteryManager.AddMasteryOnEnemyHit(string.Empty);
        bool resultWhitespace = masteryManager.AddMasteryOnEnemyHit("   ");

        masteryManager.TryGetMasteryState(ActionIds.BasicSlash, out int current, out _);

        if (!resultNull && !resultEmpty && !resultWhitespace && current == 0)
        {
            LogPass(testName, "無効な actionID では熟練度が変化しませんでした。");
        }
        else
        {
            LogFail(testName, $"無効 ID で結果が true になる、または current={current} が 0 ではありません。");
        }
    }

    // -------------------------------------------------------------------------
    // テスト5：ブリッジ経由のヒット通知
    // -------------------------------------------------------------------------
    private void RunBridgeNotifyTest()
    {
        const string testName = "ブリッジ経由ヒット通知";

        EnsureInspirationBridge();

        if (inspirationBridge == null)
        {
            inspirationBridge = PlayerCombatInspirationBridge.Instance;
        }

        if (inspirationBridge == null)
        {
            LogFail(testName,
                "PlayerCombatInspirationBridge が見つかりません。PlayerRobot に ActionMasteryManager をアタッチしてください。");
            return;
        }

        PrepareCleanState();

        inspirationBridge.NotifyActionHitEnemy(ActionIds.FireSpark);
        inspirationBridge.NotifyActionHitEnemy(ActionIds.FireSpark);

        if (masteryManager.TryGetMasteryState(
                ActionIds.FireSpark, out int current, out bool isMastered) &&
            current == 2 && !isMastered)
        {
            LogPass(testName, "NotifyActionHitEnemy 経由で currentMastery=2 になりました。");
        }
        else
        {
            LogFail(testName, $"期待: current=2 / 実際: current={current}, isMastered={isMastered}");
        }
    }

    // -------------------------------------------------------------------------
    // テスト6：素振り（LogAttack のみ）では熟練度が上がらない
    // -------------------------------------------------------------------------
    private void RunAttackLogDoesNotIncrementTest()
    {
        const string testName = "素振り（LogAttack）非加算";

        PrepareCleanState();

        if (actionLogger != null)
        {
            for (int i = 0; i < 5; i++)
            {
                actionLogger.LogAttack();
            }
        }

        masteryManager.TryGetMasteryState(ActionIds.BasicSlash, out int current, out bool isMastered);

        if (current == 0 && !isMastered)
        {
            LogPass(testName, "LogAttack のみでは currentMastery は 0 のままです。");
        }
        else
        {
            LogFail(testName, $"LogAttack 後に current={current} になっています（期待: 0）。");
        }
    }

    // -------------------------------------------------------------------------
    // テスト7：TryGetMasteryState / IsActionMastered API
    // -------------------------------------------------------------------------
    private void RunTryGetMasteryStateTest()
    {
        const string testName = "状態取得 API";

        PrepareCleanState();

        bool queryBefore = masteryManager.TryGetMasteryState(
            ActionIds.BasicStep, out int beforeCurrent, out bool beforeMastered);

        masteryManager.SetMasteryForTesting(ActionIds.BasicStep, 7, false);

        bool queryAfter = masteryManager.TryGetMasteryState(
            ActionIds.BasicStep, out int afterCurrent, out bool afterMastered);

        masteryManager.SetMasteryForTesting(ActionIds.BasicStep, 10, true);

        bool isMastered = masteryManager.IsActionMastered(ActionIds.BasicStep);

        if (queryBefore && beforeCurrent == 0 && !beforeMastered &&
            queryAfter && afterCurrent == 7 && !afterMastered &&
            isMastered)
        {
            LogPass(testName, "TryGetMasteryState / IsActionMastered が期待どおり動作しました。");
        }
        else
        {
            LogFail(testName,
                $"before=({beforeCurrent},{beforeMastered}), after=({afterCurrent},{afterMastered}), isMastered={isMastered}");
        }
    }

    // -------------------------------------------------------------------------
    // テスト8：GetRequiredMastery（設定値参照）
    // -------------------------------------------------------------------------
    private void RunGetRequiredMasteryTest()
    {
        const string testName = "GetRequiredMastery";

        PrepareCleanState();

        int basicRequired = masteryManager.GetRequiredMastery(ActionIds.BasicSlash);
        int strongRequired = masteryManager.GetRequiredMastery(ActionIds.StrongStrike);
        int unknownRequired = masteryManager.GetRequiredMastery(TestActionId);

        if (basicRequired == 10 && strongRequired == 15 && unknownRequired >= 1)
        {
            LogPass(testName,
                $"通常斬り={basicRequired}, 強撃={strongRequired}, 未登録ID={unknownRequired}（デフォルト）。");
        }
        else
        {
            LogFail(testName,
                $"basic={basicRequired}, strong={strongRequired}, unknown={unknownRequired}");
        }
    }

    private void LogPass(string testName, string detail)
    {
        passCount++;
        Debug.Log($"[TEST SUCCESS] {testName} — {detail}");
    }

    private void LogFail(string testName, string detail)
    {
        failCount++;
        Debug.LogWarning($"[TEST FAILED] {testName} — {detail}");
    }

    private void LogHeader(string message)
    {
        Debug.Log($"[ActionMasterySystemTester] {message}");
    }
}

/// <summary>エディタメニューから Mastery テストを手動実行します。</summary>
public static class ActionMasterySystemTesterMenu
{
    [MenuItem("Tools/Demo/Run Action Mastery System Tests (Play Mode)")]
    private static void RunFromMenu()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("[ActionMasterySystemTester] Play モード中のみ実行できます。");
            return;
        }

        ResolveTester().RunAllTestsNow();
    }

    [MenuItem("Tools/Demo/Grant Basic Slash Mastery (Play Mode)")]
    private static void GrantBasicSlashFromMenu()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("[ActionMasterySystemTester] Play モード中のみ実行できます。");
            return;
        }

        ResolveTester().GrantBasicSlashMasteryForTesting();
    }

    private static ActionMasterySystemTester ResolveTester()
    {
        ActionMasterySystemTester tester = UnityEngine.Object.FindAnyObjectByType<ActionMasterySystemTester>();
        if (tester != null)
        {
            return tester;
        }

        GameObject hub = GameObject.Find("DebugSystemsHub");
        GameObject host = hub != null ? hub : new GameObject(nameof(ActionMasterySystemTester));
        return host.GetComponent<ActionMasterySystemTester>() ?? host.AddComponent<ActionMasterySystemTester>();
    }
}

#endif

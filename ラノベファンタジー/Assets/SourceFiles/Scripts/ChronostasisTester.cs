#if UNITY_EDITOR

using System.Collections;
using UnityEngine;
using UnityEditor;

/// <summary>
/// 超加速・世界遅延（クロックアップ）の検証用コンポーネント（エディタ専用）。
/// </summary>
[DefaultExecutionOrder(1006)]
public class ChronostasisTester : MonoBehaviour
{
    [Header("参照（未設定ならシーンから自動検索）")]
    [SerializeField] private SpeedDomainManager speedDomainManager;
    [SerializeField] private PlayerStatusManager playerStatusManager;
    [SerializeField] private PlayerSkillSlotManager skillSlotManager;

    [Header("実行設定")]
    [SerializeField] private bool runTestsOnStart;

    [Header("デバッグ上書き")]
    [Tooltip("ON の間、思考加速スキルを所持している扱いにする")]
    [SerializeField] private bool debugForceMindAccelerationSkill;

    private int passCount;
    private int failCount;
    private bool referencesCached;

    private void Start()
    {
        if (!runTestsOnStart)
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
            Debug.LogWarning("[ChronostasisTester] Play モード中のみ実行できます。");
            return;
        }

        StartCoroutine(RunAllTestsAfterSceneReady());
    }

    [ContextMenu("Simulate Enemy Ghost Speed (G)")]
    public void SimulateEnemyGhostSpeed()
    {
        CacheReferences();

        if (speedDomainManager == null)
        {
            Debug.LogWarning("[ChronostasisTester] SpeedDomainManager が見つかりません。");
            return;
        }

        if (playerStatusManager == null)
        {
            Debug.LogWarning("[ChronostasisTester] PlayerStatusManager が見つかりません。");
            return;
        }

        bool hasMindSkill = debugForceMindAccelerationSkill || HasMindAccelerationSkill();
        speedDomainManager.SimulateEnemyChronostasisAgainstPlayer(playerStatusManager, hasMindSkill);

        bool worldSlowed = speedDomainManager.IsWorldSlowed || Time.timeScale < 0.99f;
        RuntimeInGameUIManager.EnsureInstance().SetChronostasisFilter(worldSlowed);

        Debug.Log(
            $"[ChronostasisTester] 適応経路={speedDomainManager.LastAdaptationRoute}, " +
            $"実効倍率={speedDomainManager.GetPlayerDeltaTimeMultiplier():F1}x, " +
            $"フィジカルスコア={playerStatusManager.GetPhysicalAdaptationScore()}");
    }

    [ContextMenu("Run All Chronostasis Tests Now")]
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

        LogHeader("クロックアップ・適応判定 自動テスト開始");

        RunAdaptationFailTest();
        RunTechnicalAdaptationTest();
        RunPhysicalAdaptationTest();
        RunTimeScaleResetTest();

        speedDomainManager.ResetTimeScale();

        LogHeader($"テスト完了 — 成功: {passCount} / 失敗: {failCount}");
    }

    private void RunAdaptationFailTest()
    {
        const string testName = "適応失敗（絶望）";

        playerStatusManager.ResetToDefaultForTesting();
        debugForceMindAccelerationSkill = false;

        SpeedDomainManager.AdaptationRoute route =
            speedDomainManager.SimulateEnemyChronostasisAgainstPlayer(playerStatusManager, false);

        if (route == SpeedDomainManager.AdaptationRoute.None &&
            !speedDomainManager.IsPlayerAdaptedToEnemyDomain &&
            Mathf.Approximately(Time.timeScale, 0.05f))
        {
            LogPass(testName, "低ステータスで敵領域に拘束された。");
        }
        else
        {
            LogFail(testName, $"route={route}, adapted={speedDomainManager.IsPlayerAdaptedToEnemyDomain}");
        }
    }

    private void RunTechnicalAdaptationTest()
    {
        const string testName = "技術型突破";

        playerStatusManager.ResetToDefaultForTesting();
        playerStatusManager.AddVisibleBonuses(0, 0, speedDomainManager.TechnicalRouteMinSpeedBonus, 0, 0);
        debugForceMindAccelerationSkill = true;

        SpeedDomainManager.AdaptationRoute route =
            speedDomainManager.SimulateEnemyChronostasisAgainstPlayer(playerStatusManager, true);

        bool hadSpeedMultiplier = speedDomainManager.GetPlayerDeltaTimeMultiplier() > 1f;
        speedDomainManager.ResetTimeScale();

        if (route == SpeedDomainManager.AdaptationRoute.Technical && hadSpeedMultiplier)
        {
            LogPass(testName, "思考加速×速ボーナスで適応成功。");
        }
        else
        {
            LogFail(testName, $"route={route}");
        }
    }

    private void RunPhysicalAdaptationTest()
    {
        const string testName = "フィジカル型突破";

        playerStatusManager.ResetToDefaultForTesting();
        playerStatusManager.SetLevel(20);
        playerStatusManager.AddVisibleBonuses(20, 20, 20, 20, 20);
        debugForceMindAccelerationSkill = false;

        int score = playerStatusManager.GetPhysicalAdaptationScore();
        SpeedDomainManager.AdaptationRoute route =
            speedDomainManager.SimulateEnemyChronostasisAgainstPlayer(playerStatusManager, false);

        speedDomainManager.ResetTimeScale();

        if (route == SpeedDomainManager.AdaptationRoute.Physical && score >= speedDomainManager.PhysicalRouteThreshold)
        {
            LogPass(testName, $"スコア {score} で神域到達。");
        }
        else
        {
            LogFail(testName, $"route={route}, score={score}");
        }
    }

    private void RunTimeScaleResetTest()
    {
        const string testName = "時間復元";

        speedDomainManager.TriggerChronostasis(SpeedDomainManager.CasterEnemy);
        speedDomainManager.ResetTimeScale();

        if (!speedDomainManager.IsWorldSlowed && Mathf.Approximately(Time.timeScale, 1f))
        {
            LogPass(testName, "Time.timeScale が 1.0 に復元された。");
        }
        else
        {
            LogFail(testName, $"slowed={speedDomainManager.IsWorldSlowed}, scale={Time.timeScale}");
        }
    }

    private bool HasMindAccelerationSkill()
    {
        if (skillSlotManager == null)
        {
            skillSlotManager = FindAnyObjectByType<PlayerSkillSlotManager>();
        }

        return skillSlotManager != null && skillSlotManager.OwnsSkill(SkillIds.MindAcceleration);
    }

    private void CacheReferences()
    {
        if (speedDomainManager == null)
        {
            speedDomainManager = SpeedDomainManager.Instance;
        }

        if (speedDomainManager == null)
        {
            speedDomainManager = FindAnyObjectByType<SpeedDomainManager>();
        }

        if (playerStatusManager == null)
        {
            playerStatusManager = PlayerStatusManager.Instance;
        }

        if (playerStatusManager == null)
        {
            playerStatusManager = FindAnyObjectByType<PlayerStatusManager>();
        }

        if (skillSlotManager == null)
        {
            skillSlotManager = FindAnyObjectByType<PlayerSkillSlotManager>();
        }

        referencesCached = speedDomainManager != null;
    }

    private void EnsureReferencesCached()
    {
        if (!referencesCached)
        {
            CacheReferences();
        }
    }

    private bool ValidateReferences()
    {
        if (speedDomainManager == null)
        {
            LogFail("セットアップ", "SpeedDomainManager が見つかりません。DebugSystemsHub にアタッチしてください。");
            return false;
        }

        if (playerStatusManager == null)
        {
            LogFail("セットアップ", "PlayerStatusManager が見つかりません。PlayerRobot にアタッチしてください。");
            return false;
        }

        return true;
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
        Debug.Log($"[ChronostasisTester] {message}");
    }
}

public static class ChronostasisTesterMenu
{
    [MenuItem("Tools/Demo/Run Chronostasis Tests (Play Mode)")]
    private static void RunFromMenu()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("[ChronostasisTester] Play モード中のみ実行できます。");
            return;
        }

        ResolveTester().RunAllTestsNow();
    }

    private static ChronostasisTester ResolveTester()
    {
        ChronostasisTester tester = UnityEngine.Object.FindAnyObjectByType<ChronostasisTester>();
        if (tester != null)
        {
            return tester;
        }

        GameObject hub = GameObject.Find("DebugSystemsHub");
        GameObject host = hub != null ? hub : new GameObject(nameof(ChronostasisTester));
        return host.GetComponent<ChronostasisTester>() ?? host.AddComponent<ChronostasisTester>();
    }
}

#endif

#if UNITY_EDITOR

using System.Collections;
using UnityEngine;
using UnityEditor;

/// <summary>
/// PlayerStatusManager の検証用コンポーネント（エディタ専用）。
/// </summary>
[DefaultExecutionOrder(1005)]
public class PlayerStatusTester : MonoBehaviour
{
    [Header("参照（未設定ならシーンから自動検索）")]
    [SerializeField] private PlayerStatusManager statusManager;

    [Header("実行設定")]
    [SerializeField] private bool runTestsOnStart;

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
            Debug.LogWarning("[PlayerStatusTester] Play モード中のみ実行できます。");
            return;
        }

        StartCoroutine(RunAllTestsAfterSceneReady());
    }

    [ContextMenu("Run All Status Tests Now")]
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

        LogHeader("PlayerStatusManager 自動テスト開始");

        RunDefaultValuesTest();
        RunNPCTrustFactorTest();
        RunIntelSimulationTest();
        RunKarmaSimulationTest();
        RunImpulsiveKarmaPenaltyTest();
        RunMPRegenStubTest();

        LogHeader($"テスト完了 — 成功: {passCount} / 失敗: {failCount}");
    }

    private void CacheReferences()
    {
        if (statusManager == null)
        {
            statusManager = PlayerStatusManager.Instance;
        }

        if (statusManager == null)
        {
            statusManager = FindAnyObjectByType<PlayerStatusManager>();
        }

        referencesCached = statusManager != null;
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
        if (statusManager == null)
        {
            LogFail("セットアップ", "PlayerStatusManager が見つかりません。PlayerRobot にアタッチしてください。");
            return false;
        }

        return true;
    }

    private void RunDefaultValuesTest()
    {
        const string testName = "初期ステータス";

        statusManager.ResetToDefaultForTesting();

        if (statusManager.Level >= 1 &&
            statusManager.MaxMP > 0f &&
            statusManager.CurrentMP == statusManager.MaxMP)
        {
            LogPass(testName, $"Lv.{statusManager.Level}, MP={statusManager.CurrentMP}/{statusManager.MaxMP}");
        }
        else
        {
            LogFail(testName, "初期値が不正です。");
        }
    }

    private void RunNPCTrustFactorTest()
    {
        const string testName = "NPC信頼度計算";

        statusManager.ResetToDefaultForTesting();
        statusManager.AddIntelBonus(20);
        statusManager.AddKarmaValue(30);

        float trust = statusManager.GetNPCTrustFactor();

        if (Mathf.Approximately(trust, 5f))
        {
            LogPass(testName, $"信頼度係数 = {trust:F2}（(20+30)×0.1）");
        }
        else
        {
            LogFail(testName, $"期待: 5.00 / 実際: {trust:F2}");
        }
    }

    private void RunIntelSimulationTest()
    {
        const string testName = "インテリ変動シミュレーション";

        statusManager.ResetToDefaultForTesting();

        statusManager.SimulateDeepReading();
        int intelAfterRead = statusManager.IntelBonus;

        statusManager.SimulateImpulsiveAction();
        int intelAfterImpulse = statusManager.IntelBonus;
        int karmaAfterImpulse = statusManager.KarmaValue;

        if (intelAfterRead == 5 &&
            intelAfterImpulse == 0 &&
            karmaAfterImpulse == PlayerStatusManager.ImpulsiveActionKarmaPenalty)
        {
            LogPass(testName, "熟読+5 の後、短絡でインテリ0・カルマ-20 になった。");
        }
        else
        {
            LogFail(testName,
                $"intelRead={intelAfterRead}, intelAfter={intelAfterImpulse}, karma={karmaAfterImpulse}");
        }
    }

    private void RunKarmaSimulationTest()
    {
        const string testName = "カルマ変動シミュレーション";

        statusManager.ResetToDefaultForTesting();
        float trustBefore = statusManager.GetNPCTrustFactor();

        statusManager.SimulateGoodDeed();

        float trustAfter = statusManager.GetNPCTrustFactor();

        if (trustAfter > trustBefore)
        {
            LogPass(testName, $"善行で信頼度が上昇（{trustBefore:F2} → {trustAfter:F2}）");
        }
        else
        {
            LogFail(testName, $"信頼度 before={trustBefore:F2}, after={trustAfter:F2}");
        }
    }

    private void RunImpulsiveKarmaPenaltyTest()
    {
        const string testName = "短絡行動カルマペナルティ";

        statusManager.ResetToDefaultForTesting();
        statusManager.SimulateImpulsiveAction();
        statusManager.SimulateImpulsiveAction();

        if (statusManager.IsBelowShopKarmaThreshold())
        {
            LogPass(testName,
                $"カルマ {statusManager.KarmaValue} で門前払いライン（{PlayerStatusManager.DefaultShopKarmaBanThreshold}）を下回った。");
        }
        else
        {
            LogFail(testName, $"karma={statusManager.KarmaValue}");
        }
    }

    private void RunMPRegenStubTest()
    {
        const string testName = "MP消費スタブ";

        statusManager.ResetToDefaultForTesting();
        bool used = statusManager.TryUseMP(10f);

        if (used && statusManager.CurrentMP == statusManager.MaxMP - 10f)
        {
            LogPass(testName, $"MP消費成功（残り {statusManager.CurrentMP}/{statusManager.MaxMP}）");
        }
        else
        {
            LogFail(testName, $"used={used}, MP={statusManager.CurrentMP}/{statusManager.MaxMP}");
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
        Debug.Log($"[PlayerStatusTester] {message}");
    }
}

public static class PlayerStatusTesterMenu
{
    [MenuItem("Tools/Demo/Run Player Status Tests (Play Mode)")]
    private static void RunFromMenu()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("[PlayerStatusTester] Play モード中のみ実行できます。");
            return;
        }

        ResolveTester().RunAllTestsNow();
    }

    private static PlayerStatusTester ResolveTester()
    {
        PlayerStatusTester tester = UnityEngine.Object.FindAnyObjectByType<PlayerStatusTester>();
        if (tester != null)
        {
            return tester;
        }

        GameObject hub = GameObject.Find("DebugSystemsHub");
        GameObject host = hub != null ? hub : new GameObject(nameof(PlayerStatusTester));
        return host.GetComponent<PlayerStatusTester>() ?? host.AddComponent<PlayerStatusTester>();
    }
}

#endif

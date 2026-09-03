#if UNITY_EDITOR

using System.Collections;
using UnityEngine;
using UnityEditor;

/// <summary>
/// 魔法技術（魔導）システムの検証用コンポーネント（エディタ専用）。
/// </summary>
[DefaultExecutionOrder(1007)]
public class MagicTechTester : MonoBehaviour
{
    [Header("参照（未設定ならシーンから自動検索）")]
    [SerializeField] private PlayerMagicLibrary magicLibrary;
    [SerializeField] private PlayerStatusManager statusManager;

    [Header("テスト対象")]
    [SerializeField] private string testCastMagicId = MagicIds.FireEnchant;

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
            Debug.LogWarning("[MagicTechTester] Play モード中のみ実行できます。");
            return;
        }

        StartCoroutine(RunAllTestsAfterSceneReady());
    }

    [ContextMenu("Cast Test Magic (M)")]
    public void TryCastTestMagic()
    {
        CacheReferences();

        if (magicLibrary == null || statusManager == null)
        {
            Debug.LogWarning("[MagicTechTester] 参照が不足しています。");
            return;
        }

        magicLibrary.TryCastMagic(testCastMagicId, statusManager);
    }

    [ContextMenu("Record Sample Magic (O)")]
    public void RecordSampleMagic()
    {
        CacheReferences();

        if (magicLibrary == null)
        {
            Debug.LogWarning("[MagicTechTester] PlayerMagicLibrary が見つかりません。");
            return;
        }

        MagicTechData sample = magicLibrary.GetLearnedCountForTesting() == 0
            ? MagicTechData.CreateFireEnchantStub()
            : MagicTechData.CreateCustomArcaneBoltStub();

        Debug.Log(
            $"<color=#B388FF><b>[MagicTechTester]</b> Oキー: 収録試行 → {sample.magicID} ({sample.magicName})</color>");

        bool recorded = magicLibrary.RecordMagicViaBookOrMaster(sample, "魔導書解読");
        if (!recorded)
        {
            Debug.Log(
                $"<color=#AAAAAA>[MagicTechTester] 収録されませんでした（既に所持 or データ無効）。現在 {magicLibrary.GetLearnedCountForTesting()} 件</color>");
        }
    }

    [ContextMenu("Run All Magic Tech Tests Now")]
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

        LogHeader("魔法技術（魔導）自動テスト開始");

        RunRecordMagicTest();
        RunCastSuccessTest();
        RunCastFailInsufficientMpTest();

        LogHeader($"テスト完了 — 成功: {passCount} / 失敗: {failCount}");
    }

    private void RunRecordMagicTest()
    {
        const string testName = "魔導書収録";

        PrepareTestState();
        MagicTechData magic = MagicTechData.CreateFireEnchantStub();
        bool recorded = magicLibrary.RecordMagicViaBookOrMaster(magic, "魔導書解読");

        if (recorded && magicLibrary.GetLearnedCountForTesting() == 1)
        {
            LogPass(testName, $"{magic.magicID} をライブラリに収録。");
        }
        else
        {
            LogFail(testName, $"recorded={recorded}, count={magicLibrary.GetLearnedCountForTesting()}");
        }
    }

    private void RunCastSuccessTest()
    {
        const string testName = "魔導発動（MP消費）";

        PrepareTestState();
        magicLibrary.RecordMagicViaBookOrMaster(MagicTechData.CreateFireEnchantStub(), "伝授");

        float mpBefore = statusManager.CurrentMP;
        bool cast = magicLibrary.TryCastMagic(MagicIds.FireEnchant, statusManager);
        float mpAfter = statusManager.CurrentMP;

        if (cast && mpAfter < mpBefore && magicLibrary.IsWeaponEnchanted)
        {
            LogPass(testName, $"MP {mpBefore:F0} → {mpAfter:F0}、エンチャント ON。");
        }
        else
        {
            LogFail(testName, $"cast={cast}, mp={mpBefore}->{mpAfter}, enchant={magicLibrary.IsWeaponEnchanted}");
        }
    }

    private void RunCastFailInsufficientMpTest()
    {
        const string testName = "MP不足で不発";

        PrepareTestState();
        magicLibrary.RecordMagicViaBookOrMaster(MagicTechData.CreateFireEnchantStub(), "魔導書解読");
        statusManager.TryUseMP(statusManager.MaxMP);

        bool cast = magicLibrary.TryCastMagic(MagicIds.FireEnchant, statusManager);

        if (!cast && statusManager.CurrentMP <= 0f)
        {
            LogPass(testName, "MP 枯渇時に発動が拒否された。");
        }
        else
        {
            LogFail(testName, $"cast={cast}, mp={statusManager.CurrentMP}");
        }
    }

    private void PrepareTestState()
    {
        statusManager.ResetToDefaultForTesting();
        magicLibrary.ClearLibraryForTesting();
    }

    private void CacheReferences()
    {
        if (magicLibrary == null)
        {
            magicLibrary = PlayerMagicLibrary.Instance;
        }

        if (magicLibrary == null)
        {
            magicLibrary = FindAnyObjectByType<PlayerMagicLibrary>();
        }

        if (statusManager == null)
        {
            statusManager = PlayerStatusManager.Instance;
        }

        if (statusManager == null)
        {
            statusManager = FindAnyObjectByType<PlayerStatusManager>();
        }

        referencesCached = magicLibrary != null;
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
        if (magicLibrary == null)
        {
            LogFail("セットアップ", "PlayerMagicLibrary が見つかりません。PlayerRobot にアタッチしてください。");
            return false;
        }

        if (statusManager == null)
        {
            LogFail("セットアップ", "PlayerStatusManager が見つかりません。");
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
        Debug.Log($"[MagicTechTester] {message}");
    }
}

public static class MagicTechTesterMenu
{
    [MenuItem("Tools/Demo/Run Magic Tech Tests (Play Mode)")]
    private static void RunFromMenu()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("[MagicTechTester] Play モード中のみ実行できます。");
            return;
        }

        ResolveTester().RunAllTestsNow();
    }

    private static MagicTechTester ResolveTester()
    {
        MagicTechTester tester = UnityEngine.Object.FindAnyObjectByType<MagicTechTester>();
        if (tester != null)
        {
            return tester;
        }

        GameObject hub = GameObject.Find("DebugSystemsHub");
        GameObject host = hub != null ? hub : new GameObject(nameof(MagicTechTester));
        return host.GetComponent<MagicTechTester>() ?? host.AddComponent<MagicTechTester>();
    }
}

#endif

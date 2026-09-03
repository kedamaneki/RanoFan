#if UNITY_EDITOR

using System.Collections;
using UnityEngine;
using UnityEditor;

/// <summary>
/// 能動的・技開発システムの検証用コンポーネント（エディタ専用）。
/// </summary>
[DefaultExecutionOrder(1003)]
public class ActionDevelopmentTester : MonoBehaviour
{
    [Header("参照（未設定ならシーンから自動検索）")]
    [SerializeField] private InspirationManager inspirationManager;
    [SerializeField] private PlayerSkillSlotManager skillSlotManager;
    [SerializeField] private ActionMasteryManager masteryManager;

    [Header("テスト対象レシピ")]
    [SerializeField] private string testMaterialSkillId = SkillIds.DanceArt;
    [SerializeField] private string testMaterialActionId = ActionIds.BasicSlash;
    [SerializeField] private string testResultActionId = ActionIds.DanceBlade;

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
            Debug.LogWarning("[ActionDevelopmentTester] Play モード中のみ実行できます。");
            return;
        }

        StartCoroutine(RunAllTestsAfterSceneReady());
    }

    [ContextMenu("Run All Development Tests Now")]
    private void RunAllTestsFromContextMenu()
    {
        RunAllTestsNow();
    }

    [ContextMenu("Run Manual Development (F5)")]
    public void RunManualDevelopmentTest()
    {
        CacheReferences();
        if (inspirationManager == null || skillSlotManager == null)
        {
            Debug.LogWarning("[ActionDevelopmentTester] 参照が不足しています。");
            return;
        }

        EnsureManualTestPrerequisites();
        bool success = inspirationManager.TryDevelopAction(
            testMaterialSkillId, testMaterialActionId, skillSlotManager);

        if (success)
        {
            Debug.Log(
                $"<color=#00E676>[ActionDevelopmentTester] 手動開発成功: {testResultActionId}</color>");

            RuntimeInGameUIManager.EnsureInstance().ShowInspirationPopup(
                "ピキーン！ 剣の舞",
                "能動的技開発（F5）に成功",
                RuntimeUIThemeColors.DevelopmentGreen);
            return;
        }

        Debug.Log(
            $"<color=#FF8A80>[ActionDevelopmentTester] 手動開発失敗: {DescribeManualFailureReason()}</color>");
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

        LogHeader("能動的・技開発システム 自動テスト開始");

        RunPrerequisiteFailTest();
        RunMasteryGateFailTest();
        RunDanceBladeDevelopmentTest();
        RunDuplicateDevelopmentFailTest();

        PrepareManualPlayground();

        LogHeader($"テスト完了 — 成功: {passCount} / 失敗: {failCount}");
    }

    private void CacheReferences()
    {
        if (inspirationManager == null)
        {
            inspirationManager = InspirationManager.Instance;
        }

        if (inspirationManager == null)
        {
            inspirationManager = FindAnyObjectByType<InspirationManager>();
        }

        if (skillSlotManager == null)
        {
            skillSlotManager = FindAnyObjectByType<PlayerSkillSlotManager>();
        }

        if (masteryManager == null)
        {
            masteryManager = ActionMasteryManager.Instance;
        }

        if (masteryManager == null)
        {
            masteryManager = FindAnyObjectByType<ActionMasteryManager>();
        }

        referencesCached = inspirationManager != null && skillSlotManager != null;
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
        if (inspirationManager == null || skillSlotManager == null)
        {
            LogFail("セットアップ", "InspirationManager または PlayerSkillSlotManager が見つかりません。");
            return false;
        }

        if (masteryManager == null)
        {
            LogFail("セットアップ", "ActionMasteryManager が見つかりません。");
            return false;
        }

        return true;
    }

    /// <summary>手動 F5 用：舞踏術の確保とレシピ初期化のみ（熟練度は維持）。</summary>
    private void EnsureManualTestPrerequisites()
    {
        inspirationManager.ResetDevelopmentRecipesForTesting();

        if (!skillSlotManager.OwnsSkill(testMaterialSkillId))
        {
            skillSlotManager.AddDanceArtSkillForTesting();
        }
    }

    /// <summary>
    /// 自動テスト後に手動確認用の初期状態へ戻します。
    /// 剣の舞を除去し極意もリセットするため、F5→1→F5 の流れが再現できます。
    /// </summary>
    private void PrepareManualPlayground()
    {
        skillSlotManager.ResetToDefaultForTesting();
        inspirationManager.ResetDevelopmentRecipesForTesting();
        skillSlotManager.AddDanceArtSkillForTesting();
        masteryManager?.ResetAllMasteryForTesting();
    }

    private string DescribeManualFailureReason()
    {
        if (!skillSlotManager.OwnsSkill(testMaterialSkillId))
        {
            return "舞踏術未所持";
        }

        if (!skillSlotManager.IsActionOwnedAnywhere(testMaterialActionId))
        {
            return "通常斬り未所持";
        }

        if (masteryManager == null || !masteryManager.IsActionMastered(testMaterialActionId))
        {
            return "通常斬りの極意未到達（1 キーで付与）";
        }

        if (skillSlotManager.IsActionOwnedAnywhere(testResultActionId))
        {
            return "剣の舞は既に所持済み";
        }

        if (inspirationManager.FindDevelopmentRecipe(testMaterialSkillId, testMaterialActionId) == null)
        {
            return "開発レシピ未登録";
        }

        return "装着枠不足など（Console の [InspirationManager] ログを確認）";
    }

    private void SetupTestPlayerSkills()
    {
        skillSlotManager.ResetToDefaultForTesting();
        inspirationManager.ResetDevelopmentRecipesForTesting();
        skillSlotManager.AddDanceArtSkillForTesting();
        masteryManager?.ResetAllMasteryForTesting();
    }

    private void GrantMaterialMasteryForTest()
    {
        masteryManager?.GrantMasteryForTesting(testMaterialActionId);
    }

    private void RunPrerequisiteFailTest()
    {
        const string testName = "前提不足で失敗";

        skillSlotManager.ResetToDefaultForTesting();
        inspirationManager.ResetDevelopmentRecipesForTesting();

        bool developedWithoutDance = inspirationManager.TryDevelopAction(
            testMaterialSkillId, testMaterialActionId, skillSlotManager);

        if (developedWithoutDance)
        {
            LogFail(testName, "舞踏術未所持なのに成功してしまいました。");
            return;
        }

        LogPass(testName, "舞踏術未所持時は開発に失敗しました。");
    }

    private void RunMasteryGateFailTest()
    {
        const string testName = "極意未到達で失敗";

        SetupTestPlayerSkills();

        bool developed = inspirationManager.TryDevelopAction(
            testMaterialSkillId, testMaterialActionId, skillSlotManager);

        if (!developed && !masteryManager.IsActionMastered(testMaterialActionId))
        {
            LogPass(testName, "素材技の極意未到達時は開発に失敗しました。");
        }
        else
        {
            LogFail(testName,
                $"developed={developed}, isMastered={masteryManager.IsActionMastered(testMaterialActionId)}");
        }
    }

    private void RunDanceBladeDevelopmentTest()
    {
        const string testName = "舞踏術×通常斬り→剣の舞";

        SetupTestPlayerSkills();
        GrantMaterialMasteryForTest();

        bool hasBasicSlash = skillSlotManager.IsActionOwnedAnywhere(testMaterialActionId);
        bool hasDanceArt = skillSlotManager.OwnsSkill(testMaterialSkillId);

        if (!hasBasicSlash || !hasDanceArt)
        {
            LogFail(testName, $"前提不足 basicSlash={hasBasicSlash}, danceArt={hasDanceArt}");
            return;
        }

        bool developed = inspirationManager.TryDevelopAction(
            testMaterialSkillId, testMaterialActionId, skillSlotManager);

        bool hasDanceBlade = skillSlotManager.TryFindEquippedAction(
            SkillIds.OneHandSword, testResultActionId, out ActionData danceBlade);

        if (developed && hasDanceBlade && danceBlade != null && danceBlade.actionName == "剣の舞")
        {
            LogPass(testName, "【片手剣術】に「剣の舞」が能動的に開発されました。");
        }
        else
        {
            LogFail(testName,
                $"developed={developed}, hasDanceBlade={hasDanceBlade}, name={danceBlade?.actionName}");
        }
    }

    private void RunDuplicateDevelopmentFailTest()
    {
        const string testName = "二重開発の防止";

        SetupTestPlayerSkills();
        GrantMaterialMasteryForTest();
        inspirationManager.TryDevelopAction(testMaterialSkillId, testMaterialActionId, skillSlotManager);

        bool duplicate = inspirationManager.TryDevelopAction(
            testMaterialSkillId, testMaterialActionId, skillSlotManager);

        if (!duplicate)
        {
            LogPass(testName, "既に開発済みの技は二重付与されませんでした。");
        }
        else
        {
            LogFail(testName, "二重開発が成功してしまいました。");
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
        Debug.Log($"[ActionDevelopmentTester] {message}");
    }
}

public static class ActionDevelopmentTesterMenu
{
    [MenuItem("Tools/Demo/Run Action Development Tests (Play Mode)")]
    private static void RunFromMenu()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("[ActionDevelopmentTester] Play モード中のみ実行できます。");
            return;
        }

        ResolveTester().RunAllTestsNow();
    }

    private static ActionDevelopmentTester ResolveTester()
    {
        ActionDevelopmentTester tester = UnityEngine.Object.FindAnyObjectByType<ActionDevelopmentTester>();
        if (tester != null)
        {
            return tester;
        }

        GameObject hub = GameObject.Find("DebugSystemsHub");
        GameObject host = hub != null ? hub : new GameObject(nameof(ActionDevelopmentTester));
        return host.GetComponent<ActionDevelopmentTester>() ?? host.AddComponent<ActionDevelopmentTester>();
    }
}

#endif

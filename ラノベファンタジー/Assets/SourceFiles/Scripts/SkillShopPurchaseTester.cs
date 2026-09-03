#if UNITY_EDITOR

using System.Collections;
using UnityEngine;
using UnityEditor;

/// <summary>
/// スキルショップの購入・装着・合成の検証用コンポーネント（エディタ専用）。
/// </summary>
[DefaultExecutionOrder(1004)]
public class SkillShopPurchaseTester : MonoBehaviour
{
    [Header("参照（未設定ならシーンから自動検索）")]
    [SerializeField] private SkillShopManager shopManager;
    [SerializeField] private PlayerSkillSlotManager skillSlotManager;

    [Header("テスト設定")]
    [SerializeField] private int testLineupIndex;
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
            Debug.LogWarning("[SkillShopPurchaseTester] Play モード中のみ実行できます。");
            return;
        }

        StartCoroutine(RunAllTestsAfterSceneReady());
    }

    [ContextMenu("Run All Purchase Tests Now")]
    private void RunAllTestsFromContextMenu()
    {
        RunAllTestsNow();
    }

    [ContextMenu("Manual Purchase And Apply")]
    public void RunManualPurchaseAndApply()
    {
        CacheReferences();
        if (shopManager == null || skillSlotManager == null)
        {
            return;
        }

        if (shopManager.CurrentLineup.Count == 0)
        {
            shopManager.RefreshShopInventory();
        }

        bool success = shopManager.BuyAndApplyOrbFromShop(testLineupIndex, skillSlotManager);
        Debug.Log(success
            ? "<color=#81C784>[SkillShopPurchaseTester] 購入→装着成功</color>"
            : "<color=#FF8A80>[SkillShopPurchaseTester] 購入→装着失敗</color>");
        Debug.Log(skillSlotManager.GetSkillSummary());
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

        LogHeader("スキルショップ購入・装着・合成 自動テスト開始");

        RunBuyOrbTest();
        RunApplyOrbTest();
        RunSkillCombineTest();

        LogHeader($"テスト完了 — 成功: {passCount} / 失敗: {failCount}");
    }

    private void CacheReferences()
    {
        if (shopManager == null)
        {
            shopManager = SkillShopManager.Instance;
        }

        if (shopManager == null)
        {
            shopManager = FindAnyObjectByType<SkillShopManager>();
        }

        if (skillSlotManager == null)
        {
            skillSlotManager = FindAnyObjectByType<PlayerSkillSlotManager>();
        }

        referencesCached = shopManager != null && skillSlotManager != null;
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
        EnsureReferencesCached();

        if (shopManager == null)
        {
            LogFail("セットアップ", "SkillShopManager が見つかりません。");
            return false;
        }

        if (skillSlotManager == null)
        {
            LogFail("セットアップ", "PlayerSkillSlotManager が見つかりません。");
            return false;
        }

        return true;
    }

    private void PrepareShopLineup()
    {
        shopManager.ResetShopStateForTesting();
        shopManager.ClearOwnedOrbsForTesting();
        skillSlotManager.ResetToDefaultForTesting();
        shopManager.InitializeDefaultCombinationRecipes();
        shopManager.RefreshShopInventory();
    }

    private void RunBuyOrbTest()
    {
        const string testName = "オーブ購入";

        PrepareShopLineup();

        if (shopManager.CurrentLineup.Count == 0)
        {
            LogFail(testName, "棚が空です。");
            return;
        }

        int ownedBefore = shopManager.OwnedOrbs.Count;
        bool bought = shopManager.BuyOrbFromShop(testLineupIndex, skillSlotManager);

        if (bought && shopManager.OwnedOrbs.Count == ownedBefore + 1)
        {
            LogPass(testName, "棚からオーブを購入し所持リストへ追加しました。");
        }
        else
        {
            LogFail(testName, $"bought={bought}, owned={shopManager.OwnedOrbs.Count}");
        }
    }

    private void RunApplyOrbTest()
    {
        const string testName = "オーブ装着";

        PrepareShopLineup();

        if (!shopManager.BuyOrbFromShop(testLineupIndex, skillSlotManager))
        {
            LogFail(testName, "購入段階で失敗しました。");
            return;
        }

        string skillId = shopManager.OwnedOrbs[0].skillID;
        bool applied = shopManager.TryApplyOwnedOrbViaNpc(0, skillSlotManager);

        if (applied && skillSlotManager.OwnsSkill(skillId))
        {
            LogPass(testName, $"スキル {skillId} を NPC 経由で装着しました。");
        }
        else
        {
            LogFail(testName, $"applied={applied}, owns={skillSlotManager.OwnsSkill(skillId)}");
        }
    }

    private void RunSkillCombineTest()
    {
        const string testName = "スキル合成";

        PrepareShopLineup();

        shopManager.AddOwnedOrbForTesting(new SkillOrbData
        {
            skillID = SkillIds.OneHandSword,
            creatorName = "テストA",
            skillLevelAtExtraction = 3,
            rarity = 2
        });
        shopManager.AddOwnedOrbForTesting(new SkillOrbData
        {
            skillID = SkillIds.DanceArt,
            creatorName = "テストB",
            skillLevelAtExtraction = 3,
            rarity = 2
        });

        bool combined = shopManager.TryCombineSkillsViaNpc(0, 1, skillSlotManager);

        if (combined && skillSlotManager.OwnsSkill(SkillIds.BladeDanceFusion))
        {
            LogPass(testName, "片手剣術 × 舞踏術 → 剣舞融合術の合成に成功しました。");
        }
        else
        {
            LogFail(testName, $"combined={combined}");
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
        Debug.Log($"[SkillShopPurchaseTester] {message}");
    }
}

public static class SkillShopPurchaseTesterMenu
{
    [MenuItem("Tools/Demo/Run Skill Shop Purchase Tests (Play Mode)")]
    private static void RunFromMenu()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("[SkillShopPurchaseTester] Play モード中のみ実行できます。");
            return;
        }

        ResolveTester().RunAllTestsNow();
    }

    private static SkillShopPurchaseTester ResolveTester()
    {
        SkillShopPurchaseTester tester = UnityEngine.Object.FindAnyObjectByType<SkillShopPurchaseTester>();
        if (tester != null)
        {
            return tester;
        }

        GameObject hub = GameObject.Find("DebugSystemsHub");
        GameObject host = hub != null ? hub : new GameObject(nameof(SkillShopPurchaseTester));
        return host.GetComponent<SkillShopPurchaseTester>() ?? host.AddComponent<SkillShopPurchaseTester>();
    }
}

#endif

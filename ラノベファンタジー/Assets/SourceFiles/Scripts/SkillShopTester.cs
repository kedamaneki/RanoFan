#if UNITY_EDITOR

using System.Collections;
using UnityEngine;
using UnityEditor;

/// <summary>
/// スキルショップ・スキルオーブ流通の検証用コンポーネント（エディタ専用）。
/// </summary>
[DefaultExecutionOrder(1002)]
public class SkillShopTester : MonoBehaviour
{
    [Header("参照（未設定ならシーンから自動検索）")]
    [SerializeField] private SkillShopManager shopManager;
    [SerializeField] private PlayerSkillSlotManager skillSlotManager;

    [Header("テスト用パラメーター")]
    [SerializeField] private string testPlayerName = "テスト旅人";
    [SerializeField] private string testExtractSkillId = SkillIds.OneHandSword;
    [SerializeField] private int simulatedSoldOrbCount = 5;

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
            Debug.LogWarning("[SkillShopTester] Play モード中のみ実行できます。");
            return;
        }

        StartCoroutine(RunAllTestsAfterSceneReady());
    }

    [ContextMenu("Run All Shop Tests Now")]
    private void RunAllTestsFromContextMenu()
    {
        RunAllTestsNow();
    }

    [ContextMenu("Simulate 12h Time Advance")]
    private void SimulateTimeFromContextMenu()
    {
        if (shopManager == null)
        {
            shopManager = SkillShopManager.Instance;
        }

        shopManager?.SimulateTimeAdvanceForTesting();
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

        LogHeader("スキルショップ・オーブ流通 自動テスト開始");

        RunExtractOrbTest();
        RunSellOrbHeatTest();
        RunRefreshLineupTest();
        RunHeatWeightedRarityTest();
        RunSimulatedTimeRefreshTest();
        RunKarmaBanSellTest();
        RunTrustPremiumSellTest();
        RunTrustBonusPurchaseTest();

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

        referencesCached = shopManager != null;
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
        if (shopManager == null)
        {
            LogFail("セットアップ", "SkillShopManager が見つかりません。シーンに空オブジェクトを作成してアタッチしてください。");
            return false;
        }

        if (skillSlotManager == null)
        {
            LogFail("セットアップ", "PlayerSkillSlotManager が見つかりません。");
            return false;
        }

        return true;
    }

    private void RunExtractOrbTest()
    {
        const string testName = "オーブ抽出とスキルレベル犠牲";

        skillSlotManager.ResetToDefaultForTesting();
        skillSlotManager.SetSkillLevelForTesting(testExtractSkillId, 4);

        SkillOrbData orb = shopManager.ExtractSkillToOrb(testExtractSkillId, testPlayerName);

        bool levelReduced = skillSlotManager.TryGetSkillLevel(testExtractSkillId, out int afterLevel) &&
                            afterLevel == 3;

        if (orb != null &&
            orb.skillID == testExtractSkillId &&
            orb.creatorName == testPlayerName &&
            orb.skillLevelAtExtraction == 4 &&
            orb.rarity >= 1 &&
            levelReduced)
        {
            LogPass(testName, $"Lv.4→Lv.3 を犠牲に orb 生成（★{orb.rarity}）。");
        }
        else
        {
            LogFail(testName,
                $"orb={orb}, afterLevel={afterLevel}, extractionLv={orb?.skillLevelAtExtraction}");
        }
    }

    private void RunSellOrbHeatTest()
    {
        const string testName = "売却累計（世界の熱量）";

        shopManager.ResetShopStateForTesting();
        PlayerStatusManager.Instance?.ResetToDefaultForTesting();

        SkillOrbData orb = new SkillOrbData
        {
            skillID = SkillIds.FireMagic,
            creatorName = testPlayerName,
            skillLevelAtExtraction = 2,
            rarity = 2
        };

        bool sold = shopManager.SellOrbToShop(orb);

        if (sold && shopManager.TotalOrbsSoldByPlayer == 1)
        {
            LogPass(testName, "totalOrbsSoldByPlayer = 1 に加算されました。");
        }
        else
        {
            LogFail(testName, $"sold={sold}, total={shopManager.TotalOrbsSoldByPlayer}");
        }
    }

    private void RunRefreshLineupTest()
    {
        const string testName = "棚ラインナップ更新";

        shopManager.ResetShopStateForTesting();
        shopManager.RefreshShopInventory();

        int count = shopManager.CurrentLineup.Count;
        bool allValid = true;
        foreach (SkillOrbData orb in shopManager.CurrentLineup)
        {
            if (orb == null || !orb.IsValid())
            {
                allValid = false;
                break;
            }
        }

        if (count > 0 && allValid)
        {
            LogPass(testName, $"currentLineup に {count} 件の銘入りオーブが並びました。");
        }
        else
        {
            LogFail(testName, $"count={count}, allValid={allValid}");
        }
    }

    private void RunHeatWeightedRarityTest()
    {
        const string testName = "売却累計による高レア抽選補正";

        SkillOrbData highRarityOrb = new SkillOrbData
        {
            skillID = SkillIds.FireMagic,
            creatorName = "錬金術師",
            skillLevelAtExtraction = 7,
            rarity = 5
        };

        SkillOrbData lowRarityOrb = new SkillOrbData
        {
            skillID = SkillIds.OneHandSword,
            creatorName = "名もなき旅人",
            skillLevelAtExtraction = 1,
            rarity = 1
        };

        shopManager.ResetShopStateForTesting();
        shopManager.SetTotalOrbsSoldForTesting(0);
        float lowHeatHighWeight = shopManager.GetSelectionWeightForTesting(highRarityOrb);
        float lowHeatLowWeight = shopManager.GetSelectionWeightForTesting(lowRarityOrb);

        shopManager.SetTotalOrbsSoldForTesting(simulatedSoldOrbCount);
        float highHeatHighWeight = shopManager.GetSelectionWeightForTesting(highRarityOrb);
        float highHeatLowWeight = shopManager.GetSelectionWeightForTesting(lowRarityOrb);

        bool highRarityBoosted = highHeatHighWeight > lowHeatHighWeight;
        bool lowRarityRelativeDrop = highHeatLowWeight <= lowHeatLowWeight;

        if (highRarityBoosted && lowRarityRelativeDrop)
        {
            LogPass(testName,
                $"★5重み {lowHeatHighWeight:F2}→{highHeatHighWeight:F2}, " +
                $"★1重み {lowHeatLowWeight:F2}→{highHeatLowWeight:F2}（熱量{simulatedSoldOrbCount}）");
        }
        else
        {
            LogFail(testName,
                $"★5: {lowHeatHighWeight:F2}→{highHeatHighWeight:F2}, ★1: {lowHeatLowWeight:F2}→{highHeatLowWeight:F2}");
        }
    }

    private void RunSimulatedTimeRefreshTest()
    {
        const string testName = "時間経過擬似リフレッシュ";

        shopManager.ResetShopStateForTesting();
        shopManager.RefreshShopInventory();
        int beforeCount = shopManager.CurrentLineup.Count;

        shopManager.SimulateTimeAdvanceForTesting();
        int afterCount = shopManager.CurrentLineup.Count;

        if (beforeCount > 0 && afterCount > 0)
        {
            LogPass(testName, "SimulateTimeAdvanceForTesting → CheckTimeRefresh が正常に発火しました。");
        }
        else
        {
            LogFail(testName, $"before={beforeCount}, after={afterCount}");
        }
    }

    private void RunKarmaBanSellTest()
    {
        const string testName = "カルマ門前払い（売却拒否）";

        PlayerStatusManager status = PlayerStatusManager.Instance;
        if (status == null)
        {
            LogFail(testName, "PlayerStatusManager が見つかりません。");
            return;
        }

        shopManager.ResetShopStateForTesting();
        status.ResetToDefaultForTesting();
        status.SetKarmaValueForTesting(shopManager.KarmaBanThreshold - 5);

        SkillOrbData orb = new SkillOrbData
        {
            skillID = SkillIds.FireMagic,
            creatorName = testPlayerName,
            skillLevelAtExtraction = 2,
            rarity = 2
        };

        bool sold = shopManager.SellOrbToShop(orb);

        if (!sold && shopManager.TotalOrbsSoldByPlayer == 0)
        {
            LogPass(testName, $"カルマ {status.KarmaValue} で売却が拒否された。");
        }
        else
        {
            LogFail(testName, $"sold={sold}, total={shopManager.TotalOrbsSoldByPlayer}");
        }
    }

    private void RunTrustPremiumSellTest()
    {
        const string testName = "高信頼度・高価買取査定";

        PlayerStatusManager status = PlayerStatusManager.Instance;
        if (status == null)
        {
            LogFail(testName, "PlayerStatusManager が見つかりません。");
            return;
        }

        shopManager.ResetShopStateForTesting();
        status.ResetToDefaultForTesting();
        status.AddIntelBonus(20);
        status.AddKarmaValue(30);

        SkillOrbData orb = new SkillOrbData
        {
            skillID = SkillIds.OneHandSword,
            creatorName = testPlayerName,
            skillLevelAtExtraction = 3,
            rarity = 2
        };

        bool sold = shopManager.SellOrbToShop(orb);

        if (sold &&
            shopManager.TotalOrbsSoldByPlayer == 2 &&
            status.GetNPCTrustFactor() >= shopManager.TrustPremiumSellThreshold)
        {
            LogPass(testName, "信頼度 5.0 以上で熱量 +2 が加算された。");
        }
        else
        {
            LogFail(testName,
                $"sold={sold}, total={shopManager.TotalOrbsSoldByPlayer}, trust={status.GetNPCTrustFactor():F2}");
        }
    }

    private void RunTrustBonusPurchaseTest()
    {
        const string testName = "高信頼度・購入おまけ";

        PlayerStatusManager status = PlayerStatusManager.Instance;
        if (status == null)
        {
            LogFail(testName, "PlayerStatusManager が見つかりません。");
            return;
        }

        shopManager.ResetShopStateForTesting();
        shopManager.ClearOwnedOrbsForTesting();
        shopManager.SetTrustBonusGiftAlwaysForTesting(true);
        shopManager.RefreshShopInventory();
        status.ResetToDefaultForTesting();
        status.AddIntelBonus(20);
        status.AddKarmaValue(30);

        if (shopManager.CurrentLineup.Count == 0)
        {
            LogFail(testName, "棚が空です。");
            return;
        }

        int ownedBefore = shopManager.OwnedOrbs.Count;
        bool bought = shopManager.BuyOrbFromShop(0, skillSlotManager);
        int ownedAfter = shopManager.OwnedOrbs.Count;

        if (bought && ownedAfter >= ownedBefore + 2)
        {
            LogPass(testName, "購入オーブ + おまけオーブが所持リストに追加された。");
        }
        else
        {
            LogFail(testName, $"bought={bought}, ownedBefore={ownedBefore}, ownedAfter={ownedAfter}");
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
        Debug.Log($"[SkillShopTester] {message}");
    }
}

public static class SkillShopTesterMenu
{
    [MenuItem("Tools/Demo/Run Skill Shop Tests (Play Mode)")]
    private static void RunFromMenu()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("[SkillShopTester] Play モード中のみ実行できます。");
            return;
        }

        ResolveTester().RunAllTestsNow();
    }

    private static SkillShopTester ResolveTester()
    {
        SkillShopTester tester = UnityEngine.Object.FindAnyObjectByType<SkillShopTester>();
        if (tester != null)
        {
            return tester;
        }

        GameObject hub = GameObject.Find("DebugSystemsHub");
        GameObject host = hub != null ? hub : new GameObject(nameof(SkillShopTester));
        return host.GetComponent<SkillShopTester>() ?? host.AddComponent<SkillShopTester>();
    }
}

#endif

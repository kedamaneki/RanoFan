#if UNITY_EDITOR

using System.Collections;
using UnityEngine;
using UnityEditor;

/// <summary>
/// スキル・閃きシステムの自動検証コンポーネント（エディタ専用）。
/// 空の GameObject にアタッチして Play するだけで、4 つのシナリオを一括テストします。
/// </summary>
[DefaultExecutionOrder(1000)]
public class InspirationSystemTester : MonoBehaviour
{
    [Header("参照（未設定ならシーンから自動検索）")]
    [SerializeField] private InspirationManager inspirationManager;
    [SerializeField] private PlayerSkillSlotManager skillSlotManager;
    [SerializeField] private PlayerActionLogger actionLogger;
    [SerializeField] private PlayerStats playerStats;
    [SerializeField] private CombatStats combatStats;

    [Header("実行設定")]
    [SerializeField] private bool runTestsOnStart;
    [SerializeField] private bool logVerboseSummary = true;

    private int passCount;
    private int failCount;

    private void Start()
    {
        if (!runTestsOnStart)
        {
            return;
        }

        Debug.Log("[InspirationSystemTester] 自動テストを開始します…");
        StartCoroutine(RunAllTestsAfterSceneReady());
    }

    /// <summary>手動またはメニューから全テストを実行します。</summary>
    public void RunAllTestsNow()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("[InspirationSystemTester] Play モード中のみ実行できます。");
            return;
        }

        StartCoroutine(RunAllTestsAfterSceneReady());
    }

    [ContextMenu("Run All Tests Now")]
    private void RunAllTestsFromContextMenu()
    {
        RunAllTestsNow();
    }

    /// <summary>
    /// 他コンポーネントの Start（スキル初期化など）完了後にテストを走らせます。
    /// </summary>
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

        LogHeader("スキル・閃きシステム自動テスト開始");

        RunPinchInspirationTest();
        RunNearMissAutoInspirationTest();
        RunCombineLogicTest();
        RunMaxSlotOverflowTest();

        LogHeader($"テスト完了 — 成功: {passCount} / 失敗: {failCount}");

        if (logVerboseSummary)
        {
            Debug.Log(skillSlotManager.GetSkillSummary());
        }
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

        if (actionLogger == null)
        {
            actionLogger = PlayerActionLogger.Instance;
        }

        if (playerStats == null)
        {
            playerStats = FindAnyObjectByType<PlayerStats>();
        }

        if (combatStats == null && playerStats != null)
        {
            combatStats = playerStats.GetComponent<CombatStats>();
            if (combatStats == null)
            {
                combatStats = playerStats.GetComponentInParent<CombatStats>();
            }
        }

        if (combatStats == null)
        {
            combatStats = FindAnyObjectByType<CombatStats>();
        }
    }

    private bool ValidateReferences()
    {
        if (inspirationManager == null || skillSlotManager == null)
        {
            LogFail("セットアップ", "InspirationManager または PlayerSkillSlotManager が見つかりません。");
            return false;
        }

        if (actionLogger == null)
        {
            LogFail("セットアップ", "PlayerActionLogger が見つかりません。");
            return false;
        }

        if (playerStats == null || combatStats == null)
        {
            LogFail("セットアップ", "PlayerStats / CombatStats が見つかりません。");
            return false;
        }

        return true;
    }

    // -------------------------------------------------------------------------
    // テスト1：ピンチ型閃き
    // -------------------------------------------------------------------------
    private void RunPinchInspirationTest()
    {
        const string testName = "ピンチ型閃き";

        inspirationManager.ResetInspirationFlags();
        skillSlotManager.ResetToDefaultForTesting();

        int pinchHp = Mathf.Max(1, Mathf.RoundToInt(combatStats.MaxHp * 0.2f));
        combatStats.SetHpForTesting(pinchHp);
        playerStats.SetStaminaForTesting(0f);

        inspirationManager.TriggerPinchInspiration(playerStats, combatStats);

        bool hasSilentEdge = skillSlotManager.TryFindEquippedAction(
            SkillIds.OneHandSword, ActionIds.SilentEdge, out ActionData silentEdge);

        if (hasSilentEdge && silentEdge != null && silentEdge.actionName == "絶境無音斬")
        {
            LogPass(testName, "【片手剣術】に「絶境無音斬」が追加されました。");
        }
        else
        {
            LogFail(testName, "絶境無音斬が片手剣術スキルに見つかりません。");
        }

        combatStats.FullHeal();
        playerStats.SetStaminaForTesting(playerStats.maxStamina);
    }

    // -------------------------------------------------------------------------
    // テスト2：自動達成型（ニアミスログ監視）
    // -------------------------------------------------------------------------
    private void RunNearMissAutoInspirationTest()
    {
        const string testName = "自動達成型（ニアミス）";

        inspirationManager.ResetInspirationFlags();
        skillSlotManager.ResetToDefaultForTesting();

        actionLogger.SetNearMissCountForTesting(21);
        inspirationManager.TriggerLogInspiration();

        bool hasFlashStep = skillSlotManager.TryFindEquippedAction(
            SkillIds.EvadeArt, ActionIds.FlashStep, out ActionData flashStep);

        if (hasFlashStep && flashStep != null && flashStep.actionName == "瞬歩")
        {
            LogPass(testName, "【回避術】に「瞬歩」が自動追加されました。");
        }
        else
        {
            LogFail(testName, "瞬歩が回避術スキルに見つかりません。");
        }
    }

    // -------------------------------------------------------------------------
    // テスト3：合成ロジック（強撃 × 火の粉 → フレインスラッシュ）
    // -------------------------------------------------------------------------
    private void RunCombineLogicTest()
    {
        const string testName = "合成ロジック";

        inspirationManager.ResetInspirationFlags();
        skillSlotManager.ResetToDefaultForTesting();

        bool hasStrongStrike = skillSlotManager.TryFindEquippedAction(
            SkillIds.OneHandSword, ActionIds.StrongStrike, out _);
        bool hasFireSpark = skillSlotManager.TryFindEquippedAction(
            SkillIds.FireMagic, ActionIds.FireSpark, out _);

        if (!hasStrongStrike || !hasFireSpark)
        {
            LogFail(testName, "合成材料（強撃・火の粉）がデフォルトスキルに存在しません。");
            return;
        }

        bool combineSucceeded = inspirationManager.TryCombineFlareSlash();

        bool hasFlareSlash = skillSlotManager.TryFindEquippedAction(
            SkillIds.OneHandSword, ActionIds.FlareSlash, out ActionData flareSlash);

        if (combineSucceeded && hasFlareSlash && flareSlash != null && flareSlash.actionName == "フレインスラッシュ")
        {
            LogPass(testName, "「フレインスラッシュ」が片手剣術に生成・追加されました。");
        }
        else
        {
            LogFail(testName, "フレインスラッシュの合成結果が不正です。");
        }
    }

    // -------------------------------------------------------------------------
    // テスト4：最大スロット数（4枠制限）→ ストックへ
    // -------------------------------------------------------------------------
    private void RunMaxSlotOverflowTest()
    {
        const string testName = "4枠制限とストック格納";

        skillSlotManager.ResetToDefaultForTesting();

        SkillData fireSkill = skillSlotManager.FindSkill(SkillIds.FireMagic);
        if (fireSkill == null)
        {
            LogFail(testName, "初級火魔法スキルが見つかりません。");
            return;
        }

        // 火の粉（1枠）に加え、ダミー閃き技を3つ追加して4枠満杯にする
        for (int i = 0; i < 3; i++)
        {
            ActionData dummy = new ActionData
            {
                actionID = $"test_dummy_slot_{i}",
                actionName = $"ダミー技{i + 1}",
                damageMultiplier = 1f,
                staminaCost = 10f,
                activeDetectionTime = 0.2f,
                isDerived = true,
                inspirationSource = "スロットテスト用"
            };

            skillSlotManager.InspireActionToSkill(SkillIds.FireMagic, dummy);
        }

        if (fireSkill.EquippedCount != fireSkill.maxSlots)
        {
            LogFail(testName, $"枠が満杯になりませんでした（{fireSkill.EquippedCount}/{fireSkill.maxSlots}）。");
            return;
        }

        const string overflowId = "test_overflow_action";
        ActionData overflowAction = new ActionData
        {
            actionID = overflowId,
            actionName = "溢れテスト技",
            damageMultiplier = 1.2f,
            staminaCost = 15f,
            activeDetectionTime = 0.25f,
            isDerived = true,
            inspirationSource = "5つ目の閃きテスト"
        };

        skillSlotManager.InspireActionToSkill(SkillIds.FireMagic, overflowAction);

        bool inStock = skillSlotManager.IsActionInStock(overflowId);
        bool notEquipped = !fireSkill.ContainsAction(overflowId);
        bool slotStillFull = fireSkill.EquippedCount == fireSkill.maxSlots;

        if (inStock && notEquipped && slotStillFull)
        {
            LogPass(testName, "5つ目の技がストックに格納され、スキル枠は4のまま維持されました。");
        }
        else
        {
            LogFail(testName,
                $"ストック={inStock}, 未装着={notEquipped}, 枠数={fireSkill.EquippedCount}/{fireSkill.maxSlots}");
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
        Debug.Log($"[InspirationSystemTester] {message}");
    }
}

/// <summary>エディタメニューから閃きシステムテストを手動実行します。</summary>
public static class InspirationSystemTesterMenu
{
    [MenuItem("Tools/Demo/Run Inspiration System Tests (Play Mode)")]
    private static void RunFromMenu()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("[InspirationSystemTester] Play モード中のみ実行できます。");
            return;
        }

        InspirationSystemTester tester = UnityEngine.Object.FindAnyObjectByType<InspirationSystemTester>();
        if (tester == null)
        {
            GameObject host = new GameObject(nameof(InspirationSystemTester));
            tester = host.AddComponent<InspirationSystemTester>();
        }

        tester.RunAllTestsNow();
    }
}

#endif

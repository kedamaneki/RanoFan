using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 街のスキルショップ NPC の挙動と、時間経過による非同期商品更新を管理します。
/// プレイヤー個人の閃き（InspirationManager）とは独立した「スキルオーブ流通」基盤です。
/// </summary>
public class SkillShopManager : MonoBehaviour
{
    public static SkillShopManager Instance { get; private set; }

    [Header("参照（未設定ならシーンから自動検索）")]
    [SerializeField] private PlayerSkillSlotManager playerSkillSlotManager;
    [SerializeField] private PlayerStatusManager playerStatusManager;

    [Header("信頼度・カルマ連動")]
    [Tooltip("このカルマ未満では購入・売却を門前払い")]
    [SerializeField] private int karmaBanThreshold = PlayerStatusManager.DefaultShopKarmaBanThreshold;

    [Tooltip("この信頼度以上で購入成功時におまけオーブを付与")]
    [SerializeField] private float trustBonusGiftThreshold = 5f;

    [Tooltip("この信頼度以上で売却時の熱量加算が +2 になる")]
    [SerializeField] private float trustPremiumSellThreshold = 5f;

    [Tooltip("ON の間は信頼度おまけを常に付与（OFF 時は確率判定）")]
    [SerializeField] private bool trustBonusGiftAlways = true;

    [SerializeField] private float trustBonusGiftChance = 0.5f;

    [Header("棚・世界の熱量")]
    [SerializeField] private List<SkillOrbData> currentLineup = new List<SkillOrbData>();

    [SerializeField] private int totalOrbsSoldByPlayer;

    [Header("更新スケジュール")]
    [Tooltip("棚の自動更新間隔（時間）。12 = 半日、24 = 1日")]
    [SerializeField] private float refreshIntervalHours = 12f;

    [SerializeField] private string nextRefreshTimeIso = string.Empty;

    [Header("抽選設定")]
    [SerializeField] private int lineupSize = 5;

    [Tooltip("プレイヤー売却1個あたり、高レア（4以上）の抽選重みに加算する値")]
    [SerializeField] private float highRarityWeightBonusPerSoldOrb = 0.08f;

    [Header("他プレイヤー銘入りオーブ（サーバー模擬プール）")]
    [SerializeField] private List<SkillOrbData> asyncOrbPool = new List<SkillOrbData>();

    [Header("初期化")]
    [SerializeField] private bool initializePoolOnStart = true;
    [SerializeField] private bool refreshOnStart = true;

    [Header("購入・所持オーブ")]
    [SerializeField] private List<SkillOrbData> ownedOrbs = new List<SkillOrbData>();

    [Tooltip("仮の所持ゴールド（通貨システム未実装のスタブ）")]
    [SerializeField] private int playerGold = 9999;

    [Tooltip("ON の間は購入コストを消費せず常に成功（テスト用）")]
    [SerializeField] private bool freePurchaseMode = true;

    [Header("専門職 NPC：スキル合成レシピ")]
    [SerializeField] private List<SkillOrbCombinationRecipe> combinationRecipes = new List<SkillOrbCombinationRecipe>();

    [SerializeField] private bool initializeCombinationRecipesOnStart = true;

    private int lastPurchasedOrbIndex = -1;

    public IReadOnlyList<SkillOrbData> OwnedOrbs => ownedOrbs;
    public IReadOnlyList<SkillOrbData> CurrentLineup => currentLineup;
    public int TotalOrbsSoldByPlayer => totalOrbsSoldByPlayer;
    public int PlayerGold => playerGold;
    public DateTime NextRefreshTime => ParseRefreshTime(nextRefreshTimeIso);
    public float RefreshIntervalHours => refreshIntervalHours;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[SkillShopManager] 重複インスタンスを検出しました。");
        }
        else
        {
            Instance = this;
        }

        CacheReferences();
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void Start()
    {
        if (initializePoolOnStart && asyncOrbPool.Count == 0)
        {
            InitializeDefaultAsyncPool();
        }

        if (string.IsNullOrWhiteSpace(nextRefreshTimeIso))
        {
            ScheduleNextRefreshFromNow();
        }

        if (refreshOnStart)
        {
            RefreshShopInventory();
        }

        if (initializeCombinationRecipesOnStart && combinationRecipes.Count == 0)
        {
            InitializeDefaultCombinationRecipes();
        }
    }

    private void Update()
    {
        CheckTimeRefresh();
    }

    private void CacheReferences()
    {
        if (playerSkillSlotManager == null)
        {
            playerSkillSlotManager = FindAnyObjectByType<PlayerSkillSlotManager>();
        }

        if (playerStatusManager == null)
        {
            playerStatusManager = PlayerStatusManager.Instance;
        }

        if (playerStatusManager == null)
        {
            playerStatusManager = FindAnyObjectByType<PlayerStatusManager>();
        }
    }

    /// <summary>PlayerStatusManager を解決します（未配置時は null）。</summary>
    private PlayerStatusManager ResolvePlayerStatus()
    {
        if (playerStatusManager == null)
        {
            CacheReferences();
        }

        return playerStatusManager;
    }

    /// <summary>
    /// カルマ門前払い判定。極端に低いカルマでは取引を拒否します。
    /// </summary>
    private bool TryPassShopKarmaGate()
    {
        PlayerStatusManager status = ResolvePlayerStatus();
        if (status == null)
        {
            return true;
        }

        if (status.IsBelowShopKarmaThreshold(karmaBanThreshold))
        {
            SkillShopLog.LogShopRejectedByKarma(status.KarmaValue, karmaBanThreshold);
            return false;
        }

        return true;
    }

    /// <summary>信頼度に応じた売却熱量ボーナス（通常 +1 / 高信頼 +2）を返します。</summary>
    private int CalculateSellHeatGain(PlayerStatusManager status)
    {
        if (status != null && status.GetNPCTrustFactor() >= trustPremiumSellThreshold)
        {
            return 2;
        }

        return 1;
    }

    /// <summary>購入成功時、高信頼度プレイヤーにおまけオーブを付与します。</summary>
    private void TryGrantTrustBonusGift(PlayerStatusManager status)
    {
        if (status == null || status.GetNPCTrustFactor() < trustBonusGiftThreshold)
        {
            return;
        }

        if (!trustBonusGiftAlways && UnityEngine.Random.value > trustBonusGiftChance)
        {
            return;
        }

        SkillOrbData bonusOrb = CreatePoolOrb(SkillIds.EvadeArt, "商人のおまけ", 1, 1);
        ownedOrbs.Add(bonusOrb);
        SkillShopLog.LogTrustBonusGift(bonusOrb, status.GetNPCTrustFactor());
    }

    /// <summary>
    /// プレイヤーのスキルレベルを 1 犠牲にし、銘入りスキルオーブを生成します（売却は別途）。
    /// </summary>
    public SkillOrbData ExtractSkillToOrb(string skillId, string playerSelfName)
    {
        CacheReferences();

        if (playerSkillSlotManager == null)
        {
            Debug.LogWarning("[SkillShopManager] PlayerSkillSlotManager が見つかりません。");
            return null;
        }

        if (string.IsNullOrWhiteSpace(skillId) || string.IsNullOrWhiteSpace(playerSelfName))
        {
            Debug.LogWarning("[SkillShopManager] skillId または playerSelfName が無効です。");
            return null;
        }

        if (!playerSkillSlotManager.TrySacrificeSkillLevelForOrb(skillId, out int levelAtExtraction))
        {
            Debug.LogWarning($"[SkillShopManager] スキルが見つかりません: {skillId}");
            return null;
        }

        SkillOrbData orb = new SkillOrbData
        {
            skillID = skillId,
            creatorName = playerSelfName,
            skillLevelAtExtraction = levelAtExtraction,
            rarity = CalculateOrbRarity(skillId, levelAtExtraction)
        };

        SkillShopLog.LogOrbExtracted(orb);
        return orb;
    }

    /// <summary>生成したオーブをショップに売却し、世界の熱量（売却累計）を加算します。</summary>
    public bool SellOrbToShop(SkillOrbData orb)
    {
        if (orb == null || !orb.IsValid())
        {
            Debug.LogWarning("[SkillShopManager] 売却対象のオーブが無効です。");
            return false;
        }

        if (!TryPassShopKarmaGate())
        {
            return false;
        }

        PlayerStatusManager status = ResolvePlayerStatus();
        int heatGain = CalculateSellHeatGain(status);
        totalOrbsSoldByPlayer += heatGain;

        if (heatGain > 1)
        {
            SkillShopLog.LogOrbSoldPremiumAppraisal(
                orb, totalOrbsSoldByPlayer, heatGain, status.GetNPCTrustFactor());
        }
        else
        {
            SkillShopLog.LogOrbSold(orb, totalOrbsSoldByPlayer);
        }

        return true;
    }

    /// <summary>
    /// 棚のラインナップを完全更新します。非同期プールから銘入りオーブを抽選陳列します。
    /// </summary>
    public void RefreshShopInventory()
    {
        currentLineup.Clear();

        if (asyncOrbPool.Count == 0)
        {
            InitializeDefaultAsyncPool();
        }

        List<SkillOrbData> poolCopy = BuildWeightedPoolCopy();
        int pickCount = Mathf.Min(lineupSize, poolCopy.Count);

        for (int i = 0; i < pickCount; i++)
        {
            SkillOrbData picked = PickWeightedOrb(poolCopy);
            if (picked == null)
            {
                break;
            }

            currentLineup.Add(picked.Clone());
            poolCopy.Remove(picked);
        }

        SkillShopLog.LogShopRefreshed(currentLineup.Count, totalOrbsSoldByPlayer);
        foreach (SkillOrbData orb in currentLineup)
        {
            SkillShopLog.LogOrbListed(orb);
        }
    }

    /// <summary>
    /// 現在時刻が nextRefreshTime を過ぎていれば棚を自動更新します。
    /// </summary>
    public void CheckTimeRefresh()
    {
        if (DateTime.Now < NextRefreshTime)
        {
            return;
        }

        RefreshShopInventory();
        ScheduleNextRefreshFromNow();
    }

    /// <summary>テスト用：次回更新時刻を過去に設定し、即時 Refresh を発火させます。</summary>
    public void SimulateTimeAdvanceForTesting()
    {
        nextRefreshTimeIso = DateTime.Now.AddSeconds(-1).ToString("o");
        CheckTimeRefresh();
    }

    /// <summary>テスト用：売却累計と棚をリセットします。</summary>
    public void ResetShopStateForTesting()
    {
        totalOrbsSoldByPlayer = 0;
        currentLineup.Clear();
        ownedOrbs.Clear();
        ScheduleNextRefreshFromNow();
    }

    /// <summary>テスト用：売却累計を直接設定します。</summary>
    public void SetTotalOrbsSoldForTesting(int count)
    {
        totalOrbsSoldByPlayer = Mathf.Max(0, count);
    }

    /// <summary>自動テスト用：指定オーブの抽選重みを返します。</summary>
    public float GetSelectionWeightForTesting(SkillOrbData orb)
    {
        return orb == null ? 0f : CalculateSelectionWeight(orb);
    }

    /// <summary>サーバー非同期流通を模した銘入りオーブの初期プールを構築します。</summary>
    public void InitializeDefaultAsyncPool()
    {
        asyncOrbPool.Clear();
        asyncOrbPool.Add(CreatePoolOrb(SkillIds.OneHandSword, "おじいちゃんマスター", 8, 4));
        asyncOrbPool.Add(CreatePoolOrb(SkillIds.FireMagic, "ギルド長", 6, 4));
        asyncOrbPool.Add(CreatePoolOrb(SkillIds.EvadeArt, "影の渡り鳥", 5, 3));
        asyncOrbPool.Add(CreatePoolOrb(SkillIds.OneHandSword, "異世界商人", 4, 3));
        asyncOrbPool.Add(CreatePoolOrb(SkillIds.FireMagic, "見習い魔導士", 3, 2));
        asyncOrbPool.Add(CreatePoolOrb(SkillIds.EvadeArt, "街の番人", 2, 2));
        asyncOrbPool.Add(CreatePoolOrb(SkillIds.OneHandSword, "名もなき旅人", 1, 1));
        asyncOrbPool.Add(CreatePoolOrb(SkillIds.FireMagic, "錬金術師", 7, 5));
    }

    private void ScheduleNextRefreshFromNow()
    {
        DateTime next = DateTime.Now.AddHours(refreshIntervalHours);
        nextRefreshTimeIso = next.ToString("o");
    }

    private static DateTime ParseRefreshTime(string iso)
    {
        if (string.IsNullOrWhiteSpace(iso))
        {
            return DateTime.MinValue;
        }

        if (DateTime.TryParse(iso, out DateTime parsed))
        {
            return parsed;
        }

        return DateTime.MinValue;
    }

    private int CalculateOrbRarity(string skillId, int skillLevelAtExtraction)
    {
        int baseRarity = GetBaseRarityForSkill(skillId);
        int levelBonus = skillLevelAtExtraction / 3;
        return Mathf.Clamp(baseRarity + levelBonus, 1, 5);
    }

    private static int GetBaseRarityForSkill(string skillId)
    {
        switch (skillId)
        {
            case SkillIds.FireMagic:
                return 3;
            case SkillIds.OneHandSword:
            case SkillIds.EvadeArt:
                return 2;
            default:
                return 1;
        }
    }

    private static SkillOrbData CreatePoolOrb(
        string skillId, string creatorName, int levelAtExtraction, int rarity)
    {
        return new SkillOrbData
        {
            skillID = skillId,
            creatorName = creatorName,
            skillLevelAtExtraction = levelAtExtraction,
            rarity = rarity
        };
    }

    private List<SkillOrbData> BuildWeightedPoolCopy()
    {
        List<SkillOrbData> copy = new List<SkillOrbData>();
        foreach (SkillOrbData template in asyncOrbPool)
        {
            if (template != null && template.IsValid())
            {
                copy.Add(template);
            }
        }

        return copy;
    }

    private SkillOrbData PickWeightedOrb(List<SkillOrbData> candidates)
    {
        if (candidates == null || candidates.Count == 0)
        {
            return null;
        }

        float totalWeight = 0f;
        float[] weights = new float[candidates.Count];

        for (int i = 0; i < candidates.Count; i++)
        {
            weights[i] = CalculateSelectionWeight(candidates[i]);
            totalWeight += weights[i];
        }

        if (totalWeight <= 0f)
        {
            return candidates[UnityEngine.Random.Range(0, candidates.Count)];
        }

        float roll = UnityEngine.Random.value * totalWeight;
        float cumulative = 0f;

        for (int i = 0; i < candidates.Count; i++)
        {
            cumulative += weights[i];
            if (roll <= cumulative)
            {
                return candidates[i];
            }
        }

        return candidates[candidates.Count - 1];
    }

    /// <summary>
    /// 売却累計が多いほど高レアオーブの抽選重みが上がります。
    /// </summary>
    private float CalculateSelectionWeight(SkillOrbData orb)
    {
        float baseWeight = orb.rarity * orb.rarity;
        float heatBonus = totalOrbsSoldByPlayer * highRarityWeightBonusPerSoldOrb;

        if (orb.rarity >= 4)
        {
            baseWeight *= 1f + heatBonus;
        }
        else if (orb.rarity <= 2 && heatBonus > 0f)
        {
            baseWeight /= 1f + heatBonus * 0.5f;
        }

        return Mathf.Max(0.1f, baseWeight);
    }

    // -------------------------------------------------------------------------
    // 購入・専門職 NPC（装着 / 合成）
    // -------------------------------------------------------------------------

    /// <summary>デフォルトのスキル合成レシピを構築します。</summary>
    public void InitializeDefaultCombinationRecipes()
    {
        combinationRecipes.Clear();
        combinationRecipes.Add(SkillOrbCombinationRecipe.CreateBladeDanceFusionRecipe());
    }

    /// <summary>オーブの購入価格を算出します（仮式：レアリティ × 50 + Lv × 10）。</summary>
    public int CalculateOrbPrice(SkillOrbData orb)
    {
        if (orb == null)
        {
            return 0;
        }

        return orb.rarity * 50 + orb.skillLevelAtExtraction * 10;
    }

    /// <summary>
    /// 棚からスキルオーブを購入し、プレイヤーの所持オーブリストへ格納します。
    /// </summary>
    public bool BuyOrbFromShop(int lineupIndex, PlayerSkillSlotManager slotManager)
    {
        CacheReferences();

        if (!TryPassShopKarmaGate())
        {
            return false;
        }

        if (lineupIndex < 0 || lineupIndex >= currentLineup.Count)
        {
            Debug.LogWarning($"[SkillShopManager] 購入失敗: 棚インデックスが不正 ({lineupIndex})");
            return false;
        }

        SkillOrbData orb = currentLineup[lineupIndex];
        if (orb == null || !orb.IsValid())
        {
            Debug.LogWarning("[SkillShopManager] 購入失敗: オーブデータが無効です。");
            return false;
        }

        int price = CalculateOrbPrice(orb);
        if (!freePurchaseMode && playerGold < price)
        {
            Debug.LogWarning($"[SkillShopManager] 購入失敗: ゴールド不足 ({playerGold}/{price})");
            return false;
        }

        if (!freePurchaseMode)
        {
            playerGold -= price;
        }

        SkillOrbData purchased = orb.Clone();
        currentLineup.RemoveAt(lineupIndex);
        ownedOrbs.Add(purchased);
        lastPurchasedOrbIndex = ownedOrbs.Count - 1;

        SkillShopLog.LogOrbPurchased(purchased, price);
        TryGrantTrustBonusGift(ResolvePlayerStatus());
        return true;
    }

    /// <summary>
    /// 専門職 NPC が所持オーブをプレイヤーのスキルへ装着します（パターンA）。
    /// </summary>
    public bool TryApplyOwnedOrbViaNpc(int ownedOrbIndex, PlayerSkillSlotManager slotManager)
    {
        PlayerSkillSlotManager manager = slotManager != null ? slotManager : playerSkillSlotManager;
        if (manager == null)
        {
            Debug.LogWarning("[SkillShopManager] PlayerSkillSlotManager が未設定です。");
            return false;
        }

        if (ownedOrbIndex < 0 || ownedOrbIndex >= ownedOrbs.Count)
        {
            Debug.LogWarning($"[SkillShopManager] 装着失敗: 所持オーブインデックス不正 ({ownedOrbIndex})");
            return false;
        }

        SkillOrbData orb = ownedOrbs[ownedOrbIndex];
        if (orb == null || !orb.IsValid())
        {
            return false;
        }

        bool applied = manager.TryApplyOrbInstallation(orb);
        if (applied)
        {
            ownedOrbs.RemoveAt(ownedOrbIndex);
            manager.SyncActiveActionsToPlayerController();
        }

        return applied;
    }

    /// <summary>
    /// 専門職 NPC が2つの所持オーブを合成し、新スキル（器）を付与します（パターンB）。
    /// </summary>
    public bool TryCombineSkillsViaNpc(
        int ownedOrbIndexA, int ownedOrbIndexB, PlayerSkillSlotManager slotManager)
    {
        PlayerSkillSlotManager manager = slotManager != null ? slotManager : playerSkillSlotManager;
        if (manager == null)
        {
            Debug.LogWarning("[SkillShopManager] PlayerSkillSlotManager が未設定です。");
            return false;
        }

        if (!TryGetOwnedOrbsPair(ownedOrbIndexA, ownedOrbIndexB, out SkillOrbData orbA, out SkillOrbData orbB))
        {
            return false;
        }

        SkillOrbCombinationRecipe recipe = FindCombinationRecipe(orbA.skillID, orbB.skillID);
        if (recipe == null)
        {
            Debug.LogWarning(
                $"[SkillShopManager] 合成失敗: レシピ未登録 ({orbA.skillID} × {orbB.skillID})");
            return false;
        }

        if (manager.OwnsSkill(recipe.resultSkillId))
        {
            Debug.LogWarning($"[SkillShopManager] 合成失敗: 結果スキル既所持 ({recipe.resultSkillName})");
            return false;
        }

        if (!manager.TryAcquireCombinedSkill(recipe))
        {
            return false;
        }

        int removeFirst = Mathf.Max(ownedOrbIndexA, ownedOrbIndexB);
        int removeSecond = Mathf.Min(ownedOrbIndexA, ownedOrbIndexB);
        RemoveOwnedOrbAt(removeFirst);
        RemoveOwnedOrbAt(removeSecond);

        manager.SyncActiveActionsToPlayerController();
        SkillShopLog.LogSkillCombined(recipe.resultSkillName, recipe.resultSkillId);
        return true;
    }

    /// <summary>登録済み合成レシピを検索します（順不同）。</summary>
    public SkillOrbCombinationRecipe FindCombinationRecipe(string skillIdA, string skillIdB)
    {
        foreach (SkillOrbCombinationRecipe recipe in combinationRecipes)
        {
            if (recipe != null && recipe.IsValid() && recipe.Matches(skillIdA, skillIdB))
            {
                return recipe;
            }
        }

        return null;
    }

    /// <summary>購入から装着まで一括実行（テスト・デバッグ用）。</summary>
    public bool BuyAndApplyOrbFromShop(int lineupIndex, PlayerSkillSlotManager slotManager)
    {
        if (!BuyOrbFromShop(lineupIndex, slotManager))
        {
            return false;
        }

        int applyIndex = lastPurchasedOrbIndex >= 0 ? lastPurchasedOrbIndex : ownedOrbs.Count - 1;
        return TryApplyOwnedOrbViaNpc(applyIndex, slotManager);
    }

    /// <summary>テスト用：信頼度おまけを確定付与モードにします。</summary>
    public void SetTrustBonusGiftAlwaysForTesting(bool always)
    {
        trustBonusGiftAlways = always;
    }

    /// <summary>テスト用：カルマ門前払い閾値を設定します。</summary>
    public void SetKarmaBanThresholdForTesting(int threshold)
    {
        karmaBanThreshold = threshold;
    }

    public int KarmaBanThreshold => karmaBanThreshold;
    public float TrustBonusGiftThreshold => trustBonusGiftThreshold;
    public float TrustPremiumSellThreshold => trustPremiumSellThreshold;

    /// <summary>テスト用：所持ゴールドを設定します。</summary>
    public void SetPlayerGoldForTesting(int gold)
    {
        playerGold = Mathf.Max(0, gold);
    }

    /// <summary>テスト用：所持オーブをクリアします。</summary>
    public void ClearOwnedOrbsForTesting()
    {
        ownedOrbs.Clear();
    }

    /// <summary>テスト用：所持オーブを追加します。</summary>
    public void AddOwnedOrbForTesting(SkillOrbData orb)
    {
        if (orb != null && orb.IsValid())
        {
            ownedOrbs.Add(orb.Clone());
        }
    }

    private bool TryGetOwnedOrbsPair(
        int indexA, int indexB, out SkillOrbData orbA, out SkillOrbData orbB)
    {
        orbA = null;
        orbB = null;

        if (indexA < 0 || indexA >= ownedOrbs.Count || indexB < 0 || indexB >= ownedOrbs.Count)
        {
            Debug.LogWarning("[SkillShopManager] 合成失敗: インデックス不正");
            return false;
        }

        if (indexA == indexB)
        {
            Debug.LogWarning("[SkillShopManager] 合成失敗: 同一オーブは指定できません");
            return false;
        }

        orbA = ownedOrbs[indexA];
        orbB = ownedOrbs[indexB];
        return orbA != null && orbB != null && orbA.IsValid() && orbB.IsValid();
    }

    private void RemoveOwnedOrbAt(int index)
    {
        if (index >= 0 && index < ownedOrbs.Count)
        {
            ownedOrbs.RemoveAt(index);
        }
    }
}

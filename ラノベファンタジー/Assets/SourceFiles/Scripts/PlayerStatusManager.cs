using UnityEngine;

/// <summary>
/// プレイヤーのジョブ、MP、表ボーナス、隠しステータスを統括管理します。
/// HP / スタミナは既存の CombatStats / PlayerStats が担当し、本クラスは拡張レイヤーとして共存します。
/// PlayerRobot にアタッチして使用してください。
/// </summary>
public class PlayerStatusManager : MonoBehaviour
{
    public static PlayerStatusManager Instance { get; private set; }

    [Header("参照（任意・表示用）")]
    [SerializeField] private CombatStats combatStats;
    [SerializeField] private PlayerStats playerStats;

    [Header("基本情報")]
    [SerializeField] private string mainJob = "歴史書を解読せし者";
    [SerializeField] private string subJob = "なし";
    [SerializeField] private int level = 1;

    [Header("MP（魔力リソース）")]
    [SerializeField] private float maxMP = 50f;
    [SerializeField] private float currentMP;
    [SerializeField] private float mpRegenRate = 5f;
    [SerializeField] private float mpRegenDelay = 2f;

    [Header("表ステータス（戦闘ボーナス）")]
    [SerializeField] private int damageBonus;
    [SerializeField] private int guardBonus;
    [SerializeField] private int speedBonus;
    [SerializeField] private int magicBonus;
    [SerializeField] private int technicalBonus;

    [Header("隠しステータス（裏パラメータ）")]
    [SerializeField] private int luckBonus;
    [SerializeField] private int intelBonus;
    [SerializeField] private int karmaValue;

    [Header("NPC信頼度（計算係数）")]
    [Tooltip("信頼度 = (intelBonus + karmaValue) × この倍率")]
    [SerializeField] private float npcTrustFactorMultiplier = 0.1f;

    /// <summary>ショップ門前払いの既定カルマ閾値。</summary>
    public const int DefaultShopKarmaBanThreshold = -30;

    /// <summary>短絡行動時に加算されるカルマペナルティ（デバッグ用）。</summary>
    public const int ImpulsiveActionKarmaPenalty = -20;

    private float mpRegenDelayTimer;

    // ジョブ補正の乗算元 MP 上限（初回キャプチャ後は不変）
    private bool baseMaxMpCaptured;
    private float baseMaxMP;

    public string MainJob => mainJob;
    public string SubJob => subJob;
    public int Level => level;
    public float CurrentMP => currentMP;
    public float MaxMP => maxMP;
    public int DamageBonus => damageBonus;
    public int GuardBonus => guardBonus;
    public int SpeedBonus => speedBonus;
    public int MagicBonus => magicBonus;
    public int TechnicalBonus => technicalBonus;
    public int LuckBonus => luckBonus;
    public int IntelBonus => intelBonus;
    public int KarmaValue => karmaValue;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[PlayerStatusManager] 重複インスタンスを検出しました。");
        }
        else
        {
            Instance = this;
        }

        CacheReferences();
        currentMP = maxMP;
    }

    private void Start()
    {
        MasterDataManager.EnsureInstance();
        SkillMasterRepository.EnsureInstance();
        ApplyMainJobStatModifiers();
        TryGrantInitialJobDefaultSkills();
    }

    /// <summary>起動時に現在ジョブの defaultSkillIds を未所持分だけ付与します。</summary>
    private void TryGrantInitialJobDefaultSkills()
    {
        PlayerSkillSlotManager skillSlots = GetComponent<PlayerSkillSlotManager>();
        if (skillSlots == null)
        {
            skillSlots = FindAnyObjectByType<PlayerSkillSlotManager>();
        }

        if (skillSlots == null)
        {
            return;
        }

        JobEvolutionManager.TryGrantDefaultSkillsForCurrentJob(this, skillSlots);
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void Update()
    {
        RegenerateMP();
    }

    private void CacheReferences()
    {
        if (combatStats == null)
        {
            combatStats = GetComponent<CombatStats>();
        }

        if (playerStats == null)
        {
            playerStats = GetComponent<PlayerStats>();
        }
    }

    /// <summary>
    /// インテリとカルマから NPC 信頼度（評価係数）を算出します。
    /// 高いほど取引有利・協力的な NPC 応答を想定します。
    /// </summary>
    public float GetNPCTrustFactor()
    {
        return (intelBonus + karmaValue) * npcTrustFactorMultiplier;
    }

    /// <summary>
    /// カルマがショップ取引拒否ラインを下回っているかを返します。
    /// </summary>
    public bool IsBelowShopKarmaThreshold(int threshold = DefaultShopKarmaBanThreshold)
    {
        return karmaValue < threshold;
    }

    /// <summary>テスト用：カルマ値を直接設定します。</summary>
    public void SetKarmaValueForTesting(int value)
    {
        karmaValue = value;
    }

    /// <summary>MP を消費します。</summary>
    public bool TryUseMP(float amount)
    {
        if (amount <= 0f || currentMP < amount)
        {
            return false;
        }

        currentMP -= amount;
        mpRegenDelayTimer = mpRegenDelay;
        return true;
    }

    /// <summary>MP（魔力）を回復します。UniqueSkillRuntimeExecutor の Psychic_Regen_Mana 等から呼ばれます。</summary>
    public void RestoreMP(float amount)
    {
        if (amount <= 0f)
        {
            return;
        }

        currentMP = Mathf.Min(maxMP, currentMP + amount);
    }

    /// <summary>ジョブ名を設定します。</summary>
    public void SetJobs(string newMainJob, string newSubJob)
    {
        SetJobsInternal(newMainJob, newSubJob, applyModifiers: true);
    }

    /// <summary>ジョブ表示のみ更新（NPC ミラー時など、CombatStats は別途同期する場合）。</summary>
    public void SetJobsDisplayOnly(string newMainJob, string newSubJob)
    {
        SetJobsInternal(newMainJob, newSubJob, applyModifiers: false);
    }

    private void SetJobsInternal(string newMainJob, string newSubJob, bool applyModifiers)
    {
        if (!string.IsNullOrWhiteSpace(newMainJob))
        {
            mainJob = newMainJob;
        }

        subJob = string.IsNullOrWhiteSpace(newSubJob) ? "なし" : newSubJob;
        if (applyModifiers)
        {
            ApplyMainJobStatModifiers();
        }
    }

    /// <summary>Safe-Fail 仮アバター用 MP / カルマを初期化します。</summary>
    public void ApplySafeFallbackResources(float mp = 80f)
    {
        maxMP = Mathf.Max(1f, mp);
        currentMP = maxMP;
        karmaValue = 0;
    }

    /// <summary>
    /// mainJob 文字列に基づき JobMasterData.statModifiers を CombatStats / MP へ適用します。
    /// 戦闘開始時の再適用にも利用できます。
    /// </summary>
    public void ApplyMainJobStatModifiers()
    {
        CacheReferences();
        if (combatStats == null)
        {
            return;
        }

        JobStatApplier.ApplyJobModifiers(mainJob, combatStats);
        combatStats.ApplyEquipmentModifiers(InventoryManager.Instance?.EquippedCraftedWeapon);
    }

    /// <summary>JobStatApplier から呼ばれ、manaMultiplier を MP 上限へ反映します。</summary>
    public void ApplyJobManaMultiplier(float manaMultiplier)
    {
        CaptureBaseMaxMpIfNeeded();
        float safeMultiplier = Mathf.Max(0f, manaMultiplier);
        maxMP = Mathf.Max(1f, baseMaxMP * safeMultiplier);
        currentMP = Mathf.Min(currentMP, maxMP);
    }

    /// <summary>レベルを設定します（最低 1）。</summary>
    public void SetLevel(int newLevel)
    {
        level = Mathf.Max(1, newLevel);
    }

    /// <summary>表ボーナスを加算します。</summary>
    public void AddVisibleBonuses(int damage, int guard, int speed, int magic, int technical)
    {
        damageBonus += damage;
        guardBonus += guard;
        speedBonus += speed;
        magicBonus += magic;
        technicalBonus += technical;
    }

    /// <summary>幸運ボーナスを加算します。</summary>
    public void AddLuckBonus(int amount)
    {
        luckBonus += amount;
    }

    /// <summary>インテリボーナスを加算します（熟読で上昇・短絡行動で低下）。</summary>
    public void AddIntelBonus(int amount)
    {
        intelBonus += amount;
    }

    /// <summary>カルマ値を加算します（善行で上昇・悪行で低下）。</summary>
    public void AddKarmaValue(int amount)
    {
        karmaValue += amount;
    }

    /// <summary>フレーバーテキストを熟読した（インテリ +5）。</summary>
    public void SimulateDeepReading()
    {
        AddIntelBonus(5);
        PlayerStatusLog.LogSimulationEvent("文献を熟読した（インテリ +5）");
    }

    /// <summary>短絡的な行動で痛い目を見た（インテリ -5・カルマ -20）。</summary>
    public void SimulateImpulsiveAction()
    {
        AddIntelBonus(-5);
        AddKarmaValue(ImpulsiveActionKarmaPenalty);
        PlayerStatusLog.LogSimulationEvent(
            $"短絡的な行動で痛い目を見た（インテリ -5 / カルマ {ImpulsiveActionKarmaPenalty}）");
    }

    /// <summary>善行をなした（カルマ +10）。</summary>
    public void SimulateGoodDeed()
    {
        AddKarmaValue(10);
        PlayerStatusLog.LogSimulationEvent("善行を積んだ（カルマ +10）");
    }

    /// <summary>悪行を働いた（カルマ -10）。</summary>
    public void SimulateEvilDeed()
    {
        AddKarmaValue(-10);
        PlayerStatusLog.LogSimulationEvent("悪行を働いた（カルマ -10）");
    }

    /// <summary>
    /// 戦闘用の実効攻撃力（CombatStats.STR + 表ボーナス）を返します。
    /// 既存ダメージ計算との連携スタブです。
    /// </summary>
    public int GetEffectiveAttackPower()
    {
        int baseStrength = combatStats != null ? combatStats.Strength : 0;
        return baseStrength + damageBonus;
    }

    /// <summary>
    /// 戦闘用の実効防御力（CombatStats.DEF + 表ボーナス）を返します。
    /// </summary>
    public int GetEffectiveDefense()
    {
        int baseDefense = combatStats != null ? combatStats.Defense : 0;
        return baseDefense + guardBonus;
    }

    /// <summary>
    /// 表ステータスボーナスの合計（攻・守・速・魔・技）。
    /// </summary>
    public int GetTotalVisibleBonusSum()
    {
        return damageBonus + guardBonus + speedBonus + magicBonus + technicalBonus;
    }

    /// <summary>
    /// フィジカル型突破判定用スコア（レベル＋表ボーナス合計）。
    /// </summary>
    public int GetPhysicalAdaptationScore()
    {
        return level + GetTotalVisibleBonusSum();
    }

    /// <summary>操作対象 NPC の MP / カルマをプレイヤー拡張レイヤーへミラーします。</summary>
    public void MirrorResourceLayerFromNpc(NpcStatusManager npc)
    {
        if (npc == null)
        {
            return;
        }

        maxMP = Mathf.Max(1f, npc.MaxMP);
        currentMP = Mathf.Clamp(npc.CurrentMP, 0f, maxMP);
        karmaValue = npc.KarmaValue;
    }

    /// <summary>デバッグ用ステータスサマリーを1本のテキストで返します。</summary>
    public string GetStatusSummaryText()
    {
        return $"[{mainJob}/{subJob} Lv.{level}] MP:{currentMP:F0}/{maxMP:F0} | " +
               $"攻+{damageBonus} 守+{guardBonus} 速+{speedBonus} 魔+{magicBonus} 技+{technicalBonus} | " +
               $"幸+{luckBonus} 賢+{intelBonus} カルマ{karmaValue} | NPC信頼:{GetNPCTrustFactor():F2}";
    }

    /// <summary>自動テスト用：ステータスを初期値に戻します。</summary>
    public void ResetToDefaultForTesting()
    {
        mainJob = "歴史書を解読せし者";
        subJob = "なし";
        level = 1;
        maxMP = 50f;
        currentMP = maxMP;
        damageBonus = 0;
        guardBonus = 0;
        speedBonus = 0;
        magicBonus = 0;
        technicalBonus = 0;
        luckBonus = 0;
        intelBonus = 0;
        karmaValue = 0;
        mpRegenDelayTimer = 0f;
        baseMaxMpCaptured = false;
        baseMaxMP = 0f;
        ApplyMainJobStatModifiers();
    }

    private void CaptureBaseMaxMpIfNeeded()
    {
        if (baseMaxMpCaptured)
        {
            return;
        }

        baseMaxMP = maxMP;
        baseMaxMpCaptured = true;
    }

    private void RegenerateMP()
    {
        if (mpRegenDelayTimer > 0f)
        {
            mpRegenDelayTimer -= Time.deltaTime;
            return;
        }

        if (currentMP >= maxMP)
        {
            return;
        }

        currentMP = Mathf.Min(maxMP, currentMP + mpRegenRate * Time.deltaTime);
    }
}

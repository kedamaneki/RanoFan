using UnityEngine;

// =============================================================================
// 魔物ステータス拡張レイヤー — PlayerStatusManager と対称
// 基礎戦闘は CombatStats、行動リソースは EnemyActionStats
// =============================================================================

/// <summary>
/// 魔物の MP・生態パラメータ・隠し脅威値を管理します。
/// 人型亜種は扱わず、魔獣・異形のみを想定します。
/// </summary>
[RequireComponent(typeof(CombatStats))]
[RequireComponent(typeof(EnemyActionStats))]
[DefaultExecutionOrder(40)]
public class EnemyStatusManager : MonoBehaviour
{
    public const float DefaultMaxMp = 40f;
    public const float DefaultMpRegen = 4f;

    [Header("参照")]
    [SerializeField] private CombatStats combatStats;
    [SerializeField] private EnemyActionStats actionStats;

    [Header("生態プロファイル")]
    [SerializeField] private EnemyEcologyProfileKind profileKind = EnemyEcologyProfileKind.Slime;
    [SerializeField] private string displayName = "異形生命";

    [Header("MP（魔力）")]
    [SerializeField] private float maxMP = DefaultMaxMp;
    [SerializeField] private float currentMP;
    [SerializeField] private float mpRegenRate = DefaultMpRegen;
    [SerializeField] private float mpRegenDelay = 2f;

    [Header("生態パラメータ")]
    [Range(0f, 100f)] [SerializeField] private float manaHunger = 35f;
    [Range(0f, 100f)] [SerializeField] private float territoriality = 40f;

    [Header("隠しパラメータ")]
    [SerializeField] private int threatLevel = 1;
    [SerializeField] private int karma;
    [SerializeField] private int dangerLevel = 1;

    [Header("部位素材（Purity / Density）")]
    [SerializeField] private float partPurity = 40f;
    [SerializeField] private float partDensity = 30f;

    private float mpRegenDelayTimer;
    private bool modifiersApplied;

    public CombatStats Combat => combatStats;
    public EnemyActionStats Action => actionStats;
    public EnemyEcologyProfileKind ProfileKind => profileKind;
    public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? name : displayName;
    public float CurrentMP => currentMP;
    public float MaxMP => maxMP;
    public float ManaHunger => manaHunger;
    public float Territoriality => territoriality;
    public int ThreatLevel => threatLevel;
    public int Karma => karma;
    public int DangerLevel => dangerLevel;
    public float PartPurity => partPurity;
    public float PartDensity => partDensity;
    public bool IsMpDepleted => currentMP <= 0.01f;

    /// <summary>攻撃コンボ JSON 側の行動パターン ID。</summary>
    public string BehaviorPatternId => EnemyStatModifier.ResolveBehaviorPatternId(profileKind);

    private void Reset()
    {
        combatStats = GetComponent<CombatStats>();
        actionStats = GetComponent<EnemyActionStats>();
        if (combatStats != null)
        {
            // 敵としてマーク（ログ振り分け）
        }
    }

    private void Awake()
    {
        CacheReferences();
        EnsureDefaults();
        currentMP = maxMP;
        if (combatStats != null)
        {
            combatStats.OnDied += HandleDied;
        }
    }

    private void Start()
    {
        ApplyAllModifiers(log: true);
    }

    private void OnDestroy()
    {
        if (combatStats != null)
        {
            combatStats.OnDied -= HandleDied;
        }
    }

    private void Update()
    {
        RegenerateMp();
        TickManaHungerPassive();
    }

    public void CacheReferences()
    {
        if (combatStats == null)
        {
            combatStats = GetComponent<CombatStats>();
        }

        if (actionStats == null)
        {
            actionStats = GetComponent<EnemyActionStats>();
        }

        if (actionStats == null)
        {
            actionStats = gameObject.AddComponent<EnemyActionStats>();
        }

        if (combatStats == null)
        {
            combatStats = gameObject.AddComponent<CombatStats>();
        }
    }

    /// <summary>欠損パラメータを既定値へ戻します（Safe-Fail）。</summary>
    public void EnsureDefaults()
    {
        CacheReferences();
        actionStats?.EnsureDefaults();

        if (maxMP <= 0.01f || float.IsNaN(maxMP))
        {
            maxMP = DefaultMaxMp;
        }

        if (mpRegenRate < 0f || float.IsNaN(mpRegenRate))
        {
            mpRegenRate = DefaultMpRegen;
        }

        manaHunger = Mathf.Clamp(manaHunger, 0f, 100f);
        territoriality = Mathf.Clamp(territoriality, 0f, 100f);
        currentMP = Mathf.Clamp(currentMP, 0f, maxMP);
        threatLevel = Mathf.Max(0, threatLevel);
        dangerLevel = Mathf.Max(0, dangerLevel);

        if (float.IsNaN(partPurity) || float.IsInfinity(partPurity))
        {
            partPurity = 40f;
        }

        if (float.IsNaN(partDensity) || float.IsInfinity(partDensity))
        {
            partDensity = 30f;
        }
    }

    /// <summary>プロファイル・部位・時代補正を CombatStats / MP へ適用します。</summary>
    public void ApplyAllModifiers(bool log)
    {
        try
        {
            EnsureDefaults();
            EnemyStatModifier.ApplyToEnemy(this, log);
            modifiersApplied = true;
        }
        catch (System.Exception exception)
        {
            Debug.LogWarning($"[EnemyStatusManager] 補正適用 Safe-Fail: {exception.Message}");
            modifiersApplied = false;
        }
    }

    public bool TryUseMP(float amount)
    {
        EnsureDefaults();
        float cost = EnemyStatModifier.ScaleMpCost(amount);
        if (cost <= 0f)
        {
            return true;
        }

        if (currentMP < cost)
        {
            return false;
        }

        currentMP -= cost;
        mpRegenDelayTimer = mpRegenDelay;
        return true;
    }

    public void RestoreMP(float amount)
    {
        EnsureDefaults();
        if (amount <= 0f)
        {
            return;
        }

        currentMP = Mathf.Min(maxMP, currentMP + amount);
    }

    public void SetManaHunger(float value)
    {
        manaHunger = Mathf.Clamp(value, 0f, 100f);
    }

    public void AddManaHunger(float delta)
    {
        manaHunger = Mathf.Clamp(manaHunger + delta, 0f, 100f);
    }

    public void SetTerritoriality(float value)
    {
        territoriality = Mathf.Clamp(value, 0f, 100f);
    }

    public void SetProfile(EnemyEcologyProfileKind kind, string nameOverride = null)
    {
        profileKind = kind;
        if (!string.IsNullOrWhiteSpace(nameOverride))
        {
            displayName = nameOverride;
        }

        modifiersApplied = false;
        ApplyAllModifiers(log: false);
    }

    public void SetPartMaterial(float purity, float density)
    {
        partPurity = purity;
        partDensity = density;
        ApplyAllModifiers(log: false);
    }

    public void ConfigureHidden(int threat, int karmaValue, int danger)
    {
        threatLevel = Mathf.Max(0, threat);
        karma = karmaValue;
        dangerLevel = Mathf.Max(0, danger);
    }

    /// <summary>JobStatApplier 相当: mana 倍率を MP 上限へ反映。</summary>
    public void ApplyManaMultiplier(float manaMultiplier)
    {
        float safe = manaMultiplier > 0f && !float.IsNaN(manaMultiplier) ? manaMultiplier : 1f;
        float baseMp = DefaultMaxMp * EnemyStatModifier.GetProfile(profileKind).ManaMultiplier;
        maxMP = Mathf.Max(1f, baseMp * safe);
        currentMP = Mathf.Min(currentMP <= 0f ? maxMP : currentMP, maxMP);
    }

    public string FormatSnapshot()
    {
        string stam = actionStats != null ? actionStats.FormatSnapshot() : "Stamina ?";
        return $"{DisplayName}[{profileKind}] HP {(combatStats != null ? combatStats.CurrentHp : 0)}/" +
               $"{(combatStats != null ? combatStats.MaxHp : 0)} MP {currentMP:F0}/{maxMP:F0} " +
               $"{stam} Hunger {manaHunger:F0} Threat {threatLevel}";
    }

    private void RegenerateMp()
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

    private void TickManaHungerPassive()
    {
        // 活性期は代謝亢進で飢餓が上がる（位相はシミュレータ側でも加算）
        if (!modifiersApplied)
        {
            return;
        }
    }

    private void HandleDied()
    {
        Debug.Log($"<color=#EF9A9A>【魔物・消滅】</color> {DisplayName} が生態系から外れました。");
    }
}

using UnityEngine;

/// <summary>
/// プレイヤーのスタミナ管理コンポーネント。
/// HP / STR / DEF は CombatStats が担当します。
/// </summary>
public class PlayerStats : MonoBehaviour
{
    [Header("参照")]
    [Tooltip("共通戦闘ステータス（HP / STR / DEF）")]
    [SerializeField] private CombatStats combatStats;

    [Header("スタミナ")]
    [Tooltip("最大スタミナ")]
    public float maxStamina = 100f;

    [Tooltip("現在スタミナ（実行時に自動で最大値に設定されます）")]
    [SerializeField] private float currentStamina;

    [Tooltip("1 秒あたりのスタミナ自動回復量")]
    public float staminaRegenRate = 20f;

    [Tooltip("スタミナ消費後、回復が再開するまでの待ち時間（秒）")]
    public float regenDelay = 1.5f;

    private float regenDelayTimer;

    // CombatStats へ委譲するプロパティ（既存 UI 等との互換用）
    public float CurrentHp => combatStats != null ? combatStats.CurrentHp : 0f;
    public float maxHp => combatStats != null ? combatStats.MaxHp : 0f;
    public float CurrentStamina => currentStamina;
    public bool IsAlive => combatStats != null && combatStats.IsAlive;

    private void Reset()
    {
        combatStats = GetComponent<CombatStats>();
    }

    private void Awake()
    {
        if (combatStats == null)
        {
            combatStats = GetComponent<CombatStats>();
        }

        currentStamina = maxStamina;
    }

    private void Update()
    {
        RegenerateStamina();
    }

    private void RegenerateStamina()
    {
        if (regenDelayTimer > 0f)
        {
            regenDelayTimer -= Time.deltaTime;
            return;
        }

        if (currentStamina >= maxStamina)
        {
            return;
        }

        float regenRate = staminaRegenRate;
        InventoryManager inventory = InventoryManager.Instance;
        if (inventory != null && inventory.IsOverweight())
        {
            // 重量オーバー時は回復を止めず減速のみ（UI 表示スタミナは常に回復する）
            regenRate *= 0.35f;
        }

        currentStamina = Mathf.Min(maxStamina, currentStamina + regenRate * Time.deltaTime);
    }

    public bool CanUseStamina(float cost)
    {
        return currentStamina >= cost;
    }

    public void UseStamina(float amount)
    {
        if (amount <= 0f || !CanUseStamina(amount))
        {
            return;
        }

        currentStamina -= amount;
        regenDelayTimer = regenDelay;
    }

    /// <summary>被弾ペナルティなど、消費可否に関わらずスタミナを直接減らします。</summary>
    /// <param name="amount">減少量</param>
    public void DrainStaminaDirect(float amount)
    {
        if (amount <= 0f)
        {
            return;
        }

        currentStamina = Mathf.Max(0f, currentStamina - amount);
        regenDelayTimer = regenDelay;
    }

    /// <summary>ジャストパリィ返却などでスタミナを回復します。</summary>
    /// <param name="amount">回復量</param>
    public void RestoreStamina(float amount)
    {
        if (amount <= 0f)
        {
            return;
        }

        currentStamina = Mathf.Min(maxStamina, currentStamina + amount);
    }

    /// <summary>自動テスト用：スタミナを直接設定（InspirationSystemTester 専用）</summary>
    public void SetStaminaForTesting(float stamina)
    {
        currentStamina = Mathf.Clamp(stamina, 0f, maxStamina);
        regenDelayTimer = regenDelay;
    }
}

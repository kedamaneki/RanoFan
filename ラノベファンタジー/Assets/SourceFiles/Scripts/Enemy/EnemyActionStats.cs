using UnityEngine;

// =============================================================================
// 魔物の行動リソース — プレイヤー PlayerStats（スタミナ）と対称
// =============================================================================

/// <summary>
/// 連続攻撃・強襲で消費するスタミナ。0 で IsExhausted となり息整えへ移行します。
/// </summary>
[RequireComponent(typeof(CombatStats))]
public class EnemyActionStats : MonoBehaviour
{
    public const float DefaultMaxStamina = 100f;
    public const float DefaultRegenRate = 18f;
    public const float DefaultRegenDelay = 1.2f;
    public const float ExhaustRecoverThreshold = 28f;

    [Header("スタミナ")]
    [SerializeField] private float maxStamina = DefaultMaxStamina;
    [SerializeField] private float currentStamina;
    [SerializeField] private float staminaRegenRate = DefaultRegenRate;
    [SerializeField] private float regenDelay = DefaultRegenDelay;

    [Header("息整え")]
    [SerializeField] private float exhaustHoldSeconds = 1.6f;

    private float regenDelayTimer;
    private float exhaustTimer;
    private bool wasExhausted;

    public float MaxStamina => maxStamina;
    public float CurrentStamina => currentStamina;
    public float StaminaRegenRate => staminaRegenRate;
    public bool IsExhausted => currentStamina <= 0.01f || exhaustTimer > 0f;
    public float ExhaustRemaining => exhaustTimer;

    /// <summary>プロファイル補正などで最大スタミナを再設定します。</summary>
    public void SetMaxStamina(float value, bool refillRatio = true)
    {
        float ratio = maxStamina > 0.01f ? currentStamina / maxStamina : 1f;
        maxStamina = value > 0.01f && !float.IsNaN(value) ? value : DefaultMaxStamina;
        if (refillRatio)
        {
            currentStamina = Mathf.Clamp(maxStamina * Mathf.Clamp01(ratio), 0f, maxStamina);
        }
        else
        {
            currentStamina = Mathf.Clamp(currentStamina, 0f, maxStamina);
        }
    }

    public void SetRegenRate(float rate)
    {
        staminaRegenRate = rate >= 0f && !float.IsNaN(rate) ? rate : DefaultRegenRate;
    }

    private void Awake()
    {
        EnsureDefaults();
        currentStamina = maxStamina;
    }

    private void Update()
    {
        if (exhaustTimer > 0f)
        {
            exhaustTimer -= Time.deltaTime;
            if (exhaustTimer <= 0f && currentStamina < ExhaustRecoverThreshold)
            {
                currentStamina = Mathf.Min(maxStamina, ExhaustRecoverThreshold);
            }
        }

        RegenerateStamina();
    }

    /// <summary>欠損時は既定値へ Safe-Fail します。</summary>
    public void EnsureDefaults()
    {
        if (maxStamina <= 0.01f || float.IsNaN(maxStamina) || float.IsInfinity(maxStamina))
        {
            maxStamina = DefaultMaxStamina;
        }

        if (staminaRegenRate < 0f || float.IsNaN(staminaRegenRate))
        {
            staminaRegenRate = DefaultRegenRate;
        }

        if (regenDelay < 0f)
        {
            regenDelay = DefaultRegenDelay;
        }

        currentStamina = Mathf.Clamp(currentStamina, 0f, maxStamina);
    }

    public bool CanSpend(float cost)
    {
        EnsureDefaults();
        return !IsExhausted && currentStamina >= cost;
    }

    /// <summary>スタミナを消費します。枯渇時は息整えタイマーを開始します。</summary>
    public bool TrySpend(float amount)
    {
        EnsureDefaults();
        if (amount <= 0f)
        {
            return true;
        }

        if (IsExhausted || currentStamina < amount)
        {
            EnterExhaustion("消費失敗");
            return false;
        }

        currentStamina = Mathf.Max(0f, currentStamina - amount);
        regenDelayTimer = regenDelay;
        if (currentStamina <= 0.01f)
        {
            EnterExhaustion("スタミナ切れ");
        }

        return true;
    }

    /// <summary>強制消費（失敗しても枯渇扱いへ）。</summary>
    public void Drain(float amount)
    {
        EnsureDefaults();
        if (amount <= 0f)
        {
            return;
        }

        currentStamina = Mathf.Max(0f, currentStamina - amount);
        regenDelayTimer = regenDelay;
        if (currentStamina <= 0.01f)
        {
            EnterExhaustion("強制消耗");
        }
    }

    public void Restore(float amount)
    {
        EnsureDefaults();
        if (amount <= 0f)
        {
            return;
        }

        currentStamina = Mathf.Min(maxStamina, currentStamina + amount);
        if (currentStamina >= ExhaustRecoverThreshold)
        {
            exhaustTimer = 0f;
            wasExhausted = false;
        }
    }

    public void EnterExhaustion(string reason)
    {
        if (!wasExhausted)
        {
            Debug.Log(
                $"<color=#FFAB91><b>【魔物・息整え】</b></color> {name}: {reason} → " +
                $"威嚇・間合い取り {exhaustHoldSeconds:F1}s（Stamina {currentStamina:F0}/{maxStamina:F0}）");
        }

        wasExhausted = true;
        exhaustTimer = Mathf.Max(exhaustTimer, exhaustHoldSeconds);
        currentStamina = 0f;
    }

    public string FormatSnapshot()
    {
        string tag = IsExhausted ? " EXHAUSTED" : string.Empty;
        return $"Stamina {currentStamina:F0}/{maxStamina:F0}{tag}";
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

        float rate = staminaRegenRate;
        if (IsExhausted)
        {
            rate *= 0.45f;
        }

        currentStamina = Mathf.Min(maxStamina, currentStamina + rate * Time.deltaTime);
        if (wasExhausted && currentStamina >= ExhaustRecoverThreshold && exhaustTimer <= 0f)
        {
            wasExhausted = false;
            Debug.Log($"<color=#A5D6A7>【魔物・復帰】</color> {name}: 息整え終了 Stamina {currentStamina:F0}");
        }
    }
}

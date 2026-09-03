using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// PlayerStats の HP / スタミナを UI の Slider に反映するスクリプト。
/// </summary>
public class PlayerStatsUI : MonoBehaviour
{
    [Header("参照")]
    [SerializeField] private PlayerStats stats;
    [SerializeField] private CombatStats combatStats;
    [SerializeField] private Slider hpSlider;
    [SerializeField] private Slider staminaSlider;

    private void Awake()
    {
        if (stats != null)
        {
            combatStats = stats.GetComponent<CombatStats>();
            if (combatStats == null)
            {
                combatStats = stats.GetComponentInParent<CombatStats>();
            }
        }
    }

    private void Update()
    {
        if (combatStats == null || stats == null || hpSlider == null || staminaSlider == null)
        {
            return;
        }

        float maxHpValue = combatStats.MaxHp;
        hpSlider.value = maxHpValue > 0 ? (float)combatStats.CurrentHp / maxHpValue : 0f;
        staminaSlider.value = stats.CurrentStamina / stats.maxStamina;
    }
}

using UnityEngine;

/// <summary>
/// プレイヤーに触れたら共通ダメージ計算でダメージを与えるテスト用の敵スクリプト。
/// </summary>
[RequireComponent(typeof(Collider))]
[RequireComponent(typeof(CombatStats))]
public class EnemyAttackTest : MonoBehaviour
{
    [SerializeField] private CombatStats combatStats;

    [SerializeField] private float hitCooldown = 1f;

    private float lastHitTime = -999f;

    private void Reset()
    {
        combatStats = GetComponent<CombatStats>();
        Collider col = GetComponent<Collider>();
        col.isTrigger = true;
    }

    private void Awake()
    {
        if (combatStats == null)
        {
            combatStats = GetComponent<CombatStats>();
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player"))
        {
            return;
        }

        if (Time.time - lastHitTime < hitCooldown)
        {
            return;
        }

        CombatStats playerStats = other.GetComponentInParent<CombatStats>();
        if (playerStats == null || combatStats == null)
        {
            Debug.LogWarning("CombatStats が見つかりません。");
            return;
        }

        int damage = playerStats.ReceiveDamage(combatStats.Strength);
        if (damage > 0)
        {
            lastHitTime = Time.time;
            Debug.Log($"プレイヤーが {damage} ダメージを受けた！ 残り HP: {playerStats.CurrentHp}");
        }
    }
}

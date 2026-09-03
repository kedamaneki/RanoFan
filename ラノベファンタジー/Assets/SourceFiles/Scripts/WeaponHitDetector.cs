using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 武器のトリガーコライダーを管理し、攻撃中だけ敵への当たり判定を有効にします。
/// ダメージは攻撃者の CombatStats.STR を基準に共通計算します。
/// </summary>
public class WeaponHitDetector : MonoBehaviour
{
    [Header("攻撃設定")]
    [Tooltip("武器による STR 補正（0 なら攻撃者の STR のみ使用）")]
    [SerializeField] private int weaponStrengthBonus = 0;

    [SerializeField] private Collider attackCollider;

    [Header("参照")]
    [Tooltip("攻撃者の CombatStats（未設定なら親から自動取得）")]
    [SerializeField] private CombatStats attackerStats;

    private readonly HashSet<CombatStats> hitEnemiesThisSwing = new HashSet<CombatStats>();

    // 派生攻撃技から設定されるダメージ倍率（1 = 通常）
    private float currentDamageMultiplier = 1f;

    // 今回の攻撃スイングで使用している技 ID（熟練度加算用）
    private string currentActionId;

    private void Reset()
    {
        attackCollider = GetComponent<Collider>();
        attackerStats = GetComponentInParent<CombatStats>();
    }

    private void Awake()
    {
        if (attackCollider == null)
        {
            attackCollider = GetComponent<Collider>();
        }

        if (attackerStats == null)
        {
            attackerStats = GetComponentInParent<CombatStats>();
        }
    }

    private void Start()
    {
        DisableAttackDetection();
    }

    /// <summary>
    /// 攻撃判定を有効化します。派生攻撃技使用時は damageMultiplier を指定してください。
    /// </summary>
    /// <param name="damageMultiplier">ダメージ倍率</param>
    /// <param name="actionId">今回の攻撃で使用する技 ID（熟練度加算用。省略可）</param>
    public void EnableAttackDetection(float damageMultiplier = 1f, string actionId = null)
    {
        if (attackCollider == null)
        {
            Debug.LogWarning($"{name}: 攻撃用コライダーが設定されていません。");
            return;
        }

        currentDamageMultiplier = Mathf.Max(0f, damageMultiplier);
        currentActionId = actionId;
        hitEnemiesThisSwing.Clear();
        attackCollider.enabled = true;
    }

    public void DisableAttackDetection()
    {
        if (attackCollider != null)
        {
            attackCollider.enabled = false;
        }

        currentDamageMultiplier = 1f;
        currentActionId = null;
        hitEnemiesThisSwing.Clear();
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Enemy"))
        {
            return;
        }

        CombatStats enemyStats = other.GetComponentInParent<CombatStats>();
        if (enemyStats == null || !enemyStats.IsAlive || attackerStats == null)
        {
            return;
        }

        if (hitEnemiesThisSwing.Contains(enemyStats))
        {
            return;
        }

        hitEnemiesThisSwing.Add(enemyStats);

        int baseStrength = attackerStats.Strength + weaponStrengthBonus;
        int effectiveStrength = Mathf.Max(1, Mathf.RoundToInt(baseStrength * currentDamageMultiplier));
        int damage = enemyStats.ReceiveDamage(effectiveStrength);

        if (damage > 0)
        {
            PlayerActionLogger.Instance?.LogHit();
            GamePhaseEventBridge.Instance?.UpdateMetricFromActionId(currentActionId);
            NotifyMasteryOnHit();
            Debug.Log($"武器が {enemyStats.name} に {damage} ダメージを与えました。（STR: {baseStrength} x{currentDamageMultiplier:F2} = {effectiveStrength}）");
        }
    }

    /// <summary>敵ヒット時にブリッジ経由で熟練度を加算します。</summary>
    private void NotifyMasteryOnHit()
    {
        if (string.IsNullOrWhiteSpace(currentActionId))
        {
            return;
        }

        PlayerCombatInspirationBridge bridge = PlayerCombatInspirationBridge.Instance;
        if (bridge != null)
        {
            bridge.NotifyActionHitEnemy(currentActionId);
            return;
        }

        ActionMasteryManager.Instance?.AddMasteryOnEnemyHit(currentActionId);
    }
}

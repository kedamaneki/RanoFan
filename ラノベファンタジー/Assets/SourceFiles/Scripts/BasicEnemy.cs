using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// NavMeshAgent を使ってプレイヤーを追跡し、接近したら攻撃する基本的な敵 AI。
/// 戦闘ステータスは CombatStats（プレイヤーと同じ基盤）を使用します。
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(CombatStats))]
public class BasicEnemy : MonoBehaviour
{
    [Header("ターゲット")]
    [SerializeField] private Transform target;

    [Header("移動・攻撃")]
    [SerializeField] private float speed = 3.5f;
    [SerializeField] private float attackRange = 2f;
    [SerializeField] private float attackCooldown = 1.5f;

    [Header("参照")]
    [SerializeField] private CombatStats combatStats;

    private NavMeshAgent agent;
    private float lastAttackTime = -999f;

    private void Reset()
    {
        agent = GetComponent<NavMeshAgent>();
        combatStats = GetComponent<CombatStats>();
        agent.speed = speed;
        agent.stoppingDistance = attackRange * 0.9f;
    }

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        if (combatStats == null)
        {
            combatStats = GetComponent<CombatStats>();
        }

        ApplyAgentSettings();
    }

    private void Start()
    {
        if (target == null)
        {
            GameObject player = GameObject.FindGameObjectWithTag("Player");
            if (player != null)
            {
                target = player.transform;
            }
            else
            {
                Debug.LogWarning($"{name}: 追跡対象が見つかりません。");
            }
        }

        target = ResolveMovementTarget(target);

        if (combatStats != null)
        {
            combatStats.OnDied += HandleDeath;
        }
    }

    private void OnDestroy()
    {
        if (combatStats != null)
        {
            combatStats.OnDied -= HandleDeath;
        }
    }

    private void Update()
    {
        if (combatStats == null || !combatStats.IsAlive || target == null || agent == null)
        {
            return;
        }

        float distance = GetHorizontalDistance(transform.position, target.position);

        if (distance > attackRange)
        {
            ChaseTarget();
        }
        else
        {
            StopAndFaceTarget();
            TryAttack();
        }
    }

    private void ApplyAgentSettings()
    {
        agent.speed = speed;
        agent.stoppingDistance = attackRange * 0.9f;
    }

    private void ChaseTarget()
    {
        agent.isStopped = false;
        agent.SetDestination(target.position);
    }

    private void StopAndFaceTarget()
    {
        agent.isStopped = true;
        agent.ResetPath();

        Vector3 lookDirection = target.position - transform.position;
        lookDirection.y = 0f;

        if (lookDirection.sqrMagnitude > 0.01f)
        {
            transform.rotation = Quaternion.LookRotation(lookDirection);
        }
    }

    private void TryAttack()
    {
        if (Time.time - lastAttackTime < attackCooldown)
        {
            return;
        }

        PerformAttack();
        lastAttackTime = Time.time;
    }

    /// <summary>
    /// 敵の STR を基準に、プレイヤーへ共通ダメージ計算で攻撃
    /// </summary>
    private void PerformAttack()
    {
        if (target == null)
        {
            return;
        }

        float distance = GetHorizontalDistance(transform.position, target.position);
        if (distance > attackRange)
        {
            return;
        }

        CombatStats playerCombat = target.GetComponentInParent<CombatStats>();
        if (playerCombat == null || !playerCombat.IsAlive || combatStats == null)
        {
            return;
        }

        int damage = playerCombat.ReceiveDamage(combatStats.Strength);
        if (damage > 0)
        {
            Debug.Log($"エネミーが攻撃しました（与ダメージ: {damage}）");
        }
    }

    private void HandleDeath()
    {
        if (agent != null)
        {
            agent.isStopped = true;
            agent.enabled = false;
        }

        enabled = false;
    }

    /// <summary>
    /// 追跡・攻撃距離の判定に使う、実際に移動するプレイヤー Transform を返す。
    /// PlayerRobot 親は CharacterController で動かず、子 Robot だけが動く構成に対応します。
    /// </summary>
    private static Transform ResolveMovementTarget(Transform playerRoot)
    {
        if (playerRoot == null)
        {
            return null;
        }

        CharacterController characterController = playerRoot.GetComponentInChildren<CharacterController>();
        return characterController != null ? characterController.transform : playerRoot;
    }

    private static float GetHorizontalDistance(Vector3 from, Vector3 to)
    {
        from.y = 0f;
        to.y = 0f;
        return Vector3.Distance(from, to);
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, attackRange);
    }
#endif
}

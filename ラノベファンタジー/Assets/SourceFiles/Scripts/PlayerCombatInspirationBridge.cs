using UnityEngine;

/// <summary>
/// 戦闘システム（攻撃・被弾ログ）と InspirationManager を仲介します。
/// プレイヤー本体（PlayerRobot 等）にアタッチし、各所から Notify 系メソッドを呼び出してください。
/// </summary>
public class PlayerCombatInspirationBridge : MonoBehaviour
{
    public static PlayerCombatInspirationBridge Instance { get; private set; }

    [Header("参照（未設定なら同オブジェクトから自動取得）")]
    [SerializeField] private PlayerStats playerStats;
    [SerializeField] private CombatStats combatStats;
    [SerializeField] private InspirationManager inspirationManager;
    [SerializeField] private ActionMasteryManager actionMasteryManager;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;
        CacheReferences();
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void CacheReferences()
    {
        if (playerStats == null)
        {
            playerStats = GetComponent<PlayerStats>();
        }

        if (playerStats == null)
        {
            playerStats = GetComponentInParent<PlayerStats>();
        }

        if (combatStats == null)
        {
            combatStats = GetComponent<CombatStats>();
        }

        if (combatStats == null && playerStats != null)
        {
            combatStats = playerStats.GetComponent<CombatStats>();
        }

        if (combatStats == null)
        {
            combatStats = GetComponentInParent<CombatStats>();
        }

        if (inspirationManager == null)
        {
            inspirationManager = InspirationManager.Instance;
        }

        if (inspirationManager == null)
        {
            inspirationManager = FindAnyObjectByType<InspirationManager>();
        }

        if (actionMasteryManager == null)
        {
            actionMasteryManager = ActionMasteryManager.Instance;
        }

        if (actionMasteryManager == null)
        {
            actionMasteryManager = GetComponent<ActionMasteryManager>();
        }

        if (actionMasteryManager == null)
        {
            actionMasteryManager = GetComponentInParent<ActionMasteryManager>();
        }

        if (actionMasteryManager == null)
        {
            actionMasteryManager = FindAnyObjectByType<ActionMasteryManager>();
        }
    }

    /// <summary>
    /// プレイヤーが攻撃を開始した瞬間に PlayerAttackController から呼び出します。
    /// HP・スタミナのピンチ条件を InspirationManager が判定します。
    /// </summary>
    public void NotifyPlayerAttackStarted()
    {
        CacheReferences();

        if (inspirationManager == null)
        {
            Debug.LogWarning("[PlayerCombatInspirationBridge] InspirationManager が見つかりません。");
            return;
        }

        if (playerStats == null || combatStats == null)
        {
            Debug.LogWarning("[PlayerCombatInspirationBridge] PlayerStats / CombatStats が未設定です。");
            return;
        }

        inspirationManager.TriggerPinchInspiration(playerStats, combatStats);
    }

    /// <summary>
    /// PlayerActionLogger がニアミス数を加算した直後に呼び出します。
    /// 閾値を超えた最初の 1 回のみ、ログ監視型閃きを発火します。
    /// </summary>
    public void NotifyNearMissLogged()
    {
        CacheReferences();

        if (inspirationManager == null)
        {
            Debug.LogWarning("[PlayerCombatInspirationBridge] InspirationManager が見つかりません。");
            return;
        }

        inspirationManager.TriggerLogInspiration();
    }

    /// <summary>
    /// WeaponHitDetector が敵にダメージを与えた直後に呼び出します。
    /// 実戦ヒットのみ、該当技の熟練度を +1 します。
    /// </summary>
    public void NotifyActionHitEnemy(string actionId)
    {
        CacheReferences();

        if (actionMasteryManager == null)
        {
            actionMasteryManager = ActionMasteryManager.Instance;
        }

        if (actionMasteryManager != null)
        {
            actionMasteryManager.AddMasteryOnEnemyHit(actionId);
            return;
        }

        ActionMasteryManager.Instance?.AddMasteryOnEnemyHit(actionId);
    }
}

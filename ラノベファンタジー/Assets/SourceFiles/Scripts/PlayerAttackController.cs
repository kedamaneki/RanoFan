using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// プレイヤーの近接攻撃入力を管理し、武器の当たり判定を一定時間だけ有効にします。
/// プレイヤー本体（PlayerRobot など）にアタッチしてください。
/// </summary>
public class PlayerAttackController : MonoBehaviour
{
    [Header("参照")]
    [Tooltip("武器の当たり判定スクリプト")]
    [SerializeField] private WeaponHitDetector weaponHitDetector;

    [Tooltip("HP・スタミナ管理（未設定なら同じオブジェクトまたは親から自動取得）")]
    [SerializeField] private PlayerStats stats;

    [Tooltip("派生技リストを管理する PlayerController（未設定なら同オブジェクトから自動取得）")]
    [SerializeField] private PlayerController playerController;

    [Tooltip("スキル・閃きシステム（未設定なら同オブジェクトから自動取得）")]
    [SerializeField] private PlayerSkillSlotManager skillSlotManager;

    [Tooltip("戦闘→閃き連動ブリッジ（未設定なら同オブジェクトまたは Instance から自動取得）")]
    [SerializeField] private PlayerCombatInspirationBridge inspirationBridge;

    [Header("攻撃設定（通常攻撃のデフォルト値）")]
    [Tooltip("1 回の攻撃で消費するスタミナ")]
    [SerializeField] private float staminaCost = 20f;

    [Tooltip("攻撃判定が有効な時間（秒）※アニメーション未実装時の仮時間")]
    [SerializeField] private float attackDuration = 0.3f;

    // 攻撃中かどうか（連打防止）
    private bool isAttacking;

    // 前回の攻撃開始時刻（Time.time）。初回は -1 のまま
    private float lastAttackStartTime = -1f;

    private void Reset()
    {
        stats = GetComponent<PlayerStats>();
        if (stats == null)
        {
            stats = GetComponentInParent<PlayerStats>();
        }

        weaponHitDetector = GetComponentInChildren<WeaponHitDetector>();
        playerController = GetComponent<PlayerController>();
        skillSlotManager = GetComponent<PlayerSkillSlotManager>();
        inspirationBridge = GetComponent<PlayerCombatInspirationBridge>();
    }

    private void Awake()
    {
        if (stats == null)
        {
            stats = GetComponent<PlayerStats>();
        }

        if (stats == null)
        {
            stats = GetComponentInParent<PlayerStats>();
        }

        if (playerController == null)
        {
            playerController = GetComponent<PlayerController>();
        }

        if (playerController == null)
        {
            playerController = GetComponentInParent<PlayerController>();
        }

        if (skillSlotManager == null)
        {
            skillSlotManager = GetComponent<PlayerSkillSlotManager>();
        }

        if (skillSlotManager == null)
        {
            skillSlotManager = GetComponentInParent<PlayerSkillSlotManager>();
        }

        if (inspirationBridge == null)
        {
            inspirationBridge = GetComponent<PlayerCombatInspirationBridge>();
        }

        if (inspirationBridge == null)
        {
            inspirationBridge = GetComponentInParent<PlayerCombatInspirationBridge>();
        }

        if (weaponHitDetector == null)
        {
            weaponHitDetector = GetComponentInChildren<WeaponHitDetector>();
        }

        if (weaponHitDetector == null)
        {
            Debug.LogWarning("WeaponHitDetector が見つかりません。武器オブジェクトに WeaponHitDetector をアタッチしてください。");
        }
    }

    private void Update()
    {
        HandleAttackInput();
    }

    /// <summary>
    /// 左クリックで攻撃を開始
    /// </summary>
    private void HandleAttackInput()
    {
        if (StarterAssets.StarterAssetsInputs.IsUiModeActive)
        {
            return;
        }

        if (Mouse.current == null || !Mouse.current.leftButton.wasPressedThisFrame)
        {
            return;
        }

        if (isAttacking)
        {
            return;
        }

        if (stats == null || !stats.IsAlive)
        {
            return;
        }

        if (weaponHitDetector == null)
        {
            return;
        }

        // トリガー2：九死に一生 — 攻撃開始時にブリッジ経由で閃き判定
        if (inspirationBridge == null)
        {
            inspirationBridge = PlayerCombatInspirationBridge.Instance;
        }

        if (inspirationBridge != null)
        {
            inspirationBridge.NotifyPlayerAttackStarted();
        }
        else
        {
            CombatStats combatStats = GetComponent<CombatStats>();
            if (combatStats == null)
            {
                combatStats = GetComponentInParent<CombatStats>();
            }

            InspirationManager.Instance?.TriggerPinchInspiration(stats, combatStats);
        }

        // --- 派生攻撃技の適用（器）---
        // 優先: PlayerSkillSlotManager（閃きシステム）→ 旧 DerivedAction リスト → 通常攻撃デフォルト
        float effectiveStaminaCost = staminaCost;
        float effectiveAttackDuration = attackDuration;
        float effectiveDamageMultiplier = 1f;
        string attackLabel = "通常斬り";
        string attackActionId = ActionIds.BasicSlash;

        if (skillSlotManager != null &&
            skillSlotManager.TryGetActiveActionByCategory(SkillCategory.Attack, out ActionData skillAttack))
        {
            attackActionId = skillAttack.actionID;
            attackLabel = skillAttack.actionName;

            if (skillAttack.isDerived)
            {
                effectiveStaminaCost = skillAttack.staminaCost;
                effectiveAttackDuration = skillAttack.activeDetectionTime;
                effectiveDamageMultiplier = skillAttack.damageMultiplier;
            }
        }
        else if (playerController != null &&
            playerController.TryGetLatestDerivedAction(DerivedActionType.Attack, out DerivedActionData attackAction))
        {
            attackActionId = attackAction.actionID;
            effectiveStaminaCost = attackAction.staminaCost;
            effectiveAttackDuration = attackAction.activeDetectionTime;
            effectiveDamageMultiplier = attackAction.damageMultiplier;
            attackLabel = attackAction.actionName;
        }

        CombatStats equipmentStats = GetComponent<CombatStats>();
        if (equipmentStats == null)
        {
            equipmentStats = GetComponentInParent<CombatStats>();
        }

        if (equipmentStats != null)
        {
            effectiveStaminaCost *= equipmentStats.EquipmentStaminaCostMultiplier;
        }

        if (!stats.CanUseStamina(effectiveStaminaCost))
        {
            Debug.Log("攻撃できません。スタミナが足りません。");
            return;
        }

        stats.UseStamina(effectiveStaminaCost);

        StartCoroutine(AttackRoutine(
            effectiveAttackDuration, effectiveDamageMultiplier, attackLabel, attackActionId));
    }

    /// <summary>
    /// 攻撃開始 → 一定時間判定 ON → 判定 OFF の流れ。
    /// 派生攻撃技使用時は duration / damageMultiplier を上書きします。
    /// </summary>
    private IEnumerator AttackRoutine(
        float duration, float damageMultiplier, string attackLabel, string attackActionId)
    {
        isAttacking = true;

        // --- 攻撃間隔の計測（連打・ディレイ癖のプロファイリング）---
        // 実際に攻撃が始まったタイミングで、前回攻撃からの経過時間をロガーへ送る
        if (lastAttackStartTime >= 0f)
        {
            float interval = Time.time - lastAttackStartTime;
            PlayerActionLogger.Instance?.LogAttackInterval(interval);
        }

        lastAttackStartTime = Time.time;

        Debug.Log($"{attackLabel} を開始（倍率: x{damageMultiplier:F2}）");

        PlayerActionLogger.Instance?.LogAttack();

        weaponHitDetector.EnableAttackDetection(damageMultiplier, attackActionId);

        yield return new WaitForSeconds(duration);

        weaponHitDetector.DisableAttackDetection();

        Debug.Log($"{attackLabel} を終了");

        isAttacking = false;
    }
}

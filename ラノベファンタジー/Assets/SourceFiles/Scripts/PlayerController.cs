using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// プレイヤーのキー入力を検知し、攻撃・ステップ回避を実行するコントローラー。
/// PlayerStats のスタミナ消費と連動します。
/// ThirdPersonController 使用時は子オブジェクトの CharacterController で回避します。
/// </summary>
public class PlayerController : MonoBehaviour, IInvincibilitySource
{
    [Header("参照")]
    [Tooltip("HP・スタミナを管理する PlayerStats")]
    [SerializeField] private PlayerStats stats;

    [Tooltip("ステップ回避に使う CharacterController（未設定なら子オブジェクトから自動取得）")]
    [SerializeField] private CharacterController characterController;

    [Tooltip("CharacterController がない場合に使う Rigidbody")]
    [SerializeField] private Rigidbody rb;

    [Tooltip("裏ステータスを管理する HiddenStatusManager")]
    [SerializeField] private HiddenStatusManager hiddenStatusManager;

    [Tooltip("スキル・閃きシステム（未設定なら同オブジェクトから自動取得）")]
    [SerializeField] private PlayerSkillSlotManager skillSlotManager;

    [Header("スタミナ消費量")]
    [SerializeField] private float dodgeStaminaCost = 20f;

    [Header("入力キー")]
    [Tooltip("ステップ回避に使うキー（ThirdPersonController のジャンプは Space のまま）")]
    [SerializeField] private Key dodgeKey = Key.LeftCtrl;

    [Header("ステップ回避")]
    [Tooltip("回避中の移動速度")]
    [SerializeField] private float dodgeSpeed = 12f;

    /// <summary>AI 基礎スキル等による回避速度ボーナスを加算します。</summary>
    public void ApplyDodgeSpeedBonus(float bonus)
    {
        dodgeSpeed = Mathf.Max(1f, dodgeSpeed + bonus);
    }

    [Tooltip("回避モーションが続く時間（秒）")]
    [SerializeField] private float dodgeDuration = 0.25f;

    [Tooltip("連続回避を防ぐクールダウン（秒）")]
    [SerializeField] private float dodgeCooldown = 0.5f;

    [Header("ニアミス判定")]
    [Tooltip("基本ステップ後、この秒数以内の被弾をニアミスとして記録")]
    [SerializeField] private float nearMissWindow = 0.3f;

    [Header("派生アクション（AI 習得技）")]
    [Tooltip("ゲーム中に習得した派生技リスト。AI から UnlockNewAction で追加されます")]
    [SerializeField] private List<DerivedActionData> unlockedActions = new List<DerivedActionData>();

    // 実際に動くキャラクターの Transform（Robot 子オブジェクトなど）
    private Transform movementTransform;

    // 回避中かどうか
    private bool isDodging;

    // 無敵状態（派生回避技の invincibilityTime 用）
    private bool isInvincible;
    private Coroutine invincibilityRoutine;

    // 回避の残り時間
    private float dodgeTimer;

    // 回避クールダウンの残り時間
    private float dodgeCooldownTimer;

    // 回避で移動する方向（水平方向）
    private Vector3 dodgeDirection;

    // 無敵なし基本ステップの開始時刻。ニアミス判定に使用（-1 = 未記録）
    private float lastBasicDodgeTime = -1f;
    private bool nearMissLoggedForCurrentDodge;

    /// <summary>回避中かどうか。ThirdPersonController が通常移動を止めるために参照します。</summary>
    public bool IsDodging => isDodging;

    /// <summary>無敵状態かどうか。CombatStats の被弾判定が参照します。</summary>
    public bool IsInvincible => isInvincible;

    /// <summary>習得済み派生技リスト（読み取り専用ビュー）</summary>
    public IReadOnlyList<DerivedActionData> UnlockedActions => unlockedActions;

    /// <summary>
    /// AI から送られた派生技データをリアルタイムで習得リストに追加します。
    /// 同じ actionID は重複登録しません。
    /// </summary>
    public bool UnlockNewAction(DerivedActionData newData)
    {
        if (newData == null || !newData.IsValid())
        {
            Debug.LogWarning("[PlayerController] 無効な派生技データのため習得できません。");
            return false;
        }

        foreach (DerivedActionData existing in unlockedActions)
        {
            if (existing != null && existing.actionID == newData.actionID)
            {
                Debug.Log($"[PlayerController] 派生技は既に習得済みです: {newData.actionName}");
                return false;
            }
        }

        unlockedActions.Add(newData.Clone());
        Debug.Log($"[PlayerController] 新しい派生技を習得: {newData}（理由: {newData.unlockTriggerCondition}）");
        return true;
    }

    /// <summary>
    /// JSON 文字列から派生技を生成して習得します（AI 連携用のエントリポイント）。
    /// </summary>
    public bool UnlockNewActionFromJson(string json)
    {
        DerivedActionData data = DerivedActionData.FromJson(json);
        return data != null && UnlockNewAction(data);
    }

    /// <summary>
    /// 指定種別の派生技のうち、最後に習得したものを取得します。
    /// 未習得なら false を返し、呼び出し側は通常アクションのパラメーターを使います。
    /// </summary>
    public bool TryGetLatestDerivedAction(DerivedActionType actionType, out DerivedActionData derivedAction)
    {
        for (int i = unlockedActions.Count - 1; i >= 0; i--)
        {
            DerivedActionData candidate = unlockedActions[i];
            if (candidate != null && candidate.actionType == actionType)
            {
                derivedAction = candidate;
                return true;
            }
        }

        derivedAction = null;
        return false;
    }

    /// <summary>
    /// 敵の攻撃がプレイヤーに当たったときに CombatStats から呼ばれます。
    /// 基本ステップ（無敵なし）直後の被弾なら「ニアミス」として記録します。
    /// </summary>
    public void NotifyEnemyHitDuringNearMissWindow()
    {
        // 無敵付き回避中の被弾はニアミス対象外
        if (isInvincible)
        {
            return;
        }

        // 基本ステップをまだ踏んでいない、または判定窓を過ぎている
        if (lastBasicDodgeTime < 0f)
        {
            return;
        }

        float elapsed = Time.time - lastBasicDodgeTime;
        if (elapsed > nearMissWindow)
        {
            return;
        }

        // 1 回のステップにつき 1 回だけカウント（連続被弾で水増ししない）
        if (nearMissLoggedForCurrentDodge)
        {
            return;
        }

        nearMissLoggedForCurrentDodge = true;
        PlayerActionLogger.Instance?.LogNearMiss();
        GamePhaseEventBridge.Instance?.UpdateMetricFromGameplay(GameplayActionMetricTypes.JustEvasion, 1);
    }

    private void Reset()
    {
        stats = GetComponent<PlayerStats>();
        if (stats == null)
        {
            stats = GetComponentInParent<PlayerStats>();
        }

        characterController = GetComponentInChildren<CharacterController>();
        rb = GetComponent<Rigidbody>();
        if (rb == null)
        {
            rb = GetComponentInChildren<Rigidbody>();
        }

        movementTransform = characterController != null
            ? characterController.transform
            : transform;

        hiddenStatusManager = GetComponent<HiddenStatusManager>();
        if (hiddenStatusManager == null)
        {
            hiddenStatusManager = GetComponentInParent<HiddenStatusManager>();
        }

        skillSlotManager = GetComponent<PlayerSkillSlotManager>();
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

        if (characterController == null)
        {
            characterController = GetComponentInChildren<CharacterController>();
        }

        if (rb == null)
        {
            rb = GetComponent<Rigidbody>();
        }

        if (rb == null)
        {
            rb = GetComponentInChildren<Rigidbody>();
        }

        movementTransform = characterController != null
            ? characterController.transform
            : transform;

        if (hiddenStatusManager == null)
        {
            hiddenStatusManager = GetComponent<HiddenStatusManager>();
        }

        if (hiddenStatusManager == null)
        {
            hiddenStatusManager = GetComponentInParent<HiddenStatusManager>();
        }

        if (skillSlotManager == null)
        {
            skillSlotManager = GetComponent<PlayerSkillSlotManager>();
        }

        if (skillSlotManager == null)
        {
            skillSlotManager = GetComponentInParent<PlayerSkillSlotManager>();
        }

        if (stats == null)
        {
            Debug.LogWarning("PlayerStats が見つかりません。PlayerRobot に PlayerStats をアタッチしてください。");
        }

        if (characterController == null && rb == null)
        {
            Debug.LogWarning("CharacterController も Rigidbody も見つかりません。回避移動が動きません。");
        }
    }

    private void Update()
    {
        // 攻撃は PlayerAttackController が担当
        HandleDodgeInput();
        UpdateDodgeTimers();

        // CharacterController の移動は Update で行う
        ApplyDodgeMovementCharacterController();
    }

    private void FixedUpdate()
    {
        // Rigidbody の移動は FixedUpdate で行う
        ApplyDodgeMovementRigidbody();
    }

    private void HandleDodgeInput()
    {
        if (ShouldDeferDodgeToDemoTimeline())
        {
            return;
        }

        if (!WasDodgeKeyPressedThisFrame())
        {
            return;
        }

        CombatActionFeedbackManager combatFeedback = CombatActionFeedbackManager.Instance;
        if (combatFeedback != null)
        {
            combatFeedback.AttemptStepEvade();
            return;
        }

        if (stats == null || !stats.IsAlive)
        {
            return;
        }

        if (characterController == null && rb == null)
        {
            return;
        }

        if (isDodging || dodgeCooldownTimer > 0f)
        {
            return;
        }

        InventoryManager inventory = InventoryManager.Instance ?? InventoryManager.EnsureInstance();
        if (inventory != null && inventory.IsRollingDodgeLocked)
        {
            Debug.Log(
                "<b><color=#FF3333>【重量超過警告】装備や素材が重すぎてステップ回避が実行できない！</color></b>");
            return;
        }

        // --- 派生回避技の適用（器）---
        // 優先: PlayerSkillSlotManager（閃きシステム）→ 旧 DerivedAction リスト → 通常ステップ
        float effectiveStaminaCost = dodgeStaminaCost;
        float effectiveDodgeDuration = dodgeDuration;
        float effectiveInvincibilityTime = 0f;
        string dodgeLabel = "ステップ回避";

        if (skillSlotManager != null &&
            skillSlotManager.TryGetActiveActionByCategory(SkillCategory.Evade, out ActionData skillEvade) &&
            skillEvade.isDerived)
        {
            effectiveStaminaCost = skillEvade.staminaCost;
            effectiveDodgeDuration = skillEvade.activeDetectionTime;
            effectiveInvincibilityTime = skillEvade.invincibilityTime;
            dodgeLabel = skillEvade.actionName;
        }
        else if (TryGetLatestDerivedAction(DerivedActionType.Evade, out DerivedActionData evadeAction))
        {
            effectiveStaminaCost = evadeAction.staminaCost;
            effectiveDodgeDuration = evadeAction.activeDetectionTime;
            effectiveInvincibilityTime = evadeAction.invincibilityTime;
            dodgeLabel = evadeAction.actionName;
        }

        if (!stats.CanUseStamina(effectiveStaminaCost))
        {
            Debug.Log("回避できません。スタミナが足りません。");
            return;
        }

        stats.UseStamina(effectiveStaminaCost);
        dodgeDirection = GetDodgeDirection();

        isDodging = true;
        dodgeTimer = effectiveDodgeDuration;
        dodgeCooldownTimer = dodgeCooldown;

        if (effectiveInvincibilityTime > 0f)
        {
            StartInvincibility(effectiveInvincibilityTime);
            // 無敵付き派生回避はニアミス計測の対象外
            lastBasicDodgeTime = -1f;
            nearMissLoggedForCurrentDodge = false;
        }
        else
        {
            // 無敵なしの基本ステップ：この時刻から nearMissWindow 秒がジャスト回避の試み判定窓
            lastBasicDodgeTime = Time.time;
            nearMissLoggedForCurrentDodge = false;
        }

        Debug.Log($"{dodgeLabel} を実行（無敵: {effectiveInvincibilityTime:F2}秒）");

        // 行動ログ：回避成功回数
        PlayerActionLogger.Instance?.LogDodge();

        // 回避成功時：裏ステータスの仮データを 1 加算
        hiddenStatusManager?.AddDummyValue(1);
    }

    /// <summary>DemoTimeLineManager が BattlePhase で回避入力を中央処理する場合は true。</summary>
    private static bool ShouldDeferDodgeToDemoTimeline()
    {
        DemoTimeLineManager timeline = DemoTimeLineManager.Instance;
        return timeline != null && timeline.CurrentState == DemoState.BattlePhase;
    }

    /// <summary>ステップ回避キーがこのフレームで押されたか（新 Input System とレガシー Input の両対応）。</summary>
    private bool WasDodgeKeyPressedThisFrame()
    {
#if ENABLE_LEGACY_INPUT_MANAGER
        if (dodgeKey == Key.LeftCtrl && Input.GetKeyDown(KeyCode.LeftControl))
        {
            return true;
        }

        if (dodgeKey == Key.RightCtrl && Input.GetKeyDown(KeyCode.RightControl))
        {
            return true;
        }

        if (dodgeKey == Key.LeftCtrl && Input.GetKeyDown(KeyCode.RightControl))
        {
            return true;
        }
#endif

        if (Keyboard.current == null)
        {
            return false;
        }

        if (Keyboard.current[dodgeKey].wasPressedThisFrame)
        {
            return true;
        }

        return dodgeKey == Key.LeftCtrl && Keyboard.current[Key.RightCtrl].wasPressedThisFrame;
    }

    /// <summary>派生回避技などで無敵時間を付与します。</summary>
    private void StartInvincibility(float duration)
    {
        if (invincibilityRoutine != null)
        {
            StopCoroutine(invincibilityRoutine);
        }

        invincibilityRoutine = StartCoroutine(InvincibilityRoutine(duration));
    }

    private IEnumerator InvincibilityRoutine(float duration)
    {
        isInvincible = true;
        yield return new WaitForSeconds(duration);
        isInvincible = false;
        invincibilityRoutine = null;
    }

    private Vector3 GetDodgeDirection()
    {
        // ThirdPersonController と同じ入力・カメラ基準で方向を決める
        Vector2 moveInput = GetCurrentMoveInput();
        Transform t = movementTransform != null ? movementTransform : transform;

        if (moveInput.sqrMagnitude > 0.01f)
        {
            Camera mainCamera = Camera.main;
            if (mainCamera != null)
            {
                // 移動時と同じく、カメラの向きを基準に入力方向をワールド座標へ変換
                Vector3 inputDirection = new Vector3(moveInput.x, 0f, moveInput.y).normalized;
                float targetRotationY = Mathf.Atan2(inputDirection.x, inputDirection.z) * Mathf.Rad2Deg
                    + mainCamera.transform.eulerAngles.y;

                Vector3 direction = Quaternion.Euler(0f, targetRotationY, 0f) * Vector3.forward;
                direction.y = 0f;
                return direction.normalized;
            }

            // カメラが見つからない場合の予備処理（キャラ基準）
            Vector3 fallbackDirection = t.right * moveInput.x + t.forward * moveInput.y;
            fallbackDirection.y = 0f;
            return fallbackDirection.normalized;
        }

        // 入力がなければ正面へ回避
        Vector3 forwardDirection = t.forward;
        forwardDirection.y = 0f;
        return forwardDirection.normalized;
    }

    /// <summary>
    /// 現在の移動入力を取得。StarterAssetsInputs があればそれを優先する。
    /// </summary>
    private Vector2 GetCurrentMoveInput()
    {
        StarterAssets.StarterAssetsInputs input = GetComponentInChildren<StarterAssets.StarterAssetsInputs>();
        if (input != null && input.move.sqrMagnitude > 0.01f)
        {
            return input.move;
        }

        Vector2 keyboardInput = Vector2.zero;
        if (Keyboard.current != null)
        {
            if (Keyboard.current.wKey.isPressed) keyboardInput.y += 1f;
            if (Keyboard.current.sKey.isPressed) keyboardInput.y -= 1f;
            if (Keyboard.current.aKey.isPressed) keyboardInput.x -= 1f;
            if (Keyboard.current.dKey.isPressed) keyboardInput.x += 1f;
        }

        return keyboardInput;
    }

    private void UpdateDodgeTimers()
    {
        if (isDodging)
        {
            dodgeTimer -= Time.deltaTime;
            if (dodgeTimer <= 0f)
            {
                isDodging = false;
            }
        }

        if (dodgeCooldownTimer > 0f)
        {
            dodgeCooldownTimer -= Time.deltaTime;
        }
    }

    /// <summary>
    /// CharacterController でステップ回避（PlayerRobot + ThirdPersonController 向け）
    /// </summary>
    private void ApplyDodgeMovementCharacterController()
    {
        if (!isDodging || characterController == null || !characterController.enabled)
        {
            return;
        }

        CombatActionFeedbackManager combatFeedback = CombatActionFeedbackManager.Instance;
        if (combatFeedback != null && combatFeedback.IsCombatStepSliding)
        {
            return;
        }

        if (combatFeedback != null && combatFeedback.IsParryShieldActive)
        {
            return;
        }

        Vector3 dodgeMove = dodgeDirection * dodgeSpeed * Time.deltaTime;
        characterController.Move(dodgeMove);
        PlayerActionLogger.Instance?.AddMoveDistance(dodgeMove.magnitude);
    }

    /// <summary>
    /// Rigidbody でステップ回避（テスト用 Capsule など CharacterController なし向け）
    /// </summary>
    private void ApplyDodgeMovementRigidbody()
    {
        if (!isDodging || characterController != null || rb == null)
        {
            return;
        }

        Vector3 velocity = dodgeDirection * dodgeSpeed;
        velocity.y = rb.linearVelocity.y;
        rb.linearVelocity = velocity;

        PlayerActionLogger.Instance?.AddMoveDistance(dodgeSpeed * Time.fixedDeltaTime);
    }
}

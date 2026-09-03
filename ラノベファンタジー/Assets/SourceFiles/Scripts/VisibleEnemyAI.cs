using System.Collections;
using UnityEngine;
using UnityEngine.AI;

// =============================================================================
// トランスフォーム駆動型・視覚的エネミーAI（追跡 / 溜め / 振り下ろし / 復帰）
// 連携: CombatActionFeedbackManager / EnemyAttackProfile
// =============================================================================

/// <summary>視覚的エネミーAIの行動ステート。</summary>
public enum VisibleEnemyState
{
    /// <summary>攻撃圏内での待機。フワフワ浮遊。</summary>
    Idle,

    /// <summary>プレイヤーへ接近中。</summary>
    Chasing,

    /// <summary>溜め・ディレイ。後方へ傾けて固定。</summary>
    Anticipation,

    /// <summary>振り下ろし。判定ウィンドウと同期。</summary>
    AttackActive,

    /// <summary>攻撃後の復帰。</summary>
    Recovery
}

/// <summary>
/// 追跡・3D トランスフォーム演出で溜め・振り下ろしを肉眼視認可能にする敵制御 AI。
/// 敵 GameObject に直接アタッチして使用します。
/// </summary>
public class VisibleEnemyAI : MonoBehaviour
{
    [Header("追跡")]
    [SerializeField] private bool enableChase = true;
    [SerializeField] private float chaseSpeed = 3.5f;
    [SerializeField] private float attackRange = 2.2f;
    [SerializeField] private float attackCooldown = 2f;
    [SerializeField] private bool autoAttackWhenInRange = true;
    [SerializeField] private bool preferNavMeshAgent = true;

    [Header("Idle 浮遊")]
    [SerializeField] private float idleBobAmplitude = 0.08f;
    [SerializeField] private float idleBobFrequency = 2.2f;

    [Header("Anticipation 溜め")]
    [SerializeField] private float anticipationTiltDegrees = 30f;
    [SerializeField] private Vector3 anticipationTiltAxis = Vector3.right;

    [Header("AttackActive 振り下ろし")]
    [SerializeField] private float strikeTiltDegrees = -45f;
    [SerializeField] private Vector3 strikeTiltAxis = Vector3.right;
    [SerializeField] private float strikeLungeDistance = 1.15f;
    [SerializeField] private float strikeSnapPortion = 0.35f;

    [Header("Recovery 復帰")]
    [SerializeField] private float recoveryDuration = 0.4f;

    [Header("ターゲット")]
    [SerializeField] private Transform playerTarget;

    private Vector3 restWorldPosition;
    private Quaternion restWorldRotation;
    private Coroutine attackCoroutine;
    private CombatActionFeedbackManager combatFeedback;
    private NavMeshAgent navMeshAgent;
    private float lastAutoAttackTime = -999f;
    private Collider bodyCollider;
    private bool attackColliderPhasingActive;
    private bool bodyColliderWasTrigger;
    private bool isAiEnabled = true;
    private bool isDemoShutdown;

    /// <summary>AI の Update 駆動が有効か。</summary>
    public bool IsAiEnabled => isAiEnabled && !isDemoShutdown;

    /// <summary>デモ終了（ResultPhase）等で完全停止されたか。</summary>
    public bool IsDemoShutdown => isDemoShutdown;

    /// <summary>現在の AI ステート。</summary>
    public VisibleEnemyState CurrentState { get; private set; } = VisibleEnemyState.Idle;

    /// <summary>攻撃シーケンス進行中か。</summary>
    public bool IsAttackSequenceRunning { get; private set; }

    /// <summary>攻撃を開始する水平距離。</summary>
    public float AttackRange => attackRange;

    /// <summary>
    /// キューブプリミティブの底面が地面に接するようワールド Y を補正します。
    /// </summary>
    /// <param name="worldPosition">補正前のワールド座標（ピボット中心想定）</param>
    /// <param name="cubeScaleY">キューブの localScale.y</param>
    /// <param name="ignoreRoot">レイ判定から除外する Transform（プレイヤー等）</param>
    public static Vector3 AdjustSpawnPositionForCubeFeet(
        Vector3 worldPosition,
        float cubeScaleY,
        Transform ignoreRoot = null)
    {
        float halfHeight = Mathf.Max(0.01f, cubeScaleY) * 0.5f;
        Vector3 rayOrigin = worldPosition + Vector3.up * 8f;
        float groundY = float.NegativeInfinity;

        RaycastHit[] hits = Physics.RaycastAll(
            rayOrigin,
            Vector3.down,
            24f,
            ~0,
            QueryTriggerInteraction.Ignore);
        for (int i = 0; i < hits.Length; i++)
        {
            Collider hitCollider = hits[i].collider;
            if (hitCollider == null)
            {
                continue;
            }

            if (ignoreRoot != null &&
                (hitCollider.transform == ignoreRoot || hitCollider.transform.IsChildOf(ignoreRoot)))
            {
                continue;
            }

            if (hits[i].point.y > groundY)
            {
                groundY = hits[i].point.y;
            }
        }

        if (groundY > float.NegativeInfinity)
        {
            worldPosition.y = groundY + halfHeight;
        }
        else
        {
            worldPosition.y += halfHeight;
        }

        return worldPosition;
    }

    /// <summary>現在位置を地面へ接地し、NavMeshAgent を中心ピボット用に同期します。</summary>
    public void SnapFeetToGround()
    {
        float scaleY = transform.localScale.y > 0.01f ? transform.localScale.y : 1.2f;
        ResolvePlayerTarget();
        transform.position = AdjustSpawnPositionForCubeFeet(transform.position, scaleY, playerTarget);
        SyncNavMeshAgentToGroundedPivot();
        CacheRestTransform();
    }

    private void Awake()
    {
        bodyCollider = GetComponent<Collider>();
        CacheRestTransform();
        ResolvePlayerTarget();
        ConfigureNavMeshAgentIfNeeded();
    }

    private void OnEnable()
    {
        CombatActionFeedbackManager manager = CombatActionFeedbackManager.Instance;
        manager?.RegisterVisibleEnemy(this);
    }

    private void Update()
    {
        if (!IsAiEnabled)
        {
            return;
        }

        if (ShouldSkipUpdateForDemoState())
        {
            return;
        }

        if (IsAttackSequenceRunning)
        {
            return;
        }

        ResolvePlayerTarget();
        if (!enableChase || playerTarget == null)
        {
            if (CurrentState == VisibleEnemyState.Idle)
            {
                UpdateIdleBob();
            }

            return;
        }

        CombatActionFeedbackManager combat = combatFeedback ?? CombatActionFeedbackManager.Instance;
        if (combat != null && combat.IsEnemyDefeated)
        {
            StopChaseMovement();
            return;
        }

        float horizontalDistance = GetHorizontalDistanceToPlayer();
        if (horizontalDistance > attackRange)
        {
            CurrentState = VisibleEnemyState.Chasing;
            UpdateChaseMovement();
            return;
        }

        if (CurrentState == VisibleEnemyState.Chasing)
        {
            StopChaseMovement();
            CacheRestTransform();
        }

        CurrentState = VisibleEnemyState.Idle;
        UpdateIdleBob();
        TryAutoStartAttack();
    }

    /// <summary>ホーム姿勢を現在のワールド Transform で再キャプチャします。</summary>
    public void RecaptureHomeTransform()
    {
        CacheRestTransform();
    }

    /// <summary>プレイヤー方向へ水平に向きを合わせます。</summary>
    public void FacePlayer()
    {
        ResolvePlayerTarget();
        if (playerTarget == null)
        {
            return;
        }

        Vector3 toPlayer = playerTarget.position - transform.position;
        toPlayer.y = 0f;
        if (toPlayer.sqrMagnitude < 0.001f)
        {
            return;
        }

        transform.rotation = Quaternion.LookRotation(toPlayer.normalized, Vector3.up);
        CacheRestTransform();
    }

    /// <summary>追跡の有効/無効を切り替えます。</summary>
    /// <param name="chaseEnabled">true で追跡 ON</param>
    public void SetChaseEnabled(bool chaseEnabled)
    {
        enableChase = chaseEnabled;
        if (!enableChase)
        {
            StopChaseMovement();
        }
        else if (IsAiEnabled && navMeshAgent != null && !navMeshAgent.enabled)
        {
            navMeshAgent.enabled = true;
            ConfigureNavMeshAgentIfNeeded();
        }
    }

    /// <summary>
    /// 生産フェーズ入場時の再配置直後に呼び出します。猶予中は追跡・自動攻撃を抑止します。
    /// </summary>
    /// <param name="deferThreat">true のとき猶予中（棒立ち＋浮遊のみ）</param>
    public void PrepareForCraftingHarassmentEntry(bool deferThreat)
    {
        isDemoShutdown = false;
        isAiEnabled = true;
        autoAttackWhenInRange = !deferThreat;
        SetChaseEnabled(!deferThreat);
        CancelVisualAttack();
        ResolvePlayerTarget();
        ConfigureNavMeshAgentIfNeeded();
        FacePlayer();
    }

    /// <summary>猶予終了後、追跡のみ開始（攻撃は別途解禁まで抑止）。</summary>
    public void ActivateCraftingHarassmentChaseOnly()
    {
        if (!isAiEnabled || isDemoShutdown)
        {
            return;
        }

        isAiEnabled = true;
        autoAttackWhenInRange = false;
        SetChaseEnabled(true);
        ResolvePlayerTarget();
        FacePlayer();
        InGameVisualUIManager.EnsureInstance().PlayHarassmentThreatPulse(attackEnabled: false);
    }

    /// <summary>追加猶予終了後、射程内での自動攻撃を許可します。</summary>
    public void EnableCraftingHarassmentAutoAttack()
    {
        if (!isAiEnabled || isDemoShutdown)
        {
            return;
        }

        autoAttackWhenInRange = true;
        InGameVisualUIManager.EnsureInstance().PlayHarassmentThreatPulse(attackEnabled: true);
    }

    /// <summary>ResultPhase 着地時に AI・コルーチン・ナビを完全停止します。</summary>
    public void ShutdownForDemoResult()
    {
        isDemoShutdown = true;
        isAiEnabled = false;
        enableChase = false;
        autoAttackWhenInRange = false;

        StopAllCoroutines();
        attackCoroutine = null;
        IsAttackSequenceRunning = false;
        CurrentState = VisibleEnemyState.Idle;

        CancelVisualAttack();
        StopChaseMovement();

        if (navMeshAgent != null)
        {
            navMeshAgent.enabled = false;
        }
    }

    /// <summary>BattlePhase 復帰時に AI を再有効化します。</summary>
    public void ResumeForBattle()
    {
        isDemoShutdown = false;
        isAiEnabled = true;
        enableChase = true;
        autoAttackWhenInRange = true;
        lastAutoAttackTime = -999f;

        if (navMeshAgent != null)
        {
            navMeshAgent.enabled = true;
        }

        ConfigureNavMeshAgentIfNeeded();
        ResolvePlayerTarget();
    }

    /// <summary>DemoTimeLine の終了ステート中は索敵・移動をスキップします。</summary>
    private static bool ShouldSkipUpdateForDemoState()
    {
        DemoTimeLineManager timeline = DemoTimeLineManager.Instance;
        if (timeline == null)
        {
            return false;
        }

        return timeline.CurrentState == DemoState.ResultPhase ||
               timeline.CurrentState == DemoState.TransitionPhase;
    }

    /// <summary>
    /// プロファイルの乱数ディレイを用いて、視覚攻撃シーケンスを開始します。
    /// </summary>
    /// <param name="profile">敵攻撃プロファイル</param>
    public void StartVisualAttack(EnemyAttackProfile profile)
    {
        if (profile == null || !profile.IsValid())
        {
            Debug.LogWarning("[VisibleEnemyAI] 無効なプロファイルのため攻撃を開始できません。");
            return;
        }

        float rolledDelay = profile.RollRandomDelay();
        StartVisualAttack(profile, rolledDelay, profile.activeWindow);
    }

    /// <summary>
    /// 事前に算出したディレイ値で視覚攻撃を開始し、CombatActionFeedbackManager と同期します。
    /// </summary>
    public void StartVisualAttack(
        EnemyAttackProfile profile,
        float anticipationSeconds,
        float activeWindowSeconds,
        CombatActionFeedbackManager combat = null)
    {
        if (!IsAiEnabled || isDemoShutdown)
        {
            return;
        }

        if (profile == null || !profile.IsValid())
        {
            Debug.LogWarning("[VisibleEnemyAI] 無効なプロファイルのため攻撃を開始できません。");
            return;
        }

        combatFeedback = combat ?? CombatActionFeedbackManager.Instance;
        CancelVisualAttack();
        attackCoroutine = StartCoroutine(
            VisualAttackRoutine(profile, anticipationSeconds, activeWindowSeconds, comboAction: null));
    }

    /// <summary>
    /// コンボ 1 ヒット分の視覚攻撃（CombatActionFeedbackManager のコンボループから yield 呼び出し）。
    /// </summary>
    public IEnumerator RunComboActionVisualRoutine(
        EnemyAttackProfile profile,
        EnemyAttackActionData comboAction,
        float anticipationSeconds,
        float activeWindowSeconds,
        CombatActionFeedbackManager combat = null)
    {
        if (!IsAiEnabled || isDemoShutdown || profile == null || comboAction == null)
        {
            yield break;
        }

        combatFeedback = combat ?? CombatActionFeedbackManager.Instance;
        yield return VisualAttackRoutine(profile, anticipationSeconds, activeWindowSeconds, comboAction);
    }

    /// <summary>JSON 行動プロファイルに基づくコンボ攻撃を Combat 側ループへ委譲します。</summary>
    public void StartVisualComboAttack(
        EnemyAttackProfile profile,
        EnemyBehaviorProfile behaviorProfile,
        CombatActionFeedbackManager combat = null)
    {
        if (profile == null || behaviorProfile == null)
        {
            return;
        }

        CombatActionFeedbackManager resolvedCombat = combat ?? CombatActionFeedbackManager.Instance;
        resolvedCombat?.SetTargetEnemy(profile, profile.enemyMasterId, behaviorProfile);
        resolvedCombat?.TriggerEnemyAttack();
    }

    /// <summary>進行中の視覚攻撃を中断し Idle 姿勢へ戻します。</summary>
    public void CancelVisualAttack()
    {
        if (attackCoroutine != null)
        {
            StopCoroutine(attackCoroutine);
            attackCoroutine = null;
        }

        IsAttackSequenceRunning = false;
        CurrentState = VisibleEnemyState.Idle;
        RestoreAttackColliderPhasing();
        ApplyRestTransform();
        StopChaseMovement();
    }

    /// <summary>攻撃演出中は敵コライダーをトリガー化し、プレイヤーを物理押し出ししないようにします。</summary>
    private void EnableAttackColliderPhasing()
    {
        if (bodyCollider == null || attackColliderPhasingActive)
        {
            return;
        }

        bodyColliderWasTrigger = bodyCollider.isTrigger;
        bodyCollider.isTrigger = true;
        attackColliderPhasingActive = true;
    }

    /// <summary>攻撃演出後に敵コライダー設定を元へ戻します。</summary>
    private void RestoreAttackColliderPhasing()
    {
        if (bodyCollider == null || !attackColliderPhasingActive)
        {
            return;
        }

        bodyCollider.isTrigger = bodyColliderWasTrigger;
        attackColliderPhasingActive = false;
    }

    private void ConfigureNavMeshAgentIfNeeded()
    {
        if (!preferNavMeshAgent)
        {
            return;
        }

        navMeshAgent = GetComponent<NavMeshAgent>();
        if (navMeshAgent == null)
        {
            navMeshAgent = gameObject.AddComponent<NavMeshAgent>();
        }

        float agentHeight = Mathf.Max(0.01f, transform.localScale.y);
        float halfHeight = agentHeight * 0.5f;

        navMeshAgent.speed = chaseSpeed;
        navMeshAgent.stoppingDistance = Mathf.Max(0.5f, attackRange * 0.85f);
        navMeshAgent.angularSpeed = 540f;
        navMeshAgent.acceleration = 12f;
        navMeshAgent.height = agentHeight;
        navMeshAgent.radius = Mathf.Max(0.25f, transform.localScale.x * 0.5f);
        // キューブのピボットは中心のため、NavMesh 表面＝底面になるようオフセットする
        navMeshAgent.baseOffset = halfHeight;

        SyncNavMeshAgentToGroundedPivot();
    }

    /// <summary>中心ピボットのキューブが地面に埋まらないよう NavMesh へワープします。</summary>
    private void SyncNavMeshAgentToGroundedPivot()
    {
        if (navMeshAgent == null || !navMeshAgent.enabled)
        {
            return;
        }

        float halfHeight = Mathf.Max(0.01f, transform.localScale.y) * 0.5f;
        Vector3 samplePoint = transform.position;

        if (NavMesh.SamplePosition(samplePoint, out NavMeshHit hit, 4f, NavMesh.AllAreas))
        {
            Vector3 groundedPivot = hit.position + Vector3.up * halfHeight;
            navMeshAgent.Warp(groundedPivot);
            transform.position = groundedPivot;
        }
    }

    private void UpdateChaseMovement()
    {
        Transform chaseTarget = ResolveChaseTargetTransform();
        if (chaseTarget == null)
        {
            return;
        }

        Vector3 targetPosition = chaseTarget.position;
        FaceTowardWorldPosition(targetPosition);

        if (TryChaseWithNavMesh(targetPosition))
        {
            CacheRestTransform();
            return;
        }

        Vector3 toTarget = targetPosition - transform.position;
        toTarget.y = 0f;
        if (toTarget.sqrMagnitude < 0.0001f)
        {
            return;
        }

        float step = chaseSpeed * Time.deltaTime;
        transform.position += toTarget.normalized * Mathf.Min(step, toTarget.magnitude);
        CacheRestTransform();
    }

    private bool TryChaseWithNavMesh(Vector3 targetPosition)
    {
        if (!preferNavMeshAgent || navMeshAgent == null || !navMeshAgent.isOnNavMesh)
        {
            return false;
        }

        navMeshAgent.isStopped = false;
        navMeshAgent.speed = chaseSpeed;
        navMeshAgent.stoppingDistance = Mathf.Max(0.5f, attackRange * 0.85f);
        navMeshAgent.SetDestination(targetPosition);
        return true;
    }

    private void StopChaseMovement()
    {
        SafeStopNavMeshMovement();
    }

    /// <summary>NavMesh 上のアクティブなエージェントのみパスを停止します。</summary>
    private void SafeStopNavMeshMovement()
    {
        if (navMeshAgent == null || !navMeshAgent.isActiveAndEnabled || !navMeshAgent.isOnNavMesh)
        {
            return;
        }

        navMeshAgent.isStopped = true;
        navMeshAgent.ResetPath();
    }

    private void TryAutoStartAttack()
    {
        if (!autoAttackWhenInRange || IsAttackSequenceRunning || !IsAiEnabled)
        {
            return;
        }

        if (ShouldSkipUpdateForDemoState())
        {
            return;
        }

        DemoTimeLineManager timeline = DemoTimeLineManager.Instance;
        if (timeline != null &&
            timeline.CurrentState == DemoState.CraftingPhase &&
            !timeline.IsCraftingDamageInterruptAllowed)
        {
            return;
        }

        if (Time.time - lastAutoAttackTime < attackCooldown)
        {
            return;
        }

        CombatActionFeedbackManager combat = combatFeedback ?? CombatActionFeedbackManager.Instance;
        if (combat == null || combat.currentEnemyProfile == null || combat.IsEnemyDefeated)
        {
            return;
        }

        lastAutoAttackTime = Time.time;
        combat.TriggerEnemyAttack();
    }

    private void CacheRestTransform()
    {
        if (CurrentState == VisibleEnemyState.Idle)
        {
            float bobOffset = Mathf.Sin(Time.time * idleBobFrequency) * idleBobAmplitude;
            restWorldPosition = transform.position - Vector3.up * bobOffset;
        }
        else
        {
            restWorldPosition = transform.position;
        }

        restWorldRotation = transform.rotation;
    }

    private void ApplyRestTransform()
    {
        transform.position = restWorldPosition;
        transform.rotation = restWorldRotation;
    }

    private void UpdateIdleBob()
    {
        float bobOffset = Mathf.Sin(Time.time * idleBobFrequency) * idleBobAmplitude;
        transform.position = restWorldPosition + Vector3.up * bobOffset;
    }

    private IEnumerator VisualAttackRoutine(
        EnemyAttackProfile profile,
        float anticipationSeconds,
        float activeWindowSeconds,
        EnemyAttackActionData comboAction)
    {
        IsAttackSequenceRunning = true;
        ResolvePlayerTarget();
        StopChaseMovement();
        CacheRestTransform();

        string actionLabel = comboAction != null ? comboAction.actionName : profile.enemyName;

        try
        {
            // --- Anticipation（溜め） ---
            CurrentState = VisibleEnemyState.Anticipation;
            combatFeedback?.NotifyAnticipationStarted(profile, anticipationSeconds, actionLabel);

            Quaternion windUpRotation = restWorldRotation * Quaternion.AngleAxis(
                anticipationTiltDegrees,
                anticipationTiltAxis.normalized);
            transform.rotation = windUpRotation;
            transform.position = restWorldPosition;

            float anticipationElapsed = 0f;
            float clampedAnticipation = Mathf.Max(0f, anticipationSeconds);
            while (anticipationElapsed < clampedAnticipation)
            {
                anticipationElapsed += Time.deltaTime;
                transform.rotation = windUpRotation;
                transform.position = restWorldPosition;
                yield return null;
            }

            // --- AttackActive（振り下ろし + 判定ウィンドウ） ---
            CurrentState = VisibleEnemyState.AttackActive;
            EnableAttackColliderPhasing();
            combatFeedback?.NotifyAttackWindowOpened(activeWindowSeconds, actionLabel);

            Quaternion strikeRotation = restWorldRotation * Quaternion.AngleAxis(
                strikeTiltDegrees,
                strikeTiltAxis.normalized);
            Vector3 lungeDirectionWorld = ResolveLungeDirectionWorld();
            Vector3 strikeWorldPosition = restWorldPosition + lungeDirectionWorld * strikeLungeDistance;

            Quaternion strikeStartRotation = transform.rotation;
            Vector3 strikeStartPosition = transform.position;
            float clampedActiveWindow = Mathf.Max(0.01f, activeWindowSeconds);
            float snapDuration = Mathf.Max(0.02f, clampedActiveWindow * strikeSnapPortion);
            float attackElapsed = 0f;

            while (attackElapsed < clampedActiveWindow)
            {
                attackElapsed += Time.deltaTime;
                float snapT = Mathf.Clamp01(attackElapsed / snapDuration);
                float easedSnap = 1f - Mathf.Pow(1f - snapT, 3f);

                transform.rotation = Quaternion.Slerp(strikeStartRotation, strikeRotation, easedSnap);
                transform.position = Vector3.Lerp(strikeStartPosition, strikeWorldPosition, easedSnap);
                yield return null;
            }

            combatFeedback?.NotifyAttackWindowClosed();

            // --- Recovery（復帰） ---
            CurrentState = VisibleEnemyState.Recovery;
            Vector3 recoveryStartPosition = transform.position;
            Quaternion recoveryStartRotation = transform.rotation;
            float recoveryElapsed = 0f;
            float clampedRecovery = Mathf.Max(0.05f, recoveryDuration);

            while (recoveryElapsed < clampedRecovery)
            {
                recoveryElapsed += Time.deltaTime;
                float t = Mathf.Clamp01(recoveryElapsed / clampedRecovery);
                float eased = Mathf.SmoothStep(0f, 1f, t);
                transform.position = Vector3.Lerp(recoveryStartPosition, restWorldPosition, eased);
                transform.rotation = Quaternion.Slerp(recoveryStartRotation, restWorldRotation, eased);
                yield return null;
            }

            transform.position = restWorldPosition;
            transform.rotation = restWorldRotation;
            CurrentState = VisibleEnemyState.Idle;

            Debug.Log(
                $"<color=#90CAF9>[VisibleEnemyAI] {actionLabel} — 攻撃シーケンス完了、追跡/Idle へ復帰。</color>");
        }
        finally
        {
            RestoreAttackColliderPhasing();
            IsAttackSequenceRunning = false;
            attackCoroutine = null;
        }
    }

    private float GetHorizontalDistanceToPlayer()
    {
        Transform chaseTarget = ResolveChaseTargetTransform();
        if (chaseTarget == null)
        {
            return float.MaxValue;
        }

        return GetHorizontalDistance(transform.position, chaseTarget.position);
    }

    private Transform ResolveChaseTargetTransform()
    {
        ResolvePlayerTarget();
        if (playerTarget == null)
        {
            return null;
        }

        CharacterController characterController = playerTarget.GetComponentInChildren<CharacterController>();
        return characterController != null ? characterController.transform : playerTarget;
    }

    private void FaceTowardWorldPosition(Vector3 worldPosition)
    {
        Vector3 lookDirection = worldPosition - transform.position;
        lookDirection.y = 0f;
        if (lookDirection.sqrMagnitude < 0.001f)
        {
            return;
        }

        transform.rotation = Quaternion.Slerp(
            transform.rotation,
            Quaternion.LookRotation(lookDirection.normalized, Vector3.up),
            12f * Time.deltaTime);
    }

    private Vector3 ResolveLungeDirectionWorld()
    {
        Transform chaseTarget = ResolveChaseTargetTransform();
        if (chaseTarget == null)
        {
            return transform.forward;
        }

        Vector3 toPlayer = chaseTarget.position - transform.position;
        toPlayer.y = 0f;
        if (toPlayer.sqrMagnitude < 0.001f)
        {
            return transform.forward;
        }

        return toPlayer.normalized;
    }

    private void ResolvePlayerTarget()
    {
        if (playerTarget != null)
        {
            return;
        }

        GameObject playerObject = GameObject.Find("PlayerRobot");
        if (playerObject == null)
        {
            playerObject = GameObject.Find("PlayerRobot ");
        }

        if (playerObject != null)
        {
            playerTarget = playerObject.transform;
            return;
        }

        GameObject taggedPlayer = GameObject.FindGameObjectWithTag("Player");
        if (taggedPlayer != null)
        {
            playerTarget = taggedPlayer.transform;
            return;
        }

        PlayerController controller = Object.FindAnyObjectByType<PlayerController>();
        if (controller != null)
        {
            playerTarget = controller.transform;
        }
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
        Gizmos.color = new Color(1f, 0.35f, 0.35f, 0.85f);
        Gizmos.DrawWireSphere(transform.position, attackRange);
    }
#endif
}

/// <summary>Play 開始時にデバッグ用の視覚的エネミーをスポーンします。</summary>
public static class VisibleEnemyBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void InitializeVisibleEnemySystems()
    {
        VisibleEnemySpawnSettings settings = VisibleEnemySpawnSettings.EnsureOnHub();
        if (settings != null && !settings.EnableAutoSpawn)
        {
            Debug.Log(
                "<color=#90A4AE><b>[VisibleEnemyBootstrap]</b> EnableAutoSpawn=OFF のため、" +
                "視覚的エネミーの自動スポーンをスキップしました。</color>");
            return;
        }

        SpawnDefaultVisibleEnemy();
    }

    private static void SpawnDefaultVisibleEnemy()
    {
        if (Object.FindAnyObjectByType<VisibleEnemyAI>() != null)
        {
            return;
        }

        Transform playerTransform = ResolvePlayerTransform();
        Vector3 spawnPosition = playerTransform != null
            ? playerTransform.position + playerTransform.forward * 6f + playerTransform.right * 2f
            : new Vector3(6f, 0f, 6f);

        const float cubeScale = 1.2f;

        GameObject enemyObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
        enemyObject.name = "VisibleEnemy_Slime";
        enemyObject.transform.localScale = new Vector3(cubeScale, cubeScale, cubeScale);
        enemyObject.transform.position = spawnPosition;

        Renderer renderer = enemyObject.GetComponent<Renderer>();
        if (renderer != null)
        {
            RuntimeUrpMaterialUtility.ApplyOpaqueColor(renderer, new Color(0.35f, 0.85f, 0.45f, 1f));
        }

        VisibleEnemyAI visibleEnemy = enemyObject.AddComponent<VisibleEnemyAI>();
        visibleEnemy.SnapFeetToGround();
        visibleEnemy.FacePlayer();

        CombatActionFeedbackManager combat = CombatActionFeedbackManager.EnsureInstance();
        combat.RegisterVisibleEnemy(visibleEnemy);
        combat.SetTargetEnemy(EnemyAttackProfile.CreateForestSlimeMob());

        Debug.Log(
            "<color=#A5D6A7><b>[VisibleEnemyBootstrap]</b> 視覚的エネミー（Cube）をスポーンしました。" +
            " 追跡→接近→溜め→振り下ろしが目視できます（T キーでも手動攻撃可）。</color>");
    }

    private static Transform ResolvePlayerTransform()
    {
        GameObject playerObject = GameObject.Find("PlayerRobot");
        if (playerObject == null)
        {
            playerObject = GameObject.Find("PlayerRobot ");
        }

        if (playerObject != null)
        {
            return playerObject.transform;
        }

        PlayerController controller = Object.FindAnyObjectByType<PlayerController>();
        return controller != null ? controller.transform : null;
    }
}

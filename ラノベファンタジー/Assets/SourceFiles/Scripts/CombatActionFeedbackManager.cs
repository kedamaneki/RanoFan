using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

// =============================================================================
// フロム式・完全ノーヒント戦闘フィードバック（敵プロファイル / 乱数ディレイ / 体勢値）
// 連携: InventoryManager / GamePhaseEventBridge / CraftingExperimentHub
// =============================================================================

/// <summary>敵ごとの攻撃ディレイ・体勢値（体幹）・ジャスト窓を定義するプロファイル。</summary>
[Serializable]
public class EnemyAttackProfile
{
    /// <summary>敵の表示名（例: 大森林の王グリフォン）。</summary>
    public string enemyName = "未設定の敵";

    /// <summary>EnemyMasterData.id（ドロップ処理用。未設定時は表示名から逆引き）。</summary>
    public string enemyMasterId;

    /// <summary>攻撃溜めディレイの最小値（秒）。</summary>
    public float minDelay = 0.6f;

    /// <summary>攻撃溜めディレイの最大値（秒）。</summary>
    public float maxDelay = 1.8f;

    /// <summary>攻撃判定が発生する極小ウィンドウ（秒）。</summary>
    public float activeWindow = 0.12f;

    /// <summary>その敵の最大体勢値（体幹上限）。</summary>
    public float maxPosture = 100f;

    /// <summary>プレイヤーへ与える攻撃力（STR）。</summary>
    public int attackStrength = 10;

    /// <summary>敵の最大 HP。</summary>
    public int maxHp = 60;

    /// <summary>プレイヤー攻撃を受ける際の防御力（DEF）。</summary>
    public int defense = 1;

    /// <summary>斬撃ヒット時に加算する体勢ダメージ。</summary>
    public float postureDamageSlash = 14f;

    /// <summary>突刺ヒット時に加算する体勢ダメージ。</summary>
    public float postureDamageThrust = 18f;

    /// <summary>打撃ヒット時に加算する体勢ダメージ。</summary>
    public float postureDamageStrike = 22f;

    /// <summary>ジャストパリィとして受け付ける極小ウィンドウ（秒）。</summary>
    public float parryJustWindow = CombatActionFeedbackManager.DefaultJustParryWindowSeconds;

    /// <summary>プロファイルが戦闘に使用可能か。</summary>
    public bool IsValid()
    {
        return !string.IsNullOrWhiteSpace(enemyName) &&
               maxDelay >= minDelay &&
               activeWindow > 0f &&
               maxPosture > 0f &&
               maxHp > 0 &&
               attackStrength > 0 &&
               parryJustWindow > 0f;
    }

    /// <summary>雑魚敵「森の害獣スライム」のプロファイルを生成します。</summary>
    public static EnemyAttackProfile CreateForestSlimeMob()
    {
        return new EnemyAttackProfile
        {
            enemyName = "森の害獣スライム",
            minDelay = 0.4f,
            maxDelay = 0.7f,
            activeWindow = 0.15f,
            maxPosture = 40f,
            attackStrength = 8,
            maxHp = 40,
            defense = 1,
            postureDamageSlash = 12f,
            postureDamageThrust = 16f,
            postureDamageStrike = 20f,
            parryJustWindow = 0.08f
        };
    }

    /// <summary>最凶ボス「大森林の王グリフォン」のプロファイルを生成します。</summary>
    public static EnemyAttackProfile CreateForestGriffinBoss()
    {
        return new EnemyAttackProfile
        {
            enemyName = "大森林の王グリフォン",
            minDelay = 0.7f,
            maxDelay = 2.3f,
            activeWindow = 0.08f,
            maxPosture = 120f,
            attackStrength = 18,
            maxHp = 200,
            defense = 5,
            postureDamageSlash = 10f,
            postureDamageThrust = 14f,
            postureDamageStrike = 18f,
            parryJustWindow = 0.08f
        };
    }

    /// <summary>ディレイ乱数をこのプロファイル設定から算出します。</summary>
    public float RollRandomDelay()
    {
        return UnityEngine.Random.Range(minDelay, maxDelay);
    }
}

/// <summary>
/// 敵プロファイルに基づくジャストパリィ・高速ステップ回避、
/// PlayerRobot のトランスフォーム駆動攻撃モーション、
/// プレイヤースタミナ / 敵体勢値（体幹）を一元管理します。
/// </summary>
public class CombatActionFeedbackManager : MonoBehaviour
{
    public const float DefaultJustParryWindowSeconds = 0.08f;
    public const float DefaultHitstopSeconds = 0.15f;
    public const float DefaultStepInvincibilitySeconds = 0.22f;

    /// <summary>斬撃攻撃タイプ文字列。</summary>
    public const string PlayerAttackTypeSlash = "Slash";

    /// <summary>突刺攻撃タイプ文字列。</summary>
    public const string PlayerAttackTypeThrust = "Thrust";

    /// <summary>打撃攻撃タイプ文字列。</summary>
    public const string PlayerAttackTypeStrike = "Strike";

    /// <summary>ホイール切替で巡回する攻撃属性の順序。</summary>
    private static readonly string[] PlayerAttackTypeCycleOrder =
    {
        PlayerAttackTypeSlash,
        PlayerAttackTypeThrust,
        PlayerAttackTypeStrike
    };

    public static CombatActionFeedbackManager Instance { get; private set; }

    [Header("スタミナ")]
    [Tooltip("未設定時は PlayerRobot の PlayerStats を自動解決します")]
    [SerializeField] private PlayerStats playerStats;

    [Header("デフォルト敵（未セット時）")]
    [SerializeField] private EnemyAttackProfile fallbackEnemyProfile = EnemyAttackProfile.CreateForestSlimeMob();

    [Header("判定パラメータ")]
    [SerializeField] private float stepInvincibilitySeconds = DefaultStepInvincibilitySeconds;
    [SerializeField] private float enemyPostureRecoverPerSecond = 10f;
    [SerializeField] private float parryStaminaCost = 15f;
    [SerializeField] private float parryJustRefund = 10f;
    [SerializeField] private float stepStaminaCost = 20f;
    [SerializeField] private float justParryPostureGain = 25f;

    [Header("ジャスト演出（ヒットストップ / カメラ）")]
    [SerializeField] private float justParryHitstopSeconds = 0.15f;
    [SerializeField] private float justParryShakeMagnitude = 0.85f;
    [SerializeField] private float justStepHitstopSeconds = 0.12f;
    [SerializeField] private float justStepShakeMagnitude = 0.55f;
    [SerializeField] private float cameraZoomFovDelta = 12f;

    [Header("視覚的エネミー")]
    [SerializeField] private VisibleEnemyAI registeredVisibleEnemy;

    [Header("プレイヤー物理演出")]
    [SerializeField] private Transform playerTransform;
    [SerializeField] private Transform playerMovementTransform;
    [SerializeField] private float stepSlideDuration = 0.15f;
    [SerializeField] private float stepSlideDistance = 1.35f;
    [SerializeField] private float parryShieldDuration = 0.12f;
    [SerializeField] private Vector3 parryShieldLocalOffset = new Vector3(0f, 0.55f, 0.75f);
    [SerializeField] private Vector3 parryShieldLocalScale = new Vector3(1.2f, 1f, 0.08f);

    [Header("PlayerRobot 攻撃モーション（Transform 直駆動）")]
    [SerializeField] private Transform playerRobotVisualTransform;
    [SerializeField] private float playerAttackStaminaCost = 15f;
    [SerializeField] private float slashSwingSeconds = 0.1f;
    [SerializeField] private float slashRecoverySeconds = 0.12f;
    [SerializeField] private float slashForwardSlide = 0.42f;
    [SerializeField] private float thrustStretchScaleZ = 1.9f;
    [SerializeField] private float thrustHoldSeconds = 0.05f;
    [SerializeField] private float thrustRecoverySeconds = 0.08f;
    [SerializeField] private float strikeLiftHeight = 0.28f;
    [SerializeField] private float strikeLiftSeconds = 0.07f;
    [SerializeField] private float strikeSlamSeconds = 0.11f;
    [SerializeField] private float strikeRecoverySeconds = 0.14f;
    [SerializeField] private float strikeForwardDrop = 0.22f;
    [SerializeField] private float playerAttackHitRange = 5f;

    [Header("インスタント・クラフト（即席武器）")]
    [SerializeField] private float instantCraftSnapDuration = 0.09f;
    [SerializeField] private float instantCraftSnapScaleTighten = 0.86f;
    [SerializeField] private float instantCraftSnapScaleStretchY = 1.14f;
    [SerializeField] private float instantCraftSnapTwistDegrees = 7f;

    /// <summary>現在対峙している敵の蓄積体勢値（体幹）。</summary>
    public float currentEnemyPosture { get; private set; }

    /// <summary>現在対峙している敵の残り HP。</summary>
    public int currentEnemyHp { get; private set; }

    /// <summary>現在アクティブな敵の攻撃プロファイル。</summary>
    public EnemyAttackProfile currentEnemyProfile { get; private set; }

    /// <summary>JSON 定義の複数技・コンボ行動プロファイル（設定時のみコンボループを使用）。</summary>
    public EnemyBehaviorProfile currentBehaviorProfile { get; private set; }

    /// <summary>現在アクティブな敵の EnemyMasterData.id（ドロップ配布用）。</summary>
    public string CurrentEnemyMasterId { get; private set; }

    /// <summary>コンボ実行中にヒット判定へ渡している技データ。</summary>
    private EnemyAttackActionData currentComboAction;

    /// <summary>敵の攻撃判定が現在アクティブな残り時間（秒）。</summary>
    private float attackActiveTimer;

    /// <summary>今回の攻撃ウィンドウが開いてからの経過時間（秒）。</summary>
    private float attackWindowElapsed;

    private bool defensiveActionResolvedThisWindow;
    private Coroutine enemyAttackCoroutine;
    private Coroutine hitstopCoroutine;
    private Coroutine cameraShakeCoroutine;
    private Coroutine stepInvincibilityCoroutine;
    private Coroutine stepSlideCoroutine;
    private Coroutine parryShieldCoroutine;
    private Coroutine parryShieldBumpCoroutine;
    private Coroutine playerAttackMotionCoroutine;
    private int playerAttackMotionSerial;
    private Coroutine damageBurstFlashCoroutine;
    private Coroutine instantCraftSnapCoroutine;
    private float stepInvincibilityTimer;

    /// <summary>ホイールで選択中の攻撃属性インデックス（0=Slash, 1=Thrust, 2=Strike）。</summary>
    private int selectedAttackTypeIndex;

    /// <summary>現在ホイールで選択されている攻撃属性。</summary>
    public string CurrentSelectedAttackType =>
        PlayerAttackTypeCycleOrder[
            Mathf.Clamp(selectedAttackTypeIndex, 0, PlayerAttackTypeCycleOrder.Length - 1)];

    private Vector3 playerRobotRestLocalPosition;
    private Quaternion playerRobotRestLocalRotation;
    private Vector3 playerRobotRestLocalScale;
    private bool playerRobotRestPoseCached;

    private GameObject parryShieldObject;
    private CharacterController playerCharacterController;
    private bool parryMissLoggedThisInput;

    /// <summary>パリィ防壁展開中にプレイヤー位置を固定するためのワールド座標。</summary>
    private Vector3 parryPositionLockWorld;

    /// <summary>パリィ中のプレイヤー位置ロックが有効か。</summary>
    private bool parryPositionLockActive;

    /// <summary>敵攻撃ウィンドウ中にプレイヤー位置を固定するためのワールド座標。</summary>
    private Vector3 enemyAttackAnchorWorld;

    /// <summary>敵攻撃ウィンドウ中のプレイヤー位置ロックが有効か。</summary>
    private bool enemyAttackAnchorActive;

    /// <summary>ヒットストップ前に保存した timeScale（多重発動時の復元用）。</summary>
    private float hitstopRestoreTimeScale = 1f;

    /// <summary>同時ヒットストップのネスト数。</summary>
    private int activeHitstopCount;

    /// <summary>カメラシェイク対象の元ローカル座標。</summary>
    private Vector3 cameraShakeOriginalLocalPosition;

    /// <summary>カメラシェイクを適用する Transform（Cinemachine ターゲット優先）。</summary>
    private Transform cameraShakeTransform;

    /// <summary>カメラシェイク対象の元 FOV。</summary>
    private float cameraShakeOriginalFov;

    /// <summary>現在シェイク中のカメラ参照。</summary>
    private Camera cameraShakeTarget;

    /// <summary>敵の体勢が崩れ、撃破（デモ上の戦闘クリア）済みか。</summary>
    public bool IsEnemyDefeated { get; private set; }

    /// <summary>ResultPhase 着地後、戦闘タイマー・敵 AI を凍結したか。</summary>
    public bool IsDemoCombatFrozen { get; private set; }

    /// <summary>敵の体勢が崩壊した瞬間に発火します（DemoTimeLineManager 等が購読）。</summary>
    public event Action<EnemyAttackProfile> OnEnemyPostureBroken;

    /// <summary>敵の刃が迫っている（攻撃判定 ON）か。</summary>
    public bool IsEnemyAttackActive => attackActiveTimer > 0f;

    /// <summary>ステップ回避の無敵時間が残っているか。</summary>
    public bool IsStepInvincible => stepInvincibilityTimer > 0f;

    /// <summary>戦闘ステップの高速スライド演出が進行中か。</summary>
    public bool IsCombatStepSliding { get; private set; }

    /// <summary>パリィ防壁（F）の展開中か。</summary>
    public bool IsParryShieldActive { get; private set; }

    /// <summary>こだわり工程（2〜0 / - キー）による詳細クラフト中か。</summary>
    public bool IsCrafting { get; private set; }

    /// <summary>ヒットストップ（空間フリーズ）が現在アクティブか。</summary>
    public bool IsHitstopActive => activeHitstopCount > 0;

    /// <summary>現在スタミナ（PlayerStats 委譲・UI 同期用）。</summary>
    public float currentStamina
    {
        get
        {
            PlayerStats stats = ResolvePlayerStats();
            return stats != null ? stats.CurrentStamina : 0f;
        }
    }

    /// <summary>最大スタミナ（PlayerStats 委譲）。</summary>
    public float maxStamina
    {
        get
        {
            PlayerStats stats = ResolvePlayerStats();
            return stats != null ? stats.maxStamina : 100f;
        }
    }

    /// <summary>PlayerRobot の攻撃モーション演出が進行中か。</summary>
    public bool IsPlayerAttackMotionActive { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[CombatActionFeedbackManager] 重複インスタンスを検出しました。");
            return;
        }

        Instance = this;

        if (currentEnemyProfile == null)
        {
            SetTargetEnemy(fallbackEnemyProfile);
        }

        ResolvePlayerTransforms();
        EnsureParryShieldVisual();
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }

        RestoreTimeScaleAfterHitstop(force: true);
        RestoreCameraAfterShake();
        RestorePlayerRobotVisualPoseImmediate();
        EndParryPositionLock();
        EndEnemyAttackPositionAnchor();

        if (parryShieldObject != null)
        {
            Destroy(parryShieldObject);
            parryShieldObject = null;
        }
    }

    private void Update()
    {
        if (ShouldSkipCombatTick())
        {
            return;
        }

        UpdateAttackTimers();
        UpdateStepInvincibility();
        UpdateEnemyPostureRecovery();
    }

    private void LateUpdate()
    {
        if (ShouldSkipCombatTick())
        {
            return;
        }

        ApplyDefensivePositionLock();
    }

    /// <summary>ResultPhase 中は攻撃タイマー・位置ロック等の Update を完全スキップします。</summary>
    private bool ShouldSkipCombatTick()
    {
        if (IsDemoCombatFrozen)
        {
            return true;
        }

        DemoTimeLineManager timeline = DemoTimeLineManager.Instance;
        return timeline != null && timeline.CurrentState == DemoState.ResultPhase;
    }

    /// <summary>パリィ中・敵攻撃中はプレイヤーを押し出されないよう位置を固定します（ステップ中は除く）。</summary>
    private void ApplyDefensivePositionLock()
    {
        if (playerMovementTransform == null || IsCombatStepSliding)
        {
            return;
        }

        if (parryPositionLockActive)
        {
            playerMovementTransform.position = parryPositionLockWorld;
            return;
        }

        if (enemyAttackAnchorActive)
        {
            playerMovementTransform.position = enemyAttackAnchorWorld;
        }
    }

    /// <summary>シーンに無い場合は動的生成して返します。</summary>
    public static CombatActionFeedbackManager EnsureInstance()
    {
        if (Instance != null)
        {
            return Instance;
        }

        GameObject hub = GameObject.Find("DebugSystemsHub");
        if (hub != null)
        {
            CombatActionFeedbackManager existing = hub.GetComponent<CombatActionFeedbackManager>();
            return existing != null ? existing : hub.AddComponent<CombatActionFeedbackManager>();
        }

        return new GameObject(nameof(CombatActionFeedbackManager))
            .AddComponent<CombatActionFeedbackManager>();
    }

    /// <summary>対峙する敵プロファイルをセットし、体勢値をリセットします。</summary>
    /// <param name="profile">敵攻撃プロファイル</param>
    /// <param name="enemyMasterId">EnemyMasterData.id（省略時は profile.enemyMasterId を使用）</param>
    /// <param name="behaviorProfile">複数技 JSON プロファイル（省略可）</param>
    public void SetTargetEnemy(
        EnemyAttackProfile profile,
        string enemyMasterId = null,
        EnemyBehaviorProfile behaviorProfile = null)
    {
        if (profile == null || !profile.IsValid())
        {
            Debug.LogWarning("[CombatActionFeedbackManager] 無効な敵プロファイルのためセットを拒否しました。");
            return;
        }

        CancelOngoingEnemyAttack();
        currentEnemyProfile = profile;
        currentBehaviorProfile = behaviorProfile;
        CurrentEnemyMasterId = ResolveEnemyMasterId(enemyMasterId ?? profile.enemyMasterId, profile.enemyName);
        currentEnemyPosture = 0f;
        currentEnemyHp = profile.maxHp;
        IsEnemyDefeated = false;

        // 新敵対峙時に Arts 由来の体勢デバフをリセット
        ArtsEffectExecutor.EnsureInstance()?.ResetEnemyDebuffState();

        string comboInfo = behaviorProfile != null && behaviorProfile.HasComboAttacks()
            ? $" / 行動JSON <b>{behaviorProfile.patternId}</b>（{behaviorProfile.attacks.Count} 技）"
            : string.Empty;

        Debug.Log(
            $"<color=#FF8A65><b>[CombatActionFeedbackManager] 対峙敵をセット:</b> {profile.enemyName}</color>\n" +
            $"<color=#FFD54F>  マスターID: {(string.IsNullOrWhiteSpace(CurrentEnemyMasterId) ? "未設定" : CurrentEnemyMasterId)} / " +
            $"ディレイ乱数 {profile.minDelay:F2}〜{profile.maxDelay:F2}s / " +
            $"判定 {profile.activeWindow:F2}s / 体幹 {profile.maxPosture:F0} / " +
            $"HP {profile.maxHp} / STR {profile.attackStrength} / DEF {profile.defense} / " +
            $"ジャスト窓 {profile.parryJustWindow:F2}s{comboInfo}</color>");
    }

    /// <summary>EnemyMasterBridge 経由の ActiveEnemyCombatStats から戦闘プロファイルをセットします。</summary>
    public void SetTargetEnemyFromMaster(ActiveEnemyCombatStats activeEnemy)
    {
        if (activeEnemy?.attackProfile == null)
        {
            Debug.LogWarning("[CombatActionFeedbackManager] ActiveEnemyCombatStats が無効です。");
            return;
        }

        if (!string.IsNullOrWhiteSpace(activeEnemy.enemyId))
        {
            activeEnemy.attackProfile.enemyMasterId = activeEnemy.enemyId;
        }

        SetTargetEnemy(activeEnemy.attackProfile, activeEnemy.enemyId, activeEnemy.behaviorProfile);
    }

    /// <summary>ドロップテーブル不正などの警告を戦闘ログへ出力します。</summary>
    public void LogDropTableWarning(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        Debug.Log($"<color=#FF5252><b>【ドロップ警告】{message}</b></color>");
    }

    private static string ResolveEnemyMasterId(string explicitId, string enemyName)
    {
        if (!string.IsNullOrWhiteSpace(explicitId))
        {
            return explicitId.Trim();
        }

        return EnemyDropExecutor.TryResolveEnemyIdByName(enemyName);
    }

    /// <summary>
    /// 現在セットされている敵プロファイルに基づき、視覚 AI と同期した攻撃を開始します。
    /// </summary>
    public void TriggerEnemyAttack()
    {
        if (ShouldSkipCombatTick() || IsDemoCombatFrozen)
        {
            return;
        }

        if (currentEnemyProfile == null)
        {
            Debug.LogWarning("[CombatActionFeedbackManager] 敵プロファイル未設定。先に SetTargetEnemy を呼んでください。");
            return;
        }

        CancelOngoingEnemyAttack();

        // JSON 行動プロファイルがあれば複数技コンボループへ
        if (currentBehaviorProfile != null &&
            currentBehaviorProfile.HasComboAttacks() &&
            currentBehaviorProfile.HasInitialAttackCandidates())
        {
            enemyAttackCoroutine = StartCoroutine(ExecuteComboRoutine(currentBehaviorProfile));
            return;
        }

        float randomDelay = currentEnemyProfile.RollRandomDelay();
        float activeWindow = currentEnemyProfile.activeWindow;

        VisibleEnemyAI visualEnemy = ResolveVisibleEnemy();
        if (visualEnemy != null)
        {
            visualEnemy.StartVisualAttack(currentEnemyProfile, randomDelay, activeWindow, this);
            return;
        }

        enemyAttackCoroutine = StartCoroutine(
            EnemyDelayedAttackRoutine(currentEnemyProfile, randomDelay, activeWindow));
    }

    /// <summary>VisibleEnemyAI を登録し、T キー攻撃の視覚同期先にします。</summary>
    /// <param name="visibleEnemy">視覚的エネミー AI</param>
    public void RegisterVisibleEnemy(VisibleEnemyAI visibleEnemy)
    {
        if (visibleEnemy == null)
        {
            return;
        }

        registeredVisibleEnemy = visibleEnemy;
    }

    /// <summary>溜め（Anticipation）フェーズ開始を通知します（ログ用）。</summary>
    /// <param name="profile">敵プロファイル</param>
    /// <param name="anticipationSeconds">溜め秒数</param>
    /// <param name="actionLabel">技名（コンボ時。省略時は敵名）</param>
    public void NotifyAnticipationStarted(
        EnemyAttackProfile profile,
        float anticipationSeconds,
        string actionLabel = null)
    {
        string enemyName = profile != null ? profile.enemyName : "敵";
        string label = string.IsNullOrWhiteSpace(actionLabel) ? enemyName : actionLabel;
        Debug.Log(
            $"<color=#FF8A65><b>【敵攻撃予備動作 · 視覚同期】{label}</b></color> " +
            $"溜め {anticipationSeconds:F2}s — 後方へ傾けてピタッと固定（Anticipation）");
    }

    /// <summary>振り下ろしフレームと同期して攻撃判定ウィンドウを開きます。</summary>
    /// <param name="activeWindowSeconds">判定ウィンドウ秒数</param>
    /// <param name="actionLabel">技名（コンボ時。省略時は敵名）</param>
    public void NotifyAttackWindowOpened(float activeWindowSeconds, string actionLabel = null)
    {
        attackActiveTimer = Mathf.Max(0.01f, activeWindowSeconds);
        attackWindowElapsed = 0f;
        defensiveActionResolvedThisWindow = false;
        ResolvePlayerTransforms();
        BeginEnemyAttackPositionAnchor();

        string enemyName = currentEnemyProfile != null ? currentEnemyProfile.enemyName : "敵";
        string label = string.IsNullOrWhiteSpace(actionLabel) ? enemyName : actionLabel;
        Debug.Log(
            $"<color=#FF5252><b>【攻撃判定 ON · 視覚同期】{label}</b></color> " +
            $"極小ウィンドウ {activeWindowSeconds:F2}s — 振り下ろしと同時！パリィ/ステップを合わせろ！");
    }

    /// <summary>攻撃判定ウィンドウを閉じ、未対処なら被弾を確定します。</summary>
    public void NotifyAttackWindowClosed()
    {
        if (attackActiveTimer > 0f && !defensiveActionResolvedThisWindow)
        {
            ResolvePlayerHitByEnemy("攻撃フレームを見切れなかった");
        }

        attackActiveTimer = 0f;
        attackWindowElapsed = 0f;
        EndEnemyAttackPositionAnchor();

        string enemyName = currentEnemyProfile != null ? currentEnemyProfile.enemyName : "敵";
        Debug.Log($"<color=#AAAAAA>【攻撃判定 OFF · 視覚同期】{enemyName} の刃は通り過ぎた。</color>");
    }

    /// <summary>登録済みまたはシーン内の VisibleEnemyAI を解決します。</summary>
    private VisibleEnemyAI ResolveVisibleEnemy()
    {
        if (registeredVisibleEnemy != null)
        {
            return registeredVisibleEnemy;
        }

        registeredVisibleEnemy = FindAnyObjectByType<VisibleEnemyAI>();
        return registeredVisibleEnemy;
    }

    /// <summary>
    /// プレイヤーのパリィ入力を判定します（F キー想定）。
    /// 防壁を前方に突き出し、出現中に敵攻撃と衝突すればパリィ判定します。
    /// </summary>
    public void AttemptParry()
    {
        if (currentEnemyProfile == null)
        {
            Debug.LogWarning("[CombatActionFeedbackManager] 敵プロファイル未設定のためパリィ不可。");
            return;
        }

        ResolvePlayerTransforms();
        CancelStepSlideIfActive();

        if (!TryConsumeStamina(parryStaminaCost))
        {
            Debug.LogWarning("<color=#FF6B6B>【スタミナ不足】パリィ不能、防御姿勢が崩れた！</color>");
            if (IsEnemyAttackActive)
            {
                ResolvePlayerHitByEnemy("スタミナ切れでパリィ失敗");
            }

            return;
        }

        parryMissLoggedThisInput = false;
        StartParryShieldRoutine();

        if (!IsEnemyAttackActive)
        {
            LogParryMissIfNeeded();
            return;
        }

        TryResolveParryFromGuard();
    }

    /// <summary>
    /// プレイヤーのステップ回避入力を判定します（LeftCtrl 想定）。
    /// 入力方向へ高速スライドし、敵攻撃と重なればジャスト回避を判定します。
    /// </summary>
    public void AttemptStepEvade()
    {
        InventoryManager inventory = InventoryManager.Instance ?? InventoryManager.EnsureInstance();
        if (inventory != null && inventory.IsOverweight())
        {
            Debug.Log(
                "<b><color=#FF3333>【重量超過警告】装備や素材が重すぎてステップ回避が実行できない！</color></b>");
            if (IsEnemyAttackActive)
            {
                ResolvePlayerHitByEnemy("重量超過でステップ不能");
            }

            return;
        }

        if (IsParryShieldActive)
        {
            Debug.Log(
                "<color=#AAAAAA>【ステップ不発】パリィ防壁展開中はステップ回避できません。</color>");
            return;
        }

        if (!TryConsumeStamina(stepStaminaCost))
        {
            Debug.LogWarning("<color=#FF6B6B>【スタミナ不足】脚が止まり、ステップ回避に失敗！</color>");
            if (IsEnemyAttackActive)
            {
                ResolvePlayerHitByEnemy("スタミナ切れでステップ失敗");
            }

            return;
        }

        ResolvePlayerTransforms();
        BeginStepInvincibility(stepInvincibilitySeconds);
        StartStepSlideRoutine();

        Debug.Log("<color=#B0E0E6>【ステップ回避】足を滑らせ、一瞬の隙を縫った。</color>");
        TryResolveJustStepEvade();
    }

    /// <summary>
    /// 戦闘中のインスタント・クラフト（X キー想定）を1フレームで実行します。
    /// 職人工程を完全スキップし、素材1個を消費して即席武器を生成・強制装備します。
    /// </summary>
    /// <returns>素材消費と装備に成功した場合 true</returns>
    public bool AttemptInstantCraft()
    {
        if (IsCrafting)
        {
            Debug.LogWarning(
                "<color=#FFB74D><b>[AttemptInstantCraft]</b></color> " +
                "こだわり工程クラフト中は即席クラフトできません。");
            return false;
        }

        InventoryManager inventory = InventoryManager.Instance ?? InventoryManager.EnsureInstance();
        if (!inventory.TryConsumeInstantCraftMaterial(out string consumedMaterialId))
        {
            Debug.LogError("即席クラフトに必要な素材が足りない！");
            return false;
        }

        InstantCraftWeaponProfile weaponProfile =
            InstantCraftWeaponRegistry.ResolveWeaponFromMaterial(consumedMaterialId);
        ItemData weaponItem = InstantCraftWeaponRegistry.CreateInventoryItem(weaponProfile);
        if (weaponItem == null || !weaponItem.IsValid())
        {
            Debug.LogError("[CombatActionFeedbackManager] 即席武器データの生成に失敗しました。");
            return false;
        }

        inventory.AddItem(weaponItem, 1);

        ResolvePlayerTransforms();
        if (!EnsurePlayerRobotVisualTransform())
        {
            Debug.LogWarning("[CombatActionFeedbackManager] PlayerRobot ビジュアル未解決のため装備のみ完了しました。");
        }
        else
        {
            PlayerInstantWeaponLoadout loadout = PlayerInstantWeaponLoadout.EnsureOn(playerTransform);
            loadout?.Equip(weaponProfile, playerRobotVisualTransform);
            StartInstantCraftSnapFeedback();
        }

        string materialLabel = string.Equals(consumedMaterialId, InstantCraftMaterialIds.GryphonBone, StringComparison.Ordinal)
            ? "グリフォンの骨"
            : "鉄鉱石";

        Debug.Log(
            "<b><color=#00FF00>【インスタント・クラフト成功】</color></b> " +
            $"<color=#B9F6CA>銘：{weaponProfile.EngravingName} を現地調達！</color>\n" +
            $"<color=#69F0AE>  消費素材: {materialLabel} ×1 / 攻撃: {weaponProfile.AttackType} / " +
            $"耐久 {weaponProfile.MaxDurability} — 工程ゼロ・アドリブハック完了</color>");

        return true;
    }

    /// <summary>
    /// マウスホイール方向に応じて攻撃属性（Slash / Thrust / Strike）をサイクル切り替えします。
    /// </summary>
    /// <param name="direction">正=次の属性、負=前の属性</param>
    public void CycleSelectedAttackType(int direction)
    {
        if (direction == 0 || PlayerAttackTypeCycleOrder.Length == 0)
        {
            return;
        }

        int length = PlayerAttackTypeCycleOrder.Length;
        int nextIndex = selectedAttackTypeIndex + (direction > 0 ? 1 : -1);
        if (nextIndex < 0)
        {
            nextIndex = length - 1;
        }
        else if (nextIndex >= length)
        {
            nextIndex = 0;
        }

        if (nextIndex == selectedAttackTypeIndex)
        {
            return;
        }

        selectedAttackTypeIndex = nextIndex;
        LogCurrentWeaponStance();
    }

    /// <summary>現在選択中の攻撃構えを Rich Text でコンソールへ出力します。</summary>
    public void LogCurrentWeaponStance()
    {
        string attackType = CurrentSelectedAttackType;
        string colorHex = attackType switch
        {
            PlayerAttackTypeSlash => "#FF6B6B",
            PlayerAttackTypeThrust => "#4FC3F7",
            PlayerAttackTypeStrike => "#FFD54F",
            _ => "#FFFFFF"
        };

        Debug.Log(
            $"<color={colorHex}><b>[武器選択]</b></color> " +
            $"現在の構え: <b>{attackType}</b>");

        CombatStats playerCombat = ResolvePlayerCombatStats();
        ItemData crafted = InventoryManager.Instance?.EquippedCraftedWeapon;
        if (playerCombat != null)
        {
            string weaponLabel = crafted != null ? crafted.id : "未装備";
            Debug.Log(
                $"<color=#FFE082>[武器還元] {weaponLabel} / 攻撃力STR={playerCombat.Strength} " +
                $"(装備+{playerCombat.EquipmentStrengthBonus}) " +
                $"体勢×{playerCombat.EquipmentPoiseDamageMultiplier:F2} " +
                $"スタミナ×{playerCombat.EquipmentStaminaCostMultiplier:F2}</color>");
        }
    }

    /// <summary>
    /// PlayerRobot の 3D モデルを Transform 直駆動で攻撃モーションさせます。
    /// スタミナ 15 を消費し、攻撃属性を戦闘生ログへ蓄積します。
    /// </summary>
    /// <param name="attackType">Slash / Thrust / Strike</param>
    public void PerformPlayerAttack(string attackType)
    {
        if (!TryNormalizePlayerAttackType(attackType, out string normalizedAttackType))
        {
            Debug.LogWarning($"[CombatActionFeedbackManager] 未対応の攻撃タイプ: {attackType}");
            return;
        }

        ResolvePlayerTransforms();
        PlayerInstantWeaponLoadout instantLoadout = PlayerInstantWeaponLoadout.Resolve(playerTransform);
        if (instantLoadout != null && instantLoadout.HasEquippedWeapon)
        {
            normalizedAttackType = instantLoadout.EquippedAttackType;
        }

        if (!EnsurePlayerRobotVisualTransform())
        {
            Debug.LogWarning("[CombatActionFeedbackManager] PlayerRobot のビジュアル Transform を解決できません。");
            return;
        }

        CancelOngoingPlayerAttackMotion(restoreToRestPose: true);

        if (!playerRobotRestPoseCached)
        {
            CachePlayerRobotRestPose();
        }

        float attackStaminaCost = playerAttackStaminaCost;
        CombatStats staminaCombat = ResolvePlayerCombatStats();
        if (staminaCombat != null)
        {
            attackStaminaCost *= staminaCombat.EquipmentStaminaCostMultiplier;
        }

        if (!TryConsumeStamina(attackStaminaCost))
        {
            Debug.LogWarning(
                $"<color=#FF6B6B>【スタミナ不足】{normalizedAttackType} 攻撃が不発！脚と腕が止まった。</color>");
            return;
        }

        RecordPlayerAttackToCombatLog(normalizedAttackType);

        int motionSerial = ++playerAttackMotionSerial;
        playerAttackMotionCoroutine = StartCoroutine(
            PlayerAttackMotionRoutine(normalizedAttackType, motionSerial));
    }

    /// <summary>魔法発動失敗などの警告を戦闘ログへ出力します。</summary>
    public void LogMagicCastWarning(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        Debug.Log($"<color=#FF8A80>【魔導警告】{message}</color>");
    }

    /// <summary>
    /// MagicCastController 成功時の魔法ヒットを敵へ適用します（威力 power を攻撃力相当として計算）。
    /// 敵が未設定または撃破済みの場合はログのみ残します。
    /// </summary>
    public void TryApplyPlayerMagicHit(MagicCastResult result)
    {
        string elementLabel = string.IsNullOrWhiteSpace(result.Element) ? "無" : result.Element;
        Debug.Log(
            $"<color=#CE93D8>【魔導命中】{result.MagicName}（{elementLabel}）が展開 — 威力 {result.Power:F0}</color>");

        if (currentEnemyProfile == null || IsEnemyDefeated || currentEnemyHp <= 0)
        {
            return;
        }

        VisibleEnemyAI enemy = ResolveVisibleEnemy();
        if (enemy == null)
        {
            return;
        }

        ResolvePlayerTransforms();
        if (!IsPlayerAttackInRange(enemy))
        {
            Debug.Log(
                $"<color=#9E9E9E>【魔導空振り】{result.MagicName} は {currentEnemyProfile.enemyName} に届かなかった。</color>");
            return;
        }

        int attackPower = Mathf.Max(1, Mathf.RoundToInt(result.Power));
        ApplyEnemyHpDamage(attackPower);

        float postureGain = currentEnemyProfile.postureDamageSlash * 0.5f;
        AddEnemyPosture(postureGain);
    }

    /// <summary>進行中の PlayerRobot 攻撃モーションを中断し、必要なら直立ポーズへ戻します。</summary>
    /// <param name="restoreToRestPose">true でキャッシュ済みの休息姿勢へ即時復元</param>
    private void CancelOngoingPlayerAttackMotion(bool restoreToRestPose)
    {
        if (playerAttackMotionCoroutine != null)
        {
            StopCoroutine(playerAttackMotionCoroutine);
            playerAttackMotionCoroutine = null;
            playerAttackMotionSerial++;
        }

        IsPlayerAttackMotionActive = false;

        if (restoreToRestPose)
        {
            RestorePlayerRobotVisualPoseImmediate();
        }
    }

    /// <summary>ジャスト成功時にヒットストップとカメラシェイクを同時発動します。</summary>
    /// <param name="hitstopSeconds">実時間でのヒットストップ秒数</param>
    /// <param name="shakeMagnitude">カメラシェイクの揺れ幅</param>
    /// <param name="isParry">ジャストパリィ由来か（ログ用）</param>
    private void StartJustSuccessFeedback(float hitstopSeconds, float shakeMagnitude, bool isParry)
    {
        if (hitstopCoroutine != null)
        {
            StopCoroutine(hitstopCoroutine);
            hitstopCoroutine = null;
        }

        RestoreTimeScaleAfterHitstop(force: true);

        if (cameraShakeCoroutine != null)
        {
            StopCoroutine(cameraShakeCoroutine);
            RestoreCameraAfterShake();
        }

        hitstopCoroutine = StartCoroutine(TriggerHitStop(hitstopSeconds));
        cameraShakeCoroutine = StartCoroutine(TriggerCameraShake(hitstopSeconds, shakeMagnitude, isParry));
    }

    /// <summary>ジャスト回避成功を戦歴ギャラリー用ログへ加算します。</summary>
    private void RecordJustEvasionToHistory()
    {
        GamePhaseEventBridge bridge = GamePhaseEventBridge.Instance
            ?? FindAnyObjectByType<GamePhaseEventBridge>();
        bridge?.UpdateMetricFromGameplay(GameplayActionMetricTypes.JustEvasion, 1);
    }

    /// <summary>敵攻撃を受けた際の被弾処理（クラフト中断など）を行います。</summary>
    private void ResolvePlayerHitByEnemy(string reason)
    {
        if (defensiveActionResolvedThisWindow)
        {
            return;
        }

        defensiveActionResolvedThisWindow = true;

        Debug.Log(
            $"<color=#FF5252><b>【被弾】{reason}</b></color> " +
            "<color=#FF8A80>深い一撃で体勢を崩された…</color>");

        ApplyEnemyAttackDamageToPlayer();

        if (ShouldInterruptCraftingByEnemyHit())
        {
            TriggerCraftingDamageBurstAndRecoverToBattle(reason);
        }
    }

    /// <summary>
    /// CraftingPhase 中被弾時に素材バーストと BattlePhase 強制復帰を一連で実行します。
    /// </summary>
    /// <param name="hitReason">被弾理由（ログ用）</param>
    private void TriggerCraftingDamageBurstAndRecoverToBattle(string hitReason)
    {
        Debug.Log(
            "<color=#FF7043><b>【戦闘連動 · クラフト中断】</b></color> " +
            $"<color=#FFAB91>被弾理由: {hitReason} — 投入素材バーストと BattlePhase 復帰を開始します。</color>");

        CraftingExperimentHub craftingHub = CraftingExperimentHub.Instance
            ?? CraftingExperimentHub.EnsureInstance();
        craftingHub?.ApplyDemoCraftingPhaseThreatMode();

        InterruptByDamage();
    }

    /// <summary>被弾時にクラフト中断（バースト）を発火すべきかを判定します。</summary>
    private bool ShouldInterruptCraftingByEnemyHit()
    {
        DemoTimeLineManager timeline = DemoTimeLineManager.Instance;
        if (timeline == null || timeline.CurrentState != DemoState.CraftingPhase)
        {
            return false;
        }

        if (!timeline.IsCraftingDamageInterruptAllowed)
        {
            return false;
        }

        CraftingExperimentHub craftingHub = CraftingExperimentHub.Instance;
        if (craftingHub == null || !craftingHub.IsActive)
        {
            return false;
        }

        if (craftingHub.IsSafetyZone)
        {
            Debug.Log(
                "<color=#90EE90><b>[被弾保護]</b></color> 安全地帯のためクラフト中断は発火しません。");
            return false;
        }

        DetailedCraftingProcessManager processManager = DetailedCraftingProcessManager.Instance;
        bool craftingSessionLive = IsCrafting ||
            (processManager != null && processManager.IsSessionActive);
        return craftingSessionLive;
    }

    /// <summary>こだわり工程の最初のキー入力時に詳細クラフト状態へ遷移します。</summary>
    public void NotifyDetailedCraftingStarted()
    {
        IsCrafting = true;
    }

    /// <summary>Enter 正常終了またはセッション終了時に詳細クラフト状態を解除します。</summary>
    public void NotifyDetailedCraftingFinished()
    {
        IsCrafting = false;
    }

    /// <summary>ResultPhase 着地時に敵攻撃・プレイヤー攻撃モーションを停止し、敵 AI を完全凍結します。</summary>
    public void StopCombatMotionForCraftingResult()
    {
        FreezeCombatSystemsForResultPhase();
    }

    /// <summary>ResultPhase 用に戦闘ループと VisibleEnemyAI をコンポーネントレベルで停止します。</summary>
    private void FreezeCombatSystemsForResultPhase()
    {
        IsDemoCombatFrozen = true;
        CancelOngoingEnemyAttack();
        ForcePlayerCombatIdlePose();
        NotifyDetailedCraftingFinished();
        IsEnemyDefeated = true;

        VisibleEnemyAI[] enemies = UnityEngine.Object.FindObjectsByType<VisibleEnemyAI>();
        for (int i = 0; i < enemies.Length; i++)
        {
            enemies[i].ShutdownForDemoResult();
        }
    }

    /// <summary>BattlePhase 復帰時に ResultPhase 凍結を解除します。</summary>
    public void UnfreezeCombatSystemsForBattle()
    {
        IsDemoCombatFrozen = false;
        IsEnemyDefeated = false;
    }

    /// <summary>
    /// 生産フェーズ入場時に戦闘中の敵攻撃・プレイヤーモーションを即時停止します（被弾連鎖防止）。
    /// </summary>
    public void HaltEnemyCombatForCraftingPhaseEntry()
    {
        CancelOngoingEnemyAttack();
        ForcePlayerCombatIdlePose();
        defensiveActionResolvedThisWindow = false;
        attackActiveTimer = 0f;
        attackWindowElapsed = 0f;
        NotifyDetailedCraftingFinished();

        VisibleEnemyAI enemy = ResolveVisibleEnemy();
        enemy?.CancelVisualAttack();
    }

    /// <summary>
    /// 危険地帯で詳細クラフト中に被弾した際、投入素材バーストと生産強制中断を実行します。
    /// バックパック内の所持品は消失しません。
    /// </summary>
    /// <returns>中断が実行された場合 true。</returns>
    public bool InterruptByDamage()
    {
        DetailedCraftingProcessManager processManager = DetailedCraftingProcessManager.Instance
            ?? DetailedCraftingProcessManager.EnsureInstance();
        CraftingExperimentHub craftingHub = CraftingExperimentHub.Instance
            ?? CraftingExperimentHub.EnsureInstance();

        bool detailedCraftActive = IsCrafting ||
            (processManager != null && processManager.IsSessionActive &&
             craftingHub != null && craftingHub.IsActive);

        if (!detailedCraftActive)
        {
            return false;
        }

        if (craftingHub != null && craftingHub.IsSafetyZone)
        {
            Debug.Log(
                "<color=#90EE90><b>[InterruptByDamage]</b> 街の工房（安全地帯）のため投入素材は保護されました。</color>");
            return false;
        }

        Debug.Log(
            "<b><color=#FF0000>【！！！被弾バースト！！！】ボスの直撃によりクラフトが強制大破中断！</color></b>");

        TriggerDamageBurstCameraFlash();
        InGameVisualUIManager.EnsureInstance().PlayCraftingDamageInterruptFlash();

        string craftType = processManager != null && processManager.IsSessionActive
            ? processManager.currentCraftType
            : craftingHub != null && craftingHub.IsActive
                ? craftingHub.GetCraftTypeKey()
                : CraftingStatusManager.CraftTypeForge;

        CraftingStatusManager statusManager = CraftingStatusManager.EnsureInstance();
        statusManager?.BurstResetCraftParams(craftType);

        processManager?.InterruptSessionByDamageBurst();

        if (craftingHub != null && craftingHub.IsActive && !craftingHub.IsSafetyZone)
        {
            bool hubBurst = craftingHub.InterruptCraftingPhaseByEnemyDamage();
            if (!hubBurst)
            {
                Debug.LogWarning(
                    "[CombatActionFeedbackManager] CraftingExperimentHub の被弾バーストが発火しませんでした。");
            }
        }

        IsCrafting = false;
        ForcePlayerCombatIdlePose();

        DemoTimeLineManager timeline = DemoTimeLineManager.Instance;
        if (timeline != null &&
            (timeline.CurrentState == DemoState.CraftingPhase ||
             timeline.CurrentState == DemoState.TransitionPhase))
        {
            timeline.HandleCraftingInterruptedByDamage();
        }

        return true;
    }

    /// <summary>被弾バースト時に Camera.main を一瞬赤くフラッシュさせます。</summary>
    private void TriggerDamageBurstCameraFlash()
    {
        if (damageBurstFlashCoroutine != null)
        {
            StopCoroutine(damageBurstFlashCoroutine);
        }

        damageBurstFlashCoroutine = StartCoroutine(DamageBurstCameraFlashCoroutine());
    }

    private IEnumerator DamageBurstCameraFlashCoroutine()
    {
        Camera mainCamera = Camera.main;
        if (mainCamera == null)
        {
            damageBurstFlashCoroutine = null;
            yield break;
        }

        Color originalBackground = mainCamera.backgroundColor;
        mainCamera.backgroundColor = new Color(0.62f, 0.02f, 0.02f, 1f);
        yield return new WaitForSecondsRealtime(0.14f);
        mainCamera.backgroundColor = originalBackground;
        damageBurstFlashCoroutine = null;
    }

    /// <summary>PlayerRobot の攻撃モーションを中断し、Idle 姿勢へ戻します。</summary>
    private void ForcePlayerCombatIdlePose()
    {
        CancelStepSlideIfActive();
        CancelOngoingPlayerAttackMotion(restoreToRestPose: true);
    }

    /// <summary>敵体勢値を加算し、上限到達時の体勢崩し演出を発火します。</summary>
    private void AddEnemyPosture(float value)
    {
        if (currentEnemyProfile == null)
        {
            return;
        }

        float maxPosture = currentEnemyProfile.maxPosture;
        currentEnemyPosture = Mathf.Clamp(
            currentEnemyPosture + Mathf.Max(0f, value),
            0f,
            maxPosture);

        if (currentEnemyPosture < maxPosture - 0.001f)
        {
            Debug.Log(
                $"<color=#FFD54F>[体勢値] {currentEnemyProfile.enemyName} " +
                $"{currentEnemyPosture:F1}/{maxPosture:F1}</color>");
            return;
        }

        TriggerEnemyDefeat("体勢崩し");
    }

    /// <summary>敵撃破を確定し、体勢値をリセットしてコールバックを発火します。</summary>
    private void TriggerEnemyDefeat(string reason)
    {
        if (currentEnemyProfile == null || IsEnemyDefeated)
        {
            return;
        }

        string enemyName = currentEnemyProfile.enemyName;
        if (reason == "体勢崩し")
        {
            Debug.Log(
                $"<color=#FFEB3B><b>【体勢崩し】{enemyName} の体幹が粉砕！" +
                "致命のスキが生まれた！</b></color>");
        }
        else
        {
            Debug.Log(
                $"<color=#FFEB3B><b>【撃破】{enemyName} の HP が尽きた！（{reason}）</b></color>");
        }

        currentEnemyPosture = 0f;
        currentEnemyHp = 0;
        IsEnemyDefeated = true;
        CancelOngoingEnemyAttack();
        OnEnemyPostureBroken?.Invoke(currentEnemyProfile);

        if (!string.IsNullOrWhiteSpace(CurrentEnemyMasterId))
        {
            EnemyDropExecutor.ProcessEnemyDeathRewards(CurrentEnemyMasterId);
        }
        else
        {
            Debug.LogWarning(
                "[CombatActionFeedbackManager] 敵マスター ID 未設定のためドロップ処理をスキップしました。");
        }
    }

    /// <summary>敵攻撃の STR をプレイヤー HP へ反映します。</summary>
    private void ApplyEnemyAttackDamageToPlayer()
    {
        if (currentEnemyProfile == null)
        {
            return;
        }

        if (IsStepInvincible)
        {
            Debug.Log(
                "<color=#B0E0E6>【無敵】ステップの隙間で刃をかわした——ダメージなし。</color>");
            return;
        }

        CombatStats playerCombat = ResolvePlayerCombatStats();
        if (playerCombat == null)
        {
            Debug.LogWarning(
                "[CombatActionFeedbackManager] プレイヤー CombatStats 未解決のため HP ダメージをスキップしました。");
            return;
        }

        playerCombat.ReceiveDamage(currentEnemyProfile.attackStrength);
    }

    /// <summary>プレイヤー攻撃のヒット判定と体勢・HP ダメージを適用します。</summary>
    private void ResolvePlayerAttackHit(string attackType)
    {
        if (currentEnemyProfile == null || IsEnemyDefeated || currentEnemyHp <= 0)
        {
            return;
        }

        VisibleEnemyAI enemy = ResolveVisibleEnemy();
        if (enemy == null)
        {
            return;
        }

        ResolvePlayerTransforms();
        if (!IsPlayerAttackInRange(enemy))
        {
            Debug.Log(
                $"<color=#9E9E9E>【空振り】{attackType} は {currentEnemyProfile.enemyName} に届かなかった。</color>");
            return;
        }

        float postureGain = GetPostureDamageForAttackType(attackType);
        CombatStats playerCombat = ResolvePlayerCombatStats();
        if (playerCombat != null)
        {
            postureGain *= playerCombat.EquipmentPoiseDamageMultiplier;
        }
        int strength = playerCombat != null ? playerCombat.Strength : 10;
        ApplyEnemyHpDamage(strength);
        AddEnemyPosture(postureGain);

        // 装着済み解放技の specialEffects をヒット時に実行（敵デバフ・プレイヤーバフ等）
        ApplyEquippedArtsEffectsOnPlayerHit(playerCombat);
    }

    /// <summary>
    /// 所持スキルの装着済み ArtsData を走査し、戦闘系 specialEffects を ArtsEffectExecutor へ渡します。
    /// </summary>
    private void ApplyEquippedArtsEffectsOnPlayerHit(CombatStats playerCombat)
    {
        PlayerSkillSlotManager skillSlots = FindAnyObjectByType<PlayerSkillSlotManager>();
        ArtsEffectExecutor executor = ArtsEffectExecutor.EnsureInstance();
        if (skillSlots == null || executor == null || playerCombat == null)
        {
            return;
        }

        List<ArtsData> equippedArts = skillSlots.CollectEquippedArtsMetadata();
        for (int i = 0; i < equippedArts.Count; i++)
        {
            ArtsData art = equippedArts[i];
            if (art?.specialEffects == null || art.specialEffects.Count == 0)
            {
                continue;
            }

            executor.ApplyArtsSpecialEffects(art, playerCombat);
        }
    }

    /// <summary>プレイヤーと敵の水平距離が攻撃射程内かを判定します。</summary>
    private bool IsPlayerAttackInRange(VisibleEnemyAI enemy)
    {
        if (playerTransform == null || enemy == null)
        {
            return false;
        }

        Vector3 playerPos = playerTransform.position;
        Vector3 enemyPos = enemy.transform.position;
        float horizontalDistance = Vector2.Distance(
            new Vector2(playerPos.x, playerPos.z),
            new Vector2(enemyPos.x, enemyPos.z));
        return horizontalDistance <= playerAttackHitRange;
    }

    /// <summary>攻撃タイプに応じた体勢ダメージ量を返します。</summary>
    private float GetPostureDamageForAttackType(string attackType)
    {
        if (currentEnemyProfile == null)
        {
            return 0f;
        }

        float baseDamage;
        switch (attackType)
        {
            case PlayerAttackTypeThrust:
                baseDamage = currentEnemyProfile.postureDamageThrust;
                break;
            case PlayerAttackTypeStrike:
                baseDamage = currentEnemyProfile.postureDamageStrike;
                break;
            default:
                baseDamage = currentEnemyProfile.postureDamageSlash;
                break;
        }

        // Debuff_ArmorDissolve による体勢ダメージ加算を ArtsEffectExecutor から取得
        float armorDissolve = ArtsEffectExecutor.Instance?.GetEnemyArmorDissolvePostureBonus() ?? 0f;
        return baseDamage + armorDissolve;
    }

    /// <summary>プレイヤー STR に基づき敵 HP を減らし、撃破時は TriggerEnemyDefeat を呼びます。</summary>
    private void ApplyEnemyHpDamage(int attackerStrength)
    {
        if (currentEnemyProfile == null || IsEnemyDefeated || currentEnemyHp <= 0)
        {
            return;
        }

        int damage = DamageCalculator.Calculate(attackerStrength, currentEnemyProfile.defense);
        currentEnemyHp = Mathf.Max(0, currentEnemyHp - damage);

        Debug.Log(
            $"<color=#A5D6A7>{currentEnemyProfile.enemyName} が {damage} ダメージを受けた。" +
            $"残り HP: {currentEnemyHp}/{currentEnemyProfile.maxHp}</color>");

        if (currentEnemyHp <= 0)
        {
            TriggerEnemyDefeat("HP ゼロ");
        }
    }

    /// <summary>プレイヤーの CombatStats を解決します。</summary>
    private CombatStats ResolvePlayerCombatStats()
    {
        ResolvePlayerTransforms();
        if (playerTransform == null)
        {
            return null;
        }

        CombatStats stats = playerTransform.GetComponent<CombatStats>();
        if (stats != null)
        {
            return stats;
        }

        return playerTransform.GetComponentInChildren<CombatStats>();
    }

    /// <summary>進行中の敵攻撃コルーチンを安全に中断します。</summary>
    private void CancelOngoingEnemyAttack()
    {
        if (enemyAttackCoroutine != null)
        {
            StopCoroutine(enemyAttackCoroutine);
            enemyAttackCoroutine = null;
        }

        VisibleEnemyAI visualEnemy = ResolveVisibleEnemy();
        if (visualEnemy != null && visualEnemy.IsAttackSequenceRunning)
        {
            visualEnemy.CancelVisualAttack();
        }

        attackActiveTimer = 0f;
        attackWindowElapsed = 0f;
        defensiveActionResolvedThisWindow = false;
        currentComboAction = null;
        EndEnemyAttackPositionAnchor();
    }

    /// <summary>現在の攻撃タイマーを更新し、未対処で終了した場合は被弾を確定します。</summary>
    private void UpdateAttackTimers()
    {
        if (attackActiveTimer <= 0f)
        {
            return;
        }

        attackWindowElapsed += Time.deltaTime;

        VisibleEnemyAI visualEnemy = ResolveVisibleEnemy();
        if (visualEnemy != null && visualEnemy.CurrentState == VisibleEnemyState.AttackActive)
        {
            // 視覚 AI が振り下ろし中は NotifyAttackWindowClosed で閉じる（二重被弾防止）
            return;
        }

        attackActiveTimer -= Time.deltaTime;
        if (attackActiveTimer > 0f)
        {
            return;
        }

        attackActiveTimer = 0f;
        if (!defensiveActionResolvedThisWindow)
        {
            ResolvePlayerHitByEnemy("攻撃フレームを見切れなかった");
        }
    }

    /// <summary>ステップ回避の無敵タイマーを更新します。</summary>
    private void UpdateStepInvincibility()
    {
        if (stepInvincibilityTimer <= 0f)
        {
            return;
        }

        stepInvincibilityTimer -= Time.deltaTime;
        if (stepInvincibilityTimer <= 0f)
        {
            stepInvincibilityTimer = 0f;
        }
    }

    /// <summary>PlayerStats を解決します（PlayerRobot 優先）。</summary>
    private PlayerStats ResolvePlayerStats()
    {
        if (playerStats != null)
        {
            return playerStats;
        }

        if (playerTransform != null)
        {
            playerStats = playerTransform.GetComponent<PlayerStats>();
            if (playerStats == null)
            {
                playerStats = playerTransform.GetComponentInChildren<PlayerStats>();
            }

            if (playerStats != null)
            {
                return playerStats;
            }
        }

        playerStats = FindAnyObjectByType<PlayerStats>();
        return playerStats;
    }

    /// <summary>敵体勢値（体幹）を時間経過で回復（減少）させます。</summary>
    private void UpdateEnemyPostureRecovery()
    {
        if (currentEnemyPosture <= 0f)
        {
            return;
        }

        ArtsEffectExecutor artsExecutor = ArtsEffectExecutor.Instance;
        if (artsExecutor != null && artsExecutor.IsEnemyPoiseRecoveryHalted())
        {
            return;
        }

        float regenMultiplier = artsExecutor != null ? artsExecutor.GetEnemyPoiseRegenMultiplier() : 1f;
        currentEnemyPosture = Mathf.Max(
            0f,
            currentEnemyPosture - enemyPostureRecoverPerSecond * regenMultiplier * Time.deltaTime);
    }

    /// <summary>必要スタミナを消費できれば true を返します。</summary>
    private bool TryConsumeStamina(float amount)
    {
        PlayerStats stats = ResolvePlayerStats();
        if (stats == null)
        {
            Debug.LogWarning("[CombatActionFeedbackManager] PlayerStats が見つからないためスタミナを消費できません。");
            return false;
        }

        float needed = Mathf.Max(0f, amount);
        if (!stats.CanUseStamina(needed))
        {
            return false;
        }

        stats.UseStamina(needed);
        return true;
    }

    /// <summary>スタミナを即時返却します。</summary>
    private void RestoreStamina(float amount)
    {
        ResolvePlayerStats()?.RestoreStamina(amount);
    }

    /// <summary>ステップ回避の無敵を開始します。</summary>
    private void BeginStepInvincibility(float duration)
    {
        stepInvincibilityTimer = Mathf.Max(stepInvincibilityTimer, duration);
        if (stepInvincibilityCoroutine != null)
        {
            StopCoroutine(stepInvincibilityCoroutine);
        }

        stepInvincibilityCoroutine = StartCoroutine(StepInvincibilityRoutine(duration));
    }

    /// <summary>無敵時間の終了を管理する補助コルーチンです。</summary>
    private IEnumerator StepInvincibilityRoutine(float duration)
    {
        yield return new WaitForSeconds(duration);
        stepInvincibilityCoroutine = null;
    }

    /// <summary>制御対象プレイヤーの Transform を解決します。</summary>
    private void ResolvePlayerTransforms()
    {
        if (playerTransform == null)
        {
            GameObject playerObject = GameObject.Find("PlayerRobot");
            if (playerObject == null)
            {
                playerObject = GameObject.Find("PlayerRobot ");
            }

            if (playerObject == null)
            {
                GameObject tagged = GameObject.FindGameObjectWithTag("Player");
                if (tagged != null)
                {
                    playerObject = tagged;
                }
            }

            if (playerObject != null)
            {
                playerTransform = playerObject.transform;
            }
            else
            {
                PlayerController controller = UnityEngine.Object.FindAnyObjectByType<PlayerController>();
                if (controller != null)
                {
                    playerTransform = controller.transform;
                }
            }
        }

        if (playerMovementTransform == null && playerTransform != null)
        {
            playerCharacterController = playerTransform.GetComponentInChildren<CharacterController>();
            playerMovementTransform = playerCharacterController != null
                ? playerCharacterController.transform
                : playerTransform;
        }

        EnsurePlayerRobotVisualTransform();
        ResolvePlayerStats();
    }

    /// <summary>PlayerRobot のビジュアル用 Transform（Geometry 子）を解決します。</summary>
    /// <returns>解決できた場合 true</returns>
    private bool EnsurePlayerRobotVisualTransform()
    {
        if (playerRobotVisualTransform != null)
        {
            if (!IsPlayerAttackMotionActive)
            {
                CachePlayerRobotRestPose();
            }

            return true;
        }

        if (playerTransform == null)
        {
            return false;
        }

        Transform robotTransform = playerTransform.Find("Robot");
        if (robotTransform == null && playerMovementTransform != null)
        {
            robotTransform = playerMovementTransform;
        }

        if (robotTransform != null)
        {
            Transform geometry = robotTransform.Find("Geometry");
            playerRobotVisualTransform = geometry != null ? geometry : robotTransform;
        }
        else
        {
            playerRobotVisualTransform = playerTransform;
        }

        CachePlayerRobotRestPose();
        return playerRobotVisualTransform != null;
    }

    /// <summary>攻撃モーション前のローカル直立姿勢をキャッシュします（攻撃中は上書きしません）。</summary>
    private void CachePlayerRobotRestPose()
    {
        if (playerRobotVisualTransform == null || IsPlayerAttackMotionActive)
        {
            return;
        }

        playerRobotRestLocalPosition = playerRobotVisualTransform.localPosition;
        playerRobotRestLocalRotation = playerRobotVisualTransform.localRotation;
        playerRobotRestLocalScale = playerRobotVisualTransform.localScale;
        playerRobotRestPoseCached = true;
    }

    /// <summary>PlayerRobot ビジュアルを直立ポーズへ即時復元します。</summary>
    private void RestorePlayerRobotVisualPoseImmediate()
    {
        if (!playerRobotRestPoseCached || playerRobotVisualTransform == null)
        {
            return;
        }

        playerRobotVisualTransform.localPosition = playerRobotRestLocalPosition;
        playerRobotVisualTransform.localRotation = playerRobotRestLocalRotation;
        playerRobotVisualTransform.localScale = playerRobotRestLocalScale;
    }

    /// <summary>PlayerRobot ビジュアルを滑らかに直立ポーズへ復元します。</summary>
    /// <param name="duration">復元秒数</param>
    private IEnumerator RestorePlayerRobotVisualPoseSmooth(float duration)
    {
        if (!playerRobotRestPoseCached || playerRobotVisualTransform == null)
        {
            yield break;
        }

        Vector3 startPosition = playerRobotVisualTransform.localPosition;
        Quaternion startRotation = playerRobotVisualTransform.localRotation;
        Vector3 startScale = playerRobotVisualTransform.localScale;
        float clampedDuration = Mathf.Max(0.01f, duration);
        float elapsed = 0f;

        while (elapsed < clampedDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / clampedDuration);
            float eased = Mathf.SmoothStep(0f, 1f, t);
            playerRobotVisualTransform.localPosition = Vector3.Lerp(startPosition, playerRobotRestLocalPosition, eased);
            playerRobotVisualTransform.localRotation = Quaternion.Slerp(startRotation, playerRobotRestLocalRotation, eased);
            playerRobotVisualTransform.localScale = Vector3.Lerp(startScale, playerRobotRestLocalScale, eased);
            yield return null;
        }

        RestorePlayerRobotVisualPoseImmediate();
    }

    /// <summary>攻撃タイプ文字列を正規化します。</summary>
    private static bool TryNormalizePlayerAttackType(string attackType, out string normalizedAttackType)
    {
        normalizedAttackType = null;
        if (string.IsNullOrWhiteSpace(attackType))
        {
            return false;
        }

        string trimmed = attackType.Trim();
        if (string.Equals(trimmed, PlayerAttackTypeSlash, StringComparison.OrdinalIgnoreCase))
        {
            normalizedAttackType = PlayerAttackTypeSlash;
            return true;
        }

        if (string.Equals(trimmed, PlayerAttackTypeThrust, StringComparison.OrdinalIgnoreCase))
        {
            normalizedAttackType = PlayerAttackTypeThrust;
            return true;
        }

        if (string.Equals(trimmed, PlayerAttackTypeStrike, StringComparison.OrdinalIgnoreCase))
        {
            normalizedAttackType = PlayerAttackTypeStrike;
            return true;
        }

        return false;
    }

    /// <summary>実行した攻撃属性を戦闘生ログ（PlayerActionLogger / GamePhaseEventBridge）へ蓄積します。</summary>
    /// <param name="attackType">Slash / Thrust / Strike</param>
    private void RecordPlayerAttackToCombatLog(string attackType)
    {
        PlayerActionLogger.Instance?.LogAttack();

        GamePhaseEventBridge bridge = GamePhaseEventBridge.Instance
            ?? FindAnyObjectByType<GamePhaseEventBridge>();
        string metricType = ResolveMetricTypeFromPlayerAttack(attackType);
        bridge?.UpdateMetricFromGameplay(metricType, 1);

        Debug.Log(
            $"<color=#CE93D8><b>[戦闘生ログ]</b></color> 攻撃属性 <b>{attackType}</b> を蓄積 " +
            $"（メトリクス: {metricType}）");
    }

    /// <summary>PlayerRobot 攻撃タイプを GameplayActionMetricTypes へ変換します。</summary>
    private static string ResolveMetricTypeFromPlayerAttack(string attackType)
    {
        switch (attackType)
        {
            case PlayerAttackTypeThrust:
                return GameplayActionMetricTypes.Pierce;
            case PlayerAttackTypeStrike:
                return GameplayActionMetricTypes.Blunt;
            default:
                return GameplayActionMetricTypes.Slash;
        }
    }

    /// <summary>攻撃タイプに応じた PlayerRobot モーションコルーチンを実行します。</summary>
    /// <param name="attackType">Slash / Thrust / Strike</param>
    /// <param name="motionSerial">中断された古いコルーチンが状態を汚さないための世代番号</param>
    private IEnumerator PlayerAttackMotionRoutine(string attackType, int motionSerial)
    {
        IsPlayerAttackMotionActive = true;

        try
        {
            switch (attackType)
            {
                case PlayerAttackTypeSlash:
                    yield return PlayerSlashAttackRoutine();
                    break;
                case PlayerAttackTypeThrust:
                    yield return PlayerThrustAttackRoutine();
                    break;
                case PlayerAttackTypeStrike:
                    yield return PlayerStrikeAttackRoutine();
                    break;
            }
        }
        finally
        {
            if (motionSerial == playerAttackMotionSerial)
            {
                RestorePlayerRobotVisualPoseImmediate();
                IsPlayerAttackMotionActive = false;
                playerAttackMotionCoroutine = null;
                NotifyInstantWeaponDurabilityAfterAttack();
            }
        }
    }

    /// <summary>即席武器装備時に攻撃後の耐久消費とインベントリ同期を行います。</summary>
    private void NotifyInstantWeaponDurabilityAfterAttack()
    {
        PlayerInstantWeaponLoadout loadout = PlayerInstantWeaponLoadout.Resolve(playerTransform);
        if (loadout == null || !loadout.HasEquippedWeapon)
        {
            return;
        }

        string itemIdBefore = loadout.EquippedItemId;
        bool stillUsable = loadout.ConsumeDurabilityOnAttack();
        if (stillUsable)
        {
            return;
        }

        InventoryManager inventory = InventoryManager.Instance ?? InventoryManager.EnsureInstance();
        if (!string.IsNullOrWhiteSpace(itemIdBefore))
        {
            inventory.TryConsumeItem(itemIdBefore, 1);
        }
    }

    /// <summary>インスタント・クラフト成功時の PlayerRobot クイック変形演出を開始します。</summary>
    private void StartInstantCraftSnapFeedback()
    {
        if (instantCraftSnapCoroutine != null)
        {
            StopCoroutine(instantCraftSnapCoroutine);
            instantCraftSnapCoroutine = null;
        }

        instantCraftSnapCoroutine = StartCoroutine(InstantCraftSnapRoutine());
    }

    /// <summary>
    /// 即席武器組み上げの手応えとして、Scale / Rotation を一瞬だけ鋭く引き締めて元に戻します。
    /// </summary>
    private IEnumerator InstantCraftSnapRoutine()
    {
        Transform visual = playerRobotVisualTransform;
        if (visual == null)
        {
            instantCraftSnapCoroutine = null;
            yield break;
        }

        if (!playerRobotRestPoseCached)
        {
            CachePlayerRobotRestPose();
        }

        Vector3 restScale = playerRobotRestLocalScale;
        Quaternion restRotation = playerRobotRestLocalRotation;
        Vector3 snapScale = new Vector3(
            restScale.x * instantCraftSnapScaleTighten,
            restScale.y * instantCraftSnapScaleStretchY,
            restScale.z * instantCraftSnapScaleTighten);
        Quaternion snapRotation = restRotation * Quaternion.Euler(
            -instantCraftSnapTwistDegrees,
            instantCraftSnapTwistDegrees * 0.6f,
            instantCraftSnapTwistDegrees);

        float tightenPhase = instantCraftSnapDuration * 0.35f;
        float restorePhase = instantCraftSnapDuration * 0.65f;
        float elapsed = 0f;

        while (elapsed < tightenPhase)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / tightenPhase);
            float eased = 1f - Mathf.Pow(1f - t, 3f);
            visual.localScale = Vector3.Lerp(restScale, snapScale, eased);
            visual.localRotation = Quaternion.Slerp(restRotation, snapRotation, eased);
            yield return null;
        }

        elapsed = 0f;
        while (elapsed < restorePhase)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / restorePhase);
            float eased = 1f - Mathf.Pow(1f - t, 2f);
            visual.localScale = Vector3.Lerp(snapScale, restScale, eased);
            visual.localRotation = Quaternion.Slerp(snapRotation, restRotation, eased);
            yield return null;
        }

        visual.localScale = restScale;
        visual.localRotation = restRotation;
        instantCraftSnapCoroutine = null;
    }

    /// <summary>斬撃（Slash）: 右斜め後ろの振りかぶり → 0.1 秒で鋭い横斬り＋踏み込み。</summary>
    private IEnumerator PlayerSlashAttackRoutine()
    {
        Transform visual = playerRobotVisualTransform;
        Quaternion windupRotation = playerRobotRestLocalRotation * Quaternion.Euler(-28f, 62f, 34f);
        Quaternion strikeRotation = playerRobotRestLocalRotation * Quaternion.Euler(16f, -92f, -42f);
        Vector3 strikePosition = playerRobotRestLocalPosition + new Vector3(0f, 0.02f, slashForwardSlide);

        visual.localRotation = windupRotation;
        visual.localPosition = playerRobotRestLocalPosition;
        yield return null;

        Debug.Log("<color=#81D4FA><b>【斬撃】</b></color> 刀剣を右後方へクイッと振りかぶり——ブォンッ！");

        float elapsed = 0f;
        float duration = Mathf.Max(0.01f, slashSwingSeconds);
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float eased = 1f - Mathf.Pow(1f - t, 3f);
            visual.localRotation = Quaternion.Slerp(windupRotation, strikeRotation, eased);
            visual.localPosition = Vector3.Lerp(playerRobotRestLocalPosition, strikePosition, eased);
            yield return null;
        }

        ResolvePlayerAttackHit(PlayerAttackTypeSlash);

        yield return RestorePlayerRobotVisualPoseSmooth(slashRecoverySeconds);
    }

    /// <summary>突刺（Thrust）: 奥行き Z を 1.9 倍に伸長 → 0.08 秒で復元。</summary>
    private IEnumerator PlayerThrustAttackRoutine()
    {
        Transform visual = playerRobotVisualTransform;
        Vector3 stretchScale = new Vector3(
            playerRobotRestLocalScale.x,
            playerRobotRestLocalScale.y,
            playerRobotRestLocalScale.z * thrustStretchScaleZ);
        Vector3 thrustPosition = playerRobotRestLocalPosition + new Vector3(0f, 0f, 0.12f);

        visual.localScale = stretchScale;
        visual.localPosition = thrustPosition;
        visual.localRotation = playerRobotRestLocalRotation * Quaternion.Euler(-8f, 0f, 0f);

        Debug.Log("<color=#80CBC4><b>【突刺】</b></color> 体幹が超神速で伸長——ズバッ！");

        float holdDuration = Mathf.Max(0f, thrustHoldSeconds);
        float holdElapsed = 0f;
        while (holdElapsed < holdDuration)
        {
            holdElapsed += Time.deltaTime;
            yield return null;
        }

        ResolvePlayerAttackHit(PlayerAttackTypeThrust);

        float elapsed = 0f;
        float duration = Mathf.Max(0.01f, thrustRecoverySeconds);
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float eased = 1f - Mathf.Pow(1f - t, 4f);
            visual.localScale = Vector3.Lerp(stretchScale, playerRobotRestLocalScale, eased);
            visual.localPosition = Vector3.Lerp(thrustPosition, playerRobotRestLocalPosition, eased);
            visual.localRotation = Quaternion.Slerp(
                playerRobotRestLocalRotation * Quaternion.Euler(-8f, 0f, 0f),
                playerRobotRestLocalRotation,
                eased);
            yield return null;
        }
    }

    /// <summary>打撃（Strike）: 大上段構え → 前方へ縦に叩き落とす X 軸回転。</summary>
    private IEnumerator PlayerStrikeAttackRoutine()
    {
        Transform visual = playerRobotVisualTransform;
        Vector3 liftedPosition = playerRobotRestLocalPosition + Vector3.up * strikeLiftHeight;
        Quaternion overheadRotation = playerRobotRestLocalRotation * Quaternion.Euler(-68f, 8f, 0f);
        Quaternion slamRotation = playerRobotRestLocalRotation * Quaternion.Euler(42f, -6f, 0f);
        Vector3 slamPosition = playerRobotRestLocalPosition
            + new Vector3(0f, -0.04f, strikeForwardDrop);

        float liftElapsed = 0f;
        float liftDuration = Mathf.Max(0.01f, strikeLiftSeconds);
        while (liftElapsed < liftDuration)
        {
            liftElapsed += Time.deltaTime;
            float t = Mathf.Clamp01(liftElapsed / liftDuration);
            float eased = Mathf.SmoothStep(0f, 1f, t);
            visual.localPosition = Vector3.Lerp(playerRobotRestLocalPosition, liftedPosition, eased);
            visual.localRotation = Quaternion.Slerp(playerRobotRestLocalRotation, overheadRotation, eased);
            yield return null;
        }

        Debug.Log("<color=#FFAB91><b>【打撃】</b></color> 大上段からガツンと叩き落とす！");

        float slamElapsed = 0f;
        float slamDuration = Mathf.Max(0.01f, strikeSlamSeconds);
        while (slamElapsed < slamDuration)
        {
            slamElapsed += Time.deltaTime;
            float t = Mathf.Clamp01(slamElapsed / slamDuration);
            float eased = 1f - Mathf.Pow(1f - t, 3f);
            visual.localRotation = Quaternion.Slerp(overheadRotation, slamRotation, eased);
            visual.localPosition = Vector3.Lerp(liftedPosition, slamPosition, eased);
            yield return null;
        }

        ResolvePlayerAttackHit(PlayerAttackTypeStrike);

        yield return RestorePlayerRobotVisualPoseSmooth(strikeRecoverySeconds);
    }

    /// <summary>ガード防壁（半透明キューブ）を生成・初期化します。</summary>
    private void EnsureParryShieldVisual()
    {
        if (parryShieldObject != null || playerTransform == null)
        {
            return;
        }

        parryShieldObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
        parryShieldObject.name = "CombatParryShield";
        parryShieldObject.transform.SetParent(playerTransform, worldPositionStays: false);
        parryShieldObject.transform.localPosition = parryShieldLocalOffset;
        parryShieldObject.transform.localRotation = Quaternion.identity;
        parryShieldObject.transform.localScale = parryShieldLocalScale;

        Collider shieldCollider = parryShieldObject.GetComponent<Collider>();
        if (shieldCollider != null)
        {
            Destroy(shieldCollider);
        }

        Renderer renderer = parryShieldObject.GetComponent<Renderer>();
        if (renderer != null)
        {
            RuntimeUrpMaterialUtility.ApplyTransparentColor(
                renderer,
                new Color(0.2f, 0.85f, 1f, 0.42f));
        }

        parryShieldObject.SetActive(false);
    }

    /// <summary>パリィ防壁表示コルーチンを開始します。</summary>
    private void StartParryShieldRoutine()
    {
        EnsureParryShieldVisual();
        if (parryShieldCoroutine != null)
        {
            StopCoroutine(parryShieldCoroutine);
            EndParryPositionLock();
        }

        parryShieldCoroutine = StartCoroutine(ParryShieldRoutine());
    }

    /// <summary>敵攻撃ウィンドウ開始時にプレイヤー位置のアンカーを設定します。</summary>
    private void BeginEnemyAttackPositionAnchor()
    {
        if (playerMovementTransform == null)
        {
            enemyAttackAnchorActive = false;
            return;
        }

        enemyAttackAnchorWorld = playerMovementTransform.position;
        enemyAttackAnchorActive = true;
    }

    /// <summary>敵攻撃ウィンドウ終了時にプレイヤー位置のアンカーを解除します。</summary>
    private void EndEnemyAttackPositionAnchor()
    {
        enemyAttackAnchorActive = false;
    }

    /// <summary>進行中の戦闘ステップスライドを中断します（パリィとの競合防止）。</summary>
    private void CancelStepSlideIfActive()
    {
        if (stepSlideCoroutine == null)
        {
            return;
        }

        StopCoroutine(stepSlideCoroutine);
        stepSlideCoroutine = null;
        IsCombatStepSliding = false;
    }

    /// <summary>パリィ開始時にプレイヤー位置のロックを有効化します。</summary>
    private void BeginParryPositionLock()
    {
        if (playerMovementTransform == null)
        {
            parryPositionLockActive = false;
            return;
        }

        parryPositionLockWorld = playerMovementTransform.position;
        parryPositionLockActive = true;
    }

    /// <summary>パリィ終了時にプレイヤー位置のロックを解除します。</summary>
    private void EndParryPositionLock()
    {
        parryPositionLockActive = false;
        IsParryShieldActive = false;
    }

    /// <summary>防壁を一瞬だけ突き出し、表示中の敵攻撃に対してパリィ判定を行います。</summary>
    private IEnumerator ParryShieldRoutine()
    {
        if (parryShieldObject == null)
        {
            yield break;
        }

        IsParryShieldActive = true;
        BeginParryPositionLock();

        try
        {
            parryShieldObject.SetActive(true);

            float elapsed = 0f;
            float duration = Mathf.Max(0.01f, parryShieldDuration);
            while (elapsed < duration)
            {
                if (IsEnemyAttackActive && !defensiveActionResolvedThisWindow)
                {
                    TryResolveParryFromGuard();
                }

                elapsed += Time.deltaTime;
                yield return null;
            }

            parryShieldObject.SetActive(false);

            if (!defensiveActionResolvedThisWindow && !parryMissLoggedThisInput)
            {
                LogParryMissIfNeeded();
            }
        }
        finally
        {
            EndParryPositionLock();
            parryShieldCoroutine = null;
        }
    }

    /// <summary>防壁展開中の敵攻撃（AttackActive）と重なればジャストパリィを成立させます。</summary>
    /// <returns>ガードが成立した場合 true</returns>
    private bool TryResolveParryFromGuard()
    {
        if (defensiveActionResolvedThisWindow || !IsEnemyAttackActive || currentEnemyProfile == null)
        {
            return false;
        }

        if (currentComboAction != null && currentComboAction.isGuardBreakable)
        {
            return false;
        }

        defensiveActionResolvedThisWindow = true;
        RestoreStamina(parryJustRefund);
        AddEnemyPosture(justParryPostureGain);
        Debug.Log(
            "<color=#00FFFF><b>【ジャストパリー成功】タイムスケール完全フリーズ（0.15秒）＆空間破砕カメラシェイク！</b></color>");
        StartJustSuccessFeedback(justParryHitstopSeconds, justParryShakeMagnitude, isParry: true);
        PlayParrySuccessJuice();
        PlayerHistoryTracker.Instance?.Increment(PlayerActionLogTypes.JustParryCount);
        return true;
    }

    /// <summary>パリィ空振りログを1入力につき1回だけ出力します。</summary>
    private void LogParryMissIfNeeded()
    {
        if (parryMissLoggedThisInput)
        {
            return;
        }

        parryMissLoggedThisInput = true;
        Debug.Log(
            "<color=#AAAAAA>【パリィ空振り】刃はまだ来ていない…タイミングが早すぎた。</color>");
    }

    /// <summary>高速ステップスライドのコルーチンを開始します。</summary>
    private void StartStepSlideRoutine()
    {
        if (playerMovementTransform == null)
        {
            return;
        }

        EndEnemyAttackPositionAnchor();

        if (stepSlideCoroutine != null)
        {
            StopCoroutine(stepSlideCoroutine);
            stepSlideCoroutine = null;
            IsCombatStepSliding = false;
        }

        stepSlideCoroutine = StartCoroutine(StepSlideRoutine());
    }

    /// <summary>
    /// 入力方向へ超高速スライドします。入力が無ければ後方へ回避します。
    /// 敵コライダーへのめり込みを避けるため、スライド中は CharacterController を一時無効化し Transform を直接移動します。
    /// </summary>
    private IEnumerator StepSlideRoutine()
    {
        Transform mover = playerMovementTransform;
        if (mover == null)
        {
            yield break;
        }

        IsCombatStepSliding = true;
        bool controllerWasEnabled = false;
        if (playerCharacterController != null)
        {
            controllerWasEnabled = playerCharacterController.enabled;
            playerCharacterController.enabled = false;
        }

        try
        {
            Vector3 slideDirectionWorld = ResolveStepSlideDirectionWorld();
            Vector3 startWorldPosition = mover.position;
            Vector3 targetWorldPosition = startWorldPosition + slideDirectionWorld * stepSlideDistance;

            float elapsed = 0f;
            float duration = Mathf.Max(0.01f, stepSlideDuration);
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float eased = 1f - Mathf.Pow(1f - t, 3f);
                mover.position = Vector3.Lerp(startWorldPosition, targetWorldPosition, eased);

                TryResolveJustStepEvade();
                yield return null;
            }

            mover.position = targetWorldPosition;
            TryResolveJustStepEvade();
        }
        finally
        {
            if (playerCharacterController != null)
            {
                playerCharacterController.enabled = controllerWasEnabled;
            }

            IsCombatStepSliding = false;
            stepSlideCoroutine = null;

            if (IsEnemyAttackActive)
            {
                BeginEnemyAttackPositionAnchor();
            }
        }
    }

    /// <summary>ステップ回避方向をワールド空間の単位ベクトルで返します。</summary>
    private Vector3 ResolveStepSlideDirectionWorld()
    {
        Transform reference = playerTransform != null ? playerTransform : playerMovementTransform;
        if (reference == null)
        {
            return Vector3.back;
        }

        Vector2 moveInput = GetCurrentMoveInput();
        if (moveInput.sqrMagnitude < 0.01f)
        {
            Vector3 backward = -reference.forward;
            backward.y = 0f;
            return backward.sqrMagnitude < 0.0001f ? Vector3.back : backward.normalized;
        }

        Camera mainCamera = Camera.main;
        if (mainCamera != null)
        {
            Vector3 inputDirection = new Vector3(moveInput.x, 0f, moveInput.y).normalized;
            float targetRotationY = Mathf.Atan2(inputDirection.x, inputDirection.z) * Mathf.Rad2Deg
                + mainCamera.transform.eulerAngles.y;
            Vector3 direction = Quaternion.Euler(0f, targetRotationY, 0f) * Vector3.forward;
            direction.y = 0f;
            return direction.sqrMagnitude < 0.0001f ? Vector3.back : direction.normalized;
        }

        Vector3 fallbackDirection = reference.right * moveInput.x + reference.forward * moveInput.y;
        fallbackDirection.y = 0f;
        return fallbackDirection.sqrMagnitude < 0.0001f ? Vector3.back : fallbackDirection.normalized;
    }

    /// <summary>現在の移動入力を取得します。StarterAssetsInputs と WASD のうち強い方を採用します。</summary>
    private Vector2 GetCurrentMoveInput()
    {
        Vector2 assetsInput = Vector2.zero;
        if (playerTransform != null)
        {
            StarterAssets.StarterAssetsInputs input =
                playerTransform.GetComponentInChildren<StarterAssets.StarterAssetsInputs>();
            if (input != null)
            {
                assetsInput = input.move;
            }
        }

        Vector2 keyboardInput = Vector2.zero;
        if (Keyboard.current != null)
        {
            if (Keyboard.current.wKey.isPressed) keyboardInput.y += 1f;
            if (Keyboard.current.sKey.isPressed) keyboardInput.y -= 1f;
            if (Keyboard.current.aKey.isPressed) keyboardInput.x -= 1f;
            if (Keyboard.current.dKey.isPressed) keyboardInput.x += 1f;
        }

        return keyboardInput.sqrMagnitude > assetsInput.sqrMagnitude ? keyboardInput : assetsInput;
    }

    /// <summary>ステップ無敵中に敵攻撃と重なればジャストステップを成立させます。</summary>
    private void TryResolveJustStepEvade()
    {
        if (defensiveActionResolvedThisWindow || !IsEnemyAttackActive || !IsStepInvincible)
        {
            return;
        }

        defensiveActionResolvedThisWindow = true;
        Debug.Log(
            "<color=#FFD700><b>【ジャストステップ成功】ボスの刃を紙一重で完全透過！スロー＆ブレ発生！</b></color>");
        StartJustSuccessFeedback(justStepHitstopSeconds, justStepShakeMagnitude, isParry: false);
        RecordJustEvasionToHistory();
    }

    /// <summary>敵プロファイルに基づく溜め攻撃を実行し、攻撃判定アクティブ時間を生成します。</summary>
    private IEnumerator EnemyDelayedAttackRoutine(
        EnemyAttackProfile profile,
        float delayTime,
        float activeWindow)
    {
        yield return ExecuteLegacySingleAttackRoutine(profile, delayTime, activeWindow, actionLabel: null);
        enemyAttackCoroutine = null;
    }

    /// <summary>
    /// JSON 行動プロファイルに基づく非同期コンボループ。
    /// 1 段目抽選 → 各技の溜め/判定 → comboIntervalSeconds 待機 → 次 ID を再帰追跡。
    /// </summary>
    private IEnumerator ExecuteComboRoutine(EnemyBehaviorProfile behaviorProfile)
    {
        if (behaviorProfile == null || currentEnemyProfile == null)
        {
            yield break;
        }

        EnemyAttackActionData currentAction = EnemyBehaviorProfileManager.ChooseInitialAttack(behaviorProfile);
        if (currentAction == null)
        {
            Debug.LogError(
                $"[CombatActionFeedbackManager] コンボ開始失敗: 1 段目抽選不可 ({behaviorProfile.patternId})");
            enemyAttackCoroutine = null;
            yield break;
        }

        Debug.Log(
            $"<color=#CE93D8><b>【敵コンボ開始】{behaviorProfile.patternId}</b></color> " +
            $"1 段目=<b>{currentAction.actionName}</b> ({currentAction.actionId})");

        int comboIndex = 1;
        while (currentAction != null)
        {
            yield return ExecuteComboActionStep(currentAction, comboIndex);

            string nextId = currentAction.nextComboActionId;
            if (string.IsNullOrWhiteSpace(nextId))
            {
                break;
            }

            float interval = Mathf.Max(0f, currentAction.comboIntervalSeconds);
            if (interval > 0f)
            {
                Debug.Log(
                    $"<color=#B39DDB>【コンボ待機】{interval:F2}s 後に <b>{nextId}</b> をトリガー。</color>");
                yield return new WaitForSeconds(interval);
            }

            EnemyAttackActionData nextAction =
                EnemyBehaviorProfileManager.FindActionById(behaviorProfile, nextId);
            if (nextAction == null)
            {
                Debug.LogError(
                    "[CombatActionFeedbackManager] コンボ安全終了: 未登録の nextComboActionId '" +
                    $"{nextId}' (pattern={behaviorProfile.patternId}, from={currentAction.actionId})");
                break;
            }

            comboIndex++;
            currentAction = nextAction;
        }

        currentComboAction = null;
        enemyAttackCoroutine = null;
        Debug.Log(
            $"<color=#90CAF9>【敵コンボ終了】{currentEnemyProfile.enemyName} — {behaviorProfile.patternId}</color>");
    }

    /// <summary>コンボ 1 ヒット分：溜め（フェイント延長可）→ 判定ウィンドウ → 復帰。</summary>
    private IEnumerator ExecuteComboActionStep(EnemyAttackActionData action, int comboIndex)
    {
        if (action == null || currentEnemyProfile == null)
        {
            yield break;
        }

        float anticipation = Mathf.Max(0f, action.attackDelaySeconds);
        if (action.feintChance > 0f && UnityEngine.Random.value < action.feintChance)
        {
            float feintBonus = UnityEngine.Random.Range(0.12f, 0.38f);
            anticipation += feintBonus;
            Debug.Log(
                $"<color=#FFE082>【フェイント】{action.actionName} — 溜め +{feintBonus:F2}s</color>");
        }

        float activeWindow = Mathf.Max(0.01f, action.parryWindowSeconds);
        currentComboAction = action;
        currentEnemyProfile.parryJustWindow = Mathf.Min(activeWindow, DefaultJustParryWindowSeconds);

        VisibleEnemyAI visualEnemy = ResolveVisibleEnemy();
        if (visualEnemy != null)
        {
            yield return visualEnemy.RunComboActionVisualRoutine(
                currentEnemyProfile,
                action,
                anticipation,
                activeWindow,
                this);
        }
        else
        {
            yield return ExecuteLegacySingleAttackRoutine(
                currentEnemyProfile,
                anticipation,
                activeWindow,
                action.actionName);
        }

        currentComboAction = null;
    }

    /// <summary>視覚 AI 無し／レガシー単発攻撃の溜め→判定ループ。</summary>
    private IEnumerator ExecuteLegacySingleAttackRoutine(
        EnemyAttackProfile profile,
        float delayTime,
        float activeWindow,
        string actionLabel)
    {
        string label = string.IsNullOrWhiteSpace(actionLabel) ? profile.enemyName : actionLabel;
        Debug.Log(
            $"<color=#FF8A65><b>【敵攻撃予備動作】{label}</b></color>" +
            "が武器を大きく振り上げた…（ディレイ乱数計算中…光るヒントは無い、殺意のフレームを見極めろ！）");

        if (delayTime > 0f)
        {
            yield return new WaitForSeconds(delayTime);
        }

        NotifyAttackWindowOpened(activeWindow, actionLabel);

        while (attackActiveTimer > 0f)
        {
            yield return null;
        }

        NotifyAttackWindowClosed();
        Debug.Log($"<color=#AAAAAA>【攻撃判定 OFF】{label} の刃は通り過ぎた。</color>");
    }

    /// <summary>
    /// ジャスト成功時にゲーム世界を完全停止させ、unscaled 時間で指定秒後に timeScale を復元します。
    /// VisibleEnemyAI の振り下ろし・プレイヤースライドも Time.deltaTime 依存のため同時に静止します。
    /// </summary>
    /// <param name="duration">実時間（秒）。ジャストパリィ 0.15 / ジャストステップ 0.12 想定。</param>
    private IEnumerator TriggerHitStop(float duration)
    {
        float clampedDuration = Mathf.Max(0f, duration);
        activeHitstopCount++;
        if (activeHitstopCount == 1)
        {
            hitstopRestoreTimeScale = Time.timeScale;
            if (hitstopRestoreTimeScale <= 0f)
            {
                hitstopRestoreTimeScale = 1f;
            }

            Time.timeScale = 0f;
        }

        float elapsed = 0f;
        while (elapsed < clampedDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        activeHitstopCount = Mathf.Max(0, activeHitstopCount - 1);
        if (activeHitstopCount == 0)
        {
            RestoreTimeScaleAfterHitstop(force: false);
        }

        hitstopCoroutine = null;
    }

    /// <summary>ANOMALY KILL 暗転直前のガクッとしたカメラシェイク（ヒットストップ仕様とは独立）。</summary>
    public void PlayAnomalyKillCameraShake()
    {
        if (cameraShakeCoroutine != null)
        {
            StopCoroutine(cameraShakeCoroutine);
        }

        cameraShakeCoroutine = StartCoroutine(AnomalyKillCameraShakeRoutine());
    }

    /// <summary>ジャストパリィ成功時の防壁バースト拡大。</summary>
    private void PlayParrySuccessJuice()
    {
        InGameVisualUIManager.EnsureInstance().PlayParrySuccessJuice();

        if (parryShieldBumpCoroutine != null)
        {
            StopCoroutine(parryShieldBumpCoroutine);
        }

        parryShieldBumpCoroutine = StartCoroutine(ParryShieldBurstBumpRoutine());
    }

    private IEnumerator ParryShieldBurstBumpRoutine()
    {
        EnsureParryShieldVisual();
        if (parryShieldObject == null)
        {
            yield break;
        }

        Vector3 baseScale = parryShieldLocalScale;
        Vector3 peakScale = baseScale * 2.35f;
        parryShieldObject.SetActive(true);
        parryShieldObject.transform.localScale = peakScale;

        float duration = 0.2f;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float eased = 1f - Mathf.Pow(1f - t, 3f);
            parryShieldObject.transform.localScale = Vector3.LerpUnclamped(peakScale, baseScale, eased);
            yield return null;
        }

        parryShieldObject.transform.localScale = baseScale;
        parryShieldBumpCoroutine = null;
    }

    private IEnumerator AnomalyKillCameraShakeRoutine()
    {
        Camera lensCamera = ResolveMainCamera();
        Transform shakeTransform = ResolveCameraShakeTransform(lensCamera);
        if (shakeTransform == null)
        {
            cameraShakeCoroutine = null;
            yield break;
        }

        cameraShakeTarget = lensCamera;
        cameraShakeTransform = shakeTransform;
        cameraShakeOriginalLocalPosition = shakeTransform.localPosition;
        cameraShakeOriginalFov = lensCamera != null ? lensCamera.fieldOfView : 60f;

        const float duration = 0.38f;
        const float magnitude = 0.42f;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float normalized = Mathf.Clamp01(elapsed / duration);
            float falloff = 1f - normalized;
            float jitter = magnitude * falloff;
            float offsetX = Mathf.Sin(elapsed * 92f) * jitter;
            float offsetY = Mathf.Cos(elapsed * 84f) * jitter;
            shakeTransform.localPosition = cameraShakeOriginalLocalPosition + new Vector3(offsetX, offsetY, 0f);
            yield return null;
        }

        RestoreCameraAfterShake();
        cameraShakeCoroutine = null;
    }

    /// <summary>
    /// ヒットストップと同時に走り、Cinemachine ターゲット（なければ Camera.main）を激しく揺らします。
    /// 終了時にローカル座標と FOV を正確に復元します。時間計測は Time.unscaledDeltaTime を使用します。
    /// </summary>
    /// <param name="duration">実時間でのシェイク秒数</param>
    /// <param name="magnitude">揺れ幅（ジャストパリィ 0.85 / ジャストステップ 0.55 想定）</param>
    /// <param name="isParry">ジャストパリィ由来か（ログ用）</param>
    private IEnumerator TriggerCameraShake(float duration, float magnitude, bool isParry)
    {
        Camera lensCamera = ResolveMainCamera();
        Transform shakeTransform = ResolveCameraShakeTransform(lensCamera);
        if (shakeTransform == null)
        {
            Debug.LogWarning("[CombatActionFeedbackManager] カメラシェイク対象が見つからないため演出をスキップしました。");
            cameraShakeCoroutine = null;
            yield break;
        }

        cameraShakeTarget = lensCamera;
        cameraShakeTransform = shakeTransform;
        cameraShakeOriginalLocalPosition = shakeTransform.localPosition;
        cameraShakeOriginalFov = lensCamera != null ? lensCamera.fieldOfView : 60f;
        float zoomedFov = Mathf.Max(20f, cameraShakeOriginalFov - cameraZoomFovDelta);

        string contextLabel = isParry ? "ジャストパリィ" : "ジャストステップ";
        Debug.Log(
            $"<color=#B388FF><b>【カメラ演出 · 空間破砕シェイク】</b></color> " +
            $"{contextLabel} — 揺れ幅 {magnitude:F2} / 実時間 {duration:F2}s");

        float clampedDuration = Mathf.Max(0.01f, duration);
        float clampedMagnitude = Mathf.Max(0f, magnitude);
        float elapsed = 0f;

        while (elapsed < clampedDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float normalized = Mathf.Clamp01(elapsed / clampedDuration);
            float shakeFalloff = 1f - normalized;
            float offsetX = UnityEngine.Random.Range(-1f, 1f) * clampedMagnitude * shakeFalloff;
            float offsetY = UnityEngine.Random.Range(-1f, 1f) * clampedMagnitude * shakeFalloff;
            shakeTransform.localPosition = cameraShakeOriginalLocalPosition + new Vector3(offsetX, offsetY, 0f);

            if (lensCamera != null)
            {
                float zoomPulse = normalized < 0.35f
                    ? Mathf.SmoothStep(0f, 1f, normalized / 0.35f)
                    : Mathf.SmoothStep(1f, 0f, (normalized - 0.35f) / 0.65f);
                lensCamera.fieldOfView = Mathf.Lerp(cameraShakeOriginalFov, zoomedFov, zoomPulse);
            }

            yield return null;
        }

        RestoreCameraAfterShake();
        cameraShakeCoroutine = null;
    }

    /// <summary>ヒットストップ後に Time.timeScale を安全に復元します。</summary>
    private void RestoreTimeScaleAfterHitstop(bool force)
    {
        if (!force && activeHitstopCount > 0)
        {
            return;
        }

        activeHitstopCount = 0;
        float restore = hitstopRestoreTimeScale;
        if (restore <= 0f)
        {
            restore = 1f;
        }

        Time.timeScale = restore;
    }

    /// <summary>カメラシェイク後にローカル座標と FOV を元へ戻します。</summary>
    private void RestoreCameraAfterShake()
    {
        if (cameraShakeTransform != null)
        {
            cameraShakeTransform.localPosition = cameraShakeOriginalLocalPosition;
            cameraShakeTransform = null;
        }

        if (cameraShakeTarget != null)
        {
            cameraShakeTarget.fieldOfView = cameraShakeOriginalFov;
            cameraShakeTarget = null;
        }
    }

    /// <summary>メインカメラを解決します（Camera.main → シーン内最初の Camera）。</summary>
    private static Camera ResolveMainCamera()
    {
        if (Camera.main != null)
        {
            return Camera.main;
        }

        return FindAnyObjectByType<Camera>();
    }

    /// <summary>
    /// カメラシェイクを適用する Transform を解決します。
    /// ThirdPersonController の CinemachineCameraTarget を優先し、無ければレンズ Transform を使います。
    /// </summary>
    /// <param name="lensCamera">FOV 制御用のレンズカメラ</param>
    private Transform ResolveCameraShakeTransform(Camera lensCamera)
    {
        if (playerTransform != null)
        {
            StarterAssets.ThirdPersonController thirdPerson =
                playerTransform.GetComponentInChildren<StarterAssets.ThirdPersonController>();
            if (thirdPerson != null && thirdPerson.CinemachineCameraTarget != null)
            {
                return thirdPerson.CinemachineCameraTarget.transform;
            }
        }

        StarterAssets.ThirdPersonController fallbackThirdPerson =
            FindAnyObjectByType<StarterAssets.ThirdPersonController>();
        if (fallbackThirdPerson != null && fallbackThirdPerson.CinemachineCameraTarget != null)
        {
            return fallbackThirdPerson.CinemachineCameraTarget.transform;
        }

        return lensCamera != null ? lensCamera.transform : null;
    }
}

/// <summary>Play 開始時に戦闘フィードバック系を DebugSystemsHub へ配置します。</summary>
public static class CombatActionFeedbackBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AttachToDebugSystemsHub()
    {
        GameObject hub = GameObject.Find("DebugSystemsHub");
        if (hub == null)
        {
            return;
        }

        if (hub.GetComponent<CombatActionFeedbackManager>() == null)
        {
            hub.AddComponent<CombatActionFeedbackManager>();
        }

        if (hub.GetComponent<CombatActionTester>() == null)
        {
            hub.AddComponent<CombatActionTester>();
        }

        VisibleEnemySpawnBootstrap.AttachToDebugSystemsHub();
    }
}

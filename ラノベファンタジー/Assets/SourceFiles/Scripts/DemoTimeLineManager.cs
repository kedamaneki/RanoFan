using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

// =============================================================================
// デモ進行管理：戦闘 → 工房ブラインド生産 → 例外パケット出力を一本化
// 連携: CombatActionFeedbackManager / InventoryManager / CraftingExperimentHub
//       CraftingExceptionCollector / InGameVisualUIManager / ChronosCoordinateHub
//       DetailedCraftingProcessManager
// =============================================================================

/// <summary>デモタイムラインの進行フェーズ（レガシー互換。新規は DemoState を使用）。</summary>
public enum DemoPhase
{
    /// <summary>デモ未開始または終了後の待機状態。</summary>
    Idle,

    /// <summary>第1弾：フロム式戦闘フェーズ。</summary>
    CombatPhase,

    /// <summary>第2弾：拠点工房でのブラインド生産・スキル融合フェーズ。</summary>
    WorkshopPhase,

    /// <summary>第3弾：例外データ回収とデモ終了演出フェーズ。</summary>
    ResultPhase
}

/// <summary>デモ進行の中央集権ステート。</summary>
public enum DemoState
{
    /// <summary>PlayerRobot が敵と対峙し、マウスホイール＋左クリックで戦うフェーズ。</summary>
    BattlePhase,

    /// <summary>戦闘終了後、工房へ切り替える暗転・データ移行フェーズ（全入力遮断）。</summary>
    TransitionPhase,

    /// <summary>安全圏で 1〜5 キーによりリアル科学パラメータを操作する生産フェーズ。</summary>
    CraftingPhase,

    /// <summary>Enter による品質ジャッジと例外 JSON 射出後のデモ終了フェーズ。</summary>
    ResultPhase
}

/// <summary>ベース継承型スキル融合の決定論的派生結果。</summary>
public sealed class DemoDerivedSkill
{
    /// <summary>派生スキルの内部 ID。</summary>
    public string skillId;

    /// <summary>表示名（例: 火炎斬）。</summary>
    public string displayName;

    /// <summary>素材スキル A の ID。</summary>
    public string parentSkillA;

    /// <summary>素材スキル B の ID。</summary>
    public string parentSkillB;

    /// <summary>継承属性タグ（例: Slash, Fire）。</summary>
    public string[] inheritedTags;

    /// <summary>基礎攻撃力補正。</summary>
    public float basePower;

    /// <summary>説明文。</summary>
    public string description;
}

/// <summary>
/// ゲーム全体のフェーズ遷移を司るデモステート・コア。
/// 戦闘・生産・結果のステートを中央管理し、入力ゲートと連動します。
/// </summary>
[DefaultExecutionOrder(-100)]
public partial class DemoTimeLineManager : MonoBehaviour
{
    public const string InstantFieldBladeItemId = "demo_instant_field_blade";

    public static DemoTimeLineManager Instance { get; private set; }

    [Header("デモ座標（未設定時は PlayerRobot 基準のオフセット）")]
    [SerializeField] private Vector3 combatAreaOffset = new Vector3(6f, 0f, 4f);
    [SerializeField] private Vector3 workshopAreaOffset = new Vector3(-5f, 0f, -3f);
    [Tooltip("工房テレポート先（XZ・向きの目安。Y は足元接地レイで自動補正されます）")]
    [SerializeField] private Transform workshopSpawnPoint;

    private static readonly string[] WorkshopSpawnPointCandidateNames =
    {
        "WorkshopSpawnPoint",
        "SpawnPoint_1",
    };

    [Header("自動リレー待機（秒）")]
    [SerializeField] private float autoRelayCombatIntroSeconds = 1.2f;
    [SerializeField] private float autoRelayBetweenParrySeconds = 0.55f;
    [SerializeField] private float autoRelayWorkshopIntroSeconds = 1.5f;
    [SerializeField] private float autoRelayCraftBeatSeconds = 0.8f;

    [Header("生産フェーズ既定職種")]
    [SerializeField] private string craftingPhaseDefaultCraftType = DetailedCraftingProcessManager.CraftTypeForge;

    [Header("戦闘撃破 → 工房移行")]
    [SerializeField] private float battleToCraftFadeOutSeconds = 1f;
    [SerializeField] private float battleToCraftFadeInSeconds = 0.4f;

    [Header("生産フェーズ敵脅威（工房ハラスメント）")]
    [SerializeField] private float craftingPhaseHarassmentDelaySeconds = 100f;
    [Tooltip("脅威再開（追跡開始）後、初めて攻撃可能になるまでの追加猶予（秒）")]
    [SerializeField] private float craftingHarassmentFirstAttackDelaySeconds = 10f;
    [SerializeField] private Vector3 craftingPhaseEnemySpawnOffset = new Vector3(8f, 0f, 6f);

    [Header("中央入力（ステート別インターセプト）")]
    [SerializeField] private float mouseScrollThreshold = 0.01f;

    private static readonly Dictionary<string, DemoDerivedSkill> FusionRecipeTable =
        BuildFusionRecipeTable();

    private CombatActionFeedbackManager combatFeedback;
    private InventoryManager inventoryManager;
    private CraftingExperimentHub craftingHub;
    private CraftingExceptionCollector exceptionCollector;
    private InGameVisualUIManager visualUi;
    private PlayerStatusManager statusManager;

    private DetailedCraftingProcessManager detailedCraftingProcess;
    private CraftingStatusManager craftingStatusManager;

    private Coroutine autoRelayCoroutine;
    private Coroutine autoCombatAssistCoroutine;
    private Coroutine battleToCraftTransitionCoroutine;
    private Coroutine craftingHarassmentCoroutine;
    private Coroutine craftingHarassmentAttackLoopCoroutine;
    private GameObject spawnedEnemyMarker;
    private bool combatClearedNotified;
    private bool isAutoRelayActive;
    private bool hasAutoStartedBattle;
    private bool isCraftingFinalizationInProgress;
    private bool craftingPhaseHarassmentArmed;
    private bool isRiskVerificationSuppressingAutoTransitions;

    /// <summary>現在のデモステート（中央集権の進行状態）。</summary>
    public DemoState CurrentState { get; private set; } = DemoState.BattlePhase;

    /// <summary>現在のデモフェーズ（レガシー互換プロパティ）。</summary>
    public DemoPhase CurrentPhase => MapStateToLegacyPhase(CurrentState);

    /// <summary>全自動デモリレーが進行中か。</summary>
    public bool IsAutoRelayActive => isAutoRelayActive;

    /// <summary>
    /// 生産フェーズで被弾バーストが有効か（入場直後の猶予時間中は false）。
    /// </summary>
    public bool IsCraftingDamageInterruptAllowed =>
        CurrentState == DemoState.CraftingPhase && craftingPhaseHarassmentArmed;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[DemoTimeLineManager] 重複インスタンスを検出しました。");
            Destroy(this);
            return;
        }

        Instance = this;
        CacheReferences();
        TryResolveWorkshopSpawnPoint();
    }

    private void OnEnable()
    {
        CombatActionFeedbackManager combat = CombatActionFeedbackManager.Instance
            ?? CombatActionFeedbackManager.EnsureInstance();
        combat.OnEnemyPostureBroken += HandleEnemyPostureBroken;
    }

    private void OnDisable()
    {
        if (CombatActionFeedbackManager.Instance != null)
        {
            CombatActionFeedbackManager.Instance.OnEnemyPostureBroken -= HandleEnemyPostureBroken;
        }
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    /// <summary>シーンに無い場合は DebugSystemsHub へ動的生成して返します。</summary>
    public static DemoTimeLineManager EnsureInstance()
    {
        if (Instance != null)
        {
            return Instance;
        }

        GameObject hub = GameObject.Find("DebugSystemsHub");
        if (hub != null)
        {
            DemoTimeLineManager existing = hub.GetComponent<DemoTimeLineManager>();
            return existing != null ? existing : hub.AddComponent<DemoTimeLineManager>();
        }

        return new GameObject(nameof(DemoTimeLineManager)).AddComponent<DemoTimeLineManager>();
    }

    private void Start()
    {
        if (!isAutoRelayActive && !hasAutoStartedBattle)
        {
            StartCoroutine(AutoBeginBattleOnPlayRoutine());
        }
    }

    /// <summary>
    /// デモ全体の入力を中央集権的に処理します。戦闘・生産以外のフェーズでは操作を遮断します。
    /// </summary>
    private void Update()
    {
        if (WasTownSafetyToggleKeyPressed())
        {
            TownSafetyZoneGate.ToggleInsideTown();
            Debug.Log(TownSafetyZoneGate.BuildStatusRichText());
            return;
        }

        CacheReferences();
        detailedCraftingProcess?.SyncSessionFromCraftingHub();

        switch (CurrentState)
        {
            case DemoState.BattlePhase:
                ProcessBattlePhaseInput();
                break;
            case DemoState.CraftingPhase:
                ProcessCraftingPhaseInput();
                break;
            case DemoState.TransitionPhase:
            case DemoState.ResultPhase:
                break;
        }
    }

    /// <summary>BattlePhase 専用：マウス戦闘・パリィ・ステップ・デバッグ敵攻撃（T）のみ受け付けます。</summary>
    private void ProcessBattlePhaseInput()
    {
        combatFeedback ??= CombatActionFeedbackManager.EnsureInstance();
        if (combatFeedback == null)
        {
            return;
        }

        if (WasDebugEnemyAttackKeyPressed())
        {
            TriggerDebugEnemyAttack();
            return;
        }

        if (WasParryKeyPressed())
        {
            combatFeedback.AttemptParry();
            return;
        }

        if (WasStepEvadeKeyPressed())
        {
            combatFeedback.AttemptStepEvade();
            return;
        }

        ProcessBattleMouseInput();
    }

    /// <summary>マウスホイールで攻撃属性を切り替え、左クリックで選択中攻撃を発動します。</summary>
    private void ProcessBattleMouseInput()
    {
        if (StarterAssets.StarterAssetsInputs.IsUiModeActive)
        {
            return;
        }

        combatFeedback ??= CombatActionFeedbackManager.EnsureInstance();
        if (combatFeedback == null)
        {
            return;
        }

        float scrollDelta = ReadMouseScrollDelta();
        if (scrollDelta > mouseScrollThreshold)
        {
            combatFeedback.CycleSelectedAttackType(1);
        }
        else if (scrollDelta < -mouseScrollThreshold)
        {
            combatFeedback.CycleSelectedAttackType(-1);
        }

        if (WasLeftMouseButtonPressedThisFrame())
        {
            combatFeedback.PerformPlayerAttack(combatFeedback.CurrentSelectedAttackType);
        }
    }

    /// <summary>CraftingPhase 専用：統一 1〜5 キーと Enter のみ受け付けます（攻撃入力は遮断）。</summary>
    private void ProcessCraftingPhaseInput()
    {
        if (!DemoInputGate.IsCraftingHotkeyInputAllowed())
        {
            return;
        }

        detailedCraftingProcess ??= DetailedCraftingProcessManager.EnsureInstance();
        if (detailedCraftingProcess == null || !detailedCraftingProcess.IsSessionActive)
        {
            return;
        }

        if (WasEnterKeyPressed())
        {
            FinishCraftingAndEnterResult();
            return;
        }

        for (int slot = 1; slot <= CraftingStatusManager.UnifiedCraftSlotCount; slot++)
        {
            if (!WasDigitKeyPressed(slot))
            {
                continue;
            }

            detailedCraftingProcess.ExecuteUnifiedCraftSlot(slot);
            return;
        }
    }

    /// <summary>デバッグ用：T キーで VisibleEnemyAI の乱数ディレイ攻撃を誘発します。</summary>
    private void TriggerDebugEnemyAttack()
    {
        combatFeedback ??= CombatActionFeedbackManager.EnsureInstance();
        if (combatFeedback == null)
        {
            return;
        }

        combatFeedback.TriggerEnemyAttack();
        EnemyAttackProfile profile = combatFeedback.currentEnemyProfile;
        if (profile != null)
        {
            Debug.Log(
                $"<color=#FF8A65><b>[DemoTimeLineManager]</b> Tキー（デバッグ）: " +
                $"{profile.enemyName} の攻撃開始（ディレイ {profile.minDelay:F2}〜{profile.maxDelay:F2}s）</color>");
        }
    }

    /// <summary>マウスホイールのスクロール量を取得します（レガシー Input と新 Input System の両対応）。</summary>
    private static float ReadMouseScrollDelta()
    {
        if (DemoInputTestInjector.TryConsumeScroll(out float injectedScroll))
        {
            return injectedScroll;
        }

#if ENABLE_LEGACY_INPUT_MANAGER
        float legacyScroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(legacyScroll) > 0.0001f)
        {
            return legacyScroll;
        }
#endif

        if (Mouse.current == null)
        {
            return 0f;
        }

        Vector2 scroll = Mouse.current.scroll.ReadValue();
        if (Mathf.Abs(scroll.y) < 0.01f)
        {
            return 0f;
        }

        return scroll.y > 0f ? 0.1f : -0.1f;
    }

    /// <summary>マウス左ボタンがこのフレームで押されたかを取得します。</summary>
    private static bool WasLeftMouseButtonPressedThisFrame()
    {
        if (DemoInputTestInjector.TryConsumeLeftClick())
        {
            return true;
        }

#if ENABLE_LEGACY_INPUT_MANAGER
        if (Input.GetMouseButtonDown(0))
        {
            return true;
        }
#endif

        return Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
    }

    /// <summary>統一数字キー 1〜5 がこのフレームで押されたかを取得します。</summary>
    private static bool WasDigitKeyPressed(int digit1To5)
    {
        if (DemoInputTestInjector.TryConsumeDigit(digit1To5))
        {
            return true;
        }

#if ENABLE_LEGACY_INPUT_MANAGER
        KeyCode legacyCode = digit1To5 switch
        {
            1 => KeyCode.Alpha1,
            2 => KeyCode.Alpha2,
            3 => KeyCode.Alpha3,
            4 => KeyCode.Alpha4,
            5 => KeyCode.Alpha5,
            _ => KeyCode.None
        };

        if (legacyCode != KeyCode.None && Input.GetKeyDown(legacyCode))
        {
            return true;
        }
#endif

        return digit1To5 switch
        {
            1 => DebugHotkeyUtility.WasPressed(Key.Digit1),
            2 => DebugHotkeyUtility.WasPressed(Key.Digit2),
            3 => DebugHotkeyUtility.WasPressed(Key.Digit3),
            4 => DebugHotkeyUtility.WasPressed(Key.Digit4),
            5 => DebugHotkeyUtility.WasPressed(Key.Digit5),
            _ => false
        };
    }

    /// <summary>Enter キーがこのフレームで押されたかを取得します。</summary>
    private static bool WasEnterKeyPressed()
    {
        if (DemoInputTestInjector.TryConsumeEnter())
        {
            return true;
        }

#if ENABLE_LEGACY_INPUT_MANAGER
        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
        {
            return true;
        }
#endif

        return DebugHotkeyUtility.WasPressed(Key.Enter);
    }

    /// <summary>パリィ（F）キーがこのフレームで押されたかを取得します。</summary>
    private static bool WasParryKeyPressed()
    {
        if (DemoInputTestInjector.TryConsumeParry())
        {
            return true;
        }

#if ENABLE_LEGACY_INPUT_MANAGER
        if (Input.GetKeyDown(KeyCode.F))
        {
            return true;
        }
#endif

        return DebugHotkeyUtility.WasPressed(Key.F);
    }

    /// <summary>ステップ回避（Left Ctrl / Right Ctrl）キーがこのフレームで押されたかを取得します。</summary>
    private static bool WasStepEvadeKeyPressed()
    {
        if (DemoInputTestInjector.TryConsumeStepEvade())
        {
            return true;
        }

#if ENABLE_LEGACY_INPUT_MANAGER
        if (Input.GetKeyDown(KeyCode.LeftControl) || Input.GetKeyDown(KeyCode.RightControl))
        {
            return true;
        }
#endif

        return DebugHotkeyUtility.WasPressed(Key.LeftCtrl) ||
               DebugHotkeyUtility.WasPressed(Key.RightCtrl);
    }

    /// <summary>デバッグ敵攻撃（T）キーがこのフレームで押されたかを取得します。</summary>
    private static bool WasDebugEnemyAttackKeyPressed()
    {
#if ENABLE_LEGACY_INPUT_MANAGER
        if (Input.GetKeyDown(KeyCode.T))
        {
            return true;
        }
#endif

        return DebugHotkeyUtility.WasPressed(Key.T);
    }

    /// <summary>町の中判定トグル（\\）キーがこのフレームで押されたかを取得します。</summary>
    private static bool WasTownSafetyToggleKeyPressed()
    {
#if ENABLE_LEGACY_INPUT_MANAGER
        if (Input.GetKeyDown(KeyCode.Backslash))
        {
            return true;
        }
#endif

        return DebugHotkeyUtility.WasPressed(Key.Backslash);
    }

    /// <summary>Play 開始後1フレーム待ってから戦闘フェーズへ自動遷移します。</summary>
    private IEnumerator AutoBeginBattleOnPlayRoutine()
    {
        yield return null;
        CacheReferences();

        if (hasAutoStartedBattle || isAutoRelayActive)
        {
            yield break;
        }

        hasAutoStartedBattle = true;
        TransitionTo(DemoState.BattlePhase);
        BeginBattleWave();
    }

    /// <summary>
    /// デモステートを遷移させ、フェーズ開始時のお約束処理と遷移ログを実行します。
    /// </summary>
    /// <param name="newState">遷移先ステート</param>
    public void TransitionTo(DemoState newState)
    {
        if (CurrentState == newState)
        {
            return;
        }

        DemoState previousState = CurrentState;
        CurrentState = newState;

        Debug.Log(
            "<b><color=#5DADE2>【システム遷移】フェーズが \"" +
            $"{FormatStateLabel(previousState)}\" ➔ \"{FormatStateLabel(newState)}\" へ移行しました。</color></b>");

        OnEnterState(newState);
    }

    /// <summary>ステート入場時の自動処理を実行します。</summary>
    private void OnEnterState(DemoState entered)
    {
        CacheReferences();

        switch (entered)
        {
            case DemoState.BattlePhase:
                EnterBattlePhaseSetup();
                break;
            case DemoState.TransitionPhase:
                EnterTransitionPhaseSetup();
                break;
            case DemoState.CraftingPhase:
                EnterCraftingPhaseSetup();
                break;
            case DemoState.ResultPhase:
                EnterResultPhaseSetup();
                break;
        }

        visualUi ??= InGameVisualUIManager.EnsureInstance();
        visualUi.UpdateDemoPhaseBanner(entered);
    }

    /// <summary>戦闘フェーズ入場時：詳細クラフトフラグを解除し、武器構えログと操作ナビを出力します。</summary>
    private void EnterBattlePhaseSetup()
    {
        ResetBattleTransitionGuardsForBattleEntry();
        TownSafetyZoneGate.SetInsideTown(false);

        CombatActionFeedbackManager combat = combatFeedback
            ?? CombatActionFeedbackManager.EnsureInstance();
        combat?.NotifyDetailedCraftingFinished();
        LogBattlePhaseNavigation();

        PlayerStatusManager playerStatus = PlayerStatusManager.Instance;
        if (playerStatus == null)
        {
            playerStatus = FindAnyObjectByType<PlayerStatusManager>();
        }

        playerStatus?.ApplyMainJobStatModifiers();

        CombatStats playerCombat = playerStatus != null
            ? playerStatus.GetComponent<CombatStats>()
            : null;
        if (playerCombat == null)
        {
            GameObject playerRobot = GameObject.Find("PlayerRobot");
            playerCombat = playerRobot != null ? playerRobot.GetComponent<CombatStats>() : null;
        }

        ItemData equippedWeapon = InventoryManager.Instance?.EquippedCraftedWeapon;
        if (equippedWeapon == null)
        {
            playerCombat?.ApplyEquipmentModifiers(null, logUnequipped: true);
        }

        EquipmentStatFeedback.RunFormulaSelfCheck();
        VerifyBattleEquipmentFeedback(playerCombat, equippedWeapon);
        combat?.LogCurrentWeaponStance();
    }

    /// <summary>装備還元が STR / 倍率に乗ったかをコンソールで照合します。</summary>
    private static void VerifyBattleEquipmentFeedback(CombatStats playerCombat, ItemData equippedWeapon)
    {
        EquipmentModifierValues expected = EquipmentStatFeedback.Calculate(equippedWeapon);
        if (playerCombat == null)
        {
            Debug.LogWarning("[装備還元・検証] プレイヤー CombatStats 未解決のため照合をスキップしました。");
            return;
        }

        bool strMatch = playerCombat.EquipmentStrengthBonus == expected.StrengthBonus;
        bool poiseMatch = Mathf.Abs(playerCombat.EquipmentPoiseDamageMultiplier - expected.PoiseDamageMultiplier) < 0.001f;
        bool stamMatch = Mathf.Abs(playerCombat.EquipmentStaminaCostMultiplier - expected.StaminaCostMultiplier) < 0.001f;
        if (strMatch && poiseMatch && stamMatch)
        {
            Debug.Log(
                $"<color=#A5D6A7>【装備還元・検証】BattlePhase 反映 OK — " +
                $"STR={playerCombat.Strength} (装備+{playerCombat.EquipmentStrengthBonus}) " +
                $"体勢×{playerCombat.EquipmentPoiseDamageMultiplier:F2} " +
                $"スタミナ×{playerCombat.EquipmentStaminaCostMultiplier:F2}</color>");
            return;
        }

        Debug.LogError(
            "[装備還元・検証] BattlePhase の CombatStats が算出値と不一致 " +
            $"STR+ {playerCombat.EquipmentStrengthBonus}/{expected.StrengthBonus} " +
            $"poise {playerCombat.EquipmentPoiseDamageMultiplier:F2}/{expected.PoiseDamageMultiplier:F2} " +
            $"stam {playerCombat.EquipmentStaminaCostMultiplier:F2}/{expected.StaminaCostMultiplier:F2}");
    }

    /// <summary>遷移フェーズ入場時：自動戦闘アシストを停止します。</summary>
    private void EnterTransitionPhaseSetup()
    {
        StopAutoCombatAssist();
    }

    /// <summary>
    /// 生産フェーズ入場時：職種を自動セットし、裏パラメータとこだわり工程セッションを初期化します。
    /// </summary>
    private void EnterCraftingPhaseSetup()
    {
        TownSafetyZoneGate.SetInsideTown(true);

        string craftType = ResolveCraftingPhaseCraftType();
        detailedCraftingProcess ??= DetailedCraftingProcessManager.EnsureInstance();
        craftingStatusManager ??= CraftingStatusManager.EnsureInstance();

        CraftingExperimentHub hub = craftingHub ?? CraftingExperimentHub.EnsureInstance();
        hub.ApplyDemoCraftingPhaseThreatMode();
        if (!hub.IsActive)
        {
            bool isAlch = string.Equals(
                craftType,
                DetailedCraftingProcessManager.CraftTypeAlch,
                StringComparison.OrdinalIgnoreCase);
            CraftProfessionType profession = isAlch ? CraftProfessionType.Alch : CraftProfessionType.Forge;
            hub.StartCrafting(profession.ToString(), isSafetyZone: false);
        }
        else
        {
            hub.MarkAsUnderThreatZone();
        }

        if (craftingStatusManager != null)
        {
            craftingStatusManager.ResetStatus(craftType);
        }

        if (detailedCraftingProcess != null)
        {
            detailedCraftingProcess.BeginSession(craftType);
        }

        combatFeedback ??= CombatActionFeedbackManager.EnsureInstance();
        combatFeedback?.HaltEnemyCombatForCraftingPhaseEntry();
        RefreshPlayerVitalsForCraftingEntrance();

        StopCraftingPhaseHarassmentSchedule();
        craftingPhaseHarassmentArmed = false;
        PrepareCraftingPhaseEnemyHarassment(deferAttack: true);
        craftingHarassmentCoroutine = StartCoroutine(ArmCraftingPhaseHarassmentRoutine());

        LogCraftingPhaseNavigation(craftType);

        visualUi ??= InGameVisualUIManager.EnsureInstance();
        visualUi.SetCraftingGuideVisible(true);
    }

    /// <summary>結果フェーズ入場時：自動リレーと戦闘・生産コルーチンを安全停止します。</summary>
    private void EnterResultPhaseSetup()
    {
        isAutoRelayActive = false;
        StopAutoCombatAssist();
        StopBattleToCraftTransitionIfActive();
        StopCraftingPhaseHarassmentSchedule();
        CleanupCraftingPhaseCombatMotion();

        visualUi ??= InGameVisualUIManager.EnsureInstance();
        visualUi.SetCraftingGuideVisible(false);
    }

    /// <summary>
    /// 生産フェーズで敵を工房付近へ再配置します。入場直後は追跡・攻撃を抑止できます。
    /// </summary>
    /// <param name="deferAttack">true のとき即時攻撃を開始せず、猶予後に別途武装します。</param>
    private void PrepareCraftingPhaseEnemyHarassment(bool deferAttack)
    {
        CacheReferences();

        Vector3 harassPosition = ResolveWorkshopPosition() + craftingPhaseEnemySpawnOffset;

        VisibleEnemyAI enemy = FindAnyObjectByType<VisibleEnemyAI>();
        if (enemy == null)
        {
            SpawnDemoEnemyMarker(harassPosition);
            enemy = FindAnyObjectByType<VisibleEnemyAI>();
        }
        else
        {
            enemy.transform.position = harassPosition;
            enemy.SnapFeetToGround();
            enemy.RecaptureHomeTransform();
            enemy.FacePlayer();
            combatFeedback?.RegisterVisibleEnemy(enemy);
        }

        if (enemy != null)
        {
            enemy.PrepareForCraftingHarassmentEntry(deferAttack);
        }

        if (combatFeedback == null)
        {
            return;
        }

        EnemyAttackProfile harassProfile = EnemyAttackProfile.CreateForestSlimeMob();
        combatFeedback.SetTargetEnemy(harassProfile);

        if (deferAttack)
        {
            LogNav(
                $"<color=#FFCC80>生産フェーズ：敵を工房付近（{craftingPhaseEnemySpawnOffset}）へ再配置。" +
                $"<b>{craftingPhaseHarassmentDelaySeconds:F0}秒</b> の猶予後に脅威が再開します。</color>");
            return;
        }

        combatFeedback.TriggerEnemyAttack();
        LogNav(
            "<color=#FF8A65>生産フェーズ：敵が工房付近で再出現。被弾すると投入素材がバーストします。</color>");
    }

    /// <summary>生産フェーズ入場後、猶予時間経過で敵脅威（追跡・攻撃）を再武装します。</summary>
    private IEnumerator ArmCraftingPhaseHarassmentRoutine()
    {
        float waitSeconds = Mathf.Max(0f, craftingPhaseHarassmentDelaySeconds);
        if (waitSeconds > 0f)
        {
            yield return new WaitForSecondsRealtime(waitSeconds);
        }

        craftingHarassmentCoroutine = null;

        if (CurrentState != DemoState.CraftingPhase)
        {
            yield break;
        }

        craftingPhaseHarassmentArmed = true;

        VisibleEnemyAI enemy = FindAnyObjectByType<VisibleEnemyAI>();
        enemy?.ActivateCraftingHarassmentChaseOnly();

        StopCraftingHarassmentAttackEnableSchedule();
        craftingHarassmentAttackLoopCoroutine =
            StartCoroutine(EnableCraftingHarassmentAttacksAfterDelayRoutine());

        float firstAttackDelay = Mathf.Max(0f, craftingHarassmentFirstAttackDelaySeconds);
        LogNav(
            "<color=#FF8A65><b>生産フェーズ · 敵脅威再開</b></color> " +
            $"<color=#FFAB91>スライムが工房付近を徘徊し始めました。" +
            $" <b>{firstAttackDelay:F0}秒</b> 後から攻撃可能になります（被弾でバースト）。</color>");
    }

    /// <summary>進行中の生産フェーズ敵脅威スケジュールを停止します。</summary>
    private void StopCraftingPhaseHarassmentSchedule()
    {
        craftingPhaseHarassmentArmed = false;

        if (craftingHarassmentCoroutine != null)
        {
            StopCoroutine(craftingHarassmentCoroutine);
            craftingHarassmentCoroutine = null;
        }

        StopCraftingHarassmentAttackEnableSchedule();
    }

    /// <summary>初攻撃武装コルーチンを停止します。</summary>
    private void StopCraftingHarassmentAttackEnableSchedule()
    {
        if (craftingHarassmentAttackLoopCoroutine == null)
        {
            return;
        }

        StopCoroutine(craftingHarassmentAttackLoopCoroutine);
        craftingHarassmentAttackLoopCoroutine = null;
    }

    /// <summary>脅威再開後、追跡のみ→猶予経過後に自動攻撃を許可します。</summary>
    private IEnumerator EnableCraftingHarassmentAttacksAfterDelayRoutine()
    {
        float delay = Mathf.Max(0f, craftingHarassmentFirstAttackDelaySeconds);
        if (delay > 0f)
        {
            yield return new WaitForSecondsRealtime(delay);
        }

        craftingHarassmentAttackLoopCoroutine = null;

        if (CurrentState != DemoState.CraftingPhase || !craftingPhaseHarassmentArmed)
        {
            yield break;
        }

        VisibleEnemyAI enemy = FindAnyObjectByType<VisibleEnemyAI>();
        enemy?.EnableCraftingHarassmentAutoAttack();

        LogNav(
            "<color=#FFAB91><b>生産フェーズ · 敵攻撃解禁</b></color> " +
            "<color=#FF8A65>スライムが射程内に入ると攻撃を仕掛けます。被弾に注意。</color>");
    }

    /// <summary>生産フェーズで使用する職種（Forge / Alch）を解決します。</summary>
    private string ResolveCraftingPhaseCraftType()
    {
        if (craftingHub != null && craftingHub.IsActive)
        {
            return craftingHub.CurrentProfession == CraftProfessionType.Alch
                ? DetailedCraftingProcessManager.CraftTypeAlch
                : DetailedCraftingProcessManager.CraftTypeForge;
        }

        if (!string.IsNullOrWhiteSpace(craftingPhaseDefaultCraftType))
        {
            return craftingPhaseDefaultCraftType;
        }

        return DetailedCraftingProcessManager.CraftTypeForge;
    }

    /// <summary>
    /// CraftingPhase 中に Enter が押された際、または熱科学バースト検知時に
    /// 品質ジャッジ・例外 JSON 射出後 ResultPhase へ着地します。
    /// </summary>
    public void FinishCraftingAndEnterResult()
    {
        if (isCraftingFinalizationInProgress)
        {
            return;
        }

        if (CurrentState != DemoState.CraftingPhase)
        {
            Debug.LogWarning(
                $"[DemoTimeLineManager] FinishCraftingAndEnterResult は CraftingPhase 専用です（現在: {CurrentState}）。");
            return;
        }

        CacheReferences();
        detailedCraftingProcess ??= DetailedCraftingProcessManager.EnsureInstance();
        craftingStatusManager ??= CraftingStatusManager.EnsureInstance();

        if (detailedCraftingProcess == null || craftingStatusManager == null)
        {
            Debug.LogError("[DemoTimeLineManager] 生産ジャッジに必要なマネージャが見つかりません。");
            return;
        }

        DemoCraftingJudgmentResult judgment = craftingStatusManager.EvaluateDemoCraftingJudgment(
            detailedCraftingProcess.currentProcessLogs.Count);
        if (!judgment.IsThermalBurstFailure && detailedCraftingProcess.currentProcessLogs.Count == 0)
        {
            Debug.LogWarning(
                "[DemoTimeLineManager] 工程履歴が空のため Result へ遷移できません。1〜5 で操作してください。");
            return;
        }

        ExecuteCraftingFinalJudgmentAndLandResult(judgment);
    }

    /// <summary>
    /// 品質評価・コンソールログ・例外 JSON パケット射出・クリーンアップ後に ResultPhase へ遷移します。
    /// </summary>
    /// <param name="judgment">EvaluateDemoCraftingJudgment の結果</param>
    private void ExecuteCraftingFinalJudgmentAndLandResult(DemoCraftingJudgmentResult judgment)
    {
        isCraftingFinalizationInProgress = true;

        try
        {
            CacheReferences();

            string craftType = ResolveActiveCraftTypeForJudgment(judgment);
            List<string> artisanLogs = new List<string>(detailedCraftingProcess.currentProcessLogs);
            CraftingResultItemSnapshot resultSnapshot = craftingStatusManager.FinalizeCraftingResultForPacket();
            List<string> finalParameters = craftingStatusManager.BuildStatusSnapshotLines(craftType);

            if (!judgment.IsThermalBurstFailure)
            {
                craftingStatusManager.TryDeliverCraftedResultToInventory(
                    resultSnapshot,
                    judgment.IsThermalBurstFailure);
            }

            EmitDemoResultBanner(judgment, craftType, artisanLogs.Count, finalParameters);
            EmitCraftingJudgmentConsoleLog(judgment);

            CraftingQualityPacket packet = CraftingQualityPacketEmitter.BuildPacket(
                craftType,
                judgment.FinalScore,
                finalParameters,
                artisanLogs,
                resultSnapshot);
            CraftingQualityPacketEmitter.EmitToConsole(packet);

            CleanupCraftingPhaseForResultLanding(craftType);

            TransitionTo(DemoState.ResultPhase);

            LogNav(
                "<color=#FFD54F><b>══ RESULT PHASE ══</b></color>\n" +
                "<color=#FFF59D>品質ジャッジ完了・例外 JSON を射出しました。デモは終了フェーズです。</color>");
        }
        finally
        {
            isCraftingFinalizationInProgress = false;
        }
    }

    /// <summary>JSON 射出直前に、成否・スコア・理由を一目で把握できる境界ログを出力します。</summary>
    private static void EmitDemoResultBanner(
        DemoCraftingJudgmentResult judgment,
        string craftType,
        int artisanLogCount,
        List<string> finalParameters)
    {
        if (judgment == null)
        {
            return;
        }

        string outcome = judgment.IsThermalBurstFailure
            ? "<color=#FF5252><b>OUTCOME: THERMAL BURST（10点）</b></color>"
            : "<color=#69F0AE><b>OUTCOME: NORMAL SUCCESS（60〜100点）</b></color>";

        string reasonLine = judgment.IsThermalBurstFailure
            ? $"<color=#FF8A80>バースト理由: {judgment.BurstReason}</color>"
            : $"<color=#B9F6CA>素点 {judgment.RawScore} → クランプ後 <b>{judgment.FinalScore}</b> 点（上限100厳守）</color>";

        System.Text.StringBuilder paramPreview = new System.Text.StringBuilder();
        if (finalParameters != null && finalParameters.Count > 0)
        {
            for (int i = 0; i < finalParameters.Count; i++)
            {
                if (i > 0)
                {
                    paramPreview.Append(" | ");
                }

                paramPreview.Append(finalParameters[i]);
            }
        }
        else
        {
            paramPreview.Append("（パラメータなし）");
        }

        Debug.Log(
            "\n<color=#FFD54F><b>════════════════════════════════════════</b></color>\n" +
            "<color=#FFF59D><b>[=== DEMO RESULT ===]</b></color>\n" +
            "<color=#FFD54F><b>════════════════════════════════════════</b></color>\n" +
            $"{outcome}\n" +
            $"<color=#E0F7FA>職種: <b>{craftType}</b> / 工程数: <b>{artisanLogCount}</b> / 最終スコア: <b>{judgment.FinalScore}</b></color>\n" +
            $"{reasonLine}\n" +
            $"<color=#80DEEA>finalParameters: {paramPreview}</color>\n" +
            "<color=#FFD54F><b>════════════════════════════════════════</b></color>");
    }

    /// <summary>ジャッジ結果に応じた成功 / バーストのリッチテキストをコンソールへ出力します。</summary>
    private static void EmitCraftingJudgmentConsoleLog(DemoCraftingJudgmentResult judgment)
    {
        if (judgment == null)
        {
            return;
        }

        if (judgment.IsThermalBurstFailure)
        {
            Debug.Log(
                "<b><color=#FF3333>【熱科学バースト】融点突破または熱分解により、" +
                "職人作業は完全失敗に終わった……（10点）</color></b>\n" +
                $"<color=#FF8A80>{judgment.BurstReason}</color>");
            return;
        }

        Debug.Log(
            "<b><color=#00FF00>【通常成功】初期ツールの限界までこだわりを尽くした製品が完成！" +
            $"（スコア: {judgment.FinalScore}点）</color></b>\n" +
            $"<color=#A5D6A7>素点 {judgment.RawScore} を Clamp(60,100) で丸めました。</color>");
    }

    /// <summary>ジャッジ対象の職種文字列をセッションと判定結果から解決します。</summary>
    private string ResolveActiveCraftTypeForJudgment(DemoCraftingJudgmentResult judgment)
    {
        if (!string.IsNullOrWhiteSpace(detailedCraftingProcess.currentCraftType) &&
            !string.Equals(
                detailedCraftingProcess.currentCraftType,
                DetailedCraftingProcessManager.CraftTypeNone,
                StringComparison.OrdinalIgnoreCase))
        {
            return detailedCraftingProcess.currentCraftType;
        }

        if (judgment != null && !string.IsNullOrWhiteSpace(judgment.CraftType))
        {
            return judgment.CraftType;
        }

        return ResolveCraftingPhaseCraftType();
    }

    /// <summary>ResultPhase 着地前に生産セッション・戦闘モーション・敵攻撃を安全停止します。</summary>
    private void CleanupCraftingPhaseForResultLanding(string craftType)
    {
        StopCraftingPhaseHarassmentSchedule();
        StopAutoCombatAssist();
        StopBattleToCraftTransitionIfActive();
        CleanupCraftingPhaseCombatMotion();

        if (craftingStatusManager != null && !string.IsNullOrWhiteSpace(craftType))
        {
            craftingStatusManager.ResetStatus(craftType);
        }

        detailedCraftingProcess?.EndSession();

        CraftingExperimentHub hub = craftingHub ?? CraftingExperimentHub.EnsureInstance();
        hub?.ShutdownForResultLanding();

        CombatActionFeedbackManager combat = combatFeedback
            ?? CombatActionFeedbackManager.EnsureInstance();
        combat?.NotifyDetailedCraftingFinished();
    }

    /// <summary>進行中の戦闘→工房暗転コルーチンがあれば停止します。</summary>
    private void StopBattleToCraftTransitionIfActive()
    {
        if (battleToCraftTransitionCoroutine == null)
        {
            return;
        }

        StopCoroutine(battleToCraftTransitionCoroutine);
        battleToCraftTransitionCoroutine = null;
    }

    /// <summary>生産フェーズの敵攻撃とプレイヤー攻撃モーションを停止します。</summary>
    private void CleanupCraftingPhaseCombatMotion()
    {
        combatFeedback ??= CombatActionFeedbackManager.EnsureInstance();
        combatFeedback?.StopCombatMotionForCraftingResult();
    }

    /// <summary>被弾バースト（InterruptByDamage）後に戦闘フェーズへ強制引き戻しします。</summary>
    public void HandleCraftingInterruptedByDamage()
    {
        if (CurrentState != DemoState.CraftingPhase && CurrentState != DemoState.TransitionPhase)
        {
            Debug.LogWarning(
                $"[DemoTimeLineManager] HandleCraftingInterruptedByDamage は Crafting/Transition 専用です（現在: {CurrentState}）。");
            return;
        }

        LogNav(
            "<color=#FF5252><b>══ DAMAGE BURST RECOVERY ══</b></color>\n" +
            "<color=#FF8A80>クラフト被弾により BattlePhase へ強制復帰。再び刃を交えよ。</color>");

        isCraftingFinalizationInProgress = false;
        StopBattleToCraftTransitionIfActive();
        StopAutoCombatAssist();
        StopCraftingPhaseHarassmentSchedule();

        TownSafetyZoneGate.SetInsideTown(false);

        craftingHub ??= CraftingExperimentHub.EnsureInstance();
        craftingHub?.ApplyDemoCraftingPhaseThreatMode();

        detailedCraftingProcess ??= DetailedCraftingProcessManager.EnsureInstance();
        if (detailedCraftingProcess != null && detailedCraftingProcess.IsSessionActive)
        {
            detailedCraftingProcess.EndSession();
        }

        combatFeedback ??= CombatActionFeedbackManager.EnsureInstance();
        combatFeedback?.NotifyDetailedCraftingFinished();

        ResetBattleTransitionGuardsForBattleEntry();
        TransitionTo(DemoState.BattlePhase);
        BeginBattleWave();
    }

    /// <summary>BattlePhase 再入場時に撃破→工房遷移ガードを初期化します。</summary>
    private void ResetBattleTransitionGuardsForBattleEntry()
    {
        combatClearedNotified = false;
        StopBattleToCraftTransitionIfActive();
    }

    private static string FormatStateLabel(DemoState state)
    {
        return state switch
        {
            DemoState.BattlePhase => "BattlePhase",
            DemoState.TransitionPhase => "TransitionPhase",
            DemoState.CraftingPhase => "CraftingPhase",
            DemoState.ResultPhase => "ResultPhase",
            _ => state.ToString()
        };
    }

    private static DemoPhase MapStateToLegacyPhase(DemoState state)
    {
        return state switch
        {
            DemoState.BattlePhase => DemoPhase.CombatPhase,
            DemoState.CraftingPhase => DemoPhase.WorkshopPhase,
            DemoState.ResultPhase => DemoPhase.ResultPhase,
            DemoState.TransitionPhase => DemoPhase.WorkshopPhase,
            _ => DemoPhase.Idle
        };
    }

    /// <summary>
    /// 第1弾テクニカルデモを開始します（レガシー API）。内部で BattlePhase へ遷移します。
    /// </summary>
    public void StartDemoFirstWave()
    {
        TransitionTo(DemoState.BattlePhase);
        BeginBattleWave();
    }

    /// <summary>戦闘ウェーブの物理セットアップ（敵出現・攻撃開始）を実行します。</summary>
    private void BeginBattleWave()
    {
        CacheReferences();
        combatClearedNotified = false;

        LogNav(
            "<color=#FF8A65><b>══ PHASE 01 · COMBAT ══</b></color>\n" +
            "<color=#FFE0B2>森の害獣スライムが戦闘エリアに現れた。フロム式の刃を見切れ。</color>");

        Vector3 combatPosition = ResolveCombatAreaPosition();
        TeleportPlayer(combatPosition);
        SpawnDemoEnemyMarker(combatPosition + new Vector3(2f, 0f, 0f));

        combatFeedback ??= CombatActionFeedbackManager.EnsureInstance();
        combatFeedback?.UnfreezeCombatSystemsForBattle();

        MasterDataManager.EnsureInstance();

        VisibleEnemyAI battleEnemy = FindAnyObjectByType<VisibleEnemyAI>();
        battleEnemy?.ResumeForBattle();

        EnemyAttackProfile slimeProfile = EnemyAttackProfile.CreateForestSlimeMob();
        ActiveEnemyCombatStats masterSlime = EnemyMasterBridge.BuildBattleEnemy("ENEMY_FOREST_SLIME");
        if (masterSlime?.attackProfile != null)
        {
            combatFeedback.SetTargetEnemyFromMaster(masterSlime);
        }
        else
        {
            combatFeedback.SetTargetEnemy(slimeProfile);
        }
        combatFeedback.TriggerEnemyAttack();

        if (isAutoRelayActive)
        {
            RestartAutoCombatAssist();
        }
    }

    /// <summary>
    /// 敵撃破後に呼び出します。暗転コルーチンで工房フェーズへ自動移行します。
    /// </summary>
    public void OnCombatCleared()
    {
        if (CurrentState != DemoState.BattlePhase || combatClearedNotified)
        {
            return;
        }

        combatClearedNotified = true;

        if (battleToCraftTransitionCoroutine != null)
        {
            StopCoroutine(battleToCraftTransitionCoroutine);
        }

        battleToCraftTransitionCoroutine = StartCoroutine(ExecuteBattleToCraftTransition());
    }

    /// <summary>
    /// 敵撃破から生産フェーズへの暗転・戦利品付与・座標移動を順次実行するシーケンスコルーチンです。
    /// </summary>
    private IEnumerator ExecuteBattleToCraftTransition()
    {
        Debug.Log(
            "<b><color=#FF8C00>【ANOMALY KILL】エネミーの撃破に成功！戦利品を回収し、" +
            "大釜の待つ工房へシームレスに移行します...</color></b>");

        InGameVisualUIManager ui = visualUi ?? InGameVisualUIManager.EnsureInstance();
        combatFeedback ??= CombatActionFeedbackManager.EnsureInstance();
        combatFeedback.PlayAnomalyKillCameraShake();
        ui.PlayAnomalyKillPopup();

        TransitionTo(DemoState.TransitionPhase);

        yield return ui.FadeToBlackRoutine(battleToCraftFadeOutSeconds);

        CacheReferences();

        InventoryManager inv = inventoryManager ?? InventoryManager.EnsureInstance();
        ItemData gryphonBone = InventoryItemCatalog.CreateGryphonBone();
        inv.AddItem(gryphonBone, 1);

        Debug.Log(
            "<color=#A5D6A7><b>【戦利品ドロップ】</b></color> " +
            $"<color=#C8E6C9>{gryphonBone.itemName}（{gryphonBone.id}）をインベントリに +1 付与しました。</color>");

        Vector3 workshopPosition;
        Quaternion workshopRotation;
        ResolveWorkshopSpawnPose(out workshopPosition, out workshopRotation);
        TeleportPlayer(workshopPosition, workshopRotation);

        combatFeedback ??= CombatActionFeedbackManager.EnsureInstance();
        combatFeedback.HaltEnemyCombatForCraftingPhaseEntry();

        VisibleEnemyAI enemy = FindAnyObjectByType<VisibleEnemyAI>();
        if (enemy != null)
        {
            enemy.CancelVisualAttack();
            enemy.SetChaseEnabled(false);
        }

        DestroySpawnedEnemyMarker();

        yield return ui.FadeFromBlackRoutine(battleToCraftFadeInSeconds);

        TransitionTo(DemoState.CraftingPhase);

        LogNav(
            "<color=#4DD0E1><b>══ PHASE 02 · WORKSHOP ══</b></color>\n" +
            "<color=#E0F7FA>暗転を抜け、若葉の拠点・工房へ。ブラインド職人生産が待っている。</color>");

        battleToCraftTransitionCoroutine = null;
    }

    /// <summary>
    /// 工房内で素材スキル A・B を正当に継承した派生上位スキルを決定論的に生成し、ログ出力します。
    /// </summary>
    /// <param name="skillA">素材スキル A（例: Slash）</param>
    /// <param name="skillB">素材スキル B（例: FireMagic）</param>
    public void ExecuteBaseSkillFusion(string skillA, string skillB)
    {
        if (CurrentState != DemoState.CraftingPhase && CurrentState != DemoState.BattlePhase)
        {
            Debug.LogWarning("[DemoTimeLineManager] スキル融合は工房フェーズ（または戦闘直後）で実行してください。");
        }

        DemoDerivedSkill derived = ResolveDeterministicFusion(skillA, skillB);
        string tagLine = derived.inheritedTags != null && derived.inheritedTags.Length > 0
            ? string.Join(" × ", derived.inheritedTags)
            : $"{skillA} + {skillB}";

        Debug.Log(
            "<color=#CE93D8><b>【ベース継承型 · スキル融合】</b></color>\n" +
            $"<color=#E1BEE7>  素材: <b>{skillA}</b> ⊕ <b>{skillB}</b></color>\n" +
            $"<color=#F3E5F5>  ▶ 派生: <b>『{derived.displayName}』</b> ({derived.skillId})</color>\n" +
            $"<color=#D1C4E9>  継承属性: {tagLine} / 基礎威力 {derived.basePower:F0}</color>\n" +
            $"<color=#B39DDB>  {derived.description}</color>");
    }

    /// <summary>
    /// 戦闘中の一瞬の隙で即席武器を1フレーム生成し、インベントリへ追加して強制装備します。
    /// </summary>
    public void TriggerInstantCraftInCombat()
    {
        if (!DemoInputGate.IsInstantCraftInputAllowed())
        {
            Debug.LogWarning(
                "[DemoTimeLineManager] インスタント・クラフトは BattlePhase 中のみ実行できます。");
            return;
        }

        CacheReferences();

        InventoryManager inv = inventoryManager ?? InventoryManager.EnsureInstance();
        if (!inv.ContainsAnyInstantCraftMaterial())
        {
            inv.AddItem(InventoryItemCatalog.CreateGryphonBone(), 1);
        }

        CombatActionFeedbackManager combat = CombatActionFeedbackManager.EnsureInstance();
        if (!combat.AttemptInstantCraft())
        {
            Debug.LogWarning(
                "<color=#FFAB40><b>【現地即席インスタント生産】</b></color> " +
                "<color=#FFCC80>素材不足またはクラフト中のため失敗しました。</color>");
        }
    }

    /// <summary>
    /// 工房での自由クラフト終了後、例外パケットを出力してデモを締めくくります。
    /// </summary>
    public void EndDemoWithPacketOutput()
    {
        CacheReferences();
        TransitionTo(DemoState.ResultPhase);

        CraftingExperimentHub hub = craftingHub ?? CraftingExperimentHub.EnsureInstance();
        List<string> demoCatalysts = new List<string>
        {
            "demo_player_void_catalyst",
            "demo_timeline_anomaly_residue"
        };

        if (!hub.IsActive)
        {
            hub.StartCrafting(CraftProfessionType.Forge.ToString(), isSafetyZone: true);
        }

        hub.ApplyDebugExceptionCraftState(demoCatalysts);

        int packetCountBefore = exceptionCollector.PendingPackets.Count;
        hub.FinishCrafting(statusManager, null);

        CraftingExceptionPacket packet = null;
        if (exceptionCollector.PendingPackets.Count > packetCountBefore)
        {
            packet = exceptionCollector.PendingPackets[exceptionCollector.PendingPackets.Count - 1];
        }

        string json = packet != null
            ? CraftingExceptionCollector.SerializePacket(packet)
            : "<color=#FF8A65>（例外パケット未生成 — FinishCrafting 内の回収を確認してください）</color>";

        Debug.Log(
            "\n<color=#FFD54F><size=14><b>╔══════════════════════════════════════════════════════╗</b></size></color>\n" +
            "<color=#FFF59D><size=14><b>║  DEMO COMPLETE · あなたの奇策は次の世界線へ ║</b></size></color>\n" +
            "<color=#FFD54F><size=14><b>╚══════════════════════════════════════════════════════╝</b></size></color>\n" +
            "<color=#80CBC4>次回のワイプで、今回の職人介入が<b>正史ルート</b>として受肉します。</color>\n" +
            "<color=#4DB6AC>サーバー待機パケット（JSON）:</color>\n" +
            $"<color=#B2DFDB>{json}</color>");

        isAutoRelayActive = false;
    }

    /// <summary>
    /// Alpha1 から呼ばれる全自動デモリレー。各フェーズを順次ナビゲートしながら進行します。
    /// </summary>
    public void StartFullAutoDemoRelay()
    {
        if (autoRelayCoroutine != null)
        {
            StopCoroutine(autoRelayCoroutine);
        }

        isAutoRelayActive = true;
        combatClearedNotified = false;
        hasAutoStartedBattle = true;

        LogNav(
            "<color=#FFF176><b>▶ DEMO RELAY ENGAGED</b></color> " +
            "<color=#FFF9C4>第1弾テクニカルデモ — 戦闘→工房→例外出力を全自動で駆動します。</color>");

        autoRelayCoroutine = StartCoroutine(FullAutoDemoRelayRoutine());
    }

    private IEnumerator FullAutoDemoRelayRoutine()
    {
        yield return null;

        StartDemoFirstWave();
        yield return new WaitForSeconds(autoRelayCombatIntroSeconds);

        LogNav(
            "<color=#FFAB40><b>▸ INSTANT CRAFT</b></color> " +
            "<color=#FFE082>戦闘の隙に現地即席生産を試行…</color>");
        TriggerInstantCraftInCombat();

        yield return new WaitUntil(() =>
            CurrentState == DemoState.CraftingPhase || combatClearedNotified);

        yield return new WaitForSeconds(autoRelayWorkshopIntroSeconds);

        LogNav(
            "<color=#CE93D8><b>▸ SKILL FUSION</b></color> " +
            "<color=#E1BEE7>工房でベース継承型融合を実行…</color>");
        ExecuteBaseSkillFusion("Slash", "FireMagic");

        yield return new WaitForSeconds(autoRelayCraftBeatSeconds);

        CraftingExperimentHub hub = craftingHub ?? CraftingExperimentHub.EnsureInstance();
        if (hub.IsActive)
        {
            hub.ExecuteCraftAction(1);
            yield return new WaitForSeconds(autoRelayCraftBeatSeconds * 0.5f);
            hub.ExecuteCraftAction(2);
        }

        LogNav(
            "<color=#FF7043><b>▸ EXCEPTION COLLECT</b></color> " +
            "<color=#FFAB91>未知ルートのクラフト結果をパケット化…</color>");
        EndDemoWithPacketOutput();

        autoRelayCoroutine = null;
        isAutoRelayActive = false;
    }

    private void HandleEnemyPostureBroken(EnemyAttackProfile profile)
    {
        if (isRiskVerificationSuppressingAutoTransitions)
        {
            return;
        }

        if (CurrentState != DemoState.BattlePhase)
        {
            return;
        }

        string enemyLabel = profile != null ? profile.enemyName : "敵";
        LogNav(
            $"<color=#FFEB3B><b>▸ KILL CONFIRMED</b></color> " +
            $"<color=#FFF59D>{enemyLabel} を撃破。工房フェーズへ移行します。</color>");

        OnCombatCleared();
    }

    private void RestartAutoCombatAssist()
    {
        StopAutoCombatAssist();
        autoCombatAssistCoroutine = StartCoroutine(AutoCombatParryAssistRoutine());
    }

    private void StopAutoCombatAssist()
    {
        if (autoCombatAssistCoroutine != null)
        {
            StopCoroutine(autoCombatAssistCoroutine);
            autoCombatAssistCoroutine = null;
        }
    }

    private IEnumerator AutoCombatParryAssistRoutine()
    {
        CombatActionFeedbackManager combat = combatFeedback
            ?? CombatActionFeedbackManager.EnsureInstance();

        while (CurrentState == DemoState.BattlePhase && !combatClearedNotified)
        {
            if (!combat.IsEnemyAttackActive)
            {
                combat.TriggerEnemyAttack();
                float waitBudget = 3f;
                while (!combat.IsEnemyAttackActive && waitBudget > 0f)
                {
                    waitBudget -= Time.deltaTime;
                    yield return null;
                }
            }

            if (combat.IsEnemyAttackActive)
            {
                yield return new WaitForSeconds(0.04f);
                combat.AttemptParry();
            }

            yield return new WaitForSeconds(autoRelayBetweenParrySeconds);
        }
    }

    private static DemoDerivedSkill ResolveDeterministicFusion(string skillA, string skillB)
    {
        string key = BuildFusionKey(skillA, skillB);
        if (FusionRecipeTable.TryGetValue(key, out DemoDerivedSkill recipe))
        {
            return recipe;
        }

        string normalizedA = NormalizeSkillId(skillA);
        string normalizedB = NormalizeSkillId(skillB);
        return new DemoDerivedSkill
        {
            skillId = $"skill_fusion_{normalizedA}_{normalizedB}",
            displayName = $"{skillA}・{skillB}合成",
            parentSkillA = skillA,
            parentSkillB = skillB,
            inheritedTags = new[] { normalizedA, normalizedB },
            basePower = 42f,
            description = "登録外の組み合わせだが、素材の性質をそのまま継承した派生スキルとして成立した。"
        };
    }

    private static Dictionary<string, DemoDerivedSkill> BuildFusionRecipeTable()
    {
        var table = new Dictionary<string, DemoDerivedSkill>(StringComparer.OrdinalIgnoreCase);

        RegisterFusion(table, "Slash", "FireMagic", new DemoDerivedSkill
        {
            skillId = "skill_fire_slash",
            displayName = "火炎斬",
            parentSkillA = "Slash",
            parentSkillB = "FireMagic",
            inheritedTags = new[] { "Slash", "Fire" },
            basePower = 68f,
            description = "斬撃の軌道に炎を纏い、Slash の切先と FireMagic の燃焼を完全継承した上位技。"
        });

        RegisterFusion(table, "Slash", "IceMagic", new DemoDerivedSkill
        {
            skillId = "skill_frost_slash",
            displayName = "氷結斬",
            parentSkillA = "Slash",
            parentSkillB = "IceMagic",
            inheritedTags = new[] { "Slash", "Ice" },
            basePower = 64f,
            description = "刃先に凍結膜を張り、斬撃の断面を氷晶で拡張する派生スキル。"
        });

        RegisterFusion(table, "Guard", "HolyMagic", new DemoDerivedSkill
        {
            skillId = "skill_aegis_blessing",
            displayName = "聖盾の加護",
            parentSkillA = "Guard",
            parentSkillB = "HolyMagic",
            inheritedTags = new[] { "Guard", "Holy" },
            basePower = 55f,
            description = "防御の構えに聖光を重ね、被弾時の体勢損失を抑える継承型バフ技。"
        });

        return table;
    }

    private static void RegisterFusion(
        Dictionary<string, DemoDerivedSkill> table,
        string skillA,
        string skillB,
        DemoDerivedSkill recipe)
    {
        table[BuildFusionKey(skillA, skillB)] = recipe;
    }

    private static string BuildFusionKey(string skillA, string skillB)
    {
        string a = NormalizeSkillId(skillA);
        string b = NormalizeSkillId(skillB);
        return string.CompareOrdinal(a, b) <= 0 ? $"{a}|{b}" : $"{b}|{a}";
    }

    private static string NormalizeSkillId(string skillId)
    {
        return string.IsNullOrWhiteSpace(skillId)
            ? "unknown"
            : skillId.Trim();
    }

    private void CacheReferences()
    {
        combatFeedback ??= CombatActionFeedbackManager.EnsureInstance();
        inventoryManager ??= InventoryManager.EnsureInstance();
        craftingHub ??= CraftingExperimentHub.EnsureInstance();
        exceptionCollector ??= CraftingExceptionCollector.EnsureInstance();
        visualUi ??= InGameVisualUIManager.EnsureInstance();
        statusManager ??= PlayerStatusManager.Instance ?? FindAnyObjectByType<PlayerStatusManager>();
        detailedCraftingProcess ??= DetailedCraftingProcessManager.EnsureInstance();
        craftingStatusManager ??= CraftingStatusManager.EnsureInstance();
    }

    private Vector3 ResolveCombatAreaPosition()
    {
        Transform player = ResolvePlayerRootTransform();
        if (player != null)
        {
            return player.position + combatAreaOffset;
        }

        return combatAreaOffset;
    }

    private Vector3 ResolveWorkshopPosition()
    {
        TryResolveWorkshopSpawnPoint();

        if (workshopSpawnPoint != null)
        {
            return workshopSpawnPoint.position;
        }

        Transform player = ResolvePlayerRootTransform();
        if (player != null)
        {
            return player.position + workshopAreaOffset;
        }

        return workshopAreaOffset;
    }

    /// <summary>
    /// Inspector 未設定時にシーン内の工房スポーン Transform を名前で自動解決します。
    /// </summary>
    private void TryResolveWorkshopSpawnPoint()
    {
        if (workshopSpawnPoint != null)
        {
            return;
        }

        for (int i = 0; i < WorkshopSpawnPointCandidateNames.Length; i++)
        {
            string candidateName = WorkshopSpawnPointCandidateNames[i];
            GameObject candidate = GameObject.Find(candidateName);
            if (candidate == null)
            {
                continue;
            }

            workshopSpawnPoint = candidate.transform;
            Debug.Log(
                "<color=#A5D6A7>[DemoTimeLineManager] workshopSpawnPoint を " +
                $"'{candidateName}' から自動解決しました（{workshopSpawnPoint.position}）。</color>");
            return;
        }
    }

    /// <summary>
    /// 工房スポーン Transform からテレポート姿勢を解決します。
    /// 未設定時は暫定座標へフォールバックし、警告ログを出力します。
    /// </summary>
    private void ResolveWorkshopSpawnPose(out Vector3 position, out Quaternion rotation)
    {
        TryResolveWorkshopSpawnPoint();

        if (workshopSpawnPoint != null)
        {
            position = workshopSpawnPoint.position;
            rotation = workshopSpawnPoint.rotation;
            return;
        }

        Vector3 fallbackPosition = ResolveWorkshopOffsetFallbackPosition();
        Debug.LogWarning(
            "[DemoTimeLineManager] workshopSpawnPoint が未設定です。" +
            $"暫定座標 {fallbackPosition}（回転は identity）へフォールバックします。" +
            "Inspector で工房スポーン用 Transform を割り当ててください。");

        position = fallbackPosition;
        rotation = Quaternion.identity;
    }

    private Vector3 ResolveWorkshopOffsetFallbackPosition()
    {
        Transform player = ResolvePlayerRootTransform();
        if (player != null)
        {
            return player.position + workshopAreaOffset;
        }

        return workshopAreaOffset != Vector3.zero ? workshopAreaOffset : Vector3.zero;
    }

    /// <summary>生産フェーズ入場時にプレイヤー HP / スタミナを満タンへ回復します。</summary>
    private void RefreshPlayerVitalsForCraftingEntrance()
    {
        Transform playerRoot = ResolvePlayerRootTransform();
        if (playerRoot == null)
        {
            return;
        }

        CombatStats combatStats = playerRoot.GetComponent<CombatStats>();
        if (combatStats == null)
        {
            combatStats = playerRoot.GetComponentInChildren<CombatStats>(true);
        }

        combatStats?.FullHeal();

        PlayerStats playerStats = playerRoot.GetComponent<PlayerStats>();
        if (playerStats == null)
        {
            playerStats = playerRoot.GetComponentInChildren<PlayerStats>(true);
        }

        if (playerStats != null)
        {
            playerStats.SetStaminaForTesting(playerStats.maxStamina);
        }
    }

    /// <summary>シーン上のプレイヤー根 Transform を解決します（名前末尾空白・コンポーネント検索に対応）。</summary>
    private static Transform ResolvePlayerRootTransform()
    {
        GameObject byExactName = GameObject.Find("PlayerRobot");
        if (byExactName != null)
        {
            return byExactName.transform;
        }

        // GetStarted_Scene 等でインスタンス名が「PlayerRobot 」になるケースへ対応
        GameObject byLegacySceneName = GameObject.Find("PlayerRobot ");
        if (byLegacySceneName != null)
        {
            return byLegacySceneName.transform;
        }

        if (PlayerStatusManager.Instance != null)
        {
            return PlayerStatusManager.Instance.transform;
        }

        PlayerController playerController = FindAnyObjectByType<PlayerController>();
        if (playerController != null)
        {
            return playerController.transform;
        }

        PlayerStatusManager statusManager = FindAnyObjectByType<PlayerStatusManager>();
        if (statusManager != null)
        {
            return statusManager.transform;
        }

        return null;
    }

    private static void TeleportPlayer(Vector3 worldPosition)
    {
        TeleportPlayer(worldPosition, Quaternion.identity);
    }

    private static void TeleportPlayer(Vector3 worldPosition, Quaternion worldRotation)
    {
        Transform playerRoot = ResolvePlayerRootTransform();
        if (playerRoot == null)
        {
            Debug.LogWarning(
                "[DemoTimeLineManager] プレイヤーが見つからないため座標移動をスキップしました。" +
                "（PlayerRobot / PlayerStatusManager / PlayerController を確認してください）");
            return;
        }

        worldPosition = SnapPlayerRootToGround(worldPosition, playerRoot);

        CharacterController[] controllers = playerRoot.GetComponentsInChildren<CharacterController>();
        for (int i = 0; i < controllers.Length; i++)
        {
            if (controllers[i] != null)
            {
                controllers[i].enabled = false;
            }
        }

        playerRoot.SetPositionAndRotation(worldPosition, worldRotation);

        for (int i = 0; i < controllers.Length; i++)
        {
            if (controllers[i] != null)
            {
                controllers[i].enabled = true;
            }
        }
    }

    /// <summary>テレポート先 XZ を維持し、足元が地面に接するようワールド Y を補正します。</summary>
    private static Vector3 SnapPlayerRootToGround(Vector3 worldPosition, Transform playerRoot)
    {
        if (playerRoot == null)
        {
            return worldPosition;
        }

        if (!TrySampleGroundHeight(worldPosition, playerRoot, out float groundY))
        {
            return worldPosition;
        }

        float rootToFeetOffset = MeasurePlayerRootToFeetOffset(playerRoot);
        worldPosition.y = groundY + rootToFeetOffset;
        return worldPosition;
    }

    private static float MeasurePlayerRootToFeetOffset(Transform playerRoot)
    {
        CharacterController controller = playerRoot.GetComponentInChildren<CharacterController>();
        if (controller == null)
        {
            return 0f;
        }

        Transform controllerTransform = controller.transform;
        Vector3 feetWorld = controllerTransform.position +
            controllerTransform.rotation * (controller.center - Vector3.up * (controller.height * 0.5f));
        return playerRoot.position.y - feetWorld.y;
    }

    private static bool TrySampleGroundHeight(Vector3 worldPosition, Transform ignoreRoot, out float groundY)
    {
        groundY = float.NegativeInfinity;
        float ceiling = worldPosition.y + 1.5f;

        if (TryCollectHighestGroundBelow(
                worldPosition + Vector3.up * 0.5f,
                Vector3.down,
                12f,
                ceiling,
                ignoreRoot,
                ref groundY))
        {
            return true;
        }

        return TryCollectHighestGroundBelow(
            worldPosition + Vector3.up * 16f,
            Vector3.down,
            40f,
            ceiling,
            ignoreRoot,
            ref groundY);
    }

    private static bool TryCollectHighestGroundBelow(
        Vector3 rayOrigin,
        Vector3 direction,
        float distance,
        float ceilingY,
        Transform ignoreRoot,
        ref float groundY)
    {
        bool found = false;
        RaycastHit[] hits = Physics.RaycastAll(
            rayOrigin,
            direction,
            distance,
            ~0,
            QueryTriggerInteraction.Ignore);

        for (int i = 0; i < hits.Length; i++)
        {
            Collider hitCollider = hits[i].collider;
            if (hitCollider == null || ShouldIgnoreGroundCollider(hitCollider, ignoreRoot))
            {
                continue;
            }

            float hitY = hits[i].point.y;
            if (hitY > ceilingY || hitY <= groundY)
            {
                continue;
            }

            groundY = hitY;
            found = true;
        }

        return found;
    }

    private static bool ShouldIgnoreGroundCollider(Collider hitCollider, Transform ignoreRoot)
    {
        if (ignoreRoot == null)
        {
            return false;
        }

        Transform hitTransform = hitCollider.transform;
        return hitTransform == ignoreRoot || hitTransform.IsChildOf(ignoreRoot);
    }

    private void SpawnDemoEnemyMarker(Vector3 position)
    {
        DestroySpawnedEnemyMarker();

        const float cubeScale = 1.2f;

        VisibleEnemyAI existingEnemy = FindAnyObjectByType<VisibleEnemyAI>();
        if (existingEnemy != null)
        {
            existingEnemy.transform.position = position;
            existingEnemy.SnapFeetToGround();
            existingEnemy.FacePlayer();
            existingEnemy.RecaptureHomeTransform();
            combatFeedback?.RegisterVisibleEnemy(existingEnemy);
            spawnedEnemyMarker = existingEnemy.gameObject;
            Debug.Log(
                "<color=#A5D6A7>[DemoTimeLineManager] 既存の視覚的エネミーを戦闘位置へ移動: " +
                $"{existingEnemy.transform.position.x:F1}, {existingEnemy.transform.position.y:F1}, " +
                $"{existingEnemy.transform.position.z:F1}</color>");
            return;
        }

        Vector3 snappedPosition = VisibleEnemyAI.AdjustSpawnPositionForCubeFeet(position, cubeScale);
        GameObject enemyObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
        enemyObject.name = "DemoEnemy_VisibleSlime";
        enemyObject.transform.localScale = new Vector3(cubeScale, cubeScale, cubeScale);
        enemyObject.transform.position = snappedPosition;

        Renderer renderer = enemyObject.GetComponent<Renderer>();
        if (renderer != null)
        {
            RuntimeUrpMaterialUtility.ApplyOpaqueColor(renderer, new Color(0.35f, 0.85f, 0.45f, 1f));
        }

        VisibleEnemyAI visibleEnemy = enemyObject.AddComponent<VisibleEnemyAI>();
        visibleEnemy.SnapFeetToGround();
        visibleEnemy.FacePlayer();
        combatFeedback?.RegisterVisibleEnemy(visibleEnemy);

        spawnedEnemyMarker = enemyObject;
        Vector3 landed = visibleEnemy.transform.position;
        Debug.Log(
            "<color=#A5D6A7>[DemoTimeLineManager] 視覚的エネミー出現: 森の害獣スライム @ " +
            $"{landed.x:F1}, {landed.y:F1}, {landed.z:F1}</color>");
    }

    private void DestroySpawnedEnemyMarker()
    {
        if (spawnedEnemyMarker == null)
        {
            return;
        }

        if (spawnedEnemyMarker.GetComponent<VisibleEnemyAI>() == null)
        {
            Destroy(spawnedEnemyMarker);
        }

        spawnedEnemyMarker = null;
    }

    private static void LogNav(string richText)
    {
        Debug.Log($"<color=#81D4FA><b>[DemoTimeLine]</b></color> {richText}");
    }

    /// <summary>BattlePhase 入場時に、戦闘操作の全ホットキーをコンソールへ大出力します。</summary>
    private static void LogBattlePhaseNavigation()
    {
        Debug.Log(
            "<color=#FF8A65>━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━</color>\n" +
            "<color=#FF5252><b>⚔️ SYSTEM MODE: BATTLE PHASE（死線戦闘領域）</b></color>\n" +
            "<color=#FF8A65>━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━</color>\n" +
            "<color=#FFE0B2>【マウスホイール上下】: 攻撃属性の切り替え（Slash / Thrust / Strike）</color>\n" +
            "<color=#FFE0B2>【マウス左クリック】  : 現在の属性でplayerrobotがトランスフォーム物理攻撃（スタミナ15消費）</color>\n" +
            "<color=#FFE0B2>【LeftCtrlキー】     : 入力方向へ高速ステップ回避（※重量100%超過時は完全ロックの呪い）</color>\n" +
            "<color=#FFE0B2>【Fキー】            : 目の前に一瞬だけ半透明の「ガード防壁」を突き出す（パリィ成否目押し）</color>\n" +
            "<color=#B0BEC5>------------------------------------------------------------------------</color>\n" +
            "<color=#90A4AE>[デバッグ用Tキー]    : 敵の乱数ディレイ「のけぞり➔神速振り下ろし突撃」を誘発</color>\n" +
            "<color=#FF8A65>━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━</color>");
    }

    /// <summary>CraftingPhase 入場時に、生産工程の全ホットキーをコンソールへ大出力します。</summary>
    /// <param name="craftType">現在有効な職種（Forge / Alch）</param>
    private static void LogCraftingPhaseNavigation(string craftType)
    {
        string professionLabel = string.Equals(
            craftType,
            DetailedCraftingProcessManager.CraftTypeAlch,
            StringComparison.OrdinalIgnoreCase)
            ? "調合（Alch）"
            : "鍛冶（Forge）";

        Debug.Log(
            "<color=#4DD0E1>━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━</color>\n" +
            "<color=#00BCD4><b>🏭 SYSTEM MODE: CRAFTING PHASE（若葉の拠点・工房領域）</b></color>\n" +
            $"<color=#80DEEA>  現在の職種: <b>{professionLabel}</b>（{craftType}）</color>\n" +
            "<color=#4DD0E1>━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━</color>\n" +
            "<color=#E0F7FA>【数字キー 1】        : 工程スロット①（鍛冶:精製 / 調合:茎除去）</color>\n" +
            "<color=#E0F7FA>【数字キー 2】        : 工程スロット②（鍛冶:魔物合金 / 調合:丸投入）</color>\n" +
            "<color=#E0F7FA>【数字キー 3】        : 工程スロット③（鍛冶:大槌 / 調合:すり潰し）</color>\n" +
            "<color=#E0F7FA>【数字キー 4】        : 工程スロット④（鍛冶:研磨 / 調合:沸騰）</color>\n" +
            "<color=#E0F7FA>【数字キー 5】        : 工程スロット⑤（鍛冶:魔力行使 / 調合:冷水投入）</color>\n" +
            "<color=#E0F7FA>【Enterキー】        : 品質ジャッジ + 例外JSONパケット射出 → ResultPhase</color>\n" +
            "<color=#B0BEC5>------------------------------------------------------------------------</color>\n" +
            "<color=#FFAB91>※ 工房付近の敵に被弾すると投入素材がバーストし BattlePhase へ強制復帰します</color>\n" +
            "<color=#FFAB91>※ マウス左クリック攻撃は無効 — こだわり生産専用モードです</color>\n" +
            "<color=#4DD0E1>━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━</color>");
    }
}

/// <summary>
/// DemoTimeLineManager の現在ステートに応じて、戦闘・生産入力の可否を判定します。
/// </summary>
public static class DemoInputGate
{
    /// <summary>スキル進化カットイン演出中は各種入力を遮断します。</summary>
    private static bool IsEvolutionPerformanceBlockingInput()
    {
        return SkillEvolutionPresenter.IsPerformanceActive;
    }

    /// <summary>マウスホイール切替・左クリック攻撃・F パリィ・Ctrl ステップが許可されるか。</summary>
    public static bool IsBattleCombatInputAllowed()
    {
        if (IsEvolutionPerformanceBlockingInput())
        {
            return false;
        }

        DemoTimeLineManager timeline = DemoTimeLineManager.Instance;
        if (timeline == null)
        {
            return true;
        }

        return timeline.CurrentState == DemoState.BattlePhase;
    }

    /// <summary>数字 1〜5 のこだわり工程キーが許可されるか。</summary>
    public static bool IsCraftingHotkeyInputAllowed()
    {
        if (IsEvolutionPerformanceBlockingInput())
        {
            return false;
        }

        DemoTimeLineManager timeline = DemoTimeLineManager.Instance;
        if (timeline == null)
        {
            return false;
        }

        return timeline.CurrentState == DemoState.CraftingPhase;
    }

    /// <summary>Enter による生産終了（Result 遷移）が許可されるか。</summary>
    public static bool IsCraftingFinishInputAllowed()
    {
        return IsCraftingHotkeyInputAllowed();
    }

    /// <summary>インスタント・クラフト（X）が許可されるか。</summary>
    public static bool IsInstantCraftInputAllowed()
    {
        return IsBattleCombatInputAllowed();
    }

    /// <summary>暗転中など、プレイヤー操作を完全遮断すべきか。</summary>
    public static bool IsPlayerInputFullyBlocked()
    {
        if (IsEvolutionPerformanceBlockingInput())
        {
            return true;
        }

        DemoTimeLineManager timeline = DemoTimeLineManager.Instance;
        if (timeline == null)
        {
            return false;
        }

        return timeline.CurrentState == DemoState.TransitionPhase ||
               timeline.CurrentState == DemoState.ResultPhase;
    }

    /// <summary>極意付与などマスタリーデバッグホットキーが許可されるか（BattlePhase のみ）。</summary>
    public static bool IsMasteryDebugHotkeyAllowed()
    {
        DemoTimeLineManager timeline = DemoTimeLineManager.Instance;
        if (timeline == null)
        {
            return true;
        }

        return timeline.CurrentState == DemoState.BattlePhase;
    }

    /// <summary>閃き・極意ポップアップを出さずコンソールログのみにするべきか。</summary>
    public static bool ShouldSuppressInspirationPopup()
    {
        DemoTimeLineManager timeline = DemoTimeLineManager.Instance;
        if (timeline == null)
        {
            return false;
        }

        return timeline.CurrentState == DemoState.CraftingPhase ||
               timeline.CurrentState == DemoState.TransitionPhase ||
               timeline.CurrentState == DemoState.ResultPhase;
    }
}

/// <summary>Play 開始時に DemoTimeLineManager を DebugSystemsHub へ自動配置します。</summary>
public static class DemoTimeLineBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoAttach()
    {
        AttachToDebugSystemsHub();
    }

    /// <summary>DebugSystemsHub に DemoTimeLineManager が無ければ追加します。</summary>
    public static void AttachToDebugSystemsHub()
    {
        GameObject hub = GameObject.Find("DebugSystemsHub");
        if (hub == null)
        {
            Debug.LogWarning("[DemoTimeLineBootstrap] DebugSystemsHub が見つかりません。");
            return;
        }

        DemoTimeLineManager existing = DemoTimeLineManager.Instance;
        if (existing != null)
        {
            if (existing.gameObject != hub)
            {
                existing.transform.SetParent(hub.transform, worldPositionStays: true);
            }

            return;
        }

        if (hub.GetComponent<DemoTimeLineManager>() == null)
        {
            hub.AddComponent<DemoTimeLineManager>();
        }
    }
}

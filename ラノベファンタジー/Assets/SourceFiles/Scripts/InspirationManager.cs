using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// スキル・技の「閃き」をシミュレートするシングルトンマネージャー。
/// 将来は本物の AI が同じ API（InspireActionToSkill 等）を呼び出す想定です。
/// </summary>
public class InspirationManager : MonoBehaviour
{
    public static InspirationManager Instance { get; private set; }

    [Header("参照")]
    [SerializeField] private PlayerSkillSlotManager skillSlotManager;

    [Header("トリガー1：ログ監視（ニアミス）")]
    [SerializeField] private int nearMissThreshold = 20;
    [SerializeField] private bool nearMissInspirationTriggered;

    [Header("トリガー2：九死に一生（ピンチ）")]
    [Range(0f, 1f)]
    [SerializeField] private float pinchInspirationChance = 1f;
    [SerializeField] private bool pinchInspirationTriggered;

    [Header("トリガー4：覚醒")]
    [SerializeField] private bool awakeningTriggered;

    [Header("能動的・技開発（受動的閃きとは別系統）")]
    [SerializeField] private List<ActionDevelopmentRecipe> developmentRecipes = new List<ActionDevelopmentRecipe>();

    [SerializeField] private bool initializeDefaultDevelopmentRecipesOnStart = true;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        if (skillSlotManager == null)
        {
            skillSlotManager = FindAnyObjectByType<PlayerSkillSlotManager>();
        }
    }

    private void Start()
    {
        if (initializeDefaultDevelopmentRecipesOnStart && developmentRecipes.Count == 0)
        {
            InitializeDefaultDevelopmentRecipes();
        }
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    // -------------------------------------------------------------------------
    // トリガー1：自動達成型（PlayerActionLogger のニアミス数を監視）
    // PlayerCombatInspirationBridge.NotifyNearMissLogged() から呼び出されます。
    // -------------------------------------------------------------------------

    /// <summary>
    /// ニアミス累計が閾値を超えた最初の 1 回のみ、【回避術】に「瞬歩」を閃かせます。
    /// </summary>
    public bool TriggerLogInspiration()
    {
        if (nearMissInspirationTriggered || skillSlotManager == null)
        {
            return false;
        }

        PlayerActionLogger logger = PlayerActionLogger.Instance;
        if (logger == null || logger.NearMissCount <= nearMissThreshold)
        {
            return false;
        }

        nearMissInspirationTriggered = true;
        return InspireFlashStepFromNearMiss(logger.NearMissCount);
    }

    /// <summary>後方互換：PlayerActionLogger 等からの直接呼び出し用</summary>
    public void OnNearMissLogged()
    {
        TriggerLogInspiration();
    }

    /// <summary>デバッグ用：トリガー1を強制実行</summary>
    [ContextMenu("Debug/Trigger1 FlashStep (Near Miss)")]
    public void InspireFlashStepFromNearMiss()
    {
        PlayerActionLogger logger = PlayerActionLogger.Instance;
        int nearMissCount = logger != null ? logger.NearMissCount : nearMissThreshold + 1;
        InspireFlashStepFromNearMiss(nearMissCount);
    }

    private bool InspireFlashStepFromNearMiss(int nearMissCount)
    {
        if (skillSlotManager == null)
        {
            Debug.LogWarning("[InspirationManager] skillSlotManager が未設定です。");
            return false;
        }

        ActionData flashStep = new ActionData
        {
            actionID = ActionIds.FlashStep,
            actionName = "瞬歩",
            damageMultiplier = 0f,
            staminaCost = 28f,
            activeDetectionTime = 0.35f,
            invincibilityTime = 0.2f,
            isDerived = true,
            inspirationSource = $"ニアミス累計が{nearMissThreshold}回を超えたため、生死の境界で足が研ぎ澄まされた"
        };

        bool equipped = skillSlotManager.InspireActionToSkill(SkillIds.EvadeArt, flashStep);
        if (equipped)
        {
            InspirationCombatLog.LogNearMissInspiration(flashStep.actionName, nearMissCount);
        }

        return equipped;
    }

    // -------------------------------------------------------------------------
    // トリガー2：九死に一生（HP20%以下 & スタミナ0 で攻撃）
    // PlayerCombatInspirationBridge.NotifyPlayerAttackStarted() から呼び出されます。
    // -------------------------------------------------------------------------

    /// <summary>
    /// ピンチ状態での攻撃時に、【片手剣術】へ大技を閃かせます。
    /// </summary>
    public bool TriggerPinchInspiration(PlayerStats stats, CombatStats combatStats)
    {
        if (pinchInspirationTriggered || skillSlotManager == null || stats == null || combatStats == null)
        {
            return false;
        }

        if (!stats.IsAlive)
        {
            return false;
        }

        float hpRatio = combatStats.MaxHp > 0
            ? (float)combatStats.CurrentHp / combatStats.MaxHp
            : 1f;

        bool isPinch = hpRatio <= 0.2f && stats.CurrentStamina <= 0f;
        if (!isPinch)
        {
            return false;
        }

        if (Random.value > pinchInspirationChance)
        {
            return false;
        }

        pinchInspirationTriggered = true;
        return ApplySilentEdgeInspiration();
    }

    /// <summary>後方互換：自動テスト等からの直接呼び出し用</summary>
    public void TryTriggerPinchInspiration(PlayerStats stats, CombatStats combatStats)
    {
        TriggerPinchInspiration(stats, combatStats);
    }

    /// <summary>デバッグ用：トリガー2を強制実行</summary>
    [ContextMenu("Debug/Trigger2 SilentEdge (Pinch)")]
    public void InspireSilentEdgeFromPinch()
    {
        ApplySilentEdgeInspiration();
    }

    private bool ApplySilentEdgeInspiration()
    {
        bool equipped = CreateAndEquipSilentEdge();
        if (equipped)
        {
            InspirationCombatLog.LogPinchInspiration("絶境無音斬");
        }

        return equipped;
    }

    private bool CreateAndEquipSilentEdge()
    {
        if (skillSlotManager == null)
        {
            Debug.LogWarning("[InspirationManager] skillSlotManager が未設定です。");
            return false;
        }

        ActionData silentEdge = new ActionData
        {
            actionID = ActionIds.SilentEdge,
            actionName = "絶境無音斬",
            damageMultiplier = 2.5f,
            staminaCost = 0f,
            activeDetectionTime = 0.5f,
            invincibilityTime = 0f,
            isDerived = true,
            inspirationSource = "HP20%以下かつスタミナ枯渇の絶境で、無音の一閃が脳裏を過ぎった"
        };

        return skillSlotManager.InspireActionToSkill(SkillIds.OneHandSword, silentEdge);
    }

    // -------------------------------------------------------------------------
    // トリガー3：合成・掛け合わせ型
    // -------------------------------------------------------------------------

    /// <summary>
    /// 【片手剣術】の「強撃」×【初級火魔法】の「火の粉」→ 魔法剣技「フレインスラッシュ」
    /// </summary>
    [ContextMenu("Debug/Trigger3 FlareSlash (Combine)")]
    public bool TryCombineFlareSlash()
    {
        Debug.Log("[InspirationManager] 合成を試行: 強撃 × 火の粉 → フレインスラッシュ");
        return TryCombineActions(
            SkillIds.OneHandSword, ActionIds.StrongStrike,
            SkillIds.FireMagic, ActionIds.FireSpark,
            CreateFlareSlashAction());
    }

    /// <summary>
    /// 2つの技（異なるスキルに装着済み）を材料に、新技を生成して攻撃スキルへ追加します。
    /// </summary>
    public bool TryCombineActions(
        string skillIdA, string actionIdA,
        string skillIdB, string actionIdB,
        ActionData resultAction)
    {
        if (skillSlotManager == null || resultAction == null || !resultAction.IsValid())
        {
            return false;
        }

        bool hasA = skillSlotManager.TryFindEquippedAction(skillIdA, actionIdA, out _);
        bool hasB = skillSlotManager.TryFindEquippedAction(skillIdB, actionIdB, out _);

        if (!hasA || !hasB)
        {
            Debug.LogWarning("[InspirationManager] 合成に必要な技がスキルに装着されていません。");
            return false;
        }

        if (skillSlotManager.IsActionOwnedAnywhere(resultAction.actionID))
        {
            Debug.Log("[InspirationManager] 合成結果の技は既に所持済みです。");
            return false;
        }

        resultAction.isDerived = true;
        skillSlotManager.InspireActionToSkill(SkillIds.OneHandSword, resultAction);
        Debug.Log($"[閃き・合成] {resultAction.actionName} が誕生しました！");
        return true;
    }

    private static ActionData CreateFlareSlashAction()
    {
        return new ActionData
        {
            actionID = ActionIds.FlareSlash,
            actionName = "フレインスラッシュ",
            damageMultiplier = 2f,
            staminaCost = 30f,
            activeDetectionTime = 0.4f,
            invincibilityTime = 0f,
            isDerived = true,
            inspirationSource = "【片手剣術・強撃】と【初級火魔法・火の粉】の掛け合わせにより閃いた魔法剣技"
        };
    }

    // -------------------------------------------------------------------------
    // トリガー4：覚醒イベント型
    // -------------------------------------------------------------------------

    /// <summary>ストーリーイベント等から呼び出し、確定で隠しユニーク技を付与</summary>
    [ContextMenu("Debug/Trigger4 Awakening")]
    public void TriggerAwakeningEvent()
    {
        if (skillSlotManager == null)
        {
            Debug.LogWarning("[InspirationManager] skillSlotManager が未設定です。");
            return;
        }

        if (awakeningTriggered)
        {
            Debug.Log("[InspirationManager] 覚醒イベントは既に発動済みです。（[0]キーでフラグリセット可）");
            return;
        }

        awakeningTriggered = true;

        ActionData awakening = new ActionData
        {
            actionID = ActionIds.AwakeningStrike,
            actionName = "覚醒・異界穿ち",
            damageMultiplier = 3f,
            staminaCost = 40f,
            activeDetectionTime = 0.55f,
            invincibilityTime = 0.15f,
            isDerived = true,
            inspirationSource = "異世界の記憶が覚醒し、封印されていた真の一撃が解き放たれた"
        };

        skillSlotManager.InspireActionToSkill(SkillIds.OneHandSword, awakening);
        Debug.Log("[閃き・覚醒] 隠しユニーク技が突如として生まれました！");
    }

    /// <summary>デバッグ用：全トリガーをリセット</summary>
    [ContextMenu("Debug/Reset All Inspiration Flags")]
    public void ResetInspirationFlags()
    {
        nearMissInspirationTriggered = false;
        pinchInspirationTriggered = false;
        awakeningTriggered = false;
        Debug.Log("[InspirationManager] 閃きフラグをリセットしました。");
    }

    // -------------------------------------------------------------------------
    // 能動的・技開発（安全地帯メニュー / プレイヤー自発コマンド）
    // -------------------------------------------------------------------------

    /// <summary>デフォルトの技開発レシピを構築します。</summary>
    public void InitializeDefaultDevelopmentRecipes()
    {
        developmentRecipes.Clear();
        developmentRecipes.Add(ActionDevelopmentRecipe.CreateDanceBladeRecipe());
    }

    /// <summary>
    /// プレイヤーが能動的に技を研究・開発します。
    /// 素材スキル（器）の所持、素材技の所持、素材技の極意（isMastered）が必要です。
    /// </summary>
    /// <param name="skillId">素材スキル ID（例: skill_dance_art）</param>
    /// <param name="actionId">素材技 ID（例: action_basic_slash）</param>
    /// <param name="slotManager">プレイヤーのスキル管理（null なら内部参照を使用）</param>
    /// <returns>開発成功時 true</returns>
    public bool TryDevelopAction(string skillId, string actionId, PlayerSkillSlotManager slotManager)
    {
        PlayerSkillSlotManager manager = slotManager != null ? slotManager : skillSlotManager;
        if (manager == null)
        {
            Debug.LogWarning("[InspirationManager] PlayerSkillSlotManager が未設定です。");
            return false;
        }

        if (string.IsNullOrWhiteSpace(skillId) || string.IsNullOrWhiteSpace(actionId))
        {
            Debug.LogWarning("[InspirationManager] skillId または actionId が無効です。");
            return false;
        }

        if (!manager.OwnsSkill(skillId))
        {
            Debug.Log($"[InspirationManager] 技開発できません: スキル未所持 ({skillId})");
            return false;
        }

        if (!manager.IsActionOwnedAnywhere(actionId))
        {
            Debug.Log($"[InspirationManager] 技開発できません: 技未所持 ({actionId})");
            return false;
        }

        if (!IsMaterialActionMastered(actionId))
        {
            string displayName = ResolveMaterialActionDisplayName(actionId);
            InspirationCombatLog.LogDevelopmentMasteryRequired(displayName);
            return false;
        }

        ActionDevelopmentRecipe recipe = FindDevelopmentRecipe(skillId, actionId);
        if (recipe == null)
        {
            Debug.Log($"[InspirationManager] 技開発できません: レシピ未登録 ({skillId} × {actionId})");
            return false;
        }

        ActionData result = recipe.resultAction.Clone();
        if (result == null || !result.IsValid())
        {
            Debug.LogWarning("[InspirationManager] 技開発失敗: 完成技データが無効です。");
            return false;
        }

        if (manager.IsActionOwnedAnywhere(result.actionID))
        {
            Debug.Log($"[InspirationManager] 技開発できません: 完成技は既に所持済み ({result.actionName})");
            return false;
        }

        result.isDerived = true;
        if (string.IsNullOrWhiteSpace(result.inspirationSource))
        {
            result.inspirationSource =
                $"【{skillId}】×【{actionId}】を能動的に融合して開発した術理";
        }

        bool equipped = manager.InspireActionToSkill(recipe.targetSkillId, result);
        if (equipped)
        {
            manager.SyncActiveActionsToPlayerController();
            InspirationCombatLog.LogActiveActionDevelopment(result.actionName, result.actionID);
        }

        return equipped;
    }

    /// <summary>登録済みレシピから素材ペアに合致するものを検索します。</summary>
    public ActionDevelopmentRecipe FindDevelopmentRecipe(string skillId, string actionId)
    {
        foreach (ActionDevelopmentRecipe recipe in developmentRecipes)
        {
            if (recipe != null && recipe.IsValid() && recipe.Matches(skillId, actionId))
            {
                return recipe;
            }
        }

        return null;
    }

    /// <summary>自動テスト用：レシピ一覧をデフォルトに戻します。</summary>
    public void ResetDevelopmentRecipesForTesting()
    {
        InitializeDefaultDevelopmentRecipes();
    }

    /// <summary>素材技が極意（isMastered）到達済みかを判定します。</summary>
    private static bool IsMaterialActionMastered(string actionId)
    {
        ActionMasteryManager mastery = ActionMasteryManager.Instance;
        if (mastery == null)
        {
            mastery = Object.FindAnyObjectByType<ActionMasteryManager>();
        }

        return mastery != null && mastery.IsActionMastered(actionId);
    }

    /// <summary>技開発ログ用の素材技表示名を解決します。</summary>
    private static string ResolveMaterialActionDisplayName(string actionId)
    {
        ActionMasteryManager mastery = ActionMasteryManager.Instance;
        if (mastery == null)
        {
            mastery = Object.FindAnyObjectByType<ActionMasteryManager>();
        }

        if (mastery != null)
        {
            return mastery.GetActionDisplayName(actionId);
        }

        return actionId;
    }

    // -------------------------------------------------------------------------
    // AI パイプライン連携（GamePhaseEventBridge から呼び出し）
    // -------------------------------------------------------------------------

    /// <summary>
    /// AI（Gemini）が生成したスキルデータを閃き技としてプレイヤーのスキル枠へ反映します。
    /// </summary>
    /// <param name="generatedSkill">パース済みの正常スキル（フォールバック不可）</param>
    /// <param name="sourcePhase">生成元の GamePhase</param>
    /// <returns>装着に成功した場合 true</returns>
    public bool ApplyAIGeneratedSkill(GeneratedSkillData generatedSkill, GamePhase sourcePhase)
    {
        if (generatedSkill == null || generatedSkill.IsFallback || skillSlotManager == null)
        {
            return false;
        }

        float damageMultiplier = 1.5f;
        float staminaCost = 25f;

        if (generatedSkill.EffectParameters.TryGetValue("damageMultiplier", out float parsedDamage))
        {
            damageMultiplier = Mathf.Max(0.1f, parsedDamage);
        }

        if (generatedSkill.EffectParameters.TryGetValue("staminaCostMultiplier", out float staminaMul))
        {
            staminaCost = Mathf.Max(5f, 20f * staminaMul);
        }

        string targetSkillId = ResolveAISkillTargetSlot(sourcePhase);
        string actionId = BuildAIActionId(generatedSkill.SkillName);

        ActionData inspiredAction = new ActionData
        {
            actionID = actionId,
            actionName = generatedSkill.SkillName,
            damageMultiplier = damageMultiplier,
            staminaCost = staminaCost,
            activeDetectionTime = sourcePhase == GamePhase.Crisis ? 0.45f : 0.35f,
            invincibilityTime = sourcePhase == GamePhase.Crisis ? 0.15f : 0f,
            isDerived = true,
            inspirationSource =
                $"[{sourcePhase}] {generatedSkill.FlavorText}（確率 {generatedSkill.Probability:F5}）"
        };

        bool equipped = skillSlotManager.InspireActionToSkill(targetSkillId, inspiredAction);
        if (equipped)
        {
            Debug.Log(
                $"<color=#FFD700><b>[InspirationManager][AI閃き]</b> " +
                $"『{generatedSkill.SkillName}』を {targetSkillId} に付与しました。</color>");
        }

        return equipped;
    }

    private static string ResolveAISkillTargetSlot(GamePhase sourcePhase)
    {
        switch (sourcePhase)
        {
            case GamePhase.Crisis:
            case GamePhase.Fusion:
                return SkillIds.OneHandSword;
            case GamePhase.Training:
                return SkillIds.OneHandSword;
            case GamePhase.Crafting:
                return SkillIds.FireMagic;
            default:
                return SkillIds.OneHandSword;
        }
    }

    private static string BuildAIActionId(string skillName)
    {
        string seed = string.IsNullOrWhiteSpace(skillName) ? "ai_skill" : skillName;
        string sanitized = seed.Replace(" ", "_").Replace("（", "_").Replace("）", string.Empty);
        return $"action_ai_{sanitized}_{System.Guid.NewGuid():N}".Substring(0, Mathf.Min(48, 16 + sanitized.Length));
    }
}

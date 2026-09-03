using System;
using UnityEngine;

// =============================================================================
// スキル全体進化（SkillEvolutionData.nextSkillId）実行マネージャー
// 合成（combinationRecipes）や技ツリー派生（derivatives）と分離した差し替え専用レイヤー
// =============================================================================

/// <summary>
/// プレイ中に進化条件を判定し、所持スキルを上位スキルへ差し替えます。
/// 判定失敗やハルシネーション入力は例外化せず、ログを出して安全に失敗させます。
/// </summary>
public class SkillEvolutionLinker : MonoBehaviour
{
    public static SkillEvolutionLinker Instance { get; private set; }
    public static event Action<string, string> SkillEvolved;

    /// <summary>
    /// conditionFlag 判定の外部フック。
    /// 例: StoryFlagManager.Has(flagKey) を代入してプロジェクト固有フラグへ接続します。
    /// </summary>
    public static Func<string, bool> ConditionFlagResolver { get; set; }

    [Header("参照（未設定時は Instance を自動解決）")]
    [SerializeField] private PlayerSkillSlotManager playerSkillSlotManager;
    [SerializeField] private PlayerStatusManager playerStatusManager;
    [SerializeField] private SkillMasterRepository skillMasterRepository;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        ResolveReferences();
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    /// <summary>
    /// 指定スキルの進化条件をチェックし、成立時に進化先へ差し替えます。
    /// true は「進化を実行した」、false は「条件未達または失敗」を意味します。
    /// </summary>
    public static bool CheckAndExecuteEvolution(string currentSkillId)
    {
        SkillEvolutionLinker linker = Instance ?? UnityEngine.Object.FindAnyObjectByType<SkillEvolutionLinker>();
        if (linker == null)
        {
            Debug.LogError("[SkillEvolutionLinker] インスタンスが存在しません。");
            return false;
        }

        return linker.CheckAndExecuteEvolutionInternal(currentSkillId);
    }

    /// <summary>
    /// 基礎スキルから明示的な発展スキル ID へ差し替えます（分岐選択 UI 用）。
    /// </summary>
    public static bool CheckAndExecuteEvolutionToTarget(
        string currentSkillId,
        string targetEvolutionSkillId,
        int currentSkillLevel,
        PlayerSkillSlotManager skillSlots = null)
    {
        SkillEvolutionLinker linker = Instance ?? UnityEngine.Object.FindAnyObjectByType<SkillEvolutionLinker>();
        if (linker == null)
        {
            Debug.LogError("[SkillEvolutionLinker] インスタンスが存在しません。");
            return false;
        }

        return linker.CheckAndExecuteEvolutionToTargetInternal(
            currentSkillId,
            targetEvolutionSkillId,
            currentSkillLevel,
            skillSlots);
    }

    private bool CheckAndExecuteEvolutionInternal(string currentSkillId)
    {
        ResolveReferences();

        if (string.IsNullOrWhiteSpace(currentSkillId))
        {
            Debug.LogWarning("[SkillEvolutionLinker] currentSkillId が空のため進化判定をスキップしました。");
            return false;
        }

        try
        {
            if (playerSkillSlotManager == null)
            {
                Debug.LogError("[SkillEvolutionLinker] PlayerSkillSlotManager が見つかりません。");
                return false;
            }

            if (skillMasterRepository == null)
            {
                Debug.LogError("[SkillEvolutionLinker] SkillMasterRepository が見つかりません。");
                return false;
            }

            if (!skillMasterRepository.TryGet(currentSkillId, out SkillMaster currentMaster) ||
                currentMaster == null)
            {
                Debug.LogError($"[SkillEvolutionLinker] 現在スキルのマスター未検出: {currentSkillId}");
                return false;
            }

            SkillEvolutionData evolution = currentMaster.evolutionData;
            if (evolution == null || !evolution.isEvolvable)
            {
                return false;
            }

            evolution.Sanitize(currentMaster.skillId);
            if (!evolution.HasNextSkill)
            {
                return false;
            }

            if (!playerSkillSlotManager.TryGetSkillLevel(currentSkillId, out int currentSkillLevel))
            {
                Debug.LogError($"[SkillEvolutionLinker] プレイヤー未所持スキルのため進化不可: {currentSkillId}");
                return false;
            }

            return CheckAndExecuteEvolutionToTargetInternal(
                currentSkillId,
                evolution.nextSkillId,
                currentSkillLevel,
                playerSkillSlotManager);
        }
        catch (Exception exception)
        {
            Debug.LogError(
                $"[SkillEvolutionLinker] 進化実行中に例外が発生しました: {currentSkillId}\n{exception}");
            return false;
        }
    }

    internal bool CheckAndExecuteEvolutionToTargetInternal(
        string currentSkillId,
        string targetSkillId,
        int currentSkillLevel,
        PlayerSkillSlotManager slots = null)
    {
        ResolveReferences();
        slots ??= playerSkillSlotManager;

        if (slots == null || skillMasterRepository == null)
        {
            return false;
        }

        if (!skillMasterRepository.TryGet(currentSkillId, out SkillMaster currentMaster) ||
            currentMaster == null)
        {
            return false;
        }

        if (!skillMasterRepository.TryGet(targetSkillId, out SkillMaster nextMaster) ||
            nextMaster == null)
        {
            Debug.LogError(
                $"[SkillEvolutionLinker] 進化先スキルが存在しません: {targetSkillId} (from {currentSkillId})");
            return false;
        }

        SkillEvolutionData gate = nextMaster.evolutionData ?? new SkillEvolutionData();
        gate.Sanitize(nextMaster.skillId);

        if (currentSkillLevel < gate.requiredLevel)
        {
            return false;
        }

        if (!ResolveConditionFlagSatisfied(gate.conditionFlag))
        {
            return false;
        }

        if (!ResolveEvolutionCriteriaSatisfied(gate.evolutionCriteria))
        {
            return false;
        }

        if (!slots.TryGetSkillLevel(currentSkillId, out int ownedLevel))
        {
            return false;
        }

        SkillCategory nextCategory = ResolveSkillCategory(nextMaster.category);
        int evolvedLevel = Mathf.Max(ownedLevel, gate.requiredLevel);

        bool replaced = slots.TryReplaceOwnedSkillForEvolution(
            currentSkillId,
            nextMaster.skillId,
            nextMaster.skillName,
            nextCategory,
            evolvedLevel,
            nextMaster);

        if (!replaced)
        {
            Debug.LogError(
                $"[SkillEvolutionLinker] スキル差し替えに失敗しました: {currentSkillId} -> {nextMaster.skillId}");
            return false;
        }

        Debug.Log(
            $"[SkillEvolutionLinker] 進化成功: {currentMaster.skillName}({currentSkillId}) -> " +
            $"{nextMaster.skillName}({nextMaster.skillId})");
        SkillEvolved?.Invoke(currentSkillId, nextMaster.skillId);
        return true;
    }

    private void ResolveReferences()
    {
        if (playerSkillSlotManager == null)
        {
            playerSkillSlotManager = FindAnyObjectByType<PlayerSkillSlotManager>();
        }

        if (playerStatusManager == null)
        {
            playerStatusManager = PlayerStatusManager.Instance ?? FindAnyObjectByType<PlayerStatusManager>();
        }

        if (skillMasterRepository == null)
        {
            skillMasterRepository = SkillMasterRepository.Instance ?? FindAnyObjectByType<SkillMasterRepository>();
        }
    }

    private bool ResolveConditionFlagSatisfied(string conditionFlag)
    {
        if (string.IsNullOrWhiteSpace(conditionFlag))
        {
            return true;
        }

        if (ConditionFlagResolver == null)
        {
            Debug.LogWarning(
                $"[SkillEvolutionLinker] conditionFlagResolver 未設定のため条件を満たせません: {conditionFlag}");
            return false;
        }

        try
        {
            return ConditionFlagResolver(conditionFlag);
        }
        catch (Exception exception)
        {
            Debug.LogError(
                $"[SkillEvolutionLinker] conditionFlag 判定例外: {conditionFlag}\n{exception}");
            return false;
        }
    }

    /// <summary>戦歴ログ等の evolutionCriteria を判定します。</summary>
    public static Func<string, int> LogCountResolver { get; set; }

    private static bool ResolveEvolutionCriteriaSatisfied(SkillEvolutionCriteria criteria)
    {
        return EvolutionLogScanner.IsCriteriaMet(criteria, PlayerHistoryTracker.Instance);
    }

    private static SkillCategory ResolveSkillCategory(string masterCategory)
    {
        if (string.Equals(masterCategory, SkillMasterCategories.Utility, StringComparison.OrdinalIgnoreCase))
        {
            return SkillCategory.Evade;
        }

        if (string.Equals(masterCategory, SkillMasterCategories.Production, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(masterCategory, SkillMasterCategories.Hobby, StringComparison.OrdinalIgnoreCase))
        {
            return SkillCategory.Magic;
        }

        return SkillCategory.Attack;
    }
}

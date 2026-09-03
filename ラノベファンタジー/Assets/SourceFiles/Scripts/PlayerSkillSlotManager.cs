using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// プレイヤーが所持するスキルと、枠オーバー時のストック技を管理します。
/// PlayerRobot にアタッチし、InspirationManager から閃き技を受け取ります。
/// </summary>
public class PlayerSkillSlotManager : MonoBehaviour
{
    /// <summary>プレイヤーが同時に所持できるスキル（器）の最大数。</summary>
    public const int MaxOwnedSkillSlots = 4;

    [Header("所持スキル")]
    [SerializeField] private List<SkillData> ownedSkills = new List<SkillData>();

    [Header("ストック（スキル枠満杯時に溜まる未装着技）")]
    [SerializeField] private List<ActionData> stockActions = new List<ActionData>();

    [Header("初期化")]
    [SerializeField] private bool initializeDefaultSkillsOnStart = true;

    [Header("連携（任意）")]
    [SerializeField] private PlayerController playerController;

    public IReadOnlyList<SkillData> OwnedSkills => ownedSkills;
    public IReadOnlyList<ActionData> StockActions => stockActions;

    private void Awake()
    {
        if (playerController == null)
        {
            playerController = GetComponent<PlayerController>();
        }
    }

    private void Start()
    {
        if (initializeDefaultSkillsOnStart && ownedSkills.Count == 0)
        {
            InitializeDefaultSkills();
        }
    }

    /// <summary>
    /// ゲーム開始時のデフォルトスキル・基本技を構築します。
    /// </summary>
    public void InitializeDefaultSkills()
    {
        ownedSkills.Clear();
        stockActions.Clear();

        SkillData sword = new SkillData
        {
            skillID = SkillIds.OneHandSword,
            skillName = "片手剣術",
            category = SkillCategory.Attack,
            maxSlots = 4,
            equippedActions = new List<ActionData>
            {
                CreateBasicAction(ActionIds.BasicSlash, "通常斬り", 1f, 20f, 0.3f, 0f),
                CreateBasicAction(ActionIds.StrongStrike, "強撃", 1.3f, 25f, 0.35f, 0f)
            }
        };

        SkillData evade = new SkillData
        {
            skillID = SkillIds.EvadeArt,
            skillName = "回避術",
            category = SkillCategory.Evade,
            maxSlots = 4,
            equippedActions = new List<ActionData>
            {
                CreateBasicAction(ActionIds.BasicStep, "ステップ", 0f, 20f, 0.25f, 0f)
            }
        };

        SkillData fire = new SkillData
        {
            skillID = SkillIds.FireMagic,
            skillName = "初級火魔法",
            category = SkillCategory.Magic,
            maxSlots = 4,
            equippedActions = new List<ActionData>
            {
                CreateBasicAction(ActionIds.FireSpark, "火の粉", 0.8f, 15f, 0.2f, 0f)
            }
        };

        ownedSkills.Add(sword);
        ownedSkills.Add(evade);
        ownedSkills.Add(fire);

        Debug.Log("[PlayerSkillSlotManager] デフォルトスキルを初期化しました。");
    }

    /// <summary>
    /// 閃き技を指定スキルへ追加します。枠が満杯ならストックへ送ります。
    /// </summary>
    public bool InspireActionToSkill(string skillId, ActionData inspiredAction)
    {
        if (inspiredAction == null || !inspiredAction.IsValid())
        {
            Debug.LogWarning("[PlayerSkillSlotManager] 無効な技データです。");
            return false;
        }

        inspiredAction.isDerived = true;

        if (IsActionOwnedAnywhere(inspiredAction.actionID))
        {
            Debug.Log($"[PlayerSkillSlotManager] 既に所持済みの技です: {inspiredAction.actionName}");
            return false;
        }

        SkillData skill = FindSkill(skillId);
        if (skill == null)
        {
            Debug.LogWarning($"[PlayerSkillSlotManager] スキルが見つかりません: {skillId}");
            return false;
        }

        if (skill.TryEquip(inspiredAction))
        {
            Debug.Log($"[閃き] 【{skill.skillName}】に「{inspiredAction.actionName}」を装着しました。");
            SyncActiveActionsToPlayerController();
            return true;
        }

        Debug.Log($"[PlayerSkillSlotManager] スキル枠が満杯のため【{skill.skillName}】→ ストックへ送ります。");
        stockActions.Add(inspiredAction.Clone());
        Debug.Log($"[閃き] 「{inspiredAction.actionName}」をストックに追加しました。（ストック数: {stockActions.Count}）");
        return true;
    }

    /// <summary>カテゴリーに対応するスキルから戦闘用の代表技を取得</summary>
    public bool TryGetActiveActionByCategory(SkillCategory category, out ActionData action)
    {
        foreach (SkillData skill in ownedSkills)
        {
            if (skill != null && skill.category == category && skill.TryGetActiveAction(out action))
            {
                return true;
            }
        }

        action = null;
        return false;
    }

    public SkillData FindSkill(string skillId)
    {
        foreach (SkillData skill in ownedSkills)
        {
            if (skill != null && skill.skillID == skillId)
            {
                return skill;
            }
        }

        return null;
    }

    public bool TryFindEquippedAction(string skillId, string actionId, out ActionData action)
    {
        SkillData skill = FindSkill(skillId);
        if (skill == null)
        {
            action = null;
            return false;
        }

        foreach (ActionData equipped in skill.equippedActions)
        {
            if (equipped != null && equipped.actionID == actionId)
            {
                action = equipped;
                return true;
            }
        }

        action = null;
        return false;
    }

    public bool IsActionOwnedAnywhere(string actionId)
    {
        foreach (SkillData skill in ownedSkills)
        {
            if (skill != null && skill.ContainsAction(actionId))
            {
                return true;
            }
        }

        foreach (ActionData stock in stockActions)
        {
            if (stock != null && stock.actionID == actionId)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>指定スキル（器）を所持しているか。</summary>
    public bool OwnsSkill(string skillId)
    {
        return FindSkill(skillId) != null;
    }

    /// <summary>
    /// スキル全体進化（SkillEvolutionData.nextSkillId）専用の差し替え処理です。
    /// 合成や技ツリー派生とは別経路で、所持枠インデックスを維持したまま器を交換します。
    /// </summary>
    public bool TryReplaceOwnedSkillForEvolution(
        string currentSkillId,
        string nextSkillId,
        string nextSkillName,
        SkillCategory nextCategory,
        int nextSkillLevel,
        SkillMaster nextMaster = null,
        ActionData initialAction = null)
    {
        if (string.IsNullOrWhiteSpace(currentSkillId) ||
            string.IsNullOrWhiteSpace(nextSkillId))
        {
            Debug.LogWarning("[PlayerSkillSlotManager] 進化差し替え失敗: skillId が空です。");
            return false;
        }

        int index = FindOwnedSkillIndex(currentSkillId);
        if (index < 0)
        {
            Debug.LogWarning($"[PlayerSkillSlotManager] 進化差し替え失敗: 未所持 {currentSkillId}");
            return false;
        }

        if (OwnsSkill(nextSkillId))
        {
            Debug.LogWarning($"[PlayerSkillSlotManager] 進化差し替え失敗: 進化先は既に所持 {nextSkillId}");
            return false;
        }

        SkillData evolved = CreateSkillTemplate(nextSkillId);
        evolved.skillName = string.IsNullOrWhiteSpace(nextSkillName) ? nextSkillId : nextSkillName;
        evolved.category = nextCategory;
        evolved.skillLevel = Mathf.Max(1, nextSkillLevel);

        if (nextMaster != null)
        {
            TryEquipAllUnlockedArtsFromMaster(evolved, nextMaster);
        }
        else if (initialAction != null && initialAction.IsValid())
        {
            evolved.TryEquip(initialAction);
        }

        ownedSkills[index] = evolved;
        SyncActiveActionsToPlayerController();
        return true;
    }

    /// <summary>自動テスト用：所持スキルに追加します（同一 skillID は追加しません）。</summary>
    public bool TryAddOwnedSkillForTesting(SkillData skill)
    {
        if (skill == null || string.IsNullOrWhiteSpace(skill.skillID))
        {
            return false;
        }

        if (OwnsSkill(skill.skillID))
        {
            return false;
        }

        if (ownedSkills.Count >= MaxOwnedSkillSlots)
        {
            return false;
        }

        ownedSkills.Add(skill);
        return true;
    }

    /// <summary>
    /// SkillMasterRepository に実在するスキル ID を所持枠へ付与します（ジョブ初期スキル用）。
    /// 未登録 ID は警告を出して false を返します（ハルシネーション防御）。
    /// </summary>
    public bool TryGrantSkillFromMasterId(string skillId, SkillMasterRepository repository = null)
    {
        if (string.IsNullOrWhiteSpace(skillId))
        {
            Debug.LogWarning("[PlayerSkillSlotManager] 付与スキル ID が空です。");
            return false;
        }

        if (OwnsSkill(skillId))
        {
            return true;
        }

        repository ??= SkillMasterRepository.EnsureInstance();
        if (repository == null)
        {
            Debug.LogError("[PlayerSkillSlotManager] SkillMasterRepository が未初期化です。");
            return false;
        }

        if (!repository.TryGet(skillId, out SkillMaster master) || master == null)
        {
            MasterDataManager masterData = MasterDataManager.EnsureInstance();
            if (masterData != null && masterData.TryGetSkill(skillId, out SkillMaster fromMaster))
            {
                repository.RegisterSkillMaster(fromMaster, $"MasterDataManager:{skillId}");
                master = fromMaster;
            }
        }

        if (master == null || !master.IsValid())
        {
            Debug.LogWarning(
                $"[PlayerSkillSlotManager] 未登録スキル ID '{skillId}' — 付与をスキップしました（ハルシネーションの可能性）");
            return false;
        }

        if (ownedSkills.Count >= MaxOwnedSkillSlots)
        {
            Debug.LogWarning(
                $"[PlayerSkillSlotManager] スキル枠満杯のため '{skillId}' を付与できません。");
            return false;
        }

        SkillData skill = CreateSkillDataFromSkillMaster(master);
        int equippedCount = TryEquipAllUnlockedArtsFromMaster(skill, master);
        if (equippedCount == 0)
        {
            Debug.LogWarning(
                $"[PlayerSkillSlotManager] '{skillId}' に解放済み技がありません。マスターの unlocked フラグを確認してください。");
        }

        ownedSkills.Add(skill);
        SyncActiveActionsToPlayerController();
        Debug.Log(
            $"[PlayerSkillSlotManager] マスターからスキル付与: 『{master.skillName}』（{skillId}）");
        return true;
    }

    /// <summary>SkillMaster から SkillData（器）を生成します。</summary>
    public static SkillData CreateSkillDataFromSkillMaster(SkillMaster master)
    {
        return new SkillData
        {
            skillID = master.skillId,
            skillName = master.skillName,
            category = ResolveCategoryFromSkillMaster(master.category),
            maxSlots = 4,
            skillLevel = 1,
            equippedActions = new List<ActionData>()
        };
    }

    /// <summary>
    /// SkillMaster の解放済み技ツリー全体を再帰走査し、ActionData へ変換してスキル枠へ装着します。
    /// </summary>
    /// <returns>実際に装着できた技の数</returns>
    public static int TryEquipAllUnlockedArtsFromMaster(SkillData skill, SkillMaster master)
    {
        if (skill == null || master == null)
        {
            return 0;
        }

        List<ArtsData> unlockedArts = master.CollectUnlockedArts();
        int equippedCount = 0;
        for (int i = 0; i < unlockedArts.Count; i++)
        {
            ArtsData art = unlockedArts[i];
            if (art == null)
            {
                continue;
            }

            bool isRoot = IsRootArt(master, art);
            ActionData action = CreateActionDataFromArts(art, master, isDerived: !isRoot);
            if (action != null && action.IsValid() && skill.TryEquip(action))
            {
                equippedCount++;
            }
        }

        return equippedCount;
    }

    /// <summary>ルート技（baseArts[0]）かどうかを artId で判定します。</summary>
    public static bool IsRootArt(SkillMaster master, ArtsData art)
    {
        if (master?.baseArts == null || master.baseArts.Count == 0 || art == null)
        {
            return false;
        }

        ArtsData root = master.baseArts[0];
        if (root == null)
        {
            return false;
        }

        return string.Equals(root.artId, art.artId, System.StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>ArtsData 1 件を ActionData へ型安全に変換します。</summary>
    public static ActionData CreateActionDataFromArts(ArtsData art, SkillMaster master, bool isDerived)
    {
        if (art == null || master == null)
        {
            return null;
        }

        float damageMul = 1f + Mathf.Clamp(art.poiseDamage, 0, 100) * 0.01f;
        string actionId = string.IsNullOrWhiteSpace(art.artId)
            ? $"action_{master.skillId}_{(isDerived ? "derived" : "root")}"
            : art.artId;

        return new ActionData
        {
            actionID = actionId,
            actionName = string.IsNullOrWhiteSpace(art.artName) ? $"{master.skillName}・技" : art.artName,
            damageMultiplier = damageMul,
            staminaCost = Mathf.Max(5f, art.manaCost),
            activeDetectionTime = 0.28f,
            invincibilityTime = 0f,
            isDerived = isDerived,
            inspirationSource = isDerived
                ? $"ArtsTree:{master.skillId}/{art.artId}"
                : $"JobGrant:{master.skillId}"
        };
    }

    /// <summary>SkillMaster の root 技から基礎 ActionData を生成します（後方互換）。</summary>
    public static ActionData CreateFoundationActionFromSkillMaster(SkillMaster master)
    {
        if (master?.baseArts == null || master.baseArts.Count == 0 || master.baseArts[0] == null)
        {
            return null;
        }

        return CreateActionDataFromArts(master.baseArts[0], master, isDerived: false);
    }

    /// <summary>
    /// 所持スキルの装着済み ActionData に対応する ArtsData をマスターから逆引きして収集します。
    /// 戦闘ヒット時の specialEffects 適用に利用します。
    /// </summary>
    public List<ArtsData> CollectEquippedArtsMetadata(SkillMasterRepository repository = null)
    {
        List<ArtsData> result = new List<ArtsData>();
        repository ??= SkillMasterRepository.EnsureInstance();
        if (repository == null)
        {
            return result;
        }

        for (int i = 0; i < ownedSkills.Count; i++)
        {
            SkillData skill = ownedSkills[i];
            if (skill == null || string.IsNullOrWhiteSpace(skill.skillID))
            {
                continue;
            }

            if (!repository.TryGet(skill.skillID, out SkillMaster master) || master == null)
            {
                continue;
            }

            if (skill.equippedActions == null)
            {
                continue;
            }

            for (int j = 0; j < skill.equippedActions.Count; j++)
            {
                ActionData equipped = skill.equippedActions[j];
                if (equipped == null || string.IsNullOrWhiteSpace(equipped.actionID))
                {
                    continue;
                }

                ArtsData art = master.FindArt(equipped.actionID);
                if (art != null)
                {
                    result.Add(art);
                }
            }
        }

        return result;
    }

    /// <summary>actionID から ArtsData と所属 SkillMaster を逆引きします。</summary>
    public bool TryResolveArtsDataForAction(
        string actionId,
        out ArtsData art,
        out SkillMaster master,
        SkillMasterRepository repository = null)
    {
        art = null;
        master = null;
        if (string.IsNullOrWhiteSpace(actionId))
        {
            return false;
        }

        repository ??= SkillMasterRepository.EnsureInstance();
        if (repository == null)
        {
            return false;
        }

        for (int i = 0; i < ownedSkills.Count; i++)
        {
            SkillData skill = ownedSkills[i];
            if (skill == null || string.IsNullOrWhiteSpace(skill.skillID))
            {
                continue;
            }

            if (!repository.TryGet(skill.skillID, out SkillMaster candidate) || candidate == null)
            {
                continue;
            }

            ArtsData found = candidate.FindArt(actionId);
            if (found != null)
            {
                art = found;
                master = candidate;
                return true;
            }
        }

        return false;
    }

    private static SkillCategory ResolveCategoryFromSkillMaster(string masterCategory)
    {
        if (string.Equals(masterCategory, SkillMasterCategories.Utility, System.StringComparison.OrdinalIgnoreCase))
        {
            return SkillCategory.Evade;
        }

        if (string.Equals(masterCategory, SkillMasterCategories.Production, System.StringComparison.OrdinalIgnoreCase) ||
            string.Equals(masterCategory, SkillMasterCategories.Hobby, System.StringComparison.OrdinalIgnoreCase))
        {
            return SkillCategory.Magic;
        }

        return SkillCategory.Attack;
    }

    /// <summary>
    /// AI が生成した基礎スキル（BaseSkill）を所持枠へ新規追加します。
    /// </summary>
    /// <param name="baseSkill">パース済み BaseSkill データ</param>
    /// <param name="historyLog">戦歴（カテゴリー推定用・任意）</param>
    public BaseSkillEquipResult TryAcquireAIBaseSkill(
        AIGeneratedBaseSkillData baseSkill,
        PlayerHistoryLog historyLog = null)
    {
        if (baseSkill == null || !baseSkill.IsValid())
        {
            return BaseSkillEquipResult.Failure("BaseSkill データが不完全です。パース失敗、リトライ可能。");
        }

        if (OwnsSkill(baseSkill.SkillId))
        {
            return BaseSkillEquipResult.Failure(
                $"既に所持している基礎スキルです: {baseSkill.SkillId}");
        }

        if (ownedSkills.Count >= MaxOwnedSkillSlots)
        {
            return BaseSkillEquipResult.Failure(
                $"スキル枠が満杯です（最大 {MaxOwnedSkillSlots}）。空き枠を確保してからリトライしてください。");
        }

        SkillData skill = CreateSkillDataFromBaseSkill(baseSkill, historyLog);
        int slotIndex = ownedSkills.Count;
        ownedSkills.Add(skill);

        ActionData foundation = CreateFoundationActionFromBaseSkill(baseSkill);
        if (skill.TryEquip(foundation))
        {
            Debug.Log(
                $"[PlayerSkillSlotManager] 基礎技法「{foundation.actionName}」を【{skill.skillName}】に装着しました。");
        }

        SyncActiveActionsToPlayerController();
        return BaseSkillEquipResult.Success(
            slotIndex,
            $"スロット[{slotIndex}]へ基礎スキル『{baseSkill.SkillName}』を付与しました。");
    }

    /// <summary>BaseSkill から SkillData（器）を生成します。</summary>
    public static SkillData CreateSkillDataFromBaseSkill(
        AIGeneratedBaseSkillData baseSkill,
        PlayerHistoryLog historyLog)
    {
        return new SkillData
        {
            skillID = baseSkill.SkillId,
            skillName = baseSkill.SkillName,
            category = ResolveCategoryForBaseSkill(baseSkill, historyLog),
            maxSlots = 4,
            skillLevel = 1,
            equippedActions = new List<ActionData>()
        };
    }

    /// <summary>BaseSkill の基礎技（最初の1枠）を生成します。</summary>
    public static ActionData CreateFoundationActionFromBaseSkill(AIGeneratedBaseSkillData baseSkill)
    {
        float damageMultiplier = 1f;
        string bonusKey = baseSkill.TargetBonus?.Trim().ToLowerInvariant() ?? string.Empty;
        if (bonusKey is "damagebonus" or "strengthbonus")
        {
            damageMultiplier = 1f + Mathf.Max(0f, baseSkill.BonusValue) * 0.05f;
        }

        return new ActionData
        {
            actionID = $"action_{baseSkill.SkillId}_foundation",
            actionName = $"{baseSkill.SkillName}・基礎形",
            damageMultiplier = damageMultiplier,
            staminaCost = 18f,
            activeDetectionTime = bonusKey is "speedbonus" or "evasionbonus" ? 0.22f : 0.32f,
            invincibilityTime = 0f,
            isDerived = false,
            inspirationSource = baseSkill.Description
        };
    }

    private static SkillCategory ResolveCategoryForBaseSkill(
        AIGeneratedBaseSkillData baseSkill,
        PlayerHistoryLog historyLog)
    {
        string bonusKey = baseSkill.TargetBonus?.Trim().ToLowerInvariant() ?? string.Empty;
        switch (bonusKey)
        {
            case "speedbonus":
            case "evasionbonus":
                return SkillCategory.Evade;
            case "magicbonus":
            case "manabonus":
                return SkillCategory.Magic;
            default:
                break;
        }

        if (historyLog != null)
        {
            if (historyLog.TotalThrustHits >= historyLog.TotalSlashHits &&
                historyLog.TotalThrustHits >= historyLog.TotalStrikeHits)
            {
                return SkillCategory.Attack;
            }

            if (historyLog.TotalPerfectEvades >= historyLog.TotalSlashHits)
            {
                return SkillCategory.Evade;
            }
        }

        return SkillCategory.Attack;
    }

    /// <summary>自動テスト用：舞踏術スキルを追加します。</summary>
    public void AddDanceArtSkillForTesting()
    {
        TryAddOwnedSkillForTesting(CreateDanceArtSkill());
    }

    /// <summary>舞踏術スキルのテンプレートを生成します。</summary>
    public static SkillData CreateDanceArtSkill()
    {
        return new SkillData
        {
            skillID = SkillIds.DanceArt,
            skillName = "舞踏術",
            category = SkillCategory.Attack,
            maxSlots = 4,
            skillLevel = 1,
            equippedActions = new List<ActionData>()
        };
    }

    /// <summary>自動テスト用：ストックに指定 ID の技があるか</summary>
    public bool IsActionInStock(string actionId)
    {
        foreach (ActionData stock in stockActions)
        {
            if (stock != null && stock.actionID == actionId)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>自動テスト用：デフォルトスキル構成へ戻す</summary>
    public void ResetToDefaultForTesting()
    {
        InitializeDefaultSkills();
    }

    /// <summary>
    /// 専門職 NPC がスキルオーブをプレイヤーへ装着します（パターンA）。
    /// 未所持スキルなら新規獲得、所持済みならレベル更新＋銘入り技を追加します。
    /// </summary>
    public bool TryApplyOrbInstallation(SkillOrbData orb)
    {
        if (orb == null || !orb.IsValid())
        {
            Debug.LogWarning("[PlayerSkillSlotManager] 無効なオーブです。");
            return false;
        }

        SkillData existing = FindSkill(orb.skillID);
        if (existing == null)
        {
            SkillData newSkill = CreateSkillFromOrb(orb);
            ownedSkills.Add(newSkill);
            SkillShopLog.LogOrbApplied(orb, newSkill.skillName);
            return true;
        }

        existing.skillLevel = Mathf.Max(existing.skillLevel, orb.skillLevelAtExtraction);
        ActionData inscribed = CreateInscribedActionFromOrb(orb);
        bool added = InspireActionToSkill(orb.skillID, inscribed);
        if (added)
        {
            SkillShopLog.LogOrbApplied(orb, existing.skillName);
        }

        return added;
    }

    /// <summary>合成で生成された新スキル（器）をプレイヤーに付与します。</summary>
    public bool TryAcquireCombinedSkill(SkillOrbCombinationRecipe recipe)
    {
        if (recipe == null || !recipe.IsValid())
        {
            return false;
        }

        if (OwnsSkill(recipe.resultSkillId))
        {
            Debug.LogWarning($"[PlayerSkillSlotManager] 合成スキルは既に所持済み: {recipe.resultSkillName}");
            return false;
        }

        SkillData fused = CreateCombinedSkill(recipe);
        ownedSkills.Add(fused);
        return true;
    }

    /// <summary>オーブから新規スキル（器）のテンプレートを生成します。</summary>
    public static SkillData CreateSkillFromOrb(SkillOrbData orb)
    {
        SkillData template = CreateSkillTemplate(orb.skillID);
        template.skillLevel = Mathf.Max(1, orb.skillLevelAtExtraction);
        template.equippedActions.Add(CreateInscribedActionFromOrb(orb));
        return template;
    }

    /// <summary>既知スキル ID から器テンプレートを生成します。</summary>
    public static SkillData CreateSkillTemplate(string skillId)
    {
        switch (skillId)
        {
            case SkillIds.OneHandSword:
                return new SkillData
                {
                    skillID = SkillIds.OneHandSword,
                    skillName = "片手剣術",
                    category = SkillCategory.Attack,
                    maxSlots = 4,
                    skillLevel = 1,
                    equippedActions = new List<ActionData>()
                };
            case SkillIds.EvadeArt:
                return new SkillData
                {
                    skillID = SkillIds.EvadeArt,
                    skillName = "回避術",
                    category = SkillCategory.Evade,
                    maxSlots = 4,
                    skillLevel = 1,
                    equippedActions = new List<ActionData>()
                };
            case SkillIds.FireMagic:
                return new SkillData
                {
                    skillID = SkillIds.FireMagic,
                    skillName = "初級火魔法",
                    category = SkillCategory.Magic,
                    maxSlots = 4,
                    skillLevel = 1,
                    equippedActions = new List<ActionData>()
                };
            case SkillIds.DanceArt:
                return CreateDanceArtSkill();
            case SkillIds.BladeDanceFusion:
                return new SkillData
                {
                    skillID = SkillIds.BladeDanceFusion,
                    skillName = "剣舞融合術",
                    category = SkillCategory.Attack,
                    maxSlots = 4,
                    skillLevel = 1,
                    equippedActions = new List<ActionData>()
                };
            default:
                return new SkillData
                {
                    skillID = skillId,
                    skillName = skillId,
                    category = SkillCategory.Attack,
                    maxSlots = 4,
                    skillLevel = 1,
                    equippedActions = new List<ActionData>()
                };
        }
    }

    /// <summary>オーブの銘入り技を ActionData として生成します。</summary>
    public static ActionData CreateInscribedActionFromOrb(SkillOrbData orb)
    {
        string safeCreator = string.IsNullOrWhiteSpace(orb.creatorName) ? "名無し" : orb.creatorName;
        string actionId = $"action_orb_{orb.skillID}_{safeCreator.GetHashCode():X}";

        float damageMultiplier = 1f + orb.rarity * 0.1f;
        if (orb.skillID == SkillIds.EvadeArt)
        {
            damageMultiplier = 0f;
        }

        return new ActionData
        {
            actionID = actionId,
            actionName = $"銘・{safeCreator}の技",
            damageMultiplier = damageMultiplier,
            staminaCost = 15f + orb.rarity * 3f,
            activeDetectionTime = 0.3f,
            invincibilityTime = orb.skillID == SkillIds.EvadeArt ? 0.1f : 0f,
            isDerived = true,
            inspirationSource = $"{safeCreator} 銘のスキルオーブ（Lv.{orb.skillLevelAtExtraction} / ★{orb.rarity}）"
        };
    }

    private static SkillData CreateCombinedSkill(SkillOrbCombinationRecipe recipe)
    {
        SkillData skill = CreateSkillTemplate(recipe.resultSkillId);
        skill.skillName = recipe.resultSkillName;
        skill.category = recipe.resultCategory;
        skill.skillLevel = 2;
        skill.equippedActions.Add(new ActionData
        {
            actionID = $"action_fusion_{recipe.resultSkillId}",
            actionName = $"{recipe.resultSkillName}・開眼",
            damageMultiplier = 1.8f,
            staminaCost = 28f,
            activeDetectionTime = 0.4f,
            invincibilityTime = 0f,
            isDerived = true,
            inspirationSource = "専門職による2オーブ融合で誕生した上位スキル"
        });
        return skill;
    }

    /// <summary>自動テスト用：所持オーブ適用後のスキル数を返します。</summary>
    public int GetOwnedSkillCountForTesting()
    {
        return ownedSkills.Count;
    }

    /// <summary>
    /// 閃き技を PlayerController の既存派生技リストへ同期（戦闘への即時反映用）。
    /// </summary>
    public void SyncActiveActionsToPlayerController()
    {
        if (playerController == null)
        {
            return;
        }

        if (TryGetActiveActionByCategory(SkillCategory.Attack, out ActionData attack))
        {
            playerController.UnlockNewAction(attack.ToDerivedActionData(SkillCategory.Attack));
        }

        if (TryGetActiveActionByCategory(SkillCategory.Evade, out ActionData evade))
        {
            playerController.UnlockNewAction(evade.ToDerivedActionData(SkillCategory.Evade));
        }
    }

    /// <summary>指定スキルの現在レベルを取得します。</summary>
    public bool TryGetSkillLevel(string skillId, out int skillLevel)
    {
        SkillData skill = FindSkill(skillId);
        if (skill == null)
        {
            skillLevel = 0;
            return false;
        }

        skillLevel = Mathf.Max(1, skill.skillLevel);
        return true;
    }

    private int FindOwnedSkillIndex(string skillId)
    {
        for (int i = 0; i < ownedSkills.Count; i++)
        {
            SkillData skill = ownedSkills[i];
            if (skill != null && skill.skillID == skillId)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// オーブ抽出のためスキルレベルを 1 下げます（最低 1 を維持）。
    /// 戻り値の skillLevelAtExtraction は抽出前のレベルです。
    /// </summary>
    public bool TrySacrificeSkillLevelForOrb(string skillId, out int skillLevelAtExtraction)
    {
        SkillData skill = FindSkill(skillId);
        if (skill == null)
        {
            skillLevelAtExtraction = 0;
            return false;
        }

        skillLevelAtExtraction = Mathf.Max(1, skill.skillLevel);
        skill.skillLevel = Mathf.Max(1, skill.skillLevel - 1);
        Debug.Log(
            $"[PlayerSkillSlotManager] オーブ抽出: 【{skill.skillName}】 Lv.{skillLevelAtExtraction} → Lv.{skill.skillLevel}");
        return true;
    }

    /// <summary>自動テスト用：スキルレベルを直接設定します。</summary>
    public void SetSkillLevelForTesting(string skillId, int skillLevel)
    {
        SkillData skill = FindSkill(skillId);
        if (skill != null)
        {
            skill.skillLevel = Mathf.Max(1, skillLevel);
        }
    }

    public string GetSkillSummary()
    {
        System.Text.StringBuilder builder = new System.Text.StringBuilder();
        builder.AppendLine("[所持スキル]");
        foreach (SkillData skill in ownedSkills)
        {
            if (skill == null)
            {
                continue;
            }

            builder.AppendLine($"  【{skill.skillName}】 Lv.{skill.skillLevel} {skill.EquippedCount}/{skill.maxSlots}");
            foreach (ActionData action in skill.equippedActions)
            {
                if (action != null)
                {
                    builder.AppendLine($"    - {action}");
                }
            }
        }

        builder.AppendLine($"[ストック] {stockActions.Count}件");
        foreach (ActionData stock in stockActions)
        {
            if (stock != null)
            {
                builder.AppendLine($"  - {stock}");
            }
        }

        return builder.ToString();
    }

    private static ActionData CreateBasicAction(
        string id, string name, float dmgMul, float stamina, float activeTime, float invincible)
    {
        return new ActionData
        {
            actionID = id,
            actionName = name,
            damageMultiplier = dmgMul,
            staminaCost = stamina,
            activeDetectionTime = activeTime,
            invincibilityTime = invincible,
            isDerived = false,
            inspirationSource = "初期装備の基本技"
        };
    }
}

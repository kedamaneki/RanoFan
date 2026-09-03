using System;
using UnityEngine;

// =============================================================================
// ジョブ進化（転職）・初期スキル自動付与
// 連携: MasterDataManager / PlayerStatusManager / PlayerSkillSlotManager / JobStatApplier
// =============================================================================

/// <summary>
/// ジョブ進化（転職）と就職時の初期スキル付与を統治するマネージャー。
/// </summary>
public static class JobEvolutionManager
{
    /// <summary>転職先ジョブが完了したとき（oldJobId, newJobId）。</summary>
    public static event Action<string, string> JobEvolved;

    /// <summary>
    /// unlockConditions 判定フック。未設定時は SkillEvolutionLinker.ConditionFlagResolver を流用します。
    /// </summary>
    public static Func<string, bool> UnlockConditionResolver { get; set; }

    /// <summary>
    /// 条件を満たしていれば targetJobId へ転職し、defaultSkillIds を自動付与します。
    /// </summary>
    /// <param name="targetJobId">転職先 JobMasterData.id</param>
    public static bool TryEvolveJob(
        string targetJobId,
        PlayerStatusManager playerStatus,
        PlayerSkillSlotManager skillSlotManager)
    {
        if (string.IsNullOrWhiteSpace(targetJobId))
        {
            Debug.LogWarning("[JobEvolutionManager] targetJobId が空です。");
            return false;
        }

        if (playerStatus == null)
        {
            Debug.LogError("[JobEvolutionManager] PlayerStatusManager が null です。");
            return false;
        }

        if (skillSlotManager == null)
        {
            Debug.LogError("[JobEvolutionManager] PlayerSkillSlotManager が null です。");
            return false;
        }

        MasterDataManager masterData = MasterDataManager.EnsureInstance();
        if (masterData == null)
        {
            Debug.LogError("[JobEvolutionManager] MasterDataManager を解決できません。");
            return false;
        }

        JobMasterData targetJob = masterData.GetJob(targetJobId);
        if (targetJob == null || !targetJob.IsValid())
        {
            Debug.LogError(
                $"[JobEvolutionManager] 未登録または無効なジョブ ID: '{targetJobId}'（ハルシネーションの可能性）");
            return false;
        }

        if (!EraContextResolver.AreEraUnlockFlagsMet(targetJob, ResolveUnlockCondition))
        {
            Debug.LogWarning(
                $"[JobEvolutionManager] 時代優先フラグ未達（{EraContextResolver.FormatEraLabel(EraContextResolver.CurrentEra)} / T{EraContextResolver.CurrentTurn}）のため " +
                $"'{targetJob.name}'（{targetJobId}）へ進化できません。");
            return false;
        }

        if (!AreUnlockConditionsMet(targetJob.unlockConditions))
        {
            Debug.LogWarning(
                $"[JobEvolutionManager] 転職条件未達のため '{targetJob.name}'（{targetJobId}）へ進化できません。");
            return false;
        }

        string previousJobKey = playerStatus.MainJob;

        // mainJob には表示名を設定（JobStatApplier は ID / 表示名の両方で解決可能）
        playerStatus.SetJobs(targetJob.name, playerStatus.SubJob);

        // ジョブ変更に伴い CombatStats / MP 上限を再同期
        CombatStats combatStats = playerStatus.GetComponent<CombatStats>();
        if (combatStats != null)
        {
            JobStatApplier.ApplyJobModifiers(targetJob.id, combatStats);
        }
        else
        {
            playerStatus.ApplyMainJobStatModifiers();
        }

        // 就職直後: defaultSkillIds を SkillMaster 実在確認のうえ自動付与
        int grantedCount = GrantDefaultSkills(targetJob, skillSlotManager);

        Debug.Log(
            $"<color=#81D4FA><b>【転職成功】{previousJobKey} → {targetJob.name}（{targetJobId}）</b></color> " +
            $"初期スキル付与: {grantedCount} 件");
        JobEvolved?.Invoke(previousJobKey, targetJobId);
        return true;
    }

    /// <summary>現在の mainJob から nextJobId へ進化を試みます。</summary>
    public static bool TryEvolveToNextJob(
        PlayerStatusManager playerStatus,
        PlayerSkillSlotManager skillSlotManager)
    {
        if (playerStatus == null)
        {
            return false;
        }

        if (!JobStatApplier.TryResolveJob(playerStatus.MainJob, out JobMasterData currentJob))
        {
            Debug.LogWarning(
                $"[JobEvolutionManager] 現在ジョブ '{playerStatus.MainJob}' をマスターから解決できません。");
            return false;
        }

        if (!currentJob.HasNextJob)
        {
            Debug.Log($"[JobEvolutionManager] '{currentJob.name}' に進化先（nextJobId）が定義されていません。");
            return false;
        }

        return TryEvolveJob(currentJob.nextJobId, playerStatus, skillSlotManager);
    }

    /// <summary>
    /// 現在のジョブに紐づく defaultSkillIds を付与します（ゲーム開始時の初期就職用）。
    /// unlockConditions は検証しません。
    /// </summary>
    public static int TryGrantDefaultSkillsForCurrentJob(
        PlayerStatusManager playerStatus,
        PlayerSkillSlotManager skillSlotManager)
    {
        if (playerStatus == null || skillSlotManager == null)
        {
            return 0;
        }

        if (!JobStatApplier.TryResolveJob(playerStatus.MainJob, out JobMasterData job))
        {
            return 0;
        }

        return GrantDefaultSkills(job, skillSlotManager);
    }

    /// <summary>
    /// 時代優先フラグ → 通常 unlockConditions の順で、転職可能かを判定します（転職は実行しません）。
    /// </summary>
    public static bool AreUnlockRequirementsMet(JobMasterData targetJob)
    {
        if (targetJob == null || !targetJob.IsValid())
        {
            return false;
        }

        if (!EraContextResolver.AreEraUnlockFlagsMet(targetJob, ResolveUnlockCondition))
        {
            return false;
        }

        return AreUnlockConditionsMet(targetJob.unlockConditions);
    }

    /// <summary>
    /// defaultSkillIds をループし、SkillMasterRepository で実在確認後に未所持なら付与します。
    /// </summary>
    private static int GrantDefaultSkills(JobMasterData job, PlayerSkillSlotManager skillSlotManager)
    {
        if (job?.defaultSkillIds == null || job.defaultSkillIds.Count == 0)
        {
            return 0;
        }

        int granted = 0;
        for (int i = 0; i < job.defaultSkillIds.Count; i++)
        {
            string skillId = job.defaultSkillIds[i];
            if (string.IsNullOrWhiteSpace(skillId))
            {
                continue;
            }

            if (skillSlotManager.OwnsSkill(skillId))
            {
                continue;
            }

            if (skillSlotManager.TryGrantSkillFromMasterId(skillId))
            {
                granted++;
                Debug.Log(
                    $"[JobEvolutionManager] 初期スキル付与: {skillId} ← ジョブ {job.id}");
            }
            else
            {
                Debug.LogWarning(
                    $"[JobEvolutionManager] 初期スキル付与スキップ: '{skillId}' は未登録または枠不足（ジョブ={job.id}）");
            }
        }

        return granted;
    }

    private static bool AreUnlockConditionsMet(System.Collections.Generic.List<string> conditions)
    {
        if (conditions == null || conditions.Count == 0)
        {
            return true;
        }

        for (int i = 0; i < conditions.Count; i++)
        {
            string condition = conditions[i];
            if (string.IsNullOrWhiteSpace(condition))
            {
                continue;
            }

            if (!ResolveUnlockCondition(condition))
            {
                return false;
            }
        }

        return true;
    }

    private static bool ResolveUnlockCondition(string conditionKey)
    {
        if (EraContextResolver.IsKnownEraFlagKey(conditionKey))
        {
            return EraContextResolver.IsEraConditionFlag(conditionKey);
        }

        Func<string, bool> resolver = UnlockConditionResolver ?? SkillEvolutionLinker.ConditionFlagResolver;
        if (resolver == null)
        {
            Debug.LogWarning(
                $"[JobEvolutionManager] UnlockConditionResolver 未設定のため条件を満たせません: {conditionKey}");
            return false;
        }

        try
        {
            return resolver(conditionKey);
        }
        catch (Exception exception)
        {
            Debug.LogError($"[JobEvolutionManager] 解放条件判定例外: {conditionKey}\n{exception}");
            return false;
        }
    }
}

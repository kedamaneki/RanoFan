using System;
using System.Collections.Generic;
using UnityEngine;

// =============================================================================
// 社会生活ジョブ（マスター JOB_*）の動的構築
// 連携: MasterDataManager / NpcIndividualStatus / NpcCivicJob
// =============================================================================

/// <summary>動的マスター生成のトリガー種別。</summary>
public enum DynamicMasterTrigger
{
    Innovation = 0,
    BarrierDrop = 1,
    BarrierHero = 2,
    ProductionNeed = 3,
    CivilUnrest = 4,
    AcademyRivalry = 5
}

/// <summary>ジョブ生成結果。</summary>
public sealed class DynamicJobBuildResult
{
    public bool success;
    public bool registered;
    public bool usedFallback;
    public string jobId = string.Empty;
    public string message = string.Empty;
    public JobMasterData job;
}

/// <summary>
/// 村の防衛・生産ニーズに応じた社会生活ジョブ（JOB_*）を構築し、マスターへ登録します。
/// 戦闘クラスではなく、NPC の生活・維持役割を表します。
/// </summary>
public static class DynamicJobBuilder
{
    public const string FallbackJobId = "JOB_OTHERWORLD_TRAINEE";
    public const string JobBarrierRepairer = "JOB_BARRIER_REPAIRER";
    public const string JobHighSmith = "JOB_HIGH_SMITH";
    public const string JobFieldSteward = "JOB_FIELD_STEWARD";
    public const string JobCrystalTender = "JOB_CRYSTAL_TENDER";
    public const string JobRebelLeader = "JOB_REBEL_LEADER";
    public const string JobAcademyScholar = "JOB_ACADEMY_SCHOLAR";

    private static readonly Dictionary<string, NpcCivicJob> JobIdToCivic =
        new Dictionary<string, NpcCivicJob>(StringComparer.OrdinalIgnoreCase)
        {
            { JobBarrierRepairer, NpcCivicJob.BarrierKeeper },
            { JobHighSmith, NpcCivicJob.Blacksmith },
            { JobFieldSteward, NpcCivicJob.Farmer },
            { JobCrystalTender, NpcCivicJob.BarrierKeeper },
            { JobRebelLeader, NpcCivicJob.Farmer },
            { JobAcademyScholar, NpcCivicJob.BarrierKeeper },
            { "JOB_BLACKSMITH", NpcCivicJob.Blacksmith },
            { "JOB_FARMER", NpcCivicJob.Farmer },
            { "JOB_RANCHER", NpcCivicJob.Rancher },
            { "JOB_WOODCUTTER", NpcCivicJob.Woodcutter },
            { "JOB_CARPENTER", NpcCivicJob.Carpenter }
        };

    /// <summary>トリガーに応じた社会生活ジョブを構築します。</summary>
    public static JobMasterData BuildSocialJob(DynamicMasterTrigger trigger, int nationId, int turn)
    {
        string id;
        string name;
        string description;
        StatModifiers mods = new StatModifiers();
        List<string> unlocks = new List<string>();
        List<string> defaultMagics = new List<string>();

        switch (trigger)
        {
            case DynamicMasterTrigger.Innovation:
                id = JobHighSmith;
                name = "高位鍛冶職";
                description =
                    "工房の高レベル設備で村の武具・結界資材を鍛える社会生活職。戦闘クラスではなく生産維持の役割。";
                mods.manaMultiplier = 1.05f;
                mods.strMultiplier = 1.08f;
                unlocks.Add(MicroToMacroAggregator.FlagInnovation);
                defaultMagics.Add("MAGIC_DYN_FORGE_SPIRIT");
                break;
            case DynamicMasterTrigger.BarrierDrop:
                id = JobBarrierRepairer;
                name = "結界修復職";
                description =
                    "結界破綻後に杭・核を現地修復し、魔力結晶の配分を村全体で再調整する防衛維持職。";
                mods.manaMultiplier = 1.12f;
                mods.defMultiplier = 1.05f;
                unlocks.Add(MicroToMacroAggregator.FlagBarrierDrop);
                defaultMagics.Add("MAGIC_DYN_BARRIER_SEAL");
                break;
            case DynamicMasterTrigger.BarrierHero:
                id = JobCrystalTender;
                name = "結晶調律職";
                description =
                    "休眠期に結界を満タンまで戻した結界守の伝承役。結晶循環と夜間監視を担う社会生活職。";
                mods.manaMultiplier = 1.15f;
                unlocks.Add(MicroToMacroAggregator.FlagHero);
                defaultMagics.Add("MAGIC_DYN_BARRIER_RESTORE");
                break;
            case DynamicMasterTrigger.CivilUnrest:
                id = JobRebelLeader;
                name = "暴動調停職";
                description =
                    "食糧不足や国境摩擦で起きる内乱を鎮め、配給と防衛の再配分を村で調整する社会生活職。";
                mods.strMultiplier = 1.1f;
                mods.hpMultiplier = 1.08f;
                unlocks.Add(HumanConflictEngine.FlagRebelLeader);
                defaultMagics.Add("MAGIC_DYN_FOOD_RATION");
                break;
            case DynamicMasterTrigger.AcademyRivalry:
                id = JobAcademyScholar;
                name = "学院調停職";
                description =
                    "魔導学院の派閥抗争を記録・検証し、古代魔導の解読を村の共有技術へ落とし込む社会生活職。";
                mods.manaMultiplier = 1.18f;
                mods.defMultiplier = 1.05f;
                defaultMagics.Add("MAGIC_DYN_ACADEMY_SYNTHESIS");
                break;
            default:
                id = JobFieldSteward;
                name = "農耕世話役";
                description =
                    "倉庫余剰と野生資源の配分を調整し、村の食料循環を維持する基礎社会生活職。";
                mods.hpMultiplier = 1.05f;
                break;
        }

        JobMasterData job = new JobMasterData
        {
            id = id,
            name = name,
            description = description,
            statModifiers = mods,
            unlockConditions = unlocks,
            defaultSkillIds = defaultMagics
        };
        job.Sanitize();
        _ = nationId;
        _ = turn;
        return job;
    }

    /// <summary>JOB_* ID を NpcCivilizationEngine 用の CivicJob へマップします。</summary>
    public static NpcCivicJob MapJobIdToCivicJob(string jobId)
    {
        if (string.IsNullOrWhiteSpace(jobId))
        {
            return NpcCivicJob.Farmer;
        }

        string trimmed = jobId.Trim();
        if (JobIdToCivic.TryGetValue(trimmed, out NpcCivicJob mapped))
        {
            return mapped;
        }

        if (Enum.TryParse(trimmed, true, out NpcCivicJob civic))
        {
            return civic;
        }

        return NpcCivicJob.Farmer;
    }

    /// <summary>マスターへ登録。失敗時は FallbackJobId を返します（Safe-Fail）。</summary>
    public static DynamicJobBuildResult BuildAndRegister(
        DynamicMasterTrigger trigger,
        int nationId,
        int turn,
        string sourceLabel = "DynamicJobBuilder")
    {
        DynamicJobBuildResult result = new DynamicJobBuildResult();
        try
        {
            MasterDataManager master = MasterDataManager.EnsureInstance();
            JobMasterData job = BuildSocialJob(trigger, nationId, turn);
            if (job != null && job.IsValid() && master.TryRegisterJob(job, sourceLabel))
            {
                result.success = true;
                result.registered = true;
                result.job = job;
                result.jobId = job.id;
                result.message = $"登録 {job.id} ({job.name})";
                return result;
            }

            return ApplyFallback(master, result, sourceLabel, "構築または登録失敗");
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[DynamicJobBuilder] Safe-Fail: {exception.Message}");
            MasterDataManager master = MasterDataManager.EnsureInstance();
            return ApplyFallback(master, result, sourceLabel, exception.Message);
        }
    }

    /// <summary>NPC に社会生活ジョブ ID を割り当てます。</summary>
    public static bool TryAssignJobToNpc(NpcIndividualStatus npc, string jobId)
    {
        if (npc == null || string.IsNullOrWhiteSpace(jobId))
        {
            return false;
        }

        try
        {
            MasterDataManager master = MasterDataManager.EnsureInstance();
            string trimmed = jobId.Trim();
            if (!master.TryGetJob(trimmed, out JobMasterData _))
            {
                trimmed = FallbackJobId;
                master.TryGetJob(trimmed, out _);
            }

            npc.JobId = trimmed;
            NpcCivicJob civic = MapJobIdToCivicJob(trimmed);
            if (civic == NpcCivicJob.Blacksmith)
            {
                npc.JobProficiency = Mathf.Max(npc.JobProficiency, 1.6f);
            }

            JobMasterData jobData = master.GetJob(trimmed);
            if (jobData?.defaultSkillIds != null)
            {
                for (int i = 0; i < jobData.defaultSkillIds.Count; i++)
                {
                    npc.TryLearnMagic(jobData.defaultSkillIds[i]);
                }
            }

            return true;
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[DynamicJobBuilder] NPC 割当 Safe-Fail: {exception.Message}");
            npc.JobId = FallbackJobId;
            return false;
        }
    }

    private static DynamicJobBuildResult ApplyFallback(
        MasterDataManager master,
        DynamicJobBuildResult result,
        string sourceLabel,
        string reason)
    {
        result.usedFallback = true;
        JobMasterData fallback = master.GetJob(FallbackJobId);
        if (fallback == null || !fallback.IsValid())
        {
            fallback = new JobMasterData
            {
                id = FallbackJobId,
                name = "異世界見習い",
                description = "動的ジョブ生成失敗時の安全フォールバック職。"
            };
            fallback.Sanitize();
            master.TryRegisterJob(fallback, $"{sourceLabel}:fallback");
        }

        result.job = fallback;
        result.jobId = fallback.id;
        result.success = fallback.IsValid();
        result.registered = master.TryGetJob(fallback.id, out _);
        result.message = $"フォールバック {fallback.id} ({reason})";
        return result;
    }
}

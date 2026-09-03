using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

// =============================================================================
// 特異点・イノベーション → 魔導技術（魔法）＋社会生活ジョブの動的マスター生成
// 連携: MagicSanitizerEngine / DynamicJobBuilder / MasterDataManager
//       MicroToMacroAggregator / HistoryFlagRegistry
// =============================================================================

/// <summary>1 回の動的生成パイプライン結果。</summary>
public sealed class DynamicMasterGenerationResult
{
    public bool success;
    public string triggerLabel = string.Empty;
    public string magicId = string.Empty;
    public string jobId = string.Empty;
    public bool magicRegistered;
    public bool jobRegistered;
    public bool magicUsedFallback;
    public bool jobUsedFallback;
    public string message = string.Empty;
    public float clampedValue;
    public float clampedAreaRange;
    public int clampedManaCost;
    public bool valueClamped;
    public bool areaClamped;
    public bool manaClamped;
    public bool eraManaApplied;
}

/// <summary>バッチ検証の成否。</summary>
public sealed class DynamicMasterGenerationVerifyResult
{
    public bool success;
    public string message = string.Empty;
}

/// <summary>
/// シミュレーション特異点から魔導技術と社会生活ジョブを安全に生成・登録します。
/// </summary>
[DefaultExecutionOrder(47)]
public class DynamicMasterGenerationEngine : MonoBehaviour
{
    public const string MagicForgeSpirit = "MAGIC_DYN_FORGE_SPIRIT";
    public const string MagicBarrierSeal = "MAGIC_DYN_BARRIER_SEAL";
    public const string MagicBarrierRestore = "MAGIC_DYN_BARRIER_RESTORE";
    public const string MagicFieldCycle = "MAGIC_DYN_FIELD_CYCLE";
    public const string MagicFoodRation = "MAGIC_DYN_FOOD_RATION";
    public const string MagicAcademySynthesis = "MAGIC_DYN_ACADEMY_SYNTHESIS";
    public const string LogTag = "【動的マスター生成】";

    public static DynamicMasterGenerationEngine Instance { get; private set; }

    private readonly HashSet<string> processedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public static DynamicMasterGenerationEngine EnsureInstance()
    {
        if (Instance != null)
        {
            return Instance;
        }

        DynamicMasterGenerationEngine existing =
            UnityEngine.Object.FindAnyObjectByType<DynamicMasterGenerationEngine>();
        if (existing != null)
        {
            Instance = existing;
            return existing;
        }

        GameObject hub = GameObject.Find("DebugSystemsHub");
        GameObject host = hub != null ? hub : new GameObject(nameof(DynamicMasterGenerationEngine));
        DynamicMasterGenerationEngine engine = host.GetComponent<DynamicMasterGenerationEngine>();
        return engine != null ? engine : host.AddComponent<DynamicMasterGenerationEngine>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    /// <summary>歴史フラグ解禁時（MicroToMacroAggregator から呼出）。</summary>
    public static void NotifyHistoryFlag(string flagKey, string reason)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(flagKey) || !IsKnownDynamicFlag(flagKey))
            {
                return;
            }

            DynamicMasterTrigger trigger = MapFlagToTrigger(flagKey);
            EnsureInstance().RunPipeline(
                trigger,
                MicroToMacroAggregator.DefaultNationId,
                MicroToMacroAggregator.DefaultTurn,
                $"flag:{flagKey}",
                reason);
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[DynamicMasterGenerationEngine] NotifyHistoryFlag Safe-Fail: {exception.Message}");
        }
    }

    private static bool IsKnownDynamicFlag(string flagKey)
    {
        return flagKey.Contains("INNOVATION", StringComparison.OrdinalIgnoreCase) ||
               flagKey.Contains("BARRIERDROP", StringComparison.OrdinalIgnoreCase) ||
               flagKey.Contains("BARRIER_DROP", StringComparison.OrdinalIgnoreCase) ||
               flagKey.Contains("HERO", StringComparison.OrdinalIgnoreCase) ||
               flagKey.Contains("CIVIL", StringComparison.OrdinalIgnoreCase) ||
               flagKey.Contains("ACADEMY", StringComparison.OrdinalIgnoreCase) ||
               flagKey.Contains("BORDER", StringComparison.OrdinalIgnoreCase) ||
               flagKey.Contains("SUCCESSION", StringComparison.OrdinalIgnoreCase) ||
               flagKey.Contains("REBEL", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>村状態（結界低下・生産逼迫）からの生成。</summary>
    public static void NotifyVillageState(float barrierPercent, bool foodDepleted)
    {
        try
        {
            if (barrierPercent > 50f && !foodDepleted)
            {
                return;
            }

            DynamicMasterTrigger trigger = foodDepleted
                ? DynamicMasterTrigger.ProductionNeed
                : DynamicMasterTrigger.BarrierDrop;
            EnsureInstance().RunPipeline(
                trigger,
                MicroToMacroAggregator.DefaultNationId,
                MicroToMacroAggregator.DefaultTurn,
                $"village:barrier={barrierPercent:F0}",
                foodDepleted ? "食料枯渇" : "結界維持率低下");
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[DynamicMasterGenerationEngine] NotifyVillageState Safe-Fail: {exception.Message}");
        }
    }

    /// <summary>イノベーション（鍛冶・高レベル工房）発生時。</summary>
    public static DynamicMasterGenerationResult NotifyInnovation(int workshopLevel, int nationId, int turn)
    {
        try
        {
            if (workshopLevel < MicroToMacroAggregator.InnovationWorkshopLevelMin)
            {
                return new DynamicMasterGenerationResult
                {
                    success = false,
                    message = $"工房Lv{workshopLevel} はイノベーション閾値未満"
                };
            }

            return EnsureInstance().RunPipeline(
                DynamicMasterTrigger.Innovation,
                nationId,
                turn,
                "innovation:smith",
                $"工房Lv{workshopLevel}");
        }
        catch (Exception exception)
        {
            return new DynamicMasterGenerationResult
            {
                success = false,
                message = $"Safe-Fail: {exception.Message}"
            };
        }
    }

    /// <summary>因果律整合イベント通過後（HistoryCausalValidator から呼出）。</summary>
    public static void NotifyFromCausalProposal(CausalEventProposal proposal)
    {
        if (proposal == null)
        {
            return;
        }

        try
        {
            DynamicMasterTrigger trigger = MapCausalToTrigger(proposal);
            int nationId = ExtractNationId(proposal.historyFlag);
            int turn = proposal.turn > 0 ? proposal.turn : ExtractTurn(proposal.historyFlag);
            string dedupe = $"causal:{proposal.eventCode}:{proposal.phase}";
            string reason = string.IsNullOrWhiteSpace(proposal.title)
                ? proposal.eventCode
                : proposal.title;
            EnsureInstance().RunPipeline(trigger, nationId, turn, dedupe, reason);
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[DynamicMasterGenerationEngine] NotifyFromCausal Safe-Fail: {exception.Message}");
        }
    }

    public DynamicMasterGenerationResult RunPipeline(
        DynamicMasterTrigger trigger,
        int nationId,
        int turn,
        string dedupeKey,
        string reason,
        GeneratedMagicProposal proposalOverride = null)
    {
        DynamicMasterGenerationResult result = new DynamicMasterGenerationResult
        {
            triggerLabel = trigger.ToString()
        };

        string key = $"{trigger}:{nationId}:{turn}:{dedupeKey}";
        if (processedKeys.Contains(key))
        {
            result.success = true;
            result.message = $"スキップ（既処理） {key}";
            return result;
        }

        try
        {
            MasterDataManager.EnsureInstance();
            MagicSanitizerEngine.EnsureInstance();
            HistoryFlagRegistry.EnsureWired();

            GeneratedMagicProposal proposal = proposalOverride ?? BuildMagicProposal(trigger, nationId, turn);
            MagicSanitizeResult magicResult = MagicSanitizerEngine.SanitizeAndRegister(
                proposal,
                $"DynamicMaster:{trigger}");

            DynamicJobBuildResult jobResult = DynamicJobBuilder.BuildAndRegister(
                trigger,
                nationId,
                turn,
                $"DynamicMaster:{trigger}");

            result.magicId = magicResult.magic != null ? magicResult.magic.id : string.Empty;
            result.jobId = jobResult.jobId;
            result.magicRegistered = magicResult.registered;
            result.jobRegistered = jobResult.registered;
            result.magicUsedFallback = magicResult.usedFallback;
            result.jobUsedFallback = jobResult.usedFallback;
            result.clampedValue = magicResult.ClampedValue;
            result.clampedAreaRange = magicResult.ClampedAreaRange;
            result.clampedManaCost = magicResult.ClampedManaCost;
            result.valueClamped = magicResult.valueClamped;
            result.areaClamped = magicResult.areaRangeClamped;
            result.manaClamped = magicResult.manaCostClamped;
            result.eraManaApplied = magicResult.eraManaApplied;
            result.success = magicResult.success && jobResult.success;
            result.message =
                $"魔導 {result.magicId} (登録={result.magicRegistered} fb={result.magicUsedFallback}) / " +
                $"職 {result.jobId} (登録={result.jobRegistered} fb={result.jobUsedFallback}) / {reason}";

            TryAssignNpcForTrigger(trigger, result.jobId);
            processedKeys.Add(key);
            LogGeneration(trigger, result, reason);
            return result;
        }
        catch (Exception exception)
        {
            result.success = false;
            result.message = $"パイプライン Safe-Fail: {exception.Message}";
            Debug.LogWarning($"[DynamicMasterGenerationEngine] {result.message}");
            RunEmergencyFallback(result);
            return result;
        }
    }

    private static GeneratedMagicProposal BuildMagicProposal(
        DynamicMasterTrigger trigger,
        int nationId,
        int turn)
    {
        switch (trigger)
        {
            case DynamicMasterTrigger.Innovation:
                return new GeneratedMagicProposal
                {
                    id = MagicForgeSpirit,
                    name = "炉心共鳴術",
                    description =
                        "工房の高レベル設備と魔力炉を同期し、武具精製と結界資材の純度を安定化する社会共有魔導技術。",
                    effectType = SpecialEffectTypes.CraftPurityBonus,
                    element = "結晶",
                    value = 1.35f,
                    areaRange = 4f,
                    baseManaCost = 14,
                    power = 0f,
                    castSpeedModifier = 1.05f
                };
            case DynamicMasterTrigger.BarrierDrop:
                return new GeneratedMagicProposal
                {
                    id = MagicBarrierSeal,
                    name = "緊急結界封止",
                    description =
                        "裂け目付近の魔力漏れを一時封じ、結界杭の修復猶予を村全体で確保する防衛維持技術。",
                    effectType = SpecialEffectTypes.BuffPoise,
                    element = "結晶",
                    value = 1.25f,
                    areaRange = 6f,
                    baseManaCost = 18,
                    power = 0f,
                    castSpeedModifier = 0.95f
                };
            case DynamicMasterTrigger.BarrierHero:
                return new GeneratedMagicProposal
                {
                    id = MagicBarrierRestore,
                    name = "休眠期満充填",
                    description =
                        "休眠期の低脅威環境で結晶循環を最大効率にし、結界維持率を満タン近傍まで戻す伝承技術。",
                    effectType = SpecialEffectTypes.PsychicRegenMana,
                    element = "結晶",
                    value = 1.2f,
                    areaRange = 8f,
                    baseManaCost = 12,
                    power = 0f,
                    castSpeedModifier = 1f
                };
            case DynamicMasterTrigger.CivilUnrest:
                return new GeneratedMagicProposal
                {
                    id = MagicFoodRation,
                    name = "緊急配給調律",
                    description =
                        "倉庫配給と野生資源を再配分し、内乱期の食料循環を底上げする村共有の魔導農法。",
                    effectType = SpecialEffectTypes.HobbyGatherLuck,
                    element = "無",
                    value = 1.18f,
                    areaRange = 12f,
                    baseManaCost = 11,
                    power = 0f,
                    castSpeedModifier = 1f
                };
            case DynamicMasterTrigger.AcademyRivalry:
                return new GeneratedMagicProposal
                {
                    id = MagicAcademySynthesis,
                    name = "学院統合解読",
                    description =
                        "紀録派と実用派の争点を検証し、古代魔導を村の共有技術として再編する学院魔導。",
                    effectType = SpecialEffectTypes.CraftPurityBonus,
                    element = "結晶",
                    value = 1.22f,
                    areaRange = 5f,
                    baseManaCost = 16,
                    power = 0f,
                    castSpeedModifier = 1.02f
                };
            default:
                return new GeneratedMagicProposal
                {
                    id = MagicFieldCycle,
                    name = "畑循環調律",
                    description =
                        "倉庫配給と野生資源のバランスを読み、村の食料生産効率を底上げする基礎魔導農法。",
                    effectType = SpecialEffectTypes.HobbyGatherLuck,
                    element = "無",
                    value = 1.1f,
                    areaRange = 5f,
                    baseManaCost = 10,
                    power = 0f,
                    castSpeedModifier = 1f
                };
        }
    }

    private static DynamicMasterTrigger MapFlagToTrigger(string flagKey)
    {
        if (flagKey.Contains("INNOVATION", StringComparison.OrdinalIgnoreCase))
        {
            return DynamicMasterTrigger.Innovation;
        }

        if (flagKey.Contains("BARRIERDROP", StringComparison.OrdinalIgnoreCase) ||
            flagKey.Contains("BARRIER_DROP", StringComparison.OrdinalIgnoreCase))
        {
            return DynamicMasterTrigger.BarrierDrop;
        }

        if (flagKey.Contains("HERO", StringComparison.OrdinalIgnoreCase))
        {
            return DynamicMasterTrigger.BarrierHero;
        }

        if (flagKey.Contains("ACADEMY", StringComparison.OrdinalIgnoreCase))
        {
            return DynamicMasterTrigger.AcademyRivalry;
        }

        if (flagKey.Contains("CIVIL", StringComparison.OrdinalIgnoreCase) ||
            flagKey.Contains("BORDER", StringComparison.OrdinalIgnoreCase) ||
            flagKey.Contains("SUCCESSION", StringComparison.OrdinalIgnoreCase) ||
            flagKey.Contains("REBEL", StringComparison.OrdinalIgnoreCase))
        {
            return DynamicMasterTrigger.CivilUnrest;
        }

        return DynamicMasterTrigger.ProductionNeed;
    }

    private static DynamicMasterTrigger MapCausalToTrigger(CausalEventProposal proposal)
    {
        if (proposal == null)
        {
            return DynamicMasterTrigger.ProductionNeed;
        }

        string blob =
            (proposal.eventCode ?? string.Empty) +
            (proposal.historyFlag ?? string.Empty) +
            (proposal.title ?? string.Empty) +
            (proposal.narrative ?? string.Empty);

        if (blob.Contains("INNOVATION", StringComparison.OrdinalIgnoreCase) ||
            proposal.requiresAdvancedEraTech)
        {
            return DynamicMasterTrigger.Innovation;
        }

        if (blob.Contains("BARRIERDROP", StringComparison.OrdinalIgnoreCase) ||
            blob.Contains("BARRIER_DROP", StringComparison.OrdinalIgnoreCase))
        {
            return DynamicMasterTrigger.BarrierDrop;
        }

        if (blob.Contains("HERO", StringComparison.OrdinalIgnoreCase))
        {
            return DynamicMasterTrigger.BarrierHero;
        }

        if (blob.Contains("ACADEMY", StringComparison.OrdinalIgnoreCase))
        {
            return DynamicMasterTrigger.AcademyRivalry;
        }

        if (blob.Contains("CIVIL", StringComparison.OrdinalIgnoreCase) ||
            blob.Contains("BORDER", StringComparison.OrdinalIgnoreCase) ||
            blob.Contains("SUCCESSION", StringComparison.OrdinalIgnoreCase) ||
            blob.Contains("RIOT", StringComparison.OrdinalIgnoreCase))
        {
            return DynamicMasterTrigger.CivilUnrest;
        }

        return DynamicMasterTrigger.ProductionNeed;
    }

    private static int ExtractNationId(string flagKey)
    {
        if (string.IsNullOrWhiteSpace(flagKey))
        {
            return MicroToMacroAggregator.DefaultNationId;
        }

        int nationIndex = flagKey.IndexOf("NATION_", StringComparison.OrdinalIgnoreCase);
        if (nationIndex >= 0 && flagKey.Length >= nationIndex + 10)
        {
            string digits = flagKey.Substring(nationIndex + 7, 3);
            if (int.TryParse(digits, out int nationId) && nationId > 0)
            {
                return nationId;
            }
        }

        return MicroToMacroAggregator.DefaultNationId;
    }

    private static int ExtractTurn(string flagKey)
    {
        if (string.IsNullOrWhiteSpace(flagKey))
        {
            return MicroToMacroAggregator.DefaultTurn;
        }

        int turnIndex = flagKey.IndexOf("TURN_", StringComparison.OrdinalIgnoreCase);
        if (turnIndex >= 0 && flagKey.Length >= turnIndex + 8)
        {
            string digits = flagKey.Substring(turnIndex + 5, 3);
            if (int.TryParse(digits, out int turn) && turn > 0)
            {
                return turn;
            }
        }

        return MicroToMacroAggregator.DefaultTurn;
    }

    private static void TryAssignNpcForTrigger(DynamicMasterTrigger trigger, string jobId)
    {
        try
        {
            NpcCivilizationEngine civ = NpcCivilizationEngine.EnsureInstance();
            if (civ?.Villagers == null || civ.Villagers.Count == 0)
            {
                return;
            }

            NpcIndividualStatus target = null;
            for (int i = 0; i < civ.Villagers.Count; i++)
            {
                NpcIndividualStatus npc = civ.Villagers[i];
                if (npc == null)
                {
                    continue;
                }

                switch (trigger)
                {
                    case DynamicMasterTrigger.Innovation:
                        if (npc.CivicJob == NpcCivicJob.Blacksmith)
                        {
                            target = npc;
                            break;
                        }

                        continue;
                    case DynamicMasterTrigger.BarrierDrop:
                    case DynamicMasterTrigger.BarrierHero:
                        if (npc.CivicJob == NpcCivicJob.BarrierKeeper)
                        {
                            target = npc;
                            break;
                        }

                        continue;
                    case DynamicMasterTrigger.CivilUnrest:
                        if (npc.CivicJob == NpcCivicJob.Farmer || npc.CivicJob == NpcCivicJob.Rancher)
                        {
                            target = npc;
                            break;
                        }

                        continue;
                    case DynamicMasterTrigger.AcademyRivalry:
                        if (npc.CivicJob == NpcCivicJob.BarrierKeeper)
                        {
                            target = npc;
                            break;
                        }

                        continue;
                    default:
                        if (npc.CivicJob == NpcCivicJob.Farmer || npc.CivicJob == NpcCivicJob.Rancher)
                        {
                            target = npc;
                            break;
                        }

                        continue;
                }

                if (target != null)
                {
                    break;
                }
            }

            target ??= civ.Villagers[0];
            DynamicJobBuilder.TryAssignJobToNpc(target, jobId);
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[DynamicMasterGenerationEngine] NPC 割当スキップ: {exception.Message}");
        }
    }

    private static void RunEmergencyFallback(DynamicMasterGenerationResult result)
    {
        try
        {
            MagicSanitizeResult magic = MagicSanitizerEngine.SanitizeAndRegister(
                new GeneratedMagicProposal
                {
                    id = MagicSanitizerEngine.FallbackWaterId,
                    name = "生活水凝結",
                    description = "動的生成失敗時の基本魔導技術フォールバック。",
                    effectType = SpecialEffectTypes.PsychicRegenMana,
                    value = 1f,
                    areaRange = 2f,
                    baseManaCost = MagicSanitizerEngine.MinimumBaseManaCost
                },
                "DynamicMaster:emergency");

            DynamicJobBuildResult job = DynamicJobBuilder.BuildAndRegister(
                DynamicMasterTrigger.ProductionNeed,
                MicroToMacroAggregator.DefaultNationId,
                MicroToMacroAggregator.DefaultTurn,
                "DynamicMaster:emergency");

            result.magicId = magic.magic?.id ?? MagicSanitizerEngine.FallbackWaterId;
            result.jobId = job.jobId;
            result.magicRegistered = magic.registered;
            result.jobRegistered = job.registered;
            result.success = magic.success && job.success;
            result.message += $" / 緊急FB {result.magicId}+{result.jobId}";
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[DynamicMasterGenerationEngine] 緊急FB失敗: {exception.Message}");
        }
    }

    private static void LogGeneration(
        DynamicMasterTrigger trigger,
        DynamicMasterGenerationResult result,
        string reason)
    {
        Debug.Log(
            $"<color=#B39DDB><b>{LogTag}</b></color> " +
            $"トリガー={trigger} 魔導={result.magicId} 職={result.jobId} " +
            $"Value={result.clampedValue:F2}(上限{MagicSanitizerEngine.MaxEffectValue}) " +
            $"Area={result.clampedAreaRange:F1}m(上限{MagicSanitizerEngine.MaxAreaRangeMeters}) " +
            $"MP={result.clampedManaCost}(下限{MagicSanitizerEngine.MinimumBaseManaCost}) " +
            $"クランプ V={result.valueClamped} A={result.areaClamped} M={result.manaClamped} Era={result.eraManaApplied} " +
            $"登録 M={result.magicRegistered} J={result.jobRegistered} FB M={result.magicUsedFallback} J={result.jobUsedFallback} " +
            $"理由={reason} / {result.message}");
    }

    /// <summary>全トリガーを通し、クランプ・登録を検証します。</summary>
    public static DynamicMasterGenerationVerifyResult RunVerification()
    {
        DynamicMasterGenerationVerifyResult verify = new DynamicMasterGenerationVerifyResult();
        StringBuilder log = new StringBuilder();
        MasterDataManager master = MasterDataManager.EnsureInstance();
        DynamicMasterGenerationEngine engine = EnsureInstance();
        engine.processedKeys.Clear();

        try
        {
            DynamicMasterGenerationResult innovation = engine.RunPipeline(
                DynamicMasterTrigger.Innovation, 1, 1, "verify-innovation", "検証");
            bool innovationPass = LogStep(log, "innovation", innovation) &&
                                  master.TryGetMagic(MagicForgeSpirit, out _) &&
                                  master.TryGetJob(DynamicJobBuilder.JobHighSmith, out _);

            DynamicMasterGenerationResult barrier = engine.RunPipeline(
                DynamicMasterTrigger.BarrierDrop, 1, 1, "verify-barrier", "検証");
            bool barrierPass = LogStep(log, "barrier", barrier) &&
                               master.TryGetMagic(MagicBarrierSeal, out _) &&
                               master.TryGetJob(DynamicJobBuilder.JobBarrierRepairer, out _);

            DynamicMasterGenerationResult hero = engine.RunPipeline(
                DynamicMasterTrigger.BarrierHero, 1, 1, "verify-hero", "検証");
            bool heroPass = LogStep(log, "hero", hero) &&
                            master.TryGetMagic(MagicBarrierRestore, out _);

            DynamicMasterGenerationResult civil = engine.RunPipeline(
                DynamicMasterTrigger.CivilUnrest, 1, 1, "verify-civil", "検証");
            bool civilPass = LogStep(log, "civil", civil) &&
                             master.TryGetMagic(MagicFoodRation, out _) &&
                             master.TryGetJob(DynamicJobBuilder.JobRebelLeader, out _);

            DynamicMasterGenerationResult academy = engine.RunPipeline(
                DynamicMasterTrigger.AcademyRivalry, 1, 1, "verify-academy", "検証");
            bool academyPass = LogStep(log, "academy", academy) &&
                               master.TryGetMagic(MagicAcademySynthesis, out _) &&
                               master.TryGetJob(DynamicJobBuilder.JobAcademyScholar, out _);

            DynamicMasterGenerationResult clampPipeline = engine.RunPipeline(
                DynamicMasterTrigger.Innovation,
                1,
                1,
                "verify-pipeline-clamp",
                "破綻パラメータクランプ検証",
                new GeneratedMagicProposal
                {
                    id = "MAGIC_DYN_VERIFY_CLAMP_PIPE",
                    name = "検証破綻炉心",
                    effectType = SpecialEffectTypes.BuffStr,
                    value = 99f,
                    areaRange = 99f,
                    baseManaCost = 0
                });

            bool pipelineClampPass =
                clampPipeline.success &&
                clampPipeline.valueClamped &&
                clampPipeline.areaClamped &&
                clampPipeline.manaClamped &&
                clampPipeline.clampedValue <= MagicSanitizerEngine.MaxEffectValue + 0.01f &&
                clampPipeline.clampedAreaRange <= MagicSanitizerEngine.MaxAreaRangeMeters + 0.01f &&
                clampPipeline.clampedManaCost >= MagicSanitizerEngine.MinimumBaseManaCost;
            log.AppendLine(
                $"pipeline-clamp: value={clampPipeline.clampedValue:F2} area={clampPipeline.clampedAreaRange:F1} " +
                $"mp={clampPipeline.clampedManaCost} pass={pipelineClampPass}");

            MagicSanitizeResult overpowered = MagicSanitizerEngine.SanitizeAndRegister(
                new GeneratedMagicProposal
                {
                    id = "MAGIC_DYN_VERIFY_CLAMP",
                    name = "検証破綻術",
                    effectType = SpecialEffectTypes.BuffStr,
                    value = 99f,
                    areaRange = 99f,
                    baseManaCost = 0
                },
                "DynamicMaster:verify-clamp");

            bool clampPass =
                overpowered.success &&
                overpowered.valueClamped &&
                overpowered.areaRangeClamped &&
                overpowered.manaCostClamped &&
                overpowered.ClampedValue <= MagicSanitizerEngine.MaxEffectValue + 0.01f &&
                overpowered.ClampedAreaRange <= MagicSanitizerEngine.MaxAreaRangeMeters + 0.01f;
            log.AppendLine(
                $"clamp: value={overpowered.ClampedValue:F2} area={overpowered.ClampedAreaRange:F1} " +
                $"mp={overpowered.ClampedManaCost} pass={clampPass}");

            verify.success =
                innovationPass && barrierPass && heroPass && civilPass && academyPass &&
                pipelineClampPass && clampPass;
            verify.message = log.ToString().TrimEnd();
        }
        catch (Exception exception)
        {
            verify.success = false;
            verify.message = $"Safe-Fail: {exception.Message}";
        }

        WriteVerifyLog(verify);
        return verify;
    }

    /// <summary>シミュレーション駆動相当の一連トリガーを実行し、動的マスター登録を確認します。</summary>
    public static DynamicMasterGenerationVerifyResult RunSimulationDay()
    {
        DynamicMasterGenerationVerifyResult verify = new DynamicMasterGenerationVerifyResult();
        StringBuilder log = new StringBuilder();
        MasterDataManager master = MasterDataManager.EnsureInstance();
        DynamicMasterGenerationEngine engine = EnsureInstance();
        engine.processedKeys.Clear();
        EraContextResolver.EnsureInstance();
        NpcCivilizationEngine.EnsureInstance();

        try
        {
            DynamicMasterGenerationResult innovation = NotifyInnovation(
                MicroToMacroAggregator.InnovationWorkshopLevelMin,
                MicroToMacroAggregator.DefaultNationId,
                MicroToMacroAggregator.DefaultTurn);
            log.AppendLine($"sim-innovation: {innovation.magicId}+{innovation.jobId} pass={innovation.success}");

            NotifyVillageState(42f, false);
            NotifyVillageState(0f, true);

            CausalEventProposal civilProposal = new CausalEventProposal
            {
                sourceEngine = "SimulationDay",
                turn = 1,
                phase = 12,
                eventCode = "CIVIL_WAR_FOOD_RIOT",
                title = "食糧暴動・内乱",
                historyFlag = "HIST_NATION_001_GEO_TURN_001_CIVILWAR",
                foodScarcity = 80f
            };
            NotifyFromCausalProposal(civilProposal);
            log.AppendLine($"sim-causal-civil: flag={civilProposal.historyFlag}");

            bool masterPass =
                master.TryGetMagic(MagicForgeSpirit, out _) &&
                master.TryGetJob(DynamicJobBuilder.JobHighSmith, out _) &&
                master.TryGetMagic(MagicFoodRation, out _) &&
                master.TryGetJob(DynamicJobBuilder.JobBarrierRepairer, out _);
            log.AppendLine($"master-registered: pass={masterPass}");

            verify.success = innovation.success && masterPass;
            verify.message = log.ToString().TrimEnd();
        }
        catch (Exception exception)
        {
            verify.success = false;
            verify.message = $"SimulationDay Safe-Fail: {exception.Message}";
        }

        WriteVerifyLog(verify);
        return verify;
    }

    private static bool LogStep(StringBuilder log, string label, DynamicMasterGenerationResult result)
    {
        bool pass = result.success && result.magicRegistered && result.jobRegistered;
        log.AppendLine(
            $"{label}: magic={result.magicId} job={result.jobId} pass={pass} ({result.message})");
        return pass;
    }

    private static void WriteVerifyLog(DynamicMasterGenerationVerifyResult verify)
    {
        try
        {
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
            string path = Path.Combine(projectRoot, "Logs", "dynamic_master_generation_verify.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? projectRoot);
            File.WriteAllText(
                path,
                $"{DateTime.UtcNow:O} success={verify.success}\n{verify.message}\n",
                Encoding.UTF8);
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[DynamicMasterGenerationEngine] 検証ログをスキップ: {exception.Message}");
        }
    }
}

public static class DynamicMasterGenerationBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoAttach()
    {
        DynamicMasterGenerationEngine.EnsureInstance();
    }
}

#if UNITY_EDITOR
public static class DynamicMasterGenerationMenu
{
    [MenuItem("Tools/Procedural Map/Verify Dynamic Master Generation")]
    public static void VerifyFromMenu()
    {
        DynamicMasterGenerationVerifyResult result = DynamicMasterGenerationEngine.RunVerification();
        Debug.Log(
            result.success
                ? $"<color=#A5D6A7><b>【動的マスター生成検証】PASS</b></color>\n{result.message}"
                : $"<color=#FF8A80><b>【動的マスター生成検証】FAIL</b></color>\n{result.message}");
        EditorUtility.DisplayDialog("Dynamic Master Generation", result.message, "OK");
    }

    [MenuItem("Tools/Procedural Map/Run Dynamic Master Simulation Day")]
    public static void RunSimulationDayFromMenu()
    {
        DynamicMasterGenerationVerifyResult result = DynamicMasterGenerationEngine.RunSimulationDay();
        Debug.Log(
            result.success
                ? $"<color=#A5D6A7><b>【動的マスター生成・シミュレーション日】PASS</b></color>\n{result.message}"
                : $"<color=#FF8A80><b>【動的マスター生成・シミュレーション日】FAIL</b></color>\n{result.message}");
        EditorUtility.DisplayDialog("Dynamic Master Simulation Day", result.message, "OK");
    }

    /// <summary>Unity バッチ: -executeMethod DynamicMasterGenerationMenu.BatchSimulationDayAndQuit</summary>
    public static void BatchSimulationDayAndQuit()
    {
        DynamicMasterGenerationVerifyResult result = DynamicMasterGenerationEngine.RunSimulationDay();
        Debug.Log(
            result.success
                ? $"<color=#A5D6A7><b>【動的マスター生成・シミュレーション日】PASS</b></color>\n{result.message}"
                : $"<color=#FF8A80><b>【動的マスター生成・シミュレーション日】FAIL</b></color>\n{result.message}");
        EditorApplication.Exit(result.success ? 0 : 1);
    }

    /// <summary>Unity バッチ: -executeMethod DynamicMasterGenerationMenu.BatchVerifyAndQuit</summary>
    public static void BatchVerifyAndQuit()
    {
        DynamicMasterGenerationVerifyResult result = DynamicMasterGenerationEngine.RunVerification();
        Debug.Log(
            result.success
                ? $"<color=#A5D6A7><b>【動的マスター生成検証】PASS</b></color>\n{result.message}"
                : $"<color=#FF8A80><b>【動的マスター生成検証】FAIL</b></color>\n{result.message}");
        EditorApplication.Exit(result.success ? 0 : 1);
    }
}
#endif

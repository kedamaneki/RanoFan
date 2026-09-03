using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

// =============================================================================
// 歴史主要人物キャスティング — 特異点 / HIST_ フラグからネームド NPC をフィールド実体化
// 連携: SingularPointExtractor / NpcStatusManager / VillageWorkSpotManager / EraContextResolver
// =============================================================================

/// <summary>主要人物の役割 archetype。</summary>
public enum HistoricalNpcArchetype
{
    DefenseHero = 0,
    InnovationHero = 1,
    BarrierRecoveryLeader = 2,
    CivilWarLeader = 3,
    ProxyLegend = 4
}

/// <summary>1 体のキャスティング定義。</summary>
public sealed class HistoricalNpcCastSlot
{
    public HistoricalNpcArchetype archetype;
    public string npcId = string.Empty;
    public string displayName = string.Empty;
    public string title = string.Empty;
    public string conditionFlag = string.Empty;
    public string jobId = string.Empty;
    public NpcCivicJob civicJob = NpcCivicJob.BarrierKeeper;
    public float proficiency = 2.5f;
    public NpcTrait trait = NpcTrait.Cautious;
    public string[] learnedMagics = Array.Empty<string>();
    public bool usedFallback;
}

/// <summary>スポーン結果 1 件。</summary>
[Serializable]
public sealed class HistoricalNpcSpawnRecord
{
    public string npcId = string.Empty;
    public string displayName = string.Empty;
    public HistoricalNpcArchetype archetype;
    public Vector3 worldPosition;
    public string spotId = string.Empty;
    [NonSerialized] public NpcStatusManager manager;
}

/// <summary>キャスティング実行結果。</summary>
public sealed class HistoricalNpcCastResult
{
    public bool success;
    public int turn;
    public int spawnedCount;
    public bool usedFallback;
    public List<HistoricalNpcSpawnRecord> spawned = new List<HistoricalNpcSpawnRecord>();
    public string message = string.Empty;
}

/// <summary>検証結果。</summary>
public sealed class HistoricalNpcCasterVerifyResult
{
    public bool success;
    public string message = string.Empty;
}

/// <summary>
/// 歴史ログの特異点・イベント条件から主要 NPC を動的キャスティングしフィールドへ配置します。
/// </summary>
[DefaultExecutionOrder(44)]
public class HistoricalNpcCaster : MonoBehaviour
{
    public const string LogTag = "【歴史主要人物スポーン】";

    public static HistoricalNpcCaster Instance { get; private set; }

    [SerializeField] private int nationId = MicroToMacroAggregator.DefaultNationId;
    [SerializeField] private int lastCastTurn = -1;
    [SerializeField] private List<HistoricalNpcSpawnRecord> activeRecords = new List<HistoricalNpcSpawnRecord>();

    public int LastCastTurn => lastCastTurn;
    public IReadOnlyList<HistoricalNpcSpawnRecord> ActiveRecords => activeRecords;

    public static HistoricalNpcCaster EnsureInstance()
    {
        if (Instance != null)
        {
            return Instance;
        }

        HistoricalNpcCaster existing = UnityEngine.Object.FindAnyObjectByType<HistoricalNpcCaster>();
        if (existing != null)
        {
            Instance = existing;
            return existing;
        }

        GameObject hub = GameObject.Find("DebugSystemsHub");
        GameObject host = hub != null ? hub : new GameObject(nameof(HistoricalNpcCaster));
        HistoricalNpcCaster caster = host.GetComponent<HistoricalNpcCaster>();
        return caster != null ? caster : host.AddComponent<HistoricalNpcCaster>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        activeRecords ??= new List<HistoricalNpcSpawnRecord>();
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    /// <summary>指定ターンの歴史フラグ・特異点から主要人物をキャスティングします。</summary>
    public static HistoricalNpcCastResult SpawnHistoricalKeyCharacters(int turn)
    {
        try
        {
            return EnsureInstance().SpawnInternal(turn);
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[HistoricalNpcCaster] SpawnHistoricalKeyCharacters Safe-Fail: {exception.Message}");
            return BuildFallbackResult(turn, exception.Message);
        }
    }

    private HistoricalNpcCastResult SpawnInternal(int turn)
    {
        HistoricalNpcCastResult result = new HistoricalNpcCastResult { turn = turn };
        int normalizedTurn = Mathf.Clamp(turn, EraContextResolver.MinTurn, EraContextResolver.MaxTurn);
        int restoreTurn = EraContextResolver.CurrentTurn;
        EraContextResolver.TrySetCurrentTurn(normalizedTurn);
        HistoryFlagRegistry.EnsureWired();
        MasterDataManager.EnsureInstance();

        List<HistoricalNpcCastSlot> plan = BuildCastPlan(nationId, normalizedTurn);
        result.usedFallback = plan.Exists(slot => slot.usedFallback);

        Transform fieldRoot = EnsureFieldRoot();
        StringBuilder log = new StringBuilder();
        log.AppendLine($"Turn={normalizedTurn} plan={plan.Count} fallback={result.usedFallback}");

        for (int i = 0; i < plan.Count; i++)
        {
            HistoricalNpcSpawnRecord record = TrySpawnSlot(plan[i], normalizedTurn, fieldRoot);
            if (record == null)
            {
                continue;
            }

            result.spawned.Add(record);
            activeRecords.Add(record);
            log.AppendLine(
                $"  {record.displayName} ({record.archetype}) @{record.worldPosition.ToString()} spot={record.spotId}");

            Debug.Log(
                $"<color=#FFD180><b>{LogTag}</b></color> T{normalizedTurn} " +
                $"{record.displayName} [{record.archetype}] " +
                $"Job={plan[i].jobId} POS={record.worldPosition.ToString("F1", CultureInfo.InvariantCulture)} " +
                $"FLAG={plan[i].conditionFlag} {record.manager?.FormatSnapshot()}");
        }

        EraContextResolver.TrySetCurrentTurn(restoreTurn);
        lastCastTurn = normalizedTurn;
        result.spawnedCount = result.spawned.Count;
        result.success = result.spawnedCount > 0;
        result.message = log.ToString().TrimEnd();
        return result;
    }

    private HistoricalNpcSpawnRecord TrySpawnSlot(
        HistoricalNpcCastSlot slot,
        int turn,
        Transform fieldRoot)
    {
        if (slot == null || string.IsNullOrWhiteSpace(slot.npcId))
        {
            return null;
        }

        if (TryFindExisting(slot.npcId, out HistoricalNpcSpawnRecord existing))
        {
            return existing;
        }

        Vector3 position;
        string spotId;
        ResolveSpawnLocation(slot, out position, out spotId);

        NpcIndividualStatus individual = BuildIndividual(slot, turn, position, spotId);
        NpcCivilizationEngine.EnsureInstance().TryRegisterHistoricalNpc(individual);
        EnsureHistoricalJobsRegistered(slot);

        GameObject go = new GameObject($"HIST_{slot.displayName}");
        go.transform.SetParent(fieldRoot, false);
        go.transform.position = position;

        NpcStatusManager manager = go.AddComponent<NpcStatusManager>();
        manager.ApplyHeroTierProfile(slot.archetype, turn, slot.learnedMagics, individual);

        try
        {
            WorldSpaceMarker marker = go.AddComponent<WorldSpaceMarker>();
            marker.Initialize(go.transform, WorldSpaceMarkerKind.WorkSpot, slot.title);
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[HistoricalNpcCaster] マーカー省略: {exception.Message}");
        }

        if (!string.IsNullOrWhiteSpace(slot.conditionFlag))
        {
            HistoryFlagRegistry.Unlock(slot.conditionFlag);
        }

        return new HistoricalNpcSpawnRecord
        {
            npcId = slot.npcId,
            displayName = slot.displayName,
            archetype = slot.archetype,
            worldPosition = position,
            spotId = spotId,
            manager = manager
        };
    }

    private static List<HistoricalNpcCastSlot> BuildCastPlan(int nationId, int turn)
    {
        List<HistoricalNpcCastSlot> plan = new List<HistoricalNpcCastSlot>(4);
        SingularExtractionResult extraction = SingularPointExtractor.Extract(nationId, turn);

        string heroFlag = BuildHistFlag(nationId, turn, "HERO");
        string innovatorFlag = BuildHistFlag(nationId, turn, "INNOVATOR");
        string innovationFlag = BuildHistFlag(nationId, turn, "INNOVATION");
        string barrierDropFlag = BuildHistFlag(nationId, turn, "BARRIERDROP");
        string civilWarFlag = BuildHistFlag(nationId, turn, "CIVILWAR");

        bool heroActive = HistoryFlagRegistry.IsUnlocked(heroFlag) ||
                          (extraction.hero != null &&
                           extraction.hero.conditionFlag.IndexOf("HERO", StringComparison.OrdinalIgnoreCase) >= 0);
        bool innovatorActive = HistoryFlagRegistry.IsUnlocked(innovatorFlag) ||
                               HistoryFlagRegistry.IsUnlocked(innovationFlag) ||
                               (extraction.hero != null &&
                                extraction.hero.conditionFlag.IndexOf("INNOVATOR", StringComparison.OrdinalIgnoreCase) >= 0);
        bool barrierDropActive = HistoryFlagRegistry.IsUnlocked(barrierDropFlag) ||
                                 HistoryFlagRegistry.IsUnlocked(VillageBarrierCore.Nation001BarrierDropFlag);
        bool civilWarActive = HistoryFlagRegistry.IsUnlocked(civilWarFlag) ||
                              HistoryFlagRegistry.IsUnlocked(HumanConflictEngine.FlagCivilWar);

        if (heroActive)
        {
            plan.Add(BuildSlotFromHero(extraction.hero, nationId, turn, HistoricalNpcArchetype.DefenseHero, heroFlag));
        }

        if (innovatorActive)
        {
            plan.Add(BuildInnovationSlot(extraction.hero, nationId, turn, innovatorFlag, innovationFlag));
        }

        if (barrierDropActive)
        {
            plan.Add(BuildArchetypeSlot(
                nationId,
                turn,
                HistoricalNpcArchetype.BarrierRecoveryLeader,
                barrierDropFlag,
                "結界再建の守護者",
                "破綻後の杭再生を担う伝説の結界修復技師",
                DynamicJobBuilder.JobBarrierRepairer,
                NpcCivicJob.BarrierKeeper,
                2.9f,
                NpcTrait.Cautious,
                ResolveAncientMagics(turn, "MAG_BARRIER_SEAL", "MAG_CRYSTAL_TUNE", "MAGIC_CRYSTAL_LANCE")));
        }

        if (civilWarActive)
        {
            plan.Add(BuildArchetypeSlot(
                nationId,
                turn,
                HistoricalNpcArchetype.CivilWarLeader,
                civilWarFlag,
                "内乱を鎮める調停者",
                "配給争いの渦中で村を束ねた歴史人物",
                DynamicJobBuilder.JobRebelLeader,
                NpcCivicJob.Farmer,
                2.6f,
                NpcTrait.Inquisitive,
                ResolveAncientMagics(turn, "MAG_HARVEST_FLOW", "MAG_SOIL_WARD")));
        }

        if (plan.Count == 0)
        {
            plan.Add(BuildProxyLegendSlot(nationId, turn));
        }

        return plan;
    }

    private static HistoricalNpcCastSlot BuildSlotFromHero(
        HeroData hero,
        int nationId,
        int turn,
        HistoricalNpcArchetype archetype,
        string flag)
    {
        string name = hero != null && !string.IsNullOrWhiteSpace(hero.displayName)
            ? hero.displayName
            : "伝説の結界守";
        string title = hero != null && !string.IsNullOrWhiteSpace(hero.title)
            ? hero.title
            : "防衛の偉人";
        string id = hero != null && !string.IsNullOrWhiteSpace(hero.id)
            ? hero.id
            : $"HERO_N{nationId:000}_T{turn:000}";
        string condition = hero != null && !string.IsNullOrWhiteSpace(hero.conditionFlag)
            ? hero.conditionFlag
            : flag;

        return BuildArchetypeSlot(
            nationId,
            turn,
            archetype,
            condition,
            name,
            title,
            DynamicJobBuilder.JobBarrierRepairer,
            NpcCivicJob.BarrierKeeper,
            2.8f,
            NpcTrait.Cautious,
            ResolveAncientMagics(turn, "MAG_BARRIER_SEAL", "MAG_CRYSTAL_TUNE", "MAGIC_CRYSTAL_LANCE"));
    }

    private static HistoricalNpcCastSlot BuildInnovationSlot(
        HeroData hero,
        int nationId,
        int turn,
        string innovatorFlag,
        string innovationFlag)
    {
        string name = hero != null &&
                      !string.IsNullOrWhiteSpace(hero.conditionFlag) &&
                      hero.conditionFlag.IndexOf("INNOVATOR", StringComparison.OrdinalIgnoreCase) >= 0
            ? hero.displayName
            : "伝説の高位鍛冶";
        if (string.IsNullOrWhiteSpace(name))
        {
            name = "工法改変の革新者";
        }

        string title = hero != null && !string.IsNullOrWhiteSpace(hero.title)
            ? hero.title
            : "技術革新の偉人";
        string id = $"INNOVATOR_N{nationId:000}_T{turn:000}";
        string condition = HistoryFlagRegistry.IsUnlocked(innovationFlag) ? innovationFlag : innovatorFlag;

        return BuildArchetypeSlot(
            nationId,
            turn,
            HistoricalNpcArchetype.InnovationHero,
            condition,
            name,
            title,
            DynamicJobBuilder.JobHighSmith,
            NpcCivicJob.Blacksmith,
            3.0f,
            NpcTrait.Diligent,
            ResolveAncientMagics(turn, "MAG_FURNACE_FLOW", "MAGIC_DYN_FORGE_SPIRIT", "MAGIC_CRYSTAL_LANCE"));
    }

    private static HistoricalNpcCastSlot BuildProxyLegendSlot(int nationId, int turn)
    {
        return BuildArchetypeSlot(
            nationId,
            turn,
            HistoricalNpcArchetype.ProxyLegend,
            BuildHistFlag(nationId, turn, "PROXY"),
            "歴史の代行者",
            "記録欠落を埋める伝説NPC",
            DynamicJobBuilder.FallbackJobId,
            NpcCivicJob.BarrierKeeper,
            2.5f,
            NpcTrait.Diligent,
            ResolveAncientMagics(turn, "MAG_BARRIER_SEAL", "MAG_FURNACE_FLOW"),
            usedFallback: true);
    }

    private static HistoricalNpcCastSlot BuildArchetypeSlot(
        int nationId,
        int turn,
        HistoricalNpcArchetype archetype,
        string conditionFlag,
        string displayName,
        string title,
        string jobId,
        NpcCivicJob civicJob,
        float proficiency,
        NpcTrait trait,
        string[] magics,
        bool usedFallback = false)
    {
        string suffix = archetype switch
        {
            HistoricalNpcArchetype.InnovationHero => "INNOVATOR",
            HistoricalNpcArchetype.BarrierRecoveryLeader => "RECOVERY",
            HistoricalNpcArchetype.CivilWarLeader => "CIVIL",
            HistoricalNpcArchetype.ProxyLegend => "PROXY",
            _ => "HERO"
        };

        return new HistoricalNpcCastSlot
        {
            archetype = archetype,
            npcId = $"HIST_KEY_{nationId:000}_T{turn:000}_{suffix}",
            displayName = displayName,
            title = title,
            conditionFlag = conditionFlag,
            jobId = jobId,
            civicJob = civicJob,
            proficiency = proficiency,
            trait = trait,
            learnedMagics = magics ?? Array.Empty<string>(),
            usedFallback = usedFallback
        };
    }

    private static NpcIndividualStatus BuildIndividual(
        HistoricalNpcCastSlot slot,
        int turn,
        Vector3 position,
        string spotId)
    {
        NpcIndividualStatus individual = new NpcIndividualStatus(
            slot.npcId,
            slot.displayName,
            36 + turn % 20,
            slot.civicJob,
            slot.proficiency,
            slot.trait,
            slot.learnedMagics);
        individual.JobId = slot.jobId;
        individual.WorldPosition = position;
        individual.AssignedSpotId = spotId ?? string.Empty;
        individual.MaxHP = NpcStatusManager.HeroMaxHp;
        individual.CurrentHP = NpcStatusManager.HeroMaxHp;
        individual.MaxMP = NpcStatusManager.HeroMaxMp;
        individual.CurrentMP = NpcStatusManager.HeroMaxMp;
        individual.ClampVitals();
        return individual;
    }

    private static void ResolveSpawnLocation(
        HistoricalNpcCastSlot slot,
        out Vector3 position,
        out string spotId)
    {
        spotId = string.Empty;
        try
        {
            VillageWorkSpotManager spots = VillageWorkSpotManager.EnsureInstance();
            WorkSpotData target;
            switch (slot.archetype)
            {
                case HistoricalNpcArchetype.InnovationHero:
                    target = spots.GetOptimalWorkSpot(
                        DynamicJobBuilder.JobHighSmith,
                        spots.ResolveHousingWorkshopCenter());
                    break;
                case HistoricalNpcArchetype.CivilWarLeader:
                    target = spots.GetOptimalWorkSpot(
                        DynamicJobBuilder.JobFieldSteward,
                        spots.ResolveVillageCenterOrFallback());
                    break;
                case HistoricalNpcArchetype.DefenseHero:
                case HistoricalNpcArchetype.BarrierRecoveryLeader:
                case HistoricalNpcArchetype.ProxyLegend:
                default:
                    target = spots.GetOptimalWorkSpot(
                        DynamicJobBuilder.JobBarrierRepairer,
                        spots.ResolveVillageCenterOrFallback());
                    break;
            }

            if (target != null)
            {
                position = target.WorldPosition;
                spotId = target.SpotId ?? string.Empty;
                return;
            }
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[HistoricalNpcCaster] スポット解決 Safe-Fail: {exception.Message}");
        }

        position = VillageWorkSpotManager.FallbackWorldPosition;
    }

    private static string[] ResolveAncientMagics(int turn, params string[] baseMagics)
    {
        List<string> list = new List<string>();
        if (baseMagics != null)
        {
            for (int i = 0; i < baseMagics.Length; i++)
            {
                if (!string.IsNullOrWhiteSpace(baseMagics[i]))
                {
                    list.Add(baseMagics[i]);
                }
            }
        }

        EraTag era = EraContextResolver.ResolveEraTag(turn);
        if (era == EraTag.Mid && !list.Contains("MAGIC_DYN_FORGE_SPIRIT"))
        {
            list.Add("MAGIC_DYN_FORGE_SPIRIT");
        }

        if (era == EraTag.Late)
        {
            if (!list.Contains("MAGIC_CRYSTAL_LANCE"))
            {
                list.Add("MAGIC_CRYSTAL_LANCE");
            }

            if (!list.Contains("MAGIC_FIRE_SPARK"))
            {
                list.Add("MAGIC_FIRE_SPARK");
            }
        }

        return list.ToArray();
    }

    private bool TryFindExisting(string npcId, out HistoricalNpcSpawnRecord record)
    {
        record = null;
        if (activeRecords == null || string.IsNullOrWhiteSpace(npcId))
        {
            return false;
        }

        for (int i = 0; i < activeRecords.Count; i++)
        {
            if (activeRecords[i] != null &&
                string.Equals(activeRecords[i].npcId, npcId, StringComparison.OrdinalIgnoreCase))
            {
                record = activeRecords[i];
                return true;
            }
        }

        return false;
    }

    private static Transform EnsureFieldRoot()
    {
        GameObject map = GameObject.Find(Nation001ProceduralMapBuilder.RootName);
        GameObject host = map != null ? map : GameObject.Find("DebugSystemsHub");
        if (host == null)
        {
            host = new GameObject("HistoricalNpcField");
        }

        Transform existing = host.transform.Find("HistoricalKeyCharacters");
        if (existing != null)
        {
            return existing;
        }

        GameObject child = new GameObject("HistoricalKeyCharacters");
        child.transform.SetParent(host.transform, false);
        return child.transform;
    }

    private static string BuildHistFlag(int nationId, int turn, string suffix)
    {
        return $"HIST_NATION_{nationId:000}_GEO_TURN_{turn:000}_{suffix}";
    }

    private static void EnsureHistoricalJobsRegistered(HistoricalNpcCastSlot slot)
    {
        if (slot == null)
        {
            return;
        }

        try
        {
            DynamicMasterTrigger trigger = slot.archetype switch
            {
                HistoricalNpcArchetype.InnovationHero => DynamicMasterTrigger.Innovation,
                HistoricalNpcArchetype.BarrierRecoveryLeader => DynamicMasterTrigger.BarrierDrop,
                HistoricalNpcArchetype.DefenseHero => DynamicMasterTrigger.BarrierHero,
                HistoricalNpcArchetype.CivilWarLeader => DynamicMasterTrigger.CivilUnrest,
                _ => DynamicMasterTrigger.ProductionNeed
            };
            DynamicJobBuilder.BuildAndRegister(trigger, MicroToMacroAggregator.DefaultNationId, EraContextResolver.CurrentTurn);
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[HistoricalNpcCaster] ジョブ事前登録 Safe-Fail: {exception.Message}");
        }
    }

    private static HistoricalNpcCastResult BuildFallbackResult(int turn, string reason)
    {
        return SpawnInternalFallbackProxy(turn, reason);
    }

    private static HistoricalNpcCastResult SpawnInternalFallbackProxy(int turn, string reason)
    {
        HistoricalNpcCastResult result = new HistoricalNpcCastResult { turn = turn, usedFallback = true };
        HistoricalNpcCastSlot proxy = BuildProxyLegendSlot(MicroToMacroAggregator.DefaultNationId, turn);
        proxy.displayName = "歴史の代行者";
        proxy.title = $"Safe-Fail: {reason}";

        Transform root = EnsureFieldRoot();
        HistoricalNpcCaster caster = EnsureInstance();
        HistoricalNpcSpawnRecord record = caster.TrySpawnSlot(proxy, turn, root);
        if (record != null)
        {
            result.spawned.Add(record);
            result.spawnedCount = 1;
            result.success = true;
            result.message = record.manager?.FormatSnapshot() ?? "proxy spawned";
        }

        return result;
    }

    public void ClearSpawned()
    {
        if (activeRecords == null)
        {
            return;
        }

        for (int i = 0; i < activeRecords.Count; i++)
        {
            if (activeRecords[i]?.manager != null)
            {
                Destroy(activeRecords[i].manager.gameObject);
            }
        }

        activeRecords.Clear();
    }

    public static HistoricalNpcCasterVerifyResult RunVerification()
    {
        HistoricalNpcCasterVerifyResult verify = new HistoricalNpcCasterVerifyResult();
        StringBuilder log = new StringBuilder();
        try
        {
            HistoryFlagRegistry.EnsureWired();
            MasterDataManager.EnsureInstance();
            HistoricalNpcCaster caster = EnsureInstance();
            caster.ClearSpawned();

            HistoricalNpcCastResult fallback = SpawnHistoricalKeyCharacters(88);
            bool proxyPass = fallback.success && fallback.spawnedCount >= 1;
            log.AppendLine($"turn88-proxy: count={fallback.spawnedCount} fallback={fallback.usedFallback} pass={proxyPass}");

            HistoryFlagRegistry.Unlock(MicroToMacroAggregator.FlagHero);
            HistoricalNpcCastResult turn1 = SpawnHistoricalKeyCharacters(1);
            bool turn1Pass = turn1.success && turn1.spawnedCount >= 1;
            HistoricalNpcSpawnRecord hero = turn1.spawned.Count > 0 ? turn1.spawned[0] : null;
            bool statsPass = hero?.manager != null &&
                             hero.manager.Combat != null &&
                             hero.manager.Combat.MaxHp >= NpcStatusManager.HeroMaxHp &&
                             hero.manager.Combat.Strength >= NpcStatusManager.HeroStrength;
            log.AppendLine($"turn1: count={turn1.spawnedCount} fallback={turn1.usedFallback} pass={turn1Pass}");
            log.AppendLine($"hero-stats: {hero?.manager?.FormatSnapshot()} pass={statsPass}");

            verify.success = turn1Pass && statsPass && proxyPass;
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

    private static void WriteVerifyLog(HistoricalNpcCasterVerifyResult verify)
    {
        try
        {
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
            string path = Path.Combine(projectRoot, "Logs", "historical_npc_caster_verify.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? projectRoot);
            File.WriteAllText(
                path,
                $"{DateTime.UtcNow:O} success={verify.success}\n{verify.message}\n",
                Encoding.UTF8);
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[HistoricalNpcCaster] 検証ログスキップ: {exception.Message}");
        }
    }
}

public static class HistoricalNpcCasterBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoAttach()
    {
        HistoricalNpcCaster.EnsureInstance();
    }
}

#if UNITY_EDITOR
public static class HistoricalNpcCasterMenu
{
    [MenuItem("Tools/Procedural Map/Verify Historical Npc Caster")]
    public static void VerifyFromMenu()
    {
        HistoricalNpcCasterVerifyResult result = HistoricalNpcCaster.RunVerification();
        Debug.Log(
            result.success
                ? $"<color=#A5D6A7><b>【歴史主要人物検証】PASS</b></color>\n{result.message}"
                : $"<color=#FF8A80><b>【歴史主要人物検証】FAIL</b></color>\n{result.message}");
        EditorUtility.DisplayDialog("Historical Npc Caster", result.message, "OK");
    }

    [MenuItem("Tools/Procedural Map/Cast Historical Key Characters (Turn 1)")]
    public static void CastTurn1FromMenu()
    {
        HistoryFlagRegistry.EnsureWired();
        HistoryFlagRegistry.Unlock(MicroToMacroAggregator.FlagHero);
        HistoricalNpcCastResult result = HistoricalNpcCaster.SpawnHistoricalKeyCharacters(1);
        EditorUtility.DisplayDialog(
            "Historical Npc Caster",
            $"spawned={result.spawnedCount} fallback={result.usedFallback}\n{result.message}",
            "OK");
    }

    [MenuItem("Tools/Procedural Map/Cast Historical Key Characters (Current Turn)")]
    public static void CastCurrentTurnFromMenu()
    {
        HistoricalNpcCastResult result =
            HistoricalNpcCaster.SpawnHistoricalKeyCharacters(EraContextResolver.CurrentTurn);
        EditorUtility.DisplayDialog(
            "Historical Npc Caster",
            $"spawned={result.spawnedCount}\n{result.message}",
            "OK");
    }
}
#endif

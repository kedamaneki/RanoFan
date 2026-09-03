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
// NPC ステータス管理 — プレイヤーと同一レイヤー構造（Combat / Stamina / Job / MP / Karma）
// 連携: NpcIndividualStatus / JobStatApplier / EquipmentStatFeedback / EraContextResolver
// =============================================================================

/// <summary>NPC 初期化結果。</summary>
public sealed class NpcStatusInitResult
{
    public bool success;
    public string npcId = string.Empty;
    public string message = string.Empty;
}

/// <summary>検証結果。</summary>
public sealed class NpcStatusVerifyResult
{
    public bool success;
    public string message = string.Empty;
}

/// <summary>
/// 1 体の NPC に CombatStats / PlayerStats / 拡張属性（JobId, MP, Karma, Trait）を付与します。
/// プレイヤーの PlayerStatusManager と対称のレイヤー構成です。
/// </summary>
[RequireComponent(typeof(CombatStats))]
[RequireComponent(typeof(PlayerStats))]
[DefaultExecutionOrder(42)]
public class NpcStatusManager : MonoBehaviour
{
    public const string LogTag = "【NPCステータス】";

    public const int DefaultMaxHp = 100;
    public const int DefaultStrength = 10;
    public const int DefaultDefense = 5;
    public const float DefaultMaxStamina = 100f;
    public const float DefaultMaxMp = 80f;

    public const int HeroMaxHp = 250;
    public const int HeroStrength = 20;
    public const int HeroDefense = 15;
    public const float HeroMaxStamina = 150f;
    public const float HeroMaxMp = 150f;

    [Header("参照")]
    [SerializeField] private CombatStats combatStats;
    [SerializeField] private PlayerStats playerStats;

    [Header("個人属性")]
    [SerializeField] private string npcId = string.Empty;
    [SerializeField] private string displayName = "村人";
    [SerializeField] private string jobId = DynamicJobBuilder.JobFieldSteward;
    [SerializeField] private NpcTrait trait = NpcTrait.Diligent;
    [Range(1f, 3f)] [SerializeField] private float jobProficiency = 1f;

    [Header("MP / 隠し")]
    [SerializeField] private float maxMP = DefaultMaxMp;
    [SerializeField] private float currentMP;
    [SerializeField] private int karmaValue;
    [SerializeField] private int intelBonus = 3;

    [Header("装備還元（職人道具）")]
    [SerializeField] private float toolPurity = 40f;
    [SerializeField] private float toolDensity = 30f;

    private NpcIndividualStatus linkedIndividual;
    private bool modifiersApplied;

    public string NpcId => npcId;
    public string DisplayName => displayName;
    public string JobId => jobId;
    public NpcTrait Trait => trait;
    public CombatStats Combat => combatStats;
    public PlayerStats StaminaLayer => playerStats;
    public float CurrentMP => currentMP;
    public float MaxMP => maxMP;
    public int KarmaValue => karmaValue;
    public NpcIndividualStatus LinkedIndividual => linkedIndividual;

    private void Reset()
    {
        combatStats = GetComponent<CombatStats>();
        playerStats = GetComponent<PlayerStats>();
    }

    private void Awake()
    {
        CacheReferences();
        EnsureSafeDefaults();
    }

    private void Start()
    {
        ApplyAllModifiers(log: false);
    }

    /// <summary>NpcIndividualStatus から初期化します（Safe-Fail 付き）。</summary>
    public NpcStatusInitResult InitializeFromIndividual(NpcIndividualStatus individual)
    {
        NpcStatusInitResult result = new NpcStatusInitResult();
        try
        {
            linkedIndividual = individual;
            if (individual == null)
            {
                EnsureSafeDefaults();
                result.npcId = npcId;
                result.success = true;
                result.message = "individual=null → 標準値フォールバック";
                return result;
            }

            npcId = string.IsNullOrWhiteSpace(individual.NpcId) ? name : individual.NpcId;
            displayName = string.IsNullOrWhiteSpace(individual.Name) ? npcId : individual.Name;
            jobId = ResolveJobKey(individual);
            trait = individual.Trait;
            jobProficiency = Mathf.Clamp(individual.JobProficiency, 1f, 3f);
            gameObject.name = $"NPC_{displayName}";

            EnsureSafeDefaults();
            ApplyIndividualVitals(individual);
            ApplyAllModifiers(log: true);
            SyncToIndividual();

            result.success = true;
            result.npcId = npcId;
            result.message = FormatSnapshot();
            return result;
        }
        catch (Exception exception)
        {
            EnsureSafeDefaults();
            result.success = false;
            result.npcId = npcId;
            result.message = $"Safe-Fail: {exception.Message}";
            Debug.LogWarning($"[NpcStatusManager] InitializeFromIndividual Safe-Fail: {exception.Message}");
            return result;
        }
    }

    /// <summary>ジョブ・装備・時代補正を一括適用します。</summary>
    public void ApplyAllModifiers(bool log = false)
    {
        try
        {
            CacheReferences();
            EnsureSafeDefaults(resetCombatBase: false);
            MasterDataManager.EnsureInstance();

            string jobKey = string.IsNullOrWhiteSpace(jobId) ? DynamicJobBuilder.FallbackJobId : jobId;
            StatModifiers mods = JobStatApplier.ResolveStatModifiers(jobKey, out _);
            JobStatApplier.ApplyJobModifiers(jobKey, combatStats);
            maxMP = Mathf.Max(10f, maxMP * mods.manaMultiplier);
            ApplyEraContextModifiers();
            ApplyToolEquipmentFeedback();
            ApplyTraitBonuses();

            currentMP = Mathf.Clamp(currentMP, 0f, maxMP);
            modifiersApplied = true;

            if (log)
            {
                Debug.Log(
                    $"<color=#81D4FA><b>{LogTag}</b></color> {displayName} ({jobKey}) " +
                    $"HP{combatStats.MaxHp} STR{combatStats.Strength} DEF{combatStats.Defense} " +
                    $"STA{playerStats.maxStamina:F0} MP{currentMP:F0}/{maxMP:F0} Karma{karmaValue}");
            }
        }
        catch (Exception exception)
        {
            EnsureSafeDefaults();
            Debug.LogWarning($"[NpcStatusManager] ApplyAllModifiers Safe-Fail: {exception.Message}");
        }
    }

    /// <summary>歴史主要人物向けの英雄級基礎ステータスを焼き込みます。</summary>
    public void ApplyHeroTierProfile(
        HistoricalNpcArchetype archetype,
        int turn,
        IList<string> learnedMagics = null,
        NpcIndividualStatus individual = null)
    {
        try
        {
            linkedIndividual = individual;
            if (individual != null)
            {
                npcId = string.IsNullOrWhiteSpace(individual.NpcId) ? npcId : individual.NpcId;
                displayName = string.IsNullOrWhiteSpace(individual.Name) ? displayName : individual.Name;
                gameObject.name = $"HIST_{displayName}";
            }

            CacheReferences();
            EnsureSafeDefaults(resetCombatBase: false);
            modifiersApplied = false;
            if (combatStats != null)
            {
                combatStats.ResetBaseStatsCaptureForTesting();
            }

            int hp = HeroMaxHp;
            int str = HeroStrength;
            int def = HeroDefense;
            float sta = HeroMaxStamina;
            float mp = HeroMaxMp;
            jobProficiency = 2.5f;
            intelBonus = 5;
            toolPurity = 85f;
            toolDensity = 72f;
            karmaValue = 8;

            switch (archetype)
            {
                case HistoricalNpcArchetype.DefenseHero:
                    str = 22;
                    def = 16;
                    mp = 160f;
                    jobProficiency = 2.8f;
                    trait = NpcTrait.Cautious;
                    jobId = DynamicJobBuilder.JobBarrierRepairer;
                    break;
                case HistoricalNpcArchetype.InnovationHero:
                    str = 18;
                    def = 12;
                    mp = 150f;
                    jobProficiency = 3.0f;
                    trait = NpcTrait.Diligent;
                    intelBonus = 9;
                    jobId = DynamicJobBuilder.JobHighSmith;
                    break;
                case HistoricalNpcArchetype.BarrierRecoveryLeader:
                    str = 20;
                    def = 18;
                    mp = 170f;
                    jobProficiency = 2.9f;
                    trait = NpcTrait.Cautious;
                    jobId = DynamicJobBuilder.JobBarrierRepairer;
                    break;
                case HistoricalNpcArchetype.CivilWarLeader:
                    str = 19;
                    def = 14;
                    mp = 130f;
                    jobProficiency = 2.6f;
                    trait = NpcTrait.Inquisitive;
                    intelBonus = 7;
                    jobId = DynamicJobBuilder.JobRebelLeader;
                    break;
                default:
                    trait = NpcTrait.Diligent;
                    jobId = DynamicJobBuilder.FallbackJobId;
                    break;
            }

            if (combatStats != null)
            {
                combatStats.SetBaseStatsForNpc(hp, str, def);
            }

            maxMP = mp;
            currentMP = mp;
            if (playerStats != null)
            {
                playerStats.maxStamina = sta;
            }

            ApplyLearnedMagics(learnedMagics, turn);
            ApplyAllModifiers(log: true);
            SyncToIndividual();
        }
        catch (Exception exception)
        {
            EnsureSafeDefaults();
            Debug.LogWarning($"[NpcStatusManager] ApplyHeroTierProfile Safe-Fail: {exception.Message}");
        }
    }

    private void ApplyLearnedMagics(IList<string> learnedMagics, int turn)
    {
        if (linkedIndividual == null)
        {
            return;
        }

        if (linkedIndividual.LearnedMagicIds == null)
        {
            linkedIndividual.LearnedMagicIds = new List<string>();
        }
        else
        {
            linkedIndividual.LearnedMagicIds.Clear();
        }

        if (learnedMagics != null)
        {
            for (int i = 0; i < learnedMagics.Count; i++)
            {
                linkedIndividual.TryLearnMagic(learnedMagics[i]);
            }
        }

        EraTag era = EraContextResolver.ResolveEraTag(turn);
        if (era == EraTag.Late)
        {
            linkedIndividual.TryLearnMagic("MAGIC_CRYSTAL_LANCE");
        }
    }

    /// <summary>リンク済み NpcIndividualStatus へ HP/MP を書き戻します。</summary>
    public void SyncToIndividual()
    {
        if (linkedIndividual == null || combatStats == null)
        {
            return;
        }

        linkedIndividual.MaxHP = combatStats.MaxHp;
        linkedIndividual.CurrentHP = combatStats.CurrentHp;
        linkedIndividual.MaxMP = maxMP;
        linkedIndividual.CurrentMP = currentMP;
        linkedIndividual.JobId = jobId;
        linkedIndividual.JobProficiency = jobProficiency;
        linkedIndividual.Trait = trait;
        linkedIndividual.ClampVitals();
    }

    /// <summary>操作傀儡の HP / スタミナ / MP を NPC 側へ書き戻します。</summary>
    public void MirrorVitalsFromPuppet(CombatStats puppetCombat, PlayerStats puppetStamina, float puppetMp)
    {
        if (puppetCombat != null && combatStats != null)
        {
            combatStats.SetHpForTesting(puppetCombat.CurrentHp);
        }

        if (puppetStamina != null && playerStats != null)
        {
            playerStats.SetStaminaForTesting(puppetStamina.CurrentStamina);
        }

        currentMP = Mathf.Clamp(puppetMp, 0f, maxMP);
        SyncToIndividual();
    }

    /// <summary>操作可能（生存）かどうか。</summary>
    public bool IsOperable()
    {
        return this != null && combatStats != null && combatStats.IsAlive;
    }

    public string FormatSnapshot()
    {
        int hp = combatStats != null ? combatStats.MaxHp : DefaultMaxHp;
        int str = combatStats != null ? combatStats.Strength : DefaultStrength;
        int def = combatStats != null ? combatStats.Defense : DefaultDefense;
        float sta = playerStats != null ? playerStats.maxStamina : DefaultMaxStamina;
        return
            $"{displayName} Job={jobId} Trait={trait} " +
            $"HP{hp} STR{str} DEF{def} STA{sta.ToString("F0", CultureInfo.InvariantCulture)} " +
            $"MP{currentMP.ToString("F0", CultureInfo.InvariantCulture)}/{maxMP.ToString("F0", CultureInfo.InvariantCulture)} " +
            $"Karma{karmaValue}";
    }

    private void ApplyIndividualVitals(NpcIndividualStatus individual)
    {
        if (combatStats == null)
        {
            return;
        }

        int hp = individual.MaxHP > 0.01f && !float.IsNaN(individual.MaxHP) && !float.IsInfinity(individual.MaxHP)
            ? Mathf.RoundToInt(individual.MaxHP)
            : DefaultMaxHp;
        float prof = Mathf.Clamp(individual.JobProficiency <= 0.01f ? 1f : individual.JobProficiency, 1f, 3f);
        int str = DefaultStrength + Mathf.RoundToInt((prof - 1f) * 4f);
        int def = DefaultDefense + (individual.Trait == NpcTrait.Cautious ? 2 : 0);

        combatStats.CaptureBaseStatsIfNeeded();
        combatStats.SetBaseStatsForNpc(hp, str, def);
        maxMP = individual.MaxMP > 0.01f ? individual.MaxMP : DefaultMaxMp;
        currentMP = Mathf.Clamp(individual.CurrentMP, 0f, maxMP);
        karmaValue = ResolveInitialKarma(individual);
        intelBonus = individual.Trait == NpcTrait.Inquisitive ? 6 : 3;
        toolPurity = Mathf.Clamp(individual.JobProficiency * 38f, 20f, 95f);
        toolDensity = Mathf.Clamp(individual.JobProficiency * 28f, 15f, 85f);
    }

    private void ApplyEraContextModifiers()
    {
        EraTag era = EraContextResolver.CurrentEra;
        float mpMul = era == EraTag.Late ? 1.12f : era == EraTag.Mid ? 1.06f : 1f;
        maxMP = Mathf.Max(10f, maxMP * mpMul);
        currentMP = Mathf.Min(currentMP, maxMP);

        if (playerStats != null)
        {
            float staminaMul = era == EraTag.Late ? 0.95f : 1f;
            playerStats.maxStamina = Mathf.Max(40f, playerStats.maxStamina * staminaMul);
        }
    }

    private void ApplyToolEquipmentFeedback()
    {
        if (combatStats == null)
        {
            return;
        }

        combatStats.ApplyMaterialModifiers(toolPurity, toolDensity, $"{displayName}_tool");
    }

    private void ApplyTraitBonuses()
    {
        if (combatStats == null)
        {
            return;
        }

        switch (trait)
        {
            case NpcTrait.Diligent:
                if (playerStats != null)
                {
                    playerStats.maxStamina = Mathf.Min(160f, playerStats.maxStamina * 1.08f);
                }

                break;
            case NpcTrait.Cautious:
                combatStats.ApplyDefenseBonus(1);
                break;
            case NpcTrait.Inquisitive:
                intelBonus += 2;
                break;
        }
    }

    private void EnsureSafeDefaults(bool resetCombatBase = true)
    {
        CacheReferences();

        if (combatStats == null)
        {
            combatStats = gameObject.AddComponent<CombatStats>();
        }

        if (playerStats == null)
        {
            playerStats = gameObject.AddComponent<PlayerStats>();
        }

        if (resetCombatBase && !modifiersApplied)
        {
            combatStats.SetBaseStatsForNpc(DefaultMaxHp, DefaultStrength, DefaultDefense);
        }

        if (playerStats.maxStamina <= 0.01f)
        {
            playerStats.maxStamina = DefaultMaxStamina;
        }

        if (maxMP <= 0.01f)
        {
            maxMP = DefaultMaxMp;
        }

        if (currentMP <= 0.01f)
        {
            currentMP = maxMP;
        }

        if (string.IsNullOrWhiteSpace(jobId))
        {
            jobId = DynamicJobBuilder.FallbackJobId;
        }
    }

    private void CacheReferences()
    {
        if (combatStats == null)
        {
            combatStats = GetComponent<CombatStats>();
        }

        if (playerStats == null)
        {
            playerStats = GetComponent<PlayerStats>();
        }
    }

    private static string ResolveJobKey(NpcIndividualStatus individual)
    {
        if (individual == null)
        {
            return DynamicJobBuilder.FallbackJobId;
        }

        if (!string.IsNullOrWhiteSpace(individual.JobId) &&
            individual.JobId.StartsWith("JOB_", StringComparison.OrdinalIgnoreCase))
        {
            return individual.JobId.Trim();
        }

        switch (individual.CivicJob)
        {
            case NpcCivicJob.BarrierKeeper:
                return DynamicJobBuilder.JobBarrierRepairer;
            case NpcCivicJob.Blacksmith:
                return DynamicJobBuilder.JobHighSmith;
            case NpcCivicJob.Carpenter:
                return DynamicJobBuilder.JobCrystalTender;
            case NpcCivicJob.Woodcutter:
                return DynamicJobBuilder.JobFieldSteward;
            case NpcCivicJob.Rancher:
                return DynamicJobBuilder.JobFieldSteward;
            default:
                return DynamicJobBuilder.JobFieldSteward;
        }
    }

    private static int ResolveInitialKarma(NpcIndividualStatus individual)
    {
        if (individual == null)
        {
            return 0;
        }

        return individual.Trait switch
        {
            NpcTrait.Diligent => 4,
            NpcTrait.Cautious => 2,
            NpcTrait.Inquisitive => -1,
            _ => 0
        };
    }
}

/// <summary>NpcIndividualStatus 一覧から NpcStatusManager を生成・管理します。</summary>
public static class NpcStatusManagerRegistry
{
    private static readonly Dictionary<string, NpcStatusManager> byNpcId =
        new Dictionary<string, NpcStatusManager>(StringComparer.OrdinalIgnoreCase);

    private static Transform hostRoot;

    public static int Count => byNpcId.Count;
    public static IReadOnlyDictionary<string, NpcStatusManager> All => byNpcId;

    public static NpcStatusManager EnsureForNpc(NpcIndividualStatus individual)
    {
        if (individual == null || string.IsNullOrWhiteSpace(individual.NpcId))
        {
            return null;
        }

        if (byNpcId.TryGetValue(individual.NpcId, out NpcStatusManager existing) && existing != null)
        {
            existing.InitializeFromIndividual(individual);
            return existing;
        }

        Transform root = EnsureHostRoot();
        GameObject go = new GameObject($"NPC_{individual.NpcId}");
        go.transform.SetParent(root, false);
        NpcStatusManager mgr = go.AddComponent<NpcStatusManager>();
        mgr.InitializeFromIndividual(individual);
        byNpcId[individual.NpcId] = mgr;
        return mgr;
    }

    public static void EnsureAllFromVillagers()
    {
        try
        {
            NpcCivilizationEngine civ = NpcCivilizationEngine.EnsureInstance();
            IReadOnlyList<NpcIndividualStatus> villagers = civ.Villagers;
            if (villagers == null)
            {
                return;
            }

            for (int i = 0; i < villagers.Count; i++)
            {
                EnsureForNpc(villagers[i]);
            }
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[NpcStatusManagerRegistry] EnsureAllFromVillagers Safe-Fail: {exception.Message}");
        }
    }

    public static bool TryGet(string npcId, out NpcStatusManager manager)
    {
        manager = null;
        if (string.IsNullOrWhiteSpace(npcId))
        {
            return false;
        }

        return byNpcId.TryGetValue(npcId, out manager) && manager != null;
    }

    public static void ClearAll()
    {
        foreach (KeyValuePair<string, NpcStatusManager> pair in byNpcId)
        {
            if (pair.Value != null)
            {
                UnityEngine.Object.Destroy(pair.Value.gameObject);
            }
        }

        byNpcId.Clear();
    }

    private static Transform EnsureHostRoot()
    {
        if (hostRoot != null)
        {
            return hostRoot;
        }

        GameObject hub = GameObject.Find("DebugSystemsHub");
        GameObject host = hub != null ? hub : new GameObject("NpcStatusHost");
        Transform existing = host.transform.Find("NpcStatusManagers");
        if (existing == null)
        {
            GameObject child = new GameObject("NpcStatusManagers");
            child.transform.SetParent(host.transform, false);
            hostRoot = child.transform;
        }
        else
        {
            hostRoot = existing;
        }

        return hostRoot;
    }
}

public static class NpcStatusManagerBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoAttach()
    {
        NpcCivilizationEngine.EnsureInstance();
        NpcStatusManagerRegistry.EnsureAllFromVillagers();
    }
}

#if UNITY_EDITOR
public static class NpcStatusManagerMenu
{
    [MenuItem("Tools/Procedural Map/Verify Npc Status Manager")]
    public static void VerifyFromMenu()
    {
        NpcStatusVerifyResult result = RunVerification();
        Debug.Log(
            result.success
                ? $"<color=#A5D6A7><b>【NPCステータス検証】PASS</b></color>\n{result.message}"
                : $"<color=#FF8A80><b>【NPCステータス検証】FAIL</b></color>\n{result.message}");
        EditorUtility.DisplayDialog("Npc Status Manager", result.message, "OK");
    }

    public static NpcStatusVerifyResult RunVerification()
    {
        NpcStatusVerifyResult verify = new NpcStatusVerifyResult();
        StringBuilder log = new StringBuilder();
        try
        {
            HistoryFlagRegistry.EnsureWired();
            MasterDataManager.EnsureInstance();
            NpcStatusManagerRegistry.ClearAll();

            NpcIndividualStatus keeper = new NpcIndividualStatus(
                "npc_verify_keeper",
                "ミサ",
                52,
                NpcCivicJob.BarrierKeeper,
                2.7f,
                NpcTrait.Cautious,
                "MAG_BARRIER_SEAL");
            NpcIndividualStatus smith = new NpcIndividualStatus(
                "npc_verify_smith",
                "カネ",
                38,
                NpcCivicJob.Blacksmith,
                2.1f,
                NpcTrait.Diligent,
                "MAG_FURNACE_FLOW");

            NpcStatusManager keeperMgr = NpcStatusManagerRegistry.EnsureForNpc(keeper);
            NpcStatusManager smithMgr = NpcStatusManagerRegistry.EnsureForNpc(smith);

            bool keeperPass = keeperMgr != null &&
                              keeperMgr.Combat != null &&
                              keeperMgr.Combat.MaxHp >= NpcStatusManager.DefaultMaxHp &&
                              keeperMgr.Combat.Strength >= NpcStatusManager.DefaultStrength;
            bool smithPass = smithMgr != null &&
                             smithMgr.Combat != null &&
                             smithMgr.Combat.Defense >= NpcStatusManager.DefaultDefense;

            NpcIndividualStatus broken = new NpcIndividualStatus
            {
                NpcId = "npc_verify_broken",
                Name = string.Empty,
                MaxHP = float.NaN,
                MaxMP = 0f,
                JobProficiency = 0f
            };
            NpcStatusManager brokenMgr = NpcStatusManagerRegistry.EnsureForNpc(broken);
            bool safePass = brokenMgr != null &&
                            brokenMgr.Combat.MaxHp >= NpcStatusManager.DefaultMaxHp &&
                            brokenMgr.Combat.Strength >= NpcStatusManager.DefaultStrength &&
                            brokenMgr.Combat.Defense >= NpcStatusManager.DefaultDefense;

            log.AppendLine($"keeper: {keeperMgr?.FormatSnapshot()} pass={keeperPass}");
            log.AppendLine($"smith: {smithMgr?.FormatSnapshot()} pass={smithPass}");
            log.AppendLine($"safe-fail: {brokenMgr?.FormatSnapshot()} pass={safePass}");

            verify.success = keeperPass && smithPass && safePass;
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

    private static void WriteVerifyLog(NpcStatusVerifyResult verify)
    {
        try
        {
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
            string path = Path.Combine(projectRoot, "Logs", "npc_status_verify.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? projectRoot);
            File.WriteAllText(
                path,
                $"{DateTime.UtcNow:O} success={verify.success}\n{verify.message}\n",
                Encoding.UTF8);
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[NpcStatusManager] 検証ログスキップ: {exception.Message}");
        }
    }
}
#endif

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

// =============================================================================
// 発展スキル進化の統括マネージャ（検証・ログスキャン・実行・将来の融合／最上位チェーン）
// =============================================================================

/// <summary>
/// 基礎スキルとプレイヤー戦歴から進化候補を動的に提示し、進化を実行します。
/// </summary>
public class SkillEvolutionManager : MonoBehaviour
{
    public static SkillEvolutionManager Instance { get; private set; }

    private static readonly Regex CountryBranchIdPattern =
        new Regex(@"^SKL_(.+)_C(\d{3})$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex GenericAdvancedIdPattern =
        new Regex(@"^SKL_(.+)_ADV$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    [Header("参照（未設定時は自動解決）")]
    [SerializeField] private SkillMasterRepository skillMasterRepository;
    [SerializeField] private PlayerHistoryTracker historyTracker;
    [SerializeField] private PlayerSkillSlotManager skillSlotManager;

    [Header("登録時検証")]
    [SerializeField] private bool validateEvolutionSkillsOnStart = true;

    [Header("将来拡張")]
    [SerializeField] private bool enableFusionResolver;

    private ISkillFusionResolver fusionResolver = NullSkillFusionResolver.Instance;

    // GC 抑制用再利用バッファ
    private readonly List<SkillMaster> candidateBuffer = new List<SkillMaster>(160);
    private readonly List<EvolutionLogScanResult> scanResultBuffer = new List<EvolutionLogScanResult>(160);
    private readonly List<string> idResultBuffer = new List<string>(160);

    /// <summary>融合ロジックの差し替え（未設定時は Null）。</summary>
    public ISkillFusionResolver FusionResolver
    {
        get => fusionResolver ?? NullSkillFusionResolver.Instance;
        set => fusionResolver = value ?? NullSkillFusionResolver.Instance;
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        ResolveReferences();
        WireLogResolvers();
    }

    private void Start()
    {
        ResolveReferences();
        if (validateEvolutionSkillsOnStart)
        {
            ValidateRegisteredEvolutionSkills();
        }
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    /// <summary>シーンに無い場合は生成して返します。</summary>
    public static SkillEvolutionManager EnsureInstance()
    {
        if (Instance != null)
        {
            return Instance;
        }

        GameObject hub = GameObject.Find("DebugSystemsHub");
        if (hub != null)
        {
            SkillEvolutionManager onHub = hub.GetComponent<SkillEvolutionManager>();
            if (onHub != null)
            {
                return onHub;
            }

            return hub.AddComponent<SkillEvolutionManager>();
        }

        return new GameObject(nameof(SkillEvolutionManager)).AddComponent<SkillEvolutionManager>();
    }

    /// <summary>
    /// 基礎スキルから条件を満たした発展スキル ID リストを返します（UI 提示用）。
    /// 未達の候補は含めません。
    /// </summary>
    public IReadOnlyList<string> ResolveEligibleEvolutionSkillIds(string baseSkillId, int currentSkillLevel)
    {
        idResultBuffer.Clear();
        List<EvolutionLogScanResult> scanned = ResolveEligibleEvolutionBranches(baseSkillId, currentSkillLevel);
        for (int i = 0; i < scanned.Count; i++)
        {
            EvolutionLogScanResult entry = scanned[i];
            if (!string.IsNullOrWhiteSpace(entry.targetSkillId))
            {
                idResultBuffer.Add(entry.targetSkillId);
            }
        }

        return idResultBuffer;
    }

    /// <summary>詳細付きの進化候補リストを返します。</summary>
    public List<EvolutionLogScanResult> ResolveEligibleEvolutionBranches(
        string baseSkillId,
        int currentSkillLevel)
    {
        scanResultBuffer.Clear();
        ResolveReferences();
        historyTracker?.SyncFromLegacyLoggers();

        CollectEvolutionCandidates(baseSkillId, candidateBuffer);
        if (candidateBuffer.Count == 0)
        {
            return scanResultBuffer;
        }

        List<EvolutionLogScanResult> filtered = EvolutionLogScanner.FilterEligibleBranches(
            candidateBuffer,
            currentSkillLevel,
            historyTracker);

        SortScanResults(filtered);
        scanResultBuffer.AddRange(filtered);
        return scanResultBuffer;
    }

    /// <summary>プレイヤーが選択した発展スキルへ進化を実行します。</summary>
    public bool TryEvolveToBranch(string baseSkillId, string targetEvolutionSkillId, int currentSkillLevel)
    {
        ResolveReferences();
        historyTracker?.SyncFromLegacyLoggers();

        IReadOnlyList<string> eligible = ResolveEligibleEvolutionSkillIds(baseSkillId, currentSkillLevel);
        for (int i = 0; i < eligible.Count; i++)
        {
            if (string.Equals(eligible[i], targetEvolutionSkillId, StringComparison.OrdinalIgnoreCase))
            {
                return SkillEvolutionLinker.CheckAndExecuteEvolutionToTarget(
                    baseSkillId,
                    targetEvolutionSkillId,
                    currentSkillLevel,
                    skillSlotManager);
            }
        }

        Debug.LogWarning(
            $"[SkillEvolutionManager] 進化先が条件未達または未登録: {baseSkillId} -> {targetEvolutionSkillId}");
        return false;
    }

    /// <summary>
    /// 現在スキルの evolutionData.nextSkillId チェーンを辿って最上位へ進化します。
    /// nextSkillId が空の場合は false（将来 ID 追加時にそのまま動作）。
    /// </summary>
    public bool TryEvolveChainToApex(string currentSkillId)
    {
        ResolveReferences();

        if (!skillMasterRepository.TryGet(currentSkillId, out SkillMaster current) || current == null)
        {
            Debug.LogError($"[SkillEvolutionManager] スキル未登録: {currentSkillId}");
            return false;
        }

        SkillEvolutionData evolution = current.evolutionData;
        if (evolution == null || !evolution.isEvolvable || !evolution.HasNextSkill)
        {
            return false;
        }

        evolution.Sanitize(current.skillId);
        string nextId = evolution.nextSkillId;

        if (!skillSlotManager.TryGetSkillLevel(currentSkillId, out int level))
        {
            return false;
        }

        if (!skillMasterRepository.TryGet(nextId, out SkillMaster nextMaster) || nextMaster == null)
        {
            Debug.LogError($"[SkillEvolutionManager] チェーン先未登録: {nextId}");
            return false;
        }

        SkillEvolutionData nextGate = nextMaster.evolutionData ?? new SkillEvolutionData();
        nextGate.Sanitize(nextMaster.skillId);

        if (level < nextGate.requiredLevel)
        {
            return false;
        }

        if (!EvolutionLogScanner.IsCriteriaMet(nextGate.evolutionCriteria, historyTracker))
        {
            return false;
        }

        return SkillEvolutionLinker.CheckAndExecuteEvolutionToTarget(
            currentSkillId,
            nextId,
            level,
            skillSlotManager);
    }

    /// <summary>将来のスキル融合候補 ID を返します（ISkillFusionResolver 経由）。</summary>
    public IReadOnlyList<string> ResolveFusionCandidates(IReadOnlyList<string> ownedSkillIds)
    {
        if (!enableFusionResolver)
        {
            return Array.Empty<string>();
        }

        return FusionResolver.ResolveFusionCandidates(ownedSkillIds, historyTracker);
    }

    /// <summary>登録済み SKL_* 発展スキルを GeneratedDataValidator で検証します。</summary>
    public GeneratedDataValidationReport ValidateRegisteredEvolutionSkills()
    {
        ResolveReferences();
        List<SkillMaster> evolutionOnly = new List<SkillMaster>();
        IReadOnlyList<SkillMaster> all = skillMasterRepository.GetAll();

        for (int i = 0; i < all.Count; i++)
        {
            SkillMaster skill = all[i];
            if (skill != null &&
                !string.IsNullOrWhiteSpace(skill.skillId) &&
                skill.skillId.StartsWith("SKL_", StringComparison.OrdinalIgnoreCase))
            {
                evolutionOnly.Add(skill);
            }
        }

        GeneratedDataValidationReport report = GeneratedDataValidator.ValidateSkills(evolutionOnly);
        if (!report.IsValid)
        {
            Debug.LogError($"[SkillEvolutionManager] {report.BuildSummary()}");
            report.LogAllIssues();
        }
        else
        {
            Debug.Log($"[SkillEvolutionManager] {report.BuildSummary()}");
        }

        return report;
    }

    /// <summary>単一 SkillMaster を検証します（インポート直後用）。</summary>
    public static bool ValidateImportedSkill(SkillMaster skill)
    {
        return GeneratedDataValidator.ValidateAndLog(skill);
    }

    private void CollectEvolutionCandidates(string baseSkillId, List<SkillMaster> sink)
    {
        sink.Clear();
        if (string.IsNullOrWhiteSpace(baseSkillId) || skillMasterRepository == null)
        {
            return;
        }

        string baseSuffix = StripSkillPrefix(baseSkillId);
        if (string.IsNullOrWhiteSpace(baseSuffix))
        {
            return;
        }

        IReadOnlyList<SkillMaster> all = skillMasterRepository.GetAll();
        for (int i = 0; i < all.Count; i++)
        {
            SkillMaster candidate = all[i];
            if (candidate == null || string.IsNullOrWhiteSpace(candidate.skillId))
            {
                continue;
            }

            if (MatchesEvolutionBranch(candidate.skillId, baseSuffix))
            {
                sink.Add(candidate);
            }
        }
    }

    private static bool MatchesEvolutionBranch(string evolutionSkillId, string baseSuffix)
    {
        Match country = CountryBranchIdPattern.Match(evolutionSkillId);
        if (country.Success)
        {
            return string.Equals(country.Groups[1].Value, baseSuffix, StringComparison.OrdinalIgnoreCase);
        }

        Match generic = GenericAdvancedIdPattern.Match(evolutionSkillId);
        if (generic.Success)
        {
            return string.Equals(generic.Groups[1].Value, baseSuffix, StringComparison.OrdinalIgnoreCase);
        }

        if (evolutionSkillId.StartsWith("SKL_", StringComparison.OrdinalIgnoreCase) &&
            evolutionSkillId.IndexOf(baseSuffix, StringComparison.OrdinalIgnoreCase) >= 0 &&
            (evolutionSkillId.EndsWith("_DEEP", StringComparison.OrdinalIgnoreCase) ||
             evolutionSkillId.EndsWith("_PEAK", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return false;
    }

    private static void SortScanResults(List<EvolutionLogScanResult> results)
    {
        results.Sort((a, b) =>
        {
            if (a.isGenericFallback != b.isGenericFallback)
            {
                return a.isGenericFallback ? 1 : -1;
            }

            return string.Compare(a.targetSkillName, b.targetSkillName, StringComparison.OrdinalIgnoreCase);
        });
    }

    private static string StripSkillPrefix(string skillId)
    {
        if (string.IsNullOrWhiteSpace(skillId))
        {
            return string.Empty;
        }

        return skillId.StartsWith("SKILL_", StringComparison.OrdinalIgnoreCase)
            ? skillId.Substring("SKILL_".Length)
            : skillId;
    }

    private void ResolveReferences()
    {
        if (skillMasterRepository == null)
        {
            skillMasterRepository = SkillMasterRepository.Instance
                ?? FindAnyObjectByType<SkillMasterRepository>();
        }

        if (historyTracker == null)
        {
            historyTracker = PlayerHistoryTracker.Instance
                ?? FindAnyObjectByType<PlayerHistoryTracker>();
        }

        if (skillSlotManager == null)
        {
            skillSlotManager = FindAnyObjectByType<PlayerSkillSlotManager>();
        }
    }

    private void WireLogResolvers()
    {
        PlayerHistoryTracker tracker = historyTracker ?? PlayerHistoryTracker.EnsureInstance();
        Func<string, int> resolver = logType => tracker.GetCount(logType);
        SkillEvolutionLinker.LogCountResolver = resolver;
        EvolutionLogScanner.LogCountResolver = resolver;
        EvolutionSkillBranchResolver.LogCountResolver = resolver;
    }
}

/// <summary>起動時に進化サブシステムを配線します。</summary>
public static class SkillEvolutionSystemBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        PlayerHistoryTracker.EnsureInstance();
        SkillEvolutionManager.EnsureInstance();
    }
}

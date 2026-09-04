using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

// =============================================================================
// 正史 HIST_ 改変 → ALT_ 分岐ライン — 250 ターン動的補正
//
// 【設計: アンカー固定＆局所波及モデル】
// - 正史（HIST_）250 ターンデータは MicroToMacroAggregator 等が保持するベース値
// - 改変成功時のみ MacroParamDelta + ALT_ IF フラグを currentActiveBranch にレイヤー追加
// - 全歴史の再生成は行わず、介入点からの累積 Delta のみをゲッターで合成
// 連携: HistoryTimelineBranch / HistoryFlagRegistry / MicroToMacroAggregator
// =============================================================================
/// <summary>改変登録結果。</summary>
public sealed class HistoryAlterationResult
{
    public bool success;
    public string branchId = string.Empty;
    public string outcomeFlag = string.Empty;
    public int appliedFromTurn;
    public string message = string.Empty;
}

/// <summary>現状態からの IF 分岐創出結果。</summary>
public sealed class GenerateBranchFromStateResult
{
    public bool success;
    public string branchId = string.Empty;
    public string parentBranchId = string.Empty;
    public string outcomeFlag = string.Empty;
    public int forkTurn;
    public string message = string.Empty;
}

/// <summary>検証結果。</summary>
public sealed class HistoryBranchVerifyResult
{
    public bool success;
    public string message = string.Empty;
}

/// <summary>タイムライン切替結果。</summary>
public sealed class TimelineSelectResult
{
    public bool success;
    public string branchId = string.Empty;
    public bool isCanonMode;
    public string message = string.Empty;
}

/// <summary>選択 UI 用の歴史軸サマリー。</summary>
public sealed class TimelineBranchSummary
{
    public string branchId = string.Empty;
    public string label = string.Empty;
    public int nodeCount;
    public int baseStartTurn;
    public bool isCanon;
    public bool isActive;
    public bool isMainStory;
}

/// <summary>250年正史幹の1世代分。</summary>
public sealed class CanonTimelineLineageNode
{
    public int generation;
    public string branchId = string.Empty;
    public string label = string.Empty;
    public int committedAtTurn;
    public int score;
    public string parentBranchId = string.Empty;
}

/// <summary>250年正史エクスポート結果。</summary>
public sealed class CanonTimelineExportResult
{
    public bool success;
    public bool geoWritebackOk;
    public bool gameMastersWritebackOk;
    public bool chronicleLogWritten;
    public string geoPath = string.Empty;
    public string gameMastersPath = string.Empty;
    public string exportLogPath = string.Empty;
    public string message = string.Empty;
    public List<CanonTimelineLineageNode> lineage = new List<CanonTimelineLineageNode>();
}

/// <summary>
/// HIST_ 重大イベント改変を ALT_ 分岐として記録し、以降 250 ターンのマクロパラメータを補正します。
/// </summary>
[DefaultExecutionOrder(45)]
public class HistoryBranchManager : MonoBehaviour
{
    public const string LogTag = "【歴史分岐】";
    public const string AltBranchPrefix = "ALT_";
    public const string CanonBranchId = "HIST_CANON";
    public const string LatentEnergyAccumLogTag = "【歴史エネルギー蓄積】";
    public const string LatentEnergyEruptLogTag = "【歴史エネルギー噴出・採択】";
    /// <summary>不遇枝カテゴリへの毎世代蓄積量（20〜30pt 帯の固定値）。</summary>
    public const int LatentEnergyPerGeneration = 25;
    /// <summary>正史採択後、同カテゴリがボーナスを得られない世代数（3世代 ≒ 150年）。</summary>
    public const int CategoryCooldownGenerations = 3;

    public static HistoryBranchManager Instance { get; private set; }

    [SerializeField] private HistoryTimelineBranch currentActiveBranch = new HistoryTimelineBranch();
    [SerializeField] private List<HistoryTimelineBranch> registeredBranches = new List<HistoryTimelineBranch>();
    [SerializeField] private string activeBranchId = string.Empty;
    [SerializeField] private string mainStoryBranchId = string.Empty;
    [SerializeField] private bool canonMode = true;

    /// <summary>カテゴリ別の不遇枝潜在エネルギー（LatentEnergy）。</summary>
    private readonly Dictionary<string, int> unChosenBranchLatentEnergy =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

    /// <summary>カテゴリ別クールダウン残世代（&gt;0 の間は蓄積不可）。</summary>
    private readonly Dictionary<string, int> categoryCooldowns =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

    public HistoryTimelineBranch CurrentActiveBranch => currentActiveBranch;
    public string ActiveBranchId => canonMode ? CanonBranchId : activeBranchId;
    public string MainStoryBranchId => mainStoryBranchId;
    public bool HasCommittedMainStory => !string.IsNullOrWhiteSpace(mainStoryBranchId);
    public bool IsMainStoryBaselineActive =>
        HasCommittedMainStory &&
        !IsCanonMode &&
        string.Equals(activeBranchId, mainStoryBranchId, StringComparison.OrdinalIgnoreCase);
    public IReadOnlyList<HistoryTimelineBranch> RegisteredBranches => registeredBranches;
    public bool IsCanonMode => canonMode || currentActiveBranch == null ||
                               string.IsNullOrWhiteSpace(currentActiveBranch.branchId);

    public static HistoryBranchManager EnsureInstance()
    {
        if (Instance != null)
        {
            return Instance;
        }

        HistoryBranchManager existing = UnityEngine.Object.FindAnyObjectByType<HistoryBranchManager>();
        if (existing != null)
        {
            Instance = existing;
            return existing;
        }

        GameObject hub = GameObject.Find("DebugSystemsHub");
        GameObject host = hub != null ? hub : new GameObject(nameof(HistoryBranchManager));
        HistoryBranchManager mgr = host.GetComponent<HistoryBranchManager>();
        return mgr != null ? mgr : host.AddComponent<HistoryBranchManager>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        currentActiveBranch ??= new HistoryTimelineBranch();
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    /// <summary>
    /// 確定済みメイン歴史軸（または指定親軸）を引き継ぎ、現ターンの介入成果から新 IF 枝を創出します。
    /// </summary>
    public static GenerateBranchFromStateResult GenerateBranchFromCurrentState(
        int turn,
        int nationId,
        string keyEventId,
        string outcomeFlag = null,
        MacroParamDelta deltaOverride = null,
        string parentBranchId = null,
        string branchEventSuffix = null)
    {
        GenerateBranchFromStateResult result = new GenerateBranchFromStateResult();
        try
        {
            HistoryBranchManager mgr = EnsureInstance();
            return mgr.GenerateBranchFromCurrentStateInternal(
                turn,
                nationId,
                keyEventId,
                outcomeFlag,
                deltaOverride,
                parentBranchId,
                branchEventSuffix);
        }
        catch (Exception exception)
        {
            result.message = $"Safe-Fail: {exception.Message}";
            Debug.LogWarning($"[HistoryBranchManager] GenerateBranchFromCurrentState Safe-Fail: {exception.Message}");
            return result;
        }
    }

    /// <summary>親分岐に紐づく子 IF 枝 ID 一覧を返します。</summary>
    public bool TryGetChildBranchIds(string parentBranchId, out List<string> childIds)
    {
        childIds = new List<string>();
        HistoryTimelineBranch parent = FindRegisteredBranch(parentBranchId);
        if (parent?.childBranchIds == null || parent.childBranchIds.Count == 0)
        {
            return false;
        }

        childIds.AddRange(parent.childBranchIds);
        return childIds.Count > 0;
    }

    private GenerateBranchFromStateResult GenerateBranchFromCurrentStateInternal(
        int turn,
        int nationId,
        string keyEventId,
        string outcomeFlag,
        MacroParamDelta deltaOverride,
        string parentBranchId,
        string branchEventSuffix)
    {
        GenerateBranchFromStateResult result = new GenerateBranchFromStateResult();
        if (nationId <= 0 || string.IsNullOrWhiteSpace(keyEventId))
        {
            result.message = "nationId / keyEventId が無効";
            return result;
        }

        string resolvedParentId = string.IsNullOrWhiteSpace(parentBranchId)
            ? mainStoryBranchId
            : parentBranchId.Trim();
        if (string.IsNullOrWhiteSpace(resolvedParentId))
        {
            result.message = "親歴史軸（MainStoryBranchId）が未確定";
            return result;
        }

        HistoryTimelineBranch parent = FindRegisteredBranch(resolvedParentId);
        if (parent == null)
        {
            result.message = $"親分岐未登録 parent={resolvedParentId}";
            return result;
        }

        int normalizedTurn = EraContextResolver.ClampSimulationTurn(turn);
        string eventSuffix = string.IsNullOrWhiteSpace(branchEventSuffix)
            ? SanitizeEventSuffix(keyEventId)
            : branchEventSuffix.Trim().ToUpperInvariant();
        string flag = string.IsNullOrWhiteSpace(outcomeFlag)
            ? BuildOutcomeFlag(nationId, normalizedTurn, eventSuffix)
            : outcomeFlag.Trim();
        MacroParamDelta delta = deltaOverride ?? InferDeltaFromEvent(keyEventId);
        delta.SanitizeInPlace();

        HistoryTimelineBranch child = parent.Clone();
        child.parentBranchId = resolvedParentId;
        child.forkTurn = normalizedTurn;
        child.branchId = BuildBranchId(nationId, normalizedTurn, eventSuffix);
        child.isCommittedMainStory = false;
        child.committedAtTurn = 0;
        child.childBranchIds = new List<string>();
        if (string.IsNullOrWhiteSpace(child.displayName) ||
            string.Equals(child.displayName, parent.displayName, StringComparison.OrdinalIgnoreCase))
        {
            child.displayName = $"IF_Line: {child.branchId}";
        }

        HistoryBranchNode node = new HistoryBranchNode
        {
            turn = normalizedTurn,
            nationId = nationId,
            keyEventId = keyEventId.Trim(),
            outcomeFlag = flag,
            delta = delta
        };
        child.AddNode(node);

        LinkChildBranch(resolvedParentId, child.branchId);
        currentActiveBranch = child;
        canonMode = false;
        activeBranchId = child.branchId;
        UpsertRegisteredBranch(child);

        HistoryFlagRegistry.EnsureWired();
        HistoryFlagRegistry.Unlock(flag);

        result.success = true;
        result.branchId = child.branchId;
        result.parentBranchId = resolvedParentId;
        result.outcomeFlag = flag;
        result.forkTurn = normalizedTurn;
        result.message =
            $"親={resolvedParentId} 子={child.branchId} nodes={child.nodes.Count} OUT={flag} " +
            $"Power×{delta.powerMultiplier:F2} BarrierΔ{delta.barrierEfficiencyDelta:+0.00;-0.00}";
        Debug.Log(
            $"<color=#FFB74D><b>{LogTag}</b></color> 現状態分岐 T{normalizedTurn} 国家{nationId:D3} " +
            $"親軸={resolvedParentId} → 新枝={child.branchId} {result.message}");
        return result;
    }

    private void LinkChildBranch(string parentId, string childId)
    {
        if (string.IsNullOrWhiteSpace(parentId) || string.IsNullOrWhiteSpace(childId))
        {
            return;
        }

        HistoryTimelineBranch parent = FindRegisteredBranch(parentId);
        if (parent == null)
        {
            return;
        }

        parent.childBranchIds ??= new List<string>();
        bool exists = false;
        for (int i = 0; i < parent.childBranchIds.Count; i++)
        {
            if (string.Equals(parent.childBranchIds[i], childId, StringComparison.OrdinalIgnoreCase))
            {
                exists = true;
                break;
            }
        }

        if (!exists)
        {
            parent.childBranchIds.Add(childId);
        }

        UpsertRegisteredBranch(parent);
    }

    /// <summary>重大イベント改変成功時に ALT_ 分岐を生成/更新します。</summary>
    public static HistoryAlterationResult RegisterHistoryAlteration(
        int turn,
        int nationId,
        string keyEventId,
        string outcomeFlag = null,
        MacroParamDelta deltaOverride = null,
        bool createNewBranch = false)
    {
        HistoryAlterationResult result = new HistoryAlterationResult();
        try
        {
            HistoryBranchManager mgr = EnsureInstance();
            return mgr.RegisterInternal(turn, nationId, keyEventId, outcomeFlag, deltaOverride, createNewBranch);
        }
        catch (Exception exception)
        {
            result.message = $"Safe-Fail: {exception.Message}";
            Debug.LogWarning($"[HistoryBranchManager] RegisterHistoryAlteration Safe-Fail: {exception.Message}");
            return result;
        }
    }

    private HistoryAlterationResult RegisterInternal(
        int turn,
        int nationId,
        string keyEventId,
        string outcomeFlag,
        MacroParamDelta deltaOverride,
        bool createNewBranch)
    {
        HistoryAlterationResult result = new HistoryAlterationResult();
        if (nationId <= 0 || string.IsNullOrWhiteSpace(keyEventId))
        {
            result.message = "nationId / keyEventId が無効";
            return result;
        }

        if (createNewBranch && !string.IsNullOrWhiteSpace(currentActiveBranch.branchId))
        {
            UpsertRegisteredBranch(currentActiveBranch);
            currentActiveBranch = new HistoryTimelineBranch();
        }

        int normalizedTurn = EraContextResolver.ClampSimulationTurn(turn);
        string eventSuffix = SanitizeEventSuffix(keyEventId);
        string flag = string.IsNullOrWhiteSpace(outcomeFlag)
            ? BuildOutcomeFlag(nationId, normalizedTurn, eventSuffix)
            : outcomeFlag.Trim();

        MacroParamDelta delta = deltaOverride ?? InferDeltaFromEvent(keyEventId);
        delta.SanitizeInPlace();

        if (string.IsNullOrWhiteSpace(currentActiveBranch.branchId))
        {
            currentActiveBranch.branchId = BuildBranchId(nationId, normalizedTurn, eventSuffix);
            currentActiveBranch.baseStartTurn = normalizedTurn;
            currentActiveBranch.primaryNationId = nationId;
        }

        HistoryBranchNode node = new HistoryBranchNode
        {
            turn = normalizedTurn,
            nationId = nationId,
            keyEventId = keyEventId.Trim(),
            outcomeFlag = flag,
            delta = delta
        };
        currentActiveBranch.AddNode(node);
        canonMode = false;
        activeBranchId = currentActiveBranch.branchId;
        UpsertRegisteredBranch(currentActiveBranch);

        HistoryFlagRegistry.EnsureWired();
        HistoryFlagRegistry.Unlock(flag);

        result.success = true;
        result.branchId = currentActiveBranch.branchId;
        result.outcomeFlag = flag;
        result.appliedFromTurn = normalizedTurn + 1;
        result.message =
            $"分岐={currentActiveBranch.branchId} ノード={currentActiveBranch.nodes.Count} " +
            $"翌T{result.appliedFromTurn}〜波及 Power×{delta.powerMultiplier:F2} " +
            $"BarrierΔ{delta.barrierEfficiencyDelta:+0.00;-0.00} " +
            $"Threat×{delta.threatMultiplier:F2} 生存上書={delta.isSurvivalOverridden}";
        Debug.Log(
            $"<color=#FFB74D><b>{LogTag}</b></color> 改変登録 T{normalizedTurn} 国家{nationId:D3} " +
            $"イベント={keyEventId} OUT={flag} {result.message}");
        return result;
    }

    /// <summary>歴史軸（正史 HIST_ または ALT_ 分岐）をアクティブ化します。</summary>
    public static TimelineSelectResult SelectTimelineBranch(string branchId)
    {
        TimelineSelectResult result = new TimelineSelectResult { branchId = branchId ?? string.Empty };
        try
        {
            HistoryBranchManager mgr = EnsureInstance();
            return mgr.SelectInternal(branchId);
        }
        catch (Exception exception)
        {
            result.message = $"Safe-Fail: {exception.Message}";
            Debug.LogWarning($"[HistoryBranchManager] SelectTimelineBranch Safe-Fail: {exception.Message}");
            return result;
        }
    }

    /// <summary>選択可能な歴史軸一覧（正史 + 登録済み ALT_）。</summary>
    public static List<TimelineBranchSummary> ListAvailableTimelines()
    {
        List<TimelineBranchSummary> list = new List<TimelineBranchSummary>();
        try
        {
            HistoryBranchManager mgr = EnsureInstance();
            list.Add(new TimelineBranchSummary
            {
                branchId = CanonBranchId,
                label = "正史 (HIST_)",
                nodeCount = 0,
                baseStartTurn = 0,
                isCanon = true,
                isActive = mgr.IsCanonMode
            });

            if (mgr.registeredBranches == null)
            {
                return list;
            }

            for (int i = 0; i < mgr.registeredBranches.Count; i++)
            {
                HistoryTimelineBranch branch = mgr.registeredBranches[i];
                if (branch == null || string.IsNullOrWhiteSpace(branch.branchId))
                {
                    continue;
                }

                list.Add(new TimelineBranchSummary
                {
                    branchId = branch.branchId,
                    label = FormatBranchLabel(branch),
                    nodeCount = branch.nodes?.Count ?? 0,
                    baseStartTurn = branch.baseStartTurn,
                    isCanon = false,
                    isActive = !mgr.IsCanonMode &&
                               string.Equals(mgr.activeBranchId, branch.branchId, StringComparison.OrdinalIgnoreCase),
                    isMainStory = mgr.HasCommittedMainStory &&
                                  string.Equals(mgr.mainStoryBranchId, branch.branchId, StringComparison.OrdinalIgnoreCase)
                });
            }
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[HistoryBranchManager] ListAvailableTimelines Safe-Fail: {exception.Message}");
        }

        return list;
    }

    /// <summary>評価済み ALT_ 分岐を新たなメイン歴史軸（正史ベースライン）として確定します。</summary>
    public static MainStoryCommitResult CommitBranchAsMainStory(
        string branchId,
        int? knownTotalScore = null,
        int? evaluationTurn = null)
    {
        MainStoryCommitResult result = new MainStoryCommitResult { branchId = branchId ?? string.Empty };
        try
        {
            HistoryBranchManager mgr = EnsureInstance();
            return mgr.CommitMainStoryInternal(branchId, knownTotalScore, evaluationTurn);
        }
        catch (Exception exception)
        {
            result.message = $"Safe-Fail: {exception.Message}";
            Debug.LogWarning($"[HistoryBranchManager] CommitBranchAsMainStory Safe-Fail: {exception.Message}");
            return result;
        }
    }

    /// <summary>現在ターン時点の最高スコア ALT_ 軸を自動選定して Commit します。</summary>
    public static MainStoryCommitResult CommitBestBranchAtCurrentTurn(int? targetTurn = null)
    {
        return TimelineScriptEvaluator.CommitBestBranchAtCurrentTurn(targetTurn);
    }

    private MainStoryCommitResult CommitMainStoryInternal(
        string branchId,
        int? knownTotalScore,
        int? evaluationTurn)
    {
        MainStoryCommitResult result = new MainStoryCommitResult { branchId = branchId ?? string.Empty };
        if (string.IsNullOrWhiteSpace(branchId) || IsCanonBranchKey(branchId))
        {
            result.message = "正史 HIST_CANON はメイン IF 軸として確定できません";
            return result;
        }

        HistoryTimelineBranch source = FindRegisteredBranch(branchId);
        if (source == null)
        {
            result.message = $"未登録 branchId={branchId}";
            return result;
        }

        int totalScore = knownTotalScore ??
                         TimelineScriptEvaluator.EvaluateTimelineBranch(
                             source,
                             evaluationTurn).totalScore;

        HistoryTimelineBranch committed = source.Clone();
        committed.isCommittedMainStory = true;
        committed.committedAtTurn = EraContextResolver.ClampSimulationTurn(
            evaluationTurn ?? TimelineScriptEvaluator.ResolveCurrentEvaluationTurn());
        if (string.IsNullOrWhiteSpace(committed.displayName))
        {
            committed.displayName = $"IF_Line: {committed.branchId}";
        }

        UpsertRegisteredBranch(committed);
        currentActiveBranch = committed.Clone();
        canonMode = false;
        activeBranchId = committed.branchId;
        mainStoryBranchId = committed.branchId;

        string category = ResolveBranchCategory(committed.branchId);
        int eruptedEnergy = GetLatentEnergy(category);
        NotifyMainStoryCategoryCommitted(category, eruptedEnergy);

        if (committed.nodes != null)
        {
            HistoryFlagRegistry.EnsureWired();
            for (int i = 0; i < committed.nodes.Count; i++)
            {
                HistoryBranchNode node = committed.nodes[i];
                if (!string.IsNullOrWhiteSpace(node?.outcomeFlag))
                {
                    HistoryFlagRegistry.Unlock(node.outcomeFlag);
                }
            }
        }

        result.success = true;
        result.branchId = committed.branchId;
        result.branchName = TimelineScriptEvaluator.ResolveBranchDisplayName(committed);
        result.totalScore = totalScore;
        result.message =
            $"メイン歴史軸確定 T{committed.committedAtTurn} nodes={committed.nodes?.Count ?? 0} " +
            $"score={totalScore} active={activeBranchId}";
        Debug.Log(
            $"<color=#CE93D8><b>{TimelineScriptEvaluator.AdoptLogTag}</b></color> " +
            $"「{result.branchName}」がメインストーリー軸として決定されました！ (総合スコア: {totalScore})");
        return result;
    }

    /// <summary>検証用: 登録済み分岐を取得します。</summary>
    public HistoryTimelineBranch FindRegisteredBranchForVerification(string branchId)
    {
        return FindRegisteredBranch(branchId);
    }

    /// <summary>検証用: 登録済み分岐を更新します。</summary>
    public void UpsertRegisteredBranchForVerification(HistoryTimelineBranch branch)
    {
        UpsertRegisteredBranch(branch);
    }

    private TimelineSelectResult SelectInternal(string branchId)
    {
        TimelineSelectResult result = new TimelineSelectResult { branchId = branchId ?? string.Empty };
        if (IsCanonBranchKey(branchId))
        {
            ResetToCanonMode();
            result.success = true;
            result.isCanonMode = true;
            result.branchId = CanonBranchId;
            result.message = "正史 HIST_ モードをアクティブ化";
            Debug.Log($"<color=#A5D6A7><b>{LogTag}</b></color> タイムライン切替 → 正史");
            return result;
        }

        HistoryTimelineBranch found = FindRegisteredBranch(branchId);
        if (found == null)
        {
            result.message = $"未登録 branchId={branchId}";
            return result;
        }

        currentActiveBranch = found.Clone();
        canonMode = false;
        activeBranchId = currentActiveBranch.branchId;
        result.success = true;
        result.isCanonMode = false;
        result.branchId = activeBranchId;
        result.message = $"ALT_ 分岐をアクティブ化 nodes={currentActiveBranch.nodes?.Count ?? 0}";
        Debug.Log(
            $"<color=#FFB74D><b>{LogTag}</b></color> タイムライン切替 → {activeBranchId} " +
            $"(T{currentActiveBranch.baseStartTurn}〜 nodes={currentActiveBranch.nodes?.Count ?? 0})");
        return result;
    }

    private void UpsertRegisteredBranch(HistoryTimelineBranch branch)
    {
        if (branch == null || string.IsNullOrWhiteSpace(branch.branchId))
        {
            return;
        }

        registeredBranches ??= new List<HistoryTimelineBranch>();
        for (int i = 0; i < registeredBranches.Count; i++)
        {
            if (registeredBranches[i] != null &&
                string.Equals(registeredBranches[i].branchId, branch.branchId, StringComparison.OrdinalIgnoreCase))
            {
                registeredBranches[i] = branch.Clone();
                return;
            }
        }

        registeredBranches.Add(branch.Clone());
    }

    private HistoryTimelineBranch FindRegisteredBranch(string branchId)
    {
        if (registeredBranches == null || string.IsNullOrWhiteSpace(branchId))
        {
            return null;
        }

        for (int i = 0; i < registeredBranches.Count; i++)
        {
            HistoryTimelineBranch branch = registeredBranches[i];
            if (branch != null &&
                string.Equals(branch.branchId, branchId.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return branch;
            }
        }

        return null;
    }

    public static bool IsCanonBranchKey(string branchId)
    {
        if (string.IsNullOrWhiteSpace(branchId))
        {
            return true;
        }

        string trimmed = branchId.Trim();
        return string.Equals(trimmed, CanonBranchId, StringComparison.OrdinalIgnoreCase) ||
               (trimmed.StartsWith("HIST_", StringComparison.OrdinalIgnoreCase) &&
                !trimmed.StartsWith(AltBranchPrefix, StringComparison.OrdinalIgnoreCase));
    }

    private static string FormatBranchLabel(HistoryTimelineBranch branch)
    {
        if (branch == null)
        {
            return "ALT_?";
        }

        if (!string.IsNullOrWhiteSpace(branch.displayName))
        {
            return branch.displayName;
        }

        return $"{branch.branchId} (T{branch.baseStartTurn} nodes={branch.nodes?.Count ?? 0})";
    }

    /// <summary>正史ベース国力に ALT_ 補正を適用します（正史モード時は base をそのまま返す）。</summary>
    public static float GetAdjustedPower(float basePower, int turn, int nationId)
    {
        try
        {
            HistoryBranchManager mgr = Instance;
            if (mgr == null || mgr.IsCanonMode)
            {
                return SanitizeBase(basePower, 1000f);
            }

            if (!mgr.currentActiveBranch.TryGetDeltaForTurn(turn, nationId, out MacroParamDelta delta))
            {
                return SanitizeBase(basePower, 1000f);
            }

            return SanitizeBase(basePower * delta.powerMultiplier, 1000f);
        }
        catch (Exception)
        {
            return SanitizeBase(basePower, 1000f);
        }
    }

    /// <summary>正史ベース結界（0〜1）に ALT_ 加算補正を適用します。</summary>
    public static float GetAdjustedBarrier(float baseBarrierNorm, int turn, int nationId)
    {
        try
        {
            HistoryBranchManager mgr = Instance;
            if (mgr == null || mgr.IsCanonMode)
            {
                return Mathf.Clamp01(baseBarrierNorm);
            }

            if (!mgr.currentActiveBranch.TryGetDeltaForTurn(turn, nationId, out MacroParamDelta delta))
            {
                return Mathf.Clamp01(baseBarrierNorm);
            }

            return Mathf.Clamp01(baseBarrierNorm + delta.barrierEfficiencyDelta);
        }
        catch (Exception)
        {
            return Mathf.Clamp01(baseBarrierNorm);
        }
    }

    /// <summary>正史生存フラグに ALT_ 上書きを適用します。</summary>
    public static bool GetAdjustedSurvival(bool baseAlive, int turn, int nationId)
    {
        try
        {
            HistoryBranchManager mgr = Instance;
            if (mgr == null || mgr.IsCanonMode)
            {
                return baseAlive;
            }

            if (!mgr.currentActiveBranch.TryGetDeltaForTurn(turn, nationId, out MacroParamDelta delta))
            {
                return baseAlive;
            }

            return delta.isSurvivalOverridden ? delta.survivalValue : baseAlive;
        }
        catch (Exception)
        {
            return baseAlive;
        }
    }

    /// <summary>脅威度ベース値へ ALT_ 乗数を適用します。</summary>
    public static float GetAdjustedThreat(float baseThreat, int turn, int nationId)
    {
        try
        {
            HistoryBranchManager mgr = Instance;
            if (mgr == null || mgr.IsCanonMode)
            {
                return SanitizeBase(baseThreat, 1f);
            }

            if (!mgr.currentActiveBranch.TryGetDeltaForTurn(turn, nationId, out MacroParamDelta delta))
            {
                return SanitizeBase(baseThreat, 1f);
            }

            return SanitizeBase(baseThreat * delta.threatMultiplier, 1f);
        }
        catch (Exception)
        {
            return SanitizeBase(baseThreat, 1f);
        }
    }

    /// <summary>MicroToMacroAggregator 等からマクロ統計へ補正を適用します。</summary>
    public static void TryApplyToMacroStats(MacroStats stats, int turn, int nationId)
    {
        if (stats == null)
        {
            return;
        }

        try
        {
            stats.Power = GetAdjustedPower(stats.Power, turn, nationId);
            stats.BarrierEfficiency = GetAdjustedBarrier(stats.BarrierEfficiency, turn, nationId);
            stats.Economy = GetAdjustedPower(stats.Economy, turn, nationId);
            stats.Military = GetAdjustedPower(stats.Military, turn, nationId);
            stats.Magic = GetAdjustedPower(stats.Magic, turn, nationId);
            stats.RecalculatePower();
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[HistoryBranchManager] TryApplyToMacroStats Safe-Fail: {exception.Message}");
        }
    }

    /// <summary>検証用: 正史モードへリセット。</summary>
    public void ResetToCanonMode()
    {
        currentActiveBranch = new HistoryTimelineBranch();
        activeBranchId = string.Empty;
        canonMode = true;
    }

    /// <summary>検証用: 正史モードへリセット（メイン歴史軸 ID もクリア）。</summary>
    public void ResetToCanonModeIncludingMainStory()
    {
        mainStoryBranchId = string.Empty;
        ResetToCanonMode();
    }

    /// <summary>検証用: 登録済み分岐をクリア。</summary>
    public void ClearRegisteredBranches()
    {
        registeredBranches?.Clear();
        ResetToCanonModeIncludingMainStory();
        ResetLatentEnergyStateForVerification();
    }

    // -------------------------------------------------------------------------
    // 不遇枝エネルギー蓄積・歴史噴出モデル
    // -------------------------------------------------------------------------

    /// <summary>枝 ID からテーマ・カテゴリキーを解決します（Safe-Fail: UNKNOWN）。</summary>
    public static string ResolveBranchCategory(string branchId)
    {
        if (string.IsNullOrWhiteSpace(branchId))
        {
            return "UNKNOWN";
        }

        string id = branchId.Trim();
        // 世代サフィックス _T1050 等を除去
        int tIdx = id.LastIndexOf("_T", StringComparison.OrdinalIgnoreCase);
        if (tIdx > 0)
        {
            string tail = id.Substring(tIdx + 2);
            bool allDigits = tail.Length > 0;
            for (int i = 0; i < tail.Length && allDigits; i++)
            {
                if (!char.IsDigit(tail[i]))
                {
                    allDigits = false;
                }
            }

            if (allDigits)
            {
                id = id.Substring(0, tIdx);
            }
        }

        const string revivalPrefix = "ALT_REVIVAL_";
        if (id.StartsWith(revivalPrefix, StringComparison.OrdinalIgnoreCase))
        {
            string cat = id.Substring(revivalPrefix.Length);
            return string.IsNullOrWhiteSpace(cat) ? "UNKNOWN" : cat;
        }

        // ALT_..._OUTCOME_T007_INNOVATION → INNOVATION
        int outcomeIdx = id.IndexOf("_OUTCOME_", StringComparison.OrdinalIgnoreCase);
        if (outcomeIdx >= 0)
        {
            string after = id.Substring(outcomeIdx + "_OUTCOME_".Length);
            int us = after.IndexOf('_');
            if (us >= 0 && us + 1 < after.Length)
            {
                // T007_INNOVATION → INNOVATION
                string rest = after.Substring(us + 1);
                if (!string.IsNullOrWhiteSpace(rest))
                {
                    return rest;
                }
            }
        }

        // ALT_VERIFY_*_BEAST_CATASTROPHE → 末尾トークン群
        int last = id.LastIndexOf('_');
        if (last > 0 && last + 1 < id.Length)
        {
            return id.Substring(last + 1);
        }

        return id;
    }

    public static int GetLatentEnergy(string category)
    {
        HistoryBranchManager mgr = EnsureInstance();
        string key = NormalizeCategoryKey(category);
        if (string.IsNullOrEmpty(key))
        {
            return 0;
        }

        return mgr.unChosenBranchLatentEnergy.TryGetValue(key, out int energy)
            ? Mathf.Max(0, energy)
            : 0;
    }

    public static int GetLatentEnergyBonusForBranch(string branchId)
    {
        return GetLatentEnergy(ResolveBranchCategory(branchId));
    }

    public static int GetCategoryCooldown(string category)
    {
        HistoryBranchManager mgr = EnsureInstance();
        string key = NormalizeCategoryKey(category);
        if (string.IsNullOrEmpty(key))
        {
            return 0;
        }

        return mgr.categoryCooldowns.TryGetValue(key, out int cd) ? Mathf.Max(0, cd) : 0;
    }

    public static bool IsCategoryOnCooldown(string category)
    {
        return GetCategoryCooldown(category) > 0;
    }

    /// <summary>世代進行時: 全カテゴリのクールダウンを 1 減算し、0 で解除します。</summary>
    public static void TickCategoryCooldowns()
    {
        try
        {
            HistoryBranchManager mgr = EnsureInstance();
            if (mgr.categoryCooldowns.Count == 0)
            {
                return;
            }

            List<string> keys = new List<string>(mgr.categoryCooldowns.Keys);
            for (int i = 0; i < keys.Count; i++)
            {
                string key = keys[i];
                int next = Mathf.Max(0, mgr.categoryCooldowns[key] - 1);
                if (next <= 0)
                {
                    mgr.categoryCooldowns.Remove(key);
                }
                else
                {
                    mgr.categoryCooldowns[key] = next;
                }
            }
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[HistoryBranchManager] TickCategoryCooldowns Safe-Fail: {exception.Message}");
        }
    }

    /// <summary>
    /// 健全な不遇枝カテゴリへ +25pt 蓄積します。
    /// クールダウン中は +0（蓄積しない）。
    /// </summary>
    public static int TryAccumulateLatentEnergy(string category, out int currentEnergy)
    {
        currentEnergy = 0;
        try
        {
            HistoryBranchManager mgr = EnsureInstance();
            string key = NormalizeCategoryKey(category);
            if (string.IsNullOrEmpty(key) ||
                string.Equals(key, "UNKNOWN", StringComparison.OrdinalIgnoreCase))
            {
                return 0;
            }

            if (mgr.categoryCooldowns.TryGetValue(key, out int cd) && cd > 0)
            {
                currentEnergy = GetLatentEnergy(key);
                return 0;
            }

            int before = GetLatentEnergy(key);
            int after = before + LatentEnergyPerGeneration;
            mgr.unChosenBranchLatentEnergy[key] = after;
            currentEnergy = after;

            string line =
                $"{LatentEnergyAccumLogTag} カテゴリ:{key} に +{LatentEnergyPerGeneration}pt 蓄積 (現在: {currentEnergy}pt)";
            Debug.Log($"<color=#FFE082><b>{line}</b></color>");
            AppendLatentEnergyPipelineLog(line);
            return LatentEnergyPerGeneration;
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[HistoryBranchManager] TryAccumulateLatentEnergy Safe-Fail: {exception.Message}");
            currentEnergy = 0;
            return 0;
        }
    }

    /// <summary>正史採択時: エネルギー 0 リセット＋クールダウン開始。</summary>
    public static void NotifyMainStoryCategoryCommitted(string category, int eruptedEnergy)
    {
        try
        {
            HistoryBranchManager mgr = EnsureInstance();
            string key = NormalizeCategoryKey(category);
            if (string.IsNullOrEmpty(key))
            {
                return;
            }

            mgr.unChosenBranchLatentEnergy[key] = 0;
            mgr.categoryCooldowns[key] = CategoryCooldownGenerations;

            if (eruptedEnergy > 0)
            {
                string line =
                    $"{LatentEnergyEruptLogTag} 蓄積ボーナス(+{eruptedEnergy}pt)により カテゴリ:{key} が正史へ選出！ " +
                    $"ボーナスリセット＆{CategoryCooldownGenerations}世代クールダウン開始。";
                Debug.Log($"<color=#FF8A65><b>{line}</b></color>");
                AppendLatentEnergyPipelineLog(line);
            }
            else
            {
                string line =
                    $"{LatentEnergyEruptLogTag} カテゴリ:{key} が正史へ選出（蓄積0pt）。" +
                    $"ボーナスリセット＆{CategoryCooldownGenerations}世代クールダウン開始。";
                Debug.Log(line);
                AppendLatentEnergyPipelineLog(line);
            }
        }
        catch (Exception exception)
        {
            Debug.LogWarning(
                $"[HistoryBranchManager] NotifyMainStoryCategoryCommitted Safe-Fail: {exception.Message}");
        }
    }

    public void ResetLatentEnergyStateForVerification()
    {
        unChosenBranchLatentEnergy.Clear();
        categoryCooldowns.Clear();
    }

    private static string NormalizeCategoryKey(string category)
    {
        return string.IsNullOrWhiteSpace(category) ? string.Empty : category.Trim();
    }

    private static void AppendLatentEnergyPipelineLog(string line)
    {
        try
        {
            string unityProjectRoot = Directory.GetParent(Application.dataPath)?.FullName
                                      ?? Application.dataPath;
            string repoRoot = Directory.GetParent(unityProjectRoot)?.FullName ?? unityProjectRoot;
            string stamp = $"{DateTime.Now:yyyy-MM-ddTHH:mm:ss} {line}\n";
            string[] paths =
            {
                Path.Combine(repoRoot, "Logs", "pipeline_execution.log"),
                Path.Combine(unityProjectRoot, "Logs", "pipeline_execution.log")
            };

            for (int i = 0; i < paths.Length; i++)
            {
                string path = paths[i];
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                try
                {
                    File.AppendAllText(path, stamp, Encoding.UTF8);
                }
                catch (IOException)
                {
                    // Unity -logFile 占有時は Safe-Fail でスキップ
                }
            }
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[HistoryBranchManager] LatentEnergy ログ Safe-Fail: {exception.Message}");
        }
    }

    private static float SanitizeBase(float value, float fallback)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
        {
            return fallback;
        }

        return value;
    }

    private static string BuildBranchId(int nationId, int turn, string eventSuffix)
    {
        return $"{AltBranchPrefix}NATION_{nationId:D3}_TURN_{turn:D3}_{eventSuffix}";
    }

    private static string BuildOutcomeFlag(int nationId, int turn, string eventSuffix)
    {
        return $"{AltBranchPrefix}NATION_{nationId:D3}_OUTCOME_T{turn:D3}_{eventSuffix}";
    }

    private static string SanitizeEventSuffix(string keyEventId)
    {
        if (string.IsNullOrWhiteSpace(keyEventId))
        {
            return "EVENT";
        }

        string trimmed = keyEventId.Trim();
        if (trimmed.StartsWith("HIST_", StringComparison.OrdinalIgnoreCase))
        {
            int lastUnderscore = trimmed.LastIndexOf('_');
            if (lastUnderscore >= 0 && lastUnderscore < trimmed.Length - 1)
            {
                return trimmed.Substring(lastUnderscore + 1).ToUpperInvariant();
            }
        }

        return trimmed.Replace(' ', '_').ToUpperInvariant();
    }

    private static MacroParamDelta InferDeltaFromEvent(string keyEventId)
    {
        string blob = keyEventId ?? string.Empty;
        MacroParamDelta delta = MacroParamDelta.Identity;

        if (blob.IndexOf("BARRIERDROP", StringComparison.OrdinalIgnoreCase) >= 0 ||
            blob.IndexOf("BARRIER_DROP", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            delta.powerMultiplier = 0.88f;
            delta.barrierEfficiencyDelta = -0.12f;
            delta.threatMultiplier = 1.35f;
            delta.isSurvivalOverridden = true;
            delta.survivalValue = false;
            return delta;
        }

        if (blob.IndexOf("HERO", StringComparison.OrdinalIgnoreCase) >= 0 ||
            blob.IndexOf("DEFENSE", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            delta.powerMultiplier = 1.08f;
            delta.barrierEfficiencyDelta = 0.06f;
            delta.threatMultiplier = 0.82f;
            return delta;
        }

        if (blob.IndexOf("INNOVATION", StringComparison.OrdinalIgnoreCase) >= 0 ||
            blob.IndexOf("CRAFT", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            delta.powerMultiplier = 1.12f;
            delta.barrierEfficiencyDelta = 0.03f;
            delta.threatMultiplier = 0.95f;
            return delta;
        }

        if (blob.IndexOf("CIVIL", StringComparison.OrdinalIgnoreCase) >= 0 ||
            blob.IndexOf("REBEL", StringComparison.OrdinalIgnoreCase) >= 0 ||
            blob.IndexOf("BORDER", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            delta.powerMultiplier = 0.92f;
            delta.barrierEfficiencyDelta = -0.05f;
            delta.threatMultiplier = 1.18f;
            return delta;
        }

        if (blob.IndexOf("ACADEMY", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            delta.powerMultiplier = 1.05f;
            delta.barrierEfficiencyDelta = 0.02f;
            delta.threatMultiplier = 1.05f;
            return delta;
        }

        delta.powerMultiplier = 1.02f;
        delta.barrierEfficiencyDelta = 0.01f;
        delta.threatMultiplier = 0.98f;
        return delta;
    }

    /// <summary>確定メイン軸から親を辿り、Gen1→GenN の正史幹チェーンを構築します。</summary>
    public List<CanonTimelineLineageNode> BuildMainStoryLineageChain(int leafScore = 0)
    {
        List<CanonTimelineLineageNode> chain = new List<CanonTimelineLineageNode>();
        if (string.IsNullOrWhiteSpace(mainStoryBranchId))
        {
            return chain;
        }

        List<HistoryTimelineBranch> path = new List<HistoryTimelineBranch>();
        string cursor = mainStoryBranchId;
        int guard = 0;
        while (!string.IsNullOrWhiteSpace(cursor) && guard++ < 32)
        {
            HistoryTimelineBranch branch = FindRegisteredBranch(cursor);
            if (branch == null)
            {
                break;
            }

            path.Add(branch);
            cursor = branch.parentBranchId;
        }

        path.Reverse();
        for (int i = 0; i < path.Count; i++)
        {
            HistoryTimelineBranch branch = path[i];
            int score = 0;
            if (i == path.Count - 1 && leafScore > 0)
            {
                score = leafScore;
            }
            else if (branch.committedAtTurn > 0)
            {
                TimelineScriptEvaluationResult eval =
                    TimelineScriptEvaluator.EvaluateTimelineBranch(branch, branch.committedAtTurn);
                score = eval.totalScore;
            }

            chain.Add(new CanonTimelineLineageNode
            {
                generation = i + 1,
                branchId = branch.branchId,
                label = ResolveLineageDisplayLabel(branch),
                committedAtTurn = branch.committedAtTurn > 0
                    ? branch.committedAtTurn
                    : branch.baseStartTurn,
                score = score,
                parentBranchId = branch.parentBranchId ?? string.Empty
            });
        }

        return chain;
    }

    private static string ResolveLineageDisplayLabel(HistoryTimelineBranch branch)
    {
        if (branch == null)
        {
            return string.Empty;
        }

        string branchId = branch.branchId ?? string.Empty;
        if (branchId.IndexOf("ARCANE_REVIVAL", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "IF_Line: 魔導復興";
        }

        if (branchId.IndexOf("INNOVATION", StringComparison.OrdinalIgnoreCase) >= 0 &&
            string.IsNullOrWhiteSpace(branch.displayName))
        {
            return "IF_Line: 魔導革新";
        }

        return TimelineScriptEvaluator.ResolveBranchDisplayName(branch);
    }

    /// <summary>250年正史データを JSON ログと GameMasters / geo へ書き戻します。</summary>
    public static CanonTimelineExportResult TryExportCanonTimeline250(
        int finalTurn = 250,
        int midCycleTurns = EnvironmentBiorhythmEngine.MidCycleTurns,
        int committedScore = 0)
    {
        CanonTimelineExportResult result = new CanonTimelineExportResult();
        try
        {
            HistoryBranchManager mgr = EnsureInstance();
            if (!mgr.HasCommittedMainStory)
            {
                result.message = "MainStoryBranchId 未確定";
                return result;
            }

            finalTurn = Mathf.Clamp(finalTurn, EraContextResolver.MinTurn, EraContextResolver.MaxTurn);
            result.lineage = mgr.BuildMainStoryLineageChain(committedScore);
            if (result.lineage.Count == 0)
            {
                result.message = "正史幹チェーンが空";
                return result;
            }

            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
            string logsDir = Path.Combine(projectRoot, "Logs");
            Directory.CreateDirectory(logsDir);
            result.exportLogPath = Path.Combine(logsDir, "canon_timeline_250.json");

            StringBuilder sb = new StringBuilder(2048);
            sb.Append("{\n");
            sb.Append("  \"finalized_at_utc\": \"").Append(DateTime.UtcNow.ToString("O")).Append("\",\n");
            sb.Append("  \"final_turn\": ").Append(finalTurn).Append(",\n");
            sb.Append("  \"mid_cycle_turns\": ").Append(midCycleTurns).Append(",\n");
            sb.Append("  \"main_story_branch_id\": \"").Append(EscapeJson(mgr.mainStoryBranchId)).Append("\",\n");
            sb.Append("  \"lineage\": [\n");
            for (int i = 0; i < result.lineage.Count; i++)
            {
                CanonTimelineLineageNode node = result.lineage[i];
                if (i > 0)
                {
                    sb.Append(",\n");
                }

                sb.Append("    {\n");
                sb.Append("      \"generation\": ").Append(node.generation).Append(",\n");
                sb.Append("      \"branch_id\": \"").Append(EscapeJson(node.branchId)).Append("\",\n");
                sb.Append("      \"label\": \"").Append(EscapeJson(node.label)).Append("\",\n");
                sb.Append("      \"committed_at_turn\": ").Append(node.committedAtTurn).Append(",\n");
                sb.Append("      \"score\": ").Append(node.score).Append(",\n");
                sb.Append("      \"parent_branch_id\": \"").Append(EscapeJson(node.parentBranchId)).Append("\"\n");
                sb.Append("    }");
            }

            sb.Append("\n  ]\n}\n");
            File.WriteAllText(result.exportLogPath, sb.ToString(), Encoding.UTF8);
            result.chronicleLogWritten = true;

            result.geoPath = MacroChronicleJsonScan.ResolveDataFile("macro_chronicle_geo.json");
            MicroToMacroAggregator aggregator = MicroToMacroAggregator.EnsureInstance();
            aggregator.PrepareTurnTransition(finalTurn, applyBranchAdjustments: true);
            MacroGeoNationWriteback dto = aggregator.BuildWriteback();
            dto.turn = finalTurn;
            dto.source = "HistoryBranchManager:CanonTimeline250";
            if (MacroChronicleJsonScan.TryWriteNationAtTurn(
                    result.geoPath,
                    finalTurn,
                    MicroToMacroAggregator.DefaultNationId,
                    dto,
                    out string geoError))
            {
                result.geoWritebackOk = true;
            }
            else
            {
                result.message = $"geo 書き戻し: {geoError}";
            }

            result.gameMastersPath = Path.Combine(
                Application.dataPath,
                "Resources",
                "GameMasters",
                "GameMasters_Sample.json");
            result.gameMastersWritebackOk = TryPatchGameMastersCanonTimeline(
                result.gameMastersPath,
                mgr.mainStoryBranchId,
                finalTurn,
                midCycleTurns,
                result.lineage);

            result.success = result.chronicleLogWritten &&
                             result.lineage.Count >= 5 &&
                             result.gameMastersWritebackOk;
            if (string.IsNullOrWhiteSpace(result.message) || result.geoWritebackOk)
            {
                result.message =
                    $"lineage={result.lineage.Count} geo={result.geoWritebackOk} " +
                    $"masters={result.gameMastersWritebackOk} main={mgr.mainStoryBranchId}";
            }

            return result;
        }
        catch (Exception exception)
        {
            result.message = $"Safe-Fail: {exception.Message}";
            Debug.LogWarning($"[HistoryBranchManager] TryExportCanonTimeline250 Safe-Fail: {exception.Message}");
            return result;
        }
    }

    private static bool TryPatchGameMastersCanonTimeline(
        string path,
        string mainStoryBranchId,
        int finalTurn,
        int midCycleTurns,
        List<CanonTimelineLineageNode> lineage)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return false;
        }

        try
        {
            string json = File.ReadAllText(path, Encoding.UTF8);
            const string propertyKey = "canonTimeline250";
            while (TryFindJsonPropertySpan(json, propertyKey, out int removeStart, out int removeLength))
            {
                json = json.Remove(removeStart, removeLength);
            }

            json = json.TrimEnd();
            if (json.EndsWith(",", StringComparison.Ordinal))
            {
                json = json.Substring(0, json.Length - 1);
            }

            StringBuilder block = new StringBuilder(1024);
            block.Append(",\n  \"canonTimeline250\": {\n");
            block.Append("    \"final_turn\": ").Append(finalTurn).Append(",\n");
            block.Append("    \"mid_cycle_turns\": ").Append(midCycleTurns).Append(",\n");
            block.Append("    \"main_story_branch_id\": \"").Append(EscapeJson(mainStoryBranchId)).Append("\",\n");
            block.Append("    \"lineage\": [\n");
            for (int i = 0; i < lineage.Count; i++)
            {
                CanonTimelineLineageNode node = lineage[i];
                if (i > 0)
                {
                    block.Append(",\n");
                }

                block.Append("      { \"generation\": ").Append(node.generation)
                    .Append(", \"branch_id\": \"").Append(EscapeJson(node.branchId))
                    .Append("\", \"label\": \"").Append(EscapeJson(node.label))
                    .Append("\", \"committed_at_turn\": ").Append(node.committedAtTurn)
                    .Append(", \"score\": ").Append(node.score).Append(" }");
            }

            block.Append("\n    ]\n  }");

            int closing = json.LastIndexOf('}');
            if (closing < 0)
            {
                return false;
            }

            string patched = json.Substring(0, closing) + block.ToString() + "\n}\n";
            File.WriteAllText(path, patched, Encoding.UTF8);
            return true;
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[HistoryBranchManager] GameMasters 書き戻し Safe-Fail: {exception.Message}");
            return false;
        }
    }

    private static bool TryFindJsonPropertySpan(string json, string propertyKey, out int removeStart, out int removeLength)
    {
        removeStart = 0;
        removeLength = 0;
        if (string.IsNullOrWhiteSpace(json) || string.IsNullOrWhiteSpace(propertyKey))
        {
            return false;
        }

        string marker = $"\"{propertyKey}\"";
        int keyIndex = json.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (keyIndex < 0)
        {
            return false;
        }

        removeStart = keyIndex;
        while (removeStart > 0 && char.IsWhiteSpace(json[removeStart - 1]))
        {
            removeStart--;
        }

        if (removeStart > 0 && json[removeStart - 1] == ',')
        {
            removeStart--;
        }

        int colonIndex = json.IndexOf(':', keyIndex + marker.Length);
        if (colonIndex < 0)
        {
            return false;
        }

        int braceStart = json.IndexOf('{', colonIndex);
        if (braceStart < 0)
        {
            return false;
        }

        int depth = 0;
        int braceEnd = -1;
        for (int i = braceStart; i < json.Length; i++)
        {
            char c = json[i];
            if (c == '{')
            {
                depth++;
            }
            else if (c == '}')
            {
                depth--;
                if (depth == 0)
                {
                    braceEnd = i;
                    break;
                }
            }
        }

        if (braceEnd < 0)
        {
            return false;
        }

        int removeEnd = braceEnd + 1;
        while (removeEnd < json.Length && char.IsWhiteSpace(json[removeEnd]))
        {
            removeEnd++;
        }

        if (removeEnd < json.Length && json[removeEnd] == ',')
        {
            removeEnd++;
        }

        removeLength = removeEnd - removeStart;
        return removeLength > 0;
    }

    private static string EscapeJson(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    public static HistoryBranchVerifyResult RunVerification()
    {
        HistoryBranchVerifyResult verify = new HistoryBranchVerifyResult();
        StringBuilder log = new StringBuilder();

        try
        {
            HistoryFlagRegistry.EnsureWired();
            HistoryBranchManager mgr = EnsureInstance();
            mgr.ClearRegisteredBranches();

            const float basePower = 1000f;
            const float baseBarrier = 0.95f;
            int nationId = MicroToMacroAggregator.DefaultNationId;

            float canonPower = GetAdjustedPower(basePower, 10, nationId);
            float canonBarrier = GetAdjustedBarrier(baseBarrier, 10, nationId);
            bool canonPass = Mathf.Approximately(canonPower, basePower) &&
                             Mathf.Approximately(canonBarrier, baseBarrier);
            log.AppendLine($"canon-mode: power={canonPower:F1} barrier={canonBarrier:F3} pass={canonPass}");

            HistoryAlterationResult reg = RegisterHistoryAlteration(
                5,
                nationId,
                "HIST_NATION_001_GEO_TURN_001_HERO",
                null,
                new MacroParamDelta
                {
                    powerMultiplier = 1.10f,
                    barrierEfficiencyDelta = 0.05f,
                    threatMultiplier = 0.90f
                });
            log.AppendLine($"register: success={reg.success} branch={reg.branchId} flag={reg.outcomeFlag}");

            float beforeTurn = GetAdjustedPower(basePower, 4, nationId);
            float sameTurn = GetAdjustedPower(basePower, 5, nationId);
            float afterTurn = GetAdjustedPower(basePower, 6, nationId);
            float barrierAfter = GetAdjustedBarrier(baseBarrier, 6, nationId);
            float threatAfter = GetAdjustedThreat(1.2f, 6, nationId);
            bool flagOk = HistoryFlagRegistry.IsUnlocked(reg.outcomeFlag);
            bool deltaPass =
                Mathf.Approximately(beforeTurn, basePower) &&
                Mathf.Approximately(sameTurn, basePower) &&
                afterTurn > basePower + 0.01f &&
                barrierAfter > baseBarrier + 0.01f &&
                threatAfter < 1.2f;
            log.AppendLine(
                $"delta T4={beforeTurn:F1} T5(same)={sameTurn:F1} T6 power={afterTurn:F1} barrier={barrierAfter:F3} " +
                $"threat={threatAfter:F2} flag={flagOk} pass={deltaPass}");

            MacroStats stats = MacroStats.CreateSafeDefaults();
            TryApplyToMacroStats(stats, 6, nationId);
            bool macroPass = stats.Power > basePower && stats.BarrierEfficiency > baseBarrier;
            log.AppendLine(
                $"macro-apply: power={stats.Power:F1} barrier={stats.BarrierEfficiency:F3} pass={macroPass}");

            bool survivalOverride = GetAdjustedSurvival(false, 6, nationId);
            RegisterHistoryAlteration(
                8,
                nationId,
                "HIST_NATION_001_GEO_TURN_001_BARRIERDROP",
                "ALT_NATION_001_TEST_SURVIVAL",
                new MacroParamDelta
                {
                    isSurvivalOverridden = true,
                    survivalValue = true
                });
            bool survivalPass = GetAdjustedSurvival(false, 9, nationId);
            log.AppendLine($"survival-override: {survivalOverride}→{survivalPass} pass={survivalPass}");

            string altBranchId = reg.branchId;
            HistoryAlterationResult branchB = RegisterHistoryAlteration(
                12,
                nationId,
                "HIST_NATION_001_GEO_TURN_001_INNOVATION",
                null,
                new MacroParamDelta { powerMultiplier = 1.15f, barrierEfficiencyDelta = 0.04f },
                createNewBranch: true);
            SelectTimelineBranch(CanonBranchId);
            float canonAfterReg = GetAdjustedPower(basePower, 6, nationId);
            SelectTimelineBranch(altBranchId);
            float altPower = GetAdjustedPower(basePower, 6, nationId);
            SelectTimelineBranch(branchB.branchId);
            float altBPower = GetAdjustedPower(basePower, 13, nationId);
            bool selectPass =
                Mathf.Approximately(canonAfterReg, basePower) &&
                altPower > basePower + 0.01f &&
                altBPower > basePower + 0.01f &&
                ListAvailableTimelines().Count >= 3;
            log.AppendLine(
                $"timeline-select: canon@{canonAfterReg:F0} altA@{altPower:F0} altB@{altBPower:F0} pass={selectPass}");

            verify.success = canonPass && reg.success && deltaPass && macroPass && flagOk && survivalPass && selectPass;
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

    private static void WriteVerifyLog(HistoryBranchVerifyResult verify)
    {
        try
        {
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
            string path = Path.Combine(projectRoot, "Logs", "history_branch_verify.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? projectRoot);
            File.WriteAllText(
                path,
                $"{DateTime.UtcNow:O} success={verify.success}\n{verify.message}\n",
                Encoding.UTF8);
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[HistoryBranchManager] 検証ログスキップ: {exception.Message}");
        }
    }
}

public static class HistoryBranchManagerBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoAttach()
    {
        HistoryBranchManager.EnsureInstance();
    }
}

#if UNITY_EDITOR
public static class HistoryBranchManagerMenu
{
    [MenuItem("Tools/Procedural Map/Verify History Branch Manager")]
    public static void VerifyFromMenu()
    {
        HistoryBranchVerifyResult result = HistoryBranchManager.RunVerification();
        Debug.Log(
            result.success
                ? $"<color=#A5D6A7><b>【歴史分岐検証】PASS</b></color>\n{result.message}"
                : $"<color=#FF8A80><b>【歴史分岐検証】FAIL</b></color>\n{result.message}");
        EditorUtility.DisplayDialog("History Branch Manager", result.message, "OK");
    }

    [MenuItem("Tools/Procedural Map/Register Test History Alteration")]
    public static void RegisterTestAlteration()
    {
        HistoryAlterationResult result = HistoryBranchManager.RegisterHistoryAlteration(
            EraContextResolver.CurrentTurn,
            MicroToMacroAggregator.DefaultNationId,
            MicroToMacroAggregator.FlagHero,
            null,
            null);
        EditorUtility.DisplayDialog("History Alteration", result.message, "OK");
    }

    /// <summary>Unity バッチ: -executeMethod HistoryBranchManagerMenu.BatchVerifyAndQuit</summary>
    public static void BatchVerifyAndQuit()
    {
        HistoryBranchVerifyResult result = HistoryBranchManager.RunVerification();
        Debug.Log(
            result.success
                ? $"<color=#A5D6A7><b>【歴史分岐検証】PASS</b></color>\n{result.message}"
                : $"<color=#FF8A80><b>【歴史分岐検証】FAIL</b></color>\n{result.message}");
        EditorApplication.Exit(result.success ? 0 : 1);
    }
}
#endif

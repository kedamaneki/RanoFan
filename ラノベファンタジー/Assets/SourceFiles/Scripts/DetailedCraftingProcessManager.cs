using System;
using System.Collections.Generic;
using UnityEngine;

// =============================================================================
// 職人のこだわり工程 — 能動選択・全履歴スタック・例外パケット回収
// 連携: CraftingExceptionCollector / CraftingExperimentHub / ChronosCoordinateHub
// =============================================================================

/// <summary>
/// オート生産や既定レシピを破壊し、職人の泥臭い工程選択を全履歴として蓄積するマネージャー。
/// 入力検知は DemoTimeLineManager の中央 Update が担当します。
/// </summary>
[DefaultExecutionOrder(800)]
public class DetailedCraftingProcessManager : MonoBehaviour
{
    public const string CraftTypeNone = "None";
    public const string CraftTypeForge = "Forge";
    public const string CraftTypeAlch = "Alch";

    public static DetailedCraftingProcessManager Instance { get; private set; }

    /// <summary>今回の生産でプレイヤーが実行した全工程の文字列リスト。</summary>
    public List<string> currentProcessLogs = new List<string>();

    /// <summary>現在の職種種別（Forge / Alch / None）。</summary>
    public string currentCraftType = CraftTypeNone;

    private CraftingExperimentHub craftingHub;
    private CraftingStatusManager statusManager;

    /// <summary>こだわり工程セッションが進行中か。</summary>
    public bool IsSessionActive { get; private set; }

    /// <summary>現在の職種（Forge / Alch）に応じて統一スロット1〜5の工程を実行します。</summary>
    /// <param name="slot1To5">Alpha1=1 〜 Alpha5=5</param>
    public void ExecuteUnifiedCraftSlot(int slot1To5)
    {
        statusManager ??= CraftingStatusManager.EnsureInstance();
        if (statusManager == null)
        {
            Debug.LogWarning("[DetailedCraftingProcessManager] CraftingStatusManager が見つかりません。");
            return;
        }

        statusManager.TryExecuteUnifiedCraftSlot(slot1To5, currentCraftType, this);
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[DetailedCraftingProcessManager] 重複インスタンスを検出しました。");
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

    /// <summary>シーンに無い場合は DebugSystemsHub へ動的生成して返します。</summary>
    public static DetailedCraftingProcessManager EnsureInstance()
    {
        if (Instance != null)
        {
            return Instance;
        }

        GameObject hub = GameObject.Find("DebugSystemsHub");
        if (hub != null)
        {
            DetailedCraftingProcessManager existing = hub.GetComponent<DetailedCraftingProcessManager>();
            return existing != null ? existing : hub.AddComponent<DetailedCraftingProcessManager>();
        }

        return new GameObject(nameof(DetailedCraftingProcessManager))
            .AddComponent<DetailedCraftingProcessManager>();
    }

    /// <summary>こだわり工程セッションを開始し、履歴をリセットします。</summary>
    /// <param name="craftType">Forge または Alch</param>
    public void BeginSession(string craftType)
    {
        currentProcessLogs.Clear();
        currentCraftType = NormalizeCraftType(craftType);
        IsSessionActive = !string.Equals(currentCraftType, CraftTypeNone, StringComparison.OrdinalIgnoreCase);

        CraftingStatusManager statusManager = CraftingStatusManager.EnsureInstance();
        if (statusManager != null && IsSessionActive)
        {
            statusManager.ResetStatus(currentCraftType);
            statusManager.BeginQualityEvaluationSession(currentCraftType);
        }

        // 生産系スキルの解放済み技ツリーから Craft_* 補正を工房セッションへ配線
        PlayerSkillSlotManager skillSlots = FindAnyObjectByType<PlayerSkillSlotManager>();
        ArtsEffectExecutor.EnsureInstance()?.ApplyProductionArtsCraftBonuses(skillSlots, currentCraftType);

        Debug.Log(
            $"<color=#4DD0E1><b>[DetailedCraftingProcessManager]</b> こだわり工程セッション開始 " +
            $"— 職種=<b>{currentCraftType}</b> / 裏ステータス初期化済み</color>");
    }

    /// <summary>こだわり工程セッションを終了し、状態を初期化します。</summary>
    public void EndSession()
    {
        CombatActionFeedbackManager combat = CombatActionFeedbackManager.Instance;
        combat?.NotifyDetailedCraftingFinished();

        IsSessionActive = false;
        currentCraftType = CraftTypeNone;
        currentProcessLogs.Clear();
        ArtsEffectExecutor.Instance?.ResetCraftSessionModifiers();
    }

    /// <summary>
    /// 被弾バーストによりこだわり工程を強制中断し、投入素材の記録を消失させます。
    /// バックパック内アイテムには触れません。
    /// </summary>
    public void InterruptSessionByDamageBurst()
    {
        Debug.Log(
            "<color=#FF0000><b>[DetailedCraftingProcessManager] " +
            "被弾によりこだわり工程ログはすべて消失した。</b></color>");

        EndSession();
    }

    /// <summary>
    /// プレイヤーの能動操作を1工程として記録し、コンソールへゼンゼロ風ログを出力します。
    /// </summary>
    /// <param name="actionDescription">工程の説明文</param>
    public void LogAction(string actionDescription)
    {
        if (string.IsNullOrWhiteSpace(actionDescription))
        {
            Debug.LogWarning("[DetailedCraftingProcessManager] 空の工程説明は記録できません。");
            return;
        }

        if (!IsSessionActive)
        {
            Debug.LogWarning(
                "[DetailedCraftingProcessManager] セッション未開始のため工程を記録できません。" +
                "先に C（鍛冶）または P（調合）で生産を開始してください。");
            return;
        }

        string stamped = $"[{currentCraftType}][Step{currentProcessLogs.Count + 1:D3}] {actionDescription.Trim()}";
        bool isFirstDetailedAction = currentProcessLogs.Count == 0;
        currentProcessLogs.Add(stamped);

        if (isFirstDetailedAction)
        {
            CombatActionFeedbackManager combat = CombatActionFeedbackManager.Instance
                ?? CombatActionFeedbackManager.EnsureInstance();
            combat?.NotifyDetailedCraftingStarted();
        }

        Debug.Log(
            $"<color=#00E5FF><b>[職人のこだわり]</b></color> " +
            $"<color=#80DEEA>➔</color> " +
            $"<color=#E0F7FA>\"{actionDescription.Trim()}\"</color> " +
            $"<color=#4DD0E1>(累計 {currentProcessLogs.Count} 工程)</color>");
    }

    /// <summary>
    /// 蓄積された全工程履歴を最終ジャッジへ引き渡します。
    /// CraftingPhase 中は DemoTimeLineManager が品質評価と JSON 射出を担当します。
    /// </summary>
    public void CompleteCraftingAndOutputPacket()
    {
        DemoTimeLineManager timeline = DemoTimeLineManager.Instance;
        if (timeline != null && timeline.CurrentState == DemoState.CraftingPhase)
        {
            timeline.FinishCraftingAndEnterResult();
            return;
        }

        ExecuteStandaloneCraftingFinalization();
    }

    /// <summary>DemoTimeLineManager 不在時のフォールバック：単体でジャッジと JSON 射出を行います。</summary>
    private void ExecuteStandaloneCraftingFinalization()
    {
        CacheReferences();

        if (currentProcessLogs.Count == 0)
        {
            Debug.LogWarning(
                "[DetailedCraftingProcessManager] 工程履歴が空です。1〜5 で操作してから Enter を押してください。");
            return;
        }

        string craftType = currentCraftType;
        if (string.Equals(craftType, CraftTypeNone, StringComparison.OrdinalIgnoreCase))
        {
            craftType = CraftTypeForge;
        }

        CraftingStatusManager statusManager = CraftingStatusManager.EnsureInstance();
        int actionCount = currentProcessLogs.Count;
        DemoCraftingJudgmentResult judgment = statusManager != null
            ? statusManager.EvaluateDemoCraftingJudgment(actionCount)
            : new DemoCraftingJudgmentResult
            {
                FinalScore = 10,
                RawScore = 10,
                IsThermalBurstFailure = true,
                CraftType = craftType,
                BurstReason = "CraftingStatusManager 未解決"
            };

        if (judgment.IsThermalBurstFailure)
        {
            Debug.Log(
                "<b><color=#FF3333>【熱科学バースト】融点突破または熱分解により、" +
                "職人作業は完全失敗に終わった……（10点）</color></b>");
        }
        else
        {
            Debug.Log(
                "<b><color=#00FF00>【通常成功】初期ツールの限界までこだわりを尽くした製品が完成！" +
                $"（スコア: {judgment.FinalScore}点）</color></b>");
        }

        CraftingResultItemSnapshot resultSnapshot = statusManager != null
            ? statusManager.FinalizeCraftingResultForPacket()
            : CraftingRecipeManager.BuildSnapshot(CraftRecipeIds.CraftFailure, true);

        List<string> finalParameters = statusManager != null
            ? statusManager.BuildStatusSnapshotLines(craftType)
            : new List<string>();
        List<string> artisanLogs = new List<string>(currentProcessLogs);

        CraftingQualityPacket packet = CraftingQualityPacketEmitter.BuildPacket(
            craftType,
            judgment.FinalScore,
            finalParameters,
            artisanLogs,
            resultSnapshot);
        CraftingQualityPacketEmitter.EmitToConsole(packet);

        if (statusManager != null && !judgment.IsThermalBurstFailure)
        {
            statusManager.TryDeliverCraftedResultToInventory(resultSnapshot, false);
        }

        if (statusManager != null)
        {
            statusManager.ResetStatus(craftType);
        }

        EndSession();
    }

    /// <summary>CraftingExperimentHub の稼働状態から職種を同期し、必要ならセッションを開始します。</summary>
    public void SyncSessionFromCraftingHub()
    {
        DemoTimeLineManager timeline = DemoTimeLineManager.Instance;
        if (timeline != null && timeline.CurrentState != DemoState.CraftingPhase)
        {
            if (IsSessionActive)
            {
                EndSession();
            }

            return;
        }

        CacheReferences();
        if (craftingHub == null || !craftingHub.IsActive)
        {
            if (IsSessionActive)
            {
                EndSession();
            }

            return;
        }

        string hubType = craftingHub.CurrentProfession == CraftProfessionType.Alch
            ? CraftTypeAlch
            : CraftTypeForge;

        if (!IsSessionActive || !string.Equals(currentCraftType, hubType, StringComparison.OrdinalIgnoreCase))
        {
            BeginSession(hubType);
        }
    }

    private void CacheReferences()
    {
        craftingHub ??= CraftingExperimentHub.EnsureInstance();
    }

    private static string NormalizeCraftType(string craftType)
    {
        if (string.IsNullOrWhiteSpace(craftType))
        {
            return CraftTypeNone;
        }

        if (string.Equals(craftType, CraftTypeAlch, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(craftType, CraftProfessionType.Alch.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            return CraftTypeAlch;
        }

        if (string.Equals(craftType, CraftTypeForge, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(craftType, CraftProfessionType.Forge.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            return CraftTypeForge;
        }

        return craftType.Trim();
    }
}

/// <summary>Play 開始時に DetailedCraftingProcessManager を DebugSystemsHub へ自動配置します。</summary>
public static class DetailedCraftingProcessBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoAttach()
    {
        AttachToDebugSystemsHub();
    }

    /// <summary>DebugSystemsHub に DetailedCraftingProcessManager が無ければ追加します。</summary>
    public static void AttachToDebugSystemsHub()
    {
        DetailedCraftingProcessManager.EnsureInstance();
    }
}

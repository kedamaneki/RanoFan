using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 技ごとの実戦熟練度と「極意（閃きの種）」フラグを管理します。
/// 敵へのヒット時のみ熟練度が加算されます。
/// </summary>
public class ActionMasteryManager : MonoBehaviour
{
    public static ActionMasteryManager Instance { get; private set; }

    [Header("極意到達に必要な熟練度（技ごと）")]
    [SerializeField] private List<ActionMasteryConfig> masteryConfigs = new List<ActionMasteryConfig>();

    [Header("実行時の熟練度状態（Play 中に Inspector で確認可能）")]
    [SerializeField] private List<ActionMasteryRuntimeEntry> runtimeEntries = new List<ActionMasteryRuntimeEntry>();

    [Header("初期化")]
    [SerializeField] private bool initializeDefaultConfigsOnStart = true;

    [Tooltip("設定に存在しない actionID がヒットした場合のデフォルト必要熟練度")]
    [SerializeField] private int defaultRequiredMastery = 10;

    private readonly Dictionary<string, ActionMasteryRuntimeEntry> runtimeByActionId =
        new Dictionary<string, ActionMasteryRuntimeEntry>();

    private readonly Dictionary<string, ActionMasteryConfig> configByActionId =
        new Dictionary<string, ActionMasteryConfig>();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[ActionMasteryManager] 重複インスタンスを検出しました。");
        }
        else
        {
            Instance = this;
        }
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void Start()
    {
        if (initializeDefaultConfigsOnStart && masteryConfigs.Count == 0)
        {
            InitializeDefaultConfigs();
        }

        RebuildLookupTables();
    }

    /// <summary>既知の基本技向けデフォルト設定を構築します。</summary>
    public void InitializeDefaultConfigs()
    {
        masteryConfigs.Clear();
        masteryConfigs.Add(new ActionMasteryConfig
        {
            actionID = ActionIds.BasicSlash,
            displayName = "通常斬り",
            requiredMastery = 10
        });
        masteryConfigs.Add(new ActionMasteryConfig
        {
            actionID = ActionIds.StrongStrike,
            displayName = "強撃",
            requiredMastery = 15
        });
        masteryConfigs.Add(new ActionMasteryConfig
        {
            actionID = ActionIds.FireSpark,
            displayName = "火の粉",
            requiredMastery = 12
        });
        masteryConfigs.Add(new ActionMasteryConfig
        {
            actionID = ActionIds.BasicStep,
            displayName = "ステップ",
            requiredMastery = 10
        });
        RebuildLookupTables();
    }

    /// <summary>
    /// 敵への攻撃ヒット時に熟練度を +1 します。既に極意到達済みなら加算しません。
    /// </summary>
    /// <returns>今回の加算で極意に newly 到達した場合 true</returns>
    public bool AddMasteryOnEnemyHit(string actionId)
    {
        if (string.IsNullOrWhiteSpace(actionId))
        {
            return false;
        }

        ActionMasteryRuntimeEntry entry = GetOrCreateRuntimeEntry(actionId);
        if (entry.isMastered)
        {
            return false;
        }

        int requiredMastery = GetRequiredMastery(actionId);
        entry.currentMastery++;

        if (entry.currentMastery >= requiredMastery)
        {
            entry.currentMastery = requiredMastery;
            entry.isMastered = true;

            string displayName = GetDisplayName(actionId);
            InspirationCombatLog.LogMasteryAchieved(displayName, actionId);
            return true;
        }

        return false;
    }

    /// <summary>指定技の極意（閃きの種）到達済みか。InspirationManager の将来判定用。</summary>
    public bool IsActionMastered(string actionId)
    {
        if (string.IsNullOrWhiteSpace(actionId))
        {
            return false;
        }

        return runtimeByActionId.TryGetValue(actionId, out ActionMasteryRuntimeEntry entry) && entry.isMastered;
    }

    /// <summary>指定技の熟練度状態を取得します。</summary>
    public bool TryGetMasteryState(string actionId, out int currentMastery, out bool isMastered)
    {
        currentMastery = 0;
        isMastered = false;

        if (string.IsNullOrWhiteSpace(actionId))
        {
            return false;
        }

        if (!runtimeByActionId.TryGetValue(actionId, out ActionMasteryRuntimeEntry entry))
        {
            return true;
        }

        currentMastery = entry.currentMastery;
        isMastered = entry.isMastered;
        return true;
    }

    /// <summary>指定技の極意到達に必要な熟練度を返します。</summary>
    public int GetRequiredMastery(string actionId)
    {
        if (configByActionId.TryGetValue(actionId, out ActionMasteryConfig config))
        {
            return Mathf.Max(1, config.requiredMastery);
        }

        return Mathf.Max(1, defaultRequiredMastery);
    }

    /// <summary>自動テスト用：指定技の熟練度を直接設定します。</summary>
    public void SetMasteryForTesting(string actionId, int currentMastery, bool isMastered)
    {
        ActionMasteryRuntimeEntry entry = GetOrCreateRuntimeEntry(actionId);
        entry.currentMastery = Mathf.Max(0, currentMastery);
        entry.isMastered = isMastered;

        if (entry.isMastered)
        {
            entry.currentMastery = GetRequiredMastery(actionId);
        }
    }

    /// <summary>自動テスト用：実行時状態をクリアします。</summary>
    public void ResetAllMasteryForTesting()
    {
        runtimeEntries.Clear();
        runtimeByActionId.Clear();
    }

    private ActionMasteryRuntimeEntry GetOrCreateRuntimeEntry(string actionId)
    {
        if (runtimeByActionId.TryGetValue(actionId, out ActionMasteryRuntimeEntry existing))
        {
            return existing;
        }

        ActionMasteryRuntimeEntry entry = new ActionMasteryRuntimeEntry
        {
            actionID = actionId,
            currentMastery = 0,
            isMastered = false
        };

        runtimeEntries.Add(entry);
        runtimeByActionId[actionId] = entry;
        return entry;
    }

    /// <summary>ログ表示用の技名を返します（未登録時は actionId）。</summary>
    public string GetActionDisplayName(string actionId)
    {
        return GetDisplayName(actionId);
    }

    /// <summary>テスト用：極意到達状態を付与し、極意ログを出力します。</summary>
    /// <param name="announceUi">false のときコンソールログのみ（ポップアップなし）</param>
    public void GrantMasteryForTesting(string actionId, bool announceUi = true)
    {
        if (string.IsNullOrWhiteSpace(actionId))
        {
            return;
        }

        int required = GetRequiredMastery(actionId);
        SetMasteryForTesting(actionId, required, true);

        if (!announceUi || DemoInputGate.ShouldSuppressInspirationPopup())
        {
            string displayName = GetDisplayName(actionId);
            Debug.Log(
                $"<color=#B388FF><b>【極意·テスト付与】{displayName}（{actionId}）</b></color>");
            return;
        }

        InspirationCombatLog.LogMasteryAchieved(GetDisplayName(actionId), actionId);
    }

    private string GetDisplayName(string actionId)
    {
        if (configByActionId.TryGetValue(actionId, out ActionMasteryConfig config) &&
            !string.IsNullOrWhiteSpace(config.displayName))
        {
            return config.displayName;
        }

        return actionId;
    }

    private void RebuildLookupTables()
    {
        configByActionId.Clear();
        foreach (ActionMasteryConfig config in masteryConfigs)
        {
            if (config == null || string.IsNullOrWhiteSpace(config.actionID))
            {
                continue;
            }

            configByActionId[config.actionID] = config;
        }

        runtimeByActionId.Clear();
        foreach (ActionMasteryRuntimeEntry entry in runtimeEntries)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.actionID))
            {
                continue;
            }

            runtimeByActionId[entry.actionID] = entry;
        }
    }
}

/// <summary>Inspector で設定する技ごとの極意到達条件。</summary>
[Serializable]
public class ActionMasteryConfig
{
    [Tooltip("技の一意識別子（snake_case）")]
    public string actionID;

    [Tooltip("ログ表示用の技名")]
    public string displayName;

    [Tooltip("極意到達に必要な実戦ヒット数")]
    public int requiredMastery = 10;
}

/// <summary>実行時の熟練度状態。Inspector の runtimeEntries で確認できます。</summary>
[Serializable]
public class ActionMasteryRuntimeEntry
{
    public string actionID;
    public int currentMastery;
    public bool isMastered;
}

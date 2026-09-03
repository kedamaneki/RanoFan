using System;
using System.Collections.Generic;
using UnityEngine;

// =============================================================================
// プレイヤー戦歴ログ（行動種別 × 累計回数）— 進化 criteria 動的判定用
// 既存 PlayerActionLogger / GamePhaseEventBridge と連携
// =============================================================================

/// <summary>
/// プレイヤーの立ち回り・作業履歴を辞書で保持し、進化条件の閾値判定に供します。
/// </summary>
public class PlayerHistoryTracker : MonoBehaviour
{
    public static PlayerHistoryTracker Instance { get; private set; }

    [Header("起動時に既存ロガーから同期")]
    [SerializeField] private bool syncLegacyLoggersOnStart = true;

    private readonly Dictionary<string, int> actionCounts =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, int> ActionCounts => actionCounts;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    private void Start()
    {
        if (syncLegacyLoggersOnStart)
        {
            SyncFromLegacyLoggers();
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
    public static PlayerHistoryTracker EnsureInstance()
    {
        if (Instance != null)
        {
            return Instance;
        }

        GameObject hub = GameObject.Find("DebugSystemsHub");
        if (hub != null)
        {
            PlayerHistoryTracker onHub = hub.GetComponent<PlayerHistoryTracker>();
            if (onHub != null)
            {
                return onHub;
            }

            return hub.AddComponent<PlayerHistoryTracker>();
        }

        return new GameObject(nameof(PlayerHistoryTracker)).AddComponent<PlayerHistoryTracker>();
    }

    /// <summary>ログ種別の累計を返します（未記録は 0）。</summary>
    public int GetCount(string logType)
    {
        if (string.IsNullOrWhiteSpace(logType))
        {
            return 0;
        }

        if (actionCounts.TryGetValue(logType.Trim(), out int count))
        {
            return count;
        }

        return 0;
    }

    /// <summary>ログ種別の累計に加算します。</summary>
    public void Increment(string logType, int amount = 1)
    {
        if (amount <= 0 || string.IsNullOrWhiteSpace(logType))
        {
            return;
        }

        string key = logType.Trim();
        if (actionCounts.TryGetValue(key, out int current))
        {
            actionCounts[key] = current + amount;
        }
        else
        {
            actionCounts[key] = amount;
        }
    }

    /// <summary>閾値以上かどうかを判定します。</summary>
    public bool MeetsThreshold(string logType, int requiredValue)
    {
        if (requiredValue <= 0)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(logType) ||
            string.Equals(logType, PlayerActionLogTypes.GeneralPrerequisite, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return GetCount(logType) >= requiredValue;
    }

    /// <summary>PlayerActionLogger / GamePhaseEventBridge の既存カウンタを取り込みます。</summary>
    public void SyncFromLegacyLoggers()
    {
        PlayerActionLogger logger = PlayerActionLogger.Instance;
        if (logger != null)
        {
            SetMaxCount(PlayerActionLogTypes.AttackCount, logger.TotalAttacks);
            SetMaxCount(PlayerActionLogTypes.KillCount, logger.EnemyKills);
            SetMaxCount(PlayerActionLogTypes.DodgeCount, logger.TotalDodges);
            SetMaxCount(PlayerActionLogTypes.JustEvasionCount, logger.NearMissCount);
            SetMaxCount(PlayerActionLogTypes.JustParryCount, logger.NearMissCount);
        }

        GamePhaseEventBridge bridge = GamePhaseEventBridge.Instance
            ?? FindAnyObjectByType<GamePhaseEventBridge>();
        if (bridge?.runtimeHistoryLog != null)
        {
            PlayerHistoryLog history = bridge.runtimeHistoryLog;
            SetMaxCount(PlayerActionLogTypes.JustEvasionCount, history.TotalPerfectEvades);
            SetMaxCount(PlayerActionLogTypes.JustParryCount, history.TotalPerfectEvades);
        }
    }

    /// <summary>デバッグ／テスト用に直接設定します。</summary>
    public void SetCount(string logType, int value)
    {
        if (string.IsNullOrWhiteSpace(logType))
        {
            return;
        }

        actionCounts[logType.Trim()] = Mathf.Max(0, value);
    }

    private void SetMaxCount(string logType, int value)
    {
        if (string.IsNullOrWhiteSpace(logType))
        {
            return;
        }

        string key = logType.Trim();
        if (actionCounts.TryGetValue(key, out int current))
        {
            actionCounts[key] = Mathf.Max(current, value);
        }
        else
        {
            actionCounts[key] = Mathf.Max(0, value);
        }
    }

    /// <summary>全ログをクリアします。</summary>
    public void ClearAll()
    {
        actionCounts.Clear();
    }
}

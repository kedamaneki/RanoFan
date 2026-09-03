using System;
using System.Collections.Generic;
using UnityEngine;

// =============================================================================
// EnemyBehaviorProfiles/*.json ロード・1 段目抽選・コンボ ID 解決
// =============================================================================

/// <summary>敵行動パターン JSON のロードとコンボ抽選ヘルパー。</summary>
public static class EnemyBehaviorProfileManager
{
    private const string ResourcesPrefix = "EnemyBehaviorProfiles/";

    private static readonly Dictionary<string, EnemyBehaviorProfile> Cache =
        new Dictionary<string, EnemyBehaviorProfile>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Resources/EnemyBehaviorProfiles/&lt;behaviorPattern&gt;.json をロードします。</summary>
    public static EnemyBehaviorProfile LoadFromResources(string behaviorPattern)
    {
        if (string.IsNullOrWhiteSpace(behaviorPattern))
        {
            return null;
        }

        string key = behaviorPattern.Trim();
        if (Cache.TryGetValue(key, out EnemyBehaviorProfile cached) && cached != null)
        {
            return cached;
        }

        string path = $"{ResourcesPrefix}{key}";
        TextAsset profileAsset = Resources.Load<TextAsset>(path);
        if (profileAsset == null)
        {
            return null;
        }

        try
        {
            EnemyBehaviorProfile profile = EnemyBehaviorProfile.FromJson(profileAsset.text);
            if (profile == null || !profile.IsValid())
            {
                Debug.LogError($"[EnemyBehaviorProfileManager] 無効な behavior profile JSON: {path}");
                return null;
            }

            if (!string.Equals(profile.patternId, key, StringComparison.OrdinalIgnoreCase))
            {
                Debug.LogWarning(
                    $"[EnemyBehaviorProfileManager] patternId 不一致: JSON={profile.patternId} / 要求={key}");
            }

            Cache[key] = profile;
            return profile;
        }
        catch (Exception exception)
        {
            Debug.LogError($"[EnemyBehaviorProfileManager] パース失敗: {path}\n{exception}");
            return null;
        }
    }

    /// <summary>weight に基づき 1 段目の技をランダム抽選します。</summary>
    public static EnemyAttackActionData ChooseInitialAttack(EnemyBehaviorProfile profile)
    {
        if (profile?.attacks == null || profile.attacks.Count == 0)
        {
            return null;
        }

        int totalWeight = 0;
        for (int i = 0; i < profile.attacks.Count; i++)
        {
            EnemyAttackActionData action = profile.attacks[i];
            if (action != null && action.IsValid() && action.weight > 0)
            {
                totalWeight += action.weight;
            }
        }

        if (totalWeight <= 0)
        {
            Debug.LogWarning(
                $"[EnemyBehaviorProfileManager] 抽選可能な weight>0 技がありません: {profile.patternId}");
            return null;
        }

        int roll = UnityEngine.Random.Range(0, totalWeight);
        int cumulative = 0;
        for (int i = 0; i < profile.attacks.Count; i++)
        {
            EnemyAttackActionData action = profile.attacks[i];
            if (action == null || !action.IsValid() || action.weight <= 0)
            {
                continue;
            }

            cumulative += action.weight;
            if (roll < cumulative)
            {
                return action;
            }
        }

        return null;
    }

    /// <summary>actionId で attacks リスト内の技を検索します。</summary>
    public static EnemyAttackActionData FindActionById(EnemyBehaviorProfile profile, string actionId)
    {
        if (profile?.attacks == null || string.IsNullOrWhiteSpace(actionId))
        {
            return null;
        }

        string trimmedId = actionId.Trim();
        for (int i = 0; i < profile.attacks.Count; i++)
        {
            EnemyAttackActionData action = profile.attacks[i];
            if (action != null &&
                action.IsValid() &&
                string.Equals(action.actionId, trimmedId, StringComparison.OrdinalIgnoreCase))
            {
                return action;
            }
        }

        return null;
    }

    /// <summary>マスター値と行動 JSON から既存 EnemyAttackProfile を合成します。</summary>
    public static EnemyAttackProfile BuildAttackProfileFromBehavior(
        EnemyBehaviorProfile behaviorProfile,
        EnemyMasterData master)
    {
        EnemyAttackProfile profile = EnemyAttackProfile.CreateForestSlimeMob();
        if (master != null)
        {
            profile.enemyName = master.name;
            profile.enemyMasterId = master.id;
            profile.maxHp = Mathf.Max(1, master.hp);
            profile.attackStrength = Mathf.Max(1, master.str);
            profile.defense = Mathf.Max(0, master.def);
        }

        if (behaviorProfile?.attacks == null || behaviorProfile.attacks.Count == 0)
        {
            return profile;
        }

        float minDelay = float.MaxValue;
        float maxDelay = 0f;
        float maxParryWindow = profile.activeWindow;

        for (int i = 0; i < behaviorProfile.attacks.Count; i++)
        {
            EnemyAttackActionData action = behaviorProfile.attacks[i];
            if (action == null || !action.IsValid())
            {
                continue;
            }

            if (action.weight > 0)
            {
                minDelay = Mathf.Min(minDelay, Mathf.Max(0f, action.attackDelaySeconds));
                float feintUpper = action.feintChance > 0f ? 0.38f : 0f;
                maxDelay = Mathf.Max(maxDelay, action.attackDelaySeconds + feintUpper);
            }

            maxParryWindow = Mathf.Max(maxParryWindow, action.parryWindowSeconds);
        }

        if (minDelay < float.MaxValue)
        {
            profile.minDelay = minDelay;
            profile.maxDelay = Mathf.Max(minDelay, maxDelay);
        }

        profile.activeWindow = Mathf.Max(0.01f, maxParryWindow);
        profile.parryJustWindow = Mathf.Min(profile.activeWindow, profile.parryJustWindow);
        return profile;
    }

    /// <summary>
    /// 生態・本能 AI の位相ティックへ橋渡しします。
    /// 実装本体は <see cref="EnemyEcologyBehaviorRuntime"/>（Scripts/Enemy/）。
    /// </summary>
    public static void TickEcologyPhase(int phase, VariableTimelineSeason season)
    {
        try
        {
            EnemyEcologyBehaviorRuntime.EnsureInstance().TickPhase(phase, season);
        }
        catch (System.Exception exception)
        {
            Debug.LogWarning($"[EnemyBehaviorProfileManager] 生態ティック Safe-Fail: {exception.Message}");
        }
    }

    /// <summary>24 位相の生態シミュレーションを実行します。</summary>
    public static void SimulateEcologyFullDay(VariablePhaseDistribution distribution = null)
    {
        try
        {
            EnemyEcologyBehaviorRuntime.EnsureInstance().SimulateFullDay(distribution);
        }
        catch (System.Exception exception)
        {
            Debug.LogWarning($"[EnemyBehaviorProfileManager] 生態1日 Safe-Fail: {exception.Message}");
        }
    }
}

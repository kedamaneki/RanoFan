using System;
using UnityEngine;

// =============================================================================
// EnemyMasterData -> EnemyAttackProfile 橋渡しレイヤー
// 静的マスター（HP/STR/DEF/behaviorPattern）を既存戦闘システムで即利用可能に変換
// =============================================================================

/// <summary>
/// 戦闘開始時に使用する敵ランタイム実体。
/// 静的な EnemyMasterData とタイミング制御用 EnemyAttackProfile を合成して保持します。
/// </summary>
[Serializable]
public class ActiveEnemyCombatStats
{
    [Tooltip("EnemyMasterData の ID")]
    public string enemyId;

    [Tooltip("表示名（EnemyMasterData 側）")]
    public string enemyName;

    [Tooltip("戦闘開始時 HP")]
    public int currentHp;

    [Tooltip("STR（EnemyMasterData 側）")]
    public int strength;

    [Tooltip("DEF（EnemyMasterData 側）")]
    public int defense;

    [Tooltip("行動ルーチンキー（EnemyMasterData.behaviorPattern）")]
    public string behaviorPattern;

    [Tooltip("JSON 定義の複数技・コンボ行動プロファイル")]
    public EnemyBehaviorProfile behaviorProfile;

    [Tooltip("既存戦闘システムへ渡す攻撃タイミングプロファイル")]
    public EnemyAttackProfile attackProfile;
}

/// <summary>
/// EnemyMasterData を CombatActionFeedbackManager 用データへ変換する静的ブリッジ。
/// behaviorPattern をキーに行動 JSON / 攻撃プロファイルを解決し、不足時は安全なフォールバックへ退避します。
/// </summary>
public static class EnemyMasterBridge
{
    private const string BehaviorProfileResourcesPrefix = "EnemyBehaviorProfiles/";

    /// <summary>
    /// enemyId から戦闘用ランタイムデータを構築します。
    /// 変換失敗時は null を返し、原因は Debug.LogError に記録します。
    /// </summary>
    public static ActiveEnemyCombatStats BuildBattleEnemy(string enemyId)
    {
        if (string.IsNullOrWhiteSpace(enemyId))
        {
            Debug.LogError("[EnemyMasterBridge] enemyId が空です。");
            return null;
        }

        try
        {
            MasterDataManager manager = MasterDataManager.EnsureInstance();
            if (manager == null)
            {
                Debug.LogError("[EnemyMasterBridge] MasterDataManager を解決できません。");
                return null;
            }

            EnemyMasterData master = manager.GetEnemy(enemyId);
            if (master == null)
            {
                Debug.LogError($"[EnemyMasterBridge] EnemyMasterData 未検出: {enemyId}");
                return null;
            }

            EnemyBehaviorProfile behaviorProfile =
                EnemyBehaviorProfileManager.LoadFromResources(master.behaviorPattern);

            EnemyAttackProfile profile = ResolveAttackProfile(master.behaviorPattern, master, behaviorProfile);
            if (profile == null || !profile.IsValid())
            {
                Debug.LogError(
                    $"[EnemyMasterBridge] behaviorPattern 解決失敗: {master.behaviorPattern} (enemyId={enemyId})");
                return null;
            }

            profile.enemyName = master.name;
            profile.enemyMasterId = master.id;
            profile.maxHp = Mathf.Max(1, master.hp);
            profile.attackStrength = Mathf.Max(1, master.str);
            profile.defense = Mathf.Max(0, master.def);

            ActiveEnemyCombatStats active = new ActiveEnemyCombatStats
            {
                enemyId = master.id,
                enemyName = master.name,
                currentHp = master.hp,
                strength = master.str,
                defense = master.def,
                behaviorPattern = master.behaviorPattern,
                behaviorProfile = behaviorProfile,
                attackProfile = profile
            };
            return active;
        }
        catch (Exception exception)
        {
            Debug.LogError($"[EnemyMasterBridge] BuildBattleEnemy 例外: {enemyId}\n{exception}");
            return null;
        }
    }

    private static EnemyAttackProfile ResolveAttackProfile(
        string behaviorPattern,
        EnemyMasterData master,
        EnemyBehaviorProfile behaviorProfile)
    {
        if (behaviorProfile != null && behaviorProfile.IsValid())
        {
            return EnemyBehaviorProfileManager.BuildAttackProfileFromBehavior(behaviorProfile, master);
        }

        EnemyAttackProfile legacyJsonProfile = TryLoadLegacyAttackProfileFromResources(behaviorPattern);
        if (legacyJsonProfile != null)
        {
            return legacyJsonProfile;
        }

        if (string.Equals(behaviorPattern, "AI_BOSS_GRIFFIN", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(behaviorPattern, "BOSS_GRIFFIN", StringComparison.OrdinalIgnoreCase))
        {
            return EnemyAttackProfile.CreateForestGriffinBoss();
        }

        if (string.Equals(behaviorPattern, "AI_MOB_AGGRESSIVE", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(behaviorPattern, "MOB_SLIME", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(behaviorPattern, "PATTERN_BASIC_SLIME", StringComparison.OrdinalIgnoreCase))
        {
            return EnemyAttackProfile.CreateForestSlimeMob();
        }

        Debug.LogWarning(
            $"[EnemyMasterBridge] 未知の behaviorPattern: {behaviorPattern}. " +
            $"enemyId={master?.id} は CreateForestSlimeMob へフォールバックします。");
        return EnemyAttackProfile.CreateForestSlimeMob();
    }

    /// <summary>旧形式（EnemyAttackProfile 単体）JSON の後方互換ロード。</summary>
    private static EnemyAttackProfile TryLoadLegacyAttackProfileFromResources(string behaviorPattern)
    {
        if (string.IsNullOrWhiteSpace(behaviorPattern))
        {
            return null;
        }

        string path = $"{BehaviorProfileResourcesPrefix}{behaviorPattern}";
        TextAsset profileAsset = Resources.Load<TextAsset>(path);
        if (profileAsset == null)
        {
            return null;
        }

        if (profileAsset.text.IndexOf("\"attacks\"", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return null;
        }

        try
        {
            EnemyAttackProfile parsed = JsonUtility.FromJson<EnemyAttackProfile>(profileAsset.text);
            if (parsed == null || !parsed.IsValid())
            {
                Debug.LogError($"[EnemyMasterBridge] legacy behavior profile JSON が無効です: {path}");
                return null;
            }

            return parsed;
        }
        catch (Exception exception)
        {
            Debug.LogError($"[EnemyMasterBridge] legacy behavior profile パース失敗: {path}\n{exception}");
            return null;
        }
    }
}

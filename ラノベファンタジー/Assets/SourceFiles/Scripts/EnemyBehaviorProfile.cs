using System;
using System.Collections.Generic;
using UnityEngine;

// =============================================================================
// 敵行動パターン JSON — 複数技・非線形コンボインターバル定義
// 連携: EnemyBehaviorProfileManager / EnemyMasterBridge / CombatActionFeedbackManager
// =============================================================================

/// <summary>1 ヒット分の攻撃アクション定義（コンボチェーンの各ノード）。</summary>
[Serializable]
public class EnemyAttackActionData
{
    /// <summary>技の識別 ID（例: ATTACK_CLAW_LEFT）。</summary>
    public string actionId = string.Empty;

    /// <summary>技の表示名（例: 幻惑の左爪）。</summary>
    public string actionName = string.Empty;

    /// <summary>技開始から判定発生までの溜め秒数。2 段目以降は 0 で即判定も可。</summary>
    public float attackDelaySeconds;

    /// <summary>パリィ受付ウィンドウ秒数。</summary>
    public float parryWindowSeconds = 0.12f;

    /// <summary>ヒット時に削るプレイヤースタミナ量。</summary>
    public int staminaDamageValue = 10;

    /// <summary>ガード不能／強制体勢崩し（通常ガードパリィを無効化）。</summary>
    public bool isGuardBreakable;

    /// <summary>溜め中に追加ディレイを上乗せする確率（0〜1）。</summary>
    public float feintChance;

    /// <summary>1 段目抽選の重み。0 ならコンボ派生専用技。</summary>
    public int weight = 1;

    /// <summary>次に連携する技 ID。無ければ空文字。</summary>
    public string nextComboActionId = string.Empty;

    /// <summary>このヒット発生後、次コンボ判定までの待機秒数。</summary>
    public float comboIntervalSeconds;

    /// <summary>戦闘ロジックで使用可能か。</summary>
    public bool IsValid()
    {
        return !string.IsNullOrWhiteSpace(actionId) &&
               parryWindowSeconds > 0f;
    }
}

/// <summary>JsonUtility 用 DTO（attacks は配列のみ対応）。</summary>
[Serializable]
internal class EnemyBehaviorProfileJsonDto
{
    public string patternId;
    public string comment;
    public EnemyAttackActionData[] attacks;
}

/// <summary>敵の行動パターンルート（複数技リストを内包）。</summary>
[Serializable]
public class EnemyBehaviorProfile
{
    /// <summary>マスタ ID（例: PATTERN_FEINT_DANCER）。</summary>
    public string patternId = string.Empty;

    /// <summary>プランナー用メモ。</summary>
    public string comment = string.Empty;

    /// <summary>内包する攻撃アクション一覧（ランタイム参照用）。</summary>
    public List<EnemyAttackActionData> attacks = new List<EnemyAttackActionData>();

    /// <summary>JSON 文字列からプロファイルを構築します。</summary>
    public static EnemyBehaviorProfile FromJson(string json)
    {
        EnemyBehaviorProfile profile = new EnemyBehaviorProfile();
        if (string.IsNullOrWhiteSpace(json))
        {
            return profile;
        }

        EnemyBehaviorProfileJsonDto dto = JsonUtility.FromJson<EnemyBehaviorProfileJsonDto>(json);
        if (dto == null)
        {
            return profile;
        }

        profile.patternId = dto.patternId ?? string.Empty;
        profile.comment = dto.comment ?? string.Empty;
        profile.attacks = new List<EnemyAttackActionData>();

        if (dto.attacks != null)
        {
            for (int i = 0; i < dto.attacks.Length; i++)
            {
                EnemyAttackActionData action = dto.attacks[i];
                if (action != null && action.IsValid())
                {
                    profile.attacks.Add(action);
                }
            }
        }

        return profile;
    }

    /// <summary>コンボ実行に使える攻撃が 1 件以上あるか。</summary>
    public bool HasComboAttacks()
    {
        return attacks != null && attacks.Count > 0;
    }

    /// <summary>抽選可能な 1 段目技が存在するか。</summary>
    public bool HasInitialAttackCandidates()
    {
        if (attacks == null)
        {
            return false;
        }

        for (int i = 0; i < attacks.Count; i++)
        {
            EnemyAttackActionData action = attacks[i];
            if (action != null && action.IsValid() && action.weight > 0)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>プロファイル全体が使用可能か。</summary>
    public bool IsValid()
    {
        return !string.IsNullOrWhiteSpace(patternId) && HasComboAttacks();
    }
}

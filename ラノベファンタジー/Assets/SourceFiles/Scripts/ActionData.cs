using System;
using UnityEngine;

/// <summary>
/// プレイヤーが発動する「技」の設計図データ。
/// AI が JSON で動的生成し、InspirationManager 経由でスキルに装着されます。
/// </summary>
[Serializable]
public class ActionData
{
    [Tooltip("技の一意識別子")]
    public string actionID;

    [Tooltip("技の表示名")]
    public string actionName;

    [Tooltip("基本 STR に対するダメージ倍率（回避技は 0）")]
    public float damageMultiplier = 1f;

    [Tooltip("スタミナ消費量")]
    public float staminaCost = 20f;

    [Tooltip("攻撃：判定 ON 時間／回避：移動持続時間（秒）")]
    public float activeDetectionTime = 0.3f;

    [Tooltip("無敵時間（秒）")]
    public float invincibilityTime;

    [Tooltip("false=基本技（スキル内蔵）, true=閃き・派生技")]
    public bool isDerived;

    [Tooltip("閃きの理由・AI フレーバーテキスト")]
    public string inspirationSource;

    public bool IsValid()
    {
        return !string.IsNullOrWhiteSpace(actionID);
    }

    public ActionData Clone()
    {
        return FromJson(ToJson());
    }

    public static ActionData FromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            ActionData data = JsonUtility.FromJson<ActionData>(json);
            return data != null && data.IsValid() ? data : null;
        }
        catch (Exception exception)
        {
            Debug.LogError($"[ActionData] JSON パース失敗: {exception.Message}");
            return null;
        }
    }

    public string ToJson(bool prettyPrint = false)
    {
        return JsonUtility.ToJson(this, prettyPrint);
    }

    /// <summary>既存の DerivedActionData 連携用に変換（後方互換）</summary>
    public DerivedActionData ToDerivedActionData(SkillCategory category)
    {
        return new DerivedActionData
        {
            actionID = actionID,
            actionName = actionName,
            actionType = category == SkillCategory.Evade ? DerivedActionType.Evade : DerivedActionType.Attack,
            damageMultiplier = damageMultiplier,
            staminaCost = staminaCost,
            activeDetectionTime = activeDetectionTime,
            invincibilityTime = invincibilityTime,
            unlockTriggerCondition = inspirationSource
        };
    }

    public static ActionData FromDerivedActionData(DerivedActionData derived)
    {
        if (derived == null || !derived.IsValid())
        {
            return null;
        }

        return new ActionData
        {
            actionID = derived.actionID,
            actionName = derived.actionName,
            damageMultiplier = derived.damageMultiplier,
            staminaCost = derived.staminaCost,
            activeDetectionTime = derived.activeDetectionTime,
            invincibilityTime = derived.invincibilityTime,
            isDerived = true,
            inspirationSource = derived.unlockTriggerCondition
        };
    }

    public override string ToString()
    {
        string label = isDerived ? "閃き" : "基本";
        return $"[{actionID}] {actionName} ({label})";
    }
}

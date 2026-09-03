using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 技を内包するスキル（カテゴリー／器）。
/// 例：【片手剣術】【回避術】【初級火魔法】
/// </summary>
[Serializable]
public class SkillData
{
    [Tooltip("スキルの一意識別子")]
    public string skillID;

    [Tooltip("スキル表示名")]
    public string skillName;

    [Tooltip("攻撃・回避・魔法の区分")]
    public SkillCategory category;

    [Tooltip("このスキルにセットできる技の最大数")]
    public int maxSlots = 4;

    [Tooltip("現在スキルに装着されている技リスト")]
    public List<ActionData> equippedActions = new List<ActionData>();

    [Tooltip("スキルレベル（オーブ抽出等で変動。初期値 1）")]
    public int skillLevel = 1;

    public int EquippedCount => equippedActions.Count;
    public bool HasRoom => equippedActions.Count < maxSlots;

    /// <summary>スキル内に同一 actionID が既にあるか</summary>
    public bool ContainsAction(string actionId)
    {
        if (string.IsNullOrWhiteSpace(actionId))
        {
            return false;
        }

        foreach (ActionData action in equippedActions)
        {
            if (action != null && action.actionID == actionId)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 技をスキル枠に装着します。枠が満杯なら false を返します。
    /// </summary>
    public bool TryEquip(ActionData action)
    {
        if (action == null || !action.IsValid())
        {
            return false;
        }

        if (ContainsAction(action.actionID))
        {
            Debug.Log($"[SkillData:{skillName}] 既に装着済み: {action.actionName}");
            return false;
        }

        if (!HasRoom)
        {
            return false;
        }

        equippedActions.Add(action.Clone());
        return true;
    }

    /// <summary>
    /// 戦闘で使う「代表技」を取得。
    /// 優先順位: 最後に装着した閃き技 → 最後に装着した技 → 最初の基本技
    /// </summary>
    public bool TryGetActiveAction(out ActionData action)
    {
        for (int i = equippedActions.Count - 1; i >= 0; i--)
        {
            ActionData candidate = equippedActions[i];
            if (candidate != null && candidate.isDerived)
            {
                action = candidate;
                return true;
            }
        }

        for (int i = equippedActions.Count - 1; i >= 0; i--)
        {
            ActionData candidate = equippedActions[i];
            if (candidate != null)
            {
                action = candidate;
                return true;
            }
        }

        action = null;
        return false;
    }
}

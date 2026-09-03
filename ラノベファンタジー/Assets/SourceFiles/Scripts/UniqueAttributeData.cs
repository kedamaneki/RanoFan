using System;
using UnityEngine;

// =============================================================================
// ユニークスキル — 権能（Attribute）1エントリ
// effectType は SkillMaster / SpecialEffectTypes と共通語彙を流用
// =============================================================================

/// <summary>
/// ユニークスキルに同居する単一の権能。
/// 戦闘・生産・便利枠の各フェーズを横断的に強化します。
/// </summary>
[Serializable]
public class UniqueAttributeData
{
    [Tooltip("権能の表示名（例: 思考加速）")]
    public string attributeName;

    [Tooltip("効果種別（例: Utility_MindAccelerate, Psychic_Regen_Mana）")]
    public string effectType;

    [Tooltip("効果量（effectType ごとに解釈）")]
    public float value;

    [Tooltip("持続時間（秒）。0 はパッシブ常時発動")]
    public float duration;

    [Tooltip("権能ごとの説明文")]
    public string description;

    public bool IsValid()
    {
        return !string.IsNullOrWhiteSpace(attributeName) &&
               !string.IsNullOrWhiteSpace(effectType);
    }

    /// <summary>パッシブ常時発動か（duration が 0）。</summary>
    public bool IsPassiveAlwaysOn => duration <= 0f;

    public UniqueAttributeData Clone()
    {
        return new UniqueAttributeData
        {
            attributeName = attributeName,
            effectType = effectType,
            value = value,
            duration = duration,
            description = description
        };
    }

    public override string ToString()
    {
        string mode = IsPassiveAlwaysOn ? "常時" : $"{duration:0.#}秒";
        return $"{attributeName} [{effectType}] value={value} ({mode})";
    }
}

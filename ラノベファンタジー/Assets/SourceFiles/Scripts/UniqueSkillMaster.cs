using System;
using System.Collections.Generic;
using UnityEngine;

// =============================================================================
// 転スラ型ユニークスキル — データモデル（基礎 SkillMaster とは完全独立）
// 1つの固有名の下に 4〜5 個の権能（attributes）をパッケージング
// =============================================================================

/// <summary>ユニークスキル生成時の権能数制約。</summary>
public static class UniqueSkillConstraints
{
    public const int MinimumAttributeCount = 4;
    public const int MaximumAttributeCount = 5;
}

/// <summary>
/// 転スラ型ユニークスキル（最上位概念）。
/// 流派の技ツリーではなく、多面的な権能リストを保持します。
/// </summary>
[Serializable]
public class UniqueSkillMaster
{
    [Tooltip("ユニークスキル識別子（例: USKL_NAVIGATOR）")]
    public string uniqueSkillId;

    [Tooltip("神聖な固有名（例: 導き手）")]
    public string uniqueSkillName;

    [Tooltip("スキル全体の効果説明")]
    public string description;

    [Tooltip("獲得・由来のフレーバーテキスト")]
    public string flavorText;

    [Tooltip("同居する権能リスト（4〜5個）")]
    public List<UniqueAttributeData> attributes = new List<UniqueAttributeData>();

    public int AttributeCount => attributes?.Count ?? 0;

    public bool IsValid()
    {
        if (string.IsNullOrWhiteSpace(uniqueSkillId) ||
            string.IsNullOrWhiteSpace(uniqueSkillName) ||
            attributes == null)
        {
            return false;
        }

        if (attributes.Count < UniqueSkillConstraints.MinimumAttributeCount ||
            attributes.Count > UniqueSkillConstraints.MaximumAttributeCount)
        {
            return false;
        }

        for (int i = 0; i < attributes.Count; i++)
        {
            if (attributes[i] == null || !attributes[i].IsValid())
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>effectType で権能を検索します。</summary>
    public UniqueAttributeData FindAttributeByEffectType(string effectType)
    {
        if (attributes == null || string.IsNullOrWhiteSpace(effectType))
        {
            return null;
        }

        for (int i = 0; i < attributes.Count; i++)
        {
            UniqueAttributeData attr = attributes[i];
            if (attr != null &&
                string.Equals(attr.effectType, effectType, StringComparison.OrdinalIgnoreCase))
            {
                return attr;
            }
        }

        return null;
    }

    /// <summary>attributeName で権能を検索します。</summary>
    public UniqueAttributeData FindAttributeByName(string attributeName)
    {
        if (attributes == null || string.IsNullOrWhiteSpace(attributeName))
        {
            return null;
        }

        for (int i = 0; i < attributes.Count; i++)
        {
            UniqueAttributeData attr = attributes[i];
            if (attr != null &&
                string.Equals(attr.attributeName, attributeName, StringComparison.OrdinalIgnoreCase))
            {
                return attr;
            }
        }

        return null;
    }

    public UniqueSkillMaster Clone()
    {
        return JsonUtility.FromJson<UniqueSkillMaster>(JsonUtility.ToJson(this));
    }

    public static UniqueSkillMaster FromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            UniqueSkillMaster direct = JsonUtility.FromJson<UniqueSkillMaster>(json);
            if (direct != null && HasRequiredIdentity(direct))
            {
                return direct;
            }

            UniqueSkillEnvelopeDto envelope = JsonUtility.FromJson<UniqueSkillEnvelopeDto>(json);
            if (envelope?.uniqueSkill != null && HasRequiredIdentity(envelope.uniqueSkill))
            {
                return envelope.uniqueSkill;
            }

            return null;
        }
        catch (Exception exception)
        {
            Debug.LogError($"[UniqueSkillMaster] JSON パース失敗: {exception.Message}");
            return null;
        }
    }

    private static bool HasRequiredIdentity(UniqueSkillMaster master)
    {
        return master != null &&
               !string.IsNullOrWhiteSpace(master.uniqueSkillId) &&
               !string.IsNullOrWhiteSpace(master.uniqueSkillName);
    }

    public string ToJson(bool prettyPrint = false)
    {
        return JsonUtility.ToJson(this, prettyPrint);
    }

    public override string ToString()
    {
        return $"[{uniqueSkillId}] {uniqueSkillName} (attributes={AttributeCount})";
    }
}

/// <summary>JsonUtility 用: AI 応答エンベロープ（単一ユニークスキル）。</summary>
[Serializable]
public sealed class UniqueSkillEnvelopeDto
{
    public string contentType;
    public UniqueSkillMaster uniqueSkill;
}

/// <summary>JsonUtility 用: 複数ユニークスキル一括ロード。</summary>
[Serializable]
public sealed class UniqueSkillListDto
{
    public List<UniqueSkillMaster> uniqueSkills = new List<UniqueSkillMaster>();
}

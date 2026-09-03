using System;
using UnityEngine;

/// <summary>
/// スキルオーブ（使い捨てアイテム）のデータ。
/// プレイヤーがスキルレベルを犠牲にして生成し、非同期ショップで流通します。
/// JsonUtility シリアライズに対応しています。
/// </summary>
[Serializable]
public class SkillOrbData
{
    [Tooltip("元となるスキルの識別子（snake_case）")]
    public string skillID;

    [Tooltip("オーブを作成したプレイヤー名（銘）")]
    public string creatorName;

    [Tooltip("抽出時点のスキルレベル（価値の基準）")]
    public int skillLevelAtExtraction;

    [Tooltip("スキルの基礎レアリティ（1〜5）")]
    public int rarity;

    /// <summary>最低限の識別情報があるか</summary>
    public bool IsValid()
    {
        return !string.IsNullOrWhiteSpace(skillID);
    }

    /// <summary>レアリティを 1〜5 にクランプします。</summary>
    public void NormalizeRarity()
    {
        rarity = Mathf.Clamp(rarity, 1, 5);
    }

    public SkillOrbData Clone()
    {
        return FromJson(ToJson());
    }

    public static SkillOrbData FromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            SkillOrbData data = JsonUtility.FromJson<SkillOrbData>(json);
            if (data != null && data.IsValid())
            {
                data.NormalizeRarity();
                return data;
            }
        }
        catch (Exception exception)
        {
            Debug.LogError($"[SkillOrbData] JSON パース失敗: {exception.Message}");
        }

        return null;
    }

    public string ToJson(bool prettyPrint = false)
    {
        return JsonUtility.ToJson(this, prettyPrint);
    }

    public override string ToString()
    {
        return $"[Orb:{skillID}] 銘:{creatorName} Lv.{skillLevelAtExtraction} ★{rarity}";
    }
}

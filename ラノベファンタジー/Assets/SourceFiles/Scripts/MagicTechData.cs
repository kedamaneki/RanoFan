using System;
using UnityEngine;

/// <summary>
/// 魔法技術 ID の定数。AI 動的生成時も snake_case で統一します。
/// </summary>
public static class MagicIds
{
    /// <summary>初級火魔法・武器エンチャントの型</summary>
    public const string FireEnchant = "magic_fire_enchant";

    /// <summary>将来の AI 生成スタブ用サンプル ID</summary>
    public const string CustomArcaneBolt = "magic_custom_arcane_bolt";
}

/// <summary>
/// 魔導書・伝授・将来の AI 術式生成で扱う魔法技術データ。
/// JsonUtility でシリアライズ可能な論理構造です（閃きシステムとは独立）。
/// </summary>
[Serializable]
public class MagicTechData
{
    /// <summary>snake_case の識別子（例: magic_fire_enchant）</summary>
    public string magicID;

    /// <summary>表示名（例: 初級火魔法・火の粉の型）</summary>
    public string magicName;

    /// <summary>発動に必要な MP 消費量</summary>
    public float mpCost;

    /// <summary>効果持続時間（秒）。エンチャント等のバフ時間</summary>
    public float duration;

    /// <summary>魔力操作スキル等で拡張されるカスタマイズ深度</summary>
    public int customizationLevel;

    /// <summary>将来 LLM に渡す術式構成テキスト（論理・フレーバー）</summary>
    public string spellFormulaLog;

    /// <summary>データが最低限有効かを返します。</summary>
    public bool IsValid()
    {
        return !string.IsNullOrWhiteSpace(magicID) &&
               !string.IsNullOrWhiteSpace(magicName) &&
               mpCost >= 0f &&
               duration >= 0f;
    }

    /// <summary>深いコピーを返します。</summary>
    public MagicTechData Clone()
    {
        return JsonUtility.FromJson<MagicTechData>(JsonUtility.ToJson(this));
    }

    /// <summary>JSON 文字列へ変換します（AI 連携・保存用）。</summary>
    public string ToJson()
    {
        return JsonUtility.ToJson(this, true);
    }

    /// <summary>JSON から復元します。</summary>
    public static MagicTechData FromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        return JsonUtility.FromJson<MagicTechData>(json);
    }

    /// <summary>テスト用：初級火エンチャントのスタブデータ。</summary>
    public static MagicTechData CreateFireEnchantStub()
    {
        return new MagicTechData
        {
            magicID = MagicIds.FireEnchant,
            magicName = "初級火魔法・火の粉の型",
            mpCost = 12f,
            duration = 8f,
            customizationLevel = 1,
            spellFormulaLog =
                "術式: [火素] + [粉化] + [武器付着] | 媒介: 片手剣刃 | 出力: 炎属性エンチャント"
        };
    }

    /// <summary>テスト用：AI 生成を想定したカスタム魔法スタブ。</summary>
    public static MagicTechData CreateCustomArcaneBoltStub()
    {
        return new MagicTechData
        {
            magicID = MagicIds.CustomArcaneBolt,
            magicName = "試作・星紋弾",
            mpCost = 18f,
            duration = 0f,
            customizationLevel = 3,
            spellFormulaLog =
                "AI_DRAFT: {element:arcane, shape:bolt, modifier:pierce, cost_tier:mid, notes:prototype}"
        };
    }
}

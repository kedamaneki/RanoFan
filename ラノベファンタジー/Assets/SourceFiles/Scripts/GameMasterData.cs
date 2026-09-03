using System;
using System.Collections.Generic;
using UnityEngine;

// =============================================================================
// 統合マスターデータ定義（魔法 / ジョブ / 敵 / アイテム）
// 基礎スキル SkillMaster・ユニークスキル UniqueSkillMaster は既存クラスを流用
// =============================================================================

/// <summary>ステータス倍率補正（ジョブマスター用）。</summary>
[Serializable]
public class StatModifiers
{
    [Tooltip("HP 倍率（1.0 = 変化なし）")]
    public float hpMultiplier = 1f;

    [Tooltip("STR 倍率")]
    public float strMultiplier = 1f;

    [Tooltip("DEF 倍率")]
    public float defMultiplier = 1f;

    [Tooltip("魔力（MP）倍率")]
    public float manaMultiplier = 1f;

    public void Sanitize()
    {
        hpMultiplier = Mathf.Max(0f, hpMultiplier);
        strMultiplier = Mathf.Max(0f, strMultiplier);
        defMultiplier = Mathf.Max(0f, defMultiplier);
        manaMultiplier = Mathf.Max(0f, manaMultiplier);
    }
}

/// <summary>敵ドロップ1エントリ。</summary>
[Serializable]
public class DropItemData
{
    [Tooltip("ドロップアイテム ID")]
    public string itemId;

    [Tooltip("ドロップ確率（0.0〜1.0）")]
    public float dropChance;

    public bool IsValid()
    {
        return !string.IsNullOrWhiteSpace(itemId) &&
               dropChance >= 0f && dropChance <= 1f;
    }

    public void Sanitize(string ownerLabel)
    {
        itemId = itemId?.Trim() ?? string.Empty;
        if (dropChance < 0f || dropChance > 1f)
        {
            float clamped = Mathf.Clamp01(dropChance);
            Debug.LogWarning(
                $"[DropItemData] {ownerLabel}: dropChance={dropChance} を {clamped} に補正しました。");
            dropChance = clamped;
        }
    }
}

/// <summary>
/// 魔法マスター1件。
/// プロジェクト全体では魔力表記を統一し、消費量フィールド名は manaCost を使用します。
/// </summary>
[Serializable]
public class MagicMasterData
{
    public string id;
    public string name;
    public string description;

    [Tooltip("消費魔力（manaCost）。ArtsData.manaCost と同系統")]
    public int manaCost;

    [Tooltip("属性（火 / 水 / 結晶 / 無 等）")]
    public string element;

    [Tooltip("基礎威力")]
    public float power;

    [Tooltip("ダメージ/回復/補正倍率（1.0 = 100%）。動的生成時は MagicSanitizerEngine が 3.0 上限。")]
    public float value;

    [Tooltip("特殊効果語彙（SpecialEffectTypes）。未知は Safe-Fail フォールバック。")]
    public string effectType;

    [Tooltip("動的生成時の基礎 MP。未指定なら manaCost を使う。")]
    public int baseManaCost;

    [Tooltip("発動速度倍率（1.0 = 標準）")]
    public float castSpeedModifier = 1f;

    [Tooltip("効果範囲・軌道半径など")]
    public float areaRange;

    [Tooltip("固有特殊挙動コード（TRACKING / PIERCING / REBOUND 等）")]
    public string customTraitCode;

    [Tooltip("時代補正。未設定（古い JSON）なら MagicCustomizer は 1.0 倍のまま")]
    public MagicEraSettings eraSettings;

    public bool IsValid()
    {
        return !string.IsNullOrWhiteSpace(id) &&
               !string.IsNullOrWhiteSpace(name);
    }

    public void Sanitize()
    {
        id = id?.Trim() ?? string.Empty;
        name = name?.Trim() ?? string.Empty;
        description = description?.Trim() ?? string.Empty;
        element = element?.Trim() ?? string.Empty;
        effectType = effectType?.Trim() ?? string.Empty;
        manaCost = Mathf.Max(0, manaCost);
        baseManaCost = Mathf.Max(0, baseManaCost);
        power = Mathf.Max(0f, power);
        value = Mathf.Max(0f, value);
        castSpeedModifier = castSpeedModifier <= 0f ? 1f : castSpeedModifier;
        areaRange = Mathf.Max(0f, areaRange);
        customTraitCode = customTraitCode?.Trim() ?? string.Empty;
        eraSettings?.Sanitize();
    }
}

/// <summary>魔法の時代別倍率。0 以下は「未指定」として Resolver 既定値へフォールバック。</summary>
[Serializable]
public class MagicEraSettings
{
    public float earlyManaCostMultiplier = 1f;
    public float midManaCostMultiplier = 1f;
    public float lateManaCostMultiplier;
    public float earlyCastSpeedMultiplier = 1f;
    public float midCastSpeedMultiplier = 1f;
    public float lateCastSpeedMultiplier;

    public void Sanitize()
    {
        earlyManaCostMultiplier = Mathf.Max(0f, earlyManaCostMultiplier);
        midManaCostMultiplier = Mathf.Max(0f, midManaCostMultiplier);
        lateManaCostMultiplier = Mathf.Max(0f, lateManaCostMultiplier);
        earlyCastSpeedMultiplier = Mathf.Max(0f, earlyCastSpeedMultiplier);
        midCastSpeedMultiplier = Mathf.Max(0f, midCastSpeedMultiplier);
        lateCastSpeedMultiplier = Mathf.Max(0f, lateCastSpeedMultiplier);
    }
}

/// <summary>ジョブ / 職業マスター1件。</summary>
[Serializable]
public class JobMasterData
{
    public string id;
    public string name;
    public string description;
    public StatModifiers statModifiers = new StatModifiers();
    public List<string> unlockConditions = new List<string>();

    [Tooltip("進化（転職）先ジョブ ID。無ければ空文字")]
    public string nextJobId;

    [Tooltip("就職時に自動付与する基礎スキル ID リスト")]
    public List<string> defaultSkillIds = new List<string>();

    [Tooltip("時代別の優先解禁フラグ。未設定なら通常 unlockConditions のみ")]
    public JobEraSettings eraSettings;

    public bool IsValid()
    {
        return !string.IsNullOrWhiteSpace(id) &&
               !string.IsNullOrWhiteSpace(name);
    }

    public bool HasNextJob => !string.IsNullOrWhiteSpace(nextJobId);

    public void Sanitize()
    {
        id = id?.Trim() ?? string.Empty;
        name = name?.Trim() ?? string.Empty;
        description = description?.Trim() ?? string.Empty;
        statModifiers?.Sanitize();
        unlockConditions ??= new List<string>();
        nextJobId = nextJobId?.Trim() ?? string.Empty;
        defaultSkillIds ??= new List<string>();

        for (int i = 0; i < unlockConditions.Count; i++)
        {
            unlockConditions[i] = unlockConditions[i]?.Trim() ?? string.Empty;
        }

        for (int i = 0; i < defaultSkillIds.Count; i++)
        {
            defaultSkillIds[i] = defaultSkillIds[i]?.Trim() ?? string.Empty;
        }

        eraSettings?.Sanitize();
    }
}

/// <summary>ジョブ転職の時代別優先フラグ。空配列はその時代で追加条件なし。</summary>
[Serializable]
public class JobEraSettings
{
    public string[] earlyUnlockFlags;
    public string[] midUnlockFlags;
    public string[] lateUnlockFlags;

    public void Sanitize()
    {
        TrimFlags(earlyUnlockFlags);
        TrimFlags(midUnlockFlags);
        TrimFlags(lateUnlockFlags);
    }

    private static void TrimFlags(string[] flags)
    {
        if (flags == null)
        {
            return;
        }

        for (int i = 0; i < flags.Length; i++)
        {
            flags[i] = flags[i]?.Trim() ?? string.Empty;
        }
    }
}

/// <summary>
/// 敵マスター1件。
/// 戦闘タイミング詳細は既存 EnemyAttackProfile が担当し、本クラスは ID ベースの静的定義です。
/// </summary>
[Serializable]
public class EnemyMasterData
{
    public string id;
    public string name;
    public int hp;
    public int str;
    public int def;

    [Tooltip("AI 行動ルーチン ID（VisibleEnemyAI 等と連携予定）")]
    public string behaviorPattern;

    public List<DropItemData> dropItems = new List<DropItemData>();

    public bool IsValid()
    {
        return !string.IsNullOrWhiteSpace(id) &&
               !string.IsNullOrWhiteSpace(name) &&
               hp > 0;
    }

    public void Sanitize()
    {
        id = id?.Trim() ?? string.Empty;
        name = name?.Trim() ?? string.Empty;
        behaviorPattern = behaviorPattern?.Trim() ?? string.Empty;
        hp = Mathf.Max(1, hp);
        str = Mathf.Max(0, str);
        def = Mathf.Max(0, def);
        dropItems ??= new List<DropItemData>();

        for (int i = 0; i < dropItems.Count; i++)
        {
            DropItemData drop = dropItems[i];
            if (drop != null)
            {
                drop.Sanitize($"Enemy[{id}].drop[{i}]");
            }
        }
    }
}

/// <summary>
/// アイテム / 素材マスター1件。
/// ランタイムインベントリの ItemData（id / itemName / weight / maxStack）とは別の JSON マスター定義です。
/// </summary>
[Serializable]
public class ItemMasterData
{
    public string id;
    public string name;
    public string description;

    [Tooltip("Material / Consumable / Equipment")]
    public string itemType;

    [Tooltip("インベントリ重量（kg）")]
    public float weight;

    [Tooltip("工房品質への影響（Dissolution / Extraction 連携用）")]
    public float craftValue;

    [Tooltip("1スロット最大スタック（ItemData 互換用。未指定時は 99）")]
    public int maxStack = 99;

    [Tooltip("成果物テンプレートの純度。hasCraftHiddenParams が false なら未指定（JSON 省略時は 0）")]
    public float purity;

    [Tooltip("成果物テンプレートの密度。hasCraftHiddenParams が false なら未指定")]
    public float density;

    [Tooltip("マスター側に Purity / Density が明示されているか。ランタイム焼き込みは ItemData 側で行う")]
    public bool hasCraftHiddenParams;

    public bool IsValid()
    {
        return !string.IsNullOrWhiteSpace(id) &&
               !string.IsNullOrWhiteSpace(name) &&
               weight >= 0f &&
               maxStack > 0;
    }

    public void Sanitize()
    {
        id = id?.Trim() ?? string.Empty;
        name = name?.Trim() ?? string.Empty;
        description = description?.Trim() ?? string.Empty;
        itemType = itemType?.Trim() ?? string.Empty;
        weight = Mathf.Max(0f, weight);
        craftValue = Mathf.Max(0f, craftValue);
        maxStack = Mathf.Max(1, maxStack);
    }

    /// <summary>既存 InventoryManager 向け ItemData へ変換します。</summary>
    public ItemData ToItemData()
    {
        return new ItemData
        {
            id = id,
            itemName = name,
            weight = weight,
            maxStack = maxStack,
            itemType = itemType ?? string.Empty,
            purity = purity,
            density = density,
            hasCraftHiddenParams = hasCraftHiddenParams
        };
    }
}

/// <summary>
/// AI 生成 JSON のルートコンテナ。
/// skills には既存 SkillMaster（技ツリー構造）をそのまま格納します。
/// </summary>
[Serializable]
public class GameMasterData
{
    public List<SkillMaster> skills = new List<SkillMaster>();
    public List<MagicMasterData> magics = new List<MagicMasterData>();
    public List<JobMasterData> jobs = new List<JobMasterData>();
    public List<EnemyMasterData> enemies = new List<EnemyMasterData>();
    public List<ItemMasterData> items = new List<ItemMasterData>();

    public void SanitizeAll()
    {
        SanitizeSkillList();
        SanitizeList(magics, m => m.Sanitize());
        SanitizeList(jobs, j => j.Sanitize());
        SanitizeList(enemies, e => e.Sanitize());
        SanitizeList(items, i => i.Sanitize());
    }

    private void SanitizeSkillList()
    {
        if (skills == null)
        {
            skills = new List<SkillMaster>();
            return;
        }

        for (int i = 0; i < skills.Count; i++)
        {
            SkillMaster skill = skills[i];
            if (skill == null)
            {
                continue;
            }

            skill.evolutionData ??= new SkillEvolutionData();
            skill.evolutionData.Sanitize(skill.skillId ?? $"skill[{i}]");
        }
    }

    private static void SanitizeList<T>(List<T> list, Action<T> sanitizer) where T : class
    {
        if (list == null)
        {
            return;
        }

        for (int i = 0; i < list.Count; i++)
        {
            T entry = list[i];
            if (entry != null)
            {
                sanitizer(entry);
            }
        }
    }
}

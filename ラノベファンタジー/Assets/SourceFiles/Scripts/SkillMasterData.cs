using System;
using System.Collections.Generic;
using UnityEngine;

// =============================================================================
// 基礎スキル（SkillMaster）— AI 生成 JSON の受け皿
// 連携: SkillMasterRepository / PlayerSkillSlotManager / BaseSkillAwakeningSystem
// =============================================================================

/// <summary>スキル大分類（AI 生成時の category 文字列）。</summary>
public static class SkillMasterCategories
{
    public const string Combat = "Combat";
    public const string Production = "Production";
    public const string Utility = "Utility";
    public const string Hobby = "Hobby";
}

/// <summary>特殊効果の effectType 定数（AI 生成・ランタイム解釈の共通語彙）。</summary>
public static class SpecialEffectTypes
{
    // 戦闘 — バフ / デバフ / 体勢
    public const string BuffStr = "Buff_STR";
    public const string BuffPoise = "Buff_Poise";
    public const string DebuffPoiseRegen = "Debuff_PoiseRegen";
    public const string DebuffPoiseRecoveryHalt = "Debuff_PoiseRecoveryHalt";
    public const string DebuffArmorDissolve = "Debuff_ArmorDissolve";

    // 工房 — ブラインド生産の裏パラメータ
    public const string CraftExtractionBonus = "Craft_ExtractionBonus";
    public const string CraftDissolutionRate = "Craft_DissolutionRate";
    public const string CraftPurityBonus = "Craft_PurityBonus";
    public const string CraftPurityDeltaBoost = "Craft_PurityDeltaBoost";
    public const string CraftThermalRisk = "Craft_ThermalRisk";

    // 趣味 — 探索・社交
    public const string HobbyGatherLuck = "Hobby_GatherLuck";
    public const string HobbyNpcAffinity = "Hobby_NpcAffinity";

    // 超能力・便利枠（プロンプト語彙 — Arts / ユニークスキル共通）
    public const string PsychicRegenHp = "Psychic_Regen_HP";
    public const string PsychicRegenMana = "Psychic_Regen_Mana";
    public const string PsychicManaControl = "Psychic_ManaControl";
    public const string PsychicBoostStr = "Psychic_Boost_STR";
    public const string PsychicBoostDef = "Psychic_Boost_DEF";
    public const string CraftPurityFlatBonus = "Craft_PurityFlatBonus";
    public const string CraftRecipeRecord = "Craft_RecipeRecord";
    public const string UtilityMindAccelerate = "Utility_MindAccelerate";
    public const string UtilityMapRadar = "Utility_MapRadar";
    public const string UtilityEnemyDetect = "Utility_EnemyDetect";
}

/// <summary>技に付与される特殊効果（バフ・デバフ・工房裏パラメータ等）。</summary>
[Serializable]
public class SpecialEffectData
{
    [Tooltip("効果種別（例: Buff_STR, Debuff_PoiseRegen, Craft_ExtractionBonus）")]
    public string effectType;

    [Tooltip("効果量（倍率・加算値・割合など effectType ごとに解釈）")]
    public float value;

    [Tooltip("持続時間（秒）。0 は即時または永続")]
    public float duration;

    public bool IsValid()
    {
        return !string.IsNullOrWhiteSpace(effectType);
    }

    public SpecialEffectData Clone()
    {
        return new SpecialEffectData
        {
            effectType = effectType,
            value = value,
            duration = duration
        };
    }
}

/// <summary>
/// スキル内の「技（Arts）」。
/// derivatives により派生技を再帰的に保持し、技開発システムへ接続します。
/// </summary>
[Serializable]
public class ArtsData
{
    [Tooltip("技の一意識別子")]
    public string artId;

    [Tooltip("技の表示名")]
    public string artName;

    [Tooltip("技の説明")]
    public string description;

    [Tooltip("体勢値ダメージ")]
    public int poiseDamage;

    [Tooltip("魔力消費量")]
    public int manaCost;

    [Tooltip("プレイヤーが解放済みか")]
    public bool unlocked;

    [Tooltip("技に紐づく特殊効果（複数可）")]
    public List<SpecialEffectData> specialEffects = new List<SpecialEffectData>();

    [Tooltip("技開発で開花する派生技（再帰構造）")]
    public List<ArtsData> derivatives = new List<ArtsData>();

    public bool IsValid()
    {
        return !string.IsNullOrWhiteSpace(artId) &&
               !string.IsNullOrWhiteSpace(artName);
    }

    /// <summary>派生技を含めた総技数を返します。</summary>
    public int CountArtsRecursive()
    {
        int count = 1;
        if (derivatives == null)
        {
            return count;
        }

        for (int i = 0; i < derivatives.Count; i++)
        {
            ArtsData child = derivatives[i];
            if (child != null)
            {
                count += child.CountArtsRecursive();
            }
        }

        return count;
    }

    /// <summary>artId で自身または派生ツリー内の技を検索します。</summary>
    public ArtsData FindArtRecursive(string targetArtId)
    {
        if (string.IsNullOrWhiteSpace(targetArtId))
        {
            return null;
        }

        if (string.Equals(artId, targetArtId, StringComparison.OrdinalIgnoreCase))
        {
            return this;
        }

        if (derivatives == null)
        {
            return null;
        }

        for (int i = 0; i < derivatives.Count; i++)
        {
            ArtsData child = derivatives[i];
            if (child == null)
            {
                continue;
            }

            ArtsData found = child.FindArtRecursive(targetArtId);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }

    public ArtsData Clone()
    {
        return JsonUtility.FromJson<ArtsData>(JsonUtility.ToJson(this));
    }
}

/// <summary>
/// 基礎スキル（器）。複数の技を内包し、派生技の開発ツリーを保持します。
/// </summary>
[Serializable]
public class SkillMaster
{
    [Tooltip("スキルの一意識別子")]
    public string skillId;

    [Tooltip("スキル表示名")]
    public string skillName;

    [Tooltip("大分類: Combat / Production / Hobby")]
    public string category;

    [Tooltip("スキル全体の説明")]
    public string description;

    [Tooltip("スキルに最初から内包される基本技リスト")]
    public List<ArtsData> baseArts = new List<ArtsData>();

    [Tooltip("スキル全体の進化条件（上位スキルへの段階変化。技ツリーとは別軸）")]
    public SkillEvolutionData evolutionData = new SkillEvolutionData();

    public bool IsValid()
    {
        return !string.IsNullOrWhiteSpace(skillId) &&
               !string.IsNullOrWhiteSpace(skillName) &&
               !string.IsNullOrWhiteSpace(category);
    }

    /// <summary>skillId 配下の全技（基本＋派生）をフラットに列挙します。</summary>
    public List<ArtsData> CollectAllArts()
    {
        List<ArtsData> result = new List<ArtsData>();
        if (baseArts == null)
        {
            return result;
        }

        for (int i = 0; i < baseArts.Count; i++)
        {
            CollectArtsRecursive(baseArts[i], result);
        }

        return result;
    }

    private static void CollectArtsRecursive(ArtsData art, List<ArtsData> sink)
    {
        if (art == null || sink == null)
        {
            return;
        }

        sink.Add(art);
        if (art.derivatives == null)
        {
            return;
        }

        for (int i = 0; i < art.derivatives.Count; i++)
        {
            CollectArtsRecursive(art.derivatives[i], sink);
        }
    }

    /// <summary>artId で技を検索します（派生ツリー含む）。</summary>
    public ArtsData FindArt(string targetArtId)
    {
        if (baseArts == null || string.IsNullOrWhiteSpace(targetArtId))
        {
            return null;
        }

        for (int i = 0; i < baseArts.Count; i++)
        {
            ArtsData art = baseArts[i];
            if (art == null)
            {
                continue;
            }

            ArtsData found = art.FindArtRecursive(targetArtId);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>unlocked == true の技だけをツリー全体から抽出します。</summary>
    public List<ArtsData> CollectUnlockedArts()
    {
        List<ArtsData> result = new List<ArtsData>();
        if (baseArts == null)
        {
            return result;
        }

        for (int i = 0; i < baseArts.Count; i++)
        {
            CollectUnlockedArtsRecursive(baseArts[i], result);
        }

        return result;
    }

    private static void CollectUnlockedArtsRecursive(ArtsData art, List<ArtsData> sink)
    {
        if (art == null || sink == null)
        {
            return;
        }

        if (art.unlocked)
        {
            sink.Add(art);
        }

        if (art.derivatives == null)
        {
            return;
        }

        for (int i = 0; i < art.derivatives.Count; i++)
        {
            CollectUnlockedArtsRecursive(art.derivatives[i], sink);
        }
    }

    public SkillMaster Clone()
    {
        return JsonUtility.FromJson<SkillMaster>(JsonUtility.ToJson(this));
    }

    public static SkillMaster FromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        SkillMaster deep = SkillMasterJsonLoader.ParseSkillMasterDeep(json);
        if (deep != null && deep.IsValid())
        {
            return deep;
        }

        try
        {
            SkillMasterDto shallow = JsonUtility.FromJson<SkillMasterDto>(json);
            if (shallow != null && shallow.IsValid())
            {
                return shallow.ToRuntime();
            }

            SkillMaster direct = JsonUtility.FromJson<SkillMaster>(json);
            if (direct != null && direct.IsValid())
            {
                return direct;
            }

            SkillMasterEnvelopeDto envelope = JsonUtility.FromJson<SkillMasterEnvelopeDto>(json);
            if (envelope?.skillMaster != null && envelope.skillMaster.IsValid())
            {
                return envelope.skillMaster;
            }

            return null;
        }
        catch (Exception exception)
        {
            Debug.LogError($"[SkillMaster] JSON パース失敗: {exception.Message}");
            return null;
        }
    }

    public string ToJson(bool prettyPrint = false)
    {
        return JsonUtility.ToJson(this, prettyPrint);
    }

    public override string ToString()
    {
        int artCount = CollectAllArts().Count;
        return $"[{skillId}] {skillName} ({category}) arts={artCount}";
    }
}

/// <summary>JsonUtility 用: AI 応答エンベロープ（単一スキル）。</summary>
[Serializable]
public sealed class SkillMasterEnvelopeDto
{
    public string contentType;
    public SkillMaster skillMaster;
}

/// <summary>JsonUtility 用: 複数スキル一括ロード（配列の代わりにラップ）。</summary>
[Serializable]
public sealed class SkillMasterListDto
{
    public List<SkillMaster> skills = new List<SkillMaster>();
}

/// <summary>
/// JsonUtility 用の浅い ArtsData（derivatives なし — 深度制限回避の分割パース用）。
/// </summary>
[Serializable]
public class ArtsDataShallowDto
{
    public string artId;
    public string artName;
    public string description;
    public int poiseDamage;
    public int manaCost;
    public bool unlocked;
    public List<SpecialEffectData> specialEffects = new List<SpecialEffectData>();

    public ArtsData ToRuntime()
    {
        return new ArtsData
        {
            artId = artId?.Trim() ?? string.Empty,
            artName = artName?.Trim() ?? string.Empty,
            description = description?.Trim() ?? string.Empty,
            poiseDamage = poiseDamage,
            manaCost = manaCost,
            unlocked = unlocked,
            specialEffects = ArtsDataDto.CloneSpecialEffectsForImport(specialEffects),
            derivatives = new List<ArtsData>()
        };
    }
}

/// <summary>JsonUtility 用の浅い SkillMaster（baseArts なし — 深度制限回避の分割パース用）。</summary>
[Serializable]
public class SkillMasterShallowDto
{
    public string skillId;
    public string skillName;
    public string category;
    public string description;
    public SkillEvolutionData evolutionData = new SkillEvolutionData();

    public bool IsValid()
    {
        return !string.IsNullOrWhiteSpace(skillId) &&
               !string.IsNullOrWhiteSpace(skillName);
    }

    public SkillMaster ToRuntimeShell()
    {
        return new SkillMaster
        {
            skillId = skillId?.Trim() ?? string.Empty,
            skillName = skillName?.Trim() ?? string.Empty,
            category = category?.Trim() ?? string.Empty,
            description = description?.Trim() ?? string.Empty,
            evolutionData = evolutionData ?? new SkillEvolutionData(),
            baseArts = new List<ArtsData>()
        };
    }
}

/// <summary>
/// JsonUtility 用の ArtsData DTO（derivatives は再帰リストで木構造を保持）。
/// </summary>
[Serializable]
public class ArtsDataDto
{
    public string artId;
    public string artName;
    public string description;
    public int poiseDamage;
    public int manaCost;
    public bool unlocked;
    public List<SpecialEffectData> specialEffects = new List<SpecialEffectData>();

    /// <summary>派生技（再帰 DTO）。JsonUtility が木構造を復元するための入れ子リスト。</summary>
    public List<ArtsDataDto> derivatives = new List<ArtsDataDto>();

    /// <summary>DTO をランタイム ArtsData へ再帰変換します（derivatives も完全復元）。</summary>
    public ArtsData ToRuntime()
    {
        ArtsData runtime = new ArtsData
        {
            artId = artId?.Trim() ?? string.Empty,
            artName = artName?.Trim() ?? string.Empty,
            description = description?.Trim() ?? string.Empty,
            poiseDamage = poiseDamage,
            manaCost = manaCost,
            unlocked = unlocked,
            specialEffects = CloneSpecialEffects(specialEffects),
            derivatives = new List<ArtsData>()
        };

        if (derivatives != null)
        {
            for (int i = 0; i < derivatives.Count; i++)
            {
                ArtsDataDto childDto = derivatives[i];
                if (childDto == null)
                {
                    continue;
                }

                // 子ノードも同じ ToRuntime() を再帰呼び出しし、木構造を復元
                runtime.derivatives.Add(childDto.ToRuntime());
            }
        }

        return runtime;
    }

    private static List<SpecialEffectData> CloneSpecialEffects(List<SpecialEffectData> source)
    {
        return CloneSpecialEffectsForImport(source);
    }

    /// <summary>ArtsDataShallowDto / 分割 JSON パーサーから共有利用。</summary>
    public static List<SpecialEffectData> CloneSpecialEffectsForImport(List<SpecialEffectData> source)
    {
        List<SpecialEffectData> copy = new List<SpecialEffectData>();
        if (source == null)
        {
            return copy;
        }

        for (int i = 0; i < source.Count; i++)
        {
            SpecialEffectData effect = source[i];
            if (effect != null && effect.IsValid())
            {
                copy.Add(effect.Clone());
            }
        }

        return copy;
    }
}

/// <summary>JsonUtility 用の浅い SkillMaster（baseArts は ArtsDataDto）。</summary>
[Serializable]
public class SkillMasterDto
{
    public string skillId;
    public string skillName;
    public string category;
    public string description;
    public List<ArtsDataDto> baseArts = new List<ArtsDataDto>();
    public SkillEvolutionData evolutionData = new SkillEvolutionData();

    public bool IsValid()
    {
        return !string.IsNullOrWhiteSpace(skillId) &&
               !string.IsNullOrWhiteSpace(skillName);
    }

    public SkillMaster ToRuntime()
    {
        SkillMaster master = new SkillMaster
        {
            skillId = skillId?.Trim() ?? string.Empty,
            skillName = skillName?.Trim() ?? string.Empty,
            category = category?.Trim() ?? string.Empty,
            description = description?.Trim() ?? string.Empty,
            evolutionData = evolutionData ?? new SkillEvolutionData(),
            baseArts = new List<ArtsData>()
        };

        if (baseArts != null)
        {
            for (int i = 0; i < baseArts.Count; i++)
            {
                ArtsDataDto dto = baseArts[i];
                if (dto != null)
                {
                    master.baseArts.Add(dto.ToRuntime());
                }
            }
        }

        return master;
    }
}

/// <summary>GameMaster JSON 一括ロード用 DTO（skills は別途 SkillMasterJsonLoader で読み込み）。</summary>
[Serializable]
public class GameMasterCoreLoadDto
{
    public List<MagicMasterData> magics = new List<MagicMasterData>();
    public List<JobMasterData> jobs = new List<JobMasterData>();
    public List<EnemyMasterData> enemies = new List<EnemyMasterData>();
    public List<ItemMasterData> items = new List<ItemMasterData>();
}

/// <summary>
/// GameMaster JSON 一括ロード用 DTO（レガシー・一括 FromJson は深度制限で失敗するため非推奨）。
/// </summary>
[Serializable]
public class GameMasterLoadDto : GameMasterCoreLoadDto
{
    public List<SkillMasterDto> skills = new List<SkillMasterDto>();
}

/// <summary>skills 配列ラップ（浅い DTO 版）。</summary>
[Serializable]
public sealed class SkillMasterListImportDto
{
    public List<SkillMasterDto> skills = new List<SkillMasterDto>();
}

using System;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

// =============================================================================
// AI 生成 BaseSkill（基礎スキル）のパース・SkillData 変換・プレイヤー枠への実反映
// 連携: GamePhaseEventBridge / PlayerSkillSlotManager / RealAIGameResponseParser
// =============================================================================

/// <summary>JsonUtility 用: AI が返す baseSkill ブロック。</summary>
[Serializable]
public sealed class AIGeneratedBaseSkillDto
{
    public string skillID;
    public string skillName;
    public string description;
    public string targetBonus;
    public float bonusValue;
}

/// <summary>JsonUtility 用: BaseSkill 応答エンベロープ。</summary>
[Serializable]
public sealed class BaseSkillEnvelopeDto
{
    public string generationType;
    public string phase;
    public AIGeneratedBaseSkillDto baseSkill;
    public GeneratedSkillDataDto skill;
}

/// <summary>パース済みの AI 基礎スキル（ランタイムモデル）。</summary>
public sealed class AIGeneratedBaseSkillData
{
    /// <summary>AI が生成した一意のスキル ID。</summary>
    public string SkillId { get; }

    /// <summary>基礎スキル表示名。</summary>
    public string SkillName { get; }

    /// <summary>由来・効果のフレーバーテキスト。</summary>
    public string Description { get; }

    /// <summary>影響するステータス種別（damageBonus / speedBonus 等）。</summary>
    public string TargetBonus { get; }

    /// <summary>ステータス上昇値。</summary>
    public float BonusValue { get; }

    public AIGeneratedBaseSkillData(
        string skillId,
        string skillName,
        string description,
        string targetBonus,
        float bonusValue)
    {
        SkillId = skillId ?? string.Empty;
        SkillName = skillName ?? string.Empty;
        Description = description ?? string.Empty;
        TargetBonus = targetBonus ?? string.Empty;
        BonusValue = bonusValue;
    }

    /// <summary>プレイヤー付与に必要な最低限のフィールドが揃っているか。</summary>
    public bool IsValid()
    {
        return !string.IsNullOrWhiteSpace(SkillId) &&
               !string.IsNullOrWhiteSpace(SkillName) &&
               !string.IsNullOrWhiteSpace(Description);
    }

    /// <summary>ログ表示用のボーナス表記（例: スピードボーナス +3）。</summary>
    public string FormatBonusLabel()
    {
        string label = HumanizeBonusKey(TargetBonus);
        string sign = BonusValue >= 0f ? "+" : string.Empty;
        return $"{label} {sign}{BonusValue:0.##}";
    }

    private static string HumanizeBonusKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return "ボーナス";
        }

        switch (key.Trim().ToLowerInvariant())
        {
            case "damagebonus":
            case "strengthbonus":
                return "ダメージボーナス";
            case "speedbonus":
                return "スピードボーナス";
            case "defensebonus":
                return "防御ボーナス";
            case "staminabonus":
                return "スタミナボーナス";
            case "evasionbonus":
                return "回避ボーナス";
            default:
                return key;
        }
    }
}

/// <summary>基礎スキル装着結果。</summary>
public readonly struct BaseSkillEquipResult
{
    public bool Succeeded { get; }
    public int SlotIndex { get; }
    public string Message { get; }

    public BaseSkillEquipResult(bool succeeded, int slotIndex, string message)
    {
        Succeeded = succeeded;
        SlotIndex = slotIndex;
        Message = message ?? string.Empty;
    }

    public static BaseSkillEquipResult Success(int slotIndex, string message)
    {
        return new BaseSkillEquipResult(true, slotIndex, message);
    }

    public static BaseSkillEquipResult Failure(string message)
    {
        return new BaseSkillEquipResult(false, -1, message);
    }
}

/// <summary>Gemini 応答から BaseSkill JSON を抽出・パースします。</summary>
public static class BaseSkillResponseParser
{
    private static readonly Regex SkillIdSanitizeRegex = new Regex(
        @"[^a-z0-9_]+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>生応答文字列から BaseSkill を抽出します。見つからなければ null。</summary>
    public static AIGeneratedBaseSkillData TryParseFromResponse(string rawResponse)
    {
        string extracted = AIResponseParser.ExtractJsonObject(rawResponse);
        return TryParseFromExtractedJson(extracted);
    }

    /// <summary>抽出済み JSON から BaseSkill をパースします。</summary>
    public static AIGeneratedBaseSkillData TryParseFromExtractedJson(string extractedJson)
    {
        if (string.IsNullOrWhiteSpace(extractedJson))
        {
            return null;
        }

        BaseSkillEnvelopeDto envelope;
        try
        {
            envelope = JsonUtility.FromJson<BaseSkillEnvelopeDto>(extractedJson);
        }
        catch
        {
            return null;
        }

        if (envelope?.baseSkill != null &&
            !string.IsNullOrWhiteSpace(envelope.baseSkill.skillName))
        {
            return MapDto(envelope.baseSkill);
        }

        if (IsTrainingLegacyEnvelope(envelope) && envelope.skill != null)
        {
            return FromLegacyTrainingSkillDto(envelope.skill);
        }

        return null;
    }

    /// <summary>既存の GeneratedSkillData（Training 旧形式）を BaseSkill へ変換します。</summary>
    public static AIGeneratedBaseSkillData FromLegacyGeneratedSkill(GeneratedSkillData skill)
    {
        if (skill == null || skill.IsFallback || string.IsNullOrWhiteSpace(skill.SkillName))
        {
            return null;
        }

        string targetBonus = "damageBonus";
        float bonusValue = 1f;

        foreach (System.Collections.Generic.KeyValuePair<string, float> pair in skill.EffectParameters)
        {
            targetBonus = pair.Key;
            bonusValue = pair.Value;
            break;
        }

        return new AIGeneratedBaseSkillData(
            BuildSkillIdFromName(skill.SkillName),
            skill.SkillName,
            skill.FlavorText,
            targetBonus,
            bonusValue);
    }

    private static bool IsTrainingLegacyEnvelope(BaseSkillEnvelopeDto envelope)
    {
        if (envelope == null)
        {
            return false;
        }

        if (string.Equals(envelope.phase, "Training", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return string.Equals(envelope.generationType, "skill", StringComparison.OrdinalIgnoreCase) &&
               envelope.skill != null;
    }

    private static AIGeneratedBaseSkillData FromLegacyTrainingSkillDto(GeneratedSkillDataDto dto)
    {
        string targetBonus = "damageBonus";
        float bonusValue = 1f;

        if (dto.effectParameters != null && dto.effectParameters.Length > 0 &&
            dto.effectParameters[0] != null)
        {
            targetBonus = dto.effectParameters[0].key ?? targetBonus;
            bonusValue = dto.effectParameters[0].value;
        }

        string skillId = BuildSkillIdFromName(dto.skillName);
        string description = string.IsNullOrWhiteSpace(dto.flavorText)
            ? $"{dto.skillName} の基礎理論。"
            : dto.flavorText;

        return new AIGeneratedBaseSkillData(skillId, dto.skillName, description, targetBonus, bonusValue);
    }

    private static AIGeneratedBaseSkillData MapDto(AIGeneratedBaseSkillDto dto)
    {
        string skillId = string.IsNullOrWhiteSpace(dto.skillID)
            ? BuildSkillIdFromName(dto.skillName)
            : dto.skillID.Trim();

        return new AIGeneratedBaseSkillData(
            skillId,
            dto.skillName,
            dto.description,
            dto.targetBonus,
            dto.bonusValue);
    }

    /// <summary>スキル名から skill_ プレフィックス付き ID を生成します。</summary>
    public static string BuildSkillIdFromName(string skillName)
    {
        if (string.IsNullOrWhiteSpace(skillName))
        {
            return $"skill_ai_{Guid.NewGuid():N}".Substring(0, 20);
        }

        string normalized = SkillIdSanitizeRegex.Replace(skillName.ToLowerInvariant(), "_");
        normalized = normalized.Trim('_');
        if (string.IsNullOrWhiteSpace(normalized))
        {
            normalized = Guid.NewGuid().ToString("N").Substring(0, 8);
        }

        return normalized.StartsWith("skill_", StringComparison.Ordinal)
            ? normalized
            : $"skill_{normalized}";
    }
}

/// <summary>
/// AI 基礎スキルを SkillData へ変換し、プレイヤーの所持枠へ装着します。
/// </summary>
public static class BaseSkillApplier
{
    /// <summary>
    /// パース済み BaseSkill をプレイヤーのスキル枠（最大4）へ付与し、必要ならステータスボーナスを反映します。
    /// </summary>
    public static BaseSkillEquipResult TryApplyToPlayer(
        PlayerSkillSlotManager slotManager,
        AIGeneratedBaseSkillData baseSkill,
        PlayerHistoryLog historyLog,
        CombatStats combatStats = null,
        PlayerController playerController = null)
    {
        if (slotManager == null)
        {
            return BaseSkillEquipResult.Failure("PlayerSkillSlotManager が未設定です。");
        }

        if (baseSkill == null || !baseSkill.IsValid())
        {
            return BaseSkillEquipResult.Failure("BaseSkill データが不完全です。パース失敗、リトライ可能。");
        }

        BaseSkillEquipResult equipResult = slotManager.TryAcquireAIBaseSkill(baseSkill, historyLog);
        if (!equipResult.Succeeded)
        {
            return equipResult;
        }

        ApplyStatBonus(baseSkill, combatStats, playerController);
        return equipResult;
    }

    /// <summary>targetBonus に応じて戦闘ステータスへ反映します。</summary>
    public static void ApplyStatBonus(
        AIGeneratedBaseSkillData baseSkill,
        CombatStats combatStats,
        PlayerController playerController)
    {
        if (baseSkill == null)
        {
            return;
        }

        string key = baseSkill.TargetBonus?.Trim().ToLowerInvariant() ?? string.Empty;
        int rounded = Mathf.RoundToInt(baseSkill.BonusValue);

        switch (key)
        {
            case "damagebonus":
            case "strengthbonus":
                combatStats?.ApplyStrengthBonus(rounded);
                break;
            case "defensebonus":
                combatStats?.ApplyDefenseBonus(rounded);
                break;
            case "speedbonus":
            case "evasionbonus":
                playerController?.ApplyDodgeSpeedBonus(baseSkill.BonusValue);
                break;
            case "staminabonus":
                // 将来: PlayerStats 連携。現状はログのみ。
                break;
        }
    }

    /// <summary>成功ログ用の Rich Text を整形します。</summary>
    public static string FormatSuccessLog(AIGeneratedBaseSkillData baseSkill, int slotIndex)
    {
        return $"<color=#50FA7B><b>【基礎覚醒】</b>AIが戦歴から導き出したスキル" +
               $"『{baseSkill.SkillName}（{baseSkill.SkillId}）』がプレイヤーのスロット[{slotIndex}]に実装着された！" +
               $"（{baseSkill.FormatBonusLabel()}）</color>";
    }

    /// <summary>失敗ログ用の Rich Text を整形します。</summary>
    public static string FormatFailureLog(string reason)
    {
        return $"<color=#FF6B6B><b>【基礎覚醒】</b>パース失敗、リトライ可能: {reason}</color>";
    }
}

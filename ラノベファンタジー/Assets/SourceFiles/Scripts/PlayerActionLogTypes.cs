// =============================================================================
// 進化・融合判定用の戦歴／作業ログ種別（文字列キー）
// evolutionCriteria.requiredLogType と一致させる語彙
// =============================================================================

/// <summary>プレイヤー行動ログの種別キー定数。</summary>
public static class PlayerActionLogTypes
{
    // 汎用戦闘立ち回り
    public const string JustParryCount = "JustParryCount";
    public const string JustEvasionCount = "JustEvasionCount";
    public const string DodgeCount = "DodgeCount";
    public const string LowHpAttackCount = "LowHpAttackCount";
    public const string AttackCount = "AttackCount";
    public const string KillCount = "KillCount";

    // 工房
    public const string CraftSuccessCount = "CraftLog_SuccessCount";
    public const string CraftQualityTotal = "CraftLog_QualityTotal";
    public const string MaxDangerCraftDuration = "CraftLog_BurstZoneTime";

    // 便利枠
    public const string UtilityUsageTime = "UtilityLog_UsageTime";

    // 戦闘流派ログ（131カ国歴史書由来 — evolution JSON と一致）
    public const string CombatWaterReturn = "CombatLog_WaterReturn";
    public const string CombatWaterFlow = "CombatLog_WaterFlow";
    public const string CombatWaterReject = "CombatLog_WaterReject";
    public const string CombatStonePhase = "CombatLog_StonePhase";
    public const string CombatStoneHarden = "CombatLog_StoneHarden";
    public const string CombatCrustResonance = "CombatLog_CrustResonance";
    public const string CombatPhantomVeil = "CombatLog_PhantomVeil";
    public const string CombatBiomimic = "CombatLog_Biomimic";
    public const string CombatWindForce = "CombatLog_WindForce";
    public const string CombatEvasion = "CombatLog_Evasion";
    public const string CombatGridDefense = "CombatLog_GridDefense";
    public const string CombatScorchedEarth = "CombatLog_ScorchedEarth";

    public const string GeneralPrerequisite = "GeneralPrerequisite";
}

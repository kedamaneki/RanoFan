using System;
using System.Collections.Generic;
using System.Text;

#if UNITY_5_3_OR_NEWER
using UnityEngine;
#endif

// =============================================================================
// ステータス要求・装備重量キャパシティ × タルコフ式シーズンワイプ
// 単一ファイルでコピー可能。WeightAndWipeSystemTest.Main() でモック検証。
// 連携: WeaponData / JobType / CharacterGrowth / WeaponRestrictionSystem
// =============================================================================

/// <summary>ステータス不足時の戦闘ペナルティ定数。</summary>
public static class StatRequirementCombatConstants
{
    /// <summary>要求ステータス未達時、まともに振れない攻撃力倍率。</summary>
    public const float InsufficientStatAttackMultiplier = 0.35f;
}

/// <summary>肉体ボーナスとスタミナから装備重量上限を算出するためのプロファイル。</summary>
public readonly struct PhysicalBuildProfile
{
    public float AttackBonus { get; }
    public float DefenseBonus { get; }
    public float MaxStamina { get; }

    public PhysicalBuildProfile(float attackBonus, float defenseBonus, float maxStamina)
    {
        AttackBonus = Math.Max(0f, attackBonus);
        DefenseBonus = Math.Max(0f, defenseBonus);
        MaxStamina = Math.Max(0f, maxStamina);
    }

    /// <summary>
    /// 最終ステータスから肉体プロファイルを構築します。
    /// アタック＝STR、ディフェンス＝VIT、最大スタミナ＝VIT/STR 由来の肉体式。
    /// </summary>
    public static PhysicalBuildProfile FromCharacterStats(CharacterStats stats)
    {
        float maxStamina = 40f + stats.VIT * 4f + stats.STR * 1f;
        return new PhysicalBuildProfile(stats.STR, stats.VIT, maxStamina);
    }
}

/// <summary>防具1部位の重量データ。</summary>
public sealed class ArmorData
{
    public string ArmorName { get; }
    public float Weight { get; }

    public ArmorData(string armorName, float weight)
    {
        ArmorName = armorName ?? string.Empty;
        Weight = Math.Max(0f, weight);
    }
}

/// <summary>装備重量キャパシティの算出結果。</summary>
public readonly struct WeightCapacityResult
{
    public PhysicalBuildProfile Build { get; }
    public float MaxCapacity { get; }

    public WeightCapacityResult(PhysicalBuildProfile build, float maxCapacity)
    {
        Build = build;
        MaxCapacity = maxCapacity;
    }
}

/// <summary>
/// 肉体ボーナスとスタミナから最大装備可能重量を算出します。
/// 式: (アタック×1.5) + (ディフェンス×1.5) + (最大スタミナ×0.5)
/// </summary>
public static class EquipmentWeightCapacityCalculator
{
    public const float AttackWeightFactor = 1.5f;
    public const float DefenseWeightFactor = 1.5f;
    public const float StaminaWeightFactor = 0.5f;

    public static WeightCapacityResult Calculate(ICharacterStatProvider statProvider)
    {
        if (statProvider == null)
        {
            throw new ArgumentNullException(nameof(statProvider));
        }

        PhysicalBuildProfile build = PhysicalBuildProfile.FromCharacterStats(statProvider.GetFinalStats());
        float maxCapacity = CalculateMaxCapacity(build);
        return new WeightCapacityResult(build, maxCapacity);
    }

    public static float CalculateMaxCapacity(PhysicalBuildProfile build)
    {
        return build.AttackBonus * AttackWeightFactor
               + build.DefenseBonus * DefenseWeightFactor
               + build.MaxStamina * StaminaWeightFactor;
    }
}

/// <summary>装備試行・戦闘時の総合評価結果。</summary>
public readonly struct EquipmentEvaluationResult
{
    public bool MeetsStatRequirement { get; }
    public bool WithinWeightCapacity { get; }
    public float StatPenaltyMultiplier { get; }
    public float TotalEquippedWeight { get; }
    public float MaxWeightCapacity { get; }
    public PhysicalBuildProfile Build { get; }

    public bool CanWieldProperly => MeetsStatRequirement && WithinWeightCapacity;

    public EquipmentEvaluationResult(
        bool meetsStatRequirement,
        bool withinWeightCapacity,
        float statPenaltyMultiplier,
        float totalEquippedWeight,
        float maxWeightCapacity,
        PhysicalBuildProfile build)
    {
        MeetsStatRequirement = meetsStatRequirement;
        WithinWeightCapacity = withinWeightCapacity;
        StatPenaltyMultiplier = statPenaltyMultiplier;
        TotalEquippedWeight = totalEquippedWeight;
        MaxWeightCapacity = maxWeightCapacity;
        Build = build;
    }
}

/// <summary>
/// ステータス要求と重量キャパシティを統合評価します。
/// 戦闘時は StatPenaltyMultiplier を WeaponRestrictionSystem の結果に乗算します。
/// </summary>
public static class EquipmentLoadoutEvaluator
{
    public static EquipmentEvaluationResult Evaluate(
        ICharacterStatProvider statProvider,
        WeaponData weapon,
        IReadOnlyList<ArmorData> armorPieces = null)
    {
        if (statProvider == null)
        {
            throw new ArgumentNullException(nameof(statProvider));
        }

        CharacterStats finalStats = statProvider.GetFinalStats();
        WeightCapacityResult capacity = EquipmentWeightCapacityCalculator.Calculate(statProvider);

        bool meetsStats = weapon == null || finalStats.MeetsMinimum(weapon.RequiredStats);
        float statPenalty = meetsStats
            ? 1f
            : StatRequirementCombatConstants.InsufficientStatAttackMultiplier;

        float totalWeight = weapon != null ? weapon.Weight : 0f;
        if (armorPieces != null)
        {
            for (int i = 0; i < armorPieces.Count; i++)
            {
                if (armorPieces[i] != null)
                {
                    totalWeight += armorPieces[i].Weight;
                }
            }
        }

        bool withinCapacity = totalWeight <= capacity.MaxCapacity;

        return new EquipmentEvaluationResult(
            meetsStats,
            withinCapacity,
            statPenalty,
            totalWeight,
            capacity.MaxCapacity,
            capacity.Build);
    }

    /// <summary>
    /// ジョブ相性・ステータス要求ペナルティを統合した実効攻撃力を算出します。
    /// </summary>
    public static float CalculateEffectiveAttack(
        JobType job,
        WeaponData weapon,
        EquipmentEvaluationResult equipmentEval,
        Action<string> warningLogger = null)
    {
        if (weapon == null)
        {
            return 0f;
        }

        WeaponCombatResult jobResult = WeaponRestrictionSystem.Calculate(job, weapon, warningLogger);
        float effective = jobResult.FinalAttack * equipmentEval.StatPenaltyMultiplier;

        if (!equipmentEval.MeetsStatRequirement)
        {
            warningLogger?.Invoke(
                $"【ペナルティ】要求ステータス未達（必要 {weapon.RequiredStats} / 現在は装備者の最終ステータスが不足）。" +
                $"攻撃力 ×{equipmentEval.StatPenaltyMultiplier:F2} でまともに振れない。");
        }

        if (!equipmentEval.WithinWeightCapacity)
        {
            warningLogger?.Invoke(
                $"【過重量】総重量 {equipmentEval.TotalEquippedWeight:F1} > 上限 {equipmentEval.MaxWeightCapacity:F1}。" +
                "動きが鈍く、実戦ではさらなるペナルティを想定。");
        }

        return effective;
    }
}

/// <summary>プレイヤーの成長・装備状態を1か所に集約（ワイプ対象）。</summary>
public sealed class PlayerLoadoutState
{
    public CharacterGrowth Growth { get; private set; }
    public WeaponData EquippedWeapon { get; private set; }
    public IReadOnlyList<ArmorData> EquippedArmor => equippedArmor;

    private readonly List<ArmorData> equippedArmor = new List<ArmorData>();

    public PlayerLoadoutState(JobType startingJob = JobType.Novice)
    {
        Growth = new CharacterGrowth(startingJob);
    }

    public void EquipWeapon(WeaponData weapon)
    {
        EquippedWeapon = weapon;
    }

    public void EquipArmor(params ArmorData[] armorPieces)
    {
        equippedArmor.Clear();
        if (armorPieces != null)
        {
            equippedArmor.AddRange(armorPieces);
        }
    }

    public EquipmentEvaluationResult EvaluateCurrentLoadout()
    {
        return EquipmentLoadoutEvaluator.Evaluate(Growth, EquippedWeapon, equippedArmor);
    }

    /// <summary>アバター作成直後の完全な初期値へ戻します。</summary>
    public void ResetToInitialState(JobType startingJob = JobType.Novice)
    {
        Growth = new CharacterGrowth(startingJob);
        EquippedWeapon = null;
        equippedArmor.Clear();
    }

    public string BuildStatusLog()
    {
        CharacterStats stats = Growth.GetFinalStats();
        WeightCapacityResult capacity = EquipmentWeightCapacityCalculator.Calculate(Growth);
        EquipmentEvaluationResult eval = EvaluateCurrentLoadout();

        StringBuilder sb = new StringBuilder();
        sb.AppendLine($"ジョブ: {Growth.CurrentJob}  ベースLv.{Growth.BaseLevel}  ジョブLv.{Growth.JobLevel}");
        sb.AppendLine($"最終ステータス: {stats}");
        sb.AppendLine(
            $"肉体ボーナス — アタック:{capacity.Build.AttackBonus:F0} ディフェンス:{capacity.Build.DefenseBonus:F0} " +
            $"最大スタミナ:{capacity.Build.MaxStamina:F0}");
        sb.AppendLine($"最大装備重量キャパシティ: {capacity.MaxCapacity:F1}");
        sb.AppendLine($"装備中武器: {EquippedWeapon?.WeaponName ?? "なし"}（重量 {EquippedWeapon?.Weight ?? 0f:F1}）");
        sb.AppendLine($"装備総重量: {eval.TotalEquippedWeight:F1} / {eval.MaxWeightCapacity:F1}");
        sb.AppendLine($"ステータス要求: {(eval.MeetsStatRequirement ? "達成" : "未達（ペナルティ）")}");
        sb.AppendLine($"重量キャパシティ: {(eval.WithinWeightCapacity ? "範囲内" : "超過")}");
        return sb.ToString().TrimEnd();
    }
}

/// <summary>シーズン終了時の容赦ないデータ初期化。</summary>
public sealed class WipeSystem
{
    public const string WipeLogMessage =
        "システム：シーズンが終了したため、ワイプが実行されました。すべてのデータが初期化されます";

    public void TriggerSeasonalWipe(PlayerLoadoutState playerState, Action<string> log = null)
    {
        if (playerState == null)
        {
            throw new ArgumentNullException(nameof(playerState));
        }

        log?.Invoke(WipeLogMessage);
        playerState.ResetToInitialState(JobType.Novice);
    }
}

/// <summary>モック実行・検証用エントリポイント。</summary>
public static class WeightAndWipeSystemTest
{
    private static readonly WeaponData GiantIronClub = new WeaponData(
        weaponName: "巨大な鉄の棒",
        type: WeaponType.Stick,
        baseAttack: 140f,
        baseStaminaCost: 35f,
        requiredStats: new CharacterStats(str: 35, mnd: 0, vit: 0),
        weight: 52f);

    private static readonly ArmorData HeavyChestplate = new ArmorData("重厚胸当て", weight: 18f);

    public static void Main()
    {
        RunSeasonalSurvivalScenario(Console.WriteLine);
    }

    public static void RunAllScenarios(Action<string> log)
    {
        if (log == null)
        {
            log = _ => { };
        }

        RunSeasonalSurvivalScenario(log);
    }

    /// <summary>
    /// 無職の素体 → 巨大武器でペナルティ → 成長で適正装備 → ワイプで無に帰す。
    /// </summary>
    public static void RunSeasonalSurvivalScenario(Action<string> log)
    {
        if (log == null)
        {
            log = _ => { };
        }

        log("===== WeightAndWipeSystemTest: 重量・要求ステータス・ワイプ =====");

        PlayerLoadoutState player = new PlayerLoadoutState(JobType.Novice);
        WipeSystem wipeSystem = new WipeSystem();

        // 1. 初期無職が巨大武器を装備 — ステータス不足ペナルティ
        log(string.Empty);
        log("--- 1. 無職・初期状態で「巨大な鉄の棒」を装備 ---");
        player.EquipWeapon(GiantIronClub);
        player.EquipArmor(HeavyChestplate);

        EquipmentEvaluationResult eval1 = player.EvaluateCurrentLoadout();
        log(player.BuildStatusLog());
        log(string.Empty);
        log($"  要求 STR: {GiantIronClub.RequiredStats.STR} / 現在 STR: {player.Growth.GetFinalStats().STR}");

        float effectiveAttack1 = EquipmentLoadoutEvaluator.CalculateEffectiveAttack(
            player.Growth.CurrentJob,
            GiantIronClub,
            eval1,
            log);

        log($"  実効攻撃力（ジョブ相性×ステータスペナルティ込み）: {effectiveAttack1:F1}");

        AssertScenario(
            log,
            "初期ペナルティ",
            !eval1.MeetsStatRequirement &&
            eval1.StatPenaltyMultiplier < 1f &&
            effectiveAttack1 < GiantIronClub.BaseAttack * 0.5f,
            "ステータス不足で激減");

        // 2. レベルアップで肉体強化 → キャパシティ拡張・要求クリア
        log(string.Empty);
        log("--- 2. レベルアップで肉体を強化し、装備が適正化 ---");
        player.Growth.SetJobLevel(12);
        player.Growth.SetBaseLevel(5);
        player.Growth.AllocatePoints(str: 14, mnd: 0, vit: 3);

        EquipmentEvaluationResult eval2 = player.EvaluateCurrentLoadout();
        log(player.BuildStatusLog());

        float effectiveAttack2 = EquipmentLoadoutEvaluator.CalculateEffectiveAttack(
            player.Growth.CurrentJob,
            GiantIronClub,
            eval2,
            log);

        log($"  実効攻撃力: {effectiveAttack2:F1}");

        WeightCapacityResult capacityBeforeWipe = EquipmentWeightCapacityCalculator.Calculate(player.Growth);

        AssertScenario(
            log,
            "成長後の適正装備",
            eval2.MeetsStatRequirement &&
            eval2.WithinWeightCapacity &&
            eval2.StatPenaltyMultiplier >= 1f &&
            effectiveAttack2 > effectiveAttack1 * 2f,
            $"キャパシティ {capacityBeforeWipe.MaxCapacity:F1}、ペナルティなし");

        // 3. タルコフ式ワイプ
        log(string.Empty);
        log("--- 3. TriggerSeasonalWipe() — すべて無に帰す ---");
        float capacityBefore = capacityBeforeWipe.MaxCapacity;
        int strBefore = player.Growth.GetFinalStats().STR;

        wipeSystem.TriggerSeasonalWipe(player, log);

        EquipmentEvaluationResult eval3 = player.EvaluateCurrentLoadout();
        WeightCapacityResult capacityAfter = EquipmentWeightCapacityCalculator.Calculate(player.Growth);

        log(player.BuildStatusLog());
        log(string.Empty);
        log($"  ワイプ前 STR {strBefore} → 後 {player.Growth.GetFinalStats().STR}");
        log($"  ワイプ前キャパシティ {capacityBefore:F1} → 後 {capacityAfter.MaxCapacity:F1}");
        log($"  装備武器: {(player.EquippedWeapon == null ? "なし（剥奪）" : player.EquippedWeapon.WeaponName)}");

        AssertScenario(
            log,
            "シーズンワイプ",
            player.Growth.BaseLevel == GrowthConstants.StartingLevel &&
            player.Growth.JobLevel == GrowthConstants.StartingLevel &&
            player.Growth.GetFinalStats().STR == GrowthConstants.InitialStatValue &&
            player.EquippedWeapon == null &&
            capacityAfter.MaxCapacity < capacityBefore * 0.5f,
            "すべて初期状態へ強制リセット");

        log(string.Empty);
        log("===== WeightAndWipeSystemTest 完了 =====");
    }

    private static void AssertScenario(Action<string> log, string label, bool condition, string detail)
    {
#if UNITY_5_3_OR_NEWER
        log(condition
            ? $"  ✓ [{label} OK] {detail}"
            : $"  ✗ [{label} NG] {detail}");
#else
        if (!condition)
        {
            throw new InvalidOperationException($"{label} failed: {detail}");
        }

        log($"  ✓ [{label} OK] {detail}");
#endif
    }

#if UNITY_5_3_OR_NEWER
    public static void RunInUnity()
    {
        RunAllScenarios(Debug.Log);
    }
#endif
}

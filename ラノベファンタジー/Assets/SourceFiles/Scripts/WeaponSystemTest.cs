using System;
using System.Collections.Generic;

#if UNITY_5_3_OR_NEWER
using UnityEngine;
#endif

// =============================================================================
// 武器種 × 戦闘ジョブ — 装備制限・ステータス補正（基礎ロジック）
// 単一ファイルでコピー可能。WeaponSystemTest.Main() または RunAllScenarios() で検証。
// =============================================================================

/// <summary>武器種（攻撃属性カテゴリ）。</summary>
public enum WeaponType
{
    Sword,  // 剣 / 斬属性
    Spear,  // 槍 / 突属性
    Stick,  // 棒 / 打属性
    Bow     // 弓 / 射属性
}

/// <summary>戦闘ジョブ。</summary>
public enum JobType
{
    Warrior, // 戦士
    Hunter,  // 狩人
    Priest,  // 僧侶
    Novice   // 初心者 / 無職
}

/// <summary>ジョブと武器種の相性カテゴリ（ログ・拡張用）。</summary>
public enum WeaponProficiencyTier
{
    SuperExpert,    // 超得意
    Expert,         // 得意
    Specialized,    // 特化
    Standard,       // 標準
    NovicePenalty,  // 未熟ペナルティ（全種装備可）
    Unequippable,   // 装備不可（扱いはできるが大幅ペナルティ）
    Forbidden       // 戒律不可（僧侶など）
}

/// <summary>武器の基礎データ。</summary>
public sealed class WeaponData
{
    public string WeaponName { get; }
    public WeaponType Type { get; }
    public float BaseAttack { get; }
    public float BaseStaminaCost { get; }
    /// <summary>装備に必要な最低ステータス（ゼロなら要求なし）。</summary>
    public CharacterStats RequiredStats { get; }
    /// <summary>装備重量。</summary>
    public float Weight { get; }

    public WeaponData(
        string weaponName,
        WeaponType type,
        float baseAttack,
        float baseStaminaCost,
        CharacterStats requiredStats = default,
        float weight = 0f)
    {
        WeaponName = weaponName ?? string.Empty;
        Type = type;
        BaseAttack = baseAttack;
        BaseStaminaCost = baseStaminaCost;
        RequiredStats = requiredStats;
        Weight = Math.Max(0f, weight);
    }
}

/// <summary>ジョブ×武器種に適用する倍率と警告情報。</summary>
public readonly struct WeaponProficiencyRule
{
    public WeaponProficiencyTier Tier { get; }
    public float DamageMultiplier { get; }
    public float StaminaMultiplier { get; }
    public string WarningMessage { get; }

    public WeaponProficiencyRule(
        WeaponProficiencyTier tier,
        float damageMultiplier,
        float staminaMultiplier,
        string warningMessage = null)
    {
        Tier = tier;
        DamageMultiplier = damageMultiplier;
        StaminaMultiplier = staminaMultiplier;
        WarningMessage = warningMessage;
    }

    public bool ShouldWarn => !string.IsNullOrEmpty(WarningMessage);
}

/// <summary>装備制限マトリクスの算出結果。</summary>
public readonly struct WeaponCombatResult
{
    public JobType Job { get; }
    public WeaponData Weapon { get; }
    public WeaponProficiencyRule AppliedRule { get; }
    public float FinalAttack { get; }
    public float FinalStaminaCost { get; }

    public WeaponCombatResult(
        JobType job,
        WeaponData weapon,
        WeaponProficiencyRule appliedRule,
        float finalAttack,
        float finalStaminaCost)
    {
        Job = job;
        Weapon = weapon;
        AppliedRule = appliedRule;
        FinalAttack = finalAttack;
        FinalStaminaCost = finalStaminaCost;
    }
}

/// <summary>倍率定数（マジックナンバー集約）。</summary>
internal static class WeaponProficiencyMultipliers
{
    public const float SuperExpertDamage = 1.3f;
    public const float SuperExpertStamina = 0.8f;

    public const float ExpertDamage = 1.2f;
    public const float ExpertStamina = 0.9f;

    public const float SpecializedDamage = 1.2f;
    public const float SpecializedStamina = 0.8f;

    public const float StandardDamage = 1.0f;
    public const float StandardStamina = 1.0f;

    public const float HunterSpearDamage = 1.1f;
    public const float HunterSpearStamina = 0.9f;
    public const float HunterSwordDamage = 0.9f;

    public const float UnequippableDamage = 0.5f;
    public const float WarriorBowStamina = 1.5f;
    public const float HunterStickStamina = 1.0f;

    public const float ForbiddenDamage = 0.0f;
    public const float ForbiddenStamina = 2.0f;

    public const float NoviceDamage = 0.8f;
    public const float NoviceStamina = 1.2f;
}

/// <summary>ジョブごとの武器種相性マトリクスを保持・提供します。</summary>
public static class JobWeaponProficiencyTable
{
    private static readonly IReadOnlyDictionary<JobType, IReadOnlyDictionary<WeaponType, WeaponProficiencyRule>> Table =
        BuildTable();

    public static WeaponProficiencyRule GetRule(JobType job, WeaponType weaponType)
    {
        if (Table.TryGetValue(job, out IReadOnlyDictionary<WeaponType, WeaponProficiencyRule> perWeapon) &&
            perWeapon.TryGetValue(weaponType, out WeaponProficiencyRule rule))
        {
            return rule;
        }

        throw new KeyNotFoundException($"未定義のジョブ×武器種: {job} × {weaponType}");
    }

    private static IReadOnlyDictionary<JobType, IReadOnlyDictionary<WeaponType, WeaponProficiencyRule>> BuildTable()
    {
        return new Dictionary<JobType, IReadOnlyDictionary<WeaponType, WeaponProficiencyRule>>
        {
            [JobType.Warrior] = new Dictionary<WeaponType, WeaponProficiencyRule>
            {
                [WeaponType.Sword] = Rule(
                    WeaponProficiencyTier.Expert,
                    WeaponProficiencyMultipliers.ExpertDamage,
                    WeaponProficiencyMultipliers.ExpertStamina),
                [WeaponType.Spear] = Rule(
                    WeaponProficiencyTier.Standard,
                    WeaponProficiencyMultipliers.StandardDamage,
                    WeaponProficiencyMultipliers.StandardStamina),
                [WeaponType.Stick] = Rule(
                    WeaponProficiencyTier.Standard,
                    WeaponProficiencyMultipliers.StandardDamage,
                    WeaponProficiencyMultipliers.StandardStamina),
                [WeaponType.Bow] = Rule(
                    WeaponProficiencyTier.Unequippable,
                    WeaponProficiencyMultipliers.UnequippableDamage,
                    WeaponProficiencyMultipliers.WarriorBowStamina,
                    "【警告】戦士は弓をまともに扱えません。装備は可能ですが性能が大幅に低下します。")
            },
            [JobType.Hunter] = new Dictionary<WeaponType, WeaponProficiencyRule>
            {
                [WeaponType.Bow] = Rule(
                    WeaponProficiencyTier.SuperExpert,
                    WeaponProficiencyMultipliers.SuperExpertDamage,
                    WeaponProficiencyMultipliers.SuperExpertStamina),
                [WeaponType.Spear] = Rule(
                    WeaponProficiencyTier.Standard,
                    WeaponProficiencyMultipliers.HunterSpearDamage,
                    WeaponProficiencyMultipliers.HunterSpearStamina),
                [WeaponType.Sword] = Rule(
                    WeaponProficiencyTier.Standard,
                    WeaponProficiencyMultipliers.HunterSwordDamage,
                    WeaponProficiencyMultipliers.StandardStamina),
                [WeaponType.Stick] = Rule(
                    WeaponProficiencyTier.Unequippable,
                    WeaponProficiencyMultipliers.UnequippableDamage,
                    WeaponProficiencyMultipliers.HunterStickStamina,
                    "【警告】狩人は棒をまともに扱えません。装備は可能ですがダメージが半減します。")
            },
            [JobType.Priest] = new Dictionary<WeaponType, WeaponProficiencyRule>
            {
                [WeaponType.Stick] = Rule(
                    WeaponProficiencyTier.Specialized,
                    WeaponProficiencyMultipliers.SpecializedDamage,
                    WeaponProficiencyMultipliers.SpecializedStamina),
                [WeaponType.Sword] = ForbiddenRule(),
                [WeaponType.Spear] = ForbiddenRule(),
                [WeaponType.Bow] = ForbiddenRule()
            },
            [JobType.Novice] = new Dictionary<WeaponType, WeaponProficiencyRule>
            {
                [WeaponType.Sword] = NoviceRule(),
                [WeaponType.Spear] = NoviceRule(),
                [WeaponType.Stick] = NoviceRule(),
                [WeaponType.Bow] = NoviceRule()
            }
        };
    }

    private static WeaponProficiencyRule Rule(
        WeaponProficiencyTier tier,
        float damageMultiplier,
        float staminaMultiplier,
        string warningMessage = null)
    {
        return new WeaponProficiencyRule(tier, damageMultiplier, staminaMultiplier, warningMessage);
    }

    private static WeaponProficiencyRule ForbiddenRule()
    {
        return Rule(
            WeaponProficiencyTier.Forbidden,
            WeaponProficiencyMultipliers.ForbiddenDamage,
            WeaponProficiencyMultipliers.ForbiddenStamina,
            "【警告】僧侶の戒律により、この武器は攻撃に使用できません（スタミナのみ浪費します）。");
    }

    private static WeaponProficiencyRule NoviceRule()
    {
        return Rule(
            WeaponProficiencyTier.NovicePenalty,
            WeaponProficiencyMultipliers.NoviceDamage,
            WeaponProficiencyMultipliers.NoviceStamina);
    }
}

/// <summary>
/// ジョブと武器データから最終攻撃力・最終スタミナ消費を算出します。
/// </summary>
public static class WeaponRestrictionSystem
{
    public static WeaponCombatResult Calculate(
        JobType job,
        WeaponData weapon,
        Action<string> warningLogger = null)
    {
        if (weapon == null)
        {
            throw new ArgumentNullException(nameof(weapon));
        }

        WeaponProficiencyRule rule = JobWeaponProficiencyTable.GetRule(job, weapon.Type);

        if (rule.ShouldWarn)
        {
            warningLogger?.Invoke(rule.WarningMessage);
        }

        float finalAttack = weapon.BaseAttack * rule.DamageMultiplier;
        float finalStamina = weapon.BaseStaminaCost * rule.StaminaMultiplier;

        return new WeaponCombatResult(job, weapon, rule, finalAttack, finalStamina);
    }
}

/// <summary>モック実行・シナリオ検証用エントリポイント。</summary>
public static class WeaponSystemTest
{
    private static readonly WeaponData IronSword = new WeaponData("鉄の剣", WeaponType.Sword, 100f, 20f);
    private static readonly WeaponData HunterBow = new WeaponData("狩人の弓", WeaponType.Bow, 90f, 18f);
    private static readonly WeaponData PriestStaff = new WeaponData("僧侶の棒", WeaponType.Stick, 70f, 15f);
    private static readonly WeaponData WoodenSpear = new WeaponData("木の槍", WeaponType.Spear, 85f, 22f);
    private static readonly WeaponData CrudeClub = new WeaponData("粗末な棒", WeaponType.Stick, 60f, 16f);

    /// <summary>コンソールアプリ等からの検証用エントリポイント。</summary>
    public static void Main()
    {
        RunAllScenarios(Console.WriteLine);
    }

    /// <summary>任意のログ出力先で全シナリオを実行します。</summary>
    public static void RunAllScenarios(Action<string> log)
    {
        if (log == null)
        {
            log = _ => { };
        }

        log("===== WeaponSystemTest 開始 =====");

        RunScenario(log, "戦士 × 剣（得意）", JobType.Warrior, IronSword);
        RunScenario(log, "戦士 × 弓（装備不可）", JobType.Warrior, HunterBow);
        RunScenario(log, "僧侶 × 剣（戒律不可）", JobType.Priest, IronSword);
        RunScenario(log, "僧侶 × 棒（特化）", JobType.Priest, PriestStaff);
        RunScenario(log, "無職 × 弓（未熟ペナルティ）", JobType.Novice, HunterBow);
        RunScenario(log, "狩人 × 弓（超得意）", JobType.Hunter, HunterBow);
        RunScenario(log, "狩人 × 棒（装備不可）", JobType.Hunter, CrudeClub);
        RunScenario(log, "狩人 × 槍（標準寄り）", JobType.Hunter, WoodenSpear);

        log("===== WeaponSystemTest 完了 =====");
    }

    private static void RunScenario(Action<string> log, string label, JobType job, WeaponData weapon)
    {
        log(string.Empty);
        log($"--- {label} ---");

        WeaponCombatResult result = WeaponRestrictionSystem.Calculate(
            job,
            weapon,
            warning => log($"  ! {warning}"));

        log($"  ジョブ: {job} / 武器: {weapon.WeaponName} ({weapon.Type})");
        log($"  相性: {result.AppliedRule.Tier}");
        log($"  基礎攻撃 {weapon.BaseAttack:F1} → 最終攻撃 {result.FinalAttack:F1} " +
            $"(×{result.AppliedRule.DamageMultiplier:F1})");
        log($"  基礎スタミナ {weapon.BaseStaminaCost:F1} → 最終スタミナ {result.FinalStaminaCost:F1} " +
            $"(×{result.AppliedRule.StaminaMultiplier:F1})");
    }

#if UNITY_5_3_OR_NEWER
    /// <summary>Unity Play モードから Debug.Log で検証します。</summary>
    public static void RunInUnity()
    {
        RunAllScenarios(Debug.Log);
    }
#endif
}

#if UNITY_5_3_OR_NEWER
/// <summary>シーンに配置して ContextMenu または Play 時に武器システムテストを実行します。</summary>
public class WeaponSystemTestRunner : MonoBehaviour
{
    [SerializeField] private bool runOnStart;

    private void Start()
    {
        if (runOnStart)
        {
            WeaponSystemTest.RunInUnity();
        }
    }

    [ContextMenu("Run Weapon System Tests")]
    private void RunFromContextMenu()
    {
        WeaponSystemTest.RunInUnity();
    }
}
#endif

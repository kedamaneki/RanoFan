using System;
using System.Collections.Generic;

#if UNITY_5_3_OR_NEWER
using UnityEngine;
#endif

// =============================================================================
// 敵部位・肉質（属性耐性）× 部位破壊 — 基礎ロジック
// 単一ファイルでコピー可能。EnemyPartSystemTest.Main() でモック検証。
// プレイヤー側補正は WeaponRestrictionSystem（武器×ジョブ）を継承利用します。
// =============================================================================

/// <summary>攻撃属性（武器種から派生。将来は属性魔法が直接指定することも想定）。</summary>
public enum AttackAttribute
{
    Slash,  // 斬
    Strike, // 打
    Thrust, // 突
    Pierce  // 射
}

/// <summary>部位破壊時の肉質軟化ボーナスなど、部位破壊まわりの定数。</summary>
public static class PartBreakConstants
{
    /// <summary>部位破壊後、全肉質倍率に加算される値（装甲剥離の表現）。</summary>
    public const float FleshSofteningBonus = 0.5f;
}

/// <summary>
/// 武器種と攻撃属性の対応表。
/// 開放閉鎖原則: 新武器種の追加はここへのマッピング登録のみで済む想定です。
/// </summary>
public static class WeaponAttributeMapper
{
    private static readonly IReadOnlyDictionary<WeaponType, AttackAttribute> Mapping =
        new Dictionary<WeaponType, AttackAttribute>
        {
            [WeaponType.Sword] = AttackAttribute.Slash,
            [WeaponType.Stick] = AttackAttribute.Strike,
            [WeaponType.Spear] = AttackAttribute.Thrust,
            [WeaponType.Bow] = AttackAttribute.Pierce
        };

    public static AttackAttribute FromWeaponType(WeaponType weaponType)
    {
        if (Mapping.TryGetValue(weaponType, out AttackAttribute attribute))
        {
            return attribute;
        }

        throw new KeyNotFoundException($"未定義の武器種→属性マッピング: {weaponType}");
    }

    public static string GetDisplayName(AttackAttribute attribute)
    {
        switch (attribute)
        {
            case AttackAttribute.Slash: return "斬";
            case AttackAttribute.Strike: return "打";
            case AttackAttribute.Thrust: return "突";
            case AttackAttribute.Pierce: return "射";
            default: return attribute.ToString();
        }
    }
}

/// <summary>
/// 部位ごとの肉質（属性耐性）倍率。
/// 将来の属性魔法は <see cref="IAttackAttributeSource"/> 経由で属性を渡す拡張を想定しています。
/// </summary>
public interface IAttackAttributeSource
{
    AttackAttribute AttackAttribute { get; }
}

/// <summary>肉質（装甲）倍率の集合。部位破壊で軟化します。</summary>
public sealed class FleshResistance
{
    public float Slash { get; private set; }
    public float Strike { get; private set; }
    public float Thrust { get; private set; }
    public float Pierce { get; private set; }

    public FleshResistance(float slash, float strike, float thrust, float pierce)
    {
        Slash = slash;
        Strike = strike;
        Thrust = thrust;
        Pierce = pierce;
    }

    public float GetMultiplier(AttackAttribute attribute)
    {
        switch (attribute)
        {
            case AttackAttribute.Slash: return Slash;
            case AttackAttribute.Strike: return Strike;
            case AttackAttribute.Thrust: return Thrust;
            case AttackAttribute.Pierce: return Pierce;
            default:
                throw new ArgumentOutOfRangeException(nameof(attribute), attribute, null);
        }
    }

    /// <summary>部位破壊後: 全属性の肉質倍率を一律加算（装甲剥離）。</summary>
    public void ApplyPartBreakSoftening(float bonus = PartBreakConstants.FleshSofteningBonus)
    {
        Slash += bonus;
        Strike += bonus;
        Thrust += bonus;
        Pierce += bonus;
    }

    public void SetAll(float slash, float strike, float thrust, float pierce)
    {
        Slash = slash;
        Strike = strike;
        Thrust = thrust;
        Pierce = pierce;
    }

    public FleshResistance Clone()
    {
        return new FleshResistance(Slash, Strike, Thrust, Pierce);
    }
}

/// <summary>
/// 部位破壊時に敵 AI へ伝える将来用フラグ（行動変化・よろけ等）。
/// 現段階はデータ保持のみ。CombatSimulator が破壊時に生成します。
/// </summary>
public sealed class PartBreakSideEffects
{
    public bool StaggerTriggered { get; set; } = true;
    public bool ExposeCoreWeakPoint { get; set; }
    public bool DisablePartAttack { get; set; }
}

/// <summary>ダメージ適用の結果（部位破壊の有無を含む）。</summary>
public readonly struct PartDamageResult
{
    public float DamageApplied { get; }
    public float DurabilityBefore { get; }
    public float DurabilityAfter { get; }
    public bool PartDestroyedThisHit { get; }
    public PartBreakSideEffects BreakSideEffects { get; }

    public PartDamageResult(
        float damageApplied,
        float durabilityBefore,
        float durabilityAfter,
        bool partDestroyedThisHit,
        PartBreakSideEffects breakSideEffects)
    {
        DamageApplied = damageApplied;
        DurabilityBefore = durabilityBefore;
        DurabilityAfter = durabilityAfter;
        PartDestroyedThisHit = partDestroyedThisHit;
        BreakSideEffects = breakSideEffects;
    }
}

/// <summary>大型敵の攻撃対象となる部位。</summary>
public sealed class EnemyPart
{
    public string PartName { get; }
    public FleshResistance Flesh { get; }
    public float MaxDurability { get; }
    public float CurrentDurability { get; private set; }
    public bool IsDestroyed { get; private set; }

    /// <summary>直近の部位破壊で発生した副作用（未破壊なら null）。</summary>
    public PartBreakSideEffects LastBreakSideEffects { get; private set; }

    public EnemyPart(string partName, FleshResistance flesh, float maxDurability)
    {
        PartName = partName ?? string.Empty;
        Flesh = flesh ?? throw new ArgumentNullException(nameof(flesh));
        MaxDurability = Math.Max(0f, maxDurability);
        CurrentDurability = MaxDurability;
        IsDestroyed = false;
    }

    public float GetFleshMultiplier(AttackAttribute attribute)
    {
        return Flesh.GetMultiplier(attribute);
    }

    /// <summary>ダメージを適用し、耐久が 0 以下なら部位破壊を処理します。</summary>
    public PartDamageResult ReceiveDamage(float damage, Action<string> log = null)
    {
        float appliedDamage = Math.Max(0f, damage);
        float durabilityBefore = CurrentDurability;

        CurrentDurability = Math.Max(0f, CurrentDurability - appliedDamage);

        if (!IsDestroyed && CurrentDurability <= 0f)
        {
            IsDestroyed = true;
            Flesh.ApplyPartBreakSoftening(PartBreakConstants.FleshSofteningBonus);

            LastBreakSideEffects = new PartBreakSideEffects
            {
                StaggerTriggered = true,
                ExposeCoreWeakPoint = true,
                DisablePartAttack = true
            };

            log?.Invoke(
                $"  ★ 部位破壊発生！ [{PartName}] の装甲が剥がれ、全肉質倍率 +{PartBreakConstants.FleshSofteningBonus:F1}");

            return new PartDamageResult(
                appliedDamage,
                durabilityBefore,
                CurrentDurability,
                true,
                LastBreakSideEffects);
        }

        return new PartDamageResult(
            appliedDamage,
            durabilityBefore,
            CurrentDurability,
            false,
            null);
    }

    /// <summary>テスト用: 部位状態を初期化します。</summary>
    public void ResetForTesting(FleshResistance freshFlesh)
    {
        CurrentDurability = MaxDurability;
        IsDestroyed = false;
        LastBreakSideEffects = null;

        Flesh.SetAll(freshFlesh.Slash, freshFlesh.Strike, freshFlesh.Thrust, freshFlesh.Pierce);
    }
}

/// <summary>CombatSimulator 1 回分の攻撃結果。</summary>
public readonly struct PartCombatResult
{
    public JobType Job { get; }
    public WeaponData Weapon { get; }
    public EnemyPart TargetPart { get; }
    public WeaponCombatResult PlayerSide { get; }
    public AttackAttribute AttackAttribute { get; }
    public float FleshMultiplier { get; }
    public float FinalDamage { get; }
    public PartDamageResult PartDamage { get; }

    public PartCombatResult(
        JobType job,
        WeaponData weapon,
        EnemyPart targetPart,
        WeaponCombatResult playerSide,
        AttackAttribute attackAttribute,
        float fleshMultiplier,
        float finalDamage,
        PartDamageResult partDamage)
    {
        Job = job;
        Weapon = weapon;
        TargetPart = targetPart;
        PlayerSide = playerSide;
        AttackAttribute = attackAttribute;
        FleshMultiplier = fleshMultiplier;
        FinalDamage = finalDamage;
        PartDamage = partDamage;
    }
}

/// <summary>
/// ジョブ×武器×部位を統合した戦闘シミュレーター。
/// 単一責任: ダメージ計算フローのオーケストレーションのみを担当します。
/// </summary>
public static class CombatSimulator
{
    /// <summary>
    /// 1. プレイヤー側補正 → 2. 肉質補正 → 3. 耐久減算 → 4. 部位破壊判定
    /// </summary>
    public static PartCombatResult ExecuteAttack(
        JobType job,
        WeaponData weapon,
        EnemyPart targetPart,
        Action<string> log = null,
        Action<string> warningLogger = null)
    {
        if (weapon == null)
        {
            throw new ArgumentNullException(nameof(weapon));
        }

        if (targetPart == null)
        {
            throw new ArgumentNullException(nameof(targetPart));
        }

        log?.Invoke(string.Empty);
        log?.Invoke($"▶ 攻撃: [{job}] {weapon.WeaponName} → 部位 [{targetPart.PartName}]");

        // Step 1: プレイヤー側（ジョブ×武器相性）
        WeaponCombatResult playerSide = WeaponRestrictionSystem.Calculate(job, weapon, warningLogger);

        log?.Invoke(
            $"  [Step1 プレイヤー補正] 基礎攻撃 {weapon.BaseAttack:F1} → 最終攻撃 {playerSide.FinalAttack:F1} " +
            $"(ジョブ相性 ×{playerSide.AppliedRule.DamageMultiplier:F1}, {playerSide.AppliedRule.Tier})");

        // Step 2: 敵肉質・装甲補正
        AttackAttribute attribute = WeaponAttributeMapper.FromWeaponType(weapon.Type);
        float fleshMultiplier = targetPart.GetFleshMultiplier(attribute);
        float finalDamage = playerSide.FinalAttack * fleshMultiplier;

        string breakNote = targetPart.IsDestroyed ? "（部位破壊済み・軟化後肉質）" : string.Empty;
        log?.Invoke(
            $"  [Step2 肉質補正] 属性={WeaponAttributeMapper.GetDisplayName(attribute)} " +
            $"倍率 ×{fleshMultiplier:F1}{breakNote} → 最終ダメージ {finalDamage:F1}");

        // Step 3 & 4: 耐久減算と部位破壊
        PartDamageResult partDamage = targetPart.ReceiveDamage(finalDamage, log);

        log?.Invoke(
            $"  [Step3 耐久] {partDamage.DurabilityBefore:F1} → {partDamage.DurabilityAfter:F1} " +
            $"/ {targetPart.MaxDurability:F1}" +
            (partDamage.PartDestroyedThisHit ? "  ← 破壊！" : string.Empty));

        if (targetPart.IsDestroyed && !partDamage.PartDestroyedThisHit)
        {
            log?.Invoke(
                $"  [肉質] 破壊後 Slash={targetPart.Flesh.Slash:F1}x / Strike={targetPart.Flesh.Strike:F1}x / " +
                $"Thrust={targetPart.Flesh.Thrust:F1}x / Pierce={targetPart.Flesh.Pierce:F1}x");
        }

        return new PartCombatResult(
            job,
            weapon,
            targetPart,
            playerSide,
            attribute,
            fleshMultiplier,
            finalDamage,
            partDamage);
    }
}

#if UNITY_EDITOR

/// <summary>モック実行・ボス攻略シナリオ検証用エントリポイント（エディタ専用）。</summary>
public static class EnemyPartSystemTest
{
    private static readonly WeaponData IronSword = new WeaponData("鉄の剣", WeaponType.Sword, 100f, 20f);
    private static readonly WeaponData PriestStaff = new WeaponData("僧侶の棒", WeaponType.Stick, 70f, 15f);

    /// <summary>コンソールアプリ等からの検証用エントリポイント。</summary>
    public static void Main()
    {
        RunRockArmorBehemothScenario(Console.WriteLine);
    }

    /// <summary>全シナリオ（現状は岩甲の巨獣のみ）を実行します。</summary>
    public static void RunAllScenarios(Action<string> log)
    {
        if (log == null)
        {
            log = _ => { };
        }

        RunRockArmorBehemothScenario(log);
    }

    /// <summary>
    /// 大型ボス「岩甲の巨獣」— 装甲前脚を狙うソウルライク攻略シナリオ。
    /// 戦士の斬撃は弾かれる → 僧侶の打撃で破壊 → 剥がれた装甲に斬が通る。
    /// </summary>
    public static void RunRockArmorBehemothScenario(Action<string> log)
    {
        if (log == null)
        {
            log = _ => { };
        }

        log("===== EnemyPartSystemTest: 岩甲の巨獣 =====");

        FleshResistance frontLegFleshTemplate = new FleshResistance(
            slash: 0.3f,
            strike: 1.5f,
            thrust: 0.5f,
            pierce: 0.3f);

        EnemyPart armoredFrontLeg = new EnemyPart(
            "装甲前脚",
            frontLegFleshTemplate.Clone(),
            maxDurability: 150f);

        log($"ボス部位: {armoredFrontLeg.PartName}（耐久 {armoredFrontLeg.MaxDurability:F0}）");
        log($"初期肉質: Slash={armoredFrontLeg.Flesh.Slash:F1}x Strike={armoredFrontLeg.Flesh.Strike:F1}x " +
            $"Thrust={armoredFrontLeg.Flesh.Thrust:F1}x Pierce={armoredFrontLeg.Flesh.Pierce:F1}x");

        // 検証1: 戦士 × 剣（斬）— 装甲に弾かれ微ダメ
        log(string.Empty);
        log("--- 検証1: 戦士が剣（斬）で装甲前脚を攻撃（装甲健在） ---");
        PartCombatResult verify1 = CombatSimulator.ExecuteAttack(
            JobType.Warrior,
            IronSword,
            armoredFrontLeg,
            log,
            warning => log($"  ※ {warning}"));

        AssertScenario(
            log,
            "検証1",
            verify1.FinalDamage < 50f && !verify1.PartDamage.PartDestroyedThisHit,
            $"斬属性は装甲に弾かれる想定（最終ダメージ {verify1.FinalDamage:F1}、残耐久 {armoredFrontLeg.CurrentDurability:F1}）");

        // 検証2: 僧侶 × 棒（打）— 弱点に大ダメージ、部位破壊
        // 検証1後の残耐久 114 に対し、打撃 126 で一撃破壊（僧侶の特化棒術 × 打撃弱点）
        log(string.Empty);
        log("--- 検証2: 僧侶が棒（打）で装甲前脚を攻撃（打撃弱点） ---");
        PartCombatResult verify2 = CombatSimulator.ExecuteAttack(
            JobType.Priest,
            PriestStaff,
            armoredFrontLeg,
            log,
            warning => log($"  ※ {warning}"));

        AssertScenario(
            log,
            "検証2",
            verify2.PartDamage.PartDestroyedThisHit && armoredFrontLeg.IsDestroyed,
            $"打撃弱点で大ダメージ＆部位破壊（最終ダメージ {verify2.FinalDamage:F1}）");

        // 検証3: 部位破壊後、戦士 × 剣 — Slash 0.3→0.8 に上昇し通りやすく
        log(string.Empty);
        log("--- 検証3: 部位破壊後、戦士が剣（斬）で同部位を再攻撃（装甲剥離） ---");
        PartCombatResult verify3 = CombatSimulator.ExecuteAttack(
            JobType.Warrior,
            IronSword,
            armoredFrontLeg,
            log,
            warning => log($"  ※ {warning}"));

        float expectedSlashMult = 0.3f + PartBreakConstants.FleshSofteningBonus;
        AssertScenario(
            log,
            "検証3",
            Approximately(verify3.FleshMultiplier, expectedSlashMult) &&
            verify3.FinalDamage > verify1.FinalDamage,
            $"Slash 倍率 {verify3.FleshMultiplier:F1}x（剥離後）、" +
            $"ダメージ {verify1.FinalDamage:F1} → {verify3.FinalDamage:F1} に上昇");

        log(string.Empty);
        log("===== EnemyPartSystemTest 完了 =====");
    }

    private static void AssertScenario(Action<string> log, string label, bool condition, string detail)
    {
#if UNITY_5_3_OR_NEWER
        if (condition)
        {
            log($"  ✓ [{label} OK] {detail}");
        }
        else
        {
            log($"  ✗ [{label} NG] {detail}");
        }
#else
        if (!condition)
        {
            throw new InvalidOperationException($"{label} failed: {detail}");
        }

        log($"  ✓ [{label} OK] {detail}");
#endif
    }

    private static bool Approximately(float a, float b)
    {
#if UNITY_5_3_OR_NEWER
        return UnityEngine.Mathf.Approximately(a, b);
#else
        return Math.Abs(a - b) < 0.001f;
#endif
    }

    /// <summary>Unity Play モードから Debug.Log で検証します。</summary>
    public static void RunInUnity()
    {
        RunAllScenarios(Debug.Log);
    }
}

#endif

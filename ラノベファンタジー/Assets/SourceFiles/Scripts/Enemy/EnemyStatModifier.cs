using UnityEngine;

// =============================================================================
// 魔物ステータス補正 — 4種プロファイル + 部位素材 + 時代 MP 補正
// 連携: CombatStats / EquipmentStatFeedback / EraContextResolver
// =============================================================================

/// <summary>純粋魔獣の生態プロファイル（人型亜種なし）。</summary>
public enum EnemyEcologyProfileKind
{
    Slime = 0,
    Stalker = 1,
    Titan = 2,
    Dancer = 3
}

/// <summary>プロファイルごとの乗算倍率。</summary>
public readonly struct EnemyProfileMultipliers
{
    public readonly float HpMultiplier;
    public readonly float StrMultiplier;
    public readonly float DefMultiplier;
    public readonly float ManaMultiplier;
    public readonly float StaminaMultiplier;

    public EnemyProfileMultipliers(float hp, float str, float def, float mana, float stamina)
    {
        HpMultiplier = SafeMul(hp);
        StrMultiplier = SafeMul(str);
        DefMultiplier = SafeMul(def);
        ManaMultiplier = SafeMul(mana);
        StaminaMultiplier = SafeMul(stamina);
    }

    public static EnemyProfileMultipliers Identity => new EnemyProfileMultipliers(1f, 1f, 1f, 1f, 1f);

    public StatModifiers ToStatModifiers()
    {
        return new StatModifiers
        {
            hpMultiplier = HpMultiplier,
            strMultiplier = StrMultiplier,
            defMultiplier = DefMultiplier,
            manaMultiplier = ManaMultiplier
        };
    }

    private static float SafeMul(float value)
    {
        if (value <= 0f || float.IsNaN(value) || float.IsInfinity(value))
        {
            return 1f;
        }

        return value;
    }
}

/// <summary>
/// 魔物へプロファイル乗算・部位 Purity/Density・時代 MP 消費補正を適用します。
/// </summary>
public static class EnemyStatModifier
{
    /// <summary>生態プロファイルを攻撃コンボ JSON の PATTERN_* へ対応付けます。</summary>
    public static string ResolveBehaviorPatternId(EnemyEcologyProfileKind kind)
    {
        switch (kind)
        {
            case EnemyEcologyProfileKind.Stalker:
                return "PATTERN_AGILE_STALKER";
            case EnemyEcologyProfileKind.Titan:
                return "PATTERN_HEAVY_TITAN";
            case EnemyEcologyProfileKind.Dancer:
                return "PATTERN_FEINT_DANCER";
            default:
                return "PATTERN_BASIC_SLIME";
        }
    }

    /// <summary>時代補正を掛けた後の最終 MP 消費倍率（1.0〜2.5 帯をクランプ）。</summary>
    public static float CurrentEraMpCostMultiplier
    {
        get
        {
            try
            {
                EraTag era = EraContextResolver.CurrentEra;
                switch (era)
                {
                    case EraTag.Mid:
                        float midT = Mathf.InverseLerp(
                            EraContextResolver.EarlyEraMaxTurn + 1,
                            EraContextResolver.MidEraMaxTurn,
                            EraContextResolver.CurrentTurn);
                        return Mathf.Clamp(Mathf.Lerp(1.05f, 1.45f, midT), 1f, 2.5f);
                    case EraTag.Late:
                        return Mathf.Clamp(EraContextResolver.ResolveAncientMultiplier(0f), 1.5f, 2.5f);
                    default:
                        return 1f;
                }
            }
            catch (System.Exception)
            {
                return 1f;
            }
        }
    }

    public static EnemyProfileMultipliers GetProfile(EnemyEcologyProfileKind kind)
    {
        switch (kind)
        {
            case EnemyEcologyProfileKind.Stalker:
                // 機動・低耐久・中魔力
                return new EnemyProfileMultipliers(0.85f, 1.15f, 0.8f, 1.1f, 1.25f);
            case EnemyEcologyProfileKind.Titan:
                // 高耐久・高STR・低スタミナ回転
                return new EnemyProfileMultipliers(1.55f, 1.4f, 1.35f, 0.85f, 0.75f);
            case EnemyEcologyProfileKind.Dancer:
                // フェイント型・中HP・高MP・高スタミナ
                return new EnemyProfileMultipliers(0.95f, 1.05f, 0.9f, 1.35f, 1.4f);
            default:
                // Slime: 粘性・均衡
                return new EnemyProfileMultipliers(1.1f, 0.95f, 1.05f, 1.0f, 1.0f);
        }
    }

    public static EnemyEcologyProfileKind ParseKind(string patternOrKind)
    {
        if (string.IsNullOrWhiteSpace(patternOrKind))
        {
            return EnemyEcologyProfileKind.Slime;
        }

        string key = patternOrKind.Trim().ToUpperInvariant();
        if (key.Contains("STALKER") || key.Contains("AGILE"))
        {
            return EnemyEcologyProfileKind.Stalker;
        }

        if (key.Contains("TITAN") || key.Contains("HEAVY"))
        {
            return EnemyEcologyProfileKind.Titan;
        }

        if (key.Contains("DANCER") || key.Contains("FEINT"))
        {
            return EnemyEcologyProfileKind.Dancer;
        }

        if (key.Contains("SLIME") || key == "SLIME")
        {
            return EnemyEcologyProfileKind.Slime;
        }

        return EnemyEcologyProfileKind.Slime;
    }

    /// <summary>ブレス／魔法の基礎 MP コストへ時代倍率を掛けます。</summary>
    public static float ScaleMpCost(float baseCost)
    {
        if (baseCost <= 0f || float.IsNaN(baseCost))
        {
            return 0f;
        }

        return baseCost * CurrentEraMpCostMultiplier;
    }

    /// <summary>EnemyStatusManager 経由で CombatStats へ全補正を適用します。</summary>
    public static void ApplyToEnemy(EnemyStatusManager enemy, bool log)
    {
        if (enemy == null)
        {
            Debug.LogWarning("[EnemyStatModifier] enemy が null — 補正をスキップ（Safe-Fail）。");
            return;
        }

        enemy.EnsureDefaults();
        CombatStats combat = enemy.Combat;
        EnemyActionStats action = enemy.Action;
        EnemyProfileMultipliers profile = GetProfile(enemy.ProfileKind);

        if (combat != null)
        {
            try
            {
                combat.ApplyJobStatMultipliers(profile.ToStatModifiers());
                combat.ApplyMaterialModifiers(enemy.PartPurity, enemy.PartDensity, enemy.DisplayName + "_Core");
            }
            catch (System.Exception exception)
            {
                Debug.LogWarning($"[EnemyStatModifier] CombatStats 補正 Safe-Fail: {exception.Message}");
            }
        }

        enemy.ApplyManaMultiplier(1f);

        if (action != null)
        {
            action.EnsureDefaults();
            float targetMax = EnemyActionStats.DefaultMaxStamina * profile.StaminaMultiplier;
            action.SetMaxStamina(targetMax, refillRatio: true);
            action.SetRegenRate(EnemyActionStats.DefaultRegenRate * Mathf.Lerp(0.85f, 1.2f, profile.StaminaMultiplier / 1.4f));
        }

        if (log)
        {
            Debug.Log(
                $"<color=#80CBC4><b>【魔物補正】</b></color> {enemy.DisplayName} {enemy.ProfileKind} " +
                $"HP×{profile.HpMultiplier:F2} STR×{profile.StrMultiplier:F2} DEF×{profile.DefMultiplier:F2} " +
                $"MP×{profile.ManaMultiplier:F2} Stam×{profile.StaminaMultiplier:F2} " +
                $"時代MP消費×{CurrentEraMpCostMultiplier:F2} " +
                $"部位 P{enemy.PartPurity:F0}/D{enemy.PartDensity:F0}");
        }
    }
}

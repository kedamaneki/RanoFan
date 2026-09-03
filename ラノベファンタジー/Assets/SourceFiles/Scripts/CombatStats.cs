using System;
using UnityEngine;

/// <summary>
/// プレイヤー・敵で共通の戦闘ステータス（HP / STR / DEF）。
/// 対称型のダメージ計算を IDamageable として提供します。
/// </summary>
public class CombatStats : MonoBehaviour, IDamageable
{
    public enum CombatFaction
    {
        Player,
        Enemy
    }

    [Header("戦闘ステータス")]
    [SerializeField] private int maxHp = 100;
    [SerializeField] private int currentHp;
    [SerializeField] private int strength = 10;
    [SerializeField] private int defense = 2;

    [Header("区分")]
    [Tooltip("プレイヤーか敵か（行動ログの振り分けに使用）")]
    [SerializeField] private CombatFaction faction = CombatFaction.Enemy;

    // ジョブ補正の乗算元（初回キャプチャ後は不変）
    private bool baseStatsCaptured;
    private int baseMaxHp;
    private int baseStrength;
    private int baseDefense;

    private int equipmentStrengthBonus;
    private float equipmentPoiseDamageMultiplier = 1f;
    private float equipmentStaminaCostMultiplier = 1f;

    public int MaxHp => maxHp;
    public int CurrentHp => currentHp;
    public int Strength => strength;
    public int Defense => defense;
    public bool IsAlive => currentHp > 0;

    /// <summary>装備還元の体勢削り倍率（欠損時 1.0）。</summary>
    public float EquipmentPoiseDamageMultiplier => Mathf.Max(0.01f, equipmentPoiseDamageMultiplier);

    /// <summary>装備還元の攻撃スタミナ倍率（欠損時 1.0）。</summary>
    public float EquipmentStaminaCostMultiplier => Mathf.Max(0.01f, equipmentStaminaCostMultiplier);

    /// <summary>直近の装備 STR 加算（ログ・検証用）。</summary>
    public int EquipmentStrengthBonus => equipmentStrengthBonus;

    /// <summary>ダメージを受けたとき（実ダメージ量）</summary>
    public event Action<int> OnDamaged;

    /// <summary>HP が 0 になったとき</summary>
    public event Action OnDied;

    private void Awake()
    {
        currentHp = maxHp;
    }

    /// <summary>
    /// 攻撃側 STR を基準にダメージを受ける（共通ルール）。
    /// </summary>
    public int ReceiveDamage(int attackerStrength)
    {
        if (!IsAlive)
        {
            return 0;
        }

        // プレイヤーが無敵状態（派生回避技など）なら被弾しない
        if (faction == CombatFaction.Player)
        {
            IInvincibilitySource invincibility = GetComponent<IInvincibilitySource>();
            if (invincibility != null && invincibility.IsInvincible)
            {
                return 0;
            }
        }

        int damage = DamageCalculator.Calculate(attackerStrength, defense);
        currentHp = Mathf.Max(0, currentHp - damage);

        OnDamaged?.Invoke(damage);
        LogDamaged(damage);

        if (!IsAlive)
        {
            OnDied?.Invoke();
            LogDeath();
        }

        return damage;
    }

    /// <summary>HP を全回復</summary>
    public void FullHeal()
    {
        currentHp = maxHp;
    }

    /// <summary>自動テスト用：HP を直接設定（InspirationSystemTester 専用）</summary>
    public void SetHpForTesting(int hp)
    {
        currentHp = Mathf.Clamp(hp, 0, maxHp);
    }

    /// <summary>AI 基礎スキルの strength 系ボーナスを反映します。</summary>
    public void ApplyStrengthBonus(int amount)
    {
        strength = Mathf.Max(0, strength + amount);
    }

    /// <summary>AI 基礎スキルの defense 系ボーナスを反映します。</summary>
    public void ApplyDefenseBonus(int amount)
    {
        defense = Mathf.Max(0, defense + amount);
    }

    /// <summary>特殊効果（Arts / Psychic）による HP 回復。</summary>
    public void RestoreHp(int amount)
    {
        if (amount <= 0)
        {
            return;
        }

        currentHp = Mathf.Min(maxHp, currentHp + amount);
    }

    /// <summary>ジョブ補正適用前に、現在値を基礎ステータスとして記録します。</summary>
    public void CaptureBaseStatsIfNeeded()
    {
        if (baseStatsCaptured)
        {
            return;
        }

        baseMaxHp = maxHp;
        baseStrength = strength;
        baseDefense = defense;
        baseStatsCaptured = true;
    }

    /// <summary>
    /// JobMasterData.statModifiers を基礎値へ乗算し、戦闘用実数値を更新します。
    /// modifiers が null の場合は補正なし（1.0倍）として扱います。
    /// </summary>
    public void ApplyJobStatMultipliers(StatModifiers modifiers)
    {
        CaptureBaseStatsIfNeeded();

        StatModifiers safe = modifiers ?? JobStatApplier.IdentityModifiers;
        maxHp = Mathf.Max(1, Mathf.RoundToInt(baseMaxHp * safe.hpMultiplier));
        strength = Mathf.Max(0, Mathf.RoundToInt(baseStrength * safe.strMultiplier));
        defense = Mathf.Max(0, Mathf.RoundToInt(baseDefense * safe.defMultiplier));
        currentHp = Mathf.Min(currentHp, maxHp);

        // ジョブ再計算は STR を基礎から作り直すため、装備加算は呼び出し側で ApplyEquipmentModifiers し直す
        equipmentStrengthBonus = 0;
        equipmentPoiseDamageMultiplier = 1f;
        equipmentStaminaCostMultiplier = 1f;
    }

    /// <summary>
    /// 魔物部位などの生 Purity / Density を戦闘ステータスへ還元します（プレイヤー装備式と同一）。
    /// 欠損・NaN は恒等へ Safe-Fail します。ジョブ／プロファイル補正の後に呼んでください。
    /// </summary>
    public void ApplyMaterialModifiers(float purity, float density, string partLabel = "Material")
    {
        strength = Mathf.Max(0, strength - equipmentStrengthBonus);
        equipmentStrengthBonus = 0;
        equipmentPoiseDamageMultiplier = 1f;
        equipmentStaminaCostMultiplier = 1f;

        EquipmentModifierValues values = EquipmentStatFeedback.Calculate(
            partLabel ?? "Material",
            true,
            purity,
            density);
        if (!values.Applied)
        {
            Debug.Log(
                $"<color=#90A4AE>【部位還元】{partLabel} 欠損 -> 補正なし " +
                $"(STR×1.0 / 体勢×1.0) 現在STR={strength}</color>");
            return;
        }

        equipmentStrengthBonus = values.StrengthBonus;
        equipmentPoiseDamageMultiplier = values.PoiseDamageMultiplier;
        equipmentStaminaCostMultiplier = values.StaminaCostMultiplier;
        strength = Mathf.Max(0, strength + equipmentStrengthBonus);

        Debug.Log(
            $"<color=#FFD54F><b>【部位還元】</b></color> {values.ItemId} " +
            $"(Purity: {values.Purity:F1}, Density: {values.Density:F1}) " +
            $"-> STR+{values.StrengthBonus} / 体勢×{values.PoiseDamageMultiplier:F2} / " +
            $"スタミナ×{values.StaminaCostMultiplier:F2} / 現在STR={strength}");
    }

    /// <summary>
    /// 装備中の成果物武器の Purity / Density を戦闘ステータスへ還元します。
    /// 未装備・裏パラメータ欠損時は 1.0 倍 / 加算なしへ Safe-Fail します。
    /// ジョブ補正の後に呼び出してください。
    /// </summary>
    /// <param name="logUnequipped">true のとき未装備でもフォールバックログを出します（BattlePhase 入場用）。</param>
    public void ApplyEquipmentModifiers(ItemData weapon, bool logUnequipped = false)
    {
        strength = Mathf.Max(0, strength - equipmentStrengthBonus);
        equipmentStrengthBonus = 0;
        equipmentPoiseDamageMultiplier = 1f;
        equipmentStaminaCostMultiplier = 1f;

        EquipmentModifierValues values = EquipmentStatFeedback.Calculate(weapon);
        if (!values.Applied)
        {
            if (logUnequipped || weapon != null)
            {
                string itemLabel = weapon == null || string.IsNullOrWhiteSpace(weapon.id)
                    ? "未装備"
                    : weapon.id;
                Debug.Log(
                    $"<color=#90A4AE>【装備還元】武器: {itemLabel} " +
                    "（裏パラメータ欠損または未装備）-> 補正なし（STR×1.0 / 体勢×1.0 / スタミナ×1.0） " +
                    $"現在STR={strength}</color>");
            }

            return;
        }

        equipmentStrengthBonus = values.StrengthBonus;
        equipmentPoiseDamageMultiplier = values.PoiseDamageMultiplier;
        equipmentStaminaCostMultiplier = values.StaminaCostMultiplier;
        strength = Mathf.Max(0, strength + equipmentStrengthBonus);

        Debug.Log(
            $"<color=#FFD54F><b>【装備還元】</b></color> " +
            $"武器: {values.ItemId} (Purity: {values.Purity:F1}, Density: {values.Density:F1}) " +
            $"-> STR+{values.StrengthBonus} / 体勢×{values.PoiseDamageMultiplier:F2} / " +
            $"スタミナ×{values.StaminaCostMultiplier:F2} / 現在STR={strength}");
    }

    /// <summary>自動テスト用：基礎ステータスキャプチャをリセットします。</summary>
    public void ResetBaseStatsCaptureForTesting()
    {
        baseStatsCaptured = false;
        baseMaxHp = 0;
        baseStrength = 0;
        baseDefense = 0;
        equipmentStrengthBonus = 0;
        equipmentPoiseDamageMultiplier = 1f;
        equipmentStaminaCostMultiplier = 1f;
    }

    /// <summary>NPC 等のシミュレーション用に基礎戦闘値を設定します（Safe-Fail 付き）。</summary>
    public void SetBaseStatsForNpc(int hp, int str, int def)
    {
        ResetBaseStatsCaptureForTesting();
        maxHp = Mathf.Max(1, hp);
        strength = Mathf.Max(0, str);
        defense = Mathf.Max(0, def);
        currentHp = maxHp;
        CaptureBaseStatsIfNeeded();
    }

    private void LogDamaged(int damage)
    {
        Debug.Log($"{name} が {damage} ダメージを受けた。残り HP: {currentHp}/{maxHp}");

        if (faction == CombatFaction.Player)
        {
            PlayerActionLogger.Instance?.LogTakeDamage();

            // ステップ直後の被弾 → ニアミス（ジャスト回避の試み）として記録
            PlayerController playerController = GetComponent<PlayerController>();
            playerController?.NotifyEnemyHitDuringNearMissWindow();
        }
    }

    private void LogDeath()
    {
        if (faction == CombatFaction.Player)
        {
            PlayerActionLogger.Instance?.LogDeath();
            Debug.Log($"{name} が倒れました。");
            return;
        }

        PlayerActionLogger.Instance?.LogKill();
        Debug.Log($"{name} が倒れた。");
    }
}

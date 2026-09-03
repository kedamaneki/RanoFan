using System;
using System.Collections.Generic;
using UnityEngine;

// =============================================================================
// 基礎スキル（ArtsData）の specialEffects ランタイム実行器
// 連携: CombatActionFeedbackManager / CraftingStatusManager / UniqueSkillRuntimeExecutor
//       PlayerSkillSlotManager / PlayerStatusManager
// =============================================================================

/// <summary>
/// 基礎スキル技ツリーの specialEffects を戦闘・工房へ配線する中央実行体。
/// </summary>
[DefaultExecutionOrder(-85)]
public class ArtsEffectExecutor : MonoBehaviour
{
    private struct TimedStatBuff
    {
        public CombatStats target;
        public int strDelta;
        public int defDelta;
        public float endTime;
    }

    public static ArtsEffectExecutor Instance { get; private set; }

    // 敵体勢デバフ（CombatActionFeedbackManager の回復処理へ配線）
    private float enemyPoiseRegenMultiplier = 1f;
    private float enemyPoiseRegenMultiplierUntil;
    private float enemyPoiseRecoveryHaltUntil;
    private float enemyArmorDissolvePostureBonus;

    private readonly List<TimedStatBuff> timedStatBuffs = new List<TimedStatBuff>();

    // 工房セッション補正（BeginSession で蓄積し、各操作の delta にだけ掛ける）
    private float purityDeltaMultiplier = 1f;
    private float purityPositiveFlat;
    private float thermalDeltaMultiplier = 1f;
    private float thermalPositiveFlat;
    private float extractionDeltaMultiplier = 1f;
    private float dissolutionDeltaMultiplier = 1f;
    private float craftQualityFlatBonus;
    private string sessionCraftType = string.Empty;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void Update()
    {
        PruneTimedStatBuffs();
        PruneEnemyDebuffTimers();
    }

    /// <summary>シーンに無い場合は DebugSystemsHub へ動的生成して返します。</summary>
    public static ArtsEffectExecutor EnsureInstance()
    {
        if (Instance != null)
        {
            return Instance;
        }

        GameObject hub = GameObject.Find("DebugSystemsHub");
        if (hub != null)
        {
            ArtsEffectExecutor existing = hub.GetComponent<ArtsEffectExecutor>();
            return existing != null ? existing : hub.AddComponent<ArtsEffectExecutor>();
        }

        return new GameObject(nameof(ArtsEffectExecutor)).AddComponent<ArtsEffectExecutor>();
    }

    /// <summary>
    /// 技発動時に内包 specialEffects を解釈し、戦闘ステータス／敵デバフ／Utility へ流し込みます。
    /// </summary>
    /// <param name="art">発動した ArtsData</param>
    /// <param name="casterOrTarget">主にプレイヤー CombatStats（バフ対象）</param>
    /// <param name="enemyTarget">敵デバフ対象（省略可・デモは単体敵想定）</param>
    public void ApplyArtsSpecialEffects(ArtsData art, CombatStats casterOrTarget, CombatStats enemyTarget = null)
    {
        if (art?.specialEffects == null || art.specialEffects.Count == 0)
        {
            return;
        }

        for (int i = 0; i < art.specialEffects.Count; i++)
        {
            SpecialEffectData effect = art.specialEffects[i];
            if (effect == null || !effect.IsValid())
            {
                continue;
            }

            // 工房専用効果は戦闘ヒットでは無視（BeginCraftSession 側で一括適用）
            if (IsCraftOnlyEffect(effect.effectType))
            {
                continue;
            }

            ApplySingleCombatEffect(effect, casterOrTarget, enemyTarget, art.artName);
        }
    }

    /// <summary>
    /// 生産系スキルの解放済み技ツリーから Craft_* 効果を工房セッション補正へ蓄積します。
    /// ステータスへの即時 ModifyParam は行いません（各操作の delta に <see cref="ScaleCraftDelta"/> を掛けます）。
    /// </summary>
    public void ApplyProductionArtsCraftBonuses(PlayerSkillSlotManager skillSlots, string craftType)
    {
        ResetCraftSessionModifiers();
        if (skillSlots == null || string.IsNullOrWhiteSpace(craftType))
        {
            return;
        }

        SkillMasterRepository repository = SkillMasterRepository.EnsureInstance();
        if (repository == null)
        {
            return;
        }

        sessionCraftType = NormalizeCraftTypeForArts(craftType);
        IReadOnlyList<SkillData> owned = skillSlots.OwnedSkills;
        for (int i = 0; i < owned.Count; i++)
        {
            SkillData skill = owned[i];
            if (skill == null || string.IsNullOrWhiteSpace(skill.skillID))
            {
                continue;
            }

            if (!repository.TryGet(skill.skillID, out SkillMaster master) || master == null)
            {
                continue;
            }

            if (!IsProductionCategory(master.category))
            {
                continue;
            }

            List<ArtsData> unlockedArts = master.CollectUnlockedArts();
            for (int j = 0; j < unlockedArts.Count; j++)
            {
                AbsorbCraftEffectsFromArt(unlockedArts[j], master.skillName);
            }
        }

        ClampSessionMultipliers();
        Debug.Log(
            $"<color=#FFE082>【Arts・工房delta】職種={sessionCraftType} " +
            $"Purity×{purityDeltaMultiplier:F2}(+{purityPositiveFlat:F1}) " +
            $"Thermal×{thermalDeltaMultiplier:F2}(+{thermalPositiveFlat:F1}℃) " +
            $"Extraction×{extractionDeltaMultiplier:F2} Dissolution×{dissolutionDeltaMultiplier:F2} " +
            $"Quality+{craftQualityFlatBonus:F1}</color>");
    }

    /// <summary>工房セッション終了時に delta 補正を破棄します。</summary>
    public void ResetCraftSessionModifiers()
    {
        purityDeltaMultiplier = 1f;
        purityPositiveFlat = 0f;
        thermalDeltaMultiplier = 1f;
        thermalPositiveFlat = 0f;
        extractionDeltaMultiplier = 1f;
        dissolutionDeltaMultiplier = 1f;
        craftQualityFlatBonus = 0f;
        sessionCraftType = string.Empty;
    }

    /// <summary>Arts 由来の完成品 Quality 固定加算（熱科学バースト時は使わない）。</summary>
    public float CraftQualityFlatBonus => Mathf.Max(0f, craftQualityFlatBonus);

    /// <summary>
    /// 素材倍率適用後の操作 delta を Arts バフと現在値減衰で補正します。
    /// 実行体が無い場合は baseDelta をそのまま返します。
    /// </summary>
    public static float ScaleCraftDelta(string craftType, string paramName, float baseDelta)
    {
        if (Instance == null)
        {
            return baseDelta;
        }

        return Instance.ScaleCraftDeltaInternal(craftType, paramName, baseDelta);
    }

    /// <summary>
    /// 過熱帯で加熱したあとの純度副作用を適用します。ThermalRisk が無いセッションでは何もしません。
    /// </summary>
    public static void ApplyHeatOxidationPurityTax(string craftType, float appliedHeatDelta)
    {
        Instance?.ApplyHeatOxidationPurityTaxInternal(craftType, appliedHeatDelta);
    }

    /// <summary>新しい敵対峙開始時に敵デバフ状態をリセットします。</summary>
    public void ResetEnemyDebuffState()
    {
        enemyPoiseRegenMultiplier = 1f;
        enemyPoiseRegenMultiplierUntil = 0f;
        enemyPoiseRecoveryHaltUntil = 0f;
        enemyArmorDissolvePostureBonus = 0f;
    }

    /// <summary>敵体勢回復速度への乗算（Debuff_PoiseRegen 配線先）。</summary>
    public float GetEnemyPoiseRegenMultiplier()
    {
        return Mathf.Max(0f, enemyPoiseRegenMultiplier);
    }

    /// <summary>敵体勢回復停止中か（Debuff_PoiseRecoveryHalt 配線先）。</summary>
    public bool IsEnemyPoiseRecoveryHalted()
    {
        return Time.time < enemyPoiseRecoveryHaltUntil;
    }

    /// <summary>体勢ダメージへの加算（Debuff_ArmorDissolve 配線先）。</summary>
    public float GetEnemyArmorDissolvePostureBonus()
    {
        return Mathf.Max(0f, enemyArmorDissolvePostureBonus);
    }

    private void ApplySingleCombatEffect(
        SpecialEffectData effect,
        CombatStats casterOrTarget,
        CombatStats enemyTarget,
        string artName)
    {
        string type = effect.effectType?.Trim() ?? string.Empty;
        float value = effect.value;
        float duration = effect.duration;

        switch (type)
        {
            // --- 戦闘バフ: CombatStats へ STR / 体勢耐性相当を一時付与 ---
            case SpecialEffectTypes.BuffStr:
            case SpecialEffectTypes.PsychicBoostStr:
                ApplyTimedStrengthBuff(casterOrTarget, Mathf.RoundToInt(value), duration);
                break;

            case SpecialEffectTypes.BuffPoise:
            case SpecialEffectTypes.PsychicBoostDef:
                ApplyTimedDefenseBuff(casterOrTarget, Mathf.RoundToInt(value), duration);
                break;

            // --- 敵体勢デバフ: CombatActionFeedbackManager の回復・体勢計算へ ---
            case SpecialEffectTypes.DebuffPoiseRegen:
                // value=0.5 → 回復速度 50% に減衰
                enemyPoiseRegenMultiplier = Mathf.Clamp(value, 0f, 1f);
                enemyPoiseRegenMultiplierUntil = duration > 0f ? Time.time + duration : 0f;
                break;

            case SpecialEffectTypes.DebuffPoiseRecoveryHalt:
                enemyPoiseRecoveryHaltUntil = duration > 0f
                    ? Time.time + duration
                    : Time.time + 3f;
                break;

            case SpecialEffectTypes.DebuffArmorDissolve:
                enemyArmorDissolvePostureBonus += Mathf.Max(0f, value);
                break;

            // --- Psychic: HP / MP 再生 ---
            case SpecialEffectTypes.PsychicRegenHp:
                casterOrTarget?.RestoreHp(Mathf.RoundToInt(value));
                break;

            case SpecialEffectTypes.PsychicRegenMana:
                ResolvePlayerStatusManager(casterOrTarget)?.RestoreMP(value);
                break;

            case SpecialEffectTypes.PsychicManaControl:
                // マナ消費軽減は MagicCastController 側の将来拡張用。ここではログのみ。
                Debug.Log(
                    $"<color=#B39DDB>【Arts・マナ制御】{artName}: 消費軽減 {value:P0}（{duration:F1}s）</color>");
                break;

            // --- Utility: UniqueSkillRuntimeExecutor へオーバーレイ配線 ---
            case SpecialEffectTypes.UtilityMindAccelerate:
                UniqueSkillRuntimeExecutor unique = UniqueSkillRuntimeExecutor.Instance;
                unique?.RegisterArtsTimedMindAccelerate(value, duration);
                break;

            case SpecialEffectTypes.UtilityEnemyDetect:
                UniqueSkillRuntimeExecutor.Instance?.ActivateEnemyDetect(duration);
                break;

            case SpecialEffectTypes.UtilityMapRadar:
                Debug.Log(
                    $"<color=#80CBC4>【Arts・マップレーダー】{artName}: 有効化 {duration:F1}s</color>");
                break;

            default:
                Debug.LogWarning($"[ArtsEffectExecutor] 未対応の effectType: {type}（技: {artName}）");
                break;
        }
    }

    private float ScaleCraftDeltaInternal(string craftType, string paramName, float baseDelta)
    {
        if (string.IsNullOrWhiteSpace(paramName))
        {
            return baseDelta;
        }

        CraftingStatusManager craftStatus = CraftingStatusManager.Instance ?? CraftingStatusManager.EnsureInstance();
        if (craftStatus == null)
        {
            return baseDelta;
        }

        string normalizedCraft = NormalizeCraftTypeForArts(craftType);
        float current = craftStatus.GetParam(normalizedCraft, paramName);
        float adjusted = baseDelta;

        if (IsPurityParam(paramName))
        {
            float currentTemp = ResolveSessionTemperature(craftStatus, normalizedCraft);
            adjusted = CraftDeltaCorrection.CorrectPurityDelta(
                baseDelta,
                current,
                currentTemp,
                purityDeltaMultiplier,
                purityPositiveFlat);
        }
        else if (IsTemperatureParam(paramName))
        {
            float maxT = craftStatus.GetParamMax(normalizedCraft, paramName);
            adjusted = CraftDeltaCorrection.CorrectTemperatureDelta(
                baseDelta,
                current,
                maxT,
                thermalDeltaMultiplier,
                thermalPositiveFlat);
        }
        else if (IsExtractionParam(paramName))
        {
            adjusted = CraftDeltaCorrection.CorrectScaledDelta(baseDelta, extractionDeltaMultiplier, 0f);
        }
        else if (IsDissolutionParam(paramName))
        {
            adjusted = CraftDeltaCorrection.CorrectScaledDelta(baseDelta, dissolutionDeltaMultiplier, 0f);
        }

        if (Mathf.Abs(adjusted - baseDelta) > 0.01f)
        {
            Debug.Log(
                $"<color=#FFD54F>【Arts・delta補正】{normalizedCraft}/{paramName} " +
                $"{baseDelta:F2} → {adjusted:F2}（現在={current:F1}）</color>");
        }

        return adjusted;
    }

    private void ApplyHeatOxidationPurityTaxInternal(string craftType, float appliedHeatDelta)
    {
        if (appliedHeatDelta <= 0f)
        {
            return;
        }

        CraftingStatusManager craftStatus = CraftingStatusManager.Instance ?? CraftingStatusManager.EnsureInstance();
        if (craftStatus == null)
        {
            return;
        }

        string normalizedCraft = NormalizeCraftTypeForArts(craftType);
        if (!string.Equals(normalizedCraft, CraftingStatusManager.CraftTypeForge, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        float currentTemp = craftStatus.GetParam(normalizedCraft, CraftingStatusManager.ParamTemperature);
        float tax = CraftDeltaCorrection.ComputeOxidationPurityTax(
            appliedHeatDelta,
            currentTemp,
            thermalDeltaMultiplier,
            purityDeltaMultiplier);
        if (Mathf.Approximately(tax, 0f))
        {
            return;
        }

        craftStatus.ModifyParam(normalizedCraft, CraftingStatusManager.ParamPurity, tax);
        Debug.Log(
            $"<color=#FF8A65>【Arts・過熱酸化】Purity {tax:F2}（加熱 {appliedHeatDelta:F1}℃ / {currentTemp:F0}℃）</color>");
    }

    private void AbsorbCraftEffectsFromArt(ArtsData art, string skillName)
    {
        if (art?.specialEffects == null)
        {
            return;
        }

        for (int i = 0; i < art.specialEffects.Count; i++)
        {
            SpecialEffectData effect = art.specialEffects[i];
            if (effect == null || !effect.IsValid())
            {
                continue;
            }

            string type = effect.effectType?.Trim() ?? string.Empty;
            float value = effect.value;

            switch (type)
            {
                case SpecialEffectTypes.CraftExtractionBonus:
                    AbsorbMultiplierOnly(value, ref extractionDeltaMultiplier);
                    break;

                case SpecialEffectTypes.CraftDissolutionRate:
                    AbsorbMultiplierOnly(value, ref dissolutionDeltaMultiplier);
                    break;

                case SpecialEffectTypes.CraftPurityBonus:
                case SpecialEffectTypes.CraftPurityDeltaBoost:
                    AbsorbMultiplierOrFlat(value, ref purityDeltaMultiplier, ref purityPositiveFlat);
                    break;

                case SpecialEffectTypes.CraftPurityFlatBonus:
                    craftQualityFlatBonus += Mathf.Max(0f, value);
                    break;

                case SpecialEffectTypes.CraftThermalRisk:
                    AbsorbMultiplierOrFlat(value, ref thermalDeltaMultiplier, ref thermalPositiveFlat);
                    break;

                case SpecialEffectTypes.CraftRecipeRecord:
                    Debug.Log(
                        $"<color=#FFF59D>【Arts・レシピ記録】{skillName}/{art.artName}: ガイド有効</color>");
                    break;

                default:
                    break;
            }
        }
    }

    private void ClampSessionMultipliers()
    {
        purityDeltaMultiplier = CraftDeltaCorrection.ClampPurityMultiplier(purityDeltaMultiplier);
        thermalDeltaMultiplier = CraftDeltaCorrection.ClampThermalMultiplier(thermalDeltaMultiplier);
        extractionDeltaMultiplier = Mathf.Clamp(extractionDeltaMultiplier, 0.5f, CraftDeltaCorrection.PurityMultiplierMax);
        dissolutionDeltaMultiplier = Mathf.Clamp(dissolutionDeltaMultiplier, 0.5f, CraftDeltaCorrection.PurityMultiplierMax);
    }

    private static void AbsorbMultiplierOnly(float value, ref float multiplier)
    {
        float unusedFlat = 0f;
        AbsorbMultiplierOrFlat(value, ref multiplier, ref unusedFlat);
    }

    /// <summary>
    /// value が 3 未満なら倍率積、3 以上なら操作ごとの加算。負数は倍率ペナルティ（1+value）。
    /// </summary>
    private static void AbsorbMultiplierOrFlat(float value, ref float multiplier, ref float positiveFlat)
    {
        if (CraftDeltaCorrection.IsMultiplierValue(value))
        {
            multiplier *= CraftDeltaCorrection.NormalizeMultiplierInput(value);
            return;
        }

        if (value >= CraftDeltaCorrection.MultiplierValueExclusiveMax)
        {
            positiveFlat += value;
            return;
        }

        if (value < 0f)
        {
            multiplier *= Mathf.Max(0.5f, 1f + value);
        }
    }

    private static float ResolveSessionTemperature(CraftingStatusManager craftStatus, string craftType)
    {
        if (string.Equals(craftType, CraftingStatusManager.CraftTypeAlch, StringComparison.OrdinalIgnoreCase))
        {
            return craftStatus.GetParam(craftType, CraftingStatusManager.ParamPotTemperature);
        }

        return craftStatus.GetParam(craftType, CraftingStatusManager.ParamTemperature);
    }

    private static bool IsPurityParam(string paramName)
    {
        return string.Equals(paramName, CraftingStatusManager.ParamPurity, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTemperatureParam(string paramName)
    {
        return string.Equals(paramName, CraftingStatusManager.ParamTemperature, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(paramName, CraftingStatusManager.ParamPotTemperature, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsExtractionParam(string paramName)
    {
        return string.Equals(paramName, CraftingStatusManager.ParamExtractionLevel, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDissolutionParam(string paramName)
    {
        return string.Equals(paramName, CraftingStatusManager.ParamDissolutionRate, StringComparison.OrdinalIgnoreCase);
    }

    private void ApplyTimedStrengthBuff(CombatStats target, int amount, float durationSeconds)
    {
        if (target == null || amount == 0)
        {
            return;
        }

        target.ApplyStrengthBonus(amount);
        if (durationSeconds > 0f)
        {
            timedStatBuffs.Add(new TimedStatBuff
            {
                target = target,
                strDelta = amount,
                endTime = Time.time + durationSeconds
            });
        }
    }

    private void ApplyTimedDefenseBuff(CombatStats target, int amount, float durationSeconds)
    {
        if (target == null || amount == 0)
        {
            return;
        }

        target.ApplyDefenseBonus(amount);
        if (durationSeconds > 0f)
        {
            timedStatBuffs.Add(new TimedStatBuff
            {
                target = target,
                defDelta = amount,
                endTime = Time.time + durationSeconds
            });
        }
    }

    private void PruneTimedStatBuffs()
    {
        float now = Time.time;
        for (int i = timedStatBuffs.Count - 1; i >= 0; i--)
        {
            TimedStatBuff buff = timedStatBuffs[i];
            if (now < buff.endTime)
            {
                continue;
            }

            if (buff.target != null)
            {
                if (buff.strDelta != 0)
                {
                    buff.target.ApplyStrengthBonus(-buff.strDelta);
                }

                if (buff.defDelta != 0)
                {
                    buff.target.ApplyDefenseBonus(-buff.defDelta);
                }
            }

            timedStatBuffs.RemoveAt(i);
        }
    }

    private void PruneEnemyDebuffTimers()
    {
        if (enemyPoiseRegenMultiplierUntil > 0f && Time.time > enemyPoiseRegenMultiplierUntil)
        {
            enemyPoiseRegenMultiplier = 1f;
            enemyPoiseRegenMultiplierUntil = 0f;
        }
    }

    private static bool IsCraftOnlyEffect(string effectType)
    {
        if (string.IsNullOrWhiteSpace(effectType))
        {
            return false;
        }

        return effectType.StartsWith("Craft_", StringComparison.OrdinalIgnoreCase) ||
               effectType.StartsWith("Hobby_", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsProductionCategory(string category)
    {
        return string.Equals(category, SkillMasterCategories.Production, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(category, SkillMasterCategories.Hobby, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeCraftTypeForArts(string craftType)
    {
        if (string.Equals(craftType, DetailedCraftingProcessManager.CraftTypeAlch, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(craftType, CraftingStatusManager.CraftTypeAlch, StringComparison.OrdinalIgnoreCase))
        {
            return CraftingStatusManager.CraftTypeAlch;
        }

        return CraftingStatusManager.CraftTypeForge;
    }

    private static PlayerStatusManager ResolvePlayerStatusManager(CombatStats caster)
    {
        if (caster == null)
        {
            return PlayerStatusManager.Instance;
        }

        PlayerStatusManager onCaster = caster.GetComponent<PlayerStatusManager>();
        if (onCaster != null)
        {
            return onCaster;
        }

        return PlayerStatusManager.Instance ?? caster.GetComponentInParent<PlayerStatusManager>();
    }
}

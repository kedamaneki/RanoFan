using UnityEngine;

// =============================================================================
// 工房 Temperature / Purity の delta バフ・減衰式
// 連携: ArtsEffectExecutor / CraftingStatusManager
// =============================================================================

/// <summary>
/// 工房操作の Temperature / Purity（および調合釜温）delta を、Arts 補正と現在値から決定論的に補正します。
/// セッション開始時の即時 ModifyParam は行わず、各スロット操作の加減算にだけ掛けます。
/// </summary>
public static class CraftDeltaCorrection
{
    /// <summary>
    /// JSON value の解釈境界。0 超かつこの値未満は倍率、以上は操作ごとの加算。
    /// SkillMasters.json の Craft_PurityBonus=1.1 は倍率として扱う。
    /// </summary>
    public const float MultiplierValueExclusiveMax = 3f;

    /// <summary>Purity 上限（減衰の頭打ち基準。定義テーブルと一致）。</summary>
    public const float PurityCap = 100f;

    /// <summary>室温（℃）。熱容量減衰の下限。</summary>
    public const float RoomTemperatureCelsius = 20f;

    /// <summary>オーステナイト帯の下限（℃）。この帯では Purity 上昇が最も効く。</summary>
    public const float AusteniteMinCelsius = 800f;

    /// <summary>オーステナイト帯の上限（℃）。</summary>
    public const float AusteniteMaxCelsius = 1100f;

    /// <summary>過熱酸化が始まる温度（℃）。これ以上では Purity 上昇が減衰し、加熱で純度が削れる。</summary>
    public const float OxidationOnsetCelsius = 1200f;

    /// <summary>Purity 倍率の下限（複数 Arts の積をクランプ）。</summary>
    public const float PurityMultiplierMin = 0.7f;

    /// <summary>Purity 倍率の上限。</summary>
    public const float PurityMultiplierMax = 1.85f;

    /// <summary>熱倍率の下限。</summary>
    public const float ThermalMultiplierMin = 0.85f;

    /// <summary>熱倍率の上限。ThermalRisk の積み過ぎで融点へ一瞬で飛ばないようにする。</summary>
    public const float ThermalMultiplierMax = 1.6f;

    /// <summary>オーステナイト帯での Purity 上昇倍率。</summary>
    public const float AustenitePurityZoneMul = 1.15f;

    /// <summary>過熱酸化帯での Purity 上昇倍率。</summary>
    public const float OxidationPurityZoneMul = 0.65f;

    /// <summary>最高温付近での追加加熱の残り効率。</summary>
    public const float HeatCapacityScaleAtMax = 0.35f;

    /// <summary>室温付近での冷却効率（1.0 ならスロット値と一致。高温ほど放射で効く）。</summary>
    public const float CoolScaleAtRoom = 1f;

    /// <summary>最高温付近での冷却効率（放射が大きい）。</summary>
    public const float CoolScaleAtMax = 1.35f;

    /// <summary>
    /// 過熱帯で加熱したときの純度損失係数。
    /// tax = -appliedHeat * この値 * max(0, thermalMul - 1)
    /// 例: 大槌 +280℃ × ThermalRisk 1.2 → -280 * 0.02 * 0.2 = -1.12
    /// </summary>
    public const float OxidationPurityPerHeatUnit = 0.02f;

    /// <summary>0 超かつ 3 未満の value を倍率として扱うか。</summary>
    public static bool IsMultiplierValue(float value)
    {
        return value > 0f && value < MultiplierValueExclusiveMax;
    }

    /// <summary>
    /// JSON value を掛け算用の倍率へ正規化します。
    /// 0 超 1 未満は「加算割合」（0.15 → 1.15）。1 以上 3 未満はそのまま（1.1 → 1.1）。
    /// </summary>
    public static float NormalizeMultiplierInput(float value)
    {
        if (value > 0f && value < 1f)
        {
            return 1f + value;
        }

        return value;
    }

    /// <summary>複数 Arts の積を Purity 倍率としてクランプします。</summary>
    public static float ClampPurityMultiplier(float product)
    {
        return Mathf.Clamp(product, PurityMultiplierMin, PurityMultiplierMax);
    }

    /// <summary>複数 Arts の積を熱倍率としてクランプします。</summary>
    public static float ClampThermalMultiplier(float product)
    {
        return Mathf.Clamp(product, ThermalMultiplierMin, ThermalMultiplierMax);
    }

    /// <summary>
    /// Purity delta を補正します。すでに 100 のときは上昇を打ち切り、それ以外の上限は ModifyParam がクランプします。
    /// <para>
    /// 上昇: applied = (baseDelta * purityMul + flat) * zoneMul
    /// zoneMul = 800〜1100℃ で 1.15、1200℃以上で 0.65、それ以外 1.0
    /// 室温かつ倍率 1 なら baseline のスロット値と一致します。
    /// </para>
    /// <para>
    /// 下降: applied = baseDelta / max(1, purityMul)
    /// 純度バフがあるほど不純物混入が弱い。過熱帯ではさらに 1/0.65 倍だけ損失が増える。
    /// </para>
    /// </summary>
    public static float CorrectPurityDelta(
        float baseDelta,
        float currentPurity,
        float currentTemperatureCelsius,
        float purityMultiplier,
        float purityPositiveFlat)
    {
        float mul = ClampPurityMultiplier(purityMultiplier);
        if (Mathf.Approximately(baseDelta, 0f) && purityPositiveFlat <= 0f)
        {
            return 0f;
        }

        if (baseDelta < 0f)
        {
            float protection = Mathf.Max(1f, mul);
            float loss = baseDelta / protection;
            if (currentTemperatureCelsius >= OxidationOnsetCelsius)
            {
                loss /= OxidationPurityZoneMul;
            }

            return loss;
        }

        if (currentPurity >= PurityCap)
        {
            return 0f;
        }

        float zoneMul = ResolvePurityTemperatureZoneMul(currentTemperatureCelsius);
        return (baseDelta * mul + Mathf.Max(0f, purityPositiveFlat)) * zoneMul;
    }

    /// <summary>
    /// Temperature / PotTemperature の delta を補正します。
    /// <para>
    /// 加熱: applied = (baseDelta * thermalMul + flat) * capacityDecay
    /// capacityDecay = Lerp(1.0, 0.35, norm^2)
    /// norm = InverseLerp(室温, maxT, currentT)
    /// </para>
    /// <para>
    /// 冷却: applied = baseDelta * coolBoost / max(0.85, thermalMul)
    /// coolBoost = Lerp(1.0, 1.35, norm)
    /// ThermalRisk が高いほど熱が逃げにくい。
    /// </para>
    /// </summary>
    public static float CorrectTemperatureDelta(
        float baseDelta,
        float currentTemperatureCelsius,
        float maxTemperatureCelsius,
        float thermalMultiplier,
        float thermalPositiveFlat)
    {
        float mul = ClampThermalMultiplier(thermalMultiplier);
        float maxT = Mathf.Max(RoomTemperatureCelsius + 1f, maxTemperatureCelsius);
        float norm = Mathf.InverseLerp(RoomTemperatureCelsius, maxT, currentTemperatureCelsius);
        norm = Mathf.Clamp01(norm);

        if (baseDelta < 0f)
        {
            float coolBoost = Mathf.Lerp(CoolScaleAtRoom, CoolScaleAtMax, norm);
            float retention = Mathf.Max(ThermalMultiplierMin, mul);
            return baseDelta * coolBoost / retention;
        }

        if (baseDelta <= 0f && thermalPositiveFlat <= 0f)
        {
            return 0f;
        }

        float capacityDecay = Mathf.Lerp(1f, HeatCapacityScaleAtMax, norm * norm);
        return (baseDelta * mul + Mathf.Max(0f, thermalPositiveFlat)) * capacityDecay;
    }

    /// <summary>
    /// 過熱帯での加熱に対する純度副作用。ThermalRisk が 1 を超えるときだけ発生します。
    /// tax = -appliedHeat * 0.02 * max(0, thermalMul - 1) / max(1, purityMul)
    /// </summary>
    public static float ComputeOxidationPurityTax(
        float appliedHeatDelta,
        float currentTemperatureCelsius,
        float thermalMultiplier,
        float purityMultiplier)
    {
        if (appliedHeatDelta <= 0f || currentTemperatureCelsius < OxidationOnsetCelsius)
        {
            return 0f;
        }

        float riskExcess = Mathf.Max(0f, ClampThermalMultiplier(thermalMultiplier) - 1f);
        if (riskExcess <= 0f)
        {
            return 0f;
        }

        float protection = Mathf.Max(1f, ClampPurityMultiplier(purityMultiplier));
        return -appliedHeatDelta * OxidationPurityPerHeatUnit * riskExcess / protection;
    }

    /// <summary>ExtractionLevel / DissolutionRate など、倍率のみの delta 補正。</summary>
    public static float CorrectScaledDelta(float baseDelta, float multiplier, float positiveFlat)
    {
        float mul = Mathf.Max(0.5f, multiplier);
        if (baseDelta < 0f)
        {
            return baseDelta / Mathf.Max(1f, mul);
        }

        return baseDelta * mul + Mathf.Max(0f, positiveFlat);
    }

    private static float ResolvePurityTemperatureZoneMul(float temperatureCelsius)
    {
        if (temperatureCelsius >= OxidationOnsetCelsius)
        {
            return OxidationPurityZoneMul;
        }

        if (temperatureCelsius >= AusteniteMinCelsius && temperatureCelsius <= AusteniteMaxCelsius)
        {
            return AustenitePurityZoneMul;
        }

        return 1f;
    }
}

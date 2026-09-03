using UnityEngine;

// =============================================================================
// 村結界コア — 維持率 0〜100% と BarrierDrop
// =============================================================================

/// <summary>
/// 結界維持率を管理します。活性期は自然減衰、結界守が魔力結晶で回復します。
/// 0% で BarrierDrop を一度だけ立てます。
/// </summary>
[System.Serializable]
public class VillageBarrierCore
{
    public const string BarrierDropFlag = "BarrierDrop";
    public const string Nation001BarrierDropFlag = "HIST_NATION_001_GEO_TURN_001_BARRIERDROP";
    public const float MinEfficiency = 0f;
    public const float MaxEfficiency = 100f;
    public const float ActiveDecayPerPhase = 3.2f;
    public const float BreachThresholdPercent = 50f;

    [Range(0f, 100f)] public float Efficiency = 78f;
    public bool BarrierDropped;

    /// <summary>活性期のみ自動減少。0% で破綻フラグ。</summary>
    public void ApplySeasonPressure(VariableTimelineSeason season)
    {
        if (season != VariableTimelineSeason.Active &&
            season != VariableTimelineSeason.Escalation)
        {
            EvaluateDrop();
            return;
        }

        float decay = season == VariableTimelineSeason.Active
            ? ActiveDecayPerPhase
            : ActiveDecayPerPhase * 0.55f;
        Efficiency = Clamp(Efficiency - decay);
        EvaluateDrop();
    }

    /// <summary>
    /// 結界守が消費できた結晶量に応じて回復します。
    /// 結晶 0 でも点検労力分だけごくわずかに戻します（Safe-Fail）。
    /// </summary>
    public float RestoreFromKeeper(float crystalsConsumed, float magicTechMultiplier, bool crystalShortage)
    {
        float tech = Mathf.Clamp(magicTechMultiplier, 1f, 2.5f);
        float recovered;
        if (crystalsConsumed <= 0.001f)
        {
            recovered = crystalShortage ? 0.35f : 0.8f;
        }
        else
        {
            recovered = crystalsConsumed * 6.4f * tech;
        }

        float before = Efficiency;
        Efficiency = Clamp(Efficiency + recovered);
        if (Efficiency > 0.01f)
        {
            BarrierDropped = false;
        }

        EvaluateDrop();
        return Efficiency - before;
    }

    /// <summary>緊急修復で維持率を直接回復します（+15〜25% 想定）。</summary>
    public float RestoreDirectPercent(float percent)
    {
        float recovered = Mathf.Max(0f, percent);
        if (float.IsNaN(recovered) || float.IsInfinity(recovered))
        {
            recovered = 0f;
        }

        float before = Efficiency;
        Efficiency = Clamp(Efficiency + recovered);
        if (Efficiency > 0.01f)
        {
            BarrierDropped = false;
        }

        EvaluateDrop();
        return Efficiency - before;
    }

    public bool IsBelowBreachThreshold =>
        Efficiency <= BreachThresholdPercent + 0.0001f;

    /// <summary>外部ダメージで維持率を下げ、必要なら破綻判定します。</summary>
    public float ApplyExternalDamage(float percentDrop)
    {
        if (percentDrop <= 0f || float.IsNaN(percentDrop) || float.IsInfinity(percentDrop))
        {
            EvaluateDrop();
            return 0f;
        }

        float before = Efficiency;
        Efficiency = Clamp(Efficiency - percentDrop);
        EvaluateDrop();
        return before - Efficiency;
    }

    /// <summary>Efficiency 変更後に破綻判定を実行します。</summary>
    public void RefreshDropEvaluation()
    {
        EvaluateDrop();
    }

    private void EvaluateDrop()
    {
        if (Efficiency > 0.01f)
        {
            return;
        }

        Efficiency = MinEfficiency;
        if (BarrierDropped)
        {
            return;
        }

        BarrierDropped = true;
        try
        {
            HistoryFlagRegistry.Unlock(BarrierDropFlag);
            HistoryFlagRegistry.Unlock("HIST_VILLAGE_BARRIER_DROP");
            HistoryFlagRegistry.Unlock(Nation001BarrierDropFlag);
        }
        catch (System.Exception exception)
        {
            Debug.LogWarning($"[VillageBarrierCore] BarrierDrop フラグ登録をスキップ: {exception.Message}");
        }

        Debug.LogWarning(
            "<color=#FF8A80><b>【結界破綻】BarrierDrop</b></color> 維持率 0%。" +
            "活性期の減衰に対し魔力結晶が追いつきませんでした。");
    }

    private static float Clamp(float value)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
        {
            return MinEfficiency;
        }

        return Mathf.Clamp(value, MinEfficiency, MaxEfficiency);
    }
}

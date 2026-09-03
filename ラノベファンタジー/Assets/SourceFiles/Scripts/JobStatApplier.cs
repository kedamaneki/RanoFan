using System;
using UnityEngine;

/// <summary>
/// JobMasterData.statModifiers を CombatStats / PlayerStatusManager へ乗算適用する静的ヘルパー。
/// PlayerStatusManager.mainJob（表示名または ID 文字列）をキーに解決します。
/// </summary>
public static class JobStatApplier
{
    /// <summary>ジョブ未検出時に使用する無補正倍率（安全フォールバック）。</summary>
    public static readonly StatModifiers IdentityModifiers = new StatModifiers
    {
        hpMultiplier = 1f,
        strMultiplier = 1f,
        defMultiplier = 1f,
        manaMultiplier = 1f
    };

    /// <summary>
    /// ジョブ ID またはジョブ名から statModifiers を取得し、CombatStats へ乗算適用します。
    /// 同一 GameObject の PlayerStatusManager があれば MP 上限（manaMultiplier）も反映します。
    /// </summary>
    /// <param name="jobId">JobMasterData.id または JobMasterData.name（mainJob 文字列）</param>
    /// <param name="baseStats">補正対象の CombatStats（基礎値は内部でキャプチャ）</param>
    public static void ApplyJobModifiers(string jobId, CombatStats baseStats)
    {
        if (baseStats == null)
        {
            Debug.LogWarning("[JobStatApplier] baseStats が null のためジョブ補正をスキップします。");
            return;
        }

        StatModifiers modifiers = ResolveStatModifiers(jobId, out string resolvedLabel);
        baseStats.ApplyJobStatMultipliers(modifiers);

        PlayerStatusManager status = baseStats.GetComponent<PlayerStatusManager>();
        if (status != null)
        {
            status.ApplyJobManaMultiplier(modifiers.manaMultiplier);
        }

        Debug.Log(
            $"[JobStatApplier] ジョブ補正適用: {resolvedLabel} | " +
            $"HP×{modifiers.hpMultiplier:F2} STR×{modifiers.strMultiplier:F2} " +
            $"DEF×{modifiers.defMultiplier:F2} MP×{modifiers.manaMultiplier:F2}");
    }

    /// <summary>
    /// ジョブキーから StatModifiers を解決します。未登録時は警告のうえ 1.0 倍を返します。
    /// </summary>
    public static StatModifiers ResolveStatModifiers(string jobKey, out string resolvedLabel)
    {
        resolvedLabel = jobKey ?? string.Empty;

        if (string.IsNullOrWhiteSpace(jobKey))
        {
            Debug.LogWarning("[JobStatApplier] ジョブキーが空です。補正なし（1.0倍）で続行します。");
            return IdentityModifiers;
        }

        if (!TryResolveJob(jobKey, out JobMasterData job))
        {
            Debug.LogWarning(
                $"[JobStatApplier] 未登録ジョブ '{jobKey}' — でっち上げ文字列の可能性があります。補正なし（1.0倍）で続行します。");
            resolvedLabel = $"{jobKey} (未登録)";
            return IdentityModifiers;
        }

        resolvedLabel = $"{job.name} ({job.id})";
        return job.statModifiers ?? IdentityModifiers;
    }

    /// <summary>ID 一致 → 表示名一致の順で JobMasterData を検索します。</summary>
    public static bool TryResolveJob(string jobKey, out JobMasterData job)
    {
        job = null;
        if (string.IsNullOrWhiteSpace(jobKey))
        {
            return false;
        }

        MasterDataManager masterData = MasterDataManager.EnsureInstance();
        if (masterData == null)
        {
            Debug.LogWarning("[JobStatApplier] MasterDataManager を解決できません。");
            return false;
        }

        if (masterData.TryGetJob(jobKey, out job) && job != null)
        {
            return true;
        }

        foreach (JobMasterData candidate in masterData.JobRegistry.Values)
        {
            if (candidate == null)
            {
                continue;
            }

            if (string.Equals(candidate.name, jobKey, StringComparison.OrdinalIgnoreCase))
            {
                job = candidate;
                return true;
            }
        }

        return false;
    }
}

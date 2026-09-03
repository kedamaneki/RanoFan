using System;
using System.Collections.Generic;

// =============================================================================
// 歴史フラグ解禁レジストリ — SkillEvolutionLinker.ConditionFlagResolver へ配線
// =============================================================================

/// <summary>
/// 解読済み HIST_ / 関連フラグを保持し、進化・ジョブ条件判定へ渡します。
/// </summary>
public static class HistoryFlagRegistry
{
    /// <summary>最初の正史解読完了時に立てるジョブ解放フラグ（JOB_HISTORIAN）。</summary>
    public const string InitialDecodingCompleteFlag = "HIST_INITIAL_DECODING_COMPLETE";

    private static readonly HashSet<string> UnlockedFlags =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    private static bool resolverWired;
    private static Func<string, bool> previousResolver;

    /// <summary>ConditionFlagResolver へ自レジストリを接続します（既存フックは OR 合成）。</summary>
    public static void EnsureWired()
    {
        if (resolverWired)
        {
            return;
        }

        previousResolver = SkillEvolutionLinker.ConditionFlagResolver;
        SkillEvolutionLinker.ConditionFlagResolver = IsSatisfied;
        resolverWired = true;
    }

    /// <summary>フラグを解禁します。新規なら true。</summary>
    public static bool Unlock(string flagKey)
    {
        EnsureWired();
        if (string.IsNullOrWhiteSpace(flagKey))
        {
            return false;
        }

        return UnlockedFlags.Add(flagKey.Trim());
    }

    /// <summary>解禁済みか。</summary>
    public static bool IsUnlocked(string flagKey)
    {
        if (string.IsNullOrWhiteSpace(flagKey))
        {
            return true;
        }

        return UnlockedFlags.Contains(flagKey.Trim());
    }

    /// <summary>ConditionFlagResolver 用。自フラグまたは以前のフックが true なら成立。</summary>
    public static bool IsSatisfied(string flagKey)
    {
        if (string.IsNullOrWhiteSpace(flagKey))
        {
            return true;
        }

        if (EraContextResolver.IsKnownEraFlagKey(flagKey))
        {
            return EraContextResolver.IsEraConditionFlag(flagKey);
        }

        if (IsUnlocked(flagKey))
        {
            return true;
        }

        if (previousResolver == null)
        {
            return false;
        }

        try
        {
            return previousResolver(flagKey);
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>テスト用に解禁セットを空にします。</summary>
    public static void ClearForTesting()
    {
        UnlockedFlags.Clear();
    }

    /// <summary>現在解禁されているフラグ数。</summary>
    public static int UnlockedCount => UnlockedFlags.Count;
}

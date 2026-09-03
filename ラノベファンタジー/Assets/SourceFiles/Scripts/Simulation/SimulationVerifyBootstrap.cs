using System.Collections.Generic;
using UnityEngine;

// =============================================================================
// エディタ検証メニュー共通 — Story 年次リセットと排他モード解除
// =============================================================================

/// <summary>
/// RunDayVerification 系メニューが連続実行されても独立 PASS できるよう、
/// StoryTimeline の年次状態を初期化します。
/// </summary>
public static class SimulationVerifyBootstrap
{
    public const int DefaultVerifyTurn = 1;

    /// <summary>方針A/B: yearCompleted と StoryMode 不可逆位相をリセットします。</summary>
    public static void PrepareFreshStoryDay(int turn = DefaultVerifyTurn, GameMode mode = GameMode.StoryMode)
    {
        StoryTimelineManager.EnsureInstance().TryBootstrapStoryYear(turn, mode);
    }

    /// <summary>Regional Expansion 検証前に 35 国モードの排他を解除します。</summary>
    public static void DeactivateWestRegionForRegionalVerification()
    {
        WestRegionSimulationManager.EnsureInstance().SetWestRegionActiveForVerification(false);
    }

    /// <summary>Natural Ecology 検証用にカウンタと魔物パックを初期化します。</summary>
    public static void PrepareNaturalEcologyVerification(NaturalEcologyEngine engine)
    {
        if (engine == null)
        {
            return;
        }

        engine.ResetVerificationState();

        EnemyEcologyBehaviorRuntime beasts = EnemyEcologyBehaviorRuntime.EnsureInstance();
        IReadOnlyList<EnemyEcologyInstinctAI> existing = beasts.Pack;
        for (int i = (existing?.Count ?? 0) - 1; i >= 0; i--)
        {
            beasts.RetireBeast(existing[i], "自然生態検証リセット");
        }

        beasts.SpawnMutatedBeast("検証・飢餓獣A", 90f);
        beasts.SpawnMutatedBeast("検証・飢餓獣B", 88f);

        IReadOnlyList<EnemyEcologyInstinctAI> pack = beasts.Pack;
        for (int i = 0; i < pack.Count; i++)
        {
            pack[i]?.EnsureStatus()?.SetManaHunger(86f);
        }
    }
}

#if UNITY_EDITOR

using UnityEngine;

/// <summary>DemoAIDebugFuzzer 向けの非破壊ファジング API（partial）。エディタ専用。</summary>
public partial class DemoTimeLineManager
{
    /// <summary>ファジング中は自動撃破遷移を抑止します。</summary>
    public void Fuzz_SetFuzzRunActive(bool active)
    {
        isRiskVerificationSuppressingAutoTransitions = active;
    }

    /// <summary>暗転コルーチンを飛ばし、即座に指定ステートへ遷移します。</summary>
    public void Fuzz_InstantTransitionTo(DemoState targetState)
    {
        StopBattleToCraftTransitionIfActive();
        StopCraftingPhaseHarassmentSchedule();
        StopAutoCombatAssist();

        if (targetState == DemoState.BattlePhase)
        {
            ResetBattleTransitionGuardsForBattleEntry();
        }

        if (CurrentState == targetState)
        {
            return;
        }

        TransitionTo(targetState);
    }

    /// <summary>戦闘撃破演出を省略し、工房セッションを即開始します。</summary>
    public void Fuzz_InstantBattleClearToCrafting()
    {
        StopBattleToCraftTransitionIfActive();
        combatClearedNotified = false;
        Fuzz_InstantTransitionTo(DemoState.CraftingPhase);
    }

    /// <summary>指定職種で CraftingPhase セッションを再構築します。</summary>
    public void Fuzz_PrepareCraftingSession(string craftType)
    {
        CacheReferences();

        CraftingExperimentHub hub = craftingHub ?? CraftingExperimentHub.EnsureInstance();
        bool isAlch = string.Equals(
            craftType,
            DetailedCraftingProcessManager.CraftTypeAlch,
            System.StringComparison.OrdinalIgnoreCase);
        CraftProfessionType profession = isAlch ? CraftProfessionType.Alch : CraftProfessionType.Forge;

        if (!hub.IsActive)
        {
            hub.StartCrafting(profession.ToString(), isSafetyZone: false);
        }

        if (CurrentState != DemoState.CraftingPhase)
        {
            Fuzz_InstantTransitionTo(DemoState.CraftingPhase);
        }
        else
        {
            EnterCraftingPhaseSetup();
        }
    }

    /// <summary>Result から次セッション用に Battle へ戻します。</summary>
    public void Fuzz_ResetForNextSession()
    {
        Fuzz_InstantTransitionTo(DemoState.BattlePhase);
        EnterBattlePhaseSetup();
    }
}

#endif

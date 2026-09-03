using System.Collections;
using UnityEngine;

/// <summary>DemoPhaseRiskAutoVerifier 向けの非破壊テスト API（partial）。</summary>
public partial class DemoTimeLineManager
{
    /// <summary>戦闘→工房暗転コルーチンが進行中か。</summary>
    public bool IsBattleToCraftTransitionRunning => battleToCraftTransitionCoroutine != null;

    /// <summary>Result 着地ジャッジが進行中か。</summary>
    public bool IsCraftingFinalizationLocked => isCraftingFinalizationInProgress;

    /// <summary>撃破後の工房移行を開始します（テスト用）。</summary>
    public void RiskTest_TriggerCombatClearTransition()
    {
        OnCombatCleared();
    }

    /// <summary>BattlePhase 入力ループを1フレーム分だけ手動駆動します。</summary>
    public void RiskTest_TickBattlePhaseInputOnce()
    {
        ProcessBattlePhaseInput();
    }

    /// <summary>CraftingPhase 入力ループを1フレーム分だけ手動駆動します。</summary>
    public void RiskTest_TickCraftingPhaseInputOnce()
    {
        ProcessCraftingPhaseInput();
    }

    /// <summary>暗転中にプレイヤーが猛連打した状況を、戦闘 API 直呼びで再現します。</summary>
    public void RiskTest_SpamDirectCombatActions(int repeatCount)
    {
        CombatActionFeedbackManager combat = combatFeedback
            ?? CombatActionFeedbackManager.EnsureInstance();
        if (combat == null)
        {
            return;
        }

        int count = Mathf.Max(1, repeatCount);
        for (int i = 0; i < count; i++)
        {
            combat.PerformPlayerAttack(combat.CurrentSelectedAttackType);
            combat.AttemptStepEvade();
        }
    }

    /// <summary>撃破→工房遷移ガードを初期化します（自動検証の Battle 再セットアップ用）。</summary>
    public void RiskTest_ResetBattleTransitionGuards()
    {
        ResetBattleTransitionGuardsForBattleEntry();
    }

    /// <summary>自動検証中は敵撃破イベントによる暗転遷移を抑止します。</summary>
    public void RiskTest_SetVerificationSuppressing(bool suppressing)
    {
        isRiskVerificationSuppressingAutoTransitions = suppressing;
    }

    /// <summary>進行中コルーチンを止め、BattlePhase へ安全に戻します。</summary>
    public void RiskTest_ForceReenterBattlePhase()
    {
        StopAutoCombatAssist();
        StopBattleToCraftTransitionIfActive();
        StopCraftingPhaseHarassmentSchedule();
        ResetBattleTransitionGuardsForBattleEntry();

        if (CurrentState == DemoState.BattlePhase)
        {
            EnterBattlePhaseSetup();
            return;
        }

        TransitionTo(DemoState.BattlePhase);
    }

    /// <summary>既に CraftingPhase にいる場合のセッション再初期化（自動検証用）。</summary>
    public void RiskTest_RefreshCraftingSession()
    {
        if (CurrentState != DemoState.CraftingPhase)
        {
            return;
        }

        EnterCraftingPhaseSetup();
    }

    /// <summary>暗転・自動バトルと競合しないようデモ状態を検証用に整えます。</summary>
    public void RiskTest_StabilizeForVerification()
    {
        StopAutoCombatAssist();
        StopBattleToCraftTransitionIfActive();
        StopCraftingPhaseHarassmentSchedule();
        isAutoRelayActive = false;
        hasAutoStartedBattle = true;
        ResetBattleTransitionGuardsForBattleEntry();

        if (CurrentState != DemoState.BattlePhase)
        {
            TransitionTo(DemoState.BattlePhase);
        }
        else
        {
            EnterBattlePhaseSetup();
        }
    }

    /// <summary>指定ステートへ着地するまで待機します（タイムアウト付き）。</summary>
    public IEnumerator RiskTest_WaitForState(
        DemoState targetState,
        float timeoutSeconds)
    {
        float elapsed = 0f;
        while (CurrentState != targetState && elapsed < timeoutSeconds)
        {
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }
    }
}

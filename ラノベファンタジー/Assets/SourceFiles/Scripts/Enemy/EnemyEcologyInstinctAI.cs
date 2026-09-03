using UnityEngine;

// =============================================================================
// 魔物生態・本能ベースの行動選択 AI
// ※攻撃コンボ JSON 用の静的 EnemyBehaviorProfileManager（Scripts 直下）とは別系統。
//   本クラスが要件「結界漏洩・魔力飢餓・スタミナ/MP切れ」の行動選択を担います。
// =============================================================================

/// <summary>魔物の生態行動。</summary>
public enum EnemyEcologyAction
{
    /// <summary>通常の接近・物理攻撃。</summary>
    Assault = 0,
    /// <summary>ブレス／魔法攻撃（MP 消費）。</summary>
    BreathCast = 1,
    /// <summary>スタミナ切れの息整え・威嚇・間合い。</summary>
    RecoverStance = 2,
    /// <summary>結界／魔力結晶（餌場）へ向かう。</summary>
    SeekManaPrey = 3,
    /// <summary>魔力源を摂取して MP 回復。</summary>
    FeedOnBarrier = 4,
    /// <summary>警戒・距離を取る。</summary>
    Withdraw = 5
}

/// <summary>行動決定の結果。</summary>
public struct EnemyEcologyDecision
{
    public EnemyEcologyAction Action;
    public float Score;
    public string Reason;
}

/// <summary>
/// 環境魔力（位相）・結界維持率・村の結晶蓄積・個体の ManaHunger から行動を選びます。
/// </summary>
[RequireComponent(typeof(EnemyStatusManager))]
[DefaultExecutionOrder(41)]
public class EnemyEcologyInstinctAI : MonoBehaviour
{
    public const float BarrierLeakThreshold = 55f;
    public const float HighHunger = 60f;

    [SerializeField] private EnemyStatusManager status;
    [SerializeField] private EnemyEcologyAction lastAction = EnemyEcologyAction.Assault;
    [SerializeField] private float feedCooldown;
    [SerializeField] private Vector3 forcedTargetPosition;
    [SerializeField] private string forcedTargetSpotId = string.Empty;
    [SerializeField] private bool hasForcedTarget;

    public EnemyEcologyAction LastAction => lastAction;
    public bool HasForcedTarget => hasForcedTarget;
    public Vector3 ForcedTargetPosition => forcedTargetPosition;
    public string ForcedTargetSpotId => forcedTargetSpotId;

    /// <summary>未バインド時は同オブジェクトから遅延解決します（Editor メニューでも Awake 不要）。</summary>
    public EnemyStatusManager Status => EnsureStatus();

    /// <summary>Spawn 直後など、Awake 前に明示バインドします。</summary>
    public void BindStatus(EnemyStatusManager statusManager)
    {
        status = statusManager;
        if (status == null)
        {
            status = GetComponent<EnemyStatusManager>();
        }
    }

    public EnemyStatusManager EnsureStatus()
    {
        if (status == null)
        {
            status = GetComponent<EnemyStatusManager>();
        }

        if (status == null)
        {
            status = gameObject.AddComponent<EnemyStatusManager>();
        }

        return status;
    }

    private void Awake()
    {
        EnsureStatus();
    }

    /// <summary>Breach Point などへターゲット座標を強制設定します。</summary>
    public void SetForcedAssaultTarget(Vector3 worldPosition, string spotId)
    {
        hasForcedTarget = true;
        forcedTargetPosition = worldPosition;
        forcedTargetSpotId = spotId ?? string.Empty;
    }

    public void ClearForcedTarget()
    {
        hasForcedTarget = false;
        forcedTargetSpotId = string.Empty;
    }

    /// <summary>強制ターゲットへ移動し、到達したかを返します。</summary>
    public string MoveTowardForcedTarget(float stepMeters)
    {
        if (!hasForcedTarget)
        {
            return string.Empty;
        }

        float step = stepMeters;
        if (float.IsNaN(step) || float.IsInfinity(step) || step < 0.1f)
        {
            step = 8f;
        }

        transform.position = Vector3.MoveTowards(transform.position, forcedTargetPosition, step);
        float dist = Vector3.Distance(transform.position, forcedTargetPosition);
        string pattern = status != null ? status.BehaviorPatternId : "PATTERN_BASIC_SLIME";
        bool fruit = !string.IsNullOrWhiteSpace(forcedTargetSpotId) &&
                     forcedTargetSpotId.StartsWith("MANA_FRUIT_", System.StringComparison.OrdinalIgnoreCase);
        if (dist <= 2.4f)
        {
            return fruit
                ? $"{pattern} が魔力果実ノードに到達 ({forcedTargetSpotId})"
                : $"{pattern} が杭に到達・攻撃開始 ({forcedTargetSpotId})";
        }

        return fruit
            ? $"{pattern} が魔力果実へ移動 残{dist:F1}m → {forcedTargetSpotId}"
            : $"{pattern} が結界杭へ侵攻 残{dist:F1}m → {forcedTargetSpotId}";
    }

    private void Update()
    {
        if (feedCooldown > 0f)
        {
            feedCooldown -= Time.deltaTime;
        }
    }

    /// <summary>環境パラメータから最適行動を選びます。</summary>
    public EnemyEcologyDecision Decide(
        VariableTimelineSeason season,
        float barrierEfficiencyPercent,
        float villageManaCrystalStock)
    {
        try
        {
            if (status == null)
            {
                status = GetComponent<EnemyStatusManager>();
            }

            if (status == null)
            {
                return Fallback(EnemyEcologyAction.Withdraw, "Status 欠損");
            }

            status.EnsureDefaults();
            EnemyActionStats action = status.Action;
            bool exhausted = action != null && action.IsExhausted;
            bool mpEmpty = status.IsMpDepleted;
            bool active = season == VariableTimelineSeason.Active ||
                          season == VariableTimelineSeason.Escalation;
            bool barrierLeaking = barrierEfficiencyPercent < BarrierLeakThreshold;
            bool richCrystal = villageManaCrystalStock > 8f;
            bool forced = hasForcedTarget;

            float assault = ScoreAssault(status, exhausted, mpEmpty, active);
            float breath = ScoreBreath(status, exhausted, mpEmpty, active);
            float recover = ScoreRecover(exhausted, action);
            float seek = ScoreSeekMana(status, barrierLeaking, richCrystal, active);
            float feed = ScoreFeed(status, barrierLeaking, richCrystal, mpEmpty);
            float withdraw = ScoreWithdraw(exhausted, mpEmpty, status);

            if (forced)
            {
                seek += 80f;
                assault += 40f;
            }

            EnemyEcologyAction chosen = EnemyEcologyAction.Assault;
            float best = assault;
            string reason = "接近強襲";

            best = Take(breath, EnemyEcologyAction.BreathCast, "ブレス／魔法", ref chosen, ref reason, best);
            best = Take(recover, EnemyEcologyAction.RecoverStance, "息整え・威嚇", ref chosen, ref reason, best);
            best = Take(seek, EnemyEcologyAction.SeekManaPrey,
                forced ? "Breach Point 強制誘導" : "結界／結晶を餌場と認識",
                ref chosen, ref reason, best);
            best = Take(feed, EnemyEcologyAction.FeedOnBarrier, "魔力源摂取", ref chosen, ref reason, best);
            best = Take(withdraw, EnemyEcologyAction.Withdraw, "間合い・警戒", ref chosen, ref reason, best);

            return new EnemyEcologyDecision
            {
                Action = chosen,
                Score = best,
                Reason = reason
            };
        }
        catch (System.Exception exception)
        {
            Debug.LogWarning($"[EnemyEcologyInstinctAI] Decide Safe-Fail: {exception.Message}");
            return Fallback(EnemyEcologyAction.RecoverStance, "例外フォールバック");
        }
    }

    /// <summary>決定した行動を実行し、スタミナ／MP／飢餓を更新します。</summary>
    public string Execute(
        EnemyEcologyDecision decision,
        float barrierEfficiencyPercent,
        VillageStorageMarket storage)
    {
        lastAction = decision.Action;
        if (status == null)
        {
            return "実行スキップ（Status なし）";
        }

        status.EnsureDefaults();
        EnemyActionStats stam = status.Action;

        switch (decision.Action)
        {
            case EnemyEcologyAction.RecoverStance:
                stam?.EnterExhaustion("生態AI・息整え");
                stam?.Restore(12f);
                status.AddManaHunger(2f);
                return "威嚇しながら距離を取る";

            case EnemyEcologyAction.Withdraw:
                stam?.Restore(6f);
                status.AddManaHunger(1.5f);
                return "警戒後退";

            case EnemyEcologyAction.SeekManaPrey:
                stam?.TrySpend(8f);
                status.AddManaHunger(4f);
                if (hasForcedTarget)
                {
                    return $"結界杭へ強制移動 {forcedTargetSpotId}（漏洩感知 {barrierEfficiencyPercent:F0}%）";
                }

                return $"結界へ移動（漏洩感知 {barrierEfficiencyPercent:F0}%）";

            case EnemyEcologyAction.FeedOnBarrier:
                return ExecuteFeed(storage, barrierEfficiencyPercent);

            case EnemyEcologyAction.BreathCast:
                return ExecuteBreath(stam);

            default:
                return ExecuteAssault(stam);
        }
    }

    private string ExecuteAssault(EnemyActionStats stam)
    {
        const float cost = 14f;
        if (stam == null || !stam.TrySpend(cost))
        {
            stam?.EnterExhaustion("連撃失敗");
            lastAction = EnemyEcologyAction.RecoverStance;
            return "強襲失敗→息整え";
        }

        status.AddManaHunger(3f);
        return $"物理強襲（Stamina -{cost:F0}）";
    }

    private string ExecuteBreath(EnemyActionStats stam)
    {
        const float baseMp = 12f;
        float eraCost = EnemyStatModifier.ScaleMpCost(baseMp);
        stam?.TrySpend(6f);
        if (!status.TryUseMP(baseMp))
        {
            lastAction = EnemyEcologyAction.Withdraw;
            return $"ブレス失敗（MP不足 / 必要 {eraCost:F1}）→警戒";
        }

        status.AddManaHunger(5f);
        return $"ブレス発動（MP -{eraCost:F1} 時代×{EnemyStatModifier.CurrentEraMpCostMultiplier:F2}）";
    }

    private string ExecuteFeed(VillageStorageMarket storage, float barrierPercent)
    {
        if (feedCooldown > 0f)
        {
            return "摂取クールダウン中";
        }

        float drained = 0f;
        if (storage != null && storage.HasCrystal)
        {
            drained = storage.WithdrawCrystal(0.8f);
        }

        float recover = 10f + drained * 8f + Mathf.Max(0f, (BarrierLeakThreshold - barrierPercent) * 0.15f);
        status.RestoreMP(recover);
        status.SetManaHunger(Mathf.Max(0f, status.ManaHunger - 22f));
        status.Action?.Restore(10f);
        feedCooldown = 1.2f;
        return drained > 0.01f
            ? $"結界核／結晶を摂取 Crystal-{drained:F1} MP+{recover:F0}"
            : $"漏洩魔力を吸収 MP+{recover:F0}（倉庫結晶なし）";
    }

    private static float ScoreAssault(EnemyStatusManager s, bool exhausted, bool mpEmpty, bool active)
    {
        if (exhausted)
        {
            return -20f;
        }

        float score = 30f + s.Territoriality * 0.25f + s.ThreatLevel * 4f;
        if (active)
        {
            score += 18f;
        }

        if (mpEmpty)
        {
            score += 8f;
        }

        return score;
    }

    private static float ScoreBreath(EnemyStatusManager s, bool exhausted, bool mpEmpty, bool active)
    {
        if (exhausted || mpEmpty)
        {
            return -10f;
        }

        float score = 22f + s.DangerLevel * 5f;
        if (active)
        {
            score += 12f;
        }

        if (s.ProfileKind == EnemyEcologyProfileKind.Dancer || s.ProfileKind == EnemyEcologyProfileKind.Slime)
        {
            score += 10f;
        }

        return score;
    }

    private static float ScoreRecover(bool exhausted, EnemyActionStats action)
    {
        if (!exhausted)
        {
            return action != null && action.CurrentStamina < 20f ? 40f : 5f;
        }

        return 80f;
    }

    private static float ScoreSeekMana(
        EnemyStatusManager s,
        bool barrierLeaking,
        bool richCrystal,
        bool active)
    {
        float score = s.ManaHunger * 0.7f;
        if (barrierLeaking)
        {
            score += 28f;
        }

        if (richCrystal)
        {
            score += 16f;
        }

        if (active)
        {
            score += 20f;
        }

        if (s.ManaHunger >= HighHunger)
        {
            score += 24f;
        }

        return score;
    }

    private static float ScoreFeed(
        EnemyStatusManager s,
        bool barrierLeaking,
        bool richCrystal,
        bool mpEmpty)
    {
        float score = 0f;
        if (mpEmpty)
        {
            score += 50f;
        }

        if (s.ManaHunger >= HighHunger)
        {
            score += 35f;
        }

        if (barrierLeaking || richCrystal)
        {
            score += 22f;
        }

        return score;
    }

    private static float ScoreWithdraw(bool exhausted, bool mpEmpty, EnemyStatusManager s)
    {
        float score = 8f;
        if (exhausted)
        {
            score += 25f;
        }

        if (mpEmpty && s.ManaHunger < 40f)
        {
            score += 15f;
        }

        return score;
    }

    private static float Take(
        float candidate,
        EnemyEcologyAction action,
        string reason,
        ref EnemyEcologyAction chosen,
        ref string chosenReason,
        float best)
    {
        if (candidate > best)
        {
            chosen = action;
            chosenReason = reason;
            return candidate;
        }

        return best;
    }

    private static EnemyEcologyDecision Fallback(EnemyEcologyAction action, string reason)
    {
        return new EnemyEcologyDecision
        {
            Action = action,
            Score = 1f,
            Reason = reason
        };
    }

    public static string ActionLabel(EnemyEcologyAction action)
    {
        switch (action)
        {
            case EnemyEcologyAction.BreathCast:
                return "ブレス";
            case EnemyEcologyAction.RecoverStance:
                return "息整え";
            case EnemyEcologyAction.SeekManaPrey:
                return "餌場探索";
            case EnemyEcologyAction.FeedOnBarrier:
                return "魔力摂取";
            case EnemyEcologyAction.Withdraw:
                return "警戒後退";
            default:
                return "強襲";
        }
    }
}

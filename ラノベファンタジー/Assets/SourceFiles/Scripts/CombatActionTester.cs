using UnityEngine;

/// <summary>
/// CombatActionFeedbackManager のコンテキストメニュー用デバッグ補助。
/// プレイ中の入力検知は DemoTimeLineManager が中央集権的に担当します。
/// </summary>
public class CombatActionTester : MonoBehaviour
{
    [Header("参照")]
    [SerializeField] private CombatActionFeedbackManager combatFeedback;

    private void Awake()
    {
        combatFeedback ??= CombatActionFeedbackManager.EnsureInstance();
    }

    private void Start()
    {
        combatFeedback ??= CombatActionFeedbackManager.EnsureInstance();
        Debug.Log(
            "<color=#FF8A65><b>[CombatActionTester]</b> 参照のみ配置（入力は DemoTimeLineManager が統治）</color>\n" +
            "  戦闘: マウスホイール / 左クリック / F / Ctrl / T（デバッグ敵攻撃）\n" +
            "  ContextMenu から U/I で敵プロファイルを手動切替可能");
    }

    /// <summary>U キー相当: 雑魚敵「森の害獣スライム」をセットします。</summary>
    [ContextMenu("Set Enemy: Mob Slime (U)")]
    public void SetMobEnemyProfile()
    {
        combatFeedback ??= CombatActionFeedbackManager.EnsureInstance();
        combatFeedback.SetTargetEnemy(EnemyAttackProfile.CreateForestSlimeMob());
        Debug.Log("<color=#98FB98>[CombatActionTester] 雑魚敵プロファイルをセット</color>");
    }

    /// <summary>I キー相当: ボス敵「大森林の王グリフォン」をセットします。</summary>
    [ContextMenu("Set Enemy: Boss Griffin (I)")]
    public void SetBossEnemyProfile()
    {
        combatFeedback ??= CombatActionFeedbackManager.EnsureInstance();
        combatFeedback.SetTargetEnemy(EnemyAttackProfile.CreateForestGriffinBoss());
        Debug.Log("<color=#FF6B6B>[CombatActionTester] 最凶ボスプロファイルをセット</color>");
    }

    /// <summary>T キー相当: 現在セットされている敵の乱数ディレイ攻撃を開始します。</summary>
    [ContextMenu("Trigger Enemy Attack (T)")]
    public void TriggerCurrentEnemyAttack()
    {
        combatFeedback ??= CombatActionFeedbackManager.EnsureInstance();
        combatFeedback.TriggerEnemyAttack();
        EnemyAttackProfile profile = combatFeedback.currentEnemyProfile;
        if (profile != null)
        {
            Debug.Log(
                $"<color=#FF8A65>[CombatActionTester] {profile.enemyName} の攻撃開始 " +
                $"（ディレイ {profile.minDelay:F2}〜{profile.maxDelay:F2}s）</color>");
        }
    }
}

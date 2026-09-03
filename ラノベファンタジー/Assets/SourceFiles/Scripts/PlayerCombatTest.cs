using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// PlayerStats の動作確認用スクリプト。
/// 左クリックで攻撃、Left Ctrl で回避のスタミナ消費をテストできます。
/// （Space はジャンプと被るため回避には使いません）
/// </summary>
public class PlayerCombatTest : MonoBehaviour
{
    [Header("参照")]
    [Tooltip("同じオブジェクトの PlayerStats をここにセットします")]
    [SerializeField] private PlayerStats stats;

    [Header("スタミナ消費量")]
    [SerializeField] private float attackStaminaCost = 15f;
    [SerializeField] private float dodgeStaminaCost = 20f;

    [Header("テスト用キー")]
    [Tooltip("回避テストに使うキー（Space はジャンプと被るので Left Ctrl 推奨）")]
    [SerializeField] private Key dodgeKey = Key.LeftCtrl;

    private void Reset()
    {
        // スクリプトを追加した瞬間に、同じオブジェクトから PlayerStats を自動取得
        stats = GetComponent<PlayerStats>();
    }

    private void Update()
    {
        // 左クリック：攻撃（スタミナ 15 消費）
        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
        {
            TryAttack();
        }

        // Left Ctrl：回避（スタミナ 20 消費）※ Space はジャンプと被る
        if (Keyboard.current != null && Keyboard.current[dodgeKey].wasPressedThisFrame)
        {
            TryDodge();
        }
    }

    private void TryAttack()
    {
        if (stats == null)
        {
            Debug.LogWarning("PlayerStats がセットされていません。Inspector で Stats に PlayerStats をドラッグしてください。");
            return;
        }

        if (!stats.CanUseStamina(attackStaminaCost))
        {
            Debug.Log("攻撃できません。スタミナが足りません。");
            return;
        }

        stats.UseStamina(attackStaminaCost);
        Debug.Log($"攻撃！ 残りスタミナ: {stats.CurrentStamina:F0}");
    }

    private void TryDodge()
    {
        if (stats == null)
        {
            return;
        }

        if (!stats.CanUseStamina(dodgeStaminaCost))
        {
            Debug.Log("回避できません。スタミナが足りません。");
            return;
        }

        stats.UseStamina(dodgeStaminaCost);
        Debug.Log($"回避！ 残りスタミナ: {stats.CurrentStamina:F0}");
    }
}

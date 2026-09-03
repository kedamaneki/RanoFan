using UnityEngine;

/// <summary>
/// プレイヤーステータス画面風の Rich Text コンソール出力（仮 UI）。
/// </summary>
public static class PlayerStatusLog
{
    private const string HeaderColor = "#4FC3F7";
    private const string VisibleColor = "#E0E0E0";
    private const string HiddenColor = "#CE93D8";
    private const string TrustColor = "#81C784";
    private const string EventColor = "#FFD54F";

    /// <summary>ステータス一覧をコンソールに表示します。</summary>
    public static void LogStatusSummary(PlayerStatusManager status)
    {
        if (status == null)
        {
            Debug.LogWarning("[PlayerStatusLog] PlayerStatusManager が null です。");
            return;
        }

        Debug.Log(
            $"<color={HeaderColor}><b>════════ プレイヤー・ステータス ════════</b></color>\n" +
            $"<color={VisibleColor}>【基本】 {status.MainJob} / {status.SubJob}  Lv.{status.Level}\n" +
            $"  MP: {status.CurrentMP:F0}/{status.MaxMP:F0}</color>\n" +
            $"<color={VisibleColor}>【表ボーナス】 攻+{status.DamageBonus}  守+{status.GuardBonus}  " +
            $"速+{status.SpeedBonus}  魔+{status.MagicBonus}  技+{status.TechnicalBonus}</color>\n" +
            $"<color={HiddenColor}>【隠し】 幸+{status.LuckBonus}  賢+{status.IntelBonus}  カルマ{status.KarmaValue}</color>\n" +
            $"<color={TrustColor}><b>【NPC信頼度】 係数 {status.GetNPCTrustFactor():F2}</b></color>\n" +
            $"<color={HeaderColor}>════════════════════════════════</color>");
    }

    /// <summary>シミュレーションイベントのログ</summary>
    public static void LogSimulationEvent(string message)
    {
        Debug.Log($"<color={EventColor}><b>[ステータス変動] {message}</b></color>");
    }
}

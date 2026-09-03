using UnityEngine;

/// <summary>
/// 魔法技術（魔導）関連の Rich Text コンソールログ。
/// 閃き（Inspiration）ログとは色・文言を分離しています。
/// </summary>
public static class MagicTechLog
{
    private const string RecordColor = "#7E57C2";
    private const string CastColor = "#E040FB";
    private const string FailColor = "#FF8A80";
    private const string EnchantColor = "#FF7043";

    /// <summary>魔導書解読・伝授による収録成功</summary>
    public static void LogMagicRecorded(MagicTechData magic, string sourceLabel)
    {
        if (magic == null)
        {
            return;
        }

        Debug.Log(
            $"<color={RecordColor}><b>【{sourceLabel}】術理を理解し、魔法技術『{magic.magicName}（{magic.magicID}）』をライブラリに収録した！</b></color>");
    }

    /// <summary>魔法発動成功</summary>
    public static void LogMagicCast(MagicTechData magic, float mpSpent, float remainingMp)
    {
        if (magic == null)
        {
            return;
        }

        string enchantNote = magic.duration > 0f
            ? "武器に魔力が宿った（エンチャント状態）。"
            : "術式が展開された。";

        Debug.Log(
            $"<color={CastColor}><b>【魔導発動】MPを {mpSpent:F0} 消費し、技術『{magic.magicName}』を展開！{enchantNote}</b></color> " +
            $"<color={EnchantColor}>(残MP {remainingMp:F0})</color>");
    }

    /// <summary>MP 不足などによる発動失敗</summary>
    public static void LogMagicCastFailed(MagicTechData magic, float requiredMp, float currentMp)
    {
        string name = magic != null ? magic.magicName : "不明な術式";
        Debug.Log(
            $"<color={FailColor}><b>【魔導失敗】MPが不足し、『{name}』は展開できなかった。（必要 {requiredMp:F0} / 現在 {currentMp:F0}）</b></color>");
    }

    /// <summary>既に収録済み</summary>
    public static void LogMagicAlreadyRecorded(MagicTechData magic)
    {
        if (magic == null)
        {
            return;
        }

        Debug.Log(
            $"<color={RecordColor}>【魔導書】『{magic.magicName}』は既にライブラリに収録済みです。</color>");
    }
}

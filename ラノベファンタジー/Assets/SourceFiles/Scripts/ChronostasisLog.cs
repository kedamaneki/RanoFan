using UnityEngine;

/// <summary>
/// 超加速・世界遅延（クロックアップ）関連の Rich Text コンソールログ。
/// </summary>
public static class ChronostasisLog
{
    private const string DespairColor = "#EF5350";
    private const string TechnicalColor = "#4FC3F7";
    private const string PhysicalColor = "#FFD54F";
    private const string SystemColor = "#B39DDB";

    /// <summary>敵領域への適応失敗（絶望）</summary>
    public static void LogAdaptationFailed()
    {
        Debug.Log(
            $"<color={DespairColor}><b>【絶望】敵の神速領域！プレイヤーは思考伝達が追いつかず、世界がスローのまま拘束された！</b></color>");
    }

    /// <summary>技術型（思考加速×肉体加速）による適応成功</summary>
    public static void LogTechnicalAdaptation(int speedBonus)
    {
        Debug.Log(
            $"<color={TechnicalColor}><b>【神速適応】思考加速×肉体加速が噛み合い、スローの世界で敵の刃を捉えた！（速+{speedBonus}）</b></color>");
    }

    /// <summary>フィジカル型（ステータス合計）による適応成功</summary>
    public static void LogPhysicalAdaptation(int adaptationScore, int threshold)
    {
        Debug.Log(
            $"<color={PhysicalColor}><b>【神域到達】圧倒的フィジカル（ステータス合計: {adaptationScore}）の暴力が伝達速度を凌駕！" +
            $"小細工なしに神速へ割り込んだ！（閾値 {threshold}）</b></color>");
    }

    /// <summary>世界遅延の発動</summary>
    public static void LogChronostasisTriggered(string caster, float timeScale)
    {
        Debug.Log(
            $"<color={SystemColor}><b>【クロックアップ】{caster} が超加速領域を展開（Time.timeScale = {timeScale}）</b></color>");
    }

    /// <summary>時間スケールの復元</summary>
    public static void LogTimeScaleReset()
    {
        Debug.Log(
            $"<color={SystemColor}><b>【クロックアップ】世界の時間が通常速度に復元された。</b></color>");
    }
}

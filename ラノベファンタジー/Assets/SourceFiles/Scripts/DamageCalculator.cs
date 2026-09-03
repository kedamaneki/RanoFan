using UnityEngine;

/// <summary>
/// プレイヤー・敵で共通のダメージ計算ルール（対称型設計）。
/// 計算式: ダメージ = 攻撃側 STR - 防御側 DEF（最低 1）
/// </summary>
public static class DamageCalculator
{
    /// <summary>
    /// 与ダメージを計算します。
    /// </summary>
    public static int Calculate(int attackerStrength, int defenderDefense)
    {
        return Mathf.Max(1, attackerStrength - defenderDefense);
    }
}

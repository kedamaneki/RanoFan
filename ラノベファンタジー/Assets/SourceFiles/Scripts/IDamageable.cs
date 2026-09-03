/// <summary>
/// ダメージを受けられるオブジェクト（プレイヤー・敵共通）のインターフェース。
/// </summary>
public interface IDamageable
{
    /// <summary>生存しているか</summary>
    bool IsAlive { get; }

    /// <summary>
    /// 攻撃側の STR を基準にダメージを受ける。
    /// </summary>
    /// <param name="attackerStrength">攻撃側の STR</param>
    /// <returns>実際に与えられたダメージ量</returns>
    int ReceiveDamage(int attackerStrength);
}

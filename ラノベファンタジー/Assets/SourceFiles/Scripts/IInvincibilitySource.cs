/// <summary>
/// 無敵状態を問い合わせるためのインターフェース。
/// CombatStats がプレイヤー被弾時に参照します。
/// </summary>
public interface IInvincibilitySource
{
    bool IsInvincible { get; }
}

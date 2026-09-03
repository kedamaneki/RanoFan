/// <summary>
/// 派生アクションの種別。AI が生成する技のカテゴリ判別に使用します。
/// </summary>
public enum DerivedActionType
{
    /// <summary>攻撃派生（通常攻撃の強化・派生技）</summary>
    Attack,

    /// <summary>回避派生（ローリング回避・無敵ステップなど）</summary>
    Evade
}

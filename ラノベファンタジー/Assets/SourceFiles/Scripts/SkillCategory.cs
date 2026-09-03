/// <summary>
/// スキル（技のカテゴリー）が扱うアクション種別。
/// PlayerController / PlayerAttackController がどちらの入力に割り当てるかを決めます。
/// </summary>
public enum SkillCategory
{
    /// <summary>近接攻撃系（片手剣術など）</summary>
    Attack,

    /// <summary>回避系（回避術など）</summary>
    Evade,

    /// <summary>魔法系（初級火魔法など）</summary>
    Magic
}

using System.Collections.Generic;

// =============================================================================
// 将来のスキル融合（複数便利枠の合体・ユニークスキル内包）用拡張ポイント
// =============================================================================

/// <summary>
/// 複数スキルを融合して新スキルを生成するロジックの差し込み口。
/// SkillEvolutionManager が参照し、未登録時は融合候補を返しません。
/// </summary>
public interface ISkillFusionResolver
{
    /// <summary>
    /// 所持スキル ID 群とプレイヤーログから、融合可能な結果スキル ID を列挙します。
    /// </summary>
    IReadOnlyList<string> ResolveFusionCandidates(
        IReadOnlyList<string> ownedSkillIds,
        PlayerHistoryTracker historyTracker);
}

/// <summary>融合未実装時のスタブ（常に空リスト）。</summary>
public sealed class NullSkillFusionResolver : ISkillFusionResolver
{
    public static readonly NullSkillFusionResolver Instance = new NullSkillFusionResolver();

    public IReadOnlyList<string> ResolveFusionCandidates(
        IReadOnlyList<string> ownedSkillIds,
        PlayerHistoryTracker historyTracker)
    {
        return System.Array.Empty<string>();
    }
}

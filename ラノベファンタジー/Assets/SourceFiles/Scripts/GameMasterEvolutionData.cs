using System;
using UnityEngine;

// =============================================================================
// 進化システム共通データ（スキル / 魔法 / 将来のアイテム進化）
// 合成（Combination）とは別軸：特定条件達成で nextId へ段階変化するリンク
// =============================================================================

/// <summary>戦歴ログ・クラフトログ等、進化分岐の動的提示に使う閾値条件。</summary>
[Serializable]
public class SkillEvolutionCriteria
{
    [Tooltip("参照する立ち回り／作業ログ種別（例: CombatLog_WaterFlow, GeneralPrerequisite）")]
    public string requiredLogType;

    [Tooltip("requiredLogType の累計がこの値以上で分岐候補に載る")]
    public int requiredValue;

    public void Sanitize()
    {
        requiredLogType = requiredLogType?.Trim() ?? string.Empty;
        requiredValue = Mathf.Max(0, requiredValue);
    }

    public bool IsGeneralPrerequisite()
    {
        return string.IsNullOrWhiteSpace(requiredLogType) ||
               string.Equals(requiredLogType, "GeneralPrerequisite", StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// スキル進化条件。
/// 既存の技ツリー（Arts/derivatives）とは別に、スキル全体が上位スキルへ変わる際に使用します。
/// </summary>
[Serializable]
public class SkillEvolutionData
{
    [Tooltip("進化（上位スキルへの段階変化）が可能か")]
    public bool isEvolvable;

    [Tooltip("進化に必要な熟練度またはスキルレベル")]
    public int requiredLevel;

    [Tooltip("歴史フラグ・イベント等の解放条件キー（空ならレベルのみ）")]
    public string conditionFlag;

    [Tooltip("戦歴ログ等の閾値条件（動的分岐提示用）")]
    public SkillEvolutionCriteria evolutionCriteria = new SkillEvolutionCriteria();

    [Tooltip("進化先スキル ID。最上位未実装時は空文字でチェーン拡張を予約")]
    public string nextSkillId;

    /// <summary>進化先が定義済みか（空 nextSkillId は将来の最上位チェーン用）。</summary>
    public bool HasNextSkill =>
        isEvolvable && !string.IsNullOrWhiteSpace(nextSkillId);

    /// <summary>将来の最上位スキルへ繋げるチェーン拡張スロットが空か。</summary>
    public bool HasOpenEvolutionChainSlot =>
        isEvolvable && string.IsNullOrWhiteSpace(nextSkillId);

    /// <summary>パース後の欠損を補正します。</summary>
    public void Sanitize(string ownerLabel)
    {
        requiredLevel = Mathf.Max(0, requiredLevel);
        conditionFlag = conditionFlag?.Trim() ?? string.Empty;
        nextSkillId = nextSkillId?.Trim() ?? string.Empty;
        evolutionCriteria ??= new SkillEvolutionCriteria();
        evolutionCriteria.Sanitize();
    }
}

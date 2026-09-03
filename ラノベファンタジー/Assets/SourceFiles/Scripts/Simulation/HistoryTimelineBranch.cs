using System;
using System.Collections.Generic;
using UnityEngine;

// =============================================================================
// 歴史ライン分岐データ — HIST_ 正史アンカー + ALT_ 局所波及（250 ターン）
//
// 【設計: アンカー固定＆局所波及モデル】
// - 正史 HIST_ の 250 ターン値は再計算せずベースとして保持（HistoryBranchManager 側）
// - プレイヤー介入時のみ HistoryBranchNode + MacroParamDelta をレイヤー追加
// - 介入ターン T の改変は T+1 以降へ累積波及（T 当日は正史アンカー維持）
// 連携: HistoryBranchManager / HistoryFlagRegistry / EraContextResolver
// =============================================================================
/// <summary>1 件の重大イベント改変ノード。</summary>
[Serializable]
public sealed class HistoryBranchNode
{
    public int turn = 1;
    public int nationId = 1;
    public string keyEventId = string.Empty;
    public string outcomeFlag = string.Empty;
    public MacroParamDelta delta = new MacroParamDelta();

    public bool IsValid =>
        turn >= EraContextResolver.MinTurn &&
        turn <= EraContextResolver.MaxTurn &&
        nationId > 0 &&
        !string.IsNullOrWhiteSpace(keyEventId);
}

/// <summary>ターン別マクロパラメータ差分（乗数・加算・生存上書き）。</summary>
[Serializable]
public sealed class MacroParamDelta
{
    public float powerMultiplier = 1f;
    public float barrierEfficiencyDelta;
    public float threatMultiplier = 1f;
    public bool isSurvivalOverridden;
    public bool survivalValue = true;

    public static MacroParamDelta Identity =>
        new MacroParamDelta
        {
            powerMultiplier = 1f,
            barrierEfficiencyDelta = 0f,
            threatMultiplier = 1f,
            isSurvivalOverridden = false,
            survivalValue = true
        };

    public MacroParamDelta Clone()
    {
        return new MacroParamDelta
        {
            powerMultiplier = powerMultiplier,
            barrierEfficiencyDelta = barrierEfficiencyDelta,
            threatMultiplier = threatMultiplier,
            isSurvivalOverridden = isSurvivalOverridden,
            survivalValue = survivalValue
        };
    }

    /// <summary>other を乗算・加算合成します。</summary>
    public void Accumulate(MacroParamDelta other)
    {
        if (other == null)
        {
            return;
        }

        powerMultiplier *= SanitizeMultiplier(other.powerMultiplier);
        barrierEfficiencyDelta += other.barrierEfficiencyDelta;
        threatMultiplier *= SanitizeMultiplier(other.threatMultiplier);
        if (other.isSurvivalOverridden)
        {
            isSurvivalOverridden = true;
            survivalValue = other.survivalValue;
        }
    }

    public void SanitizeInPlace()
    {
        powerMultiplier = SanitizeMultiplier(powerMultiplier);
        threatMultiplier = SanitizeMultiplier(threatMultiplier);
        barrierEfficiencyDelta = Mathf.Clamp(barrierEfficiencyDelta, -0.95f, 0.95f);
    }

    private static float SanitizeMultiplier(float value)
    {
        if (float.IsNaN(value) || float.IsInfinity(value) || value <= 0f)
        {
            return 1f;
        }

        return Mathf.Clamp(value, 0.05f, 5f);
    }
}

/// <summary>ALT_ 歴史ライン分岐（ノード列 + ターン別累積補正）。</summary>
[Serializable]
public sealed class HistoryTimelineBranch
{
    public string branchId = string.Empty;
    public int baseStartTurn = 1;
    public int primaryNationId = 1;
    public string displayName = string.Empty;
    public bool isCommittedMainStory;
    public int committedAtTurn;
    public string parentBranchId = string.Empty;
    public int forkTurn;
    public List<string> childBranchIds = new List<string>();
    public List<HistoryBranchNode> nodes = new List<HistoryBranchNode>();

    [NonSerialized]
    private Dictionary<int, MacroParamDelta> turnParamDeltasCache;

    public IReadOnlyList<HistoryBranchNode> Nodes => nodes;

    /// <summary>ターン別累積補正マップ（介入翌ターン以降のみエントリあり）。</summary>
    public bool TryGetTurnParamDelta(int turn, out MacroParamDelta delta)
    {
        delta = MacroParamDelta.Identity;
        if (turnParamDeltasCache == null || turnParamDeltasCache.Count == 0)
        {
            RebuildTurnParamDeltas();
        }

        return turnParamDeltasCache != null &&
               turnParamDeltasCache.TryGetValue(turn, out delta) &&
               delta != null;
    }

    /// <summary>指定ターンより前に介入されたノードが存在するか（翌ターン波及判定）。</summary>
    public bool HasEffectiveNodeBefore(int turn)
    {
        if (nodes == null || nodes.Count == 0)
        {
            return false;
        }

        for (int i = 0; i < nodes.Count; i++)
        {
            HistoryBranchNode node = nodes[i];
            if (node != null && node.turn < turn)
            {
                return true;
            }
        }

        return false;
    }

    public void AddNode(HistoryBranchNode node)
    {        if (node == null || !node.IsValid)
        {
            return;
        }

        nodes ??= new List<HistoryBranchNode>();
        nodes.Add(node);
        if (baseStartTurn <= 0 || node.turn < baseStartTurn)
        {
            baseStartTurn = node.turn;
        }

        primaryNationId = node.nationId;
        RebuildTurnParamDeltas();
    }

    /// <summary>介入ターン以降 250 ターンへ累積補正マップを再構築します。</summary>
    public void RebuildTurnParamDeltas()
    {
        turnParamDeltasCache ??= new Dictionary<int, MacroParamDelta>();
        turnParamDeltasCache.Clear();

        if (nodes == null || nodes.Count == 0)
        {
            return;
        }

        nodes.Sort((a, b) => a.turn.CompareTo(b.turn));
        MacroParamDelta running = MacroParamDelta.Identity;

        int cursor = EraContextResolver.MinTurn;
        int nodeIndex = 0;
        while (cursor <= EraContextResolver.MaxTurn)
        {
            while (nodeIndex < nodes.Count && nodes[nodeIndex].turn < cursor)
            {
                MacroParamDelta nodeDelta = nodes[nodeIndex].delta ?? MacroParamDelta.Identity;
                nodeDelta.SanitizeInPlace();
                running.Accumulate(nodeDelta);
                running.SanitizeInPlace();
                nodeIndex++;
            }

            if (cursor > baseStartTurn)
            {
                turnParamDeltasCache[cursor] = running.Clone();
            }
            cursor++;
        }
    }

    public bool TryGetDeltaForTurn(int turn, int nationId, out MacroParamDelta delta)
    {
        delta = MacroParamDelta.Identity;
        if (nationId != primaryNationId || !HasEffectiveNodeBefore(turn))
        {
            return false;
        }

        if (turnParamDeltasCache == null || turnParamDeltasCache.Count == 0)
        {
            RebuildTurnParamDeltas();
        }

        if (turnParamDeltasCache != null && turnParamDeltasCache.TryGetValue(turn, out MacroParamDelta found))
        {
            delta = found ?? MacroParamDelta.Identity;
            return true;
        }

        return false;
    }

    /// <summary>レジストリ保存用の深いコピー。</summary>
    public HistoryTimelineBranch Clone()
    {
        HistoryTimelineBranch copy = new HistoryTimelineBranch
        {
            branchId = branchId,
            baseStartTurn = baseStartTurn,
            primaryNationId = primaryNationId,
            displayName = displayName,
            isCommittedMainStory = isCommittedMainStory,
            committedAtTurn = committedAtTurn,
            parentBranchId = parentBranchId,
            forkTurn = forkTurn,
            childBranchIds = childBranchIds != null ? new List<string>(childBranchIds) : new List<string>(),
            nodes = new List<HistoryBranchNode>()
        };

        if (nodes != null)
        {
            for (int i = 0; i < nodes.Count; i++)
            {
                HistoryBranchNode node = nodes[i];
                if (node == null)
                {
                    continue;
                }

                copy.nodes.Add(new HistoryBranchNode
                {
                    turn = node.turn,
                    nationId = node.nationId,
                    keyEventId = node.keyEventId,
                    outcomeFlag = node.outcomeFlag,
                    delta = node.delta?.Clone() ?? MacroParamDelta.Identity
                });
            }
        }

        copy.RebuildTurnParamDeltas();
        return copy;
    }

    /// <summary>指定ターンまで（T1〜targetTurn 含む）の介入ノードを返します。</summary>
    public List<HistoryBranchNode> GetNodesUpToTurn(int targetTurn)
    {
        List<HistoryBranchNode> result = new List<HistoryBranchNode>();
        if (nodes == null || nodes.Count == 0)
        {
            return result;
        }

        int maxTurn = Mathf.Clamp(targetTurn, EraContextResolver.MinTurn, EraContextResolver.MaxTurn);
        for (int i = 0; i < nodes.Count; i++)
        {
            HistoryBranchNode node = nodes[i];
            if (node != null &&
                node.turn >= EraContextResolver.MinTurn &&
                node.turn <= maxTurn)
            {
                result.Add(node);
            }
        }

        return result;
    }

    /// <summary>
    /// targetTurn 時点で有効な累積 MacroParamDelta（T+1 波及ルール、T1〜targetTurn のノードのみ）。
    /// </summary>
    public MacroParamDelta AccumulateParamDeltaUpToTurn(int targetTurn, int nationId)
    {
        MacroParamDelta running = MacroParamDelta.Identity;
        if (nationId != primaryNationId || nodes == null || nodes.Count == 0)
        {
            return running;
        }

        int probeTurn = Mathf.Max(targetTurn, EraContextResolver.MinTurn);
        nodes.Sort((a, b) => a.turn.CompareTo(b.turn));
        for (int i = 0; i < nodes.Count; i++)
        {
            HistoryBranchNode node = nodes[i];
            if (node?.delta == null)
            {
                continue;
            }

            if (node.turn >= EraContextResolver.MinTurn && node.turn < probeTurn)
            {
                MacroParamDelta nodeDelta = node.delta.Clone();
                nodeDelta.SanitizeInPlace();
                running.Accumulate(nodeDelta);
                running.SanitizeInPlace();
            }
        }

        return running;
    }
}
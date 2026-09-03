using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

// =============================================================================
// 数値スパイク → 偉人 / ネームド魔獣の自動抽出
// 連携: MicroHistoryTimelineTimelineEngine / EnemyBehaviorProfile
// =============================================================================

/// <summary>防衛・革新の特異点から起こる偉人。</summary>
[Serializable]
public sealed class HeroData
{
    public string id = string.Empty;
    public string displayName = string.Empty;
    public string title = string.Empty;
    public string biography = string.Empty;
    public string conditionFlag = string.Empty;
    public string sourceCategory = string.Empty;
    public int nationId;
    public int turn;
    public float militaryLoss;
    public float territoryGain;
}

/// <summary>突破・高脅威の特異点から起こるネームド魔獣。</summary>
[Serializable]
public sealed class NamedBeastData
{
    public string id = string.Empty;
    public string displayName = string.Empty;
    public string behaviorPatternId = string.Empty;
    public string conditionFlag = string.Empty;
    public string sourceCategory = string.Empty;
    public int nationId;
    public int turn;
    public float threatIndex;
    public float militaryLoss;
    public string region = string.Empty;
}

/// <summary>偉人とネームド魔獣の抽出結果。</summary>
[Serializable]
public sealed class SingularExtractionResult
{
    public HeroData hero;
    public NamedBeastData namedBeast;
    public bool usedSafeFailFallback;
}

/// <summary>
/// 国家×ターンの数値スパイクから偉人・ネームド魔獣を生成します。
/// ログ欠損時は空結果（例外なし）。
/// </summary>
public static class SingularPointExtractor
{
    public const string PatternBasicSlime = "PATTERN_BASIC_SLIME";
    public const string PatternAgileStalker = "PATTERN_AGILE_STALKER";
    public const string PatternHeavyTitan = "PATTERN_HEAVY_TITAN";
    public const string PatternFeintDancer = "PATTERN_FEINT_DANCER";

    public const float HeroMilitaryLossThreshold = 5f;
    public const float HighThreatThreshold = 2f;

    public static SingularExtractionResult Extract(int nationId, int turn)
    {
        MicroHistoryTimeline timeline = MicroHistoryTimelineTimelineEngine.BuildTimeline(nationId, turn);
        return Extract(timeline);
    }

    public static SingularExtractionResult Extract(MicroHistoryTimeline timeline)
    {
        SingularExtractionResult result = new SingularExtractionResult();
        if (timeline == null || timeline.usedSafeFailFallback)
        {
            result.usedSafeFailFallback = true;
            return result;
        }

        result.hero = TryBuildHero(timeline);
        result.namedBeast = TryBuildNamedBeast(timeline);
        return result;
    }

    private static HeroData TryBuildHero(MicroHistoryTimeline timeline)
    {
        MacroChronicleLogEvent defense = FindBest(
            timeline.sourceEvents,
            "monster_defense",
            e => e.militaryLoss + e.territoryGain);
        MacroChronicleLogEvent innovation = FindBest(
            timeline.sourceEvents,
            "innovation",
            e => 1f);

        bool defenseHero = defense != null &&
                           (defense.militaryLoss >= HeroMilitaryLossThreshold || defense.territoryGain > 0f);
        if (!defenseHero && innovation == null)
        {
            return null;
        }

        MacroChronicleLogEvent source = defenseHero ? defense : innovation;
        string regionLabel = RegionDisplayName(timeline.region);
        bool fromInnovation = !defenseHero;
        string flag = BuildFlag(timeline.nationId, timeline.turn, fromInnovation ? "INNOVATOR" : "HERO");
        return new HeroData
        {
            id = $"HERO_N{timeline.nationId:000}_T{timeline.turn:000}",
            displayName = fromInnovation ? $"{regionLabel}の工法改変者" : $"{regionLabel}の結界守",
            title = fromInnovation ? "障壁工法の革新者" : $"{regionLabel}の守将",
            biography = fromInnovation
                ? $"{regionLabel}で結界運用の手順が改められ、以後の維持率が底上げされた。"
                : BuildDefenseBiography(timeline, source),
            conditionFlag = flag,
            sourceCategory = source.category,
            nationId = timeline.nationId,
            turn = timeline.turn,
            militaryLoss = source.militaryLoss,
            territoryGain = source.territoryGain
        };
    }

    private static NamedBeastData TryBuildNamedBeast(MicroHistoryTimeline timeline)
    {
        MacroChronicleLogEvent attack = FindBest(
            timeline.sourceEvents,
            "monster_attack",
            e => e.territoryLoss + e.militaryLoss);
        MacroChronicleLogEvent defense = FindBest(
            timeline.sourceEvents,
            "monster_defense",
            e => e.militaryLoss);

        float threat = ReadThreat(timeline);
        bool spike = attack != null ||
                     (defense != null && defense.militaryLoss >= HeroMilitaryLossThreshold) ||
                     threat >= HighThreatThreshold;
        if (!spike)
        {
            return null;
        }

        string pattern = BindBehaviorPattern(attack, defense, threat);
        string regionLabel = RegionDisplayName(timeline.region);
        MacroChronicleLogEvent source = attack ?? defense;
        return new NamedBeastData
        {
            id = $"BEAST_N{timeline.nationId:000}_T{timeline.turn:000}",
            displayName = NameForPattern(pattern, regionLabel),
            behaviorPatternId = pattern,
            conditionFlag = BuildFlag(timeline.nationId, timeline.turn, "NAMED_BEAST"),
            sourceCategory = source != null ? source.category : "threat_environment",
            nationId = timeline.nationId,
            turn = timeline.turn,
            threatIndex = threat,
            militaryLoss = source != null ? source.militaryLoss : 0f,
            region = timeline.region ?? string.Empty
        };
    }

    private static string BindBehaviorPattern(
        MacroChronicleLogEvent attack,
        MacroChronicleLogEvent defense,
        float threat)
    {
        if (attack != null && attack.territoryLoss >= 4f)
        {
            return PatternHeavyTitan;
        }

        if (attack != null)
        {
            return PatternBasicSlime;
        }

        if (defense != null && defense.militaryLoss >= 7f)
        {
            return PatternAgileStalker;
        }

        if (threat >= 2.4f)
        {
            return PatternFeintDancer;
        }

        return PatternBasicSlime;
    }

    private static string NameForPattern(string pattern, string regionLabel)
    {
        if (pattern == PatternHeavyTitan)
        {
            return $"{regionLabel}を裂く甲殻巨獣";
        }

        if (pattern == PatternAgileStalker)
        {
            return $"{regionLabel}に走る霧影の追跡獣";
        }

        if (pattern == PatternFeintDancer)
        {
            return $"{regionLabel}の遅撃異形";
        }

        return $"{regionLabel}の粘性異形";
    }

    private static string BuildDefenseBiography(MicroHistoryTimeline timeline, MacroChronicleLogEvent source)
    {
        float military = source.militaryLoss;
        float territory = source.territoryGain;
        string barrier = timeline.geoAtTurn != null
            ? timeline.geoAtTurn.barrierEfficiency.ToString("F2", CultureInfo.InvariantCulture)
            : "—";
        return
            $"脅威波を障壁網で拒否した。軍事 {military:F1} を失いながら生存圏は {territory:+0.0;-0.0}、" +
            $"結界効率は {barrier} を維持した。";
    }

    private static string RegionDisplayName(string region)
    {
        if (string.IsNullOrWhiteSpace(region))
        {
            return "辺境";
        }

        if (region.Contains("西"))
        {
            return "最西端の統合王国周辺";
        }

        if (region.Contains("東"))
        {
            return "東方の魔導生産圏";
        }

        if (region.Contains("中央"))
        {
            return "中央海平原";
        }

        if (region.Contains("南"))
        {
            return "山岳要塞帯";
        }

        if (region.Contains("北"))
        {
            return "極寒針葉の前線";
        }

        return "辺境";
    }

    private static string BuildFlag(int nationId, int turn, string kind)
    {
        return $"HIST_NATION_{nationId:000}_GEO_TURN_{turn:000}_{kind}";
    }

    private static float ReadThreat(MicroHistoryTimeline timeline)
    {
        if (timeline.worldThreatIndex > 0f)
        {
            return timeline.worldThreatIndex;
        }

        if (timeline.sourceEvents != null)
        {
            for (int i = 0; i < timeline.sourceEvents.Count; i++)
            {
                if (timeline.sourceEvents[i].globalThreatIndex > 0f)
                {
                    return timeline.sourceEvents[i].globalThreatIndex;
                }
            }
        }

        if (timeline.phases != null)
        {
            for (int i = 0; i < timeline.phases.Length; i++)
            {
                if (timeline.phases[i].threatLevel > 0f)
                {
                    return timeline.phases[i].threatLevel / 1.05f;
                }
            }
        }

        return 0f;
    }

    private static MacroChronicleLogEvent FindBest(
        List<MacroChronicleLogEvent> events,
        string category,
        Func<MacroChronicleLogEvent, float> score)
    {
        if (events == null)
        {
            return null;
        }

        MacroChronicleLogEvent best = null;
        float bestScore = float.NegativeInfinity;
        for (int i = 0; i < events.Count; i++)
        {
            MacroChronicleLogEvent ev = events[i];
            if (ev == null || !string.Equals(ev.category, category, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            float s = score(ev);
            if (s > bestScore)
            {
                bestScore = s;
                best = ev;
            }
        }

        return best;
    }
}

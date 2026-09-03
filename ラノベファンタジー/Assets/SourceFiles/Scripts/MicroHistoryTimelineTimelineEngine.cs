using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

// =============================================================================
// マクロログ → 24位相・4期可変マイクロ歴史タイムライン
// 入力: macro_chronicle_log.json / macro_chronicle_geo.json
// =============================================================================

/// <summary>1 マクロ年を構成する 4 期間。</summary>
public enum VariableTimelineSeason
{
    Active = 0,
    Deescalation = 1,
    Dormant = 2,
    Escalation = 3
}

/// <summary>category 出現頻度。</summary>
[Serializable]
public sealed class MacroCategoryFrequency
{
    public string category = string.Empty;
    public int count;
    public float ratio;
}

/// <summary>4 期の位相枠（合計 24）。</summary>
[Serializable]
public sealed class VariablePhaseDistribution
{
    public int activePhases = 6;
    public int deescalationPhases = 6;
    public int dormantPhases = 6;
    public int escalationPhases = 6;
    public bool usedSafeFailFallback;

    public int TotalPhases =>
        activePhases + deescalationPhases + dormantPhases + escalationPhases;

    public static VariablePhaseDistribution EvenFallback()
    {
        return new VariablePhaseDistribution { usedSafeFailFallback = true };
    }
}

/// <summary>24 位相のうち 1 枠。365 日へ展開可能な基礎パラメータ。</summary>
[Serializable]
public sealed class MicroPhaseSlot
{
    public int phaseIndex;
    public VariableTimelineSeason season;
    public int dayStart;
    public int dayEnd;
    public float barrierMaintenanceRate;
    public float threatLevel;
    public string[] eventLogs = Array.Empty<string>();
}

/// <summary>国家×ターンのマイクロ年表。</summary>
[Serializable]
public sealed class MicroHistoryTimeline
{
    public int nationId;
    public int turn;
    public string nationName = string.Empty;
    public string region = string.Empty;
    public MacroCategoryFrequency[] categoryFrequencies = Array.Empty<MacroCategoryFrequency>();
    public VariablePhaseDistribution distribution = VariablePhaseDistribution.EvenFallback();
    public MicroPhaseSlot[] phases = Array.Empty<MicroPhaseSlot>();
    public MacroChronicleNationSnapshot geoAtTurn;
    public MacroChronicleNationSnapshot geoPrevious;
    public List<MacroChronicleLogEvent> sourceEvents = new List<MacroChronicleLogEvent>();
    public float worldThreatIndex;
    public bool usedSafeFailFallback;
}

/// <summary>ログ 1 件。</summary>
[Serializable]
public sealed class MacroChronicleLogEvent
{
    public int turn;
    public string eventText = string.Empty;
    public string category = string.Empty;
    public int nationId;
    public int[] nationIds = Array.Empty<int>();
    public string region = string.Empty;
    public float militaryLoss;
    public float territoryGain;
    public float territoryLoss;
    public float economyLoss;
    public float globalThreatIndex;
    public string cyclePhase = string.Empty;

    public bool ConcernsNation(int id)
    {
        if (nationId == id)
        {
            return true;
        }

        if (nationIds == null)
        {
            return false;
        }

        for (int i = 0; i < nationIds.Length; i++)
        {
            if (nationIds[i] == id)
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>geo 快照の 1 国家。</summary>
[Serializable]
public sealed class MacroChronicleNationSnapshot
{
    public int id;
    public string name = string.Empty;
    public string region = string.Empty;
    public float lat;
    public float lng;
    public bool alive = true;
    public float territory;
    public float power;
    public float economy;
    public float military;
    public float magic;
    public float barrierEfficiency = 1f;
    public int bornTurn;
    public int diedTurn = -1;
}

/// <summary>マクロイベントから 4 期可変タイムライン（24 位相）を組み立てます。</summary>
public static class MicroHistoryTimelineTimelineEngine
{
    public const int PhaseCount = 24;
    public const int DaysPerYear = 365;
    public const int ActivePhaseMin = 8;
    public const int ActivePhaseMax = 12;
    public const int DefaultNationId = 1;
    public const int DefaultTurn = 1;
    public const string DefaultLogFileName = "macro_chronicle_log.json";
    public const string DefaultGeoFileName = "macro_chronicle_geo.json";

    public static MicroHistoryTimeline BuildTimeline(int nationId, int turn)
    {
        return BuildTimeline(nationId, turn, ResolveLogPath(), ResolveGeoPath());
    }

    public static MicroHistoryTimeline BuildTimeline(int nationId, int turn, string logPath, string geoPath)
    {
        if (nationId < 1 || turn < 1)
        {
            Debug.LogWarning(
                $"[MicroHistoryTimeline] 無効な指定 nation={nationId} turn={turn}。均等 4 期へフォールバックします。");
            return BuildFallbackTimeline(Mathf.Max(1, nationId), Mathf.Max(1, turn));
        }

        List<MacroChronicleLogEvent> turnEvents = MacroChronicleJsonScan.ReadTurnEvents(logPath, turn);
        if (turnEvents == null || turnEvents.Count == 0)
        {
            Debug.LogWarning(
                $"[MicroHistoryTimeline] ターン {turn} のログがありません。均等 4 期へフォールバックします。");
            return BuildFallbackTimeline(nationId, turn);
        }

        MacroChronicleNationSnapshot geoNow = MacroChronicleJsonScan.ReadNationAtTurn(geoPath, turn, nationId);
        MacroChronicleNationSnapshot geoPrev = MacroChronicleJsonScan.ReadNationAtTurn(geoPath, turn - 1, nationId);
        if (geoNow == null)
        {
            Debug.LogWarning(
                $"[MicroHistoryTimeline] geo に国家 {nationId:000} / ターン {turn} がありません。均等 4 期へフォールバックします。");
            return BuildFallbackTimeline(nationId, turn);
        }

        List<MacroChronicleLogEvent> nationEvents = FilterNationEvents(turnEvents, nationId);
        VariablePhaseDistribution distribution = GetVariablePhaseDistribution(turnEvents, nationEvents);
        float worldThreat = 0f;
        for (int i = 0; i < turnEvents.Count; i++)
        {
            if (turnEvents[i].globalThreatIndex > worldThreat)
            {
                worldThreat = turnEvents[i].globalThreatIndex;
            }
        }

        return new MicroHistoryTimeline
        {
            nationId = nationId,
            turn = turn,
            nationName = string.IsNullOrEmpty(geoNow.name) ? $"国家{nationId:000}" : geoNow.name,
            region = geoNow.region ?? string.Empty,
            categoryFrequencies = ComputeFrequencies(turnEvents),
            distribution = distribution,
            phases = BuildPhaseSlots(distribution, turnEvents, nationEvents, geoNow),
            geoAtTurn = geoNow,
            geoPrevious = geoPrev,
            sourceEvents = nationEvents,
            worldThreatIndex = worldThreat,
            usedSafeFailFallback = false
        };
    }

    /// <summary>
    /// 活性 / 衰退中間 / 休眠 / 兆候中間 の 24 位相配分。
    /// monster_attack / monster_defense が多いほど活性期を 8〜12 位相へ伸ばします。
    /// </summary>
    public static VariablePhaseDistribution GetVariablePhaseDistribution(
        List<MacroChronicleLogEvent> turnEvents,
        List<MacroChronicleLogEvent> nationEvents)
    {
        if (turnEvents == null || turnEvents.Count == 0)
        {
            return VariablePhaseDistribution.EvenFallback();
        }

        CountCombat(turnEvents, out int worldAttack, out int worldDefense, out int worldInnovation, out int worldTotal);
        CountCombat(nationEvents, out int nationAttack, out int nationDefense, out int nationInnovation, out int nationTotal);
        if (worldTotal <= 0 && nationTotal <= 0)
        {
            return VariablePhaseDistribution.EvenFallback();
        }

        float worldCombatRatio = (worldAttack + worldDefense) / Mathf.Max(1f, worldTotal);
        float nationCombatRatio = nationTotal > 0
            ? (nationAttack + nationDefense) / (float)nationTotal
            : worldCombatRatio;
        float combatBlend = nationTotal > 0
            ? nationCombatRatio * 0.55f + worldCombatRatio * 0.45f
            : worldCombatRatio;

        int active = Mathf.Clamp(
            Mathf.RoundToInt(Mathf.Lerp(ActivePhaseMin, ActivePhaseMax, combatBlend)),
            ActivePhaseMin,
            ActivePhaseMax);

        float worldInnovRatio = worldInnovation / Mathf.Max(1f, worldTotal);
        float nationInnovRatio = nationTotal > 0 ? nationInnovation / (float)nationTotal : worldInnovRatio;
        float dormantWeight = Mathf.Clamp01((1f - combatBlend) * 0.7f + Mathf.Max(worldInnovRatio, nationInnovRatio) * 0.5f);
        int dormant = Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(4f, 10f, dormantWeight)), 4, 10);
        if (active + dormant > 20)
        {
            dormant = 20 - active;
        }

        int remainder = PhaseCount - active - dormant;
        if (remainder < 4)
        {
            dormant = Mathf.Max(4, dormant - (4 - remainder));
            remainder = PhaseCount - active - dormant;
        }

        int worldCombat = worldAttack + worldDefense;
        float worldDefenseShare = worldCombat > 0 ? worldDefense / (float)worldCombat : 0.5f;
        int nationCombat = nationAttack + nationDefense;
        float nationDefenseShare = nationCombat > 0 ? nationDefense / (float)nationCombat : worldDefenseShare;
        float defenseMix = nationTotal > 0
            ? nationDefenseShare * 0.6f + worldDefenseShare * 0.4f
            : worldDefenseShare;

        int deescalation = Mathf.Clamp(
            Mathf.RoundToInt(remainder * (0.35f + 0.3f * defenseMix)),
            2,
            remainder - 2);
        return new VariablePhaseDistribution
        {
            activePhases = active,
            deescalationPhases = deescalation,
            dormantPhases = dormant,
            escalationPhases = remainder - deescalation,
            usedSafeFailFallback = false
        };
    }

    public static MicroPhaseSlot[] ExpandToDays(MicroHistoryTimeline timeline)
    {
        if (timeline?.phases == null || timeline.phases.Length == 0)
        {
            return Array.Empty<MicroPhaseSlot>();
        }

        List<MicroPhaseSlot> days = new List<MicroPhaseSlot>(DaysPerYear);
        for (int i = 0; i < timeline.phases.Length; i++)
        {
            MicroPhaseSlot phase = timeline.phases[i];
            for (int day = phase.dayStart; day <= phase.dayEnd; day++)
            {
                days.Add(new MicroPhaseSlot
                {
                    phaseIndex = phase.phaseIndex,
                    season = phase.season,
                    dayStart = day,
                    dayEnd = day,
                    barrierMaintenanceRate = phase.barrierMaintenanceRate,
                    threatLevel = phase.threatLevel,
                    eventLogs = day == phase.dayStart ? phase.eventLogs : Array.Empty<string>()
                });
            }
        }

        return days.ToArray();
    }

    public static VariableTimelineSeason SeasonAtPhase(VariablePhaseDistribution distribution, int phaseIndex)
    {
        VariablePhaseDistribution dist = distribution ?? VariablePhaseDistribution.EvenFallback();
        int p = Mathf.Clamp(phaseIndex, 0, PhaseCount - 1);
        if (p < dist.activePhases)
        {
            return VariableTimelineSeason.Active;
        }

        p -= dist.activePhases;
        if (p < dist.deescalationPhases)
        {
            return VariableTimelineSeason.Deescalation;
        }

        p -= dist.deescalationPhases;
        return p < dist.dormantPhases ? VariableTimelineSeason.Dormant : VariableTimelineSeason.Escalation;
    }

    public static string ResolveLogPath() => MacroChronicleJsonScan.ResolveDataFile(DefaultLogFileName);

    public static string ResolveGeoPath() => MacroChronicleJsonScan.ResolveDataFile(DefaultGeoFileName);

    /// <summary>国家001・ターン1の割り振りをコンソールへ出します。</summary>
    public static MicroHistoryTimeline LogNation001Turn1Verification()
    {
        MicroHistoryTimeline timeline = BuildTimeline(DefaultNationId, DefaultTurn);
        SingularExtractionResult singular = SingularPointExtractor.Extract(timeline);
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("<color=#80CBC4><b>【マイクロ歴史・検証】国家001 / ターン1</b></color>");
        sb.AppendLine(timeline.usedSafeFailFallback ? "  Safe-Fail: 均等 6+6+6+6 位相" : "  ログ/geo パース成功");
        sb.Append("  category: ");
        for (int i = 0; i < timeline.categoryFrequencies.Length; i++)
        {
            MacroCategoryFrequency freq = timeline.categoryFrequencies[i];
            if (i > 0)
            {
                sb.Append(" / ");
            }

            sb.Append($"{freq.category} {freq.count}({freq.ratio * 100f:F1}%)");
        }

        sb.AppendLine();
        VariablePhaseDistribution d = timeline.distribution;
        sb.AppendLine(
            $"  4期: 活性 {d.activePhases} / 衰退中間 {d.deescalationPhases} / " +
            $"休眠 {d.dormantPhases} / 兆候中間 {d.escalationPhases} （計 {d.TotalPhases}）");
        if (timeline.phases != null && timeline.phases.Length > 0)
        {
            MicroPhaseSlot first = timeline.phases[0];
            sb.AppendLine(
                $"  位相1: {first.season} 日{first.dayStart}-{first.dayEnd} " +
                $"結界維持 {first.barrierMaintenanceRate:F3} 脅威 {first.threatLevel:F3}");
            MicroPhaseSlot[] days = ExpandToDays(timeline);
            sb.AppendLine($"  日次展開: {days.Length} 日");
        }

        sb.AppendLine(
            singular.hero != null
                ? $"  偉人: {singular.hero.displayName} / {singular.hero.conditionFlag}"
                : "  偉人: なし");
        sb.AppendLine(
            singular.namedBeast != null
                ? $"  ネームド魔獣: {singular.namedBeast.displayName} / {singular.namedBeast.behaviorPatternId} / {singular.namedBeast.conditionFlag}"
                : "  ネームド魔獣: なし");
        Debug.Log(sb.ToString());
        return timeline;
    }

    private static MicroHistoryTimeline BuildFallbackTimeline(int nationId, int turn)
    {
        VariablePhaseDistribution dist = VariablePhaseDistribution.EvenFallback();
        return new MicroHistoryTimeline
        {
            nationId = nationId,
            turn = turn,
            nationName = $"国家{nationId:000}",
            distribution = dist,
            phases = BuildPhaseSlots(dist, new List<MacroChronicleLogEvent>(), new List<MacroChronicleLogEvent>(), null),
            sourceEvents = new List<MacroChronicleLogEvent>(),
            usedSafeFailFallback = true
        };
    }

    private static List<MacroChronicleLogEvent> FilterNationEvents(List<MacroChronicleLogEvent> turnEvents, int nationId)
    {
        List<MacroChronicleLogEvent> filtered = new List<MacroChronicleLogEvent>();
        for (int i = 0; i < turnEvents.Count; i++)
        {
            if (turnEvents[i] != null && turnEvents[i].ConcernsNation(nationId))
            {
                filtered.Add(turnEvents[i]);
            }
        }

        return filtered;
    }

    private static MacroCategoryFrequency[] ComputeFrequencies(List<MacroChronicleLogEvent> events)
    {
        Dictionary<string, int> counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        int total = 0;
        for (int i = 0; i < events.Count; i++)
        {
            string category = string.IsNullOrWhiteSpace(events[i].category) ? "(empty)" : events[i].category.Trim();
            counts.TryGetValue(category, out int n);
            counts[category] = n + 1;
            total++;
        }

        List<MacroCategoryFrequency> list = new List<MacroCategoryFrequency>(counts.Count);
        foreach (KeyValuePair<string, int> pair in counts)
        {
            list.Add(new MacroCategoryFrequency
            {
                category = pair.Key,
                count = pair.Value,
                ratio = total > 0 ? pair.Value / (float)total : 0f
            });
        }

        list.Sort((a, b) => b.count.CompareTo(a.count));
        return list.ToArray();
    }

    private static void CountCombat(
        List<MacroChronicleLogEvent> events,
        out int attack,
        out int defense,
        out int innovation,
        out int total)
    {
        attack = 0;
        defense = 0;
        innovation = 0;
        total = events == null ? 0 : events.Count;
        if (events == null)
        {
            return;
        }

        for (int i = 0; i < events.Count; i++)
        {
            string category = events[i].category ?? string.Empty;
            if (string.Equals(category, "monster_attack", StringComparison.OrdinalIgnoreCase))
            {
                attack++;
            }
            else if (string.Equals(category, "monster_defense", StringComparison.OrdinalIgnoreCase))
            {
                defense++;
            }
            else if (string.Equals(category, "innovation", StringComparison.OrdinalIgnoreCase))
            {
                innovation++;
            }
        }
    }

    private static MicroPhaseSlot[] BuildPhaseSlots(
        VariablePhaseDistribution distribution,
        List<MacroChronicleLogEvent> turnEvents,
        List<MacroChronicleLogEvent> nationEvents,
        MacroChronicleNationSnapshot geoNow)
    {
        VariablePhaseDistribution dist = distribution ?? VariablePhaseDistribution.EvenFallback();
        float baseBarrier = geoNow != null && geoNow.barrierEfficiency > 0f ? geoNow.barrierEfficiency : 0.95f;
        float threat = 1.2f;
        for (int i = 0; i < turnEvents.Count; i++)
        {
            if (turnEvents[i].globalThreatIndex > 0f)
            {
                threat = turnEvents[i].globalThreatIndex;
                break;
            }
        }

        string[] nationLogs = CollectEventTexts(nationEvents, 8);
        string[] worldLogs = CollectEventTexts(turnEvents, 4);
        MicroPhaseSlot[] slots = new MicroPhaseSlot[PhaseCount];
        int cursorDay = 1;
        for (int i = 0; i < PhaseCount; i++)
        {
            int remainingPhases = PhaseCount - i;
            int remainingDays = DaysPerYear - cursorDay + 1;
            int span = Mathf.Max(1, Mathf.RoundToInt(remainingDays / (float)remainingPhases));
            if (i == PhaseCount - 1)
            {
                span = remainingDays;
            }

            int dayStart = cursorDay;
            int dayEnd = Mathf.Min(DaysPerYear, cursorDay + span - 1);
            VariableTimelineSeason season = SeasonAtPhase(dist, i);
            float seasonBarrier = baseBarrier;
            float seasonThreat = threat;
            switch (season)
            {
                case VariableTimelineSeason.Active:
                    seasonBarrier = Mathf.Clamp(baseBarrier * 0.88f, 0.35f, 1.6f);
                    seasonThreat = threat * 1.05f;
                    break;
                case VariableTimelineSeason.Deescalation:
                    seasonBarrier = Mathf.Clamp(baseBarrier * 0.96f, 0.35f, 1.6f);
                    seasonThreat = threat * 0.82f;
                    break;
                case VariableTimelineSeason.Dormant:
                    seasonBarrier = Mathf.Clamp(baseBarrier * 1.04f, 0.35f, 1.8f);
                    seasonThreat = threat * 0.55f;
                    break;
                default:
                    seasonBarrier = Mathf.Clamp(baseBarrier * 0.93f, 0.35f, 1.6f);
                    seasonThreat = threat * 0.92f;
                    break;
            }

            bool isFirstOfSeason = i == 0 || SeasonAtPhase(dist, i - 1) != season;
            slots[i] = new MicroPhaseSlot
            {
                phaseIndex = i + 1,
                season = season,
                dayStart = dayStart,
                dayEnd = dayEnd,
                barrierMaintenanceRate = seasonBarrier,
                threatLevel = seasonThreat,
                eventLogs = isFirstOfSeason
                    ? (season == VariableTimelineSeason.Active ? ConcatLogs(nationLogs, worldLogs) : nationLogs)
                    : Array.Empty<string>()
            };
            cursorDay = dayEnd + 1;
        }

        return slots;
    }

    private static string[] CollectEventTexts(List<MacroChronicleLogEvent> events, int max)
    {
        if (events == null || events.Count == 0)
        {
            return Array.Empty<string>();
        }

        int n = Mathf.Min(max, events.Count);
        string[] texts = new string[n];
        for (int i = 0; i < n; i++)
        {
            texts[i] = events[i].eventText ?? string.Empty;
        }

        return texts;
    }

    private static string[] ConcatLogs(string[] a, string[] b)
    {
        if (a == null || a.Length == 0)
        {
            return b ?? Array.Empty<string>();
        }

        if (b == null || b.Length == 0)
        {
            return a;
        }

        string[] merged = new string[a.Length + b.Length];
        Array.Copy(a, merged, a.Length);
        Array.Copy(b, 0, merged, a.Length, b.Length);
        return merged;
    }
}

/// <summary>巨大 JSON からターン／国家だけをストリーム抽出します。</summary>
public static class MacroChronicleJsonScan
{
    public static string ResolveDataFile(string fileName)
    {
        string[] candidates =
        {
            Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "macro_chronicle", fileName)),
            Path.GetFullPath(Path.Combine(Application.dataPath, "..", "macro_chronicle", fileName)),
            Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "macro_chronicle", fileName)),
            Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), fileName))
        };
        for (int i = 0; i < candidates.Length; i++)
        {
            if (File.Exists(candidates[i]))
            {
                return candidates[i];
            }
        }

        return candidates[0];
    }

    public static List<MacroChronicleLogEvent> ReadTurnEvents(string path, int turn)
    {
        List<MacroChronicleLogEvent> events = new List<MacroChronicleLogEvent>();
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return events;
        }

        try
        {
            using (StreamReader reader = new StreamReader(path, Encoding.UTF8))
            {
                foreach (string objectJson in EnumerateTopArrayObjects(reader))
                {
                    if (!ContainsTurn(objectJson, turn))
                    {
                        continue;
                    }

                    MacroChronicleLogEvent parsed = ParseLogEvent(objectJson);
                    if (parsed != null && parsed.turn == turn)
                    {
                        events.Add(parsed);
                    }
                }
            }
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[MacroChronicleJsonScan] ログ読込 Safe-Fail: {exception.Message}");
        }

        return events;
    }

    public static MacroChronicleNationSnapshot ReadNationAtTurn(string path, int turn, int nationId)
    {
        if (turn < 0 || string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            using (FileStream stream = File.OpenRead(path))
            using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
            {
                if (!SkipUntil(reader, "\"snapshots\""))
                {
                    return null;
                }

                foreach (string snapshotJson in EnumerateTopArrayObjects(reader))
                {
                    if (ReadInt(snapshotJson, "turn") != turn)
                    {
                        continue;
                    }

                    int nationsIdx = snapshotJson.IndexOf("\"nations\"", StringComparison.Ordinal);
                    int arrayStart = nationsIdx < 0 ? -1 : snapshotJson.IndexOf('[', nationsIdx);
                    if (arrayStart < 0)
                    {
                        return null;
                    }

                    foreach (string nationJson in EnumerateArrayObjectsFromString(snapshotJson, arrayStart))
                    {
                        if (ReadInt(nationJson, "id") == nationId)
                        {
                            return ParseNation(nationJson);
                        }
                    }

                    return null;
                }
            }
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[MacroChronicleJsonScan] geo 読込 Safe-Fail: {exception.Message}");
        }

        return null;
    }

    /// <summary>指定ターンの nations[] から領域一致の国家をすべて返します（Safe-Fail）。</summary>
    public static List<MacroChronicleNationSnapshot> ReadNationsInRegionAtTurn(
        string path,
        int turn,
        string regionName)
    {
        List<MacroChronicleNationSnapshot> nations = new List<MacroChronicleNationSnapshot>();
        if (turn < 0 || string.IsNullOrWhiteSpace(path) || !File.Exists(path) ||
            string.IsNullOrWhiteSpace(regionName))
        {
            return nations;
        }

        try
        {
            using (FileStream stream = File.OpenRead(path))
            using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
            {
                if (!SkipUntil(reader, "\"snapshots\""))
                {
                    return nations;
                }

                foreach (string snapshotJson in EnumerateTopArrayObjects(reader))
                {
                    if (ReadInt(snapshotJson, "turn") != turn)
                    {
                        continue;
                    }

                    int nationsIdx = snapshotJson.IndexOf("\"nations\"", StringComparison.Ordinal);
                    int arrayStart = nationsIdx < 0 ? -1 : snapshotJson.IndexOf('[', nationsIdx);
                    if (arrayStart < 0)
                    {
                        return nations;
                    }

                    foreach (string nationJson in EnumerateArrayObjectsFromString(snapshotJson, arrayStart))
                    {
                        MacroChronicleNationSnapshot snap = ParseNation(nationJson);
                        if (snap != null && string.Equals(snap.region, regionName, StringComparison.Ordinal))
                        {
                            nations.Add(snap);
                        }
                    }

                    return nations;
                }
            }
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[MacroChronicleJsonScan] 領域国家読込 Safe-Fail: {exception.Message}");
        }

        return nations;
    }

    public static MacroChronicleLogEvent ParseLogEvent(string json)
    {
        return new MacroChronicleLogEvent
        {
            turn = ReadInt(json, "turn"),
            eventText = ReadString(json, "event"),
            category = ReadString(json, "category"),
            nationId = ReadInt(json, "nation_id"),
            nationIds = ReadIntArray(json, "nation_ids"),
            region = ReadString(json, "region"),
            militaryLoss = ReadFloat(json, "military_loss"),
            territoryGain = ReadFloat(json, "territory_gain"),
            territoryLoss = ReadFloat(json, "territory_loss"),
            economyLoss = ReadFloat(json, "economy_loss"),
            globalThreatIndex = ReadFloat(json, "global_threat_index"),
            cyclePhase = ReadString(json, "cycle_phase")
        };
    }

    public static MacroChronicleNationSnapshot ParseNation(string json)
    {
        return new MacroChronicleNationSnapshot
        {
            id = ReadInt(json, "id"),
            name = ReadString(json, "name"),
            region = ReadString(json, "region"),
            lat = ReadFloat(json, "lat"),
            lng = ReadFloat(json, "lng"),
            alive = !string.Equals(ReadRaw(json, "alive"), "false", StringComparison.OrdinalIgnoreCase),
            territory = ReadFloat(json, "territory"),
            power = ReadFloat(json, "power"),
            economy = ReadFloat(json, "economy"),
            military = ReadFloat(json, "military"),
            magic = ReadFloat(json, "magic"),
            barrierEfficiency = ReadFloat(json, "barrier_efficiency"),
            bornTurn = ReadInt(json, "born_turn"),
            diedTurn = ReadNullableInt(json, "died_turn")
        };
    }

    private static bool ContainsTurn(string json, int turn)
    {
        string a = $"\"turn\": {turn}";
        string b = $"\"turn\":{turn}";
        int idx = json.IndexOf(a, StringComparison.Ordinal);
        if (idx < 0)
        {
            idx = json.IndexOf(b, StringComparison.Ordinal);
        }

        if (idx < 0)
        {
            return false;
        }

        int after = idx + (json.IndexOf(a, StringComparison.Ordinal) == idx ? a.Length : b.Length);
        return after >= json.Length || json[after] < '0' || json[after] > '9';
    }

    private static IEnumerable<string> EnumerateTopArrayObjects(StreamReader reader, bool snapshotsArrayAlreadyOpen = false)
    {
        int depth = 0;
        bool inString = false;
        bool escape = false;
        bool startedArray = snapshotsArrayAlreadyOpen;
        int bracketDepth = snapshotsArrayAlreadyOpen ? 1 : 0;
        StringBuilder current = new StringBuilder(512);
        int ch;
        while ((ch = reader.Read()) >= 0)
        {
            char c = (char)ch;
            if (inString)
            {
                if (depth > 0)
                {
                    current.Append(c);
                }

                if (escape)
                {
                    escape = false;
                }
                else if (c == '\\')
                {
                    escape = true;
                }
                else if (c == '"')
                {
                    inString = false;
                }

                continue;
            }

            if (c == '"')
            {
                inString = true;
                if (depth > 0)
                {
                    current.Append(c);
                }

                continue;
            }

            if (!startedArray)
            {
                if (c == '[')
                {
                    startedArray = true;
                    bracketDepth = 1;
                }

                continue;
            }

            if (c == '{')
            {
                if (depth == 0)
                {
                    current.Length = 0;
                }

                depth++;
                current.Append(c);
                continue;
            }

            if (depth == 0)
            {
                if (c == '[')
                {
                    bracketDepth++;
                }
                else if (c == ']')
                {
                    bracketDepth--;
                    if (bracketDepth <= 0)
                    {
                        yield break;
                    }
                }

                continue;
            }

            current.Append(c);
            if (c == '{')
            {
                depth++;
            }
            else if (c == '}')
            {
                depth--;
                if (depth == 0)
                {
                    yield return current.ToString();
                }
            }
            else if (c == '[')
            {
                depth++;
            }
            else if (c == ']')
            {
                depth--;
            }
        }
    }

    private static IEnumerable<string> EnumerateArrayObjectsFromString(string source, int arrayStart)
    {
        int depth = 0;
        bool inString = false;
        bool escape = false;
        StringBuilder current = new StringBuilder(256);
        for (int i = arrayStart; i < source.Length; i++)
        {
            char c = source[i];
            if (inString)
            {
                current.Append(c);
                if (escape)
                {
                    escape = false;
                }
                else if (c == '\\')
                {
                    escape = true;
                }
                else if (c == '"')
                {
                    inString = false;
                }

                continue;
            }

            if (c == '"')
            {
                inString = true;
                if (depth > 0)
                {
                    current.Append(c);
                }

                continue;
            }

            if (c == '{')
            {
                if (depth == 0)
                {
                    current.Length = 0;
                }

                depth++;
                current.Append(c);
                continue;
            }

            if (depth == 0)
            {
                if (c == ']')
                {
                    yield break;
                }

                continue;
            }

            current.Append(c);
            if (c == '{')
            {
                depth++;
            }
            else if (c == '}')
            {
                depth--;
                if (depth == 0)
                {
                    yield return current.ToString();
                }
            }
            else if (c == '[')
            {
                depth++;
            }
            else if (c == ']')
            {
                depth--;
            }
        }
    }

    private static bool SkipUntil(StreamReader reader, string token)
    {
        char[] window = new char[token.Length];
        int filled = 0;
        int ch;
        while ((ch = reader.Read()) >= 0)
        {
            if (filled < window.Length)
            {
                window[filled++] = (char)ch;
            }
            else
            {
                Array.Copy(window, 1, window, 0, window.Length - 1);
                window[window.Length - 1] = (char)ch;
            }

            if (filled == window.Length && new string(window) == token)
            {
                return true;
            }
        }

        return false;
    }

    public static int ReadInt(string json, string key)
    {
        string raw = ReadRaw(json, key);
        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : 0;
    }

    public static int ReadNullableInt(string json, string key)
    {
        string raw = ReadRaw(json, key);
        if (string.IsNullOrEmpty(raw) || raw == "null")
        {
            return -1;
        }

        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : -1;
    }

    public static float ReadFloat(string json, string key)
    {
        string raw = ReadRaw(json, key);
        return float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : 0f;
    }

    public static string ReadString(string json, string key)
    {
        string token = $"\"{key}\"";
        int idx = json.IndexOf(token, StringComparison.Ordinal);
        if (idx < 0)
        {
            return string.Empty;
        }

        int colon = json.IndexOf(':', idx + token.Length);
        if (colon < 0)
        {
            return string.Empty;
        }

        int i = colon + 1;
        while (i < json.Length && char.IsWhiteSpace(json[i]))
        {
            i++;
        }

        if (i >= json.Length || json[i] != '"')
        {
            return string.Empty;
        }

        i++;
        StringBuilder sb = new StringBuilder();
        bool escape = false;
        for (; i < json.Length; i++)
        {
            char c = json[i];
            if (escape)
            {
                sb.Append(c);
                escape = false;
                continue;
            }

            if (c == '\\')
            {
                escape = true;
                continue;
            }

            if (c == '"')
            {
                break;
            }

            sb.Append(c);
        }

        return sb.ToString();
    }

    public static string ReadRaw(string json, string key)
    {
        string token = $"\"{key}\"";
        int idx = json.IndexOf(token, StringComparison.Ordinal);
        if (idx < 0)
        {
            return string.Empty;
        }

        int colon = json.IndexOf(':', idx + token.Length);
        if (colon < 0)
        {
            return string.Empty;
        }

        int i = colon + 1;
        while (i < json.Length && char.IsWhiteSpace(json[i]))
        {
            i++;
        }

        if (i >= json.Length)
        {
            return string.Empty;
        }

        if (json[i] == '"')
        {
            return ReadString(json, key);
        }

        int start = i;
        while (i < json.Length && json[i] != ',' && json[i] != '}' && json[i] != ']' && !char.IsWhiteSpace(json[i]))
        {
            i++;
        }

        return json.Substring(start, i - start);
    }

    public static int[] ReadIntArray(string json, string key)
    {
        string token = $"\"{key}\"";
        int idx = json.IndexOf(token, StringComparison.Ordinal);
        if (idx < 0)
        {
            return Array.Empty<int>();
        }

        int bracket = json.IndexOf('[', idx + token.Length);
        int end = bracket < 0 ? -1 : json.IndexOf(']', bracket + 1);
        if (bracket < 0 || end < 0)
        {
            return Array.Empty<int>();
        }

        string inner = json.Substring(bracket + 1, end - bracket - 1);
        string[] parts = inner.Split(',');
        List<int> values = new List<int>(parts.Length);
        for (int i = 0; i < parts.Length; i++)
        {
            if (int.TryParse(parts[i].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v))
            {
                values.Add(v);
            }
        }

        return values.ToArray();
    }

    /// <summary>
    /// macro_chronicle_geo.json の指定ターン・国家スナップショットへ数値を書き戻します（Safe-Fail）。
    /// </summary>
    public static bool TryWriteNationAtTurn(
        string path,
        int turn,
        int nationId,
        MacroGeoNationWriteback dto,
        out string error)
    {
        error = string.Empty;
        if (dto == null)
        {
            error = "writeback dto が null";
            return false;
        }

        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            error = $"geo ファイル未存在: {path}";
            return false;
        }

        string tempPath = path + ".micromacro.tmp";
        bool found = false;
        try
        {
            using (FileStream inStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (StreamReader reader = new StreamReader(inStream, Encoding.UTF8))
            using (StreamWriter writer = new StreamWriter(tempPath, false, Encoding.UTF8))
            {
                CopyUntilSnapshotsArrayOpen(reader, writer);
                bool firstSnapshot = true;
                foreach (string snapshotJson in EnumerateTopArrayObjects(reader, snapshotsArrayAlreadyOpen: true))
                {
                    if (!firstSnapshot)
                    {
                        writer.Write(',');
                    }

                    firstSnapshot = false;
                    string output = snapshotJson;
                    if (ReadInt(snapshotJson, "turn") == turn)
                    {
                        string patched = PatchNationInSnapshot(snapshotJson, nationId, dto);
                        output = patched;
                        if (!string.Equals(patched, snapshotJson, StringComparison.Ordinal))
                        {
                            found = true;
                        }
                    }

                    writer.Write(output);
                }

                CopyReaderRemainder(reader, writer);
            }

            if (!found)
            {
                error = $"ターン {turn} / 国家 {nationId} を geo 内で更新できませんでした";
                try
                {
                    File.Delete(tempPath);
                }
                catch (Exception)
                {
                    // Safe-Fail
                }

                return false;
            }

            File.Copy(tempPath, path, true);
            try
            {
                File.Delete(tempPath);
            }
            catch (Exception)
            {
                // Safe-Fail
            }

            return true;
        }
        catch (UnauthorizedAccessException exception)
        {
            error = $"権限エラー: {exception.Message}";
            TryDeleteTemp(tempPath);
            return false;
        }
        catch (Exception exception)
        {
            error = exception.Message;
            TryDeleteTemp(tempPath);
            return false;
        }
    }

    private static void TryDeleteTemp(string tempPath)
    {
        try
        {
            if (!string.IsNullOrEmpty(tempPath) && File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
        catch (Exception)
        {
            // Safe-Fail
        }
    }

    private static void CopyUntilSnapshotsArrayOpen(StreamReader reader, StreamWriter writer)
    {
        StringBuilder tail = new StringBuilder(48);
        while (true)
        {
            int ch = reader.Read();
            if (ch < 0)
            {
                return;
            }

            char c = (char)ch;
            writer.Write(c);
            tail.Append(c);
            if (tail.Length > 48)
            {
                tail.Remove(0, tail.Length - 48);
            }

            if (tail.ToString().Contains("\"snapshots\"", StringComparison.Ordinal))
            {
                while (true)
                {
                    ch = reader.Read();
                    if (ch < 0)
                    {
                        return;
                    }

                    c = (char)ch;
                    writer.Write(c);
                    if (c == '[')
                    {
                        return;
                    }
                }
            }
        }
    }

    private static void CopyReaderRemainder(StreamReader reader, StreamWriter writer)
    {
        char[] buffer = new char[8192];
        int read;
        while ((read = reader.Read(buffer, 0, buffer.Length)) > 0)
        {
            writer.Write(buffer, 0, read);
        }
    }

    private static string PatchNationInSnapshot(
        string snapshotJson,
        int nationId,
        MacroGeoNationWriteback dto)
    {
        int nationsIdx = snapshotJson.IndexOf("\"nations\"", StringComparison.Ordinal);
        if (nationsIdx < 0)
        {
            return snapshotJson;
        }

        int arrayStart = snapshotJson.IndexOf('[', nationsIdx);
        if (arrayStart < 0)
        {
            return snapshotJson;
        }

        int cursor = 0;
        StringBuilder sb = new StringBuilder(snapshotJson.Length + 96);
        bool patched = false;
        foreach (string nationJson in EnumerateArrayObjectsFromString(snapshotJson, arrayStart))
        {
            if (ReadInt(nationJson, "id") != nationId)
            {
                continue;
            }

            int pos = snapshotJson.IndexOf(nationJson, nationsIdx, StringComparison.Ordinal);
            if (pos < 0)
            {
                continue;
            }

            sb.Append(snapshotJson, cursor, pos - cursor);
            sb.Append(PatchNationJsonNumbers(nationJson, dto));
            cursor = pos + nationJson.Length;
            patched = true;
            break;
        }

        if (!patched)
        {
            return snapshotJson;
        }

        sb.Append(snapshotJson, cursor, snapshotJson.Length - cursor);
        return sb.ToString();
    }

    private static string PatchNationJsonNumbers(string nationJson, MacroGeoNationWriteback dto)
    {
        nationJson = ReplaceFirstFloatField(nationJson, "power", dto.power);
        nationJson = ReplaceFirstFloatField(nationJson, "economy", dto.economy);
        nationJson = ReplaceFirstFloatField(nationJson, "military", dto.military);
        nationJson = ReplaceFirstFloatField(nationJson, "magic", dto.magic);
        nationJson = ReplaceFirstFloatField(nationJson, "barrier_efficiency", dto.barrier_efficiency);
        return nationJson;
    }

    private static string ReplaceFirstFloatField(string json, string key, float value)
    {
        string formatted = value.ToString("0.####", CultureInfo.InvariantCulture);
        string pattern = "\"" + key + "\"\\s*:\\s*[-+]?[0-9]*\\.?[0-9]+(?:[eE][-+]?[0-9]+)?";
        return Regex.Replace(
            json,
            pattern,
            "\"" + key + "\":" + formatted,
            RegexOptions.None,
            TimeSpan.FromSeconds(2));
    }
}

#if UNITY_EDITOR
public static class MicroHistoryTimelineTimelineMenu
{
    [MenuItem("Tools/Demo/Micro History/Verify Nation 001 Turn 1")]
    public static void VerifyNation001Turn1()
    {
        MicroHistoryTimelineTimelineEngine.LogNation001Turn1Verification();
        MicroHistoryTimeline missing = MicroHistoryTimelineTimelineEngine.BuildTimeline(9999, 1);
        Debug.Log(
            missing.usedSafeFailFallback && missing.distribution.activePhases == 6
                ? "<color=#A5D6A7>【マイクロ歴史・検証】欠番国家は均等 6 位相 Safe-Fail PASS</color>"
                : "<color=#EF9A9A>【マイクロ歴史・検証】Safe-Fail が均等 6 位相になっていません</color>");
    }
}
#endif

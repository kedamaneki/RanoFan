using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

// =============================================================================
// 街の噂話ログ — 365 日シミュレーション数値から日常会話テキストを動的生成
// 連携: DailySimulationEngine / HumanConflictEngine / NpcCivilizationEngine
// =============================================================================

/// <summary>1 件の噂話ログ。</summary>
public sealed class RumorLogEntry
{
    public int dayOfYear;
    public string category = string.Empty;
    public string speakerName = string.Empty;
    public string text = string.Empty;
    public float credibility = 1f;

    public string FormatLine()
    {
        return $"{dayOfYear}日目 [{category}] {speakerName}: 「{text}」";
    }
}

/// <summary>噂生成の入力コンテキスト。</summary>
public sealed class RumorContext
{
    public int dayOfYear = 1;
    public int phase = 1;
    public VariableTimelineSeason season = VariableTimelineSeason.Active;
    public float barrierPercent = 100f;
    public float barrierDelta;
    public float macroBarrierNorm = 0.95f;
    public float foodScarcity;
    public float factionTension;
    public float techMonopolyLevel;
    public float threatLevel = 1f;
    public float manaEcologyLevel = 50f;
    public bool innovationActive;
    public string storageSnapshot = string.Empty;
}

/// <summary>検証結果。</summary>
public sealed class RumorLogVerifyResult
{
    public bool success;
    public string message = string.Empty;
}

/// <summary>
/// デイリー数値変動（結界・食料・派閥・技術・脅威）から街の噂話を生成します。
/// </summary>
public static class RumorLogGenerator
{
    public const string LogTag = "【街の噂】";

    private const float BarrierDropDeltaThreshold = -0.35f;
    private const float BarrierLowPercentThreshold = 52f;
    private const float FoodScarcityRumorThreshold = 58f;
    private const float FactionTensionRumorThreshold = 55f;
    private const float TechInnovationThreshold = 62f;
    private const float ThreatOmenThreshold = 1.45f;

    private static readonly List<RumorLogEntry> recentRumors = new List<RumorLogEntry>(32);
    public static IReadOnlyList<RumorLogEntry> RecentRumors => recentRumors;

    /// <summary>シミュレーション状態からコンテキストを組み立てます（欠損時 Safe-Fail）。</summary>
    public static RumorContext BuildContext(
        int dayOfYear,
        VariableTimelineSeason season,
        int phase,
        float barrierBefore,
        float barrierAfter,
        float threatLevel,
        float manaEcologyLevel,
        VillageStorageMarket market)
    {
        RumorContext ctx = new RumorContext
        {
            dayOfYear = Mathf.Max(1, dayOfYear),
            season = season,
            phase = Mathf.Max(1, phase),
            barrierPercent = SanitizePercent(barrierAfter, 100f),
            barrierDelta = barrierAfter - barrierBefore,
            threatLevel = Sanitize(threatLevel, 1f),
            manaEcologyLevel = Sanitize(manaEcologyLevel, 50f)
        };

        try
        {
            MicroToMacroAggregator macro = MicroToMacroAggregator.EnsureInstance();
            if (macro != null && macro.Stats != null)
            {
                ctx.macroBarrierNorm = Mathf.Clamp01(macro.Stats.BarrierEfficiency);
            }
        }
        catch (Exception)
        {
            ctx.macroBarrierNorm = 0.95f;
        }

        try
        {
            HumanConflictEngine conflict = HumanConflictEngine.EnsureInstance();
            if (conflict != null && conflict.Society != null)
            {
                HumanSocietyStatus society = conflict.Society;
                ctx.foodScarcity = SanitizePercent(society.foodScarcity, 0f);
                ctx.factionTension = SanitizePercent(society.factionTension, 22f);
                ctx.techMonopolyLevel = SanitizePercent(society.techMonopolyLevel, 28f);
            }
        }
        catch (Exception)
        {
            // Safe-Fail: 社会指標は 0 のまま
        }

        if (market != null)
        {
            ctx.storageSnapshot = market.FormatSnapshot();
            if (market.FoodDepleted)
            {
                ctx.foodScarcity = Mathf.Max(ctx.foodScarcity, 88f);
            }
        }

        ctx.innovationActive =
            HistoryFlagRegistry.IsUnlocked("ALT_NATION_001_CRAFT_T001") ||
            HistoryFlagRegistry.IsUnlocked("HIST_NATION_001_GEO_TURN_001_INNOVATION") ||
            ctx.techMonopolyLevel >= TechInnovationThreshold;

        return ctx;
    }

    /// <summary>コンテキストから噂話を 1 件生成します。</summary>
    public static RumorLogEntry Generate(RumorContext ctx, NpcIndividualStatus speaker = null)
    {
        RumorContext safe = ctx ?? new RumorContext();
        NpcIndividualStatus npc = speaker ?? PickRandomVillager(safe);
        string name = npc != null && !string.IsNullOrWhiteSpace(npc.Name) ? npc.Name : "村人";
        string jobLabel = npc != null ? NpcJobProfile.JobLabel(npc.CivicJob) : "市民";

        RumorLogEntry entry = new RumorLogEntry
        {
            dayOfYear = safe.dayOfYear,
            speakerName = name,
            credibility = ComputeCredibility(safe, npc)
        };

        if (safe.barrierDelta <= BarrierDropDeltaThreshold || safe.barrierPercent <= BarrierLowPercentThreshold)
        {
            entry.category = "結界急落";
            float drop = Mathf.Abs(safe.barrierDelta);
            entry.text = drop >= 0.4f
                ? $"結界杭の光が一気に{drop.ToString("F0", CultureInfo.InvariantCulture)}%も弱まった。{jobLabel}の手が追いつかない。"
                : $"結界の維持率が{safe.barrierPercent.ToString("F0", CultureInfo.InvariantCulture)}%まで落ちた。今夜は杭の見回りを増やす話だ。";
            return entry;
        }

        if (safe.foodScarcity >= FoodScarcityRumorThreshold)
        {
            entry.category = "食料枯渇";
            entry.text = safe.foodScarcity >= 85f
                ? "倉庫の底が見えた。配給を減らさなければ、明日の朝が来ないかもしれない。"
                : $"食料が細る。今の危機度は{safe.foodScarcity.ToString("F0", CultureInfo.InvariantCulture)}%くらいらしい。";
            return entry;
        }

        if (safe.innovationActive || safe.techMonopolyLevel >= TechInnovationThreshold)
        {
            entry.category = "技術イノベーション";
            entry.text = safe.techMonopolyLevel >= 72f
                ? "工房の新技法が村の話題だ。隣国の職人が羨むほどの仕上がりだとか。"
                : "鍛冶と結界守が新しい手順を試している。うまくいけば、来年の防衛が楽になる。";
            return entry;
        }

        if (safe.threatLevel >= ThreatOmenThreshold)
        {
            entry.category = "魔獣急襲予兆";
            entry.text = safe.manaEcologyLevel >= 60f
                ? $"魔獣の気配が濃い。脅威指数{safe.threatLevel.ToString("F1", CultureInfo.InvariantCulture)}、魔力環境も不安定だ。"
                : "森の方で獣の唸りが続いている。結界の外に出るのは危ない日だ。";
            return entry;
        }

        if (safe.factionTension >= FactionTensionRumorThreshold)
        {
            entry.category = "派閥摩擦";
            entry.text = $"村の意見が割れている。派閥の緊張は{safe.factionTension.ToString("F0", CultureInfo.InvariantCulture)}%くらいまで上がったらしい。";
            return entry;
        }

        entry.category = "日常";
        entry.text = safe.season == VariableTimelineSeason.Dormant
            ? "休眠期の静かな一日。炉の火と粥の匂いだけが村を満たしている。"
            : $"今日も{jobLabel}たちが畑と工房を回っている。特に大きな変事はない。";
        return entry;
    }

    /// <summary>生成してコンソールへ出力し、履歴へ追加します。</summary>
    public static RumorLogEntry TryEmitDailyRumor(RumorContext ctx)
    {
        try
        {
            RumorLogEntry entry = Generate(ctx);
            AppendRecent(entry);
            Debug.Log(
                $"<color=#CE93D8><b>{LogTag}</b></color> {entry.FormatLine()} " +
                $"(信憑{entry.credibility.ToString("F2", CultureInfo.InvariantCulture)})");
            return entry;
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[RumorLogGenerator] Safe-Fail: {exception.Message}");
            return null;
        }
    }

    /// <summary>DailySimulationEngine 向けショートカット。</summary>
    public static RumorLogEntry TryEmitDailyRumor(
        int dayOfYear,
        VariableTimelineSeason season,
        int phase,
        float barrierBefore,
        float barrierAfter,
        float threatLevel,
        float manaEcologyLevel,
        VillageStorageMarket market)
    {
        RumorContext ctx = BuildContext(
            dayOfYear,
            season,
            phase,
            barrierBefore,
            barrierAfter,
            threatLevel,
            manaEcologyLevel,
            market);
        return TryEmitDailyRumor(ctx);
    }

    public static void ClearRecent()
    {
        recentRumors.Clear();
    }

    private static void AppendRecent(RumorLogEntry entry)
    {
        if (entry == null || string.IsNullOrWhiteSpace(entry.text))
        {
            return;
        }

        recentRumors.Add(entry);
        if (recentRumors.Count > 32)
        {
            recentRumors.RemoveAt(0);
        }
    }

    private static float ComputeCredibility(RumorContext ctx, NpcIndividualStatus npc)
    {
        float baseCred = 0.72f;
        if (npc != null)
        {
            if (npc.CivicJob == NpcCivicJob.BarrierKeeper && ctx.barrierPercent < 70f)
            {
                baseCred += 0.12f;
            }

            if (npc.Trait == NpcTrait.Cautious)
            {
                baseCred += 0.05f;
            }

            if (npc.Trait == NpcTrait.Inquisitive && ctx.factionTension > 40f)
            {
                baseCred += 0.04f;
            }
        }

        PlayerStatusManager player = PlayerStatusManager.Instance;
        if (player != null)
        {
            baseCred += Mathf.Clamp(player.GetNPCTrustFactor() * 0.02f, 0f, 0.08f);
        }

        return Mathf.Clamp01(baseCred);
    }

    private static NpcIndividualStatus PickRandomVillager(RumorContext ctx)
    {
        try
        {
            NpcCivilizationEngine civ = NpcCivilizationEngine.EnsureInstance();
            IReadOnlyList<NpcIndividualStatus> list = civ.Villagers;
            if (list == null || list.Count == 0)
            {
                return null;
            }

            int start = Mathf.Abs(ctx.dayOfYear * 17 + ctx.phase * 3) % list.Count;
            for (int i = 0; i < list.Count; i++)
            {
                NpcIndividualStatus candidate = list[(start + i) % list.Count];
                if (candidate != null)
                {
                    return candidate;
                }
            }
        }
        catch (Exception)
        {
            // Safe-Fail
        }

        return null;
    }

    private static float Sanitize(float value, float fallback)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
        {
            return fallback;
        }

        return value;
    }

    private static float SanitizePercent(float value, float fallback)
    {
        return Mathf.Clamp(Sanitize(value, fallback), 0f, 100f);
    }

    public static RumorLogVerifyResult RunVerification()
    {
        RumorLogVerifyResult verify = new RumorLogVerifyResult();
        StringBuilder log = new StringBuilder();
        try
        {
            HistoryFlagRegistry.EnsureWired();
            ClearRecent();

            RumorLogEntry barrier = Generate(new RumorContext
            {
                dayOfYear = 12,
                barrierPercent = 48f,
                barrierDelta = -0.6f
            });
            bool barrierPass = barrier.category == "結界急落";
            log.AppendLine($"barrier-drop: {barrier.FormatLine()} pass={barrierPass}");

            RumorLogEntry food = Generate(new RumorContext
            {
                dayOfYear = 20,
                foodScarcity = 72f
            });
            bool foodPass = food.category == "食料枯渇";
            log.AppendLine($"food-scarcity: {food.FormatLine()} pass={foodPass}");

            RumorLogEntry tech = Generate(new RumorContext
            {
                dayOfYear = 33,
                techMonopolyLevel = 68f,
                innovationActive = true
            });
            bool techPass = tech.category == "技術イノベーション";
            log.AppendLine($"innovation: {tech.FormatLine()} pass={techPass}");

            RumorLogEntry threat = Generate(new RumorContext
            {
                dayOfYear = 44,
                threatLevel = 1.8f,
                manaEcologyLevel = 65f
            });
            bool threatPass = threat.category == "魔獣急襲予兆";
            log.AppendLine($"beast-omen: {threat.FormatLine()} pass={threatPass}");

            NpcCivilizationEngine.EnsureInstance();
            NpcStatusManagerRegistry.EnsureAllFromVillagers();
            int npcCount = NpcStatusManagerRegistry.Count;
            bool npcPass = npcCount >= 2;
            log.AppendLine($"npc-status: managers={npcCount} pass={npcPass}");

            DailySimulationEngine daily = DailySimulationEngine.EnsureInstance();
            daily.ResetSessionForVerification();
            DailyAdvanceResult day1 = daily.AdvanceOneDay();
            bool dailyPass = day1.success && recentRumors.Count > 0;
            log.AppendLine($"daily-rumor: life={day1.lifeLog} rumors={recentRumors.Count} pass={dailyPass}");

            verify.success = barrierPass && foodPass && techPass && threatPass && npcPass && dailyPass;
            verify.message = log.ToString().TrimEnd();
        }
        catch (Exception exception)
        {
            verify.success = false;
            verify.message = $"Safe-Fail: {exception.Message}";
        }

        WriteVerifyLog(verify);
        return verify;
    }

    private static void WriteVerifyLog(RumorLogVerifyResult verify)
    {
        try
        {
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
            string path = Path.Combine(projectRoot, "Logs", "rumor_log_verify.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? projectRoot);
            File.WriteAllText(
                path,
                $"{DateTime.UtcNow:O} success={verify.success}\n{verify.message}\n",
                Encoding.UTF8);
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[RumorLogGenerator] 検証ログスキップ: {exception.Message}");
        }
    }
}

#if UNITY_EDITOR
public static class RumorLogGeneratorMenu
{
    [MenuItem("Tools/Procedural Map/Verify Rumor Log Generator")]
    public static void VerifyFromMenu()
    {
        RumorLogVerifyResult result = RumorLogGenerator.RunVerification();
        Debug.Log(
            result.success
                ? $"<color=#A5D6A7><b>【噂話検証】PASS</b></color>\n{result.message}"
                : $"<color=#FF8A80><b>【噂話検証】FAIL</b></color>\n{result.message}");
        EditorUtility.DisplayDialog("Rumor Log Generator", result.message, "OK");
    }
}
#endif

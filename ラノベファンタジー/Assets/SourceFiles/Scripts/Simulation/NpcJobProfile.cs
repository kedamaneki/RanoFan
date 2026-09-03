using UnityEngine;

// =============================================================================
// 社会生活ジョブ定義（ゲームクラス JOB_* とは別系統）
// =============================================================================

/// <summary>村の社会生活における仕事。</summary>
public enum NpcCivicJob
{
    Farmer = 0,
    Woodcutter = 1,
    Rancher = 2,
    Blacksmith = 3,
    BarrierKeeper = 4,
    Carpenter = 5
}

/// <summary>1 位相分の生産結果（加算は正、消費は負）。</summary>
public struct VillageResourceDelta
{
    public float wood;
    public float food;
    public float ore;
    public float manaCrystal;
    public string actionLabel;
    public bool usedSafeFail;

    public static VillageResourceDelta Empty(string label)
    {
        return new VillageResourceDelta { actionLabel = label ?? string.Empty };
    }
}

/// <summary>
/// NPC 1 人の社会生活ジョブと、24 位相における自律生産計算です。
/// </summary>
[System.Serializable]
public class NpcJobProfile
{
    public string npcId = string.Empty;
    public string displayName = string.Empty;
    public NpcCivicJob job = NpcCivicJob.Farmer;
    [Range(0.4f, 1.8f)] public float skill = 1f;

    public NpcJobProfile()
    {
    }

    public NpcJobProfile(string id, string name, NpcCivicJob civicJob, float skillLevel)
    {
        npcId = id ?? string.Empty;
        displayName = name ?? civicJob.ToString();
        job = civicJob;
        skill = Mathf.Clamp(skillLevel, 0.4f, 1.8f);
    }

    /// <summary>現在位相の生産行動を計算します。在庫不足時は効率低下の Safe-Fail。</summary>
    public VillageResourceDelta SimulatePhase(
        int phase,
        VariableTimelineSeason season,
        float magicTechMultiplier,
        VillageResourceStatus stock)
    {
        float tech = Mathf.Clamp(magicTechMultiplier, 1f, 2.5f);
        float outdoor = OutdoorFactor(season);
        float indoor = IndoorFactor(season);
        VillageResourceStatus safeStock = stock ?? new VillageResourceStatus();

        try
        {
            switch (job)
            {
                case NpcCivicJob.Woodcutter:
                    return SimulateWoodcutter(phase, outdoor, tech, safeStock);
                case NpcCivicJob.Rancher:
                    return SimulateRancher(phase, outdoor, tech, safeStock);
                case NpcCivicJob.Blacksmith:
                    return SimulateBlacksmith(phase, indoor, tech, safeStock);
                case NpcCivicJob.BarrierKeeper:
                    return SimulateBarrierKeeper(phase, indoor, tech, safeStock);
                case NpcCivicJob.Carpenter:
                    return SimulateCarpenter(phase, indoor, tech, safeStock);
                default:
                    return SimulateFarmer(phase, outdoor, tech, safeStock);
            }
        }
        catch (System.Exception exception)
        {
            Debug.LogWarning($"[NpcJobProfile] {displayName} の位相計算を Safe-Fail: {exception.Message}");
            VillageResourceDelta fallback = VillageResourceDelta.Empty($"{JobLabel(job)}（休止・Safe-Fail）");
            fallback.usedSafeFail = true;
            return fallback;
        }
    }

    public static string JobLabel(NpcCivicJob civicJob)
    {
        switch (civicJob)
        {
            case NpcCivicJob.Woodcutter:
                return "樵";
            case NpcCivicJob.Rancher:
                return "牧畜";
            case NpcCivicJob.Blacksmith:
                return "鍛冶";
            case NpcCivicJob.BarrierKeeper:
                return "結界守";
            case NpcCivicJob.Carpenter:
                return "大工";
            default:
                return "農耕";
        }
    }

    private VillageResourceDelta SimulateFarmer(
        int phase,
        float outdoor,
        float tech,
        VillageResourceStatus stock)
    {
        bool fieldWork = IsDayShift(phase);
        float efficiency = fieldWork ? outdoor : outdoor * 0.35f;
        bool starved = stock.Food < 0.5f;
        if (starved)
        {
            efficiency *= VillageResourceStatus.DepletionEfficiency;
        }

        VillageResourceDelta delta = new VillageResourceDelta
        {
            food = (fieldWork ? 1.35f : 0.35f) * skill * tech * efficiency,
            manaCrystal = fieldWork && outdoor > 0.7f ? 0.08f * skill * tech : 0.02f * skill,
            usedSafeFail = starved,
            actionLabel = starved
                ? "農耕（配給不足・効率低下）"
                : fieldWork ? "耕作・収穫" : "夜の畑見回し"
        };
        return delta;
    }

    private VillageResourceDelta SimulateWoodcutter(
        int phase,
        float outdoor,
        float tech,
        VillageResourceStatus stock)
    {
        bool haul = IsDayShift(phase);
        float efficiency = haul ? outdoor : 0.2f;
        bool starved = stock.Food < 0.4f;
        if (starved)
        {
            efficiency *= VillageResourceStatus.DepletionEfficiency;
        }

        VillageResourceDelta delta = new VillageResourceDelta
        {
            wood = (haul ? 1.6f : 0.25f) * skill * tech * efficiency,
            manaCrystal = haul ? 0.12f * skill * tech * outdoor : 0f,
            food = starved ? 0f : -0.12f,
            usedSafeFail = starved,
            actionLabel = starved
                ? "木材調達（空腹・効率低下）"
                : haul ? "木材調達" : "薪の整理"
        };
        return delta;
    }

    private VillageResourceDelta SimulateRancher(
        int phase,
        float outdoor,
        float tech,
        VillageResourceStatus stock)
    {
        bool tend = phase % 3 != 0;
        float efficiency = tend ? Mathf.Lerp(0.55f, 1f, outdoor) : 0.4f;
        bool noFodder = stock.Food < 0.3f && stock.Wood < 0.3f;
        if (noFodder)
        {
            efficiency *= VillageResourceStatus.DepletionEfficiency;
        }

        VillageResourceDelta delta = new VillageResourceDelta
        {
            food = (tend ? 1.15f : 0.45f) * skill * tech * efficiency,
            wood = tend ? -0.08f : 0f,
            usedSafeFail = noFodder,
            actionLabel = noFodder
                ? "畜産物採取（飼料不足・効率低下）"
                : tend ? "畜産物採取" : "群れの見守り"
        };
        return delta;
    }

    private VillageResourceDelta SimulateBlacksmith(
        int phase,
        float indoor,
        float tech,
        VillageResourceStatus stock)
    {
        bool forge = IsDayShift(phase) || phase % 2 == 0;
        float wantWood = forge ? 0.45f : 0.12f;
        float wantOre = forge ? 0.28f : 0.05f;
        float efficiency = stock.ResolveConsumeEfficiency(wantWood, 0f, wantOre, 0f);
        bool depleted = efficiency < 0.999f;

        VillageResourceDelta delta = new VillageResourceDelta
        {
            wood = -wantWood,
            ore = -wantOre + (forge ? 0.18f * skill * tech * indoor * efficiency : 0.04f),
            usedSafeFail = depleted,
            actionLabel = depleted
                ? "鍛冶（薪/鉱石不足・効率低下）"
                : forge ? "鉱石精錬・道具手入れ" : "炉の維持"
        };
        return delta;
    }

    private VillageResourceDelta SimulateBarrierKeeper(
        int phase,
        float indoor,
        float tech,
        VillageResourceStatus stock)
    {
        float wantCrystal = (0.55f + 0.15f * skill) / Mathf.Lerp(1f, 1.2f, (tech - 1f) / 1.5f);
        wantCrystal *= Mathf.Lerp(1f, 0.92f, indoor);
        float efficiency = stock.ResolveConsumeEfficiency(0f, 0f, 0f, wantCrystal);
        bool depleted = efficiency < 0.999f;
        return new VillageResourceDelta
        {
            manaCrystal = -wantCrystal,
            usedSafeFail = depleted,
            actionLabel = depleted
                ? "結界点検（魔力結晶不足・回復低下）"
                : "結界点検・結晶充填"
        };
    }

    private VillageResourceDelta SimulateCarpenter(
        int phase,
        float indoor,
        float tech,
        VillageResourceStatus stock)
    {
        float wantWood = 0.35f;
        float efficiency = stock.ResolveConsumeEfficiency(wantWood, 0f, 0f, 0f);
        bool depleted = efficiency < 0.999f;
        return new VillageResourceDelta
        {
            wood = -wantWood,
            ore = 0.05f * skill * tech * indoor * efficiency,
            usedSafeFail = depleted,
            actionLabel = depleted
                ? "木工（材木不足・効率低下）"
                : "建材加工・骨組み補強"
        };
    }

    private static bool IsDayShift(int phase)
    {
        int p = ProceduralMapPopulator.NormalizePhase(phase);
        return p <= 14 || p >= 23;
    }

    private static float OutdoorFactor(VariableTimelineSeason season)
    {
        switch (season)
        {
            case VariableTimelineSeason.Active:
                return 0.42f;
            case VariableTimelineSeason.Escalation:
                return 0.58f;
            case VariableTimelineSeason.Deescalation:
                return 0.86f;
            default:
                return 1f;
        }
    }

    private static float IndoorFactor(VariableTimelineSeason season)
    {
        switch (season)
        {
            case VariableTimelineSeason.Active:
                return 0.9f;
            default:
                return 1f;
        }
    }
}

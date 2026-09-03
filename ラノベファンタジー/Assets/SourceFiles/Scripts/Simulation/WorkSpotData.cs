using UnityEngine;

// =============================================================================
// 村作業スポット定義 — NPC Utility AI / 倉庫納品の物理拠点
// =============================================================================

/// <summary>3D マップ上の作業スポット種別。</summary>
public enum WorkSpotType
{
    BarrierAnchor = 0,
    BarrierCore = 1,
    WoodcutterCamp = 2,
    Farmland = 3,
    MiningShaft = 4,
    Workshop = 5
}

/// <summary>スポットが産出／消費する資源の係数（位相あたり）。</summary>
[System.Serializable]
public struct WorkSpotResourceYield
{
    public float food;
    public float timber;
    public float ore;
    public float manaCrystal;

    public static WorkSpotResourceYield ForType(WorkSpotType type)
    {
        switch (type)
        {
            case WorkSpotType.WoodcutterCamp:
                return new WorkSpotResourceYield { timber = 1.4f, manaCrystal = 0.08f };
            case WorkSpotType.Farmland:
                return new WorkSpotResourceYield { food = 1.25f, manaCrystal = 0.05f };
            case WorkSpotType.MiningShaft:
                return new WorkSpotResourceYield { ore = 0.95f, timber = -0.1f };
            case WorkSpotType.Workshop:
                return new WorkSpotResourceYield { ore = 0.35f, timber = -0.25f };
            case WorkSpotType.BarrierCore:
            case WorkSpotType.BarrierAnchor:
                return new WorkSpotResourceYield { manaCrystal = -0.35f };
            default:
                return default;
        }
    }

    public string FormatShort()
    {
        return $"F{food:F1}/T{timber:F1}/O{ore:F1}/C{manaCrystal:F2}";
    }
}

/// <summary>1 箇所の作業スポット。</summary>
[System.Serializable]
public class WorkSpotData
{
    public string SpotId = string.Empty;
    public WorkSpotType Type = WorkSpotType.Workshop;
    public Vector3 WorldPosition;
    public string TargetJobId = nameof(NpcCivicJob.Farmer);
    public WorkSpotResourceYield ResourceYield;
    [Range(0f, 100f)] public float Durability = 100f;
    [Range(0f, 100f)] public float Integrity = 100f;

    public bool IsFallback;
    public Transform SceneAnchor;

    [Tooltip("伐採場の魔導変異・魔力果実。未設定時は通常材へ Safe-Fail。")]
    public BotanicalMutationStatus Botany = new BotanicalMutationStatus();

    public float YieldEfficiency =>
        Mathf.Clamp01((Durability * 0.5f + Integrity * 0.5f) / 100f);

    public BotanicalMutationStatus ResolveBotany()
    {
        return Botany ?? (Botany = new BotanicalMutationStatus());
    }

    public static WorkSpotData CreateFallback(Vector3 position)
    {
        return new WorkSpotData
        {
            SpotId = "SPOT_FALLBACK_CENTER",
            Type = WorkSpotType.Workshop,
            WorldPosition = position,
            TargetJobId = nameof(NpcCivicJob.Farmer),
            ResourceYield = WorkSpotResourceYield.ForType(WorkSpotType.Farmland),
            Durability = 50f,
            Integrity = 50f,
            IsFallback = true,
            Botany = new BotanicalMutationStatus()
        };
    }

    public static string DefaultJobForType(WorkSpotType type)
    {
        switch (type)
        {
            case WorkSpotType.WoodcutterCamp:
                return nameof(NpcCivicJob.Woodcutter);
            case WorkSpotType.Farmland:
                return nameof(NpcCivicJob.Farmer);
            case WorkSpotType.MiningShaft:
                return nameof(NpcCivicJob.Blacksmith);
            case WorkSpotType.Workshop:
                return nameof(NpcCivicJob.Blacksmith);
            case WorkSpotType.BarrierCore:
            case WorkSpotType.BarrierAnchor:
                return nameof(NpcCivicJob.BarrierKeeper);
            default:
                return nameof(NpcCivicJob.Farmer);
        }
    }

    public static string TypeLabel(WorkSpotType type)
    {
        switch (type)
        {
            case WorkSpotType.BarrierAnchor:
                return "結界杭";
            case WorkSpotType.BarrierCore:
                return "結界核";
            case WorkSpotType.WoodcutterCamp:
                return "伐採場";
            case WorkSpotType.Farmland:
                return "農地・牧場";
            case WorkSpotType.MiningShaft:
                return "採掘坑道";
            case WorkSpotType.Workshop:
                return "中央工房";
            default:
                return type.ToString();
        }
    }

    public bool MatchesJob(string jobId)
    {
        NpcCivicJob job = NpcIndividualStatus.ParseJobId(jobId);
        NpcCivicJob target = NpcIndividualStatus.ParseJobId(TargetJobId);
        if (job == target)
        {
            return true;
        }

        // 牧場は牧畜も利用可 / 農地は Farmer・Rancher
        if (Type == WorkSpotType.Farmland &&
            (job == NpcCivicJob.Farmer || job == NpcCivicJob.Rancher))
        {
            return true;
        }

        // 結界守は杭・核の両方
        if (job == NpcCivicJob.BarrierKeeper &&
            (Type == WorkSpotType.BarrierAnchor || Type == WorkSpotType.BarrierCore))
        {
            return true;
        }

        // 鍛冶は工房・坑道
        if (job == NpcCivicJob.Blacksmith &&
            (Type == WorkSpotType.Workshop || Type == WorkSpotType.MiningShaft))
        {
            return true;
        }

        // 大工は工房・伐採場（材木調達）
        if (job == NpcCivicJob.Carpenter &&
            (Type == WorkSpotType.Workshop || Type == WorkSpotType.WoodcutterCamp))
        {
            return true;
        }

        return false;
    }
}

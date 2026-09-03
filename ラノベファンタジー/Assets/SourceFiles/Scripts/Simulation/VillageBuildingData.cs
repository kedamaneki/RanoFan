using UnityEngine;

// =============================================================================
// 村施設データ — 家屋 / 防壁 / 工房 / 倉庫
// =============================================================================

/// <summary>生活インフラの施設種別。</summary>
public enum BuildingType
{
    Housing = 0,
    Palisade = 1,
    Workshop = 2,
    Warehouse = 3
}

/// <summary>修復に必要な資材。</summary>
[System.Serializable]
public struct BuildingMaintenanceCost
{
    public float timber;
    public float ore;

    public static BuildingMaintenanceCost ForType(BuildingType type, int level)
    {
        float lv = Mathf.Clamp(level, 1, 5);
        switch (type)
        {
            case BuildingType.Palisade:
                return new BuildingMaintenanceCost { timber = 0.9f + lv * 0.15f, ore = 0.25f + lv * 0.05f };
            case BuildingType.Workshop:
                return new BuildingMaintenanceCost { timber = 0.55f + lv * 0.1f, ore = 0.45f + lv * 0.12f };
            case BuildingType.Warehouse:
                return new BuildingMaintenanceCost { timber = 0.7f + lv * 0.1f, ore = 0.2f + lv * 0.05f };
            default:
                return new BuildingMaintenanceCost { timber = 0.6f + lv * 0.08f, ore = 0.1f };
        }
    }
}

/// <summary>1 棟の施設状態。</summary>
[System.Serializable]
public class VillageBuildingData
{
    public string BuildingId = string.Empty;
    public BuildingType Type = BuildingType.Housing;
    [Range(0f, 100f)] public float Durability = 100f;
    [Range(1, 5)] public int Level = 1;
    public BuildingMaintenanceCost MaintenanceCost;
    public string LinkedSpotId = string.Empty;
    public Transform SceneAnchor;
    public bool IsRepairing;

    public bool NeedsRepair => Durability < 72f;
    public bool IsCritical => Durability < 35f;
    public bool IsDamaged => Durability < 55f;

    public string StatusLabel
    {
        get
        {
            if (IsRepairing)
            {
                return "[修復中]";
            }

            if (IsCritical)
            {
                return "[破損]";
            }

            if (IsDamaged)
            {
                return "[損傷]";
            }

            return string.Empty;
        }
    }

    public static VillageBuildingData Create(
        string id,
        BuildingType type,
        int level,
        float durability,
        string linkedSpotId = null,
        Transform anchor = null)
    {
        int safeLevel = Mathf.Clamp(level, 1, 5);
        return new VillageBuildingData
        {
            BuildingId = string.IsNullOrWhiteSpace(id) ? $"BLD_{type}" : id.Trim(),
            Type = type,
            Level = safeLevel,
            Durability = Mathf.Clamp(durability, 0f, 100f),
            MaintenanceCost = BuildingMaintenanceCost.ForType(type, safeLevel),
            LinkedSpotId = linkedSpotId ?? string.Empty,
            SceneAnchor = anchor,
            IsRepairing = false
        };
    }

    public static string TypeLabel(BuildingType type)
    {
        switch (type)
        {
            case BuildingType.Palisade:
                return "防壁";
            case BuildingType.Workshop:
                return "工房";
            case BuildingType.Warehouse:
                return "倉庫";
            default:
                return "家屋";
        }
    }
}

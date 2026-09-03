using System.Collections.Generic;
using UnityEngine;

// =============================================================================
// 村インフラ — 施設耐久・生活効果・自動修復ハブ
// 連携: NpcCivilizationEngine / VillageStorageMarket / ProceduralMapPopulator
// =============================================================================

/// <summary>
/// Housing / Palisade / Workshop / Warehouse の耐久を管理し、
/// 休息効率・空腹増加・マーカー表示へ反映します。
/// </summary>
[DefaultExecutionOrder(49)]
public class VillageInfrastructureEngine : MonoBehaviour
{
    public const float RestMulMin = 0.5f;
    public const float RestMulMax = 1.5f;
    public const float DefaultRestMul = 1f;
    public const float ActiveDecayHousing = 2.8f;
    public const float ActiveDecayPalisade = 4.2f;
    public const float BarrierDropExtraDecay = 6.5f;
    public const float RepairThreshold = 72f;

    public static VillageInfrastructureEngine Instance { get; private set; }

    [SerializeField] private List<VillageBuildingData> buildings = new List<VillageBuildingData>();

    public IReadOnlyList<VillageBuildingData> Buildings => buildings;
    public int BuildingCount => buildings != null ? buildings.Count : 0;

    public static VillageInfrastructureEngine EnsureInstance()
    {
        if (Instance != null)
        {
            return Instance;
        }

        VillageInfrastructureEngine existing = Object.FindAnyObjectByType<VillageInfrastructureEngine>();
        if (existing != null)
        {
            Instance = existing;
            return Instance;
        }

        GameObject map = GameObject.Find(Nation001ProceduralMapBuilder.RootName);
        GameObject hub = map != null ? map : GameObject.Find("DebugSystemsHub");
        GameObject host = hub != null ? hub : new GameObject(nameof(VillageInfrastructureEngine));
        VillageInfrastructureEngine engine = host.GetComponent<VillageInfrastructureEngine>();
        return engine != null ? engine : host.AddComponent<VillageInfrastructureEngine>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            return;
        }

        Instance = this;
        EnsureDefaults();
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    public void EnsureDefaults()
    {
        if (buildings == null)
        {
            buildings = new List<VillageBuildingData>();
        }

        if (buildings.Count > 0)
        {
            return;
        }

        // マップ未生成時の最低限シェルター（Safe-Fail・補正 1.0）
        buildings.Add(VillageBuildingData.Create("BLD_HOUSE_SHELTER", BuildingType.Housing, 1, 100f));
        buildings.Add(VillageBuildingData.Create("BLD_PALISADE_GATE", BuildingType.Palisade, 1, 100f));
        buildings.Add(VillageBuildingData.Create("BLD_WORKSHOP_MAIN", BuildingType.Workshop, 2, 100f));
        buildings.Add(VillageBuildingData.Create("BLD_WAREHOUSE", BuildingType.Warehouse, 1, 100f));
    }

    /// <summary>マップ配置後に家屋・結界杭・工房へ施設を紐づけます。</summary>
    public void BootstrapFromMap(ProceduralMapPopulator populator)
    {
        try
        {
            if (buildings == null)
            {
                buildings = new List<VillageBuildingData>();
            }

            buildings.Clear();
            VillageWorkSpotManager spots = VillageWorkSpotManager.EnsureInstance();

            if (populator != null && populator.transform != null)
            {
                Transform village = populator.transform.Find("Village");
                if (village != null)
                {
                    int houseIndex = 0;
                    for (int i = 0; i < village.childCount; i++)
                    {
                        Transform child = village.GetChild(i);
                        if (child == null || !child.name.StartsWith("House_", System.StringComparison.Ordinal))
                        {
                            continue;
                        }

                        houseIndex++;
                        buildings.Add(VillageBuildingData.Create(
                            $"BLD_HOUSE_{houseIndex}",
                            BuildingType.Housing,
                            1,
                            92f + (houseIndex % 3) * 2f,
                            child.name,
                            child));
                    }
                }
            }

            if (spots != null)
            {
                for (int i = 0; i < spots.Spots.Count; i++)
                {
                    WorkSpotData spot = spots.Spots[i];
                    if (spot == null)
                    {
                        continue;
                    }

                    if (spot.Type == WorkSpotType.BarrierAnchor || spot.Type == WorkSpotType.BarrierCore)
                    {
                        buildings.Add(VillageBuildingData.Create(
                            $"BLD_PALISADE_{spot.SpotId}",
                            BuildingType.Palisade,
                            spot.Type == WorkSpotType.BarrierCore ? 3 : 2,
                            88f,
                            spot.SpotId,
                            spot.SceneAnchor));
                    }
                    else if (spot.Type == WorkSpotType.Workshop)
                    {
                        buildings.Add(VillageBuildingData.Create(
                            $"BLD_WS_{spot.SpotId}",
                            BuildingType.Workshop,
                            3,
                            90f,
                            spot.SpotId,
                            spot.SceneAnchor));
                    }
                }
            }

            buildings.Add(VillageBuildingData.Create(
                "BLD_WAREHOUSE_CENTRAL",
                BuildingType.Warehouse,
                2,
                95f,
                "SPOT_BARRIER_CORE",
                spots?.FindById("SPOT_BARRIER_CORE")?.SceneAnchor));

            if (buildings.Count == 0)
            {
                EnsureDefaults();
            }

            SyncMarkerLabels();
            Debug.Log(
                $"<color=#B0BEC5><b>【インフラ】</b></color> 施設 {buildings.Count} 棟を登録 " +
                $"Housing平均耐久 {AverageHousingDurability():F0}% Rest×{ResolveRestRecoveryMultiplier():F2}");
        }
        catch (System.Exception exception)
        {
            Debug.LogWarning($"[VillageInfrastructureEngine] Bootstrap Safe-Fail: {exception.Message}");
            EnsureDefaults();
        }
    }

    /// <summary>Housing 平均耐久 → 休息 HP/スタミナ回復倍率（0.5〜1.5、欠損時 1.0）。</summary>
    public float ResolveRestRecoveryMultiplier()
    {
        try
        {
            float avg = AverageHousingDurability();
            if (buildings == null || CountByType(BuildingType.Housing) == 0)
            {
                return DefaultRestMul;
            }

            // 0%→0.5 / 50%→1.0 / 100%→1.5
            return Mathf.Clamp(RestMulMin + (avg / 100f), RestMulMin, RestMulMax);
        }
        catch (System.Exception)
        {
            return DefaultRestMul;
        }
    }

    /// <summary>家屋が悪いほど空腹が増えやすい（1.0〜1.6、欠損時 1.0）。</summary>
    public float ResolveHungerGrowthMultiplier()
    {
        try
        {
            float avg = AverageHousingDurability();
            if (CountByType(BuildingType.Housing) == 0)
            {
                return 1f;
            }

            float t = 1f - Mathf.Clamp01(avg / 100f);
            return Mathf.Clamp(1f + t * 0.6f, 1f, 1.6f);
        }
        catch (System.Exception)
        {
            return 1f;
        }
    }

    public float AverageHousingDurability()
    {
        float sum = 0f;
        int n = 0;
        if (buildings == null)
        {
            return 100f;
        }

        for (int i = 0; i < buildings.Count; i++)
        {
            VillageBuildingData b = buildings[i];
            if (b == null || b.Type != BuildingType.Housing)
            {
                continue;
            }

            sum += b.Durability;
            n++;
        }

        return n == 0 ? 100f : sum / n;
    }

    public float LowestRepairableDurability()
    {
        float lowest = 100f;
        bool any = false;
        if (buildings == null)
        {
            return 100f;
        }

        for (int i = 0; i < buildings.Count; i++)
        {
            VillageBuildingData b = buildings[i];
            if (b == null)
            {
                continue;
            }

            any = true;
            if (b.Durability < lowest)
            {
                lowest = b.Durability;
            }
        }

        return any ? lowest : 100f;
    }

    public VillageBuildingData FindMostDamagedRepairable()
    {
        VillageBuildingData best = null;
        if (buildings == null)
        {
            return null;
        }

        for (int i = 0; i < buildings.Count; i++)
        {
            VillageBuildingData b = buildings[i];
            if (b == null || !b.NeedsRepair)
            {
                continue;
            }

            if (best == null || b.Durability < best.Durability)
            {
                best = b;
            }
        }

        return best;
    }

    /// <summary>活性期・結界破綻時の耐久減衰。</summary>
    public void ApplySeasonPressure(VariableTimelineSeason season, bool barrierDropped)
    {
        EnsureDefaults();
        bool active = season == VariableTimelineSeason.Active ||
                      season == VariableTimelineSeason.Escalation;
        if (!active && !barrierDropped)
        {
            ClearRepairingFlagsIfHealthy();
            SyncMarkerLabels();
            return;
        }

        for (int i = 0; i < buildings.Count; i++)
        {
            VillageBuildingData b = buildings[i];
            if (b == null)
            {
                continue;
            }

            float decay = 0f;
            if (active)
            {
                if (b.Type == BuildingType.Housing)
                {
                    decay += ActiveDecayHousing;
                }
                else if (b.Type == BuildingType.Palisade)
                {
                    decay += ActiveDecayPalisade;
                }
                else
                {
                    decay += 1.2f;
                }
            }

            if (barrierDropped &&
                (b.Type == BuildingType.Housing || b.Type == BuildingType.Palisade))
            {
                decay += BarrierDropExtraDecay;
            }

            b.Durability = Mathf.Clamp(b.Durability - decay, 0f, 100f);
            b.IsRepairing = false;
        }

        SyncMarkerLabels();
    }

    /// <summary>魔物侵攻による施設耐久ダメージ。Housing / Workshop を優先します。</summary>
    public string ApplyInvasionDamage(float amount, bool housingAndWorkshopOnly, string reason)
    {
        EnsureDefaults();
        float dmg = amount;
        if (float.IsNaN(dmg) || float.IsInfinity(dmg) || dmg < 0f)
        {
            dmg = 0f;
        }

        int hit = 0;
        float total = 0f;
        for (int i = 0; i < buildings.Count; i++)
        {
            VillageBuildingData b = buildings[i];
            if (b == null)
            {
                continue;
            }

            if (housingAndWorkshopOnly &&
                b.Type != BuildingType.Housing &&
                b.Type != BuildingType.Workshop)
            {
                continue;
            }

            float before = b.Durability;
            b.Durability = Mathf.Clamp(b.Durability - dmg, 0f, 100f);
            total += before - b.Durability;
            hit++;
        }

        SyncMarkerLabels();
        string why = string.IsNullOrWhiteSpace(reason) ? "魔物侵入" : reason;
        return $"【施設被弾】{why} {hit}棟 合計-{total:F1}%";
    }

    /// <summary>指定スポットに紐づく防壁（結界杭）へ直接ダメージします。</summary>
    public string DamageLinkedPalisade(string spotId, float amount)
    {
        EnsureDefaults();
        float dmg = Mathf.Max(0f, amount);
        if (float.IsNaN(dmg) || float.IsInfinity(dmg))
        {
            dmg = 0f;
        }

        VillageBuildingData target = null;
        for (int i = 0; i < buildings.Count; i++)
        {
            VillageBuildingData b = buildings[i];
            if (b == null || b.Type != BuildingType.Palisade)
            {
                continue;
            }

            if (!string.IsNullOrEmpty(spotId) &&
                string.Equals(b.LinkedSpotId, spotId, System.StringComparison.OrdinalIgnoreCase))
            {
                target = b;
                break;
            }

            if (target == null)
            {
                target = b;
            }
        }

        if (target == null)
        {
            return "杭攻撃対象なし（Safe-Fail）";
        }

        float before = target.Durability;
        target.Durability = Mathf.Clamp(target.Durability - dmg, 0f, 100f);
        SyncMarkerLabels();
        return $"{VillageBuildingData.TypeLabel(target.Type)} {target.BuildingId} {before:F0}%→{target.Durability:F0}%";
    }

    /// <summary>倉庫資材で修復。不足時は回復量低下（Safe-Fail）。</summary>
    public string TryRepair(VillageBuildingData building, NpcIndividualStatus npc, VillageStorageMarket market)
    {
        if (building == null)
        {
            return "修復対象なし（Safe-Fail）";
        }

        if (market == null)
        {
            market = new VillageStorageMarket();
        }

        building.IsRepairing = true;
        float needT = Mathf.Max(0.05f, building.MaintenanceCost.timber);
        float needO = Mathf.Max(0.01f, building.MaintenanceCost.ore);
        float gotT = market.WithdrawTimber(needT);
        float gotO = market.WithdrawOre(needO);
        float fill = 1f;
        if (needT > 0.001f)
        {
            fill = Mathf.Min(fill, gotT / needT);
        }

        if (needO > 0.001f)
        {
            fill = Mathf.Min(fill, gotO / needO);
        }

        bool shortage = fill < 0.999f;
        if (shortage)
        {
            fill = Mathf.Max(VillageStorageMarket.DepletionEfficiency, fill);
        }

        float skill = npc != null ? Mathf.Clamp(npc.JobProficiency, 1f, 3f) : 1f;
        float recover = (12f + skill * 4f) * fill;
        if (npc != null && npc.CivicJob == NpcCivicJob.Blacksmith && building.Type == BuildingType.Workshop)
        {
            recover *= 1.15f;
        }

        if (npc != null && npc.CivicJob == NpcCivicJob.Carpenter)
        {
            recover *= 1.2f;
        }

        float before = building.Durability;
        building.Durability = Mathf.Clamp(building.Durability + recover, 0f, 100f);
        if (building.Durability >= RepairThreshold)
        {
            building.IsRepairing = false;
        }

        SyncMarkerLabels();

        string tag = shortage ? "資材不足・回復低下" : "完了";
        string npcName = npc != null && !string.IsNullOrEmpty(npc.Name) ? npc.Name : "?";
        string typeLabel = VillageBuildingData.TypeLabel(building.Type);
        return string.Format(
            "【インフラ修復】{0}->{1} {2} {3:F0}%->{4:F0}% (T-{5:F1}/O-{6:F1}) {7}",
            npcName,
            typeLabel,
            building.BuildingId,
            before,
            building.Durability,
            gotT,
            gotO,
            tag);
    }

    public void SyncMarkerLabels()
    {
        try
        {
            if (buildings == null)
            {
                return;
            }

            for (int i = 0; i < buildings.Count; i++)
            {
                VillageBuildingData b = buildings[i];
                if (b?.SceneAnchor == null)
                {
                    continue;
                }

                string baseName = VillageBuildingData.TypeLabel(b.Type);
                string status = b.StatusLabel;
                string label = string.IsNullOrEmpty(status)
                    ? $"{baseName} {b.Durability:F0}%"
                    : $"{baseName}{status} {b.Durability:F0}%";

                EnsureMarkerOnAnchor(b.SceneAnchor, label);
                UpdateMarkersFollowing(b.SceneAnchor, label);
            }

            ProceduralMapPopulator populator = Object.FindAnyObjectByType<ProceduralMapPopulator>();
            populator?.NotifyInfrastructureVisuals(buildings);
        }
        catch (System.Exception exception)
        {
            Debug.LogWarning($"[VillageInfrastructureEngine] Marker 同期 Safe-Fail: {exception.Message}");
        }
    }

    private void EnsureMarkerOnAnchor(Transform anchor, string label)
    {
        if (anchor == null)
        {
            return;
        }

        WorldSpaceMarker existing = anchor.GetComponentInChildren<WorldSpaceMarker>(true);
        if (existing != null)
        {
            existing.SetDisplayLabel(label);
            return;
        }

        GameObject markerObject = new GameObject($"InfraMarker_{anchor.name}");
        markerObject.transform.SetParent(anchor, false);
        WorldSpaceMarker marker = markerObject.AddComponent<WorldSpaceMarker>();
        marker.Initialize(anchor, WorldSpaceMarkerKind.WorkSpot, label);
        marker.SetDisplayLabel(label);
    }

    public string FormatSnapshot()
    {
        return $"施設{BuildingCount} Housing平均{AverageHousingDurability():F0}% " +
               $"最低耐久{LowestRepairableDurability():F0}% Rest×{ResolveRestRecoveryMultiplier():F2}";
    }

    private void UpdateMarkersFollowing(Transform anchor, string label)
    {
        WorldSpaceMarker[] all = Object.FindObjectsByType<WorldSpaceMarker>(
            FindObjectsInactive.Exclude);
        if (all == null)
        {
            return;
        }

        for (int i = 0; i < all.Length; i++)
        {
            WorldSpaceMarker marker = all[i];
            if (marker != null && marker.FollowTarget == anchor)
            {
                marker.SetDisplayLabel(label);
            }
        }
    }

    private void ClearRepairingFlagsIfHealthy()
    {
        for (int i = 0; i < buildings.Count; i++)
        {
            if (buildings[i] != null && buildings[i].Durability >= RepairThreshold)
            {
                buildings[i].IsRepairing = false;
            }
        }
    }

    private int CountByType(BuildingType type)
    {
        int n = 0;
        if (buildings == null)
        {
            return 0;
        }

        for (int i = 0; i < buildings.Count; i++)
        {
            if (buildings[i] != null && buildings[i].Type == type)
            {
                n++;
            }
        }

        return n;
    }
}

public static class VillageInfrastructureBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoAttach()
    {
        VillageInfrastructureEngine.EnsureInstance();
    }
}

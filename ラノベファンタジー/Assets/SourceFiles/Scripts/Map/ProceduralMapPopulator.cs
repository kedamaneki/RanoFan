using System.Collections.Generic;
using UnityEngine;

// =============================================================================
// 国家001 マップ — 村・祠・乾燥植生・魔獣スポーンの自律配置
// 24位相（活性 / 衰退中間 / 休眠 / 兆候中間）で見た目と安全地帯を切替
// =============================================================================

/// <summary>
/// 仮 Prefab（無ければ Primitive）をルール配置し、位相に応じて警告／工房マーカーを切り替えます。
/// </summary>
public class ProceduralMapPopulator : MonoBehaviour
{
    public const int PhaseCount = 24;
    public const string ResourcesHousePath = "Map/Nation001/House";
    public const string ResourcesShrinePath = "Map/Nation001/Shrine";
    public const string ResourcesShrubPath = "Map/Nation001/Shrub";
    public const string ResourcesRockPath = "Map/Nation001/Rock";
    public const string ResourcesSpawnPath = "Map/Nation001/SpawnMarker";
    public const string ResourcesWorkshopPath = "Map/Nation001/WorkshopPad";

    [SerializeField] private Terrain boundTerrain;
    [SerializeField] private int lastAppliedPhase = 1;
    [SerializeField] private int houseCount;
    [SerializeField] private int workshopCount;
    [SerializeField] private int spawnCount;
    [SerializeField] private VariablePhaseDistribution boundDistribution;
    [SerializeField] private List<PhaseMarker> beastWarningMarkers = new List<PhaseMarker>();
    [SerializeField] private List<PhaseMarker> workshopMarkers = new List<PhaseMarker>();
    [SerializeField] private List<PhaseMarker> safetyZoneMarkers = new List<PhaseMarker>();
    [SerializeField] private Transform villageRoot;
    [SerializeField] private Transform wildRoot;
    [SerializeField] private Transform spawnRoot;
    [SerializeField] private Transform workshopRoot;
    [SerializeField] private Transform workSpotRoot;
    [SerializeField] private int workSpotCount;

    public int LastAppliedPhase => lastAppliedPhase;
    public int HouseCount => houseCount;
    public int WorkshopCount => workshopCount;
    public int SpawnCount => spawnCount;
    public int WorkSpotCount => workSpotCount;
    public Terrain BoundTerrain => boundTerrain;
    public Transform WorkshopRoot => workshopRoot;
    public Transform SpawnRoot => spawnRoot;
    public VariablePhaseDistribution BoundDistribution => boundDistribution;
    public VariableTimelineSeason LastSeason => ResolveSeason(lastAppliedPhase, boundDistribution);

    /// <summary>工房パッド（WorkshopKey_1〜5）を dest へ収集します。</summary>
    public void CollectWorkshopPads(List<Transform> dest)
    {
        CollectPrefixedChildren(workshopRoot, "WorkshopKey_", dest);
    }

    /// <summary>魔獣スポーン（BeastSpawn_1〜）を dest へ収集します。</summary>
    public void CollectBeastSpawns(List<Transform> dest)
    {
        CollectPrefixedChildren(spawnRoot, "BeastSpawn_", dest);
    }

    /// <summary>年表の 24 位相配分をバインドし、以降の環境切替に使います。</summary>
    public void BindTimeline(MicroHistoryTimeline timeline)
    {
        if (timeline != null && timeline.distribution != null)
        {
            boundDistribution = timeline.distribution;
            return;
        }

        boundDistribution = DefaultNation001Turn1Distribution();
    }

    /// <summary>地形上へ村・祠・低木・岩・スポーンと工房パッドを配置します。</summary>
    public void Populate(Terrain terrain)
    {
        boundTerrain = terrain;
        ClearChildren();
        EnsureFolders();
        houseCount = 0;
        workshopCount = 0;
        spawnCount = 0;
        workSpotCount = 0;

        PlaceVillage();
        PlaceShrine();
        PlaceDryFloraAndRocks();
        PlaceBeastSpawns();
        PlaceWorkshopPads();
        PlaceWorkSpots();
        UpdatePhaseEnvironment(1);
    }

    /// <summary>
    /// 24 位相の環境切替。活性期は魔獣警告 ON、休眠期は工房 1〜5 と TownSafetyZoneGate を強調します。
    /// <paramref name="currentPhase"/> は 1〜24（0 始まりも 1 として扱います）。
    /// </summary>
    public void UpdatePhaseEnvironment(int currentPhase)
    {
        int phase = NormalizePhase(currentPhase);
        lastAppliedPhase = phase;
        VariableTimelineSeason season = ResolveSeason(phase, boundDistribution);

        bool warnBeasts = season == VariableTimelineSeason.Active ||
                          season == VariableTimelineSeason.Escalation;
        bool highlightWorkshop = season == VariableTimelineSeason.Dormant;
        bool highlightSafety = season == VariableTimelineSeason.Dormant ||
                               season == VariableTimelineSeason.Deescalation;

        ApplyMarkers(beastWarningMarkers, warnBeasts, warnBeasts ? 1.25f : 1f);
        ApplyMarkers(workshopMarkers, true, highlightWorkshop ? 1.35f : 1f);
        ApplyMarkers(safetyZoneMarkers, true, highlightSafety ? 1.2f : 1f);

        try
        {
            TownSafetyZoneGate.SetInsideTown(highlightSafety);
        }
        catch (System.Exception exception)
        {
            Debug.LogWarning($"[ProceduralMapPopulator] TownSafetyZoneGate 切替をスキップ: {exception.Message}");
        }

        Debug.Log(
            $"<color=#FFE082>【国家001マップ】位相 {phase}/24 → {season} " +
            $"魔獣警告={(warnBeasts ? "ON" : "off")} 工房強調={(highlightWorkshop ? "ON" : "off")} " +
            $"安全地帯={(highlightSafety ? "町内" : "屋外")}</color>");
    }

    /// <summary>365 日のうち指定日に対応する 24 位相へ環境を同期します。</summary>
    public void UpdateEnvironmentForDay(int dayOfYear)
    {
        int day = Mathf.Clamp(dayOfYear, 1, MicroHistoryTimelineTimelineEngine.DaysPerYear);
        int phase = Mathf.Clamp(
            Mathf.CeilToInt(day * PhaseCount / (float)MicroHistoryTimelineTimelineEngine.DaysPerYear),
            1,
            PhaseCount);
        UpdatePhaseEnvironment(phase);
    }

    public static int NormalizePhase(int currentPhase)
    {
        if (currentPhase <= 0)
        {
            return 1;
        }

        if (currentPhase > PhaseCount)
        {
            return ((currentPhase - 1) % PhaseCount) + 1;
        }

        return currentPhase;
    }

    /// <summary>年表があればその配分、無ければ国家001・ターン1の 12/4/4/4 を使います。</summary>
    public static VariableTimelineSeason ResolveSeason(int phase1To24, VariablePhaseDistribution distribution)
    {
        int index = Mathf.Clamp(phase1To24 - 1, 0, PhaseCount - 1);
        VariablePhaseDistribution dist = distribution;
        if (dist == null || dist.TotalPhases != PhaseCount)
        {
            dist = DefaultNation001Turn1Distribution();
        }

        return MicroHistoryTimelineTimelineEngine.SeasonAtPhase(dist, index);
    }

    public static VariablePhaseDistribution DefaultNation001Turn1Distribution()
    {
        return new VariablePhaseDistribution
        {
            usedSafeFailFallback = false,
            activePhases = 12,
            deescalationPhases = 4,
            dormantPhases = 4,
            escalationPhases = 4
        };
    }

    /// <summary>配分上の休眠期の先頭位相（1始まり）。</summary>
    public static int FirstDormantPhase(VariablePhaseDistribution distribution)
    {
        VariablePhaseDistribution dist = distribution ?? DefaultNation001Turn1Distribution();
        return Mathf.Clamp(dist.activePhases + dist.deescalationPhases + 1, 1, PhaseCount);
    }

    private void PlaceVillage()
    {
        Vector2[] houseOffsets =
        {
            new Vector2(0f, 0f),
            new Vector2(0.028f, 0.012f),
            new Vector2(-0.024f, 0.018f),
            new Vector2(0.018f, -0.022f),
            new Vector2(-0.030f, -0.016f),
            new Vector2(0.008f, 0.032f),
            new Vector2(-0.012f, -0.034f)
        };

        for (int i = 0; i < houseOffsets.Length; i++)
        {
            Vector2 n = ProceduralTerrainGenerator.VillageNormalized + houseOffsets[i];
            GameObject house = SpawnProp(
                ResourcesHousePath,
                PrimitiveType.Cube,
                new Color(0.72f, 0.55f, 0.38f),
                GroundedWorld(n.x, n.y, 3.4f),
                new Vector3(4.2f, 3.4f, 5.1f),
                villageRoot,
                $"House_{i + 1}");
            if (house != null)
            {
                house.transform.rotation = Quaternion.Euler(0f, i * 27f, 0f);
                houseCount++;
            }
        }
    }

    private void PlaceShrine()
    {
        Vector2 shrineN = ProceduralTerrainGenerator.VillageNormalized + new Vector2(0.002f, 0.006f);
        GameObject shrine = SpawnProp(
            ResourcesShrinePath,
            PrimitiveType.Cylinder,
            new Color(0.55f, 0.78f, 0.92f),
            GroundedWorld(shrineN.x, shrineN.y, 6.5f),
            new Vector3(2.4f, 6.5f, 2.4f),
            villageRoot,
            "BarrierShrine");
        if (shrine != null)
        {
            GameObject halo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            halo.name = "ShrineHalo";
            halo.transform.SetParent(shrine.transform, false);
            halo.transform.localPosition = new Vector3(0f, 0.7f, 0f);
            halo.transform.localScale = new Vector3(0.55f, 0.18f, 0.55f);
            ApplyColor(halo, new Color(0.4f, 0.9f, 1f, 0.55f), transparent: true);
            DisableCollider(halo);
            RegisterMarker(safetyZoneMarkers, halo);
        }

        GameObject safetyRing = SpawnProp(
            string.Empty,
            PrimitiveType.Cylinder,
            new Color(0.35f, 0.85f, 0.55f, 0.4f),
            GroundedWorld(
                ProceduralTerrainGenerator.VillageNormalized.x,
                ProceduralTerrainGenerator.VillageNormalized.y,
                0.25f),
            new Vector3(28f, 0.25f, 28f),
            villageRoot,
            "TownSafetyZoneMarker");
        if (safetyRing != null)
        {
            DisableCollider(safetyRing);
            RegisterMarker(safetyZoneMarkers, safetyRing);
        }
    }

    private void PlaceDryFloraAndRocks()
    {
        int seed = 1001;
        for (int i = 0; i < 40; i++)
        {
            float nx = Hash01(seed + i * 17) * 0.7f + 0.22f;
            float nz = Hash01(seed + i * 29 + 3);
            if (DistanceToVillage(nx, nz) < 0.08f)
            {
                continue;
            }

            bool rock = i % 3 == 0;
            float height = rock ? 1.6f : 1.4f;
            SpawnProp(
                rock ? ResourcesRockPath : ResourcesShrubPath,
                rock ? PrimitiveType.Cube : PrimitiveType.Capsule,
                rock ? new Color(0.45f, 0.40f, 0.34f) : new Color(0.50f, 0.42f, 0.22f),
                GroundedWorld(nx, nz, height),
                rock ? new Vector3(2.2f, 1.6f, 1.8f) : new Vector3(0.7f, 1.4f, 0.7f),
                wildRoot,
                rock ? $"Rock_{i}" : $"Shrub_{i}");
        }
    }

    private void PlaceBeastSpawns()
    {
        Vector2[] foothills =
        {
            new Vector2(0.30f, 0.42f),
            new Vector2(0.33f, 0.58f),
            new Vector2(0.27f, 0.50f),
            new Vector2(0.36f, 0.36f),
            new Vector2(0.38f, 0.66f)
        };

        for (int i = 0; i < foothills.Length; i++)
        {
            GameObject marker = SpawnProp(
                ResourcesSpawnPath,
                PrimitiveType.Sphere,
                new Color(0.95f, 0.25f, 0.18f, 0.7f),
                WorldOnTerrain(foothills[i].x, foothills[i].y) + Vector3.up * 2.2f,
                new Vector3(3.2f, 3.2f, 3.2f),
                spawnRoot,
                $"BeastSpawn_{i + 1}");
            if (marker != null)
            {
                DisableCollider(marker);
                RegisterMarker(beastWarningMarkers, marker);
                spawnCount++;
            }
        }
    }

    private void PlaceWorkshopPads()
    {
        Vector2 origin = ProceduralTerrainGenerator.VillageNormalized + new Vector2(0.04f, -0.03f);
        for (int i = 0; i < 5; i++)
        {
            float ang = i * Mathf.PI * 0.4f - 0.4f;
            Vector2 n = origin + new Vector2(Mathf.Cos(ang) * 0.022f, Mathf.Sin(ang) * 0.022f);
            GameObject pad = SpawnProp(
                ResourcesWorkshopPath,
                PrimitiveType.Cylinder,
                new Color(0.95f, 0.82f, 0.35f),
                GroundedWorld(n.x, n.y, 0.35f),
                new Vector3(3.4f, 0.35f, 3.4f),
                workshopRoot,
                $"WorkshopKey_{i + 1}");
            if (pad != null)
            {
                DisableCollider(pad);
                RegisterMarker(workshopMarkers, pad);
                workshopCount++;
            }
        }
    }

    /// <summary>
    /// 結界杭・結界核・伐採場・農地・採掘坑・工房を作業スポットとして配置し、
    /// VillageWorkSpotManager へ登録します。
    /// </summary>
    private void PlaceWorkSpots()
    {
        VillageWorkSpotManager manager = GetComponent<VillageWorkSpotManager>();
        if (manager == null)
        {
            manager = gameObject.AddComponent<VillageWorkSpotManager>();
        }

        manager.ClearSpots();
        workSpotCount = 0;
        Vector2 village = ProceduralTerrainGenerator.VillageNormalized;

        // 結界核（村中央）
        RegisterWorkSpotVisual(
            manager,
            "SPOT_BARRIER_CORE",
            WorkSpotType.BarrierCore,
            village,
            PrimitiveType.Cylinder,
            new Color(0.35f, 0.85f, 1f),
            new Vector3(2.2f, 5.5f, 2.2f),
            withPointLight: true,
            treeDecor: false);

        // 結界杭（外周 4 点）
        Vector2[] anchors =
        {
            village + new Vector2(0.11f, 0.08f),
            village + new Vector2(-0.10f, 0.09f),
            village + new Vector2(0.10f, -0.09f),
            village + new Vector2(-0.11f, -0.08f)
        };
        for (int i = 0; i < anchors.Length; i++)
        {
            RegisterWorkSpotVisual(
                manager,
                $"SPOT_BARRIER_ANCHOR_{i + 1}",
                WorkSpotType.BarrierAnchor,
                anchors[i],
                PrimitiveType.Cylinder,
                new Color(0.45f, 0.7f, 0.95f),
                new Vector3(1.1f, 4.2f, 1.1f),
                withPointLight: true,
                treeDecor: false);
        }

        // 伐採場（西側山麓）+ 木々
        RegisterWorkSpotVisual(
            manager,
            "SPOT_WOODCUTTER_CAMP",
            WorkSpotType.WoodcutterCamp,
            new Vector2(0.34f, 0.50f),
            PrimitiveType.Cube,
            new Color(0.42f, 0.32f, 0.18f),
            new Vector3(4.5f, 1.2f, 4.5f),
            withPointLight: false,
            treeDecor: true);

        // 農地・牧場（東側台地）
        RegisterWorkSpotVisual(
            manager,
            "SPOT_FARMLAND",
            WorkSpotType.Farmland,
            new Vector2(0.74f, 0.50f),
            PrimitiveType.Cube,
            new Color(0.48f, 0.62f, 0.28f),
            new Vector3(8f, 0.4f, 8f),
            withPointLight: false,
            treeDecor: false);

        // 採掘坑道（北西岩山）
        RegisterWorkSpotVisual(
            manager,
            "SPOT_MINING_SHAFT",
            WorkSpotType.MiningShaft,
            new Vector2(0.28f, 0.64f),
            PrimitiveType.Cube,
            new Color(0.38f, 0.36f, 0.40f),
            new Vector3(3.5f, 2.4f, 3.5f),
            withPointLight: false,
            treeDecor: false);

        // 中央工房 1〜5（既存 Workshop パッド位置と同期）
        Vector2 origin = village + new Vector2(0.04f, -0.03f);
        for (int i = 0; i < 5; i++)
        {
            float ang = i * Mathf.PI * 0.4f - 0.4f;
            Vector2 n = origin + new Vector2(Mathf.Cos(ang) * 0.022f, Mathf.Sin(ang) * 0.022f);
            RegisterWorkSpotVisual(
                manager,
                $"SPOT_WORKSHOP_{i + 1}",
                WorkSpotType.Workshop,
                n,
                PrimitiveType.Cylinder,
                new Color(0.9f, 0.55f, 0.25f),
                new Vector3(2.6f, 1.8f, 2.6f),
                withPointLight: false,
                treeDecor: false);
        }

        Debug.Log(
            $"<color=#80CBC4><b>【作業スポット】</b></color> {manager.SpotCount} 箇所を登録 " +
            manager.FormatRegistrySnapshot());

        try
        {
            VillageInfrastructureEngine.EnsureInstance().BootstrapFromMap(this);
        }
        catch (System.Exception exception)
        {
            Debug.LogWarning($"[ProceduralMapPopulator] インフラ Bootstrap をスキップ: {exception.Message}");
        }
    }

    /// <summary>施設耐久ステータスを Floating Marker へ反映します。</summary>
    public void NotifyInfrastructureVisuals(IReadOnlyList<VillageBuildingData> buildings)
    {
        if (buildings == null)
        {
            return;
        }

        try
        {
            for (int i = 0; i < buildings.Count; i++)
            {
                VillageBuildingData b = buildings[i];
                if (b?.SceneAnchor == null)
                {
                    continue;
                }

                string status = b.StatusLabel;
                string label = string.IsNullOrEmpty(status)
                    ? $"{VillageBuildingData.TypeLabel(b.Type)} {b.Durability:F0}%"
                    : $"{VillageBuildingData.TypeLabel(b.Type)}{status} {b.Durability:F0}%";

                WorldSpaceMarker[] markers = b.SceneAnchor.GetComponentsInChildren<WorldSpaceMarker>(true);
                if (markers == null)
                {
                    continue;
                }

                for (int m = 0; m < markers.Length; m++)
                {
                    markers[m]?.SetDisplayLabel(label);
                }
            }
        }
        catch (System.Exception exception)
        {
            Debug.LogWarning($"[ProceduralMapPopulator] インフラ表示 Safe-Fail: {exception.Message}");
        }
    }

    private void RegisterWorkSpotVisual(
        VillageWorkSpotManager manager,
        string spotId,
        WorkSpotType type,
        Vector2 normalized,
        PrimitiveType primitive,
        Color color,
        Vector3 scale,
        bool withPointLight,
        bool treeDecor)
    {
        float nx = Mathf.Clamp01(normalized.x);
        float nz = Mathf.Clamp01(normalized.y);
        Vector3 world = GroundedWorld(nx, nz, scale.y);
        GameObject prop = SpawnProp(
            string.Empty,
            primitive,
            color,
            world,
            scale,
            workSpotRoot,
            spotId);
        if (prop == null)
        {
            manager.RegisterSpot(
                spotId,
                type,
                VillageWorkSpotManager.FallbackWorldPosition,
                WorkSpotData.DefaultJobForType(type),
                null);
            workSpotCount++;
            return;
        }

        if (withPointLight)
        {
            Light light = prop.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(0.55f, 0.85f, 1f);
            light.intensity = type == WorkSpotType.BarrierCore ? 2.4f : 1.2f;
            light.range = type == WorkSpotType.BarrierCore ? 18f : 10f;
        }

        if (treeDecor)
        {
            for (int t = 0; t < 4; t++)
            {
                float ang = t * Mathf.PI * 0.5f;
                Vector3 treePos = world + new Vector3(Mathf.Cos(ang) * 5f, 0f, Mathf.Sin(ang) * 5f);
                treePos.y = WorldOnTerrain(nx + Mathf.Cos(ang) * 0.008f, nz + Mathf.Sin(ang) * 0.008f).y + 1.2f;
                SpawnProp(
                    string.Empty,
                    PrimitiveType.Capsule,
                    new Color(0.28f, 0.45f, 0.22f),
                    treePos,
                    new Vector3(1.2f, 3.2f, 1.2f),
                    workSpotRoot,
                    $"{spotId}_Tree_{t + 1}");
            }
        }

        manager.RegisterSpot(
            spotId,
            type,
            prop.transform.position,
            WorkSpotData.DefaultJobForType(type),
            prop.transform);
        workSpotCount++;
    }

    private Vector3 WorldOnTerrain(float nx, float nz)
    {
        return ProceduralTerrainGenerator.NormalizedToWorld(boundTerrain, Mathf.Clamp01(nx), Mathf.Clamp01(nz));
    }

    private Vector3 GroundedWorld(float nx, float nz, float height)
    {
        Vector3 world = WorldOnTerrain(nx, nz);
        world.y += height * 0.5f;
        return world;
    }

    private static float DistanceToVillage(float nx, float nz)
    {
        Vector2 v = ProceduralTerrainGenerator.VillageNormalized;
        float dx = nx - v.x;
        float dz = nz - v.y;
        return Mathf.Sqrt(dx * dx + dz * dz);
    }

    private GameObject SpawnProp(
        string resourcePath,
        PrimitiveType fallback,
        Color color,
        Vector3 worldPos,
        Vector3 scale,
        Transform parent,
        string objectName)
    {
        GameObject instance = null;
        if (!string.IsNullOrEmpty(resourcePath))
        {
            try
            {
                GameObject prefab = Resources.Load<GameObject>(resourcePath);
                if (prefab != null)
                {
                    instance = Instantiate(prefab);
                }
            }
            catch (System.Exception exception)
            {
                Debug.LogWarning($"[ProceduralMapPopulator] Prefab 読込失敗（{resourcePath}）: {exception.Message}");
            }
        }

        if (instance == null)
        {
            instance = GameObject.CreatePrimitive(fallback);
            ApplyColor(instance, color, color.a < 0.99f);
        }

        instance.name = objectName;
        instance.transform.SetParent(parent != null ? parent : transform, true);
        instance.transform.position = worldPos;
        instance.transform.localScale = scale;
        return instance;
    }

    private void EnsureFolders()
    {
        villageRoot = EnsureChild("Village");
        wildRoot = EnsureChild("WildProps");
        spawnRoot = EnsureChild("BeastSpawns");
        workshopRoot = EnsureChild("Workshops");
        workSpotRoot = EnsureChild("WorkSpots");
        beastWarningMarkers.Clear();
        workshopMarkers.Clear();
        safetyZoneMarkers.Clear();
    }

    private static void CollectPrefixedChildren(Transform root, string namePrefix, List<Transform> dest)
    {
        if (dest == null)
        {
            return;
        }

        dest.Clear();
        if (root == null || string.IsNullOrEmpty(namePrefix))
        {
            return;
        }

        for (int i = 0; i < root.childCount; i++)
        {
            Transform child = root.GetChild(i);
            if (child != null && child.name.StartsWith(namePrefix, System.StringComparison.Ordinal))
            {
                dest.Add(child);
            }
        }
    }

    private Transform EnsureChild(string childName)
    {
        Transform existing = transform.Find(childName);
        if (existing != null)
        {
            return existing;
        }

        GameObject child = new GameObject(childName);
        child.transform.SetParent(transform, false);
        return child.transform;
    }

    private void ClearChildren()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            Transform child = transform.GetChild(i);
            if (child.GetComponent<Terrain>() != null)
            {
                continue;
            }

            DestroySafe(child.gameObject);
        }
    }

    private static void RegisterMarker(List<PhaseMarker> markers, GameObject target)
    {
        if (markers == null || target == null)
        {
            return;
        }

        markers.Add(new PhaseMarker
        {
            target = target,
            baseScale = target.transform.localScale
        });
    }

    private static void ApplyMarkers(List<PhaseMarker> markers, bool visible, float scaleMul)
    {
        if (markers == null)
        {
            return;
        }

        float mul = Mathf.Clamp(scaleMul, 0.5f, 2f);
        for (int i = 0; i < markers.Count; i++)
        {
            PhaseMarker marker = markers[i];
            if (marker.target == null)
            {
                continue;
            }

            marker.target.SetActive(visible);
            marker.target.transform.localScale = marker.baseScale * mul;
        }
    }

    private static void ApplyColor(GameObject target, Color color, bool transparent)
    {
        Renderer renderer = target != null ? target.GetComponent<Renderer>() : null;
        if (renderer == null)
        {
            return;
        }

        try
        {
            if (transparent)
            {
                RuntimeUrpMaterialUtility.ApplyTransparentColor(renderer, color);
            }
            else
            {
                RuntimeUrpMaterialUtility.ApplyOpaqueColor(renderer, color);
            }
        }
        catch (System.Exception)
        {
            if (renderer.sharedMaterial != null)
            {
                renderer.material.color = color;
            }
        }
    }

    private static void DisableCollider(GameObject target)
    {
        Collider collider = target != null ? target.GetComponent<Collider>() : null;
        if (collider != null)
        {
            collider.enabled = false;
        }
    }

    private static void DestroySafe(GameObject target)
    {
        if (target == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            Destroy(target);
        }
        else
        {
            DestroyImmediate(target);
        }
    }

    private static float Hash01(int seed)
    {
        uint x = (uint)seed;
        x ^= x >> 17;
        x *= 0xed5ad4bbu;
        x ^= x >> 11;
        x *= 0xac4c1b37u;
        return (x & 0x00ffffffu) / 16777215f;
    }

    [System.Serializable]
    private struct PhaseMarker
    {
        public GameObject target;
        public Vector3 baseScale;
    }
}

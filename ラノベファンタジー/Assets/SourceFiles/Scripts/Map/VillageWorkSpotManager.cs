using System.Collections.Generic;
using UnityEngine;

// =============================================================================
// 村作業スポット管理 — マップ配置と NPC Utility AI の参照ハブ
// 連携: ProceduralMapPopulator / NpcCivilizationEngine / WorldSpaceMarker
// =============================================================================

/// <summary>
/// Nation001 マップ上の作業スポットを保持し、JobId＋位置から最適スポットを返します。
/// 未配置時は (0,0,0) フォールバック（Safe-Fail）。
/// </summary>
[DefaultExecutionOrder(45)]
public class VillageWorkSpotManager : MonoBehaviour
{
    public static VillageWorkSpotManager Instance { get; private set; }

    public static readonly Vector3 FallbackWorldPosition = Vector3.zero;

    [SerializeField] private List<WorkSpotData> spots = new List<WorkSpotData>();
    [SerializeField] private Transform markerRoot;
    [SerializeField] private int lastRegisteredCount;

    public IReadOnlyList<WorkSpotData> Spots => spots;
    public int SpotCount => spots != null ? spots.Count : 0;

    public static VillageWorkSpotManager EnsureInstance()
    {
        if (Instance != null)
        {
            return Instance;
        }

        VillageWorkSpotManager existing = Object.FindAnyObjectByType<VillageWorkSpotManager>();
        if (existing != null)
        {
            Instance = existing;
            return Instance;
        }

        GameObject map = GameObject.Find(Nation001ProceduralMapBuilder.RootName);
        GameObject host = map != null ? map : new GameObject(nameof(VillageWorkSpotManager));
        VillageWorkSpotManager manager = host.GetComponent<VillageWorkSpotManager>();
        if (manager == null)
        {
            manager = host.AddComponent<VillageWorkSpotManager>();
        }

        Instance = manager;
        return Instance;
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            return;
        }

        Instance = this;
        if (spots == null)
        {
            spots = new List<WorkSpotData>();
        }
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    /// <summary>登録をクリアします（マップ再生成時）。</summary>
    public void ClearSpots()
    {
        if (spots == null)
        {
            spots = new List<WorkSpotData>();
        }
        else
        {
            spots.Clear();
        }

        lastRegisteredCount = 0;
        if (markerRoot != null)
        {
            for (int i = markerRoot.childCount - 1; i >= 0; i--)
            {
                DestroySafe(markerRoot.GetChild(i).gameObject);
            }
        }
    }

    /// <summary>既存登録をクリアし、リストを一括登録します（広域セル用）。</summary>
    public void ClearAndRegister(IReadOnlyList<WorkSpotData> source)
    {
        ClearSpots();
        if (source == null)
        {
            return;
        }

        for (int i = 0; i < source.Count; i++)
        {
            WorkSpotData src = source[i];
            if (src == null)
            {
                continue;
            }

            RegisterSpot(
                src.SpotId,
                src.Type,
                src.WorldPosition,
                src.TargetJobId,
                src.SceneAnchor,
                src.Durability,
                src.Integrity);
        }
    }

    /// <summary>スポットを登録し、頭上 Floating Marker を付けます。</summary>
    public WorkSpotData RegisterSpot(
        string spotId,
        WorkSpotType type,
        Vector3 worldPosition,
        string targetJobId,
        Transform sceneAnchor,
        float durability = 100f,
        float integrity = 100f)
    {
        if (spots == null)
        {
            spots = new List<WorkSpotData>();
        }

        WorkSpotData data = new WorkSpotData
        {
            SpotId = string.IsNullOrWhiteSpace(spotId) ? $"SPOT_{type}_{spots.Count + 1}" : spotId.Trim(),
            Type = type,
            WorldPosition = worldPosition,
            TargetJobId = string.IsNullOrWhiteSpace(targetJobId)
                ? WorkSpotData.DefaultJobForType(type)
                : targetJobId.Trim(),
            ResourceYield = WorkSpotResourceYield.ForType(type),
            Durability = Mathf.Clamp(durability, 0f, 100f),
            Integrity = Mathf.Clamp(integrity, 0f, 100f),
            SceneAnchor = sceneAnchor,
            IsFallback = false,
            Botany = new BotanicalMutationStatus()
        };

        spots.Add(data);
        lastRegisteredCount = spots.Count;
        AttachFloatingMarker(data);
        return data;
    }

    /// <summary>
    /// JobId に合う近隣スポットを返します。無ければ (0,0,0) フォールバック。
    /// </summary>
    public WorkSpotData GetOptimalWorkSpot(string jobId, Vector3 npcPosition)
    {
        try
        {
            if (spots == null || spots.Count == 0)
            {
                return WorkSpotData.CreateFallback(FallbackWorldPosition);
            }

            WorkSpotData best = null;
            float bestScore = float.NegativeInfinity;
            for (int i = 0; i < spots.Count; i++)
            {
                WorkSpotData spot = spots[i];
                if (spot == null || !spot.MatchesJob(jobId))
                {
                    continue;
                }

                float dist = Vector3.Distance(npcPosition, spot.WorldPosition);
                float integrityBias = spot.YieldEfficiency;
                float score = 1000f - dist + integrityBias * 40f;
                if (spot.Type == WorkSpotType.BarrierCore &&
                    NpcIndividualStatus.ParseJobId(jobId) == NpcCivicJob.BarrierKeeper)
                {
                    score += 25f;
                }

                try
                {
                    VillageBarrierBreachEngine breach = VillageBarrierBreachEngine.Instance;
                    if (breach != null && breach.IsBreachSpot(spot.SpotId))
                    {
                        score += 800f;
                    }
                }
                catch (System.Exception)
                {
                    // 侵入エンジン未配置でもスポット選択は継続
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    best = spot;
                }
            }

            if (best != null)
            {
                return best;
            }

            Debug.LogWarning(
                $"[VillageWorkSpotManager] Job '{jobId}' 向けスポットなし → (0,0,0) フォールバック（Safe-Fail）");
            return WorkSpotData.CreateFallback(FallbackWorldPosition);
        }
        catch (System.Exception exception)
        {
            Debug.LogWarning($"[VillageWorkSpotManager] GetOptimalWorkSpot Safe-Fail: {exception.Message}");
            return WorkSpotData.CreateFallback(FallbackWorldPosition);
        }
    }

    /// <summary>NpcUtilityAI から呼べる静的ラッパ。</summary>
    public static WorkSpotData ResolveOptimalWorkSpot(string jobId, Vector3 npcPosition)
    {
        try
        {
            VillageWorkSpotManager manager = EnsureInstance();
            if (manager == null)
            {
                return WorkSpotData.CreateFallback(FallbackWorldPosition);
            }

            return manager.GetOptimalWorkSpot(jobId, npcPosition);
        }
        catch (System.Exception)
        {
            return WorkSpotData.CreateFallback(FallbackWorldPosition);
        }
    }

    public WorkSpotData FindById(string spotId)
    {
        if (spots == null || string.IsNullOrWhiteSpace(spotId))
        {
            return null;
        }

        for (int i = 0; i < spots.Count; i++)
        {
            if (spots[i] != null &&
                string.Equals(spots[i].SpotId, spotId, System.StringComparison.OrdinalIgnoreCase))
            {
                return spots[i];
            }
        }

        return null;
    }

    public Vector3 ResolveVillageCenterOrFallback()
    {
        for (int i = 0; i < (spots?.Count ?? 0); i++)
        {
            if (spots[i] != null && spots[i].Type == WorkSpotType.BarrierCore)
            {
                return spots[i].WorldPosition;
            }
        }

        return FallbackWorldPosition;
    }

    /// <summary>伐採場一覧。未配置なら森林フォールバック 1 箇所を登録します（Safe-Fail）。</summary>
    public List<WorkSpotData> EnsureWoodcutterCamps()
    {
        List<WorkSpotData> camps = GetSpotsOfType(WorkSpotType.WoodcutterCamp);
        if (camps.Count > 0)
        {
            return camps;
        }

        Vector3 center = ResolveVillageCenterOrFallback();
        WorkSpotData fallback = RegisterSpot(
            "SPOT_WOOD_FALLBACK",
            WorkSpotType.WoodcutterCamp,
            center + new Vector3(22f, 0f, 18f),
            nameof(NpcCivicJob.Woodcutter),
            null,
            80f,
            80f);
        fallback.IsFallback = true;
        Debug.LogWarning("[VillageWorkSpotManager] 伐採場未登録 → 森林フォールバック（Safe-Fail）");
        camps.Add(fallback);
        return camps;
    }

    public List<WorkSpotData> GetSpotsOfType(WorkSpotType type)
    {
        List<WorkSpotData> list = new List<WorkSpotData>();
        if (spots == null)
        {
            return list;
        }

        for (int i = 0; i < spots.Count; i++)
        {
            if (spots[i] != null && spots[i].Type == type)
            {
                list.Add(spots[i]);
            }
        }

        return list;
    }

    public WorkSpotData FindNearestWoodcutterCamp(Vector3 from)
    {
        List<WorkSpotData> camps = EnsureWoodcutterCamps();
        WorkSpotData best = camps.Count > 0 ? camps[0] : null;
        float bestDist = float.PositiveInfinity;
        for (int i = 0; i < camps.Count; i++)
        {
            if (camps[i] == null)
            {
                continue;
            }

            float d = Vector3.Distance(from, camps[i].WorldPosition);
            if (d < bestDist)
            {
                bestDist = d;
                best = camps[i];
            }
        }

        return best;
    }

    /// <summary>環境魔力に応じて伐採場の魔導変異と魔力果実を更新します。</summary>
    public int UpdateBotanicalMutations(float manaEcologyLevel)
    {
        int mutated = 0;
        int fruits = 0;
        try
        {
            List<WorkSpotData> camps = EnsureWoodcutterCamps();
            for (int i = 0; i < camps.Count; i++)
            {
                WorkSpotData camp = camps[i];
                if (camp == null)
                {
                    continue;
                }

                BotanicalMutationStatus botany = camp.ResolveBotany();
                bool wasMutated = botany.IsManaMutated;
                bool hadFruit = botany.HasEdibleFruit;
                botany.EvaluateFromMana(manaEcologyLevel, camp.SpotId);
                if (botany.IsManaMutated)
                {
                    mutated++;
                    if (!wasMutated)
                    {
                        Debug.Log(
                            $"<color=#C5E1A5>【魔導変異】</color> {camp.SpotId} の樹木が変異 " +
                            $"(品質×{botany.TimberQualityMultiplier:0.00} 魔力 {manaEcologyLevel:F1})");
                    }
                }

                if (botany.HasEdibleFruit)
                {
                    fruits++;
                    if (!hadFruit)
                    {
                        Debug.Log(
                            $"<color=#FFD54F><b>【魔力果実】</b></color> {botany.FruitNodeId} が結実 " +
                            $"@{camp.SpotId} 在庫 {botany.FruitStock:F1}");
                    }
                }
            }
        }
        catch (System.Exception exception)
        {
            Debug.LogWarning($"[VillageWorkSpotManager] 植物変異 Safe-Fail: {exception.Message}");
        }

        return mutated + fruits;
    }

    public List<WorkSpotData> GetManaFruitCamps()
    {
        List<WorkSpotData> result = new List<WorkSpotData>();
        List<WorkSpotData> camps = GetSpotsOfType(WorkSpotType.WoodcutterCamp);
        for (int i = 0; i < camps.Count; i++)
        {
            WorkSpotData camp = camps[i];
            BotanicalMutationStatus botany = camp != null ? camp.ResolveBotany() : null;
            if (botany != null && botany.HasEdibleFruit)
            {
                result.Add(camp);
            }
        }

        return result;
    }

    public WorkSpotData FindNearestManaFruit(Vector3 from)
    {
        List<WorkSpotData> fruits = GetManaFruitCamps();
        WorkSpotData best = null;
        float bestDist = float.PositiveInfinity;
        for (int i = 0; i < fruits.Count; i++)
        {
            if (fruits[i] == null)
            {
                continue;
            }

            float d = Vector3.Distance(from, fruits[i].WorldPosition);
            if (d < bestDist)
            {
                bestDist = d;
                best = fruits[i];
            }
        }

        return best;
    }

    /// <summary>結界杭 4 箇所。未登録なら原点周辺のフォールバックを返します。</summary>
    public List<WorkSpotData> GetBarrierAnchors()
    {
        List<WorkSpotData> anchors = new List<WorkSpotData>();
        if (spots != null)
        {
            for (int i = 0; i < spots.Count; i++)
            {
                if (spots[i] != null && spots[i].Type == WorkSpotType.BarrierAnchor)
                {
                    anchors.Add(spots[i]);
                }
            }
        }

        if (anchors.Count > 0)
        {
            return anchors;
        }

        Vector3 center = ResolveVillageCenterOrFallback();
        Vector3[] offsets =
        {
            new Vector3(18f, 0f, 14f),
            new Vector3(-16f, 0f, 15f),
            new Vector3(16f, 0f, -14f),
            new Vector3(-18f, 0f, -13f)
        };
        for (int i = 0; i < offsets.Length; i++)
        {
            anchors.Add(new WorkSpotData
            {
                SpotId = $"SPOT_BARRIER_ANCHOR_{i + 1}",
                Type = WorkSpotType.BarrierAnchor,
                WorldPosition = center + offsets[i],
                TargetJobId = nameof(NpcCivicJob.BarrierKeeper),
                ResourceYield = WorkSpotResourceYield.ForType(WorkSpotType.BarrierAnchor),
                Durability = 70f,
                Integrity = 70f,
                IsFallback = true
            });
        }

        Debug.LogWarning("[VillageWorkSpotManager] 結界杭未登録 → 4点フォールバック（Safe-Fail）");
        return anchors;
    }

    public Vector3 ResolveHousingWorkshopCenter()
    {
        Vector3 sum = Vector3.zero;
        int n = 0;
        if (spots != null)
        {
            for (int i = 0; i < spots.Count; i++)
            {
                WorkSpotData spot = spots[i];
                if (spot == null)
                {
                    continue;
                }

                if (spot.Type == WorkSpotType.Workshop || spot.Type == WorkSpotType.BarrierCore)
                {
                    sum += spot.WorldPosition;
                    n++;
                }
            }
        }

        return n > 0 ? sum / n : ResolveVillageCenterOrFallback();
    }

    public string FormatRegistrySnapshot()
    {
        return $"spots={SpotCount} (Anchor/Core/Wood/Farm/Mine/Shop registered)";
    }

    private void AttachFloatingMarker(WorkSpotData data)
    {
        if (data == null || data.SceneAnchor == null)
        {
            return;
        }

        try
        {
            EnsureMarkerRoot();
            GameObject markerObject = new GameObject($"WorkSpotMarker_{data.SpotId}");
            markerObject.transform.SetParent(markerRoot, false);
            WorldSpaceMarker marker = markerObject.AddComponent<WorldSpaceMarker>();
            marker.Initialize(
                data.SceneAnchor,
                WorldSpaceMarkerKind.WorkSpot,
                $"{WorkSpotData.TypeLabel(data.Type)}");
        }
        catch (System.Exception exception)
        {
            Debug.LogWarning($"[VillageWorkSpotManager] Marker 配置をスキップ: {exception.Message}");
        }
    }

    private void EnsureMarkerRoot()
    {
        if (markerRoot != null)
        {
            return;
        }

        Transform existing = transform.Find("WorkSpotMarkers");
        if (existing != null)
        {
            markerRoot = existing;
            return;
        }

        GameObject root = new GameObject("WorkSpotMarkers");
        root.transform.SetParent(transform, false);
        markerRoot = root.transform;
    }

    private static void DestroySafe(GameObject target)
    {
        if (target == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            Object.Destroy(target);
        }
        else
        {
            Object.DestroyImmediate(target);
        }
    }
}

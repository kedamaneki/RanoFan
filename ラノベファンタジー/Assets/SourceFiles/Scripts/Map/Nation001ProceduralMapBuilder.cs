using UnityEngine;
#if UNITY_EDITOR
using System.IO;
using System.Text;
using UnityEditor;
#endif

// =============================================================================
// 国家001 プロシージャルマップ一括生成（ターン1・位相1 = 活性期）
// メニュー: Tools / Procedural Map / Generate Nation 001 Map
// =============================================================================

/// <summary>Terrain とプロップ配置をまとめて生成し、ターン1の活性期環境を適用します。</summary>
public static class Nation001ProceduralMapBuilder
{
    public const string RootName = "Nation001_ProceduralMap";
    public const int DefaultTurn = 1;
    public const int DefaultPhase = 1;

    /// <summary>シーン上の国家001マップを再生成し、配置結果を返します。</summary>
    public static Nation001MapBuildResult GenerateNation001Map(int turn = DefaultTurn, int phase = DefaultPhase)
    {
        Nation001MapBuildResult result = new Nation001MapBuildResult();
        try
        {
            Transform root = EnsureRoot();
            Terrain existing = root.GetComponentInChildren<Terrain>();
            Terrain terrain = ProceduralTerrainGenerator.GenerateOrApply(root, existing);
            result.terrainCreated = terrain != null;
            result.westHeight = ProceduralTerrainGenerator.SampleNormalizedHeight(0.12f, 0.50f);
            result.villageHeight = ProceduralTerrainGenerator.SampleNormalizedHeight(
                ProceduralTerrainGenerator.VillageNormalized.x,
                ProceduralTerrainGenerator.VillageNormalized.y);
            result.eastHeight = ProceduralTerrainGenerator.SampleNormalizedHeight(0.88f, 0.50f);
            result.westSlopeDegrees = ProceduralTerrainGenerator.EstimateWesternApproachSlopeDegrees();
            result.maxLocalSlopeDegrees = ProceduralTerrainGenerator.EstimateMaxLocalSlopeDegrees();

            ProceduralMapPopulator populator = root.GetComponent<ProceduralMapPopulator>();
            if (populator == null)
            {
                populator = root.gameObject.AddComponent<ProceduralMapPopulator>();
            }

            MicroHistoryTimeline timeline = null;
            try
            {
                timeline = MicroHistoryTimelineTimelineEngine.BuildTimeline(1, Mathf.Max(1, turn));
            }
            catch (System.Exception exception)
            {
                Debug.LogWarning($"[Nation001ProceduralMapBuilder] 年表同期をスキップ: {exception.Message}");
            }

            populator.BindTimeline(timeline);
            populator.Populate(terrain);
            populator.UpdatePhaseEnvironment(phase);

            result.houseCount = populator.HouseCount;
            result.workshopCount = populator.WorkshopCount;
            result.spawnCount = populator.SpawnCount;
            result.workSpotCount = populator.WorkSpotCount;
            result.propCount = CountTransforms(root);
            result.phase = ProceduralMapPopulator.NormalizePhase(phase);
            result.season = populator.LastSeason;
            bool slopeOk = result.westSlopeDegrees >= 28f &&
                           result.westSlopeDegrees <= 45f &&
                           result.maxLocalSlopeDegrees <= 50f;
            result.success =
                result.terrainCreated &&
                result.westHeight > result.villageHeight &&
                slopeOk &&
                result.houseCount >= 7 &&
                result.workshopCount == 5 &&
                result.spawnCount >= 5 &&
                result.workSpotCount >= 10 &&
                result.phase == 1 &&
                result.season == VariableTimelineSeason.Active;
            result.message =
                $"国家001 T{turn} 位相{result.phase} ({result.season}) " +
                $"西稜={ToMeters(result.westHeight):F0}m " +
                $"村台地={ToMeters(result.villageHeight):F0}m " +
                $"東={ToMeters(result.eastHeight):F0}m " +
                $"西勾配={result.westSlopeDegrees:F1}° 局所最大={result.maxLocalSlopeDegrees:F1}° " +
                $"家屋={result.houseCount} 工房={result.workshopCount} スポーン={result.spawnCount} " +
                $"作業スポット={result.workSpotCount} オブジェクト={result.propCount}";

            LogNpcWorkSpotBindings();
        }
        catch (System.Exception exception)
        {
            result.success = false;
            result.message = $"Safe-Fail: {exception.Message}";
            Debug.LogWarning($"[Nation001ProceduralMapBuilder] {result.message}");
        }

        if (result.success)
        {
            Debug.Log($"<color=#A5D6A7><b>【Procedural Map】生成完了</b></color> {result.message}");
        }
        else
        {
            Debug.LogWarning($"[Nation001ProceduralMapBuilder] 生成不完全: {result.message}");
        }

        return result;
    }

    /// <summary>マップ生成直後に、既定村人ジョブと最適スポットの対応をログします。</summary>
    private static void LogNpcWorkSpotBindings()
    {
        try
        {
            VillageWorkSpotManager spots = VillageWorkSpotManager.EnsureInstance();
            NpcCivilizationEngine civ = NpcCivilizationEngine.EnsureInstance();
            if (civ?.Villagers == null || civ.Villagers.Count == 0)
            {
                Debug.LogWarning("[Nation001ProceduralMapBuilder] 村人未生成のためスポット割当ログをスキップ");
                return;
            }

            System.Text.StringBuilder log = new System.Text.StringBuilder();
            log.Append("<color=#80CBC4><b>【作業スポット割当】</b></color> ");
            log.Append(spots.FormatRegistrySnapshot());
            Vector3 center = spots.ResolveVillageCenterOrFallback();
            for (int i = 0; i < civ.Villagers.Count; i++)
            {
                NpcIndividualStatus npc = civ.Villagers[i];
                if (npc == null)
                {
                    continue;
                }

                if (npc.WorldPosition.sqrMagnitude < 0.01f)
                {
                    npc.WorldPosition = center;
                }

                WorkSpotData spot = NpcUtilityAI.GetOptimalWorkSpot(npc.JobId, npc.WorldPosition);
                npc.WorldPosition = spot.WorldPosition;
                npc.AssignedSpotId = spot.SpotId;
                log.AppendLine();
                log.Append(
                    $"  {npc.Name}({npc.JobId}) → {WorkSpotData.TypeLabel(spot.Type)} @{spot.SpotId} " +
                    $"pos={spot.WorldPosition} yield={spot.ResourceYield.FormatShort()}" +
                    (spot.IsFallback ? " [FALLBACK]" : string.Empty));
            }

            Debug.Log(log.ToString());
        }
        catch (System.Exception exception)
        {
            Debug.LogWarning($"[Nation001ProceduralMapBuilder] スポット割当ログ Safe-Fail: {exception.Message}");
        }
    }

    private static float ToMeters(float normalized)
    {
        return normalized * ProceduralTerrainGenerator.TerrainHeightMeters;
    }

    private static Transform EnsureRoot()
    {
        GameObject existing = GameObject.Find(RootName);
        if (existing != null)
        {
            return existing.transform;
        }

        GameObject root = new GameObject(RootName);
        return root.transform;
    }

    private static int CountTransforms(Transform root)
    {
        if (root == null)
        {
            return 0;
        }

        return root.GetComponentsInChildren<Transform>(true).Length;
    }
}

/// <summary>一括生成の検証結果。</summary>
public struct Nation001MapBuildResult
{
    public bool success;
    public bool terrainCreated;
    public int propCount;
    public int houseCount;
    public int workshopCount;
    public int spawnCount;
    public int workSpotCount;
    public int phase;
    public VariableTimelineSeason season;
    public float westHeight;
    public float villageHeight;
    public float eastHeight;
    public float westSlopeDegrees;
    public float maxLocalSlopeDegrees;
    public string message;
}

#if UNITY_EDITOR
/// <summary>エディタ専用メニューと自動検証。本番ビルドには含まれません。</summary>
public static class Nation001ProceduralMapMenu
{
    private const string VerifyLogPath = "Logs/nation001_map_verify.txt";
    private const string RegenRequestPath = "Logs/nation001_map_regen.request";

    [InitializeOnLoadMethod]
    private static void GenerateWhenRequestFilePresent()
    {
        EditorApplication.delayCall += () =>
        {
            string request = ResolveProjectPath(RegenRequestPath);
            if (string.IsNullOrEmpty(request) || !File.Exists(request))
            {
                return;
            }

            try
            {
                File.Delete(request);
            }
            catch (System.Exception)
            {
                return;
            }

            GenerateAndVerifyTurn1();
        };
    }

    [MenuItem("Tools/Procedural Map/Generate Nation 001 Map")]
    public static void GenerateFromMenu()
    {
        Nation001MapBuildResult result = GenerateAndVerifyTurn1();
        EditorUtility.DisplayDialog(
            "Nation 001 Map",
            result.success
                ? $"ターン1・活性期で生成しました。\n{result.message}"
                : $"生成に問題があります（Safe-Fail）。\n{result.message}",
            "OK");
    }

    [MenuItem("Tools/Procedural Map/Apply Phase Active (1)")]
    public static void ApplyActivePhase()
    {
        ApplyPhase(1);
    }

    [MenuItem("Tools/Procedural Map/Apply Phase Dormant")]
    public static void ApplyDormantPhase()
    {
        ProceduralMapPopulator populator = Object.FindAnyObjectByType<ProceduralMapPopulator>();
        int dormantPhase = ProceduralMapPopulator.FirstDormantPhase(
            populator != null ? populator.BoundDistribution : null);
        ApplyPhase(dormantPhase);
    }

    /// <summary>Unity バッチモード用: -executeMethod Nation001ProceduralMapMenu.BatchGenerateNation001AndQuit</summary>
    public static void BatchGenerateNation001AndQuit()
    {
        Nation001MapBuildResult result = GenerateAndVerifyTurn1();
        EditorApplication.Exit(result.success ? 0 : 1);
    }

    private static Nation001MapBuildResult GenerateAndVerifyTurn1()
    {
        Nation001MapBuildResult result = Nation001ProceduralMapBuilder.GenerateNation001Map(1, 1);
        result = AppendPhaseToggleCheck(result);
        WriteVerifyLog(result);
        if (result.success)
        {
            EditorUtility.SetDirty(GameObject.Find(Nation001ProceduralMapBuilder.RootName));
        }

        return result;
    }

    private static Nation001MapBuildResult AppendPhaseToggleCheck(Nation001MapBuildResult result)
    {
        ProceduralMapPopulator populator = Object.FindAnyObjectByType<ProceduralMapPopulator>();
        if (populator == null)
        {
            result.success = false;
            result.message += " / Populator 未配置";
            return result;
        }

        int dormantPhase = ProceduralMapPopulator.FirstDormantPhase(populator.BoundDistribution);
        populator.UpdatePhaseEnvironment(dormantPhase);
        VariableTimelineSeason dormant = populator.LastSeason;
        bool dormantOk = dormant == VariableTimelineSeason.Dormant && TownSafetyZoneGate.IsInsideTown;

        populator.UpdatePhaseEnvironment(1);
        VariableTimelineSeason active = populator.LastSeason;
        bool activeOk = active == VariableTimelineSeason.Active && !TownSafetyZoneGate.IsInsideTown;

        if (!dormantOk || !activeOk)
        {
            result.success = false;
            result.message += $" / 位相切替失敗 dormant={dormant} active={active}";
        }

        return result;
    }

    private static void ApplyPhase(int phase)
    {
        ProceduralMapPopulator populator = Object.FindAnyObjectByType<ProceduralMapPopulator>();
        if (populator == null)
        {
            Debug.LogWarning("[Procedural Map] 先に Generate Nation 001 Map を実行してください。");
            return;
        }

        populator.UpdatePhaseEnvironment(phase);
    }

    private static void WriteVerifyLog(Nation001MapBuildResult result)
    {
        try
        {
            string path = ResolveProjectPath(VerifyLogPath);
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? string.Empty);
            StringBuilder builder = new StringBuilder();
            builder.AppendLine(result.success ? "PASS" : "FAIL");
            builder.AppendLine(result.message ?? string.Empty);
            builder.AppendLine($"terrainCreated={result.terrainCreated}");
            builder.AppendLine($"west>{result.villageHeight} east={result.eastHeight}");
            builder.AppendLine($"westSlope={result.westSlopeDegrees:F1} maxLocalSlope={result.maxLocalSlopeDegrees:F1}");
            builder.AppendLine($"houses={result.houseCount} workshops={result.workshopCount} spawns={result.spawnCount} workSpots={result.workSpotCount}");
            File.WriteAllText(path, builder.ToString(), Encoding.UTF8);
        }
        catch (System.Exception exception)
        {
            Debug.LogWarning($"[Nation001ProceduralMapMenu] 検証ログ書き込みをスキップ: {exception.Message}");
        }
    }

    private static string ResolveProjectPath(string relativePath)
    {
        string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
        return Path.Combine(projectRoot, relativePath);
    }
}
#endif

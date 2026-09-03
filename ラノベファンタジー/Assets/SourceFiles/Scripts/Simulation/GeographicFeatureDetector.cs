using System.Text;
using UnityEngine;

// =============================================================================
// 標高・水域・地質に基づく地形条件判定
// 連携: ProceduralTerrainGenerator / VillageWorkSpotManager / ProceduralMapPopulator
// =============================================================================

/// <summary>地形依存災害の前提フラグ。</summary>
[System.Serializable]
public sealed class GeographicFeatureFlags
{
    public bool HasWaterBody;
    public bool HasVolcanicFeature;
    public bool HasSteepSlope;
    public float LowlandElevationMeters;
    public float PeakElevationMeters;
    public float MaxSlopeDegrees;
    public int WaterSampleCount;
    public string Summary = string.Empty;
}

/// <summary>
/// ProceduralMap の標高・勾配・作業スポット位置から災害前提条件を判定します。
/// </summary>
public static class GeographicFeatureDetector
{
    public const float WaterBodyElevationMaxMeters = 108f;
    public const float VolcanicPeakMinMeters = 280f;
    public const float SteepSlopeMinDegrees = 28f;
    public const float LowlandNormalizedXMin = 0.62f;

    /// <summary>マップデータを走査し、洪水・噴火・土砂崩れの前提フラグを返します。</summary>
    public static GeographicFeatureFlags Detect(Terrain terrain = null)
    {
        GeographicFeatureFlags flags = new GeographicFeatureFlags();
        StringBuilder log = new StringBuilder();

        try
        {
            float maxSlope = ProceduralTerrainGenerator.EstimateMaxLocalSlopeDegrees(96);
            float westSlope = ProceduralTerrainGenerator.EstimateWesternApproachSlopeDegrees();
            flags.MaxSlopeDegrees = Mathf.Max(maxSlope, westSlope);
            flags.HasSteepSlope = flags.MaxSlopeDegrees >= SteepSlopeMinDegrees;

            float peakMeters = 0f;
            float lowMeters = float.MaxValue;
            int waterSamples = 0;

            const int grid = 9;
            for (int iz = 0; iz < grid; iz++)
            {
                float nz = iz / (float)(grid - 1);
                for (int ix = 0; ix < grid; ix++)
                {
                    float nx = ix / (float)(grid - 1);
                    float norm = ProceduralTerrainGenerator.SampleNormalizedHeight(nx, nz);
                    float meters = norm * ProceduralTerrainGenerator.TerrainHeightMeters;
                    peakMeters = Mathf.Max(peakMeters, meters);
                    lowMeters = Mathf.Min(lowMeters, meters);

                    bool eastLowland = nx >= LowlandNormalizedXMin;
                    if (eastLowland && meters <= WaterBodyElevationMaxMeters)
                    {
                        waterSamples++;
                    }
                }
            }

            flags.PeakElevationMeters = peakMeters;
            flags.LowlandElevationMeters = lowMeters < float.MaxValue ? lowMeters : 0f;
            flags.WaterSampleCount = waterSamples;

            bool spotInLowland = HasWorkSpotInLowlandWaterZone(terrain);
            flags.HasWaterBody = waterSamples >= 3 || spotInLowland;

            float crestNorm = ProceduralTerrainGenerator.MountainCrestNormalizedX;
            float crestHeight = ProceduralTerrainGenerator.SampleNormalizedHeight(crestNorm, 0.5f) *
                                ProceduralTerrainGenerator.TerrainHeightMeters;
            flags.HasVolcanicFeature =
                peakMeters >= VolcanicPeakMinMeters ||
                crestHeight >= VolcanicPeakMinMeters * 0.92f;

            log.Append(
                $"水={flags.HasWaterBody}(低地標本{waterSamples}) " +
                $"火山={flags.HasVolcanicFeature}(峰{peakMeters:F0}m) " +
                $"急斜面={flags.HasSteepSlope}({flags.MaxSlopeDegrees:F1}°)");
            flags.Summary = log.ToString();
        }
        catch (System.Exception exception)
        {
            flags.Summary = $"Safe-Fail: {exception.Message}";
            Debug.LogWarning($"[GeographicFeatureDetector] {flags.Summary}");
        }

        return flags;
    }

    private static bool HasWorkSpotInLowlandWaterZone(Terrain terrain)
    {
        try
        {
            VillageWorkSpotManager spots = VillageWorkSpotManager.EnsureInstance();
            if (spots?.Spots == null || spots.Spots.Count == 0)
            {
                return false;
            }

            Vector3 size = terrain != null && terrain.terrainData != null
                ? terrain.terrainData.size
                : new Vector3(
                    ProceduralTerrainGenerator.TerrainWidthMeters,
                    ProceduralTerrainGenerator.TerrainHeightMeters,
                    ProceduralTerrainGenerator.TerrainLengthMeters);

            for (int i = 0; i < spots.Spots.Count; i++)
            {
                WorkSpotData spot = spots.Spots[i];
                if (spot == null)
                {
                    continue;
                }

                float nx = size.x > 0.01f ? spot.WorldPosition.x / size.x : 0.5f;
                float nz = size.z > 0.01f ? spot.WorldPosition.z / size.z : 0.5f;
                nx = Mathf.Clamp01(nx);
                nz = Mathf.Clamp01(nz);

                float meters = ProceduralTerrainGenerator.SampleNormalizedHeight(nx, nz) *
                               ProceduralTerrainGenerator.TerrainHeightMeters;
                if (nx >= LowlandNormalizedXMin && meters <= WaterBodyElevationMaxMeters + 8f)
                {
                    return true;
                }

                if (spot.Type == WorkSpotType.Farmland && meters <= WaterBodyElevationMaxMeters + 12f)
                {
                    return true;
                }
            }
        }
        catch (System.Exception)
        {
            // Safe-Fail
        }

        return false;
    }
}

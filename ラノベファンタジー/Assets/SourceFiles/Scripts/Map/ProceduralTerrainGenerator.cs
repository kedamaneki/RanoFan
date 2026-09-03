using UnityEngine;

// =============================================================================
// 国家001 — メキシコ高原 / シエラ・マドレ西山脈の Terrain 生成
// 北緯 22.971°・西経 103.124° の内陸台地を相対標高で再現
// =============================================================================

/// <summary>
/// Perlin ノイズと東西勾配で、中央の浅い台地ボウルと西側へなだらかに上がる山脈を Heightmap に焼き込みます。
/// Prefab / Terrain 欠損時も新規 Terrain を生成して Safe-Fail します。
/// </summary>
public static class ProceduralTerrainGenerator
{
    public const float ReferenceLatitude = 22.971f;
    public const float ReferenceLongitude = -103.124f;

    public const float TerrainWidthMeters = 800f;
    public const float TerrainLengthMeters = 800f;

    /// <summary>
    /// TerrainData.size.y（HeightmapScale.y）。800m 四方に対し 300〜400m に抑え、
    /// 実世界の 1780〜3120m 差をゲームスケールへ圧縮する。
    /// </summary>
    public const float TerrainHeightMeters = 360f;

    /// <summary>台地の相対標高（m）。実世界 ~2100m 高原の圧縮値。</summary>
    public const float PlateauElevationMeters = 155f;

    /// <summary>西側稜線の相対ピーク（m）。実世界 ~3120m の圧縮値。</summary>
    public const float SierraPeakElevationMeters = 335f;

    /// <summary>東側低地の相対標高（m）。実世界 ~1780m の圧縮値。</summary>
    public const float EastLowlandMeters = 92f;

    /// <summary>稜線の正規化 X（西端付近）。ここから東へ山麓まで傾斜する。</summary>
    public const float MountainCrestNormalizedX = 0.05f;

    /// <summary>
    /// 山麓の正規化 X。幅 0.43 × 800m = 344m で 180m 上昇。
    /// SmoothStep の最大勾配係数 1.5 を加味すると約 38°（目標 30〜45°）。
    /// </summary>
    public const float MountainFootNormalizedX = 0.48f;

    /// <summary>村ボウルの深さ。ピーク〜台地の標高差に対する比率（5〜10%）。</summary>
    public const float VillageBowlDepthRatio = 0.08f;

    public const int HeightmapResolution = 513;

    /// <summary>正規化座標上の村中心（東寄りの台地。西は山脈）。</summary>
    public static readonly Vector2 VillageNormalized = new Vector2(0.58f, 0.50f);

    /// <summary>既存 Terrain があれば Heightmap を上書き、なければ新規生成します。</summary>
    public static Terrain GenerateOrApply(Transform parent, Terrain existing)
    {
        Terrain terrain = existing;
        if (terrain == null || terrain.terrainData == null)
        {
            terrain = CreateTerrainObject(parent);
        }

        if (terrain == null || terrain.terrainData == null)
        {
            Debug.LogWarning("[ProceduralTerrainGenerator] Terrain を確保できませんでした。平面フォールバックを使います。");
            return null;
        }

        ApplyMexicanPlateauHeightmap(terrain.terrainData);
        terrain.transform.SetParent(parent, false);
        terrain.transform.localPosition = Vector3.zero;
        terrain.gameObject.name = "Nation001_PlateauTerrain";
        return terrain;
    }

    /// <summary>メキシコ高原の Heightmap を TerrainData へ適用します。</summary>
    public static void ApplyMexicanPlateauHeightmap(TerrainData data)
    {
        if (data == null)
        {
            Debug.LogWarning("[ProceduralTerrainGenerator] TerrainData が null のため Heightmap 適用をスキップします。");
            return;
        }

        data.heightmapResolution = HeightmapResolution;
        data.size = new Vector3(TerrainWidthMeters, TerrainHeightMeters, TerrainLengthMeters);

        int res = data.heightmapResolution;
        float[,] heights = new float[res, res];
        float inv = res > 1 ? 1f / (res - 1) : 1f;

        for (int z = 0; z < res; z++)
        {
            float nz = z * inv;
            for (int x = 0; x < res; x++)
            {
                float nx = x * inv;
                heights[z, x] = SampleNormalizedHeight(nx, nz);
            }
        }

        data.SetHeights(0, 0, heights);
    }

    /// <summary>
    /// 正規化 0-1 の地形高（Terrain の 0〜1）。X=0 が西の山脈、中央〜東が台地と村。
    /// 山脈は山麓まで数百メートルかけて上がり、村は標高差の数パーセントの浅い凹みにする。
    /// </summary>
    public static float SampleNormalizedHeight(float nx, float nz)
    {
        nx = Mathf.Clamp01(nx);
        nz = Mathf.Clamp01(nz);

        float invHeight = 1f / TerrainHeightMeters;

        float mountainMask = 1f - SmoothStep(MountainCrestNormalizedX, MountainFootNormalizedX, nx);
        float northSouthRidge = 0.74f + 0.26f * Mathf.Sin(nz * Mathf.PI);
        mountainMask *= northSouthRidge;

        float rumble = Fbm(nx * 2.0f + 11.3f, nz * 1.7f + 4.1f, 3) - 0.5f;
        float broad = Fbm(nx * 1.35f + 2.2f, nz * 1.15f, 3);
        float sierraMeters = Mathf.Lerp(PlateauElevationMeters, SierraPeakElevationMeters, mountainMask);
        sierraMeters += mountainMask * (broad * 16f + rumble * 8f);

        float eastBlend = SmoothStep(0.64f, 0.96f, nx);
        float surfaceMeters = Mathf.Lerp(sierraMeters, EastLowlandMeters, eastBlend * (1f - mountainMask));

        float dx = nx - VillageNormalized.x;
        float dz = nz - VillageNormalized.y;
        float villageDist = Mathf.Sqrt(dx * dx + dz * dz);
        float villageMask = 1f - SmoothStep(0.07f, 0.18f, villageDist);
        villageMask *= 1f - mountainMask * 0.9f;

        float bowlMeters = (SierraPeakElevationMeters - PlateauElevationMeters) * VillageBowlDepthRatio;
        float villageMeters = PlateauElevationMeters - bowlMeters + rumble * 2.5f;

        float heightMeters = Mathf.Lerp(surfaceMeters, villageMeters, villageMask);
        return Mathf.Clamp01(heightMeters * invHeight);
    }

    /// <summary>西山脈の平均勾配（度）。山麓〜稜線の標高差 / 水平距離。</summary>
    public static float EstimateWesternApproachSlopeDegrees()
    {
        const float z = 0.50f;
        float xCrest = MountainCrestNormalizedX + 0.02f;
        float xFoot = MountainFootNormalizedX - 0.02f;
        float yCrest = SampleNormalizedHeight(xCrest, z) * TerrainHeightMeters;
        float yFoot = SampleNormalizedHeight(xFoot, z) * TerrainHeightMeters;
        float run = Mathf.Max(1f, (xFoot - xCrest) * TerrainWidthMeters);
        return Mathf.Atan2(Mathf.Abs(yCrest - yFoot), run) * Mathf.Rad2Deg;
    }

    /// <summary>東西断面の局所最大勾配（度）。崖化していないことの確認用。</summary>
    public static float EstimateMaxLocalSlopeDegrees(int samples = 80)
    {
        int count = Mathf.Max(8, samples);
        float maxDeg = 0f;
        const float z = 0.50f;
        float step = 1f / count;
        float run = step * TerrainWidthMeters;
        for (int i = 0; i < count; i++)
        {
            float x0 = i * step;
            float y0 = SampleNormalizedHeight(x0, z) * TerrainHeightMeters;
            float y1 = SampleNormalizedHeight(x0 + step, z) * TerrainHeightMeters;
            float deg = Mathf.Atan2(Mathf.Abs(y1 - y0), run) * Mathf.Rad2Deg;
            if (deg > maxDeg)
            {
                maxDeg = deg;
            }
        }

        return maxDeg;
    }

    /// <summary>正規化座標を Terrain 上のワールド位置へ変換します。</summary>
    public static Vector3 NormalizedToWorld(Terrain terrain, float nx, float nz)
    {
        if (terrain == null || terrain.terrainData == null)
        {
            return new Vector3(nx * TerrainWidthMeters, PlateauElevationMeters, nz * TerrainLengthMeters);
        }

        Vector3 origin = terrain.transform.position;
        Vector3 size = terrain.terrainData.size;
        Vector3 world = new Vector3(origin.x + nx * size.x, origin.y + size.y, origin.z + nz * size.z);
        float sampled = terrain.SampleHeight(world);
        return new Vector3(world.x, origin.y + sampled, world.z);
    }

    private static Terrain CreateTerrainObject(Transform parent)
    {
        TerrainData data = new TerrainData();
        data.heightmapResolution = HeightmapResolution;
        data.size = new Vector3(TerrainWidthMeters, TerrainHeightMeters, TerrainLengthMeters);

        GameObject terrainObject = Terrain.CreateTerrainGameObject(data);
        if (terrainObject == null)
        {
            return null;
        }

        if (parent != null)
        {
            terrainObject.transform.SetParent(parent, false);
        }

        Terrain terrain = terrainObject.GetComponent<Terrain>();
        if (terrain != null)
        {
            terrain.groupingID = 1;
            terrain.allowAutoConnect = false;
            terrain.drawHeightmap = true;
        }

        return terrain;
    }

    private static float Fbm(float x, float y, int octaves)
    {
        float sum = 0f;
        float amp = 0.5f;
        float freq = 1f;
        float norm = 0f;
        for (int i = 0; i < octaves; i++)
        {
            sum += Mathf.PerlinNoise(x * freq, y * freq) * amp;
            norm += amp;
            amp *= 0.5f;
            freq *= 2.03f;
        }

        return norm > 0f ? sum / norm : 0f;
    }

    private static float SmoothStep(float edge0, float edge1, float x)
    {
        if (Mathf.Approximately(edge0, edge1))
        {
            return x < edge0 ? 0f : 1f;
        }

        float t = Mathf.Clamp01((x - edge0) / (edge1 - edge0));
        return t * t * (3f - 2f * t);
    }
}

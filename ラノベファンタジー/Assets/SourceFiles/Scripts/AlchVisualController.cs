using System;
using System.Collections;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

// =============================================================================
// 調合ビジュアル制御 — CraftingStatusManager の温度・抽出度・溶解度と 3D 表現を同期
// 連携: CraftingStatusManager / CraftingStatusTester / DetailedCraftingProcessManager
// =============================================================================

/// <summary>攪拌スピンの回転方向。</summary>
public enum AlchStirSpinDirection
{
    /// <summary>時計回り（右攪拌）。</summary>
    Clockwise = 1,

    /// <summary>反時計回り（左攪拌）。</summary>
    CounterClockwise = -1
}

/// <summary>
/// 調合大釜オブジェクトのマテリアル色・沸騰シェイク・攪拌スピンを、
/// CraftingStatusManager の PotTemperature / ExtractionLevel / DissolutionRate と同期するビジュアル制御。
/// </summary>
[DisallowMultipleComponent]
public class AlchVisualController : MonoBehaviour
{
    public const string ParamPotTemperature = "PotTemperature";
    public const string ParamExtractionLevel = "ExtractionLevel";
    public const string ParamDissolutionRate = "DissolutionRate";

    private static AlchVisualController activeInstance;

    [Header("参照（未設定時は自身の Renderer / Transform を使用）")]
    [SerializeField] private Transform cauldronTransform;
    [SerializeField] private Renderer cauldronRenderer;
    [SerializeField] private CraftingStatusManager statusManager;

    [Header("熱力学色彩（℃）")]
    [SerializeField] private float coldSolutionMaxCelsius = 40f;
    [SerializeField] private float goldenExtractStartCelsius = 60f;
    [SerializeField] private float goldenExtractPeakCelsius = 95f;
    [SerializeField] private float thermalDangerCelsius = 120f;
    [SerializeField] private float potTemperatureMaxCelsius = 300f;
    [SerializeField] private float boilShakeStartCelsius = 90f;

    [Header("生薬学カラーパレット（Standard マテリアル _Color）")]
    [SerializeField] private Color coldBlueGreenColor = new Color(0.12f, 0.55f, 0.72f, 1f);
    [SerializeField] private Color emeraldSolutionColor = new Color(0.18f, 0.78f, 0.52f, 1f);
    [SerializeField] private Color goldenAmberColor = new Color(0.95f, 0.78f, 0.18f, 1f);
    [SerializeField] private Color yellowGoldColor = new Color(1f, 0.88f, 0.22f, 1f);
    [SerializeField] private Color neonOverheatColor = new Color(1f, 0.12f, 0.28f, 1f);
    [SerializeField] private Color charredPurpleColor = new Color(0.22f, 0.02f, 0.18f, 1f);
    [SerializeField] private Color thermalBurstBlackColor = new Color(0.03f, 0.03f, 0.03f, 1f);

    [Header("補間・沸騰シェイク")]
    [SerializeField] private float colorLerpSpeed = 9f;
    [SerializeField] private float boilShakeBaseAmplitude = 0.018f;
    [SerializeField] private float boilShakeMaxAmplitude = 0.11f;
    [SerializeField] private float boilShakeFrequency = 24f;
    [SerializeField] private float boilRandomJitter = 0.035f;

    [Header("攪拌スピン")]
    [SerializeField] private float stirSpinDuration = 0.55f;
    [SerializeField] private float stirSpinTurnsMin = 1f;
    [SerializeField] private float stirSpinTurnsMax = 2f;
    [SerializeField] private float paramStirMinDelta = 3f;

    [Header("熱分解バースト（120℃超）")]
    [SerializeField] private float thermalBurstDuration = 0.42f;
    [SerializeField] private float thermalBurstScalePunch = 0.28f;
    [SerializeField] private float thermalBurstScaleShrink = 0.18f;

    private Vector3 baselineLocalPosition;
    private Vector3 baselineLocalScale;
    private Quaternion baselineLocalRotation;
    private Material cauldronMaterial;
    private Color displayedColor;
    private Color targetColor;
    private float previousPotTemperature;
    private float heatVisualEfficiencyMultiplier = 1f;
    private float boilNoiseSeed;
    private Coroutine stirSpinCoroutine;
    private Coroutine thermalBurstCoroutine;
    private float lastExplicitStirTime = -1f;
    private const float StirDebounceSeconds = 0.07f;

    /// <summary>シーン内で駆動中の AlchVisualController（最初に有効化された1体）。</summary>
    public static AlchVisualController ActiveInstance => activeInstance;

    private void Awake()
    {
        ResolveReferences();
        CaptureBaselines();
        boilNoiseSeed = UnityEngine.Random.Range(0f, 1000f);

        if (cauldronRenderer != null)
        {
            cauldronMaterial = cauldronRenderer.material;
            if (cauldronMaterial == cauldronRenderer.sharedMaterial)
            {
                cauldronMaterial = new Material(cauldronRenderer.sharedMaterial);
                cauldronRenderer.material = cauldronMaterial;
            }

            cauldronMaterial.color = coldBlueGreenColor;
        }

        displayedColor = coldBlueGreenColor;
        targetColor = coldBlueGreenColor;
        previousPotTemperature = roomPotTemperature();
    }

    private void OnEnable()
    {
        ResolveReferences();

        if (activeInstance == null)
        {
            activeInstance = this;
        }

        CraftingStatusManager.ParamModified += HandleParamModified;
        CraftingStatusManager.StatusReset += HandleStatusReset;
        RefreshFromStatusManager(forceImmediate: true);
    }

    private void OnDisable()
    {
        CraftingStatusManager.ParamModified -= HandleParamModified;
        CraftingStatusManager.StatusReset -= HandleStatusReset;

        if (activeInstance == this)
        {
            activeInstance = null;
        }
    }

    private void Update()
    {
        ResolveReferences();
        if (statusManager == null || cauldronTransform == null)
        {
            return;
        }

        float potTemperature = statusManager.GetParam(
            CraftingStatusManager.CraftTypeAlch,
            ParamPotTemperature);

        if (previousPotTemperature < thermalDangerCelsius &&
            potTemperature >= thermalDangerCelsius)
        {
            TriggerThermalDecompositionBurst();
        }

        previousPotTemperature = potTemperature;

        float effectiveTemperature = potTemperature * heatVisualEfficiencyMultiplier;
        targetColor = EvaluateSolutionColorByTemperature(effectiveTemperature);

        float colorStep = 1f - Mathf.Exp(-colorLerpSpeed * Time.deltaTime);
        displayedColor = Color.Lerp(displayedColor, targetColor, colorStep);
        ApplyMaterialColor(displayedColor);

        if (stirSpinCoroutine == null && thermalBurstCoroutine == null)
        {
            ApplyBoilShake(potTemperature);
        }
    }

    /// <summary>右攪拌（時計回り）スピン演出を起動します。</summary>
    public void TriggerRightStirSpin(float intensity = 1f)
    {
        TriggerStirSpin(AlchStirSpinDirection.Clockwise, intensity);
    }

    /// <summary>左攪拌（反時計回り）スピン演出を起動します。</summary>
    public void TriggerLeftStirSpin(float intensity = 1f)
    {
        TriggerStirSpin(AlchStirSpinDirection.CounterClockwise, intensity);
    }

    /// <summary>9キーすり潰し後の「温度上昇ビジュアル効率2倍」フラグを有効化します。</summary>
    public void TriggerGrindHerbVisualBoost()
    {
        heatVisualEfficiencyMultiplier = 2f;
        Debug.Log(
            "<color=#CE93D8><b>[AlchVisualController]</b> すり潰し完了 — " +
            "熱気・色彩ビジュアルの効率が <b>2倍</b> に加速しました。</color>");
        TriggerStirSpin(AlchStirSpinDirection.Clockwise, 1.15f);
    }

    /// <summary>0キー強火沸騰の視覚フィードバックを起動します。</summary>
    public void TriggerRapidBoilVisual()
    {
        TriggerStirSpin(AlchStirSpinDirection.Clockwise, 0.75f);
        Debug.Log(
            "<color=#FF7043><b>[AlchVisualController]</b> 強火投入 — 沸騰シェイクが激化します。</color>");
    }

    /// <summary>攪拌スピン演出を方向指定で起動します。</summary>
    /// <param name="direction">回転方向。</param>
    /// <param name="intensity">0〜1 の演出強度。</param>
    public void TriggerStirSpin(AlchStirSpinDirection direction, float intensity = 1f)
    {
        lastExplicitStirTime = Time.time;
        float clampedIntensity = Mathf.Clamp01(intensity);

        if (stirSpinCoroutine != null)
        {
            StopCoroutine(stirSpinCoroutine);
        }

        stirSpinCoroutine = StartCoroutine(PlayStirSpinCoroutine(direction, clampedIntensity));
    }

    /// <summary>120℃超えの熱分解バースト演出を起動します。</summary>
    public void TriggerThermalDecompositionBurst()
    {
        if (thermalBurstCoroutine != null)
        {
            StopCoroutine(thermalBurstCoroutine);
        }

        thermalBurstCoroutine = StartCoroutine(PlayThermalBurstCoroutine());
    }

    /// <summary>アクティブな AlchVisualController へ右攪拌スピンを通知します。</summary>
    public static void NotifyRightStirGlobally()
    {
        ResolveActiveController()?.TriggerRightStirSpin();
    }

    /// <summary>アクティブな AlchVisualController へ左攪拌スピンを通知します。</summary>
    public static void NotifyLeftStirGlobally()
    {
        ResolveActiveController()?.TriggerLeftStirSpin();
    }

    /// <summary>アクティブな AlchVisualController へすり潰しビジュアルブーストを通知します。</summary>
    public static void NotifyGrindHerbGlobally()
    {
        ResolveActiveController()?.TriggerGrindHerbVisualBoost();
    }

    /// <summary>アクティブな AlchVisualController へ強火沸騰ビジュアルを通知します。</summary>
    public static void NotifyRapidBoilGlobally()
    {
        ResolveActiveController()?.TriggerRapidBoilVisual();
    }

    /// <summary>現在の CraftingStatusManager 値から色と基準姿勢を即時同期します。</summary>
    /// <param name="forceImmediate">true のとき補間をスキップして即反映します。</param>
    public void RefreshFromStatusManager(bool forceImmediate = false)
    {
        ResolveReferences();
        if (statusManager == null)
        {
            return;
        }

        float potTemperature = statusManager.GetParam(
            CraftingStatusManager.CraftTypeAlch,
            ParamPotTemperature);
        previousPotTemperature = potTemperature;

        float effectiveTemperature = potTemperature * heatVisualEfficiencyMultiplier;
        targetColor = EvaluateSolutionColorByTemperature(effectiveTemperature);

        if (forceImmediate)
        {
            displayedColor = targetColor;
            ApplyMaterialColor(displayedColor);
            RestoreBaselinePose();
        }
    }

    /// <summary>釜温度（℃）から溶液の見た目カラーを算出します。</summary>
    /// <param name="temperatureCelsius">PotTemperature（ビジュアル効率補正後可）。</param>
    public Color EvaluateSolutionColorByTemperature(float temperatureCelsius)
    {
        float temp = Mathf.Clamp(temperatureCelsius, 0f, potTemperatureMaxCelsius);

        if (temp <= coldSolutionMaxCelsius)
        {
            float t = InverseLerp(0f, coldSolutionMaxCelsius, temp);
            return Color.Lerp(coldBlueGreenColor, emeraldSolutionColor, t);
        }

        if (temp <= goldenExtractStartCelsius)
        {
            float t = InverseLerp(coldSolutionMaxCelsius, goldenExtractStartCelsius, temp);
            return Color.Lerp(emeraldSolutionColor, goldenAmberColor * 0.85f, t);
        }

        if (temp <= goldenExtractPeakCelsius)
        {
            float t = InverseLerp(goldenExtractStartCelsius, goldenExtractPeakCelsius, temp);
            return Color.Lerp(goldenAmberColor, yellowGoldColor, t);
        }

        if (temp < thermalDangerCelsius)
        {
            float t = InverseLerp(goldenExtractPeakCelsius, thermalDangerCelsius, temp);
            return Color.Lerp(yellowGoldColor, neonOverheatColor, t);
        }

        float dangerT = InverseLerp(thermalDangerCelsius, potTemperatureMaxCelsius, temp);
        return Color.Lerp(neonOverheatColor, charredPurpleColor, dangerT);
    }

    private void HandleParamModified(CraftingParamChangeInfo changeInfo)
    {
        if (!string.Equals(
                changeInfo.CraftType,
                CraftingStatusManager.CraftTypeAlch,
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (Time.time - lastExplicitStirTime <= StirDebounceSeconds)
        {
            return;
        }

        if (string.Equals(changeInfo.ParamName, ParamExtractionLevel, StringComparison.OrdinalIgnoreCase) &&
            changeInfo.Delta >= paramStirMinDelta)
        {
            TriggerStirSpin(AlchStirSpinDirection.Clockwise, Mathf.Clamp01(changeInfo.Delta / 15f));
            return;
        }

        if (string.Equals(changeInfo.ParamName, ParamDissolutionRate, StringComparison.OrdinalIgnoreCase) &&
            changeInfo.Delta >= paramStirMinDelta)
        {
            TriggerStirSpin(AlchStirSpinDirection.CounterClockwise, Mathf.Clamp01(changeInfo.Delta / 15f));
        }
    }

    private void HandleStatusReset(string craftType)
    {
        if (!string.Equals(
                craftType,
                CraftingStatusManager.CraftTypeAlch,
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        heatVisualEfficiencyMultiplier = 1f;
        previousPotTemperature = roomPotTemperature();

        if (stirSpinCoroutine != null)
        {
            StopCoroutine(stirSpinCoroutine);
            stirSpinCoroutine = null;
        }

        if (thermalBurstCoroutine != null)
        {
            StopCoroutine(thermalBurstCoroutine);
            thermalBurstCoroutine = null;
        }

        RefreshFromStatusManager(forceImmediate: true);
        RestoreBaselinePose();
    }

    private void ApplyBoilShake(float potTemperature)
    {
        if (potTemperature <= boilShakeStartCelsius)
        {
            cauldronTransform.localPosition = baselineLocalPosition;
            return;
        }

        float boilRatio = InverseLerp(
            boilShakeStartCelsius,
            potTemperatureMaxCelsius,
            potTemperature);
        float amplitude = Mathf.Lerp(
            boilShakeBaseAmplitude,
            boilShakeMaxAmplitude,
            boilRatio);

        float time = Time.time * boilShakeFrequency + boilNoiseSeed;
        Vector3 sinShake = new Vector3(
            Mathf.Sin(time * 1.17f),
            Mathf.Sin(time * 1.63f),
            Mathf.Sin(time * 1.41f)) * amplitude;

        Vector3 randomJitter = UnityEngine.Random.insideUnitSphere * (boilRandomJitter * boilRatio);
        cauldronTransform.localPosition = baselineLocalPosition + sinShake + randomJitter;
    }

    private IEnumerator PlayStirSpinCoroutine(AlchStirSpinDirection direction, float intensity)
    {
        ResolveReferences();
        float duration = Mathf.Max(0.12f, stirSpinDuration);
        float turns = Mathf.Lerp(stirSpinTurnsMin, stirSpinTurnsMax, intensity);
        float signedTurns = turns * (int)direction;
        float elapsed = 0f;

        Quaternion startRotation = baselineLocalRotation;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float normalized = Mathf.Clamp01(elapsed / duration);
            float eased = 1f - Mathf.Pow(1f - normalized, 3f);
            float angle = signedTurns * 360f * eased;
            cauldronTransform.localRotation = startRotation * Quaternion.Euler(0f, angle, 0f);
            yield return null;
        }

        cauldronTransform.localRotation = baselineLocalRotation;
        stirSpinCoroutine = null;
    }

    private IEnumerator PlayThermalBurstCoroutine()
    {
        ResolveReferences();
        float duration = Mathf.Max(0.1f, thermalBurstDuration);
        float elapsed = 0f;
        Color burstStart = displayedColor;

        Debug.Log(
            "<color=#FF1744><b>[AlchVisualController] ☠ 熱分解バースト！</b></color> " +
            "<color=#B0BEC5>有効成分が炭化寸前 — 溶液が一瞬どす黒に染まり、釜が激しく脈動しました。</color>");

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float normalized = Mathf.Clamp01(elapsed / duration);

            float scalePulse;
            if (normalized < 0.35f)
            {
                float punchT = normalized / 0.35f;
                scalePulse = 1f + thermalBurstScalePunch * punchT;
            }
            else
            {
                float shrinkT = (normalized - 0.35f) / 0.65f;
                scalePulse = 1f + thermalBurstScalePunch * (1f - shrinkT) - thermalBurstScaleShrink * shrinkT;
            }

            cauldronTransform.localScale = baselineLocalScale * Mathf.Max(0.5f, scalePulse);

            Color burstColor = Color.Lerp(burstStart, thermalBurstBlackColor, normalized * 0.92f);
            burstColor = Color.Lerp(burstColor, neonOverheatColor, Mathf.Sin(normalized * Mathf.PI) * 0.35f);
            displayedColor = burstColor;
            ApplyMaterialColor(burstColor);

            yield return null;
        }

        cauldronTransform.localScale = baselineLocalScale;
        thermalBurstCoroutine = null;
    }

    private void CaptureBaselines()
    {
        baselineLocalPosition = cauldronTransform.localPosition;
        baselineLocalScale = cauldronTransform.localScale;
        if (baselineLocalScale == Vector3.zero)
        {
            baselineLocalScale = Vector3.one;
            cauldronTransform.localScale = baselineLocalScale;
        }

        baselineLocalRotation = cauldronTransform.localRotation;
    }

    private void RestoreBaselinePose()
    {
        if (cauldronTransform == null)
        {
            return;
        }

        cauldronTransform.localPosition = baselineLocalPosition;
        cauldronTransform.localScale = baselineLocalScale;
        cauldronTransform.localRotation = baselineLocalRotation;
    }

    private void ResolveReferences()
    {
        if (cauldronTransform == null)
        {
            cauldronTransform = transform;
        }

        if (cauldronRenderer == null)
        {
            cauldronRenderer = cauldronTransform.GetComponent<Renderer>();
        }

        if (statusManager == null)
        {
            statusManager = CraftingStatusManager.EnsureInstance();
        }
    }

    private void ApplyMaterialColor(Color color)
    {
        if (cauldronMaterial == null)
        {
            return;
        }

        cauldronMaterial.color = color;
    }

    private float roomPotTemperature()
    {
        return statusManager != null
            ? statusManager.GetParam(CraftingStatusManager.CraftTypeAlch, ParamPotTemperature)
            : 20f;
    }

    private static AlchVisualController ResolveActiveController()
    {
        if (activeInstance != null)
        {
            return activeInstance;
        }

        return UnityEngine.Object.FindAnyObjectByType<AlchVisualController>();
    }

    private static float InverseLerp(float from, float to, float value)
    {
        if (Mathf.Approximately(from, to))
        {
            return 0f;
        }

        return Mathf.Clamp01((value - from) / (to - from));
    }
}

/// <summary>Play / エディタ起動時に調合大釜ビジュアルターゲットを自動配置します。</summary>
public static class AlchVisualBootstrap
{
    public const string DefaultCauldronObjectName = "AlchCauldron";

    private const float SpawnForwardDistance = 2.35f;
    private const float SpawnHeightOffset = 0.95f;
    private const float RepositionIfFartherThan = 18f;

    private static AlchVisualController cachedController;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoAttach()
    {
        AttachToScene();
    }

    /// <summary>シーン内の調合釜を探索し、無ければ Cylinder を生成して駆動します。</summary>
    public static AlchVisualController AttachToScene()
    {
        if (cachedController != null)
        {
            return cachedController;
        }

        CraftingStatusBootstrap.AttachToDebugSystemsHub();

        GameObject targetObject = GameObject.Find(DefaultCauldronObjectName);
        if (targetObject == null)
        {
            targetObject = CreateCauldronObject();
        }
        else
        {
            EnsureVisiblePlacement(targetObject.transform);
        }

        AlchVisualController controller = targetObject.GetComponent<AlchVisualController>();
        if (controller == null)
        {
            controller = targetObject.AddComponent<AlchVisualController>();
        }

        Debug.Log(
            "<color=#CE93D8><b>[AlchVisualBootstrap]</b> 調合ビジュアルを配置しました。" +
            $" 対象=<b>{targetObject.name}</b> / 座標=<b>{targetObject.transform.position}</b></color>");

        cachedController = controller;
        return controller;
    }

    private static GameObject CreateCauldronObject()
    {
        GameObject targetObject = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        targetObject.name = DefaultCauldronObjectName;
        targetObject.transform.position = ResolveSpawnPosition();
        targetObject.transform.localScale = new Vector3(1.15f, 0.55f, 1.15f);

        Collider collider = targetObject.GetComponent<Collider>();
        if (collider != null)
        {
            collider.isTrigger = true;
        }

        Renderer renderer = targetObject.GetComponent<Renderer>();
        RuntimeUrpMaterialUtility.ApplyOpaqueColor(renderer, new Color(0.12f, 0.55f, 0.72f, 1f));

#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            Undo.RegisterCreatedObjectUndo(targetObject, "Create AlchCauldron");
            MarkSceneDirty(targetObject);
        }
#endif

        return targetObject;
    }

    private static void EnsureVisiblePlacement(Transform cauldronTransform)
    {
        Transform player = ResolvePlayerTransform();
        if (player == null || cauldronTransform == null)
        {
            return;
        }

        float horizontalDistance = Vector3.Distance(
            new Vector3(cauldronTransform.position.x, 0f, cauldronTransform.position.z),
            new Vector3(player.position.x, 0f, player.position.z));

        if (horizontalDistance > RepositionIfFartherThan)
        {
            cauldronTransform.position = ResolveSpawnPosition(player);
            Debug.Log(
                "<color=#CE93D8>[AlchVisualBootstrap] AlchCauldron をプレイヤー前方へ再配置しました。</color>");
        }
    }

    private static Vector3 ResolveSpawnPosition()
    {
        return ResolveSpawnPosition(ResolvePlayerTransform());
    }

    private static Vector3 ResolveSpawnPosition(Transform player)
    {
        if (player == null)
        {
            return new Vector3(1.4f, SpawnHeightOffset, SpawnForwardDistance);
        }

        Vector3 forward = player.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.0001f)
        {
            forward = Vector3.forward;
        }

        forward.Normalize();
        Vector3 spawnPosition = player.position + forward * SpawnForwardDistance;
        spawnPosition.y = player.position.y + SpawnHeightOffset;
        return spawnPosition;
    }

    private static Transform ResolvePlayerTransform()
    {
        GameObject playerObject = GameObject.Find("PlayerRobot");
        if (playerObject == null)
        {
            playerObject = GameObject.Find("PlayerRobot ");
        }

        if (playerObject != null)
        {
            return playerObject.transform;
        }

        PlayerController controller = UnityEngine.Object.FindAnyObjectByType<PlayerController>();
        return controller != null ? controller.transform : null;
    }

    private static void MarkSceneDirty(GameObject targetObject)
    {
#if UNITY_EDITOR
        if (targetObject == null)
        {
            return;
        }

        EditorUtility.SetDirty(targetObject);
        if (targetObject.scene.IsValid())
        {
            EditorSceneManager.MarkSceneDirty(targetObject.scene);
        }
#endif
    }
}

#if UNITY_EDITOR
/// <summary>エディタ起動時に AlchCauldron をシーンへ配置し、Hierarchy から見えるようにします。</summary>
[InitializeOnLoad]
internal static class AlchVisualEditorInstaller
{
    static AlchVisualEditorInstaller()
    {
        EditorApplication.delayCall += InstallWhenSceneOpen;
    }

    private static void InstallWhenSceneOpen()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            return;
        }

        if (GameObject.Find(AlchVisualBootstrap.DefaultCauldronObjectName) != null)
        {
            return;
        }

        AlchVisualBootstrap.AttachToScene();
    }
}
#endif

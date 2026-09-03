using System;
using System.Collections;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

// =============================================================================
// 鍛冶ビジュアル制御 — CraftingStatusManager の温度・密度と 3D 形状・発光を同期
// 連携: CraftingStatusManager / CraftingStatusTester / DetailedCraftingProcessManager
// =============================================================================

/// <summary>
/// 鍛冶対象オブジェクトのマテリアル色とトランスフォームを、
/// CraftingStatusManager の Temperature / Density とリアルタイム同期するビジュアル制御。
/// </summary>
[DisallowMultipleComponent]
public class ForgeVisualController : MonoBehaviour
{
    public const string ParamTemperature = "Temperature";
    public const string ParamDensity = "Density";

    private static ForgeVisualController activeInstance;

    [Header("参照（未設定時は自身の Renderer / Transform を使用）")]
    [SerializeField] private Transform forgeTargetTransform;
    [SerializeField] private Renderer forgeTargetRenderer;
    [SerializeField] private CraftingStatusManager statusManager;

    [Header("温度色彩（℃）")]
    [SerializeField] private float roomTemperatureCelsius = 20f;
    [SerializeField] private float charcoalTemperatureCelsius = 500f;
    [SerializeField] private float austeniteStartTemperatureCelsius = 800f;
    [SerializeField] private float austenitePeakTemperatureCelsius = 1100f;
    [SerializeField] private float incandescentTemperatureCelsius = 1200f;
    [SerializeField] private float temperatureMaxCelsius = 2000f;

    [Header("冶金学カラーパレット（Standard マテリアル _Color）")]
    [SerializeField] private Color roomIronColor = new Color(0.22f, 0.22f, 0.24f, 1f);
    [SerializeField] private Color charcoalIronColor = new Color(0.08f, 0.07f, 0.07f, 1f);
    [SerializeField] private Color austeniteRedColor = new Color(0.78f, 0.12f, 0.04f, 1f);
    [SerializeField] private Color austeniteOrangeColor = new Color(1f, 0.42f, 0.08f, 1f);
    [SerializeField] private Color incandescentWhiteColor = new Color(1f, 0.96f, 0.88f, 1f);
    [SerializeField] private Color superheatedWhiteColor = new Color(1.25f, 1.2f, 1.1f, 1f);

    [Header("密度変形")]
    [SerializeField] private float densityMaxReference = 200f;
    [SerializeField] [Range(0f, 0.85f)] private float maxVerticalCompression = 0.62f;
    [SerializeField] [Range(0f, 1f)] private float maxHorizontalExpansion = 0.48f;
    [SerializeField] private float colorLerpSpeed = 8f;
    [SerializeField] private float scaleLerpSpeed = 12f;

    [Header("大槌インパクト（4キー）")]
    [SerializeField] private float hammerImpactDuration = 0.34f;
    [SerializeField] [Range(0f, 0.8f)] private float hammerVerticalSquash = 0.38f;
    [SerializeField] [Range(0f, 0.5f)] private float hammerHorizontalPunch = 0.14f;
    [SerializeField] [Range(0f, 0.35f)] private float hammerBounceOvershoot = 0.12f;
    [SerializeField] private float densityImpactMinDelta = 5f;

    private Vector3 baselineLocalScale = Vector3.one;
    private Material forgeMaterial;
    private Color displayedColor;
    private Color targetColor;
    private Vector3 displayedDensityScale = Vector3.one;
    private Vector3 targetDensityScale = Vector3.one;
    private Coroutine impactCoroutine;
    private float lastExplicitHammerImpactTime = -1f;
    private const float HammerImpactDebounceSeconds = 0.08f;

    /// <summary>シーン内で駆動中の ForgeVisualController（最初に有効化された1体）。</summary>
    public static ForgeVisualController ActiveInstance => activeInstance;

    private void Awake()
    {
        ResolveReferences();

        baselineLocalScale = forgeTargetTransform.localScale;
        if (baselineLocalScale == Vector3.zero)
        {
            baselineLocalScale = Vector3.one;
            forgeTargetTransform.localScale = baselineLocalScale;
        }

        displayedDensityScale = baselineLocalScale;
        targetDensityScale = baselineLocalScale;
        displayedColor = roomIronColor;
        targetColor = roomIronColor;

        if (forgeTargetRenderer != null)
        {
            forgeMaterial = forgeTargetRenderer.material;
            if (forgeMaterial == forgeTargetRenderer.sharedMaterial)
            {
                forgeMaterial = new Material(forgeTargetRenderer.sharedMaterial);
                forgeTargetRenderer.material = forgeMaterial;
            }

            forgeMaterial.color = displayedColor;
        }
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
        if (statusManager == null)
        {
            return;
        }

        float temperature = statusManager.GetParam(
            CraftingStatusManager.CraftTypeForge,
            ParamTemperature);
        float density = statusManager.GetParam(
            CraftingStatusManager.CraftTypeForge,
            ParamDensity);

        targetColor = EvaluateIronColorByTemperature(temperature);
        targetDensityScale = EvaluateDensityScale(density);

        float colorStep = 1f - Mathf.Exp(-colorLerpSpeed * Time.deltaTime);
        float scaleStep = 1f - Mathf.Exp(-scaleLerpSpeed * Time.deltaTime);

        displayedColor = Color.Lerp(displayedColor, targetColor, colorStep);
        displayedDensityScale = Vector3.Lerp(displayedDensityScale, targetDensityScale, scaleStep);

        ApplyMaterialColor(displayedColor);

        if (impactCoroutine == null)
        {
            forgeTargetTransform.localScale = displayedDensityScale;
        }
    }

    /// <summary>大槌打鍛（4キー）用の強烈な叩かれブレ演出を外部から起動します。</summary>
    public void TriggerHammerImpact()
    {
        TriggerHammerImpact(1f);
    }

    /// <summary>大槌打鍛の叩かれブレ演出を強度指定で起動します。</summary>
    /// <param name="intensity">0〜1 の演出強度。</param>
    public void TriggerHammerImpact(float intensity)
    {
        lastExplicitHammerImpactTime = Time.time;
        float clampedIntensity = Mathf.Clamp01(intensity);
        if (impactCoroutine != null)
        {
            StopCoroutine(impactCoroutine);
        }

        impactCoroutine = StartCoroutine(PlayHammerImpactCoroutine(clampedIntensity));
    }

    /// <summary>Inspector 未設定の Unity 参照を自身の Transform / Renderer へ補完します。</summary>
    private void ResolveReferences()
    {
        if (forgeTargetTransform == null)
        {
            forgeTargetTransform = transform;
        }

        if (forgeTargetRenderer == null)
        {
            forgeTargetRenderer = forgeTargetTransform.GetComponent<Renderer>();
        }

        if (statusManager == null)
        {
            statusManager = CraftingStatusManager.EnsureInstance();
        }
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (forgeTargetTransform == null)
        {
            forgeTargetTransform = transform;
        }

        if (forgeTargetRenderer == null && forgeTargetTransform != null)
        {
            forgeTargetRenderer = forgeTargetTransform.GetComponent<Renderer>();
        }
    }
#endif

    /// <summary>アクティブな ForgeVisualController へ大槌インパクトを通知します。</summary>
    public static void NotifyHammerStrikeGlobally()
    {
        if (activeInstance != null)
        {
            activeInstance.TriggerHammerImpact();
            return;
        }

        ForgeVisualController found = FindAnyObjectByType<ForgeVisualController>();
        if (found != null)
        {
            found.TriggerHammerImpact();
        }
    }

    /// <summary>現在の CraftingStatusManager 値から色とスケールを即時同期します。</summary>
    /// <param name="forceImmediate">true のとき補間をスキップして即反映します。</param>
    public void RefreshFromStatusManager(bool forceImmediate = false)
    {
        ResolveReferences();
        if (statusManager == null)
        {
            return;
        }

        float temperature = statusManager.GetParam(
            CraftingStatusManager.CraftTypeForge,
            ParamTemperature);
        float density = statusManager.GetParam(
            CraftingStatusManager.CraftTypeForge,
            ParamDensity);

        targetColor = EvaluateIronColorByTemperature(temperature);
        targetDensityScale = EvaluateDensityScale(density);

        if (forceImmediate)
        {
            displayedColor = targetColor;
            displayedDensityScale = targetDensityScale;
            ApplyMaterialColor(displayedColor);
            if (impactCoroutine == null)
            {
                forgeTargetTransform.localScale = displayedDensityScale;
            }
        }
    }

    private void HandleParamModified(CraftingParamChangeInfo changeInfo)
    {
        if (!string.Equals(
                changeInfo.CraftType,
                CraftingStatusManager.CraftTypeForge,
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (string.Equals(changeInfo.ParamName, ParamDensity, StringComparison.OrdinalIgnoreCase) &&
            changeInfo.Delta >= densityImpactMinDelta &&
            Time.time - lastExplicitHammerImpactTime > HammerImpactDebounceSeconds)
        {
            float normalizedStrength = Mathf.Clamp01(changeInfo.Delta / densityMaxReference);
            TriggerHammerImpact(Mathf.Lerp(0.35f, 0.85f, normalizedStrength));
        }
    }

    private void HandleStatusReset(string craftType)
    {
        if (!string.Equals(
                craftType,
                CraftingStatusManager.CraftTypeForge,
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (impactCoroutine != null)
        {
            StopCoroutine(impactCoroutine);
            impactCoroutine = null;
        }

        RefreshFromStatusManager(forceImmediate: true);
        forgeTargetTransform.localScale = displayedDensityScale;
    }

    /// <summary>鉄の温度（℃）から冶金学的な見た目カラーを算出します。</summary>
    /// <param name="temperatureCelsius">CraftingStatusManager の Temperature 値。</param>
    public Color EvaluateIronColorByTemperature(float temperatureCelsius)
    {
        float temp = Mathf.Clamp(temperatureCelsius, roomTemperatureCelsius, temperatureMaxCelsius);

        if (temp <= charcoalTemperatureCelsius)
        {
            float t = InverseLerp(roomTemperatureCelsius, charcoalTemperatureCelsius, temp);
            return Color.Lerp(roomIronColor, charcoalIronColor, t);
        }

        if (temp <= austeniteStartTemperatureCelsius)
        {
            float t = InverseLerp(charcoalTemperatureCelsius, austeniteStartTemperatureCelsius, temp);
            Color warmGray = Color.Lerp(charcoalIronColor, austeniteRedColor * 0.55f, t);
            return warmGray;
        }

        if (temp <= austenitePeakTemperatureCelsius)
        {
            float t = InverseLerp(austeniteStartTemperatureCelsius, austenitePeakTemperatureCelsius, temp);
            return Color.Lerp(austeniteRedColor, austeniteOrangeColor, t);
        }

        if (temp <= incandescentTemperatureCelsius)
        {
            float t = InverseLerp(austenitePeakTemperatureCelsius, incandescentTemperatureCelsius, temp);
            return Color.Lerp(austeniteOrangeColor, incandescentWhiteColor, t);
        }

        float whiteT = InverseLerp(incandescentTemperatureCelsius, temperatureMaxCelsius, temp);
        return Color.Lerp(incandescentWhiteColor, superheatedWhiteColor, whiteT);
    }

    /// <summary>密度パラメータから Y 軸圧縮・XZ 軸拡張の決定論スケールを算出します。</summary>
    /// <param name="density">CraftingStatusManager の Density 値。</param>
    public Vector3 EvaluateDensityScale(float density)
    {
        float maxDensity = Mathf.Max(1f, densityMaxReference);
        if (statusManager != null)
        {
            maxDensity = Mathf.Max(1f, statusManager.GetParamMax(
                CraftingStatusManager.CraftTypeForge,
                ParamDensity));
        }

        float ratio = Mathf.Clamp01(density / maxDensity);
        float yScale = baselineLocalScale.y * (1f - ratio * maxVerticalCompression);
        float xzScale = baselineLocalScale.x * (1f + ratio * maxHorizontalExpansion);
        return new Vector3(xzScale, yScale, xzScale);
    }

    private IEnumerator PlayHammerImpactCoroutine(float intensity)
    {
        ResolveReferences();

        float clampedIntensity = Mathf.Clamp01(intensity);
        float duration = Mathf.Max(0.08f, hammerImpactDuration);
        float elapsed = 0f;

        Vector3 restScale = targetDensityScale;
        float squashY = 1f - hammerVerticalSquash * clampedIntensity;
        float punchXZ = 1f + hammerHorizontalPunch * clampedIntensity;
        float bounceY = 1f + hammerBounceOvershoot * clampedIntensity;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float normalizedTime = Mathf.Clamp01(elapsed / duration);

            float bouncePhase = Mathf.Sin(normalizedTime * Mathf.PI * 2.2f) * Mathf.Exp(-normalizedTime * 4.5f);

            float yMultiplier = 1f;
            float xzMultiplier = 1f;

            if (normalizedTime < 0.22f)
            {
                float strikeT = normalizedTime / 0.22f;
                yMultiplier = Mathf.Lerp(1f, squashY, strikeT);
                xzMultiplier = Mathf.Lerp(1f, punchXZ, strikeT);
            }
            else
            {
                float reboundT = (normalizedTime - 0.22f) / 0.78f;
                float settle = 1f - reboundT;
                yMultiplier = Mathf.Lerp(squashY, 1f, reboundT) + bouncePhase * bounceY * settle;
                xzMultiplier = Mathf.Lerp(punchXZ, 1f, reboundT);
            }

            yMultiplier = Mathf.Max(0.12f, yMultiplier);
            Vector3 impactScale = new Vector3(
                restScale.x * xzMultiplier,
                restScale.y * yMultiplier,
                restScale.z * xzMultiplier);

            forgeTargetTransform.localScale = impactScale;
            yield return null;
        }

        forgeTargetTransform.localScale = restScale;
        displayedDensityScale = restScale;
        impactCoroutine = null;
    }

    private void ApplyMaterialColor(Color color)
    {
        if (forgeMaterial == null)
        {
            return;
        }

        forgeMaterial.color = color;
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

/// <summary>Play / エディタ起動時に鍛冶用ビジュアルターゲット（Cube）を自動配置します。</summary>
public static class ForgeVisualBootstrap
{
    public const string DefaultForgeObjectName = "ForgeIngot";

    private const float SpawnForwardDistance = 2.6f;
    private const float SpawnHeightOffset = 1.05f;
    private const float RepositionIfFartherThan = 18f;

    private static ForgeVisualController cachedController;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoAttach()
    {
        AttachToScene();
    }

    /// <summary>シーン内の鍛冶ターゲットを探索し、無ければ Primitive Cube を生成して駆動します。</summary>
    public static ForgeVisualController AttachToScene()
    {
        if (cachedController != null)
        {
            return cachedController;
        }

        CraftingStatusBootstrap.AttachToDebugSystemsHub();

        GameObject targetObject = GameObject.Find(DefaultForgeObjectName);
        if (targetObject == null)
        {
            targetObject = CreateForgeIngotObject();
        }
        else
        {
            EnsureVisiblePlacement(targetObject.transform);
        }

        ForgeVisualController controller = targetObject.GetComponent<ForgeVisualController>();
        if (controller == null)
        {
            controller = targetObject.AddComponent<ForgeVisualController>();
        }

        Debug.Log(
            "<color=#FF7043><b>[ForgeVisualBootstrap]</b> 鍛冶ビジュアルを配置しました。" +
            $" 対象=<b>{targetObject.name}</b> / 座標=<b>{targetObject.transform.position}</b></color>");

        cachedController = controller;
        return controller;
    }

    private static GameObject CreateForgeIngotObject()
    {
        GameObject targetObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
        targetObject.name = DefaultForgeObjectName;
        targetObject.transform.position = ResolveForgeSpawnPosition();
        targetObject.transform.localScale = new Vector3(1.1f, 1.1f, 1.1f);

        Collider collider = targetObject.GetComponent<Collider>();
        if (collider != null)
        {
            collider.isTrigger = true;
        }

        Renderer renderer = targetObject.GetComponent<Renderer>();
        RuntimeUrpMaterialUtility.ApplyOpaqueColor(renderer, new Color(0.22f, 0.22f, 0.24f, 1f));

#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            Undo.RegisterCreatedObjectUndo(targetObject, "Create ForgeIngot");
            MarkSceneDirty(targetObject);
        }
#endif

        return targetObject;
    }

    private static void EnsureVisiblePlacement(Transform forgeTransform)
    {
        Transform player = ResolvePlayerTransform();
        if (player == null || forgeTransform == null)
        {
            return;
        }

        float horizontalDistance = Vector3.Distance(
            new Vector3(forgeTransform.position.x, 0f, forgeTransform.position.z),
            new Vector3(player.position.x, 0f, player.position.z));

        if (horizontalDistance > RepositionIfFartherThan)
        {
            forgeTransform.position = ResolveForgeSpawnPosition(player);
            Debug.Log(
                "<color=#FFAB40>[ForgeVisualBootstrap] ForgeIngot をプレイヤー前方へ再配置しました。</color>");
        }
    }

    private static Vector3 ResolveForgeSpawnPosition()
    {
        return ResolveForgeSpawnPosition(ResolvePlayerTransform());
    }

    private static Vector3 ResolveForgeSpawnPosition(Transform player)
    {
        if (player == null)
        {
            return new Vector3(0f, SpawnHeightOffset, SpawnForwardDistance);
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
/// <summary>エディタ起動時に ForgeIngot をシーンへ配置し、Hierarchy から見えるようにします。</summary>
[InitializeOnLoad]
internal static class ForgeVisualEditorInstaller
{
    static ForgeVisualEditorInstaller()
    {
        EditorApplication.delayCall += InstallWhenSceneOpen;
    }

    private static void InstallWhenSceneOpen()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            return;
        }

        if (GameObject.Find(ForgeVisualBootstrap.DefaultForgeObjectName) != null)
        {
            return;
        }

        ForgeVisualBootstrap.AttachToScene();
    }
}
#endif

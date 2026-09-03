using UnityEngine;
using UnityEngine.UI;

// =============================================================================
// 工房 / 魔獣スポーン直上のカメラ向き Floating UI
// 連携: MicroPhaseSimUI / ProceduralMapPopulator
// =============================================================================

/// <summary>ワールド空間マーカーの対象種別。</summary>
public enum WorldSpaceMarkerKind
{
    Workshop = 0,
    BeastSpawn = 1,
    WorkSpot = 2,
    BreachAlert = 3
}

/// <summary>
/// 対象トランスフォームの直上に浮かび、カメラを向くマーカーです。
/// Canvas / Text が作れない場合は Quad にフォールバックします。
/// </summary>
[DefaultExecutionOrder(56)]
public class WorldSpaceMarker : MonoBehaviour
{
    public const float DefaultHeightOffset = 5.2f;

    [SerializeField] private Transform followTarget;
    [SerializeField] private WorldSpaceMarkerKind kind;
    [SerializeField] private string label = string.Empty;
    [SerializeField] private Vector3 worldOffset = new Vector3(0f, DefaultHeightOffset, 0f);
    [SerializeField] private Canvas worldCanvas;
    [SerializeField] private Image backdrop;
    [SerializeField] private Text labelText;
    [SerializeField] private Transform fallbackQuad;
    [SerializeField] private bool emphasized;

    private Vector3 baseLocalScale = Vector3.one;
    private bool built;

    public WorldSpaceMarkerKind Kind => kind;
    public bool IsEmphasized => emphasized;
    public Transform FollowTarget => followTarget;

    /// <summary>追従対象とラベルを設定し、ビジュアルを構築します。</summary>
    public void Initialize(Transform target, WorldSpaceMarkerKind markerKind, string markerLabel)
    {
        followTarget = target;
        kind = markerKind;
        label = string.IsNullOrEmpty(markerLabel) ? DefaultLabel(markerKind) : markerLabel;
        EnsureVisuals();
        SnapToTarget();
    }

    /// <summary>4 期に応じて工房（休眠）／スポーン（活性）を強調します。</summary>
    public void ApplySeason(VariableTimelineSeason season)
    {
        EnsureVisuals();
        bool workshopHot = kind == WorldSpaceMarkerKind.Workshop &&
                           season == VariableTimelineSeason.Dormant;
        bool workSpotHot = kind == WorldSpaceMarkerKind.WorkSpot &&
                           (season == VariableTimelineSeason.Dormant ||
                            season == VariableTimelineSeason.Deescalation);
        bool spawnHot = kind == WorldSpaceMarkerKind.BeastSpawn &&
                        (season == VariableTimelineSeason.Active ||
                         season == VariableTimelineSeason.Escalation);
        bool breachHot = kind == WorldSpaceMarkerKind.BreachAlert;
        SetEmphasized(workshopHot || workSpotHot || spawnHot || breachHot);
    }

    /// <summary>結界裂け目など赤VFXの警戒ステータスを切り替えます。</summary>
    public void SetHazardAlert(bool on, string hazardLabel)
    {
        if (on)
        {
            if (kind != WorldSpaceMarkerKind.BreachAlert)
            {
                kind = WorldSpaceMarkerKind.BreachAlert;
            }

            SetDisplayLabel(string.IsNullOrWhiteSpace(hazardLabel)
                ? "[結界危険 / 侵入警戒]"
                : hazardLabel);
            SetEmphasized(true);
            ApplyHazardColor();
            return;
        }

        if (kind == WorldSpaceMarkerKind.BreachAlert)
        {
            kind = WorldSpaceMarkerKind.WorkSpot;
        }

        SetEmphasized(false);
    }

    public void SetEmphasized(bool on)
    {
        emphasized = on;
        float scaleMul = on ? 1.32f : 0.92f;
        transform.localScale = baseLocalScale * scaleMul;

        Color color = ResolveColor(kind, on);
        if (backdrop != null)
        {
            backdrop.color = color;
        }

        if (labelText != null)
        {
            labelText.color = on ? Color.white : new Color(1f, 1f, 1f, 0.72f);
        }

        ApplyFallbackColor(color);
    }

    private void ApplyHazardColor()
    {
        Color hazard = new Color(0.95f, 0.12f, 0.08f, 0.95f);
        if (backdrop != null)
        {
            backdrop.color = hazard;
        }

        if (labelText != null)
        {
            labelText.color = Color.white;
        }

        ApplyFallbackColor(hazard);
    }

    /// <summary>施設破損／修復中などの動的ステータス文言を反映します。</summary>
    public void SetDisplayLabel(string newLabel)
    {
        if (string.IsNullOrWhiteSpace(newLabel))
        {
            return;
        }

        label = newLabel.Trim();
        EnsureVisuals();
        if (labelText != null)
        {
            labelText.text = label;
        }

        bool hazard = label.Contains("結界危険") ||
                      label.Contains("侵入警戒") ||
                      label.Contains("裂け目");
        bool damaged = label.Contains("破損") || label.Contains("損傷");
        bool repairing = label.Contains("修復");
        if (hazard || damaged)
        {
            SetEmphasized(true);
            ApplyHazardColor();
        }
        else if (repairing)
        {
            SetEmphasized(true);
            if (backdrop != null)
            {
                backdrop.color = new Color(0.95f, 0.75f, 0.25f, 0.9f);
            }
        }
    }

    private void ApplyFallbackColor(Color color)
    {
        if (fallbackQuad == null)
        {
            return;
        }

        Renderer renderer = fallbackQuad.GetComponent<Renderer>();
        if (renderer == null)
        {
            return;
        }

        try
        {
            RuntimeUrpMaterialUtility.ApplyTransparentColor(renderer, color);
        }
        catch (System.Exception)
        {
            if (renderer.sharedMaterial != null)
            {
                renderer.material.color = color;
            }
        }
    }

    private void LateUpdate()
    {
        SnapToTarget();
        FaceCamera();
    }

    private void SnapToTarget()
    {
        if (followTarget == null)
        {
            return;
        }

        transform.position = followTarget.position + worldOffset;
    }

    private void FaceCamera()
    {
        Camera camera = Camera.main;
        if (camera == null)
        {
            return;
        }

        transform.rotation = camera.transform.rotation;
    }

    private void EnsureVisuals()
    {
        if (built)
        {
            return;
        }

        built = true;
        baseLocalScale = transform.localScale.sqrMagnitude > 0.0001f ? transform.localScale : Vector3.one;

        if (!TryBuildCanvasLabel())
        {
            BuildFallbackQuad();
        }
    }

    private bool TryBuildCanvasLabel()
    {
        try
        {
            GameObject canvasObject = new GameObject("MarkerCanvas", typeof(RectTransform));
            canvasObject.transform.SetParent(transform, false);
            canvasObject.transform.localPosition = Vector3.zero;
            canvasObject.transform.localRotation = Quaternion.identity;
            canvasObject.transform.localScale = new Vector3(0.025f, 0.025f, 0.025f);

            worldCanvas = canvasObject.AddComponent<Canvas>();
            worldCanvas.renderMode = RenderMode.WorldSpace;
            worldCanvas.sortingOrder = 80;
            RectTransform canvasRect = canvasObject.GetComponent<RectTransform>();
            canvasRect.sizeDelta = new Vector2(220f, 56f);

            GameObject imageObject = new GameObject("Backdrop", typeof(RectTransform));
            imageObject.transform.SetParent(canvasObject.transform, false);
            RectTransform imageRect = imageObject.GetComponent<RectTransform>();
            imageRect.anchorMin = Vector2.zero;
            imageRect.anchorMax = Vector2.one;
            imageRect.offsetMin = Vector2.zero;
            imageRect.offsetMax = Vector2.zero;
            backdrop = imageObject.AddComponent<Image>();
            backdrop.raycastTarget = false;
            backdrop.color = ResolveColor(kind, false);

            GameObject textObject = new GameObject("Label", typeof(RectTransform));
            textObject.transform.SetParent(canvasObject.transform, false);
            RectTransform textRect = textObject.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(8f, 4f);
            textRect.offsetMax = new Vector2(-8f, -4f);
            labelText = textObject.AddComponent<Text>();
            RuntimeUIFontHelper.ApplyTo(labelText, 22, FontStyle.Bold, TextAnchor.MiddleCenter);
            labelText.raycastTarget = false;
            labelText.supportRichText = false;
            labelText.text = label;
            return labelText != null && backdrop != null;
        }
        catch (System.Exception exception)
        {
            Debug.LogWarning($"[WorldSpaceMarker] Canvas 構築をスキップ: {exception.Message}");
            worldCanvas = null;
            backdrop = null;
            labelText = null;
            return false;
        }
    }

    private void BuildFallbackQuad()
    {
        try
        {
            GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "MarkerQuad";
            quad.transform.SetParent(transform, false);
            quad.transform.localPosition = Vector3.zero;
            quad.transform.localScale = new Vector3(3.2f, 0.9f, 1f);
            Collider collider = quad.GetComponent<Collider>();
            if (collider != null)
            {
                collider.enabled = false;
            }

            fallbackQuad = quad.transform;
        }
        catch (System.Exception exception)
        {
            Debug.LogWarning($"[WorldSpaceMarker] Quad フォールバック失敗: {exception.Message}");
        }
    }

    private static Color ResolveColor(WorldSpaceMarkerKind markerKind, bool on)
    {
        if (markerKind == WorldSpaceMarkerKind.Workshop)
        {
            return on
                ? new Color(1f, 0.86f, 0.18f, 0.95f)
                : new Color(0.72f, 0.62f, 0.28f, 0.42f);
        }

        if (markerKind == WorldSpaceMarkerKind.WorkSpot)
        {
            return on
                ? new Color(0.45f, 0.92f, 0.78f, 0.95f)
                : new Color(0.28f, 0.55f, 0.48f, 0.45f);
        }

        if (markerKind == WorldSpaceMarkerKind.BreachAlert)
        {
            return new Color(0.95f, 0.12f, 0.08f, 0.95f);
        }

        return on
            ? new Color(1f, 0.18f, 0.12f, 0.95f)
            : new Color(0.55f, 0.16f, 0.14f, 0.32f);
    }

    private static string DefaultLabel(WorldSpaceMarkerKind markerKind)
    {
        switch (markerKind)
        {
            case WorldSpaceMarkerKind.Workshop:
                return "工房";
            case WorldSpaceMarkerKind.WorkSpot:
                return "作業スポット";
            case WorldSpaceMarkerKind.BreachAlert:
                return "[結界危険 / 侵入警戒]";
            default:
                return "魔獣スポーン";
        }
    }
}

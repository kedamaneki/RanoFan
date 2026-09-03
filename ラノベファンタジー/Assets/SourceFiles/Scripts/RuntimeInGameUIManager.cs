using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// =============================================================================
// 閃き・超加速・ショップ更新などの画面内ビジュアルフィードバック統括
// DebugSystemsHub または PlayerRobot に配置してください。
// ※ 日本語表示のため TMP ではなく OS フォント + uGUI Text を使用します。
// =============================================================================

/// <summary>画面演出でよく使うテーマカラー定義。</summary>
public static class RuntimeUIThemeColors
{
    /// <summary>ユニークスキル・ピンチ閃き（金色）</summary>
    public static readonly Color UniqueGold = new Color(1f, 0.84f, 0f, 1f);

    /// <summary>技術型・ログ監視閃き（水色）</summary>
    public static readonly Color TechnicalCyan = new Color(0f, 0.9f, 1f, 1f);

    /// <summary>能動的技開発（エメラルドグリーン）</summary>
    public static readonly Color DevelopmentGreen = new Color(0f, 0.9f, 0.46f, 1f);

    /// <summary>極意到達（紫）</summary>
    public static readonly Color MasteryPurple = new Color(0.7f, 0.53f, 1f, 1f);

    /// <summary>AI Crisis フェーズ（禍々しいピンク）</summary>
    public static readonly Color CrisisMagenta = new Color(0.85f, 0.2f, 0.75f, 1f);

    /// <summary>AI Fusion フェーズ（藤色）</summary>
    public static readonly Color FusionViolet = new Color(0.78f, 0.49f, 1f, 1f);

    /// <summary>ショップ通知（スカイブルー）</summary>
    public static readonly Color ShopSkyBlue = new Color(0.31f, 0.76f, 0.97f, 1f);
}

/// <summary>日本語対応 uGUI 用フォント（OS 動的フォント）を提供します。</summary>
public static class RuntimeUIFontHelper
{
    private static Font cachedFont;

    /// <summary>日本語を表示できる OS フォントを返します。</summary>
    public static Font GetJapaneseUIFont()
    {
        if (cachedFont != null)
        {
            return cachedFont;
        }

        string[] candidates =
        {
            "Yu Gothic UI",
            "Meiryo UI",
            "MS Gothic",
            "Hiragino Sans",
            "Noto Sans CJK JP",
            "Arial Unicode MS",
            "Arial"
        };

        cachedFont = Font.CreateDynamicFontFromOSFont(candidates, 32);
        return cachedFont;
    }

    /// <summary>uGUI Text に日本語フォントと基本スタイルを適用します。</summary>
    public static void ApplyTo(Text text, int fontSize, FontStyle style, TextAnchor alignment)
    {
        if (text == null)
        {
            return;
        }

        text.font = GetJapaneseUIFont();
        text.fontSize = fontSize;
        text.fontStyle = style;
        text.alignment = alignment;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Truncate;
        text.supportRichText = false;
        text.color = Color.white;
    }
}

/// <summary>
/// ゲーム画面上のポップアップ・フィルター・ショップ通知を統括するランタイム UI マネージャー。
/// </summary>
[DefaultExecutionOrder(50)]
public class RuntimeInGameUIManager : MonoBehaviour
{
    private const int UiBuildVersion = 2;

    public static RuntimeInGameUIManager Instance { get; private set; }

    [Header("演出タイミング")]
    [SerializeField] private float inspirationPopupDuration = 3f;
    [SerializeField] private float inspirationFadeDuration = 0.55f;
    [SerializeField] private float shopNotificationDuration = 4f;
    [SerializeField] private float shopSlideDuration = 0.35f;
    [SerializeField] private float chronostasisFadeDuration = 0.4f;

    [Header("クロノスタシス・フィルター色")]
    [SerializeField] private Color chronostasisFilterColor = new Color(0f, 0f, 0f, 0.4f);

    [Header("参照（未設定時は自動生成）")]
    [SerializeField] private Canvas rootCanvas;
    [SerializeField] private Image chronostasisFilterImage;
    [SerializeField] private RectTransform inspirationPopupRoot;
    [SerializeField] private Text inspirationTitleText;
    [SerializeField] private Text inspirationDetailsText;
    [SerializeField] private Image inspirationBackdrop;
    [SerializeField] private RectTransform shopNotificationRoot;
    [SerializeField] private Text shopNotificationText;
    [SerializeField] private Image shopNotificationBackdrop;

    [SerializeField] private int uiBuildVersion;

    private Coroutine inspirationPopupCoroutine;
    private Coroutine shopNotificationCoroutine;
    private Coroutine chronostasisFadeCoroutine;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        EnsureUiHierarchy();
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    /// <summary>
    /// シーンにマネージャーが無い場合に動的生成して返します。
    /// </summary>
    public static RuntimeInGameUIManager EnsureInstance()
    {
        if (Instance != null)
        {
            return Instance;
        }

        GameObject host = new GameObject("RuntimeInGameUIManager");
        return host.AddComponent<RuntimeInGameUIManager>();
    }

    /// <summary>
    /// 閃き・技開発・AI生成成功時の中央ポップアップを表示します。
    /// </summary>
    public void ShowInspirationPopup(string title, string details, Color themeColor)
    {
        EnsureUiHierarchy();

        if (inspirationPopupCoroutine != null)
        {
            StopCoroutine(inspirationPopupCoroutine);
        }

        inspirationPopupCoroutine = StartCoroutine(ShowInspirationPopupRoutine(title, details, themeColor));
    }

    /// <summary>
    /// 超加速（世界遅延）発動時の全画面フィルターを ON/OFF します。
    /// </summary>
    public void SetChronostasisFilter(bool active)
    {
        EnsureUiHierarchy();

        if (chronostasisFadeCoroutine != null)
        {
            StopCoroutine(chronostasisFadeCoroutine);
        }

        chronostasisFadeCoroutine = StartCoroutine(FadeChronostasisFilterRoutine(active));
    }

    /// <summary>
    /// ショップの棚更新・おまけ獲得などを右上通知として表示します。
    /// </summary>
    public void ShowShopNotification(string message)
    {
        EnsureUiHierarchy();

        if (shopNotificationCoroutine != null)
        {
            StopCoroutine(shopNotificationCoroutine);
        }

        shopNotificationCoroutine = StartCoroutine(ShowShopNotificationRoutine(message));
    }

    /// <summary>GamePhase に応じたテーマ色で閃きポップアップを表示します。</summary>
    public void ShowInspirationPopupForPhase(GamePhase phase, string skillName, string details)
    {
        Color theme = ResolvePhaseThemeColor(phase);
        string prefix = phase == GamePhase.Crisis ? "ピンチ覚醒" : "閃き";
        ShowInspirationPopup($"【{prefix}】{skillName}", details, theme);
    }

    /// <summary>生産実験の決定論的評価結果を画面中央に表示します。</summary>
    public void ShowCraftingResultPopup(string archetypeLabel, string details, bool isJunk)
    {
        Color theme = isJunk
            ? new Color(1f, 0.42f, 0.42f, 1f)
            : RuntimeUIThemeColors.DevelopmentGreen;
        ShowInspirationPopup($"【生産確定】{archetypeLabel}", details, theme);
    }

    private static Color ResolvePhaseThemeColor(GamePhase phase)
    {
        switch (phase)
        {
            case GamePhase.Crisis:
                return RuntimeUIThemeColors.CrisisMagenta;
            case GamePhase.Fusion:
                return RuntimeUIThemeColors.FusionViolet;
            case GamePhase.Training:
                return RuntimeUIThemeColors.TechnicalCyan;
            case GamePhase.Crafting:
                return RuntimeUIThemeColors.ShopSkyBlue;
            default:
                return RuntimeUIThemeColors.UniqueGold;
        }
    }

    private void EnsureUiHierarchy()
    {
        if (uiBuildVersion != UiBuildVersion)
        {
            DestroyGeneratedUi();
            uiBuildVersion = UiBuildVersion;
        }

        if (rootCanvas != null && inspirationTitleText != null)
        {
            return;
        }

        EnsureEventSystem();

        if (rootCanvas == null)
        {
            rootCanvas = GetComponentInChildren<Canvas>();
        }

        if (rootCanvas == null)
        {
            GameObject canvasObject = new GameObject("RuntimeInGameCanvas", typeof(RectTransform));
            canvasObject.transform.SetParent(transform, false);

            rootCanvas = canvasObject.AddComponent<Canvas>();
            rootCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            rootCanvas.sortingOrder = 9000;
            rootCanvas.vertexColorAlwaysGammaSpace = true;

            CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            canvasObject.AddComponent<GraphicRaycaster>();
        }

        if (chronostasisFilterImage == null)
        {
            chronostasisFilterImage = CreateFullscreenImage(
                "ChronostasisFilter",
                rootCanvas.transform,
                new Color(0f, 0f, 0f, 0f));
            chronostasisFilterImage.raycastTarget = false;
        }

        if (inspirationPopupRoot == null)
        {
            inspirationPopupRoot = CreatePopupPanel(
                "InspirationPopup",
                rootCanvas.transform,
                new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f),
                new Vector2(760f, 240f),
                Vector2.zero,
                out inspirationBackdrop,
                out inspirationTitleText,
                out inspirationDetailsText);

            RuntimeUIFontHelper.ApplyTo(inspirationTitleText, 30, FontStyle.Bold, TextAnchor.MiddleCenter);
            RuntimeUIFontHelper.ApplyTo(inspirationDetailsText, 20, FontStyle.Normal, TextAnchor.MiddleCenter);
            inspirationPopupRoot.gameObject.SetActive(false);
        }

        if (shopNotificationRoot == null)
        {
            shopNotificationRoot = CreatePopupPanel(
                "ShopNotification",
                rootCanvas.transform,
                new Vector2(1f, 1f),
                new Vector2(1f, 1f),
                new Vector2(560f, 110f),
                new Vector2(-24f, -24f),
                out shopNotificationBackdrop,
                out shopNotificationText,
                out Text unusedDetails);

            unusedDetails.gameObject.SetActive(false);
            RuntimeUIFontHelper.ApplyTo(shopNotificationText, 22, FontStyle.Bold, TextAnchor.MiddleRight);
            shopNotificationRoot.gameObject.SetActive(false);
        }
    }

    private void DestroyGeneratedUi()
    {
        if (rootCanvas != null)
        {
            Destroy(rootCanvas.gameObject);
        }

        rootCanvas = null;
        chronostasisFilterImage = null;
        inspirationPopupRoot = null;
        inspirationTitleText = null;
        inspirationDetailsText = null;
        inspirationBackdrop = null;
        shopNotificationRoot = null;
        shopNotificationText = null;
        shopNotificationBackdrop = null;
    }

    private static void EnsureEventSystem()
    {
        if (FindAnyObjectByType<EventSystem>() != null)
        {
            return;
        }

        GameObject eventSystemObject = new GameObject("EventSystem");
        eventSystemObject.AddComponent<EventSystem>();
        eventSystemObject.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
    }

    private static Image CreateFullscreenImage(string objectName, Transform parent, Color color)
    {
        GameObject imageObject = new GameObject(objectName, typeof(RectTransform));
        imageObject.transform.SetParent(parent, false);

        RectTransform rect = imageObject.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        Image image = imageObject.AddComponent<Image>();
        image.color = color;
        return image;
    }

    private static RectTransform CreatePopupPanel(
        string objectName,
        Transform parent,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Vector2 sizeDelta,
        Vector2 anchoredPosition,
        out Image backdrop,
        out Text titleText,
        out Text detailsText)
    {
        GameObject rootObject = new GameObject(objectName, typeof(RectTransform));
        rootObject.transform.SetParent(parent, false);

        RectTransform root = rootObject.GetComponent<RectTransform>();
        root.anchorMin = anchorMin;
        root.anchorMax = anchorMax;
        root.pivot = anchorMax;
        root.sizeDelta = sizeDelta;
        root.anchoredPosition = anchoredPosition;

        GameObject backdropObject = new GameObject("Backdrop", typeof(RectTransform));
        backdropObject.transform.SetParent(root, false);
        RectTransform backdropRect = backdropObject.GetComponent<RectTransform>();
        backdropRect.anchorMin = Vector2.zero;
        backdropRect.anchorMax = Vector2.one;
        backdropRect.offsetMin = Vector2.zero;
        backdropRect.offsetMax = Vector2.zero;
        backdrop = backdropObject.AddComponent<Image>();
        backdrop.color = new Color(0f, 0f, 0f, 0.72f);

        GameObject titleObject = new GameObject("Title", typeof(RectTransform));
        titleObject.transform.SetParent(root, false);
        RectTransform titleRect = titleObject.GetComponent<RectTransform>();
        titleRect.anchorMin = new Vector2(0.05f, 0.52f);
        titleRect.anchorMax = new Vector2(0.95f, 0.92f);
        titleRect.offsetMin = Vector2.zero;
        titleRect.offsetMax = Vector2.zero;
        titleText = titleObject.AddComponent<Text>();
        titleText.text = "閃き";

        GameObject detailsObject = new GameObject("Details", typeof(RectTransform));
        detailsObject.transform.SetParent(root, false);
        RectTransform detailsRect = detailsObject.GetComponent<RectTransform>();
        detailsRect.anchorMin = new Vector2(0.06f, 0.08f);
        detailsRect.anchorMax = new Vector2(0.94f, 0.5f);
        detailsRect.offsetMin = Vector2.zero;
        detailsRect.offsetMax = Vector2.zero;
        detailsText = detailsObject.AddComponent<Text>();
        detailsText.text = string.Empty;

        return root;
    }

    private IEnumerator ShowInspirationPopupRoutine(string title, string details, Color themeColor)
    {
        inspirationPopupRoot.gameObject.SetActive(true);
        inspirationTitleText.text = title ?? string.Empty;
        inspirationTitleText.color = themeColor;
        inspirationDetailsText.text = details ?? string.Empty;
        inspirationDetailsText.color = Color.white;
        inspirationBackdrop.color = new Color(themeColor.r, themeColor.g, themeColor.b, 0.28f);

        Vector3 baseScale = Vector3.one;
        inspirationPopupRoot.localScale = Vector3.zero;

        float punchDuration = 0.22f;
        float elapsed = 0f;
        while (elapsed < punchDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / punchDuration);
            float scale = Mathf.Lerp(0f, 1.12f, t);
            inspirationPopupRoot.localScale = baseScale * scale;
            yield return null;
        }

        inspirationPopupRoot.localScale = baseScale;

        float hold = Mathf.Max(0.5f, inspirationPopupDuration - inspirationFadeDuration);
        yield return new WaitForSecondsRealtime(hold);

        elapsed = 0f;
        CanvasGroup group = inspirationPopupRoot.GetComponent<CanvasGroup>();
        if (group == null)
        {
            group = inspirationPopupRoot.gameObject.AddComponent<CanvasGroup>();
        }

        group.alpha = 1f;
        while (elapsed < inspirationFadeDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            group.alpha = 1f - Mathf.Clamp01(elapsed / inspirationFadeDuration);
            yield return null;
        }

        group.alpha = 1f;
        inspirationPopupRoot.gameObject.SetActive(false);
        inspirationPopupCoroutine = null;
    }

    private IEnumerator FadeChronostasisFilterRoutine(bool active)
    {
        Color start = chronostasisFilterImage.color;
        Color target = active ? chronostasisFilterColor : new Color(0f, 0f, 0f, 0f);

        float elapsed = 0f;
        while (elapsed < chronostasisFadeDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / chronostasisFadeDuration);
            chronostasisFilterImage.color = Color.Lerp(start, target, t);
            yield return null;
        }

        chronostasisFilterImage.color = target;
        chronostasisFadeCoroutine = null;
    }

    private IEnumerator ShowShopNotificationRoutine(string message)
    {
        shopNotificationRoot.gameObject.SetActive(true);
        shopNotificationText.text = message ?? string.Empty;
        shopNotificationBackdrop.color = new Color(
            RuntimeUIThemeColors.ShopSkyBlue.r,
            RuntimeUIThemeColors.ShopSkyBlue.g,
            RuntimeUIThemeColors.ShopSkyBlue.b,
            0.35f);

        Vector2 shownPosition = shopNotificationRoot.anchoredPosition;
        Vector2 hiddenPosition = shownPosition + new Vector2(420f, 0f);
        shopNotificationRoot.anchoredPosition = hiddenPosition;

        float elapsed = 0f;
        while (elapsed < shopSlideDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / shopSlideDuration);
            shopNotificationRoot.anchoredPosition = Vector2.Lerp(hiddenPosition, shownPosition, t);
            yield return null;
        }

        shopNotificationRoot.anchoredPosition = shownPosition;
        yield return new WaitForSecondsRealtime(shopNotificationDuration);

        elapsed = 0f;
        while (elapsed < shopSlideDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / shopSlideDuration);
            shopNotificationRoot.anchoredPosition = Vector2.Lerp(shownPosition, hiddenPosition, t);
            yield return null;
        }

        shopNotificationRoot.gameObject.SetActive(false);
        shopNotificationCoroutine = null;
    }
}

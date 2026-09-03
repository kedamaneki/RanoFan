using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// =============================================================================
// ゼンゼロ風・完全ブラインド生産UI / 戦歴ギャラリー / バーストフラッシュ統括
// 連携: CraftingExperimentHub / GamePhaseEventBridge
// ※ ParamA/B/C のスライダー・数値ヒント・状態エフェクトは一切出しません
// =============================================================================

/// <summary>ゼンレス風生産UIの更新用スナップショット（パラメータ値は含めない）。</summary>
public sealed class ZenlessProductionUiSnapshot
{
    public bool IsCraftingActive { get; set; }
    public CraftProfessionType Profession { get; set; }
    public bool IsSafetyZone { get; set; }
    public bool ShowAdvancedCommand { get; set; }
    public bool AdvancedCommandJustUnlocked { get; set; }
    public string ProfessionLabel { get; set; } = string.Empty;
    public string LocationLabel { get; set; } = string.Empty;
}

/// <summary>
/// 生産の完全ブラインドUI・戦歴ギャラリー・被弾バースト演出を統括します。
/// </summary>
[DefaultExecutionOrder(40)]
public partial class InGameVisualUIManager : MonoBehaviour
{
    private static readonly Color ZenlessPanelBase = new Color(0.08f, 0.09f, 0.14f, 0.94f);
    private static readonly Color ZenlessAccentCyan = new Color(0f, 0.95f, 1f, 1f);
    private static readonly Color ZenlessAccentYellow = new Color(1f, 0.92f, 0.2f, 1f);
    private static readonly Color ZenlessBurstNeonRed = new Color(1f, 0.05f, 0.25f, 0.92f);
    private static readonly Color ZenlessNeonBlue = new Color(0f, 0.83f, 1f, 1f);

    private const float UiReadabilityScale = 1.8f;

    private static int ScaledFont(int baseSize)
    {
        return Mathf.RoundToInt(baseSize * UiReadabilityScale);
    }

    public static InGameVisualUIManager Instance { get; private set; }

    [Header("演出タイミング")]
    [SerializeField] private float advancedSlideDuration = 0.22f;
    [SerializeField] private float burstFlashPeakDuration = 0.08f;
    [SerializeField] private float burstFlashFadeDuration = 0.18f;
    [SerializeField] private float parryWhiteFlashPeakDuration = 0.05f;
    [SerializeField] private float parryWhiteFlashFadeDuration = 0.12f;
    [SerializeField] private float anomalyKillPopupDuration = 1.5f;
    [SerializeField] private float craftingInterruptNoiseDuration = 0.42f;
    [SerializeField] private float productionFadeOutDuration = 0.15f;
    [SerializeField] private float locationLogDisplayDuration = 8f;
    [SerializeField] private float locationLogFadeInDuration = 0.2f;

    [SerializeField] private float demoTransitionFadeInDuration = 0.35f;
    [SerializeField] private float demoTransitionHoldDuration = 0.45f;
    [SerializeField] private float demoTransitionFadeOutDuration = 0.4f;

    [Header("ギャラリー")]
    [SerializeField] private Key galleryToggleKey = Key.Q;

    [Header("参照（未設定時は動的生成）")]
    [SerializeField] private Canvas rootCanvas;
    [SerializeField] private GameObject craftingGuideUIPanel;
    [SerializeField] private RectTransform productionPanelRoot;
    [SerializeField] private Text productionTitleText;
    [SerializeField] private Text productionSubtitleText;
    [SerializeField] private Button action1Button;
    [SerializeField] private Button action2Button;
    [SerializeField] private Button action3Button;
    [SerializeField] private RectTransform action3SlideRoot;
    [SerializeField] private Image burstFlashImage;
    [SerializeField] private RectTransform galleryPanelRoot;
    [SerializeField] private Text galleryBodyText;
    [SerializeField] private RectTransform locationLogPanelRoot;
    [SerializeField] private Text locationLogTitleText;
    [SerializeField] private Text locationLogBodyText;

    private bool wasAdvancedCommandVisible;
    private Coroutine advancedSlideCoroutine;
    private Coroutine burstFlashCoroutine;
    private Coroutine productionFadeCoroutine;
    private Coroutine locationLogCoroutine;
    private Coroutine demoTransitionCoroutine;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        EnsureUiHierarchy();
        HideProductionPanelImmediate();
        SetGalleryVisible(false);
        SetCraftingGuideVisible(false);
        SetBurstFlashAlpha(0f);
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void Update()
    {
        if (DebugHotkeyUtility.WasPressed(galleryToggleKey))
        {
            ToggleCombatMetricsGallery();
        }
    }

    /// <summary>シーンに無い場合は動的生成して返します。</summary>
    public static InGameVisualUIManager EnsureInstance()
    {
        if (Instance != null)
        {
            return Instance;
        }

        GameObject host = new GameObject(nameof(InGameVisualUIManager));
        return host.AddComponent<InGameVisualUIManager>();
    }

    /// <summary>生産フェーズ用の操作ガイド（1〜5 / Enter）パネルの表示を切り替えます。</summary>
    public void SetCraftingGuideVisible(bool visible)
    {
        EnsureUiHierarchy();

        if (craftingGuideRoot == null && craftingGuideUIPanel != null)
        {
            craftingGuideRoot = craftingGuideUIPanel.GetComponent<RectTransform>();
        }

        isCraftingGuideVisible = visible;
        ApplyHudLayoutPositions();

        if (craftingGuideRoot != null)
        {
            craftingGuideRoot.gameObject.SetActive(visible);
        }
        else if (craftingGuideUIPanel != null)
        {
            craftingGuideUIPanel.SetActive(visible);
        }

        if (visible)
        {
            HideProductionPanelImmediate();
            wasAdvancedCommandVisible = false;
        }
    }

    private bool IsCraftingGuideVisibleForLayout()
    {
        return IsCraftingGuideCurrentlyVisible();
    }

    /// <summary>
    /// CraftingExperimentHub の状態からゼンゼロ風UIを更新します（パラメータは一切表示しません）。
    /// </summary>
    public void RefreshZenlessStyleUI(CraftingExperimentHub hub, bool advancedCommandJustUnlocked = false)
    {
        if (hub == null)
        {
            HideProductionPanelImmediate();
            return;
        }

        EnsureUiHierarchy();
        ZenlessProductionUiSnapshot snapshot = BuildSnapshot(hub, advancedCommandJustUnlocked);
        ApplyProductionPanel(snapshot);
    }

    /// <summary>スナップショットでゼンゼロ風UIを更新します。</summary>
    public void RefreshZenlessStyleUI(ZenlessProductionUiSnapshot snapshot)
    {
        EnsureUiHierarchy();
        ApplyProductionPanel(snapshot ?? new ZenlessProductionUiSnapshot());
    }

    /// <summary>被弾バースト時のネオンレッド全画面フラッシュ＋生産UI即時フェードアウト。</summary>
    public void PlayBurstCancelFlash()
    {
        PlayCraftingDamageInterruptFlash();
    }

    /// <summary>工房被弾中断：激しい赤フラッシュ＋ノイズ風ちらつき。</summary>
    public void PlayCraftingDamageInterruptFlash()
    {
        EnsureUiHierarchy();

        if (burstFlashCoroutine != null)
        {
            StopCoroutine(burstFlashCoroutine);
        }

        if (craftingInterruptNoiseCoroutine != null)
        {
            StopCoroutine(craftingInterruptNoiseCoroutine);
        }

        if (productionFadeCoroutine != null)
        {
            StopCoroutine(productionFadeCoroutine);
        }

        burstFlashCoroutine = StartCoroutine(BurstFlashRoutine());
        craftingInterruptNoiseCoroutine = StartCoroutine(CraftingInterruptNoiseRoutine());
        productionFadeCoroutine = StartCoroutine(FadeOutProductionPanelRoutine());
        wasAdvancedCommandVisible = false;
    }

    /// <summary>デモ進行用：戦闘から工房へ移行するゼンゼロ風の暗転フェードを再生します。</summary>
    /// <param name="onMidFade">画面が最も暗いタイミングで呼ばれるコールバック（座標移動等）</param>
    public void PlayDemoWorkshopTransitionFade(Action onMidFade = null)
    {
        EnsureUiHierarchy();

        if (demoTransitionCoroutine != null)
        {
            StopCoroutine(demoTransitionCoroutine);
        }

        demoTransitionCoroutine = StartCoroutine(DemoWorkshopTransitionFadeRoutine(onMidFade));
    }

    /// <summary>
    /// 指定秒数（実時間）で画面を暗転させます。DemoTimeLineManager の戦闘→工房移行で使用します。
    /// </summary>
    /// <param name="durationSeconds">フェードアウトにかける秒数（Time.unscaledDeltaTime 基準）</param>
    /// <param name="peakAlpha">暗転ピーク時のオーバーレイ不透明度（0〜1）</param>
    public IEnumerator FadeToBlackRoutine(float durationSeconds, float peakAlpha = 0.97f)
    {
        EnsureUiHierarchy();

        if (burstFlashImage == null)
        {
            yield break;
        }

        burstFlashImage.gameObject.SetActive(true);
        Color darkColor = ZenlessPanelBase;
        darkColor.a = 0f;
        burstFlashImage.color = darkColor;

        float clampedDuration = Mathf.Max(0.01f, durationSeconds);
        float fadeElapsed = 0f;
        while (fadeElapsed < clampedDuration)
        {
            fadeElapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(fadeElapsed / clampedDuration);
            darkColor.a = Mathf.Lerp(0f, peakAlpha, t);
            burstFlashImage.color = darkColor;
            yield return null;
        }

        darkColor.a = peakAlpha;
        burstFlashImage.color = darkColor;
    }

    /// <summary>
    /// 暗転オーバーレイから指定秒数（実時間）で復帰します。
    /// </summary>
    /// <param name="durationSeconds">フェードインにかける秒数（Time.unscaledDeltaTime 基準）</param>
    /// <param name="startAlpha">復帰開始時のオーバーレイ不透明度</param>
    public IEnumerator FadeFromBlackRoutine(float durationSeconds, float startAlpha = 0.97f)
    {
        EnsureUiHierarchy();

        if (burstFlashImage == null)
        {
            yield break;
        }

        Color darkColor = ZenlessPanelBase;
        darkColor.a = Mathf.Clamp01(startAlpha);
        burstFlashImage.color = darkColor;
        burstFlashImage.gameObject.SetActive(true);

        float clampedDuration = Mathf.Max(0.01f, durationSeconds);
        float fadeElapsed = 0f;
        while (fadeElapsed < clampedDuration)
        {
            fadeElapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(fadeElapsed / clampedDuration);
            darkColor.a = Mathf.Lerp(startAlpha, 0f, t);
            burstFlashImage.color = darkColor;
            yield return null;
        }

        darkColor.a = 0f;
        burstFlashImage.color = darkColor;
        burstFlashImage.gameObject.SetActive(false);
    }

    /// <summary>戦歴ギャラリー（CombatMetricsGallery）の表示をトグルします。</summary>
    public void ToggleCombatMetricsGallery()
    {
        EnsureUiHierarchy();
        bool show = galleryPanelRoot == null || !galleryPanelRoot.gameObject.activeSelf;
        if (show)
        {
            RefreshGalleryContent();
        }

        SetGalleryVisible(show);
        Debug.Log(
            show
                ? "<color=#B388FF><b>[CombatMetricsGallery]</b> 戦歴ギャラリーを開きました（Q で閉じる）</color>"
                : "<color=#AAAAAA>[CombatMetricsGallery] ギャラリーを閉じました。</color>");
    }

    /// <summary>ギャラリー本文を GamePhaseEventBridge の蓄積履歴で更新します。</summary>
    public void RefreshGalleryContent()
    {
        if (galleryBodyText == null)
        {
            return;
        }

        GamePhaseEventBridge bridge = GamePhaseEventBridge.Instance
            ?? FindAnyObjectByType<GamePhaseEventBridge>();
        PlayerHistoryLog history = bridge?.runtimeHistoryLog;

        StringBuilder sb = new StringBuilder();
        sb.AppendLine("── 戦闘・行動ギャラリー（裏側集計）──");
        if (history == null)
        {
            sb.AppendLine("履歴データがありません。");
        }
        else
        {
            sb.AppendLine($"斬撃ヒット: {history.TotalSlashHits}");
            sb.AppendLine($"打撃ヒット: {history.TotalStrikeHits}");
            sb.AppendLine($"突刺ヒット: {history.TotalThrustHits}");
            sb.AppendLine($"ジャスト回避: {history.TotalPerfectEvades}");
            sb.AppendLine($"ステ割り傾向: {history.PreferredStatAllocation}");
        }

        sb.AppendLine();
        sb.AppendLine("※ 常時HUDは表示しません。Q キーでのみ確認できます。");
        galleryBodyText.text = sb.ToString();
    }

    /// <summary>
    /// 過去時代ダイブの LOCATION LOG をゼンゼロ風ネオンブルーで画面上部に表示します。
    /// </summary>
    /// <param name="archiveKey">アーカイブキー（例: 欧州魔素動乱期：若葉の拠点）</param>
    /// <param name="mode">正史 / 改変</param>
    /// <param name="log">AI が返した3行ログ</param>
    public void ShowPastStageLocationLog(string archiveKey, string mode, ChronosLocationLogData log)
    {
        if (log == null)
        {
            return;
        }

        EnsureUiHierarchy();

        if (locationLogTitleText != null)
        {
            locationLogTitleText.text =
                $"<b>LOCATION LOG</b>  <size=18>{archiveKey}</size>  <color=#9FFBFF>[{mode}]</color>";
        }

        if (locationLogBodyText != null)
        {
            locationLogBodyText.text =
                $"<color=#00D4FF>{log.Line1}</color>\n" +
                $"<color=#4DE8FF>{log.Line2}</color>\n" +
                $"<color=#9FFBFF>{log.Line3}</color>";
        }

        if (locationLogCoroutine != null)
        {
            StopCoroutine(locationLogCoroutine);
        }

        locationLogCoroutine = StartCoroutine(LocationLogDisplayRoutine());
    }

    private static ZenlessProductionUiSnapshot BuildSnapshot(
        CraftingExperimentHub hub,
        bool advancedCommandJustUnlocked)
    {
        CraftParameterLabels labels = CraftProfessionCatalog.GetLabels(hub.CurrentProfession);
        CraftFacilityLimits limits = hub.GetCurrentFacilityLimits();
        bool showAdvanced = ResolveAdvancedCommandVisibility(hub, limits);

        return new ZenlessProductionUiSnapshot
        {
            IsCraftingActive = hub.IsActive,
            Profession = hub.CurrentProfession,
            IsSafetyZone = hub.IsSafetyZone,
            ShowAdvancedCommand = showAdvanced,
            AdvancedCommandJustUnlocked = advancedCommandJustUnlocked && showAdvanced,
            ProfessionLabel = labels.ProfessionDisplayName,
            LocationLabel = hub.ResolveCraftLocationLabel()
        };
    }

    private static bool ResolveAdvancedCommandVisibility(
        CraftingExperimentHub hub,
        CraftFacilityLimits limits)
    {
        if (hub.CurrentProfession == CraftProfessionType.Alch)
        {
            return hub.HasAdvancedAttachment && hub.IsSafetyZone;
        }

        return limits.CanExecuteAdvancedAction;
    }

    private void ApplyProductionPanel(ZenlessProductionUiSnapshot snapshot)
    {
        if (!snapshot.IsCraftingActive || IsCraftingGuideVisibleForLayout())
        {
            HideProductionPanelImmediate();
            wasAdvancedCommandVisible = false;
            return;
        }

        SetProductionPanelAlpha(1f);
        productionPanelRoot.gameObject.SetActive(true);

        if (productionTitleText != null)
        {
            productionTitleText.text = $"【{snapshot.ProfessionLabel}】職人介入";
        }

        if (productionSubtitleText != null)
        {
            productionSubtitleText.text = $"{snapshot.LocationLabel} — 手応えは結果ログのみ";
        }

        SetActionButtonVisible(action1Button, true);
        SetActionButtonVisible(action2Button, true);

        bool showAdvanced = snapshot.ShowAdvancedCommand;
        if (action3Button != null)
        {
            action3Button.gameObject.SetActive(showAdvanced);
        }

        if (showAdvanced && !wasAdvancedCommandVisible)
        {
            if (snapshot.AdvancedCommandJustUnlocked)
            {
                Debug.Log(
                    "<color=#FFE566><b>【UI演出】高度術式コマンドがカチッと滑り込んできた！</b></color>");
            }

            PlayAdvancedCommandPopIn();
        }

        if (!showAdvanced && action3SlideRoot != null)
        {
            action3SlideRoot.localScale = Vector3.zero;
        }

        wasAdvancedCommandVisible = showAdvanced;
    }

    private void PlayAdvancedCommandPopIn()
    {
        if (action3SlideRoot == null)
        {
            return;
        }

        if (advancedSlideCoroutine != null)
        {
            StopCoroutine(advancedSlideCoroutine);
        }

        advancedSlideCoroutine = StartCoroutine(PopInAdvancedCommandRoutine());
    }

    private IEnumerator PopInAdvancedCommandRoutine()
    {
        action3SlideRoot.localScale = Vector3.zero;
        float elapsed = 0f;

        while (elapsed < advancedSlideDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / advancedSlideDuration);
            float eased = 1f - Mathf.Pow(1f - t, 3f);
            float overshoot = 1f + Mathf.Sin(t * Mathf.PI) * 0.08f;
            action3SlideRoot.localScale = Vector3.one * Mathf.Lerp(0f, 1f, eased) * overshoot;
            yield return null;
        }

        action3SlideRoot.localScale = Vector3.one;
        advancedSlideCoroutine = null;
    }

    private IEnumerator BurstFlashRoutine()
    {
        SetBurstFlashAlpha(0f);
        burstFlashImage.gameObject.SetActive(true);

        float peakElapsed = 0f;
        while (peakElapsed < burstFlashPeakDuration)
        {
            peakElapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(peakElapsed / burstFlashPeakDuration);
            SetBurstFlashAlpha(Mathf.Lerp(0f, 0.92f, t));
            yield return null;
        }

        float fadeElapsed = 0f;
        while (fadeElapsed < burstFlashFadeDuration)
        {
            fadeElapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(fadeElapsed / burstFlashFadeDuration);
            SetBurstFlashAlpha(Mathf.Lerp(0.92f, 0f, t));
            yield return null;
        }

        SetBurstFlashAlpha(0f);
        burstFlashImage.gameObject.SetActive(false);
        burstFlashCoroutine = null;
    }

    private IEnumerator DemoWorkshopTransitionFadeRoutine(Action onMidFade)
    {
        if (burstFlashImage == null)
        {
            onMidFade?.Invoke();
            demoTransitionCoroutine = null;
            yield break;
        }

        burstFlashImage.gameObject.SetActive(true);
        Color darkColor = ZenlessPanelBase;
        darkColor.a = 0f;
        burstFlashImage.color = darkColor;

        float fadeInElapsed = 0f;
        while (fadeInElapsed < demoTransitionFadeInDuration)
        {
            fadeInElapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(fadeInElapsed / demoTransitionFadeInDuration);
            darkColor.a = Mathf.Lerp(0f, 0.97f, t);
            burstFlashImage.color = darkColor;
            yield return null;
        }

        darkColor.a = 0.97f;
        burstFlashImage.color = darkColor;
        onMidFade?.Invoke();

        yield return new WaitForSecondsRealtime(demoTransitionHoldDuration);

        float fadeOutElapsed = 0f;
        while (fadeOutElapsed < demoTransitionFadeOutDuration)
        {
            fadeOutElapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(fadeOutElapsed / demoTransitionFadeOutDuration);
            darkColor.a = Mathf.Lerp(0.97f, 0f, t);
            burstFlashImage.color = darkColor;
            yield return null;
        }

        darkColor.a = 0f;
        burstFlashImage.color = darkColor;
        burstFlashImage.gameObject.SetActive(false);
        demoTransitionCoroutine = null;
    }

    private IEnumerator FadeOutProductionPanelRoutine()
    {
        CanvasGroup group = EnsureProductionCanvasGroup();
        float start = group.alpha;
        float elapsed = 0f;

        while (elapsed < productionFadeOutDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / productionFadeOutDuration);
            group.alpha = Mathf.Lerp(start, 0f, t);
            yield return null;
        }

        HideProductionPanelImmediate();
        productionFadeCoroutine = null;
    }

    private void HideProductionPanelImmediate()
    {
        if (productionPanelRoot != null)
        {
            productionPanelRoot.gameObject.SetActive(false);
        }

        SetProductionPanelAlpha(1f);
    }

    private void SetProductionPanelAlpha(float alpha)
    {
        CanvasGroup group = EnsureProductionCanvasGroup();
        if (group != null)
        {
            group.alpha = alpha;
        }
    }

    private CanvasGroup EnsureProductionCanvasGroup()
    {
        if (productionPanelRoot == null)
        {
            return null;
        }

        CanvasGroup group = productionPanelRoot.GetComponent<CanvasGroup>();
        if (group == null)
        {
            group = productionPanelRoot.gameObject.AddComponent<CanvasGroup>();
        }

        return group;
    }

    private void SetGalleryVisible(bool visible)
    {
        if (galleryPanelRoot != null)
        {
            galleryPanelRoot.gameObject.SetActive(visible);
        }
    }

    private void SetBurstFlashAlpha(float alpha)
    {
        if (burstFlashImage == null)
        {
            return;
        }

        Color color = ZenlessBurstNeonRed;
        color.a = alpha;
        burstFlashImage.color = color;
    }

    private static void SetActionButtonVisible(Button button, bool visible)
    {
        if (button != null)
        {
            button.gameObject.SetActive(visible);
        }
    }

    private void EnsureUiHierarchy()
    {
        EnsureEventSystem();

        if (rootCanvas == null)
        {
            GameObject canvasObject = new GameObject("ZenlessVisualCanvas", typeof(RectTransform));
            canvasObject.transform.SetParent(transform, false);
            rootCanvas = canvasObject.AddComponent<Canvas>();
            rootCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            rootCanvas.sortingOrder = 9100;
            rootCanvas.vertexColorAlwaysGammaSpace = true;

            CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            canvasObject.AddComponent<GraphicRaycaster>();
        }

        if (burstFlashImage == null)
        {
            burstFlashImage = CreateFullscreenImage("BurstFlashOverlay", rootCanvas.transform, ZenlessBurstNeonRed);
            burstFlashImage.raycastTarget = false;
            burstFlashImage.gameObject.SetActive(false);
        }

        if (productionPanelRoot == null)
        {
            BuildProductionPanel();
        }

        if (galleryPanelRoot == null)
        {
            BuildGalleryPanel();
        }

        if (locationLogPanelRoot == null)
        {
            BuildLocationLogPanel();
        }

        EnsureJuiceUiHierarchy();
    }

    private void BuildProductionPanel()
    {
        productionPanelRoot = CreateZenlessPanel(
            "ProductionVisualPanel",
            rootCanvas.transform,
            new Vector2(0.5f, 0f),
            new Vector2(0.5f, 0f),
            new Vector2(1180f, 300f),
            new Vector2(0f, 42f),
            out Image panelBg);

        panelBg.color = ZenlessPanelBase;

        GameObject accent = new GameObject("AccentStrip", typeof(RectTransform));
        accent.transform.SetParent(productionPanelRoot, false);
        RectTransform accentRect = accent.GetComponent<RectTransform>();
        accentRect.anchorMin = new Vector2(0f, 1f);
        accentRect.anchorMax = new Vector2(1f, 1f);
        accentRect.pivot = new Vector2(0.5f, 1f);
        accentRect.sizeDelta = new Vector2(0f, 6f);
        accentRect.anchoredPosition = Vector2.zero;
        Image accentImage = accent.AddComponent<Image>();
        accentImage.color = ZenlessAccentCyan;

        GameObject titleObj = new GameObject("Title", typeof(RectTransform));
        titleObj.transform.SetParent(productionPanelRoot, false);
        RectTransform titleRect = titleObj.GetComponent<RectTransform>();
        titleRect.anchorMin = new Vector2(0f, 1f);
        titleRect.anchorMax = new Vector2(1f, 1f);
        titleRect.pivot = new Vector2(0.5f, 1f);
        titleRect.sizeDelta = new Vector2(-40f, 64f);
        titleRect.anchoredPosition = new Vector2(0f, -22f);
        productionTitleText = titleObj.AddComponent<Text>();
        RuntimeUIFontHelper.ApplyTo(productionTitleText, ScaledFont(28), FontStyle.Bold, TextAnchor.MiddleLeft);
        productionTitleText.color = ZenlessAccentYellow;

        GameObject subObj = new GameObject("Subtitle", typeof(RectTransform));
        subObj.transform.SetParent(productionPanelRoot, false);
        RectTransform subRect = subObj.GetComponent<RectTransform>();
        subRect.anchorMin = new Vector2(0f, 1f);
        subRect.anchorMax = new Vector2(1f, 1f);
        subRect.pivot = new Vector2(0.5f, 1f);
        subRect.sizeDelta = new Vector2(-40f, 40f);
        subRect.anchoredPosition = new Vector2(0f, -78f);
        productionSubtitleText = subObj.AddComponent<Text>();
        RuntimeUIFontHelper.ApplyTo(productionSubtitleText, ScaledFont(18), FontStyle.Normal, TextAnchor.MiddleLeft);
        productionSubtitleText.color = new Color(0.75f, 0.8f, 0.9f, 1f);

        GameObject buttonRow = new GameObject("CommandRow", typeof(RectTransform));
        buttonRow.transform.SetParent(productionPanelRoot, false);
        RectTransform rowRect = buttonRow.GetComponent<RectTransform>();
        rowRect.anchorMin = new Vector2(0f, 0f);
        rowRect.anchorMax = new Vector2(1f, 1f);
        rowRect.offsetMin = new Vector2(20f, 20f);
        rowRect.offsetMax = new Vector2(-20f, -118f);

        HorizontalLayoutGroup layout = buttonRow.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 16f;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = true;

        action1Button = CreateZenlessCommandButton(buttonRow.transform, "強引な推進", "大槌 / 右攪拌", 1);
        action2Button = CreateZenlessCommandButton(buttonRow.transform, "繊細な調律", "小槌 / 左攪拌", 2);

        GameObject action3Host = new GameObject("AdvancedCommandHost", typeof(RectTransform));
        action3Host.transform.SetParent(buttonRow.transform, false);
        action3SlideRoot = action3Host.GetComponent<RectTransform>();
        LayoutElement layoutElement = action3Host.AddComponent<LayoutElement>();
        layoutElement.flexibleWidth = 1f;
        layoutElement.minHeight = 108f;

        action3Button = CreateZenlessCommandButton(action3SlideRoot, "高度術式干渉", "触媒 / 遠心分離", 3);
        action3Button.gameObject.SetActive(false);
        action3SlideRoot.localScale = Vector3.zero;

        productionPanelRoot.gameObject.SetActive(false);
    }

    private void BuildGalleryPanel()
    {
        galleryPanelRoot = CreateZenlessPanel(
            "CombatMetricsGallery",
            rootCanvas.transform,
            new Vector2(1f, 0.5f),
            new Vector2(1f, 0.5f),
            new Vector2(420f, 360f),
            new Vector2(-24f, 0f),
            out Image galleryBg);

        galleryBg.color = new Color(0.05f, 0.06f, 0.1f, 0.92f);

        GameObject bodyObj = new GameObject("Body", typeof(RectTransform));
        bodyObj.transform.SetParent(galleryPanelRoot, false);
        RectTransform bodyRect = bodyObj.GetComponent<RectTransform>();
        bodyRect.anchorMin = Vector2.zero;
        bodyRect.anchorMax = Vector2.one;
        bodyRect.offsetMin = new Vector2(20f, 20f);
        bodyRect.offsetMax = new Vector2(-20f, -20f);

        galleryBodyText = bodyObj.AddComponent<Text>();
        RuntimeUIFontHelper.ApplyTo(galleryBodyText, ScaledFont(20), FontStyle.Normal, TextAnchor.UpperLeft);
        galleryBodyText.color = new Color(0.85f, 0.9f, 1f, 1f);
        galleryBodyText.lineSpacing = 1.1f;

        galleryPanelRoot.gameObject.SetActive(false);
    }

    private void BuildLocationLogPanel()
    {
        locationLogPanelRoot = CreateZenlessPanel(
            "PastStageLocationLogPanel",
            rootCanvas.transform,
            new Vector2(0.5f, 1f),
            new Vector2(0.5f, 1f),
            new Vector2(880f, 220f),
            new Vector2(0f, -28f),
            out Image panelBg);

        panelBg.color = new Color(0.04f, 0.07f, 0.14f, 0.94f);

        Outline panelOutline = locationLogPanelRoot.GetComponent<Outline>();
        if (panelOutline != null)
        {
            panelOutline.effectColor = ZenlessNeonBlue;
        }

        GameObject accent = new GameObject("NeonAccent", typeof(RectTransform));
        accent.transform.SetParent(locationLogPanelRoot, false);
        RectTransform accentRect = accent.GetComponent<RectTransform>();
        accentRect.anchorMin = new Vector2(0f, 1f);
        accentRect.anchorMax = new Vector2(1f, 1f);
        accentRect.pivot = new Vector2(0.5f, 1f);
        accentRect.sizeDelta = new Vector2(0f, 5f);
        accentRect.anchoredPosition = Vector2.zero;
        Image accentImage = accent.AddComponent<Image>();
        accentImage.color = ZenlessNeonBlue;

        GameObject titleObj = new GameObject("Title", typeof(RectTransform));
        titleObj.transform.SetParent(locationLogPanelRoot, false);
        RectTransform titleRect = titleObj.GetComponent<RectTransform>();
        titleRect.anchorMin = new Vector2(0f, 1f);
        titleRect.anchorMax = new Vector2(1f, 1f);
        titleRect.pivot = new Vector2(0.5f, 1f);
        titleRect.sizeDelta = new Vector2(-32f, 44f);
        titleRect.anchoredPosition = new Vector2(0f, -12f);

        locationLogTitleText = titleObj.AddComponent<Text>();
        RuntimeUIFontHelper.ApplyTo(locationLogTitleText, ScaledFont(22), FontStyle.Bold, TextAnchor.UpperLeft);
        locationLogTitleText.color = ZenlessNeonBlue;
        locationLogTitleText.supportRichText = true;

        GameObject bodyObj = new GameObject("Body", typeof(RectTransform));
        bodyObj.transform.SetParent(locationLogPanelRoot, false);
        RectTransform bodyRect = bodyObj.GetComponent<RectTransform>();
        bodyRect.anchorMin = Vector2.zero;
        bodyRect.anchorMax = Vector2.one;
        bodyRect.offsetMin = new Vector2(24f, 20f);
        bodyRect.offsetMax = new Vector2(-24f, -58f);

        locationLogBodyText = bodyObj.AddComponent<Text>();
        RuntimeUIFontHelper.ApplyTo(locationLogBodyText, ScaledFont(24), FontStyle.Bold, TextAnchor.UpperLeft);
        locationLogBodyText.color = Color.white;
        locationLogBodyText.lineSpacing = 1.15f;
        locationLogBodyText.supportRichText = true;

        locationLogPanelRoot.gameObject.SetActive(false);
    }

    private IEnumerator LocationLogDisplayRoutine()
    {
        CanvasGroup group = EnsureLocationLogCanvasGroup();
        locationLogPanelRoot.gameObject.SetActive(true);

        float fadeElapsed = 0f;
        while (fadeElapsed < locationLogFadeInDuration)
        {
            fadeElapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(fadeElapsed / locationLogFadeInDuration);
            group.alpha = Mathf.Lerp(0f, 1f, t);
            yield return null;
        }

        group.alpha = 1f;
        yield return new WaitForSecondsRealtime(locationLogDisplayDuration);

        float fadeOutElapsed = 0f;
        const float fadeOutDuration = 0.35f;
        while (fadeOutElapsed < fadeOutDuration)
        {
            fadeOutElapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(fadeOutElapsed / fadeOutDuration);
            group.alpha = Mathf.Lerp(1f, 0f, t);
            yield return null;
        }

        locationLogPanelRoot.gameObject.SetActive(false);
        group.alpha = 1f;
        locationLogCoroutine = null;
    }

    private CanvasGroup EnsureLocationLogCanvasGroup()
    {
        if (locationLogPanelRoot == null)
        {
            return null;
        }

        CanvasGroup group = locationLogPanelRoot.GetComponent<CanvasGroup>();
        if (group == null)
        {
            group = locationLogPanelRoot.gameObject.AddComponent<CanvasGroup>();
        }

        return group;
    }

    private Button CreateZenlessCommandButton(Transform parent, string label, string subLabel, int actionType)
    {
        GameObject buttonObject = new GameObject($"Command_{actionType}", typeof(RectTransform));
        buttonObject.transform.SetParent(parent, false);

        LayoutElement element = buttonObject.AddComponent<LayoutElement>();
        element.flexibleWidth = 1f;
        element.minHeight = 108f;

        Image bg = buttonObject.AddComponent<Image>();
        bg.color = new Color(0.12f, 0.14f, 0.22f, 1f);

        GameObject textBackdrop = new GameObject("TextBackdrop", typeof(RectTransform));
        textBackdrop.transform.SetParent(buttonObject.transform, false);
        RectTransform backdropRect = textBackdrop.GetComponent<RectTransform>();
        backdropRect.anchorMin = Vector2.zero;
        backdropRect.anchorMax = Vector2.one;
        backdropRect.offsetMin = new Vector2(8f, 8f);
        backdropRect.offsetMax = new Vector2(-8f, -8f);
        Image backdropImage = textBackdrop.AddComponent<Image>();
        backdropImage.color = new Color(0f, 0f, 0f, 0.62f);
        backdropImage.raycastTarget = false;

        Button button = buttonObject.AddComponent<Button>();
        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1.2f, 1.2f, 1.2f, 1f);
        colors.pressedColor = new Color(0.85f, 0.85f, 0.85f, 1f);
        button.colors = colors;
        button.targetGraphic = bg;

        Outline outline = buttonObject.AddComponent<Outline>();
        outline.effectColor = ZenlessAccentCyan;
        outline.effectDistance = new Vector2(2f, -2f);

        GameObject textObj = new GameObject("Label", typeof(RectTransform));
        textObj.transform.SetParent(buttonObject.transform, false);
        RectTransform textRect = textObj.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        Text text = textObj.AddComponent<Text>();
        RuntimeUIFontHelper.ApplyTo(text, ScaledFont(20), FontStyle.Bold, TextAnchor.MiddleCenter);
        text.color = Color.white;
        text.text = $"{label}\n<size={ScaledFont(16)}>{subLabel}</size>";
        text.supportRichText = true;

        int captured = actionType;
        button.onClick.AddListener(() => OnProductionCommandClicked(captured));
        return button;
    }

    private void OnProductionCommandClicked(int actionType)
    {
        CraftingExperimentHub hub = CraftingExperimentHub.Instance
            ?? CraftingExperimentHub.EnsureInstance();
        if (hub == null || !hub.IsActive)
        {
            return;
        }

        hub.ExecuteCraftAction(actionType);
        RefreshZenlessStyleUI(hub);
    }

    private static RectTransform CreateZenlessPanel(
        string name,
        Transform parent,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Vector2 sizeDelta,
        Vector2 anchoredPosition,
        out Image background)
    {
        GameObject panelObject = new GameObject(name, typeof(RectTransform));
        panelObject.transform.SetParent(parent, false);

        RectTransform rect = panelObject.GetComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = anchorMin;
        rect.sizeDelta = sizeDelta;
        rect.anchoredPosition = anchoredPosition;

        background = panelObject.AddComponent<Image>();
        background.color = ZenlessPanelBase;

        Outline outline = panelObject.AddComponent<Outline>();
        outline.effectColor = ZenlessAccentYellow;
        outline.effectDistance = new Vector2(3f, -3f);

        return rect;
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
}

/// <summary>Play 開始時に InGameVisualUIManager を自動配置します。</summary>
public static class InGameVisualUiBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureManager()
    {
        InGameVisualUIManager.EnsureInstance();
    }
}

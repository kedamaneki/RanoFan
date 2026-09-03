using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>デモ向けジュース演出・フェーズ帯・操作ガイド UI（partial）。</summary>
public partial class InGameVisualUIManager
{
    private static readonly Color ParryWhiteFlashColor = new Color(1f, 1f, 1f, 0.94f);
    private static readonly Color AnomalyKillAccent = new Color(1f, 0.55f, 0.05f, 1f);
    private static readonly Color BattlePhaseBandColor = new Color(0.95f, 0.22f, 0.18f, 0.88f);
    private static readonly Color CraftingPhaseBandColor = new Color(0.08f, 0.62f, 0.78f, 0.88f);
    private static readonly Color HarassmentWarningColor = new Color(1f, 0.12f, 0.18f, 0.75f);

    private Image whiteFlashImage;
    private Image harassmentEdgeImage;
    private RectTransform phaseBannerRoot;
    private Text phaseBannerText;
    private RectTransform craftingGuideRoot;
    private Text craftingGuideTitleText;
    private Text craftingGuideBodyText;
    private RectTransform playerHudBumpTarget;
    private bool isCraftingGuideVisible;

    private const float CraftingGuideBottomOffset = 24f;
    private const float CraftingGuideHeight = 248f;
    private const float PhaseBannerTopOffset = 28f;
    private const float PhaseBannerHeight = 84f;

    private Coroutine whiteFlashCoroutine;
    private Coroutine anomalyKillPopupCoroutine;
    private Coroutine craftingInterruptNoiseCoroutine;
    private Coroutine harassmentPulseCoroutine;
    private Coroutine playerHudBumpCoroutine;

    /// <summary>ジャストパリィ成功：白フラッシュ＋プレイヤーHUDバンプ。</summary>
    public void PlayParrySuccessJuice()
    {
        EnsureUiHierarchy();

        if (whiteFlashCoroutine != null)
        {
            StopCoroutine(whiteFlashCoroutine);
        }

        whiteFlashCoroutine = StartCoroutine(ParryWhiteFlashRoutine());
        PlayPlayerHudBump();
    }

    /// <summary>ANOMALY KILL 大文字ポップアップ（暗転と同時）。</summary>
    public void PlayAnomalyKillPopup(float durationSeconds = -1f)
    {
        EnsureUiHierarchy();

        if (anomalyKillPopupCoroutine != null)
        {
            StopCoroutine(anomalyKillPopupCoroutine);
        }

        float duration = durationSeconds > 0f ? durationSeconds : anomalyKillPopupDuration;
        anomalyKillPopupCoroutine = StartCoroutine(AnomalyKillPopupRoutine(duration));
    }

    /// <summary>デモフェーズ帯（画面下部中央）を更新します。</summary>
    public void UpdateDemoPhaseBanner(DemoState state)
    {
        EnsureUiHierarchy();

        switch (state)
        {
            case DemoState.BattlePhase:
                ShowPhaseBanner("【 BATTLE PHASE 】", BattlePhaseBandColor);
                break;
            case DemoState.CraftingPhase:
                ShowPhaseBanner("【 CRAFTING PHASE 】", CraftingPhaseBandColor);
                break;
            default:
                HidePhaseBanner();
                break;
        }
    }

    /// <summary>工房ハラスメント再開の警告パルス。</summary>
    public void PlayHarassmentThreatPulse(bool attackEnabled)
    {
        EnsureUiHierarchy();

        if (harassmentPulseCoroutine != null)
        {
            StopCoroutine(harassmentPulseCoroutine);
        }

        harassmentPulseCoroutine = StartCoroutine(HarassmentThreatPulseRoutine(attackEnabled));
    }

    private void EnsureJuiceUiHierarchy()
    {
        if (whiteFlashImage == null)
        {
            whiteFlashImage = CreateFullscreenImage("ParryWhiteFlashOverlay", rootCanvas.transform, ParryWhiteFlashColor);
            whiteFlashImage.raycastTarget = false;
            whiteFlashImage.gameObject.SetActive(false);
        }

        if (harassmentEdgeImage == null)
        {
            harassmentEdgeImage = CreateFullscreenImage("HarassmentEdgePulse", rootCanvas.transform, HarassmentWarningColor);
            harassmentEdgeImage.raycastTarget = false;
            harassmentEdgeImage.gameObject.SetActive(false);
        }

        if (phaseBannerRoot == null)
        {
            BuildPhaseBannerPanel();
        }

        if (craftingGuideRoot == null && craftingGuideUIPanel == null)
        {
            BuildCraftingGuidePanel();
        }
        else if (craftingGuideRoot == null && craftingGuideUIPanel != null)
        {
            craftingGuideRoot = craftingGuideUIPanel.GetComponent<RectTransform>();
        }

        ApplyHudLayoutPositions();
    }

    private void ApplyHudLayoutPositions()
    {
        if (phaseBannerRoot != null)
        {
            phaseBannerRoot.anchorMin = new Vector2(0.5f, 1f);
            phaseBannerRoot.anchorMax = new Vector2(0.5f, 1f);
            phaseBannerRoot.pivot = new Vector2(0.5f, 1f);
            phaseBannerRoot.sizeDelta = new Vector2(1000f, PhaseBannerHeight);
            phaseBannerRoot.anchoredPosition = new Vector2(0f, -PhaseBannerTopOffset);
        }

        if (craftingGuideRoot != null)
        {
            craftingGuideRoot.anchorMin = new Vector2(0.5f, 0f);
            craftingGuideRoot.anchorMax = new Vector2(0.5f, 0f);
            craftingGuideRoot.pivot = new Vector2(0.5f, 0f);
            craftingGuideRoot.sizeDelta = new Vector2(1240f, CraftingGuideHeight);
            craftingGuideRoot.anchoredPosition = new Vector2(0f, CraftingGuideBottomOffset);
        }

        if (productionPanelRoot != null)
        {
            productionPanelRoot.anchorMin = new Vector2(0.5f, 0f);
            productionPanelRoot.anchorMax = new Vector2(0.5f, 0f);
            productionPanelRoot.pivot = new Vector2(0.5f, 0f);
            productionPanelRoot.sizeDelta = new Vector2(1180f, 300f);
            productionPanelRoot.anchoredPosition = new Vector2(0f, 42f);
        }
    }

    private bool IsCraftingGuideCurrentlyVisible()
    {
        if (isCraftingGuideVisible)
        {
            return true;
        }

        if (craftingGuideRoot != null && craftingGuideRoot.gameObject.activeSelf)
        {
            return true;
        }

        return craftingGuideUIPanel != null && craftingGuideUIPanel.activeSelf;
    }

    private void BuildPhaseBannerPanel()
    {
        phaseBannerRoot = CreateZenlessPanel(
            "DemoPhaseBanner",
            rootCanvas.transform,
            new Vector2(0.5f, 1f),
            new Vector2(0.5f, 1f),
            new Vector2(1000f, PhaseBannerHeight),
            new Vector2(0f, -PhaseBannerTopOffset),
            out Image bandBg);

        bandBg.color = BattlePhaseBandColor;

        GameObject textObj = new GameObject("PhaseLabel", typeof(RectTransform));
        textObj.transform.SetParent(phaseBannerRoot, false);
        RectTransform textRect = textObj.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        phaseBannerText = textObj.AddComponent<Text>();
        RuntimeUIFontHelper.ApplyTo(phaseBannerText, ScaledFont(30), FontStyle.Bold, TextAnchor.MiddleCenter);
        phaseBannerText.color = Color.white;
        phaseBannerText.text = "【 BATTLE PHASE 】";

        Outline outline = textObj.AddComponent<Outline>();
        outline.effectColor = new Color(0f, 0f, 0f, 0.85f);
        outline.effectDistance = new Vector2(3f, -3f);

        phaseBannerRoot.gameObject.SetActive(false);
    }

    private void BuildCraftingGuidePanel()
    {
        craftingGuideRoot = CreateZenlessPanel(
            "CraftingGuidePanel",
            rootCanvas.transform,
            new Vector2(0.5f, 0f),
            new Vector2(0.5f, 0f),
            new Vector2(1240f, CraftingGuideHeight),
            new Vector2(0f, CraftingGuideBottomOffset),
            out Image panelBg);

        panelBg.color = new Color(0.02f, 0.04f, 0.08f, 0.82f);
        craftingGuideUIPanel = craftingGuideRoot.gameObject;

        GameObject titleObj = new GameObject("GuideTitle", typeof(RectTransform));
        titleObj.transform.SetParent(craftingGuideRoot, false);
        RectTransform titleRect = titleObj.GetComponent<RectTransform>();
        titleRect.anchorMin = new Vector2(0f, 1f);
        titleRect.anchorMax = new Vector2(1f, 1f);
        titleRect.pivot = new Vector2(0.5f, 1f);
        titleRect.sizeDelta = new Vector2(-32f, 56f);
        titleRect.anchoredPosition = new Vector2(0f, -10f);

        craftingGuideTitleText = titleObj.AddComponent<Text>();
        RuntimeUIFontHelper.ApplyTo(craftingGuideTitleText, ScaledFont(24), FontStyle.Bold, TextAnchor.MiddleCenter);
        craftingGuideTitleText.color = ZenlessAccentCyan;
        craftingGuideTitleText.text = "【 こだわり工程 · ブラインド操作 】";

        GameObject slotRow = new GameObject("SlotHints", typeof(RectTransform));
        slotRow.transform.SetParent(craftingGuideRoot, false);
        RectTransform slotRect = slotRow.GetComponent<RectTransform>();
        slotRect.anchorMin = new Vector2(0f, 0f);
        slotRect.anchorMax = new Vector2(1f, 1f);
        slotRect.offsetMin = new Vector2(18f, 58f);
        slotRect.offsetMax = new Vector2(-18f, -64f);

        HorizontalLayoutGroup layout = slotRow.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 12f;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = true;

        string[] slotLabels =
        {
            "1\n精製",
            "2\n鍛錬",
            "3\n冷却",
            "4\n研磨",
            "5\n仕上げ"
        };

        for (int i = 0; i < slotLabels.Length; i++)
        {
            CreateCraftingGuideSlot(slotRow.transform, slotLabels[i]);
        }

        GameObject enterObj = new GameObject("EnterHint", typeof(RectTransform));
        enterObj.transform.SetParent(craftingGuideRoot, false);
        RectTransform enterRect = enterObj.GetComponent<RectTransform>();
        enterRect.anchorMin = new Vector2(0f, 0f);
        enterRect.anchorMax = new Vector2(1f, 0f);
        enterRect.pivot = new Vector2(0.5f, 0f);
        enterRect.sizeDelta = new Vector2(-24f, 48f);
        enterRect.anchoredPosition = new Vector2(0f, 10f);

        Image enterBackdrop = enterObj.AddComponent<Image>();
        enterBackdrop.color = new Color(0f, 0f, 0f, 0.68f);

        GameObject enterTextObj = new GameObject("Label", typeof(RectTransform));
        enterTextObj.transform.SetParent(enterObj.transform, false);
        RectTransform enterTextRect = enterTextObj.GetComponent<RectTransform>();
        enterTextRect.anchorMin = Vector2.zero;
        enterTextRect.anchorMax = Vector2.one;
        enterTextRect.offsetMin = Vector2.zero;
        enterTextRect.offsetMax = Vector2.zero;

        craftingGuideBodyText = enterTextObj.AddComponent<Text>();
        RuntimeUIFontHelper.ApplyTo(craftingGuideBodyText, ScaledFont(20), FontStyle.Bold, TextAnchor.MiddleCenter);
        craftingGuideBodyText.color = ZenlessAccentYellow;
        craftingGuideBodyText.text = "Enter … 品質ジャッジ＆パケット回収";

        craftingGuideRoot.gameObject.SetActive(false);
    }

    private void CreateCraftingGuideSlot(Transform parent, string label)
    {
        GameObject slotObject = new GameObject($"Slot_{label[0]}", typeof(RectTransform));
        slotObject.transform.SetParent(parent, false);

        LayoutElement element = slotObject.AddComponent<LayoutElement>();
        element.flexibleWidth = 1f;
        element.minHeight = 108f;

        Image backdrop = slotObject.AddComponent<Image>();
        backdrop.color = new Color(0f, 0f, 0f, 0.68f);

        Outline outline = slotObject.AddComponent<Outline>();
        outline.effectColor = ZenlessAccentCyan;
        outline.effectDistance = new Vector2(2f, -2f);

        GameObject textObj = new GameObject("Label", typeof(RectTransform));
        textObj.transform.SetParent(slotObject.transform, false);
        RectTransform textRect = textObj.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(6f, 6f);
        textRect.offsetMax = new Vector2(-6f, -6f);

        Text text = textObj.AddComponent<Text>();
        RuntimeUIFontHelper.ApplyTo(text, ScaledFont(18), FontStyle.Bold, TextAnchor.MiddleCenter);
        text.color = Color.white;
        text.text = label;
    }

    private void ShowPhaseBanner(string label, Color bandColor)
    {
        if (phaseBannerRoot == null || phaseBannerText == null)
        {
            return;
        }

        Image bandBg = phaseBannerRoot.GetComponent<Image>();
        if (bandBg != null)
        {
            bandBg.color = bandColor;
        }

        phaseBannerText.text = label;
        phaseBannerRoot.gameObject.SetActive(true);
        phaseBannerRoot.localScale = Vector3.one;
    }

    private void HidePhaseBanner()
    {
        if (phaseBannerRoot != null)
        {
            phaseBannerRoot.gameObject.SetActive(false);
        }
    }

    private void PlayPlayerHudBump()
    {
        if (playerHudBumpCoroutine != null)
        {
            StopCoroutine(playerHudBumpCoroutine);
        }

        playerHudBumpCoroutine = StartCoroutine(PlayerHudBumpRoutine());
    }

    private IEnumerator ParryWhiteFlashRoutine()
    {
        if (whiteFlashImage == null)
        {
            yield break;
        }

        whiteFlashImage.gameObject.SetActive(true);
        Color flashColor = ParryWhiteFlashColor;

        float peakElapsed = 0f;
        while (peakElapsed < parryWhiteFlashPeakDuration)
        {
            peakElapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(peakElapsed / Mathf.Max(0.01f, parryWhiteFlashPeakDuration));
            flashColor.a = Mathf.Lerp(0f, 0.94f, t);
            whiteFlashImage.color = flashColor;
            yield return null;
        }

        float fadeElapsed = 0f;
        while (fadeElapsed < parryWhiteFlashFadeDuration)
        {
            fadeElapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(fadeElapsed / Mathf.Max(0.01f, parryWhiteFlashFadeDuration));
            flashColor.a = Mathf.Lerp(0.94f, 0f, t);
            whiteFlashImage.color = flashColor;
            yield return null;
        }

        flashColor.a = 0f;
        whiteFlashImage.color = flashColor;
        whiteFlashImage.gameObject.SetActive(false);
        whiteFlashCoroutine = null;
    }

    private IEnumerator AnomalyKillPopupRoutine(float durationSeconds)
    {
        GameObject popupRoot = new GameObject("AnomalyKillPopup", typeof(RectTransform));
        popupRoot.transform.SetParent(rootCanvas.transform, false);

        RectTransform popupRect = popupRoot.GetComponent<RectTransform>();
        popupRect.anchorMin = new Vector2(0.5f, 0.5f);
        popupRect.anchorMax = new Vector2(0.5f, 0.5f);
        popupRect.pivot = new Vector2(0.5f, 0.5f);
        popupRect.sizeDelta = new Vector2(1280f, 220f);
        popupRect.anchoredPosition = Vector2.zero;

        Image backdrop = popupRoot.AddComponent<Image>();
        backdrop.color = new Color(0f, 0f, 0f, 0.55f);
        backdrop.raycastTarget = false;

        GameObject textObj = new GameObject("Label", typeof(RectTransform));
        textObj.transform.SetParent(popupRoot.transform, false);
        RectTransform textRect = textObj.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        Text popupText = textObj.AddComponent<Text>();
        RuntimeUIFontHelper.ApplyTo(popupText, ScaledFont(54), FontStyle.Bold, TextAnchor.MiddleCenter);
        popupText.color = AnomalyKillAccent;
        popupText.text = "ANOMALY KILL";

        Outline outline = textObj.AddComponent<Outline>();
        outline.effectColor = Color.black;
        outline.effectDistance = new Vector2(4f, -4f);

        Shadow shadow = textObj.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.85f);
        shadow.effectDistance = new Vector2(6f, -6f);

        float elapsed = 0f;
        float clampedDuration = Mathf.Max(0.2f, durationSeconds);
        Vector3 startScale = Vector3.one * 0.72f;
        Vector3 peakScale = Vector3.one * 1.08f;
        popupRoot.transform.localScale = startScale;

        while (elapsed < clampedDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / clampedDuration);
            float pop = t < 0.18f
                ? Mathf.SmoothStep(0f, 1f, t / 0.18f)
                : 1f - Mathf.SmoothStep(0f, 1f, (t - 0.18f) / 0.82f);
            popupRoot.transform.localScale = Vector3.LerpUnclamped(startScale, peakScale, pop);

            Color textColor = popupText.color;
            textColor.a = t > 0.82f ? Mathf.Lerp(1f, 0f, (t - 0.82f) / 0.18f) : 1f;
            popupText.color = textColor;

            Color backdropColor = backdrop.color;
            backdropColor.a = textColor.a * 0.55f;
            backdrop.color = backdropColor;

            yield return null;
        }

        Destroy(popupRoot);
        anomalyKillPopupCoroutine = null;
    }

    private IEnumerator CraftingInterruptNoiseRoutine()
    {
        if (burstFlashImage == null)
        {
            yield break;
        }

        float elapsed = 0f;
        while (elapsed < craftingInterruptNoiseDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float flicker = UnityEngine.Random.Range(0.55f, 1f);
            Color noisyRed = ZenlessBurstNeonRed;
            noisyRed.a = flicker * 0.88f;
            burstFlashImage.color = noisyRed;
            burstFlashImage.gameObject.SetActive(true);
            yield return null;
        }

        craftingInterruptNoiseCoroutine = null;
    }

    private IEnumerator HarassmentThreatPulseRoutine(bool attackEnabled)
    {
        if (harassmentEdgeImage == null)
        {
            yield break;
        }

        harassmentEdgeImage.gameObject.SetActive(true);
        Color pulseColor = HarassmentWarningColor;
        float duration = attackEnabled ? 0.42f : 0.28f;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float wave = Mathf.Sin(t * Mathf.PI * 4f) * (1f - t);
            pulseColor.a = (attackEnabled ? 0.82f : 0.55f) * wave;
            harassmentEdgeImage.color = pulseColor;
            yield return null;
        }

        pulseColor.a = 0f;
        harassmentEdgeImage.color = pulseColor;
        harassmentEdgeImage.gameObject.SetActive(false);
        harassmentPulseCoroutine = null;
    }

    private IEnumerator PlayerHudBumpRoutine()
    {
        RectTransform target = ResolvePlayerHudBumpTarget();
        if (target == null)
        {
            yield break;
        }

        Vector3 baseScale = Vector3.one;
        Vector3 peakScale = Vector3.one * 1.22f;
        float duration = 0.2f;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float eased = 1f - Mathf.Pow(1f - t, 3f);
            target.localScale = Vector3.LerpUnclamped(peakScale, baseScale, eased);
            yield return null;
        }

        target.localScale = baseScale;
        playerHudBumpCoroutine = null;
    }

    private RectTransform ResolvePlayerHudBumpTarget()
    {
        if (playerHudBumpTarget != null)
        {
            return playerHudBumpTarget;
        }

        PlayerStatsUI statsUi = FindAnyObjectByType<PlayerStatsUI>();
        if (statsUi != null)
        {
            playerHudBumpTarget = statsUi.GetComponent<RectTransform>();
            if (playerHudBumpTarget == null)
            {
                playerHudBumpTarget = statsUi.transform as RectTransform;
            }
        }

        return playerHudBumpTarget;
    }
}

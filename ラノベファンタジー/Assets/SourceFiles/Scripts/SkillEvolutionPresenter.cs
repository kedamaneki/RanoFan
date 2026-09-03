using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

// =============================================================================
// スキル全体進化のログ通知 + 全画面カットイン演出
// 連携: SkillEvolutionLinker / SkillMasterRepository / InGameVisualUIManager / DemoInputGate
// =============================================================================

/// <summary>
/// SkillEvolutionLinker.SkillEvolved を購読し、進化ログとカットイン演出を表示します。
/// 演出中は DemoInputGate 経由でプレイヤー入力を一時遮断します。
/// </summary>
[DefaultExecutionOrder(45)]
public class SkillEvolutionPresenter : MonoBehaviour
{
    private const int MaxBattleLogLines = 8;
    private static readonly Color CutInGold = new Color(1f, 0.84f, 0.2f, 1f);
    private static readonly Color CutInBackdrop = new Color(0.02f, 0.04f, 0.1f, 0.88f);

    public static SkillEvolutionPresenter Instance { get; private set; }

    /// <summary>カットイン演出中は true。DemoInputGate が参照します。</summary>
    public static bool IsPerformanceActive { get; private set; }

    [Header("参照（未設定時は自動生成）")]
    [SerializeField] private Canvas rootCanvas;
    [SerializeField] private RectTransform battleLogPanelRoot;
    [SerializeField] private Text battleLogText;
    [SerializeField] private CanvasGroup battleLogCanvasGroup;
    [SerializeField] private RectTransform cutInPanelRoot;
    [SerializeField] private CanvasGroup cutInCanvasGroup;
    [SerializeField] private Image cutInBackdropImage;
    [SerializeField] private Text cutInTitleText;
    [SerializeField] private Text cutInOldSkillText;
    [SerializeField] private Text cutInNewSkillText;
    [SerializeField] private Text cutInDescriptionText;
    [SerializeField] private Image cutInIconImage;

    [Header("参照（任意）")]
    [SerializeField] private SkillMasterRepository skillMasterRepository;

    [Header("演出タイミング（実時間）")]
    [SerializeField] private float battleLogFadeInDuration = 0.18f;
    [SerializeField] private float cutInFadeInDuration = 0.35f;
    [SerializeField] private float cutInHoldDuration = 2.2f;
    [SerializeField] private float cutInFadeOutDuration = 0.4f;
    [SerializeField] private float cutInPunchDuration = 0.24f;

    private readonly Queue<EvolutionPerformanceRequest> pendingRequests =
        new Queue<EvolutionPerformanceRequest>();

    private readonly List<string> battleLogLines = new List<string>();
    private Coroutine performanceQueueCoroutine;
    private bool isSubscribed;

    private struct EvolutionPerformanceRequest
    {
        public string OldSkillId;
        public string NewSkillId;
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        InGameVisualUIManager.EnsureInstance();
        EnsureUiHierarchy();
        HideCutInImmediate();
        ResolveRepository();
    }

    private void OnEnable()
    {
        SubscribeEvolutionEvent();
    }

    private void Start()
    {
        SubscribeEvolutionEvent();
    }

    private void OnDisable()
    {
        UnsubscribeEvolutionEvent();
        StopAllPerformanceCoroutines();
        IsPerformanceActive = false;
    }

    private void OnDestroy()
    {
        UnsubscribeEvolutionEvent();
        StopAllPerformanceCoroutines();
        IsPerformanceActive = false;

        if (Instance == this)
        {
            Instance = null;
        }
    }

    /// <summary>シーンに無い場合は動的生成して返します。</summary>
    public static SkillEvolutionPresenter EnsureInstance()
    {
        if (Instance != null)
        {
            return Instance;
        }

        GameObject hub = GameObject.Find("DebugSystemsHub");
        if (hub != null && hub.GetComponent<SkillEvolutionPresenter>() != null)
        {
            return hub.GetComponent<SkillEvolutionPresenter>();
        }

        GameObject host = hub != null
            ? hub
            : new GameObject(nameof(SkillEvolutionPresenter));

        SkillEvolutionPresenter presenter = host.GetComponent<SkillEvolutionPresenter>();
        if (presenter == null)
        {
            presenter = host.AddComponent<SkillEvolutionPresenter>();
        }

        return presenter;
    }

    /// <summary>
    /// 進化成功時の演出を手動実行します（イベント経由と同じ処理）。
    /// </summary>
    public void ExecuteEvolutionPerformance(string oldSkillId, string newSkillId)
    {
        if (string.IsNullOrWhiteSpace(oldSkillId) || string.IsNullOrWhiteSpace(newSkillId))
        {
            Debug.LogWarning("[SkillEvolutionPresenter] 進化演出をスキップ: skillId が空です。");
            return;
        }

        pendingRequests.Enqueue(new EvolutionPerformanceRequest
        {
            OldSkillId = oldSkillId,
            NewSkillId = newSkillId
        });

        if (performanceQueueCoroutine == null)
        {
            performanceQueueCoroutine = StartCoroutine(ProcessPerformanceQueueRoutine());
        }
    }

    private void SubscribeEvolutionEvent()
    {
        if (isSubscribed)
        {
            return;
        }

        SkillEvolutionLinker.SkillEvolved += HandleSkillEvolved;
        isSubscribed = true;
    }

    private void UnsubscribeEvolutionEvent()
    {
        if (!isSubscribed)
        {
            return;
        }

        SkillEvolutionLinker.SkillEvolved -= HandleSkillEvolved;
        isSubscribed = false;
    }

    private void HandleSkillEvolved(string oldSkillId, string newSkillId)
    {
        ExecuteEvolutionPerformance(oldSkillId, newSkillId);
    }

    private IEnumerator ProcessPerformanceQueueRoutine()
    {
        while (pendingRequests.Count > 0)
        {
            EvolutionPerformanceRequest request = pendingRequests.Dequeue();
            yield return ExecuteEvolutionPerformanceRoutine(request.OldSkillId, request.NewSkillId);
        }

        performanceQueueCoroutine = null;
    }

    /// <summary>
    /// 演出フェーズ本体: ログ通知 → カットイン開始 → ホールド → フェードアウト。
    /// </summary>
    private IEnumerator ExecuteEvolutionPerformanceRoutine(string oldSkillId, string newSkillId)
    {
        if (!isActiveAndEnabled)
        {
            yield break;
        }

        EnsureUiHierarchy();
        ResolveRepository();

        if (!TryResolveSkillDisplay(oldSkillId, out string oldName, out _))
        {
            Debug.LogError($"[SkillEvolutionPresenter] 旧スキル名の解決に失敗: {oldSkillId}");
            oldName = oldSkillId;
        }

        if (!TryResolveSkillDisplay(newSkillId, out string newName, out string newDescription))
        {
            Debug.LogError($"[SkillEvolutionPresenter] 新スキル名の解決に失敗: {newSkillId}");
            newName = newSkillId;
            newDescription = string.Empty;
        }

        string logLine = $"【進化】スキル『{oldName}』は『{newName}』へと進化した！";
        Debug.Log($"<color=#FFD54F><b>{logLine}</b></color>");

        // フェーズ1: 左下バトルログへ通知
        yield return PushBattleLogRoutine(logLine);

        // フェーズ2: 全画面カットイン（入力遮断付き）
        IsPerformanceActive = true;
        PopulateCutInTexts(oldName, newName, newDescription);
        cutInPanelRoot.gameObject.SetActive(true);

        if (cutInCanvasGroup != null)
        {
            cutInCanvasGroup.alpha = 0f;
            cutInCanvasGroup.interactable = true;
            cutInCanvasGroup.blocksRaycasts = true;
        }

        float fadeInElapsed = 0f;
        while (fadeInElapsed < cutInFadeInDuration)
        {
            if (!isActiveAndEnabled)
            {
                CleanupAfterPerformanceAbort();
                yield break;
            }

            fadeInElapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(fadeInElapsed / Mathf.Max(0.01f, cutInFadeInDuration));
            if (cutInCanvasGroup != null)
            {
                cutInCanvasGroup.alpha = t;
            }

            if (cutInPanelRoot != null)
            {
                float punchT = Mathf.Clamp01(fadeInElapsed / Mathf.Max(0.01f, cutInPunchDuration));
                float scale = Mathf.Lerp(0.82f, 1.08f, punchT);
                cutInPanelRoot.localScale = Vector3.one * scale;
            }

            yield return null;
        }

        if (cutInCanvasGroup != null)
        {
            cutInCanvasGroup.alpha = 1f;
        }

        if (cutInPanelRoot != null)
        {
            cutInPanelRoot.localScale = Vector3.one;
        }

        // フェーズ3: ホールド（新スキル名を大きく見せる）
        yield return new WaitForSecondsRealtime(Mathf.Max(0.1f, cutInHoldDuration));

        // フェーズ4: フェードアウトして演出終了
        float fadeOutElapsed = 0f;
        while (fadeOutElapsed < cutInFadeOutDuration)
        {
            if (!isActiveAndEnabled)
            {
                CleanupAfterPerformanceAbort();
                yield break;
            }

            fadeOutElapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(fadeOutElapsed / Mathf.Max(0.01f, cutInFadeOutDuration));
            if (cutInCanvasGroup != null)
            {
                cutInCanvasGroup.alpha = 1f - t;
            }

            yield return null;
        }

        HideCutInImmediate();
        IsPerformanceActive = false;
    }

    private IEnumerator PushBattleLogRoutine(string logLine)
    {
        if (battleLogPanelRoot == null || battleLogText == null)
        {
            yield break;
        }

        battleLogLines.Add(logLine);
        while (battleLogLines.Count > MaxBattleLogLines)
        {
            battleLogLines.RemoveAt(0);
        }

        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < battleLogLines.Count; i++)
        {
            if (i > 0)
            {
                sb.AppendLine();
            }

            sb.Append(battleLogLines[i]);
        }

        battleLogText.text = sb.ToString();
        battleLogPanelRoot.gameObject.SetActive(true);

        if (battleLogCanvasGroup == null)
        {
            yield break;
        }

        float elapsed = 0f;
        battleLogCanvasGroup.alpha = 0f;
        while (elapsed < battleLogFadeInDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            battleLogCanvasGroup.alpha = Mathf.Clamp01(elapsed / Mathf.Max(0.01f, battleLogFadeInDuration));
            yield return null;
        }

        battleLogCanvasGroup.alpha = 1f;
    }

    /// <summary>外部（歴史解読など）からバトルログへ1行追加します。</summary>
    public void PublishBattleLogLine(string logLine)
    {
        if (string.IsNullOrWhiteSpace(logLine))
        {
            return;
        }

        EnsureUiHierarchy();
        if (battleLogPanelRoot == null || battleLogText == null)
        {
            return;
        }

        battleLogLines.Add(logLine);
        while (battleLogLines.Count > MaxBattleLogLines)
        {
            battleLogLines.RemoveAt(0);
        }

        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < battleLogLines.Count; i++)
        {
            if (i > 0)
            {
                sb.AppendLine();
            }

            sb.Append(battleLogLines[i]);
        }

        battleLogText.text = sb.ToString();
        battleLogPanelRoot.gameObject.SetActive(true);
        if (battleLogCanvasGroup != null)
        {
            battleLogCanvasGroup.alpha = 1f;
        }
    }

    private void PopulateCutInTexts(string oldName, string newName, string newDescription)
    {
        if (cutInTitleText != null)
        {
            cutInTitleText.text = "【スキル進化】";
        }

        if (cutInOldSkillText != null)
        {
            cutInOldSkillText.text = $"『{oldName}』";
        }

        if (cutInNewSkillText != null)
        {
            cutInNewSkillText.text = $"『{newName}』";
        }

        if (cutInDescriptionText != null)
        {
            cutInDescriptionText.text = string.IsNullOrWhiteSpace(newDescription)
                ? "新たな上位スキルが解放された。"
                : newDescription;
        }

        if (cutInIconImage != null)
        {
            cutInIconImage.enabled = cutInIconImage.sprite != null;
        }
    }

    private bool TryResolveSkillDisplay(string skillId, out string skillName, out string description)
    {
        skillName = skillId ?? string.Empty;
        description = string.Empty;

        if (string.IsNullOrWhiteSpace(skillId))
        {
            return false;
        }

        if (skillMasterRepository != null &&
            skillMasterRepository.TryGet(skillId, out SkillMaster fromRepo) &&
            fromRepo != null)
        {
            skillName = string.IsNullOrWhiteSpace(fromRepo.skillName) ? skillId : fromRepo.skillName;
            description = fromRepo.description ?? string.Empty;
            return true;
        }

        if (MasterDataManager.Instance != null)
        {
            SkillMaster fromManager = MasterDataManager.Instance.GetSkill(skillId);
            if (fromManager != null)
            {
                skillName = string.IsNullOrWhiteSpace(fromManager.skillName) ? skillId : fromManager.skillName;
                description = fromManager.description ?? string.Empty;
                return true;
            }
        }

        return false;
    }

    private void ResolveRepository()
    {
        if (skillMasterRepository == null)
        {
            skillMasterRepository = SkillMasterRepository.Instance
                ?? FindAnyObjectByType<SkillMasterRepository>();
        }
    }

    private void HideCutInImmediate()
    {
        if (cutInPanelRoot != null)
        {
            cutInPanelRoot.gameObject.SetActive(false);
            cutInPanelRoot.localScale = Vector3.one;
        }

        if (cutInCanvasGroup != null)
        {
            cutInCanvasGroup.alpha = 0f;
            cutInCanvasGroup.blocksRaycasts = false;
            cutInCanvasGroup.interactable = false;
        }
    }

    private void CleanupAfterPerformanceAbort()
    {
        HideCutInImmediate();
        IsPerformanceActive = false;
        performanceQueueCoroutine = null;
    }

    private void StopAllPerformanceCoroutines()
    {
        if (performanceQueueCoroutine != null)
        {
            StopCoroutine(performanceQueueCoroutine);
            performanceQueueCoroutine = null;
        }

        pendingRequests.Clear();
    }

    private void EnsureUiHierarchy()
    {
        EnsureEventSystem();

        if (rootCanvas == null)
        {
            rootCanvas = GetComponentInChildren<Canvas>();
        }

        if (rootCanvas == null)
        {
            GameObject canvasObject = new GameObject("SkillEvolutionCanvas", typeof(RectTransform));
            canvasObject.transform.SetParent(transform, false);

            rootCanvas = canvasObject.AddComponent<Canvas>();
            rootCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            rootCanvas.sortingOrder = 9500;
            rootCanvas.vertexColorAlwaysGammaSpace = true;

            CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            canvasObject.AddComponent<GraphicRaycaster>();
        }

        if (battleLogPanelRoot == null)
        {
            battleLogPanelRoot = CreatePanel(
                "EvolutionBattleLog",
                rootCanvas.transform,
                new Vector2(0f, 0f),
                new Vector2(0f, 0f),
                new Vector2(0f, 0f),
                new Vector2(640f, 180f),
                new Vector2(24f, 24f),
                new Color(0.04f, 0.06f, 0.12f, 0.9f),
                out Image battleLogBackdrop);

            battleLogBackdrop.raycastTarget = false;

            GameObject textObject = new GameObject("LogText", typeof(RectTransform));
            textObject.transform.SetParent(battleLogPanelRoot, false);
            RectTransform textRect = textObject.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(16f, 12f);
            textRect.offsetMax = new Vector2(-16f, -12f);

            battleLogText = textObject.AddComponent<Text>();
            RuntimeUIFontHelper.ApplyTo(battleLogText, 20, FontStyle.Bold, TextAnchor.LowerLeft);
            battleLogText.color = Color.white;
            battleLogText.supportRichText = true;
            battleLogText.horizontalOverflow = HorizontalWrapMode.Wrap;
            battleLogText.verticalOverflow = VerticalWrapMode.Truncate;

            battleLogCanvasGroup = battleLogPanelRoot.gameObject.AddComponent<CanvasGroup>();
            battleLogPanelRoot.gameObject.SetActive(false);
        }

        if (cutInPanelRoot == null)
        {
            cutInPanelRoot = CreatePanel(
                "EvolutionCutIn",
                rootCanvas.transform,
                Vector2.zero,
                Vector2.one,
                Vector2.zero,
                Vector2.zero,
                Vector2.zero,
                CutInBackdrop,
                out cutInBackdropImage);

            cutInCanvasGroup = cutInPanelRoot.gameObject.AddComponent<CanvasGroup>();
            cutInBackdropImage.raycastTarget = true;

            cutInTitleText = CreateCutInLabel(cutInPanelRoot, "Title", new Vector2(0.5f, 0.72f), 34, CutInGold);
            cutInOldSkillText = CreateCutInLabel(cutInPanelRoot, "OldSkill", new Vector2(0.5f, 0.58f), 28, Color.white);
            cutInNewSkillText = CreateCutInLabel(cutInPanelRoot, "NewSkill", new Vector2(0.5f, 0.46f), 52, CutInGold);
            cutInDescriptionText = CreateCutInLabel(cutInPanelRoot, "Description", new Vector2(0.5f, 0.28f), 22, new Color(0.9f, 0.95f, 1f, 1f));

            GameObject iconObject = new GameObject("Icon", typeof(RectTransform));
            iconObject.transform.SetParent(cutInPanelRoot, false);
            RectTransform iconRect = iconObject.GetComponent<RectTransform>();
            iconRect.anchorMin = new Vector2(0.5f, 0.5f);
            iconRect.anchorMax = new Vector2(0.5f, 0.5f);
            iconRect.pivot = new Vector2(0.5f, 0.5f);
            iconRect.sizeDelta = new Vector2(96f, 96f);
            iconRect.anchoredPosition = new Vector2(0f, 120f);
            cutInIconImage = iconObject.AddComponent<Image>();
            cutInIconImage.color = Color.white;
            cutInIconImage.preserveAspect = true;
            cutInIconImage.enabled = false;
        }
    }

    private static Text CreateCutInLabel(RectTransform parent, string name, Vector2 anchorY, int fontSize, Color color)
    {
        GameObject labelObject = new GameObject(name, typeof(RectTransform));
        labelObject.transform.SetParent(parent, false);

        RectTransform rect = labelObject.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.08f, anchorY.y - 0.08f);
        rect.anchorMax = new Vector2(0.92f, anchorY.y + 0.08f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        Text text = labelObject.AddComponent<Text>();
        RuntimeUIFontHelper.ApplyTo(text, fontSize, FontStyle.Bold, TextAnchor.MiddleCenter);
        text.color = color;
        text.supportRichText = true;

        Outline outline = labelObject.AddComponent<Outline>();
        outline.effectColor = new Color(0f, 0f, 0f, 0.85f);
        outline.effectDistance = new Vector2(3f, -3f);

        return text;
    }

    private static RectTransform CreatePanel(
        string objectName,
        Transform parent,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Vector2 pivot,
        Vector2 sizeDelta,
        Vector2 anchoredPosition,
        Color backgroundColor,
        out Image backdrop)
    {
        GameObject panelObject = new GameObject(objectName, typeof(RectTransform));
        panelObject.transform.SetParent(parent, false);

        RectTransform rect = panelObject.GetComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = pivot;
        rect.sizeDelta = sizeDelta;
        rect.anchoredPosition = anchoredPosition;

        backdrop = panelObject.AddComponent<Image>();
        backdrop.color = backgroundColor;
        return rect;
    }

    private static void EnsureEventSystem()
    {
        if (FindAnyObjectByType<EventSystem>() != null)
        {
            return;
        }

        GameObject eventSystemObject = new GameObject("EventSystem");
        eventSystemObject.AddComponent<EventSystem>();
        eventSystemObject.AddComponent<InputSystemUIInputModule>();
    }
}

/// <summary>Play 開始時に SkillEvolutionPresenter を自動配置します。</summary>
public static class SkillEvolutionPresenterBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsurePresenter()
    {
        SkillEvolutionPresenter.EnsureInstance();
    }
}

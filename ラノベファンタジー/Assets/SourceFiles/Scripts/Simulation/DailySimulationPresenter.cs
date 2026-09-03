using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

// =============================================================================
// 1 日単位ミクロシミュレーション UI・進行 Presenter
// 連携: DailySimulationEngine / VillageAutonomyEngine / NpcCivilizationEngine
// =============================================================================

/// <summary>
/// 365 日デイリータイムラインの表示と「1日経過」操作を提供します。
/// uGUI 欠損時は OnGUI フォールバックで停止しません。
/// </summary>
[DefaultExecutionOrder(56)]
public class DailySimulationPresenter : MonoBehaviour
{
    public const string LogTag = "【デイリーUI】";

    public static DailySimulationPresenter Instance { get; private set; }

    [Header("参照（未設定時は自動解決）")]
    [SerializeField] private Canvas screenCanvas;
    [SerializeField] private Text dayText;
    [SerializeField] private Text statusText;
    [SerializeField] private Text logText;
    [SerializeField] private Button advanceDayButton;
    [SerializeField] private Button injectTestButton;

    private DailySimulationEngine engine;
    private bool screenUiReady;
    private bool useOnGuiFallback = true;
    private string lastLifeLog = string.Empty;

    public bool ScreenUiReady => screenUiReady;
    public string LastLifeLog => lastLifeLog;

    public static DailySimulationPresenter EnsureInstance()
    {
        if (Instance != null)
        {
            return Instance;
        }

        DailySimulationPresenter existing = Object.FindAnyObjectByType<DailySimulationPresenter>();
        if (existing != null)
        {
            return existing;
        }

        GameObject hub = GameObject.Find("DebugSystemsHub");
        GameObject host = hub != null ? hub : new GameObject(nameof(DailySimulationPresenter));
        DailySimulationPresenter presenter = host.GetComponent<DailySimulationPresenter>();
        return presenter != null ? presenter : host.AddComponent<DailySimulationPresenter>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        engine = DailySimulationEngine.EnsureInstance();
        try
        {
            EnsureScreenUi();
            RefreshAllLabels();
        }
        catch (System.Exception exception)
        {
            useOnGuiFallback = true;
            screenUiReady = false;
            Debug.LogWarning($"[DailySimulationPresenter] Awake Safe-Fail: {exception.Message}");
        }
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void Start()
    {
        if (!Application.isPlaying)
        {
            return;
        }

        Debug.Log(
            $"<color=#80DEEA><b>{LogTag} Play 準備完了</b></color> " +
            "パネルの「1日経過」「納品テスト」をクリック、または " +
            "<b>Shift+N</b>（1日経過）/ <b>Shift+G</b>（納品テスト）。" +
            "Game ビューを一度クリックしてから操作してください。");
    }

    private void Update()
    {
        if (!Application.isPlaying)
        {
            return;
        }

        if (!DebugHotkeyUtility.TryGetKeyboard(out Keyboard keyboard))
        {
            return;
        }

        if (!keyboard.shiftKey.isPressed)
        {
            return;
        }

        if (DebugHotkeyUtility.WasPressed(Key.N))
        {
            OnAdvanceDayClicked();
        }
        else if (DebugHotkeyUtility.WasPressed(Key.G))
        {
            OnInjectTestClicked();
        }
    }

    private void OnGUI()
    {
        if (screenUiReady && !useOnGuiFallback)
        {
            return;
        }

        try
        {
            const float width = 320f;
            Rect box = new Rect(12f, 200f, width, 220f);
            GUI.Box(box, "Daily Simulation");
            engine = engine ?? DailySimulationEngine.EnsureInstance();
            VillageAutonomyEngine village = VillageAutonomyEngine.EnsureInstance();

            GUI.Label(new Rect(24f, 224f, width - 24f, 22f), BuildDayLabel());
            GUI.Label(new Rect(24f, 248f, width - 24f, 22f), BuildBarrierLabel(village));
            GUI.Label(new Rect(24f, 272f, width - 24f, 22f), BuildThreatLabel());
            GUI.Label(new Rect(24f, 296f, width - 24f, 40f), BuildLogPreview());

            if (GUI.Button(new Rect(24f, 340f, 140f, 32f), "1日経過"))
            {
                AdvanceOneDay();
            }

            if (GUI.Button(new Rect(164f, 340f, 120f, 32f), "納品テスト"))
            {
                InjectTestCrystal();
            }
        }
        catch (System.Exception)
        {
            // OnGUI 自体の失敗では止めない
        }
    }

    /// <summary>1 日進め、生活ログと村状態を更新します。</summary>
    public DailyAdvanceResult AdvanceOneDay()
    {
        engine = engine ?? DailySimulationEngine.EnsureInstance();
        DailyAdvanceResult result = engine.AdvanceOneDay();
        if (!string.IsNullOrWhiteSpace(result.lifeLog))
        {
            lastLifeLog = result.lifeLog;
        }

        RefreshAllLabels();
        return result;
    }

    private void OnAdvanceDayClicked()
    {
        AdvanceOneDay();
    }

    /// <summary>検証用: Purity 80 の高純度魔力結晶を納品。</summary>
    public CraftVillageInjectionResult InjectTestCrystal()
    {
        CraftVillageInjectionResult result = DailySimulationEngine.InjectCraftResultToVillage(
            CraftRecipeIds.CrystalShard,
            80f,
            55f,
            "高純度魔力結晶");
        RefreshAllLabels();
        return result;
    }

    private void OnInjectTestClicked()
    {
        InjectTestCrystal();
    }

    private void RefreshAllLabels()
    {
        engine = engine ?? DailySimulationEngine.EnsureInstance();
        VillageAutonomyEngine village = VillageAutonomyEngine.EnsureInstance();
        NpcCivilizationEngine civ = NpcCivilizationEngine.EnsureInstance();

        string dayLine = BuildDayLabel();
        string statusLine =
            $"{BuildBarrierLabel(village)} | 脅威 {engine.CurrentThreatLevel:F2} | " +
            $"魔力 {NaturalEcologyEngine.EnsureInstance().Environment.ManaEcologyLevel:F1} | " +
            civ.Storage.FormatSnapshot();
        string logLine = BuildLogPreview();

        if (dayText != null)
        {
            dayText.text = dayLine;
        }

        if (statusText != null)
        {
            statusText.text = statusLine;
        }

        if (logText != null)
        {
            logText.text = logLine;
        }
    }

    private string BuildDayLabel()
    {
        engine = engine ?? DailySimulationEngine.EnsureInstance();
        return $"国家{engine.NationId:D3} ターン{engine.Turn} — {engine.CurrentDayOfYear}日目 / 365";
    }

    private static string BuildBarrierLabel(VillageAutonomyEngine village)
    {
        float pct = village?.Barrier?.Efficiency ?? 0f;
        return $"結界維持率 {pct:F1}%";
    }

    private string BuildThreatLabel()
    {
        engine = engine ?? DailySimulationEngine.EnsureInstance();
        DailyInterventionState intervention = engine.Intervention;
        string extra = intervention.remainingDays > 0
            ? $" 介入残{intervention.remainingDays}日 脅威-{intervention.threatReduction:F2}"
            : string.Empty;
        return $"魔獣侵入リスク {engine.CurrentThreatLevel:F2}{extra}";
    }

    private string BuildLogPreview()
    {
        engine = engine ?? DailySimulationEngine.EnsureInstance();
        if (!string.IsNullOrWhiteSpace(lastLifeLog))
        {
            return lastLifeLog;
        }

        IReadOnlyList<string> logs = engine.RecentLifeLogs;
        if (logs == null || logs.Count == 0)
        {
            return "生活ログ: まだ記録がありません";
        }

        return logs[logs.Count - 1];
    }

    private void EnsureScreenUi()
    {
        EnsureEventSystem();
        screenUiReady = false;
        useOnGuiFallback = true;

        try
        {
            if (screenCanvas == null)
            {
                screenCanvas = Object.FindAnyObjectByType<Canvas>();
            }

            if (screenCanvas == null || screenCanvas.renderMode != RenderMode.ScreenSpaceOverlay)
            {
                GameObject canvasObject = new GameObject(
                    "DailySimulationCanvas",
                    typeof(RectTransform),
                    typeof(Canvas),
                    typeof(CanvasScaler),
                    typeof(GraphicRaycaster));
                canvasObject.transform.SetParent(transform, false);
                screenCanvas = canvasObject.GetComponent<Canvas>();
                screenCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
                screenCanvas.sortingOrder = 9310;
                CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                scaler.matchWidthOrHeight = 0.5f;
            }

            Transform existing = screenCanvas.transform.Find("DailySimulationPanel");
            if (existing != null)
            {
                BindExistingUi(existing);
                screenUiReady = dayText != null && advanceDayButton != null;
                useOnGuiFallback = !screenUiReady;
                return;
            }

            RectTransform panel = CreatePanel(screenCanvas.transform);
            dayText = CreateLabel("DayText", panel, new Vector2(16f, -12f), 20);
            statusText = CreateLabel("StatusText", panel, new Vector2(16f, -40f), 16);
            logText = CreateLabel("LogText", panel, new Vector2(16f, -68f), 15);
            logText.GetComponent<RectTransform>().sizeDelta = new Vector2(-32f, 72f);

            advanceDayButton = CreateButton(panel, "AdvanceDayButton", "1日経過", new Vector2(16f, 14f));
            advanceDayButton.onClick.AddListener(OnAdvanceDayClicked);

            injectTestButton = CreateButton(panel, "InjectTestButton", "納品テスト", new Vector2(176f, 14f));
            injectTestButton.onClick.AddListener(OnInjectTestClicked);

            screenUiReady = dayText != null && statusText != null && advanceDayButton != null;
            useOnGuiFallback = !screenUiReady;
        }
        catch (System.Exception exception)
        {
            screenUiReady = false;
            useOnGuiFallback = true;
            Debug.LogWarning($"[DailySimulationPresenter] uGUI 構築失敗: {exception.Message}");
        }
    }

    private static void EnsureEventSystem()
    {
        if (Object.FindAnyObjectByType<EventSystem>() != null)
        {
            return;
        }

        GameObject eventSystemObject = new GameObject(
            "EventSystem",
            typeof(EventSystem),
            typeof(InputSystemUIInputModule));
        eventSystemObject.transform.SetParent(null, false);
    }

    private void BindExistingUi(Transform panel)
    {
        dayText = panel.Find("DayText")?.GetComponent<Text>();
        statusText = panel.Find("StatusText")?.GetComponent<Text>();
        logText = panel.Find("LogText")?.GetComponent<Text>();
        advanceDayButton = panel.Find("AdvanceDayButton")?.GetComponent<Button>();
        injectTestButton = panel.Find("InjectTestButton")?.GetComponent<Button>();
        if (advanceDayButton != null)
        {
            advanceDayButton.onClick.RemoveListener(OnAdvanceDayClicked);
            advanceDayButton.onClick.AddListener(OnAdvanceDayClicked);
        }

        if (injectTestButton != null)
        {
            injectTestButton.onClick.RemoveListener(OnInjectTestClicked);
            injectTestButton.onClick.AddListener(OnInjectTestClicked);
        }
    }

    private static RectTransform CreatePanel(Transform parent)
    {
        GameObject panelObject = new GameObject("DailySimulationPanel", typeof(RectTransform), typeof(Image));
        panelObject.transform.SetParent(parent, false);
        RectTransform rect = panelObject.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.sizeDelta = new Vector2(380f, 200f);
        rect.anchoredPosition = new Vector2(18f, -210f);
        Image backdrop = panelObject.GetComponent<Image>();
        backdrop.color = new Color(0.04f, 0.1f, 0.14f, 0.9f);
        backdrop.raycastTarget = false;
        return rect;
    }

    private static Text CreateLabel(string name, RectTransform parent, Vector2 anchoredPos, int fontSize)
    {
        GameObject textObject = new GameObject(name, typeof(RectTransform));
        textObject.transform.SetParent(parent, false);
        RectTransform rect = textObject.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = anchoredPos;
        rect.sizeDelta = new Vector2(-32f, 28f);
        Text text = textObject.AddComponent<Text>();
        RuntimeUIFontHelper.ApplyTo(text, fontSize, FontStyle.Bold, TextAnchor.UpperLeft);
        text.color = Color.white;
        text.raycastTarget = false;
        text.supportRichText = false;
        return text;
    }

    private static Button CreateButton(RectTransform parent, string name, string label, Vector2 anchoredPos)
    {
        GameObject buttonObject = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        buttonObject.transform.SetParent(parent, false);
        RectTransform rect = buttonObject.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 0f);
        rect.anchorMax = new Vector2(0f, 0f);
        rect.pivot = new Vector2(0f, 0f);
        rect.anchoredPosition = anchoredPos;
        rect.sizeDelta = new Vector2(148f, 34f);

        Image image = buttonObject.GetComponent<Image>();
        image.color = new Color(0.12f, 0.48f, 0.58f, 0.95f);

        GameObject labelObject = new GameObject("Label", typeof(RectTransform));
        labelObject.transform.SetParent(buttonObject.transform, false);
        RectTransform labelRect = labelObject.GetComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;
        Text labelText = labelObject.AddComponent<Text>();
        RuntimeUIFontHelper.ApplyTo(labelText, 17, FontStyle.Bold, TextAnchor.MiddleCenter);
        labelText.text = label;
        labelText.raycastTarget = false;

        Button button = buttonObject.GetComponent<Button>();
        button.targetGraphic = image;
        return button;
    }
}

#if UNITY_EDITOR
public static class DailySimulationPresenterMenu
{
    [MenuItem("Tools/Procedural Map/Open Daily Simulation Panel")]
    public static void OpenPanel()
    {
        DailySimulationPresenter presenter = DailySimulationPresenter.EnsureInstance();
        EditorUtility.DisplayDialog(
            "Daily Simulation",
            presenter.ScreenUiReady
                ? "Daily Simulation パネルを表示しました。Play 中に「1日経過」を押してください。"
                : "OnGUI フォールバックで表示されます（画面左下付近）。",
            "OK");
    }
}
#endif

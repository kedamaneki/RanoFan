using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

// =============================================================================
// タイムライン選択 Presenter — 正史 HIST_ / ALT_ 分岐軸の切替 UI
// 連携: HistoryBranchManager / MicroToMacroAggregator / EraContextResolver
// =============================================================================

/// <summary>Presenter 検証結果。</summary>
public sealed class TimelineSelectorVerifyResult
{
    public bool success;
    public string message = string.Empty;
}

/// <summary>
/// 存在する歴史軸（正史または生成済み ALT_）を一覧表示し、アクティブ化します。
/// </summary>
[DefaultExecutionOrder(57)]
public class TimelineSelectorPresenter : MonoBehaviour
{
    public const string LogTag = "【タイムライン選択】";

    public static TimelineSelectorPresenter Instance { get; private set; }

    [Header("参照（未設定時は自動構築）")]
    [SerializeField] private Canvas screenCanvas;
    [SerializeField] private Text headerText;
    [SerializeField] private Text activeText;
    [SerializeField] private Text previewText;
    [SerializeField] private RectTransform branchButtonRoot;

    private readonly List<Button> branchButtons = new List<Button>();
    private bool screenUiReady;
    private bool useOnGuiFallback = true;
    private string lastSelectMessage = string.Empty;

    public string LastSelectMessage => lastSelectMessage;
    public bool ScreenUiReady => screenUiReady;

    public static TimelineSelectorPresenter EnsureInstance()
    {
        if (Instance != null)
        {
            return Instance;
        }

        TimelineSelectorPresenter existing = Object.FindAnyObjectByType<TimelineSelectorPresenter>();
        if (existing != null)
        {
            Instance = existing;
            return existing;
        }

        GameObject hub = GameObject.Find("DebugSystemsHub");
        GameObject host = hub != null ? hub : new GameObject(nameof(TimelineSelectorPresenter));
        TimelineSelectorPresenter presenter = host.GetComponent<TimelineSelectorPresenter>();
        return presenter != null ? presenter : host.AddComponent<TimelineSelectorPresenter>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        HistoryBranchManager.EnsureInstance();
        try
        {
            EnsureScreenUi();
            RefreshTimelineUi();
        }
        catch (System.Exception exception)
        {
            useOnGuiFallback = true;
            Debug.LogWarning($"[TimelineSelectorPresenter] Awake Safe-Fail: {exception.Message}");
        }
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void OnGUI()
    {
        if (!useOnGuiFallback)
        {
            return;
        }

        const int width = 420;
        const int height = 280;
        GUILayout.BeginArea(new Rect(12f, Screen.height - height - 12f, width, height), "Timeline Selector", GUI.skin.window);
        GUILayout.Label($"Active: {HistoryBranchManager.EnsureInstance().ActiveBranchId}");
        GUILayout.Label(lastSelectMessage);

        List<TimelineBranchSummary> timelines = HistoryBranchManager.ListAvailableTimelines();
        for (int i = 0; i < timelines.Count; i++)
        {
            TimelineBranchSummary summary = timelines[i];
            string label = summary.isActive ? $"* {summary.label}" : summary.label;
            if (GUILayout.Button(label))
            {
                SelectTimelineBranch(summary.branchId);
            }
        }

        if (GUILayout.Button("Preview T6 Power/Barrier"))
        {
            RefreshPreview();
        }

        GUILayout.Label(previewText != null ? previewText.text : string.Empty);
        GUILayout.EndArea();
    }

    /// <summary>歴史軸をアクティブ化します（Presenter エントリポイント）。</summary>
    public TimelineSelectResult SelectTimelineBranch(string branchId)
    {
        TimelineSelectResult result = HistoryBranchManager.SelectTimelineBranch(branchId);
        lastSelectMessage = result.message;
        RefreshTimelineUi();
        RefreshPreview();

        if (result.success)
        {
            Debug.Log(
                $"<color=#CE93D8><b>{LogTag}</b></color> {result.message} canon={result.isCanonMode}");
        }

        return result;
    }

    public void RefreshTimelineUi()
    {
        HistoryBranchManager mgr = HistoryBranchManager.EnsureInstance();
        if (activeText != null)
        {
            activeText.text = mgr.IsCanonMode
                ? "Active: 正史 (HIST_CANON)"
                : $"Active: {mgr.ActiveBranchId}";
        }

        if (headerText != null)
        {
            headerText.text = "歴史タイムライン選択";
        }

        RebuildBranchButtons();
        RefreshPreview();
    }

    private void RefreshPreview()
    {
        const float basePower = 1000f;
        const float baseBarrier = 0.95f;
        int turn = 6;
        int nationId = MicroToMacroAggregator.DefaultNationId;
        float power = HistoryBranchManager.GetAdjustedPower(basePower, turn, nationId);
        float barrier = HistoryBranchManager.GetAdjustedBarrier(baseBarrier, turn, nationId);
        string line =
            $"T{turn} preview: Power {basePower:F0}→{power:F0}  Barrier {baseBarrier:F3}→{barrier:F3}";

        if (previewText != null)
        {
            previewText.text = line;
        }
    }

    private void RebuildBranchButtons()
    {
        if (branchButtonRoot == null)
        {
            return;
        }

        for (int i = 0; i < branchButtons.Count; i++)
        {
            if (branchButtons[i] != null)
            {
                Destroy(branchButtons[i].gameObject);
            }
        }

        branchButtons.Clear();
        List<TimelineBranchSummary> timelines = HistoryBranchManager.ListAvailableTimelines();
        float y = 0f;
        for (int i = 0; i < timelines.Count; i++)
        {
            TimelineBranchSummary summary = timelines[i];
            string label = summary.isActive ? $"● {summary.label}" : summary.label;
            Button button = CreateBranchButton(branchButtonRoot, $"Branch_{i}", label, y);
            string branchId = summary.branchId;
            button.onClick.AddListener(() => SelectTimelineBranch(branchId));
            branchButtons.Add(button);
            y -= 36f;
        }
    }

    private void EnsureScreenUi()
    {
        if (screenCanvas != null && headerText != null && branchButtonRoot != null)
        {
            screenUiReady = true;
            useOnGuiFallback = false;
            return;
        }

        if (EventSystem.current == null)
        {
            GameObject es = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            es.transform.SetParent(transform, false);
        }

        GameObject canvasObject = new GameObject("TimelineSelectorCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasObject.transform.SetParent(transform, false);
        screenCanvas = canvasObject.GetComponent<Canvas>();
        screenCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        screenCanvas.sortingOrder = 120;

        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);

        RectTransform panel = CreatePanel(canvasObject.transform);
        headerText = CreateLabel(panel, "Header", "歴史タイムライン選択", new Vector2(12f, -8f), 22);
        activeText = CreateLabel(panel, "Active", "Active: HIST_CANON", new Vector2(12f, -38f), 18);
        previewText = CreateLabel(panel, "Preview", "T6 preview: —", new Vector2(12f, -64f), 16);

        GameObject rootObject = new GameObject("BranchButtons", typeof(RectTransform));
        rootObject.transform.SetParent(panel, false);
        branchButtonRoot = rootObject.GetComponent<RectTransform>();
        branchButtonRoot.anchorMin = new Vector2(0f, 1f);
        branchButtonRoot.anchorMax = new Vector2(0f, 1f);
        branchButtonRoot.pivot = new Vector2(0f, 1f);
        branchButtonRoot.anchoredPosition = new Vector2(12f, -92f);
        branchButtonRoot.sizeDelta = new Vector2(360f, 220f);

        screenUiReady = true;
        useOnGuiFallback = false;
    }

    private static RectTransform CreatePanel(Transform parent)
    {
        GameObject panelObject = new GameObject("Panel", typeof(RectTransform), typeof(Image));
        panelObject.transform.SetParent(parent, false);
        RectTransform rect = panelObject.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 0f);
        rect.anchorMax = new Vector2(0f, 0f);
        rect.pivot = new Vector2(0f, 0f);
        rect.anchoredPosition = new Vector2(12f, 12f);
        rect.sizeDelta = new Vector2(400f, 320f);
        panelObject.GetComponent<Image>().color = new Color(0.06f, 0.08f, 0.12f, 0.92f);
        return rect;
    }

    private static Text CreateLabel(RectTransform parent, string name, string text, Vector2 pos, int fontSize)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = pos;
        rect.sizeDelta = new Vector2(376f, 28f);
        Text label = go.AddComponent<Text>();
        RuntimeUIFontHelper.ApplyTo(label, fontSize, FontStyle.Normal, TextAnchor.MiddleLeft);
        label.text = text;
        label.color = Color.white;
        label.raycastTarget = false;
        return label;
    }

    private static Button CreateBranchButton(RectTransform parent, string name, string label, float y)
    {
        GameObject buttonObject = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        buttonObject.transform.SetParent(parent, false);
        RectTransform rect = buttonObject.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(0f, y);
        rect.sizeDelta = new Vector2(360f, 32f);

        Image image = buttonObject.GetComponent<Image>();
        image.color = new Color(0.14f, 0.36f, 0.52f, 0.95f);

        GameObject labelObject = new GameObject("Label", typeof(RectTransform));
        labelObject.transform.SetParent(buttonObject.transform, false);
        RectTransform labelRect = labelObject.GetComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = new Vector2(8f, 0f);
        labelRect.offsetMax = Vector2.zero;
        Text labelText = labelObject.AddComponent<Text>();
        RuntimeUIFontHelper.ApplyTo(labelText, 15, FontStyle.Normal, TextAnchor.MiddleLeft);
        labelText.text = label;
        labelText.color = Color.white;
        labelText.raycastTarget = false;

        Button button = buttonObject.GetComponent<Button>();
        button.targetGraphic = image;
        return button;
    }

    public static TimelineSelectorVerifyResult RunVerification()
    {
        TimelineSelectorVerifyResult verify = new TimelineSelectorVerifyResult();
        StringBuilder log = new StringBuilder();
        try
        {
            HistoryFlagRegistry.EnsureWired();
            TimelineSelectorPresenter presenter = EnsureInstance();
            HistoryBranchManager mgr = HistoryBranchManager.EnsureInstance();
            mgr.ClearRegisteredBranches();

            HistoryAlterationResult reg = HistoryBranchManager.RegisterHistoryAlteration(
                5,
                MicroToMacroAggregator.DefaultNationId,
                "HIST_NATION_001_GEO_TURN_001_HERO",
                null,
                new MacroParamDelta { powerMultiplier = 1.10f, barrierEfficiencyDelta = 0.05f });
            string branchA = reg.branchId;

            TimelineSelectResult toCanon = presenter.SelectTimelineBranch(HistoryBranchManager.CanonBranchId);
            float canonPower = HistoryBranchManager.GetAdjustedPower(1000f, 6, MicroToMacroAggregator.DefaultNationId);
            TimelineSelectResult toAlt = presenter.SelectTimelineBranch(branchA);
            float altPower = HistoryBranchManager.GetAdjustedPower(1000f, 6, MicroToMacroAggregator.DefaultNationId);

            bool pass =
                reg.success &&
                toCanon.success &&
                toAlt.success &&
                Mathf.Approximately(canonPower, 1000f) &&
                altPower > 1000f;

            log.AppendLine($"register: {reg.branchId} success={reg.success}");
            log.AppendLine($"select-canon: power={canonPower:F0} pass={Mathf.Approximately(canonPower, 1000f)}");
            log.AppendLine($"select-alt: power={altPower:F0} pass={altPower > 1000f}");
            log.AppendLine($"branches={HistoryBranchManager.ListAvailableTimelines().Count}");

            verify.success = pass;
            verify.message = log.ToString().TrimEnd();
        }
        catch (System.Exception exception)
        {
            verify.success = false;
            verify.message = $"Safe-Fail: {exception.Message}";
        }

        WriteVerifyLog(verify);
        return verify;
    }

    private static void WriteVerifyLog(TimelineSelectorVerifyResult verify)
    {
        try
        {
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
            string path = Path.Combine(projectRoot, "Logs", "timeline_selector_verify.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? projectRoot);
            File.WriteAllText(
                path,
                $"{System.DateTime.UtcNow:O} success={verify.success}\n{verify.message}\n",
                Encoding.UTF8);
        }
        catch (System.Exception exception)
        {
            Debug.LogWarning($"[TimelineSelectorPresenter] 検証ログスキップ: {exception.Message}");
        }
    }
}

public static class TimelineSelectorPresenterBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoAttach()
    {
        TimelineSelectorPresenter.EnsureInstance();
    }
}

#if UNITY_EDITOR
public static class TimelineSelectorPresenterMenu
{
    [MenuItem("Tools/Procedural Map/Open Timeline Selector Panel")]
    public static void OpenPanel()
    {
        TimelineSelectorPresenter presenter = TimelineSelectorPresenter.EnsureInstance();
        presenter.RefreshTimelineUi();
        EditorUtility.DisplayDialog(
            "Timeline Selector",
            presenter.ScreenUiReady
                ? "タイムライン選択パネルを表示しました。"
                : "OnGUI フォールバック（画面左下）で表示されます。",
            "OK");
    }

    [MenuItem("Tools/Procedural Map/Verify Timeline Selector Presenter")]
    public static void VerifyFromMenu()
    {
        TimelineSelectorVerifyResult result = TimelineSelectorPresenter.RunVerification();
        Debug.Log(
            result.success
                ? $"<color=#A5D6A7><b>【タイムライン選択検証】PASS</b></color>\n{result.message}"
                : $"<color=#FF8A80><b>【タイムライン選択検証】FAIL</b></color>\n{result.message}");
        EditorUtility.DisplayDialog("Timeline Selector", result.message, "OK");
    }
}
#endif

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
#if UNITY_EDITOR
using System.IO;
using System.Text;
using UnityEditor;
#endif

// =============================================================================
// recompile-stamp
// 24位相・4期可変ミクロシミュレーション UI
// 連携: MicroHistoryTimelineTimelineEngine / ProceduralMapPopulator / WorldSpaceMarker
// =============================================================================

/// <summary>
/// 位相・期・脅威度・結界維持率を画面に出し、Next Phase でマップ環境を進めます。
/// uGUI が欠けていても OnGUI フォールバックで停止しません。
/// </summary>
[DefaultExecutionOrder(55)]
public class MicroPhaseSimUI : MonoBehaviour
{
    public const int PhaseCount = ProceduralMapPopulator.PhaseCount;
    public const int DefaultNationId = MicroHistoryTimelineTimelineEngine.DefaultNationId;
    public const int DefaultTurn = MicroHistoryTimelineTimelineEngine.DefaultTurn;

    public static MicroPhaseSimUI Instance { get; private set; }

    [Header("シミュレーション")]
    [SerializeField] private int nationId = DefaultNationId;
    [SerializeField] private int turn = DefaultTurn;
    [SerializeField] private int currentPhase = 1;

    [Header("参照（未設定時は自動解決）")]
    [SerializeField] private ProceduralMapPopulator populator;
    [SerializeField] private Canvas screenCanvas;
    [SerializeField] private Text phaseText;
    [SerializeField] private Text seasonText;
    [SerializeField] private Text threatText;
    [SerializeField] private Text barrierText;
    [SerializeField] private Button nextPhaseButton;

    private MicroHistoryTimeline timeline;
    private readonly List<Transform> workshopPads = new List<Transform>();
    private readonly List<Transform> beastSpawns = new List<Transform>();
    private readonly List<WorldSpaceMarker> worldMarkers = new List<WorldSpaceMarker>();
    private Transform worldMarkerRoot;
    private bool screenUiReady;
    private bool useOnGuiFallback = true;
    private float displayedThreat = 1.2f;
    private float displayedBarrier = 0.95f;
    private VariableTimelineSeason displayedSeason = VariableTimelineSeason.Active;

    public int CurrentPhase => currentPhase;
    public VariableTimelineSeason CurrentSeason => displayedSeason;
    public float CurrentThreat => displayedThreat;
    public float CurrentBarrierEfficiency => displayedBarrier;
    public bool ScreenUiReady => screenUiReady;
    public int WorldMarkerCount => worldMarkers.Count;

    /// <summary>シーンに無ければ生成して返します。</summary>
    public static MicroPhaseSimUI EnsureInstance()
    {
        if (Instance != null)
        {
            return Instance;
        }

        MicroPhaseSimUI existing = Object.FindAnyObjectByType<MicroPhaseSimUI>();
        if (existing != null)
        {
            return existing;
        }

        GameObject hub = GameObject.Find("DebugSystemsHub");
        GameObject host = hub != null ? hub : new GameObject(nameof(MicroPhaseSimUI));
        MicroPhaseSimUI ui = host.GetComponent<MicroPhaseSimUI>();
        return ui != null ? ui : host.AddComponent<MicroPhaseSimUI>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        try
        {
            EnsureScreenUi();
            BindSimulation(forceRebuild: true);
            ApplyPhase(currentPhase, rebuildMarkers: true);
        }
        catch (System.Exception exception)
        {
            useOnGuiFallback = true;
            screenUiReady = false;
            Debug.LogWarning($"[MicroPhaseSimUI] Awake Safe-Fail: {exception.Message}");
        }
    }

    private void Start()
    {
        if (!Application.isPlaying)
        {
            return;
        }

        if (worldMarkers.Count == 0)
        {
            RebuildWorldMarkers();
        }

        Debug.Log(
            $"<color=#80CBC4><b>【Micro Phase UI】Play 準備完了</b></color> " +
            $"{BuildPhaseLabel()} {BuildSeasonLabel()} {BuildThreatLabel()} {BuildBarrierLabel()} " +
            $"ugui={screenUiReady} fallback={useOnGuiFallback} markers={worldMarkers.Count}");
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
        if (!screenUiReady)
        {
            return;
        }

        RefreshScreenLabels();
    }

    private void OnGUI()
    {
        if (screenUiReady && !useOnGuiFallback)
        {
            return;
        }

        try
        {
            const float width = 280f;
            Rect box = new Rect(12f, 12f, width, 168f);
            GUI.Box(box, "Micro Phase Sim");
            GUI.Label(new Rect(24f, 36f, width - 24f, 22f), BuildPhaseLabel());
            GUI.Label(new Rect(24f, 58f, width - 24f, 22f), BuildSeasonLabel());
            GUI.Label(new Rect(24f, 80f, width - 24f, 22f), BuildThreatLabel());
            GUI.Label(new Rect(24f, 102f, width - 24f, 22f), BuildBarrierLabel());
            if (GUI.Button(new Rect(24f, 130f, 160f, 32f), "Next Phase"))
            {
                AdvancePhase();
            }
        }
        catch (System.Exception)
        {
            // OnGUI 自体の失敗では止めない
        }
    }

    /// <summary>位相を 1 進め、タイムライン枠とマップ環境を更新します。</summary>
    public void AdvancePhase()
    {
        int next = ProceduralMapPopulator.NormalizePhase(currentPhase + 1);
        ApplyPhase(next, rebuildMarkers: false);
    }

    /// <summary>指定位相へ同期します（1〜24）。</summary>
    public void ApplyPhase(int phase, bool rebuildMarkers)
    {
        try
        {
            currentPhase = ProceduralMapPopulator.NormalizePhase(phase);
            BindSimulation(forceRebuild: false);
            ReadSlotParameters(currentPhase);

            populator = ResolvePopulator();
            if (populator != null)
            {
                try
                {
                    populator.BindTimeline(timeline);
                    populator.UpdatePhaseEnvironment(currentPhase);
                }
                catch (System.Exception exception)
                {
                    Debug.LogWarning($"[MicroPhaseSimUI] マップ位相更新をスキップ: {exception.Message}");
                }
            }

            if (rebuildMarkers || worldMarkers.Count == 0)
            {
                RebuildWorldMarkers();
            }
            else
            {
                ApplySeasonToMarkers(displayedSeason);
            }

            try
            {
                NpcCivilizationEngine civilization = NpcCivilizationEngine.EnsureInstance();
                civilization.TickPhase(currentPhase, displayedSeason);
                VillageAutonomyEngine autonomy = VillageAutonomyEngine.EnsureInstance();
                displayedBarrier = autonomy.Barrier.Efficiency / 100f;

                EnemyBehaviorProfileManager.TickEcologyPhase(currentPhase, displayedSeason);
            }
            catch (System.Exception villageException)
            {
                Debug.LogWarning($"[MicroPhaseSimUI] 村/魔物シミュレーションをスキップ: {villageException.Message}");
            }

            RefreshScreenLabels();
            Debug.Log(
                $"<color=#80CBC4><b>【Micro Phase UI】</b></color> {BuildPhaseLabel()} / {BuildSeasonLabel()} " +
                $"{BuildThreatLabel()} {BuildBarrierLabel()} markers={worldMarkers.Count}");
        }
        catch (System.Exception exception)
        {
            useOnGuiFallback = true;
            Debug.LogWarning($"[MicroPhaseSimUI] ApplyPhase Safe-Fail: {exception.Message}");
        }
    }

    /// <summary>工房・スポーン直上の Floating UI を作り直します。</summary>
    public void RebuildWorldMarkers()
    {
        ClearWorldMarkers();
        populator = ResolvePopulator();
        if (populator == null)
        {
            return;
        }

        EnsureWorldMarkerRoot();
        populator.CollectWorkshopPads(workshopPads);
        populator.CollectBeastSpawns(beastSpawns);

        for (int i = 0; i < workshopPads.Count; i++)
        {
            CreateMarker(workshopPads[i], WorldSpaceMarkerKind.Workshop, $"工房 {i + 1}");
        }

        for (int i = 0; i < beastSpawns.Count; i++)
        {
            CreateMarker(beastSpawns[i], WorldSpaceMarkerKind.BeastSpawn, $"魔獣スポーン {i + 1}");
        }

        ApplySeasonToMarkers(displayedSeason);
    }

    /// <summary>活性期のスポーン強調と休眠期の工房強調が切り替わるかを検証します。</summary>
    public bool RunHighlightSelfCheck(out string message)
    {
        ApplyPhase(1, rebuildMarkers: true);
        bool activeOk = displayedSeason == VariableTimelineSeason.Active &&
                        (worldMarkers.Count == 0 || AllMarkersMatch(WorldSpaceMarkerKind.BeastSpawn, emphasized: true));

        VariablePhaseDistribution dist = populator != null
            ? populator.BoundDistribution
            : timeline != null ? timeline.distribution : ProceduralMapPopulator.DefaultNation001Turn1Distribution();
        int dormantPhase = ProceduralMapPopulator.FirstDormantPhase(dist);
        ApplyPhase(dormantPhase, rebuildMarkers: false);
        bool dormantOk = displayedSeason == VariableTimelineSeason.Dormant &&
                         (worldMarkers.Count == 0 || AllMarkersMatch(WorldSpaceMarkerKind.Workshop, emphasized: true));

        bool advanced = RunAdvanceSelfCheck(out string advanceDetail);
        bool ok = activeOk && dormantOk && advanced;
        message =
            $"active={displayedSeason} activeOk={activeOk} dormantPhase={dormantPhase} dormantOk={dormantOk} " +
            $"advance=[{advanceDetail}] ugui={screenUiReady} markers={worldMarkers.Count}";
        return ok;
    }

    /// <summary>自己検証。位相が進み、マップとマーカーが同期しているかを返します。</summary>
    public bool RunAdvanceSelfCheck(out string message)
    {
        int before = currentPhase;
        VariableTimelineSeason seasonBefore = displayedSeason;
        AdvancePhase();
        bool phaseMoved = currentPhase != before;
        bool populatorSynced = populator == null || populator.LastAppliedPhase == currentPhase;
        bool seasonConsistent = displayedSeason ==
                                ProceduralMapPopulator.ResolveSeason(currentPhase, populator != null
                                    ? populator.BoundDistribution
                                    : timeline != null ? timeline.distribution : null);
        bool workshopHotWhenDormant = displayedSeason != VariableTimelineSeason.Dormant ||
                                      AllMarkersMatch(WorldSpaceMarkerKind.Workshop, emphasized: true);
        bool spawnHotWhenActive = displayedSeason != VariableTimelineSeason.Active ||
                                  AllMarkersMatch(WorldSpaceMarkerKind.BeastSpawn, emphasized: true);

        bool ok = phaseMoved && populatorSynced && seasonConsistent &&
                  workshopHotWhenDormant && spawnHotWhenActive && currentPhase >= 1;
        message =
            $"phase {before}->{currentPhase} season {seasonBefore}->{displayedSeason} " +
            $"threat={displayedThreat:F2} barrier={displayedBarrier:F2} " +
            $"markers={worldMarkers.Count} ugui={screenUiReady} " +
            $"populator={(populator != null ? "yes" : "none")}";
        return ok;
    }

    private void BindSimulation(bool forceRebuild)
    {
        if (timeline != null && !forceRebuild)
        {
            return;
        }

        try
        {
            timeline = MicroHistoryTimelineTimelineEngine.BuildTimeline(
                Mathf.Max(1, nationId),
                Mathf.Max(1, turn));
        }
        catch (System.Exception exception)
        {
            Debug.LogWarning($"[MicroPhaseSimUI] タイムライン構築失敗。均等 4 期を使います: {exception.Message}");
            timeline = null;
        }
    }

    private void ReadSlotParameters(int phase)
    {
        MicroPhaseSlot slot = FindSlot(phase);
        if (slot != null)
        {
            displayedSeason = slot.season;
            displayedThreat = slot.threatLevel;
            displayedBarrier = slot.barrierMaintenanceRate;
            return;
        }

        VariablePhaseDistribution dist = timeline != null
            ? timeline.distribution
            : ProceduralMapPopulator.DefaultNation001Turn1Distribution();
        displayedSeason = ProceduralMapPopulator.ResolveSeason(phase, dist);
        displayedThreat = FallbackThreat(displayedSeason);
        displayedBarrier = FallbackBarrier(displayedSeason);
    }

    private MicroPhaseSlot FindSlot(int phase)
    {
        if (timeline == null || timeline.phases == null)
        {
            return null;
        }

        for (int i = 0; i < timeline.phases.Length; i++)
        {
            MicroPhaseSlot slot = timeline.phases[i];
            if (slot != null && slot.phaseIndex == phase)
            {
                return slot;
            }
        }

        int index = Mathf.Clamp(phase - 1, 0, timeline.phases.Length - 1);
        return timeline.phases.Length > 0 ? timeline.phases[index] : null;
    }

    private ProceduralMapPopulator ResolvePopulator()
    {
        if (populator != null)
        {
            return populator;
        }

        populator = Object.FindAnyObjectByType<ProceduralMapPopulator>();
        return populator;
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
                GameObject canvasObject = new GameObject("MicroPhaseSimCanvas", typeof(RectTransform));
                canvasObject.transform.SetParent(transform, false);
                screenCanvas = canvasObject.AddComponent<Canvas>();
                screenCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
                screenCanvas.sortingOrder = 9300;
                CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                scaler.matchWidthOrHeight = 0.5f;
                canvasObject.AddComponent<GraphicRaycaster>();
            }

            RectTransform panel = CreatePanel(screenCanvas.transform);
            phaseText = CreateLabel("PhaseText", panel, new Vector2(16f, -14f), 22);
            seasonText = CreateLabel("SeasonText", panel, new Vector2(16f, -44f), 20);
            threatText = CreateLabel("ThreatText", panel, new Vector2(16f, -74f), 18);
            barrierText = CreateLabel("BarrierText", panel, new Vector2(16f, -100f), 18);
            nextPhaseButton = CreateButton(panel);

            screenUiReady = phaseText != null && seasonText != null &&
                            threatText != null && barrierText != null &&
                            nextPhaseButton != null && screenCanvas != null;
            useOnGuiFallback = !screenUiReady;
        }
        catch (System.Exception exception)
        {
            screenUiReady = false;
            useOnGuiFallback = true;
            Debug.LogWarning($"[MicroPhaseSimUI] uGUI 構築失敗。OnGUI へフォールバック: {exception.Message}");
        }
    }

    private void RefreshScreenLabels()
    {
        if (phaseText != null)
        {
            phaseText.text = BuildPhaseLabel();
        }

        if (seasonText != null)
        {
            seasonText.text = BuildSeasonLabel();
        }

        if (threatText != null)
        {
            threatText.text = BuildThreatLabel();
        }

        if (barrierText != null)
        {
            barrierText.text = BuildBarrierLabel();
        }
    }

    private string BuildPhaseLabel()
    {
        return $"Phase {currentPhase} / {PhaseCount}";
    }

    private string BuildSeasonLabel()
    {
        return displayedSeason.ToString().ToUpperInvariant();
    }

    private string BuildThreatLabel()
    {
        return $"Threat {displayedThreat:F2}";
    }

    private string BuildBarrierLabel()
    {
        return $"Barrier Efficiency {displayedBarrier * 100f:F0}%";
    }

    private void EnsureWorldMarkerRoot()
    {
        if (worldMarkerRoot != null)
        {
            return;
        }

        Transform existing = transform.Find("WorldSpaceMarkers");
        if (existing != null)
        {
            worldMarkerRoot = existing;
            return;
        }

        GameObject root = new GameObject("WorldSpaceMarkers");
        root.transform.SetParent(transform, false);
        worldMarkerRoot = root.transform;
    }

    private void CreateMarker(Transform target, WorldSpaceMarkerKind kind, string markerLabel)
    {
        if (target == null || worldMarkerRoot == null)
        {
            return;
        }

        try
        {
            GameObject markerObject = new GameObject($"WSM_{kind}_{markerLabel}");
            markerObject.transform.SetParent(worldMarkerRoot, true);
            WorldSpaceMarker marker = markerObject.AddComponent<WorldSpaceMarker>();
            marker.Initialize(target, kind, markerLabel);
            worldMarkers.Add(marker);
        }
        catch (System.Exception exception)
        {
            Debug.LogWarning($"[MicroPhaseSimUI] マーカー生成をスキップ: {exception.Message}");
        }
    }

    private void ApplySeasonToMarkers(VariableTimelineSeason season)
    {
        for (int i = 0; i < worldMarkers.Count; i++)
        {
            WorldSpaceMarker marker = worldMarkers[i];
            if (marker != null)
            {
                marker.ApplySeason(season);
            }
        }
    }

    private void ClearWorldMarkers()
    {
        for (int i = 0; i < worldMarkers.Count; i++)
        {
            WorldSpaceMarker marker = worldMarkers[i];
            if (marker == null)
            {
                continue;
            }

            if (Application.isPlaying)
            {
                Destroy(marker.gameObject);
            }
            else
            {
                DestroyImmediate(marker.gameObject);
            }
        }

        worldMarkers.Clear();
    }

    private bool AllMarkersMatch(WorldSpaceMarkerKind kind, bool emphasized)
    {
        bool any = false;
        for (int i = 0; i < worldMarkers.Count; i++)
        {
            WorldSpaceMarker marker = worldMarkers[i];
            if (marker == null || marker.Kind != kind)
            {
                continue;
            }

            any = true;
            if (marker.IsEmphasized != emphasized)
            {
                return false;
            }
        }

        return any;
    }

    private static RectTransform CreatePanel(Transform parent)
    {
        GameObject panelObject = new GameObject("MicroPhasePanel", typeof(RectTransform), typeof(Image));
        panelObject.transform.SetParent(parent, false);
        RectTransform rect = panelObject.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.sizeDelta = new Vector2(360f, 188f);
        rect.anchoredPosition = new Vector2(18f, -18f);
        Image backdrop = panelObject.GetComponent<Image>();
        backdrop.color = new Color(0.05f, 0.08f, 0.12f, 0.88f);
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

    private Button CreateButton(RectTransform parent)
    {
        GameObject buttonObject = new GameObject("NextPhaseButton", typeof(RectTransform), typeof(Image), typeof(Button));
        buttonObject.transform.SetParent(parent, false);
        RectTransform rect = buttonObject.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 0f);
        rect.anchorMax = new Vector2(0f, 0f);
        rect.pivot = new Vector2(0f, 0f);
        rect.anchoredPosition = new Vector2(16f, 14f);
        rect.sizeDelta = new Vector2(168f, 36f);

        Image image = buttonObject.GetComponent<Image>();
        image.color = new Color(0.18f, 0.55f, 0.62f, 0.95f);

        GameObject labelObject = new GameObject("Label", typeof(RectTransform));
        labelObject.transform.SetParent(buttonObject.transform, false);
        RectTransform labelRect = labelObject.GetComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;
        Text label = labelObject.AddComponent<Text>();
        RuntimeUIFontHelper.ApplyTo(label, 18, FontStyle.Bold, TextAnchor.MiddleCenter);
        label.text = "Next Phase";
        label.raycastTarget = false;

        Button button = buttonObject.GetComponent<Button>();
        button.targetGraphic = image;
        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(AdvancePhase);
        return button;
    }

    private static void EnsureEventSystem()
    {
        if (Object.FindAnyObjectByType<EventSystem>() != null)
        {
            return;
        }

        GameObject eventSystemObject = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        eventSystemObject.transform.SetParent(null, false);
    }

    private static float FallbackThreat(VariableTimelineSeason season)
    {
        switch (season)
        {
            case VariableTimelineSeason.Active:
                return 1.26f;
            case VariableTimelineSeason.Deescalation:
                return 0.98f;
            case VariableTimelineSeason.Dormant:
                return 0.66f;
            default:
                return 1.10f;
        }
    }

    private static float FallbackBarrier(VariableTimelineSeason season)
    {
        switch (season)
        {
            case VariableTimelineSeason.Active:
                return 0.84f;
            case VariableTimelineSeason.Deescalation:
                return 0.91f;
            case VariableTimelineSeason.Dormant:
                return 0.99f;
            default:
                return 0.88f;
        }
    }
}

/// <summary>Play 開始時に位相 UI を配置します。</summary>
public static class MicroPhaseSimUIBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoAttach()
    {
        MicroPhaseSimUI.EnsureInstance();
    }
}

#if UNITY_EDITOR
/// <summary>エディタから位相 UI とマップ連動を検証します。</summary>
public static class MicroPhaseSimUIMenu
{
    private const string VerifyLogPath = "Logs/micro_phase_ui_verify.txt";
    private const string VerifyRequestPath = "Logs/micro_phase_ui_verify.request";

    [InitializeOnLoadMethod]
    private static void VerifyWhenRequestFilePresent()
    {
        EditorApplication.delayCall += () =>
        {
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
            string request = Path.Combine(projectRoot, VerifyRequestPath);
            if (!File.Exists(request))
            {
                return;
            }

            try
            {
                File.Delete(request);
            }
            catch (System.Exception)
            {
                return;
            }

            VerifyPhaseUi(showDialog: false);
        };
    }

    [MenuItem("Tools/Procedural Map/Verify Phase UI")]
    public static void VerifyPhaseUiFromMenu()
    {
        VerifyPhaseUi(showDialog: true);
    }

    private static void VerifyPhaseUi(bool showDialog)
    {
        Nation001MapBuildResult map = Nation001ProceduralMapBuilder.GenerateNation001Map(1, 1);
        MicroPhaseSimUI ui = MicroPhaseSimUI.EnsureInstance();
        ui.ApplyPhase(1, rebuildMarkers: true);
        bool ok = ui.RunHighlightSelfCheck(out string detail);
        string line = $"{(ok && map.success ? "PASS" : "FAIL")} map={map.success} {detail}";
        WriteLog(line);
        if (showDialog)
        {
            EditorUtility.DisplayDialog("Phase UI", line, "OK");
        }
        else
        {
            Debug.Log($"<color=#80CBC4><b>【Phase UI 検証】</b></color> {line}");
        }
    }

    private static void WriteLog(string line)
    {
        try
        {
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
            string path = Path.Combine(projectRoot, VerifyLogPath);
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? projectRoot);
            File.WriteAllText(path, line + "\n", Encoding.UTF8);
        }
        catch (System.Exception exception)
        {
            Debug.LogWarning($"[MicroPhaseSimUI] 検証ログをスキップ: {exception.Message}");
        }
    }
}
#endif

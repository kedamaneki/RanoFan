using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

// =============================================================================
// 歴史シミュレーション解読 UI / プレゼンター
// 連携: HistorySimulationLoader / HistoryFlagRegistry / SkillEvolutionLinker / JobEvolutionManager
// =============================================================================

/// <summary>
/// 歴史ログを選択・解読し、conditionFlag を解禁したうえでスキル進化・ジョブ判定を再実行します。
/// </summary>
[DefaultExecutionOrder(46)]
public class HistoryDecodingPresenter : MonoBehaviour
{
    public const int DefaultVerifyTurn = 5;
    private const int MaxLogLines = 12;

    public static HistoryDecodingPresenter Instance { get; private set; }

    [Header("参照（未設定時は自動生成）")]
    [SerializeField] private Canvas rootCanvas;
    [SerializeField] private RectTransform panelRoot;
    [SerializeField] private Text titleText;
    [SerializeField] private Text loreText;
    [SerializeField] private Text logText;
    [SerializeField] private CanvasGroup panelCanvasGroup;

    [Header("解読対象")]
    [SerializeField] private int selectedTurn = DefaultVerifyTurn;

    private readonly List<string> decoderLogLines = new List<string>();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        HistoryFlagRegistry.EnsureWired();
        EnsureUiHierarchy();
        HidePanelImmediate();
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    /// <summary>シーンに無い場合は DebugSystemsHub へ生成して返します。</summary>
    public static HistoryDecodingPresenter EnsureInstance()
    {
        if (Instance != null)
        {
            return Instance;
        }

        GameObject hub = GameObject.Find("DebugSystemsHub");
        GameObject host = hub != null ? hub : new GameObject(nameof(HistoryDecodingPresenter));
        HistoryDecodingPresenter presenter = host.GetComponent<HistoryDecodingPresenter>();
        return presenter != null ? presenter : host.AddComponent<HistoryDecodingPresenter>();
    }

    /// <summary>歴史ログを選択表示します（フラグはまだ解禁しません）。</summary>
    public void SelectHistoryLog(int turn)
    {
        selectedTurn = turn;
        EraContextResolver.TrySetCurrentTurn(turn);
        List<HistorySimulationEventDto> events = HistorySimulationLoader.CollectEventsForTurn(turn);
        EnsureUiHierarchy();
        ShowPanel();

        if (titleText != null)
        {
            titleText.text = $"【歴史書】ターン {turn:000}";
        }

        if (loreText != null)
        {
            loreText.text = BuildLorePreview(turn, events);
        }

        if (events.Count == 0)
        {
            AppendDecoderLog($"ターン {turn} の正史ログは見つかりませんでした（Safe-Fail）。");
        }
    }

    /// <summary>
    /// 指定ターンの歴史ログを解読し、conditionFlag を解禁、進化・ジョブ判定を再実行します。
    /// </summary>
    public List<string> ExecuteHistoryDecoding(int turn)
    {
        HistoryFlagRegistry.EnsureWired();
        SelectHistoryLog(turn);

        List<string> flags = HistorySimulationLoader.ImportHistoryFlagsForTurn(turn);
        if (flags.Count == 0)
        {
            string emptyMessage =
                $"【歴史解読】ターン{turn}: 獲得フラグなし（JSON 未存在またはイベント無し）。古代技術/ジョブ条件は更新されていません。";
            PublishDecodingMessage(emptyMessage, isWarning: true);
            VerifyFlagsPropagated(turn, flags);
            return flags;
        }

        for (int i = 0; i < flags.Count; i++)
        {
            string flagKey = flags[i];
            string line =
                $"【歴史解読】ターン{turn}: フラグ「{flagKey}」を獲得！ 古代技術/ジョブ条件が更新されました";
            PublishDecodingMessage(line, isWarning: false);
        }

        ReevaluateSkillAndJobProgression();
        VerifyFlagsPropagated(turn, flags);
        return flags;
    }

    /// <summary>現在選択中ターンを解読します。</summary>
    public List<string> ExecuteSelectedHistoryDecoding()
    {
        return ExecuteHistoryDecoding(selectedTurn);
    }

    [ContextMenu("Decode Selected Turn")]
    private void DebugDecodeSelectedTurn()
    {
        ExecuteSelectedHistoryDecoding();
    }

    [ContextMenu("Safe-Fail Missing Turn 9999")]
    private void DebugDecodeMissingTurn()
    {
        ExecuteHistoryDecoding(9999);
    }

    [ContextMenu("Verify Era Turn 5 vs 200")]
    private void DebugVerifyEraTurns()
    {
        EraContextResolver.RunTurnComparisonSelfCheck();
    }

    /// <summary>所持スキルの進化判定と、現在ジョブの nextJobId 判定を再実行します。</summary>
    public static void ReevaluateSkillAndJobProgression()
    {
        PlayerSkillSlotManager slots = Object.FindAnyObjectByType<PlayerSkillSlotManager>();
        PlayerStatusManager status =
            PlayerStatusManager.Instance ?? Object.FindAnyObjectByType<PlayerStatusManager>();

        if (SkillEvolutionLinker.Instance == null)
        {
            GameObject hub = GameObject.Find("DebugSystemsHub");
            GameObject host = hub != null ? hub : new GameObject(nameof(SkillEvolutionLinker));
            if (host.GetComponent<SkillEvolutionLinker>() == null)
            {
                host.AddComponent<SkillEvolutionLinker>();
            }
        }

        int evolvedSkills = 0;
        if (slots != null)
        {
            List<string> ownedIds = new List<string>();
            IReadOnlyList<SkillData> owned = slots.OwnedSkills;
            for (int i = 0; i < owned.Count; i++)
            {
                if (owned[i] != null && !string.IsNullOrWhiteSpace(owned[i].skillID))
                {
                    ownedIds.Add(owned[i].skillID);
                }
            }

            for (int i = 0; i < ownedIds.Count; i++)
            {
                if (SkillEvolutionLinker.CheckAndExecuteEvolution(ownedIds[i]))
                {
                    evolvedSkills++;
                }
            }
        }

        bool jobEvolved = false;
        if (status != null && slots != null)
        {
            jobEvolved = JobEvolutionManager.TryEvolveToNextJob(status, slots);
        }

        Debug.Log(
            $"<color=#B39DDB>【歴史解読・再判定】スキル進化 {evolvedSkills} 件 / " +
            $"ジョブ進化={(jobEvolved ? "成功" : "条件未達または変化なし")}</color>");
    }

    private static void VerifyFlagsPropagated(int turn, List<string> flags)
    {
        HistoryFlagRegistry.EnsureWired();
        if (SkillEvolutionLinker.ConditionFlagResolver == null)
        {
            Debug.LogError("[歴史解読・検証] ConditionFlagResolver が未配線です。");
            return;
        }

        int resolved = 0;
        for (int i = 0; i < flags.Count; i++)
        {
            if (SkillEvolutionLinker.ConditionFlagResolver(flags[i]))
            {
                resolved++;
            }
            else
            {
                Debug.LogError($"[歴史解読・検証] フラグが Resolver で未成立: {flags[i]}");
            }
        }

        bool initial = SkillEvolutionLinker.ConditionFlagResolver(
            HistoryFlagRegistry.InitialDecodingCompleteFlag);
        bool missingSpineOk = flags.Count > 0 || turn < 1;

        if (flags.Count > 0 && resolved == flags.Count && initial)
        {
            Debug.Log(
                $"<color=#A5D6A7>【歴史解読・検証】ターン{turn} PASS — " +
                $"{resolved} 件が ConditionFlagResolver 経由で成立。HIST_INITIAL_DECODING_COMPLETE={initial}</color>");
        }
        else if (flags.Count == 0)
        {
            Debug.Log(
                $"<color=#FFE082>【歴史解読・検証】ターン{turn} はフラグ 0 件（未存在 JSON の Safe-Fail 想定可）</color>");
        }
        else
        {
            Debug.LogWarning(
                $"[歴史解読・検証] ターン{turn} 不完全: resolved={resolved}/{flags.Count} initial={initial} spineMissingHint={missingSpineOk}");
        }
    }

    private void PublishDecodingMessage(string line, bool isWarning)
    {
        if (isWarning)
        {
            Debug.LogWarning(line);
        }
        else
        {
            Debug.Log($"<color=#FFD54F><b>{line}</b></color>");
        }

        AppendDecoderLog(line);
        SkillEvolutionPresenter evolutionPresenter = SkillEvolutionPresenter.Instance
            ?? SkillEvolutionPresenter.EnsureInstance();
        evolutionPresenter?.PublishBattleLogLine(line);
    }

    private void AppendDecoderLog(string line)
    {
        decoderLogLines.Add(line);
        while (decoderLogLines.Count > MaxLogLines)
        {
            decoderLogLines.RemoveAt(0);
        }

        if (logText == null)
        {
            return;
        }

        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < decoderLogLines.Count; i++)
        {
            if (i > 0)
            {
                sb.AppendLine();
            }

            sb.Append(decoderLogLines[i]);
        }

        logText.text = sb.ToString();
    }

    private static string BuildLorePreview(int turn, List<HistorySimulationEventDto> events)
    {
        if (events == null || events.Count == 0)
        {
            return $"ターン {turn:000} の記録は書架に無い。";
        }

        StringBuilder sb = new StringBuilder();
        sb.AppendLine($"記録 {events.Count} 件");
        int previewCount = Mathf.Min(6, events.Count);
        for (int i = 0; i < previewCount; i++)
        {
            HistorySimulationEventDto evt = events[i];
            string title = string.IsNullOrWhiteSpace(evt.logMessageTemplate)
                ? evt.eventId
                : evt.logMessageTemplate;
            sb.AppendLine($"・{title}");
        }

        if (events.Count > previewCount)
        {
            sb.AppendLine($"…他 {events.Count - previewCount} 件");
        }

        return sb.ToString();
    }

    private void ShowPanel()
    {
        if (panelRoot != null)
        {
            panelRoot.gameObject.SetActive(true);
        }

        if (panelCanvasGroup != null)
        {
            panelCanvasGroup.alpha = 1f;
        }
    }

    private void HidePanelImmediate()
    {
        if (panelRoot != null)
        {
            panelRoot.gameObject.SetActive(false);
        }
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
            GameObject canvasObject = new GameObject("HistoryDecodingCanvas", typeof(RectTransform));
            canvasObject.transform.SetParent(transform, false);
            rootCanvas = canvasObject.AddComponent<Canvas>();
            rootCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            rootCanvas.sortingOrder = 9400;
            CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            canvasObject.AddComponent<GraphicRaycaster>();
        }

        if (panelRoot != null)
        {
            return;
        }

        GameObject panelObject = new GameObject("HistoryDecodingPanel", typeof(RectTransform), typeof(CanvasGroup), typeof(Image));
        panelObject.transform.SetParent(rootCanvas.transform, false);
        panelRoot = panelObject.GetComponent<RectTransform>();
        panelRoot.anchorMin = new Vector2(1f, 1f);
        panelRoot.anchorMax = new Vector2(1f, 1f);
        panelRoot.pivot = new Vector2(1f, 1f);
        panelRoot.sizeDelta = new Vector2(520f, 420f);
        panelRoot.anchoredPosition = new Vector2(-24f, -24f);

        Image backdrop = panelObject.GetComponent<Image>();
        backdrop.color = new Color(0.05f, 0.07f, 0.12f, 0.92f);
        backdrop.raycastTarget = false;
        panelCanvasGroup = panelObject.GetComponent<CanvasGroup>();

        titleText = CreateBodyText("Title", panelRoot, new Vector2(16f, -16f), new Vector2(-16f, -52f), 22, TextAnchor.UpperLeft);
        loreText = CreateBodyText("Lore", panelRoot, new Vector2(16f, -56f), new Vector2(-16f, -200f), 16, TextAnchor.UpperLeft);
        logText = CreateBodyText("Log", panelRoot, new Vector2(16f, -210f), new Vector2(-16f, -16f), 15, TextAnchor.LowerLeft);
        logText.color = new Color(1f, 0.92f, 0.7f, 1f);
    }

    private static Text CreateBodyText(
        string name,
        RectTransform parent,
        Vector2 offsetMin,
        Vector2 offsetMax,
        int fontSize,
        TextAnchor anchor)
    {
        GameObject textObject = new GameObject(name, typeof(RectTransform));
        textObject.transform.SetParent(parent, false);
        RectTransform rect = textObject.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;

        Text text = textObject.AddComponent<Text>();
        RuntimeUIFontHelper.ApplyTo(text, fontSize, FontStyle.Bold, anchor);
        text.color = Color.white;
        text.supportRichText = true;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Truncate;
        return text;
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
}

/// <summary>Play 開始時に歴史解読プレゼンターを配置し、Resolver を配線します。</summary>
public static class HistoryDecodingPresenterBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoAttach()
    {
        HistoryFlagRegistry.EnsureWired();
        HistoryDecodingPresenter.EnsureInstance();
        EraContextResolver.EnsureInstance();
        EraContextResolver.RunTurnComparisonSelfCheck();
    }
}

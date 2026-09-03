using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

// =============================================================================
// ストーリータイムライン統治 — モード別タイムスケール・激動年ドラマフラグ
// =============================================================================

/// <summary>ゲーム進行モード（タイムスケール制御）。</summary>
public enum GameMode
{
    /// <summary>1日/1位相がプレイヤー行動単位で不可逆進行。</summary>
    StoryMode = 0,

    /// <summary>防衛成功・高品質クラフトで ALT 分岐フラグを発行。</summary>
    IFMode = 1,

    /// <summary>CurrentTurn を固定し時間の自動進行を停止。</summary>
    PseudoMMO = 2
}

/// <summary>激動年に配置するラノベ風ドラマ種別。</summary>
public enum SideQuestDramaKind
{
    BarrierStakeCrisis = 0,
    NamedBeastAttack = 1,
    DesperateFieldRepair = 2,
    VictimSupport = 3,
    EmergencySupply = 4,
    OverforgeDiscovery = 5,
    AncientTechAwakening = 6
}

/// <summary>1 位相に配置されたサイドクエスト／ドラマフラグ。</summary>
[Serializable]
public sealed class SideQuestFlag
{
    public int turn;
    public int phase;
    public VariableTimelineSeason season;
    public SideQuestDramaKind dramaKind;
    public string dramaTitle = string.Empty;
    public string flagKey = string.Empty;
    public string narrative = string.Empty;
    public bool emitted;

    public string FormatLogLine()
    {
        return $"位相{phase}/24 {season.ToString().ToUpperInvariant()} [{dramaTitle}] {narrative} FLAG={flagKey}";
    }
}

/// <summary>ストーリー年検証結果。</summary>
[Serializable]
public sealed class StoryTimelineVerifyResult
{
    public bool success;
    public string message = string.Empty;
    public int emittedDramas;
    public int turbulentPhases;
    public bool turbulentYear;
}

/// <summary>
/// 特定1年（24位相）の高密度イベント・ラノベ風ドラマを統治し、
/// Story / IF / PseudoMMO のタイムスケールを切り替えます。
/// </summary>
[DefaultExecutionOrder(48)]
public class StoryTimelineManager : MonoBehaviour
{
    public const string LogTag = "【ストーリータイムライン】";
    public const float TurbulentThreatThreshold = 2.45f;
    public const int TurbulentEventCountMin = 4;
    public const float HighQualityCraftThreshold = 0.82f;

    public static StoryTimelineManager Instance { get; private set; }

    [SerializeField] private GameMode gameMode = GameMode.StoryMode;
    [SerializeField] private int storyTurn = MicroHistoryTimelineTimelineEngine.DefaultTurn;
    [SerializeField] private int lockedTurn = MicroHistoryTimelineTimelineEngine.DefaultTurn;
    [SerializeField] private bool forceTurbulentYear = false;
    [SerializeField] private int lastStoryPhase = 0;
    [SerializeField] private bool yearCompleted = false;
    [SerializeField] private bool turbulentYearActive = false;
    [SerializeField] private List<SideQuestFlag> turbulentSchedule = new List<SideQuestFlag>();
    [SerializeField] private List<SideQuestFlag> emittedFlags = new List<SideQuestFlag>();

    public GameMode CurrentMode => gameMode;
    public int StoryTurn => storyTurn;
    public bool IsTurbulentYear => turbulentYearActive;
    public bool YearCompleted => yearCompleted;
    public int LastStoryPhase => lastStoryPhase;
    public IReadOnlyList<SideQuestFlag> EmittedFlags => emittedFlags;

    public static StoryTimelineManager EnsureInstance()
    {
        if (Instance != null)
        {
            return Instance;
        }

        StoryTimelineManager existing = FindAnyObjectByType<StoryTimelineManager>();
        if (existing != null)
        {
            Instance = existing;
            return existing;
        }

        GameObject hub = GameObject.Find("DebugSystemsHub");
        GameObject host = hub != null ? hub : new GameObject(nameof(StoryTimelineManager));
        StoryTimelineManager mgr = host.GetComponent<StoryTimelineManager>();
        return mgr != null ? mgr : host.AddComponent<StoryTimelineManager>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        TryBootstrapStoryYear(storyTurn, gameMode);
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    /// <summary>モードとターンで年次タイムラインを初期化します。</summary>
    public bool TryBootstrapStoryYear(int turn, GameMode mode)
    {
        try
        {
            gameMode = mode;
            storyTurn = Mathf.Max(1, turn);
            lastStoryPhase = 0;
            yearCompleted = false;
            emittedFlags.Clear();
            turbulentSchedule.Clear();

            EraContextResolver.EnsureInstance();
            if (mode == GameMode.PseudoMMO)
            {
                lockedTurn = storyTurn;
                EraContextResolver.TrySetCurrentTurn(lockedTurn);
            }
            else
            {
                EraContextResolver.TrySetCurrentTurn(storyTurn);
            }

            turbulentYearActive = forceTurbulentYear || EvaluateIsTurbulentYear(storyTurn);
            if (turbulentYearActive)
            {
                BuildTurbulentSchedule(storyTurn);
            }

            HistoryFlagRegistry.EnsureWired();
            HistoryCausalValidator.EnsureInstance().ResetYearTracking();
            Debug.Log(
                $"<color=#FFB74D><b>{LogTag}</b></color> 初期化 " +
                $"Mode={mode} Turn={storyTurn} 激動年={turbulentYearActive} " +
                $"ドラマ予定={turbulentSchedule.Count}件 " +
                $"CurrentTurn={EraContextResolver.CurrentTurn}");
            return true;
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"{LogTag} 初期化 Safe-Fail: {exception.Message}");
            turbulentYearActive = false;
            return false;
        }
    }

    /// <summary>位相ティック前に呼び出し。不可逆・PseudoMMO 停止を検証します。</summary>
    public static bool TryPreparePhaseTick(int phase, VariableTimelineSeason season)
    {
        try
        {
            StoryTimelineManager mgr = EnsureInstance();
            return mgr.PreparePhaseTick(phase, season);
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"{LogTag} Prepare Safe-Fail: {exception.Message}");
            return true;
        }
    }

    /// <summary>位相ティック後にドラマフラグを出力します。</summary>
    public static void TryEmitPhaseDrama(int phase, VariableTimelineSeason season)
    {
        try
        {
            StoryTimelineManager mgr = EnsureInstance();
            mgr.EmitPhaseDrama(phase, season);
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"{LogTag} Emit Safe-Fail: {exception.Message}");
        }
    }

    public bool PreparePhaseTick(int phase, VariableTimelineSeason season)
    {
        if (gameMode == GameMode.PseudoMMO)
        {
            EraContextResolver.TrySetCurrentTurn(lockedTurn);
        }

        int normalized = ProceduralMapPopulator.NormalizePhase(phase);
        if (yearCompleted && normalized <= ProceduralMapPopulator.PhaseCount)
        {
            return false;
        }

        if (gameMode == GameMode.StoryMode && normalized < lastStoryPhase)
        {
            Debug.LogWarning(
                $"{LogTag} StoryMode 不可逆: 位相{normalized} は既過位相{lastStoryPhase}より前 — スキップ。");
            return false;
        }

        return true;
    }

    public void EmitPhaseDrama(int phase, VariableTimelineSeason season)
    {
        int normalized = ProceduralMapPopulator.NormalizePhase(phase);
        if (!PreparePhaseTick(normalized, season))
        {
            RunBasicDayFallback(normalized, season);
            return;
        }

        lastStoryPhase = Mathf.Max(lastStoryPhase, normalized);
        if (normalized >= ProceduralMapPopulator.PhaseCount)
        {
            yearCompleted = true;
        }

        if (!turbulentYearActive || turbulentSchedule == null || turbulentSchedule.Count == 0)
        {
            return;
        }

        for (int i = 0; i < turbulentSchedule.Count; i++)
        {
            SideQuestFlag flag = turbulentSchedule[i];
            if (flag == null || flag.emitted || flag.phase != normalized)
            {
                continue;
            }

            flag.emitted = true;
            flag.season = season;
            emittedFlags.Add(flag);

            CausalEventProposal proposal = HistoryCausalValidator.BuildFromSideQuest(flag, season);
            if (proposal == null || !HistoryCausalValidator.TryEmitValidatedEvent(proposal))
            {
                flag.emitted = false;
                if (emittedFlags.Count > 0)
                {
                    emittedFlags.RemoveAt(emittedFlags.Count - 1);
                }

                continue;
            }
        }
    }

    /// <summary>IFMode: 防衛成功時に ALT 分岐フラグを発行します。</summary>
    public static void TryNotifyDefenseSuccess(int nationId, int phase, string detail = null)
    {
        try
        {
            StoryTimelineManager mgr = EnsureInstance();
            if (mgr.gameMode != GameMode.IFMode)
            {
                return;
            }

            int turn = EraContextResolver.CurrentTurn;
            string flagKey = $"ALT_NATION_{nationId:D3}_DEFENSE_T{turn:D3}";
            HistoryFlagRegistry.Unlock(flagKey);
            HistoryBranchManager.RegisterHistoryAlteration(
                turn,
                nationId,
                $"HIST_NATION_{nationId:D3}_GEO_TURN_{turn:D3}_HERO",
                flagKey);
            string line = string.IsNullOrWhiteSpace(detail) ? "結界防衛成功" : detail;
            Debug.Log(
                $"<color=#FFB74D><b>{LogTag} IF分岐</b></color> 位相{phase}/24 {line} ALT_FLAG={flagKey}");
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"{LogTag} IF防衛 Safe-Fail: {exception.Message}");
        }
    }

    /// <summary>IFMode: 高品質クラフト納品時に ALT 分岐フラグを発行します。</summary>
    public static void TryNotifyHighQualityCraft(int nationId, float quality, string itemLabel = null)
    {
        try
        {
            StoryTimelineManager mgr = EnsureInstance();
            if (mgr.gameMode != GameMode.IFMode || quality < HighQualityCraftThreshold)
            {
                return;
            }

            int turn = EraContextResolver.CurrentTurn;
            string flagKey = $"ALT_NATION_{nationId:D3}_CRAFT_T{turn:D3}";
            HistoryFlagRegistry.Unlock(flagKey);
            HistoryBranchManager.RegisterHistoryAlteration(
                turn,
                nationId,
                $"HIST_NATION_{nationId:D3}_GEO_TURN_{turn:D3}_INNOVATION",
                flagKey);
            string label = string.IsNullOrWhiteSpace(itemLabel) ? "高品質クラフト" : itemLabel;
            Debug.Log(
                $"<color=#FFB74D><b>{LogTag} IF分岐</b></color> {label} 品質{quality.ToString("F2", CultureInfo.InvariantCulture)} " +
                $"ALT_FLAG={flagKey}");
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"{LogTag} IFクラフト Safe-Fail: {exception.Message}");
        }
    }

    /// <summary>PseudoMMO: 時間自動進行が許可されるか。</summary>
    public bool IsAutoTimeAdvanceAllowed()
    {
        return gameMode != GameMode.PseudoMMO;
    }

    public void SetGameMode(GameMode mode)
    {
        gameMode = mode;
        if (mode == GameMode.PseudoMMO)
        {
            lockedTurn = storyTurn;
            EraContextResolver.TrySetCurrentTurn(lockedTurn);
        }
    }

    public static bool EvaluateIsTurbulentYear(int turn)
    {
        try
        {
            string logPath = MicroHistoryTimelineTimelineEngine.ResolveLogPath();
            List<MacroChronicleLogEvent> events = MacroChronicleJsonScan.ReadTurnEvents(logPath, turn);
            if (events == null || events.Count == 0)
            {
                return turn == MicroHistoryTimelineTimelineEngine.DefaultTurn;
            }

            float maxThreat = 0f;
            int conflictEvents = 0;
            for (int i = 0; i < events.Count; i++)
            {
                MacroChronicleLogEvent evt = events[i];
                if (evt == null)
                {
                    continue;
                }

                if (evt.globalThreatIndex > maxThreat)
                {
                    maxThreat = evt.globalThreatIndex;
                }

                string category = evt.category ?? string.Empty;
                if (category.IndexOf("monster", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    category.IndexOf("attack", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    category.IndexOf("defense", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    category.IndexOf("threat", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    conflictEvents++;
                }
            }

            return maxThreat >= TurbulentThreatThreshold || conflictEvents >= TurbulentEventCountMin;
        }
        catch (Exception)
        {
            return turn == MicroHistoryTimelineTimelineEngine.DefaultTurn;
        }
    }

    private void BuildTurbulentSchedule(int turn)
    {
        turbulentSchedule = new List<SideQuestFlag>
        {
            CreateDrama(turn, 2, SideQuestDramaKind.BarrierStakeCrisis,
                "結界杭破綻の危機",
                "西の結界杭が軋み、守護線に亀裂が走る。結界守が駆けつける。"),
            CreateDrama(turn, 4, SideQuestDramaKind.NamedBeastAttack,
                "ネームド魔獣襲来",
                "『黒翼の好景』が低空を掠め、村の防壁に爪痕を残す。"),
            CreateDrama(turn, 7, SideQuestDramaKind.DesperateFieldRepair,
                "決死の現地修復",
                "大工と結界守が雪原の現地で杭を打ち直し、夜を徹する。"),
            CreateDrama(turn, 10, SideQuestDramaKind.NamedBeastAttack,
                "ネームド魔獣襲来",
                "第二波の襲来。若手が怯え、ベテランが前に立つ。"),
            CreateDrama(turn, 12, SideQuestDramaKind.BarrierStakeCrisis,
                "結界杭破綻の危機",
                "活性期末盤、結界効率が急落。全員が防衛ラインへ。"),
            CreateDrama(turn, 13, SideQuestDramaKind.VictimSupport,
                "被災者の支援",
                "隣村から避難民が流入。宿屋と倉庫が一時避難所になる。"),
            CreateDrama(turn, 14, SideQuestDramaKind.EmergencySupply,
                "食料/木材の緊急配給",
                "倉庫から緊急配給が始まり、配給票が村を走る。"),
            CreateDrama(turn, 15, SideQuestDramaKind.VictimSupport,
                "被災者の支援",
                "負傷者の手当てと、失われた家屋の名簿作成。"),
            CreateDrama(turn, 16, SideQuestDramaKind.EmergencySupply,
                "食料/木材の緊急配給",
                "木材を削って仮設防壁を組み、冬風を凌ぐ。"),
            CreateDrama(turn, 18, SideQuestDramaKind.OverforgeDiscovery,
                "過熱精錬での新素材発見",
                "工房の炉が紅く脈打ち、未知の合金が坩堝底に現れる。"),
            CreateDrama(turn, 19, SideQuestDramaKind.AncientTechAwakening,
                "古代魔導技術の覚醒",
                "休眠の古譜が自己解読し、失われた術式が一瞬だけ蘇る。"),
            CreateDrama(turn, 20, SideQuestDramaKind.OverforgeDiscovery,
                "過熱精錬での新素材発見",
                "新素材の試作片が結界結晶と共鳴し、魔力流量が変化する。")
        };
    }

    private static SideQuestFlag CreateDrama(
        int turn,
        int phase,
        SideQuestDramaKind kind,
        string title,
        string narrative)
    {
        string kindSlug = kind.ToString();
        return new SideQuestFlag
        {
            turn = turn,
            phase = phase,
            dramaKind = kind,
            dramaTitle = title,
            narrative = narrative,
            flagKey = $"STORY_DRAMA_{kindSlug}_T{turn:D3}_P{phase:D2}"
        };
    }

    private static void RunBasicDayFallback(int phase, VariableTimelineSeason season)
    {
        Debug.Log(
            $"<color=#FFB74D><b>{LogTag}</b></color> 基本1日ループ 位相{phase}/24 {season.ToString().ToUpperInvariant()}（イベントデータ欠損フォールバック）");
    }

    public static StoryTimelineVerifyResult RunStoryModeDayVerification()
    {
        StoryTimelineVerifyResult result = new StoryTimelineVerifyResult();
        try
        {
            StoryTimelineManager mgr = EnsureInstance();
            mgr.forceTurbulentYear = false;
            mgr.TryBootstrapStoryYear(MicroHistoryTimelineTimelineEngine.DefaultTurn, GameMode.StoryMode);

            NpcCivilizationEngine civ = NpcCivilizationEngine.EnsureInstance();
            VariablePhaseDistribution dist = ProceduralMapPopulator.DefaultNation001Turn1Distribution();
            MicroToMacroAggregator.EnsureInstance().BeginYearBaseline();

            for (int phase = 1; phase <= ProceduralMapPopulator.PhaseCount; phase++)
            {
                VariableTimelineSeason season = ProceduralMapPopulator.ResolveSeason(phase, dist);
                civ.TickPhase(phase, season);
            }

            result.turbulentYear = mgr.turbulentYearActive;
            result.emittedDramas = mgr.emittedFlags?.Count ?? 0;
            result.turbulentPhases = mgr.turbulentSchedule?.Count ?? 0;
            result.success = mgr.IsTurbulentYear &&
                             result.emittedDramas >= 7 &&
                             mgr.lastStoryPhase >= ProceduralMapPopulator.PhaseCount;
            result.message =
                $"mode=StoryMode turn={mgr.storyTurn} turbulent={result.turbulentYear} " +
                $"dramas={result.emittedDramas}/{result.turbulentPhases} phase={mgr.lastStoryPhase}";
        }
        catch (Exception exception)
        {
            result.success = false;
            result.message = $"Safe-Fail: {exception.Message}";
        }

        return result;
    }
}

public static class StoryTimelineManagerBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoAttach()
    {
        StoryTimelineManager.EnsureInstance();
    }
}

#if UNITY_EDITOR
public static class StoryTimelineManagerMenu
{
    private const string VerifyLogPath = "Logs/story_timeline_verify.txt";

    [MenuItem("Tools/Procedural Map/Run Story Mode Turbulent Year")]
    public static void RunStoryModeTurbulentYear()
    {
        StoryTimelineVerifyResult result = StoryTimelineManager.RunStoryModeDayVerification();
        try
        {
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
            string path = Path.Combine(projectRoot, VerifyLogPath);
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? projectRoot);
            File.WriteAllText(path, result.message + "\n", Encoding.UTF8);
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[StoryTimelineManager] 検証ログをスキップ: {exception.Message}");
        }

        Debug.Log(
            result.success
                ? $"<color=#FFB74D><b>{StoryTimelineManager.LogTag}・検証】PASS</b></color> {result.message}"
                : $"<color=#FF8A80><b>{StoryTimelineManager.LogTag}・検証】FAIL</b></color> {result.message}");
        EditorUtility.DisplayDialog("Story Timeline", result.message, "OK");
    }

    /// <summary>Unity バッチ: -executeMethod StoryTimelineManagerMenu.BatchVerifyAndQuit</summary>
    public static void BatchVerifyAndQuit()
    {
        StoryTimelineVerifyResult result = StoryTimelineManager.RunStoryModeDayVerification();
        try
        {
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
            string path = Path.Combine(projectRoot, VerifyLogPath);
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? projectRoot);
            File.WriteAllText(path, result.message + "\n", Encoding.UTF8);
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[StoryTimelineManager] 検証ログをスキップ: {exception.Message}");
        }

        Debug.Log(
            result.success
                ? $"<color=#FFB74D><b>{StoryTimelineManager.LogTag}・検証】PASS</b></color> {result.message}"
                : $"<color=#FF8A80><b>{StoryTimelineManager.LogTag}・検証】FAIL</b></color> {result.message}");
        EditorApplication.Exit(0);
    }
}
#endif

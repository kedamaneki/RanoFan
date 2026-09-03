using System.Collections.Generic;
using System.Text;
using UnityEngine;
#if UNITY_EDITOR
using System.IO;
using UnityEditor;
#endif

// =============================================================================
// 村自律シミュレーション — プレイヤー操作なしで NPC が生産・結界維持
// 連携: NpcJobProfile / VillageResourceStatus / VillageBarrierCore
//       EraContextResolver / MicroPhaseSimUI / ProceduralMapPopulator
// =============================================================================

/// <summary>
/// 24 位相の進行に合わせて社会生活ジョブの NPC を自律計算します。
/// </summary>
[DefaultExecutionOrder(52)]
public class VillageAutonomyEngine : MonoBehaviour
{
    public static VillageAutonomyEngine Instance { get; private set; }

    [SerializeField] private VillageResourceStatus resources = new VillageResourceStatus();
    [SerializeField] private VillageBarrierCore barrier = new VillageBarrierCore();
    [SerializeField] private List<NpcJobProfile> roster = new List<NpcJobProfile>();
    [SerializeField] private int lastTickedPhase = -1;

    public VillageResourceStatus Resources => resources ?? (resources = new VillageResourceStatus());
    public VillageBarrierCore Barrier => barrier ?? (barrier = new VillageBarrierCore());
    public IReadOnlyList<NpcJobProfile> Roster => roster;
    public int LastTickedPhase => lastTickedPhase;

    /// <summary>シーンに無ければ DebugSystemsHub へ生成します。</summary>
    public static VillageAutonomyEngine EnsureInstance()
    {
        if (Instance != null)
        {
            return Instance;
        }

        VillageAutonomyEngine existing = Object.FindAnyObjectByType<VillageAutonomyEngine>();
        if (existing != null)
        {
            return existing;
        }

        GameObject hub = GameObject.Find("DebugSystemsHub");
        GameObject host = hub != null ? hub : new GameObject(nameof(VillageAutonomyEngine));
        VillageAutonomyEngine engine = host.GetComponent<VillageAutonomyEngine>();
        return engine != null ? engine : host.AddComponent<VillageAutonomyEngine>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        EnsureDefaults();
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    /// <summary>
    /// 1 位相をシミュレートします。同一位相の連打は無視しません（再計算可）。
    /// 生活 AI 統合版は NpcCivilizationEngine.TickPhase を使ってください。
    /// </summary>
    public void TickPhase(int phase, VariableTimelineSeason season)
    {
        try
        {
            EnsureDefaults();
            EraContextResolver.EnsureInstance();
            int normalized = ProceduralMapPopulator.NormalizePhase(phase);
            float tech = ResolveMagicTechMultiplier();
            VillageResourceStatus stock = Resources;
            VillageBarrierCore core = Barrier;

            core.ApplySeasonPressure(season);

            StringBuilder log = new StringBuilder();
            log.Append("<color=#A5D6A7><b>【村自律】</b></color> ");
            log.Append($"位相 {normalized}/24 {season.ToString().ToUpperInvariant()} ");
            log.Append($"魔導技術×{tech:F2} ({EraContextResolver.FormatEraLabel(EraContextResolver.CurrentEra)})");

            int safeFails = 0;

            for (int i = 0; i < roster.Count; i++)
            {
                NpcJobProfile npc = roster[i];
                if (npc == null)
                {
                    continue;
                }

                VillageResourceDelta delta = npc.SimulatePhase(normalized, season, tech, stock);
                float consumeScale = delta.usedSafeFail ? VillageResourceStatus.DepletionEfficiency : 1f;
                if (delta.usedSafeFail)
                {
                    safeFails++;
                }

                if (npc.job == NpcCivicJob.BarrierKeeper)
                {
                    float requested = Mathf.Abs(Mathf.Min(0f, delta.manaCrystal));
                    float spent = stock.ConsumeUpTo(ref stock.ManaCrystal, requested * consumeScale);
                    bool keeperShortage = delta.usedSafeFail || spent + 0.0001f < requested;
                    float recovered = core.RestoreFromKeeper(spent, tech, keeperShortage);
                    log.AppendLine();
                    log.Append(
                        $"  {npc.displayName}（{NpcJobProfile.JobLabel(npc.job)}）{delta.actionLabel} " +
                        $"結晶消費 {spent:F2} 結界 +{recovered:F1}% → {core.Efficiency:F1}%");
                    continue;
                }

                try
                {
                    if (npc.job == NpcCivicJob.Woodcutter)
                    {
                        WorkSpotData camp = VillageWorkSpotManager.EnsureInstance()
                            .FindNearestWoodcutterCamp(Vector3.zero);
                        VillageStorageMarket market = NpcCivilizationEngine.EnsureInstance().Storage;
                        WoodcutterHarvestResult harvest = NaturalEcologyEngine.EnsureInstance()
                            .HarvestAtCamp(camp, npc.skill, market);
                        delta.wood *= harvest.QualityMultiplier;
                        if (harvest.IsManaMutated && !string.IsNullOrEmpty(harvest.Label))
                        {
                            delta.actionLabel = harvest.Label;
                        }
                    }
                    else if (npc.job == NpcCivicJob.Rancher || npc.job == NpcCivicJob.Farmer)
                    {
                        NaturalEcologyEngine.EnsureInstance()
                            .ResolveWildlifeFoodMultiplier(npc.job == NpcCivicJob.Rancher);
                    }
                }
                catch (System.Exception)
                {
                    // 自然生態未配置でも生産は継続（通常材フォールバック）
                }

                stock.Apply(delta, consumeScale);

                log.AppendLine();
                log.Append(
                    $"  {npc.displayName}（{NpcJobProfile.JobLabel(npc.job)}）{delta.actionLabel}");
            }

            lastTickedPhase = normalized;
            log.AppendLine();
            log.Append($"  在庫 {stock.FormatSnapshot()} / 結界 {core.Efficiency:F1}%");
            if (core.BarrierDropped)
            {
                log.Append(" / <b>BarrierDrop</b>");
            }

            if (safeFails > 0)
            {
                log.Append($" / Safe-Fail {safeFails}件");
            }

            Debug.Log(log.ToString());
        }
        catch (System.Exception exception)
        {
            Debug.LogWarning($"[VillageAutonomyEngine] TickPhase Safe-Fail: {exception.Message}");
        }
    }

    /// <summary>1 日（24 位相）を連続実行します。</summary>
    public void SimulateFullDay(VariablePhaseDistribution distribution)
    {
        VariablePhaseDistribution dist = distribution ?? ProceduralMapPopulator.DefaultNation001Turn1Distribution();
        for (int phase = 1; phase <= ProceduralMapPopulator.PhaseCount; phase++)
        {
            VariableTimelineSeason season = ProceduralMapPopulator.ResolveSeason(phase, dist);
            TickPhase(phase, season);
        }
    }

    /// <summary>
    /// 魔導技術解禁状況に応じた生産・結界回復倍率（1.0〜2.5）。
    /// 普遍期 1.0、過渡期はターンで 1.0〜1.45、古代化期は EraContextResolver の 1.5〜2.5。
    /// </summary>
    public static float ResolveMagicTechMultiplier()
    {
        try
        {
            EraTag era = EraContextResolver.CurrentEra;
            int turn = EraContextResolver.CurrentTurn;
            switch (era)
            {
                case EraTag.Mid:
                    float midT = Mathf.InverseLerp(
                        EraContextResolver.EarlyEraMaxTurn + 1,
                        EraContextResolver.MidEraMaxTurn,
                        turn);
                    return Mathf.Clamp(Mathf.Lerp(1.05f, 1.45f, midT), 1f, 2.5f);
                case EraTag.Late:
                    return Mathf.Clamp(EraContextResolver.ResolveAncientMultiplier(0f), 1.5f, 2.5f);
                default:
                    return 1f;
            }
        }
        catch (System.Exception)
        {
            return 1f;
        }
    }

    private void EnsureDefaults()
    {
        if (resources == null)
        {
            resources = new VillageResourceStatus();
        }

        if (barrier == null)
        {
            barrier = new VillageBarrierCore();
        }

        if (roster != null && roster.Count > 0)
        {
            return;
        }

        roster = new List<NpcJobProfile>
        {
            new NpcJobProfile("npc_farmer_a", "ハル", NpcCivicJob.Farmer, 1.05f),
            new NpcJobProfile("npc_farmer_b", "ナギ", NpcCivicJob.Farmer, 0.92f),
            new NpcJobProfile("npc_wood_a", "トガ", NpcCivicJob.Woodcutter, 1.1f),
            new NpcJobProfile("npc_ranch_a", "シキ", NpcCivicJob.Rancher, 1.0f),
            new NpcJobProfile("npc_smith_a", "カネ", NpcCivicJob.Blacksmith, 1.08f),
            new NpcJobProfile("npc_keeper_a", "ミサ", NpcCivicJob.BarrierKeeper, 1.15f)
        };
    }
}

/// <summary>Play 開始時に村シミュレーションを配置します。</summary>
public static class VillageAutonomyEngineBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoAttach()
    {
        VillageAutonomyEngine.EnsureInstance();
    }
}

#if UNITY_EDITOR
/// <summary>24 位相の自律ログをエディタから確認します。</summary>
public static class VillageAutonomyEngineMenu
{
    private const string VerifyLogPath = "Logs/village_autonomy_verify.txt";

    [MenuItem("Tools/Procedural Map/Run Village Autonomy Day")]
    public static void RunVillageDayFromMenu()
    {
        VillageAutonomyEngine engine = VillageAutonomyEngine.EnsureInstance();
        engine.SimulateFullDay(ProceduralMapPopulator.DefaultNation001Turn1Distribution());
        string line =
            $"PASS phase={engine.LastTickedPhase} {engine.Resources.FormatSnapshot()} " +
            $"barrier={engine.Barrier.Efficiency:F1} dropped={engine.Barrier.BarrierDropped} " +
            $"tech={VillageAutonomyEngine.ResolveMagicTechMultiplier():F2}";
        try
        {
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
            string path = Path.Combine(projectRoot, VerifyLogPath);
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? projectRoot);
            File.WriteAllText(path, line + "\n", Encoding.UTF8);
        }
        catch (System.Exception exception)
        {
            Debug.LogWarning($"[VillageAutonomyEngine] 検証ログをスキップ: {exception.Message}");
        }

        Debug.Log($"<color=#A5D6A7><b>【村自律・1日完了】</b></color> {line}");
        EditorUtility.DisplayDialog("Village Autonomy", line, "OK");
    }
}
#endif

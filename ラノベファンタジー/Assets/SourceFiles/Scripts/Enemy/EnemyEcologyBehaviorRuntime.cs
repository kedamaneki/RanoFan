using System.Collections.Generic;
using System.Text;
using UnityEngine;
#if UNITY_EDITOR
using System.IO;
using UnityEditor;
#endif

// =============================================================================
// 魔物生態シミュレーション基盤 — 共通ステータス + 本能 AI の位相駆動
//
// 注意: Scripts/EnemyBehaviorProfileManager（static）は攻撃コンボ JSON ローダ。
//       生態・本能の行動選択ランタイムは本クラス EnemyEcologyBehaviorRuntime。
//       （同名 MonoBehaviour にすると CS0101 になるため分離）
// =============================================================================

/// <summary>
/// 複数魔物を位相・結界・村倉庫と連携させて自律駆動します。
/// 要件の「生態系・本能ベース行動選択」のエントリポイントです。
/// </summary>
[DefaultExecutionOrder(50)]
public class EnemyEcologyBehaviorRuntime : MonoBehaviour
{
    public static EnemyEcologyBehaviorRuntime Instance { get; private set; }

    [SerializeField] private List<EnemyEcologyInstinctAI> pack = new List<EnemyEcologyInstinctAI>();
    [SerializeField] private int lastTickedPhase = -1;

    public IReadOnlyList<EnemyEcologyInstinctAI> Pack => pack;
    public int LastTickedPhase => lastTickedPhase;

    public static EnemyEcologyBehaviorRuntime EnsureInstance()
    {
        if (Instance != null)
        {
            return Instance;
        }

        EnemyEcologyBehaviorRuntime existing = Object.FindAnyObjectByType<EnemyEcologyBehaviorRuntime>();
        if (existing != null)
        {
            Instance = existing;
            return Instance;
        }

        GameObject hub = GameObject.Find("DebugSystemsHub");
        GameObject host = hub != null ? hub : new GameObject("EnemyEcologyHub");
        EnemyEcologyBehaviorRuntime runtime = host.GetComponent<EnemyEcologyBehaviorRuntime>();
        if (runtime == null)
        {
            runtime = host.AddComponent<EnemyEcologyBehaviorRuntime>();
        }

        Instance = runtime;
        return Instance;
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;
        EnsurePack();
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    /// <summary>1 位相分の生態ティック。</summary>
    public void TickPhase(int phase, VariableTimelineSeason season)
    {
        try
        {
            EnsurePack();
            if (pack.Count == 0)
            {
                Debug.LogWarning("[EnemyEcology] 魔物パックが空です（Safe-Fail）。");
                lastTickedPhase = ProceduralMapPopulator.NormalizePhase(phase);
                return;
            }

            float barrier = 78f;
            float crystals = 12f;
            VillageStorageMarket storage = null;

            try
            {
                VillageAutonomyEngine village = VillageAutonomyEngine.EnsureInstance();
                if (village?.Barrier != null)
                {
                    barrier = village.Barrier.Efficiency;
                }

                NpcCivilizationEngine civ = NpcCivilizationEngine.EnsureInstance();
                if (civ?.Storage != null)
                {
                    storage = civ.Storage;
                    crystals = storage.ManaCrystal;
                }
            }
            catch (System.Exception)
            {
                // 村未配置でも生態 AI は既定環境で動かす
            }

            bool active = season == VariableTimelineSeason.Active ||
                          season == VariableTimelineSeason.Escalation;
            float hungerBoost = active ? 6.5f : 1.5f;

            VillageBarrierBreachEngine breach = null;
            try
            {
                breach = VillageBarrierBreachEngine.EnsureInstance();
                VillageAutonomyEngine village = VillageAutonomyEngine.EnsureInstance();
                breach.Evaluate(
                    ProceduralMapPopulator.NormalizePhase(phase),
                    season,
                    village != null ? village.Barrier : null);
            }
            catch (System.Exception)
            {
                breach = VillageBarrierBreachEngine.Instance;
            }

            bool forceBreach = breach != null && (breach.HasActiveBreach || breach.IsDropInvasion);
            Vector3 beastDest = forceBreach ? breach.ResolveBeastDestination() : Vector3.zero;
            string beastDestId = forceBreach ? breach.ResolveBeastDestinationId() : string.Empty;

            StringBuilder log = new StringBuilder();
            log.Append("<color=#EF9A9A><b>【魔物生態】</b></color> ");
            log.Append($"位相 {ProceduralMapPopulator.NormalizePhase(phase)}/24 {season.ToString().ToUpperInvariant()} ");
            log.Append($"結界 {barrier:F1}% 結晶 {crystals:F1} 時代MP×{EnemyStatModifier.CurrentEraMpCostMultiplier:F2}");

            for (int i = 0; i < pack.Count; i++)
            {
                EnemyEcologyInstinctAI ai = pack[i];
                if (ai == null)
                {
                    continue;
                }

                EnemyStatusManager st = ai.EnsureStatus();
                if (st == null)
                {
                    Debug.LogWarning("[EnemyEcology] Status を解決できない個体をスキップ（Safe-Fail）。");
                    continue;
                }

                st.EnsureDefaults();
                st.AddManaHunger(hungerBoost * (0.7f + st.Territoriality * 0.005f));

                bool fruitBait = IsManaFruitTarget(ai.ForcedTargetSpotId);
                if (forceBreach)
                {
                    ai.SetForcedAssaultTarget(beastDest, beastDestId);
                    fruitBait = false;
                    if (Vector3.Distance(ai.transform.position, beastDest) > 80f)
                    {
                        Vector3 away = ai.transform.position - beastDest;
                        if (away.sqrMagnitude < 0.01f)
                        {
                            away = Vector3.back * 28f;
                        }

                        ai.transform.position = beastDest + away.normalized * 28f;
                    }
                }
                else if (!fruitBait)
                {
                    ai.ClearForcedTarget();
                }

                EnemyEcologyDecision decision = ai.Decide(season, barrier, crystals);
                if (forceBreach &&
                    (decision.Action == EnemyEcologyAction.Withdraw ||
                     decision.Action == EnemyEcologyAction.RecoverStance) &&
                    st.ProfileKind == EnemyEcologyProfileKind.Stalker)
                {
                    decision = new EnemyEcologyDecision
                    {
                        Action = EnemyEcologyAction.SeekManaPrey,
                        Score = 99f,
                        Reason = "PATTERN_AGILE_STALKER 強制誘導"
                    };
                }

                string detail = ai.Execute(decision, barrier, storage);
                if (forceBreach || fruitBait)
                {
                    float step = st.ProfileKind == EnemyEcologyProfileKind.Stalker ? 24f : 16f;
                    string move = ai.MoveTowardForcedTarget(step);
                    if (!string.IsNullOrEmpty(move))
                    {
                        detail += " / " + move;
                    }

                    if (forceBreach && Vector3.Distance(ai.transform.position, beastDest) <= 2.4f)
                    {
                        try
                        {
                            VillageInfrastructureEngine infra = VillageInfrastructureEngine.EnsureInstance();
                            if (breach.IsDropInvasion)
                            {
                                detail += " / " + infra.ApplyInvasionDamage(
                                    8.5f, true, "村中心部へ侵入");
                            }
                            else
                            {
                                detail += " / " + infra.DamageLinkedPalisade(beastDestId, 6.5f);
                            }
                        }
                        catch (System.Exception)
                        {
                            detail += " / 杭攻撃をスキップ（Safe-Fail）";
                        }
                    }
                }

                log.AppendLine();
                log.Append(
                    $"  {st.FormatSnapshot()} → {EnemyEcologyInstinctAI.ActionLabel(decision.Action)} " +
                    $"[{decision.Reason}] {detail}");
            }

            lastTickedPhase = ProceduralMapPopulator.NormalizePhase(phase);
            Debug.Log(log.ToString());
        }
        catch (System.Exception exception)
        {
            Debug.LogWarning($"[EnemyEcology] TickPhase Safe-Fail: {exception.Message}");
        }
    }

    public void SimulateFullDay(VariablePhaseDistribution distribution)
    {
        VariablePhaseDistribution dist = distribution ?? ProceduralMapPopulator.DefaultNation001Turn1Distribution();
        for (int phase = 1; phase <= ProceduralMapPopulator.PhaseCount; phase++)
        {
            VariableTimelineSeason season = ProceduralMapPopulator.ResolveSeason(phase, dist);
            TickPhase(phase, season);
        }
    }

    private void EnsurePack()
    {
        if (pack == null)
        {
            pack = new List<EnemyEcologyInstinctAI>();
        }

        pack.RemoveAll(a => a == null);
        if (pack.Count > 0)
        {
            return;
        }

        SpawnBeast("粘核スライム", EnemyEcologyProfileKind.Slime, 45f, 2, 1, 35f, 30f, 40f);
        SpawnBeast("影踏みストーカー", EnemyEcologyProfileKind.Stalker, 55f, 3, 2, 60f, 25f, 55f);
        SpawnBeast("岩殻タイタン", EnemyEcologyProfileKind.Titan, 25f, 4, 3, 35f, 70f, 75f);
        SpawnBeast("幻霧ダンサー", EnemyEcologyProfileKind.Dancer, 40f, 3, 2, 50f, 40f, 48f);
    }

    private static bool IsManaFruitTarget(string spotId)
    {
        return !string.IsNullOrWhiteSpace(spotId) &&
               spotId.StartsWith("MANA_FRUIT_", System.StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>環境魔力による野生動物の魔物化。パック上限時は null（Safe-Fail）。</summary>
    public EnemyEcologyInstinctAI SpawnMutatedBeast(string displayName, float hunger)
    {
        try
        {
            EnsurePack();
            if (pack.Count >= 14)
            {
                Debug.LogWarning("[EnemyEcology] パック上限のため変異スポーンを抑制（Safe-Fail）。");
                return null;
            }

            float safeHunger = Mathf.Clamp(hunger, 20f, 100f);
            EnemyEcologyProfileKind kind = pack.Count % 2 == 0
                ? EnemyEcologyProfileKind.Slime
                : EnemyEcologyProfileKind.Stalker;
            string name = string.IsNullOrWhiteSpace(displayName) ? "変異獣" : displayName.Trim();
            SpawnBeast(name, kind, safeHunger, 2, 2, 38f, 32f, 48f);
            return pack.Count > 0 ? pack[pack.Count - 1] : null;
        }
        catch (System.Exception exception)
        {
            Debug.LogWarning($"[EnemyEcology] 変異スポーン Safe-Fail: {exception.Message}");
            return null;
        }
    }

    /// <summary>
    /// 文明復興期 — 街間の自然害獣・隣人（PATTERN_BASIC_SLIME / PATTERN_AGILE_STALKER）を生成します。
    /// </summary>
    public EnemyEcologyInstinctAI SpawnNeighborBeast(
        string patternId,
        string displayName,
        float hunger,
        float territoriality)
    {
        try
        {
            EnsurePack();
            if (pack.Count >= 14)
            {
                Debug.LogWarning("[EnemyEcology] パック上限のため隣人スポーンを抑制（Safe-Fail）。");
                return null;
            }

            EnemyEcologyProfileKind kind = EnemyEcologyProfileKind.Slime;
            if (!string.IsNullOrWhiteSpace(patternId) &&
                patternId.IndexOf("STALKER", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                kind = EnemyEcologyProfileKind.Stalker;
            }

            float safeHunger = Mathf.Clamp(hunger, 18f, 75f);
            float safeTerritory = Mathf.Clamp(territoriality, 20f, 85f);
            string name = string.IsNullOrWhiteSpace(displayName) ? "自然の隣人" : displayName.Trim();
            SpawnBeast(name, kind, safeHunger, 1, 1, 42f, 28f, safeTerritory);
            EnemyEcologyInstinctAI spawned = pack.Count > 0 ? pack[pack.Count - 1] : null;
            if (spawned != null)
            {
                Debug.Log(
                    $"<color=#A5D6A7><b>【自然の隣人】</b></color> {name} " +
                    $"({patternId ?? "PATTERN_BASIC_SLIME"}) 飢餓 {safeHunger:F0} 縄張り {safeTerritory:F0}");
            }

            return spawned;
        }
        catch (System.Exception exception)
        {
            Debug.LogWarning($"[EnemyEcology] 隣人スポーン Safe-Fail: {exception.Message}");
            return null;
        }
    }

    /// <summary>討伐隊・防衛隊による該当エリアの魔獣駆除（安全地帯化）。</summary>
    public int PurgeBeastsInArea(string areaId, float clearanceStrength = 1f)
    {
        try
        {
            EnsurePack();
            if (pack == null || pack.Count == 0)
            {
                return 0;
            }

            float strength = Mathf.Clamp(clearanceStrength, 0.2f, 3f);
            int purgeCount = Mathf.Clamp(Mathf.CeilToInt(pack.Count * 0.35f * strength), 1, pack.Count);
            int removed = 0;
            for (int i = pack.Count - 1; i >= 0 && removed < purgeCount; i--)
            {
                EnemyEcologyInstinctAI ai = pack[i];
                if (ai == null)
                {
                    continue;
                }

                if (RetireBeast(ai, $"討伐隊・防衛隊による駆除 @{areaId ?? "AREA"}"))
                {
                    removed++;
                }
            }

            if (removed > 0)
            {
                Debug.Log(
                    $"<color=#81D4FA><b>【安全地帯化】</b></color> {areaId ?? "AREA"} " +
                    $"討伐隊が魔獣 {removed} 体を駆除しました（強度×{strength:F1}）");
            }

            return removed;
        }
        catch (System.Exception exception)
        {
            Debug.LogWarning($"[EnemyEcology] PurgeBeastsInArea Safe-Fail: {exception.Message}");
            return 0;
        }
    }

    private void SpawnBeast(
        string displayName,
        EnemyEcologyProfileKind kind,
        float hunger,
        int threat,
        int danger,
        float purity,
        float density,
        float territoriality)
    {
        GameObject go = new GameObject($"Beast_{kind}");
        go.transform.SetParent(transform, false);

        go.AddComponent<CombatStats>();
        go.AddComponent<EnemyActionStats>();
        EnemyStatusManager status = go.AddComponent<EnemyStatusManager>();
        EnemyEcologyInstinctAI ai = go.AddComponent<EnemyEcologyInstinctAI>();
        ai.BindStatus(status);

        status.CacheReferences();
        status.EnsureDefaults();
        status.SetProfile(kind, displayName);
        status.SetManaHunger(hunger);
        status.ConfigureHidden(threat, 0, danger);
        status.SetTerritoriality(territoriality);
        status.SetPartMaterial(purity, density);
        status.ApplyAllModifiers(log: false);

        pack.Add(ai);
    }

    /// <summary>縄張り争いで敗れた個体をパックから外します。</summary>
    public bool RetireBeast(EnemyEcologyInstinctAI ai, string reason)
    {
        try
        {
            if (pack == null || ai == null)
            {
                return false;
            }

            pack.Remove(ai);
            Debug.Log($"[EnemyEcology] 個体引退: {ai.name} ({reason})");
            GameObject go = ai.gameObject;
            if (go == null)
            {
                return true;
            }

            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(go);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(go);
            }

            return true;
        }
        catch (System.Exception exception)
        {
            Debug.LogWarning($"[EnemyEcology] RetireBeast Safe-Fail: {exception.Message}");
            return false;
        }
    }
}

public static class EnemyEcologyBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoAttach()
    {
        EnemyEcologyBehaviorRuntime.EnsureInstance();
    }
}

#if UNITY_EDITOR
public static class EnemyEcologyMenu
{
    private const string VerifyLogPath = "Logs/enemy_ecology_verify.txt";

    [MenuItem("Tools/Procedural Map/Run Enemy Ecology Day")]
    public static void RunEcologyDayFromMenu()
    {
        EnemyEcologyBehaviorRuntime runtime = EnemyEcologyBehaviorRuntime.EnsureInstance();
        runtime.SimulateFullDay(ProceduralMapPopulator.DefaultNation001Turn1Distribution());
        string line =
            $"PASS phase={runtime.LastTickedPhase} pack={runtime.Pack.Count} " +
            $"eraMp×{EnemyStatModifier.CurrentEraMpCostMultiplier:F2}";
        try
        {
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
            string path = Path.Combine(projectRoot, VerifyLogPath);
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? projectRoot);
            File.WriteAllText(path, line + "\n", Encoding.UTF8);
        }
        catch (System.Exception exception)
        {
            Debug.LogWarning($"[EnemyEcology] 検証ログをスキップ: {exception.Message}");
        }

        Debug.Log($"<color=#EF9A9A><b>【魔物生態・1日完了】</b></color> {line}");
        EditorUtility.DisplayDialog("Enemy Ecology", line, "OK");
    }
}
#endif

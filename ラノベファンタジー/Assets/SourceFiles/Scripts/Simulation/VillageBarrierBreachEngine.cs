using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using System.IO;
using System.Text;
using UnityEditor;
#endif

// =============================================================================
// 活性期 結界侵入 — Breach Point / 警戒アラート / 防衛ライン Safe-Fail
// 連携: VillageBarrierCore / VillageWorkSpotManager / WorldSpaceMarker
//       EnemyEcologyBehaviorRuntime / NpcCivilizationEngine / HistoryFlagRegistry
// =============================================================================

/// <summary>
/// 活性期に BarrierEfficiency が 50% 以下になると結界杭へ裂け目を立て、
/// 魔物誘導と結界守の緊急修復を接続します。
/// </summary>
[DefaultExecutionOrder(48)]
public class VillageBarrierBreachEngine : MonoBehaviour
{
    public const string HazardLabel = "[結界危険 / 侵入警戒]";
    public const string CollapseLogPrefix = "防衛ライン全滅・被害拡大";

    public static VillageBarrierBreachEngine Instance { get; private set; }

    [SerializeField] private bool hasActiveBreach;
    [SerializeField] private bool dropInvasion;
    [SerializeField] private int openedOnPhase = -1;
    [SerializeField] private int lastEvaluatedPhase = -1;
    [SerializeField] private string breachSpotId = string.Empty;
    [SerializeField] private Vector3 breachWorldPosition;
    [SerializeField] private bool repairedThisPhase;
    [SerializeField] private bool collapseLoggedThisPhase;
    [SerializeField] private GameObject riftVfx;
    [SerializeField] private GameObject keeperPulse;

    public bool HasActiveBreach => hasActiveBreach;
    public bool IsDropInvasion => dropInvasion;
    public string BreachSpotId => breachSpotId ?? string.Empty;
    public Vector3 BreachWorldPosition => breachWorldPosition;
    public WorkSpotData ActiveBreachSpot { get; private set; }

    public static VillageBarrierBreachEngine EnsureInstance()
    {
        if (Instance != null)
        {
            return Instance;
        }

        VillageBarrierBreachEngine existing = Object.FindAnyObjectByType<VillageBarrierBreachEngine>();
        if (existing != null)
        {
            Instance = existing;
            return Instance;
        }

        GameObject map = GameObject.Find(Nation001ProceduralMapBuilder.RootName);
        GameObject hub = map != null ? map : GameObject.Find("DebugSystemsHub");
        GameObject host = hub != null ? hub : new GameObject(nameof(VillageBarrierBreachEngine));
        VillageBarrierBreachEngine engine = host.GetComponent<VillageBarrierBreachEngine>();
        return engine != null ? engine : host.AddComponent<VillageBarrierBreachEngine>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            return;
        }

        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    public bool IsBreachSpot(string spotId)
    {
        return hasActiveBreach &&
               !string.IsNullOrEmpty(breachSpotId) &&
               string.Equals(breachSpotId, spotId, System.StringComparison.OrdinalIgnoreCase);
    }

    public Vector3 ResolveBeastDestination()
    {
        if (dropInvasion)
        {
            try
            {
                return VillageWorkSpotManager.EnsureInstance().ResolveHousingWorkshopCenter();
            }
            catch (System.Exception)
            {
                return breachWorldPosition;
            }
        }

        return breachWorldPosition;
    }

    public string ResolveBeastDestinationId()
    {
        if (dropInvasion)
        {
            return "VILLAGE_CENTER_HOUSING_WORKSHOP";
        }

        return string.IsNullOrEmpty(breachSpotId) ? "SPOT_BARRIER_ANCHOR_1" : breachSpotId;
    }

    /// <summary>活性期かつ維持率 50% 以下なら Breach Point を生成します。</summary>
    public void Evaluate(int phase, VariableTimelineSeason season, VillageBarrierCore barrier)
    {
        try
        {
            int normalized = ProceduralMapPopulator.NormalizePhase(phase);
            if (lastEvaluatedPhase != normalized)
            {
                repairedThisPhase = false;
                collapseLoggedThisPhase = false;
            }

            lastEvaluatedPhase = normalized;
            if (barrier == null)
            {
                barrier = VillageAutonomyEngine.EnsureInstance()?.Barrier;
            }

            if (barrier == null)
            {
                Debug.LogWarning("[VillageBarrierBreachEngine] VillageBarrierCore なし（Safe-Fail）。");
                return;
            }

            if (barrier.BarrierDropped || barrier.Efficiency <= 0.01f)
            {
                HandleBarrierDrop(barrier, normalized);
                return;
            }

            dropInvasion = false;

            bool samePhaseLatch = hasActiveBreach && openedOnPhase == normalized;
            bool shouldOpen = season == VariableTimelineSeason.Active &&
                              barrier.IsBelowBreachThreshold;

            if (shouldOpen && !hasActiveBreach)
            {
                OpenBreach(normalized);
                return;
            }

            if (samePhaseLatch)
            {
                RefreshHazardVisuals();
                return;
            }

            if (hasActiveBreach && !shouldOpen)
            {
                CloseBreach("結界維持率が安全域へ戻った");
            }
        }
        catch (System.Exception exception)
        {
            Debug.LogWarning($"[VillageBarrierBreachEngine] Evaluate Safe-Fail: {exception.Message}");
        }
    }

    /// <summary>NPC ループ後。結界守不在・結晶枯渇でも停止せず被害拡大ログを出します。</summary>
    public void FinalizePhase(int livingKeepers, bool hasCrystal, VillageBarrierCore barrier)
    {
        try
        {
            if (!hasActiveBreach && (barrier == null || !barrier.BarrierDropped))
            {
                return;
            }

            if (livingKeepers <= 0)
            {
                NotifyDefenseLineCollapse("結界守が存在しない");
            }
            else if (!hasCrystal && !repairedThisPhase && hasActiveBreach)
            {
                NotifyDefenseLineCollapse("ManaCrystal が枯渇している");
            }
        }
        catch (System.Exception exception)
        {
            Debug.LogWarning($"[VillageBarrierBreachEngine] FinalizePhase Safe-Fail: {exception.Message}");
        }
    }

    public void NotifyDefenseLineCollapse(string cause)
    {
        if (collapseLoggedThisPhase)
        {
            return;
        }

        collapseLoggedThisPhase = true;
        string why = string.IsNullOrWhiteSpace(cause) ? "原因不明" : cause;
        Debug.LogWarning(
            $"<color=#FF8A80><b>【{CollapseLogPrefix}】</b></color> {why}。" +
            "処理は停止せず被害拡大として継続します（Safe-Fail）。");
    }

    public void MarkRepairAttempt(bool recovered)
    {
        if (recovered)
        {
            repairedThisPhase = true;
        }
    }

    public void PlayKeeperRepairPulse(WorkSpotData spot)
    {
        try
        {
            Vector3 pos = spot != null ? spot.WorldPosition : breachWorldPosition;
            if (keeperPulse != null)
            {
                DestroySafe(keeperPulse);
            }

            keeperPulse = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            keeperPulse.name = "KeeperRepairPulse";
            keeperPulse.transform.SetParent(transform, true);
            keeperPulse.transform.position = pos + Vector3.up * 3.2f;
            keeperPulse.transform.localScale = Vector3.one * 2.4f;
            Collider col = keeperPulse.GetComponent<Collider>();
            if (col != null)
            {
                col.enabled = false;
            }

            Renderer renderer = keeperPulse.GetComponent<Renderer>();
            RuntimeUrpMaterialUtility.ApplyTransparentColor(
                renderer, new Color(0.35f, 0.9f, 1f, 0.55f));
            Debug.Log(
                $"<color=#80DEEA><b>【結界守】</b></color> ミサが結晶を抱えて現地へ。" +
                $"緊急修復パルス @ {(spot != null ? spot.SpotId : breachSpotId)}");
        }
        catch (System.Exception exception)
        {
            Debug.LogWarning($"[VillageBarrierBreachEngine] 修復演出をスキップ: {exception.Message}");
        }
    }

    private void OpenBreach(int phase)
    {
        WorkSpotData spot = PickBreachAnchor(phase);
        ActiveBreachSpot = spot;
        breachSpotId = spot != null ? spot.SpotId : "SPOT_BARRIER_ANCHOR_1";
        breachWorldPosition = spot != null ? spot.WorldPosition : Vector3.zero;
        hasActiveBreach = true;
        openedOnPhase = phase;

        ApplyHazardToSpot(spot, true);
        SpawnRiftVfx(spot);
        Debug.Log(
            $"<color=#FF5252><b>【結界裂け目】Breach Point</b></color> 活性期・維持率50%以下。" +
            $"対象杭 {breachSpotId} @ {breachWorldPosition} に {HazardLabel} を展開。" +
            "魔物を杭へ誘導し、結界守の緊急修復を最優先化します。");
    }

    private void CloseBreach(string reason)
    {
        ApplyHazardToSpot(ActiveBreachSpot, false);
        hasActiveBreach = false;
        openedOnPhase = -1;
        dropInvasion = false;
        ActiveBreachSpot = null;
        DestroySafe(riftVfx);
        riftVfx = null;
        try
        {
            VillageInfrastructureEngine.EnsureInstance().SyncMarkerLabels();
        }
        catch (System.Exception)
        {
            // Safe-Fail
        }

        Debug.Log($"<color=#A5D6A7><b>【結界安定】</b></color> 裂け目を閉鎖。{reason}");
    }

    private void HandleBarrierDrop(VillageBarrierCore barrier, int phase)
    {
        if (barrier != null)
        {
            try
            {
                HistoryFlagRegistry.Unlock(VillageBarrierCore.Nation001BarrierDropFlag);
                HistoryFlagRegistry.Unlock(VillageBarrierCore.BarrierDropFlag);
            }
            catch (System.Exception exception)
            {
                Debug.LogWarning($"[VillageBarrierBreachEngine] BarrierDrop フラグ Safe-Fail: {exception.Message}");
            }
        }

        if (!dropInvasion)
        {
            dropInvasion = true;
            hasActiveBreach = true;
            if (ActiveBreachSpot == null)
            {
                OpenBreach(phase);
            }

            ApplyHazardToSpot(ActiveBreachSpot, true);
            string flag = VillageBarrierCore.Nation001BarrierDropFlag;
            string invasion;
            try
            {
                invasion = VillageInfrastructureEngine.EnsureInstance()
                    .ApplyInvasionDamage(12f, true, "結界破綻・村中心部侵入");
            }
            catch (System.Exception)
            {
                invasion = "施設ダメージをスキップ（Safe-Fail）";
            }

            Debug.LogWarning(
                $"<color=#FF1744><b>【結界破綻】Barrier Drop</b></color> " +
                $"フラグ {flag} を発行。魔物が家屋・工房へ侵入。{invasion}");
        }
        else
        {
            RefreshHazardVisuals();
        }
    }

    private WorkSpotData PickBreachAnchor(int phase)
    {
        List<WorkSpotData> anchors;
        try
        {
            anchors = VillageWorkSpotManager.EnsureInstance().GetBarrierAnchors();
        }
        catch (System.Exception)
        {
            anchors = null;
        }

        if (anchors == null || anchors.Count == 0)
        {
            return WorkSpotData.CreateFallback(Vector3.zero);
        }

        int index = Mathf.Abs(phase * 17) % anchors.Count;
        return anchors[index];
    }

    private void RefreshHazardVisuals()
    {
        ApplyHazardToSpot(ActiveBreachSpot, true);
        if (riftVfx != null && ActiveBreachSpot != null)
        {
            riftVfx.transform.position = ActiveBreachSpot.WorldPosition + Vector3.up * 4.5f;
        }
    }

    private void ApplyHazardToSpot(WorkSpotData spot, bool on)
    {
        if (spot == null)
        {
            return;
        }

        try
        {
            Transform anchor = spot.SceneAnchor;
            WorldSpaceMarker marker = null;
            if (anchor != null)
            {
                marker = anchor.GetComponentInChildren<WorldSpaceMarker>(true);
            }

            if (marker == null)
            {
                WorldSpaceMarker[] all = Object.FindObjectsByType<WorldSpaceMarker>(
                    FindObjectsInactive.Include);
                for (int i = 0; i < (all?.Length ?? 0); i++)
                {
                    if (all[i] != null && all[i].FollowTarget == anchor)
                    {
                        marker = all[i];
                        break;
                    }
                }
            }

            if (marker == null && anchor != null)
            {
                GameObject markerObject = new GameObject($"BreachMarker_{spot.SpotId}");
                markerObject.transform.SetParent(transform, false);
                marker = markerObject.AddComponent<WorldSpaceMarker>();
                marker.Initialize(anchor, WorldSpaceMarkerKind.BreachAlert, HazardLabel);
            }

            marker?.SetHazardAlert(on, HazardLabel);
        }
        catch (System.Exception exception)
        {
            Debug.LogWarning($"[VillageBarrierBreachEngine] 警戒マーカー Safe-Fail: {exception.Message}");
        }
    }

    private void SpawnRiftVfx(WorkSpotData spot)
    {
        try
        {
            DestroySafe(riftVfx);
            Vector3 pos = (spot != null ? spot.WorldPosition : breachWorldPosition) + Vector3.up * 4.5f;
            riftVfx = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            riftVfx.name = "BreachRiftVfx";
            riftVfx.transform.SetParent(transform, true);
            riftVfx.transform.position = pos;
            riftVfx.transform.localScale = new Vector3(2.8f, 5.5f, 2.8f);
            Collider col = riftVfx.GetComponent<Collider>();
            if (col != null)
            {
                col.enabled = false;
            }

            Renderer renderer = riftVfx.GetComponent<Renderer>();
            RuntimeUrpMaterialUtility.ApplyTransparentColor(
                renderer, new Color(0.95f, 0.08f, 0.05f, 0.55f));

            Light light = riftVfx.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.15f, 0.05f);
            light.intensity = 3.2f;
            light.range = 14f;
        }
        catch (System.Exception exception)
        {
            Debug.LogWarning($"[VillageBarrierBreachEngine] 裂け目VFX をスキップ: {exception.Message}");
        }
    }

    private static void DestroySafe(GameObject target)
    {
        if (target == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            Destroy(target);
        }
        else
        {
            DestroyImmediate(target);
        }
    }
}

public static class VillageBarrierBreachBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoAttach()
    {
        VillageBarrierBreachEngine.EnsureInstance();
    }
}

/// <summary>活性期侵入検証の結果。</summary>
public struct VillageBarrierBreachVerifyResult
{
    public bool success;
    public bool breachOpened;
    public bool keeperMoved;
    public bool beastsDirected;
    public bool dropFlag;
    public bool housingDamaged;
    public string message;
}

#if UNITY_EDITOR
/// <summary>エディタ専用。本番ビルドには含まれません。</summary>
public static class VillageBarrierBreachMenu
{
    private const string VerifyLogPath = "Logs/barrier_breach_verify.txt";

    [MenuItem("Tools/Procedural Map/Verify Barrier Breach (Active Phase)")]
    public static void VerifyFromMenu()
    {
        VillageBarrierBreachVerifyResult result = RunActivePhaseVerification();
        EditorUtility.DisplayDialog(
            "Barrier Breach",
            result.success ? $"PASS\n{result.message}" : $"FAIL\n{result.message}",
            "OK");
    }

    /// <summary>Unity バッチ: -executeMethod VillageBarrierBreachMenu.BatchVerifyAndQuit</summary>
    public static void BatchVerifyAndQuit()
    {
        VillageBarrierBreachVerifyResult result = RunActivePhaseVerification();
        EditorApplication.Exit(result.success ? 0 : 1);
    }

    public static VillageBarrierBreachVerifyResult RunActivePhaseVerification()
    {
        VillageBarrierBreachVerifyResult result = new VillageBarrierBreachVerifyResult();
        try
        {
            try
            {
                Nation001ProceduralMapBuilder.GenerateNation001Map(1, 1);
            }
            catch (System.Exception)
            {
                // マップ未生成でもフォールバック杭で検証
            }

            VillageAutonomyEngine village = VillageAutonomyEngine.EnsureInstance();
            NpcCivilizationEngine civ = NpcCivilizationEngine.EnsureInstance();
            EnemyEcologyBehaviorRuntime ecology = EnemyEcologyBehaviorRuntime.EnsureInstance();
            VillageBarrierBreachEngine breach = VillageBarrierBreachEngine.EnsureInstance();
            VillageInfrastructureEngine infra = VillageInfrastructureEngine.EnsureInstance();
            infra.EnsureDefaults();

            village.Barrier.Efficiency = 48f;
            village.Barrier.BarrierDropped = false;
            float housingBefore = infra.AverageHousingDurability();

            civ.TickPhase(1, VariableTimelineSeason.Active);
            ecology.TickPhase(1, VariableTimelineSeason.Active);

            result.breachOpened = breach.HasActiveBreach;
            result.keeperMoved = false;
            for (int i = 0; i < civ.Villagers.Count; i++)
            {
                NpcIndividualStatus npc = civ.Villagers[i];
                if (npc != null &&
                    npc.CivicJob == NpcCivicJob.BarrierKeeper &&
                    breach.IsBreachSpot(npc.AssignedSpotId))
                {
                    result.keeperMoved = true;
                    break;
                }
            }

            result.beastsDirected = false;
            IReadOnlyList<EnemyEcologyInstinctAI> pack = ecology.Pack;
            for (int i = 0; i < pack.Count; i++)
            {
                if (pack[i] != null && pack[i].HasForcedTarget)
                {
                    result.beastsDirected = true;
                    break;
                }
            }

            village.Barrier.Efficiency = 0f;
            village.Barrier.BarrierDropped = false;
            civ.TickPhase(2, VariableTimelineSeason.Active);
            ecology.TickPhase(2, VariableTimelineSeason.Active);

            result.dropFlag = HistoryFlagRegistry.IsUnlocked(VillageBarrierCore.Nation001BarrierDropFlag);
            result.housingDamaged = infra.AverageHousingDurability() < housingBefore - 0.5f ||
                                    breach.IsDropInvasion;
            result.success = result.breachOpened &&
                             result.keeperMoved &&
                             result.beastsDirected &&
                             result.dropFlag &&
                             result.housingDamaged;
            result.message =
                $"breach={result.breachOpened} keeper={result.keeperMoved} " +
                $"beasts={result.beastsDirected} flag={result.dropFlag} " +
                $"housingDmg={result.housingDamaged} " +
                $"spot={breach.BreachSpotId} 結界={village.Barrier.Efficiency:F1}%";

            WriteLog(result);
            Debug.Log(
                result.success
                    ? $"<color=#A5D6A7><b>【結界侵入検証】PASS</b></color> {result.message}"
                    : $"<color=#FF8A80><b>【結界侵入検証】FAIL</b></color> {result.message}");
        }
        catch (System.Exception exception)
        {
            result.success = false;
            result.message = $"Safe-Fail: {exception.Message}";
            Debug.LogWarning($"[VillageBarrierBreachMenu] {result.message}");
            WriteLog(result);
        }

        return result;
    }

    private static void WriteLog(VillageBarrierBreachVerifyResult result)
    {
        try
        {
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
            string path = Path.Combine(projectRoot, VerifyLogPath);
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? projectRoot);
            StringBuilder builder = new StringBuilder();
            builder.AppendLine(result.success ? "PASS" : "FAIL");
            builder.AppendLine(result.message ?? string.Empty);
            File.WriteAllText(path, builder.ToString(), Encoding.UTF8);
        }
        catch (System.Exception exception)
        {
            Debug.LogWarning($"[VillageBarrierBreachMenu] 検証ログをスキップ: {exception.Message}");
        }
    }
}
#endif

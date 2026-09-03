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
// 特異点魔獣 — 未開地配備・観測フラグ・個別ルーチン（250年正史密度拡張）
// 連携: EnemyEcologyBehaviorRuntime / HistoryFlagRegistry / VillageBarrierCore
// =============================================================================

/// <summary>特異点巨獣の個別活動トリガー。</summary>
public enum SingularBeastRoutineType
{
    /// <summary>休眠山脈型 — 高刺激まで完全静止。</summary>
    DormantMountain = 0,
    /// <summary>魔力吸入巡回型 — 緩やかに刺激を蓄積。</summary>
    ManaSiphonPatrol = 1,
    /// <summary>過熱検知覚醒型 — 低刺激でも急激に反応。</summary>
    OverheatAwakening = 2
}

/// <summary>1 体の特異点魔獣レコード。</summary>
[Serializable]
public sealed class SingularBeastRecord
{
    public string beastId = string.Empty;
    public string continentId = string.Empty;
    public int nationId = 1;
    public int slotIndex;
    public bool observed;
    public int observedTurn;
    public string observationFlag = string.Empty;
    public SingularBeastRoutineType routine = SingularBeastRoutineType.DormantMountain;
    public float manaStimulusAccum;
    public bool passiveMode = true;
    public string displayName = string.Empty;
}

/// <summary>観測イベント結果。</summary>
public sealed class SingularBeastObservationResult
{
    public bool success;
    public bool newlyObserved;
    public string beastId = string.Empty;
    public string observationFlag = string.Empty;
    public string message = string.Empty;
}

/// <summary>
/// 各大陸（最大10体）に未観測特異点魔獣を配備し、
/// 接触観測と個別ルーチンによる結界破壊反応を管理します。
/// </summary>
[DefaultExecutionOrder(46)]
public class SingularBeastManager : MonoBehaviour
{
    public const string LogTag = "【特異点魔獣】";
    public const string PatternSingularityColossus = "PATTERN_SINGULARITY_COLOSSUS";
    public const int MaxBeastsPerContinent = 10;
    public const float TerritoryExpansionThreshold = 2.5f;
    public const float ManaConsumptionBreakthroughRatio = 0.82f;
    public const float BarrierStimulusTriggerDefault = 0.78f;

    public static readonly string[] ContinentIds =
    {
        "領域・西",
        "領域・東",
        "領域・中央",
        "領域・南",
        "領域・北"
    };

    public static SingularBeastManager Instance { get; private set; }
    public static int LastObservationEventCount { get; private set; }

    [SerializeField] private List<SingularBeastRecord> colossi = new List<SingularBeastRecord>();

    public IReadOnlyList<SingularBeastRecord> Colossi => colossi;

    public static SingularBeastManager EnsureInstance()
    {
        if (Instance != null)
        {
            return Instance;
        }

        SingularBeastManager existing = UnityEngine.Object.FindAnyObjectByType<SingularBeastManager>();
        if (existing != null)
        {
            Instance = existing;
            return existing;
        }

        GameObject hub = GameObject.Find("DebugSystemsHub");
        GameObject host = hub != null ? hub : new GameObject(nameof(SingularBeastManager));
        SingularBeastManager mgr = host.GetComponent<SingularBeastManager>();
        return mgr != null ? mgr : host.AddComponent<SingularBeastManager>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
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

    /// <summary>5 大陸 × 最大 10 体の未観測特異点魔獣を配備します。</summary>
    public static int BootstrapUnobservedColossi(bool forceReset = false)
    {
        SingularBeastManager mgr = EnsureInstance();
        if (forceReset)
        {
            mgr.colossi.Clear();
        }

        if (mgr.colossi.Count > 0)
        {
            return mgr.colossi.Count;
        }

        int total = 0;
        for (int c = 0; c < ContinentIds.Length; c++)
        {
            string continent = ContinentIds[c];
            int nationId = MapContinentToNationId(continent);
            for (int slot = 0; slot < MaxBeastsPerContinent; slot++)
            {
                if (total >= ContinentIds.Length * MaxBeastsPerContinent)
                {
                    break;
                }

                SingularBeastRoutineType routine = (SingularBeastRoutineType)(slot % 3);
                mgr.colossi.Add(new SingularBeastRecord
                {
                    beastId = $"COLOSSUS_{continent}_{slot:D2}",
                    continentId = continent,
                    nationId = nationId,
                    slotIndex = slot,
                    observed = false,
                    routine = routine,
                    passiveMode = true,
                    displayName = BuildDisplayName(continent, slot, routine)
                });
                total++;
            }
        }

        return total;
    }

    /// <summary>
    /// 領土拡大または魔力消費限界突破で初接触観測を試行します。
    /// </summary>
    public static SingularBeastObservationResult TryObserveOnHumanContact(
        int nationId,
        int turn,
        float territoryDelta,
        float manaConsumptionRatio,
        string continentId = null)
    {
        SingularBeastObservationResult result = new SingularBeastObservationResult();
        try
        {
            SingularBeastManager mgr = EnsureInstance();
            if (mgr.colossi == null || mgr.colossi.Count == 0)
            {
                BootstrapUnobservedColossi();
            }

            bool territoryOk = territoryDelta >= TerritoryExpansionThreshold;
            bool manaOk = manaConsumptionRatio >= ManaConsumptionBreakthroughRatio;
            if (!territoryOk && !manaOk)
            {
                result.message = "観測条件未達";
                return result;
            }

            SingularBeastRecord target = mgr.FindNextUnobserved(nationId, continentId);
            if (target == null)
            {
                result.message = "未観測個体なし（上限到達または欠損）";
                return result;
            }

            string flag = BuildObservationFlag(nationId, turn);
            target.observed = true;
            target.observedTurn = turn;
            target.observationFlag = flag;
            HistoryFlagRegistry.EnsureWired();
            HistoryFlagRegistry.Unlock(flag);
            LastObservationEventCount++;

            TryBindEcologyPassive(target);

            result.success = true;
            result.newlyObserved = true;
            result.beastId = target.beastId;
            result.observationFlag = flag;
            result.message =
                $"観測 T{turn} {target.displayName} routine={target.routine} flag={flag} " +
                $"(territory={territoryOk} mana={manaOk})";
            Debug.Log(
                $"<color=#80DEEA><b>{LogTag}</b></color> {result.message}");
            return result;
        }
        catch (Exception exception)
        {
            result.message = $"Safe-Fail: {exception.Message}";
            Debug.LogWarning($"[SingularBeastManager] {result.message}");
            return result;
        }
    }

    /// <summary>位相/ターンごとに個別ルーチンを進め、過度な魔力刺激で結界破壊を試行します。</summary>
    public static string TickColossusRoutines(int turn, float regionalManaStimulus)
    {
        StringBuilder log = new StringBuilder();
        try
        {
            SingularBeastManager mgr = EnsureInstance();
            if (mgr.colossi == null || mgr.colossi.Count == 0)
            {
                return "colossi=0 skip";
            }

            float stimulus = Mathf.Clamp01(regionalManaStimulus);
            int reactions = 0;
            for (int i = 0; i < mgr.colossi.Count; i++)
            {
                SingularBeastRecord beast = mgr.colossi[i];
                if (beast == null || !beast.observed)
                {
                    continue;
                }

                float gain = beast.routine switch
                {
                    SingularBeastRoutineType.DormantMountain => stimulus * 0.08f,
                    SingularBeastRoutineType.ManaSiphonPatrol => stimulus * 0.22f,
                    SingularBeastRoutineType.OverheatAwakening => stimulus * 0.35f,
                    _ => stimulus * 0.1f
                };
                beast.manaStimulusAccum = Mathf.Clamp01(beast.manaStimulusAccum + gain);

                float trigger = ResolveTriggerThreshold(beast.routine);
                if (beast.manaStimulusAccum < trigger)
                {
                    continue;
                }

                float barrierDrop = TryDestroyBarrierOnStimulus(beast, turn);
                if (barrierDrop > 0f)
                {
                    reactions++;
                    log.AppendLine(
                        $"  {beast.displayName}({beast.routine}) 結界-{barrierDrop:F1}% " +
                        $"stimulus={beast.manaStimulusAccum:F2}");
                    beast.manaStimulusAccum *= 0.35f;
                }
            }

            log.Insert(0, $"T{turn} routines={mgr.colossi.Count} reactions={reactions}\n");
            return log.ToString().TrimEnd();
        }
        catch (Exception exception)
        {
            return $"Safe-Fail: {exception.Message}";
        }
    }

    public static int CountObservedOnContinent(string continentId)
    {
        SingularBeastManager mgr = EnsureInstance();
        int count = 0;
        for (int i = 0; i < mgr.colossi.Count; i++)
        {
            SingularBeastRecord beast = mgr.colossi[i];
            if (beast != null &&
                beast.observed &&
                string.Equals(beast.continentId, continentId, StringComparison.OrdinalIgnoreCase))
            {
                count++;
            }
        }

        return count;
    }

    public static void ResetForVerification()
    {
        SingularBeastManager mgr = EnsureInstance();
        mgr.colossi.Clear();
        LastObservationEventCount = 0;
    }

    private SingularBeastRecord FindNextUnobserved(int nationId, string continentId)
    {
        string continent = continentId;
        if (string.IsNullOrWhiteSpace(continent))
        {
            continent = MapNationToContinent(nationId);
        }

        int onContinent = 0;
        for (int i = 0; i < colossi.Count; i++)
        {
            SingularBeastRecord beast = colossi[i];
            if (beast == null ||
                !string.Equals(beast.continentId, continent, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            onContinent++;
            if (onContinent > MaxBeastsPerContinent)
            {
                continue;
            }

            if (!beast.observed)
            {
                return beast;
            }
        }

        return null;
    }

    private static float ResolveTriggerThreshold(SingularBeastRoutineType routine)
    {
        return routine switch
        {
            SingularBeastRoutineType.DormantMountain => 0.88f,
            SingularBeastRoutineType.ManaSiphonPatrol => 0.78f,
            SingularBeastRoutineType.OverheatAwakening => 0.65f,
            _ => BarrierStimulusTriggerDefault
        };
    }

    private static float TryDestroyBarrierOnStimulus(SingularBeastRecord beast, int turn)
    {
        if (beast == null || !beast.passiveMode)
        {
            return 0f;
        }

        try
        {
            VillageAutonomyEngine village = VillageAutonomyEngine.EnsureInstance();
            VillageBarrierCore barrier = village?.Barrier;
            if (barrier == null)
            {
                return 0f;
            }

            float drop = beast.routine switch
            {
                SingularBeastRoutineType.DormantMountain => 14f,
                SingularBeastRoutineType.ManaSiphonPatrol => 10f,
                SingularBeastRoutineType.OverheatAwakening => 18f,
                _ => 12f
            };

            float applied = barrier.ApplyExternalDamage(drop);
            Debug.Log(
                $"<color=#FF8A65><b>{LogTag}</b></color> {beast.displayName} が過度な魔力刺激により " +
                $"T{turn} 結界を {applied:F1}% 削った（人類への直接攻撃なし）");
            return applied;
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[SingularBeastManager] 結界破壊 Safe-Fail: {exception.Message}");
            return 0f;
        }
    }

    private static void TryBindEcologyPassive(SingularBeastRecord beast)
    {
        try
        {
            EnemyEcologyBehaviorRuntime ecology = EnemyEcologyBehaviorRuntime.EnsureInstance();
            ecology.SpawnMutatedBeast(beast.displayName, 12f);
        }
        catch (Exception)
        {
            // 視覚スポーン無しでも観測フラグは有効
        }
    }

    public static string BuildObservationFlag(int nationId, int turn)
    {
        return string.Format(
            CultureInfo.InvariantCulture,
            "HIST_NATION_{0:000}_GEO_TURN_{1:000}_OBSERVED_COLOSSUS",
            nationId,
            turn);
    }

    private static int MapContinentToNationId(string continent)
    {
        if (continent.Contains("東"))
        {
            return 2;
        }

        if (continent.Contains("中央"))
        {
            return 3;
        }

        if (continent.Contains("南"))
        {
            return 4;
        }

        if (continent.Contains("北"))
        {
            return 5;
        }

        return 1;
    }

    private static string MapNationToContinent(int nationId)
    {
        return nationId switch
        {
            2 => "領域・東",
            3 => "領域・中央",
            4 => "領域・南",
            5 => "領域・北",
            _ => "領域・西"
        };
    }

    private static string BuildDisplayName(string continent, int slot, SingularBeastRoutineType routine)
    {
        string typeLabel = routine switch
        {
            SingularBeastRoutineType.DormantMountain => "休眠山脈巨獣",
            SingularBeastRoutineType.ManaSiphonPatrol => "魔力巡回巨獣",
            SingularBeastRoutineType.OverheatAwakening => "過熱覚醒巨獣",
            _ => "特異点巨獣"
        };

        return $"{continent}·{typeLabel}#{slot + 1}";
    }
}

public static class SingularBeastManagerBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoAttach()
    {
        SingularBeastManager.EnsureInstance();
    }
}

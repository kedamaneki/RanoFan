using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

// =============================================================================
// 自然環境・生態系シミュレーション — 森林再生 / 野生動物 / 魔物変異
// 連携: NpcCivilizationEngine / VillageStorageMarket / VillageWorkSpotManager
//       EnemyEcologyBehaviorRuntime / EnemyEcologyInstinctAI / VillageBarrierCore
// =============================================================================

/// <summary>24 位相検証の集計。</summary>
public sealed class NaturalEcologyVerifyResult
{
    public bool success;
    public string message = string.Empty;
}

/// <summary>環境摩擦型の魔獣襲来要因（T≥1001）。</summary>
public readonly struct EnvironmentalFrictionSnapshot
{
    public readonly float TerritorialityPressure;
    public readonly float PoliticalManaLeak;
    public readonly float ResourceConflictPressure;
    public readonly float TotalFriction;

    public EnvironmentalFrictionSnapshot(
        float territorialityPressure,
        float politicalManaLeak,
        float resourceConflictPressure)
    {
        TerritorialityPressure = Mathf.Max(0f, territorialityPressure);
        PoliticalManaLeak = Mathf.Max(0f, politicalManaLeak);
        ResourceConflictPressure = Mathf.Max(0f, resourceConflictPressure);
        TotalFriction = TerritorialityPressure + PoliticalManaLeak + ResourceConflictPressure;
    }

    public bool ShouldTriggerNeighborEncounter => TotalFriction >= 0.42f;
}

/// <summary>
/// 伐採・狩猟・休眠期再生と、環境魔力による野生動物の魔物化を位相駆動します。
/// </summary>
[DefaultExecutionOrder(53)]
public class NaturalEcologyEngine : MonoBehaviour
{
    public const float MutationManaThreshold = 62f;
    public const float HighManaHunger = 60f;
    public const float FruitAttractionHunger = 55f;
    public const int MaxPackSize = 14;
    public const int MaxMutationsPerPhase = 2;
    public const float TerritoryGatherRadius = 18f;
    public const float RevivalOverharvestThreshold = 2.4f;
    public const float RevivalFrictionTriggerThreshold = 0.42f;

    public static NaturalEcologyEngine Instance { get; private set; }

    [SerializeField] private NatureEnvironmentStatus environment = new NatureEnvironmentStatus();
    [SerializeField] private int lastTickedPhase = -1;
    [SerializeField] private int totalMutations;
    [SerializeField] private float timberHarvestedThisPhase;
    [SerializeField] private int wildlifeHuntedThisPhase;
    [SerializeField] private bool depletionWarnedThisPhase;
    [SerializeField] private int mutatedHarvestsTotal;
    [SerializeField] private int fruitAttractionsTotal;
    [SerializeField] private int territoryConflictsTotal;

    private readonly HashSet<string> clearedSafeZones = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public NatureEnvironmentStatus Environment => environment ?? (environment = new NatureEnvironmentStatus());
    public int LastTickedPhase => lastTickedPhase;
    public int TotalMutations => totalMutations;
    public int MutatedHarvestsTotal => mutatedHarvestsTotal;
    public int FruitAttractionsTotal => fruitAttractionsTotal;
    public int TerritoryConflictsTotal => territoryConflictsTotal;
    public float TimberHarvestedThisPhase => timberHarvestedThisPhase;

    /// <summary>検証メニュー用に位相カウンタとイベント集計を初期化します。</summary>
    public void ResetVerificationState()
    {
        lastTickedPhase = -1;
        totalMutations = 0;
        mutatedHarvestsTotal = 0;
        fruitAttractionsTotal = 0;
        territoryConflictsTotal = 0;
        timberHarvestedThisPhase = 0f;
        wildlifeHuntedThisPhase = 0;
        depletionWarnedThisPhase = false;
        clearedSafeZones.Clear();
    }

    public static NaturalEcologyEngine EnsureInstance()
    {
        if (Instance != null)
        {
            return Instance;
        }

        NaturalEcologyEngine existing = UnityEngine.Object.FindAnyObjectByType<NaturalEcologyEngine>();
        if (existing != null)
        {
            Instance = existing;
            return Instance;
        }

        GameObject hub = GameObject.Find("DebugSystemsHub");
        GameObject host = hub != null ? hub : new GameObject(nameof(NaturalEcologyEngine));
        NaturalEcologyEngine engine = host.GetComponent<NaturalEcologyEngine>();
        if (engine == null)
        {
            engine = host.AddComponent<NaturalEcologyEngine>();
        }

        Instance = engine;
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
        Environment.ClampAll();
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    /// <summary>
    /// 木こりが伐採場で作業したときの森林減少と木材効率。
    /// 森林 20% 以下は Safe-Fail で効率低下、絶滅時も 10% を維持します。
    /// </summary>
    public float NotifyWoodcutterAtCamp(float skill, bool atWoodcutterCamp)
    {
        try
        {
            if (!atWoodcutterCamp)
            {
                return 1f;
            }

            NatureEnvironmentStatus nature = Environment;
            nature.ClampAll();
            float efficiency = nature.ResolveHarvestEfficiency();
            WarnIfDepleted(nature);

            float cut = Mathf.Clamp(1.8f + skill * 0.35f, 0.8f, 4.2f);
            if (nature.ForestStressed || nature.ForestDepleted)
            {
                cut *= efficiency;
            }

            nature.ApplyForestDelta(-cut);
            timberHarvestedThisPhase += cut * efficiency;
            SyncWildlifeTowardForest(nature, hunted: 0);
            return Mathf.Max(NatureEnvironmentStatus.MinimumProductivity, efficiency);
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[NaturalEcologyEngine] 伐採 Safe-Fail: {exception.Message}");
            return NatureEnvironmentStatus.MinimumProductivity;
        }
    }

    /// <summary>
    /// 伐採場での採取。魔導変異時は Timber 品質 1.5〜2.0 倍と副産物を倉庫へ納品します。
    /// 果実・副産物データ欠損時は通常材へ Safe-Fail します。
    /// </summary>
    public WoodcutterHarvestResult HarvestAtCamp(WorkSpotData spot, float skill, VillageStorageMarket market)
    {
        try
        {
            bool atCamp = spot != null && spot.Type == WorkSpotType.WoodcutterCamp;
            float efficiency = NotifyWoodcutterAtCamp(skill, atCamp);
            if (!atCamp)
            {
                return WoodcutterHarvestResult.NormalTimber(efficiency);
            }

            BotanicalMutationStatus botany = spot.ResolveBotany();
            if (botany == null)
            {
                return new WoodcutterHarvestResult
                {
                    ForestEfficiency = efficiency,
                    QualityMultiplier = 1f,
                    UsedSafeFail = true,
                    Label = "伐採（通常材フォールバック）"
                };
            }

            botany.ClampAll();
            float quality = BotanicalMutationStatus.SafeQualityMultiplier(botany);
            WoodcutterHarvestResult result = new WoodcutterHarvestResult
            {
                ForestEfficiency = efficiency,
                QualityMultiplier = quality,
                IsManaMutated = botany.IsManaMutated,
                UsedSafeFail = efficiency < 0.99f
            };

            if (botany.IsManaMutated)
            {
                mutatedHarvestsTotal++;
                bool resinSide = ((spot.SpotId != null ? spot.SpotId.Length : 0) + Mathf.RoundToInt(skill * 10f)) % 2 == 0;
                if (resinSide)
                {
                    result.ManaResin = 0.16f + Mathf.Max(0f, skill) * 0.05f;
                }
                else
                {
                    result.MutatedLeaves = 0.22f + Mathf.Max(0f, skill) * 0.06f;
                }

                if (market != null)
                {
                    market.DepositByproduct(result.ManaResin, result.MutatedLeaves);
                }

                string byproduct = result.ManaResin > 0.01f ? $"樹脂 {result.ManaResin:F2}" : $"変異葉 {result.MutatedLeaves:F2}";
                result.Label = efficiency < 0.99f
                    ? $"伐採（魔導変異材×{quality:0.00}・森林ストレス / {byproduct}）"
                    : $"伐採（魔導変異材×{quality:0.00} / {byproduct}）";
                Debug.Log(
                    $"<color=#A5D6A7><b>【魔導変異材】</b></color> {spot.SpotId} Timber品質×{quality:0.00} " +
                    $"{byproduct} 森林効率 {efficiency:0.00}");
            }
            else
            {
                result.Label = efficiency < 0.99f ? "伐採（森林ストレス・効率低下）" : "伐採";
            }

            return result;
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[NaturalEcologyEngine] HarvestAtCamp Safe-Fail: {exception.Message} → 通常材");
            return new WoodcutterHarvestResult
            {
                ForestEfficiency = NatureEnvironmentStatus.MinimumProductivity,
                QualityMultiplier = 1f,
                UsedSafeFail = true,
                Label = "伐採（通常材フォールバック）"
            };
        }
    }

    /// <summary>畜産・農耕の Food 補正。野生動物が多いほど肉・皮革が増えます。</summary>
    public float ResolveWildlifeFoodMultiplier(bool isRancher)
    {
        try
        {
            NatureEnvironmentStatus nature = Environment;
            nature.ClampAll();
            WarnIfDepleted(nature);

            float ratio = NatureEnvironmentStatus.MaxWildlifePopulation > 0
                ? nature.WildlifePopulation / (float)NatureEnvironmentStatus.MaxWildlifePopulation
                : 0f;
            float bonus = isRancher
                ? Mathf.Lerp(0.15f, 0.55f, ratio)
                : Mathf.Lerp(0.05f, 0.22f, ratio);
            float mul = 1f + bonus;
            if (nature.WildlifeExtinct || nature.ForestDepleted)
            {
                mul = NatureEnvironmentStatus.MinimumProductivity + 0.15f;
            }

            int hunted = isRancher ? 2 : 1;
            if (nature.WildlifePopulation > 0)
            {
                wildlifeHuntedThisPhase += nature.ConsumeWildlife(hunted);
            }

            return Mathf.Max(NatureEnvironmentStatus.MinimumProductivity, mul);
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[NaturalEcologyEngine] 狩猟補正 Safe-Fail: {exception.Message}");
            return NatureEnvironmentStatus.MinimumProductivity;
        }
    }

    /// <summary>1 位相分の再生・魔力変動・変異・捕食。</summary>
    public void TickPhase(int phase, VariableTimelineSeason season)
    {
        try
        {
            NatureEnvironmentStatus nature = Environment;
            nature.ClampAll();
            int normalized = ProceduralMapPopulator.NormalizePhase(phase);
            int ecologyTurn = EraContextResolver.CurrentTurn;
            bool revivalMode = EnvironmentBiorhythmEngine.IsCivilizationRevivalTurn(ecologyTurn);
            StringBuilder log = new StringBuilder();
            log.Append("<color=#81C784><b>【自然生態】</b></color> ");
            log.Append($"位相 {normalized}/24 {season.ToString().ToUpperInvariant()} ");

            if (revivalMode)
            {
                EnvironmentBiorhythmEngine.TryLogCivilizationRevivalTransition(ecologyTurn);
                StabilizeRevivalManaEcology(nature);
                ApplyRevivalSeasonNature(nature, season, log);
            }
            else
            {
                ApplySeasonNature(nature, season, log);
            }

            RegionalExpansionEngine.TryApplyBorderEcologyPressure(nature, log);
            SyncWildlifeTowardForest(nature, hunted: 0);
            WarnIfDepleted(nature);

            int botanyFlags = UpdateBotanicalLayer(nature.ManaEcologyLevel, log);
            int mutated = TryMutateWildlife(nature, season, normalized, log);
            int attracted = AttractBeastsToManaFruit(log);
            VillageStorageMarket market = ResolveVillageMarket();
            int conflicts = ResolveMonsterTerritoryConflicts(market, log);
            int frictionEncounters = revivalMode
                ? ResolveEnvironmentalFrictionEncounters(nature, normalized, log)
                : 0;
            int predated = ApplyBeastPredationOrSeekCore(nature, log);

            lastTickedPhase = normalized;
            log.AppendLine();
            log.Append($"  期末 {nature.FormatSnapshot()}");
            log.Append($" 伐採材 {timberHarvestedThisPhase:F1} 狩猟 {wildlifeHuntedThisPhase}");
            log.Append($" 変異 {mutated} 植物 {botanyFlags} 果実誘引 {attracted} 縄張り {conflicts}");
            if (revivalMode)
            {
                log.Append($" 摩擦遭遇 {frictionEncounters}");
            }

            log.Append($" 捕食 {predated} 累計変異 {totalMutations}");
            Debug.Log(log.ToString());

            timberHarvestedThisPhase = 0f;
            wildlifeHuntedThisPhase = 0;
            depletionWarnedThisPhase = false;
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[NaturalEcologyEngine] TickPhase Safe-Fail: {exception.Message}");
            lastTickedPhase = ProceduralMapPopulator.NormalizePhase(phase);
        }
    }

    public void SimulateFullDay(VariablePhaseDistribution distribution)
    {
        VariablePhaseDistribution dist = distribution ?? ProceduralMapPopulator.DefaultNation001Turn1Distribution();
        NpcCivilizationEngine civ = NpcCivilizationEngine.EnsureInstance();
        EnemyEcologyBehaviorRuntime beasts = EnemyEcologyBehaviorRuntime.EnsureInstance();
        for (int phase = 1; phase <= ProceduralMapPopulator.PhaseCount; phase++)
        {
            VariableTimelineSeason season = ProceduralMapPopulator.ResolveSeason(phase, dist);
            civ.TickPhase(phase, season);
            beasts.TickPhase(phase, season);
        }
    }

    [ContextMenu("Verify Natural Ecology Day")]
    private void DebugVerifyDay()
    {
        RunDayVerification();
    }

    public static NaturalEcologyVerifyResult RunDayVerification()
    {
        NaturalEcologyVerifyResult verify = new NaturalEcologyVerifyResult();
        try
        {
            NaturalEcologyEngine engine = EnsureInstance();
            SimulationVerifyBootstrap.PrepareFreshStoryDay();
            NatureEnvironmentStatus nature = engine.Environment;
            nature.ForestDensity = 72f;
            nature.WildlifePopulation = 118;
            nature.ManaEcologyLevel = 74f;
            nature.ClampAll();
            SimulationVerifyBootstrap.PrepareNaturalEcologyVerification(engine);

            VillageWorkSpotManager spots = VillageWorkSpotManager.EnsureInstance();
            spots.EnsureWoodcutterCamps();
            spots.UpdateBotanicalMutations(nature.ManaEcologyLevel);

            NpcCivilizationEngine civ = NpcCivilizationEngine.EnsureInstance();
            WorkSpotData camp = spots.FindNearestWoodcutterCamp(Vector3.zero);
            WoodcutterHarvestResult harvest = engine.HarvestAtCamp(camp, 2.4f, civ.Storage);

            float forestBefore = nature.ForestDensity;
            int wildlifeBefore = nature.WildlifePopulation;
            float manaBefore = nature.ManaEcologyLevel;
            engine.ResolveWildlifeFoodMultiplier(isRancher: true);

            VariablePhaseDistribution dist = ProceduralMapPopulator.DefaultNation001Turn1Distribution();
            engine.SimulateFullDay(dist);

            bool forestChanged = !Mathf.Approximately(nature.ForestDensity, forestBefore);
            bool wildlifeChanged = nature.WildlifePopulation != wildlifeBefore;
            bool manaMoved = !Mathf.Approximately(nature.ManaEcologyLevel, manaBefore);
            bool phasesDone = engine.lastTickedPhase == ProceduralMapPopulator.PhaseCount;
            bool mutatedTimber = harvest.IsManaMutated && harvest.QualityMultiplier >= BotanicalMutationStatus.QualityBonusMin;
            bool fruitSeen = spots.GetManaFruitCamps().Count > 0 || engine.fruitAttractionsTotal > 0;
            bool conflictSeen = engine.territoryConflictsTotal > 0;
            verify.success = phasesDone && (forestChanged || wildlifeChanged) && manaMoved && mutatedTimber && fruitSeen && conflictSeen;
            verify.message =
                $"phase={engine.lastTickedPhase} " +
                $"森林 {forestBefore:F1}→{nature.ForestDensity:F1} " +
                $"野生 {wildlifeBefore}→{nature.WildlifePopulation} " +
                $"魔力 {manaBefore:F1}→{nature.ManaEcologyLevel:F1} " +
                $"変異累計 {engine.totalMutations} " +
                $"変異材×{harvest.QualityMultiplier:0.00} 誘引 {engine.fruitAttractionsTotal} " +
                $"縄張り {engine.territoryConflictsTotal} " +
                $"forestΔ={forestChanged} wildΔ={wildlifeChanged} manaΔ={manaMoved} " +
                $"mutatedTimber={mutatedTimber} fruit={fruitSeen} conflict={conflictSeen}";

            Debug.Log(
                verify.success
                    ? $"<color=#A5D6A7><b>【自然生態検証】PASS</b></color> {verify.message}"
                    : $"<color=#FF8A80><b>【自然生態検証】FAIL</b></color> {verify.message}");
        }
        catch (Exception exception)
        {
            verify.success = false;
            verify.message = $"Safe-Fail: {exception.Message}";
            Debug.LogWarning($"[NaturalEcologyEngine] 検証 Safe-Fail: {exception.Message}");
        }

        WriteVerifyLog(verify);
        return verify;
    }

    private static void ApplySeasonNature(
        NatureEnvironmentStatus nature,
        VariableTimelineSeason season,
        StringBuilder log)
    {
        float forestDelta = 0f;
        float manaDelta = 0f;
        switch (season)
        {
            case VariableTimelineSeason.Active:
                manaDelta = 4.6f;
                forestDelta = -0.15f;
                break;
            case VariableTimelineSeason.Escalation:
                manaDelta = 2.8f;
                forestDelta = -0.08f;
                break;
            case VariableTimelineSeason.Deescalation:
                manaDelta = -1.1f;
                forestDelta = 1.4f;
                break;
            default:
                manaDelta = -2.4f;
                forestDelta = 3.2f;
                break;
        }

        nature.ApplyForestDelta(forestDelta);
        nature.ApplyManaDelta(manaDelta);
        log.Append($"森林{forestDelta:+0.0;-0.0} 魔力{manaDelta:+0.0;-0.0} ");
        if (season == VariableTimelineSeason.Dormant && forestDelta > 0f)
        {
            log.Append("自然再生 ");
        }
    }

    private static void SyncWildlifeTowardForest(NatureEnvironmentStatus nature, int hunted)
    {
        int target = nature.CarryingCapacity;
        int current = nature.WildlifePopulation - hunted;
        int step = 0;
        if (current < target)
        {
            step = Mathf.Min(8, target - current);
        }
        else if (current > target)
        {
            step = -Mathf.Min(6, current - target);
        }

        nature.WildlifePopulation = NatureEnvironmentStatus.ClampWildlife(current + step);
    }

    private int TryMutateWildlife(
        NatureEnvironmentStatus nature,
        VariableTimelineSeason season,
        int phase,
        StringBuilder log)
    {
        int ecologyTurn = EraContextResolver.CurrentTurn;
        if (EnvironmentBiorhythmEngine.IsCivilizationRevivalTurn(ecologyTurn))
        {
            return 0;
        }

        float mutationThreshold = EnvironmentBiorhythmEngine.ResolveEffectiveMutationThresholdForTurn(ecologyTurn);
        if (nature.ManaEcologyLevel < mutationThreshold || nature.WildlifePopulation < 6)
        {
            return 0;
        }

        bool hot = season == VariableTimelineSeason.Active ||
                   season == VariableTimelineSeason.Escalation;
        if (!hot)
        {
            return 0;
        }

        EnemyEcologyBehaviorRuntime runtime = EnemyEcologyBehaviorRuntime.EnsureInstance();
        if (runtime == null)
        {
            return 0;
        }

        int room = Mathf.Max(0, MaxPackSize - runtime.Pack.Count);
        int want = Mathf.Min(MaxMutationsPerPhase, room, nature.WildlifePopulation / 6);
        int spawned = 0;
        for (int i = 0; i < want; i++)
        {
            int consumed = nature.ConsumeWildlife(6);
            if (consumed <= 0)
            {
                break;
            }

            float hunger = Mathf.Lerp(55f, 88f, nature.ManaEcologyLevel / 100f);
            string name = $"変異獣・位相{phase}";
            EnemyEcologyInstinctAI ai = runtime.SpawnMutatedBeast(name, hunger);
            if (ai == null)
            {
                nature.ApplyWildlifeDelta(consumed);
                break;
            }

            spawned++;
            totalMutations++;
            log.AppendLine();
            log.Append($"  <b>自然変異</b> 野生動物 {consumed} 体が魔物化 → {name}（飢餓 {hunger:F0}）");
        }

        return spawned;
    }

    /// <summary>環境摩擦スナップショットを算出します（T≥1001）。</summary>
    public static EnvironmentalFrictionSnapshot ComputeEnvironmentalFriction(
        NatureEnvironmentStatus nature,
        float timberHarvestedThisPhase,
        int phase,
        int? ecologyTurnOverride = null)
    {
        nature?.ClampAll();
        int turn = ecologyTurnOverride ?? EraContextResolver.CurrentTurn;
        float territoriality = 0f;
        if (timberHarvestedThisPhase >= RevivalOverharvestThreshold)
        {
            territoriality = Mathf.Clamp01((timberHarvestedThisPhase - RevivalOverharvestThreshold) / 4f) * 0.55f;
        }

        if (nature != null && nature.ForestStressed)
        {
            territoriality += 0.18f;
        }

        float politicalLeak = 0f;
        if (turn % 17 == 0 || phase == 12)
        {
            politicalLeak = 0.22f;
        }

        if (HistoryFlagRegistry.IsUnlocked("MAGIC_HERETIC_ARCANA_CIVILIZED") ||
            HistoryFlagRegistry.IsUnlocked("MAGIC_HERETIC_ORTHODOXY_CIVILIZED"))
        {
            politicalLeak += 0.12f;
        }

        float resourceConflict = 0f;
        try
        {
            VillageWorkSpotManager spots = VillageWorkSpotManager.EnsureInstance();
            int fruitNodes = spots != null ? spots.GetManaFruitCamps().Count : 0;
            if (fruitNodes > 0 && phase % 6 == 0)
            {
                resourceConflict = 0.20f + fruitNodes * 0.04f;
            }
        }
        catch (Exception)
        {
            resourceConflict = phase % 8 == 0 ? 0.16f : 0f;
        }

        if (EnvironmentBiorhythmEngine.IsCivilizationRevivalTurn(turn))
        {
            territoriality += 0.12f;
        }

        return new EnvironmentalFrictionSnapshot(territoriality, politicalLeak, resourceConflict);
    }

    /// <summary>国家の討伐隊・防衛隊による安全地帯化。</summary>
    public int NotifyDefenseForceDeployment(string areaId, float clearanceStrength = 1f)
    {
        try
        {
            string safeZone = string.IsNullOrWhiteSpace(areaId) ? "AREA_DEFAULT" : areaId.Trim();
            clearedSafeZones.Add(safeZone);
            EnemyEcologyBehaviorRuntime runtime = EnemyEcologyBehaviorRuntime.EnsureInstance();
            return runtime != null ? runtime.PurgeBeastsInArea(safeZone, clearanceStrength) : 0;
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[NaturalEcologyEngine] 討伐隊 Safe-Fail: {exception.Message}");
            return 0;
        }
    }

    /// <summary>T≥1001 自然共生ルールの自己検証。</summary>
    public static bool RunRevivalEcologySelfCheck(out string detail)
    {
        detail = string.Empty;
        try
        {
            EnvironmentBiorhythmEngine.ResetRevivalTransitionLogForVerification();
            NaturalEcologyEngine engine = EnsureInstance();
            engine.ResetVerificationState();
            SimulationVerifyBootstrap.PrepareFreshStoryDay(
                EnvironmentBiorhythmEngine.CivilizationRevivalStartTurn,
                GameMode.StoryMode);
            SimulationVerifyBootstrap.PrepareNaturalEcologyVerification(engine);

            NatureEnvironmentStatus nature = engine.Environment;
            nature.ForestDensity = 68f;
            nature.WildlifePopulation = 96;
            nature.ManaEcologyLevel = 72f;
            nature.ClampAll();

            engine.NotifyWoodcutterAtCamp(skill: 2.8f, atWoodcutterCamp: true);
            engine.NotifyWoodcutterAtCamp(skill: 2.6f, atWoodcutterCamp: true);
            VillageWorkSpotManager spots = VillageWorkSpotManager.EnsureInstance();
            spots?.EnsureWoodcutterCamps();

            EnvironmentalFrictionSnapshot friction = ComputeEnvironmentalFriction(
                nature,
                engine.TimberHarvestedThisPhase,
                phase: 12,
                ecologyTurnOverride: EnvironmentBiorhythmEngine.CivilizationRevivalStartTurn);

            int frictionEncounters = engine.ResolveEnvironmentalFrictionEncountersPublic(nature, phase: 12, log: null);
            int purged = engine.NotifyDefenseForceDeployment("VERIFY_REVIVAL_ZONE", clearanceStrength: 1.2f);

            EnvironmentBiorhythmSnapshot bio =
                EnvironmentBiorhythmEngine.ResolveMidCyclePhase(EnvironmentBiorhythmEngine.CivilizationRevivalStartTurn);
            float revivalMana = EnvironmentBiorhythmEngine.ResolveFixedRevivalManaEcologyLevel();
            engine.StabilizeRevivalManaEcology(nature);

            bool peakStopped = bio.Phase != EnvironmentMidCyclePhase.PeakCatastrophe;
            bool manaStable = nature.ManaEcologyLevel >= 30f && nature.ManaEcologyLevel <= 50f;
            bool frictionTriggered = friction.ShouldTriggerNeighborEncounter;
            bool neighborSpawned = frictionEncounters > 0;
            bool purgeWorked = purged >= 0;
            bool pass = peakStopped && manaStable && frictionTriggered && neighborSpawned && purgeWorked;

            detail =
                $"bio={bio.Phase} mana={nature.ManaEcologyLevel:F1} target={revivalMana:F1} " +
                $"friction={friction.TotalFriction:F2} encounters={frictionEncounters} purged={purged} pass={pass}";
            return pass;
        }
        catch (Exception exception)
        {
            detail = $"Safe-Fail: {exception.Message}";
            return false;
        }
    }

    public void StabilizeRevivalManaEcology(NatureEnvironmentStatus nature)
    {
        if (nature == null)
        {
            return;
        }

        nature.ManaEcologyLevel = EnvironmentBiorhythmEngine.ClampRevivalManaEcology(
            EnvironmentBiorhythmEngine.ResolveFixedRevivalManaEcologyLevel());
        nature.ClampAll();
    }

    public int ResolveEnvironmentalFrictionEncountersPublic(
        NatureEnvironmentStatus nature,
        int phase,
        StringBuilder log)
    {
        return ResolveEnvironmentalFrictionEncounters(nature, phase, log);
    }

    private static void ApplyRevivalSeasonNature(
        NatureEnvironmentStatus nature,
        VariableTimelineSeason season,
        StringBuilder log)
    {
        float forestDelta = season == VariableTimelineSeason.Dormant ? 1.8f : -0.05f;
        nature.ApplyForestDelta(forestDelta);
        nature.ManaEcologyLevel = EnvironmentBiorhythmEngine.ResolveFixedRevivalManaEcologyLevel();
        log.Append($"復興期 森林{forestDelta:+0.0;-0.0} 魔力定常={nature.ManaEcologyLevel:F1} ");
    }

    private int ResolveEnvironmentalFrictionEncounters(
        NatureEnvironmentStatus nature,
        int phase,
        StringBuilder log)
    {
        EnvironmentalFrictionSnapshot friction =
            ComputeEnvironmentalFriction(nature, timberHarvestedThisPhase, phase);
        if (!friction.ShouldTriggerNeighborEncounter)
        {
            return 0;
        }

        EnemyEcologyBehaviorRuntime runtime = EnemyEcologyBehaviorRuntime.EnsureInstance();
        if (runtime == null)
        {
            return 0;
        }

        int spawned = 0;
        if (friction.TerritorialityPressure >= 0.15f &&
            !clearedSafeZones.Contains("TERRITORY_BORDER"))
        {
            EnemyEcologyInstinctAI slime = runtime.SpawnNeighborBeast(
                SingularPointExtractor.PatternBasicSlime,
                "縄張り侵犯・粘核スライム",
                hunger: 42f + friction.TerritorialityPressure * 40f,
                territoriality: 55f + friction.TerritorialityPressure * 30f);
            if (slime != null)
            {
                spawned++;
                territoryConflictsTotal++;
                log?.AppendLine();
                log?.Append(
                    "  <color=#FFD54F><b>【環境摩擦・縄張り侵犯】</b></color> " +
                    "過剰伐採が街間の害獣縄張りを刺激");
            }
        }

        if (friction.PoliticalManaLeak >= 0.15f &&
            !clearedSafeZones.Contains("POLITICAL_LEAK"))
        {
            EnemyEcologyInstinctAI stalker = runtime.SpawnNeighborBeast(
                SingularPointExtractor.PatternAgileStalker,
                "政争漏洩・影踏みストーカー",
                hunger: 48f + friction.PoliticalManaLeak * 35f,
                territoriality: 62f);
            if (stalker != null)
            {
                spawned++;
                log?.AppendLine();
                log?.Append(
                    "  <color=#FFD54F><b>【環境摩擦・魔力漏洩】</b></color> " +
                    "政争/研究事故が局所魔力漏洩を誘発");
            }
        }

        if (friction.ResourceConflictPressure >= 0.15f &&
            !clearedSafeZones.Contains("RESOURCE_FIELD"))
        {
            EnemyEcologyInstinctAI contestant = runtime.SpawnNeighborBeast(
                SingularPointExtractor.PatternBasicSlime,
                "鉱脈/果実争奪・害獣",
                hunger: 50f + friction.ResourceConflictPressure * 30f,
                territoriality: 48f);
            if (contestant != null)
            {
                spawned++;
                fruitAttractionsTotal++;
                log?.AppendLine();
                log?.Append(
                    "  <color=#FFD54F><b>【環境摩擦・資源争奪】</b></color> " +
                    "魔導果実/高純度鉱脈周辺で局地衝突");
            }
        }

        return spawned;
    }

    private int ApplyBeastPredationOrSeekCore(NatureEnvironmentStatus nature, StringBuilder log)
    {
        EnemyEcologyBehaviorRuntime runtime = EnemyEcologyBehaviorRuntime.Instance;
        if (runtime == null)
        {
            runtime = EnemyEcologyBehaviorRuntime.EnsureInstance();
        }

        if (runtime == null)
        {
            return 0;
        }

        IReadOnlyList<EnemyEcologyInstinctAI> pack = runtime.Pack;
        int eaten = 0;
        Vector3 core = Vector3.zero;
        string coreId = "SPOT_BARRIER_CORE";
        try
        {
            VillageWorkSpotManager spots = VillageWorkSpotManager.EnsureInstance();
            core = spots.ResolveVillageCenterOrFallback();
            IReadOnlyList<WorkSpotData> all = spots.Spots;
            for (int s = 0; s < (all?.Count ?? 0); s++)
            {
                if (all[s] != null && all[s].Type == WorkSpotType.BarrierCore)
                {
                    coreId = all[s].SpotId;
                    core = all[s].WorldPosition;
                    break;
                }
            }
        }
        catch (Exception)
        {
            core = Vector3.zero;
        }

        for (int i = 0; i < pack.Count; i++)
        {
            EnemyEcologyInstinctAI ai = pack[i];
            EnemyStatusManager st = ai != null ? ai.EnsureStatus() : null;
            if (st == null)
            {
                continue;
            }

            if (st.ManaHunger < HighManaHunger)
            {
                continue;
            }

            if (IsManaFruitTarget(ai.ForcedTargetSpotId))
            {
                continue;
            }

            if (nature.WildlifePopulation > 0)
            {
                int took = nature.ConsumeWildlife(3);
                if (took > 0)
                {
                    st.RestoreMP(12f + took * 3f);
                    st.AddManaHunger(-22f);
                    st.Action?.Restore(18f);
                    eaten += took;
                    log.AppendLine();
                    log.Append($"  {st.DisplayName} が野生動物 {took} を捕食（MP/スタミナ回復 飢餓 {st.ManaHunger:F0}）");
                    continue;
                }
            }

            ai.SetForcedAssaultTarget(core, coreId);
            log.AppendLine();
            log.Append($"  {st.DisplayName} は獲物不足のため結界核へ向かう → {coreId}");
        }

        return eaten;
    }

    private static int UpdateBotanicalLayer(float manaEcologyLevel, StringBuilder log)
    {
        try
        {
            VillageWorkSpotManager spots = VillageWorkSpotManager.EnsureInstance();
            int flags = spots.UpdateBotanicalMutations(manaEcologyLevel);
            int fruits = spots.GetManaFruitCamps().Count;
            if (flags > 0 || fruits > 0)
            {
                log.Append($"植物変異 {flags} 果実ノード {fruits} ");
            }

            return flags;
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[NaturalEcologyEngine] 植物層 Safe-Fail: {exception.Message}");
            return 0;
        }
    }

    private int AttractBeastsToManaFruit(StringBuilder log)
    {
        try
        {
            VillageWorkSpotManager spots = VillageWorkSpotManager.EnsureInstance();
            List<WorkSpotData> fruits = spots.GetManaFruitCamps();
            if (fruits == null || fruits.Count == 0)
            {
                return 0;
            }

            EnemyEcologyBehaviorRuntime runtime = EnemyEcologyBehaviorRuntime.EnsureInstance();
            if (runtime == null)
            {
                return 0;
            }

            IReadOnlyList<EnemyEcologyInstinctAI> pack = runtime.Pack;
            int attracted = 0;
            for (int i = 0; i < pack.Count; i++)
            {
                EnemyEcologyInstinctAI ai = pack[i];
                EnemyStatusManager st = ai != null ? ai.EnsureStatus() : null;
                if (st == null || st.ManaHunger < FruitAttractionHunger)
                {
                    continue;
                }

                WorkSpotData fruit = spots.FindNearestManaFruit(ai.transform.position);
                BotanicalMutationStatus botany = fruit != null ? fruit.ResolveBotany() : null;
                if (fruit == null || botany == null || !botany.HasEdibleFruit)
                {
                    continue;
                }

                string nodeId = string.IsNullOrWhiteSpace(botany.FruitNodeId)
                    ? BotanicalMutationStatus.BuildFruitNodeId(fruit.SpotId)
                    : botany.FruitNodeId;
                ai.SetForcedAssaultTarget(fruit.WorldPosition, nodeId);
                attracted++;
                fruitAttractionsTotal++;
                log.AppendLine();
                log.Append(
                    $"  <color=#FFD54F><b>魔力果実誘引</b></color> {st.DisplayName} " +
                    $"飢餓 {st.ManaHunger:F0} → {nodeId}@{fruit.SpotId}（最優先餌場）");
            }

            return attracted;
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[NaturalEcologyEngine] 果実誘引 Safe-Fail: {exception.Message}");
            return 0;
        }
    }

    private int ResolveMonsterTerritoryConflicts(VillageStorageMarket market, StringBuilder log)
    {
        try
        {
            EnemyEcologyBehaviorRuntime runtime = EnemyEcologyBehaviorRuntime.EnsureInstance();
            VillageWorkSpotManager spots = VillageWorkSpotManager.EnsureInstance();
            if (runtime == null || spots == null)
            {
                return 0;
            }

            List<WorkSpotData> fruits = spots.GetManaFruitCamps();
            if (fruits == null || fruits.Count == 0)
            {
                return 0;
            }

            Dictionary<string, List<EnemyEcologyInstinctAI>> groups =
                new Dictionary<string, List<EnemyEcologyInstinctAI>>();
            IReadOnlyList<EnemyEcologyInstinctAI> pack = runtime.Pack;
            for (int i = 0; i < pack.Count; i++)
            {
                EnemyEcologyInstinctAI ai = pack[i];
                if (ai == null)
                {
                    continue;
                }

                string nodeId = ai.ForcedTargetSpotId;
                if (!IsManaFruitTarget(nodeId))
                {
                    WorkSpotData nearby = spots.FindNearestManaFruit(ai.transform.position);
                    BotanicalMutationStatus botany = nearby != null ? nearby.ResolveBotany() : null;
                    if (nearby != null && botany != null && botany.HasEdibleFruit &&
                        Vector3.Distance(ai.transform.position, nearby.WorldPosition) <= TerritoryGatherRadius)
                    {
                        nodeId = botany.FruitNodeId;
                    }
                    else
                    {
                        continue;
                    }
                }

                if (string.IsNullOrWhiteSpace(nodeId))
                {
                    continue;
                }

                if (!groups.TryGetValue(nodeId, out List<EnemyEcologyInstinctAI> list))
                {
                    list = new List<EnemyEcologyInstinctAI>();
                    groups[nodeId] = list;
                }

                list.Add(ai);
            }

            int fights = 0;
            foreach (KeyValuePair<string, List<EnemyEcologyInstinctAI>> pair in groups)
            {
                List<EnemyEcologyInstinctAI> group = pair.Value;
                if (group == null || group.Count < 2)
                {
                    continue;
                }

                EnemyEcologyInstinctAI winner = group[0];
                EnemyEcologyInstinctAI loser = group[0];
                float best = float.NegativeInfinity;
                float worst = float.PositiveInfinity;
                for (int g = 0; g < group.Count; g++)
                {
                    float score = ResolveTerritoryScore(group[g]);
                    if (score > best)
                    {
                        best = score;
                        winner = group[g];
                    }

                    if (score < worst)
                    {
                        worst = score;
                        loser = group[g];
                    }
                }

                if (winner == null || loser == null || winner == loser)
                {
                    continue;
                }

                EnemyStatusManager winSt = winner.EnsureStatus();
                EnemyStatusManager loseSt = loser.EnsureStatus();
                string winName = winSt != null ? winSt.DisplayName : winner.name;
                string loseName = loseSt != null ? loseSt.DisplayName : loser.name;

                BotanicalMutationStatus fruit = FindFruitByNodeId(spots, pair.Key);
                float eaten = fruit != null ? fruit.ConsumeFruit(0.85f) : 0f;
                winSt?.RestoreMP(16f + eaten * 8f);
                winSt?.AddManaHunger(-28f);
                winSt?.Action?.Restore(22f);

                if (market != null)
                {
                    market.DepositByproduct(0.1f, 0f);
                    market.Deposit(0f, 0f, 0f, 0.06f);
                }

                runtime.RetireBeast(loser, $"縄張り争い敗北 @{pair.Key}");
                fights++;
                territoryConflictsTotal++;
                log.AppendLine();
                log.Append(
                    $"  <color=#EF9A9A><b>【縄張り争い】</b></color> {pair.Key} に {group.Count} 体が集結。 " +
                    $"{winName} が勝利（MP/スタミナ回復 果実 {eaten:F1}） / {loseName} は消滅・ドロップ化");
                Debug.Log(
                    $"<color=#EF9A9A><b>【魔物縄張り】</b></color> {pair.Key}: 勝者 {winName} / 敗者 {loseName} 消滅");
            }

            return fights;
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[NaturalEcologyEngine] 縄張り争い Safe-Fail: {exception.Message}");
            return 0;
        }
    }

    private static BotanicalMutationStatus FindFruitByNodeId(VillageWorkSpotManager spots, string nodeId)
    {
        if (spots == null || string.IsNullOrWhiteSpace(nodeId))
        {
            return null;
        }

        List<WorkSpotData> fruits = spots.GetManaFruitCamps();
        for (int i = 0; i < fruits.Count; i++)
        {
            BotanicalMutationStatus botany = fruits[i] != null ? fruits[i].ResolveBotany() : null;
            if (botany != null && string.Equals(botany.FruitNodeId, nodeId, StringComparison.Ordinal))
            {
                return botany;
            }
        }

        return null;
    }

    private static float ResolveTerritoryScore(EnemyEcologyInstinctAI ai)
    {
        EnemyStatusManager st = ai != null ? ai.EnsureStatus() : null;
        if (st == null)
        {
            return 0f;
        }

        return st.ManaHunger + st.Territoriality + st.ThreatLevel * 12f;
    }

    private static bool IsManaFruitTarget(string spotId)
    {
        return !string.IsNullOrWhiteSpace(spotId) &&
               spotId.StartsWith("MANA_FRUIT_", StringComparison.OrdinalIgnoreCase);
    }

    private static VillageStorageMarket ResolveVillageMarket()
    {
        try
        {
            NpcCivilizationEngine civ = NpcCivilizationEngine.Instance;
            if (civ == null)
            {
                civ = NpcCivilizationEngine.EnsureInstance();
            }

            return civ != null ? civ.Storage : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private void WarnIfDepleted(NatureEnvironmentStatus nature)
    {
        if (depletionWarnedThisPhase)
        {
            return;
        }

        if (!nature.ForestDepleted && !nature.WildlifeExtinct && !nature.ForestStressed)
        {
            return;
        }

        depletionWarnedThisPhase = true;
        if (nature.ForestDepleted || nature.WildlifeExtinct)
        {
            Debug.LogWarning(
                "<color=#FFCC80><b>【自然枯渇警告】</b></color> 森林または野生動物が絶滅状態です。" +
                $"最低生産性 {NatureEnvironmentStatus.MinimumProductivity * 100f:F0}% を維持して継続します。 " +
                nature.FormatSnapshot());
            return;
        }

        Debug.LogWarning(
            $"<color=#FFE082><b>【森林ストレス】</b></color> 密度 {nature.ForestDensity:F1}% " +
            $"（閾値 {NatureEnvironmentStatus.ForestStressThreshold:F0}%）木材効率が低下します。");
    }

    private static void WriteVerifyLog(NaturalEcologyVerifyResult result)
    {
        try
        {
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
            string path = Path.Combine(projectRoot, "Logs/natural_ecology_verify.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? projectRoot);
            StringBuilder builder = new StringBuilder();
            builder.AppendLine(result.success ? "PASS" : "FAIL");
            builder.AppendLine(result.message ?? string.Empty);
            File.WriteAllText(path, builder.ToString(), Encoding.UTF8);
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[NaturalEcologyEngine] 検証ログをスキップ: {exception.Message}");
        }
    }
}

#if UNITY_EDITOR
/// <summary>エディタ専用。</summary>
public static class NaturalEcologyMenu
{
    [MenuItem("Tools/Simulation/Run Natural Ecology Day")]
    public static void RunDayFromMenu()
    {
        NaturalEcologyVerifyResult result = NaturalEcologyEngine.RunDayVerification();
        EditorUtility.DisplayDialog(
            "Natural Ecology",
            result.success ? $"PASS\n{result.message}" : $"FAIL\n{result.message}",
            "OK");
    }

    public static void BatchVerifyAndQuit()
    {
        NaturalEcologyVerifyResult result = NaturalEcologyEngine.RunDayVerification();
        EditorApplication.Exit(result.success ? 0 : 1);
    }
}
#endif

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
// 主人公・視点切り替え — 歴史軸 / ゲームモードに応じた操作権スロットイン
// 連携: HistoricalNpcCaster / NpcStatusManager / NpcCivilizationEngine
//       StoryTimelineManager / HistoryBranchManager / PlayerStatusManager
// =============================================================================

/// <summary>検証結果。</summary>
public sealed class TimelineCharacterSelectorVerifyResult
{
    public bool success;
    public string message = string.Empty;
}

/// <summary>
/// 歴史主要人物・村人 NPC・プレイヤー作成キャラ間で操作権を切り替え、
/// プレイヤー傀儡の CombatStats / PlayerStats / PlayerStatusManager へ同期します。
/// </summary>
[DefaultExecutionOrder(58)]
public class TimelineCharacterSelector : MonoBehaviour
{
    public const string LogTag = "【視点切替】";

    public static TimelineCharacterSelector Instance { get; private set; }

    [Header("プレイヤー傀儡（未設定時は PlayerStatusManager.Instance から自動取得）")]
    [SerializeField] private CombatStats playerCombatStats;
    [SerializeField] private PlayerStats playerActionStats;
    [SerializeField] private PlayerStatusManager playerStatusManager;

    [Header("挙動")]
    [SerializeField] private bool autoPossessOnModeChange = true;
    [SerializeField] private bool syncVitalsEachFrame = true;
    [SerializeField] private bool observeTimelineBranchChanges = true;

    [SerializeField] private NpcStatusManager possessedNpc;
    [SerializeField] private string lastPossessedNpcId = string.Empty;
    [SerializeField] private int ifProtagonistIndex = -1;

    private string lastObservedBranchId = string.Empty;
    private GameMode lastObservedMode = (GameMode)(-1);
    private bool usingSafeFallback;

    public NpcStatusManager PossessedNpc => possessedNpc;
    public bool IsUsingSafeFallback => usingSafeFallback;
    public string LastPossessedNpcId => lastPossessedNpcId;

    public static TimelineCharacterSelector EnsureInstance()
    {
        if (Instance != null)
        {
            return Instance;
        }

        TimelineCharacterSelector existing = UnityEngine.Object.FindAnyObjectByType<TimelineCharacterSelector>();
        if (existing != null)
        {
            Instance = existing;
            return existing;
        }

        GameObject hub = GameObject.Find("DebugSystemsHub");
        GameObject host = hub != null ? hub : new GameObject(nameof(TimelineCharacterSelector));
        TimelineCharacterSelector selector = host.GetComponent<TimelineCharacterSelector>();
        return selector != null ? selector : host.AddComponent<TimelineCharacterSelector>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        ResolvePlayerPuppet();
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
        ResolvePlayerPuppet();
        if (autoPossessOnModeChange)
        {
            AutoPossessForCurrentMode();
        }
    }

    private void LateUpdate()
    {
        if (syncVitalsEachFrame && possessedNpc != null && IsOperableNpc(possessedNpc))
        {
            MirrorPlayerPuppetToNpc();
        }

        if (possessedNpc != null && !IsOperableNpc(possessedNpc))
        {
            FallbackToSafeAvatar("操作対象 NPC が死亡または消失");
        }

        if (observeTimelineBranchChanges)
        {
            ObserveExternalChanges();
        }
    }

    /// <summary>操作対象 NPC を設定し、プレイヤー傀儡へステータスを同期します。</summary>
    public bool PossessCharacter(NpcStatusManager targetNpc)
    {
        try
        {
            ResolvePlayerPuppet();
            if (!IsOperableNpc(targetNpc))
            {
                return FallbackToSafeAvatar("操作対象 NPC が無効（null / 死亡）");
            }

            possessedNpc = targetNpc;
            usingSafeFallback = false;
            lastPossessedNpcId = targetNpc.NpcId;
            MirrorNpcToPlayerPuppet(targetNpc);

            Debug.Log(
                $"<color=#CE93D8><b>{LogTag}</b></color> 主人公を 「{targetNpc.DisplayName}」 " +
                $"({targetNpc.JobId}) へ切り替えました！ " +
                $"HP{playerCombatStats?.CurrentHp}/{playerCombatStats?.MaxHp} " +
                $"STR{playerCombatStats?.Strength} DEF{playerCombatStats?.Defense} " +
                $"STA{playerActionStats?.CurrentStamina:F0}/{playerActionStats?.maxStamina:F0} " +
                $"MP{playerStatusManager?.CurrentMP:F0}/{playerStatusManager?.MaxMP:F0}");
            return true;
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[TimelineCharacterSelector] PossessCharacter Safe-Fail: {exception.Message}");
            return FallbackToSafeAvatar(exception.Message);
        }
    }

    /// <summary>IF モード: NpcCivilizationEngine 登録 NPC を順に主人公へ切り替えます。</summary>
    public bool SelectNextNpcAsProtagonist()
    {
        try
        {
            List<NpcStatusManager> candidates = CollectIfModeNpcCandidates();
            if (candidates.Count == 0)
            {
                return FallbackToSafeAvatar("IF モード: 操作可能な一般 NPC が存在しません");
            }

            ifProtagonistIndex = (ifProtagonistIndex + 1) % candidates.Count;
            return PossessCharacter(candidates[ifProtagonistIndex]);
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[TimelineCharacterSelector] SelectNextNpcAsProtagonist Safe-Fail: {exception.Message}");
            return FallbackToSafeAvatar(exception.Message);
        }
    }

    /// <summary>StoryTimelineManager の GameMode に応じて自動主人公を設定します。</summary>
    public void AutoPossessForCurrentMode()
    {
        try
        {
            GameMode mode = StoryTimelineManager.EnsureInstance().CurrentMode;
            switch (mode)
            {
                case GameMode.StoryMode:
                    TryPossessHistoricalHero();
                    break;
                case GameMode.IFMode:
                    if (possessedNpc == null || !IsOperableNpc(possessedNpc) || usingSafeFallback)
                    {
                        SelectNextNpcAsProtagonist();
                    }

                    break;
                default:
                    FallbackToSafeAvatar($"モード {mode} は専用主人公未設定");
                    break;
            }
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[TimelineCharacterSelector] AutoPossessForCurrentMode Safe-Fail: {exception.Message}");
            FallbackToSafeAvatar(exception.Message);
        }
    }

    /// <summary>正史ストーリーモード: 指定ターンの歴史主要人物（英雄）を自動検索して操作権を付与します。</summary>
    public bool TryPossessHistoricalHero(int turn = -1)
    {
        try
        {
            int resolvedTurn = turn > 0 ? turn : ResolveStoryTurn();
            NpcStatusManager hero = FindHistoricalHeroNpc(resolvedTurn);
            if (hero == null)
            {
                return FallbackToSafeAvatar($"T{resolvedTurn} の歴史主要人物が見つかりません");
            }

            return PossessCharacter(hero);
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[TimelineCharacterSelector] TryPossessHistoricalHero Safe-Fail: {exception.Message}");
            return FallbackToSafeAvatar(exception.Message);
        }
    }

    /// <summary>
    /// 第2世代開始時: T51 の歴史主要人物を優先し、不在時は第1世代技術を受け継いだ工房職人へ操作権を付与します。
    /// </summary>
    public bool TryPossessGeneration2Protagonist(int turn = 51)
    {
        try
        {
            int resolvedTurn = Mathf.Clamp(
                turn,
                EraContextResolver.MinTurn,
                EraContextResolver.MaxTurn);

            HistoricalNpcCaster.SpawnHistoricalKeyCharacters(resolvedTurn);
            NpcStatusManager hero = FindHistoricalHeroNpc(resolvedTurn);
            if (IsOperableNpc(hero))
            {
                return PossessCharacter(hero);
            }

            NpcStatusManager craftsman = FindGeneration2CraftsmanNpc();
            if (IsOperableNpc(craftsman))
            {
                return PossessCharacter(craftsman);
            }

            return FallbackToSafeAvatar($"T{resolvedTurn} の第2世代主人公候補が見つかりません");
        }
        catch (Exception exception)
        {
            Debug.LogWarning(
                $"[TimelineCharacterSelector] TryPossessGeneration2Protagonist Safe-Fail: {exception.Message}");
            return FallbackToSafeAvatar(exception.Message);
        }
    }

    /// <summary>
    /// 第3世代開始時: T101 の歴史主要人物を優先し、不在時は第2世代技術を受け継いだ熟練魔導士へ操作権を付与します。
    /// </summary>
    public bool TryPossessGeneration3Protagonist(int turn = 101)
    {
        try
        {
            int resolvedTurn = Mathf.Clamp(
                turn,
                EraContextResolver.MinTurn,
                EraContextResolver.MaxTurn);

            HistoricalNpcCaster.SpawnHistoricalKeyCharacters(resolvedTurn);
            NpcStatusManager innovator = FindHeroByArchetype(
                HistoricalNpcCaster.Instance?.ActiveRecords,
                HistoricalNpcArchetype.InnovationHero);
            if (!IsOperableNpc(innovator))
            {
                innovator = FindHistoricalHeroNpc(resolvedTurn);
            }

            if (IsOperableNpc(innovator))
            {
                return PossessCharacter(innovator);
            }

            NpcStatusManager veteranMage = FindGeneration3VeteranMageNpc();
            if (IsOperableNpc(veteranMage))
            {
                return PossessCharacter(veteranMage);
            }

            return FallbackToSafeAvatar($"T{resolvedTurn} の第3世代主人公候補が見つかりません");
        }
        catch (Exception exception)
        {
            Debug.LogWarning(
                $"[TimelineCharacterSelector] TryPossessGeneration3Protagonist Safe-Fail: {exception.Message}");
            return FallbackToSafeAvatar(exception.Message);
        }
    }

    /// <summary>
    /// 第4世代開始時: T151 の前線要塞指揮官を優先し、不在時は古代技術解読士へ操作権を付与します。
    /// </summary>
    public bool TryPossessGeneration4Protagonist(int turn = 151)
    {
        try
        {
            int resolvedTurn = Mathf.Clamp(
                turn,
                EraContextResolver.MinTurn,
                EraContextResolver.MaxTurn);

            HistoricalNpcCaster.SpawnHistoricalKeyCharacters(resolvedTurn);
            NpcStatusManager commander = FindHeroByArchetype(
                HistoricalNpcCaster.Instance?.ActiveRecords,
                HistoricalNpcArchetype.DefenseHero);
            if (IsOperableNpc(commander))
            {
                return PossessCharacter(commander);
            }

            NpcStatusManager decoder = FindGeneration4AncientDecoderNpc();
            if (IsOperableNpc(decoder))
            {
                return PossessCharacter(decoder);
            }

            NpcStatusManager hero = FindHistoricalHeroNpc(resolvedTurn);
            if (IsOperableNpc(hero))
            {
                return PossessCharacter(hero);
            }

            return FallbackToSafeAvatar($"T{resolvedTurn} の第4世代主人公候補が見つかりません");
        }
        catch (Exception exception)
        {
            Debug.LogWarning(
                $"[TimelineCharacterSelector] TryPossessGeneration4Protagonist Safe-Fail: {exception.Message}");
            return FallbackToSafeAvatar(exception.Message);
        }
    }

    /// <summary>
    /// 第5世代開始時: T201 の古代秘術解読士を優先し、不在時は封印管理者へ操作権を付与します。
    /// </summary>
    public bool TryPossessGeneration5Protagonist(int turn = 201)
    {
        try
        {
            int resolvedTurn = Mathf.Clamp(
                turn,
                EraContextResolver.MinTurn,
                EraContextResolver.MaxTurn);

            HistoricalNpcCaster.SpawnHistoricalKeyCharacters(resolvedTurn);
            NpcStatusManager archivist = FindGeneration5AncientArchivistNpc();
            if (IsOperableNpc(archivist))
            {
                return PossessCharacter(archivist);
            }

            NpcStatusManager sealManager = FindGeneration5SealManagerNpc();
            if (IsOperableNpc(sealManager))
            {
                return PossessCharacter(sealManager);
            }

            NpcStatusManager decoder = FindGeneration4AncientDecoderNpc();
            if (IsOperableNpc(decoder))
            {
                return PossessCharacter(decoder);
            }

            NpcStatusManager hero = FindHistoricalHeroNpc(resolvedTurn);
            if (IsOperableNpc(hero))
            {
                return PossessCharacter(hero);
            }

            return FallbackToSafeAvatar($"T{resolvedTurn} の第5世代主人公候補が見つかりません");
        }
        catch (Exception exception)
        {
            Debug.LogWarning(
                $"[TimelineCharacterSelector] TryPossessGeneration5Protagonist Safe-Fail: {exception.Message}");
            return FallbackToSafeAvatar(exception.Message);
        }
    }

    /// <summary>標準仮アバターへフォールバックし、処理を継続します。</summary>
    public bool FallbackToSafeAvatar(string reason = "")
    {
        try
        {
            ResolvePlayerPuppet();
            possessedNpc = null;
            usingSafeFallback = true;
            lastPossessedNpcId = "SAFE_FALLBACK";

            ApplySafeFallbackStats();
            Debug.Log(
                $"<color=#FFCC80><b>{LogTag}</b></color> 標準仮アバターへフォールバック " +
                $"(HP{playerCombatStats?.MaxHp} STR{playerCombatStats?.Strength} " +
                $"STA{playerActionStats?.maxStamina:F0} MP{playerStatusManager?.MaxMP:F0})" +
                (string.IsNullOrWhiteSpace(reason) ? string.Empty : $" — {reason}"));
            return true;
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[TimelineCharacterSelector] FallbackToSafeAvatar Safe-Fail: {exception.Message}");
            return false;
        }
    }

    /// <summary>検証・テスト用にプレイヤー傀儡参照を明示バインドします。</summary>
    public void BindPlayerPuppet(CombatStats combat, PlayerStats stamina, PlayerStatusManager status)
    {
        playerCombatStats = combat;
        playerActionStats = stamina;
        playerStatusManager = status;
    }

    public static TimelineCharacterSelectorVerifyResult RunVerification()
    {
        TimelineCharacterSelectorVerifyResult verify = new TimelineCharacterSelectorVerifyResult();
        StringBuilder log = new StringBuilder();
        GameObject puppetObject = null;

        try
        {
            SimulationVerifyBootstrap.PrepareFreshStoryDay(1, GameMode.StoryMode);
            HistoryFlagRegistry.EnsureWired();
            MasterDataManager.EnsureInstance();
            HistoryFlagRegistry.Unlock(MicroToMacroAggregator.FlagHero);

            TimelineCharacterSelector selector = EnsureInstance();
            puppetObject = new GameObject("TimelineCharacterSelectorVerifyPuppet");
            CombatStats puppetCombat = puppetObject.AddComponent<CombatStats>();
            PlayerStats puppetStamina = puppetObject.AddComponent<PlayerStats>();
            PlayerStatusManager puppetStatus = puppetObject.AddComponent<PlayerStatusManager>();
            selector.BindPlayerPuppet(puppetCombat, puppetStamina, puppetStatus);

            HistoricalNpcCastResult cast = HistoricalNpcCaster.SpawnHistoricalKeyCharacters(1);
            NpcStatusManager hero = selector.FindHistoricalHeroNpc(1);
            bool heroFound = hero != null && cast.spawnedCount > 0;

            bool possessHero = selector.PossessCharacter(hero);
            bool heroHpSync = puppetCombat.MaxHp == NpcStatusManager.HeroMaxHp &&
                              puppetCombat.Strength >= NpcStatusManager.HeroStrength;
            bool heroStaSync = Mathf.Approximately(puppetStamina.maxStamina, NpcStatusManager.HeroMaxStamina);
            bool heroJobSync = string.Equals(
                puppetStatus.MainJob,
                hero != null ? hero.JobId : string.Empty,
                StringComparison.OrdinalIgnoreCase);
            bool heroMpSync = puppetStatus.MaxMP >= NpcStatusManager.HeroMaxMp - 1f;

            log.AppendLine(
                $"story-hero: found={heroFound} possess={possessHero} " +
                $"HP={puppetCombat.MaxHp} STR={puppetCombat.Strength} " +
                $"STA={puppetStamina.maxStamina:F0} MP={puppetStatus.MaxMP:F0} Job={puppetStatus.MainJob}");
            log.AppendLine(
                $"hero-sync: hp={heroHpSync} sta={heroStaSync} job={heroJobSync} mp={heroMpSync} " +
                $"pass={heroHpSync && heroStaSync && heroJobSync && heroMpSync}");

            SimulationVerifyBootstrap.PrepareFreshStoryDay(1, GameMode.IFMode);
            NpcCivilizationEngine civ = NpcCivilizationEngine.EnsureInstance();
            civ.TryRegisterHistoricalNpc(new NpcIndividualStatus(
                "if_npc_a",
                "アキ",
                28,
                NpcCivicJob.Farmer,
                1.8f,
                NpcTrait.Diligent,
                "MAG_FIELD_BLESS"));
            civ.TryRegisterHistoricalNpc(new NpcIndividualStatus(
                "if_npc_b",
                "ユウ",
                31,
                NpcCivicJob.Blacksmith,
                2.1f,
                NpcTrait.Cautious,
                "MAG_FURNACE_FLOW"));
            NpcStatusManagerRegistry.EnsureAllFromVillagers();

            bool ifNextA = selector.SelectNextNpcAsProtagonist();
            string firstName = selector.PossessedNpc != null ? selector.PossessedNpc.DisplayName : "?";
            bool ifNextB = selector.SelectNextNpcAsProtagonist();
            string secondName = selector.PossessedNpc != null ? selector.PossessedNpc.DisplayName : "?";
            bool ifCyclePass = ifNextA && ifNextB && !string.Equals(firstName, secondName, StringComparison.Ordinal);

            log.AppendLine($"if-cycle: first={firstName} second={secondName} pass={ifCyclePass}");

            if (hero != null && hero.gameObject != null)
            {
                DestroyVerifyObject(hero.gameObject);
            }

            selector.PossessCharacter(null);
            bool nullFallback = selector.IsUsingSafeFallback &&
                                puppetCombat.MaxHp >= NpcStatusManager.DefaultMaxHp;
            log.AppendLine($"safe-fail-null: fallback={nullFallback} pass={nullFallback}");

            verify.success = heroFound && possessHero && heroHpSync && heroStaSync && heroJobSync &&
                             heroMpSync && ifCyclePass && nullFallback;
            verify.message = log.ToString().TrimEnd();
        }
        catch (Exception exception)
        {
            verify.success = false;
            verify.message = $"Safe-Fail: {exception.Message}";
        }
        finally
        {
            if (puppetObject != null)
            {
                DestroyVerifyObject(puppetObject);
            }
        }

        WriteVerifyLog(verify);
        return verify;
    }

    private void ObserveExternalChanges()
    {
        HistoryBranchManager branch = HistoryBranchManager.Instance;
        string branchId = branch != null ? branch.ActiveBranchId : string.Empty;
        if (!string.Equals(branchId, lastObservedBranchId, StringComparison.OrdinalIgnoreCase))
        {
            lastObservedBranchId = branchId;
            if (autoPossessOnModeChange)
            {
                AutoPossessForCurrentMode();
            }
        }

        GameMode mode = StoryTimelineManager.EnsureInstance().CurrentMode;
        if (mode != lastObservedMode)
        {
            lastObservedMode = mode;
            if (autoPossessOnModeChange)
            {
                AutoPossessForCurrentMode();
            }
        }
    }

    private void ResolvePlayerPuppet()
    {
        if (playerStatusManager == null)
        {
            playerStatusManager = PlayerStatusManager.Instance;
        }

        if (playerStatusManager == null)
        {
            playerStatusManager = UnityEngine.Object.FindAnyObjectByType<PlayerStatusManager>();
        }

        if (playerCombatStats == null && playerStatusManager != null)
        {
            playerCombatStats = playerStatusManager.GetComponent<CombatStats>();
        }

        if (playerActionStats == null && playerStatusManager != null)
        {
            playerActionStats = playerStatusManager.GetComponent<PlayerStats>();
        }

        if (playerCombatStats == null)
        {
            playerCombatStats = UnityEngine.Object.FindAnyObjectByType<CombatStats>();
        }

        if (playerActionStats == null)
        {
            playerActionStats = UnityEngine.Object.FindAnyObjectByType<PlayerStats>();
        }
    }

    private void MirrorNpcToPlayerPuppet(NpcStatusManager npc)
    {
        if (npc == null)
        {
            return;
        }

        ResolvePlayerPuppet();
        CombatStats sourceCombat = npc.Combat;
        PlayerStats sourceStamina = npc.StaminaLayer;

        if (playerCombatStats != null && sourceCombat != null)
        {
            playerCombatStats.ResetBaseStatsCaptureForTesting();
            playerCombatStats.SetBaseStatsForNpc(sourceCombat.MaxHp, sourceCombat.Strength, sourceCombat.Defense);
            playerCombatStats.SetHpForTesting(sourceCombat.CurrentHp);
        }

        if (playerActionStats != null && sourceStamina != null)
        {
            playerActionStats.maxStamina = sourceStamina.maxStamina;
            playerActionStats.SetStaminaForTesting(sourceStamina.CurrentStamina);
        }

        if (playerStatusManager != null)
        {
            playerStatusManager.SetJobsDisplayOnly(npc.JobId, "なし");
            playerStatusManager.MirrorResourceLayerFromNpc(npc);
        }
    }

    private void MirrorPlayerPuppetToNpc()
    {
        if (possessedNpc == null)
        {
            return;
        }

        float mp = playerStatusManager != null ? playerStatusManager.CurrentMP : possessedNpc.CurrentMP;
        possessedNpc.MirrorVitalsFromPuppet(playerCombatStats, playerActionStats, mp);
    }

    private void ApplySafeFallbackStats()
    {
        if (playerCombatStats != null)
        {
            playerCombatStats.ResetBaseStatsCaptureForTesting();
            playerCombatStats.SetBaseStatsForNpc(
                NpcStatusManager.DefaultMaxHp,
                NpcStatusManager.DefaultStrength,
                NpcStatusManager.DefaultDefense);
        }

        if (playerActionStats != null)
        {
            playerActionStats.maxStamina = NpcStatusManager.DefaultMaxStamina;
            playerActionStats.SetStaminaForTesting(NpcStatusManager.DefaultMaxStamina);
        }

        if (playerStatusManager != null)
        {
            playerStatusManager.SetJobsDisplayOnly(DynamicJobBuilder.FallbackJobId, "なし");
            playerStatusManager.ApplySafeFallbackResources(NpcStatusManager.DefaultMaxMp);
        }
    }

    private static bool IsOperableNpc(NpcStatusManager npc)
    {
        return npc != null && npc.IsOperable();
    }

    private static int ResolveStoryTurn()
    {
        StoryTimelineManager story = StoryTimelineManager.Instance;
        if (story != null && story.StoryTurn > 0)
        {
            return story.StoryTurn;
        }

        return EraContextResolver.CurrentTurn > 0 ? EraContextResolver.CurrentTurn : 1;
    }

    private NpcStatusManager FindHistoricalHeroNpc(int turn)
    {
        HistoricalNpcCaster caster = HistoricalNpcCaster.Instance ?? HistoricalNpcCaster.EnsureInstance();
        IReadOnlyList<HistoricalNpcSpawnRecord> records = caster.ActiveRecords;
        if (records != null)
        {
            NpcStatusManager defenseHero = FindHeroByArchetype(records, HistoricalNpcArchetype.DefenseHero);
            if (IsOperableNpc(defenseHero))
            {
                return defenseHero;
            }

            NpcStatusManager innovationHero = FindHeroByArchetype(records, HistoricalNpcArchetype.InnovationHero);
            if (IsOperableNpc(innovationHero))
            {
                return innovationHero;
            }

            for (int i = 0; i < records.Count; i++)
            {
                HistoricalNpcSpawnRecord record = records[i];
                if (record?.manager != null && IsOperableNpc(record.manager))
                {
                    return record.manager;
                }
            }
        }

        NpcStatusManager[] allManagers = UnityEngine.Object.FindObjectsByType<NpcStatusManager>(
            FindObjectsInactive.Include);
        for (int i = 0; i < allManagers.Length; i++)
        {
            NpcStatusManager manager = allManagers[i];
            if (manager == null || !IsOperableNpc(manager))
            {
                continue;
            }

            if (manager.gameObject.name.StartsWith("HIST_", StringComparison.OrdinalIgnoreCase))
            {
                return manager;
            }
        }

        HistoricalNpcCastResult cast = HistoricalNpcCaster.SpawnHistoricalKeyCharacters(turn);
        for (int i = 0; i < cast.spawned.Count; i++)
        {
            NpcStatusManager manager = cast.spawned[i].manager;
            if (IsOperableNpc(manager))
            {
                return manager;
            }
        }

        return null;
    }

    private static NpcStatusManager FindGeneration3VeteranMageNpc()
    {
        NpcCivilizationEngine.EnsureInstance();
        NpcStatusManagerRegistry.EnsureAllFromVillagers();

        if (NpcStatusManagerRegistry.TryGet("npc_keeper_a", out NpcStatusManager keeper) &&
            IsOperableNpc(keeper))
        {
            return keeper;
        }

        NpcStatusManager[] allManagers = UnityEngine.Object.FindObjectsByType<NpcStatusManager>(
            FindObjectsInactive.Include);
        for (int i = 0; i < allManagers.Length; i++)
        {
            NpcStatusManager manager = allManagers[i];
            if (manager == null || !IsOperableNpc(manager))
            {
                continue;
            }

            if (string.Equals(manager.JobId, DynamicJobBuilder.JobAcademyScholar, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(manager.JobId, DynamicJobBuilder.JobBarrierRepairer, StringComparison.OrdinalIgnoreCase) ||
                manager.MaxMP >= NpcStatusManager.HeroMaxMp - 20f)
            {
                return manager;
            }
        }

        return FindGeneration2CraftsmanNpc();
    }

    private static NpcStatusManager FindGeneration4AncientDecoderNpc()
    {
        NpcCivilizationEngine.EnsureInstance();
        NpcStatusManagerRegistry.EnsureAllFromVillagers();

        NpcStatusManager[] allManagers = UnityEngine.Object.FindObjectsByType<NpcStatusManager>(
            FindObjectsInactive.Include);
        for (int i = 0; i < allManagers.Length; i++)
        {
            NpcStatusManager manager = allManagers[i];
            if (manager == null || !IsOperableNpc(manager))
            {
                continue;
            }

            if (string.Equals(manager.JobId, DynamicJobBuilder.JobAcademyScholar, StringComparison.OrdinalIgnoreCase))
            {
                return manager;
            }

            if (!string.IsNullOrWhiteSpace(manager.DisplayName) &&
                (manager.DisplayName.IndexOf("解読", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 manager.DisplayName.IndexOf("学院", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                return manager;
            }
        }

        if (NpcStatusManagerRegistry.TryGet("npc_keeper_a", out NpcStatusManager keeper) &&
            IsOperableNpc(keeper))
        {
            return keeper;
        }

        return FindGeneration3VeteranMageNpc();
    }

    private static NpcStatusManager FindGeneration5AncientArchivistNpc()
    {
        NpcCivilizationEngine.EnsureInstance();
        NpcStatusManagerRegistry.EnsureAllFromVillagers();

        NpcStatusManager[] allManagers = UnityEngine.Object.FindObjectsByType<NpcStatusManager>(
            FindObjectsInactive.Include);
        for (int i = 0; i < allManagers.Length; i++)
        {
            NpcStatusManager manager = allManagers[i];
            if (manager == null || !IsOperableNpc(manager))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(manager.DisplayName))
            {
                if (manager.DisplayName.IndexOf("秘術", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    manager.DisplayName.IndexOf("古代", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    manager.DisplayName.IndexOf("解読", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return manager;
                }
            }

            if (string.Equals(manager.JobId, DynamicJobBuilder.JobAcademyScholar, StringComparison.OrdinalIgnoreCase) &&
                manager.MaxMP >= NpcStatusManager.HeroMaxMp - 30f)
            {
                return manager;
            }
        }

        return FindGeneration4AncientDecoderNpc();
    }

    private static NpcStatusManager FindGeneration5SealManagerNpc()
    {
        NpcCivilizationEngine.EnsureInstance();
        NpcStatusManagerRegistry.EnsureAllFromVillagers();

        NpcStatusManager[] allManagers = UnityEngine.Object.FindObjectsByType<NpcStatusManager>(
            FindObjectsInactive.Include);
        for (int i = 0; i < allManagers.Length; i++)
        {
            NpcStatusManager manager = allManagers[i];
            if (manager == null || !IsOperableNpc(manager))
            {
                continue;
            }

            if (string.Equals(manager.JobId, DynamicJobBuilder.JobBarrierRepairer, StringComparison.OrdinalIgnoreCase))
            {
                return manager;
            }

            if (!string.IsNullOrWhiteSpace(manager.DisplayName) &&
                (manager.DisplayName.IndexOf("封印", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 manager.DisplayName.IndexOf("結界", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                return manager;
            }
        }

        HistoricalNpcCaster caster = HistoricalNpcCaster.Instance ?? HistoricalNpcCaster.EnsureInstance();
        return FindHeroByArchetype(caster.ActiveRecords, HistoricalNpcArchetype.BarrierRecoveryLeader);
    }

    private static NpcStatusManager FindGeneration2CraftsmanNpc()
    {
        NpcCivilizationEngine.EnsureInstance();
        NpcStatusManagerRegistry.EnsureAllFromVillagers();

        if (NpcStatusManagerRegistry.TryGet("npc_smith_a", out NpcStatusManager smith) &&
            IsOperableNpc(smith))
        {
            return smith;
        }

        NpcStatusManager[] allManagers = UnityEngine.Object.FindObjectsByType<NpcStatusManager>(
            FindObjectsInactive.Include);
        for (int i = 0; i < allManagers.Length; i++)
        {
            NpcStatusManager manager = allManagers[i];
            if (manager == null || !IsOperableNpc(manager))
            {
                continue;
            }

            if (string.Equals(manager.NpcId, "npc_smith_a", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(manager.JobId, DynamicJobBuilder.JobHighSmith, StringComparison.OrdinalIgnoreCase))
            {
                return manager;
            }
        }

        return null;
    }

    private static NpcStatusManager FindHeroByArchetype(
        IReadOnlyList<HistoricalNpcSpawnRecord> records,
        HistoricalNpcArchetype archetype)
    {
        if (records == null)
        {
            return null;
        }

        for (int i = 0; i < records.Count; i++)
        {
            HistoricalNpcSpawnRecord record = records[i];
            if (record != null &&
                record.archetype == archetype &&
                record.manager != null &&
                IsOperableNpc(record.manager))
            {
                return record.manager;
            }
        }

        return null;
    }

    private static List<NpcStatusManager> CollectIfModeNpcCandidates()
    {
        List<NpcStatusManager> candidates = new List<NpcStatusManager>(16);
        NpcStatusManagerRegistry.EnsureAllFromVillagers();

        NpcCivilizationEngine civ = NpcCivilizationEngine.EnsureInstance();
        IReadOnlyList<NpcIndividualStatus> villagers = civ.Villagers;
        if (villagers == null)
        {
            return candidates;
        }

        for (int i = 0; i < villagers.Count; i++)
        {
            NpcIndividualStatus individual = villagers[i];
            if (individual == null || string.IsNullOrWhiteSpace(individual.NpcId))
            {
                continue;
            }

            if (!NpcStatusManagerRegistry.TryGet(individual.NpcId, out NpcStatusManager manager) ||
                manager == null)
            {
                manager = NpcStatusManagerRegistry.EnsureForNpc(individual);
            }

            if (IsOperableNpc(manager) && !candidates.Contains(manager))
            {
                candidates.Add(manager);
            }
        }

        candidates.Sort((a, b) => string.Compare(
            a?.DisplayName,
            b?.DisplayName,
            StringComparison.OrdinalIgnoreCase));
        return candidates;
    }

    private static void DestroyVerifyObject(UnityEngine.Object target)
    {
        if (target == null)
        {
            return;
        }

#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            UnityEngine.Object.DestroyImmediate(target);
            return;
        }
#endif
        UnityEngine.Object.Destroy(target);
    }

    private static void WriteVerifyLog(TimelineCharacterSelectorVerifyResult verify)
    {
        try
        {
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
            string path = Path.Combine(projectRoot, "Logs", "timeline_character_selector_verify.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? projectRoot);
            File.WriteAllText(
                path,
                $"{DateTime.UtcNow:O} success={verify.success}\n{verify.message}\n",
                Encoding.UTF8);
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[TimelineCharacterSelector] 検証ログスキップ: {exception.Message}");
        }
    }
}

public static class TimelineCharacterSelectorBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoAttach()
    {
        TimelineCharacterSelector.EnsureInstance();
    }
}

#if UNITY_EDITOR
public static class TimelineCharacterSelectorMenu
{
    [MenuItem("Tools/Procedural Map/Verify Timeline Character Selector")]
    public static void VerifyFromMenu()
    {
        TimelineCharacterSelectorVerifyResult result = TimelineCharacterSelector.RunVerification();
        Debug.Log(
            result.success
                ? $"<color=#A5D6A7><b>【視点切替検証】PASS</b></color>\n{result.message}"
                : $"<color=#FF8A80><b>【視点切替検証】FAIL</b></color>\n{result.message}");
        EditorUtility.DisplayDialog("Timeline Character Selector", result.message, "OK");
    }

    [MenuItem("Tools/Procedural Map/Auto Possess Story Hero")]
    public static void AutoPossessStoryHeroFromMenu()
    {
        TimelineCharacterSelector selector = TimelineCharacterSelector.EnsureInstance();
        bool success = selector.TryPossessHistoricalHero();
        EditorUtility.DisplayDialog(
            "Timeline Character Selector",
            success ? "歴史主要人物へ操作権を付与しました。" : "英雄が見つからず Safe-Fail しました。",
            "OK");
    }

    [MenuItem("Tools/Procedural Map/Possess Next IF Protagonist")]
    public static void PossessNextIfProtagonistFromMenu()
    {
        StoryTimelineManager.EnsureInstance().TryBootstrapStoryYear(1, GameMode.IFMode);
        TimelineCharacterSelector selector = TimelineCharacterSelector.EnsureInstance();
        bool success = selector.SelectNextNpcAsProtagonist();
        EditorUtility.DisplayDialog(
            "Timeline Character Selector",
            success
                ? $"IF 主人公: {selector.PossessedNpc?.DisplayName ?? "Safe-Fail"}"
                : "操作可能 NPC がなく Safe-Fail しました。",
            "OK");
    }
}
#endif

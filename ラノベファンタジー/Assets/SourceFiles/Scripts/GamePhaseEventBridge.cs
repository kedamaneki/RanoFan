using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

// =============================================================================
// ゲーム内イベント × GamePhase × AI パイプライン統括ブリッジ
// DebugSystemsHub または PlayerRobot に配置してください。
// =============================================================================

/// <summary>プレイログ加算時に使用する行動メトリクス種別（文字列定数）。</summary>
public static class GameplayActionMetricTypes
{
    /// <summary>斬撃ヒット</summary>
    public const string Slash = "Slash";

    /// <summary>打撃ヒット</summary>
    public const string Blunt = "Blunt";

    /// <summary>突刺ヒット</summary>
    public const string Pierce = "Pierce";

    /// <summary>ジャスト回避（ニアミス）</summary>
    public const string JustEvasion = "JustEvasion";
}

/// <summary>Inspector 設定用の融合対価エントリ（Unity シリアライズ対応）。</summary>
[Serializable]
public sealed class FusionSacrificeSkillEntry
{
    public string skillId = "skill_fire_spark";
    public string skillName = "火の粉";

    /// <summary>ランタイム融合処理用の SacrificeSkillData に変換します。</summary>
    public SacrificeSkillData ToSacrificeSkillData()
    {
        return new SacrificeSkillData(skillId, skillName);
    }
}

/// <summary>AI パイプライン実行結果。</summary>
public sealed class GamePhasePipelineResult
{
    public bool Succeeded { get; }
    public GamePhase Phase { get; }
    public string BuiltPrompt { get; }
    public AIGenerationResult ParseResult { get; }
    public string Message { get; }

    public GamePhasePipelineResult(
        bool succeeded,
        GamePhase phase,
        string builtPrompt,
        AIGenerationResult parseResult,
        string message)
    {
        Succeeded = succeeded;
        Phase = phase;
        BuiltPrompt = builtPrompt ?? string.Empty;
        ParseResult = parseResult;
        Message = message ?? string.Empty;
    }
}

/// <summary>
/// リアルタイムのプレイ出来事と AI インフラ（プロンプト・実通信・永続化・閃き付与）を繋ぐハブ。
/// </summary>
public class GamePhaseEventBridge : MonoBehaviour
{
    public static GamePhaseEventBridge Instance { get; private set; }

    [Header("参照（未設定時は自動検索）")]
    [SerializeField] private PlayerStatusManager statusManager;
    [SerializeField] private InspirationManager inspirationManager;
    [SerializeField] private PlayerSkillSlotManager skillSlotManager;
    [SerializeField] private PlayerController playerController;

    [Header("フェーズ判定")]
    [Tooltip("HP がこの割合以下で Crisis へ強制移行")]
    [Range(0.05f, 0.5f)]
    [SerializeField] private float crisisHpThreshold = 0.3f;

    [Header("Crafting デバッグ入力")]
    [SerializeField] private float defaultCraftQuality = 0.75f;
    [SerializeField] private List<string> defaultCraftMaterials = new List<string> { "鉄鉱石", "獣骨" };
    [SerializeField] private bool requestNewRecipeOnCrafting;

    [Header("Fusion 対価（Fusion フェーズ用・任意）")]
    [SerializeField] private List<FusionSacrificeSkillEntry> fusionSacrificeSkills = new List<FusionSacrificeSkillEntry>();

    [Header("ステータス割り振り表示（履歴ログ用）")]
    [SerializeField] private string preferredStatAllocationLabel = "未設定";

    [Header("起動時同期")]
    [SerializeField] private bool syncFromPlayerActionLoggerOnStart = true;

    private RealAICommunicationService communicationService;
    private PlayerSkillInventory fusionInventory;
    private bool pipelineRunning;
    private CraftExperimentReport pendingCraftExperiment;
    private float pendingCraftQuality = 0.75f;

    /// <summary>現在の GamePhase（EvaluateCurrentPhase で更新）。</summary>
    public GamePhase currentPhase { get; private set; } = GamePhase.Training;

    /// <summary>プレイ中に蓄積される実ランタイム行動履歴。</summary>
    public PlayerHistoryLog runtimeHistoryLog { get; private set; } = new PlayerHistoryLog();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[GamePhaseEventBridge] 重複インスタンスを検出しました。");
            return;
        }

        Instance = this;
        ResolveReferences();
        InitializeRuntimeHistory();
    }

    private void Start()
    {
        if (syncFromPlayerActionLoggerOnStart)
        {
            SyncFromPlayerActionLogger();
        }
    }

    private void OnDestroy()
    {
        communicationService?.Dispose();
        communicationService = null;

        if (Instance == this)
        {
            Instance = null;
        }
    }

    /// <summary>
    /// 戦闘・回避システムから呼び出し、ランタイム履歴カウンターを加算します。
    /// </summary>
    /// <param name="actionType">Slash / Blunt / Pierce / JustEvasion 等</param>
    /// <param name="value">加算量（通常は 1）</param>
    public void UpdateMetricFromGameplay(string actionType, int value)
    {
        if (runtimeHistoryLog == null)
        {
            runtimeHistoryLog = new PlayerHistoryLog();
        }

        if (value <= 0 || string.IsNullOrWhiteSpace(actionType))
        {
            return;
        }

        switch (actionType.Trim())
        {
            case GameplayActionMetricTypes.Slash:
                runtimeHistoryLog.TotalSlashHits += value;
                break;
            case GameplayActionMetricTypes.Blunt:
                runtimeHistoryLog.TotalStrikeHits += value;
                break;
            case GameplayActionMetricTypes.Pierce:
                runtimeHistoryLog.TotalThrustHits += value;
                break;
            case GameplayActionMetricTypes.JustEvasion:
                runtimeHistoryLog.TotalPerfectEvades += value;
                break;
            default:
                Debug.LogWarning($"[GamePhaseEventBridge] 未対応の actionType: {actionType}");
                return;
        }

        runtimeHistoryLog.PreferredStatAllocation = preferredStatAllocationLabel;
    }

    /// <summary>
    /// 技 ID から近接3タイプを推定し、メトリクスを加算します（WeaponHitDetector 連携用）。
    /// </summary>
    public void UpdateMetricFromActionId(string actionId, int value = 1)
    {
        UpdateMetricFromGameplay(ResolveMetricTypeFromActionId(actionId), value);
    }

    /// <summary>
    /// PlayerActionLogger の現在値をランタイム履歴へ取り込みます（ニアミス等の補完同期）。
    /// </summary>
    public void SyncFromPlayerActionLogger()
    {
        PlayerActionLogger logger = PlayerActionLogger.Instance;
        if (logger == null)
        {
            return;
        }

        runtimeHistoryLog.TotalPerfectEvades = Mathf.Max(
            runtimeHistoryLog.TotalPerfectEvades,
            logger.NearMissCount);

        runtimeHistoryLog.PreferredStatAllocation = preferredStatAllocationLabel;
    }

    /// <summary>
    /// HP・生産中・安全地帯フラグから currentPhase を自動判定します。
    /// </summary>
    public void EvaluateCurrentPhase(
        PlayerStatusManager statusManager,
        bool isCrafting,
        bool isSafetyZone)
    {
        if (statusManager != null)
        {
            this.statusManager = statusManager;
        }

        float hpRatio = ResolveHpRatio(this.statusManager);
        if (hpRatio <= crisisHpThreshold)
        {
            currentPhase = GamePhase.Crisis;
            return;
        }

        if (isCrafting)
        {
            currentPhase = GamePhase.Crafting;
            return;
        }

        if (isSafetyZone)
        {
            currentPhase = GamePhase.Fusion;
            return;
        }

        currentPhase = GamePhase.Training;
    }

    /// <summary>
    /// 現在フェーズに応じた AI パイプラインを非同期起動します（ファイア・アンド・フォーゲット）。
    /// </summary>
    public void TriggerAIPipeline(Action<string> log = null)
    {
        _ = TriggerAIPipelineAsync(log);
    }

    /// <summary>
    /// ランタイム戦歴を基に AI パイプラインを点火します（Training の BaseSkill 目覚めを含む）。
    /// </summary>
    public void TriggerRuntimeAIPipeline(Action<string> log = null)
    {
        TriggerAIPipeline(log);
    }

    /// <summary>
    /// ランタイム戦歴を基に AI パイプラインを非同期実行します。
    /// </summary>
    public Task<GamePhasePipelineResult> TriggerRuntimeAIPipelineAsync(
        Action<string> log = null,
        CancellationToken cancellationToken = default)
    {
        return TriggerAIPipelineAsync(log, cancellationToken);
    }

    /// <summary>フェーズを Training（技術修練）へ固定します。</summary>
    public void ForceTrainingPhase()
    {
        currentPhase = GamePhase.Training;
    }

    /// <summary>
    /// BaseSkill 覚醒テスト用に戦歴ログへデバッグ値を注入します。
    /// </summary>
    public void InjectDebugTrainingHistory(
        int thrustHits = 10,
        int slashHits = 0,
        int strikeHits = 0,
        int perfectEvades = 0,
        string statAllocationLabel = "敏捷寄り")
    {
        runtimeHistoryLog ??= new PlayerHistoryLog();
        runtimeHistoryLog.TotalThrustHits += Mathf.Max(0, thrustHits);
        runtimeHistoryLog.TotalSlashHits += Mathf.Max(0, slashHits);
        runtimeHistoryLog.TotalStrikeHits += Mathf.Max(0, strikeHits);
        runtimeHistoryLog.TotalPerfectEvades += Mathf.Max(0, perfectEvades);
        runtimeHistoryLog.PreferredStatAllocation = statAllocationLabel;
        preferredStatAllocationLabel = statAllocationLabel;
    }

    /// <summary>フェーズを Crafting（生産・解体）へ固定します。</summary>
    public void ForceCraftingPhase()
    {
        currentPhase = GamePhase.Crafting;
    }

    /// <summary>
    /// 職人実験ハブの決定論的評価結果を AI パイプライン用コンテキストへ登録します。
    /// </summary>
    public void SubmitCraftingExperiment(CraftExperimentReport report, float craftQuality)
    {
        pendingCraftExperiment = report;
        pendingCraftQuality = Mathf.Clamp01(craftQuality);
        currentPhase = GamePhase.Crafting;
    }

    /// <summary>
    /// 登録済みの生産実験レポートを GamePhase.Crafting として Gemini へ引き渡します。
    /// </summary>
    public void TriggerCraftingPipelineFromExperiment(Action<string> log = null)
    {
        ForceCraftingPhase();
        TriggerRuntimeAIPipeline(log);
    }

    /// <summary>
    /// 現在フェーズ・蓄積履歴をプロンプト化し、Gemini 実通信 → 永続化 → 閃き付与まで実行します。
    /// </summary>
    public async Task<GamePhasePipelineResult> TriggerAIPipelineAsync(
        Action<string> log = null,
        CancellationToken cancellationToken = default)
    {
        log = WrapMainThreadLog(log);

        if (pipelineRunning)
        {
            const string busy = "AI パイプラインは既に実行中です。";
            log?.Invoke(busy);
            return new GamePhasePipelineResult(false, currentPhase, null, null, busy);
        }

        pipelineRunning = true;

        try
        {
            ResolveReferences();
            SyncFromPlayerActionLogger();

            if (!RealAIHttpClient.IsApiKeyConfigured)
            {
                string apiMessage =
                    "Gemini API キー未設定。LocalSecrets/gemini-api-key.txt を作成してください。";
                log?.Invoke(apiMessage);
                return new GamePhasePipelineResult(false, currentPhase, null, null, apiMessage);
            }

            communicationService ??= new RealAICommunicationService();

            if (currentPhase == GamePhase.Fusion && fusionSacrificeSkills.Count > 0)
            {
                return await RunFusionPipelineAsync(log, cancellationToken).ConfigureAwait(false);
            }

            string prompt = BuildPromptForCurrentPhase();
            log?.Invoke(FormatPhaseLog(
                currentPhase,
                $"プロンプト組み立て完了（{prompt.Length} 文字）\n---\n{prompt}\n---"));

            RealAIGenerationOutcome outcome = await communicationService
                .GenerateAsync(prompt, cancellationToken)
                .ConfigureAwait(false);

            return await UnityMainThreadAwaiter.RunOnMainThreadAsync(
                () => FinalizePipeline(currentPhase, prompt, outcome.ParseResult, log),
                cancellationToken);
        }
        catch (RealAIHttpException exception)
        {
            string message = $"HTTP/Gemini エラー (status={exception.StatusCode}): {exception.Message}";
            log?.Invoke(FormatPhaseLog(currentPhase, message, isError: true));
            return new GamePhasePipelineResult(false, currentPhase, null, null, message);
        }
        catch (Exception exception)
        {
            string message = $"AI パイプライン例外: {exception.Message}";
            log?.Invoke(FormatPhaseLog(currentPhase, message, isError: true));
            return new GamePhasePipelineResult(false, currentPhase, null, null, message);
        }
        finally
        {
            pipelineRunning = false;
        }
    }

    private async Task<GamePhasePipelineResult> RunFusionPipelineAsync(
        Action<string> log,
        CancellationToken cancellationToken)
    {
        fusionInventory ??= new PlayerSkillInventory();
        fusionInventory.AddOwnedSkills(BuildSacrificeSkillList());

        string prompt = BuildPromptForCurrentPhase();
        log?.Invoke(FormatPhaseLog(
            GamePhase.Fusion,
            $"融合プロンプト組み立て完了\n---\n{prompt}\n---"));

        SkillFusionTransactionSystem fusionSystem = new SkillFusionTransactionSystem(
            fusionInventory,
            new AIGeneratorConnector(new RealAIHttpClient()));

        IReadOnlyList<SacrificeSkillData> sacrifices = BuildSacrificeSkillList();
        SkillFusionResult fusionResult = await fusionSystem.TrySkillFusionWithPromptAsync(
            prompt,
            sacrifices,
            log,
            cancellationToken,
            RealAIGameResponseParser.ParseGenerationResponse).ConfigureAwait(false);

        return await UnityMainThreadAwaiter.RunOnMainThreadAsync(
            () => FinalizeFusionPipeline(fusionResult, prompt, log),
            cancellationToken);
    }

    private GamePhasePipelineResult FinalizeFusionPipeline(
        SkillFusionResult fusionResult,
        string prompt,
        Action<string> log)
    {
        if (fusionResult.Outcome == SkillFusionOutcome.Success && fusionResult.GrantedSkill != null)
        {
            bool applied = ApplySkillThroughInspiration(fusionResult.GrantedSkill, GamePhase.Fusion, log);
            string message = applied
                ? $"融合成功・閃き反映: {fusionResult.GrantedSkill.SkillName}"
                : $"融合成功（閃き反映はスキップ）: {fusionResult.GrantedSkill.SkillName}";

            RuntimeInGameUIManager.EnsureInstance().ShowInspirationPopupForPhase(
                GamePhase.Fusion,
                fusionResult.GrantedSkill.SkillName,
                fusionResult.GrantedSkill.FlavorText);

            log?.Invoke(FormatPhaseLog(GamePhase.Fusion, message));
            return new GamePhasePipelineResult(true, GamePhase.Fusion, prompt, null, message);
        }

        if (fusionResult.Outcome == SkillFusionOutcome.Refunded)
        {
            string refundMessage = $"融合失敗・対価返還: {fusionResult.Message}";
            log?.Invoke(FormatPhaseLog(GamePhase.Fusion, refundMessage, isError: true));
            return new GamePhasePipelineResult(false, GamePhase.Fusion, prompt, null, refundMessage);
        }

        string failMessage = fusionResult.Message ?? "融合リクエストが無効です。";
        log?.Invoke(FormatPhaseLog(GamePhase.Fusion, failMessage, isError: true));
        return new GamePhasePipelineResult(false, GamePhase.Fusion, prompt, null, failMessage);
    }

    private GamePhasePipelineResult FinalizePipeline(
        GamePhase phase,
        string prompt,
        AIGenerationResult parseResult,
        Action<string> log)
    {
        if (parseResult == null)
        {
            return new GamePhasePipelineResult(false, phase, prompt, null, "パース結果が null です。");
        }

        log?.Invoke(FormatPhaseLog(
            phase,
            $"パース状態: {parseResult.ParseStatus}\n診断: {parseResult.DiagnosticMessage}\n抽出JSON:\n{parseResult.ExtractedJson}"));

        if (parseResult.ParseStatus != AIParseStatus.Success)
        {
            string fail = phase == GamePhase.Training
                ? "パース失敗、リトライ可能（基礎スキル付与をスキップしました）。"
                : "パース失敗のためスキル付与・永続化をスキップしました（フォールバック非保存）。";
            log?.Invoke(FormatPhaseLog(phase, fail, isError: true));
            return new GamePhasePipelineResult(false, phase, prompt, parseResult, fail);
        }

        if (phase == GamePhase.Training)
        {
            return FinalizeTrainingBaseSkill(phase, prompt, parseResult, log);
        }

        if (parseResult.Kind == AIGenerationKind.Craft && parseResult.Craft != null)
        {
            GameContentRegistry.RegisterCraft(parseResult.Craft, log);
            string craftMessage = $"クラフト成果を登録: {parseResult.Craft.ItemName}";
            log?.Invoke(FormatPhaseLog(phase, craftMessage));
            return new GamePhasePipelineResult(true, phase, prompt, parseResult, craftMessage);
        }

        if (parseResult.Skill != null && !parseResult.Skill.IsFallback)
        {
            GameContentRegistry.RegisterSkill(parseResult.Skill, log);
            bool applied = ApplySkillThroughInspiration(parseResult.Skill, phase, log);
            string skillMessage = applied
                ? $"AI生成スキルを閃き付与: {parseResult.Skill.SkillName}"
                : $"AI生成スキルを取得（閃き枠は満杯等でスキップ）: {parseResult.Skill.SkillName}";

            RuntimeInGameUIManager.EnsureInstance().ShowInspirationPopupForPhase(
                phase,
                parseResult.Skill.SkillName,
                parseResult.Skill.FlavorText);

            log?.Invoke(FormatPhaseLog(phase, skillMessage));
            return new GamePhasePipelineResult(true, phase, prompt, parseResult, skillMessage);
        }

        string incomplete = "成功応答でしたが、登録可能なスキル/クラフトがありませんでした。";
        log?.Invoke(FormatPhaseLog(phase, incomplete, isError: true));
        return new GamePhasePipelineResult(false, phase, prompt, parseResult, incomplete);
    }

    private string BuildPromptForCurrentPhase()
    {
        switch (currentPhase)
        {
            case GamePhase.Crisis:
                return PromptBuilder.Build(
                    GamePhase.Crisis,
                    runtimeHistoryLog,
                    crisisContext: BuildCrisisContext(),
                    additionalIntent: "死線の覚醒として、転スラ風【漢字3〜4文字（読みカタカナ）】のユニークスキルを1つ生成せよ。");

            case GamePhase.Fusion:
                return PromptBuilder.Build(
                    GamePhase.Fusion,
                    runtimeHistoryLog,
                    ConvertOwnedSkillsForFusionPrompt(),
                    additionalIntent: "メリットとデメリットのペアを必ず数値付きで含めよ。");

            case GamePhase.Crafting:
                return PromptBuilder.Build(
                    GamePhase.Crafting,
                    runtimeHistoryLog,
                    craftQuality: pendingCraftExperiment != null ? pendingCraftQuality : defaultCraftQuality,
                    craftingInput: BuildCraftingInput(),
                    additionalIntent: pendingCraftExperiment != null
                        ? "職人の自由実験レポートに基づき、偏りと評価点を反映した成果物を生成せよ。"
                        : requestNewRecipeOnCrafting
                            ? "不足している新レシピデータを生成せよ。"
                            : "素材と品質に基づきアイテムを生成せよ。");

            case GamePhase.Training:
            default:
                return PromptBuilder.Build(
                    GamePhase.Training,
                    runtimeHistoryLog,
                    awakeningContext: BuildAwakeningContext(),
                    additionalIntent:
                        "この戦歴の持ち主に目覚めるべきオリジナルの基礎技法（BaseSkill）を1つ生成せよ。" +
                        "generationType は baseSkill、baseSkill オブジェクトに skillID / skillName / description / " +
                        "targetBonus（damageBonus や speedBonus 等）/ bonusValue を必ず含めること。");
        }
    }

    /// <summary>
    /// Training フェーズ専用: BaseSkill をパースし、PlayerSkillSlotManager へ実反映します。
    /// </summary>
    private GamePhasePipelineResult FinalizeTrainingBaseSkill(
        GamePhase phase,
        string prompt,
        AIGenerationResult parseResult,
        Action<string> log)
    {
        AIGeneratedBaseSkillData baseSkill = parseResult.BaseSkill
            ?? BaseSkillResponseParser.TryParseFromExtractedJson(parseResult.ExtractedJson)
            ?? BaseSkillResponseParser.FromLegacyGeneratedSkill(parseResult.Skill);

        if (baseSkill == null || !baseSkill.IsValid())
        {
            string fail = "BaseSkill のパースに失敗しました。パース失敗、リトライ可能。";
            log?.Invoke(BaseSkillApplier.FormatFailureLog(fail));
            return new GamePhasePipelineResult(false, phase, prompt, parseResult, fail);
        }

        if (skillSlotManager == null)
        {
            string fail = "PlayerSkillSlotManager が見つかりません。パース失敗、リトライ可能。";
            log?.Invoke(BaseSkillApplier.FormatFailureLog(fail));
            return new GamePhasePipelineResult(false, phase, prompt, parseResult, fail);
        }

        CombatStats combatStats = statusManager != null
            ? statusManager.GetComponent<CombatStats>()
            : null;
        if (playerController == null)
        {
            playerController = FindAnyObjectByType<PlayerController>();
        }

        BaseSkillEquipResult equipResult = BaseSkillApplier.TryApplyToPlayer(
            skillSlotManager,
            baseSkill,
            runtimeHistoryLog,
            combatStats,
            playerController);

        if (!equipResult.Succeeded)
        {
            log?.Invoke(BaseSkillApplier.FormatFailureLog(equipResult.Message));
            return new GamePhasePipelineResult(false, phase, prompt, parseResult, equipResult.Message);
        }

        log?.Invoke(BaseSkillApplier.FormatSuccessLog(baseSkill, equipResult.SlotIndex));

        RuntimeInGameUIManager.EnsureInstance().ShowInspirationPopupForPhase(
            phase,
            baseSkill.SkillName,
            $"{baseSkill.Description}\n（{baseSkill.FormatBonusLabel()}）");

        string message =
            $"基礎スキル実装着: {baseSkill.SkillName} → スロット[{equipResult.SlotIndex}]";
        log?.Invoke(FormatPhaseLog(phase, message));
        return new GamePhasePipelineResult(true, phase, prompt, parseResult, message);
    }

    private bool ApplySkillThroughInspiration(
        GeneratedSkillData skill,
        GamePhase phase,
        Action<string> log)
    {
        if (inspirationManager == null)
        {
            log?.Invoke("[GamePhaseEventBridge] InspirationManager が見つかりません。");
            return false;
        }

        return inspirationManager.ApplyAIGeneratedSkill(skill, phase);
    }

    private CurrentBattleContext BuildCrisisContext()
    {
        return new CurrentBattleContext
        {
            EnemyRank = EnemyRank.Boss,
            IsCrisisAwakeningTriggered = true
        };
    }

    private BaseSkillAwakeningContext BuildAwakeningContext()
    {
        return new BaseSkillAwakeningContext
        {
            TotalProductionActions = 0,
            UnlearnedBaseSkillCategories = new List<string> { "片手剣基礎", "回避基礎", "火魔導基礎" }
        };
    }

    private GamePhaseCraftingInput BuildCraftingInput()
    {
        List<string> materials = new List<string>(defaultCraftMaterials);
        if (pendingCraftExperiment?.Materials != null && pendingCraftExperiment.Materials.Count > 0)
        {
            materials = new List<string>(pendingCraftExperiment.Materials);
        }

        GamePhaseCraftingInput input = new GamePhaseCraftingInput
        {
            Materials = materials,
            RequestNewRecipe = requestNewRecipeOnCrafting,
            RecipeWishDescription = pendingCraftExperiment != null
                ? $"{pendingCraftExperiment.ArchetypeLabel} — {pendingCraftExperiment.ResultDescription}"
                : "鉄と骨から短剣を鍛造したい",
            ExperimentReport = pendingCraftExperiment
        };

        return input;
    }

    private List<SkillData> ConvertOwnedSkillsForFusionPrompt()
    {
        List<SkillData> list = new List<SkillData>();

        if (skillSlotManager == null)
        {
            IReadOnlyList<SacrificeSkillData> sacrifices = BuildSacrificeSkillList();
            for (int i = 0; i < sacrifices.Count; i++)
            {
                SacrificeSkillData sacrifice = sacrifices[i];
                if (sacrifice == null)
                {
                    continue;
                }

                list.Add(new SkillData
                {
                    skillID = sacrifice.SkillId,
                    skillName = sacrifice.SkillName
                });
            }

            return list;
        }

        IReadOnlyList<SkillData> owned = skillSlotManager.OwnedSkills;
        for (int i = 0; i < owned.Count; i++)
        {
            if (owned[i] != null)
            {
                list.Add(owned[i]);
            }
        }

        return list;
    }

    private List<SacrificeSkillData> BuildSacrificeSkillList()
    {
        List<SacrificeSkillData> list = new List<SacrificeSkillData>();
        for (int i = 0; i < fusionSacrificeSkills.Count; i++)
        {
            FusionSacrificeSkillEntry entry = fusionSacrificeSkills[i];
            if (entry == null || string.IsNullOrWhiteSpace(entry.skillId))
            {
                continue;
            }

            list.Add(entry.ToSacrificeSkillData());
        }

        return list;
    }

    private void ResolveReferences()
    {
        if (statusManager == null)
        {
            statusManager = PlayerStatusManager.Instance ?? FindAnyObjectByType<PlayerStatusManager>();
        }

        if (inspirationManager == null)
        {
            inspirationManager = InspirationManager.Instance ?? FindAnyObjectByType<InspirationManager>();
        }

        if (skillSlotManager == null)
        {
            skillSlotManager = FindAnyObjectByType<PlayerSkillSlotManager>();
        }

        if (playerController == null)
        {
            playerController = FindAnyObjectByType<PlayerController>();
        }
    }

    private void InitializeRuntimeHistory()
    {
        runtimeHistoryLog ??= new PlayerHistoryLog();
        runtimeHistoryLog.PreferredStatAllocation = preferredStatAllocationLabel;
    }

    private static float ResolveHpRatio(PlayerStatusManager manager)
    {
        if (manager == null)
        {
            return 1f;
        }

        CombatStats combatStats = manager.GetComponent<CombatStats>();
        if (combatStats == null || combatStats.MaxHp <= 0)
        {
            return 1f;
        }

        return (float)combatStats.CurrentHp / combatStats.MaxHp;
    }

    private static string ResolveMetricTypeFromActionId(string actionId)
    {
        if (string.IsNullOrWhiteSpace(actionId))
        {
            return GameplayActionMetricTypes.Slash;
        }

        if (actionId == ActionIds.StrongStrike)
        {
            return GameplayActionMetricTypes.Blunt;
        }

        if (actionId == ActionIds.FireSpark || actionId == ActionIds.FlareSlash)
        {
            return GameplayActionMetricTypes.Pierce;
        }

        return GameplayActionMetricTypes.Slash;
    }

    /// <summary>Debug.Log 等の Unity API を含むコールバックをメインスレッド経由にします。</summary>
    private static Action<string> WrapMainThreadLog(Action<string> log)
    {
        if (log == null)
        {
            return null;
        }

        return message => UnityMainThreadAwaiter.RunOnMainThread(() => log(message));
    }

    /// <summary>フェーズに応じた Rich Text ログ色でメッセージを整形します。</summary>
    public static string FormatPhaseLog(GamePhase phase, string message, bool isError = false)
    {
        if (isError)
        {
            return $"<color=#FF6B6B><b>[GamePhaseBridge][{phase}]</b> {message}</color>";
        }

        if (phase == GamePhase.Crisis)
        {
            return $"<color=#DA70D6><b>[GamePhaseBridge][Crisis]</b> {message}</color>";
        }

        if (phase == GamePhase.Fusion)
        {
            return $"<color=#C77DFF><b>[GamePhaseBridge][Fusion]</b> {message}</color>";
        }

        return $"<color=#50C878><b>[GamePhaseBridge][{phase}]</b> {message}</color>";
    }
}

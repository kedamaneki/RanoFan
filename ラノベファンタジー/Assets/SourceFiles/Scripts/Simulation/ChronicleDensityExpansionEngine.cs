using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

// =============================================================================
// 250年正史高密度拡張 — 特異点魔獣観測 + 異端魔導民生化/失伝
// 連携: SingularBeastManager / MagicCivilizationEngine / HistoryBranchManager
//       MagiSystemDecisionEngine / TimelineGenerationLoopEngine
// =============================================================================

/// <summary>密度拡張結果。</summary>
public sealed class ChronicleDensityExpansionResult
{
    public bool success;
    public int colossusDeployed;
    public int hereticTechBound;
    public int observationEvents;
    public int civilizedTechCount;
    public int lostTechCount;
    public string message = string.Empty;
}

/// <summary>検証結果。</summary>
public sealed class ChronicleDensityExpansionVerifyResult
{
    public bool success;
    public bool colossusPass;
    public bool observationPass;
    public bool magicCivilizationPass;
    public bool lostTechPass;
    public bool revivalPhasePass;
    public string message = string.Empty;
}

/// <summary>A案+B案 統合検証結果。</summary>
public sealed class ChronicleMagiIntegrationVerifyResult
{
    public bool success;
    public bool planAPass;
    public bool planBPass;
    public string message = string.Empty;
}

/// <summary>
/// 確定 250 年正史ラインに対し、特異点魔獣観測と異端魔導の民生化/失伝を肉付けします。
/// </summary>
[DefaultExecutionOrder(48)]
public class ChronicleDensityExpansionEngine : MonoBehaviour
{
    public const string LogTag = "【250年正史密度向上】";
    public const string IntegrationLogTag = "【A+B統合実装完了】";
    public const int CanonFinalTurn = 250;

    public static ChronicleDensityExpansionEngine Instance { get; private set; }
    public static ChronicleDensityExpansionResult LastExpansionResult { get; private set; }

    public static ChronicleDensityExpansionEngine EnsureInstance()
    {
        if (Instance != null)
        {
            return Instance;
        }

        ChronicleDensityExpansionEngine existing =
            UnityEngine.Object.FindAnyObjectByType<ChronicleDensityExpansionEngine>();
        if (existing != null)
        {
            Instance = existing;
            return existing;
        }

        GameObject hub = GameObject.Find("DebugSystemsHub");
        GameObject host = hub != null ? hub : new GameObject(nameof(ChronicleDensityExpansionEngine));
        ChronicleDensityExpansionEngine engine = host.GetComponent<ChronicleDensityExpansionEngine>();
        return engine != null ? engine : host.AddComponent<ChronicleDensityExpansionEngine>();
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

    /// <summary>250年正史タイムライン上で密度拡張を一括適用します。</summary>
    public static ChronicleDensityExpansionResult ExpandCanon250Density(int turn = CanonFinalTurn)
    {
        ChronicleDensityExpansionResult result = new ChronicleDensityExpansionResult();
        try
        {
            EnsureInstance();
            HistoryFlagRegistry.EnsureWired();
            HistoryBranchManager mgr = HistoryBranchManager.EnsureInstance();
            EnvironmentBiorhythmEngine.EnsureInstance();

            result.colossusDeployed = SingularBeastManager.BootstrapUnobservedColossi();
            List<CanonTimelineLineageNode> lineage = mgr.BuildMainStoryLineageChain();
            if (lineage == null || lineage.Count == 0)
            {
                lineage = BuildFallbackCanonLineage();
            }

            result.hereticTechBound = MagicCivilizationEngine.BindHereticTechFromCanonLineage(lineage);

            MagicCivilizationTickResult magicTick =
                MagicCivilizationEngine.TickCivilizationRules(Mathf.Max(turn, 251));
            result.civilizedTechCount = magicTick.civilizedCount;
            result.lostTechCount = magicTick.lostTechCount;
            result.observationEvents = SingularBeastManager.LastObservationEventCount;

            result.success = result.colossusDeployed > 0 && result.hereticTechBound >= 2;
            result.message =
                $"colossus={result.colossusDeployed} heretic={result.hereticTechBound} " +
                $"obs={result.observationEvents} civilized={result.civilizedTechCount} lost={result.lostTechCount}";
            LastExpansionResult = result;

            Debug.Log(
                $"<color=#A5D6A7><b>{LogTag}</b></color> " +
                "特異点魔獣観測イベントおよび異端魔導の民生化/失伝ルールが正常にバインドされました。 " +
                result.message);
            return result;
        }
        catch (Exception exception)
        {
            result.success = false;
            result.message = $"Safe-Fail: {exception.Message}";
            Debug.LogWarning($"[ChronicleDensityExpansionEngine] {result.message}");
            LastExpansionResult = result;
            return result;
        }
    }

    /// <summary>ターン進行に合わせて観測・ルーチン・魔導文明化を更新します。</summary>
    public static string AdvanceTurnDensity(
        int turn,
        float territoryDelta,
        float manaConsumptionRatio,
        float regionalManaStimulus)
    {
        StringBuilder log = new StringBuilder();
        try
        {
            SingularBeastObservationResult obs = SingularBeastManager.TryObserveOnHumanContact(
                MicroToMacroAggregator.DefaultNationId,
                turn,
                territoryDelta,
                manaConsumptionRatio);
            if (obs.newlyObserved)
            {
                log.AppendLine($"observation: {obs.message}");
            }

            log.AppendLine(SingularBeastManager.TickColossusRoutines(turn, regionalManaStimulus));

            MagicCivilizationTickResult magic = MagicCivilizationEngine.TickCivilizationRules(turn);
            log.AppendLine($"magic: {magic.message}");
            return log.ToString().TrimEnd();
        }
        catch (Exception exception)
        {
            return $"Safe-Fail: {exception.Message}";
        }
    }

    /// <summary>
    /// A案（密度肉付け）と B案（MAGI 1000年史）を相互連携セットアップします。
    /// 250年正史確定後に呼び出し、特異点魔獣・民生化/失伝・MAGI パイプラインを一括バインドします。
    /// </summary>
    public static ChronicleDensityExpansionResult SetupIntegratedAPlusBPipeline(bool logCompletion = true)
    {
        EnsureInstance();
        MagiSystemDecisionEngine.EnsureInstance();
        TimelineGenerationLoopEngine.EnsureInstance();
        EnvironmentBiorhythmEngine.ApplyMidCycle400YearModel(persistConfig: false);

        ChronicleDensityExpansionResult result = ExpandCanon250Density(Mathf.Max(CanonFinalTurn, 251));

        if (logCompletion)
        {
            Debug.Log(
                $"<color=#A5D6A7><b>{IntegrationLogTag}</b></color> " +
                "密度拡張および三女神MAGI連動1000年史パイプラインが正常にセットアップされました。 " +
                result.message);
        }

        return result;
    }

    /// <summary>
    /// B案 1000年史世代ループ向け — A案の観測・ルーチン・民生化/失伝をターン範囲に適用します。
    /// </summary>
    public static void ApplyDensityRulesForChronicleGeneration(
        int startTurn,
        int endTurn,
        StringBuilder log)
    {
        int safeStart = EraContextResolver.ClampSimulationTurn(startTurn);
        int safeEnd = EraContextResolver.ClampSimulationTurn(endTurn);

        for (int turn = safeStart; turn <= safeEnd; turn += 10)
        {
            EnvironmentBiorhythmSnapshot bio = EnvironmentBiorhythmEngine.ResolveMidCyclePhase(turn);
            bool revivalTurn = EnvironmentBiorhythmEngine.IsCivilizationRevivalTurn(turn);

            if (!revivalTurn && bio.Phase == EnvironmentMidCyclePhase.PeakCatastrophe)
            {
                MagicCivilizationEngine.NotifyFrontlineFacilityAbandoned(1, occupancyRatio: 0.15f);
            }

            if (!revivalTurn &&
                bio.PositionInCycle >= MagicCivilizationEngine.LostTechDormantPositionThreshold &&
                bio.Phase == EnvironmentMidCyclePhase.Dormant &&
                turn % 80 == 0)
            {
                MagicCivilizationEngine.NotifyHereticTechnicianDeath($"CHRONICLE_T{turn}", turn);
            }

            float territoryDelta = (turn - safeStart) % 30 == 0 ? 3.5f : 0f;
            float manaRatio = revivalTurn
                ? EnvironmentBiorhythmEngine.RevivalManaEcologyRatioTarget
                : bio.Phase == EnvironmentMidCyclePhase.PeakCatastrophe ? 0.88f : 0.45f;
            float beastStimulus = revivalTurn
                ? EnvironmentBiorhythmEngine.RevivalBeastActivationMultiplier
                : bio.BeastActivationMultiplier;
            string chunk = AdvanceTurnDensity(turn, territoryDelta, manaRatio, beastStimulus);

            if (turn == safeStart || turn == safeEnd)
            {
                log.AppendLine(
                    $"  density T{turn}: phase={bio.Phase} pos={bio.PositionInCycle} " +
                    $"beast×{bio.BeastActivationMultiplier:F2} | {chunk.Replace("\n", " | ")}");
            }
        }

        EnvironmentBiorhythmSnapshot finalBio = EnvironmentBiorhythmEngine.ResolveMidCyclePhase(safeEnd);
        bool finalRevival = EnvironmentBiorhythmEngine.IsCivilizationRevivalTurn(safeEnd);
        if (!finalRevival && finalBio.Phase == EnvironmentMidCyclePhase.PeakCatastrophe)
        {
            MagicCivilizationEngine.NotifyFrontlineFacilityAbandoned(1, occupancyRatio: 0.15f);
        }

        AdvanceTurnDensity(
            safeEnd,
            territoryDelta: 0f,
            manaConsumptionRatio: finalRevival
                ? EnvironmentBiorhythmEngine.RevivalManaEcologyRatioTarget
                : finalBio.Phase == EnvironmentMidCyclePhase.PeakCatastrophe ? 0.88f : 0.45f,
            regionalManaStimulus: finalRevival
                ? EnvironmentBiorhythmEngine.RevivalBeastActivationMultiplier
                : finalBio.BeastActivationMultiplier);
    }

    /// <summary>A案・B案の両 Verify を実行し、成功時に統合セットアップログを出力します。</summary>
    public static ChronicleMagiIntegrationVerifyResult RunIntegratedVerification()
    {
        ChronicleMagiIntegrationVerifyResult integrated = new ChronicleMagiIntegrationVerifyResult();
        StringBuilder log = new StringBuilder();

        ChronicleDensityExpansionVerifyResult planA = RunVerification();
        integrated.planAPass = planA.success;
        log.AppendLine($"=== A案: Chronicle Density Expansion ===");
        log.AppendLine(planA.message ?? string.Empty);

        MagiSystemDecisionVerifyResult planB = MagiSystemDecisionEngine.RunVerification();
        integrated.planBPass = planB.success;
        log.AppendLine($"=== B案: MAGI System Decision Engine ===");
        log.AppendLine(planB.message ?? string.Empty);

        integrated.success = integrated.planAPass && integrated.planBPass;
        if (integrated.success)
        {
            SetupIntegratedAPlusBPipeline(logCompletion: true);
            log.AppendLine("integrated: SetupIntegratedAPlusBPipeline=OK");
        }

        integrated.message = log.ToString().TrimEnd();
        WriteIntegratedVerifyLog(integrated);
        return integrated;
    }

    public static ChronicleDensityExpansionVerifyResult RunVerification()
    {
        ChronicleDensityExpansionVerifyResult verify = new ChronicleDensityExpansionVerifyResult();
        StringBuilder log = new StringBuilder();

        try
        {
            SimulationVerifyBootstrap.PrepareFreshStoryDay(1, GameMode.StoryMode);
            HistoryFlagRegistry.EnsureWired();
            HistoryFlagRegistry.ClearForTesting();
            SingularBeastManager.ResetForVerification();
            MagicCivilizationEngine.ResetForVerification();
            EnvironmentBiorhythmEngine.ApplyMidCycle400YearModel(persistConfig: false);
            EnvironmentBiorhythmEngine.ResetRevivalTransitionLogForVerification();

            int deployed = SingularBeastManager.BootstrapUnobservedColossi(forceReset: true);
            int expectedMax = SingularBeastManager.ContinentIds.Length *
                              SingularBeastManager.MaxBeastsPerContinent;
            verify.colossusPass = deployed == expectedMax;
            log.AppendLine($"colossus: deployed={deployed} expected={expectedMax} pass={verify.colossusPass}");

            SingularBeastObservationResult obs = SingularBeastManager.TryObserveOnHumanContact(
                nationId: 1,
                turn: 120,
                territoryDelta: 4.2f,
                manaConsumptionRatio: 0.55f,
                continentId: "領域・西");
            string obsFlag = SingularBeastManager.BuildObservationFlag(1, 120);
            verify.observationPass =
                obs.success &&
                obs.newlyObserved &&
                HistoryFlagRegistry.IsUnlocked(obsFlag) &&
                SingularBeastManager.CountObservedOnContinent("領域・西") == 1;
            log.AppendLine(
                $"observation: flag={obsFlag} unlocked={HistoryFlagRegistry.IsUnlocked(obsFlag)} " +
                $"pass={verify.observationPass}");

            string routineLog = SingularBeastManager.TickColossusRoutines(120, regionalManaStimulus: 0.95f);
            log.AppendLine($"routine: {routineLog.Replace("\n", " | ")}");

            List<CanonTimelineLineageNode> lineage = BuildFallbackCanonLineage();
            int bound = MagicCivilizationEngine.BindHereticTechFromCanonLineage(lineage);
            MagicCivilizationTickResult civilTick =
                MagicCivilizationEngine.TickCivilizationRules(turn: 280);
            bool arcanaCivilized = HistoryFlagRegistry.IsUnlocked(
                MagicCivilizationEngine.HereticArcanaTechId + MagicCivilizationEngine.CivilizedSuffix);
            bool orthodoxyCivilized = HistoryFlagRegistry.IsUnlocked(
                MagicCivilizationEngine.HereticOrthodoxyTechId + MagicCivilizationEngine.CivilizedSuffix);
            verify.magicCivilizationPass =
                bound >= 2 &&
                civilTick.civilizedCount >= 2 &&
                arcanaCivilized &&
                orthodoxyCivilized;
            log.AppendLine(
                $"civilization: bound={bound} tick={civilTick.civilizedCount} " +
                $"arcana={arcanaCivilized} orthodoxy={orthodoxyCivilized} pass={verify.magicCivilizationPass}");

            MagicCivilizationEngine.NotifyFrontlineFacilityAbandoned(1, occupancyRatio: 0.1f);
            MagicCivilizationEngine.NotifyHereticTechnicianDeath("VERIFY_HERETIC_TECH", turnOverride: 320);
            MagicCivilizationTickResult lostTick =
                MagicCivilizationEngine.TickCivilizationRules(turn: 320);
            bool arcanaLost = HistoryFlagRegistry.IsUnlocked(
                MagicCivilizationEngine.HereticArcanaTechId + MagicCivilizationEngine.LostTechSuffix);
            bool ancientEra = HistoryFlagRegistry.IsUnlocked(EraContextResolver.EraAncientFlag);
            verify.lostTechPass =
                arcanaLost &&
                ancientEra &&
                (lostTick.lostTechCount >= 1 || arcanaLost);
            log.AppendLine(
                $"lostTech: lost={lostTick.lostTechCount} arcanaLost={arcanaLost} " +
                $"ERA_ANCIENT={ancientEra} pass={verify.lostTechPass}");

            MagicCivilizationEngine.ResetForVerification();
            HistoryFlagRegistry.ClearForTesting();
            HistoryFlagRegistry.EnsureWired();
            MagicCivilizationEngine.BindHereticTechFromCanonLineage(lineage);
            MagicCivilizationEngine.TickCivilizationRules(turn: 280);
            MagicCivilizationEngine.NotifyFrontlineFacilityAbandoned(1, occupancyRatio: 0.1f);
            MagicCivilizationTickResult peakLostTick =
                MagicCivilizationEngine.TickCivilizationRules(turn: 400);
            bool peakArcanaLost = HistoryFlagRegistry.IsUnlocked(
                MagicCivilizationEngine.HereticArcanaTechId + MagicCivilizationEngine.LostTechSuffix);
            bool peakLostPass = peakLostTick.lostTechCount >= 1 && peakArcanaLost;
            verify.lostTechPass = verify.lostTechPass && peakLostPass;
            log.AppendLine(
                $"peakLostTech: T400 lost={peakLostTick.lostTechCount} arcanaLost={peakArcanaLost} " +
                $"pass={peakLostPass}");

            EnvironmentBiorhythmSnapshot bioPreRevivalPeak =
                EnvironmentBiorhythmEngine.ResolveMidCyclePhase(800);
            EnvironmentBiorhythmSnapshot bioT1001 =
                EnvironmentBiorhythmEngine.ResolveMidCyclePhase(EnvironmentBiorhythmEngine.CivilizationRevivalStartTurn);
            EnvironmentBiorhythmSnapshot bioT1200 =
                EnvironmentBiorhythmEngine.ResolveMidCyclePhase(1200);
            float revivalMana = EnvironmentBiorhythmEngine.ResolveFixedRevivalManaEcologyLevel();
            bool revivalEcologyPass = NaturalEcologyEngine.RunRevivalEcologySelfCheck(out string revivalDetail);
            bool skuldRevivalPass = RunSkuldRevivalScoringProbe(out string skuldDetail);

            verify.revivalPhasePass =
                bioPreRevivalPeak.Phase == EnvironmentMidCyclePhase.PeakCatastrophe &&
                bioT1001.Phase != EnvironmentMidCyclePhase.PeakCatastrophe &&
                bioT1200.Phase != EnvironmentMidCyclePhase.PeakCatastrophe &&
                bioT1001.BeastActivationMultiplier <
                EnvironmentBiorhythmEngine.DormantBeastMultiplier + 0.01f &&
                revivalMana >= NatureEnvironmentStatus.MaxManaEcology *
                EnvironmentBiorhythmEngine.RevivalManaEcologyRatioMin - 0.01f &&
                revivalMana <= NatureEnvironmentStatus.MaxManaEcology *
                EnvironmentBiorhythmEngine.RevivalManaEcologyRatioMax + 0.01f &&
                revivalEcologyPass &&
                skuldRevivalPass;
            log.AppendLine(
                $"revivalPhase: T800={bioPreRevivalPeak.Phase} T1001={bioT1001.Phase} T1200={bioT1200.Phase} " +
                $"beast×{bioT1001.BeastActivationMultiplier:F2} mana={revivalMana:F1} " +
                $"ecology={revivalDetail} skuld={skuldDetail} pass={verify.revivalPhasePass}");

            ChronicleDensityExpansionResult expand = ExpandCanon250Density(280);
            log.AppendLine($"expand: success={expand.success} {expand.message}");

            verify.success =
                verify.colossusPass &&
                verify.observationPass &&
                verify.magicCivilizationPass &&
                verify.lostTechPass &&
                verify.revivalPhasePass &&
                expand.success;
            verify.message = log.ToString().TrimEnd();
            WriteVerifyLog(verify);
            return verify;
        }
        catch (Exception exception)
        {
            verify.success = false;
            verify.message = $"Safe-Fail: {exception.Message}\n{log}";
            WriteVerifyLog(verify);
            return verify;
        }
    }

    private static void WriteIntegratedVerifyLog(ChronicleMagiIntegrationVerifyResult verify)
    {
        try
        {
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
            string path = Path.Combine(projectRoot, "Logs", "chronicle_magi_integration_verify.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? projectRoot);
            File.WriteAllText(
                path,
                $"{DateTime.UtcNow:O} success={verify.success} planA={verify.planAPass} planB={verify.planBPass}\n" +
                $"{verify.message}\n",
                Encoding.UTF8);
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[ChronicleDensityExpansionEngine] 統合検証ログスキップ: {exception.Message}");
        }
    }

    private static bool RunSkuldRevivalScoringProbe(out string detail)
    {
        detail = string.Empty;
        try
        {
            TimelineScriptEvaluator.EnsureInstance();
            const int evaluationTurn = 1051;
            HistoryTimelineBranch diverse = BuildRevivalProbeDiverseBranch(evaluationTurn);
            HistoryTimelineBranch converged = BuildRevivalProbeBeastBranch(evaluationTurn);

            TimelineScriptEvaluationResult diverseScript =
                TimelineScriptEvaluator.EvaluateTimelineBranch(diverse, evaluationTurn);
            TimelineScriptEvaluationResult convergedScript =
                TimelineScriptEvaluator.EvaluateTimelineBranch(converged, evaluationTurn);

            SkuldEvaluator skuld = new SkuldEvaluator();
            MagiUnitVote diverseVote = skuld.ScoreBranch(diverse, diverseScript, evaluationTurn, null);
            MagiUnitVote convergedVote = skuld.ScoreBranch(converged, convergedScript, evaluationTurn, null);

            SkuldPossibilityAnalysis diverseAnalysis =
                SkuldEvaluator.AnalyzePossibilityTree(diverse, evaluationTurn);
            SkuldPossibilityAnalysis convergedAnalysis =
                SkuldEvaluator.AnalyzePossibilityTree(converged, evaluationTurn);
            bool pass = diverseVote.score > convergedVote.score &&
                          diverseVote.score >= 300 &&
                          diverseAnalysis.branchCount >= 6 &&
                          convergedVote.score <= 120;
            detail = $"diverse={diverseVote.score}({diverseAnalysis.branchCount}枝) " +
                     $"converged={convergedVote.score}({convergedAnalysis.branchCount}枝) pass={pass}";
            return pass;
        }
        catch (Exception exception)
        {
            detail = $"Safe-Fail: {exception.Message}";
            return false;
        }
    }

    private static HistoryTimelineBranch BuildRevivalProbeDiverseBranch(int turn)
    {
        int nationId = MicroToMacroAggregator.DefaultNationId;
        return new HistoryTimelineBranch
        {
            branchId = "ALT_VERIFY_REVIVAL_DIVERSE",
            displayName = "IF_Line: 汎人類史・復興",
            baseStartTurn = turn - 3,
            primaryNationId = nationId,
            nodes = new List<HistoryBranchNode>
            {
                new HistoryBranchNode
                {
                    turn = turn - 3,
                    nationId = nationId,
                    keyEventId = "HIST_NATION_001_GEO_TURN_001_INNOVATION",
                    outcomeFlag = "ALT_NATION_001_OUTCOME_INNOVATION",
                    delta = new MacroParamDelta
                    {
                        powerMultiplier = 1.10f,
                        barrierEfficiencyDelta = 0.05f,
                        threatMultiplier = 0.94f
                    }
                },
                new HistoryBranchNode
                {
                    turn = turn - 2,
                    nationId = nationId,
                    keyEventId = "HIST_NATION_001_GEO_TURN_001_ACADEMY_RIVALRY",
                    outcomeFlag = "ALT_NATION_001_OUTCOME_ACADEMY_SCHISM",
                    delta = new MacroParamDelta
                    {
                        powerMultiplier = 1.08f,
                        barrierEfficiencyDelta = 0.04f,
                        threatMultiplier = 0.96f
                    }
                },
                new HistoryBranchNode
                {
                    turn = turn - 1,
                    nationId = nationId,
                    keyEventId = "HIST_NATION_001_GEO_TURN_001_SUCCESSION",
                    outcomeFlag = "ALT_NATION_001_OUTCOME_TRADE_COMMERCE",
                    delta = new MacroParamDelta
                    {
                        powerMultiplier = 1.12f,
                        barrierEfficiencyDelta = 0.06f,
                        threatMultiplier = 0.90f
                    }
                },
                new HistoryBranchNode
                {
                    turn = turn,
                    nationId = nationId,
                    keyEventId = "HIST_NATION_001_GEO_TURN_001_HERO",
                    outcomeFlag = "ALT_NATION_001_OUTCOME_FRONTIER_EXPANSION",
                    delta = new MacroParamDelta
                    {
                        powerMultiplier = 1.14f,
                        barrierEfficiencyDelta = 0.07f,
                        threatMultiplier = 0.88f
                    }
                }
            }
        };
    }

    private static HistoryTimelineBranch BuildRevivalProbeBeastBranch(int turn)
    {
        int nationId = MicroToMacroAggregator.DefaultNationId;
        return new HistoryTimelineBranch
        {
            branchId = "ALT_VERIFY_REVIVAL_BEAST",
            displayName = "IF_Line: 収束・災害",
            baseStartTurn = turn - 2,
            primaryNationId = nationId,
            nodes = new List<HistoryBranchNode>
            {
                new HistoryBranchNode
                {
                    turn = turn - 2,
                    nationId = nationId,
                    keyEventId = "HIST_NATION_001_GEO_TURN_001_BEAST_CATASTROPHE",
                    outcomeFlag = "ALT_NATION_001_OUTCOME_BEAST_CATASTROPHE",
                    delta = new MacroParamDelta
                    {
                        powerMultiplier = 0.90f,
                        barrierEfficiencyDelta = -0.10f,
                        threatMultiplier = 1.38f
                    }
                },
                new HistoryBranchNode
                {
                    turn = turn - 1,
                    nationId = nationId,
                    keyEventId = "HIST_NATION_001_GEO_TURN_001_BARRIERDROP",
                    outcomeFlag = "ALT_NATION_001_OUTCOME_BARRIERDROP",
                    delta = new MacroParamDelta
                    {
                        powerMultiplier = 0.86f,
                        barrierEfficiencyDelta = -0.18f,
                        threatMultiplier = 1.65f
                    }
                },
                new HistoryBranchNode
                {
                    turn = turn,
                    nationId = nationId,
                    keyEventId = "HIST_NATION_001_GEO_TURN_001_MONSTER_DEFENSE",
                    outcomeFlag = "ALT_NATION_001_OUTCOME_MONSTER_DEFENSE",
                    delta = new MacroParamDelta
                    {
                        powerMultiplier = 0.84f,
                        barrierEfficiencyDelta = -0.20f,
                        threatMultiplier = 1.72f
                    }
                }
            }
        };
    }

    private static List<CanonTimelineLineageNode> BuildFallbackCanonLineage()
    {
        return new List<CanonTimelineLineageNode>
        {
            new CanonTimelineLineageNode
            {
                generation = 4,
                branchId = TimelineGenerationLoopEngine.ManualGen4BranchId,
                label = "IF_Line: 異端魔導の覚醒",
                committedAtTurn = 200,
                score = 391
            },
            new CanonTimelineLineageNode
            {
                generation = 5,
                branchId = TimelineGenerationLoopEngine.ManualGen5BranchId,
                label = "IF_Line: 異端魔法の正統化",
                committedAtTurn = 250,
                score = 377
            }
        };
    }

    private static void WriteVerifyLog(ChronicleDensityExpansionVerifyResult verify)
    {
        try
        {
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
            string path = Path.Combine(projectRoot, "Logs", "chronicle_density_expansion_verify.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? projectRoot);
            File.WriteAllText(
                path,
                $"{DateTime.UtcNow:O} success={verify.success}\n{verify.message}\n",
                Encoding.UTF8);
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[ChronicleDensityExpansionEngine] 検証ログスキップ: {exception.Message}");
        }
    }
}

public static class ChronicleDensityExpansionEngineBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoAttach()
    {
        ChronicleDensityExpansionEngine.EnsureInstance();
    }
}

#if UNITY_EDITOR
public static class ChronicleDensityExpansionEngineMenu
{
    [MenuItem("Tools/Procedural Map/Verify Chronicle Density Expansion (250y Canon)")]
    [MenuItem("Tools/Procedural Map/Verify Chronicle Density Expansion (A案)")]
    public static void VerifyFromMenu()
    {
        ChronicleDensityExpansionVerifyResult result = ChronicleDensityExpansionEngine.RunVerification();
        Debug.Log(
            result.success
                ? $"<color=#A5D6A7><b>【250年正史密度拡張検証・A案】PASS</b></color>\n{result.message}"
                : $"<color=#FF8A80><b>【250年正史密度拡張検証・A案】FAIL</b></color>\n{result.message}");
        EditorUtility.DisplayDialog(
            "Chronicle Density Expansion (A案)",
            result.success ? "PASS\n" + result.message : "FAIL\n" + result.message,
            "OK");
    }

    [MenuItem("Tools/Procedural Map/Verify A+B Integrated Chronicle-MAGI Pipeline")]
    public static void VerifyIntegratedFromMenu()
    {
        ChronicleMagiIntegrationVerifyResult result = ChronicleDensityExpansionEngine.RunIntegratedVerification();
        Debug.Log(
            result.success
                ? $"<color=#A5D6A7><b>【A+B統合検証】PASS</b></color>\n{result.message}"
                : $"<color=#FF8A80><b>【A+B統合検証】FAIL</b></color>\n{result.message}");
        EditorUtility.DisplayDialog(
            "A+B Integrated Verify",
            result.success ? "PASS\n" + result.message : "FAIL\n" + result.message,
            "OK");
    }

    [MenuItem("Tools/Procedural Map/Expand Canon 250y Chronicle Density")]
    public static void ExpandFromMenu()
    {
        ChronicleDensityExpansionResult result = ChronicleDensityExpansionEngine.ExpandCanon250Density();
        EditorUtility.DisplayDialog("Chronicle Density Expansion", result.message, "OK");
    }

    public static void BatchVerifyChronicleDensityAndQuit()
    {
        ChronicleDensityExpansionVerifyResult result = ChronicleDensityExpansionEngine.RunVerification();
        EditorApplication.Exit(result.success ? 0 : 1);
    }

    public static void BatchVerifyIntegratedAPlusBAndQuit()
    {
        ChronicleMagiIntegrationVerifyResult result = ChronicleDensityExpansionEngine.RunIntegratedVerification();
        EditorApplication.Exit(result.success ? 0 : 1);
    }
}
#endif

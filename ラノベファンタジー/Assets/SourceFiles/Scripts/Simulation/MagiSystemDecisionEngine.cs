using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

// =============================================================================
// MAGI 三位一体 — 歴史評価・自動採択エンジン
// 連携: VerdandiEvaluator / UrdEvaluator / SkuldEvaluator
//       HistoryBranchManager / TimelineGenerationLoopEngine / TimelineScriptEvaluator
//       ChronicleDensityExpansionEngine
// =============================================================================

/// <summary>
/// 3 女神 MAGI による合議で IF 分岐枝を審議し、全会一致時は自動 Commit します。
/// </summary>
[DefaultExecutionOrder(47)]
public class MagiSystemDecisionEngine : MonoBehaviour
{
    public const string LogTag = "【MAGIシステム審議完了】";
    public const string DeliberationLogTag = "【MAGI合議】";

    public static MagiSystemDecisionEngine Instance { get; private set; }
    public static MagiDeliberationResult LastDeliberationResult { get; private set; }

    private readonly VerdandiEvaluator verdandi = new VerdandiEvaluator();
    private readonly UrdEvaluator urd = new UrdEvaluator();
    private readonly SkuldEvaluator skuld = new SkuldEvaluator();

    public static MagiSystemDecisionEngine EnsureInstance()
    {
        if (Instance != null)
        {
            return Instance;
        }

        MagiSystemDecisionEngine existing = UnityEngine.Object.FindAnyObjectByType<MagiSystemDecisionEngine>();
        if (existing != null)
        {
            Instance = existing;
            return existing;
        }

        GameObject hub = GameObject.Find("DebugSystemsHub");
        GameObject host = hub != null ? hub : new GameObject(nameof(MagiSystemDecisionEngine));
        MagiSystemDecisionEngine engine = host.GetComponent<MagiSystemDecisionEngine>();
        return engine != null ? engine : host.AddComponent<MagiSystemDecisionEngine>();
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

    /// <summary>全生成枝を 3 ユニットで審議し、採択または手動待ちを返します。</summary>
    public static MagiDeliberationResult EvaluateAndProposeBranch(
        List<HistoryTimelineBranch> branches,
        int? evaluationTurn = null)
    {
        MagiDeliberationResult result = new MagiDeliberationResult();
        try
        {
            EnsureInstance();
            TimelineScriptEvaluator.EnsureInstance();
            HistoryBranchManager mgr = HistoryBranchManager.EnsureInstance();
            HistoryFlagRegistry.EnsureWired();

            int turn = evaluationTurn ?? TimelineScriptEvaluator.ResolveCurrentEvaluationTurn();
            List<HistoryTimelineBranch> candidates = SanitizeCandidates(branches, mgr, out bool usedFallback);
            result.usedSafeFailFallback = usedFallback;

            if (candidates.Count == 0)
            {
                result.status = MagiDeliberationStatus.SafeFailFallback;
                result.message = "Safe-Fail: 審議候補 0 件 — スクルド順フォールバック不可";
                result.success = false;
                LogDeliberation(result, BuildEmptyVotes("候補なし"));
                LastDeliberationResult = result;
                return result;
            }

            Dictionary<string, TimelineScriptEvaluationResult> scriptById =
                BuildScriptScoreMap(candidates, turn);
            List<TimelineScriptEvaluationResult> rankingList = BuildRankingList(scriptById);
            HistoryTimelineBranch mainAnchor = ResolveMainStoryAnchor(mgr);

            MagiUnitVote verdandiVote = EnsureInstance().verdandi.SelectRecommendation(
                candidates, scriptById, turn, mainAnchor);
            MagiUnitVote urdVote = EnsureInstance().urd.SelectRecommendation(
                candidates, scriptById, turn, mainAnchor);
            MagiUnitVote skuldVote = EnsureInstance().skuld.SelectRecommendation(
                candidates, scriptById, turn, mainAnchor);

            List<MagiUnitVote> votes = new List<MagiUnitVote> { verdandiVote, urdVote, skuldVote };
            result.comparison = BuildComparisonTable(candidates, scriptById, turn, mainAnchor);
            result.votes = votes;

            bool verdandiSkuldSplit = !string.IsNullOrWhiteSpace(verdandiVote.recommendedBranchId) &&
                                      !string.IsNullOrWhiteSpace(skuldVote.recommendedBranchId) &&
                                      !string.Equals(
                                          verdandiVote.recommendedBranchId,
                                          skuldVote.recommendedBranchId,
                                          StringComparison.OrdinalIgnoreCase);

            bool unanimous = TryResolveUnanimousChoice(votes, out string agreedBranchId, out int agreedScore);
            if (unanimous)
            {
                result.status = MagiDeliberationStatus.AllAgreed;
                result.agreedBranchId = agreedBranchId;
                result.awaitingManualSelection = false;
                TimelineGenerationLoopEngine.SyncMagiDeliberationState(false, rankingList);

                MainStoryCommitResult commit = HistoryBranchManager.CommitBranchAsMainStory(
                    agreedBranchId,
                    agreedScore > 0 ? agreedScore : (int?)null,
                    turn);
                result.commitResult = commit;
                result.success = commit.success;
                result.message =
                    commit.success
                        ? $"全会一致 ➔ 自動 Commit: {agreedBranchId} score={commit.totalScore}"
                        : $"全会一致だが Commit 失敗: {commit.message}";
            }
            else
            {
                result.status = MagiDeliberationStatus.Disagreed;
                result.awaitingManualSelection = true;
                result.success = true;
                result.message = verdandiSkuldSplit
                    ? "審議割れ ➔ 手動選定待ち（ヴェルダンディ≠スクルド）"
                    : "審議割れ ➔ 手動選定待ち";
                TimelineGenerationLoopEngine.SyncMagiDeliberationState(true, rankingList);
                LogDisagreementComparison(votes, result.comparison);
            }

            LogDeliberation(result, votes);
            LastDeliberationResult = result;
            return result;
        }
        catch (Exception exception)
        {
            result.success = false;
            result.status = MagiDeliberationStatus.SafeFailFallback;
            result.usedSafeFailFallback = true;
            result.message = $"Safe-Fail: {exception.Message}";
            Debug.LogWarning($"[MagiSystemDecisionEngine] {result.message}");
            TrySkuldFallbackCommit(mgr: null, turn: evaluationTurn ?? 1, result);
            LastDeliberationResult = result;
            return result;
        }
    }

    private static List<HistoryTimelineBranch> SanitizeCandidates(
        List<HistoryTimelineBranch> branches,
        HistoryBranchManager mgr,
        out bool usedFallback)
    {
        usedFallback = false;
        List<HistoryTimelineBranch> list = new List<HistoryTimelineBranch>();
        if (branches != null)
        {
            for (int i = 0; i < branches.Count; i++)
            {
                HistoryTimelineBranch branch = branches[i];
                if (branch != null && !string.IsNullOrWhiteSpace(branch.branchId))
                {
                    list.Add(branch);
                }
            }
        }

        if (list.Count > 0)
        {
            return list;
        }

        usedFallback = true;
        IReadOnlyList<HistoryTimelineBranch> registered = mgr?.RegisteredBranches;
        if (registered == null)
        {
            return list;
        }

        for (int i = 0; i < registered.Count; i++)
        {
            HistoryTimelineBranch branch = registered[i];
            if (branch != null &&
                !string.IsNullOrWhiteSpace(branch.branchId) &&
                !HistoryBranchManager.IsCanonBranchKey(branch.branchId))
            {
                list.Add(branch);
            }
        }

        list.Sort((a, b) => string.CompareOrdinal(a?.branchId, b?.branchId));
        return list;
    }

    private static Dictionary<string, TimelineScriptEvaluationResult> BuildScriptScoreMap(
        IReadOnlyList<HistoryTimelineBranch> candidates,
        int turn)
    {
        Dictionary<string, TimelineScriptEvaluationResult> map =
            new Dictionary<string, TimelineScriptEvaluationResult>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < candidates.Count; i++)
        {
            HistoryTimelineBranch branch = candidates[i];
            if (branch == null || string.IsNullOrWhiteSpace(branch.branchId))
            {
                continue;
            }

            TimelineScriptEvaluationResult eval =
                TimelineScriptEvaluator.EvaluateTimelineBranch(branch, turn);
            map[branch.branchId] = eval;
        }

        return map;
    }

    private static List<TimelineScriptEvaluationResult> BuildRankingList(
        Dictionary<string, TimelineScriptEvaluationResult> scriptById)
    {
        List<TimelineScriptEvaluationResult> list = new List<TimelineScriptEvaluationResult>(scriptById.Values);
        list.Sort((a, b) => b.totalScore.CompareTo(a.totalScore));
        return list;
    }

    private static HistoryTimelineBranch ResolveMainStoryAnchor(HistoryBranchManager mgr)
    {
        if (mgr == null)
        {
            return null;
        }

        if (mgr.CurrentActiveBranch != null && mgr.HasCommittedMainStory)
        {
            return mgr.CurrentActiveBranch;
        }

        string mainId = mgr.MainStoryBranchId;
        if (!string.IsNullOrWhiteSpace(mainId))
        {
            return mgr.FindRegisteredBranchForVerification(mainId);
        }

        return null;
    }

    private static List<MagiBranchComparisonRow> BuildComparisonTable(
        IReadOnlyList<HistoryTimelineBranch> candidates,
        IReadOnlyDictionary<string, TimelineScriptEvaluationResult> scriptById,
        int turn,
        HistoryTimelineBranch mainAnchor)
    {
        VerdandiEvaluator verdandi = new VerdandiEvaluator();
        UrdEvaluator urd = new UrdEvaluator();
        SkuldEvaluator skuld = new SkuldEvaluator();
        List<MagiBranchComparisonRow> rows = new List<MagiBranchComparisonRow>();

        for (int i = 0; i < candidates.Count; i++)
        {
            HistoryTimelineBranch branch = candidates[i];
            if (branch == null)
            {
                continue;
            }

            scriptById.TryGetValue(branch.branchId, out TimelineScriptEvaluationResult script);
            script ??= TimelineScriptEvaluator.EvaluateTimelineBranch(branch, turn);

            MagiUnitVote v = verdandi.ScoreBranch(branch, script, turn, mainAnchor);
            MagiUnitVote u = urd.ScoreBranch(branch, script, turn, mainAnchor);
            MagiUnitVote s = skuld.ScoreBranch(branch, script, turn, mainAnchor);

            rows.Add(new MagiBranchComparisonRow
            {
                branchId = branch.branchId,
                branchName = TimelineScriptEvaluator.ResolveBranchDisplayName(branch),
                verdandiScore = v.score,
                urdScore = u.vetoTriggered ? int.MinValue : u.score,
                skuldScore = s.score,
                urdVeto = u.vetoTriggered,
                scriptTotalScore = script.totalScore
            });
        }

        rows.Sort((a, b) => b.skuldScore.CompareTo(a.skuldScore));
        return rows;
    }

    private static bool TryResolveUnanimousChoice(
        IReadOnlyList<MagiUnitVote> votes,
        out string branchId,
        out int score)
    {
        branchId = string.Empty;
        score = 0;
        if (votes == null || votes.Count < 3)
        {
            return false;
        }

        string first = votes[0]?.recommendedBranchId ?? string.Empty;
        if (string.IsNullOrWhiteSpace(first))
        {
            return false;
        }

        for (int i = 1; i < votes.Count; i++)
        {
            string other = votes[i]?.recommendedBranchId ?? string.Empty;
            if (!string.Equals(first, other, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        branchId = first;
        score = votes[2]?.score ?? 0;
        return true;
    }

    private static void LogDeliberation(MagiDeliberationResult result, IReadOnlyList<MagiUnitVote> votes)
    {
        string verdandiChoice = FindVoteLabel(votes, VerdandiEvaluator.Id);
        string urdChoice = FindVoteLabel(votes, UrdEvaluator.Id);
        string skuldChoice = FindVoteLabel(votes, SkuldEvaluator.Id);
        string statusLabel = result.status switch
        {
            MagiDeliberationStatus.AllAgreed => "全会一致・自動Commit",
            MagiDeliberationStatus.Disagreed => "審議割れ・手動待ち",
            _ => "Safe-Fail"
        };

        Debug.Log(
            $"<color=#CE93D8><b>{LogTag}</b></color> " +
            $"ヴェルダンディ: {verdandiChoice}, ウルズ: {urdChoice}, スクルド: {skuldChoice} " +
            $"➔ [結果: {statusLabel}] {result.message}");
    }

    private static void LogDisagreementComparison(
        IReadOnlyList<MagiUnitVote> votes,
        IReadOnlyList<MagiBranchComparisonRow> comparison)
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine($"<color=#FFB74D><b>{DeliberationLogTag}</b></color> 審議割れ — 推薦理由:");
        for (int i = 0; i < votes.Count; i++)
        {
            MagiUnitVote vote = votes[i];
            sb.AppendLine(
                $"  {vote.unitDisplayName}: {vote.recommendedBranchName} ({vote.recommendedBranchId}) " +
                $"score={vote.score} — {vote.reason}");
        }

        sb.AppendLine("  --- スコア比較 ---");
        for (int i = 0; i < comparison.Count; i++)
        {
            MagiBranchComparisonRow row = comparison[i];
            string urdLabel = row.urdVeto ? "VETO" : row.urdScore.ToString();
            sb.AppendLine(
                $"  {row.branchName}: V={row.verdandiScore} U={urdLabel} S={row.skuldScore} " +
                $"(script={row.scriptTotalScore})");
        }

        Debug.Log(sb.ToString());
    }

    private static string FindVoteLabel(IReadOnlyList<MagiUnitVote> votes, string unitId)
    {
        for (int i = 0; i < votes.Count; i++)
        {
            MagiUnitVote vote = votes[i];
            if (vote != null && string.Equals(vote.unitId, unitId, StringComparison.OrdinalIgnoreCase))
            {
                return string.IsNullOrWhiteSpace(vote.recommendedBranchName)
                    ? vote.recommendedBranchId
                    : $"{vote.recommendedBranchName}({vote.score})";
            }
        }

        return "—";
    }

    private static List<MagiUnitVote> BuildEmptyVotes(string reason)
    {
        return new List<MagiUnitVote>
        {
            new MagiUnitVote { unitId = VerdandiEvaluator.Id, unitDisplayName = VerdandiEvaluator.DisplayName, reason = reason },
            new MagiUnitVote { unitId = UrdEvaluator.Id, unitDisplayName = UrdEvaluator.DisplayName, reason = reason },
            new MagiUnitVote { unitId = SkuldEvaluator.Id, unitDisplayName = SkuldEvaluator.DisplayName, reason = reason }
        };
    }

    private static void TrySkuldFallbackCommit(
        HistoryBranchManager mgr,
        int turn,
        MagiDeliberationResult result)
    {
        try
        {
            mgr ??= HistoryBranchManager.EnsureInstance();
            List<TimelineScriptEvaluationResult> rankings =
                TimelineScriptEvaluator.EvaluateAllRegisteredBranches(turn);
            if (rankings == null || rankings.Count == 0)
            {
                return;
            }

            TimelineScriptEvaluationResult best = rankings[0];
            for (int i = 1; i < rankings.Count; i++)
            {
                if (rankings[i].totalScore > best.totalScore)
                {
                    best = rankings[i];
                }
            }

            if (string.IsNullOrWhiteSpace(best.branchId) || best.isCanon)
            {
                return;
            }

            result.agreedBranchId = best.branchId;
            result.message += $" | スクルド順フォールバック候補={best.branchId}";
            TimelineGenerationLoopEngine.SyncMagiDeliberationState(true, rankings);
            result.awaitingManualSelection = true;
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[MagiSystemDecisionEngine] Skuld fallback Safe-Fail: {exception.Message}");
        }
    }

    public static MagiSystemDecisionVerifyResult RunVerification()
    {
        MagiSystemDecisionVerifyResult verify = new MagiSystemDecisionVerifyResult();
        StringBuilder log = new StringBuilder();

        try
        {
            SimulationVerifyBootstrap.PrepareFreshStoryDay(1, GameMode.StoryMode);
            HistoryFlagRegistry.EnsureWired();
            HistoryFlagRegistry.ClearForTesting();
            HistoryBranchManager mgr = HistoryBranchManager.EnsureInstance();
            mgr.ClearRegisteredBranches();
            TimelineGenerationLoopEngine.SyncMagiDeliberationState(false);

            int nationId = MicroToMacroAggregator.DefaultNationId;

            HistoryAlterationResult innovation = HistoryBranchManager.RegisterHistoryAlteration(
                5,
                nationId,
                "HIST_NATION_001_GEO_TURN_001_INNOVATION",
                "ALT_NATION_001_OUTCOME_T005_INNOVATION",
                new MacroParamDelta
                {
                    powerMultiplier = 1.14f,
                    barrierEfficiencyDelta = 0.05f,
                    threatMultiplier = 0.94f
                },
                createNewBranch: true);
            HistoryTimelineBranch innovationBranch =
                mgr.FindRegisteredBranchForVerification(innovation.branchId);
            if (innovationBranch != null)
            {
                innovationBranch.displayName = "IF_Line: 魔導革新";
                mgr.UpsertRegisteredBranchForVerification(innovationBranch);
                HistoryBranchManager.CommitBranchAsMainStory(innovation.branchId, null, 5);
            }

            HistoryAlterationResult civil = HistoryBranchManager.RegisterHistoryAlteration(
                10,
                nationId,
                "HIST_NATION_001_GEO_TURN_001_CIVILWAR",
                null,
                new MacroParamDelta
                {
                    powerMultiplier = 0.96f,
                    barrierEfficiencyDelta = -0.02f,
                    threatMultiplier = 1.12f
                },
                createNewBranch: true);
            HistoryTimelineBranch civilBranch = mgr.FindRegisteredBranchForVerification(civil.branchId);
            if (civilBranch != null)
            {
                civilBranch.displayName = "IF_Line: 内乱の嵐";
                mgr.UpsertRegisteredBranchForVerification(civilBranch);
            }

            HistoryAlterationResult tech = HistoryBranchManager.RegisterHistoryAlteration(
                7,
                nationId,
                "HIST_NATION_001_GEO_TURN_001_INNOVATION",
                "ALT_NATION_001_OUTCOME_T007_INNOVATION",
                new MacroParamDelta
                {
                    powerMultiplier = 1.18f,
                    barrierEfficiencyDelta = 0.08f,
                    threatMultiplier = 0.88f
                },
                createNewBranch: true);
            HistoryTimelineBranch techBranch = mgr.FindRegisteredBranchForVerification(tech.branchId);
            if (techBranch != null)
            {
                techBranch.displayName = "IF_Line: 魔導技術突破";
                mgr.UpsertRegisteredBranchForVerification(techBranch);
            }

            HistoryAlterationResult monster = HistoryBranchManager.RegisterHistoryAlteration(
                14,
                nationId,
                "HIST_NATION_001_GEO_TURN_001_MONSTER_DEFENSE",
                "ALT_NATION_001_OUTCOME_T014_MONSTER_DEFENSE",
                new MacroParamDelta
                {
                    powerMultiplier = 1.04f,
                    barrierEfficiencyDelta = 0.04f,
                    threatMultiplier = 0.90f
                },
                createNewBranch: true);
            HistoryTimelineBranch monsterBranch = mgr.FindRegisteredBranchForVerification(monster.branchId);
            if (monsterBranch != null)
            {
                monsterBranch.displayName = "IF_Line: 魔獣防衛線";
                mgr.UpsertRegisteredBranchForVerification(monsterBranch);
            }

            HistoryAlterationResult vetoBranch = HistoryBranchManager.RegisterHistoryAlteration(
                8,
                nationId,
                "HIST_NATION_001_GEO_TURN_001_BARRIERDROP",
                null,
                new MacroParamDelta
                {
                    powerMultiplier = 1.22f,
                    barrierEfficiencyDelta = -0.32f,
                    threatMultiplier = 2.4f,
                    isSurvivalOverridden = true,
                    survivalValue = false
                },
                createNewBranch: true);
            HistoryTimelineBranch veto = mgr.FindRegisteredBranchForVerification(vetoBranch.branchId);
            if (veto != null)
            {
                veto.displayName = "IF_Line: 大暴走・矛盾枝";
                mgr.UpsertRegisteredBranchForVerification(veto);
            }

            List<HistoryTimelineBranch> disagreeSet = new List<HistoryTimelineBranch>();
            if (civilBranch != null)
            {
                disagreeSet.Add(civilBranch);
            }

            if (techBranch != null)
            {
                disagreeSet.Add(techBranch);
            }

            if (monsterBranch != null)
            {
                disagreeSet.Add(monsterBranch);
            }

            if (veto != null)
            {
                disagreeSet.Add(veto);
            }

            MagiDeliberationResult disagreeResult =
                EvaluateAndProposeBranch(disagreeSet, evaluationTurn: 10);
            bool votesDistinct = disagreeResult.votes.Count == 3 &&
                !string.Equals(
                    disagreeResult.votes[0].recommendedBranchId,
                    disagreeResult.votes[1].recommendedBranchId,
                    StringComparison.OrdinalIgnoreCase);
            verify.disagreedPass =
                disagreeResult.status == MagiDeliberationStatus.Disagreed &&
                disagreeResult.awaitingManualSelection &&
                TimelineGenerationLoopEngine.AwaitingManualBranchSelection &&
                votesDistinct;
            log.AppendLine(
                $"disagreed: status={disagreeResult.status} awaiting={disagreeResult.awaitingManualSelection} " +
                $"pass={verify.disagreedPass}");

            MagiDeliberationResult safeFailResult = EvaluateAndProposeBranch(null, evaluationTurn: 10);
            verify.safeFailPass =
                safeFailResult.usedSafeFailFallback &&
                safeFailResult.status == MagiDeliberationStatus.Disagreed;
            log.AppendLine(
                $"safeFail: status={safeFailResult.status} fallback={safeFailResult.usedSafeFailFallback} " +
                $"awaiting={safeFailResult.awaitingManualSelection} pass={verify.safeFailPass}");

            mgr.ClearRegisteredBranches();
            TimelineGenerationLoopEngine.SyncMagiDeliberationState(false);

            HistoryAlterationResult unanimous = HistoryBranchManager.RegisterHistoryAlteration(
                15,
                nationId,
                "HIST_NATION_001_GEO_TURN_001_CIVILWAR",
                "ALT_NATION_001_OUTCOME_T015_CIVIL",
                new MacroParamDelta
                {
                    powerMultiplier = 1.06f,
                    barrierEfficiencyDelta = 0.03f,
                    threatMultiplier = 1.05f
                },
                createNewBranch: true);
            HistoryTimelineBranch uniBranch = mgr.FindRegisteredBranchForVerification(unanimous.branchId);
            if (uniBranch != null)
            {
                uniBranch.displayName = "IF_Line: 統一審議枝";
                mgr.UpsertRegisteredBranchForVerification(uniBranch);
            }

            List<HistoryTimelineBranch> agreeSet = new List<HistoryTimelineBranch>();
            if (uniBranch != null)
            {
                agreeSet.Add(uniBranch);
            }

            MagiDeliberationResult agreeResult = EvaluateAndProposeBranch(agreeSet, evaluationTurn: 15);
            verify.allAgreedPass =
                agreeSet.Count == 1 &&
                agreeResult.status == MagiDeliberationStatus.AllAgreed &&
                agreeResult.commitResult != null &&
                agreeResult.commitResult.success &&
                !TimelineGenerationLoopEngine.AwaitingManualBranchSelection &&
                mgr.MainStoryBranchId == uniBranch?.branchId;
            log.AppendLine(
                $"allAgreed: status={agreeResult.status} commit={agreeResult.commitResult?.success} " +
                $"main={mgr.MainStoryBranchId} pass={verify.allAgreedPass}");

            verify.stalenessDisagreedPass = RunStalenessDisagreedProbe(mgr, log);

            EnvironmentBiorhythmEngine.ResetRevivalTransitionLogForVerification();
            verify.skuldPruningPass = RunSkuldPruningProbe(mgr, log);

            VerdandiEvaluator verdandiProbe = new VerdandiEvaluator();
            UrdEvaluator urdProbe = new UrdEvaluator();
            SkuldEvaluator skuldProbe = new SkuldEvaluator();
            bool unitProbePass = verdandiProbe.UnitId == VerdandiEvaluator.Id &&
                                 urdProbe.UnitId == UrdEvaluator.Id &&
                                 skuldProbe.UnitId == SkuldEvaluator.Id;
            log.AppendLine($"units: verdandi={verdandiProbe.UnitDisplayName} urd={urdProbe.UnitDisplayName} " +
                           $"skuld={skuldProbe.UnitDisplayName} pass={unitProbePass}");

            verify.success = verify.allAgreedPass && verify.disagreedPass && verify.safeFailPass &&
                             verify.stalenessDisagreedPass && verify.skuldPruningPass && unitProbePass;
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

    private static bool RunStalenessDisagreedProbe(HistoryBranchManager mgr, StringBuilder log)
    {
        try
        {
            mgr.ClearRegisteredBranches();
            TimelineGenerationLoopEngine.SyncMagiDeliberationState(false);

            const string gen1Id = "ALT_VERIFY_BEAST_GEN1_BEAST_CATASTROPHE";
            const string gen2Id = "ALT_VERIFY_BEAST_GEN2_BEAST_CATASTROPHE";
            const string gen3Id = "ALT_VERIFY_BEAST_GEN3_BEAST_CATASTROPHE";
            const string beastCandidateId = "ALT_VERIFY_BEAST_GEN4_BEAST_CATASTROPHE";
            const string civilCandidateId = "ALT_VERIFY_STALENESS_CIVILWAR";

            HistoryTimelineBranch gen1 = BuildVerifyBeastChainBranch(gen1Id, string.Empty, 251);
            HistoryTimelineBranch gen2 = BuildVerifyBeastChainBranch(gen2Id, gen1Id, 276);
            HistoryTimelineBranch gen3 = BuildVerifyBeastChainBranch(gen3Id, gen2Id, 301);
            HistoryTimelineBranch beastCandidate = BuildVerifyBeastChainBranch(
                beastCandidateId,
                gen3Id,
                326,
                new MacroParamDelta
                {
                    powerMultiplier = 1.38f,
                    barrierEfficiencyDelta = 0.14f,
                    threatMultiplier = 0.78f
                });
            HistoryTimelineBranch civilCandidate = BuildVerifyTurmoilBranch(
                civilCandidateId,
                gen3Id,
                326);

            mgr.UpsertRegisteredBranchForVerification(gen1);
            HistoryBranchManager.CommitBranchAsMainStory(gen1Id, null, 251);
            mgr.UpsertRegisteredBranchForVerification(gen2);
            HistoryBranchManager.CommitBranchAsMainStory(gen2Id, null, 276);
            mgr.UpsertRegisteredBranchForVerification(gen3);
            HistoryBranchManager.CommitBranchAsMainStory(gen3Id, null, 301);

            HistoryTimelineBranch anchor = mgr.FindRegisteredBranchForVerification(gen3Id);
            int streak = VerdandiEvaluator.CountConsecutiveSameTendencyStreak(anchor, mgr);

            mgr.UpsertRegisteredBranchForVerification(beastCandidate);
            mgr.UpsertRegisteredBranchForVerification(civilCandidate);

            List<HistoryTimelineBranch> staleSet = new List<HistoryTimelineBranch>
            {
                beastCandidate,
                civilCandidate
            };

            MagiDeliberationResult staleResult =
                EvaluateAndProposeBranch(staleSet, evaluationTurn: 326);

            string verdandiPick = staleResult.votes.Count > 0
                ? staleResult.votes[0].recommendedBranchId
                : string.Empty;
            string skuldPick = staleResult.votes.Count > 2
                ? staleResult.votes[2].recommendedBranchId
                : string.Empty;

            bool verdandiForcedAlternate = string.Equals(
                verdandiPick,
                civilCandidateId,
                StringComparison.OrdinalIgnoreCase);
            bool skuldKeptBeast = string.Equals(
                skuldPick,
                beastCandidateId,
                StringComparison.OrdinalIgnoreCase);
            bool staleDisagreed =
                staleResult.status == MagiDeliberationStatus.Disagreed &&
                staleResult.awaitingManualSelection &&
                TimelineGenerationLoopEngine.AwaitingManualBranchSelection &&
                streak >= VerdandiEvaluator.ForceAlternateStreakThreshold &&
                verdandiForcedAlternate &&
                skuldKeptBeast;

            VerdandiEvaluator verdandiScorer = new VerdandiEvaluator();
            TimelineScriptEvaluationResult beastScript =
                TimelineScriptEvaluator.EvaluateTimelineBranch(beastCandidate, targetTurn: 326);
            MagiUnitVote beastVerdandiScore = verdandiScorer.ScoreBranch(
                beastCandidate,
                beastScript,
                evaluationTurn: 326,
                anchor);
            bool beastScoreWrecked = beastVerdandiScore.score <= 0;
            bool verdandiScaleInRange = staleResult.votes.Count > 0 &&
                staleResult.votes[0].score >= VerdandiEvaluator.MinScore &&
                staleResult.votes[0].score <= VerdandiEvaluator.MaxScore;

            staleDisagreed = staleDisagreed && beastScoreWrecked && verdandiScaleInRange;

            string verdandiScoreLabel = staleResult.votes.Count > 0
                ? staleResult.votes[0].score.ToString()
                : "-";
            log.AppendLine(
                $"staleness: streak={streak} status={staleResult.status} " +
                $"verdandi={verdandiPick}({verdandiScoreLabel}) skuld={skuldPick} " +
                $"beastVerdandi={beastVerdandiScore.score} scaleOk={verdandiScaleInRange} pass={staleDisagreed}");
            return staleDisagreed;
        }
        catch (Exception exception)
        {
            log.AppendLine($"staleness: Safe-Fail {exception.Message} pass=false");
            return false;
        }
    }

    private static bool RunSkuldPruningProbe(HistoryBranchManager mgr, StringBuilder log)
    {
        try
        {
            mgr.ClearRegisteredBranches();
            TimelineGenerationLoopEngine.SyncMagiDeliberationState(false);

            const int evaluationTurn = 1051;
            const string parentId = "ALT_VERIFY_PRUNING_PARENT";
            const string beastCandidateId = "ALT_VERIFY_PRUNING_BEAST_CONVERGED";
            const string diverseCandidateId = "ALT_VERIFY_PRUNING_DIVERSE_POSSIBILITY";

            HistoryTimelineBranch parent = BuildVerifyBeastChainBranch(parentId, string.Empty, 1026);
            HistoryTimelineBranch beastCandidate = BuildVerifyConvergedBeastBranch(
                beastCandidateId,
                parentId,
                evaluationTurn);
            HistoryTimelineBranch diverseCandidate = BuildVerifyDiversePossibilityBranch(
                diverseCandidateId,
                parentId,
                evaluationTurn);

            mgr.UpsertRegisteredBranchForVerification(parent);
            HistoryBranchManager.CommitBranchAsMainStory(parentId, null, 1026);

            mgr.UpsertRegisteredBranchForVerification(beastCandidate);
            mgr.UpsertRegisteredBranchForVerification(diverseCandidate);

            SkuldEvaluator skuldScorer = new SkuldEvaluator();
            TimelineScriptEvaluationResult beastScript =
                TimelineScriptEvaluator.EvaluateTimelineBranch(beastCandidate, evaluationTurn);
            TimelineScriptEvaluationResult diverseScript =
                TimelineScriptEvaluator.EvaluateTimelineBranch(diverseCandidate, evaluationTurn);

            MagiUnitVote beastSkuld = skuldScorer.ScoreBranch(
                beastCandidate,
                beastScript,
                evaluationTurn,
                parent);
            MagiUnitVote diverseSkuld = skuldScorer.ScoreBranch(
                diverseCandidate,
                diverseScript,
                evaluationTurn,
                parent);

            SkuldPossibilityAnalysis beastAnalysis =
                SkuldEvaluator.AnalyzePossibilityTree(beastCandidate, evaluationTurn);
            SkuldPossibilityAnalysis diverseAnalysis =
                SkuldEvaluator.AnalyzePossibilityTree(diverseCandidate, evaluationTurn);

            List<HistoryTimelineBranch> pruningSet = new List<HistoryTimelineBranch>
            {
                beastCandidate,
                diverseCandidate
            };

            // Claude パイプライン (eval_verdandi_claude.py) 用に IF 枝候補をリポジトリ直下へ書き出す
            TryExportBranchCandidatesForPipeline(pruningSet, evaluationTurn);

            MagiDeliberationResult pruningResult =
                EvaluateAndProposeBranch(pruningSet, evaluationTurn);

            string skuldPick = pruningResult.votes.Count > 2
                ? pruningResult.votes[2].recommendedBranchId
                : string.Empty;

            bool diversePreferred = string.Equals(
                skuldPick,
                diverseCandidateId,
                StringComparison.OrdinalIgnoreCase);
            bool scoreGapValid = diverseSkuld.score > beastSkuld.score &&
                                 diverseSkuld.score >= 300 &&
                                 diverseAnalysis.branchCount >= 6 &&
                                 beastSkuld.score <= 120;
            bool scaleInRange = diverseSkuld.score >= 0 &&
                                diverseSkuld.score <= SkuldEvaluator.MaxPossibilityScore &&
                                beastSkuld.score >= 0 &&
                                beastSkuld.score <= SkuldEvaluator.MaxPossibilityScore;

            bool pass = diversePreferred && scoreGapValid && scaleInRange;

            log.AppendLine(
                $"skuldPruning: turn={evaluationTurn} beast={beastSkuld.score}({beastAnalysis.branchCount}枝) " +
                $"diverse={diverseSkuld.score}({diverseAnalysis.branchCount}枝) pick={skuldPick} " +
                $"model=BranchingFactor pass={pass}");
            return pass;
        }
        catch (Exception exception)
        {
            log.AppendLine($"skuldPruning: Safe-Fail {exception.Message} pass=false");
            return false;
        }
    }

    private static HistoryTimelineBranch BuildVerifyConvergedBeastBranch(
        string branchId,
        string parentBranchId,
        int turn)
    {
        int nationId = MicroToMacroAggregator.DefaultNationId;
        return new HistoryTimelineBranch
        {
            branchId = branchId,
            parentBranchId = parentBranchId ?? string.Empty,
            displayName = "IF_Line: 収束・魔獣災害ループ",
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
                    keyEventId = "HIST_NATION_001_GEO_TURN_001_MONSTER_DEFENSE",
                    outcomeFlag = "ALT_NATION_001_OUTCOME_MONSTER_DEFENSE",
                    delta = new MacroParamDelta
                    {
                        powerMultiplier = 0.88f,
                        barrierEfficiencyDelta = -0.14f,
                        threatMultiplier = 1.52f
                    }
                },
                new HistoryBranchNode
                {
                    turn = turn,
                    nationId = nationId,
                    keyEventId = "HIST_NATION_001_GEO_TURN_001_BARRIERDROP",
                    outcomeFlag = "ALT_NATION_001_OUTCOME_BARRIERDROP",
                    delta = new MacroParamDelta
                    {
                        powerMultiplier = 0.86f,
                        barrierEfficiencyDelta = -0.18f,
                        threatMultiplier = 1.65f
                    }
                }
            }
        };
    }

    private static HistoryTimelineBranch BuildVerifyDiversePossibilityBranch(
        string branchId,
        string parentBranchId,
        int turn)
    {
        int nationId = MicroToMacroAggregator.DefaultNationId;
        return new HistoryTimelineBranch
        {
            branchId = branchId,
            parentBranchId = parentBranchId ?? string.Empty,
            displayName = "IF_Line: 汎人類史・多枝開拓",
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
                    keyEventId = "HIST_NATION_001_GEO_TURN_001_HERO",
                    outcomeFlag = "ALT_NATION_001_OUTCOME_LOST_TECH_REVIVAL",
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
                    keyEventId = "HIST_NATION_001_GEO_TURN_001_SUCCESSION",
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

    private static HistoryTimelineBranch BuildVerifyBeastChainBranch(
        string branchId,
        string parentBranchId,
        int turn,
        MacroParamDelta deltaOverride = null)
    {
        MacroParamDelta delta = deltaOverride ?? new MacroParamDelta
        {
            powerMultiplier = 1.06f,
            barrierEfficiencyDelta = 0.02f,
            threatMultiplier = 1.10f
        };

        return new HistoryTimelineBranch
        {
            branchId = branchId,
            parentBranchId = parentBranchId ?? string.Empty,
            displayName = $"IF_Line: {branchId}",
            baseStartTurn = turn,
            primaryNationId = MicroToMacroAggregator.DefaultNationId,
            nodes = new List<HistoryBranchNode>
            {
                new HistoryBranchNode
                {
                    turn = turn,
                    nationId = MicroToMacroAggregator.DefaultNationId,
                    keyEventId = "HIST_NATION_001_GEO_TURN_001_BEAST_CATASTROPHE",
                    outcomeFlag = "ALT_NATION_001_OUTCOME_BEAST_CATASTROPHE",
                    delta = delta
                }
            }
        };
    }

    private static HistoryTimelineBranch BuildVerifyTurmoilBranch(
        string branchId,
        string parentBranchId,
        int turn)
    {
        return new HistoryTimelineBranch
        {
            branchId = branchId,
            parentBranchId = parentBranchId ?? string.Empty,
            displayName = "IF_Line: 内乱・学院対立",
            baseStartTurn = turn,
            primaryNationId = MicroToMacroAggregator.DefaultNationId,
            nodes = new List<HistoryBranchNode>
            {
                new HistoryBranchNode
                {
                    turn = turn,
                    nationId = MicroToMacroAggregator.DefaultNationId,
                    keyEventId = "HIST_NATION_001_GEO_TURN_001_CIVILWAR",
                    outcomeFlag = "ALT_NATION_001_OUTCOME_CIVILWAR",
                    delta = new MacroParamDelta
                    {
                        powerMultiplier = 0.94f,
                        barrierEfficiencyDelta = -0.04f,
                        threatMultiplier = 1.18f
                    }
                },
                new HistoryBranchNode
                {
                    turn = turn + 1,
                    nationId = MicroToMacroAggregator.DefaultNationId,
                    keyEventId = "HIST_NATION_001_GEO_TURN_001_ACADEMY_RIVALRY",
                    outcomeFlag = "ALT_NATION_001_OUTCOME_ACADEMY_SCHISM",
                    delta = new MacroParamDelta
                    {
                        powerMultiplier = 0.98f,
                        threatMultiplier = 1.08f
                    }
                }
            }
        };
    }

    /// <summary>
    /// リポジトリ直下へ CurrentBranchCandidates.json を書き出します（Claude Verdandi 評価用）。
    /// Safe-Fail: 失敗しても false を返し、呼び出し側は継続できます。
    /// </summary>
    public static bool ExportBranchCandidatesForPipeline(
        IReadOnlyList<HistoryTimelineBranch> branches,
        int evaluationTurn,
        out string exportedPath)
    {
        exportedPath = string.Empty;
        try
        {
            if (branches == null || branches.Count == 0)
            {
                return false;
            }

            string unityProjectRoot = Directory.GetParent(Application.dataPath)?.FullName
                                     ?? Application.dataPath;
            string repoRoot = Directory.GetParent(unityProjectRoot)?.FullName ?? unityProjectRoot;
            string path = Path.Combine(repoRoot, "CurrentBranchCandidates.json");

            var sb = new StringBuilder(1024);
            sb.AppendLine("{");
            sb.AppendLine($"  \"evaluationTurn\": {evaluationTurn},");
            sb.AppendLine("  \"branches\": [");
            for (int i = 0; i < branches.Count; i++)
            {
                HistoryTimelineBranch b = branches[i];
                if (b == null)
                {
                    continue;
                }

                string id = EscapeJson(b.branchId ?? string.Empty);
                string name = EscapeJson(b.displayName ?? string.Empty);
                string parent = EscapeJson(b.parentBranchId ?? string.Empty);
                sb.AppendLine("    {");
                sb.AppendLine($"      \"branchId\": \"{id}\",");
                sb.AppendLine($"      \"displayName\": \"{name}\",");
                sb.AppendLine($"      \"parentBranchId\": \"{parent}\",");
                sb.AppendLine($"      \"baseStartTurn\": {b.baseStartTurn}");
                sb.Append(i < branches.Count - 1 ? "    }," : "    }");
                sb.AppendLine();
            }

            sb.AppendLine("  ]");
            sb.AppendLine("}");

            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
            exportedPath = path;
            Debug.Log($"[MagiSystemDecisionEngine] IF枝候補を書き出しました: {path}");
            return true;
        }
        catch (Exception exception)
        {
            Debug.LogWarning(
                $"[MagiSystemDecisionEngine] CurrentBranchCandidates.json 書き出し Safe-Fail: {exception.Message}");
            return false;
        }
    }

    /// <summary>
    /// リポジトリ直下へ CurrentBranchCandidates.json を書き出します（Claude Verdandi 評価用）。
    /// Safe-Fail: 失敗しても検証自体は継続します。
    /// </summary>
    private static void TryExportBranchCandidatesForPipeline(
        IReadOnlyList<HistoryTimelineBranch> branches,
        int evaluationTurn)
    {
        ExportBranchCandidatesForPipeline(branches, evaluationTurn, out _);
    }

    private static string EscapeJson(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\r", "\\r")
            .Replace("\n", "\\n");
    }

    private static void WriteVerifyLog(MagiSystemDecisionVerifyResult verify)
    {
        try
        {
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
            string path = Path.Combine(projectRoot, "Logs", "magi_system_decision_verify.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? projectRoot);
            File.WriteAllText(
                path,
                $"{DateTime.UtcNow:O} success={verify.success}\n{verify.message}\n",
                Encoding.UTF8);
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[MagiSystemDecisionEngine] 検証ログスキップ: {exception.Message}");
        }
    }
}

public static class MagiSystemDecisionEngineBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoAttach()
    {
        MagiSystemDecisionEngine.EnsureInstance();
    }
}

#if UNITY_EDITOR
public static class MagiSystemDecisionEngineMenu
{
    [MenuItem("Tools/Procedural Map/Verify MAGI System Decision Engine")]
    [MenuItem("Tools/Procedural Map/Verify MAGI System Decision Engine (B案)")]
    public static void VerifyFromMenu()
    {
        MagiSystemDecisionVerifyResult result = MagiSystemDecisionEngine.RunVerification();
        Debug.Log(
            result.success
                ? $"<color=#A5D6A7><b>【MAGIシステム検証・B案】PASS</b></color>\n{result.message}"
                : $"<color=#FF8A80><b>【MAGIシステム検証・B案】FAIL</b></color>\n{result.message}");
        EditorUtility.DisplayDialog(
            "MAGI System Decision Engine (B案)",
            result.success ? "PASS\n" + result.message : "FAIL\n" + result.message,
            "OK");
    }

    public static void BatchVerifyMagiAndQuit()
    {
        MagiSystemDecisionVerifyResult result = MagiSystemDecisionEngine.RunVerification();
        EditorApplication.Exit(result.success ? 0 : 1);
    }

    [MenuItem("Tools/Procedural Map/Batch Run Civilization Revival 50 Years And Quit")]
    [MenuItem("Tools/Procedural Map/Run Civilization Revival 50 Years (T1001-1050)")]
    public static void RunCivilizationRevival50YearsFromMenu()
    {
        CivilizationRevivalBatchResult result =
            TimelineGenerationLoopEngine.RunCivilizationRevival50YearsAndCommit();
        Debug.Log(
            result.success
                ? $"<color=#A5D6A7><b>{result.message}</b></color>"
                : $"<color=#FF8A80><b>【文明復興50年】FAIL</b></color>\n{result.message}");
        EditorUtility.DisplayDialog(
            "Civilization Revival 50 Years",
            result.message,
            result.success ? "OK" : "FAIL");
    }

    /// <summary>
    /// Unity バッチ:
    /// -executeMethod MagiSystemDecisionEngineMenu.BatchRunCivilizationRevival50YearsAndQuit
    /// </summary>
    public static void BatchRunCivilizationRevival50YearsAndQuit()
    {
        CivilizationRevivalBatchResult result =
            TimelineGenerationLoopEngine.RunCivilizationRevival50YearsAndCommit();
        Debug.Log(
            result.success
                ? $"<color=#A5D6A7><b>{result.message}</b></color>"
                : $"<color=#FF8A80><b>【文明復興50年】FAIL</b></color>\n{result.message}");
        EditorApplication.Exit(result.success ? 0 : 1);
    }

    [MenuItem("Tools/Procedural Map/Batch Run From Pipeline Job And Quit")]
    [MenuItem("Tools/Procedural Map/Run From Pipeline Job (pipeline_job.json)")]
    public static void RunFromPipelineJobFromMenu()
    {
        PipelineJobBatchResult result = TimelineGenerationLoopEngine.RunFromPipelineJob();
        Debug.Log(
            result.success
                ? $"<color=#A5D6A7><b>{result.message}</b></color>"
                : $"<color=#FF8A80><b>【PipelineJob】FAIL</b></color>\n{result.message}");
        EditorUtility.DisplayDialog(
            "Pipeline Job",
            result.message,
            result.success ? "OK" : "FAIL");
    }

    /// <summary>
    /// Unity バッチ（ジョブ駆動）:
    /// -executeMethod MagiSystemDecisionEngineMenu.BatchRunFromJobFileAndQuit
    /// 事前にリポジトリ直下へ pipeline_job.json を書いておくこと。
    /// </summary>
    public static void BatchRunFromJobFileAndQuit()
    {
        PipelineJobBatchResult result = TimelineGenerationLoopEngine.RunFromPipelineJob();
        Debug.Log(
            result.success
                ? $"<color=#A5D6A7><b>{result.message}</b></color>"
                : $"<color=#FF8A80><b>【PipelineJob】FAIL</b></color>\n{result.message}");
        EditorApplication.Exit(result.success ? 0 : 1);
    }
}
#endif

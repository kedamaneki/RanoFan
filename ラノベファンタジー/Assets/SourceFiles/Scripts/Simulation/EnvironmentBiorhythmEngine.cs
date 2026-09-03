using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

// =============================================================================
// 環境魔力バイオリズム — 中周期（大活性期・魔獣群災害）≈400年モデル
// 連携: EraContextResolver / NaturalEcologyEngine / HistoryBranchManager
// =============================================================================

/// <summary>400年中周期内の位相。</summary>
public enum EnvironmentMidCyclePhase
{
    /// <summary>休眠・内ゲバの余白（平穏期）。</summary>
    Dormant = 0,
    /// <summary>魔力上昇・魔獣活性化の前兆。</summary>
    Rising = 1,
    /// <summary>大活性期・魔獣群災害ピーク。</summary>
    PeakCatastrophe = 2
}

/// <summary>中周期バイオリズムの解決結果。</summary>
public readonly struct EnvironmentBiorhythmSnapshot
{
    public readonly int Turn;
    public readonly int MidCycleTurns;
    public readonly int PositionInCycle;
    public readonly EnvironmentMidCyclePhase Phase;
    public readonly float ManaEcologyMultiplier;
    public readonly float BeastActivationMultiplier;
    public readonly float EffectiveMutationThreshold;

    public EnvironmentBiorhythmSnapshot(
        int turn,
        int midCycleTurns,
        int positionInCycle,
        EnvironmentMidCyclePhase phase,
        float manaEcologyMultiplier,
        float beastActivationMultiplier,
        float effectiveMutationThreshold)
    {
        Turn = turn;
        MidCycleTurns = midCycleTurns;
        PositionInCycle = positionInCycle;
        Phase = phase;
        ManaEcologyMultiplier = manaEcologyMultiplier;
        BeastActivationMultiplier = beastActivationMultiplier;
        EffectiveMutationThreshold = effectiveMutationThreshold;
    }
}

/// <summary>
/// 環境魔力の短周期／中周期（≈400ターン）バイオリズムを解決し、魔獣活性化係数を提供します。
/// </summary>
[DefaultExecutionOrder(-88)]
public class EnvironmentBiorhythmEngine : MonoBehaviour
{
    public const string LogTag = "【環境バイオリズム】";

    /// <summary>中周期（大活性期・魔獣群災害）のおおよその周期 [ターン≈年]。</summary>
    public const int MidCycleTurns = 400;

    /// <summary>400年周期内の平穏（休眠）区間 [ターン]。</summary>
    public const int MidCycleDormantSpan = 340;

    /// <summary>平穏から大災厄へ向かう上昇区間 [ターン]。</summary>
    public const int MidCycleRampSpan = 40;

    /// <summary>大活性期・魔獣群災害ピーク [ターン]。</summary>
    public const int MidCyclePeakSpan = 20;

    public const float DormantManaMultiplier = 0.92f;
    public const float DormantBeastMultiplier = 0.45f;
    public const float PeakManaMultiplier = 1.55f;
    public const float PeakBeastMultiplier = 2.35f;

    /// <summary>文明復興フェーズ開始ターン（400年大災厄サイクル停止）。</summary>
    public const int CivilizationRevivalStartTurn = EraContextResolver.ChronicleMaxTurn + 1;

    /// <summary>復興期の環境魔力定常比（0.3〜0.5 → ManaEcologyLevel 30〜50）。</summary>
    public const float RevivalManaEcologyRatioMin = 0.30f;
    public const float RevivalManaEcologyRatioMax = 0.50f;
    public const float RevivalManaEcologyRatioTarget = 0.40f;

    public const float RevivalManaEcologyMultiplier = 0.40f;
    public const float RevivalBeastActivationMultiplier = 0.32f;

    public const string NewEraLogTag = "【新時代到来: ターン1001〜】";

    private static bool revivalTransitionLogged;

    public static EnvironmentBiorhythmEngine Instance { get; private set; }

    [SerializeField] private int cycleEpochTurn = 1;

    public static EnvironmentBiorhythmEngine EnsureInstance()
    {
        if (Instance != null)
        {
            return Instance;
        }

        EnvironmentBiorhythmEngine existing = UnityEngine.Object.FindAnyObjectByType<EnvironmentBiorhythmEngine>();
        if (existing != null)
        {
            Instance = existing;
            return Instance;
        }

        GameObject hub = GameObject.Find("DebugSystemsHub");
        GameObject host = hub != null ? hub : new GameObject(nameof(EnvironmentBiorhythmEngine));
        EnvironmentBiorhythmEngine engine = host.GetComponent<EnvironmentBiorhythmEngine>();
        return engine != null ? engine : host.AddComponent<EnvironmentBiorhythmEngine>();
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

    /// <summary>中周期モデル（400ターン）を各所へ同期します。</summary>
    public static bool ApplyMidCycle400YearModel(bool persistConfig = true)
    {
        try
        {
            EnvironmentBiorhythmEngine engine = EnsureInstance();
            engine.cycleEpochTurn = 1;
            EraContextResolver.SyncEnvironmentBiorhythm(EraContextResolver.CurrentTurn);

            if (persistConfig)
            {
                TryPersistMidCycleConfig();
            }

            EnvironmentBiorhythmSnapshot snap = Resolve(EraContextResolver.CurrentTurn);
            Debug.Log(
                $"<color=#80CBC4><b>{LogTag}</b></color> 中周期={MidCycleTurns}年モデルを適用 " +
                $"(T{snap.Turn} pos={snap.PositionInCycle} phase={snap.Phase} " +
                $"mana×{snap.ManaEcologyMultiplier:F2} beast×{snap.BeastActivationMultiplier:F2})");
            return true;
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[EnvironmentBiorhythmEngine] ApplyMidCycle400YearModel Safe-Fail: {exception.Message}");
            return false;
        }
    }

    /// <summary>指定ターンの環境バイオリズムを解決します（Safe-Fail: 恒等）。</summary>
    public static EnvironmentBiorhythmSnapshot Resolve(int turn)
    {
        int safeTurn = Mathf.Clamp(turn, EraContextResolver.MinTurn, EraContextResolver.MaxTurn);
        return BuildSnapshot(safeTurn);
    }

    /// <summary>
    /// 400年周期位相を解決します（正史250年上限を超える推移向け）。
    /// 民生化/失伝など中周期ルールはこちらを使用してください。
    /// </summary>
    public static EnvironmentBiorhythmSnapshot ResolveMidCyclePhase(int turn)
    {
        int safeTurn = Mathf.Max(turn, EraContextResolver.MinTurn);
        return BuildSnapshot(safeTurn);
    }

    /// <summary>T≥1001 の文明復興フェーズか。</summary>
    public static bool IsCivilizationRevivalTurn(int turn)
    {
        return turn >= CivilizationRevivalStartTurn;
    }

    /// <summary>復興期の平穏な定常魔力レベル（30〜50）を返します。</summary>
    public static float ResolveFixedRevivalManaEcologyLevel()
    {
        float target = NatureEnvironmentStatus.MaxManaEcology * RevivalManaEcologyRatioTarget;
        return ClampRevivalManaEcology(target);
    }

    /// <summary>復興期の環境魔力を平穏定常領域へクランプします。</summary>
    public static float ClampRevivalManaEcology(float value)
    {
        float min = NatureEnvironmentStatus.MaxManaEcology * RevivalManaEcologyRatioMin;
        float max = NatureEnvironmentStatus.MaxManaEcology * RevivalManaEcologyRatioMax;
        return Mathf.Clamp(value, min, max);
    }

    /// <summary>復興フェーズ移行ログ（1回のみ）。</summary>
    public static void TryLogCivilizationRevivalTransition(int turn)
    {
        if (turn < CivilizationRevivalStartTurn || revivalTransitionLogged)
        {
            return;
        }

        revivalTransitionLogged = true;
        Debug.Log(
            $"<color=#80DEEA><b>{NewEraLogTag}</b></color> " +
            "400年周期の大災厄を解除。スクルドの評価基準を『剪定理論（未来の可能性の太さ）』へ更新し、魔獣を「自然の隣人」へ再定義しました。");
    }

    /// <summary>検証用 — 復興移行ログフラグをリセットします。</summary>
    public static void ResetRevivalTransitionLogForVerification()
    {
        revivalTransitionLogged = false;
    }

    private static EnvironmentBiorhythmSnapshot BuildSnapshot(int turn)
    {
        if (IsCivilizationRevivalTurn(turn))
        {
            TryLogCivilizationRevivalTransition(turn);
            return new EnvironmentBiorhythmSnapshot(
                turn,
                MidCycleTurns,
                0,
                EnvironmentMidCyclePhase.Dormant,
                RevivalManaEcologyMultiplier,
                RevivalBeastActivationMultiplier,
                ResolveEffectiveMutationThreshold(RevivalBeastActivationMultiplier));
        }

        int position = NormalizeCyclePosition(turn);
        EnvironmentMidCyclePhase phase = ResolvePhase(position);
        float manaMul = ResolveManaEcologyMultiplier(position, phase);
        float beastMul = ResolveBeastActivationMultiplier(position, phase);
        float threshold = ResolveEffectiveMutationThreshold(beastMul);
        return new EnvironmentBiorhythmSnapshot(
            turn,
            MidCycleTurns,
            position,
            phase,
            manaMul,
            beastMul,
            threshold);
    }

    public static int NormalizeCyclePosition(int turn)
    {
        if (MidCycleTurns <= 0)
        {
            return 0;
        }

        int zeroBased = turn - 1;
        int mod = zeroBased % MidCycleTurns;
        return mod < 0 ? mod + MidCycleTurns : mod;
    }

    public static EnvironmentMidCyclePhase ResolvePhase(int positionInCycle)
    {
        int pos = Mathf.Clamp(positionInCycle, 0, MidCycleTurns - 1);
        if (pos < MidCycleDormantSpan)
        {
            return EnvironmentMidCyclePhase.Dormant;
        }

        if (pos < MidCycleDormantSpan + MidCycleRampSpan)
        {
            return EnvironmentMidCyclePhase.Rising;
        }

        return EnvironmentMidCyclePhase.PeakCatastrophe;
    }

    public static float ResolveManaEcologyMultiplier(int positionInCycle, EnvironmentMidCyclePhase phase)
    {
        switch (phase)
        {
            case EnvironmentMidCyclePhase.PeakCatastrophe:
            {
                float peakT = Mathf.InverseLerp(
                    MidCycleDormantSpan + MidCycleRampSpan,
                    MidCycleTurns - 1,
                    positionInCycle);
                return Mathf.Lerp(1.25f, PeakManaMultiplier, peakT);
            }
            case EnvironmentMidCyclePhase.Rising:
            {
                float rampT = Mathf.InverseLerp(MidCycleDormantSpan, MidCycleDormantSpan + MidCycleRampSpan, positionInCycle);
                return Mathf.Lerp(DormantManaMultiplier, 1.25f, rampT);
            }
            default:
                return DormantManaMultiplier;
        }
    }

    public static float ResolveBeastActivationMultiplier(int positionInCycle, EnvironmentMidCyclePhase phase)
    {
        switch (phase)
        {
            case EnvironmentMidCyclePhase.PeakCatastrophe:
            {
                float peakT = Mathf.InverseLerp(
                    MidCycleDormantSpan + MidCycleRampSpan,
                    MidCycleTurns - 1,
                    positionInCycle);
                return Mathf.Lerp(1.65f, PeakBeastMultiplier, peakT);
            }
            case EnvironmentMidCyclePhase.Rising:
            {
                float rampT = Mathf.InverseLerp(MidCycleDormantSpan, MidCycleDormantSpan + MidCycleRampSpan, positionInCycle);
                return Mathf.Lerp(DormantBeastMultiplier, 1.65f, rampT);
            }
            default:
                return DormantBeastMultiplier;
        }
    }

    /// <summary>魔獣変異閾値（高いほど変異しにくい）。</summary>
    public static float ResolveEffectiveMutationThreshold(float beastActivationMultiplier)
    {
        float mul = beastActivationMultiplier <= 0f ? 1f : beastActivationMultiplier;
        return NaturalEcologyEngine.MutationManaThreshold * mul;
    }

    public static float ResolveEffectiveMutationThresholdForTurn(int turn)
    {
        EnvironmentBiorhythmSnapshot snap = Resolve(turn);
        return snap.EffectiveMutationThreshold;
    }

    /// <summary>環境魔力レベルへ中周期補正を適用します。</summary>
    public static float ApplyManaEcologyBiorhythm(float baseManaEcology, int turn)
    {
        if (float.IsNaN(baseManaEcology) || float.IsInfinity(baseManaEcology))
        {
            baseManaEcology = 0f;
        }

        if (IsCivilizationRevivalTurn(turn))
        {
            return ResolveFixedRevivalManaEcologyLevel();
        }

        EnvironmentBiorhythmSnapshot snap = Resolve(turn);
        return NatureEnvironmentStatus.ClampMana(baseManaEcology * snap.ManaEcologyMultiplier);
    }

    private static void TryPersistMidCycleConfig()
    {
        try
        {
            string configPath = MacroChronicleJsonScan.ResolveDataFile("macro_chronicle_config.json");
            string block =
                "  \"environment_biorhythm\": {\n" +
                "    \"mid_cycle_turns\": 400,\n" +
                "    \"mid_cycle_dormant_span\": 340,\n" +
                "    \"mid_cycle_ramp_span\": 40,\n" +
                "    \"mid_cycle_peak_span\": 20,\n" +
                "    \"note\": \"400年平穏→大活性期・魔獣群災害\"\n" +
                "  }";

            if (string.IsNullOrWhiteSpace(configPath) || !File.Exists(configPath))
            {
                return;
            }

            string json = File.ReadAllText(configPath, Encoding.UTF8);
            if (json.IndexOf("\"environment_biorhythm\"", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return;
            }

            int insertAt = json.LastIndexOf('}');
            if (insertAt < 0)
            {
                return;
            }

            string trimmed = json.TrimEnd();
            bool needsComma = trimmed.Length > 1 && trimmed[trimmed.Length - 2] != '{' && trimmed[trimmed.Length - 2] != ',';
            string prefix = trimmed.Substring(0, insertAt).TrimEnd();
            if (needsComma && !prefix.EndsWith(",", StringComparison.Ordinal))
            {
                prefix += ",";
            }

            string patched = prefix + "\n" + block + "\n}\n";
            File.WriteAllText(configPath, patched, Encoding.UTF8);
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[EnvironmentBiorhythmEngine] config 書き戻し Safe-Fail: {exception.Message}");
        }
    }

    public static bool RunMidCycleSelfCheck()
    {
        EnvironmentBiorhythmSnapshot dormant = Resolve(50);
        EnvironmentBiorhythmSnapshot rising = Resolve(MidCycleDormantSpan + 10);
        EnvironmentBiorhythmSnapshot peak = Resolve(MidCycleTurns - 5);
        EnvironmentBiorhythmSnapshot extendedLostTech = ResolveMidCyclePhase(320);
        EnvironmentBiorhythmSnapshot revival = ResolveMidCyclePhase(CivilizationRevivalStartTurn);
        float revivalMana = ResolveFixedRevivalManaEcologyLevel();
        bool legacyPass = dormant.Phase == EnvironmentMidCyclePhase.Dormant &&
                          rising.Phase == EnvironmentMidCyclePhase.Rising &&
                          peak.Phase == EnvironmentMidCyclePhase.PeakCatastrophe &&
                          dormant.BeastActivationMultiplier < rising.BeastActivationMultiplier &&
                          rising.BeastActivationMultiplier < peak.BeastActivationMultiplier &&
                          MidCycleTurns == 400 &&
                          extendedLostTech.PositionInCycle >= 280 &&
                          extendedLostTech.Phase == EnvironmentMidCyclePhase.Dormant;
        bool revivalPass = revival.Phase != EnvironmentMidCyclePhase.PeakCatastrophe &&
                           revival.BeastActivationMultiplier < dormant.BeastActivationMultiplier &&
                           revivalMana >= NatureEnvironmentStatus.MaxManaEcology * RevivalManaEcologyRatioMin - 0.01f &&
                           revivalMana <= NatureEnvironmentStatus.MaxManaEcology * RevivalManaEcologyRatioMax + 0.01f;
        bool pass = legacyPass && revivalPass;
        Debug.Log(
            $"<color=#A5D6A7>【環境バイオリズム・検証】dormant(T50)={dormant.Phase} beast×{dormant.BeastActivationMultiplier:F2} / " +
            $"rising(T{MidCycleDormantSpan + 10})={rising.Phase} beast×{rising.BeastActivationMultiplier:F2} / " +
            $"peak(T{MidCycleTurns - 5})={peak.Phase} beast×{peak.BeastActivationMultiplier:F2} / " +
            $"revival(T{CivilizationRevivalStartTurn})={revival.Phase} mana={revivalMana:F1} " +
            $"→ {(pass ? "PASS" : "CHECK")}</color>");
        return pass;
    }
}

public static class EnvironmentBiorhythmEngineBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoAttach()
    {
        EnvironmentBiorhythmEngine.EnsureInstance();
        EnvironmentBiorhythmEngine.ApplyMidCycle400YearModel(persistConfig: false);
    }
}

#if UNITY_EDITOR
public static class EnvironmentBiorhythmEngineMenu
{
    [MenuItem("Tools/Procedural Map/Verify Environment Biorhythm (400y Mid-Cycle)")]
    public static void VerifyFromMenu()
    {
        EnvironmentBiorhythmEngine.ApplyMidCycle400YearModel(persistConfig: true);
        bool pass = EnvironmentBiorhythmEngine.RunMidCycleSelfCheck();
        EditorUtility.DisplayDialog(
            "Environment Biorhythm",
            pass ? "400年中周期モデル PASS" : "CHECK — ログを確認してください",
            "OK");
    }
}
#endif

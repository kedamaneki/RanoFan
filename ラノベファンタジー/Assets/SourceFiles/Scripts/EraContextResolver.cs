using System;
using UnityEngine;

// =============================================================================
// 時代変動型アンロック — プレイ/解読ターンから時代タグと EraModifier を解決
// 連携: MagicCustomizer / JobEvolutionManager / HistoryDecodingPresenter
// =============================================================================

/// <summary>ターン帯から決まる時代タグ。</summary>
public enum EraTag
{
    /// <summary>1〜50：普遍期。補正なし。</summary>
    Early = 0,
    /// <summary>51〜150：過渡期。</summary>
    Mid = 1,
    /// <summary>151〜250：古代化期（Ancient）。</summary>
    Late = 2
}

/// <summary>魔法へ乗算する時代補正。未定義データは恒等（1.0）。</summary>
public readonly struct EraModifier
{
    public readonly float ManaCostMultiplier;
    public readonly float CastSpeedMultiplier;
    public readonly EraTag Era;
    public readonly bool AppliedFromSettings;

    public EraModifier(float manaCostMultiplier, float castSpeedMultiplier, EraTag era, bool appliedFromSettings)
    {
        ManaCostMultiplier = manaCostMultiplier <= 0f ? 1f : manaCostMultiplier;
        CastSpeedMultiplier = castSpeedMultiplier <= 0f ? 1f : castSpeedMultiplier;
        Era = era;
        AppliedFromSettings = appliedFromSettings;
    }

    public static EraModifier Identity(EraTag era)
    {
        return new EraModifier(1f, 1f, era, false);
    }
}

/// <summary>
/// 現在のプレイ／解読年代（CurrentTurn: 1〜250）と時代タグを保持するシングルトン。
/// </summary>
[DefaultExecutionOrder(-90)]
public class EraContextResolver : MonoBehaviour
{
    public const int MinTurn = 1;
    public const int MaxTurn = 250;
    /// <summary>1000年史バッチ等、正史250年を超えるシミュレーション上限。</summary>
    public const int ChronicleMaxTurn = 1000;
    public const int EarlyEraMaxTurn = 50;
    public const int MidEraMaxTurn = 150;

    public const string EraEarlyFlag = "ERA_EARLY";
    public const string EraMidFlag = "ERA_MID";
    public const string EraLateFlag = "ERA_LATE";
    public const string EraAncientFlag = "ERA_ANCIENT";
    public const string EraUniversalFlag = "ERA_UNIVERSAL";
    public const string AncientArchivistFlag = "ERA_ANCIENT_ARCHIVIST";

    public const float AncientMultiplierMin = 1.5f;
    public const float AncientMultiplierMax = 2.5f;

    public static EraContextResolver Instance { get; private set; }

    [SerializeField] private int currentTurn = MinTurn;

    /// <summary>現在の解読／プレイターン（1〜250）。未配置時は 1。</summary>
    public static int CurrentTurn
    {
        get
        {
            EraContextResolver resolver = Instance;
            return resolver != null ? resolver.currentTurn : MinTurn;
        }
    }

    /// <summary>現在の時代タグ。未配置時は普遍期。</summary>
    public static EraTag CurrentEra => ResolveEraTag(CurrentTurn);

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        if (!IsValidTurn(currentTurn))
        {
            currentTurn = MinTurn;
        }
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    /// <summary>シーンに無い場合は DebugSystemsHub へ生成します。Awake 前（エディタ検証）でも Instance を結線します。</summary>
    public static EraContextResolver EnsureInstance()
    {
        if (Instance != null)
        {
            return Instance;
        }

        EraContextResolver found = UnityEngine.Object.FindAnyObjectByType<EraContextResolver>();
        if (found != null)
        {
            Instance = found;
            return Instance;
        }

        GameObject hub = GameObject.Find("DebugSystemsHub");
        GameObject host = hub != null ? hub : new GameObject(nameof(EraContextResolver));
        EraContextResolver existing = host.GetComponent<EraContextResolver>();
        if (existing != null)
        {
            Instance = existing;
            return Instance;
        }

        Instance = host.AddComponent<EraContextResolver>();
        return Instance;
    }

    [ContextMenu("Verify Turn 5 vs 200")]
    private void DebugVerifyTurns()
    {
        RunTurnComparisonSelfCheck();
    }

    [ContextMenu("Set Turn 5 Universal")]
    private void DebugSetTurn5()
    {
        TrySetCurrentTurn(5);
    }

    [ContextMenu("Set Turn 200 Ancient")]
    private void DebugSetTurn200()
    {
        TrySetCurrentTurn(200);
    }

    /// <summary>
    /// ターンを更新します。範囲外は変更せず false（Safe-Fail、補正は 1.0 のまま）。
    /// </summary>
    public static bool TrySetCurrentTurn(int turn)
    {
        EraContextResolver resolver = EnsureInstance();
        if (resolver == null)
        {
            return false;
        }

        if (!IsValidTurn(turn))
        {
            Debug.LogWarning(
                $"[EraContextResolver] 無効なターン {turn}（有効範囲 {MinTurn}〜{MaxTurn}）。" +
                $"年代を維持します（CurrentTurn={resolver.currentTurn}, {FormatEraLabel(ResolveEraTag(resolver.currentTurn))}）。");
            return false;
        }

        int previous = resolver.currentTurn;
        resolver.currentTurn = turn;
        if (previous != turn)
        {
            Debug.Log(
                $"<color=#80CBC4>【時代変動】ターン {previous} → {turn} " +
                $"（{FormatEraLabel(ResolveEraTag(previous))} → {FormatEraLabel(ResolveEraTag(turn))}）</color>");
        }

        return true;
    }

    /// <summary>ターンから時代タグを判定します。範囲外は普遍期（Safe-Fail）。</summary>
    public static EraTag ResolveEraTag(int turn)
    {
        if (!IsValidTurn(turn))
        {
            return EraTag.Early;
        }

        if (turn <= EarlyEraMaxTurn)
        {
            return EraTag.Early;
        }

        if (turn <= MidEraMaxTurn)
        {
            return EraTag.Mid;
        }

        return EraTag.Late;
    }

    /// <summary>ERA_EARLY / ERA_MID / ERA_LATE / ERA_ANCIENT / ERA_UNIVERSAL が現在時代で成立するか。</summary>
    public static bool IsEraConditionFlag(string flagKey)
    {
        if (string.IsNullOrWhiteSpace(flagKey))
        {
            return false;
        }

        string key = flagKey.Trim();
        EraTag era = CurrentEra;
        if (string.Equals(key, EraEarlyFlag, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(key, EraUniversalFlag, StringComparison.OrdinalIgnoreCase))
        {
            return era == EraTag.Early;
        }

        if (string.Equals(key, EraMidFlag, StringComparison.OrdinalIgnoreCase))
        {
            return era == EraTag.Mid;
        }

        if (string.Equals(key, EraLateFlag, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(key, EraAncientFlag, StringComparison.OrdinalIgnoreCase))
        {
            return era == EraTag.Late;
        }

        return false;
    }

    /// <summary>ERA_* タグか（ConditionFlagResolver が時代条件として扱うキー）。</summary>
    public static bool IsKnownEraFlagKey(string flagKey)
    {
        if (string.IsNullOrWhiteSpace(flagKey))
        {
            return false;
        }

        string key = flagKey.Trim();
        return string.Equals(key, EraEarlyFlag, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(key, EraMidFlag, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(key, EraLateFlag, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(key, EraAncientFlag, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(key, EraUniversalFlag, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 魔法の eraSettings と現在時代から補正を算出します。
    /// eraSettings 未定義（古いデータ）は恒等 1.0。古代化期は 1.5〜2.5 へクランプした倍率。
    /// </summary>
    public static EraModifier ResolveMagicModifier(MagicEraSettings eraSettings)
    {
        EraTag era = CurrentEra;
        if (eraSettings == null)
        {
            return EraModifier.Identity(era);
        }

        float manaMul;
        float castMul;
        switch (era)
        {
            case EraTag.Mid:
                manaMul = PositiveOrDefault(eraSettings.midManaCostMultiplier, 1f);
                castMul = PositiveOrDefault(eraSettings.midCastSpeedMultiplier, 1f);
                break;
            case EraTag.Late:
                manaMul = ResolveAncientMultiplier(eraSettings.lateManaCostMultiplier);
                castMul = ResolveAncientMultiplier(eraSettings.lateCastSpeedMultiplier);
                break;
            default:
                manaMul = PositiveOrDefault(eraSettings.earlyManaCostMultiplier, 1f);
                castMul = PositiveOrDefault(eraSettings.earlyCastSpeedMultiplier, 1f);
                break;
        }

        return new EraModifier(manaMul, castMul, era, true);
    }

    /// <summary>古代化期の 1.5〜2.5 倍（ターン 151→1.5、250→2.5）。設定値があればそれをクランプ。</summary>
    public static float ResolveAncientMultiplier(float configuredOrZero)
    {
        if (configuredOrZero > 0f)
        {
            return Mathf.Clamp(configuredOrZero, AncientMultiplierMin, AncientMultiplierMax);
        }

        int turn = CurrentTurn;
        if (!IsValidTurn(turn) || ResolveEraTag(turn) != EraTag.Late)
        {
            return 1f;
        }

        float t = Mathf.InverseLerp(MidEraMaxTurn + 1, MaxTurn, turn);
        return Mathf.Lerp(AncientMultiplierMin, AncientMultiplierMax, t);
    }

    /// <summary>ジョブ eraSettings の時代別フラグを、通常 unlockConditions より先に評価します。</summary>
    public static bool AreEraUnlockFlagsMet(JobMasterData job, Func<string, bool> flagResolver)
    {
        if (job?.eraSettings == null)
        {
            return true;
        }

        string[] flags = SelectEraUnlockFlags(job.eraSettings, CurrentEra);
        if (flags == null || flags.Length == 0)
        {
            return true;
        }

        for (int i = 0; i < flags.Length; i++)
        {
            string flag = flags[i];
            if (string.IsNullOrWhiteSpace(flag))
            {
                continue;
            }

            if (flagResolver == null)
            {
                Debug.LogWarning($"[EraContextResolver] フラグ判定フック未設定: {flag}");
                return false;
            }

            try
            {
                if (!flagResolver(flag.Trim()))
                {
                    return false;
                }
            }
            catch (Exception exception)
            {
                Debug.LogError($"[EraContextResolver] 時代フラグ判定例外: {flag}\n{exception}");
                return false;
            }
        }

        return true;
    }

    public static string FormatEraLabel(EraTag era)
    {
        switch (era)
        {
            case EraTag.Mid:
                return "ERA_MID（過渡期）";
            case EraTag.Late:
                return "ERA_LATE（古代化期）";
            default:
                return "ERA_EARLY（普遍期）";
        }
    }

    public static bool IsValidTurn(int turn)
    {
        return turn >= MinTurn && turn <= MaxTurn;
    }

    /// <summary>1000年史シミュレーション用ターンクランプ（1〜1000）。</summary>
    public static int ClampSimulationTurn(int turn)
    {
        return Mathf.Clamp(turn, MinTurn, ChronicleMaxTurn);
    }

    /// <summary>正史250年を超える拡張年代ターンか。</summary>
    public static bool IsExtendedChronicleTurn(int turn)
    {
        return turn > MaxTurn && turn <= ChronicleMaxTurn;
    }

    /// <summary>環境魔力中周期（≈400年）バイオリズムを現在ターンへ同期します。</summary>
    public static EnvironmentBiorhythmSnapshot SyncEnvironmentBiorhythm(int? turn = null)
    {
        int probeTurn = turn ?? CurrentTurn;
        EnvironmentBiorhythmEngine.EnsureInstance();
        EnvironmentBiorhythmSnapshot snapshot = EnvironmentBiorhythmEngine.Resolve(probeTurn);
        return snapshot;
    }

    /// <summary>現在ターンの中周期位相（400年モデル）。</summary>
    public static EnvironmentMidCyclePhase CurrentMidCyclePhase =>
        SyncEnvironmentBiorhythm().Phase;

    /// <summary>ターン5とターン200で魔法コスト／ジョブ時代フラグが変わることをログ検証します。</summary>
    public static bool RunTurnComparisonSelfCheck()
    {
        HistoryFlagRegistry.EnsureWired();
        EnsureInstance();
        int restoreTurn = CurrentTurn;
        MasterDataManager masterData = MasterDataManager.EnsureInstance();
        MagicMasterData spark = null;
        MagicMasterData crystal = null;
        JobMasterData historian = null;
        if (masterData != null)
        {
            masterData.TryGetMagic("MAGIC_FIRE_SPARK", out spark);
            masterData.TryGetMagic("MAGIC_CRYSTAL_LANCE", out crystal);
            historian = masterData.GetJob("JOB_HISTORIAN");
        }

        // 範囲外ターンは IsValidTurn で静かに棄却する（起動時に TrySetCurrentTurn(0) の警告を出さない）
        bool invalidRejected = !IsValidTurn(0) && !IsValidTurn(999) && !IsValidTurn(-5);

        TrySetCurrentTurn(5);
        EraModifier earlyMod = ResolveMagicModifier(spark?.eraSettings);
        ResolvedMagicParameters earlyResolved = MagicCustomizer.Resolve(spark, null);
        bool earlyJobEra = AreEraUnlockFlagsMet(historian, ResolveJobFlag);
        bool earlyJobFull = JobEvolutionManager.AreUnlockRequirementsMet(historian);

        TrySetCurrentTurn(200);
        EraModifier lateMod = ResolveMagicModifier(spark?.eraSettings);
        ResolvedMagicParameters lateResolved = MagicCustomizer.Resolve(spark, null);
        EraModifier missingSettingsLate = ResolveMagicModifier(crystal?.eraSettings);
        bool lateJobEra = AreEraUnlockFlagsMet(historian, ResolveJobFlag);
        bool lateJobFull = JobEvolutionManager.AreUnlockRequirementsMet(historian);

        TrySetCurrentTurn(restoreTurn);

        bool ancientMulInRange = lateMod.ManaCostMultiplier >= AncientMultiplierMin - 0.001f &&
                                 lateMod.ManaCostMultiplier <= AncientMultiplierMax + 0.001f;
        bool magicChanged = !Mathf.Approximately(earlyResolved.EffectiveManaCost, lateResolved.EffectiveManaCost);
        bool jobChanged = earlyJobEra != lateJobEra;
        bool pass = invalidRejected &&
                    earlyMod.Era == EraTag.Early &&
                    lateMod.Era == EraTag.Late &&
                    Mathf.Approximately(earlyMod.ManaCostMultiplier, 1f) &&
                    ancientMulInRange &&
                    magicChanged &&
                    earlyJobEra &&
                    !lateJobEra &&
                    jobChanged &&
                    Mathf.Approximately(missingSettingsLate.ManaCostMultiplier, 1f);

        Debug.Log(
            $"<color=#A5D6A7>【時代変動・検証】T5 {FormatEraLabel(EraTag.Early)} " +
            $"mana×{earlyMod.ManaCostMultiplier:F2}（{earlyResolved.EffectiveManaCost:F1}） " +
            $"cast×{earlyResolved.CastSpeedModifier:F2} jobEra={earlyJobEra} jobFull={earlyJobFull} / " +
            $"T200 {FormatEraLabel(EraTag.Late)} mana×{lateMod.ManaCostMultiplier:F2}（{lateResolved.EffectiveManaCost:F1}） " +
            $"cast×{lateResolved.CastSpeedModifier:F2} jobEra={lateJobEra} jobFull={lateJobFull} / " +
            $"無設定魔法T200×{missingSettingsLate.ManaCostMultiplier:F2} invalidRejected={invalidRejected} " +
            $"→ {(pass ? "PASS" : "CHECK")}</color>");
        return pass;
    }

    private static bool ResolveJobFlag(string flagKey)
    {
        return HistoryFlagRegistry.IsSatisfied(flagKey);
    }

    private static string[] SelectEraUnlockFlags(JobEraSettings settings, EraTag era)
    {
        if (settings == null)
        {
            return null;
        }

        switch (era)
        {
            case EraTag.Mid:
                return settings.midUnlockFlags;
            case EraTag.Late:
                return settings.lateUnlockFlags;
            default:
                return settings.earlyUnlockFlags;
        }
    }

    private static float PositiveOrDefault(float value, float fallback)
    {
        return value > 0f ? value : fallback;
    }
}

/// <summary>Play 開始時に EraContextResolver を配置します。</summary>
public static class EraContextResolverBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoAttach()
    {
        EraContextResolver.EnsureInstance();
    }
}

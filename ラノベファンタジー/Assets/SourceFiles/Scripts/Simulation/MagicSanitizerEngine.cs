using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

// =============================================================================
// 動的生成魔法の安全クランプ — Value / AreaRange / BaseManaCost / 時代補正
// 連携: EraContextResolver / MasterDataManager / MagicMasterData / SpecialEffectTypes
// =============================================================================

/// <summary>シミュレーション中に提案される新魔法パケット（LLM / NPC 要請）。</summary>
[Serializable]
public class GeneratedMagicProposal
{
    public string id;
    public string name;
    public string description;
    public string effectType;
    public string element;
    public string customTraitCode;

    /// <summary>ダメージ / 回復 / 補正倍率（1.0 = 100%）。</summary>
    public float value;

    /// <summary>効果範囲（メートル）。</summary>
    public float areaRange;

    /// <summary>基礎 MP。0 のときは manaCost を使う。</summary>
    public int baseManaCost;

    public int manaCost;
    public float power;
    public float castSpeedModifier = 1f;
    public MagicEraSettings eraSettings;
}

/// <summary>Sanitize 1 件の結果。</summary>
public sealed class MagicSanitizeResult
{
    public bool success;
    public bool usedFallback;
    public bool registered;
    public bool valueClamped;
    public bool areaRangeClamped;
    public bool manaCostClamped;
    public bool eraManaApplied;
    public string fallbackId = string.Empty;
    public string message = string.Empty;
    public MagicMasterData magic;

    public float ClampedValue => magic != null ? magic.value : 0f;
    public float ClampedAreaRange => magic != null ? magic.areaRange : 0f;
    public int ClampedManaCost => magic != null ? magic.manaCost : 0;
}

/// <summary>過剰パラメータ検証の集計。</summary>
public sealed class MagicSanitizerVerifyResult
{
    public bool success;
    public string message = string.Empty;
}

// =============================================================================
// スクルド剪定理論連携: MagicPruningStatus / JobPruningStatus
// SkuldPruningTheoryManager から呼ばれる Safe-Fail ステータス管理
// =============================================================================

/// <summary>剪定理論により管理される Magic のステータス。</summary>
public enum MagicPruningStatus
{
    Active,      // 利用可能
    Pruned,      // 剪定により無効化
    Deprecated,  // 非推奨（効率低下済みだが使用可）
    Unavailable  // 未発見・その他の理由で使用不可
}

/// <summary>剪定理論により管理される Job のステータス。</summary>
public enum JobPruningStatus
{
    Active,
    Pruned,
    Deprecated,
    Unavailable
}

/// <summary>
/// 動的生成・提案マジックを時代上限へクランプし、MasterDataManager へ Safe-Fail 登録します。
/// </summary>
[DefaultExecutionOrder(46)]
public class MagicSanitizerEngine : MonoBehaviour
{
    public const float MaxEffectValue = 3f;
    public const float MaxAreaRangeMeters = 15f;
    public const int MinimumBaseManaCost = 10;
    public const string FallbackWaterId = "MAGIC_WATER_CREATE";
    public const string FallbackFireId = "MAGIC_FIRE_SPARK";

    public static MagicSanitizerEngine Instance { get; private set; }

    private static HashSet<string> knownEffectTypes;

    // 剪定理論ステータス辞書（SkuldPruningTheoryManager と連携）
    private readonly Dictionary<string, MagicPruningStatus> magicPruningStatuses =
        new Dictionary<string, MagicPruningStatus>();
    private readonly Dictionary<string, JobPruningStatus> jobPruningStatuses =
        new Dictionary<string, JobPruningStatus>();
    private readonly Dictionary<string, float> magicEfficiencyModifiers =
        new Dictionary<string, float>();
    private readonly Dictionary<string, float> jobEfficiencyModifiers =
        new Dictionary<string, float>();

    public static MagicSanitizerEngine EnsureInstance()
    {
        if (Instance != null)
        {
            return Instance;
        }

        MagicSanitizerEngine existing = UnityEngine.Object.FindAnyObjectByType<MagicSanitizerEngine>();
        if (existing != null)
        {
            Instance = existing;
            return Instance;
        }

        GameObject hub = GameObject.Find("DebugSystemsHub");
        GameObject host = hub != null ? hub : new GameObject(nameof(MagicSanitizerEngine));
        MagicSanitizerEngine engine = host.GetComponent<MagicSanitizerEngine>();
        return engine != null ? engine : host.AddComponent<MagicSanitizerEngine>();
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

    [ContextMenu("Verify Overpowered Magic Clamp")]
    private void DebugVerifyOverpoweredClamp()
    {
        RunOverpoweredClampVerification();
    }

    /// <summary>提案パケットをクランプしてマスターへ登録します。</summary>
    public static MagicSanitizeResult SanitizeAndRegister(
        GeneratedMagicProposal proposal,
        string sourceLabel = "simulation")
    {
        EnsureInstance();
        MagicSanitizeResult result = Sanitize(proposal);
        result.registered = TryRegisterSafe(result.magic, sourceLabel, result);
        return result;
    }

    /// <summary>既存 MagicMasterData（LLM JSON 直）をクランプして登録します。</summary>
    public static MagicSanitizeResult SanitizeAndRegister(
        MagicMasterData raw,
        string sourceLabel = "simulation")
    {
        return SanitizeAndRegister(FromMaster(raw), sourceLabel);
    }

    /// <summary>登録せずクランプ結果だけ返します。</summary>
    public static MagicSanitizeResult Sanitize(GeneratedMagicProposal proposal)
    {
        MagicSanitizeResult result = new MagicSanitizeResult();
        try
        {
            if (IsFormatBroken(proposal, out string formatReason))
            {
                return ApplyFallback(result, proposal, FallbackFireId, $"フォーマット破壊: {formatReason}");
            }

            if (!IsKnownEffectType(proposal.effectType))
            {
                string fallbackId = ChooseFallbackId(proposal);
                return ApplyFallback(
                    result,
                    proposal,
                    fallbackId,
                    $"未知の effectType '{proposal.effectType}'");
            }

            MagicMasterData magic = BuildFromProposal(proposal);
            ApplyNumericClamps(magic, result);
            ApplyEraManaMultiplier(magic, result);
            NeutralizeEraSettingsToAvoidDoubleApply(magic);
            result.magic = magic;
            result.success = magic.IsValid();
            result.message = BuildClampMessage(magic, result, usedFallback: false);
            LogClamp(result);
            return result;
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[MagicSanitizerEngine] Sanitize Safe-Fail: {exception.Message}");
            return ApplyFallback(result, proposal, FallbackFireId, $"例外: {exception.Message}");
        }
    }

    public static MagicSanitizeResult Sanitize(MagicMasterData raw)
    {
        return Sanitize(FromMaster(raw));
    }

    /// <summary>Value=999 / AreaRange=1000 などを投入し、上限クランプを検証します。</summary>
    public static MagicSanitizerVerifyResult RunOverpoweredClampVerification()
    {
        MagicSanitizerVerifyResult verify = new MagicSanitizerVerifyResult();
        StringBuilder log = new StringBuilder();
        EraContextResolver.EnsureInstance();
        MasterDataManager.EnsureInstance();
        int restoreTurn = EraContextResolver.CurrentTurn;

        try
        {
            EraContextResolver.TrySetCurrentTurn(1);

            MagicSanitizeResult overpowered = SanitizeAndRegister(new GeneratedMagicProposal
            {
                id = "MAGIC_TEST_OVERPOWER",
                name = "破綻核撃",
                description = "検証用の過剰パラメータ",
                effectType = SpecialEffectTypes.BuffStr,
                value = 999f,
                areaRange = 1000f,
                baseManaCost = 0,
                power = 999f
            }, "verify-overpower");

            bool clampPass =
                overpowered.success &&
                overpowered.registered &&
                !overpowered.usedFallback &&
                overpowered.valueClamped &&
                overpowered.areaRangeClamped &&
                overpowered.manaCostClamped &&
                Mathf.Approximately(overpowered.ClampedValue, MaxEffectValue) &&
                Mathf.Approximately(overpowered.ClampedAreaRange, MaxAreaRangeMeters) &&
                overpowered.ClampedManaCost >= MinimumBaseManaCost;

            log.AppendLine(
                $"overpower: success={overpowered.success} registered={overpowered.registered} " +
                $"value {999f}->{overpowered.ClampedValue:0.##} " +
                $"area {1000f}->{overpowered.ClampedAreaRange:0.##} " +
                $"mp 0->{overpowered.ClampedManaCost} " +
                $"pass={clampPass}");
            log.AppendLine($"  {overpowered.message}");

            MagicSanitizeResult unknown = SanitizeAndRegister(new GeneratedMagicProposal
            {
                id = "MAGIC_TEST_UNKNOWN_EFFECT",
                name = "惑星蒸発",
                description = "未知語彙の破綻術",
                effectType = "NUKE_THE_PLANET",
                value = 999f,
                areaRange = 1000f,
                baseManaCost = 1
            }, "verify-unknown");

            bool fallbackPass =
                unknown.success &&
                unknown.registered &&
                unknown.usedFallback &&
                (unknown.fallbackId == FallbackFireId || unknown.fallbackId == FallbackWaterId) &&
                unknown.ClampedValue <= MaxEffectValue + 0.001f &&
                unknown.ClampedAreaRange <= MaxAreaRangeMeters + 0.001f &&
                unknown.ClampedManaCost >= MinimumBaseManaCost;

            log.AppendLine(
                $"unknown: fallback={unknown.usedFallback} id={unknown.magic?.id} " +
                $"fallbackId={unknown.fallbackId} value={unknown.ClampedValue:0.##} " +
                $"area={unknown.ClampedAreaRange:0.##} mp={unknown.ClampedManaCost} pass={fallbackPass}");

            MagicSanitizeResult broken = SanitizeAndRegister((GeneratedMagicProposal)null, "verify-null");
            bool brokenPass =
                broken.success &&
                broken.usedFallback &&
                broken.registered &&
                broken.magic != null &&
                broken.magic.IsValid();
            log.AppendLine(
                $"null: fallback={broken.usedFallback} id={broken.magic?.id} pass={brokenPass}");

            EraContextResolver.TrySetCurrentTurn(200);
            MagicSanitizeResult ancient = SanitizeAndRegister(new GeneratedMagicProposal
            {
                id = "MAGIC_TEST_ANCIENT_COST",
                name = "古代補正検証",
                effectType = SpecialEffectTypes.PsychicRegenMana,
                value = 1.2f,
                areaRange = 2f,
                baseManaCost = 10
            }, "verify-ancient");

            int expectedMin = Mathf.RoundToInt(MinimumBaseManaCost * EraContextResolver.AncientMultiplierMin);
            int expectedMax = Mathf.RoundToInt(MinimumBaseManaCost * EraContextResolver.AncientMultiplierMax);
            bool ancientPass =
                ancient.success &&
                ancient.eraManaApplied &&
                ancient.ClampedManaCost >= expectedMin &&
                ancient.ClampedManaCost <= expectedMax;
            log.AppendLine(
                $"ancient T200: eraApplied={ancient.eraManaApplied} mp={ancient.ClampedManaCost} " +
                $"range={expectedMin}-{expectedMax} pass={ancientPass}");

            verify.success = clampPass && fallbackPass && brokenPass && ancientPass;
            verify.message = log.ToString().TrimEnd();

            Debug.Log(
                verify.success
                    ? $"<color=#A5D6A7><b>【新魔法 Sanitize 検証】PASS</b></color>\n{verify.message}"
                    : $"<color=#FF8A80><b>【新魔法 Sanitize 検証】FAIL</b></color>\n{verify.message}");
        }
        catch (Exception exception)
        {
            verify.success = false;
            verify.message = $"Safe-Fail: {exception.Message}";
            Debug.LogWarning($"[MagicSanitizerEngine] 検証 Safe-Fail: {exception.Message}");
        }
        finally
        {
            EraContextResolver.TrySetCurrentTurn(
                EraContextResolver.IsValidTurn(restoreTurn) ? restoreTurn : EraContextResolver.MinTurn);
        }

        WriteVerifyLog(verify);
        return verify;
    }

    private static bool TryRegisterSafe(MagicMasterData magic, string sourceLabel, MagicSanitizeResult result)
    {
        if (magic == null || !magic.IsValid())
        {
            result.success = false;
            result.message += " / 登録スキップ（無効データ）";
            return false;
        }

        MasterDataManager master = MasterDataManager.EnsureInstance();
        if (master == null)
        {
            Debug.LogWarning("[MagicSanitizerEngine] MasterDataManager を解決できません（Safe-Fail）。");
            result.message += " / MasterDataManager 未解決";
            return false;
        }

        if (IsBuiltInFallbackId(magic.id) &&
            master.TryGetMagic(magic.id, out MagicMasterData existing) &&
            existing != null &&
            existing.IsValid())
        {
            result.magic = existing;
            Debug.Log(
                $"[MagicSanitizerEngine] 組み込みフォールバック {magic.id} は既に登録済みのため上書きしません。");
            result.success = true;
            return true;
        }

        bool registered = master.TryRegisterMagic(magic, $"MagicSanitizer:{sourceLabel}");
        if (registered)
        {
            Debug.Log(
                $"<color=#80CBC4>[MagicSanitizerEngine] 登録 {magic.id} " +
                $"value={magic.value:0.##} area={magic.areaRange:0.##} mp={magic.manaCost}</color>");
        }

        result.success = result.success && registered;
        return registered;
    }

    private static MagicSanitizeResult ApplyFallback(
        MagicSanitizeResult result,
        GeneratedMagicProposal proposal,
        string fallbackId,
        string reason)
    {
        result.usedFallback = true;
        result.fallbackId = fallbackId;
        MagicMasterData magic = CloneFallbackTemplate(fallbackId);

        if (proposal != null && IsUsableGeneratedId(proposal.id) &&
            !IsBuiltInFallbackId(proposal.id))
        {
            magic.id = proposal.id.Trim();
        }

        ApplyNumericClamps(magic, result);
        ApplyEraManaMultiplier(magic, result);
        NeutralizeEraSettingsToAvoidDoubleApply(magic);
        result.magic = magic;
        result.success = magic.IsValid();
        result.message = $"{reason} → {fallbackId} へフォールバック / {BuildClampMessage(magic, result, true)}";
        Debug.LogWarning($"[MagicSanitizerEngine] {result.message}");
        return result;
    }

    private static void ApplyNumericClamps(MagicMasterData magic, MagicSanitizeResult result)
    {
        if (magic == null)
        {
            return;
        }

        float rawValue = ResolveEffectValue(magic);
        float clampedValue = ClampValue(rawValue);
        if (!Mathf.Approximately(rawValue, clampedValue))
        {
            result.valueClamped = true;
            Debug.LogWarning(
                $"[MagicSanitizerEngine] Value をクランプ: {rawValue} → {clampedValue} （上限 {MaxEffectValue}）");
        }

        magic.value = clampedValue;
        magic.power = clampedValue;

        float rawArea = magic.areaRange;
        float clampedArea = ClampAreaRange(rawArea);
        if (!Mathf.Approximately(rawArea, clampedArea))
        {
            result.areaRangeClamped = true;
            Debug.LogWarning(
                $"[MagicSanitizerEngine] AreaRange をクランプ: {rawArea} → {clampedArea} （上限 {MaxAreaRangeMeters}m）");
        }

        magic.areaRange = clampedArea;

        int rawMana = ResolveBaseManaCost(magic);
        int clampedMana = Mathf.Max(MinimumBaseManaCost, rawMana);
        if (clampedMana != rawMana)
        {
            result.manaCostClamped = true;
            Debug.LogWarning(
                $"[MagicSanitizerEngine] BaseManaCost を下限クランプ: {rawMana} → {clampedMana} （最低 {MinimumBaseManaCost}）");
        }

        magic.baseManaCost = clampedMana;
        magic.manaCost = clampedMana;
        magic.castSpeedModifier = magic.castSpeedModifier <= 0f ? 1f : magic.castSpeedModifier;
    }

    private static void ApplyEraManaMultiplier(MagicMasterData magic, MagicSanitizeResult result)
    {
        if (magic == null)
        {
            return;
        }

        EraContextResolver.EnsureInstance();
        EraTag era = EraContextResolver.CurrentEra;
        if (era != EraTag.Late)
        {
            return;
        }

        float multiplier = EraContextResolver.ResolveAncientMultiplier(0f);
        int before = magic.manaCost;
        int after = Mathf.Max(
            MinimumBaseManaCost,
            Mathf.RoundToInt(before * multiplier));
        magic.manaCost = after;
        magic.baseManaCost = after;
        result.eraManaApplied = true;
        Debug.Log(
            $"[MagicSanitizerEngine] 古代化期 MP 補正 x{multiplier:0.00}: {before} → {after} " +
            $"({EraContextResolver.FormatEraLabel(era)} T{EraContextResolver.CurrentTurn})");
    }

    /// <summary>
    /// 登録時に古代 MP を焼き込むため、詠唱時 MagicCustomizer の再乗算を 1.0 に固定します。
    /// </summary>
    private static void NeutralizeEraSettingsToAvoidDoubleApply(MagicMasterData magic)
    {
        if (magic == null)
        {
            return;
        }

        magic.eraSettings = new MagicEraSettings
        {
            earlyManaCostMultiplier = 1f,
            midManaCostMultiplier = 1f,
            lateManaCostMultiplier = 1f,
            earlyCastSpeedMultiplier = 1f,
            midCastSpeedMultiplier = 1f,
            lateCastSpeedMultiplier = 1f
        };
    }

    private static MagicMasterData BuildFromProposal(GeneratedMagicProposal proposal)
    {
        MagicMasterData magic = new MagicMasterData
        {
            id = proposal.id?.Trim() ?? string.Empty,
            name = string.IsNullOrWhiteSpace(proposal.name) ? "未命名の術式" : proposal.name.Trim(),
            description = proposal.description?.Trim() ?? string.Empty,
            effectType = proposal.effectType?.Trim() ?? string.Empty,
            element = proposal.element?.Trim() ?? string.Empty,
            customTraitCode = proposal.customTraitCode?.Trim() ?? string.Empty,
            value = ResolveProposalValue(proposal),
            power = proposal.power,
            areaRange = SanitizeFloat(proposal.areaRange),
            baseManaCost = ResolveProposalMana(proposal),
            manaCost = ResolveProposalMana(proposal),
            castSpeedModifier = proposal.castSpeedModifier <= 0f ? 1f : proposal.castSpeedModifier,
            eraSettings = proposal.eraSettings
        };
        return magic;
    }

    private static GeneratedMagicProposal FromMaster(MagicMasterData raw)
    {
        if (raw == null)
        {
            return null;
        }

        return new GeneratedMagicProposal
        {
            id = raw.id,
            name = raw.name,
            description = raw.description,
            effectType = raw.effectType,
            element = raw.element,
            customTraitCode = raw.customTraitCode,
            value = raw.value,
            power = raw.power,
            areaRange = raw.areaRange,
            baseManaCost = raw.baseManaCost,
            manaCost = raw.manaCost,
            castSpeedModifier = raw.castSpeedModifier,
            eraSettings = raw.eraSettings
        };
    }

    private static bool IsFormatBroken(GeneratedMagicProposal proposal, out string reason)
    {
        if (proposal == null)
        {
            reason = "proposal が null";
            return true;
        }

        if (IsBrokenNumber(proposal.value) ||
            IsBrokenNumber(proposal.power) ||
            IsBrokenNumber(proposal.areaRange) ||
            IsBrokenNumber(proposal.castSpeedModifier))
        {
            reason = "NaN / Infinity";
            return true;
        }

        reason = string.Empty;
        return false;
    }

    private static bool IsKnownEffectType(string effectType)
    {
        if (string.IsNullOrWhiteSpace(effectType))
        {
            return false;
        }

        HashSet<string> known = GetKnownEffectTypes();
        return known.Contains(effectType.Trim());
    }

    private static HashSet<string> GetKnownEffectTypes()
    {
        if (knownEffectTypes != null)
        {
            return knownEffectTypes;
        }

        knownEffectTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        CollectConstStrings(typeof(SpecialEffectTypes), knownEffectTypes);
        CollectConstStrings(typeof(UniqueSkillEffectTypes), knownEffectTypes);
        return knownEffectTypes;
    }

    private static void CollectConstStrings(Type type, HashSet<string> target)
    {
        if (type == null)
        {
            return;
        }

        FieldInfo[] fields = type.GetFields(BindingFlags.Public | BindingFlags.Static);
        for (int i = 0; i < fields.Length; i++)
        {
            FieldInfo field = fields[i];
            if (field.IsLiteral && !field.IsInitOnly && field.FieldType == typeof(string))
            {
                string value = field.GetRawConstantValue() as string;
                if (!string.IsNullOrWhiteSpace(value))
                {
                    target.Add(value);
                }
            }
        }
    }

    private static string ChooseFallbackId(GeneratedMagicProposal proposal)
    {
        string haystack = string.Concat(
            proposal?.effectType,
            " ",
            proposal?.name,
            " ",
            proposal?.description,
            " ",
            proposal?.element);
        if (haystack.IndexOf("水", StringComparison.OrdinalIgnoreCase) >= 0 ||
            haystack.IndexOf("生活", StringComparison.OrdinalIgnoreCase) >= 0 ||
            haystack.IndexOf("回復", StringComparison.OrdinalIgnoreCase) >= 0 ||
            haystack.IndexOf("water", StringComparison.OrdinalIgnoreCase) >= 0 ||
            haystack.IndexOf("heal", StringComparison.OrdinalIgnoreCase) >= 0 ||
            haystack.IndexOf("create", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return FallbackWaterId;
        }

        return FallbackFireId;
    }

    private static MagicMasterData CloneFallbackTemplate(string fallbackId)
    {
        MasterDataManager master = MasterDataManager.EnsureInstance();
        if (master != null &&
            master.TryGetMagic(fallbackId, out MagicMasterData source) &&
            source != null &&
            source.IsValid())
        {
            MagicMasterData clone = CloneMagic(source);
            if (string.IsNullOrWhiteSpace(clone.effectType))
            {
                clone.effectType = fallbackId == FallbackWaterId
                    ? SpecialEffectTypes.PsychicRegenHp
                    : SpecialEffectTypes.BuffPoise;
            }

            return clone;
        }

        return CreateBuiltInFallback(fallbackId);
    }

    private static MagicMasterData CreateBuiltInFallback(string fallbackId)
    {
        if (fallbackId == FallbackWaterId)
        {
            return new MagicMasterData
            {
                id = FallbackWaterId,
                name = "標準生活水生成",
                description = "井戸と結界触媒向けの生活水を凝結する基礎術。",
                element = "水",
                effectType = SpecialEffectTypes.PsychicRegenHp,
                value = 1f,
                power = 1f,
                manaCost = MinimumBaseManaCost,
                baseManaCost = MinimumBaseManaCost,
                areaRange = 1.5f,
                castSpeedModifier = 1f,
                customTraitCode = "CREATE"
            };
        }

        return new MagicMasterData
        {
            id = FallbackFireId,
            name = "基本松明",
            description = "Safe-Fail 用の基礎松明術。",
            element = "火",
            effectType = SpecialEffectTypes.BuffPoise,
            value = 1f,
            power = 1f,
            manaCost = MinimumBaseManaCost,
            baseManaCost = MinimumBaseManaCost,
            areaRange = 3.5f,
            castSpeedModifier = 1f,
            customTraitCode = "SPLASH"
        };
    }

    private static MagicMasterData CloneMagic(MagicMasterData source)
    {
        if (source == null)
        {
            return CreateBuiltInFallback(FallbackFireId);
        }

        try
        {
            return JsonUtility.FromJson<MagicMasterData>(JsonUtility.ToJson(source));
        }
        catch (Exception)
        {
            return CreateBuiltInFallback(FallbackFireId);
        }
    }

    private static float ResolveProposalValue(GeneratedMagicProposal proposal)
    {
        if (proposal == null)
        {
            return 1f;
        }

        if (!Mathf.Approximately(proposal.value, 0f))
        {
            return SanitizeFloat(proposal.value);
        }

        return SanitizeFloat(proposal.power);
    }

    private static float ResolveEffectValue(MagicMasterData magic)
    {
        if (magic == null)
        {
            return 1f;
        }

        if (!Mathf.Approximately(magic.value, 0f))
        {
            return SanitizeFloat(magic.value);
        }

        return SanitizeFloat(magic.power);
    }

    private static int ResolveProposalMana(GeneratedMagicProposal proposal)
    {
        if (proposal == null)
        {
            return 0;
        }

        if (proposal.baseManaCost != 0)
        {
            return proposal.baseManaCost;
        }

        return proposal.manaCost;
    }

    private static int ResolveBaseManaCost(MagicMasterData magic)
    {
        if (magic == null)
        {
            return 0;
        }

        if (magic.baseManaCost != 0)
        {
            return magic.baseManaCost;
        }

        return magic.manaCost;
    }

    private static float ClampValue(float raw)
    {
        float safe = SanitizeFloat(raw);
        if (safe < 0f)
        {
            safe = 0f;
        }

        return Mathf.Min(safe, MaxEffectValue);
    }

    private static float ClampAreaRange(float raw)
    {
        float safe = SanitizeFloat(raw);
        if (safe < 0f)
        {
            safe = 0f;
        }

        return Mathf.Min(safe, MaxAreaRangeMeters);
    }

    private static float SanitizeFloat(float raw)
    {
        if (float.IsNaN(raw) || float.IsInfinity(raw))
        {
            return 0f;
        }

        return raw;
    }

    private static bool IsBrokenNumber(float value)
    {
        return float.IsNaN(value) || float.IsInfinity(value);
    }

    private static bool IsUsableGeneratedId(string id)
    {
        return !string.IsNullOrWhiteSpace(id);
    }

    private static bool IsBuiltInFallbackId(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return false;
        }

        return string.Equals(id.Trim(), FallbackWaterId, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(id.Trim(), FallbackFireId, StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildClampMessage(MagicMasterData magic, MagicSanitizeResult result, bool usedFallback)
    {
        if (magic == null)
        {
            return "magic=null";
        }

        return
            $"id={magic.id} value={magic.value:0.##} area={magic.areaRange:0.##} " +
            $"mp={magic.manaCost} effect={magic.effectType} " +
            $"clamped(value={result.valueClamped}, area={result.areaRangeClamped}, " +
            $"mp={result.manaCostClamped}, era={result.eraManaApplied}) fallback={usedFallback}";
    }

    private static void LogClamp(MagicSanitizeResult result)
    {
        if (result?.magic == null)
        {
            return;
        }

        if (result.valueClamped || result.areaRangeClamped || result.manaCostClamped || result.eraManaApplied)
        {
            Debug.Log($"[MagicSanitizerEngine] {result.message}");
        }
    }

    // =========================================================================
    // スクルド剪定理論 連携メソッド
    // =========================================================================

    /// <summary>
    /// 指定 Magic の剪定ステータスを設定します。
    /// Safe-Fail: 登録済みでなくても辞書に書き込み、警告を出力しません（新規登録扱い）。
    /// </summary>
    public void SetMagicPruningStatus(string magicId, MagicPruningStatus status)
    {
        if (string.IsNullOrWhiteSpace(magicId))
        {
            Debug.LogWarning("[MagicSanitizerEngine] SetMagicPruningStatus: magicId が空です。スキップします。");
            return;
        }

        magicPruningStatuses[magicId] = status;
        Debug.Log(
            $"[MagicSanitizerEngine] Magic '{magicId}' の剪定ステータスを {status} に変更しました。");
    }

    /// <summary>
    /// 指定 Job の剪定ステータスを設定します。
    /// Safe-Fail: 空の ID は警告を出してスキップします。
    /// </summary>
    public void SetJobPruningStatus(string jobId, JobPruningStatus status)
    {
        if (string.IsNullOrWhiteSpace(jobId))
        {
            Debug.LogWarning("[MagicSanitizerEngine] SetJobPruningStatus: jobId が空です。スキップします。");
            return;
        }

        jobPruningStatuses[jobId] = status;
        Debug.Log(
            $"[MagicSanitizerEngine] Job '{jobId}' の剪定ステータスを {status} に変更しました。");
    }

    /// <summary>
    /// 指定 Magic の効率修飾子を設定します（0〜1 にクランプ）。
    /// Safe-Fail: 空の ID は警告を出してスキップします。
    /// </summary>
    public void SetMagicEfficiencyModifier(string magicId, float modifier)
    {
        if (string.IsNullOrWhiteSpace(magicId))
        {
            Debug.LogWarning("[MagicSanitizerEngine] SetMagicEfficiencyModifier: magicId が空です。スキップします。");
            return;
        }

        float clamped = Mathf.Clamp01(modifier);
        magicEfficiencyModifiers[magicId] = clamped;
        Debug.Log(
            $"[MagicSanitizerEngine] Magic '{magicId}' の効率修飾子を {clamped:F2} に設定しました。");
    }

    /// <summary>
    /// 指定 Job の効率修飾子を設定します（0〜1 にクランプ）。
    /// Safe-Fail: 空の ID は警告を出してスキップします。
    /// </summary>
    public void SetJobEfficiencyModifier(string jobId, float modifier)
    {
        if (string.IsNullOrWhiteSpace(jobId))
        {
            Debug.LogWarning("[MagicSanitizerEngine] SetJobEfficiencyModifier: jobId が空です。スキップします。");
            return;
        }

        float clamped = Mathf.Clamp01(modifier);
        jobEfficiencyModifiers[jobId] = clamped;
        Debug.Log(
            $"[MagicSanitizerEngine] Job '{jobId}' の効率修飾子を {clamped:F2} に設定しました。");
    }

    /// <summary>指定 Magic が現在利用可能（Pruned でない）かを返します。</summary>
    public bool IsMagicActive(string magicId)
    {
        if (string.IsNullOrWhiteSpace(magicId))
        {
            return false;
        }

        if (magicPruningStatuses.TryGetValue(magicId, out MagicPruningStatus status))
        {
            return status == MagicPruningStatus.Active || status == MagicPruningStatus.Deprecated;
        }

        return true; // 剪定管理外の Magic はデフォルト利用可能
    }

    /// <summary>指定 Job が現在利用可能（Pruned でない）かを返します。</summary>
    public bool IsJobActive(string jobId)
    {
        if (string.IsNullOrWhiteSpace(jobId))
        {
            return false;
        }

        if (jobPruningStatuses.TryGetValue(jobId, out JobPruningStatus status))
        {
            return status == JobPruningStatus.Active || status == JobPruningStatus.Deprecated;
        }

        return true;
    }

    /// <summary>
    /// 新しい Magic を提案 ID で登録します（剪定理論の IntroduceNew 対応）。
    /// Safe-Fail: MasterDataManager が見つからない場合はログのみ出力してスキップします。
    /// </summary>
    public void ProposeNewMagicById(string magicId)
    {
        if (string.IsNullOrWhiteSpace(magicId))
        {
            Debug.LogWarning("[MagicSanitizerEngine] ProposeNewMagicById: magicId が空です。スキップします。");
            return;
        }

        // HistoryFlagRegistry に解禁フラグを記録
        HistoryFlagRegistry.EnsureWired();
        HistoryFlagRegistry.TryUnlock($"MAGIC_INTRODUCE_{magicId}");

        MasterDataManager master = MasterDataManager.EnsureInstance();
        if (master == null)
        {
            Debug.LogWarning(
                $"[MagicSanitizerEngine] ProposeNewMagicById: MasterDataManager 未解決。" +
                $" {magicId} はフラグ記録のみ実施します。");
            return;
        }

        if (master.TryGetMagic(magicId, out MagicMasterData existing) && existing != null)
        {
            // 既存の場合は剪定ステータスを Active に戻す
            SetMagicPruningStatus(magicId, MagicPruningStatus.Active);
            Debug.Log(
                $"[MagicSanitizerEngine] 新魔法 '{magicId}' は既存のためステータスを Active に復元しました。");
            return;
        }

        // 未登録の場合は Safe-Fail フォールバック提案として登録
        MagicSanitizeResult result = SanitizeAndRegister(new GeneratedMagicProposal
        {
            id = magicId,
            name = magicId.Replace("MAGIC_", string.Empty).Replace("_", " "),
            description = $"剪定理論により解放された新しい社会技術: {magicId}",
            effectType = SpecialEffectTypes.BuffStr,
            value = 1.0f,
            areaRange = 5.0f,
            baseManaCost = MinimumBaseManaCost
        }, $"skuld-introduce");

        Debug.Log(
            $"[MagicSanitizerEngine] 新魔法提案 '{magicId}': " +
            $"success={result.success} fallback={result.usedFallback}");
    }

    private const string VerifyLogPath = "Logs/magic_sanitizer_verify.txt";

    private static void WriteVerifyLog(MagicSanitizerVerifyResult result)
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
        catch (Exception exception)
        {
            Debug.LogWarning($"[MagicSanitizerEngine] 検証ログをスキップ: {exception.Message}");
        }
    }
}

#if UNITY_EDITOR
/// <summary>エディタ専用。本番ビルドには含まれません。</summary>
public static class MagicSanitizerMenu
{
    [MenuItem("Tools/Simulation/Verify Magic Sanitizer (Overpowered Clamp)")]
    public static void VerifyFromMenu()
    {
        MagicSanitizerVerifyResult result = MagicSanitizerEngine.RunOverpoweredClampVerification();
        EditorUtility.DisplayDialog(
            "Magic Sanitizer",
            result.success ? $"PASS\n{result.message}" : $"FAIL\n{result.message}",
            "OK");
    }

    /// <summary>Unity バッチ: -executeMethod MagicSanitizerMenu.BatchVerifyAndQuit</summary>
    public static void BatchVerifyAndQuit()
    {
        MagicSanitizerVerifyResult result = MagicSanitizerEngine.RunOverpoweredClampVerification();
        EditorApplication.Exit(result.success ? 0 : 1);
    }
}
#endif

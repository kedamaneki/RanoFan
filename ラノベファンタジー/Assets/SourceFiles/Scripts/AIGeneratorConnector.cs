using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

#if UNITY_5_3_OR_NEWER
using UnityEngine;
#endif

// =============================================================================
// AI（LLM）生成スキル・クラフト — API通信・JSONパース基盤
// AIGeneratorConnector.cs — モック検証: AIGeneratorConnectorTest.Main()
// =============================================================================

/// <summary>AI 生成コンテンツの種別。</summary>
public enum AIGenerationKind
{
    Skill,
    Craft,
    BaseSkill
}

/// <summary>パース結果の成否。</summary>
public enum AIParseStatus
{
    Success,
    FallbackUsed,
    Failed
}

/// <summary>JsonUtility 用: 効果パラメータ1エントリ。</summary>
[Serializable]
public sealed class EffectParameterEntry
{
    public string key;
    public float value;
}

/// <summary>JsonUtility 用: ユニークスキル DTO。</summary>
[Serializable]
public sealed class GeneratedSkillDataDto
{
    public string skillName;
    public string flavorText;
    public float probability;
    public EffectParameterEntry[] effectParameters;
}

/// <summary>JsonUtility 用: 生産クラフト DTO。</summary>
[Serializable]
public sealed class GeneratedCraftDataDto
{
    public string itemName;
    public string[] processSteps;
    public float qualityBonusOnSuccess;
}

/// <summary>JsonUtility 用: ルートエンベロープ。</summary>
[Serializable]
public sealed class AIGenerationEnvelopeDto
{
    public string generationType;
    public GeneratedSkillDataDto skill;
    public GeneratedCraftDataDto craft;
}

/// <summary>転スラ風ユニークスキル（ランタイムモデル）。</summary>
public sealed class GeneratedSkillData
{
    public string SkillName { get; }
    public string FlavorText { get; }
    public float Probability { get; }
    public IReadOnlyDictionary<string, float> EffectParameters { get; }
    public bool IsFallback { get; }

    public GeneratedSkillData(
        string skillName,
        string flavorText,
        float probability,
        IReadOnlyDictionary<string, float> effectParameters,
        bool isFallback = false)
    {
        SkillName = skillName ?? string.Empty;
        FlavorText = flavorText ?? string.Empty;
        Probability = Math.Max(0f, probability);
        EffectParameters = effectParameters ?? new Dictionary<string, float>();
        IsFallback = isFallback;
    }

    public static GeneratedSkillData FromDto(GeneratedSkillDataDto dto, bool isFallback = false)
    {
        if (dto == null)
        {
            return CreateFallback();
        }

        Dictionary<string, float> map = ConvertEffectParameters(dto.effectParameters);
        return new GeneratedSkillData(
            dto.skillName,
            dto.flavorText,
            dto.probability,
            map,
            isFallback);
    }

    public static GeneratedSkillData CreateFallback()
    {
        return new GeneratedSkillData(
            skillName: "凡庸な一撃",
            flavorText: "AI応答の解析に失敗したため、世界が用意したありふれた技が降りてきた。",
            probability: 0.05f,
            effectParameters: new Dictionary<string, float>
            {
                ["damageMultiplier"] = 1.0f,
                ["staminaCostMultiplier"] = 1.0f
            },
            isFallback: true);
    }

    private static Dictionary<string, float> ConvertEffectParameters(EffectParameterEntry[] entries)
    {
        Dictionary<string, float> map = new Dictionary<string, float>();
        if (entries == null)
        {
            return map;
        }

        for (int i = 0; i < entries.Length; i++)
        {
            EffectParameterEntry entry = entries[i];
            if (entry != null && !string.IsNullOrWhiteSpace(entry.key))
            {
                map[entry.key] = entry.value;
            }
        }

        return map;
    }
}

/// <summary>生産クラフト結果（ランタイムモデル）。</summary>
public sealed class GeneratedCraftData
{
    public string ItemName { get; }
    public IReadOnlyList<string> ProcessSteps { get; }
    public float QualityBonusOnSuccess { get; }
    public bool IsFallback { get; }

    public GeneratedCraftData(
        string itemName,
        IReadOnlyList<string> processSteps,
        float qualityBonusOnSuccess,
        bool isFallback = false)
    {
        ItemName = itemName ?? string.Empty;
        ProcessSteps = processSteps ?? Array.Empty<string>();
        QualityBonusOnSuccess = qualityBonusOnSuccess;
        IsFallback = isFallback;
    }

    public static GeneratedCraftData FromDto(GeneratedCraftDataDto dto, bool isFallback = false)
    {
        if (dto == null)
        {
            return CreateFallback();
        }

        List<string> steps = dto.processSteps != null
            ? new List<string>(dto.processSteps)
            : new List<string>();

        return new GeneratedCraftData(
            dto.itemName,
            steps,
            dto.qualityBonusOnSuccess,
            isFallback);
    }

    public static GeneratedCraftData CreateFallback()
    {
        return new GeneratedCraftData(
            itemName: "粗末な鉄片",
            processSteps: new List<string> { "1. 適当に叩いて形を整える" },
            qualityBonusOnSuccess: 0f,
            isFallback: true);
    }
}

/// <summary>AI 生成リクエストの統合結果。</summary>
public sealed class AIGenerationResult
{
    public AIGenerationKind Kind { get; }
    public AIParseStatus ParseStatus { get; }
    public GeneratedSkillData Skill { get; }
    public GeneratedCraftData Craft { get; }
    public AIGeneratedBaseSkillData BaseSkill { get; }
    public string RawResponse { get; }
    public string ExtractedJson { get; }
    public string DiagnosticMessage { get; }

    public AIGenerationResult(
        AIGenerationKind kind,
        AIParseStatus parseStatus,
        GeneratedSkillData skill,
        GeneratedCraftData craft,
        string rawResponse,
        string extractedJson,
        string diagnosticMessage,
        AIGeneratedBaseSkillData baseSkill = null)
    {
        Kind = kind;
        ParseStatus = parseStatus;
        Skill = skill;
        Craft = craft;
        BaseSkill = baseSkill;
        RawResponse = rawResponse ?? string.Empty;
        ExtractedJson = extractedJson ?? string.Empty;
        DiagnosticMessage = diagnosticMessage ?? string.Empty;
    }
}

/// <summary>
/// LLM 応答から純粋な JSON を抽出し、安全にデシリアライズします。
/// 枕詞・欠損 JSON に耐える堅牢パーサー。
/// </summary>
public static class AIResponseParser
{
    private static readonly Regex MarkdownJsonFenceRegex = new Regex(
        @"```(?:json)?\s*(\{[\s\S]*?\})\s*```",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>生文字列から最初の完全な JSON オブジェクト `{...}` を抽出します。</summary>
    public static string ExtractJsonObject(string rawResponse)
    {
        if (string.IsNullOrWhiteSpace(rawResponse))
        {
            return null;
        }

        Match fenceMatch = MarkdownJsonFenceRegex.Match(rawResponse);
        if (fenceMatch.Success)
        {
            string fenced = fenceMatch.Groups[1].Value;
            string balanced = ExtractBalancedJsonObject(fenced);
            if (!string.IsNullOrWhiteSpace(balanced))
            {
                return balanced;
            }
        }

        return ExtractBalancedJsonObject(rawResponse);
    }

    /// <summary>波括弧のネスト深度で最初のオブジェクトを切り出します（末尾欠損に強い）。</summary>
    public static string ExtractBalancedJsonObject(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        int start = text.IndexOf('{');
        if (start < 0)
        {
            return null;
        }

        int depth = 0;
        bool inString = false;
        bool escape = false;

        for (int i = start; i < text.Length; i++)
        {
            char c = text[i];

            if (inString)
            {
                if (escape)
                {
                    escape = false;
                }
                else if (c == '\\')
                {
                    escape = true;
                }
                else if (c == '"')
                {
                    inString = false;
                }

                continue;
            }

            if (c == '"')
            {
                inString = true;
                continue;
            }

            if (c == '{')
            {
                depth++;
            }
            else if (c == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return text.Substring(start, i - start + 1);
                }
            }
        }

        return null;
    }

    /// <summary>JSON をパースし、失敗時はフォールバックを返します（クラッシュしない）。</summary>
    public static AIGenerationResult ParseGenerationResponse(string rawResponse)
    {
        string extracted = ExtractJsonObject(rawResponse);

        if (string.IsNullOrWhiteSpace(extracted))
        {
            return CreateFallbackResult(
                AIGenerationKind.Skill,
                rawResponse,
                extracted,
                "JSON オブジェクトを抽出できませんでした。");
        }

        try
        {
            AIGenerationEnvelopeDto envelope = JsonDeserializeEnvelope(extracted);
            if (envelope == null)
            {
                return CreateFallbackResult(
                    AIGenerationKind.Skill,
                    rawResponse,
                    extracted,
                    "エンベロープのデシリアライズに失敗しました。");
            }

            AIGenerationKind kind = ResolveKind(envelope.generationType);

            if (kind == AIGenerationKind.Craft)
            {
                GeneratedCraftData craft = GeneratedCraftData.FromDto(envelope.craft);
                if (!IsValidCraft(craft, envelope.craft))
                {
                    return CreateCraftFallback(rawResponse, extracted, "クラフトデータが不完全です。");
                }

                return new AIGenerationResult(
                    AIGenerationKind.Craft,
                    AIParseStatus.Success,
                    null,
                    craft,
                    rawResponse,
                    extracted,
                    "クラフト生成データを正常に解析しました。");
            }

            GeneratedSkillData skill = GeneratedSkillData.FromDto(envelope.skill);
            if (!IsValidSkill(skill, envelope.skill))
            {
                return CreateSkillFallback(rawResponse, extracted, "スキルデータが不完全です。");
            }

            return new AIGenerationResult(
                AIGenerationKind.Skill,
                AIParseStatus.Success,
                skill,
                null,
                rawResponse,
                extracted,
                "ユニークスキルを正常に解析しました。");
        }
        catch (Exception exception)
        {
            return CreateFallbackResult(
                AIGenerationKind.Skill,
                rawResponse,
                extracted,
                $"例外を捕捉: {exception.Message}");
        }
    }

    private static AIGenerationEnvelopeDto JsonDeserializeEnvelope(string json)
    {
#if UNITY_5_3_OR_NEWER
        return JsonUtility.FromJson<AIGenerationEnvelopeDto>(json);
#else
        // スタンドアロン検証用の最小実装（Unity 外ではテスト用モック DTO を手動構築も可）
        return JsonUtilityShim.FromJson<AIGenerationEnvelopeDto>(json);
#endif
    }

    private static AIGenerationKind ResolveKind(string generationType)
    {
        if (string.Equals(generationType, "craft", StringComparison.OrdinalIgnoreCase))
        {
            return AIGenerationKind.Craft;
        }

        if (string.Equals(generationType, "baseSkill", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(generationType, "base_skill", StringComparison.OrdinalIgnoreCase))
        {
            return AIGenerationKind.BaseSkill;
        }

        return AIGenerationKind.Skill;
    }

    private static bool IsValidSkill(GeneratedSkillData skill, GeneratedSkillDataDto dto)
    {
        return dto != null &&
               !string.IsNullOrWhiteSpace(dto.skillName) &&
               skill != null &&
               !skill.IsFallback;
    }

    private static bool IsValidCraft(GeneratedCraftData craft, GeneratedCraftDataDto dto)
    {
        return dto != null &&
               !string.IsNullOrWhiteSpace(dto.itemName) &&
               craft != null &&
               !craft.IsFallback;
    }

    private static AIGenerationResult CreateSkillFallback(
        string raw,
        string extracted,
        string reason)
    {
        return new AIGenerationResult(
            AIGenerationKind.Skill,
            AIParseStatus.FallbackUsed,
            GeneratedSkillData.CreateFallback(),
            null,
            raw,
            extracted ?? string.Empty,
            reason);
    }

    private static AIGenerationResult CreateCraftFallback(
        string raw,
        string extracted,
        string reason)
    {
        return new AIGenerationResult(
            AIGenerationKind.Craft,
            AIParseStatus.FallbackUsed,
            null,
            GeneratedCraftData.CreateFallback(),
            raw,
            extracted ?? string.Empty,
            reason);
    }

    private static AIGenerationResult CreateFallbackResult(
        AIGenerationKind kind,
        string raw,
        string extracted,
        string reason)
    {
        return kind == AIGenerationKind.Craft
            ? CreateCraftFallback(raw, extracted, reason)
            : CreateSkillFallback(raw, extracted, reason);
    }
}

#if !UNITY_5_3_OR_NEWER
/// <summary>Unity 外コンソール実行用の JsonUtility 代替（最小限）。</summary>
internal static class JsonUtilityShim
{
    // スタンドアロン Main ではモックが直接 DTO を返す経路を使うため未使用でも可。
    public static T FromJson<T>(string json) where T : new()
    {
        throw new NotSupportedException("Unity 外では AIGeneratorConnectorTest のモック経路を使用してください。");
    }
}
#endif

/// <summary>生成コンテンツをゲーム内へ登録するスタブ（ログ出力）。</summary>
public static class GameContentRegistry
{
    public static void RegisterSkill(GeneratedSkillData skill, Action<string> log)
    {
        if (skill == null)
        {
            log?.Invoke("[Registry] スキル登録失敗: null");
            return;
        }

        StringBuilder sb = new StringBuilder();
        sb.Append(skill.IsFallback ? "[Registry] フォールバックスキル登録: " : "[Registry] ユニークスキル登録: ");
        sb.Append(skill.SkillName);
        sb.Append($"（確率 {skill.Probability:F5}）");
        sb.AppendLine();
        sb.Append($"  フレーバー: {skill.FlavorText}");

        foreach (KeyValuePair<string, float> pair in skill.EffectParameters)
        {
            sb.AppendLine();
            sb.Append($"  効果 {pair.Key} = {pair.Value:F2}");
        }

        log?.Invoke(sb.ToString());
    }

    public static void RegisterCraft(GeneratedCraftData craft, Action<string> log)
    {
        if (craft == null)
        {
            log?.Invoke("[Registry] クラフト登録失敗: null");
            return;
        }

        StringBuilder sb = new StringBuilder();
        sb.Append(craft.IsFallback ? "[Registry] フォールバック素材登録: " : "[Registry] クラフト成果登録: ");
        sb.Append(craft.ItemName);
        sb.Append($"（品質補正 +{craft.QualityBonusOnSuccess:F1}）");

        for (int i = 0; i < craft.ProcessSteps.Count; i++)
        {
            sb.AppendLine();
            sb.Append($"  工程: {craft.ProcessSteps[i]}");
        }

        log?.Invoke(sb.ToString());
    }
}

/// <summary>
/// LLM API との通信コネクター（現段階はモック実装）。
/// 非同期 API を提供し、将来 HttpClient 等に差し替え可能です。
/// </summary>
public sealed class AIGeneratorConnector
{
    private readonly IAIGenerationBackend backend;

    public AIGeneratorConnector(IAIGenerationBackend backend = null)
    {
        this.backend = backend ?? new MockAIGenerationBackend();
    }

    /// <summary>プレイヤーログを送信し、生成結果を非同期で取得・パースします。</summary>
    public async Task<AIGenerationResult> RequestAIGenerationAsync(
        string playerLogInput,
        CancellationToken cancellationToken = default,
        Func<string, AIGenerationResult> parseFunc = null)
    {
        if (playerLogInput == null)
        {
            playerLogInput = string.Empty;
        }

        string rawResponse = await backend.FetchRawResponseAsync(playerLogInput, cancellationToken)
            .ConfigureAwait(false);

        return await UnityMainThreadAwaiter.RunOnMainThreadAsync(
            () => ParseAndCollectResponse(rawResponse, parseFunc, cancellationToken),
            cancellationToken);
    }

    private static AIGenerationResult ParseAndCollectResponse(
        string rawResponse,
        Func<string, AIGenerationResult> parseFunc,
        CancellationToken cancellationToken)
    {
        AIGenerationResult result = parseFunc != null
            ? parseFunc(rawResponse)
            : AIResponseParser.ParseGenerationResponse(rawResponse);

        DataCollectionSystem.TryCollectParsedResultAsync(result, cancellationToken: cancellationToken)
            .GetAwaiter()
            .GetResult();

        return result;
    }

#if UNITY_5_3_OR_NEWER
    /// <summary>Unity コルーチンから呼び出すためのラッパー。</summary>
    public IEnumerator RequestAIGenerationCoroutine(
        string playerLogInput,
        Action<AIGenerationResult> onCompleted)
    {
        Task<AIGenerationResult> task = RequestAIGenerationAsync(playerLogInput);
        while (!task.IsCompleted)
        {
            yield return null;
        }

        if (task.IsFaulted)
        {
            onCompleted?.Invoke(AIResponseParser.ParseGenerationResponse(string.Empty));
        }
        else
        {
            onCompleted?.Invoke(task.Result);
        }
    }
#endif
}

/// <summary>バックエンド API の抽象（本番は OpenAI 等に差し替え）。</summary>
public interface IAIGenerationBackend
{
    Task<string> FetchRawResponseAsync(string playerLogInput, CancellationToken cancellationToken);
}

/// <summary>検証用モック API。入力プレフィックスで正常／異常応答を切り替えます。</summary>
public sealed class MockAIGenerationBackend : IAIGenerationBackend
{
    public const string SuccessToken = "[MOCK_SUCCESS]";
    public const string FailureToken = "[MOCK_FAILURE]";

    public Task<string> FetchRawResponseAsync(string playerLogInput, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        bool simulateFailure = playerLogInput != null &&
                               playerLogInput.IndexOf(FailureToken, StringComparison.Ordinal) >= 0;

        string response = simulateFailure
            ? BuildBrokenResponse()
            : BuildCleanSkillResponse();

        return Task.FromResult(response);
    }

    public static string BuildCleanSkillResponse()
    {
        return
            "はい、生成しました！転スラ風のスキルはこちらです：\n" +
            "```json\n" +
            "{\n" +
            "  \"generationType\": \"skill\",\n" +
            "  \"skill\": {\n" +
            "    \"skillName\": \"虚空之眼\",\n" +
            "    \"flavorText\": \"視線の先の存在律をねじ伏せ、万物の輪郭を溶かす禁忌の瞳。\",\n" +
            "    \"probability\": 0.00001,\n" +
            "    \"effectParameters\": [\n" +
            "      { \"key\": \"damageMultiplier\", \"value\": 3.5 },\n" +
            "      { \"key\": \"staminaCostMultiplier\", \"value\": 0.7 }\n" +
            "    ]\n" +
            "  }\n" +
            "}\n" +
            "```\n" +
            "ご活用ください！";
    }

    public static string BuildBrokenResponse()
    {
        return
            "了解です！以下がプレイヤーログに基づく生成結果ですね：\n" +
            "{\n" +
            "  \"generationType\": \"skill\",\n" +
            "  \"skill\": {\n" +
            "    \"skillName\": \"万物融解\",\n" +
            "    \"flavorText\": \"未完の応答——\n" +
            "    \"probability\": 0.00002,\n" +
            "    \"effectParameters\": [\n" +
            "      { \"key\": \"damageMultiplier\", \"value\": 4.0\n" +
            "    ]\n" +
            "  }\n";
    }
}

/// <summary>モック実行・パース検証用エントリポイント。</summary>
public static class AIGeneratorConnectorTest
{
    public static void Main()
    {
        RunAllTestsAsync().GetAwaiter().GetResult();
    }

    public static async Task RunAllTestsAsync(Action<string> log = null)
    {
        log ??= Console.WriteLine;

        log("===== AIGeneratorConnectorTest 開始 =====");
        await RunSuccessCaseAsync(log);
        await RunFailureFallbackCaseAsync(log);
        log("===== AIGeneratorConnectorTest 完了 =====");
    }

    private static async Task RunSuccessCaseAsync(Action<string> log)
    {
        log(string.Empty);
        log("--- 正常系: 綺麗な JSON（枕詞付き）からユニークスキル登録 ---");

        AIGeneratorConnector connector = new AIGeneratorConnector(new MockAIGenerationBackend());
        AIGenerationResult result = await connector.RequestAIGenerationAsync(
            $"{MockAIGenerationBackend.SuccessToken} player_log=双剣連撃×50, boss_part_break");

        log($"  パース状態: {result.ParseStatus}");
        log($"  抽出 JSON 長: {result.ExtractedJson?.Length ?? 0} 文字");

        if (result.Skill != null)
        {
            GameContentRegistry.RegisterSkill(result.Skill, log);
        }

        AssertScenario(
            log,
            "正常系",
            result.ParseStatus == AIParseStatus.Success &&
            result.Skill != null &&
            result.Skill.SkillName == "虚空之眼" &&
            !result.Skill.IsFallback,
            $"スキル「{result.Skill?.SkillName}」を登録");
    }

    private static async Task RunFailureFallbackCaseAsync(Action<string> log)
    {
        log(string.Empty);
        log("--- 異常系: 雑談混じり・構文欠損 JSON → フォールバック ---");

        AIGeneratorConnector connector = new AIGeneratorConnector(new MockAIGenerationBackend());
        AIGenerationResult result = await connector.RequestAIGenerationAsync(
            $"{MockAIGenerationBackend.FailureToken} player_log=泥仕合150hit");

        log($"  パース状態: {result.ParseStatus}");
        log($"  診断: {result.DiagnosticMessage}");

        if (result.Skill != null)
        {
            GameContentRegistry.RegisterSkill(result.Skill, log);
        }

        AssertScenario(
            log,
            "異常系フォールバック",
            result.ParseStatus == AIParseStatus.FallbackUsed &&
            result.Skill != null &&
            result.Skill.IsFallback,
            $"代替スキル「{result.Skill?.SkillName}」でクラッシュ回避");
    }

    private static void AssertScenario(Action<string> log, string label, bool condition, string detail)
    {
#if UNITY_5_3_OR_NEWER
        log(condition
            ? $"  ✓ [{label} OK] {detail}"
            : $"  ✗ [{label} NG] {detail}");
#else
        if (!condition)
        {
            throw new InvalidOperationException($"{label} failed: {detail}");
        }

        log($"  ✓ [{label} OK] {detail}");
#endif
    }

#if UNITY_5_3_OR_NEWER
    public static void RunInUnity()
    {
        RunAllTestsAsync(Debug.Log).GetAwaiter().GetResult();
    }
#endif
}

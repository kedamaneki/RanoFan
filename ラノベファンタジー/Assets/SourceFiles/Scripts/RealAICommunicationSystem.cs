using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

#if UNITY_5_3_OR_NEWER
using UnityEngine;
#endif

// =============================================================================
// 本物の LLM 通信（Gemini API）× パース × 融合返還ロールバック
// RealAICommunicationSystemTest.Main() で検証。
// 連携: AIGeneratorConnector / AIResponseParser / GamePhasePromptBuilder / SkillFusionTransactionSystem
// =============================================================================

/// <summary>Gemini API 通信時の HTTP 例外。</summary>
public sealed class RealAIHttpException : Exception
{
    public int StatusCode { get; }

    public RealAIHttpException(string message, int statusCode)
        : base(message)
    {
        StatusCode = statusCode;
    }
}

/// <summary>JsonUtility 用: Gemini generateContent 応答。</summary>
[Serializable]
public sealed class GeminiGenerateContentResponseDto
{
    public GeminiCandidateDto[] candidates;
    public GeminiErrorDto error;
}

[Serializable]
public sealed class GeminiCandidateDto
{
    public GeminiContentDto content;
}

[Serializable]
public sealed class GeminiContentDto
{
    public GeminiPartDto[] parts;
}

[Serializable]
public sealed class GeminiPartDto
{
    public string text;
}

[Serializable]
public sealed class GeminiErrorDto
{
    public int code;
    public string message;
    public string status;
}

/// <summary>JsonUtility 用: 合成スキル（メリット/デメリット付き）DTO。</summary>
[Serializable]
public sealed class GeneratedCompositeSkillDataDto
{
    public string skillName;
    public string meritEffect;
    public string demeritEffect;
    public string flavorText;
    public float probability;
    public EffectParameterEntry[] effectParameters;
}

/// <summary>JsonUtility 用: compositeSkill を含むエンベロープ。</summary>
[Serializable]
public sealed class AIGenerationEnvelopeWithCompositeDto
{
    public string generationType;
    public GeneratedSkillDataDto skill;
    public GeneratedCompositeSkillDataDto compositeSkill;
    public GeneratedCraftDataDto craft;
}

/// <summary>実 API 通信結果（合成スキル情報を保持可能）。</summary>
public sealed class RealAIGenerationOutcome
{
    public AIGenerationResult ParseResult { get; }
    public CompositeSkillData CompositeSkill { get; }
    public string RawGeminiText { get; }

    public RealAIGenerationOutcome(
        AIGenerationResult parseResult,
        CompositeSkillData compositeSkill,
        string rawGeminiText)
    {
        ParseResult = parseResult;
        CompositeSkill = compositeSkill;
        RawGeminiText = rawGeminiText ?? string.Empty;
    }
}

/// <summary>
/// 合成スキル（compositeSkill）を含む応答を標準パーサーへ橋渡しします。
/// </summary>
public static class RealAIGameResponseParser
{
    public static AIGenerationResult ParseGenerationResponse(string rawResponse)
    {
        string extracted = AIResponseParser.ExtractJsonObject(rawResponse);

        if (!string.IsNullOrWhiteSpace(extracted))
        {
            AIGenerationEnvelopeWithCompositeDto envelope =
                JsonDeserialize<AIGenerationEnvelopeWithCompositeDto>(extracted);

            if (envelope?.compositeSkill != null &&
                !string.IsNullOrWhiteSpace(envelope.compositeSkill.skillName))
            {
                GeneratedSkillData skill = MapCompositeToSkill(envelope.compositeSkill);
                return new AIGenerationResult(
                    AIGenerationKind.Skill,
                    AIParseStatus.Success,
                    skill,
                    null,
                    rawResponse,
                    extracted,
                    "合成スキル（メリット/デメリット付き）を正常に解析しました。");
            }

            AIGeneratedBaseSkillData baseSkill = BaseSkillResponseParser.TryParseFromExtractedJson(extracted);
            if (baseSkill != null && baseSkill.IsValid())
            {
                return new AIGenerationResult(
                    AIGenerationKind.BaseSkill,
                    AIParseStatus.Success,
                    null,
                    null,
                    rawResponse,
                    extracted,
                    "基礎スキル（BaseSkill）を正常に解析しました。",
                    baseSkill);
            }
        }

        return AIResponseParser.ParseGenerationResponse(rawResponse);
    }

    public static CompositeSkillData TryExtractCompositeSkill(string rawResponse)
    {
        string extracted = AIResponseParser.ExtractJsonObject(rawResponse);
        if (string.IsNullOrWhiteSpace(extracted))
        {
            return null;
        }

        AIGenerationEnvelopeWithCompositeDto envelope =
            JsonDeserialize<AIGenerationEnvelopeWithCompositeDto>(extracted);

        if (envelope?.compositeSkill == null ||
            string.IsNullOrWhiteSpace(envelope.compositeSkill.skillName))
        {
            return null;
        }

        GeneratedCompositeSkillDataDto dto = envelope.compositeSkill;
        return new CompositeSkillData
        {
            SkillName = dto.skillName,
            MeritEffect = dto.meritEffect ?? string.Empty,
            DemeritEffect = dto.demeritEffect ?? string.Empty
        };
    }

    private static GeneratedSkillData MapCompositeToSkill(GeneratedCompositeSkillDataDto dto)
    {
        Dictionary<string, float> map = new Dictionary<string, float>();
        if (dto.effectParameters != null)
        {
            for (int i = 0; i < dto.effectParameters.Length; i++)
            {
                EffectParameterEntry entry = dto.effectParameters[i];
                if (entry != null && !string.IsNullOrWhiteSpace(entry.key))
                {
                    map[entry.key] = entry.value;
                }
            }
        }

        string flavor =
            $"{dto.flavorText}\n【メリット】{dto.meritEffect}\n【デメリット】{dto.demeritEffect}";

        return new GeneratedSkillData(
            dto.skillName,
            flavor,
            dto.probability,
            map);
    }

    private static T JsonDeserialize<T>(string json) where T : class
    {
#if UNITY_5_3_OR_NEWER
        return JsonUtility.FromJson<T>(json);
#else
        throw new NotSupportedException("Unity 環境で実行してください。");
#endif
    }
}

/// <summary>
/// LocalSecrets/gemini-api-key.txt から API キーを読み込みます（Git 管理外）。
/// </summary>
public static class GeminiApiKeyStore
{
    public const string SecretFolderName = "LocalSecrets";
    public const string SecretFileName = "gemini-api-key.txt";
    public const string ExampleFileName = "gemini-api-key.txt.example";

    private static string cachedApiKey;
    private static bool cacheLoaded;

    public static string GetSecretFilePath()
    {
#if UNITY_5_3_OR_NEWER
        string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        return Path.Combine(projectRoot, SecretFolderName, SecretFileName);
#else
        return Path.GetFullPath(Path.Combine(SecretFolderName, SecretFileName));
#endif
    }

    public static bool IsApiKeyConfigured => TryLoadApiKey(out _);

    public static bool TryLoadApiKey(out string apiKey)
    {
        if (cacheLoaded)
        {
            apiKey = cachedApiKey;
            return IsValidApiKey(apiKey);
        }

        cacheLoaded = true;
        cachedApiKey = ReadApiKeyFromFile(GetSecretFilePath());
        apiKey = cachedApiKey;
        return IsValidApiKey(apiKey);
    }

    public static void ClearCache()
    {
        cacheLoaded = false;
        cachedApiKey = null;
    }

    private static bool IsValidApiKey(string apiKey)
    {
        return !string.IsNullOrWhiteSpace(apiKey) &&
               !string.Equals(apiKey.Trim(), "YOUR_API_KEY", StringComparison.Ordinal);
    }

    private static string ReadApiKeyFromFile(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return null;
        }

        string[] lines = File.ReadAllLines(filePath);
        StringBuilder builder = new StringBuilder();

        for (int i = 0; i < lines.Length; i++)
        {
            string trimmed = lines[i].Trim();
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#", StringComparison.Ordinal))
            {
                continue;
            }

            builder.Append(trimmed);
        }

        return builder.Length > 0 ? builder.ToString() : null;
    }
}

/// <summary>
/// HttpClient を用いた Gemini API (v1beta) 非同期通信クライアント。
/// API キーはプロジェクト直下の LocalSecrets/gemini-api-key.txt から読み込みます。
/// </summary>
public sealed class RealAIHttpClient : IAIGenerationBackend, IDisposable
{
    private const string ModelId = "gemini-2.5-flash";
    public const int DefaultTimeoutSeconds = 90;

    private static readonly HttpClient SharedHttpClient = CreateSharedClient();
    private bool disposed;

    public static bool IsApiKeyConfigured => GeminiApiKeyStore.IsApiKeyConfigured;

    public static string BuildEndpointUrl(string apiKey) =>
        $"https://generativelanguage.googleapis.com/v1beta/models/{ModelId}:generateContent?key={apiKey}";

    public Task<string> FetchRawResponseAsync(string playerLogInput, CancellationToken cancellationToken)
    {
        return FetchGeminiTextAsync(playerLogInput, cancellationToken);
    }

    /// <summary>プロンプトを Gemini API へ POST し、生成テキストを返します。</summary>
    public async Task<string> FetchGeminiTextAsync(string prompt, CancellationToken cancellationToken = default)
    {
        if (!GeminiApiKeyStore.TryLoadApiKey(out string apiKey))
        {
            throw new InvalidOperationException(
                "Gemini API キーが未設定です。\n" +
                $"  1. {GeminiApiKeyStore.SecretFolderName}/{GeminiApiKeyStore.ExampleFileName} を\n" +
                $"     {GeminiApiKeyStore.SecretFolderName}/{GeminiApiKeyStore.SecretFileName} にコピー\n" +
                "  2. 1行目に API キーを貼り付け（# から始まる行はコメント）\n" +
                $"  期待パス: {GeminiApiKeyStore.GetSecretFilePath()}");
        }

        if (prompt == null)
        {
            prompt = string.Empty;
        }

        string payload = BuildGeminiRequestPayload(prompt);
        string endpointUrl = BuildEndpointUrl(apiKey);

        using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, endpointUrl);
        request.Content = new StringContent(payload, Encoding.UTF8, "application/json");

        HttpResponseMessage response;
        try
        {
            response = await SharedHttpClient
                .SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new RealAIHttpException(
                $"通信タイムアウト（{DefaultTimeoutSeconds}秒）: {exception.Message}",
                0);
        }
        catch (HttpRequestException exception)
        {
            throw new RealAIHttpException($"HTTP 通信エラー: {exception.Message}", 0);
        }

        string responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            int statusCode = (int)response.StatusCode;
            throw new RealAIHttpException(
                $"HTTP {statusCode} ({response.ReasonPhrase}): {TrimForLog(responseBody, 500)}",
                statusCode);
        }

        string text = ExtractGeminiText(responseBody);
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new RealAIHttpException(
                "Gemini 応答から text を抽出できませんでした。",
                (int)HttpStatusCode.OK);
        }

        return text;
    }

    public static string BuildGeminiRequestPayload(string prompt)
    {
        StringBuilder sb = new StringBuilder();
        sb.Append("{\"contents\":[{\"parts\":[{\"text\":");
        sb.Append(JsonEscape(prompt));
        sb.Append("}]}]}");
        return sb.ToString();
    }

    public static string ExtractGeminiText(string responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return null;
        }

        GeminiGenerateContentResponseDto dto = JsonDeserialize<GeminiGenerateContentResponseDto>(responseBody);
        if (dto?.error != null && !string.IsNullOrWhiteSpace(dto.error.message))
        {
            throw new RealAIHttpException(
                $"Gemini API エラー {dto.error.code} ({dto.error.status}): {dto.error.message}",
                dto.error.code);
        }

        if (dto?.candidates == null || dto.candidates.Length == 0)
        {
            return null;
        }

        GeminiContentDto content = dto.candidates[0].content;
        if (content?.parts == null || content.parts.Length == 0)
        {
            return null;
        }

        StringBuilder textBuilder = new StringBuilder();
        for (int i = 0; i < content.parts.Length; i++)
        {
            GeminiPartDto part = content.parts[i];
            if (part != null && !string.IsNullOrEmpty(part.text))
            {
                textBuilder.Append(part.text);
            }
        }

        return textBuilder.Length > 0 ? textBuilder.ToString() : null;
    }

    private static HttpClient CreateSharedClient()
    {
        HttpClient client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(DefaultTimeoutSeconds)
        };

        client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json");
        return client;
    }

    private static string JsonEscape(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "\"\"";
        }

        StringBuilder sb = new StringBuilder(value.Length + 16);
        sb.Append('"');

        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            switch (c)
            {
                case '\\': sb.Append("\\\\"); break;
                case '"': sb.Append("\\\""); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default: sb.Append(c); break;
            }
        }

        sb.Append('"');
        return sb.ToString();
    }

    private static string TrimForLog(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
        {
            return text ?? string.Empty;
        }

        return text.Substring(0, maxLength) + "...";
    }

    private static T JsonDeserialize<T>(string json) where T : class
    {
#if UNITY_5_3_OR_NEWER
        return JsonUtility.FromJson<T>(json);
#else
        throw new NotSupportedException("Unity 環境で実行してください。");
#endif
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
    }
}

/// <summary>
/// 実 API 通信・パース・結果表示を統合するコネクターサービス。
/// </summary>
public sealed class RealAICommunicationService : IDisposable
{
    private readonly AIGeneratorConnector connector;
    private readonly RealAIHttpClient httpClient;
    private readonly bool ownsHttpClient;

    public RealAICommunicationService(RealAIHttpClient httpClient = null)
    {
        this.httpClient = httpClient ?? new RealAIHttpClient();
        ownsHttpClient = httpClient == null;
        connector = new AIGeneratorConnector(this.httpClient);
    }

    public async Task<RealAIGenerationOutcome> GenerateAsync(
        string prompt,
        CancellationToken cancellationToken = default)
    {
        string rawText = await httpClient
            .FetchGeminiTextAsync(prompt, cancellationToken)
            .ConfigureAwait(false);

        return await UnityMainThreadAwaiter.RunOnMainThreadAsync(
            () => BuildGenerationOutcome(rawText, cancellationToken),
            cancellationToken);
    }

    private static RealAIGenerationOutcome BuildGenerationOutcome(
        string rawText,
        CancellationToken cancellationToken)
    {
        AIGenerationResult parsed = RealAIGameResponseParser.ParseGenerationResponse(rawText);
        CompositeSkillData composite = RealAIGameResponseParser.TryExtractCompositeSkill(rawText);

        DataCollectionSystem.TryCollectParsedResultAsync(parsed, cancellationToken: cancellationToken)
            .GetAwaiter()
            .GetResult();

        return new RealAIGenerationOutcome(parsed, composite, rawText);
    }

    public async Task<SkillFusionResult> TryFusionWithGamePhasePromptAsync(
        PlayerSkillInventory inventory,
        IReadOnlyList<SacrificeSkillData> sacrificeSkills,
        PlayerHistoryLog history,
        Action<string> log = null,
        CancellationToken cancellationToken = default)
    {
        List<SkillData> skillDataSacrifices = ConvertToSkillDataList(sacrificeSkills);

        string fusionPrompt = PromptBuilder.Build(
            GamePhase.Fusion,
            history,
            skillDataSacrifices,
            additionalIntent: "メリットとデメリットのペアを必ず数値付きで含めよ。");

        SkillFusionTransactionSystem fusionSystem = new SkillFusionTransactionSystem(
            inventory,
            connector);

        return await fusionSystem.TrySkillFusionWithPromptAsync(
            fusionPrompt,
            sacrificeSkills,
            log,
            cancellationToken,
            RealAIGameResponseParser.ParseGenerationResponse).ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (ownsHttpClient)
        {
            httpClient?.Dispose();
        }
    }

    private static List<SkillData> ConvertToSkillDataList(IReadOnlyList<SacrificeSkillData> sacrifices)
    {
        List<SkillData> list = new List<SkillData>();
        if (sacrifices == null)
        {
            return list;
        }

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
}

/// <summary>モック実行・実 API 通信検証用エントリポイント。</summary>
public static class RealAICommunicationSystemTest
{
    public static void Main()
    {
        RunAllTestsAsync().GetAwaiter().GetResult();
    }

    public static async Task RunAllTestsAsync(Action<string> log = null)
    {
        log ??= GetDefaultLogger();

        log("===== RealAICommunicationSystemTest 開始 =====");

        if (!RealAIHttpClient.IsApiKeyConfigured)
        {
            log("【スキップ】Gemini API キーが未設定です。");
            log($"  {GeminiApiKeyStore.SecretFolderName}/{GeminiApiKeyStore.ExampleFileName} を");
            log($"  {GeminiApiKeyStore.SecretFolderName}/{GeminiApiKeyStore.SecretFileName} にコピーし、API キーを1行で記載してください。");
            log($"  期待パス: {GeminiApiKeyStore.GetSecretFilePath()}");
            log("===== RealAICommunicationSystemTest 完了（未実行）=====");
            return;
        }

        using RealAICommunicationService service = new RealAICommunicationService();

        await RunCrisisUniqueSkillLiveTestAsync(service, log).ConfigureAwait(false);
        await RunFusionTradeoffWithRollbackLiveTestAsync(service, log).ConfigureAwait(false);

        log("===== RealAICommunicationSystemTest 完了 =====");
    }

    private static async Task RunCrisisUniqueSkillLiveTestAsync(
        RealAICommunicationService service,
        Action<string> log)
    {
        log(string.Empty);
        log("--- 実通信検証：Crisis モード（転スラ風ユニークスキル）---");

        PlayerHistoryLog history = new PlayerHistoryLog
        {
            TotalSlashHits = 1500,
            TotalStrikeHits = 400,
            TotalThrustHits = 220,
            TotalPerfectEvades = 55,
            PreferredStatAllocation = "MND極振り"
        };

        CurrentBattleContext crisisContext = new CurrentBattleContext
        {
            EnemyRank = EnemyRank.Boss,
            IsCrisisAwakeningTriggered = true
        };

        string crisisPrompt = PromptBuilder.Build(
            GamePhase.Crisis,
            history,
            crisisContext: crisisContext,
            additionalIntent: "死線の覚醒として、漢字3〜4文字の重厚なユニークスキルを1つ生成せよ。");

        try
        {
            RealAIGenerationOutcome outcome = await service
                .GenerateAsync(crisisPrompt)
                .ConfigureAwait(false);

            log($"  パース状態: {outcome.ParseResult.ParseStatus}");
            log($"  診断: {outcome.ParseResult.DiagnosticMessage}");
            log($"  抽出 JSON:\n{outcome.ParseResult.ExtractedJson}");

            if (outcome.ParseResult.Skill != null)
            {
                GameContentRegistry.RegisterSkill(outcome.ParseResult.Skill, log);
            }

            AssertScenario(
                log,
                "Crisis実通信",
                outcome.ParseResult.ParseStatus == AIParseStatus.Success &&
                outcome.ParseResult.Skill != null &&
                !outcome.ParseResult.Skill.IsFallback,
                $"ユニークスキル「{outcome.ParseResult.Skill?.SkillName}」");
        }
        catch (RealAIHttpException exception)
        {
            log($"  ✗ HTTP/Gemini エラー (status={exception.StatusCode}): {exception.Message}");
        }
        catch (Exception exception)
        {
            log($"  ✗ 例外: {exception.Message}");
        }
    }

    private static async Task RunFusionTradeoffWithRollbackLiveTestAsync(
        RealAICommunicationService service,
        Action<string> log)
    {
        log(string.Empty);
        log("--- 実通信検証：Fusion モード（デメリット付き合成スキル + 失敗時返還）---");

        PlayerSkillInventory inventory = new PlayerSkillInventory();
        List<SacrificeSkillData> sacrifices = new List<SacrificeSkillData>
        {
            new SacrificeSkillData("skill_fire_spark", "火の粉"),
            new SacrificeSkillData("skill_one_hand_sword", "片手剣術")
        };
        inventory.AddOwnedSkills(sacrifices);

        PlayerHistoryLog history = new PlayerHistoryLog
        {
            TotalSlashHits = 600,
            TotalStrikeHits = 250,
            TotalThrustHits = 90,
            TotalPerfectEvades = 20,
            PreferredStatAllocation = "STR+MND"
        };

        try
        {
            SkillFusionResult fusionResult = await service.TryFusionWithGamePhasePromptAsync(
                inventory,
                sacrifices,
                history,
                log).ConfigureAwait(false);

            log($"  融合結果: {fusionResult.Outcome}");
            log($"  メッセージ: {fusionResult.Message}");

            if (fusionResult.Outcome == SkillFusionOutcome.Success && fusionResult.GrantedSkill != null)
            {
                GameContentRegistry.RegisterSkill(fusionResult.GrantedSkill, log);

                if (fusionResult.GrantedSkill.FlavorText.Contains("【メリット】"))
                {
                    log("  【合成スキル・トレードオフ（フレーバー内包）】");
                    log($"    {fusionResult.GrantedSkill.FlavorText}");
                }

                AssertScenario(
                    log,
                    "Fusion実通信",
                    true,
                    $"合成スキル「{fusionResult.GrantedSkill.SkillName}」獲得");
            }
            else if (fusionResult.Outcome == SkillFusionOutcome.Refunded)
            {
                log("  対価スキルはロールバック（完全返還）されました。");
                AssertScenario(
                    log,
                    "Fusion返還",
                    inventory.CountOwnedSkill("skill_fire_spark") > 0 &&
                    inventory.CountOwnedSkill("skill_one_hand_sword") > 0,
                    "捧げたスキルが所持に戻っている");
            }
        }
        catch (RealAIHttpException exception)
        {
            log($"  ✗ HTTP/Gemini エラー (status={exception.StatusCode}): {exception.Message}");
            log("  返還処理は SkillFusionTransactionSystem が例外捕捉時に実行します。");
        }
        catch (Exception exception)
        {
            log($"  ✗ 例外: {exception.Message}");
        }
    }

    private static void AssertScenario(Action<string> log, string label, bool condition, string detail)
    {
#if UNITY_5_3_OR_NEWER
        log(condition ? $"  ✓ [{label} OK] {detail}" : $"  ✗ [{label} NG] {detail}");
#else
        if (!condition)
        {
            throw new InvalidOperationException($"{label} failed: {detail}");
        }

        log($"  ✓ [{label} OK] {detail}");
#endif
    }

    private static Action<string> GetDefaultLogger()
    {
#if UNITY_5_3_OR_NEWER
        return Debug.Log;
#else
        return Console.WriteLine;
#endif
    }

#if UNITY_5_3_OR_NEWER
    public static void RunInUnity()
    {
        RunAllTestsAsync(Debug.Log).GetAwaiter().GetResult();
    }
#endif
}

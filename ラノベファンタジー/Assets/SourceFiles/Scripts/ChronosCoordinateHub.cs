using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

// =============================================================================
// ワイプ連動・世代交代型の世界歴史座標管理（Chronos Coordinate）
// 連携: RealAIHttpClient / InGameVisualUIManager
// =============================================================================

/// <summary>正史・改変ダイブ用のプレイモード。</summary>
public static class ChronosStoryDiveModes
{
    /// <summary>歴史通りの絶望を辿る正史モード。</summary>
    public const string Canon = "正史";

    /// <summary>原作知識でフラグを破壊する改変モード。</summary>
    public const string Alter = "改変";
}

/// <summary>JsonUtility 用: AI が返す LOCATION LOG（3行）。</summary>
[Serializable]
public sealed class ChronosLocationLogDto
{
    public string line1;
    public string line2;
    public string line3;
}

/// <summary>JsonUtility 用: 過去ステージ応答エンベロープ。</summary>
[Serializable]
public sealed class ChronosPastStageResponseDto
{
    public ChronosLocationLogDto locationLog;
    public string mood;
    public string era;
}

/// <summary>パース済み LOCATION LOG（ランタイム）。</summary>
public sealed class ChronosLocationLogData
{
    public string Line1 { get; }
    public string Line2 { get; }
    public string Line3 { get; }

    public ChronosLocationLogData(string line1, string line2, string line3)
    {
        Line1 = line1 ?? string.Empty;
        Line2 = line2 ?? string.Empty;
        Line3 = line3 ?? string.Empty;
    }

    /// <summary>3行を結合した表示用テキスト。</summary>
    public string ToDisplayBlock()
    {
        return $"{Line1}\n{Line2}\n{Line3}".Trim();
    }
}

/// <summary>過去時代ダイブの AI 通信結果。</summary>
public sealed class PastStageContextResult
{
    public bool Succeeded { get; }
    public string SelectedPastEra { get; }
    public string SelectedMode { get; }
    public ChronosLocationLogData LocationLog { get; }
    public string RawGeminiText { get; }
    public string ExtractedJson { get; }
    public string Message { get; }

    public PastStageContextResult(
        bool succeeded,
        string selectedPastEra,
        string selectedMode,
        ChronosLocationLogData locationLog,
        string rawGeminiText,
        string extractedJson,
        string message)
    {
        Succeeded = succeeded;
        SelectedPastEra = selectedPastEra ?? string.Empty;
        SelectedMode = selectedMode ?? string.Empty;
        LocationLog = locationLog;
        RawGeminiText = rawGeminiText ?? string.Empty;
        ExtractedJson = extractedJson ?? string.Empty;
        Message = message ?? string.Empty;
    }
}

/// <summary>Gemini 応答から LOCATION LOG を抽出します。</summary>
public static class ChronosLocationLogParser
{
    /// <summary>生応答から3行 LOCATION LOG をパースします。</summary>
    public static ChronosLocationLogData TryParse(string rawResponse)
    {
        if (string.IsNullOrWhiteSpace(rawResponse))
        {
            return null;
        }

        string extracted = AIResponseParser.ExtractJsonObject(rawResponse);
        if (string.IsNullOrWhiteSpace(extracted))
        {
            return null;
        }

        try
        {
            ChronosPastStageResponseDto dto = JsonUtility.FromJson<ChronosPastStageResponseDto>(extracted);
            if (dto?.locationLog == null)
            {
                return null;
            }

            ChronosLocationLogDto log = dto.locationLog;
            if (string.IsNullOrWhiteSpace(log.line1))
            {
                return null;
            }

            return new ChronosLocationLogData(log.line1, log.line2, log.line3);
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>
/// ワイプ（世代交代）で世界年代が進行し、通過した時代のみが正史・改変用にアンロックされる歴史ハブ。
/// </summary>
public class ChronosCoordinateHub : MonoBehaviour
{
    public const string DefaultInitialEra = "欧州魔素動乱期";
    public const string DefaultInitialLocation = "若葉の拠点";
    public const string DefaultNextEraAfterWipe = "西暦2000年極東戦区";
    public const string DefaultNextLocationAfterWipe = "旧日本エリア";

    public static ChronosCoordinateHub Instance { get; private set; }

    private RealAIHttpClient httpClient;

    /// <summary>開拓モード等で進行中の、地球全体の最新時代。</summary>
    public string currentGlobalEra { get; private set; } = DefaultInitialEra;

    /// <summary>最新の世界座標（場所）。</summary>
    public string currentGlobalLocation { get; private set; } = DefaultInitialLocation;

    /// <summary>ワイプ通過済み・正史/改変選択可能な過去時代アーカイブ。</summary>
    private readonly List<string> unlockedErasForStory = new List<string>();

    private bool ownsHttpClient;

    /// <summary>アンロック済み過去時代（読み取り専用）。形式: 「時代名：場所名」</summary>
    public IReadOnlyList<string> UnlockedErasForStory => unlockedErasForStory;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[ChronosCoordinateHub] 重複インスタンスを検出しました。");
            return;
        }

        Instance = this;
        if (httpClient == null)
        {
            httpClient = new RealAIHttpClient();
            ownsHttpClient = true;
        }
    }

    private void OnDestroy()
    {
        if (ownsHttpClient)
        {
            httpClient?.Dispose();
        }

        if (Instance == this)
        {
            Instance = null;
        }
    }

    /// <summary>シーンに無い場合は動的生成して返します。</summary>
    public static ChronosCoordinateHub EnsureInstance()
    {
        if (Instance != null)
        {
            return Instance;
        }

        GameObject hub = GameObject.Find("DebugSystemsHub");
        if (hub != null)
        {
            ChronosCoordinateHub existing = hub.GetComponent<ChronosCoordinateHub>();
            return existing != null ? existing : hub.AddComponent<ChronosCoordinateHub>();
        }

        return new GameObject(nameof(ChronosCoordinateHub)).AddComponent<ChronosCoordinateHub>();
    }

    /// <summary>
    /// ワイプ（世代交代）を発生させ、現在時代をアーカイブして世界線を強制進行します。
    /// </summary>
    /// <param name="nextEra">進行先の最新時代</param>
    /// <param name="nextLocation">進行先の最新場所</param>
    public void TriggerWipeAndAdvanceEra(string nextEra, string nextLocation)
    {
        if (string.IsNullOrWhiteSpace(nextEra) || string.IsNullOrWhiteSpace(nextLocation))
        {
            Debug.LogWarning("[ChronosCoordinateHub] nextEra / nextLocation が空のためワイプを中止しました。");
            return;
        }

        string archivedKey = FormatArchiveKey(currentGlobalEra, currentGlobalLocation);
        if (!unlockedErasForStory.Contains(archivedKey))
        {
            unlockedErasForStory.Add(archivedKey);
        }

        string previousEra = currentGlobalEra;
        string previousLocation = currentGlobalLocation;

        currentGlobalEra = nextEra.Trim();
        currentGlobalLocation = nextLocation.Trim();

        Debug.Log(
            $"<color=#FF8C42><b>[ChronosCoordinateHub] ワイプ・世代交代</b></color>\n" +
            $"<color=#FFD54F>過去アーカイブ登録 ➔ {archivedKey}</color>\n" +
            $"<color=#7DF9FF>世界線強制進行 ➔ 【{currentGlobalEra}】 / {currentGlobalLocation}</color>\n" +
            $"<color=#AAAAAA>（旧: {previousEra} / {previousLocation}）</color>");
    }

    /// <summary>
    /// アンロック済みの過去時代へ正史/改変ダイブするため、Gemini へステージ背景を要求します。
    /// </summary>
    /// <param name="selectedPastEra">アーカイブキー（例: 欧州魔素動乱期：若葉の拠点）</param>
    /// <param name="selectedMode">"正史" または "改変"</param>
    public async Task<PastStageContextResult> RequestPastStageContextFromAI(
        string selectedPastEra,
        string selectedMode,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(selectedPastEra))
        {
            return Fail("過去時代が未指定です。", selectedPastEra, selectedMode);
        }

        if (!IsEraUnlockedForStory(selectedPastEra))
        {
            return Fail(
                $"未アンロックの時代です: {selectedPastEra}（先にワイプで通過が必要）",
                selectedPastEra,
                selectedMode);
        }

        if (!RealAIHttpClient.IsApiKeyConfigured)
        {
            return Fail(
                "Gemini API キー未設定。LocalSecrets/gemini-api-key.txt を確認してください。",
                selectedPastEra,
                selectedMode);
        }

        TrySplitArchiveKey(selectedPastEra, out string eraName, out string locationName);
        string mode = NormalizeMode(selectedMode);
        string prompt = BuildPastStagePrompt(eraName, locationName, mode);

        try
        {
            string raw = await httpClient
                .FetchGeminiTextAsync(prompt, cancellationToken)
                .ConfigureAwait(false);

            return await UnityMainThreadAwaiter.RunOnMainThreadAsync(
                () => FinalizePastStageContextFromRaw(selectedPastEra, mode, raw),
                cancellationToken);
        }
        catch (RealAIHttpException exception)
        {
            string message =
                $"HTTP/Gemini エラー (status={exception.StatusCode}): {exception.Message}";
            return await UnityMainThreadAwaiter.RunOnMainThreadAsync(
                () => Fail(message, selectedPastEra, selectedMode),
                cancellationToken);
        }
        catch (Exception exception)
        {
            string message = $"AI 通信例外: {exception.Message}";
            return await UnityMainThreadAwaiter.RunOnMainThreadAsync(
                () => Fail(message, selectedPastEra, selectedMode),
                cancellationToken);
        }
    }

    /// <summary>メインスレッド上で Gemini 応答をパースし UI へ反映します。</summary>
    private PastStageContextResult FinalizePastStageContextFromRaw(
        string selectedPastEra,
        string mode,
        string raw)
    {
        string extracted = AIResponseParser.ExtractJsonObject(raw) ?? string.Empty;
        ChronosLocationLogData locationLog = ChronosLocationLogParser.TryParse(raw);

        if (locationLog == null)
        {
            return new PastStageContextResult(
                false,
                selectedPastEra,
                mode,
                null,
                raw,
                extracted,
                "LOCATION LOG のパースに失敗しました。リトライ可能。");
        }

        string richLog = FormatLocationLogRichText(selectedPastEra, mode, locationLog);
        Debug.Log(richLog);

        InGameVisualUIManager.EnsureInstance().ShowPastStageLocationLog(
            selectedPastEra,
            mode,
            locationLog);

        return new PastStageContextResult(
            true,
            selectedPastEra,
            mode,
            locationLog,
            raw,
            extracted,
            "過去ステージの LOCATION LOG を取得しました。");
    }

    /// <summary>指定アーカイブが正史・改変用にアンロック済みか。</summary>
    public bool IsEraUnlockedForStory(string archiveKey)
    {
        if (string.IsNullOrWhiteSpace(archiveKey))
        {
            return false;
        }

        string normalized = NormalizeArchiveKey(archiveKey);
        for (int i = 0; i < unlockedErasForStory.Count; i++)
        {
            if (string.Equals(
                    NormalizeArchiveKey(unlockedErasForStory[i]),
                    normalized,
                    StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>デバッグ用：状態を初期世代へリセットします。</summary>
    public void ResetChronologyForTesting()
    {
        currentGlobalEra = DefaultInitialEra;
        currentGlobalLocation = DefaultInitialLocation;
        unlockedErasForStory.Clear();
        Debug.Log("[ChronosCoordinateHub] 歴史座標を初期状態にリセットしました。");
    }

    /// <summary>アーカイブキー「時代：場所」を生成します。</summary>
    public static string FormatArchiveKey(string era, string location)
    {
        return $"{era?.Trim() ?? "不明時代"}：{location?.Trim() ?? "不明場所"}";
    }

    private static PastStageContextResult Fail(string message, string era, string mode)
    {
        Debug.LogWarning($"[ChronosCoordinateHub] {message}");
        return new PastStageContextResult(false, era, mode, null, null, null, message);
    }

    private static string NormalizeMode(string mode)
    {
        if (string.IsNullOrWhiteSpace(mode))
        {
            return ChronosStoryDiveModes.Canon;
        }

        string trimmed = mode.Trim();
        if (trimmed.Equals(ChronosStoryDiveModes.Alter, StringComparison.Ordinal) ||
            trimmed.Equals("改変モード", StringComparison.Ordinal))
        {
            return ChronosStoryDiveModes.Alter;
        }

        return ChronosStoryDiveModes.Canon;
    }

    private static string NormalizeArchiveKey(string key)
    {
        return key.Replace(":", "：").Trim();
    }

    private static void TrySplitArchiveKey(string archiveKey, out string era, out string location)
    {
        string normalized = NormalizeArchiveKey(archiveKey);
        int separator = normalized.IndexOf('：');
        if (separator < 0)
        {
            era = normalized;
            location = "不明地点";
            return;
        }

        era = normalized.Substring(0, separator).Trim();
        location = normalized.Substring(separator + 1).Trim();
    }

    private static string BuildPastStagePrompt(string eraName, string locationName, string mode)
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("あなたは魔法が存在したIF地球の歴史アーカイブを肉付けするライターです。");
        sb.AppendLine($"対象時代: {eraName}");
        sb.AppendLine($"対象場所: {locationName}");
        sb.AppendLine($"プレイモード: {mode}");
        sb.AppendLine();
        sb.AppendLine("モード指針:");
        if (mode == ChronosStoryDiveModes.Canon)
        {
            sb.AppendLine("- 正史: モブの絶望、歴史の重さ、逃げ場のない空気を描く。");
        }
        else
        {
            sb.AppendLine("- 改変: 原作知識によるフラグ破壊、IFの亀裂、違和感のある希望を描く。");
        }

        sb.AppendLine();
        sb.AppendLine("ゼンレスゾーンゼロ風のスタイリッシュで短い日本語3行を JSON のみで返せ。");
        sb.AppendLine("枕詞・Markdown 禁止。以下の形のみ:");
        sb.AppendLine("{");
        sb.AppendLine("  \"locationLog\": {");
        sb.AppendLine("    \"line1\": \"ネオンが滲む一行目\",");
        sb.AppendLine("    \"line2\": \"歴史の匂いがする二行目\",");
        sb.AppendLine("    \"line3\": \"プレイヤーの足元を冷ます三行目\"");
        sb.AppendLine("  },");
        sb.AppendLine("  \"mood\": \"絶望|希望|緊張 等\",");
        sb.AppendLine($"  \"era\": \"{eraName}\"");
        sb.AppendLine("}");
        return sb.ToString();
    }

    private static string FormatLocationLogRichText(
        string archiveKey,
        string mode,
        ChronosLocationLogData log)
    {
        return $"<color=#00D4FF><b>══ LOCATION LOG ══</b></color>\n" +
               $"<color=#7DF9FF><b>[{archiveKey}]</b> モード: {mode}</color>\n" +
               $"<color=#00D4FF>{log.Line1}</color>\n" +
               $"<color=#4DE8FF>{log.Line2}</color>\n" +
               $"<color=#9FFBFF>{log.Line3}</color>";
    }
}

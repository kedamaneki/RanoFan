using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

#if UNITY_5_3_OR_NEWER
using UnityEngine;
#endif

// =============================================================================
// AI 生成データのローカル収集・永続化（ワイプ後も残るマスターデータ）
// DataCollectionSystemTest.Main() で検証。
// 連携: AIGeneratorConnector / AIGenerationResult / AIResponseParser
// =============================================================================

/// <summary>収集アセットの保存先パス定義。</summary>
public static class StoredAIAssetPaths
{
    public const string RootFolderName = "StoredAIAssets";
    public const string SkillsFolderName = "Skills";
    public const string RecipesFolderName = "Recipes";

    public static string GetProjectRoot()
    {
#if UNITY_5_3_OR_NEWER
        return Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
#else
        return Directory.GetCurrentDirectory();
#endif
    }

    public static string GetSkillsDirectory()
    {
        return Path.Combine(GetProjectRoot(), RootFolderName, SkillsFolderName);
    }

    public static string GetRecipesDirectory()
    {
        return Path.Combine(GetProjectRoot(), RootFolderName, RecipesFolderName);
    }

    public static string GetDirectoryForKind(AIGenerationKind kind)
    {
        return kind == AIGenerationKind.Craft
            ? GetRecipesDirectory()
            : GetSkillsDirectory();
    }
}

/// <summary>ファイル書き出し結果。</summary>
public readonly struct DataCollectionWriteResult
{
    public bool Saved { get; }
    public string FilePath { get; }
    public string Message { get; }

    public DataCollectionWriteResult(bool saved, string filePath, string message)
    {
        Saved = saved;
        FilePath = filePath ?? string.Empty;
        Message = message ?? string.Empty;
    }

    public static DataCollectionWriteResult Success(string filePath)
    {
        return new DataCollectionWriteResult(true, filePath, $"保存完了: {filePath}");
    }

    public static DataCollectionWriteResult Skipped(string reason)
    {
        return new DataCollectionWriteResult(false, null, reason);
    }
}

/// <summary>
/// AI パース済み JSON を StoredAIAssets 配下へ非同期書き出します。
/// </summary>
public static class DataFileWriter
{
    public static string BuildUniqueFileName(
        string categoryPrefix,
        string displayName,
        DateTime? timestamp = null)
    {
        string safePrefix = SanitizeFileName(categoryPrefix, fallback: "AIAsset");
        string safeName = SanitizeFileName(displayName, fallback: "Unknown");
        string datePart = (timestamp ?? DateTime.Now).ToString("yyyyMMdd");
        string shortGuid = Guid.NewGuid().ToString("N").Substring(0, 8);

        return $"{safePrefix}_{safeName}_{datePart}_{shortGuid}.json";
    }

    public static Task WriteJsonAsync(
        string fullPath,
        string jsonContent,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(fullPath))
        {
            throw new ArgumentException("保存先パスが空です。", nameof(fullPath));
        }

        if (jsonContent == null)
        {
            throw new ArgumentNullException(nameof(jsonContent));
        }

        string directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

#if UNITY_5_3_OR_NEWER
        cancellationToken.ThrowIfCancellationRequested();
        File.WriteAllText(fullPath, jsonContent, Encoding.UTF8);
        return Task.CompletedTask;
#else
        return File.WriteAllTextAsync(fullPath, jsonContent, Encoding.UTF8, cancellationToken);
#endif
    }

    public static string SanitizeFileName(string value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        StringBuilder builder = new StringBuilder(value.Length);
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if (Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0 || c == ' ')
            {
                builder.Append('_');
            }
            else
            {
                builder.Append(c);
            }
        }

        string sanitized = builder.ToString().Trim('_');
        return string.IsNullOrWhiteSpace(sanitized) ? fallback : sanitized;
    }
}

/// <summary>ディスクから読み込んだ AI 生成アセット1件。</summary>
public sealed class CollectedAIAssetEntry
{
    public string FilePath { get; }
    public string FileName { get; }
    public string RawJson { get; }
    public AIGenerationKind Kind { get; }
    public GeneratedSkillData Skill { get; }
    public GeneratedCraftData Craft { get; }

    public CollectedAIAssetEntry(
        string filePath,
        string rawJson,
        AIGenerationKind kind,
        GeneratedSkillData skill,
        GeneratedCraftData craft)
    {
        FilePath = filePath ?? string.Empty;
        FileName = string.IsNullOrEmpty(filePath) ? string.Empty : Path.GetFileName(filePath);
        RawJson = rawJson ?? string.Empty;
        Kind = kind;
        Skill = skill;
        Craft = craft;
    }
}

/// <summary>
/// 収集済み AI アセットのマスターデータキャッシュ（起動時ロード用モック）。
/// </summary>
public sealed class AIAssetMasterDatabase
{
    private readonly List<CollectedAIAssetEntry> skillEntries = new List<CollectedAIAssetEntry>();
    private readonly List<CollectedAIAssetEntry> recipeEntries = new List<CollectedAIAssetEntry>();
    private bool loaded;

    public IReadOnlyList<CollectedAIAssetEntry> SkillEntries => skillEntries;
    public IReadOnlyList<CollectedAIAssetEntry> RecipeEntries => recipeEntries;
    public bool IsLoaded => loaded;
    public int TotalCount => skillEntries.Count + recipeEntries.Count;

    public async Task LoadAllAsync(CancellationToken cancellationToken = default)
    {
        skillEntries.Clear();
        recipeEntries.Clear();

        await LoadDirectoryAsync(
            StoredAIAssetPaths.GetSkillsDirectory(),
            AIGenerationKind.Skill,
            skillEntries,
            cancellationToken).ConfigureAwait(false);

        await LoadDirectoryAsync(
            StoredAIAssetPaths.GetRecipesDirectory(),
            AIGenerationKind.Craft,
            recipeEntries,
            cancellationToken).ConfigureAwait(false);

        loaded = true;
    }

    private static async Task LoadDirectoryAsync(
        string directory,
        AIGenerationKind expectedKind,
        List<CollectedAIAssetEntry> target,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(directory))
        {
            return;
        }

        string[] files = Directory.GetFiles(directory, "*.json", SearchOption.TopDirectoryOnly);
        Array.Sort(files, StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < files.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string filePath = files[i];
            string rawJson = await ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false);
            AIGenerationResult parsed = RealAIGameResponseParser.ParseGenerationResponse(rawJson);

            if (parsed.ParseStatus != AIParseStatus.Success)
            {
                continue;
            }

            if (parsed.Skill != null && parsed.Skill.IsFallback)
            {
                continue;
            }

            if (parsed.Craft != null && parsed.Craft.IsFallback)
            {
                continue;
            }

            target.Add(new CollectedAIAssetEntry(
                filePath,
                parsed.ExtractedJson ?? rawJson,
                parsed.Kind,
                parsed.Skill,
                parsed.Craft));
        }
    }

    private static Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken)
    {
#if UNITY_5_3_OR_NEWER
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(File.ReadAllText(path, Encoding.UTF8));
#else
        return File.ReadAllTextAsync(path, Encoding.UTF8, cancellationToken);
#endif
    }
}

/// <summary>
/// パース成功時の自動収集・永続化を統括します。
/// </summary>
public static class DataCollectionSystem
{
    public static async Task<DataCollectionWriteResult> TryCollectParsedResultAsync(
        AIGenerationResult result,
        Action<string> log = null,
        CancellationToken cancellationToken = default)
    {
        if (!CanCollect(result))
        {
            string reason = BuildSkipReason(result);
            log?.Invoke($"[DataCollection] 保存スキップ: {reason}");
            return DataCollectionWriteResult.Skipped(reason);
        }

        string directory = StoredAIAssetPaths.GetDirectoryForKind(result.Kind);
        string prefix = ResolveFilePrefix(result);
        string displayName = ResolveDisplayName(result);
        string fileName = DataFileWriter.BuildUniqueFileName(prefix, displayName);
        string fullPath = Path.Combine(directory, fileName);

        await DataFileWriter.WriteJsonAsync(fullPath, result.ExtractedJson, cancellationToken)
            .ConfigureAwait(false);

        log?.Invoke($"[DataCollection] フォルダ自動生成・書き出し: {fullPath}");
        return DataCollectionWriteResult.Success(fullPath);
    }

    public static bool CanCollect(AIGenerationResult result)
    {
        if (result == null)
        {
            return false;
        }

        if (result.ParseStatus != AIParseStatus.Success)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(result.ExtractedJson))
        {
            return false;
        }

        if (result.Skill != null && result.Skill.IsFallback)
        {
            return false;
        }

        if (result.Craft != null && result.Craft.IsFallback)
        {
            return false;
        }

        return result.Skill != null || result.Craft != null || result.BaseSkill != null;
    }

    private static string BuildSkipReason(AIGenerationResult result)
    {
        if (result == null)
        {
            return "結果が null です。";
        }

        if (result.ParseStatus != AIParseStatus.Success)
        {
            return $"パース状態が Success ではありません（{result.ParseStatus}）。";
        }

        if (result.Skill != null && result.Skill.IsFallback)
        {
            return "フォールバックスキルのため保存しません。";
        }

        if (result.Craft != null && result.Craft.IsFallback)
        {
            return "フォールバック素材のため保存しません。";
        }

        if (string.IsNullOrWhiteSpace(result.ExtractedJson))
        {
            return "抽出 JSON が空です。";
        }

        return "収集条件を満たしません。";
    }

    private static string ResolveFilePrefix(AIGenerationResult result)
    {
        string json = result.ExtractedJson ?? string.Empty;

        if (json.IndexOf("\"compositeSkill\"", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "CompositeSkill";
        }

        if (result.Kind == AIGenerationKind.Craft)
        {
            return json.IndexOf("\"recipeRequest\"", StringComparison.OrdinalIgnoreCase) >= 0
                ? "Recipe"
                : "Craft";
        }

        if (json.IndexOf("\"phase\":\"Training\"", StringComparison.OrdinalIgnoreCase) >= 0 ||
            json.IndexOf("\"phase\": \"Training\"", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "BaseSkill";
        }

        return "UniqueSkill";
    }

    private static string ResolveDisplayName(AIGenerationResult result)
    {
        if (result.BaseSkill != null && !string.IsNullOrWhiteSpace(result.BaseSkill.SkillName))
        {
            return result.BaseSkill.SkillName;
        }

        if (result.Skill != null && !string.IsNullOrWhiteSpace(result.Skill.SkillName))
        {
            return result.Skill.SkillName;
        }

        if (result.Craft != null && !string.IsNullOrWhiteSpace(result.Craft.ItemName))
        {
            return result.Craft.ItemName;
        }

        return "Unknown";
    }
}

/// <summary>通信・登録フローへの収集組み込みヘルパー。</summary>
public static class DataCollectionBridge
{
    public static async Task<AIGenerationResult> RegisterParseAndCollectAsync(
        AIGenerationResult result,
        Action<string> log = null,
        CancellationToken cancellationToken = default)
    {
        if (result == null)
        {
            return null;
        }

        if (result.Skill != null)
        {
            GameContentRegistry.RegisterSkill(result.Skill, log);
        }

        if (result.Craft != null)
        {
            GameContentRegistry.RegisterCraft(result.Craft, log);
        }

        await DataCollectionSystem.TryCollectParsedResultAsync(result, log, cancellationToken)
            .ConfigureAwait(false);

        return result;
    }
}

/// <summary>モック実行・収集/ロード検証用エントリポイント。</summary>
public static class DataCollectionSystemTest
{
    public static void Main()
    {
        RunAllTestsAsync().GetAwaiter().GetResult();
    }

    public static async Task RunAllTestsAsync(Action<string> log = null)
    {
        log ??= GetDefaultLogger();

        log("===== DataCollectionSystemTest 開始 =====");
        await RunCompositeSkillStockAndLoadTestAsync(log).ConfigureAwait(false);
        await RunFallbackGuardTestAsync(log).ConfigureAwait(false);
        log("===== DataCollectionSystemTest 完了 =====");
    }

    private static async Task RunCompositeSkillStockAndLoadTestAsync(Action<string> log)
    {
        log(string.Empty);
        log("--- 検証：AIデータの自動ストック（収集）テスト ---");

        string mockCompositeJson =
            "{\n" +
            "  \"generationType\": \"skill\",\n" +
            "  \"phase\": \"Fusion\",\n" +
            "  \"compositeSkill\": {\n" +
            "    \"skillName\": \"業火刃\",\n" +
            "    \"meritEffect\": \"自身が作成した武器のダメージ+50%\",\n" +
            "    \"demeritEffect\": \"他人が作成した武器を使用時、攻撃倍率-70%\",\n" +
            "    \"flavorText\": \"火の粉と片手剣術が交わった呪われた剣技。\",\n" +
            "    \"probability\": 1.0,\n" +
            "    \"effectParameters\": [\n" +
            "      { \"key\": \"selfCraftedWeaponDamageBonus\", \"value\": 0.5 },\n" +
            "      { \"key\": \"foreignWeaponDamagePenalty\", \"value\": -0.7 }\n" +
            "    ],\n" +
            "    \"fusedFromSkillIds\": [\"skill_fire_spark\", \"skill_one_hand_sword\"]\n" +
            "  }\n" +
            "}";

        AIGenerationResult parsed = RealAIGameResponseParser.ParseGenerationResponse(mockCompositeJson);

        AssertScenario(
            log,
            "合成スキルパース",
            parsed.ParseStatus == AIParseStatus.Success && parsed.Skill != null,
            $"スキル名={parsed.Skill?.SkillName}");

        DataCollectionWriteResult writeResult = await DataCollectionSystem
            .TryCollectParsedResultAsync(parsed, log)
            .ConfigureAwait(false);

        AssertScenario(log, "ファイル書き出し", writeResult.Saved, writeResult.Message);
        AssertScenario(
            log,
            "Skillsフォルダ",
            Directory.Exists(StoredAIAssetPaths.GetSkillsDirectory()),
            StoredAIAssetPaths.GetSkillsDirectory());

        AIAssetMasterDatabase master = new AIAssetMasterDatabase();
        await master.LoadAllAsync().ConfigureAwait(false);

        log($"[DataCollection] マスターデータ読込: Skills={master.SkillEntries.Count}, Recipes={master.RecipeEntries.Count}");

        bool foundCollected = false;
        for (int i = 0; i < master.SkillEntries.Count; i++)
        {
            CollectedAIAssetEntry entry = master.SkillEntries[i];
            if (entry.Skill != null && entry.Skill.SkillName == "業火刃")
            {
                foundCollected = true;
                log("ファイルから1件のAI生成スキルをマスターデータとして収集完了！");
                log($"  収集ファイル: {entry.FileName}");
                log($"  スキル名: {entry.Skill.SkillName}");
                log($"  フレーバー: {entry.Skill.FlavorText}");
                break;
            }
        }

        AssertScenario(log, "マスターデータ収集", foundCollected, "業火刃をキャッシュから取得");
    }

    private static async Task RunFallbackGuardTestAsync(Action<string> log)
    {
        log(string.Empty);
        log("--- 検証：フォールバック時は保存しない安全ガード ---");

        AIGenerationResult fallback = AIResponseParser.ParseGenerationResponse(
            MockAIGenerationBackend.BuildBrokenResponse());

        DataCollectionWriteResult writeResult = await DataCollectionSystem
            .TryCollectParsedResultAsync(fallback, log)
            .ConfigureAwait(false);

        AssertScenario(
            log,
            "フォールバック非保存",
            !writeResult.Saved && fallback.ParseStatus != AIParseStatus.Success,
            writeResult.Message);
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

using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

// =============================================================================
// 想定外の職人介入・未知レシピをパケット化して将来資産として蓄積
// 連携: CraftingExperimentHub / ChronosCoordinateHub / PlayerCraftLootInventory
// =============================================================================

/// <summary>例外行動として回収された職人ルートの1パケット。</summary>
[Serializable]
public sealed class CraftingExceptionPacket
{
    /// <summary>実行された IF 地球の時代名。</summary>
    public string era;

    /// <summary>実行された場所名。</summary>
    public string location;

    /// <summary>職種種別（Forge または Alch）。</summary>
    public string craftType;

    /// <summary>終了時 ParamA（密度 / 抽出度）。</summary>
    public float finalParamA;

    /// <summary>終了時 ParamB（熱量 / 釜温）。</summary>
    public float finalParamB;

    /// <summary>終了時 ParamC（純度 / 溶解度）。</summary>
    public float finalParamC;

    /// <summary>途中投入された特殊触媒・介入素材 ID リスト。</summary>
    public List<string> injectedCatalysts = new List<string>();

    /// <summary>職人が能動的に選択したこだわり工程の全履歴（DetailedCraftingProcessManager 由来）。</summary>
    public List<string> detailedProcessLogs = new List<string>();

    /// <summary>拡張型生産ステータスのスナップショット（CraftingStatusManager 由来・Key=Value）。</summary>
    public List<string> dynamicCraftStatus = new List<string>();

    /// <summary>検知日時（ISO 8601 文字列）。</summary>
    public string timestamp;

    /// <summary>同時に付与されたルート報酬アイテム ID。</summary>
    public string grantedLootItemId;

    /// <summary>同時に付与されたルート報酬の表示名。</summary>
    public string grantedLootDisplayName;
}

/// <summary>JsonUtility 用の例外パケット DTO（配列フィールド対応）。</summary>
[Serializable]
internal sealed class CraftingExceptionPacketJsonDto
{
    public string era;
    public string location;
    public string craftType;
    public float finalParamA;
    public float finalParamB;
    public float finalParamC;
    public string[] injectedCatalysts;
    public string[] detailedProcessLogs;
    public string[] dynamicCraftStatus;
    public string timestamp;
    public string grantedLootItemId;
    public string grantedLootDisplayName;
}

/// <summary>例外生産から得られる10点級の歪な成果物データ。</summary>
[Serializable]
public sealed class CraftExceptionLootItem
{
    /// <summary>アイテム ID。</summary>
    public string itemId;

    /// <summary>UI 表示名。</summary>
    public string displayName;

    /// <summary>評価点（例外ルートは常に10点想定）。</summary>
    public int scoreRating;

    /// <summary>カテゴリ（WarpedScrap / UnknownBlackMatter）。</summary>
    public string category;

    /// <summary>取得元の例外パケット JSON（参照用）。</summary>
    public string sourcePacketJson;
}

/// <summary>例外生産ルートの成果物を保持する簡易インベントリ。</summary>
public class PlayerCraftLootInventory : MonoBehaviour
{
    public static PlayerCraftLootInventory Instance { get; private set; }

    private readonly List<CraftExceptionLootItem> storedLoot = new List<CraftExceptionLootItem>();

    /// <summary>所持している例外ルート成果物（読み取り専用）。</summary>
    public IReadOnlyList<CraftExceptionLootItem> StoredLoot => storedLoot;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[PlayerCraftLootInventory] 重複インスタンスを検出しました。");
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

    /// <summary>シーンに無い場合は動的生成して返します。</summary>
    public static PlayerCraftLootInventory EnsureInstance()
    {
        if (Instance != null)
        {
            return Instance;
        }

        GameObject hub = GameObject.Find("DebugSystemsHub");
        if (hub != null)
        {
            PlayerCraftLootInventory existing = hub.GetComponent<PlayerCraftLootInventory>();
            return existing != null ? existing : hub.AddComponent<PlayerCraftLootInventory>();
        }

        return new GameObject(nameof(PlayerCraftLootInventory)).AddComponent<PlayerCraftLootInventory>();
    }

    /// <summary>例外ルート成果物をインベントリへ追加します。</summary>
    public void AddExceptionLoot(CraftExceptionLootItem loot)
    {
        if (loot == null || string.IsNullOrWhiteSpace(loot.itemId))
        {
            return;
        }

        storedLoot.Add(loot);
        Debug.Log(
            $"<color=#FF7043><b>[PlayerCraftLootInventory]</b> 例外成果物を受領: " +
            $"{loot.displayName}（{loot.scoreRating}点 / {loot.category}）" +
            $" — 所持数 {storedLoot.Count}</color>");
    }
}

/// <summary>
/// 正規レシピ外の職人介入を検知し、将来アップデート用データとしてパケット化します。
/// </summary>
public class CraftingExceptionCollector : MonoBehaviour
{
    public const string LootWarpedScrapId = "loot_warped_scrap_10";
    public const string LootUnknownBlackMatterId = "loot_unknown_black_matter_10";

    public static CraftingExceptionCollector Instance { get; private set; }

    private readonly List<CraftingExceptionPacket> pendingPackets = new List<CraftingExceptionPacket>();

    /// <summary>サーバー送信待ちの例外パケット一覧（読み取り専用）。</summary>
    public IReadOnlyList<CraftingExceptionPacket> PendingPackets => pendingPackets;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[CraftingExceptionCollector] 重複インスタンスを検出しました。");
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

    /// <summary>シーンに無い場合は動的生成して返します。</summary>
    public static CraftingExceptionCollector EnsureInstance()
    {
        if (Instance != null)
        {
            return Instance;
        }

        GameObject hub = GameObject.Find("DebugSystemsHub");
        if (hub != null)
        {
            CraftingExceptionCollector existing = hub.GetComponent<CraftingExceptionCollector>();
            return existing != null ? existing : hub.AddComponent<CraftingExceptionCollector>();
        }

        return new GameObject(nameof(CraftingExceptionCollector)).AddComponent<CraftingExceptionCollector>();
    }

    /// <summary>
    /// 簡易レシピ定義スタブ。正規ルートなら true、未知ルートなら false を返します。
    /// </summary>
    public bool CheckIfRecipeDefined(
        string type,
        float a,
        float b,
        float c,
        List<string> catalysts)
    {
        if (HasInjectedCatalysts(catalysts))
        {
            return false;
        }

        if (IsForgeType(type))
        {
            return a >= 50f && a <= 70f &&
                   b >= 45f && b <= 75f &&
                   c >= 40f && c <= 60f;
        }

        if (IsAlchType(type))
        {
            return a >= 45f && a <= 65f &&
                   b >= 45f && b <= 65f &&
                   c >= 45f && c <= 65f;
        }

        return false;
    }

    /// <summary>
    /// 例外行動をパケット化し、JSON ログ出力と10点級ルート成果物の付与を行います。
    /// </summary>
    public CraftingExceptionPacket CollectAndPacketize(
        string era,
        string loc,
        string type,
        float a,
        float b,
        float c,
        List<string> catalysts)
    {
        return CollectAndPacketize(era, loc, type, a, b, c, catalysts, null, null);
    }

    /// <summary>
    /// 例外行動をパケット化し、こだわり工程ログをマージして JSON 出力と成果物付与を行います。
    /// </summary>
    /// <param name="processLogs">職人の全工程履歴（null 可）</param>
    public CraftingExceptionPacket CollectAndPacketize(
        string era,
        string loc,
        string type,
        float a,
        float b,
        float c,
        List<string> catalysts,
        List<string> processLogs)
    {
        return CollectAndPacketize(era, loc, type, a, b, c, catalysts, processLogs, null);
    }

    /// <summary>
    /// 例外行動をパケット化し、工程ログと動的ステータスをマージして JSON 出力と成果物付与を行います。
    /// </summary>
    /// <param name="processLogs">職人の全工程履歴（null 可）</param>
    /// <param name="dynamicStatusLines">CraftingStatusManager のスナップショット（null 可）</param>
    public CraftingExceptionPacket CollectAndPacketize(
        string era,
        string loc,
        string type,
        float a,
        float b,
        float c,
        List<string> catalysts,
        List<string> processLogs,
        List<string> dynamicStatusLines)
    {
        List<string> mergedCatalysts = MergeCatalystsWithProcessLogs(catalysts, processLogs, dynamicStatusLines);
        CraftExceptionLootItem loot = BuildExceptionLoot(type, a, b, c, mergedCatalysts);
        CraftingExceptionPacket packet = new CraftingExceptionPacket
        {
            era = era ?? string.Empty,
            location = loc ?? string.Empty,
            craftType = type ?? string.Empty,
            finalParamA = a,
            finalParamB = b,
            finalParamC = c,
            injectedCatalysts = CopyCatalystList(mergedCatalysts),
            detailedProcessLogs = CopyCatalystList(processLogs),
            dynamicCraftStatus = CopyCatalystList(dynamicStatusLines),
            timestamp = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
            grantedLootItemId = loot.itemId,
            grantedLootDisplayName = loot.displayName
        };

        pendingPackets.Add(packet);

        string json = SerializePacket(packet);
        loot.sourcePacketJson = json;

        ItemData inventoryItem = InventoryItemCatalog.FromExceptionLoot(loot);
        if (inventoryItem != null)
        {
            InventoryManager.EnsureInstance().AddItem(inventoryItem, 1);
        }

        PlayerCraftLootInventory.EnsureInstance().AddExceptionLoot(loot);

        Debug.Log(
            "<color=#FF5722><b>【データ収集システム】想定外の職人ルートを検知！" +
            "サーバーへパケット送信をスタブ待機中...</b></color>\n" +
            $"<color=#FF8A65>{json}</color>");

        return packet;
    }

    /// <summary>触媒リスト・工程ログ・動的ステータスを injectedCatalysts 配列へマージします。</summary>
    public static List<string> MergeCatalystsWithProcessLogs(
        List<string> catalysts,
        List<string> processLogs,
        List<string> dynamicStatusLines = null)
    {
        List<string> merged = new List<string>();
        if (catalysts != null)
        {
            for (int i = 0; i < catalysts.Count; i++)
            {
                if (!string.IsNullOrWhiteSpace(catalysts[i]))
                {
                    merged.Add(catalysts[i]);
                }
            }
        }

        if (dynamicStatusLines != null && dynamicStatusLines.Count > 0)
        {
            merged.Add("---[DYNAMIC_CRAFT_STATUS_BEGIN]---");
            for (int i = 0; i < dynamicStatusLines.Count; i++)
            {
                if (!string.IsNullOrWhiteSpace(dynamicStatusLines[i]))
                {
                    merged.Add($"craft_status:{dynamicStatusLines[i]}");
                }
            }

            merged.Add("---[DYNAMIC_CRAFT_STATUS_END]---");
        }

        if (processLogs == null || processLogs.Count == 0)
        {
            return merged;
        }

        merged.Add("---[DETAILED_PROCESS_LOG_BEGIN]---");
        for (int i = 0; i < processLogs.Count; i++)
        {
            string step = processLogs[i];
            if (string.IsNullOrWhiteSpace(step))
            {
                continue;
            }

            merged.Add($"process_step_{i + 1:D3}:{step}");
        }

        merged.Add("---[DETAILED_PROCESS_LOG_END]---");
        return merged;
    }

    /// <summary>例外パケットを JSON 文字列へシリアライズします。</summary>
    public static string SerializePacket(CraftingExceptionPacket packet)
    {
        if (packet == null)
        {
            return "{}";
        }

        CraftingExceptionPacketJsonDto dto = new CraftingExceptionPacketJsonDto
        {
            era = packet.era,
            location = packet.location,
            craftType = packet.craftType,
            finalParamA = packet.finalParamA,
            finalParamB = packet.finalParamB,
            finalParamC = packet.finalParamC,
            injectedCatalysts = packet.injectedCatalysts?.ToArray() ?? Array.Empty<string>(),
            detailedProcessLogs = packet.detailedProcessLogs?.ToArray() ?? Array.Empty<string>(),
            dynamicCraftStatus = packet.dynamicCraftStatus?.ToArray() ?? Array.Empty<string>(),
            timestamp = packet.timestamp,
            grantedLootItemId = packet.grantedLootItemId,
            grantedLootDisplayName = packet.grantedLootDisplayName
        };

        return JsonUtility.ToJson(dto, prettyPrint: true);
    }

    private static bool HasInjectedCatalysts(List<string> catalysts)
    {
        return catalysts != null && catalysts.Count > 0;
    }

    private static bool IsForgeType(string type)
    {
        return string.Equals(type, "Forge", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(type, CraftProfessionType.Forge.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAlchType(string type)
    {
        return string.Equals(type, "Alch", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(type, CraftProfessionType.Alch.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    private static List<string> CopyCatalystList(List<string> catalysts)
    {
        if (catalysts == null || catalysts.Count == 0)
        {
            return new List<string>();
        }

        return new List<string>(catalysts);
    }

    private static CraftExceptionLootItem BuildExceptionLoot(
        string type,
        float a,
        float b,
        float c,
        List<string> catalysts)
    {
        bool useBlackMatter = HasInjectedCatalysts(catalysts) ||
                             a >= 90f || b >= 90f || c <= 5f;

        if (useBlackMatter)
        {
            return new CraftExceptionLootItem
            {
                itemId = LootUnknownBlackMatterId,
                displayName = "未知の黒色物質",
                scoreRating = 10,
                category = "UnknownBlackMatter"
            };
        }

        return new CraftExceptionLootItem
        {
            itemId = LootWarpedScrapId,
            displayName = "歪な廃棄物",
            scoreRating = 10,
            category = "WarpedScrap"
        };
    }
}

/// <summary>Play 開始時に例外回収系コンポーネントを DebugSystemsHub へ配置します。</summary>
public static class CraftingExceptionBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AttachToDebugSystemsHub()
    {
        GameObject hub = GameObject.Find("DebugSystemsHub");
        if (hub == null)
        {
            return;
        }

        AttachCollectorIfNeeded(hub);
    }

    /// <summary>指定 GameObject に例外回収コンポーネントを不足分だけ追加します。</summary>
    public static void AttachCollectorIfNeeded(GameObject hub)
    {
        if (hub == null)
        {
            return;
        }

        if (hub.GetComponent<CraftingExceptionCollector>() == null)
        {
            hub.AddComponent<CraftingExceptionCollector>();
        }

        if (hub.GetComponent<PlayerCraftLootInventory>() == null)
        {
            hub.AddComponent<PlayerCraftLootInventory>();
        }
    }
}

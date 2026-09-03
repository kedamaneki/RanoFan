using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

// =============================================================================
// 職人クオリティ例外パケット — Enter 終了時のスコア付き JSON 射出
// 連携: CraftingStatusManager / DetailedCraftingProcessManager
// =============================================================================

/// <summary>生産終了時にサーバー共有を想定して射出する品質付き例外パケット。</summary>
[Serializable]
public sealed class CraftingQualityPacket
{
    /// <summary>パケット固有 ID（GUID）。</summary>
    public string packetId = string.Empty;

    /// <summary>職種（Forge / Alch）。</summary>
    public string craftType = string.Empty;

    /// <summary>最終クオリティスコア（熱科学バースト時 10点、通常成功時 60〜100点）。</summary>
    public int finalScore;

    /// <summary>終了時点の裏パラメータ（Key=Value 形式）。</summary>
    public List<string> finalParameters = new List<string>();

    /// <summary>職人の全工程ログ（DetailedCraftingProcessManager の 1〜5 キー操作履歴）。</summary>
    public List<string> artisanProcessLogs = new List<string>();

    /// <summary>射出日時（ISO 8601）。</summary>
    public string timestamp = string.Empty;

    /// <summary>レシピ解決後の最終成果物 ItemMasterData.id。</summary>
    public string resultItemId = string.Empty;

    /// <summary>最終成果物の表示名。</summary>
    public string resultItemName = string.Empty;

    /// <summary>最終成果物の説明文。</summary>
    public string resultItemDescription = string.Empty;
}

/// <summary>JsonUtility 用の品質パケット DTO。</summary>
[Serializable]
internal sealed class CraftingQualityPacketJsonDto
{
    public string packetId;
    public string craftType;
    public int finalScore;
    public string[] finalParameters;
    public string[] artisanProcessLogs;
    public string timestamp;
    public string resultItemId;
    public string resultItemName;
    public string resultItemDescription;
}

/// <summary>Discord Webhook 送信用 JSON ボディ DTO。</summary>
[Serializable]
internal sealed class DiscordWebhookPayloadDto
{
    public string content;
}

/// <summary>品質パケットの生成・シリアライズ・コンソール射出・Discord 送出を担当します。</summary>
public class CraftingQualityPacketEmitter : MonoBehaviour
{
    private const int DiscordMessageContentLimit = 2000;

    private static CraftingQualityPacketEmitter instance;

    [Header("Discord Relay")]
    [Tooltip("Edit モードで DebugSystemsHub 上の本コンポーネントへ設定し、シーンを保存してください（Play 中の変更は保存されません）。")]
    [SerializeField] private string discordWebhookUrl;

    /// <summary>自動検証用：EmitToConsole の累計呼び出し回数。</summary>
    public static int TotalEmitCount { get; private set; }

    /// <summary>自動検証用：射出カウンタをリセットします。</summary>
    public static void ResetEmitTelemetry()
    {
        TotalEmitCount = 0;
    }

    /// <summary>シーン上の CraftingQualityPacketEmitter を解決します。</summary>
    public static CraftingQualityPacketEmitter EnsureInstance()
    {
        if (instance != null)
        {
            return instance;
        }

        CraftingQualityPacketEmitter found = FindAnyObjectByType<CraftingQualityPacketEmitter>();
        if (found != null)
        {
            instance = found;
            return found;
        }

        Debug.LogWarning(
            "[CraftingQualityPacketEmitter] シーンに未配置です。DebugSystemsHub へ追加し、" +
            "Edit モードで Discord Webhook URL を設定してシーンを保存してください。");
        return null;
    }

    /// <summary>品質パケットを組み立てます。</summary>
    /// <param name="craftType">Forge / Alch</param>
    /// <param name="finalScore">EvaluateCraftingQuality の結果</param>
    /// <param name="finalParameters">裏パラメータスナップショット</param>
    /// <param name="artisanProcessLogs">工程履歴（1〜5 キー操作の純粋ログ）</param>
    /// <param name="resultSnapshot">レシピ解決済み成果物（任意）</param>
    public static CraftingQualityPacket BuildPacket(
        string craftType,
        int finalScore,
        List<string> finalParameters,
        List<string> artisanProcessLogs,
        CraftingResultItemSnapshot resultSnapshot = default)
    {
        CraftingQualityPacket packet = new CraftingQualityPacket
        {
            packetId = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture),
            craftType = craftType ?? string.Empty,
            finalScore = finalScore,
            finalParameters = CopyStringList(finalParameters),
            artisanProcessLogs = CopyStringList(artisanProcessLogs),
            timestamp = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)
        };

        if (!string.IsNullOrWhiteSpace(resultSnapshot.ResultItemId))
        {
            packet.resultItemId = resultSnapshot.ResultItemId;
            packet.resultItemName = resultSnapshot.ResultItemName ?? string.Empty;
            packet.resultItemDescription = resultSnapshot.ResultItemDescription ?? string.Empty;
        }

        return packet;
    }

    /// <summary>品質パケットを組み立てます（後方互換オーバーロード）。</summary>
    public static CraftingQualityPacket BuildPacket(
        string craftType,
        int finalScore,
        List<string> finalParameters,
        List<string> artisanProcessLogs)
    {
        return BuildPacket(craftType, finalScore, finalParameters, artisanProcessLogs, default);
    }

    /// <summary>品質パケットを pretty JSON 文字列へシリアライズします。</summary>
    public static string SerializePacket(CraftingQualityPacket packet)
    {
        if (packet == null)
        {
            return "{}";
        }

        CraftingQualityPacketJsonDto dto = new CraftingQualityPacketJsonDto
        {
            packetId = packet.packetId,
            craftType = packet.craftType,
            finalScore = packet.finalScore,
            finalParameters = packet.finalParameters?.ToArray() ?? Array.Empty<string>(),
            artisanProcessLogs = packet.artisanProcessLogs?.ToArray() ?? Array.Empty<string>(),
            timestamp = packet.timestamp,
            resultItemId = packet.resultItemId ?? string.Empty,
            resultItemName = packet.resultItemName ?? string.Empty,
            resultItemDescription = packet.resultItemDescription ?? string.Empty
        };

        return JsonUtility.ToJson(dto, prettyPrint: true);
    }

    /// <summary>品質パケットをコンソールへ大々的に射出し、設定済みなら Discord へも非同期送出します。</summary>
    public static void EmitToConsole(CraftingQualityPacket packet)
    {
        if (packet == null)
        {
            Debug.LogWarning("[CraftingQualityPacketEmitter] 射出対象パケットが null です。");
            return;
        }

        TotalEmitCount++;
        string json = SerializePacket(packet);
        Debug.Log(
            "\n<color=#FFD54F><size=14><b>╔══════════════════════════════════════════════════════╗</b></size></color>\n" +
            "<color=#FFF59D><size=14><b>║  DEMO CRAFT EXCEPTION PACKET · 最終ジャッジ回収 ║</b></size></color>\n" +
            "<color=#FFD54F><size=14><b>╚══════════════════════════════════════════════════════╝</b></size></color>\n" +
            $"<color=#F8BBD0>packetId: <b>{packet.packetId}</b> / craftType: <b>{packet.craftType}</b> / " +
            $"finalScore: <b>{packet.finalScore}</b> 点 / 工程数: <b>{packet.artisanProcessLogs.Count}</b></color>\n" +
            $"<color=#CE93D8>成果物: <b>{packet.resultItemName}</b>（{packet.resultItemId}）</color>\n" +
            "<color=#4DB6AC>── 例外 JSON パケット（JsonUtility pretty） ──</color>\n" +
            $"<color=#E1BEE7>{json}</color>");

        CraftingQualityPacketEmitter emitter = EnsureInstance();
        if (emitter != null && emitter.isActiveAndEnabled)
        {
            emitter.StartCoroutine(emitter.SendJsonToDiscord(json));
        }
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(this);
            return;
        }

        instance = this;
    }

    private void OnDestroy()
    {
        if (instance == this)
        {
            instance = null;
        }
    }

    private IEnumerator SendJsonToDiscord(string jsonText)
    {
        if (string.IsNullOrWhiteSpace(discordWebhookUrl))
        {
            yield break;
        }

        if (string.IsNullOrWhiteSpace(jsonText))
        {
            yield break;
        }

        string requestBody;
        try
        {
            string discordContent = FormatDiscordJsonCodeBlock(jsonText);
            DiscordWebhookPayloadDto payload = new DiscordWebhookPayloadDto
            {
                content = discordContent
            };
            requestBody = JsonUtility.ToJson(payload);
        }
        catch (Exception ex)
        {
            Debug.LogWarning(
                "[CraftingQualityPacketEmitter] Discord ペイロード成形に失敗しました（ゲーム続行）: " +
                ex.Message);
            yield break;
        }

        UnityWebRequest request = null;
        try
        {
            request = new UnityWebRequest(discordWebhookUrl.Trim(), UnityWebRequest.kHttpVerbPOST);
            byte[] bodyRaw = Encoding.UTF8.GetBytes(requestBody);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
        }
        catch (Exception ex)
        {
            Debug.LogWarning(
                "[CraftingQualityPacketEmitter] Discord リクエスト初期化に失敗しました（ゲーム続行）: " +
                ex.Message);
            request?.Dispose();
            yield break;
        }

        yield return request.SendWebRequest();

        try
        {
            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning(
                    "[CraftingQualityPacketEmitter] Discord 送信失敗（ゲーム続行）: " +
                    request.error);
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning(
                "[CraftingQualityPacketEmitter] Discord 応答処理中に例外（ゲーム続行）: " +
                ex.Message);
        }
        finally
        {
            request.Dispose();
        }
    }

    private static string FormatDiscordJsonCodeBlock(string jsonText)
    {
        const string prefix = "```json\n";
        const string suffix = "\n```";
        string content = prefix + jsonText + suffix;

        if (content.Length <= DiscordMessageContentLimit)
        {
            return content;
        }

        int jsonBudget = DiscordMessageContentLimit - prefix.Length - suffix.Length - 24;
        if (jsonBudget < 0)
        {
            jsonBudget = 0;
        }

        string truncatedJson = jsonText.Length > jsonBudget
            ? jsonText.Substring(0, jsonBudget) + "\n…(truncated)"
            : jsonText;

        return prefix + truncatedJson + suffix;
    }

    private static List<string> CopyStringList(List<string> source)
    {
        List<string> copy = new List<string>();
        if (source == null)
        {
            return copy;
        }

        for (int i = 0; i < source.Count; i++)
        {
            if (!string.IsNullOrWhiteSpace(source[i]))
            {
                copy.Add(source[i]);
            }
        }

        return copy;
    }
}

/// <summary>Play 開始時にシーン上の CraftingQualityPacketEmitter を解決します。</summary>
public static class CraftingQualityPacketEmitterBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void ResolveSceneInstance()
    {
        CraftingQualityPacketEmitter.EnsureInstance();
    }
}

#if UNITY_EDITOR
/// <summary>エディタ起動時に DebugSystemsHub へ CraftingQualityPacketEmitter を永続配置します。</summary>
[InitializeOnLoad]
internal static class CraftingQualityPacketEmitterEditorInstaller
{
    static CraftingQualityPacketEmitterEditorInstaller()
    {
        EditorApplication.delayCall += InstallWhenSceneOpen;
    }

    private static void InstallWhenSceneOpen()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            return;
        }

        GameObject hub = GameObject.Find("DebugSystemsHub");
        if (hub == null)
        {
            return;
        }

        if (hub.GetComponent<CraftingQualityPacketEmitter>() != null)
        {
            return;
        }

        Undo.AddComponent<CraftingQualityPacketEmitter>(hub);
        EditorSceneManager.MarkSceneDirty(hub.scene);
        Debug.Log(
            "<color=#FFD54F>[CraftingQualityPacketEmitter]</color> DebugSystemsHub にコンポーネントを追加しました。" +
            " Edit モードで Discord Webhook URL を設定し、シーンを保存してください。");
    }
}
#endif

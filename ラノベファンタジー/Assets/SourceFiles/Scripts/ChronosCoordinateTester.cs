using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// ChronosCoordinateHub のデバッグテスター。DebugSystemsHub に自動配置されます。
/// H=ワイプ進行 / N=過去ダイブAI通信（正史⇔改変交互）
/// </summary>
public class ChronosCoordinateTester : MonoBehaviour
{
    [Header("参照")]
    [SerializeField] private ChronosCoordinateHub chronosHub;

    [Header("ワイプ進行先（H キー）")]
    [SerializeField] private string nextEraAfterWipe = ChronosCoordinateHub.DefaultNextEraAfterWipe;
    [SerializeField] private string nextLocationAfterWipe = ChronosCoordinateHub.DefaultNextLocationAfterWipe;

    [Header("過去ダイブ（N キー）")]
    [SerializeField] private string diveArchiveKey =
        ChronosCoordinateHub.FormatArchiveKey(
            ChronosCoordinateHub.DefaultInitialEra,
            ChronosCoordinateHub.DefaultInitialLocation);

    [SerializeField] private bool alternateDiveMode = true;
    [SerializeField] private string diveMode = ChronosStoryDiveModes.Canon;

    [Header("ホットキー")]
    [SerializeField] private Key wipeAdvanceKey = Key.H;
    [SerializeField] private Key pastDiveKey = Key.N;

    private bool diveInFlight;

    private void Awake()
    {
        chronosHub ??= ChronosCoordinateHub.EnsureInstance();
    }

    private void Start()
    {
        Debug.Log(
            "<color=#7DF9FF><b>[ChronosCoordinateTester]</b> 準備完了\n" +
            "  H=ワイプ＆時代進行 / N=過去ダイブAI（正史⇔改変交互）</color>");
        LogChronologyStatus();
    }

    private void Update()
    {
        chronosHub ??= ChronosCoordinateHub.EnsureInstance();

        if (DebugHotkeyUtility.WasPressed(wipeAdvanceKey))
        {
            SimulateWipeAndEraAdvance();
            return;
        }

        if (DebugHotkeyUtility.WasPressed(pastDiveKey))
        {
            _ = RequestPastEraDiveAsync();
        }
    }

    /// <summary>H キー: ワイプで世界線を次時代へ強制進行します。</summary>
    [ContextMenu("Simulate Wipe And Era Advance (H)")]
    public void SimulateWipeAndEraAdvance()
    {
        chronosHub.TriggerWipeAndAdvanceEra(nextEraAfterWipe, nextLocationAfterWipe);
        LogChronologyStatus();
    }

    /// <summary>N キー: アンロック済み過去時代へ AI ダイブを要求します。</summary>
    [ContextMenu("Request Past Era Dive (N)")]
    public async Task RequestPastEraDiveAsync()
    {
        if (diveInFlight)
        {
            Debug.LogWarning("[ChronosCoordinateTester] 過去ダイブ AI 通信は既に実行中です。");
            return;
        }

        if (!chronosHub.IsEraUnlockedForStory(diveArchiveKey))
        {
            Debug.LogWarning(
                $"<color=#FF6B6B>[ChronosCoordinateTester] '{diveArchiveKey}' は未アンロックです。" +
                "先に H キーでワイプを実行してください。</color>");
            return;
        }

        string modeForRequest = diveMode;
        if (alternateDiveMode)
        {
            diveMode = diveMode == ChronosStoryDiveModes.Canon
                ? ChronosStoryDiveModes.Alter
                : ChronosStoryDiveModes.Canon;
        }

        diveInFlight = true;
        Debug.Log(
            $"<color=#00D4FF><b>[ChronosCoordinateTester]</b> 過去ダイブ開始: {diveArchiveKey} / モード={modeForRequest}</color>");

        try
        {
            PastStageContextResult result = await chronosHub.RequestPastStageContextFromAI(
                diveArchiveKey,
                modeForRequest);

            if (!result.Succeeded)
            {
                Debug.LogWarning($"[ChronosCoordinateTester] ダイブ失敗: {result.Message}");
            }
        }
        finally
        {
            diveInFlight = false;
        }
    }

    private void LogChronologyStatus()
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("<color=#7DF9FF><b>[ChronosCoordinate] 現在の世界座標</b></color>");
        sb.AppendLine($"  最新時代: {chronosHub.currentGlobalEra}");
        sb.AppendLine($"  最新場所: {chronosHub.currentGlobalLocation}");
        sb.AppendLine("  アンロック済み過去:");
        if (chronosHub.UnlockedErasForStory.Count == 0)
        {
            sb.AppendLine("    （なし — ワイプで時代がアーカイブされます）");
        }
        else
        {
            for (int i = 0; i < chronosHub.UnlockedErasForStory.Count; i++)
            {
                sb.AppendLine($"    - {chronosHub.UnlockedErasForStory[i]}");
            }
        }

        Debug.Log(sb.ToString());
    }
}

/// <summary>Play 開始時に ChronosCoordinate 系コンポーネントを自動配置します。</summary>
public static class ChronosCoordinateBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AttachToDebugSystemsHub()
    {
        GameObject hub = GameObject.Find("DebugSystemsHub");
        if (hub == null)
        {
            return;
        }

        if (hub.GetComponent<ChronosCoordinateHub>() == null)
        {
            hub.AddComponent<ChronosCoordinateHub>();
        }

        if (hub.GetComponent<ChronosCoordinateTester>() == null)
        {
            hub.AddComponent<ChronosCoordinateTester>();
        }
    }
}

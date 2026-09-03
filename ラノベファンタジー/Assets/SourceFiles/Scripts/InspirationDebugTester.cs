#if UNITY_EDITOR

using UnityEngine;
using UnityEditor;

/// <summary>
/// 閃きシステムのデバッグ用テスター（エディタ専用）。
/// 必要時のみ GameObject にアタッチして OnGUI ボタンで検証します。
/// </summary>
public class InspirationDebugTester : MonoBehaviour
{
    [SerializeField] private InspirationManager inspirationManager;
    [SerializeField] private PlayerSkillSlotManager skillSlotManager;

    [Header("OnGUI")]
    [SerializeField] private bool showDebugGui;

    private void Awake()
    {
        CacheReferences();
    }

    private void CacheReferences()
    {
        if (inspirationManager == null)
        {
            inspirationManager = GetComponent<InspirationManager>();
        }

        if (inspirationManager == null)
        {
            inspirationManager = InspirationManager.Instance;
        }

        if (skillSlotManager == null)
        {
            skillSlotManager = FindAnyObjectByType<PlayerSkillSlotManager>();
        }
    }

    private void PrintSkillSummary()
    {
        if (skillSlotManager == null)
        {
            CacheReferences();
        }

        if (skillSlotManager == null)
        {
            Debug.LogWarning("[InspirationDebugTester] PlayerSkillSlotManager が見つかりません。");
            return;
        }

        Debug.Log(skillSlotManager.GetSkillSummary());
    }

    private void OnGUI()
    {
        if (!showDebugGui)
        {
            return;
        }

        if (inspirationManager == null)
        {
            CacheReferences();
        }

        const float width = 240f;
        const float height = 26f;
        float x = 10f;
        float y = 10f;

        GUI.Box(new Rect(x, y, width + 10f, height * 7f + 30f), "閃きデバッグ（数字キー推奨）");
        y += 28f;

        if (inspirationManager != null)
        {
            if (GUI.Button(new Rect(x + 5f, y, width, height), "[5] トリガー1: 瞬歩"))
            {
                inspirationManager.InspireFlashStepFromNearMiss();
            }

            y += height + 3f;
            if (GUI.Button(new Rect(x + 5f, y, width, height), "[6] トリガー2: 絶境無音斬"))
            {
                inspirationManager.InspireSilentEdgeFromPinch();
            }

            y += height + 3f;
            if (GUI.Button(new Rect(x + 5f, y, width, height), "[7] トリガー3: フレイン合成"))
            {
                inspirationManager.TryCombineFlareSlash();
            }

            y += height + 3f;
            if (GUI.Button(new Rect(x + 5f, y, width, height), "[8] トリガー4: 覚醒イベント"))
            {
                inspirationManager.TriggerAwakeningEvent();
            }

            y += height + 3f;
            if (GUI.Button(new Rect(x + 5f, y, width, height), "[0] 閃きフラグリセット"))
            {
                inspirationManager.ResetInspirationFlags();
            }

            y += height + 3f;
        }
        else
        {
            GUI.Label(new Rect(x + 5f, y, width, height * 2f), "InspirationManager 未接続");
            y += height * 2f + 3f;
        }

        if (GUI.Button(new Rect(x + 5f, y, width, height), "[9] スキル一覧をログ出力"))
        {
            PrintSkillSummary();
        }
    }
}

/// <summary>エディタメニューから閃きデバッグ GUI の表示を切り替えます。</summary>
public static class InspirationDebugTesterMenu
{
    [MenuItem("Tools/Demo/Inspiration Debug GUI/Show")]
    private static void ShowGui()
    {
        SetGuiVisible(true);
    }

    [MenuItem("Tools/Demo/Inspiration Debug GUI/Hide")]
    private static void HideGui()
    {
        SetGuiVisible(false);
    }

    private static void SetGuiVisible(bool visible)
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("[InspirationDebugTester] Play モード中のみ切り替えできます。");
            return;
        }

        InspirationDebugTester tester = ResolveTester();
        SerializedObject serialized = new SerializedObject(tester);
        serialized.FindProperty("showDebugGui").boolValue = visible;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        Debug.Log($"[InspirationDebugTester] OnGUI を {(visible ? "表示" : "非表示")} にしました。");
    }

    private static InspirationDebugTester ResolveTester()
    {
        InspirationDebugTester tester = UnityEngine.Object.FindAnyObjectByType<InspirationDebugTester>();
        if (tester != null)
        {
            return tester;
        }

        InspirationManager manager = InspirationManager.Instance
            ?? UnityEngine.Object.FindAnyObjectByType<InspirationManager>();
        GameObject host = manager != null ? manager.gameObject : new GameObject("InspirationDebug");
        return host.GetComponent<InspirationDebugTester>() ?? host.AddComponent<InspirationDebugTester>();
    }
}

#endif

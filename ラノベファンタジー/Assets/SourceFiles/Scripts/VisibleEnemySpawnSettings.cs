using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

// =============================================================================
// 視覚的エネミー自動スポーン設定 — DebugSystemsHub から Inspector で ON/OFF
// 連携: VisibleEnemyBootstrap / CombatActionFeedbackBootstrap
// =============================================================================

/// <summary>
/// 視覚的エネミーの自動スポーン ON/OFF 設定。
/// DebugSystemsHub に配置し、Inspector から切り替えます。
/// </summary>
[AddComponentMenu("デバッグ/Visible Enemy Spawn Settings")]
[DisallowMultipleComponent]
public class VisibleEnemySpawnSettings : MonoBehaviour
{
    public static VisibleEnemySpawnSettings Instance { get; private set; }

    [Header("自動スポーン")]
    [Tooltip("OFF にすると Play 開始時の VisibleEnemy_Slime 自動生成を停止します。")]
    [SerializeField] private bool enableAutoSpawn = true;

    /// <summary>Play 開始時の自動スポーンを許可するか。</summary>
    public bool EnableAutoSpawn => enableAutoSpawn;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
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

    /// <summary>DebugSystemsHub 上の設定コンポーネントを保証して返します。</summary>
    public static VisibleEnemySpawnSettings EnsureOnHub()
    {
        if (Instance != null)
        {
            return Instance;
        }

        GameObject hub = GameObject.Find("DebugSystemsHub");
        if (hub == null)
        {
            Debug.LogWarning(
                "[VisibleEnemySpawnSettings] DebugSystemsHub が見つかりません。" +
                " Hierarchy で DebugSystemsHub を選択し、" +
                "Add Component → デバッグ → Visible Enemy Spawn Settings を追加してください。");
            return null;
        }

        RemoveBrokenScriptSlotsInEditor(hub);

        VisibleEnemySpawnSettings existing = hub.GetComponent<VisibleEnemySpawnSettings>();
        if (existing != null)
        {
            return existing;
        }

        VisibleEnemySpawnSettings created = hub.AddComponent<VisibleEnemySpawnSettings>();
        MarkHubDirtyInEditor(hub);
        Debug.Log(
            "<color=#A5D6A7><b>[VisibleEnemySpawnSettings]</b> DebugSystemsHub に配置しました。" +
            " Inspector の <b>Enable Auto Spawn</b> で敵の自動スポーンを切り替えられます。</color>");
        return created;
    }

    private static void MarkHubDirtyInEditor(GameObject hub)
    {
#if UNITY_EDITOR
        if (hub == null || Application.isPlaying)
        {
            return;
        }

        EditorUtility.SetDirty(hub);
        if (hub.scene.IsValid())
        {
            EditorSceneManager.MarkSceneDirty(hub.scene);
        }
#endif
    }

    private static void RemoveBrokenScriptSlotsInEditor(GameObject hub)
    {
#if UNITY_EDITOR
        if (hub == null || Application.isPlaying)
        {
            return;
        }

        int removed = GameObjectUtility.RemoveMonoBehavioursWithMissingScript(hub);
        if (removed > 0)
        {
            MarkHubDirtyInEditor(hub);
        }
#endif
    }
}

/// <summary>DebugSystemsHub へ VisibleEnemySpawnSettings を配置します。</summary>
public static class VisibleEnemySpawnBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoAttach()
    {
        AttachToDebugSystemsHub();
    }

    /// <summary>DebugSystemsHub に VisibleEnemySpawnSettings が無ければ追加します。</summary>
    public static void AttachToDebugSystemsHub()
    {
        VisibleEnemySpawnSettings.EnsureOnHub();
    }
}

#if UNITY_EDITOR
/// <summary>エディタ起動時に DebugSystemsHub へ設定コンポーネントを自動配置します。</summary>
[InitializeOnLoad]
internal static class VisibleEnemySpawnSettingsEditorInstaller
{
    static VisibleEnemySpawnSettingsEditorInstaller()
    {
        EditorApplication.delayCall += InstallWhenHubExists;
    }

    private static void InstallWhenHubExists()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            return;
        }

        VisibleEnemySpawnBootstrap.AttachToDebugSystemsHub();
    }
}
#endif

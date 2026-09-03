#if UNITY_EDITOR

using UnityEngine;
using UnityEditor;

/// <summary>
/// 部位システムのモック検証ランナー（エディタ専用）。
/// シーン配置は不要。Tools → Demo メニューまたは ContextMenu から実行します。
/// </summary>
public class EnemyPartSystemTestRunner : MonoBehaviour
{
    [SerializeField] private bool runOnStart;

    private void Start()
    {
        if (runOnStart)
        {
            EnemyPartSystemTest.RunInUnity();
        }
    }

    [ContextMenu("Run Enemy Part System Tests")]
    private void RunFromContextMenu()
    {
        EnemyPartSystemTest.RunInUnity();
    }
}

public static class EnemyPartSystemTestRunnerMenu
{
    [MenuItem("Tools/Demo/Run Enemy Part System Tests (Play Mode)")]
    private static void RunAllFromMenu()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("[EnemyPartSystemTestRunner] Play モード中のみ実行できます。");
            return;
        }

        EnemyPartSystemTest.RunInUnity();
    }
}

#endif

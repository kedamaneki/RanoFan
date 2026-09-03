using UnityEngine;

/// <summary>
/// シーンに配置して ContextMenu または Play 時に2軸レベルシステムテストを実行します。
/// </summary>
public class DualLevelSystemTestRunner : MonoBehaviour
{
    [SerializeField] private bool runOnStart;

    private void Start()
    {
        if (runOnStart)
        {
            DualLevelSystemTest.RunInUnity();
        }
    }

    [ContextMenu("Run Dual Level System Tests")]
    private void RunFromContextMenu()
    {
        DualLevelSystemTest.RunInUnity();
    }
}

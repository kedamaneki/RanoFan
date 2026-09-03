using UnityEngine;

/// <summary>
/// シーンに配置して ContextMenu または Play 時に重量・ワイプシステムテストを実行します。
/// </summary>
public class WeightAndWipeSystemTestRunner : MonoBehaviour
{
    [SerializeField] private bool runOnStart;

    private void Start()
    {
        if (runOnStart)
        {
            WeightAndWipeSystemTest.RunInUnity();
        }
    }

    [ContextMenu("Run Weight And Wipe System Tests")]
    private void RunFromContextMenu()
    {
        WeightAndWipeSystemTest.RunInUnity();
    }
}

using UnityEngine;

/// <summary>
/// シーンに配置して ContextMenu または Play 時にデータ収集テストを実行します。
/// </summary>
public class DataCollectionSystemTestRunner : MonoBehaviour
{
    [SerializeField] private bool runOnStart;

    private void Start()
    {
        if (runOnStart)
        {
            DataCollectionSystemTest.RunInUnity();
        }
    }

    [ContextMenu("Run Data Collection System Tests")]
    private void RunFromContextMenu()
    {
        DataCollectionSystemTest.RunInUnity();
    }
}

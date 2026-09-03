using UnityEngine;

/// <summary>
/// シーンに配置して ContextMenu または Play 時に蓄積ログプロンプトテストを実行します。
/// </summary>
public class AccumulatedLogPromptBuilderTestRunner : MonoBehaviour
{
    [SerializeField] private bool runOnStart;

    private void Start()
    {
        if (runOnStart)
        {
            AccumulatedLogPromptBuilderTest.RunInUnity();
        }
    }

    [ContextMenu("Run Accumulated Log Prompt Builder Tests")]
    private void RunFromContextMenu()
    {
        AccumulatedLogPromptBuilderTest.RunInUnity();
    }
}

using UnityEngine;

/// <summary>
/// シーンに配置して ContextMenu または Play 時に AIPromptBuilderSystem テストを実行します。
/// </summary>
public class AIPromptBuilderSystemTestRunner : MonoBehaviour
{
    [SerializeField] private bool runOnStart;

    private void Start()
    {
        if (runOnStart)
        {
            AIPromptBuilderSystemTest.RunInUnity();
        }
    }

    [ContextMenu("Run AI Prompt Builder System Tests")]
    private void RunFromContextMenu()
    {
        AIPromptBuilderSystemTest.RunInUnity();
    }
}

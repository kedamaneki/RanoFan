using UnityEngine;

/// <summary>
/// シーンに配置して ContextMenu または Play 時に GamePhase プロンプトテストを実行します。
/// </summary>
public class GamePhasePromptBuilderSystemTestRunner : MonoBehaviour
{
    [SerializeField] private bool runOnStart;

    private void Start()
    {
        if (runOnStart)
        {
            GamePhasePromptBuilderSystemTest.RunInUnity();
        }
    }

    [ContextMenu("Run GamePhase Prompt Builder System Tests")]
    private void RunFromContextMenu()
    {
        GamePhasePromptBuilderSystemTest.RunInUnity();
    }
}

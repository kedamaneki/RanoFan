using UnityEngine;

/// <summary>
/// シーンに配置して ContextMenu または Play 時に AI コネクターテストを実行します。
/// </summary>
public class AIGeneratorConnectorTestRunner : MonoBehaviour
{
    [SerializeField] private bool runOnStart;

    private void Start()
    {
        if (runOnStart)
        {
            AIGeneratorConnectorTest.RunInUnity();
        }
    }

    [ContextMenu("Run AI Generator Connector Tests")]
    private void RunFromContextMenu()
    {
        AIGeneratorConnectorTest.RunInUnity();
    }
}

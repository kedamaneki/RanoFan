using UnityEngine;

/// <summary>
/// シーンに配置して ContextMenu または Play 時に実 API 通信テストを実行します。
/// ※ LocalSecrets/gemini-api-key.txt に API キーを設定してから実行してください。
/// </summary>
public class RealAICommunicationSystemTestRunner : MonoBehaviour
{
    [SerializeField] private bool runOnStart;

    private void Start()
    {
        if (runOnStart)
        {
            RealAICommunicationSystemTest.RunInUnity();
        }
    }

    [ContextMenu("Run Real AI Communication System Tests")]
    private void RunFromContextMenu()
    {
        RealAICommunicationSystemTest.RunInUnity();
    }
}

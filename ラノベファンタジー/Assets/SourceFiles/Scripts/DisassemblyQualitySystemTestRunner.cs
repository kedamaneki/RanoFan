using UnityEngine;

/// <summary>
/// シーンに配置して ContextMenu または Play 時に解体品質システムテストを実行します。
/// </summary>
public class DisassemblyQualitySystemTestRunner : MonoBehaviour
{
    [SerializeField] private bool runOnStart;

    private void Start()
    {
        if (runOnStart)
        {
            DisassemblyQualitySystemTest.RunInUnity();
        }
    }

    [ContextMenu("Run Disassembly Quality System Tests")]
    private void RunFromContextMenu()
    {
        DisassemblyQualitySystemTest.RunInUnity();
    }
}

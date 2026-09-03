using UnityEngine;

/// <summary>
/// シーンに配置して ContextMenu または Play 時にスキル返還システムテストを実行します。
/// </summary>
public class SafeSkillRefundSystemTestRunner : MonoBehaviour
{
    [SerializeField] private bool runOnStart;

    private void Start()
    {
        if (runOnStart)
        {
            SafeSkillRefundSystemTest.RunInUnity();
        }
    }

    [ContextMenu("Run Safe Skill Refund System Tests")]
    private void RunFromContextMenu()
    {
        SafeSkillRefundSystemTest.RunInUnity();
    }
}

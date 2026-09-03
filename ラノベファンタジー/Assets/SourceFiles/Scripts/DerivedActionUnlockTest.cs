using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 派生技の習得・動作確認用デバッグコンポーネント。
/// 本番では AI 連携側が UnlockNewAction / UnlockNewActionFromJson を呼び出します。
/// PlayerRobot にアタッチして Play モードでテストしてください。
/// </summary>
public class DerivedActionUnlockTest : MonoBehaviour
{
    [SerializeField] private PlayerController playerController;

    [Header("テスト用キー")]
    [SerializeField] private Key unlockAttackKey = Key.F1;
    [SerializeField] private Key unlockEvadeKey = Key.F2;
    [SerializeField] private Key unlockFromJsonKey = Key.F3;

    private void Reset()
    {
        playerController = GetComponent<PlayerController>();
    }

    private void Awake()
    {
        if (playerController == null)
        {
            playerController = GetComponent<PlayerController>();
        }
    }

    private void Update()
    {
        if (playerController == null)
        {
            playerController = GetComponent<PlayerController>();
        }

        if (playerController == null)
        {
            return;
        }

        if (DebugHotkeyUtility.WasPressed(unlockAttackKey))
        {
            playerController.UnlockNewAction(DerivedActionData.CreateSampleAttack());
        }

        if (DebugHotkeyUtility.WasPressed(unlockEvadeKey))
        {
            playerController.UnlockNewAction(DerivedActionData.CreateSampleEvade());
        }

        if (DebugHotkeyUtility.WasPressed(unlockFromJsonKey))
        {
            string sampleJson = DerivedActionData.CreateSampleAttack().ToJson();
            playerController.UnlockNewActionFromJson(sampleJson);
        }
    }
}

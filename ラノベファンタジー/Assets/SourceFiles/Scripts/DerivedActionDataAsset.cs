using UnityEngine;

/// <summary>
/// 派生技データの ScriptableObject 版。
/// デザイナーが Inspector でプリセットを作る場合や、AI 生成前の仮データ置き場に使います。
/// ランタイムの動的習得は DerivedActionData（クラス）＋ UnlockNewAction を使用してください。
/// </summary>
[CreateAssetMenu(fileName = "NewDerivedAction", menuName = "Game/Derived Action Data")]
public class DerivedActionDataAsset : ScriptableObject
{
    [SerializeField] private DerivedActionData data = new DerivedActionData();

    public DerivedActionData Data => data;

    /// <summary>ScriptableObject の内容をランタイム用にコピーして返す</summary>
    public DerivedActionData CreateRuntimeCopy()
    {
        return data != null ? data.Clone() : null;
    }
}

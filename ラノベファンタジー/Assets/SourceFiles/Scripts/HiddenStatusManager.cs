using UnityEngine;

/// <summary>
/// プレイヤーの隠れた行動データ（裏ステータス）を管理するマネージャー。
/// 将来的に複数種類の裏ステータスをここに追加していく想定です。
/// </summary>
public class HiddenStatusManager : MonoBehaviour
{
    [Header("仮の裏ステータス")]
    [Tooltip("行動データの蓄積を表す仮の値（初期値 0）")]
    [SerializeField] private int dummyValue;

    /// <summary>現在の仮データ蓄積値（読み取り専用）</summary>
    public int DummyValue => dummyValue;

    /// <summary>
    /// 仮のデータ蓄積値を指定量だけ加算します。
    /// </summary>
    /// <param name="amount">加算する量</param>
    public void AddDummyValue(int amount)
    {
        if (amount == 0)
        {
            return;
        }

        dummyValue += amount;

        // 仮実装：加算後の合計値をログで確認
        Debug.Log($"[HiddenStatus] 仮データ蓄積値が {amount} 加算されました。現在値: {dummyValue}");
    }
}

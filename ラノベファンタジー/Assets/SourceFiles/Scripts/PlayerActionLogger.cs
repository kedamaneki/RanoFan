using UnityEngine;

/// <summary>
/// プレイヤーの基礎行動ログを一元管理するシングルトン。
/// ゲーム内に 1 つだけ配置し、各所から Instance 経由で記録します。
/// </summary>
public class PlayerActionLogger : MonoBehaviour
{
    public static PlayerActionLogger Instance { get; private set; }

    [Header("行動ログ（読み取り専用表示用）")]
    [SerializeField] private int totalAttacks;
    [SerializeField] private int totalHits;
    [SerializeField] private int enemyKills;
    [SerializeField] private int totalTakesDamage;
    [SerializeField] private int totalDeaths;
    [SerializeField] private int totalDodges;
    [SerializeField] private float totalMoveDistance;

    [Header("コンボ・タイミングログ（AI プロファイリング用）")]
    [Tooltip("前回攻撃から 0.3 秒未満で次の攻撃を押した回数（連打癖）")]
    [SerializeField] private int mashCount;

    [Tooltip("前回攻撃から 0.6〜1.2 秒の絶妙な間隔で繋いだ回数（ディレイ狙い）")]
    [SerializeField] private int delayAttackCount;

    [Tooltip("ステップ直後 0.3 秒以内に敵攻撃が接触した回数（ジャスト回避の試み）")]
    [SerializeField] private int nearMissCount;

    // 攻撃間隔の判定閾値（秒）
    private const float MashThresholdSeconds = 0.3f;
    private const float DelayMinSeconds = 0.6f;
    private const float DelayMaxSeconds = 1.2f;

    // 外部から現在値を参照できるプロパティ
    public int TotalAttacks => totalAttacks;
    public int TotalHits => totalHits;
    public int EnemyKills => enemyKills;
    public int TotalTakesDamage => totalTakesDamage;
    public int TotalDeaths => totalDeaths;
    public int TotalDodges => totalDodges;
    public float TotalMoveDistance => totalMoveDistance;
    public int MashCount => mashCount;
    public int DelayAttackCount => delayAttackCount;
    public int NearMissCount => nearMissCount;

    /// <summary>自動テスト用：ニアミス数を直接設定（InspirationSystemTester 専用）</summary>
    public void SetNearMissCountForTesting(int count)
    {
        nearMissCount = Mathf.Max(0, count);
    }

    private void Awake()
    {
        // シングルトン：既に存在する場合は自分を破棄
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("PlayerActionLogger が重複しています。余分なオブジェクトを削除します。");
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    /// <summary>総攻撃回数を +1</summary>
    public void LogAttack()
    {
        totalAttacks++;
        Debug.Log($"[行動ログ] 総攻撃回数: {totalAttacks}");
    }

    /// <summary>総ヒット回数を +1</summary>
    public void LogHit()
    {
        totalHits++;
        Debug.Log($"[行動ログ] 総ヒット回数: {totalHits}");
    }

    /// <summary>敵撃破数を +1</summary>
    public void LogKill()
    {
        enemyKills++;
        Debug.Log($"[行動ログ] 敵撃破数: {enemyKills}");
    }

    /// <summary>総被弾回数を +1</summary>
    public void LogTakeDamage()
    {
        totalTakesDamage++;
        Debug.Log($"[行動ログ] 総被弾回数: {totalTakesDamage}");
    }

    /// <summary>総死亡回数を +1</summary>
    public void LogDeath()
    {
        totalDeaths++;
        Debug.Log($"[行動ログ] 総死亡回数: {totalDeaths}");
    }

    /// <summary>総回避回数を +1</summary>
    public void LogDodge()
    {
        totalDodges++;
        Debug.Log($"[行動ログ] 総回避回数: {totalDodges}");
    }

    /// <summary>総移動距離に加算（メートル単位・水平移動のみ想定）</summary>
    /// <param name="distance">加算する移動距離</param>
    public void AddMoveDistance(float distance)
    {
        if (distance <= 0f)
        {
            return;
        }

        totalMoveDistance += distance;
    }

    /// <summary>
    /// 前回の攻撃開始からの経過時間を記録し、連打癖・ディレイ狙いを分類します。
    /// 初回攻撃（intervalTime が 0 未満）の場合は何も記録しません。
    /// </summary>
    /// <param name="intervalTime">前回攻撃開始からの経過秒数</param>
    public void LogAttackInterval(float intervalTime)
    {
        if (intervalTime < 0f)
        {
            return;
        }

        // 0.3 秒未満 → 連打（マッシュ）とみなす
        if (intervalTime < MashThresholdSeconds)
        {
            mashCount++;
            Debug.Log($"[行動ログ] 連打検知（間隔: {intervalTime:F2}秒）→ 連打癖: {mashCount}回");
            return;
        }

        // 0.6〜1.2 秒 → コンボが途切れない絶妙なディレイ狙い
        if (intervalTime >= DelayMinSeconds && intervalTime <= DelayMaxSeconds)
        {
            delayAttackCount++;
            Debug.Log($"[行動ログ] ディレイ攻撃検知（間隔: {intervalTime:F2}秒）→ ディレイ狙い: {delayAttackCount}回");
        }
    }

    /// <summary>
    /// ステップ回避直後のニアミス（敵攻撃が肉薄した／当たった）を記録します。
    /// </summary>
    public void LogNearMiss()
    {
        nearMissCount++;
        Debug.Log($"[行動ログ] ニアミス（ジャスト回避の試み）: {nearMissCount}回");

        // 閃きシステム：ブリッジ経由でログ監視型閃きを判定
        PlayerCombatInspirationBridge bridge = PlayerCombatInspirationBridge.Instance;
        if (bridge != null)
        {
            bridge.NotifyNearMissLogged();
        }
        else
        {
            InspirationManager.Instance?.TriggerLogInspiration();
        }
    }

    /// <summary>
    /// 蓄積されたログを AI 送信用の 1 本のテキストにまとめて返す
    /// </summary>
    public string GetPlayerProfileSummary()
    {
        float hitRate = totalAttacks > 0
            ? (float)totalHits / totalAttacks * 100f
            : 0f;

        return $"[プレイヤー行動ログ] 総攻撃: {totalAttacks}回, ヒット: {totalHits}回 (命中率: {hitRate:F0}%), " +
               $"撃破数: {enemyKills}回, 被弾数: {totalTakesDamage}回, 死亡数: {totalDeaths}回, " +
               $"回避数: {totalDodges}回, 総移動距離: {totalMoveDistance:F1}m | " +
               $"連打癖: {mashCount}回, ディレイ狙い: {delayAttackCount}回, ニアミス(肉薄): {nearMissCount}回";
    }
}

using UnityEngine;

/// <summary>
/// 超加速・世界遅延（クロックアップ）領域を管理するシングルトン。
/// Time.timeScale による世界スローと、プレイヤーの2大突破適応判定を担います。
/// </summary>
public class SpeedDomainManager : MonoBehaviour
{
    public static SpeedDomainManager Instance { get; private set; }

    /// <summary>敵領域への適応経路。</summary>
    public enum AdaptationRoute
    {
        /// <summary>適応失敗（世界スローに拘束）</summary>
        None = 0,
        /// <summary>技術型：思考加速×肉体加速</summary>
        Technical = 1,
        /// <summary>フィジカル型：レベル＋表ボーナス合計でのごり押し</summary>
        Physical = 2
    }

    public const string CasterPlayer = "Player";
    public const string CasterEnemy = "Enemy";

    [Header("世界遅延パラメーター")]
    [Tooltip("超加速発動時の Time.timeScale（0.05 = 20倍スロー）")]
    [SerializeField] private float slowedWorldTimeScale = 0.05f;

    [Tooltip("通常時の Fixed Timestep 基準値")]
    [SerializeField] private float baseFixedDeltaTime = 0.02f;

    [Header("技術型突破（ルートA）")]
    [Tooltip("思考加速スキル所持時に必要な最低 speedBonus")]
    [SerializeField] private int technicalRouteMinSpeedBonus = 10;

    [Header("フィジカル型突破（ルートB）")]
    [Tooltip("レベル＋表ボーナス合計がこの値以上で肉体ごり押し適応")]
    [SerializeField] private int physicalRouteThreshold = 100;

    [Header("ランタイム状態（読み取り専用）")]
    [SerializeField] private bool isWorldSlowed;
    [SerializeField] private string currentCaster = string.Empty;
    [SerializeField] private bool isPlayerAdaptedToEnemyDomain;
    [SerializeField] private AdaptationRoute lastAdaptationRoute = AdaptationRoute.None;

    private float playerLocalTimeMultiplier = 1f;

    /// <summary>世界がスローモーション化しているか。</summary>
    public bool IsWorldSlowed => isWorldSlowed;

    /// <summary>超加速を発動した側（Player / Enemy）。</summary>
    public string CurrentCaster => currentCaster;

    /// <summary>敵領域にプレイヤーが適応できているか。</summary>
    public bool IsPlayerAdaptedToEnemyDomain => isPlayerAdaptedToEnemyDomain;

    /// <summary>直近の適応経路。</summary>
    public AdaptationRoute LastAdaptationRoute => lastAdaptationRoute;

    /// <summary>
    /// プレイヤー個人の時間倍率。
    /// 適応成功時は世界スローを相殺し実質通常速度（≈20倍）で動ける器。
    /// </summary>
    public float PlayerLocalTimeMultiplier => playerLocalTimeMultiplier;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[SpeedDomainManager] 重複インスタンスを検出しました。");
        }
        else
        {
            Instance = this;
        }
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }

        ForceResetTimeScaleInternal(log: false);
    }

    private void OnApplicationQuit()
    {
        ForceResetTimeScaleInternal(log: false);
    }

    /// <summary>
    /// 超加速領域を発動し、世界の時間スケールを低下させます。
    /// </summary>
    /// <param name="caster">発動者識別子（Player / Enemy）</param>
    public void TriggerChronostasis(string caster)
    {
        currentCaster = string.IsNullOrWhiteSpace(caster) ? "Unknown" : caster;
        isWorldSlowed = true;
        ApplyTimeScale(slowedWorldTimeScale);
        ChronostasisLog.LogChronostasisTriggered(currentCaster, slowedWorldTimeScale);
    }

    /// <summary>世界の時間スケールを通常に戻し、領域状態をクリアします。</summary>
    public void ResetTimeScale()
    {
        ForceResetTimeScaleInternal(log: true);
    }

    /// <summary>
    /// プレイヤーが敵の超加速領域に適応できるかを2大突破ロジックで判定します。
    /// </summary>
    public bool CanPlayerAdaptToEnemySpeed(PlayerStatusManager playerStatus, bool hasMindAccelerationSkill)
    {
        return EvaluatePlayerAdaptation(playerStatus, hasMindAccelerationSkill) != AdaptationRoute.None;
    }

    /// <summary>
    /// 敵の超加速を発動し、プレイヤー適応判定まで一括実行します（プロトタイプ用）。
    /// </summary>
    public AdaptationRoute SimulateEnemyChronostasisAgainstPlayer(
        PlayerStatusManager playerStatus, bool hasMindAccelerationSkill)
    {
        TriggerChronostasis(CasterEnemy);

        AdaptationRoute route = EvaluatePlayerAdaptation(playerStatus, hasMindAccelerationSkill);
        ApplyPlayerAdaptationResult(route, playerStatus);
        return route;
    }

    /// <summary>
    /// プレイヤーが敵領域下で実質的に「通常速度」で動けるか（移動・アニメ・入力制御の参照用）。
    /// </summary>
    public bool IsPlayerEffectivelyUnbound()
    {
        return isPlayerAdaptedToEnemyDomain &&
               isWorldSlowed &&
               currentCaster == CasterEnemy;
    }

    /// <summary>
    /// プレイヤー用のデルタタイム倍率。適応成功時は世界スローを相殺します。
    /// </summary>
    public float GetPlayerDeltaTimeMultiplier()
    {
        if (!isWorldSlowed || currentCaster != CasterEnemy)
        {
            return 1f;
        }

        return isPlayerAdaptedToEnemyDomain ? playerLocalTimeMultiplier : 1f;
    }

    /// <summary>技術型突破に必要な最低 speedBonus（テスト・UI 参照用）。</summary>
    public int TechnicalRouteMinSpeedBonus => technicalRouteMinSpeedBonus;

    /// <summary>フィジカル型突破の閾値（テスト・UI 参照用）。</summary>
    public int PhysicalRouteThreshold => physicalRouteThreshold;

    private AdaptationRoute EvaluatePlayerAdaptation(
        PlayerStatusManager playerStatus, bool hasMindAccelerationSkill)
    {
        if (playerStatus == null)
        {
            return AdaptationRoute.None;
        }

        if (hasMindAccelerationSkill && playerStatus.SpeedBonus >= technicalRouteMinSpeedBonus)
        {
            return AdaptationRoute.Technical;
        }

        if (playerStatus.GetPhysicalAdaptationScore() >= physicalRouteThreshold)
        {
            return AdaptationRoute.Physical;
        }

        return AdaptationRoute.None;
    }

    private void ApplyPlayerAdaptationResult(AdaptationRoute route, PlayerStatusManager playerStatus)
    {
        lastAdaptationRoute = route;
        isPlayerAdaptedToEnemyDomain = route != AdaptationRoute.None;

        if (route == AdaptationRoute.Technical)
        {
            playerLocalTimeMultiplier = 1f / slowedWorldTimeScale;
            ChronostasisLog.LogTechnicalAdaptation(playerStatus.SpeedBonus);
        }
        else if (route == AdaptationRoute.Physical)
        {
            playerLocalTimeMultiplier = 1f / slowedWorldTimeScale;
            ChronostasisLog.LogPhysicalAdaptation(
                playerStatus.GetPhysicalAdaptationScore(), physicalRouteThreshold);
        }
        else
        {
            playerLocalTimeMultiplier = 1f;
            ChronostasisLog.LogAdaptationFailed();
        }
    }

    private void ApplyTimeScale(float timeScale)
    {
        Time.timeScale = timeScale;
        Time.fixedDeltaTime = baseFixedDeltaTime * timeScale;
    }

    private void ForceResetTimeScaleInternal(bool log)
    {
        isWorldSlowed = false;
        currentCaster = string.Empty;
        isPlayerAdaptedToEnemyDomain = false;
        lastAdaptationRoute = AdaptationRoute.None;
        playerLocalTimeMultiplier = 1f;
        ApplyTimeScale(1f);

        if (log)
        {
            ChronostasisLog.LogTimeScaleReset();
        }
    }
}

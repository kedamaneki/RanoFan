using UnityEngine;

// =============================================================================
// ワールド「町の中」判定 — 安全地帯フラグの中央ゲート
// 連携: CraftingExperimentHub / DemoTimeLineManager / InGameVisualUIManager
// =============================================================================

/// <summary>
/// プレイヤーが町（安全地帯）にいるかどうかのワールド判定を一元管理します。
/// 生産開始時の isSafetyZone リクエストと組み合わせて最終的な安全地帯を決定します。
/// </summary>
public static class TownSafetyZoneGate
{
    /// <summary>現在、ワールド上で「町の中」と判定されているか（デフォルト: 屋外）。</summary>
    public static bool IsInsideTown { get; private set; }

    /// <summary>町の中判定を設定し、進行中の生産セッションがあれば安全地帯を再評価します。</summary>
    /// <param name="insideTown">true で町内、false で屋外</param>
    public static void SetInsideTown(bool insideTown)
    {
        if (IsInsideTown == insideTown)
        {
            return;
        }

        IsInsideTown = insideTown;

        string stateLabel = insideTown ? "町の中（安全地帯）" : "屋外（被弾リスク）";
        Debug.Log(
            "<color=#81D4FA><b>【町判定】</b></color> " +
            $"<color=#E1F5FE>エリア判定を <b>{stateLabel}</b> に切り替えました。</color>");

        CraftingExperimentHub hub = CraftingExperimentHub.Instance;
        hub?.RefreshEffectiveSafetyZoneFromWorld();
    }

    /// <summary>町の中判定を反転します。</summary>
    public static void ToggleInsideTown()
    {
        SetInsideTown(!IsInsideTown);
    }

    /// <summary>
    /// 生産開始リクエストとワールド町判定から、実際に適用する安全地帯フラグを解決します。
    /// </summary>
    /// <param name="requestedTownSafety">C/P 等で街モードを要求したか</param>
    public static bool ResolveEffectiveSafetyZone(bool requestedTownSafety)
    {
        if (!requestedTownSafety)
        {
            return false;
        }

        return IsInsideTown;
    }

    /// <summary>例外パケット・UI 用の場所ラベルを返します。</summary>
    /// <param name="effectiveSafetyZone">最終的な安全地帯フラグ</param>
    public static string ResolveAreaLabel(bool effectiveSafetyZone)
    {
        if (!effectiveSafetyZone)
        {
            return "屋外（携帯キット）";
        }

        if (ChronosCoordinateHub.Instance != null)
        {
            return ChronosCoordinateHub.Instance.currentGlobalLocation;
        }

        return "街の工房";
    }

    /// <summary>現在の町判定状態を Rich Text で返します。</summary>
    public static string BuildStatusRichText()
    {
        string area = IsInsideTown ? "町の中" : "屋外";
        string color = IsInsideTown ? "#A5D6A7" : "#FFAB40";
        return $"<color={color}><b>町判定: {area}</b></color> " +
               "<color=#B0BEC5>（\\ キーで切替 / C・P の街モードは町内のみ安全）</color>";
    }
}

/// <summary>Play 開始時に DebugSystemsHub へ町判定の初期状態をログ出力します。</summary>
public static class TownSafetyZoneBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void LogInitialState()
    {
        Debug.Log(
            "<color=#81D4FA><b>[TownSafetyZoneGate]</b></color> " +
            "初期状態: <b>屋外</b>（全域が町扱いになりません）。\\ キーで町の中判定を切替できます。");
    }
}

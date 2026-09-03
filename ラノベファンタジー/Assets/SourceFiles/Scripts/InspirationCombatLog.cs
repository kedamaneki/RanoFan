using UnityEngine;

/// <summary>
/// 閃き成功時のコンソールログ出力（仮UI）。本番 UI 実装前の確認用です。
/// </summary>
public static class InspirationCombatLog
{
    private const string GoldColor = "#FFD700";
    private const string CyanColor = "#00E5FF";
    private const string MasteryColor = "#B388FF";

    /// <summary>ピンチ型閃き成功</summary>
    public static void LogPinchInspiration(string actionName)
    {
        Debug.Log(
            $"<color={GoldColor}><b>【閃き】プレイヤーは極限状態から『{actionName}』を閃いた！</b></color>");

        RuntimeInGameUIManager.EnsureInstance().ShowInspirationPopup(
            $"ピキーン! {actionName}",
            "極限状態から閃いた受動スキル",
            RuntimeUIThemeColors.UniqueGold);
    }

    /// <summary>ログ監視型（ニアミス累計）閃き成功</summary>
    public static void LogNearMissInspiration(string actionName, int nearMissCount)
    {
        Debug.Log(
            $"<color={CyanColor}><b>【閃き】プレイヤーは生死の境を{nearMissCount}度渡り『{actionName}』を閃いた！</b></color>");

        RuntimeInGameUIManager.EnsureInstance().ShowInspirationPopup(
            $"ピキーン! {actionName}",
            $"生死の境を{nearMissCount}度超えたログ監視型閃き",
            RuntimeUIThemeColors.TechnicalCyan);
    }

    /// <summary>技の極意到達（閃きの種獲得）</summary>
    public static void LogMasteryAchieved(string actionName, string actionId)
    {
        Debug.Log(
            $"<color={MasteryColor}><b>【極意】プレイヤーは『{actionName}（{actionId}）』の真髄を理解し、新たな閃きの種を得た！</b></color>");

        if (DemoInputGate.ShouldSuppressInspirationPopup())
        {
            return;
        }

        RuntimeInGameUIManager.EnsureInstance().ShowInspirationPopup(
            $"極意 — {actionName}",
            "新たな閃きの種を獲得",
            RuntimeUIThemeColors.MasteryPurple);
    }

    /// <summary>能動的・技開発失敗（素材技の極意未到達）</summary>
    public static void LogDevelopmentMasteryRequired(string actionName)
    {
        Debug.Log(
            $"<color=#FF8A65><b>【技開発失敗】素材となる技『{actionName}』の極意（真髄）をまだ掴んでいないため、新しい術理を編み出すことができない……！</b></color>");
    }

    private const string DevelopmentColor = "#00E676";

    /// <summary>能動的・技開発成功（受動的閃きとは別系統）</summary>
    public static void LogActiveActionDevelopment(string actionName, string actionId)
    {
        Debug.Log(
            $"<color={DevelopmentColor}><b>【技開発】技術が融合し、新たな術理『{actionName}（{actionId}）』を能動的に開発した！</b></color>");

        RuntimeInGameUIManager.EnsureInstance().ShowInspirationPopup(
            $"ピキーン! {actionName}",
            $"能動的技開発成功（{actionId}）",
            RuntimeUIThemeColors.DevelopmentGreen);
    }
}

using UnityEngine;

/// <summary>
/// スキルショップ関連の Rich Text コンソールログ（仮 UI）。
/// </summary>
public static class SkillShopLog
{
    private const string ShopColor = "#4FC3F7";
    private const string OrbColor = "#CE93D8";
    private const string HeatColor = "#FFB74D";

    /// <summary>ショップ棚更新</summary>
    public static void LogShopRefreshed(int lineupCount, int totalSoldByPlayer)
    {
        Debug.Log(
            $"<color={ShopColor}><b>【スキルショップ】棚を更新しました（{lineupCount} 点陳列 / 売却累計:{totalSoldByPlayer}）</b></color>");

        RuntimeInGameUIManager.EnsureInstance().ShowShopNotification(
            $"ショップ更新 — {lineupCount} 点陳列（熱量 {totalSoldByPlayer}）");
    }

    /// <summary>銘入りオーブの陳列</summary>
    public static void LogOrbListed(SkillOrbData orb)
    {
        if (orb == null)
        {
            return;
        }

        Debug.Log(
            $"<color={OrbColor}>  ★{orb.rarity} [{orb.skillID}] 銘:「{orb.creatorName}」 Lv.{orb.skillLevelAtExtraction}</color>");
    }

    /// <summary>プレイヤー売却</summary>
    public static void LogOrbSold(SkillOrbData orb, int totalSoldByPlayer)
    {
        if (orb == null)
        {
            return;
        }

        Debug.Log(
            $"<color={HeatColor}><b>【スキルショップ】「{orb.creatorName}」のオーブを売却（世界の熱量: {totalSoldByPlayer}）</b></color>");
    }

    /// <summary>オーブ生成</summary>
    public static void LogOrbExtracted(SkillOrbData orb)
    {
        if (orb == null)
        {
            return;
        }

        Debug.Log(
            $"<color={OrbColor}><b>【スキルオーブ】{orb.creatorName} が Lv.{orb.skillLevelAtExtraction} の技術を orb 化（★{orb.rarity}）</b></color>");
    }

    private const string PurchaseColor = "#81C784";

    /// <summary>オーブ購入成功</summary>
    public static void LogOrbPurchased(SkillOrbData orb, int goldCost)
    {
        if (orb == null)
        {
            return;
        }

        Debug.Log(
            $"<color={PurchaseColor}><b>【ショップ】{orb.creatorName} の『オーブ（{orb.skillID}）』を {goldCost} ゴールドで購入した！</b></color>");
    }

    /// <summary>専門職 NPC によるオーブ装着</summary>
    public static void LogOrbApplied(SkillOrbData orb, string skillName)
    {
        if (orb == null)
        {
            return;
        }

        Debug.Log(
            $"<color={OrbColor}><b>【スキル装着】専門職が {orb.creatorName} 銘のオーブを【{skillName}】へ装着した！</b></color>");
    }

    private const string CombineColor = "#FF7043";

    /// <summary>専門職 NPC によるスキル合成</summary>
    public static void LogSkillCombined(string resultSkillName, string resultSkillId)
    {
        Debug.Log(
            $"<color={CombineColor}><b>【スキル合成】専門職の技により、2つのオーブが融合して新たなスキル『{resultSkillName}（{resultSkillId}）』が誕生した！</b></color>");
    }

    private const string RejectColor = "#EF5350";
    private const string BonusColor = "#66BB6A";
    private const string PremiumColor = "#FFA726";

    /// <summary>カルマ極低による門前払い</summary>
    public static void LogShopRejectedByKarma(int karmaValue, int threshold)
    {
        Debug.Log(
            $"<color={RejectColor}><b>【ショップ】「あんたのような悪党に売る物はねえ！」" +
            $"商人はあなたを睨みつけ、取引を拒否した。（カルマ {karmaValue} / 閾値 {threshold}）</b></color>");
    }

    /// <summary>高信頼度プレイヤーへの購入おまけ</summary>
    public static void LogTrustBonusGift(SkillOrbData bonusOrb, float trustFactor)
    {
        if (bonusOrb == null)
        {
            return;
        }

        Debug.Log(
            $"<color={BonusColor}><b>【ショップ】「いつも懇意にしてくれるお礼だ、これを持っていきな！」" +
            $"商人からおまけのオーブ（★{bonusOrb.rarity} [{bonusOrb.skillID}]）を貰った！（信頼度 {trustFactor:F2}）</b></color>");

        RuntimeInGameUIManager.EnsureInstance().ShowShopNotification(
            $"おまけ獲得！ ★{bonusOrb.rarity} [{bonusOrb.skillID}]（信頼 {trustFactor:F1}）");
    }

    /// <summary>高信頼度プレイヤーへの高価買取査定</summary>
    public static void LogOrbSoldPremiumAppraisal(
        SkillOrbData orb, int totalSoldByPlayer, int heatGain, float trustFactor)
    {
        if (orb == null)
        {
            return;
        }

        Debug.Log(
            $"<color={PremiumColor}><b>【ショップ】「いつもの御厚意への礼だ、高く買い取ろう！」" +
            $"「{orb.creatorName}」のオーブを特別査定（熱量 +{heatGain} → 累計 {totalSoldByPlayer} / 信頼度 {trustFactor:F2}）</b></color>");
    }
}

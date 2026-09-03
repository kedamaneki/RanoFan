using System;
using System.Collections.Generic;
using System.Text;

#if UNITY_5_3_OR_NEWER
using UnityEngine;
#endif

// =============================================================================
// 死体収納・解体 × 被弾ヒット数による素材品質（Quality）劣化
// 単一ファイルでコピー可能。DisassemblyQualitySystemTest.Main() でモック検証。
// =============================================================================

/// <summary>エネミーのランク（素材劣化の激しさに影響）。</summary>
public enum EnemyRank
{
    /// <summary>通常雑魚。劣化比率 0.01x</summary>
    Normal,
    /// <summary>硬質敵。劣化比率 0.5x</summary>
    Armored,
    /// <summary>強敵・大型ボス。劣化比率 2.0x</summary>
    Boss
}

/// <summary>抽出素材のレアリティ。</summary>
public enum MaterialRarity
{
    Common,
    Rare,
    Phantom
}

/// <summary>ランクごとのヒット数→品質劣化比率。</summary>
public static class EnemyRankDegradationTable
{
    public static float GetDegradationRate(EnemyRank rank)
    {
        switch (rank)
        {
            case EnemyRank.Normal: return 0.01f;
            case EnemyRank.Armored: return 0.5f;
            case EnemyRank.Boss: return 2.0f;
            default:
                throw new ArgumentOutOfRangeException(nameof(rank), rank, null);
        }
    }
}

/// <summary>品質計算の定数。</summary>
public static class DisassemblyQualityConstants
{
    public const float BaseQuality = 100f;
    public const float MinQuality = 0f;
    public const float MaxQuality = 100f;
    public const float DisassemblySkillBonusPerLevel = 2f;

    /// <summary>この品質以上で「高鮮度」扱い（シナリオ検証用）。</summary>
    public const float HighFreshnessThreshold = 90f;

    /// <summary>この品質未満は部位破壊していてもコモン止まり。</summary>
    public const float RuinedQualityThreshold = 30f;
}

/// <summary>戦闘中の被弾を記録し、死体として保持するデータ。</summary>
public sealed class DroppedCorpse
{
    public string EnemyName { get; }
    public EnemyRank Rank { get; }
    public int TotalHitCount { get; private set; }

    /// <summary>部位破壊フラグ（前回システム連携）。レア素材ドロップ判定に使用。</summary>
    public bool HasDestroyedPart { get; private set; }

    public string DestroyedPartName { get; private set; }

    public DroppedCorpse(string enemyName, EnemyRank rank)
    {
        EnemyName = enemyName ?? string.Empty;
        Rank = rank;
    }

    /// <summary>戦闘中：被弾ヒットを1回記録します。</summary>
    public void RegisterHit()
    {
        TotalHitCount++;
    }

    /// <summary>戦闘中：指定回数分のヒットを一括記録（テスト用）。</summary>
    public void RegisterHits(int hitCount)
    {
        if (hitCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(hitCount));
        }

        TotalHitCount += hitCount;
    }

    /// <summary>部位破壊を記録します（大型ボス等）。</summary>
    public void MarkPartDestroyed(string partName)
    {
        HasDestroyedPart = true;
        DestroyedPartName = partName ?? string.Empty;
    }
}

/// <summary>解体で得られる素材。</summary>
public sealed class HarvestedMaterial
{
    public string MaterialName { get; }
    public float Quality { get; }
    public MaterialRarity Rarity { get; }

    public HarvestedMaterial(string materialName, float quality, MaterialRarity rarity)
    {
        MaterialName = materialName ?? string.Empty;
        Quality = ClampQuality(quality);
        Rarity = rarity;
    }

    private static float ClampQuality(float quality)
    {
        return Math.Max(
            DisassemblyQualityConstants.MinQuality,
            Math.Min(DisassemblyQualityConstants.MaxQuality, quality));
    }

    public override string ToString()
    {
        return $"{MaterialName} [品質:{Quality:F1}] ({Rarity})";
    }
}

/// <summary>品質計算結果。</summary>
public readonly struct QualityCalculationResult
{
    public float FinalQuality { get; }
    public float DegradationAmount { get; }
    public float SkillBonus { get; }

    public QualityCalculationResult(float finalQuality, float degradationAmount, float skillBonus)
    {
        FinalQuality = finalQuality;
        DegradationAmount = degradationAmount;
        SkillBonus = skillBonus;
    }
}

/// <summary>
/// 被弾ヒット数とエネミーランクから素材品質を算出します。
/// 最終品質 = 100 - (総被弾ヒット数 × 劣化比率) + (解体スキルLv × 2)
/// </summary>
public static class DisassemblyQualityCalculator
{
    public static QualityCalculationResult Calculate(
        int totalHitCount,
        EnemyRank rank,
        int disassemblySkillLevel)
    {
        float rate = EnemyRankDegradationTable.GetDegradationRate(rank);
        float degradation = totalHitCount * rate;
        float skillBonus = disassemblySkillLevel * DisassemblyQualityConstants.DisassemblySkillBonusPerLevel;
        float raw = DisassemblyQualityConstants.BaseQuality - degradation + skillBonus;
        float final = Clamp(raw);

        return new QualityCalculationResult(final, degradation, skillBonus);
    }

    public static float Clamp(float quality)
    {
        return Math.Max(
            DisassemblyQualityConstants.MinQuality,
            Math.Min(DisassemblyQualityConstants.MaxQuality, quality));
    }
}

/// <summary>部位破壊フラグと品質に応じた素材ドロップ判定。</summary>
public static class MaterialDropResolver
{
    /// <summary>解体結果の素材リストを生成します。</summary>
    public static List<HarvestedMaterial> ResolveMaterials(DroppedCorpse corpse, float finalQuality)
    {
        if (corpse == null)
        {
            throw new ArgumentNullException(nameof(corpse));
        }

        List<HarvestedMaterial> materials = new List<HarvestedMaterial>();

        materials.Add(new HarvestedMaterial(
            $"{corpse.EnemyName}の基本素材",
            finalQuality,
            MaterialRarity.Common));

        if (!corpse.HasDestroyedPart)
        {
            return materials;
        }

        if (finalQuality >= DisassemblyQualityConstants.HighFreshnessThreshold)
        {
            materials.Add(new HarvestedMaterial(
                $"幻の超レア核（{corpse.DestroyedPartName}）",
                finalQuality,
                MaterialRarity.Phantom));
        }
        else if (finalQuality >= DisassemblyQualityConstants.RuinedQualityThreshold)
        {
            materials.Add(new HarvestedMaterial(
                $"部位レア片（{corpse.DestroyedPartName}）",
                finalQuality,
                MaterialRarity.Rare));
        }
        else
        {
            materials.Add(new HarvestedMaterial(
                $"ボロボロの欠片（{corpse.DestroyedPartName}）",
                finalQuality,
                MaterialRarity.Common));
        }

        return materials;
    }
}

/// <summary>解体処理の結果。</summary>
public readonly struct DisassemblyResult
{
    public DroppedCorpse SourceCorpse { get; }
    public QualityCalculationResult QualityCalc { get; }
    public IReadOnlyList<HarvestedMaterial> Materials { get; }

    public DisassemblyResult(
        DroppedCorpse sourceCorpse,
        QualityCalculationResult qualityCalc,
        IReadOnlyList<HarvestedMaterial> materials)
    {
        SourceCorpse = sourceCorpse;
        QualityCalc = qualityCalc;
        Materials = materials ?? Array.Empty<HarvestedMaterial>();
    }
}

/// <summary>死体収納・解体スキルによる素材抽出システム。</summary>
public static class DisassemblySystem
{
    /// <summary>
    /// 死体を解体し、品質計算と素材ドロップを実行します。
    /// </summary>
    public static DisassemblyResult Disassemble(
        DroppedCorpse corpse,
        int disassemblySkillLevel,
        Action<string> log = null)
    {
        if (corpse == null)
        {
            throw new ArgumentNullException(nameof(corpse));
        }

        QualityCalculationResult qualityCalc = DisassemblyQualityCalculator.Calculate(
            corpse.TotalHitCount,
            corpse.Rank,
            disassemblySkillLevel);

        log?.Invoke(
            $"[解体] {corpse.EnemyName}（{corpse.Rank}）被弾 {corpse.TotalHitCount} 回 " +
            $"→ 品質 {qualityCalc.FinalQuality:F1} " +
            $"(劣化 -{qualityCalc.DegradationAmount:F1}, スキル補正 +{qualityCalc.SkillBonus:F1})");

        List<HarvestedMaterial> materials = MaterialDropResolver.ResolveMaterials(
            corpse,
            qualityCalc.FinalQuality);

        for (int i = 0; i < materials.Count; i++)
        {
            log?.Invoke($"  抽出: {materials[i]}");
        }

        return new DisassemblyResult(corpse, qualityCalc, materials);
    }
}

/// <summary>モック実行・品質劣化シナリオ検証用エントリポイント。</summary>
public static class DisassemblyQualitySystemTest
{
    private const int DefaultDisassemblySkillLevel = 0;

    public static void Main()
    {
        RunAllScenarios(Console.WriteLine);
    }

    public static void RunAllScenarios(Action<string> log)
    {
        if (log == null)
        {
            log = _ => { };
        }

        log("===== DisassemblyQualitySystemTest 開始 =====");
        RunScenarioA_NormalEnemy(log);
        RunScenarioB_BossMudFight(log);
        RunScenarioC_BossPerfectKill(log);
        log("===== DisassemblyQualitySystemTest 完了 =====");
    }

    /// <summary>
    /// A: 通常エネミーを双剣50回滅多刺し — 品質は90以上をキープ。
    /// </summary>
    private static void RunScenarioA_NormalEnemy(Action<string> log)
    {
        log(string.Empty);
        log("--- シチュエーションA：通常エネミー（Normal）× 双剣50回滅多刺し ---");

        DroppedCorpse corpse = new DroppedCorpse("森のゴブリン", EnemyRank.Normal);
        corpse.RegisterHits(50);

        DisassemblyResult result = DisassemblySystem.Disassemble(corpse, DefaultDisassemblySkillLevel, log);

        AssertScenario(
            log,
            "A: 通常敵は品質維持",
            result.QualityCalc.FinalQuality >= DisassemblyQualityConstants.HighFreshnessThreshold,
            $"品質 {result.QualityCalc.FinalQuality:F1}（50hit×0.01=0.5 劣化のみ）");
    }

    /// <summary>
    /// B: 大型ボスを弓・双剣150回の泥仕合 — 品質0、部位破壊でもコモン止まり。
    /// </summary>
    private static void RunScenarioB_BossMudFight(Action<string> log)
    {
        log(string.Empty);
        log("--- シチュエーションB：大型ボス（Boss）× 手数武器150回の泥仕合 ---");
        log("  戦闘経過: 弓と双剣で小突き続け、装甲前脚は破壊したが死体はボロボロ");

        DroppedCorpse corpse = new DroppedCorpse("岩甲の巨獣", EnemyRank.Boss);
        corpse.RegisterHits(150);
        corpse.MarkPartDestroyed("装甲前脚");

        DisassemblyResult result = DisassemblySystem.Disassemble(corpse, DefaultDisassemblySkillLevel, log);

        bool onlyCommonRare = true;
        MaterialRarity? bestRarity = null;
        for (int i = 0; i < result.Materials.Count; i++)
        {
            if (result.Materials[i].Rarity == MaterialRarity.Phantom ||
                result.Materials[i].Rarity == MaterialRarity.Rare)
            {
                onlyCommonRare = false;
            }

            if (bestRarity == null || result.Materials[i].Rarity > bestRarity)
            {
                bestRarity = result.Materials[i].Rarity;
            }
        }

        log(string.Empty);
        log("  【理不尽】部位破壊の成果が品質0に飲み込まれた。幻の核は幻のまま消えた。");

        AssertScenario(
            log,
            "B: ボス泥仕合は品質0",
            result.QualityCalc.FinalQuality <= DisassemblyQualityConstants.MinQuality,
            $"品質 {result.QualityCalc.FinalQuality:F1}（150×2.0=300 劣化）");

        AssertScenario(
            log,
            "B: 部位破壊でもコモン止まり",
            onlyCommonRare && bestRarity == MaterialRarity.Common,
            "ボロボロの欠片（Common）のみ");
    }

    /// <summary>
    /// C: 大型ボスを5ヒットの一撃必殺 — 品質90以上、幻の超レア大成功。
    /// </summary>
    private static void RunScenarioC_BossPerfectKill(Action<string> log)
    {
        log(string.Empty);
        log("--- シチュエーションC：大型ボス（Boss）× 特大武器5ヒットの美討伐 ---");
        log("  戦闘経過: 最小手数で弱点を貫き、核心部を完璧に破壊");

        DroppedCorpse corpse = new DroppedCorpse("岩甲の巨獣", EnemyRank.Boss);
        corpse.RegisterHits(5);
        corpse.MarkPartDestroyed("巨獣の核心");

        DisassemblyResult result = DisassemblySystem.Disassemble(corpse, DefaultDisassemblySkillLevel, log);

        bool hasPhantom = false;
        for (int i = 0; i < result.Materials.Count; i++)
        {
            if (result.Materials[i].Rarity == MaterialRarity.Phantom)
            {
                hasPhantom = true;
            }
        }

        log(string.Empty);
        log("  【熱狂】最高鮮度の死体から、幻の超レア核の抽出に大成功！");

        AssertScenario(
            log,
            "C: 美討伐は高品質",
            result.QualityCalc.FinalQuality >= DisassemblyQualityConstants.HighFreshnessThreshold,
            $"品質 {result.QualityCalc.FinalQuality:F1}（5×2.0=10 劣化のみ）");

        AssertScenario(
            log,
            "C: 幻の超レア抽出",
            hasPhantom,
            "Phantom 素材を獲得");
    }

    private static void AssertScenario(Action<string> log, string label, bool condition, string detail)
    {
#if UNITY_5_3_OR_NEWER
        log(condition
            ? $"  ✓ [{label} OK] {detail}"
            : $"  ✗ [{label} NG] {detail}");
#else
        if (!condition)
        {
            throw new InvalidOperationException($"{label} failed: {detail}");
        }

        log($"  ✓ [{label} OK] {detail}");
#endif
    }

#if UNITY_5_3_OR_NEWER
    public static void RunInUnity()
    {
        RunAllScenarios(Debug.Log);
    }
#endif
}

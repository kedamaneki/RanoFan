using System;
using System.Collections.Generic;
using System.Text;

#if UNITY_5_3_OR_NEWER
using UnityEngine;
#endif

// =============================================================================
// ベースレベル（自由割り振り）× ジョブレベル（自動上昇）— 2軸ステータス成長
// 単一ファイルでコピー可能。DualLevelSystemTest.Main() でモック検証。
// JobType は WeaponSystemTest.cs と共通です。
// =============================================================================

/// <summary>キャラクターの基本ステータス（筋力・精神・生命力）。</summary>
public readonly struct CharacterStats
{
    public int STR { get; }
    public int MND { get; }
    public int VIT { get; }

    public CharacterStats(int str, int mnd, int vit)
    {
        STR = Math.Max(0, str);
        MND = Math.Max(0, mnd);
        VIT = Math.Max(0, vit);
    }

    /// <summary>ゲーム開始時の基礎値（ALL 10）。</summary>
    public static CharacterStats InitialBase => new CharacterStats(10, 10, 10);

    public static CharacterStats Zero => new CharacterStats(0, 0, 0);

    public CharacterStats Add(CharacterStats other)
    {
        return new CharacterStats(STR + other.STR, MND + other.MND, VIT + other.VIT);
    }

    public CharacterStats MultiplyLevels(int levelCount)
    {
        if (levelCount <= 0)
        {
            return Zero;
        }

        return new CharacterStats(STR * levelCount, MND * levelCount, VIT * levelCount);
    }

    /// <summary>将来の「武器要求ステータス」判定用。全項目が閾値以上か。</summary>
    public bool MeetsMinimum(CharacterStats required)
    {
        return STR >= required.STR && MND >= required.MND && VIT >= required.VIT;
    }

    public override string ToString()
    {
        return $"STR={STR} MND={MND} VIT={VIT}";
    }
}

/// <summary>成長システムの定数。</summary>
public static class GrowthConstants
{
    public const int InitialStatValue = 10;
    public const int StartingLevel = 1;
    public const int BasePointsPerLevelUp = 5;
}

/// <summary>
/// 将来の装備制限（例: STR 50 未満は大剣不可）で参照するインターフェース。
/// </summary>
public interface ICharacterStatProvider
{
    CharacterStats GetFinalStats();
    bool MeetsStatRequirement(CharacterStats required);
}

/// <summary>ジョブごとのレベルアップ時・自動ステータス上昇テーブル。</summary>
public static class JobLevelGrowthTable
{
    private static readonly IReadOnlyDictionary<JobType, CharacterStats> PerLevelGrowth =
        new Dictionary<JobType, CharacterStats>
        {
            [JobType.Warrior] = new CharacterStats(str: 3, mnd: 0, vit: 2),
            [JobType.Priest] = new CharacterStats(str: 1, mnd: 3, vit: 1),
            [JobType.Novice] = new CharacterStats(str: 1, mnd: 1, vit: 1),
            // 仕様外だが JobType 列挙との整合のため定義（弓使い・敏捷型）
            [JobType.Hunter] = new CharacterStats(str: 2, mnd: 0, vit: 1)
        };

    public static CharacterStats GetPerLevelGrowth(JobType job)
    {
        if (PerLevelGrowth.TryGetValue(job, out CharacterStats growth))
        {
            return growth;
        }

        throw new KeyNotFoundException($"未定義のジョブ成長: {job}");
    }

    public static string GetGrowthDescription(JobType job)
    {
        CharacterStats g = GetPerLevelGrowth(job);
        return $"STR+{g.STR}, MND+{g.MND}, VIT+{g.VIT}";
    }
}

/// <summary>ポイント割り振りの結果。</summary>
public readonly struct StatAllocationResult
{
    public bool Succeeded { get; }
    public string Message { get; }

    public StatAllocationResult(bool succeeded, string message)
    {
        Succeeded = succeeded;
        Message = message ?? string.Empty;
    }

    public static StatAllocationResult Ok(string message = "割り振り成功")
    {
        return new StatAllocationResult(true, message);
    }

    public static StatAllocationResult Fail(string message)
    {
        return new StatAllocationResult(false, message);
    }
}

/// <summary>
/// ベースレベル（自由割り振り）とジョブレベル（自動上昇）の2軸成長を管理します。
/// 最終ステータス = 初期(10/10/10) + ベース割り振り分 + ジョブ自動上昇分
/// </summary>
public sealed class CharacterGrowth : ICharacterStatProvider
{
    public int BaseLevel { get; private set; } = GrowthConstants.StartingLevel;
    public int JobLevel { get; private set; } = GrowthConstants.StartingLevel;
    public JobType CurrentJob { get; private set; }
    public int UnallocatedBasePoints { get; private set; }

    /// <summary>ベースレベル由来の自由割り振り累計（初期10は含まない）。</summary>
    public CharacterStats BaseAllocatedStats { get; private set; } = CharacterStats.Zero;

    /// <summary>ジョブレベル由来の自動上昇累計。</summary>
    public CharacterStats JobAccumulatedStats { get; private set; } = CharacterStats.Zero;

    public CharacterGrowth(JobType startingJob = JobType.Warrior)
    {
        CurrentJob = startingJob;
    }

    /// <summary>
    /// 現在の最終ステータスを算出します。
    /// </summary>
    public CharacterStats GetFinalStats()
    {
        return CharacterStats.InitialBase
            .Add(BaseAllocatedStats)
            .Add(JobAccumulatedStats);
    }

    public bool MeetsStatRequirement(CharacterStats required)
    {
        return GetFinalStats().MeetsMinimum(required);
    }

    /// <summary>ベースレベルを1上げ、ステータスポイントを5付与します。</summary>
    public void LevelUpBase()
    {
        BaseLevel++;
        UnallocatedBasePoints += GrowthConstants.BasePointsPerLevelUp;
    }

    /// <summary>指定ベースレベルまで一気に上げます（レベルアップ分のポイントを付与）。</summary>
    public void SetBaseLevel(int targetLevel)
    {
        if (targetLevel < BaseLevel)
        {
            throw new ArgumentException($"ベースレベルは降下できません: {BaseLevel} → {targetLevel}");
        }

        while (BaseLevel < targetLevel)
        {
            LevelUpBase();
        }
    }

    /// <summary>ジョブレベルを1上げ、ジョブ固有のステータスを自動加算します。</summary>
    public void LevelUpJob()
    {
        JobLevel++;
        CharacterStats perLevel = JobLevelGrowthTable.GetPerLevelGrowth(CurrentJob);
        JobAccumulatedStats = JobAccumulatedStats.Add(perLevel);
    }

    /// <summary>指定ジョブレベルまで一気に上げます。</summary>
    public void SetJobLevel(int targetLevel)
    {
        if (targetLevel < JobLevel)
        {
            throw new ArgumentException($"ジョブレベルは降下できません: {JobLevel} → {targetLevel}");
        }

        while (JobLevel < targetLevel)
        {
            LevelUpJob();
        }
    }

    /// <summary>
    /// 未割り振りのベースポイントを STR / MND / VIT に配分します。
    /// 合計が未割り振りポイントを超える場合は失敗します。
    /// </summary>
    public StatAllocationResult AllocatePoints(int str, int mnd, int vit)
    {
        if (str < 0 || mnd < 0 || vit < 0)
        {
            return StatAllocationResult.Fail("割り振りは 0 以上の整数のみ指定できます。");
        }

        int totalCost = str + mnd + vit;
        if (totalCost == 0)
        {
            return StatAllocationResult.Fail("割り振るポイントが 0 です。");
        }

        if (totalCost > UnallocatedBasePoints)
        {
            return StatAllocationResult.Fail(
                $"ポイント不足（必要 {totalCost} / 残り {UnallocatedBasePoints}）。");
        }

        UnallocatedBasePoints -= totalCost;
        BaseAllocatedStats = BaseAllocatedStats.Add(new CharacterStats(str, mnd, vit));
        return StatAllocationResult.Ok(
            $"STR+{str}, MND+{mnd}, VIT+{vit} を割り振り（残り {UnallocatedBasePoints} pt）");
    }

    /// <summary>成長状態のサマリ文字列を返します。</summary>
    public string BuildSummary()
    {
        CharacterStats final = GetFinalStats();
        StringBuilder sb = new StringBuilder();
        sb.AppendLine($"ジョブ: {CurrentJob}  ベースLv.{BaseLevel}  ジョブLv.{JobLevel}");
        sb.AppendLine($"未割り振りベースpt: {UnallocatedBasePoints}");
        sb.AppendLine($"初期基礎: {CharacterStats.InitialBase}");
        sb.AppendLine($"＋ベース割り振り: {BaseAllocatedStats}");
        sb.AppendLine($"＋ジョブ自動上昇: {JobAccumulatedStats}");
        sb.AppendLine($"＝最終ステータス: {final}");
        return sb.ToString().TrimEnd();
    }
}

/// <summary>モック実行・育成シナリオ検証用エントリポイント。</summary>
public static class DualLevelSystemTest
{
    /// <summary>コンソールアプリ等からの検証用エントリポイント。</summary>
    public static void Main()
    {
        RunRomanticBuildScenario(Console.WriteLine);
    }

    public static void RunAllScenarios(Action<string> log)
    {
        if (log == null)
        {
            log = _ => { };
        }

        RunRomanticBuildScenario(log);
        RunAllocationRejectionTest(log);
    }

    /// <summary>
    /// ラノベ的ユニークビルド: 戦士ジョブに MND 全極振りした「魔法戦士のタマゴ」。
    /// </summary>
    public static void RunRomanticBuildScenario(Action<string> log)
    {
        if (log == null)
        {
            log = _ => { };
        }

        log("===== DualLevelSystemTest: 魔法戦士のタマゴ =====");

        CharacterGrowth hero = new CharacterGrowth(JobType.Warrior);

        // 1. 初期状態
        log(string.Empty);
        log("--- 1. 初期状態（ベースLv.1 / ジョブLv.1 戦士） ---");
        log(hero.BuildSummary());
        AssertScenario(
            log,
            "初期ステータス",
            hero.GetFinalStats().STR == 10 &&
            hero.GetFinalStats().MND == 10 &&
            hero.GetFinalStats().VIT == 10,
            "ALL 10 で開始");

        // 2. ジョブレベル 5（Lv.1→5 で 4 回の自動上昇）
        log(string.Empty);
        log("--- 2. ジョブレベル 5 到達（戦士: 1Lvごと STR+3, VIT+2） ---");
        hero.SetJobLevel(5);
        CharacterStats afterJob = hero.GetFinalStats();
        log(hero.BuildSummary());
        AssertScenario(
            log,
            "ジョブ成長",
            afterJob.STR == 22 && afterJob.VIT == 18 && afterJob.MND == 10,
            $"肉体が引き締まった（STR {afterJob.STR}, VIT {afterJob.VIT}）");

        // 3. ベースレベル 3（2 回のレベルアップ → 10 pt）
        log(string.Empty);
        log("--- 3. ベースレベル 3 到達（+5pt × 2 = 10pt 入手） ---");
        hero.SetBaseLevel(3);
        log(hero.BuildSummary());
        AssertScenario(
            log,
            "ベースポイント付与",
            hero.UnallocatedBasePoints == 10,
            "未割り振り 10 pt");

        // 4. MND 全極振り
        log(string.Empty);
        log("--- 4. 【ロマンビルド】10pt を MND に全極振り ---");
        StatAllocationResult allocation = hero.AllocatePoints(str: 0, mnd: 10, vit: 0);
        log($"  割り振り結果: {allocation.Message}");
        AssertScenario(log, "全極振り", allocation.Succeeded, "MND+10 成功");

        // 5. 最終証明
        log(string.Empty);
        log("--- 5. 最終ステータス — 魔法戦士のタマゴ ---");
        CharacterStats final = hero.GetFinalStats();
        log(hero.BuildSummary());
        log(string.Empty);
        log("  【ビルド解説】");
        log($"  ・戦士ジョブ補正で STR={final.STR} / VIT={final.VIT} と肉体は戦士らしい");
        log($"  ・本人のこだわりで MND={final.MND} と精神も超一流");
        log("  → 数値上、「引き締まった肉体＋規格外の精神」を両立したタマゴが成立");

        bool isMagicWarriorEgg =
            final.STR >= 20 &&
            final.VIT >= 15 &&
            final.MND >= 18 &&
            final.MND > final.STR - 5;

        AssertScenario(
            log,
            "魔法戦士のタマゴ",
            isMagicWarriorEgg,
            $"最終 {final}（STR肉体型 + MND特化のハイブリッド）");

        log(string.Empty);
        log("===== DualLevelSystemTest 完了 =====");
    }

    private static void RunAllocationRejectionTest(Action<string> log)
    {
        log(string.Empty);
        log("--- 付録: ポイント不足時は割り振り拒否 ---");
        CharacterGrowth sample = new CharacterGrowth(JobType.Novice);
        sample.SetBaseLevel(2); // 5 pt のみ

        StatAllocationResult rejected = sample.AllocatePoints(3, 3, 0);
        log($"  結果: {rejected.Message}");
        AssertScenario(log, "割り振り拒否", !rejected.Succeeded, "6pt 要求は 5pt 残で失敗");
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

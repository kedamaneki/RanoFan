#if UNITY_EDITOR

using UnityEngine;
using UnityEditor;

/// <summary>
/// 武器種 × 戦闘ジョブの相性を Console に出力するテスター（エディタ専用）。
/// </summary>
public class WeaponSystemTester : MonoBehaviour
{
    [Header("実行設定")]
    [SerializeField] private bool runAllOnStart;

    private static readonly WeaponData IronSword = new WeaponData("鉄の剣", WeaponType.Sword, 100f, 20f);
    private static readonly WeaponData HunterBow = new WeaponData("狩人の弓", WeaponType.Bow, 90f, 18f);
    private static readonly WeaponData PriestStaff = new WeaponData("僧侶の棒", WeaponType.Stick, 70f, 15f);
    private static readonly WeaponData HunterBowForNovice = new WeaponData("狩人の弓", WeaponType.Bow, 90f, 18f);
    private static readonly WeaponData CrudeClub = new WeaponData("粗末な棒", WeaponType.Stick, 60f, 16f);

    private void Start()
    {
        if (runAllOnStart)
        {
            RunAllScenariosNow();
        }
    }

    /// <summary>全ジョブの武器相性シナリオを出力します。</summary>
    public void RunAllScenariosNow()
    {
        WeaponSystemTest.RunAllScenarios(Debug.Log);
    }

    [ContextMenu("Run All Weapon Scenarios")]
    private void RunAllFromContextMenu()
    {
        RunAllScenariosNow();
    }

    public void RunWarriorScenarios()
    {
        LogJobHeader(JobType.Warrior, "#E57373", "戦士 — 剣得意・弓は大幅ペナルティ");
        RunScenario("戦士 × 剣（得意）", JobType.Warrior, IronSword);
        RunScenario("戦士 × 弓（装備不可級）", JobType.Warrior, HunterBow);
    }

    public void RunHunterScenarios()
    {
        LogJobHeader(JobType.Hunter, "#81C784", "狩人 — 弓超得意・棒は半減");
        RunScenario("狩人 × 弓（超得意）", JobType.Hunter, HunterBow);
        RunScenario("狩人 × 棒（装備不可級）", JobType.Hunter, CrudeClub);
    }

    public void RunPriestScenarios()
    {
        LogJobHeader(JobType.Priest, "#64B5F6", "僧侶 — 棒特化・刃物は戒律で攻撃不可");
        RunScenario("僧侶 × 棒（特化）", JobType.Priest, PriestStaff);
        RunScenario("僧侶 × 剣（戒律不可）", JobType.Priest, IronSword);
    }

    public void RunNoviceScenarios()
    {
        LogJobHeader(JobType.Novice, "#FFD54F", "無職 — 全武器に未熟ペナルティ");
        RunScenario("無職 × 弓（未熟ペナルティ）", JobType.Novice, HunterBowForNovice);
        RunScenario("無職 × 剣（未熟ペナルティ）", JobType.Novice, IronSword);
    }

    private static void LogJobHeader(JobType job, string colorHex, string tagline)
    {
        Debug.Log(
            $"<color={colorHex}><b>════ 【{job}】武器相性テスト ════</b></color>\n" +
            $"<color={colorHex}>{tagline}</color>");
    }

    private static void RunScenario(string label, JobType job, WeaponData weapon)
    {
        Debug.Log($"--- {label} ---");

        WeaponCombatResult result = WeaponRestrictionSystem.Calculate(
            job,
            weapon,
            warning => Debug.Log($"  <color=#FFB74D>※ {warning}</color>"));

        Debug.Log(
            $"  ジョブ: {job} / 武器: {weapon.WeaponName} ({weapon.Type})\n" +
            $"  相性: {result.AppliedRule.Tier}\n" +
            $"  基礎攻撃 {weapon.BaseAttack:F1} → 最終攻撃 {result.FinalAttack:F1} " +
            $"(×{result.AppliedRule.DamageMultiplier:F1})\n" +
            $"  基礎スタミナ {weapon.BaseStaminaCost:F1} → 最終スタミナ {result.FinalStaminaCost:F1} " +
            $"(×{result.AppliedRule.StaminaMultiplier:F1})");
    }
}

public static class WeaponSystemTesterMenu
{
    [MenuItem("Tools/Demo/Run Weapon System Tests (Play Mode)")]
    private static void RunAllFromMenu()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("[WeaponSystemTester] Play モード中のみ実行できます。");
            return;
        }

        ResolveTester().RunAllScenariosNow();
    }

    private static WeaponSystemTester ResolveTester()
    {
        WeaponSystemTester tester = UnityEngine.Object.FindAnyObjectByType<WeaponSystemTester>();
        if (tester != null)
        {
            return tester;
        }

        GameObject hub = GameObject.Find("DebugSystemsHub");
        GameObject host = hub != null ? hub : new GameObject(nameof(WeaponSystemTester));
        return host.GetComponent<WeaponSystemTester>() ?? host.AddComponent<WeaponSystemTester>();
    }
}

#endif

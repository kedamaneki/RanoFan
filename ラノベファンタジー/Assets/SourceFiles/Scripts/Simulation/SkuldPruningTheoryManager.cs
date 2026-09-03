using System;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

// =============================================================================
// スクルド剪定理論マネージャー — Magic/Job/レシピ/資源ノードの剪定適用
// 連携: MagicSanitizerEngine / EnvironmentManager / EraContextResolver
// 規約: ルールはデータ駆動（PruningRuleSet ScriptableObject or JSON）
// =============================================================================

/// <summary>剪定対象の種別。</summary>
public enum PruningTargetType
{
    Magic,
    Job,
    CraftingRecipe,
    ResourceNode
}

/// <summary>剪定の効果種別。</summary>
public enum PruningEffect
{
    Disable,            // 完全無効化
    ReduceEfficiency,   // 効率低下（EffectValue で比率指定）
    RemoveDependency,   // 依存関係解除
    IntroduceNew        // 新要素を導入（NewElementId を指定）
}

/// <summary>1 件の剪定ルール定義。</summary>
[Serializable]
public class PruningRule
{
    public string RuleId = string.Empty;
    public int TriggerYear = EraContextResolver.ChronicleMaxTurn + 1;
    public PruningTargetType TargetType;
    public string TargetId = string.Empty;
    public PruningEffect Effect;

    /// <summary>ReduceEfficiency の場合の効率比率（例: 0.5 = 50%）。</summary>
    public float EffectValue = 1.0f;

    /// <summary>IntroduceNew の場合の新要素 ID。</summary>
    public string NewElementId = string.Empty;

    /// <summary>ブラインドヒント用メッセージ（プレイヤーには文脈のみ通知）。</summary>
    public string Description = string.Empty;
}

/// <summary>
/// スクルド剪定理論の適用を管理するシングルトン。
/// T≥1001 の剪定ルールを年次チェックし、MagicSanitizerEngine へ委譲します。
/// Safe-Fail: ルール個別の例外はキャッチして他ルールへ影響しません。
/// </summary>
[DefaultExecutionOrder(50)]
public class SkuldPruningTheoryManager : MonoBehaviour
{
    public const string LogTag = "【スクルド剪定理論】";

    public static SkuldPruningTheoryManager Instance { get; private set; }

    private List<PruningRule> pruningRules;
    private readonly HashSet<string> appliedRuleIds = new HashSet<string>();

    public static SkuldPruningTheoryManager EnsureInstance()
    {
        if (Instance != null)
        {
            return Instance;
        }

        SkuldPruningTheoryManager existing =
            UnityEngine.Object.FindAnyObjectByType<SkuldPruningTheoryManager>();
        if (existing != null)
        {
            Instance = existing;
            return Instance;
        }

        GameObject hub = GameObject.Find("DebugSystemsHub");
        GameObject host = hub != null ? hub : new GameObject(nameof(SkuldPruningTheoryManager));
        SkuldPruningTheoryManager mgr = host.GetComponent<SkuldPruningTheoryManager>();
        return mgr != null ? mgr : host.AddComponent<SkuldPruningTheoryManager>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        pruningRules = LoadPruningRules();
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private static List<PruningRule> LoadPruningRules()
    {
        // 初期剪定ルールセット（データ駆動への橋渡し用デフォルト定義）
        // 本番では Resources.Load<TextAsset>("Data/PruningRules") + JsonUtility でロードする
        return new List<PruningRule>
        {
            new PruningRule
            {
                RuleId = "PRUNE_BASIC_SMELTING_1",
                TriggerYear = 1050,
                TargetType = PruningTargetType.CraftingRecipe,
                TargetId = "RECIPE_BASIC_IRON_SMELT",
                Effect = PruningEffect.ReduceEfficiency,
                EffectValue = 0.3f,
                Description = "原始的な製錬技術は、もはや時代の流れに合わない。"
            },
            new PruningRule
            {
                RuleId = "PRUNE_ANCIENT_RITUAL_1",
                TriggerYear = 1100,
                TargetType = PruningTargetType.Magic,
                TargetId = "MAGIC_ANCIENT_RITUAL_OF_HARVEST",
                Effect = PruningEffect.Disable,
                Description = "古き収穫の儀式は、大地との繋がりを失った。"
            },
            new PruningRule
            {
                RuleId = "PRUNE_WOODCUTTER_JOB_1",
                TriggerYear = 1150,
                TargetType = PruningTargetType.Job,
                TargetId = "JOB_WOODCUTTER",
                Effect = PruningEffect.ReduceEfficiency,
                EffectValue = 0.5f,
                Description = "森林資源の枯渇は、木こりの生業を困難にする。"
            },
            new PruningRule
            {
                RuleId = "INTRODUCE_ADVANCED_ALLOY_1",
                TriggerYear = 1050,
                TargetType = PruningTargetType.CraftingRecipe,
                TargetId = "RECIPE_ADVANCED_ALLOY_SMELT",
                Effect = PruningEffect.IntroduceNew,
                NewElementId = "RECIPE_ADVANCED_ALLOY_SMELT",
                Description = "旧き技術の衰退は、新たな錬金術の扉を開く。"
            }
        };
    }

    /// <summary>
    /// 現在年に対し、発動すべき剪定ルールを適用します。
    /// GameTimeManager の AdvanceYear から毎年呼び出します。
    /// </summary>
    public void ApplyPruningTheory(int currentYear)
    {
        if (pruningRules == null || pruningRules.Count == 0)
        {
            return;
        }

        foreach (PruningRule rule in pruningRules)
        {
            if (currentYear < rule.TriggerYear)
            {
                continue;
            }

            if (appliedRuleIds.Contains(rule.RuleId))
            {
                continue;
            }

            try
            {
                Debug.Log(
                    $"<color=#FFF59D><b>{LogTag}</b></color> " +
                    $"ルール適用: {rule.RuleId} " +
                    $"(対象={rule.TargetType}:{rule.TargetId} 効果={rule.Effect})");

                ApplyRule(rule);
                appliedRuleIds.Add(rule.RuleId);

                Debug.Log(
                    $"<color=#FFF59D><b>{LogTag}</b></color> " +
                    $"スクルドの囁き: 「{rule.Description}」");
            }
            catch (Exception ex)
            {
                Debug.LogWarning(
                    $"[SkuldPruningTheoryManager] ルール {rule.RuleId} の適用中に Safe-Fail: {ex.Message}");
            }
        }
    }

    private void ApplyRule(PruningRule rule)
    {
        switch (rule.TargetType)
        {
            case PruningTargetType.Magic:
                ApplyMagicPruning(rule);
                break;
            case PruningTargetType.Job:
                ApplyJobPruning(rule);
                break;
            case PruningTargetType.CraftingRecipe:
                ApplyCraftingRecipePruning(rule);
                break;
            case PruningTargetType.ResourceNode:
                ApplyResourceNodePruning(rule);
                break;
            default:
                Debug.LogWarning(
                    $"[SkuldPruningTheoryManager] 未知の PruningTargetType: {rule.TargetType}");
                break;
        }
    }

    private static void ApplyMagicPruning(PruningRule rule)
    {
        MagicSanitizerEngine engine = MagicSanitizerEngine.EnsureInstance();
        if (engine == null)
        {
            Debug.LogWarning("[SkuldPruningTheoryManager] MagicSanitizerEngine が見つかりません。");
            return;
        }

        switch (rule.Effect)
        {
            case PruningEffect.Disable:
                engine.SetMagicPruningStatus(rule.TargetId, MagicPruningStatus.Pruned);
                break;
            case PruningEffect.ReduceEfficiency:
                engine.SetMagicEfficiencyModifier(rule.TargetId, rule.EffectValue);
                break;
            case PruningEffect.IntroduceNew:
                // 既存 MagicSanitizerEngine に新魔法を提案登録
                engine.ProposeNewMagicById(rule.NewElementId);
                break;
            default:
                Debug.LogWarning(
                    $"[SkuldPruningTheoryManager] Magic 剪定: 未対応の Effect {rule.Effect}");
                break;
        }
    }

    private static void ApplyJobPruning(PruningRule rule)
    {
        MagicSanitizerEngine engine = MagicSanitizerEngine.EnsureInstance();
        if (engine == null)
        {
            return;
        }

        switch (rule.Effect)
        {
            case PruningEffect.Disable:
                engine.SetJobPruningStatus(rule.TargetId, JobPruningStatus.Pruned);
                break;
            case PruningEffect.ReduceEfficiency:
                engine.SetJobEfficiencyModifier(rule.TargetId, rule.EffectValue);
                break;
            default:
                Debug.LogWarning(
                    $"[SkuldPruningTheoryManager] Job 剪定: 未対応の Effect {rule.Effect}");
                break;
        }
    }

    private static void ApplyCraftingRecipePruning(PruningRule rule)
    {
        // CraftingManager が実装された際に委譲する
        // 現時点では HistoryFlagRegistry に剪定フラグを記録
        string flagId = $"PRUNE_RECIPE_{rule.TargetId}_{rule.Effect}";
        HistoryFlagRegistry.EnsureWired();
        HistoryFlagRegistry.TryUnlock(flagId);
        Debug.Log(
            $"[SkuldPruningTheoryManager] クラフトレシピ剪定を記録: {rule.TargetId} → {rule.Effect} (flag={flagId})");
    }

    private static void ApplyResourceNodePruning(PruningRule rule)
    {
        // ResourceManager が実装された際に委譲する
        // 現時点では HistoryFlagRegistry に記録
        string flagId = $"PRUNE_RESOURCE_{rule.TargetId}_{rule.Effect}";
        HistoryFlagRegistry.EnsureWired();
        HistoryFlagRegistry.TryUnlock(flagId);
        Debug.Log(
            $"[SkuldPruningTheoryManager] 資源ノード剪定を記録: {rule.TargetId} → {rule.Effect} (flag={flagId})");
    }

    /// <summary>外部からルールを追加登録します（データ駆動拡張用）。</summary>
    public void RegisterRule(PruningRule rule)
    {
        if (rule == null || string.IsNullOrWhiteSpace(rule.RuleId))
        {
            Debug.LogWarning("[SkuldPruningTheoryManager] 無効なルールの登録をスキップしました。");
            return;
        }

        pruningRules ??= new List<PruningRule>();
        pruningRules.Add(rule);
    }

    /// <summary>検証用リセット。</summary>
    public void ResetForVerification()
    {
        appliedRuleIds.Clear();
    }
}

public static class SkuldPruningBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoAttach()
    {
        SkuldPruningTheoryManager.EnsureInstance();
    }
}

#if UNITY_EDITOR
public static class SkuldPruningMenu
{
    [MenuItem("Tools/Procedural Map/Apply Skuld Pruning Theory (T1001 Test)")]
    public static void ApplyFromMenu()
    {
        SkuldPruningTheoryManager mgr = SkuldPruningTheoryManager.EnsureInstance();
        mgr.ResetForVerification();
        mgr.ApplyPruningTheory(1051);
        EditorUtility.DisplayDialog(
            "Skuld Pruning",
            "T1051 の剪定理論を適用しました。コンソールでログを確認してください。",
            "OK");
    }
}
#endif

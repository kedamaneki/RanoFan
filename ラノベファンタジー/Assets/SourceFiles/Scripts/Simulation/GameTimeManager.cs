using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

// =============================================================================
// ゲーム内年次管理 — T≥1001 環境転換 & スクルド剪定理論トリガー
// 連携: EnvironmentManager / SkuldPruningTheoryManager / EraContextResolver
// =============================================================================

/// <summary>
/// ゲーム内の現在年を管理し、T≥1001 の環境転換と剪定理論の適用を統括します。
/// EraContextResolver の ChronicleMaxTurn と連動します。
/// </summary>
[DefaultExecutionOrder(-84)]
public class GameTimeManager : MonoBehaviour
{
    public const string LogTag = "【ゲーム時間管理】";
    private const int TransformationYear = EnvironmentManager.TransformationYear;

    public static GameTimeManager Instance { get; private set; }

    [SerializeField] private int currentYear = 1;

    private bool transformationTriggered;
    private int lastPruningYear = -1;

    public int CurrentYear => currentYear;

    public static GameTimeManager EnsureInstance()
    {
        if (Instance != null)
        {
            return Instance;
        }

        GameTimeManager existing = UnityEngine.Object.FindAnyObjectByType<GameTimeManager>();
        if (existing != null)
        {
            Instance = existing;
            return Instance;
        }

        GameObject hub = GameObject.Find("DebugSystemsHub");
        GameObject host = hub != null ? hub : new GameObject(nameof(GameTimeManager));
        GameTimeManager mgr = host.GetComponent<GameTimeManager>();
        return mgr != null ? mgr : host.AddComponent<GameTimeManager>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
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

    /// <summary>
    /// ゲームの年を進め、トリガーをチェックします。
    /// Safe-Fail: 内部の転換・剪定処理で例外が発生してもシミュレーションを継続します。
    /// </summary>
    public void AdvanceYear(int years = 1)
    {
        currentYear += Mathf.Max(1, years);
        Debug.Log($"<color=#B0BEC5><b>{LogTag}</b></color> 現在年: {currentYear}");
        CheckAndFireTriggers();
    }

    /// <summary>現在年を外部から設定します（パイプライン連動用）。</summary>
    public void SetYear(int year)
    {
        currentYear = Mathf.Max(1, year);
        CheckAndFireTriggers();
    }

    private void CheckAndFireTriggers()
    {
        // 環境転換（T≥1001、1回のみ）
        if (currentYear >= TransformationYear && !transformationTriggered)
        {
            transformationTriggered = true;
            try
            {
                EnvironmentManager.EnsureInstance().ApplyEnvironmentalTransformation();
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning(
                    $"[GameTimeManager] 環境転換 Safe-Fail: {ex.Message}");
            }
        }

        // スクルド剪定理論（毎年チェック、同年重複なし）
        if (currentYear != lastPruningYear)
        {
            lastPruningYear = currentYear;
            try
            {
                SkuldPruningTheoryManager.Instance?.ApplyPruningTheory(currentYear);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning(
                    $"[GameTimeManager] 剪定理論 Safe-Fail: {ex.Message}");
            }
        }
    }

    /// <summary>検証用リセット。</summary>
    public void ResetForVerification()
    {
        transformationTriggered = false;
        lastPruningYear = -1;
    }
}

public static class GameTimeManagerBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoAttach()
    {
        GameTimeManager.EnsureInstance();
    }
}

#if UNITY_EDITOR
public static class GameTimeManagerMenu
{
    [MenuItem("Tools/Procedural Map/Advance Chronicle Year (Test)")]
    public static void AdvanceYearFromMenu()
    {
        GameTimeManager mgr = GameTimeManager.EnsureInstance();
        mgr.SetYear(EraContextResolver.ChronicleMaxTurn + 1);
        EditorUtility.DisplayDialog(
            "GameTimeManager",
            $"現在年 = {mgr.CurrentYear}\n環境転換={EnvironmentManager.Instance?.HasTransformed}",
            "OK");
    }
}
#endif

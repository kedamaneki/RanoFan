using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

// =============================================================================
// 拡張型生産ステータス — 鍛冶 / 調合の動的パラメータ基盤
// 連携: DetailedCraftingProcessManager / CraftingExceptionCollector / ForgeVisualController / CraftingQualityPacketEmitter
// =============================================================================

/// <summary>生産パラメータ変動通知のペイロード。</summary>
public readonly struct CraftingParamChangeInfo
{
    /// <summary>職種（Forge / Alch）。</summary>
    public string CraftType { get; }

    /// <summary>パラメータ名。</summary>
    public string ParamName { get; }

    /// <summary>変動前の値。</summary>
    public float PreviousValue { get; }

    /// <summary>変動後の値。</summary>
    public float NewValue { get; }

    /// <summary>加減算の差分。</summary>
    public float Delta => NewValue - PreviousValue;

    /// <summary>変動情報を構築します。</summary>
    public CraftingParamChangeInfo(
        string craftType,
        string paramName,
        float previousValue,
        float newValue)
    {
        CraftType = craftType ?? string.Empty;
        ParamName = paramName ?? string.Empty;
        PreviousValue = previousValue;
        NewValue = newValue;
    }
}

/// <summary>デモ初期ステータス制約に基づく生産最終ジャッジの結果。</summary>
public sealed class DemoCraftingJudgmentResult
{
    /// <summary>熱科学バースト失敗か（true のとき finalScore は常に 10）。</summary>
    public bool IsThermalBurstFailure;

    /// <summary>最終スコア（バースト時 10、通常成功時 60〜100）。</summary>
    public int FinalScore;

    /// <summary>クランプ前の素点（通常成功時のみ意味を持つ）。</summary>
    public int RawScore;

    /// <summary>判定対象の職種（Forge / Alch）。</summary>
    public string CraftType = string.Empty;

    /// <summary>バースト時の理由（通常成功時は空）。</summary>
    public string BurstReason = string.Empty;
}

/// <summary>1つの生産パラメータ定義（初期値・クランプ範囲）。</summary>
[Serializable]
public sealed class CraftingParamDefinition
{
    /// <summary>パラメータ名（Dictionary キー）。</summary>
    public string paramName = string.Empty;

    /// <summary>初期値。</summary>
    public float defaultValue;

    /// <summary>最小値。</summary>
    public float minValue;

    /// <summary>最大値。</summary>
    public float maxValue;

    /// <summary>加減算後に値をクランプします。</summary>
    public float Clamp(float value)
    {
        return Mathf.Clamp(value, minValue, maxValue);
    }
}

/// <summary>
/// 鍛冶・調合の裏パラメータを Dictionary で動的管理するシングルトン。
/// 文字列キーを追加するだけでマニアックな職人パラメータを拡張できます。
/// </summary>
public class CraftingStatusManager : MonoBehaviour
{
    public const string CraftTypeForge = "Forge";
    public const string CraftTypeAlch = "Alch";

    public const string ParamTemperature = "Temperature";
    public const string ParamDensity = "Density";
    public const string ParamPurity = "Purity";
    public const string ParamPotTemperature = "PotTemperature";
    public const string ParamExtractionLevel = "ExtractionLevel";
    public const string ParamDissolutionRate = "DissolutionRate";

    /// <summary>1始まりの統一クラフト操作スロット数（Alpha1〜Alpha5）。</summary>
    public const int UnifiedCraftSlotCount = 5;

    private const float ForgeMeltdownTemperatureCelsius = 1538f;
    private const float AlchThermalDecompositionPotCelsius = 120f;
    private const int AlchThermalBurstMinimumActions = 3;
    private const int DemoSuccessScoreMin = 60;
    private const int DemoSuccessScoreMax = 100;

    public static CraftingStatusManager Instance { get; private set; }

    /// <summary>パラメータが加減算された直後に発火します（Reset では発火しません）。</summary>
    public static event Action<CraftingParamChangeInfo> ParamModified;

    /// <summary>指定職種のステータスが初期値へリセットされた直後に発火します。</summary>
    public static event Action<string> StatusReset;

    [Header("鍛冶 Forge 定義（Inspector からも拡張可）")]
    [SerializeField] private List<CraftingParamDefinition> forgeParamDefinitions = new List<CraftingParamDefinition>
    {
        new CraftingParamDefinition { paramName = "Temperature", defaultValue = 20f, minValue = 0f, maxValue = 2000f },
        new CraftingParamDefinition { paramName = "Density", defaultValue = 0f, minValue = 0f, maxValue = 200f },
        new CraftingParamDefinition { paramName = "Purity", defaultValue = 100f, minValue = 0f, maxValue = 100f }
    };

    [Header("調合 Alch 定義（Inspector からも拡張可）")]
    [SerializeField] private List<CraftingParamDefinition> alchParamDefinitions = new List<CraftingParamDefinition>
    {
        new CraftingParamDefinition { paramName = "PotTemperature", defaultValue = 20f, minValue = 0f, maxValue = 300f },
        new CraftingParamDefinition { paramName = "ExtractionLevel", defaultValue = 0f, minValue = 0f, maxValue = 200f },
        new CraftingParamDefinition { paramName = "DissolutionRate", defaultValue = 0f, minValue = 0f, maxValue = 100f }
    };

    private readonly Dictionary<string, Dictionary<string, float>> runtimeStatus =
        new Dictionary<string, Dictionary<string, float>>(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, Dictionary<string, CraftingParamDefinition>> definitionTables =
        new Dictionary<string, Dictionary<string, CraftingParamDefinition>>(StringComparer.OrdinalIgnoreCase);

    private bool coreInitialized;
    private string qualityEvaluationCraftType = CraftTypeForge;
    private float peakForgeTemperature;
    private int alchUnifiedActionCount;

    // 工房セッション中に投入素材の craftValue を累積（品質ジャッジへ乗算）
    private float sessionMaterialCraftMultiplier = 1f;
    private readonly List<string> materialContributionLogs = new List<string>();

    // レシピ解決: 投入素材 ID 履歴（最大 UnifiedCraftSlotCount）と成果物プレビュー
    private readonly List<string> sessionIngredientIds = new List<string>();
    private string lastConsumedIngredientId;
    private int consecutiveSameIngredientCount;
    private string pendingResultItemId = string.Empty;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[CraftingStatusManager] 重複インスタンスを検出しました。");
            Destroy(this);
            return;
        }

        Instance = this;
        InitializeCore(resetRuntimeIfEmpty: true);
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    /// <summary>シーンに無い場合は DebugSystemsHub へ動的生成して返します。</summary>
    public static CraftingStatusManager EnsureInstance()
    {
        CraftingStatusManager manager = ResolveInstanceReference();
        manager?.EnsureInitialized();
        return manager;
    }

    /// <summary>定義テーブルとランタイム辞書が未構築なら初期化します。</summary>
    public void EnsureInitialized()
    {
        if (Instance != null && Instance != this)
        {
            return;
        }

        if (Instance == null)
        {
            Instance = this;
        }

        InitializeCore(resetRuntimeIfEmpty: !coreInitialized);
    }

    private static CraftingStatusManager ResolveInstanceReference()
    {
        if (Instance != null)
        {
            return Instance;
        }

        GameObject hub = GameObject.Find("DebugSystemsHub");
        if (hub != null)
        {
            CraftingStatusManager existing = hub.GetComponent<CraftingStatusManager>();
            if (existing != null)
            {
                return existing;
            }

            return hub.AddComponent<CraftingStatusManager>();
        }

        return new GameObject(nameof(CraftingStatusManager)).AddComponent<CraftingStatusManager>();
    }

    private void InitializeCore(bool resetRuntimeIfEmpty)
    {
        RebuildDefinitionTables();
        coreInitialized = true;

        if (!resetRuntimeIfEmpty)
        {
            return;
        }

        if (runtimeStatus.Count == 0 ||
            !runtimeStatus.ContainsKey(CraftTypeForge) ||
            !runtimeStatus.ContainsKey(CraftTypeAlch))
        {
            ResetAllStatus();
        }
    }

    /// <summary>指定職種のパラメータ現在値を取得します。</summary>
    /// <param name="craftType">Forge / Alch</param>
    /// <param name="paramName">パラメータ名</param>
    public float GetParam(string craftType, string paramName)
    {
        EnsureInitialized();
        if (!TryGetRuntimeValue(craftType, paramName, out float value))
        {
            Debug.LogWarning(
                $"[CraftingStatusManager] 未定義パラメータ: {craftType}/{paramName}");
            return 0f;
        }

        return value;
    }

    /// <summary>指定パラメータへ加減算し、定義済み範囲へクランプします。</summary>
    /// <param name="craftType">Forge / Alch</param>
    /// <param name="paramName">パラメータ名</param>
    /// <param name="value">加算値（減算は負数）</param>
    public void ModifyParam(string craftType, string paramName, float value)
    {
        EnsureInitialized();
        string normalizedType = NormalizeCraftType(craftType);
        if (!definitionTables.TryGetValue(normalizedType, out Dictionary<string, CraftingParamDefinition> defs) ||
            !defs.TryGetValue(paramName, out CraftingParamDefinition definition))
        {
            Debug.LogWarning(
                $"[CraftingStatusManager] ModifyParam 失敗: 未定義 {craftType}/{paramName}");
            return;
        }

        if (!runtimeStatus.TryGetValue(normalizedType, out Dictionary<string, float> bucket))
        {
            bucket = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
            runtimeStatus[normalizedType] = bucket;
        }

        float current = bucket.TryGetValue(paramName, out float existing) ? existing : definition.defaultValue;
        float next = definition.Clamp(current + value);
        bucket[paramName] = next;

        ParamModified?.Invoke(new CraftingParamChangeInfo(
            normalizedType,
            paramName.Trim(),
            current,
            next));

        TrackQualityMetrics(normalizedType, paramName.Trim(), next);
    }

    /// <summary>品質ジャッジ対象の職種セッションを開始し、ピーク追跡を初期化します。</summary>
    /// <param name="craftType">Forge / Alch</param>
    public void BeginQualityEvaluationSession(string craftType)
    {
        EnsureInitialized();
        qualityEvaluationCraftType = NormalizeCraftType(craftType);

        if (string.Equals(qualityEvaluationCraftType, CraftTypeForge, StringComparison.OrdinalIgnoreCase))
        {
            peakForgeTemperature = GetParam(CraftTypeForge, ParamTemperature);
            alchUnifiedActionCount = 0;
        }
        else
        {
            alchUnifiedActionCount = 0;
        }

        ResetRecipeSessionState();
    }

    /// <summary>工程 Enter 時に呼び出し、最終成果物を確定してパケット用スナップショットを返します。</summary>
    public CraftingResultItemSnapshot FinalizeCraftingResultForPacket()
    {
        CraftingResultItemSnapshot snapshot = CraftingRecipeManager.ResolveCraftingResultDetailed(sessionIngredientIds);
        pendingResultItemId = snapshot.ResultItemId;
        return snapshot;
    }

    /// <summary>
    /// 通常成功時に成果物をインベントリへ渡し、武器なら裏パラメータを焼き込んで装備します。
    /// バースト失敗・非装備アイテムは Safe-Fail（武器装備をスキップ）。
    /// </summary>
    public ItemData TryDeliverCraftedResultToInventory(
        CraftingResultItemSnapshot snapshot,
        bool isThermalBurstFailure)
    {
        if (isThermalBurstFailure || string.IsNullOrWhiteSpace(snapshot.ResultItemId))
        {
            Debug.Log(
                "<color=#90A4AE>【成果物】熱科学バーストまたは未解決のため、武器還元をスキップしました。</color>");
            return null;
        }

        ItemData item = CreateRuntimeResultItem(snapshot);
        if (item == null || !item.IsValid())
        {
            Debug.LogWarning("[CraftingStatusManager] 成果物 ItemData の生成に失敗したためインベントリへ渡しません。");
            return null;
        }

        if (item.IsEquipmentWeapon &&
            string.Equals(qualityEvaluationCraftType, CraftTypeForge, StringComparison.OrdinalIgnoreCase))
        {
            item.BindCraftHiddenParams(
                GetParam(CraftTypeForge, ParamPurity),
                GetParam(CraftTypeForge, ParamDensity));
        }

        InventoryManager inventory = InventoryManager.EnsureInstance();
        if (inventory == null || !inventory.AddItem(item, 1))
        {
            Debug.LogWarning($"[CraftingStatusManager] 成果物 {item.id} をインベントリへ追加できませんでした。");
            return item;
        }

        Debug.Log(
            $"<color=#CE93D8>【成果物納品】{item.itemName}（{item.id}）をインベントリへ追加" +
            (item.hasCraftHiddenParams
                ? $" / Purity={item.purity:F1} Density={item.density:F1}"
                : " / 裏パラメータなし") +
            "</color>");

        try
        {
            DailySimulationEngine.InjectCraftResultToVillage(item);
        }
        catch (System.Exception dailyException)
        {
            Debug.LogWarning(
                $"[CraftingStatusManager] デイリー村納品ブリッジ Safe-Fail: {dailyException.Message}");
        }

        if (item.IsEquipmentWeapon)
        {
            inventory.EquipCraftedWeapon(item);
        }

        return item;
    }

    private static ItemData CreateRuntimeResultItem(CraftingResultItemSnapshot snapshot)
    {
        MasterDataManager masterData = MasterDataManager.Instance ?? MasterDataManager.EnsureInstance();
        if (masterData != null && masterData.TryGetItem(snapshot.ResultItemId, out ItemMasterData master) &&
            master != null && master.IsValid())
        {
            return master.ToItemData();
        }

        return new ItemData
        {
            id = snapshot.ResultItemId,
            itemName = string.IsNullOrWhiteSpace(snapshot.ResultItemName)
                ? snapshot.ResultItemId
                : snapshot.ResultItemName,
            weight = 1f,
            maxStack = 1,
            itemType = snapshot.ResultItemId.StartsWith("WEAPON_", StringComparison.OrdinalIgnoreCase)
                ? CraftingMaterialRegistry.ItemTypeEquipment
                : string.Empty
        };
    }

    /// <summary>投入中の素材リストから算出した暫定成果物 ID（工程途中のプレビュー）。</summary>
    public string PendingResultItemId => pendingResultItemId;

    /// <summary>今セッションで消費した素材 ID の読み取り専用コピー。</summary>
    public IReadOnlyList<string> SessionIngredientIds => sessionIngredientIds;

    /// <summary>
    /// デモ初期ステータス制約に基づき、熱科学バースト（10点）か通常成功（60〜100点）の純粋2分岐を判定します。
    /// </summary>
    /// <param name="artisanActionCount">工程履歴の件数（未指定時は内部カウンタを使用）</param>
    public DemoCraftingJudgmentResult EvaluateDemoCraftingJudgment(int artisanActionCount = -1)
    {
        EnsureInitialized();
        string craftType = qualityEvaluationCraftType;
        int actionCount = artisanActionCount >= 0 ? artisanActionCount : alchUnifiedActionCount;

        RefreshPeakForgeTemperatureFromRuntime();

        if (string.Equals(craftType, CraftTypeForge, StringComparison.OrdinalIgnoreCase) &&
            IsForgeThermalBurstFailure())
        {
            return new DemoCraftingJudgmentResult
            {
                IsThermalBurstFailure = true,
                FinalScore = 10,
                RawScore = 10,
                CraftType = CraftTypeForge,
                BurstReason =
                    $"鍛冶 Temperature 融点突破（ピーク {peakForgeTemperature:F1}℃ / 閾値 {ForgeMeltdownTemperatureCelsius:F0}℃）"
            };
        }

        if (string.Equals(craftType, CraftTypeAlch, StringComparison.OrdinalIgnoreCase) &&
            IsAlchThermalBurstFailure(actionCount))
        {
            float potTemperature = GetParam(CraftTypeAlch, ParamPotTemperature);
            float extractionLevel = GetParam(CraftTypeAlch, ParamExtractionLevel);
            return new DemoCraftingJudgmentResult
            {
                IsThermalBurstFailure = true,
                FinalScore = 10,
                RawScore = 10,
                CraftType = CraftTypeAlch,
                BurstReason =
                    $"調合熱分解（工程 {actionCount} 回 / PotTemperature {potTemperature:F1}℃ > {AlchThermalDecompositionPotCelsius:F0}℃ / " +
                    $"ExtractionLevel {extractionLevel:F1} <= 0）"
            };
        }

        int calculatedScore = CalculateDemoNormalSuccessScore(craftType);
        calculatedScore = ApplyCraftQualityFlatBonuses(calculatedScore);
        int clampedScore = Mathf.Clamp(calculatedScore, DemoSuccessScoreMin, DemoSuccessScoreMax);

        return new DemoCraftingJudgmentResult
        {
            IsThermalBurstFailure = false,
            FinalScore = clampedScore,
            RawScore = calculatedScore,
            CraftType = craftType,
            BurstReason = string.Empty
        };
    }

    /// <summary>
    /// 現在セッションの裏パラメータから生産クオリティを決定論的に算出します（デモジャッジへ委譲）。
    /// </summary>
    public int EvaluateCraftingQuality()
    {
        return EvaluateDemoCraftingJudgment().FinalScore;
    }

    /// <summary>鍛冶中に鉄の温度が融点（1538℃）を一度でも超えたかを判定します。</summary>
    public bool IsForgeThermalBurstFailure()
    {
        EnsureInitialized();
        RefreshPeakForgeTemperatureFromRuntime();
        float temperature = GetParam(CraftTypeForge, ParamTemperature);
        return peakForgeTemperature > ForgeMeltdownTemperatureCelsius ||
               temperature > ForgeMeltdownTemperatureCelsius;
    }

    /// <summary>
    /// 調合中に釜温 120℃ 超・3アクション以上・抽出度 0 の熱分解バースト条件を満たすかを判定します。
    /// </summary>
    /// <param name="artisanActionCount">工程履歴の件数</param>
    public bool IsAlchThermalBurstFailure(int artisanActionCount)
    {
        EnsureInitialized();
        if (artisanActionCount < AlchThermalBurstMinimumActions)
        {
            return false;
        }

        float potTemperature = GetParam(CraftTypeAlch, ParamPotTemperature);
        float extractionLevel = GetParam(CraftTypeAlch, ParamExtractionLevel);
        return potTemperature > AlchThermalDecompositionPotCelsius &&
               extractionLevel <= 0f;
    }

    /// <summary>現在の鍛冶温度からピーク値を再同期します（工程実行時・ジャッジ直前に呼び出し）。</summary>
    public void RefreshPeakForgeTemperatureFromRuntime()
    {
        if (!string.Equals(qualityEvaluationCraftType, CraftTypeForge, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        float temperature = GetParam(CraftTypeForge, ParamTemperature);
        peakForgeTemperature = Mathf.Max(peakForgeTemperature, temperature);
    }

    /// <summary>ピーク鍛冶温度（℃）を返します（デバッグログ用）。</summary>
    public float PeakForgeTemperature => peakForgeTemperature;

    /// <summary>バーストを回避した場合の素点を職種パラメータから算出します（クランプ前）。</summary>
    private int CalculateDemoNormalSuccessScore(string craftType)
    {
        if (string.Equals(craftType, CraftTypeAlch, StringComparison.OrdinalIgnoreCase))
        {
            float extractionLevel = GetParam(CraftTypeAlch, ParamExtractionLevel);
            float dissolutionRate = GetParam(CraftTypeAlch, ParamDissolutionRate);
            float average = (extractionLevel + dissolutionRate) * 0.5f * sessionMaterialCraftMultiplier;
            return Mathf.RoundToInt(average);
        }

        float density = GetParam(CraftTypeForge, ParamDensity);
        float purity = GetParam(CraftTypeForge, ParamPurity);
        float forgeAverage = (density + purity) * 0.5f * sessionMaterialCraftMultiplier;
        return Mathf.RoundToInt(forgeAverage);
    }

    /// <summary>
    /// Arts の Craft_PurityFlatBonus とユニークスキルの品質加算を素点へ足します。
    /// 熱科学バースト分岐では呼ばれません。最終クランプは呼び出し側が 60〜100 で行います。
    /// </summary>
    private static int ApplyCraftQualityFlatBonuses(int rawScore)
    {
        float score = rawScore;
        if (ArtsEffectExecutor.Instance != null)
        {
            score += ArtsEffectExecutor.Instance.CraftQualityFlatBonus;
        }

        UniqueSkillRuntimeExecutor unique = UniqueSkillRuntimeExecutor.Instance;
        if (unique != null)
        {
            score = unique.ApplyCraftQualityFlatBonus(score);
        }

        return Mathf.RoundToInt(score);
    }

    private void TrackQualityMetrics(string craftType, string paramName, float newValue)
    {
        if (string.Equals(craftType, CraftTypeForge, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(paramName, ParamTemperature, StringComparison.OrdinalIgnoreCase))
        {
            peakForgeTemperature = Mathf.Max(peakForgeTemperature, newValue);
        }
    }

    private void ResetQualitySessionState(string normalizedType)
    {
        if (string.Equals(normalizedType, CraftTypeForge, StringComparison.OrdinalIgnoreCase))
        {
            peakForgeTemperature = 0f;
        }

        if (string.Equals(normalizedType, CraftTypeAlch, StringComparison.OrdinalIgnoreCase))
        {
            alchUnifiedActionCount = 0;
        }

        sessionMaterialCraftMultiplier = 1f;
        materialContributionLogs.Clear();
        ResetRecipeSessionState();
    }

    private void ResetRecipeSessionState()
    {
        sessionIngredientIds.Clear();
        lastConsumedIngredientId = null;
        consecutiveSameIngredientCount = 0;
        pendingResultItemId = string.Empty;
    }

    /// <summary>指定職種のパラメータを初期値へリセットします。</summary>
    /// <param name="craftType">Forge / Alch</param>
    public void ResetStatus(string craftType)
    {
        EnsureInitialized();
        string normalizedType = NormalizeCraftType(craftType);
        if (!definitionTables.TryGetValue(normalizedType, out Dictionary<string, CraftingParamDefinition> defs))
        {
            Debug.LogWarning($"[CraftingStatusManager] ResetStatus 失敗: 未定義職種 {craftType}");
            return;
        }

        Dictionary<string, float> bucket = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        foreach (KeyValuePair<string, CraftingParamDefinition> pair in defs)
        {
            bucket[pair.Key] = pair.Value.defaultValue;
        }

        runtimeStatus[normalizedType] = bucket;
        ResetQualitySessionState(normalizedType);
        StatusReset?.Invoke(normalizedType);
    }

    /// <summary>指定パラメータの定義済み最大値を返します（未定義時は 0）。</summary>
    public float GetParamMax(string craftType, string paramName)
    {
        EnsureInitialized();
        string normalizedType = NormalizeCraftType(craftType);
        if (definitionTables.TryGetValue(normalizedType, out Dictionary<string, CraftingParamDefinition> defs) &&
            defs.TryGetValue(paramName.Trim(), out CraftingParamDefinition definition))
        {
            return definition.maxValue;
        }

        return 0f;
    }

    /// <summary>Forge / Alch 両方を初期値へリセットします。</summary>
    public void ResetAllStatus()
    {
        ResetStatus(CraftTypeForge);
        ResetStatus(CraftTypeAlch);
    }

    /// <summary>
    /// 被弾バースト時に、生産投入中パラメータのみを 0 へ強制リセットします。
    /// インベントリ（バックパック）は触りません。
    /// </summary>
    /// <param name="craftType">Forge / Alch</param>
    public void BurstResetCraftParams(string craftType)
    {
        EnsureInitialized();
        string normalizedType = NormalizeCraftType(craftType);
        if (!definitionTables.TryGetValue(normalizedType, out Dictionary<string, CraftingParamDefinition> defs))
        {
            Debug.LogWarning($"[CraftingStatusManager] BurstReset 失敗: 未定義職種 {craftType}");
            return;
        }

        Dictionary<string, float> bucket = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        foreach (KeyValuePair<string, CraftingParamDefinition> pair in defs)
        {
            bucket[pair.Key] = 0f;
        }

        runtimeStatus[normalizedType] = bucket;
        ResetQualitySessionState(normalizedType);
        StatusReset?.Invoke(normalizedType);
    }

    /// <summary>指定職種の全パラメータを読み取り専用コピーで返します。</summary>
    public IReadOnlyDictionary<string, float> GetAllParams(string craftType)
    {
        string normalizedType = NormalizeCraftType(craftType);
        if (!runtimeStatus.TryGetValue(normalizedType, out Dictionary<string, float> bucket))
        {
            return new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        }

        return new Dictionary<string, float>(bucket, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>例外パケット用に Key=Value 形式のスナップショットリストを生成します。</summary>
    public List<string> BuildStatusSnapshotLines(string craftType)
    {
        List<string> lines = new List<string>();
        string normalizedType = NormalizeCraftType(craftType);
        if (!definitionTables.TryGetValue(normalizedType, out Dictionary<string, CraftingParamDefinition> defs))
        {
            return lines;
        }

        foreach (KeyValuePair<string, CraftingParamDefinition> pair in defs)
        {
            float value = GetParam(normalizedType, pair.Key);
            lines.Add($"{pair.Key}={value:F2}");
        }

        if (sessionMaterialCraftMultiplier > 1.001f)
        {
            lines.Add($"MaterialCraftMultiplier={sessionMaterialCraftMultiplier:F3}");
        }

        for (int i = 0; i < materialContributionLogs.Count; i++)
        {
            lines.Add(materialContributionLogs[i]);
        }

        if (sessionIngredientIds.Count > 0)
        {
            lines.Add($"IngredientIds={string.Join("|", sessionIngredientIds)}");
        }

        if (!string.IsNullOrWhiteSpace(pendingResultItemId))
        {
            CraftingResultItemSnapshot preview = CraftingRecipeManager.BuildSnapshot(pendingResultItemId, false);
            lines.Add($"ResultItemId={preview.ResultItemId}");
            lines.Add($"ResultItemName={preview.ResultItemName}");
            lines.Add($"ResultItemDescription={preview.ResultItemDescription}");
        }

        return lines;
    }

    /// <summary>コンソール向けのゼンゼロ風ステータス表示文を生成します。</summary>
    public string BuildStatusDisplayRichText(string craftType)
    {
        string normalizedType = NormalizeCraftType(craftType);
        if (!definitionTables.TryGetValue(normalizedType, out Dictionary<string, CraftingParamDefinition> defs))
        {
            return $"<color=#FF8A80>未定義職種: {craftType}</color>";
        }

        StringBuilder sb = new StringBuilder();
        sb.AppendLine(
            $"<color=#4DD0E1><b>【CRAFT STATUS · {normalizedType}】</b></color> " +
            "<color=#80DEEA>裏パラメータ（動的 Dictionary）</color>");

        foreach (KeyValuePair<string, CraftingParamDefinition> pair in defs)
        {
            float value = GetParam(normalizedType, pair.Key);
            CraftingParamDefinition def = pair.Value;
            sb.AppendLine(
                $"  <color=#B2EBF2>{pair.Key}</color> " +
                $"<color=#FFFFFF>{value:F1}</color> " +
                $"<color=#4DB6AC>({def.minValue:F0}〜{def.maxValue:F0})</color>");
        }

        return sb.ToString();
    }

    private void RebuildDefinitionTables()
    {
        definitionTables.Clear();
        RegisterDefinitions(CraftTypeForge, forgeParamDefinitions);
        RegisterDefinitions(CraftTypeAlch, alchParamDefinitions);
    }

    private void RegisterDefinitions(string craftType, List<CraftingParamDefinition> definitions)
    {
        Dictionary<string, CraftingParamDefinition> table =
            new Dictionary<string, CraftingParamDefinition>(StringComparer.OrdinalIgnoreCase);

        if (definitions != null)
        {
            for (int i = 0; i < definitions.Count; i++)
            {
                CraftingParamDefinition definition = definitions[i];
                if (definition == null || string.IsNullOrWhiteSpace(definition.paramName))
                {
                    continue;
                }

                table[definition.paramName.Trim()] = definition;
            }
        }

        definitionTables[craftType] = table;
    }

    private bool TryGetRuntimeValue(string craftType, string paramName, out float value)
    {
        value = 0f;
        string normalizedType = NormalizeCraftType(craftType);
        if (string.IsNullOrWhiteSpace(paramName) ||
            !runtimeStatus.TryGetValue(normalizedType, out Dictionary<string, float> bucket) ||
            !bucket.TryGetValue(paramName.Trim(), out value))
        {
            return false;
        }

        return true;
    }

    private static string NormalizeCraftType(string craftType)
    {
        if (string.IsNullOrWhiteSpace(craftType))
        {
            return CraftTypeForge;
        }

        if (string.Equals(craftType, CraftTypeAlch, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(craftType, CraftProfessionType.Alch.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            return CraftTypeAlch;
        }

        return CraftTypeForge;
    }

    /// <summary>
    /// 統一キー（1〜5）のスロット操作を職種（Forge / Alch）に応じて実行し、裏パラメータを変動させます。
    /// </summary>
    /// <param name="slot1To5">Alpha1=1 〜 Alpha5=5</param>
    /// <param name="craftType">Forge または Alch</param>
    /// <param name="processManager">工程ログ記録先</param>
    /// <returns>実行できた場合 true</returns>
    public bool TryExecuteUnifiedCraftSlot(
        int slot1To5,
        string craftType,
        DetailedCraftingProcessManager processManager)
    {
        if (slot1To5 < 1 || slot1To5 > UnifiedCraftSlotCount)
        {
            Debug.LogWarning(
                $"[CraftingStatusManager] スロット {slot1To5} は範囲外です（1〜{UnifiedCraftSlotCount}）。");
            return false;
        }

        if (!DemoInputGate.IsCraftingHotkeyInputAllowed())
        {
            return false;
        }

        if (processManager == null || !processManager.IsSessionActive)
        {
            Debug.LogWarning("[CraftingStatusManager] こだわり工程セッションが未開始のため操作を拒否しました。");
            return false;
        }

        string normalizedType = NormalizeCraftType(craftType);
        if (string.Equals(normalizedType, CraftTypeAlch, StringComparison.OrdinalIgnoreCase))
        {
            ExecuteAlchUnifiedSlot(slot1To5, processManager);
            alchUnifiedActionCount++;
            TryAutoFinalizeCraftingOnThermalBurst();
            return true;
        }

        ExecuteForgeUnifiedSlot(slot1To5, processManager);
        TryAutoFinalizeCraftingOnThermalBurst();
        return true;
    }

    /// <summary>
    /// 素材投入時の分岐処理。
    /// 同素材追加入力 ➔ craftValue 累積ブースト（Purity/Density の delta 倍率強化）。
    /// 異素材投入 ➔ 素材リストへ蓄積し CraftingRecipeManager で成果物 ID を再評価。
    /// </summary>
    private float ApplyMaterialCraftInfluence(int craftSlot, string craftType, string actionDescription)
    {
        float consumedCraftValue = CraftingMaterialRegistry.TryConsumeMaterialForCraftSlot(
            craftSlot,
            craftType,
            out string consumedItemId);

        if (string.IsNullOrWhiteSpace(consumedItemId))
        {
            return 1f;
        }

        if (!CraftingRecipeManager.IsValidIngredient(consumedItemId))
        {
            Debug.LogError(
                $"[CraftingStatusManager] 不正素材 '{consumedItemId}' — 粗悪な鉄くずレシピへフォールバックします。");
            pendingResultItemId = CraftRecipeIds.CraftFailure;
            materialContributionLogs.Add(
                $"InvalidMaterial[{consumedItemId}] fallback={CraftRecipeIds.CraftFailure}");
            return 1f;
        }

        float craftValue = consumedCraftValue > 0f
            ? consumedCraftValue
            : CraftingMaterialRegistry.GetMaterialCraftValue(consumedItemId);
        bool isSameAsLast = string.Equals(
            consumedItemId,
            lastConsumedIngredientId,
            StringComparison.OrdinalIgnoreCase);

        if (isSameAsLast)
        {
            // 同素材: レシピ ID は維持しつつ、純度・密度の変動幅だけ craftValue で累積強化
            consecutiveSameIngredientCount++;
        }
        else
        {
            consecutiveSameIngredientCount = 1;
            lastConsumedIngredientId = consumedItemId;
        }

        if (sessionIngredientIds.Count < UnifiedCraftSlotCount)
        {
            sessionIngredientIds.Add(consumedItemId);
        }

        // 異素材が混ざるたびに（および追加入力のたびに）成果物候補を再ジャッジ
        pendingResultItemId = CraftingRecipeManager.ResolveCraftingResult(sessionIngredientIds);

        float stackBoost = craftValue * Mathf.Max(1, consecutiveSameIngredientCount);
        float craftMultiplier = isSameAsLast ? stackBoost : craftValue;

        sessionMaterialCraftMultiplier *= craftValue;
        materialContributionLogs.Add(
            isSameAsLast
                ? $"SameMaterial[{consumedItemId}] stack={consecutiveSameIngredientCount} delta×{craftMultiplier:F2} recipe->{pendingResultItemId}"
                : $"NewMaterial[{consumedItemId}] delta×{craftMultiplier:F2} recipe->{pendingResultItemId} @ {actionDescription}");

        Debug.Log(
            $"<color=#B39DDB>【工房レシピ】素材={consumedItemId} / " +
            $"{(isSameAsLast ? "同素材ブースト" : "異素材分岐")} / 暫定成果物={pendingResultItemId}</color>");

        return craftMultiplier;
    }

    /// <summary>科学上限突破（熱科学バースト）を検知したら DemoTimeLineManager へ最終ジャッジを委譲します。</summary>
    private void TryAutoFinalizeCraftingOnThermalBurst()
    {
        DetailedCraftingProcessManager processManager = DetailedCraftingProcessManager.Instance;
        int actionCount = processManager != null ? processManager.currentProcessLogs.Count : alchUnifiedActionCount;
        DemoCraftingJudgmentResult judgment = EvaluateDemoCraftingJudgment(actionCount);
        if (!judgment.IsThermalBurstFailure)
        {
            return;
        }

        DemoTimeLineManager timeline = DemoTimeLineManager.Instance;
        if (timeline == null || timeline.CurrentState != DemoState.CraftingPhase)
        {
            return;
        }

        timeline.FinishCraftingAndEnterResult();
    }

    /// <summary>鍛冶（Forge）の統一スロット1〜5を実行します。</summary>
    private void ExecuteForgeUnifiedSlot(int slot1To5, DetailedCraftingProcessManager processManager)
    {
        switch (slot1To5)
        {
            case 1:
                ApplyForgeAction(
                    processManager,
                    slot1To5,
                    "材料となるインゴットの精製から始める",
                    (ParamPurity, 5f),
                    (ParamTemperature, 5f));
                break;
            case 2:
                ApplyForgeAction(
                    processManager,
                    slot1To5,
                    "魔物素材を投入して『魔物合金』へ昇華させる",
                    (ParamDensity, 10f),
                    (ParamPurity, -5f));
                break;
            case 3:
                ApplyForgeAction(
                    processManager,
                    slot1To5,
                    "大槌で一気に形を打ち出す（1200度加熱）",
                    (ParamDensity, 15f),
                    (ParamTemperature, 280f));
                ForgeVisualController.NotifyHammerStrikeGlobally();
                break;
            case 4:
                ApplyForgeAction(
                    processManager,
                    slot1To5,
                    "砥石による丁寧な『研磨』を施す",
                    (ParamPurity, 8f),
                    (ParamDensity, 3f));
                break;
            case 5:
                ApplyForgeAction(
                    processManager,
                    slot1To5,
                    "術式を展開し、魔法による魔力行使（属性付与）を行う",
                    (ParamTemperature, 20f),
                    (ParamPurity, 5f));
                break;
            default:
                Debug.LogWarning($"[CraftingStatusManager] 未対応の鍛冶スロット: {slot1To5}");
                break;
        }
    }

    /// <summary>調合（Alch）の統一スロット1〜5を実行します。</summary>
    private void ExecuteAlchUnifiedSlot(int slot1To5, DetailedCraftingProcessManager processManager)
    {
        switch (slot1To5)
        {
            case 1:
                ApplyAlchAction(
                    processManager,
                    slot1To5,
                    "素材から『茎の部分』をちぎって排除する",
                    (ParamExtractionLevel, 5f),
                    (ParamDissolutionRate, -2f));
                AlchVisualController.NotifyLeftStirGlobally();
                break;
            case 2:
                ApplyAlchAction(
                    processManager,
                    slot1To5,
                    "素材をちぎらず『丸ごと釜に投入』する",
                    (ParamDissolutionRate, 10f),
                    (ParamExtractionLevel, -3f));
                AlchVisualController.NotifyRightStirGlobally();
                break;
            case 3:
                ApplyAlchAction(
                    processManager,
                    slot1To5,
                    "薬草を乳鉢で『跡形もなくすり潰す』",
                    (ParamExtractionLevel, 12f),
                    (ParamDissolutionRate, 5f));
                AlchVisualController.NotifyGrindHerbGlobally();
                break;
            case 4:
                ApplyAlchAction(
                    processManager,
                    slot1To5,
                    "強火で一気に沸騰させる",
                    (ParamPotTemperature, 48f),
                    (ParamDissolutionRate, 8f));
                AlchVisualController.NotifyRapidBoilGlobally();
                break;
            case 5:
                ApplyAlchAction(
                    processManager,
                    slot1To5,
                    "火にかけず、冷水のまま別の特殊素材を投入する",
                    (ParamPotTemperature, -15f),
                    (ParamExtractionLevel, 6f));
                AlchVisualController.NotifyLeftStirGlobally();
                break;
            default:
                Debug.LogWarning($"[CraftingStatusManager] 未対応の調合スロット: {slot1To5}");
                break;
        }
    }

    /// <summary>鍛冶パラメータを変動し、工程ログとステータス表示を出力します。</summary>
    private void ApplyForgeAction(
        DetailedCraftingProcessManager processManager,
        int craftSlot,
        string actionDescription,
        params (string param, float delta)[] modifications)
    {
        float craftMultiplier = ApplyMaterialCraftInfluence(craftSlot, CraftTypeForge, actionDescription);
        float appliedHeatDelta = 0f;

        for (int i = 0; i < modifications.Length; i++)
        {
            (string param, float delta) = modifications[i];
            float scaledDelta = delta * craftMultiplier;
            float adjustedDelta = ArtsEffectExecutor.ScaleCraftDelta(CraftTypeForge, param, scaledDelta);
            if (string.Equals(param, ParamTemperature, StringComparison.OrdinalIgnoreCase) && adjustedDelta > 0f)
            {
                appliedHeatDelta += adjustedDelta;
            }

            ModifyParam(CraftTypeForge, param, adjustedDelta);
        }

        ArtsEffectExecutor.ApplyHeatOxidationPurityTax(CraftTypeForge, appliedHeatDelta);

        processManager.LogAction(actionDescription);
        LogBlindCraftParametersToConsole(CraftTypeForge);
        Debug.Log(BuildStatusDisplayRichText(CraftTypeForge));
    }

    /// <summary>調合パラメータを変動し、工程ログとステータス表示を出力します。</summary>
    private void ApplyAlchAction(
        DetailedCraftingProcessManager processManager,
        int craftSlot,
        string actionDescription,
        params (string param, float delta)[] modifications)
    {
        float craftMultiplier = ApplyMaterialCraftInfluence(craftSlot, CraftTypeAlch, actionDescription);

        for (int i = 0; i < modifications.Length; i++)
        {
            (string param, float delta) = modifications[i];
            float scaledDelta = delta * craftMultiplier;
            float adjustedDelta = ArtsEffectExecutor.ScaleCraftDelta(CraftTypeAlch, param, scaledDelta);
            ModifyParam(CraftTypeAlch, param, adjustedDelta);
        }

        processManager.LogAction(actionDescription);
        LogBlindCraftParametersToConsole(CraftTypeAlch);
        Debug.Log(BuildStatusDisplayRichText(CraftTypeAlch));
    }

    /// <summary>
    /// ブラインド UI では非表示の裏パラメータを、開発者向けにコンソールへ明示出力します。
    /// </summary>
    /// <param name="craftType">Forge / Alch</param>
    public void LogBlindCraftParametersToConsole(string craftType)
    {
        string normalizedType = NormalizeCraftType(craftType);
        if (!definitionTables.TryGetValue(normalizedType, out Dictionary<string, CraftingParamDefinition> defs))
        {
            return;
        }

        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        sb.Append("<color=#CE93D8><b>[BLIND PARAMS · ");
        sb.Append(normalizedType);
        sb.Append("]</b></color> ");

        bool first = true;
        foreach (KeyValuePair<string, CraftingParamDefinition> pair in defs)
        {
            if (!first)
            {
                sb.Append(" | ");
            }

            first = false;
            float value = GetParam(normalizedType, pair.Key);
            sb.Append("<color=#E1BEE7>");
            sb.Append(pair.Key);
            sb.Append("=</color><color=#FFFFFF>");
            sb.Append(value.ToString("F1", System.Globalization.CultureInfo.InvariantCulture));
            sb.Append("</color>");
        }

        if (string.Equals(normalizedType, CraftTypeForge, StringComparison.OrdinalIgnoreCase))
        {
            sb.Append(" | <color=#FFAB91>PeakTemperature=");
            sb.Append(peakForgeTemperature.ToString("F1", System.Globalization.CultureInfo.InvariantCulture));
            sb.Append("℃</color>");
        }

        if (string.Equals(normalizedType, CraftTypeAlch, StringComparison.OrdinalIgnoreCase))
        {
            sb.Append(" | <color=#FFAB91>工程回数=");
            sb.Append(alchUnifiedActionCount);
            sb.Append("</color>");
        }

        Debug.Log(sb.ToString());
    }
}

/// <summary>Play 開始時に CraftingStatusManager を DebugSystemsHub へ自動配置します。</summary>
public static class CraftingStatusBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoAttach()
    {
        AttachToDebugSystemsHub();
    }

    /// <summary>DebugSystemsHub に CraftingStatusManager が無ければ追加します。</summary>
    public static void AttachToDebugSystemsHub()
    {
        CraftingStatusManager.EnsureInstance();
    }
}

using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

// =============================================================================
// オート廃止・設備依存度・アタッチメント制限・決定論的スコアリング
// 連携: GamePhaseEventBridge / PlayerStatusManager / RuntimeInGameUIManager
// =============================================================================

/// <summary>生産職種（内部的には同一の3大パラメータ、表示名目のみ切替）。</summary>
public enum CraftProfessionType
{
    /// <summary>鍛冶（密度・熱量・純度）— 設備依存ヘビー級</summary>
    Forge,

    /// <summary>調合（抽出度・釜温・溶解度）— 備品投資ライト級</summary>
    Alch
}

/// <summary>介入アクションの実行結果。</summary>
public readonly struct CraftActionResult
{
    public bool Succeeded { get; }
    public string Message { get; }

    public CraftActionResult(bool succeeded, string message)
    {
        Succeeded = succeeded;
        Message = message ?? string.Empty;
    }

    public static CraftActionResult Success(string message = "")
    {
        return new CraftActionResult(true, message);
    }

    public static CraftActionResult Rejected(string message)
    {
        return new CraftActionResult(false, message);
    }
}

/// <summary>職種・ロケーション・備品に基づくパラメータ上限とアクション可否。</summary>
public sealed class CraftFacilityLimits
{
    /// <summary>ParamA の上限。</summary>
    public float MaxParamA { get; }

    /// <summary>ParamB の上限。</summary>
    public float MaxParamB { get; }

    /// <summary>ParamC の上限。</summary>
    public float MaxParamC { get; }

    /// <summary>アクション3（高度な術式干渉）を実行可能か。</summary>
    public bool CanExecuteAdvancedAction { get; }

    /// <summary>120点限界突破が可能か。</summary>
    public bool CanBreakthrough { get; }

    /// <summary>制限の説明（ログ用）。</summary>
    public string RestrictionSummary { get; }

    public CraftFacilityLimits(
        float maxParamA,
        float maxParamB,
        float maxParamC,
        bool canExecuteAdvancedAction,
        bool canBreakthrough,
        string restrictionSummary)
    {
        MaxParamA = maxParamA;
        MaxParamB = maxParamB;
        MaxParamC = maxParamC;
        CanExecuteAdvancedAction = canExecuteAdvancedAction;
        CanBreakthrough = canBreakthrough;
        RestrictionSummary = restrictionSummary ?? string.Empty;
    }
}

/// <summary>職種・設備状態から決定論的に上限を算出します。</summary>
public static class CraftFacilityResolver
{
    /// <summary>現在の生産環境に応じた制限値を返します。</summary>
    public static CraftFacilityLimits Resolve(
        CraftProfessionType profession,
        bool isSafetyZone,
        bool hasHeavyTool,
        bool hasAdvancedAttachment)
    {
        switch (profession)
        {
            case CraftProfessionType.Alch:
                if (isSafetyZone && hasAdvancedAttachment)
                {
                    return new CraftFacilityLimits(
                        120f, 120f, 120f,
                        canExecuteAdvancedAction: true,
                        canBreakthrough: true,
                        "街の拠点＋固定設備：高度介入解放・120点限界突破可能");
                }

                return new CraftFacilityLimits(
                    60f, 60f, 60f,
                    canExecuteAdvancedAction: false,
                    canBreakthrough: false,
                    "携帯キットのみ：全パラメータ上限60・アクション3ロック");

            case CraftProfessionType.Forge:
            default:
                if (isSafetyZone)
                {
                    return new CraftFacilityLimits(
                        120f, 120f, 120f,
                        canExecuteAdvancedAction: true,
                        canBreakthrough: true,
                        "街の工房：120点限界突破可能");
                }

                if (hasHeavyTool)
                {
                    return new CraftFacilityLimits(
                        100f, 80f, 100f,
                        canExecuteAdvancedAction: true,
                        canBreakthrough: false,
                        "屋外＋携帯魔力炉：ParamB上限80（重量ペナルティあり）");
                }

                return new CraftFacilityLimits(
                    100f, 30f, 100f,
                    canExecuteAdvancedAction: true,
                    canBreakthrough: false,
                    "屋外・手ぶら：炉なし ParamB上限30");
        }
    }
}

/// <summary>職種ごとに ParamA/B/C へ割り当てる表示ラベル。</summary>
public sealed class CraftParameterLabels
{
    public string ParamA { get; }
    public string ParamB { get; }
    public string ParamC { get; }
    public string ProfessionDisplayName { get; }

    public CraftParameterLabels(string professionDisplayName, string paramA, string paramB, string paramC)
    {
        ProfessionDisplayName = professionDisplayName ?? string.Empty;
        ParamA = paramA ?? string.Empty;
        ParamB = paramB ?? string.Empty;
        ParamC = paramC ?? string.Empty;
    }
}

/// <summary>職種→パラメータ名目のカタログ。</summary>
public static class CraftProfessionCatalog
{
    public static CraftProfessionType ResolveProfession(string craftType)
    {
        if (string.IsNullOrWhiteSpace(craftType))
        {
            return CraftProfessionType.Forge;
        }

        string normalized = craftType.Trim();
        if (normalized.Equals("Alch", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("Alchemy", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("調合", StringComparison.OrdinalIgnoreCase))
        {
            return CraftProfessionType.Alch;
        }

        return CraftProfessionType.Forge;
    }

    public static CraftParameterLabels GetLabels(CraftProfessionType profession)
    {
        switch (profession)
        {
            case CraftProfessionType.Alch:
                return new CraftParameterLabels(
                    "調合",
                    "抽出度 (Extraction)",
                    "釜の温度 (Temperature)",
                    "溶解度・混ざり度 (Solubility)");
            case CraftProfessionType.Forge:
            default:
                return new CraftParameterLabels(
                    "鍛冶",
                    "密度・結合度 (Density)",
                    "熱量・温度 (Thermal)",
                    "純度・均一性 (Purity)");
        }
    }
}

/// <summary>クラフト中のスナップショット（スコアリング入力）。</summary>
public sealed class CraftExperimentSnapshot
{
    public CraftProfessionType Profession { get; set; }
    public float ParamA { get; set; }
    public float ParamB { get; set; }
    public float ParamC { get; set; }
    public float PeakParamB { get; set; }
    public int ActionCount { get; set; }
    public int CatalystCount { get; set; }
    public int RejectedAdvancedActionCount { get; set; }
    public bool HasCharmEffect { get; set; }
    public bool HadThermalSpike { get; set; }
    public bool IsSafetyZone { get; set; }
    public bool HasHeavyTool { get; set; }
    public bool HasAdvancedAttachment { get; set; }
    public bool HitEquipmentCeiling { get; set; }
    public string FacilityRestrictionSummary { get; set; } = string.Empty;
}

/// <summary>決定論的スコアリングの成果。</summary>
public sealed class CraftScoringResult
{
    public int Score { get; }
    public string ArchetypeLabel { get; }
    public string Description { get; }
    public int DamageBonus { get; }
    public int GuardBonus { get; }
    public int SpeedBonus { get; }
    public int MagicBonus { get; }
    public bool IsJunk { get; }

    public CraftScoringResult(
        int score, string archetypeLabel, string description,
        int damageBonus, int guardBonus, int speedBonus, int magicBonus, bool isJunk)
    {
        Score = score;
        ArchetypeLabel = archetypeLabel ?? string.Empty;
        Description = description ?? string.Empty;
        DamageBonus = damageBonus;
        GuardBonus = guardBonus;
        SpeedBonus = speedBonus;
        MagicBonus = magicBonus;
        IsJunk = isJunk;
    }
}

/// <summary>AI パイプラインへ渡す生産実験レポート。</summary>
public sealed class CraftExperimentReport
{
    public CraftProfessionType Profession { get; set; }
    public float ParamA { get; set; }
    public float ParamB { get; set; }
    public float ParamC { get; set; }
    public float PeakParamB { get; set; }
    public int FinalScore { get; set; }
    public string ArchetypeLabel { get; set; } = string.Empty;
    public string ResultDescription { get; set; } = string.Empty;
    public int DamageBonus { get; set; }
    public int GuardBonus { get; set; }
    public int SpeedBonus { get; set; }
    public int MagicBonus { get; set; }
    public bool HasCharmEffect { get; set; }
    public bool IsSafetyZone { get; set; }
    public bool HasHeavyTool { get; set; }
    public bool HasAdvancedAttachment { get; set; }
    public string FacilityRestrictionSummary { get; set; } = string.Empty;
    public int ActionCount { get; set; }
    public List<string> Materials { get; set; } = new List<string>();
    public List<string> Catalysts { get; set; } = new List<string>();

    public void AppendExperimentSection(StringBuilder sb)
    {
        CraftParameterLabels labels = CraftProfessionCatalog.GetLabels(Profession);
        sb.AppendLine("【職人の自由実験レポート（設備制限・決定論的評価済み）】");
        sb.AppendLine($"- 職種: {labels.ProfessionDisplayName}");
        sb.AppendLine($"- 設備制限: {FacilityRestrictionSummary}");
        sb.AppendLine($"- 携帯魔力炉(hasHeavyTool): {HasHeavyTool}");
        sb.AppendLine($"- 固定設備(hasAdvancedAttachment): {HasAdvancedAttachment}");
        sb.AppendLine($"- {labels.ParamA}: {ParamA:F1}");
        sb.AppendLine($"- {labels.ParamB}: {ParamB:F1}（ピーク {PeakParamB:F1}）");
        sb.AppendLine($"- {labels.ParamC}: {ParamC:F1}");
        sb.AppendLine($"- 最終評価点: {FinalScore} / 100");
        sb.AppendLine($"- アーキタイプ: {ArchetypeLabel}");
        sb.AppendLine($"- 評価: {ResultDescription}");
        sb.AppendLine(
            $"- 確定ボーナス: 攻撃+{DamageBonus} / ガード+{GuardBonus} / スピード+{SpeedBonus} / 魔術+{MagicBonus}");
        sb.AppendLine($"- 魅了効果: {(HasCharmEffect ? "ON" : "OFF")}");
        sb.AppendLine($"- 安全地帯: {(IsSafetyZone ? "街" : "屋外・被弾リスク")}");
        sb.AppendLine($"- 介入回数: {ActionCount}");
        sb.AppendLine("- 投入素材:");
        AppendStringList(sb, Materials, "  - ");
        sb.AppendLine("- 高度介入記録:");
        AppendStringList(sb, Catalysts, "  - ");
        sb.AppendLine(
            "上記の設備制限と偏りを尊重し、職人の試行錯誤から生まれた成果物を generationType craft の JSON で1つ生成せよ。");
    }

    private static void AppendStringList(StringBuilder sb, List<string> items, string prefix)
    {
        if (items == null || items.Count == 0)
        {
            sb.AppendLine($"{prefix}（なし）");
            return;
        }

        for (int i = 0; i < items.Count; i++)
        {
            sb.AppendLine($"{prefix}{items[i]}");
        }
    }
}

public sealed class CraftFinishResult
{
    public bool Succeeded { get; }
    public bool WasInterrupted { get; }
    public CraftScoringResult Scoring { get; }
    public CraftExperimentReport Report { get; }
    public string Message { get; }

    public CraftFinishResult(bool succeeded, bool wasInterrupted, CraftScoringResult scoring,
        CraftExperimentReport report, string message)
    {
        Succeeded = succeeded;
        WasInterrupted = wasInterrupted;
        Scoring = scoring;
        Report = report;
        Message = message ?? string.Empty;
    }
}

/// <summary>3大パラメータの偏りから成果性能を決定論的に算出します。</summary>
public static class CraftDeterministicScorer
{
    public static CraftScoringResult Evaluate(CraftExperimentSnapshot snapshot)
    {
        if (snapshot == null)
        {
            return BuildJunk("評価データが null です。");
        }

        float paramA = snapshot.ParamA;
        float paramB = snapshot.ParamB;
        float paramC = snapshot.ParamC;
        float avg = (paramA + paramB + paramC) / 3f;
        float spread = Mathf.Abs(paramA - avg) + Mathf.Abs(paramB - avg) + Mathf.Abs(paramC - avg);

        if (IsEquipmentLimitedFailure(snapshot, avg))
        {
            return BuildJunk("設備制限による未達。ゴミ・劣化品（10点）。");
        }

        if (IsMeaninglessExperiment(snapshot, avg, spread))
        {
            return BuildJunk("無意味な介入の連打。ゴミ・劣化品（10点）。");
        }

        if (paramA >= 95f && paramC <= 30f)
        {
            return new CraftScoringResult(
                Mathf.Clamp(Mathf.RoundToInt(75f + (paramA - 95f) * 2f), 72, 92),
                "重厚・大剣化（物理・ガード特化）",
                "密度極大・純度最悪の歪な実験。ガードに全振りした巨剣が完成。",
                12, 30, -5, 0, false);
        }

        if (snapshot.PeakParamB >= 90f && paramB < 45f)
        {
            return new CraftScoringResult(
                Mathf.Clamp(Mathf.RoundToInt(68f + snapshot.PeakParamB * 0.15f), 65, 88),
                "神速・細剣化（スピード特化）",
                "熱量が一瞬で跳ね上がり急冷された。耐久は半減するが神速の刃となった。",
                5, -15, 20, 0, false);
        }

        if (snapshot.HasCharmEffect && paramB >= 55f)
        {
            return new CraftScoringResult(
                78, "魅了付与・調合奇跡",
                "高度介入が成功。魔術寄りの特殊品が生まれた。",
                3, 5, 5, 18, false);
        }

        if (IsBalancedRecipe(paramA, paramB, paramC, spread))
        {
            float balanceFactor = Mathf.Clamp01(1f - spread / 60f);
            int score = Mathf.RoundToInt(Mathf.Lerp(50f, 70f, balanceFactor));
            return new CraftScoringResult(
                score, "及第点・安定ベース武器",
                "レシピ通りの均等バランス。堅実で信頼できる性能。",
                Mathf.RoundToInt(score * 0.12f), Mathf.RoundToInt(score * 0.1f),
                Mathf.RoundToInt(score * 0.06f), Mathf.RoundToInt(score * 0.04f), false);
        }

        int defaultScore = Mathf.Clamp(Mathf.RoundToInt(avg * 0.55f), 25, 65);
        return new CraftScoringResult(
            defaultScore, "実験型・偏りあり",
            "マニュアル外の偏り。一長一短の個性派。",
            Mathf.RoundToInt(paramA * 0.08f), Mathf.RoundToInt(paramC * 0.06f),
            Mathf.RoundToInt(paramB * 0.05f), snapshot.HasCharmEffect ? 8 : 0, false);
    }

    private static bool IsEquipmentLimitedFailure(CraftExperimentSnapshot snapshot, float avg)
    {
        if (snapshot.Profession == CraftProfessionType.Alch &&
            !snapshot.HasAdvancedAttachment &&
            snapshot.RejectedAdvancedActionCount >= 2 &&
            avg < 25f)
        {
            return true;
        }

        if (snapshot.Profession == CraftProfessionType.Forge &&
            !snapshot.IsSafetyZone &&
            !snapshot.HasHeavyTool &&
            snapshot.ParamB >= 29f &&
            snapshot.HitEquipmentCeiling &&
            avg < 22f)
        {
            return true;
        }

        return false;
    }

    private static bool IsBalancedRecipe(float paramA, float paramB, float paramC, float spread)
    {
        return paramA >= 35f && paramA <= 65f &&
               paramB >= 35f && paramB <= 65f &&
               paramC >= 35f && paramC <= 65f &&
               spread <= 25f;
    }

    private static bool IsMeaninglessExperiment(CraftExperimentSnapshot snapshot, float avg, float spread)
    {
        if (avg < 15f) return true;
        if (snapshot.CatalystCount >= 4 && !snapshot.HasCharmEffect) return true;
        if (snapshot.ActionCount > 25 && avg < 30f) return true;
        if (spread > 120f && snapshot.ActionCount < 3) return true;
        return false;
    }

    private static CraftScoringResult BuildJunk(string description)
    {
        return new CraftScoringResult(10, "ゴミ・劣化品", description, 0, 0, 0, 0, true);
    }
}

/// <summary>
/// 設備依存度・アタッチメント制限を内包した汎用生産実験ハブ（シングルトン）。
/// </summary>
public class CraftingExperimentHub : MonoBehaviour
{
    public static CraftingExperimentHub Instance { get; private set; }

    [Header("初期レシピ")]
    [SerializeField] private List<string> defaultMaterials = new List<string> { "鉄鉱石", "獣骨" };

    [Header("デバッグ用携行備品・拠点設備フラグ")]
    [SerializeField] private bool hasHeavyTool;
    [SerializeField] private bool hasAdvancedAttachment;

    [Header("参照")]
    [SerializeField] private GamePhaseEventBridge eventBridge;

    public bool IsActive { get; private set; }
    public CraftProfessionType CurrentProfession { get; private set; } = CraftProfessionType.Forge;
    public float ParamA { get; private set; }
    public float ParamB { get; private set; }
    public float ParamC { get; private set; }
    public float PeakParamB { get; private set; }
    public bool IsSafetyZone { get; private set; }
    public bool HasCharmEffect { get; private set; }
    public bool HasHeavyTool => hasHeavyTool;
    public bool HasAdvancedAttachment => hasAdvancedAttachment;
    public int ActionCount { get; private set; }
    public int RejectedAdvancedActionCount { get; private set; }
    public IReadOnlyList<string> InvestedMaterials => investedMaterials;
    public IReadOnlyList<string> AdvancedInterventionLog => advancedInterventionLog;

    private bool hitEquipmentCeiling;
    private bool sessionRequestedTownSafety;
    private readonly List<string> investedMaterials = new List<string>();
    private readonly List<string> advancedInterventionLog = new List<string>();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[CraftingExperimentHub] 重複インスタンスを検出しました。");
            return;
        }

        Instance = this;
        if (eventBridge == null)
        {
            eventBridge = GamePhaseEventBridge.Instance ?? FindAnyObjectByType<GamePhaseEventBridge>();
        }
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public static CraftingExperimentHub EnsureInstance()
    {
        if (Instance != null) return Instance;

        GameObject hub = GameObject.Find("DebugSystemsHub");
        if (hub != null)
        {
            CraftingExperimentHub onHub = hub.GetComponent<CraftingExperimentHub>();
            return onHub != null ? onHub : hub.AddComponent<CraftingExperimentHub>();
        }

        return new GameObject(nameof(CraftingExperimentHub)).AddComponent<CraftingExperimentHub>();
    }

    /// <summary>現在の設備制限を取得します。</summary>
    public CraftFacilityLimits GetCurrentFacilityLimits()
    {
        return CraftFacilityResolver.Resolve(
            CurrentProfession, IsSafetyZone, hasHeavyTool, hasAdvancedAttachment);
    }

    /// <summary>
    /// 工房セッション中に CraftingStatusManager が解決した暫定成果物 ID を返します。
    /// 詳細工程（1〜5キー）での素材投入のたびに更新されます。
    /// </summary>
    public string GetPendingCraftResultItemId()
    {
        CraftingStatusManager status = CraftingStatusManager.Instance;
        return status != null ? status.PendingResultItemId : string.Empty;
    }

    /// <summary>蓄積素材リストから最終成果物を確定し、品質パケット用スナップショットを返します。</summary>
    public CraftingResultItemSnapshot ResolveCraftResultFromSessionIngredients()
    {
        CraftingStatusManager status = CraftingStatusManager.Instance;
        if (status == null)
        {
            return CraftingRecipeManager.BuildSnapshot(CraftRecipeIds.CraftFailure, true);
        }

        return status.FinalizeCraftingResultForPacket();
    }

    /// <summary>デバッグ用：携帯魔力炉フラグを反転します。</summary>
    public void ToggleHeavyToolFlag()
    {
        hasHeavyTool = !hasHeavyTool;
        Debug.Log(
            $"<color=#FFD54F>[CraftingExperimentHub] hasHeavyTool = {hasHeavyTool} " +
            $"（屋外鍛冶 ParamB 上限: {(hasHeavyTool ? "80" : "30")}）</color>");
        if (IsActive) LogCurrentParameters();
        NotifyVisualUi();
    }

    /// <summary>インベントリ所持状態から hasHeavyTool を同期します。</summary>
    public void SetHeavyToolFromInventory(bool equipped)
    {
        if (hasHeavyTool == equipped)
        {
            return;
        }

        hasHeavyTool = equipped;
        Debug.Log(
            $"<color=#FFD54F>[CraftingExperimentHub] インベントリ連動: hasHeavyTool = {hasHeavyTool}</color>");
        if (IsActive)
        {
            LogCurrentParameters();
            NotifyVisualUi();
        }
    }

    /// <summary>デバッグ用：拠点固定設備フラグを反転します。</summary>
    public void ToggleAdvancedAttachmentFlag()
    {
        bool wasAttached = hasAdvancedAttachment;
        hasAdvancedAttachment = !hasAdvancedAttachment;
        Debug.Log(
            $"<color=#FFD54F>[CraftingExperimentHub] hasAdvancedAttachment = {hasAdvancedAttachment} " +
            $"（調合アクション3: {(hasAdvancedAttachment && IsSafetyZone ? "解放" : "ロック")}）</color>");
        if (IsActive) LogCurrentParameters();
        bool justUnlocked = !wasAttached && hasAdvancedAttachment &&
                            IsActive && CurrentProfession == CraftProfessionType.Alch;
        NotifyVisualUi(justUnlocked);
    }

    /// <summary>デバッグ用：両フラグの状態をログ出力します。</summary>
    public void ToggleDebugUpgradeFlags()
    {
        ToggleHeavyToolFlag();
        ToggleAdvancedAttachmentFlag();
    }

    /// <summary>生産開始時に街の安全地帯を要求したか（ワールド町判定前のリクエスト）。</summary>
    public bool SessionRequestedTownSafety => sessionRequestedTownSafety;

    /// <summary>ワールド町判定の変化に合わせて、進行中セッションの安全地帯を再評価します。</summary>
    public void RefreshEffectiveSafetyZoneFromWorld()
    {
        if (!IsActive)
        {
            return;
        }

        bool previous = IsSafetyZone;
        IsSafetyZone = TownSafetyZoneGate.ResolveEffectiveSafetyZone(sessionRequestedTownSafety);
        if (previous == IsSafetyZone)
        {
            return;
        }

        CraftFacilityLimits limits = GetCurrentFacilityLimits();
        ApplyFacilityClamp(limits);

        string label = IsSafetyZone ? "安全地帯（町内）" : "屋外・被弾リスク";
        Debug.Log(
            $"<color=#4DD0E1>[CraftingExperimentHub] 町判定連動で安全地帯を再評価 → <b>{label}</b></color>");
        LogCurrentParameters();
        NotifyVisualUi();
    }

    /// <summary>生産を開始します。オート進行は行いません。</summary>
    public bool StartCrafting(string craftType, bool isSafetyZone)
    {
        if (IsActive)
        {
            Debug.LogWarning("[CraftingExperimentHub] 既に生産中です。同じトグルキーで終了してください。");
            return false;
        }

        CurrentProfession = CraftProfessionCatalog.ResolveProfession(craftType);
        sessionRequestedTownSafety = isSafetyZone;
        IsSafetyZone = TownSafetyZoneGate.ResolveEffectiveSafetyZone(isSafetyZone);
        IsActive = true;
        HasCharmEffect = false;
        ActionCount = 0;
        RejectedAdvancedActionCount = 0;
        hitEquipmentCeiling = false;

        ParamA = 45f;
        ParamB = 50f;
        ParamC = 48f;
        PeakParamB = ParamB;

        investedMaterials.Clear();
        advancedInterventionLog.Clear();
        if (defaultMaterials != null)
        {
            for (int i = 0; i < defaultMaterials.Count; i++)
            {
                if (!string.IsNullOrWhiteSpace(defaultMaterials[i]))
                {
                    investedMaterials.Add(defaultMaterials[i]);
                }
            }
        }

        CraftParameterLabels labels = CraftProfessionCatalog.GetLabels(CurrentProfession);
        CraftFacilityLimits limits = GetCurrentFacilityLimits();
        ApplyFacilityClamp(limits);

        Debug.Log(
            $"<color=#87CEEB><b>[CraftingExperimentHub] 生産開始【{labels.ProfessionDisplayName}】</b> " +
            $"{ResolveCraftLocationLabel()} / {limits.RestrictionSummary} / " +
            $"素材=[{string.Join(", ", investedMaterials)}]" +
            (sessionRequestedTownSafety && !IsSafetyZone
                ? " <color=#FFAB40>（街モード要求だが屋外判定のため携帯キット制限）</color>"
                : string.Empty) +
            "</color>");
        LogCurrentParameters();
        NotifyVisualUi();
        return true;
    }

    /// <summary>
    /// デモ生産フェーズ用：安全地帯フラグを解除し、被弾バーストの対象にします。
    /// </summary>
    public void MarkAsUnderThreatZone()
    {
        if (!IsActive || !IsSafetyZone)
        {
            return;
        }

        IsSafetyZone = false;
        Debug.Log(
            "<color=#FF7043><b>[CraftingExperimentHub] 敵の脅威下モード</b></color> " +
            "— 被弾で投入素材がバーストします。");
        NotifyVisualUi();
    }

    /// <summary>介入アクションを実行します。設備・アタッチメント制限を検査します。</summary>
    public CraftActionResult ExecuteCraftAction(int actionType)
    {
        if (!IsActive)
        {
            return CraftActionResult.Rejected("生産未開始です。");
        }

        CraftParameterLabels labels = CraftProfessionCatalog.GetLabels(CurrentProfession);
        CraftFacilityLimits limits = GetCurrentFacilityLimits();

        if (actionType == 3)
        {
            return ExecuteAdvancedAction(labels, limits);
        }

        ActionCount++;

        switch (actionType)
        {
            case 1:
                ParamA += 18f;
                ParamB -= 12f;
                ParamC -= 5f;
                Debug.Log(
                    $"<color=#FFA07A><b>[介入①:大槌/右攪拌]</b> {labels.ParamA}↑大 / " +
                    $"{labels.ParamB}↓ / {labels.ParamC}↓微</color>");
                break;

            case 2:
                ParamA += 2f;
                ParamC += 8f;
                ParamB -= 3f;
                Debug.Log(
                    $"<color=#98FB98><b>[介入②:小槌/左攪拌]</b> {labels.ParamC}↑じわじわ / " +
                    $"{labels.ParamB}↓微</color>");
                break;

            default:
                return CraftActionResult.Rejected($"未対応 actionType={actionType}");
        }

        ApplyFacilityClamp(limits);
        UpdatePeakParamB();
        LogCurrentParameters();
        NotifyVisualUi();
        return CraftActionResult.Success();
    }

    /// <summary>アクション3：高度な術式干渉。調合は固定設備＋街のみ解放。</summary>
    private CraftActionResult ExecuteAdvancedAction(CraftParameterLabels labels, CraftFacilityLimits limits)
    {
        if (!limits.CanExecuteAdvancedAction)
        {
            RejectedAdvancedActionCount++;
            string reason = CurrentProfession == CraftProfessionType.Alch
                ? "<color=#FF6B6B><b>【設備不足】</b>高度な術式干渉を拒否しました。" +
                  "街の拠点に固定設備(hasAdvancedAttachment)が必要です。</color>"
                : "<color=#FF6B6B><b>【設備不足】</b>高度な術式干渉を拒否しました。</color>";
            Debug.Log(reason);
            return CraftActionResult.Rejected("設備不足：高度な術式干渉は実行できません。");
        }

        ActionCount++;
        string interventionLabel = CurrentProfession == CraftProfessionType.Alch
            ? "魔力遠心分離"
            : "特殊触媒投入";
        advancedInterventionLog.Add(interventionLabel);

        ParamB += 30f;
        ParamA += 10f;
        ParamC += 12f;
        HasCharmEffect = true;

        ApplyFacilityClamp(limits);
        UpdatePeakParamB();

        Debug.Log(
            $"<color=#DDA0DD><b>[介入③:高度干渉]</b> {interventionLabel} → パラメータ劇的変動！" +
            " 【魅了効果フラグ ON】</color>");
        LogCurrentParameters();
        NotifyVisualUi();
        return CraftActionResult.Success(interventionLabel);
    }

    /// <summary>後方互換：触媒投入はアクション3へ委譲します。</summary>
    public bool InsertCatalyst(string catalystId)
    {
        CraftActionResult result = ExecuteCraftAction(3);
        if (result.Succeeded && !string.IsNullOrWhiteSpace(catalystId))
        {
            advancedInterventionLog[advancedInterventionLog.Count - 1] = catalystId;
        }

        return result.Succeeded;
    }

    /// <summary>
    /// デモ CraftingPhase 用：被弾バースト対象として脅威ゾーンを強制適用します。
    /// 町内にいても isSafetyZone=false を維持し、投入素材のバーストを有効化します。
    /// </summary>
    public void ApplyDemoCraftingPhaseThreatMode()
    {
        if (!IsActive)
        {
            return;
        }

        sessionRequestedTownSafety = false;
        if (IsSafetyZone)
        {
            IsSafetyZone = false;
            Debug.Log(
                "<color=#FF7043><b>[CraftingExperimentHub] デモ脅威ゾーン強制</b></color> " +
                "— 工房付近の敵脅威により被弾バーストが有効化されました。");
            NotifyVisualUi();
            return;
        }

        IsSafetyZone = false;
    }

    /// <summary>
    /// CraftingPhase 中被弾した際の素材バーストを実行します（バックパックは保護）。
    /// </summary>
    /// <returns>バーストが実行された場合 true</returns>
    public bool InterruptCraftingPhaseByEnemyDamage()
    {
        ApplyDemoCraftingPhaseThreatMode();
        return InterruptByDamage();
    }

    /// <summary>被弾時：屋外の生産セッションを中断（バックパックは保護）。</summary>
    public bool InterruptByDamage()
    {
        if (!IsActive)
        {
            return false;
        }

        if (IsSafetyZone)
        {
            Debug.Log(
                "<color=#90EE90>[CraftingExperimentHub] 街の工房では被弾しても継続（完全安全）。</color>");
            return false;
        }

        List<string> lost = new List<string>(investedMaterials);
        lost.AddRange(advancedInterventionLog);

        InGameVisualUIManager.EnsureInstance().PlayBurstCancelFlash();
        ResetSession();

        Debug.Log(
            $"<color=#FF4444><b>[CraftingExperimentHub] バーストキャンセル！</b> " +
            $"屋外生産中の被弾。投入素材のみロス（バックパックは安全）: {string.Join(", ", lost)}</color>");
        NotifyVisualUi();
        return true;
    }

    /// <summary>現在の職種を例外回収用の文字列（Forge / Alch）で返します。</summary>
    public string GetCraftTypeKey()
    {
        return CurrentProfession == CraftProfessionType.Alch ? "Alch" : "Forge";
    }

    /// <summary>例外回収用の場所ラベルを返します（歴史ハブ・町判定を反映）。</summary>
    public string ResolveCraftLocationLabel()
    {
        return TownSafetyZoneGate.ResolveAreaLabel(IsSafetyZone);
    }

    /// <summary>デバッグ用：意図的な未知ルート状態へパラメータと触媒を書き換えます。</summary>
    public bool ApplyDebugExceptionCraftState(IReadOnlyList<string> testCatalysts = null)
    {
        if (!IsActive)
        {
            return false;
        }

        ParamA = 99f;
        ParamB = 99f;
        ParamC = 1f;
        PeakParamB = Mathf.Max(PeakParamB, ParamB);
        advancedInterventionLog.Clear();

        if (testCatalysts != null)
        {
            for (int i = 0; i < testCatalysts.Count; i++)
            {
                if (!string.IsNullOrWhiteSpace(testCatalysts[i]))
                {
                    advancedInterventionLog.Add(testCatalysts[i]);
                }
            }
        }

        if (advancedInterventionLog.Count == 0)
        {
            advancedInterventionLog.Add("debug_void_catalyst_alpha");
            advancedInterventionLog.Add("debug_unstable_ether_residue");
        }

        Debug.Log(
            "<color=#FF7043><b>[CraftingExperimentHub] デバッグ例外状態を適用</b></color> " +
            $"ParamA=99 / ParamB=99 / ParamC=1 / 触媒=[{string.Join(", ", advancedInterventionLog)}]");
        LogCurrentParameters();
        NotifyVisualUi();
        return true;
    }

    /// <summary>生産終了。決定論的評価 → ステータス反映 → AI パイプラインへ。</summary>
    public CraftFinishResult FinishCrafting(PlayerStatusManager statusManager, Action<string> log = null)
    {
        if (!IsActive)
        {
            const string message = "生産が開始されていません。";
            log?.Invoke(message);
            return new CraftFinishResult(false, false, null, null, message);
        }

        CraftExperimentSnapshot snapshot = BuildSnapshot();
        TryCollectCraftingException(snapshot);

        CraftScoringResult scoring = CraftDeterministicScorer.Evaluate(snapshot);
        CraftExperimentReport report = BuildReport(snapshot, scoring);

        statusManager?.AddVisibleBonuses(
            scoring.DamageBonus, scoring.GuardBonus, scoring.SpeedBonus, scoring.MagicBonus, 0);

        eventBridge ??= GamePhaseEventBridge.Instance ?? FindAnyObjectByType<GamePhaseEventBridge>();
        eventBridge?.SubmitCraftingExperiment(report, scoring.Score / 100f);

        string richLog = FormatFinishLog(scoring, report);
        log?.Invoke(richLog);
        Debug.Log(richLog);

        RuntimeInGameUIManager.EnsureInstance().ShowCraftingResultPopup(
            scoring.ArchetypeLabel,
            $"{scoring.Description}\n評価 {scoring.Score}点 / " +
            $"攻撃+{scoring.DamageBonus} ガード+{scoring.GuardBonus} " +
            $"スピード+{scoring.SpeedBonus} 魔術+{scoring.MagicBonus}",
            scoring.IsJunk);

        if (eventBridge != null && RealAIHttpClient.IsApiKeyConfigured)
        {
            eventBridge.TriggerCraftingPipelineFromExperiment(log);
        }
        else
        {
            log?.Invoke(
                "<color=#FFD54F>[CraftingExperimentHub] ローカル評価完了。API未設定のため Gemini 連携スキップ。</color>");
        }

        ResetSession();
        NotifyVisualUi();
        return new CraftFinishResult(true, false, scoring, report, scoring.Description);
    }

    public string FormatParameterStatusLog()
    {
        CraftParameterLabels labels = CraftProfessionCatalog.GetLabels(CurrentProfession);
        CraftFacilityLimits limits = GetCurrentFacilityLimits();
        return $"<color=#B0E0E6>[{labels.ProfessionDisplayName}] " +
               $"{labels.ParamA}={ParamA:F0}(≤{limits.MaxParamA:F0}) / " +
               $"{labels.ParamB}={ParamB:F0}(≤{limits.MaxParamB:F0}) / " +
               $"{labels.ParamC}={ParamC:F0}(≤{limits.MaxParamC:F0}) " +
               $"(介入{ActionCount}回)</color>";
    }

    private void ApplyFacilityClamp(CraftFacilityLimits limits)
    {
        float beforeB = ParamB;
        ParamA = Mathf.Clamp(ParamA, 0f, limits.MaxParamA);
        ParamB = Mathf.Clamp(ParamB, 0f, limits.MaxParamB);
        ParamC = Mathf.Clamp(ParamC, 0f, limits.MaxParamC);

        if (Mathf.Abs(beforeB - ParamB) > 0.01f && ParamB >= limits.MaxParamB - 0.01f)
        {
            hitEquipmentCeiling = true;
        }
    }

    private CraftExperimentSnapshot BuildSnapshot()
    {
        CraftFacilityLimits limits = GetCurrentFacilityLimits();
        return new CraftExperimentSnapshot
        {
            Profession = CurrentProfession,
            ParamA = ParamA,
            ParamB = ParamB,
            ParamC = ParamC,
            PeakParamB = PeakParamB,
            ActionCount = ActionCount,
            CatalystCount = advancedInterventionLog.Count,
            RejectedAdvancedActionCount = RejectedAdvancedActionCount,
            HasCharmEffect = HasCharmEffect,
            HadThermalSpike = PeakParamB >= 90f && ParamB < 45f,
            IsSafetyZone = IsSafetyZone,
            HasHeavyTool = hasHeavyTool,
            HasAdvancedAttachment = hasAdvancedAttachment,
            HitEquipmentCeiling = hitEquipmentCeiling,
            FacilityRestrictionSummary = limits.RestrictionSummary
        };
    }

    private CraftExperimentReport BuildReport(CraftExperimentSnapshot snapshot, CraftScoringResult scoring)
    {
        return new CraftExperimentReport
        {
            Profession = snapshot.Profession,
            ParamA = snapshot.ParamA,
            ParamB = snapshot.ParamB,
            ParamC = snapshot.ParamC,
            PeakParamB = snapshot.PeakParamB,
            FinalScore = scoring.Score,
            ArchetypeLabel = scoring.ArchetypeLabel,
            ResultDescription = scoring.Description,
            DamageBonus = scoring.DamageBonus,
            GuardBonus = scoring.GuardBonus,
            SpeedBonus = scoring.SpeedBonus,
            MagicBonus = scoring.MagicBonus,
            HasCharmEffect = snapshot.HasCharmEffect,
            IsSafetyZone = IsSafetyZone,
            HasHeavyTool = hasHeavyTool,
            HasAdvancedAttachment = hasAdvancedAttachment,
            FacilityRestrictionSummary = snapshot.FacilityRestrictionSummary,
            ActionCount = snapshot.ActionCount,
            Materials = new List<string>(investedMaterials),
            Catalysts = new List<string>(advancedInterventionLog)
        };
    }

    private static string FormatFinishLog(CraftScoringResult scoring, CraftExperimentReport report)
    {
        CraftParameterLabels labels = CraftProfessionCatalog.GetLabels(report.Profession);
        string color = scoring.IsJunk ? "#FF6B6B" : "#50FA7B";
        return $"<color={color}><b>【生産確定】{scoring.ArchetypeLabel}</b> " +
               $"評価 {scoring.Score}点 / {labels.ParamA}={report.ParamA:F0} " +
               $"{labels.ParamB}={report.ParamB:F0} {labels.ParamC}={report.ParamC:F0} / " +
               $"攻撃+{scoring.DamageBonus} ガード+{scoring.GuardBonus} " +
               $"スピード+{scoring.SpeedBonus} 魔術+{scoring.MagicBonus}</color>";
    }

    private void UpdatePeakParamB() => PeakParamB = Mathf.Max(PeakParamB, ParamB);

    /// <summary>正規レシピ外の介入を検知し、例外パケットを回収します。</summary>
    private void TryCollectCraftingException(CraftExperimentSnapshot snapshot)
    {
        if (snapshot == null)
        {
            return;
        }

        string craftType = GetCraftTypeKey();
        List<string> catalysts = new List<string>(advancedInterventionLog);
        CraftingExceptionCollector collector = CraftingExceptionCollector.EnsureInstance();

        if (collector.CheckIfRecipeDefined(
                craftType,
                snapshot.ParamA,
                snapshot.ParamB,
                snapshot.ParamC,
                catalysts))
        {
            return;
        }

        string era = ChronosCoordinateHub.Instance != null
            ? ChronosCoordinateHub.Instance.currentGlobalEra
            : ChronosCoordinateHub.DefaultInitialEra;

        collector.CollectAndPacketize(
            era,
            ResolveCraftLocationLabel(),
            craftType,
            snapshot.ParamA,
            snapshot.ParamB,
            snapshot.ParamC,
            catalysts);
    }

    private void LogCurrentParameters()
    {
        CraftParameterLabels labels = CraftProfessionCatalog.GetLabels(CurrentProfession);
        CraftFacilityLimits limits = GetCurrentFacilityLimits();
        Debug.Log(
            $"<color=#B0E0E6>[CraftingExperimentHub] {labels.ParamA}={ParamA:F1}(≤{limits.MaxParamA}) / " +
            $"{labels.ParamB}={ParamB:F1}(≤{limits.MaxParamB}, peak {PeakParamB:F1}) / " +
            $"{labels.ParamC}={ParamC:F1}(≤{limits.MaxParamC})</color>");
        Debug.Log($"<color=#AAAAAA>  └ 制限: {limits.RestrictionSummary}</color>");
    }

    /// <summary>ResultPhase 着地時にハブセッションを停止し、工程の自動再開を防ぎます。</summary>
    public void ShutdownForResultLanding()
    {
        if (!IsActive)
        {
            return;
        }

        ResetSession();
    }

    private void ResetSession()
    {
        IsActive = false;
        IsSafetyZone = false;
        sessionRequestedTownSafety = false;
        HasCharmEffect = false;
        ActionCount = 0;
        RejectedAdvancedActionCount = 0;
        hitEquipmentCeiling = false;
        investedMaterials.Clear();
        advancedInterventionLog.Clear();
        ParamA = ParamB = ParamC = PeakParamB = 0f;
    }

    /// <summary>InGameVisualUIManager へゼンゼロ風ブラインドUI更新を通知します。</summary>
    private void NotifyVisualUi(bool advancedCommandJustUnlocked = false)
    {
        InGameVisualUIManager.EnsureInstance()
            .RefreshZenlessStyleUI(this, advancedCommandJustUnlocked);
    }
}

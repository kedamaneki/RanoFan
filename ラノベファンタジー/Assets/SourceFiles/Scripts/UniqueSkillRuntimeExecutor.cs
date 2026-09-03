using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

// =============================================================================
// 転スラ型ユニークスキル — ランタイム権能実行・既存システム配線
// 連携: UniqueSkillRepository / DemoTimeLineManager / VisibleEnemyAI
//       CraftingStatusManager / PlayerStatusManager / TownSafetyZoneGate
//       InGameVisualUIManager / CraftingExperimentHub
// =============================================================================

/// <summary>ユニークスキル専用 effectType（基礎スキル語彙＋拡張）。</summary>
public static class UniqueSkillEffectTypes
{
    public const string CraftParamVision = "Craft_ParamVision";

    public const string UtilityMindAccelerate = "Utility_MindAccelerate";
    public const string UtilityEnemyDetect = "Utility_EnemyDetect";
    public const string UtilityMapRadar = "Utility_MapRadar";

    public const string PsychicManaControl = "Psychic_ManaControl";
    public const string PsychicRegenMana = "Psychic_Regen_Mana";
    public const string PsychicRegenHp = "Psychic_Regen_HP";

    public const string CraftPurityFlatBonus = "Craft_PurityFlatBonus";
    public const string CraftRecipeRecord = "Craft_RecipeRecord";
}

/// <summary>
/// ユニークスキルの権能（effectType）を戦闘・工房・マップ各フェーズへ配線する中央実行体。
/// DemoTimeLineManager の DemoState に同期してティック処理します。
/// </summary>
[DefaultExecutionOrder(-90)]
public class UniqueSkillRuntimeExecutor : MonoBehaviour
{
    public const float DefaultPsychicManaRegenIntervalSeconds = 3f;

    public static UniqueSkillRuntimeExecutor Instance { get; private set; }

    /// <summary>敵感知 UI フラグが変化したときに発火します。</summary>
    public static event Action<bool> EnemyDetectActiveChanged;

    /// <summary>マップレーダー UI フラグが変化したときに発火します。</summary>
    public static event Action<bool> MapRadarActiveChanged;

    /// <summary>工房パラメータ可視化（解析鑑定）フラグが変化したときに発火します。</summary>
    public static event Action<bool> CraftParamVisionActiveChanged;

    [Header("参照（任意・未設定時は自動解決）")]
    [SerializeField] private PlayerStatusManager playerStatusManager;

    [Header("アクティブスキル")]
    [SerializeField] private string equippedUniqueSkillId = "USKL_NAVIGATOR";

    [Header("Psychic 再生")]
    [SerializeField] private float psychicManaRegenIntervalSeconds = DefaultPsychicManaRegenIntervalSeconds;

    private UniqueSkillMaster activeSkill;
    private readonly List<VisibleEnemyAI> trackedEnemies = new List<VisibleEnemyAI>();

    private float mindAccelerateRate;
    private float artsOverlayMindAccelerateRate;
    private float artsOverlayMindAccelerateUntil;
    private float manaControlRate;
    private float psychicManaRegenAmount;
    private float craftPurityFlatBonus;

    private float enemyDetectTimer;
    private float mapRadarTimer;
    private float manaRegenTickAccumulator;

    private bool enemyDetectActive;
    private bool mapRadarActive;
    private bool craftParamVisionActive;
    private bool recipeRecordEnabled;

    /// <summary>現在装備中のユニークスキル。</summary>
    public UniqueSkillMaster ActiveSkill => activeSkill;

    /// <summary>敵感知アラート UI を表示すべきか。</summary>
    public bool IsEnemyDetectActive => enemyDetectActive;

    /// <summary>マップレーダー（安全地帯・鉱脈）UI を表示すべきか。</summary>
    public bool IsMapRadarActive => mapRadarActive;

    /// <summary>ブラインド生産の裏パラメータを正確値表示するか。</summary>
    public bool IsCraftParamVisionActive => craftParamVisionActive;

    /// <summary>最高品質レシピ記録ガイドが有効か。</summary>
    public bool IsRecipeRecordEnabled => recipeRecordEnabled;

    /// <summary>思考加速による敵攻撃予兆の時間伸長率（0.3 → 30% 遅延）。Arts オーバーレイを含みます。</summary>
    public float MindAccelerateRate => Mathf.Max(mindAccelerateRate, GetActiveArtsMindAccelerateRate());

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        ResolveReferences();
        ReloadEquippedSkill();
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void Update()
    {
        DemoState state = ResolveDemoState();
        float deltaTime = Time.deltaTime;

        TickActiveTimers(deltaTime);

        if (state == DemoState.BattlePhase)
        {
            TickBattlePhase(deltaTime);
        }
        else if (state == DemoState.CraftingPhase)
        {
            ApplyCraftingPhaseFlags();
        }
    }

    // -------------------------------------------------------------------------
    // 装備・再読込
    // -------------------------------------------------------------------------

    /// <summary>uniqueSkillId を装備し、権能キャッシュを再構築します。</summary>
    public bool EquipUniqueSkill(string uniqueSkillId)
    {
        equippedUniqueSkillId = uniqueSkillId ?? string.Empty;

        if (UniqueSkillRepository.Instance != null &&
            UniqueSkillRepository.Instance.TryGet(equippedUniqueSkillId, out UniqueSkillMaster fromRepo))
        {
            return SetActiveSkill(fromRepo);
        }

        Debug.LogWarning($"[UniqueSkillRuntimeExecutor] ユニークスキル未検出: {equippedUniqueSkillId}");
        SetActiveSkill(null);
        return false;
    }

    /// <summary>リポジトリから serialized ID を再読込します。</summary>
    public void ReloadEquippedSkill()
    {
        if (!string.IsNullOrWhiteSpace(equippedUniqueSkillId))
        {
            if (UniqueSkillRepository.Instance != null &&
                UniqueSkillRepository.Instance.TryGet(equippedUniqueSkillId, out UniqueSkillMaster skill))
            {
                SetActiveSkill(skill);
                return;
            }
        }

        RebuildAttributeCache(null);
    }

    /// <summary>ランタイムモデルを直接装備します。</summary>
    public bool SetActiveSkill(UniqueSkillMaster skill)
    {
        activeSkill = skill;
        RebuildAttributeCache(skill);
        ApplyPassiveAlwaysOnFlags();
        Debug.Log(
            $"[UniqueSkillRuntimeExecutor] 装備: {(skill != null ? skill.ToString() : "なし")} | " +
            $"思考加速={mindAccelerateRate:P0} 魔力軽減={manaControlRate:P0} 品質+={craftPurityFlatBonus}");
        return skill != null;
    }

    // -------------------------------------------------------------------------
    // 公開 API — 戦闘フェーズ（VisibleEnemyAI / 魔力）
    // -------------------------------------------------------------------------

    /// <summary>manaCost へ Psychic_ManaControl 割合カットを適用します。</summary>
    public float GetModifiedManaCost(float baseCost)
    {
        if (baseCost <= 0f || manaControlRate <= 0f)
        {
            return baseCost;
        }

        float multiplier = Mathf.Clamp01(1f - manaControlRate);
        return Mathf.Max(0f, baseCost * multiplier);
    }

    /// <summary>敵 AI の溜め（Anticipation）秒数を思考加速で引き延ばします。</summary>
    public float ScaleAnticipationDuration(float baseAnticipationSeconds)
    {
        float effectiveRate = MindAccelerateRate;
        if (effectiveRate <= 0f)
        {
            return baseAnticipationSeconds;
        }

        return baseAnticipationSeconds * (1f + effectiveRate);
    }

    /// <summary>VisibleEnemyAI の溜めループ内 elapsed 加算用スケール。</summary>
    public float GetAnticipationElapsedDeltaScale()
    {
        float effectiveRate = MindAccelerateRate;
        if (effectiveRate <= 0f)
        {
            return 1f;
        }

        return 1f / (1f + effectiveRate);
    }

    /// <summary>ArtsEffectExecutor からの一時的な思考加速オーバーレイを登録します。</summary>
    public void RegisterArtsTimedMindAccelerate(float rate, float durationSeconds)
    {
        if (rate <= 0f)
        {
            return;
        }

        artsOverlayMindAccelerateRate = Mathf.Max(artsOverlayMindAccelerateRate, rate);
        if (durationSeconds > 0f)
        {
            artsOverlayMindAccelerateUntil = Mathf.Max(
                artsOverlayMindAccelerateUntil,
                Time.time + durationSeconds);
        }
    }

    private float GetActiveArtsMindAccelerateRate()
    {
        if (artsOverlayMindAccelerateUntil > 0f && Time.time > artsOverlayMindAccelerateUntil)
        {
            artsOverlayMindAccelerateRate = 0f;
            artsOverlayMindAccelerateUntil = 0f;
        }

        return artsOverlayMindAccelerateRate;
    }

    /// <summary>
    /// 敵 AI への配線フック。思考加速倍率を評価し、敵感知対象として登録します。
    /// VisibleEnemyAI の Anticipation ループから ScaleAnticipationDuration / GetAnticipationElapsedDeltaScale を呼ぶ想定。
    /// </summary>
    public void UpdateEnemyTimeScale(VisibleEnemyAI enemyAI)
    {
        if (enemyAI == null || !enemyAI.IsAiEnabled)
        {
            return;
        }

        RegisterTrackedEnemy(enemyAI);
    }

    /// <summary>敵感知 UI 用に追跡中の敵一覧を返します。</summary>
    public IReadOnlyList<VisibleEnemyAI> GetTrackedEnemiesForDetect()
    {
        trackedEnemies.RemoveAll(e => e == null);
        return trackedEnemies;
    }

    /// <summary>アクティブ権能「敵感知」を手動発動（duration 秒）。</summary>
    public void ActivateEnemyDetect(float durationSeconds)
    {
        UniqueAttributeData attr = activeSkill?.FindAttributeByEffectType(UniqueSkillEffectTypes.UtilityEnemyDetect);
        float duration = durationSeconds > 0f
            ? durationSeconds
            : attr != null && attr.duration > 0f ? attr.duration : 8f;
        SetEnemyDetectActive(true, duration);
    }

    // -------------------------------------------------------------------------
    // 公開 API — 工房フェーズ（CraftingStatusManager）
    // -------------------------------------------------------------------------

    /// <summary>工房フェーズ突入時に Craft 系フラグを CraftingStatusManager へ配線します。</summary>
    public void ApplyCraftingBonus(CraftingStatusManager craftStatus)
    {
        craftStatus?.EnsureInitialized();
        ApplyCraftingPhaseFlags();

        if (craftParamVisionActive)
        {
            PublishCraftParamVisionLog(craftStatus);
        }
    }

    /// <summary>クラフト品質スコアへ Craft_PurityFlatBonus を加算します。</summary>
    public float ApplyCraftQualityFlatBonus(float baseQuality)
    {
        if (craftPurityFlatBonus <= 0f)
        {
            return baseQuality;
        }

        return baseQuality + craftPurityFlatBonus;
    }

    /// <summary>Craft_ParamVision 用: Dissolution / Extraction 等の正確値テキスト。</summary>
    public string BuildCraftParamVisionReport(CraftingStatusManager craftStatus, string craftType = null)
    {
        if (craftStatus == null)
        {
            return string.Empty;
        }

        craftStatus.EnsureInitialized();
        string type = string.IsNullOrWhiteSpace(craftType)
            ? CraftingStatusManager.CraftTypeAlch
            : craftType;

        StringBuilder sb = new StringBuilder(256);
        sb.AppendLine("<color=#80DEEA><b>【解析鑑定】ブラインド裏パラメータ正確値</b></color>");

        if (string.Equals(type, CraftingStatusManager.CraftTypeAlch, StringComparison.OrdinalIgnoreCase))
        {
            sb.AppendLine(
                $"ExtractionLevel = {craftStatus.GetParam(type, CraftingStatusManager.ParamExtractionLevel):F2}");
            sb.AppendLine(
                $"DissolutionRate = {craftStatus.GetParam(type, CraftingStatusManager.ParamDissolutionRate):F2}");
            sb.AppendLine(
                $"PotTemperature = {craftStatus.GetParam(type, CraftingStatusManager.ParamPotTemperature):F1}℃");
        }
        else
        {
            sb.AppendLine($"Purity = {craftStatus.GetParam(type, CraftingStatusManager.ParamPurity):F2}");
            sb.AppendLine($"Density = {craftStatus.GetParam(type, CraftingStatusManager.ParamDensity):F2}");
            sb.AppendLine(
                $"Temperature = {craftStatus.GetParam(type, CraftingStatusManager.ParamTemperature):F1}℃");
        }

        if (recipeRecordEnabled)
        {
            sb.AppendLine("<color=#FFF59D>RecipeRecord: 最高品質 JSON 変動幅をガイド UI へ記録済み</color>");
        }

        return sb.ToString();
    }

    // -------------------------------------------------------------------------
    // 公開 API — マップ / 安全地帯（TownSafetyZoneGate）
    // -------------------------------------------------------------------------

    /// <summary>マップレーダー UI 用ステータス Rich Text。</summary>
    public string BuildMapRadarStatusRichText()
    {
        if (!mapRadarActive)
        {
            return string.Empty;
        }

        string townLabel = TownSafetyZoneGate.IsInsideTown ? "町内（安全地帯）" : "屋外（被弾リスク）";
        return "<color=#81D4FA><b>【魔力共振レーダー】</b></color> " +
               $"<color=#E1F5FE>安全境界: {townLabel}</color> | " +
               "<color=#FFAB40>隠密魔力鉱脈: 可視化中</color>";
    }

    /// <summary>Utility_MapRadar アクティブ権能を発動します。</summary>
    public void ActivateMapRadar(float durationSeconds)
    {
        UniqueAttributeData attr = activeSkill?.FindAttributeByEffectType(UniqueSkillEffectTypes.UtilityMapRadar);
        float duration = durationSeconds > 0f
            ? durationSeconds
            : attr != null && attr.duration > 0f ? attr.duration : 5f;
        SetMapRadarActive(true, duration);
    }

    // -------------------------------------------------------------------------
    // 内部 — 権能評価
    // -------------------------------------------------------------------------

    private void RebuildAttributeCache(UniqueSkillMaster skill)
    {
        mindAccelerateRate = 0f;
        manaControlRate = 0f;
        psychicManaRegenAmount = 0f;
        craftPurityFlatBonus = 0f;
        craftParamVisionActive = false;
        recipeRecordEnabled = false;

        if (skill?.attributes == null)
        {
            return;
        }

        for (int i = 0; i < skill.attributes.Count; i++)
        {
            UniqueAttributeData attr = skill.attributes[i];
            if (attr == null || string.IsNullOrWhiteSpace(attr.effectType))
            {
                continue;
            }

            AccumulateAttribute(attr);
        }
    }

    private void AccumulateAttribute(UniqueAttributeData attr)
    {
        switch (attr.effectType)
        {
            case UniqueSkillEffectTypes.UtilityMindAccelerate:
                mindAccelerateRate = Mathf.Max(mindAccelerateRate, attr.value);
                break;
            case UniqueSkillEffectTypes.PsychicManaControl:
                manaControlRate = Mathf.Max(manaControlRate, attr.value);
                break;
            case UniqueSkillEffectTypes.PsychicRegenMana:
                psychicManaRegenAmount = Mathf.Max(psychicManaRegenAmount, attr.value);
                break;
            case UniqueSkillEffectTypes.CraftPurityFlatBonus:
                craftPurityFlatBonus += attr.value;
                break;
            case UniqueSkillEffectTypes.CraftParamVision:
                if (attr.value > 0f)
                {
                    craftParamVisionActive = true;
                }
                break;
            case UniqueSkillEffectTypes.CraftRecipeRecord:
                if (attr.value > 0f)
                {
                    recipeRecordEnabled = true;
                    craftParamVisionActive = true;
                }
                break;
        }
    }

    private void ApplyPassiveAlwaysOnFlags()
    {
        if (activeSkill?.attributes == null)
        {
            return;
        }

        for (int i = 0; i < activeSkill.attributes.Count; i++)
        {
            UniqueAttributeData attr = activeSkill.attributes[i];
            if (attr == null || !attr.IsPassiveAlwaysOn)
            {
                continue;
            }

            if (attr.effectType == UniqueSkillEffectTypes.CraftParamVision && attr.value > 0f)
            {
                craftParamVisionActive = true;
            }
            else if (attr.effectType == UniqueSkillEffectTypes.CraftRecipeRecord && attr.value > 0f)
            {
                recipeRecordEnabled = true;
                craftParamVisionActive = true;
            }
        }
    }

    private void ApplyCraftingPhaseFlags()
    {
        if (craftParamVisionActive)
        {
            CraftParamVisionActiveChanged?.Invoke(true);
            PublishCraftParamVisionLog(CraftingStatusManager.Instance);
        }
    }

    private void PublishCraftParamVisionLog(CraftingStatusManager craftStatus)
    {
        string report = BuildCraftParamVisionReport(craftStatus);
        if (string.IsNullOrWhiteSpace(report))
        {
            return;
        }

        Debug.Log(report);
        InGameVisualUIManager ui = InGameVisualUIManager.Instance;
        if (ui != null && recipeRecordEnabled)
        {
            ui.SetCraftingGuideVisible(true);
        }
    }

    private void TickBattlePhase(float deltaTime)
    {
        RefreshTrackedEnemies();
        TickPsychicManaRegen(deltaTime);

        if (enemyDetectActive)
        {
            ScanEnemiesForDetectAlerts();
        }
    }

    private void TickPsychicManaRegen(float deltaTime)
    {
        if (psychicManaRegenAmount <= 0f)
        {
            return;
        }

        PlayerStatusManager status = ResolvePlayerStatusManager();
        if (status == null)
        {
            return;
        }

        manaRegenTickAccumulator += deltaTime;
        float interval = Mathf.Max(0.5f, psychicManaRegenIntervalSeconds);
        while (manaRegenTickAccumulator >= interval)
        {
            manaRegenTickAccumulator -= interval;
            status.RestoreMP(psychicManaRegenAmount);
        }
    }

    private void TickActiveTimers(float deltaTime)
    {
        if (enemyDetectActive && enemyDetectTimer > 0f)
        {
            enemyDetectTimer -= deltaTime;
            if (enemyDetectTimer <= 0f)
            {
                SetEnemyDetectActive(false, 0f);
            }
        }

        if (mapRadarActive && mapRadarTimer > 0f)
        {
            mapRadarTimer -= deltaTime;
            if (mapRadarTimer <= 0f)
            {
                SetMapRadarActive(false, 0f);
            }
        }
    }

    private void ScanEnemiesForDetectAlerts()
    {
        for (int i = trackedEnemies.Count - 1; i >= 0; i--)
        {
            VisibleEnemyAI enemy = trackedEnemies[i];
            if (enemy == null)
            {
                trackedEnemies.RemoveAt(i);
                continue;
            }

            UpdateEnemyTimeScale(enemy);
            string stateLabel = enemy.CurrentState.ToString();
            bool isTelegraph = enemy.CurrentState == VisibleEnemyState.Anticipation ||
                               enemy.IsAttackSequenceRunning;
            if (isTelegraph)
            {
                Debug.Log(
                    $"<color=#FF8A80><b>【敵感知】</b></color> {enemy.name} | 状態={stateLabel} | 次行動=攻撃予兆");
            }
        }
    }

    private void RegisterTrackedEnemy(VisibleEnemyAI enemyAI)
    {
        if (!trackedEnemies.Contains(enemyAI))
        {
            trackedEnemies.Add(enemyAI);
        }
    }

    private void RefreshTrackedEnemies()
    {
        trackedEnemies.RemoveAll(e => e == null);
#if UNITY_2023_1_OR_NEWER
        VisibleEnemyAI[] found = FindObjectsByType<VisibleEnemyAI>();
#else
        VisibleEnemyAI[] found = FindObjectsOfType<VisibleEnemyAI>();
#endif
        for (int i = 0; i < found.Length; i++)
        {
            RegisterTrackedEnemy(found[i]);
        }
    }

    private void SetEnemyDetectActive(bool active, float duration)
    {
        if (enemyDetectActive == active && (!active || Mathf.Approximately(enemyDetectTimer, duration)))
        {
            return;
        }

        enemyDetectActive = active;
        enemyDetectTimer = active ? duration : 0f;
        EnemyDetectActiveChanged?.Invoke(active);
    }

    private void SetMapRadarActive(bool active, float duration)
    {
        if (mapRadarActive == active && (!active || Mathf.Approximately(mapRadarTimer, duration)))
        {
            return;
        }

        mapRadarActive = active;
        mapRadarTimer = active ? duration : 0f;
        MapRadarActiveChanged?.Invoke(active);

        if (active)
        {
            Debug.Log(BuildMapRadarStatusRichText());
        }
    }

    private DemoState ResolveDemoState()
    {
        DemoTimeLineManager timeline = DemoTimeLineManager.Instance;
        return timeline != null ? timeline.CurrentState : DemoState.BattlePhase;
    }

    private PlayerStatusManager ResolvePlayerStatusManager()
    {
        if (playerStatusManager != null)
        {
            return playerStatusManager;
        }

        playerStatusManager = PlayerStatusManager.Instance;
        if (playerStatusManager != null)
        {
            return playerStatusManager;
        }

#if UNITY_2023_1_OR_NEWER
        playerStatusManager = FindAnyObjectByType<PlayerStatusManager>();
#else
        playerStatusManager = FindObjectOfType<PlayerStatusManager>();
#endif
        return playerStatusManager;
    }

    private void ResolveReferences()
    {
        ResolvePlayerStatusManager();
    }
}
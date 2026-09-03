using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// プレイヤーが収録（習得）した魔法技術ライブラリを管理します。
/// 閃き（InspirationManager）とは独立した、論理的魔導システムの基盤です。
/// PlayerRobot にアタッチして使用してください。
/// </summary>
public class PlayerMagicLibrary : MonoBehaviour
{
    public static PlayerMagicLibrary Instance { get; private set; }

    [Header("参照（未設定ならシーンから自動検索）")]
    [SerializeField] private PlayerStatusManager statusManager;

    [Header("収録済み魔法技術")]
    [SerializeField] private List<MagicTechData> learnedMagics = new List<MagicTechData>();

    [Header("MP コスト補正")]
    [Tooltip("magicBonus 1 点あたりの MP コスト軽減量")]
    [SerializeField] private float mpCostReductionPerMagicBonus = 0.5f;

    [Header("ランタイム状態（読み取り専用）")]
    [SerializeField] private MagicTechData activeEnchantMagic;
    [SerializeField] private float activeEnchantTimer;

    /// <summary>収録済み魔法の読み取り専用リスト。</summary>
    public IReadOnlyList<MagicTechData> LearnedMagics => learnedMagics;

    /// <summary>武器エンチャント等の効果が残っているか。</summary>
    public bool IsWeaponEnchanted => activeEnchantMagic != null && activeEnchantTimer > 0f;

    /// <summary>現在有効なエンチャント魔法（無ければ null）。</summary>
    public MagicTechData ActiveEnchantMagic => IsWeaponEnchanted ? activeEnchantMagic : null;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[PlayerMagicLibrary] 重複インスタンスを検出しました。");
        }
        else
        {
            Instance = this;
        }

        CacheReferences();
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
        TickEnchantTimer();
    }

    /// <summary>
    /// 魔導書の解読や NPC 伝授により魔法技術をライブラリへ収録します。
    /// </summary>
    public bool RecordMagicViaBookOrMaster(MagicTechData newMagic, string sourceLabel = "魔導書解読")
    {
        if (newMagic == null || !newMagic.IsValid())
        {
            Debug.LogWarning("[PlayerMagicLibrary] 収録対象の魔法データが無効です。");
            return false;
        }

        if (TryFindMagic(newMagic.magicID, out _))
        {
            MagicTechLog.LogMagicAlreadyRecorded(newMagic);
            return false;
        }

        learnedMagics.Add(newMagic.Clone());
        MagicTechLog.LogMagicRecorded(newMagic, sourceLabel);
        return true;
    }

    /// <summary>
    /// 指定 ID の魔法技術を発動します。MP を消費し、効果を適用します。
    /// </summary>
    public bool TryCastMagic(string magicId, PlayerStatusManager playerStatus = null)
    {
        PlayerStatusManager status = playerStatus != null ? playerStatus : ResolveStatusManager();
        if (status == null)
        {
            Debug.LogWarning("[PlayerMagicLibrary] PlayerStatusManager が見つかりません。");
            return false;
        }

        if (!TryFindMagic(magicId, out MagicTechData magic))
        {
            Debug.LogWarning($"[PlayerMagicLibrary] 未収録の魔法 ID: {magicId}");
            return false;
        }

        float effectiveMpCost = CalculateEffectiveMpCost(magic, status);
        if (!status.TryUseMP(effectiveMpCost))
        {
            MagicTechLog.LogMagicCastFailed(magic, effectiveMpCost, status.CurrentMP);
            return false;
        }

        ApplyMagicEffect(magic);
        MagicTechLog.LogMagicCast(magic, effectiveMpCost, status.CurrentMP);
        return true;
    }

    /// <summary>magicBonus を反映した実効 MP コストを返します。</summary>
    public float CalculateEffectiveMpCost(MagicTechData magic, PlayerStatusManager playerStatus)
    {
        if (magic == null)
        {
            return 0f;
        }

        if (playerStatus == null)
        {
            return magic.mpCost;
        }

        float reduction = playerStatus.MagicBonus * mpCostReductionPerMagicBonus;
        return Mathf.Max(1f, magic.mpCost - reduction);
    }

    /// <summary>テスト用：ライブラリを空にします。</summary>
    public void ClearLibraryForTesting()
    {
        learnedMagics.Clear();
        activeEnchantMagic = null;
        activeEnchantTimer = 0f;
    }

    /// <summary>テスト用：収録済み件数を返します。</summary>
    public int GetLearnedCountForTesting()
    {
        return learnedMagics.Count;
    }

    private void ApplyMagicEffect(MagicTechData magic)
    {
        if (magic.duration > 0f)
        {
            activeEnchantMagic = magic.Clone();
            activeEnchantTimer = magic.duration;
        }
        else
        {
            activeEnchantMagic = null;
            activeEnchantTimer = 0f;
        }
    }

    private void TickEnchantTimer()
    {
        if (activeEnchantTimer <= 0f)
        {
            return;
        }

        activeEnchantTimer -= Time.deltaTime;
        if (activeEnchantTimer <= 0f)
        {
            activeEnchantMagic = null;
            activeEnchantTimer = 0f;
        }
    }

    private bool TryFindMagic(string magicId, out MagicTechData magic)
    {
        magic = null;
        if (string.IsNullOrWhiteSpace(magicId))
        {
            return false;
        }

        foreach (MagicTechData entry in learnedMagics)
        {
            if (entry != null && entry.magicID == magicId)
            {
                magic = entry;
                return true;
            }
        }

        return false;
    }

    private PlayerStatusManager ResolveStatusManager()
    {
        if (statusManager == null)
        {
            CacheReferences();
        }

        return statusManager;
    }

    private void CacheReferences()
    {
        if (statusManager == null)
        {
            statusManager = GetComponent<PlayerStatusManager>();
        }

        if (statusManager == null)
        {
            statusManager = PlayerStatusManager.Instance;
        }

        if (statusManager == null)
        {
            statusManager = FindAnyObjectByType<PlayerStatusManager>();
        }
    }
}

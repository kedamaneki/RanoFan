using System;
using System.Collections.Generic;
using UnityEngine;

// =============================================================================
// UniqueSkillMaster の JSON ロード・キャッシュ基盤
// SkillMasterRepository と同一設計規約
// =============================================================================

/// <summary>
/// AI 生成 UniqueSkill JSON をいつでもロード・参照できるランタイムレジストリ。
/// </summary>
public class UniqueSkillRepository : MonoBehaviour
{
    public static UniqueSkillRepository Instance { get; private set; }

    [Header("初期ロード（任意）")]
    [Tooltip("起動時に Resources から読み込む JSON（UniqueSkills/ 配下）")]
    [SerializeField] private List<TextAsset> bootstrapJsonAssets = new List<TextAsset>();

    private readonly Dictionary<string, UniqueSkillMaster> registry =
        new Dictionary<string, UniqueSkillMaster>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, UniqueSkillMaster> Registry => registry;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        LoadBootstrapAssets();
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    /// <summary>起動時バインドされた TextAsset を一括登録します。</summary>
    public void LoadBootstrapAssets()
    {
        if (bootstrapJsonAssets == null)
        {
            return;
        }

        for (int i = 0; i < bootstrapJsonAssets.Count; i++)
        {
            TextAsset asset = bootstrapJsonAssets[i];
            if (asset != null)
            {
                RegisterFromJson(asset.text, asset.name);
            }
        }
    }

    /// <summary>JSON 文字列から UniqueSkillMaster をパースして登録します。</summary>
    public bool RegisterFromJson(string json, string sourceLabel = "inline")
    {
        UniqueSkillMaster master = UniqueSkillMaster.FromJson(json);
        if (master == null || !master.IsValid())
        {
            Debug.LogWarning($"[UniqueSkillRepository] 登録失敗: {sourceLabel}");
            return false;
        }

        registry[master.uniqueSkillId] = master;
        Debug.Log($"[UniqueSkillRepository] 登録: {master} ({sourceLabel})");
        return true;
    }

    /// <summary>複数ユニークスキル JSON（uniqueSkills 配列ラップ）を一括登録します。</summary>
    public int RegisterListFromJson(string json, string sourceLabel = "inline")
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return 0;
        }

        try
        {
            UniqueSkillListDto listDto = JsonUtility.FromJson<UniqueSkillListDto>(json);
            if (listDto?.uniqueSkills == null || listDto.uniqueSkills.Count == 0)
            {
                return RegisterFromJson(json, sourceLabel) ? 1 : 0;
            }

            int count = 0;
            for (int i = 0; i < listDto.uniqueSkills.Count; i++)
            {
                UniqueSkillMaster master = listDto.uniqueSkills[i];
                if (master != null && master.IsValid())
                {
                    registry[master.uniqueSkillId] = master;
                    count++;
                }
            }

            Debug.Log($"[UniqueSkillRepository] 一括登録: {count} 件 ({sourceLabel})");
            return count;
        }
        catch (Exception exception)
        {
            Debug.LogError($"[UniqueSkillRepository] 一括登録失敗: {exception.Message}");
            return 0;
        }
    }

    /// <summary>Resources フォルダから JSON を読み込み登録します。</summary>
    public int LoadFromResources(string resourcePath)
    {
        TextAsset asset = Resources.Load<TextAsset>(resourcePath);
        if (asset == null)
        {
            Debug.LogWarning($"[UniqueSkillRepository] Resources 未検出: {resourcePath}");
            return 0;
        }

        return RegisterListFromJson(asset.text, resourcePath);
    }

    /// <summary>uniqueSkillId で UniqueSkillMaster を取得します。</summary>
    public bool TryGet(string uniqueSkillId, out UniqueSkillMaster master)
    {
        if (string.IsNullOrWhiteSpace(uniqueSkillId))
        {
            master = null;
            return false;
        }

        return registry.TryGetValue(uniqueSkillId, out master);
    }

    /// <summary>登録済みユニークスキルをすべて返します。</summary>
    public IReadOnlyList<UniqueSkillMaster> GetAll()
    {
        return new List<UniqueSkillMaster>(registry.Values);
    }

    /// <summary>レジストリを空にします。</summary>
    public void Clear()
    {
        registry.Clear();
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;

// =============================================================================
// SkillMaster の JSON ロード・キャッシュ基盤
// =============================================================================

/// <summary>
/// AI 生成 SkillMaster JSON をいつでもロード・参照できるランタイムレジストリ。
/// </summary>
public class SkillMasterRepository : MonoBehaviour
{
    /// <summary>Resources ロード時の既定パス（SkillMasters/SkillMasters）。</summary>
    public const string DefaultResourcesPath = "SkillMasters/SkillMasters";

    public static SkillMasterRepository Instance { get; private set; }

    [Header("初期ロード（任意）")]
    [Tooltip("起動時に Resources から読み込む JSON（SkillMasters/ 配下）")]
    [SerializeField] private List<TextAsset> bootstrapJsonAssets = new List<TextAsset>();

    [Header("起動時自動ロード")]
    [Tooltip("true のとき Awake で DefaultResourcesPath を追加ロードします")]
    [SerializeField] private bool loadDefaultResourcesOnAwake = true;

    private readonly Dictionary<string, SkillMaster> registry =
        new Dictionary<string, SkillMaster>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, SkillMaster> Registry => registry;

    public int RegisteredCount => registry.Count;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        ClearAllCache();

        if (loadDefaultResourcesOnAwake)
        {
            LoadFromResources(DefaultResourcesPath);
            LoadEvolutionMastersFromResources();
        }

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
                RegisterListFromJson(asset.text, asset.name);
            }
        }
    }

    /// <summary>
    /// 保持しているすべてのスキルマスターキャッシュを完全に空にします。
    /// 新規インポート前に必ず呼び出してください。
    /// </summary>
    public void ClearAllCache()
    {
        registry.Clear();
        Debug.Log("[SkillMasterRepository] 全スキルマスターキャッシュをクリアしました。");
    }

    /// <summary>ClearAllCache のエイリアス（後方互換）。</summary>
    public void Clear()
    {
        ClearAllCache();
    }

    /// <summary>インスタンスが存在すればキャッシュをクリアします（エディタ／テスト用）。</summary>
    public static void ClearAllCachesIfPresent()
    {
        if (Instance != null)
        {
            Instance.ClearAllCache();
        }
    }

    /// <summary>キャッシュを空にしてから JSON を一括登録します。</summary>
    public int ReplaceAllFromJson(string json, string sourceLabel = "inline")
    {
        ClearAllCache();
        return RegisterListFromJson(json, sourceLabel);
    }

    /// <summary>JSON 文字列から SkillMaster をパースして登録します。</summary>
    public bool RegisterFromJson(string json, string sourceLabel = "inline")
    {
        SkillMaster master = SkillMaster.FromJson(json);
        if (master == null)
        {
            Debug.LogWarning($"[SkillMasterRepository] 登録失敗: {sourceLabel}");
            return false;
        }

        registry[master.skillId] = master;
        Debug.Log($"[SkillMasterRepository] 登録: {master} ({sourceLabel})");
        return true;
    }

    /// <summary>複数スキル JSON（skills 配列ラップ）を一括登録します。</summary>
    public int RegisterListFromJson(string json, string sourceLabel = "inline")
    {
        if (IsEmptySkillListJson(json))
        {
            Debug.Log($"[SkillMasterRepository] 空のスキルリストを検出（登録0件）: {sourceLabel}");
            return 0;
        }

        try
        {
            // 深度制限回避: skills 配列を 1 件ずつ SkillMasterDto へパース
            List<SkillMaster> parsedSkills = SkillMasterJsonLoader.ParseSkillsFromWrappedListJson(json);
            if (parsedSkills.Count > 0)
            {
                int count = 0;
                for (int i = 0; i < parsedSkills.Count; i++)
                {
                    SkillMaster master = parsedSkills[i];
                    if (master == null || !master.IsValid())
                    {
                        continue;
                    }

                    if (master.skillId.StartsWith("SKL_", StringComparison.OrdinalIgnoreCase))
                    {
                        GeneratedDataValidator.ValidateAndLog(master);
                    }

                    registry[master.skillId] = master;
                    count++;
                }

                Debug.Log($"[SkillMasterRepository] 一括登録（分割パース）: {count} 件 ({sourceLabel})");
                return count;
            }

            return RegisterFromJson(json, sourceLabel) ? 1 : 0;
        }
        catch (Exception exception)
        {
            Debug.LogError($"[SkillMasterRepository] 一括登録失敗: {exception.Message}");
            return 0;
        }
    }

    /// <summary>Resources フォルダから JSON を読み込み登録します。</summary>
    public int LoadFromResources(string resourcePath)
    {
        TextAsset asset = Resources.Load<TextAsset>(resourcePath);
        if (asset == null)
        {
            Debug.LogWarning($"[SkillMasterRepository] Resources 未検出: {resourcePath}");
            return 0;
        }

        return RegisterListFromJson(asset.text, resourcePath);
    }

    /// <summary>Resources/SkillMasters/Evolution 配下の発展スキル JSON を一括登録します。</summary>
    public int LoadEvolutionMastersFromResources(string resourceFolder = "SkillMasters/Evolution")
    {
        TextAsset[] assets = Resources.LoadAll<TextAsset>(resourceFolder);
        if (assets == null || assets.Length == 0)
        {
            Debug.LogWarning($"[SkillMasterRepository] 発展スキル未検出: {resourceFolder}");
            return 0;
        }

        int total = 0;
        for (int i = 0; i < assets.Length; i++)
        {
            TextAsset asset = assets[i];
            if (asset == null)
            {
                continue;
            }

            if (asset.name is "CountryTechIndex" or "Evolution_Manifest")
            {
                continue;
            }

            total += RegisterListFromJson(asset.text, $"{resourceFolder}/{asset.name}");
        }

        Debug.Log($"[SkillMasterRepository] 発展スキル一括登録: {total} 件 ({resourceFolder})");
        return total;
    }

    /// <summary>skillId で SkillMaster を取得します。</summary>
    public bool TryGet(string skillId, out SkillMaster master)
    {
        if (string.IsNullOrWhiteSpace(skillId))
        {
            master = null;
            return false;
        }

        return registry.TryGetValue(skillId, out master);
    }

    /// <summary>ランタイム SkillMaster を直接登録します（JSON 再パースを避ける）。</summary>
    public void RegisterSkillMaster(SkillMaster master, string sourceLabel = "runtime")
    {
        if (master == null || !master.IsValid())
        {
            Debug.LogWarning($"[SkillMasterRepository] 直接登録失敗: {sourceLabel}");
            return;
        }

        registry[master.skillId] = master;
        Debug.Log($"[SkillMasterRepository] 直接登録: {master} ({sourceLabel})");
    }

    /// <summary>シーンに無い場合は生成して返します。</summary>
    public static SkillMasterRepository EnsureInstance()
    {
        if (Instance != null)
        {
            return Instance;
        }

        GameObject hub = GameObject.Find("DebugSystemsHub");
        if (hub != null)
        {
            SkillMasterRepository onHub = hub.GetComponent<SkillMasterRepository>();
            if (onHub != null)
            {
                return onHub;
            }

            return hub.AddComponent<SkillMasterRepository>();
        }

        return new GameObject(nameof(SkillMasterRepository)).AddComponent<SkillMasterRepository>();
    }

    /// <summary>登録済みスキルをすべて返します。</summary>
    public IReadOnlyList<SkillMaster> GetAll()
    {
        List<SkillMaster> list = new List<SkillMaster>(registry.Values);
        return list;
    }

    private static bool IsEmptySkillListJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return true;
        }

        string trimmed = json.Trim();
        return trimmed == "[]" ||
               trimmed == "{\"skills\":[]}" ||
               trimmed == "{ \"skills\": [] }";
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;

// =============================================================================
// 統合マスターデータ JSON ロード・キャッシュ
// SkillMasterRepository / UniqueSkillRepository と併用（スキルは両方へ同期可能）
// =============================================================================

/// <summary>LoadMasterData の結果サマリー。</summary>
public sealed class MasterDataLoadResult
{
    public bool Success;
    public int SkillsLoaded;
    public int MagicsLoaded;
    public int JobsLoaded;
    public int EnemiesLoaded;
    public int ItemsLoaded;
    public string ErrorMessage;

    public int TotalLoaded =>
        SkillsLoaded + MagicsLoaded + JobsLoaded + EnemiesLoaded + ItemsLoaded;
}

/// <summary>
/// AI 生成 JSON を一括パースし、ID ベースで各マスターを取得するシングルトン。
/// パース失敗時はクラッシュせずログ出力し、読み込めた分のみキャッシュします。
/// </summary>
public class MasterDataManager : MonoBehaviour
{
    public static MasterDataManager Instance { get; private set; }

    [Header("起動時ロード（任意）")]
    [Tooltip("true のとき Awake で既定 JSON を読み込みます")]
    [SerializeField] private bool loadDefaultResourcesOnAwake = true;

    [Tooltip("既定 Resources パス（拡張子なし）")]
    [SerializeField] private string defaultResourcesPath = "GameMasters/GameMasters_Sample";

    [Header("スキル同期")]
    [Tooltip("true のとき skills 読み込み後に SkillMasterRepository へも登録します")]
    [SerializeField] private bool syncSkillsToSkillMasterRepository = true;

    private bool defaultDataLoaded;

    private readonly Dictionary<string, SkillMaster> skillRegistry =
        new Dictionary<string, SkillMaster>(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, MagicMasterData> magicRegistry =
        new Dictionary<string, MagicMasterData>(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, JobMasterData> jobRegistry =
        new Dictionary<string, JobMasterData>(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, EnemyMasterData> enemyRegistry =
        new Dictionary<string, EnemyMasterData>(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, ItemMasterData> itemRegistry =
        new Dictionary<string, ItemMasterData>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, SkillMaster> SkillRegistry => skillRegistry;
    public IReadOnlyDictionary<string, MagicMasterData> MagicRegistry => magicRegistry;
    public IReadOnlyDictionary<string, JobMasterData> JobRegistry => jobRegistry;
    public IReadOnlyDictionary<string, EnemyMasterData> EnemyRegistry => enemyRegistry;
    public IReadOnlyDictionary<string, ItemMasterData> ItemRegistry => itemRegistry;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        if (loadDefaultResourcesOnAwake)
        {
            EnsureDefaultDataLoaded();
        }
    }

    /// <summary>シーンに無い場合は生成し、未ロードなら既定 JSON を読み込みます。</summary>
    public static MasterDataManager EnsureInstance()
    {
        if (Instance != null)
        {
            Instance.EnsureDefaultDataLoaded();
            return Instance;
        }

        GameObject hub = GameObject.Find("DebugSystemsHub");
        if (hub != null)
        {
            MasterDataManager onHub = hub.GetComponent<MasterDataManager>();
            if (onHub != null)
            {
                onHub.EnsureDefaultDataLoaded();
                return onHub;
            }

            MasterDataManager added = hub.AddComponent<MasterDataManager>();
            added.EnsureDefaultDataLoaded();
            return added;
        }

        GameObject player = GameObject.Find("PlayerRobot");
        if (player != null)
        {
            MasterDataManager onPlayer = player.GetComponent<MasterDataManager>();
            if (onPlayer != null)
            {
                onPlayer.EnsureDefaultDataLoaded();
                return onPlayer;
            }
        }

        MasterDataManager created = new GameObject(nameof(MasterDataManager))
            .AddComponent<MasterDataManager>();
        created.EnsureDefaultDataLoaded();
        return created;
    }

    /// <summary>キャッシュが空なら defaultResourcesPath から JSON を読み込みます。</summary>
    public void EnsureDefaultDataLoaded()
    {
        if (defaultDataLoaded && HasAnyRegistryData())
        {
            return;
        }

        if (HasAnyRegistryData())
        {
            defaultDataLoaded = true;
            return;
        }

        MasterDataLoadResult result = LoadFromResources(defaultResourcesPath);
        defaultDataLoaded = result.Success || HasAnyRegistryData();

        if (!defaultDataLoaded)
        {
            Debug.LogWarning(
                $"[MasterDataManager] 既定マスターのロードに失敗しました: {defaultResourcesPath}");
        }
    }

    private bool HasAnyRegistryData()
    {
        return skillRegistry.Count > 0 ||
               magicRegistry.Count > 0 ||
               jobRegistry.Count > 0 ||
               enemyRegistry.Count > 0 ||
               itemRegistry.Count > 0;
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    /// <summary>全キャッシュを空にします。</summary>
    public void ClearAll()
    {
        skillRegistry.Clear();
        magicRegistry.Clear();
        jobRegistry.Clear();
        enemyRegistry.Clear();
        itemRegistry.Clear();
        Debug.Log("[MasterDataManager] 全マスターキャッシュをクリアしました。");
    }

    /// <summary>AI が生成した JSON 文字列を一括パースして各辞書に登録します。</summary>
    public MasterDataLoadResult LoadMasterData(string jsonText, string sourceLabel = "inline")
    {
        MasterDataLoadResult result = new MasterDataLoadResult();

        if (string.IsNullOrWhiteSpace(jsonText))
        {
            result.ErrorMessage = "JSON が空です。";
            Debug.LogWarning($"[MasterDataManager] {result.ErrorMessage} ({sourceLabel})");
            return result;
        }

        try
        {
            // skills は深い derivatives を含むため GameMasterCoreLoadDto で一括パースしない（深度制限回避）
            GameMasterCoreLoadDto data = JsonUtility.FromJson<GameMasterCoreLoadDto>(jsonText);
            if (data == null)
            {
                result.ErrorMessage = "GameMasterCoreLoadDto のパース結果が null です。";
                Debug.LogError($"[MasterDataManager] {result.ErrorMessage} ({sourceLabel})");
                return result;
            }

            List<SkillMaster> runtimeSkills = SkillMasterJsonLoader.ParseSkillsFromWrappedListJson(jsonText);
            result.SkillsLoaded = RegisterSkills(runtimeSkills, sourceLabel);
            result.MagicsLoaded = RegisterEntries(data.magics, magicRegistry, m => m.id, m => m.IsValid(), sourceLabel, "Magic");
            result.JobsLoaded = RegisterEntries(data.jobs, jobRegistry, j => j.id, j => j.IsValid(), sourceLabel, "Job");
            result.EnemiesLoaded = RegisterEntries(data.enemies, enemyRegistry, e => e.id, e => e.IsValid(), sourceLabel, "Enemy");
            result.ItemsLoaded = RegisterEntries(data.items, itemRegistry, i => i.id, i => i.IsValid(), sourceLabel, "Item");

            SanitizeRegisteredMagicsJobsEnemiesItems();

            result.Success = result.TotalLoaded > 0;
            Debug.Log(
                $"[MasterDataManager] ロード完了 ({sourceLabel}): " +
                $"skills={result.SkillsLoaded}, magics={result.MagicsLoaded}, " +
                $"jobs={result.JobsLoaded}, enemies={result.EnemiesLoaded}, items={result.ItemsLoaded}");
            return result;
        }
        catch (Exception exception)
        {
            result.ErrorMessage = exception.Message;
            Debug.LogError($"[MasterDataManager] パース例外 ({sourceLabel}): {exception}");
            return result;
        }
    }

    /// <summary>キャッシュをクリアしてから JSON を読み込みます。</summary>
    public MasterDataLoadResult ReplaceAllFromJson(string jsonText, string sourceLabel = "inline")
    {
        ClearAll();
        return LoadMasterData(jsonText, sourceLabel);
    }

    /// <summary>Resources フォルダから JSON を読み込みます。</summary>
    public MasterDataLoadResult LoadFromResources(string resourcePath)
    {
        TextAsset asset = Resources.Load<TextAsset>(resourcePath);
        if (asset == null)
        {
            MasterDataLoadResult fail = new MasterDataLoadResult
            {
                ErrorMessage = $"Resources 未検出: {resourcePath}"
            };
            Debug.LogWarning($"[MasterDataManager] {fail.ErrorMessage}");
            return fail;
        }

        return LoadMasterData(asset.text, resourcePath);
    }

    public bool TryGetSkill(string id, out SkillMaster skill) =>
        TryGet(skillRegistry, id, out skill);

    public bool TryGetMagic(string id, out MagicMasterData magic) =>
        TryGet(magicRegistry, id, out magic);

    public bool TryGetJob(string id, out JobMasterData job) =>
        TryGet(jobRegistry, id, out job);

    public bool TryGetEnemy(string id, out EnemyMasterData enemy) =>
        TryGet(enemyRegistry, id, out enemy);

    public bool TryGetItem(string id, out ItemMasterData item) =>
        TryGet(itemRegistry, id, out item);

    public SkillMaster GetSkill(string id) => GetOrNull(skillRegistry, id);
    public MagicMasterData GetMagic(string id) => GetOrNull(magicRegistry, id);
    public JobMasterData GetJob(string id) => GetOrNull(jobRegistry, id);
    public EnemyMasterData GetEnemy(string id) => GetOrNull(enemyRegistry, id);
    public ItemMasterData GetItem(string id) => GetOrNull(itemRegistry, id);

    /// <summary>
    /// 動的生成社会生活ジョブを1件登録します。無効データは登録せず false（Safe-Fail）。
    /// </summary>
    public bool TryRegisterJob(JobMasterData job, string sourceLabel = "runtime")
    {
        if (job == null)
        {
            Debug.LogWarning($"[MasterDataManager] null Job をスキップ ({sourceLabel})");
            return false;
        }

        try
        {
            job.Sanitize();
            if (!job.IsValid())
            {
                Debug.LogWarning($"[MasterDataManager] 検証失敗 Job ({sourceLabel})");
                return false;
            }

            string entryId = job.id;
            if (jobRegistry.ContainsKey(entryId))
            {
                Debug.LogWarning($"[MasterDataManager] Job ID 重複を上書き: {entryId} ({sourceLabel})");
            }

            jobRegistry[entryId] = job;
            return true;
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[MasterDataManager] Job 登録 Safe-Fail ({sourceLabel}): {exception.Message}");
            return false;
        }
    }

    /// <summary>
    /// 動的生成魔法を1件登録します。無効データは登録せず false（Safe-Fail）。
    /// </summary>
    public bool TryRegisterMagic(MagicMasterData magic, string sourceLabel = "runtime")
    {
        if (magic == null)
        {
            Debug.LogWarning($"[MasterDataManager] null Magic をスキップ ({sourceLabel})");
            return false;
        }

        try
        {
            magic.Sanitize();
            if (!magic.IsValid())
            {
                Debug.LogWarning($"[MasterDataManager] 検証失敗 Magic ({sourceLabel})");
                return false;
            }

            string entryId = magic.id;
            if (magicRegistry.ContainsKey(entryId))
            {
                Debug.LogWarning($"[MasterDataManager] Magic ID 重複を上書き: {entryId} ({sourceLabel})");
            }

            magicRegistry[entryId] = magic;
            return true;
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[MasterDataManager] Magic 登録 Safe-Fail ({sourceLabel}): {exception.Message}");
            return false;
        }
    }

    private int RegisterSkills(List<SkillMaster> skills, string sourceLabel)
    {
        if (skills == null)
        {
            return 0;
        }

        SkillMasterRepository skillRepo = syncSkillsToSkillMasterRepository
            ? SkillMasterRepository.EnsureInstance()
            : null;

        int count = 0;
        for (int i = 0; i < skills.Count; i++)
        {
            SkillMaster skill = skills[i];
            if (skill == null || !skill.IsValid())
            {
                Debug.LogWarning($"[MasterDataManager] 無効な SkillMaster をスキップ: index={i} ({sourceLabel})");
                continue;
            }

            if (skill.evolutionData != null)
            {
                skill.evolutionData.Sanitize(skill.skillId ?? $"skill[{i}]");
            }

            skillRegistry[skill.skillId] = skill;
            count++;

            if (skillRepo != null)
            {
                skillRepo.RegisterSkillMaster(skill, $"{sourceLabel}:{skill.skillId}");
            }
        }

        return count;
    }

    private void SanitizeRegisteredMagicsJobsEnemiesItems()
    {
        foreach (MagicMasterData magic in magicRegistry.Values)
        {
            magic?.Sanitize();
        }

        foreach (JobMasterData job in jobRegistry.Values)
        {
            job?.Sanitize();
        }

        foreach (EnemyMasterData enemy in enemyRegistry.Values)
        {
            enemy?.Sanitize();
        }

        foreach (ItemMasterData item in itemRegistry.Values)
        {
            item?.Sanitize();
        }
    }

    private static int RegisterEntries<T>(
        List<T> entries,
        Dictionary<string, T> registry,
        Func<T, string> idSelector,
        Func<T, bool> validator,
        string sourceLabel,
        string typeLabel) where T : class
    {
        if (entries == null)
        {
            return 0;
        }

        int count = 0;
        for (int i = 0; i < entries.Count; i++)
        {
            T entry = entries[i];
            if (entry == null)
            {
                Debug.LogWarning($"[MasterDataManager] null {typeLabel} をスキップ: index={i} ({sourceLabel})");
                continue;
            }

            string entryId = idSelector(entry);
            if (string.IsNullOrWhiteSpace(entryId))
            {
                Debug.LogWarning($"[MasterDataManager] ID 空の {typeLabel} をスキップ: index={i} ({sourceLabel})");
                continue;
            }

            if (!validator(entry))
            {
                Debug.LogWarning($"[MasterDataManager] 検証失敗 {typeLabel}[{entryId}] ({sourceLabel})");
                continue;
            }

            if (registry.ContainsKey(entryId))
            {
                Debug.LogWarning($"[MasterDataManager] {typeLabel} ID 重複を上書き: {entryId} ({sourceLabel})");
            }

            registry[entryId] = entry;
            count++;
        }

        return count;
    }

    private static bool TryGet<T>(Dictionary<string, T> registry, string id, out T value)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            value = default;
            return false;
        }

        return registry.TryGetValue(id, out value);
    }

    private static T GetOrNull<T>(Dictionary<string, T> registry, string id) where T : class
    {
        TryGet(registry, id, out T value);
        return value;
    }
}

/// <summary>Play 開始時に MasterDataManager を他コンポーネントより先に配置・ロードします。</summary>
public static class MasterDataBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AttachMasterDataManager()
    {
        SkillMasterRepository.EnsureInstance();
        MasterDataManager.EnsureInstance();
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;

// =============================================================================
// Resources/HistorySimulation 正史 JSON ローダー
// 連携: HistoryFlagRegistry / HistoryDecodingPresenter
// =============================================================================

/// <summary>
/// spine / nations / sections / greatpowers の HistorySimulation JSON を JsonUtility で読み、
/// 指定ターンの conditionFlag を ConditionFlagResolver へ登録します。
/// </summary>
public static class HistorySimulationLoader
{
    public const string ResourcesRoot = "HistorySimulation";
    public const string SpineFolder = "HistorySimulation/spine";
    public const string NationsFolder = "HistorySimulation/nations";
    public const string SectionsFolder = "HistorySimulation/sections";
    public const string GreatPowersFolder = "HistorySimulation/greatpowers";

    private static readonly Dictionary<string, HistorySimulationFileDto> FileCache =
        new Dictionary<string, HistorySimulationFileDto>(StringComparer.OrdinalIgnoreCase);

    private static TextAsset[] cachedNationAssets;
    private static TextAsset[] cachedSectionAssets;
    private static TextAsset[] cachedGreatPowerAssets;

    /// <summary>spine/SIM_T{turn:03d} をロードします。未存在時は null（Safe-Fail）。</summary>
    public static HistorySimulationFileDto LoadSpineTurn(int turn)
    {
        if (turn < 1)
        {
            Debug.LogWarning($"[HistorySimulationLoader] 無効なターン: {turn}");
            return null;
        }

        string path = $"{SpineFolder}/SIM_T{turn:000}";
        return LoadFile(path, allowMissing: true);
    }

    /// <summary>nations/SIM_NATION_{id:03d} をロードします。未存在時は null（Safe-Fail）。</summary>
    public static HistorySimulationFileDto LoadNation(int nationId)
    {
        if (nationId < 1)
        {
            Debug.LogWarning($"[HistorySimulationLoader] 無効な国家 ID: {nationId}");
            return null;
        }

        string path = $"{NationsFolder}/SIM_NATION_{nationId:000}";
        return LoadFile(path, allowMissing: true);
    }

    /// <summary>
    /// 指定ターンの spine + nations + sections + greatpowers から conditionFlag を抽出し、
    /// HistoryFlagRegistry（ConditionFlagResolver）へ解禁登録します。
    /// ファイルが無いターンは警告のみで空リストを返します。
    /// </summary>
    public static List<string> ImportHistoryFlagsForTurn(int turn)
    {
        HistoryFlagRegistry.EnsureWired();
        List<string> imported = new List<string>();
        HashSet<string> unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        HistorySimulationFileDto spine = LoadSpineTurn(turn);
        if (spine == null)
        {
            Debug.LogWarning(
                $"[HistorySimulationLoader] spine JSON がありません: {SpineFolder}/SIM_T{turn:000}。解読を無視します。");
        }
        else
        {
            CollectFlagsFromFile(spine, turn, unique, imported);
        }

        CollectFlagsFromAssetFolder(EnsureNationAssets(), turn, unique, imported);
        CollectFlagsFromAssetFolder(EnsureSectionAssets(), turn, unique, imported);
        CollectFlagsFromAssetFolder(EnsureGreatPowerAssets(), turn, unique, imported);

        for (int i = 0; i < imported.Count; i++)
        {
            HistoryFlagRegistry.Unlock(imported[i]);
        }

        if (imported.Count > 0)
        {
            HistoryFlagRegistry.Unlock(HistoryFlagRegistry.InitialDecodingCompleteFlag);
        }

        return imported;
    }

    /// <summary>指定ターンに属するイベントを収集します（フラグ解禁はしません）。</summary>
    public static List<HistorySimulationEventDto> CollectEventsForTurn(int turn)
    {
        List<HistorySimulationEventDto> events = new List<HistorySimulationEventDto>();
        HistorySimulationFileDto spine = LoadSpineTurn(turn);
        AppendTurnEvents(spine, turn, events);
        AppendTurnEventsFromAssets(EnsureNationAssets(), turn, events);
        AppendTurnEventsFromAssets(EnsureSectionAssets(), turn, events);
        AppendTurnEventsFromAssets(EnsureGreatPowerAssets(), turn, events);
        return events;
    }

    private static HistorySimulationFileDto LoadFile(string resourcesPath, bool allowMissing)
    {
        if (FileCache.TryGetValue(resourcesPath, out HistorySimulationFileDto cached))
        {
            return cached;
        }

        TextAsset asset = Resources.Load<TextAsset>(resourcesPath);
        if (asset == null)
        {
            if (!allowMissing)
            {
                Debug.LogError($"[HistorySimulationLoader] 必須 JSON がありません: {resourcesPath}");
            }
            else
            {
                Debug.LogWarning($"[HistorySimulationLoader] JSON 未検出（Safe-Fail）: {resourcesPath}");
            }

            return null;
        }

        HistorySimulationFileDto parsed = ParseAsset(asset, resourcesPath);
        if (parsed != null)
        {
            FileCache[resourcesPath] = parsed;
        }

        return parsed;
    }

    private static HistorySimulationFileDto ParseAsset(TextAsset asset, string label)
    {
        if (asset == null || string.IsNullOrWhiteSpace(asset.text))
        {
            Debug.LogWarning($"[HistorySimulationLoader] 空の JSON: {label}");
            return null;
        }

        try
        {
            HistorySimulationFileDto dto = JsonUtility.FromJson<HistorySimulationFileDto>(asset.text);
            if (dto == null)
            {
                Debug.LogWarning($"[HistorySimulationLoader] パース結果が空です: {label}");
                return null;
            }

            dto.simulationEvents ??= Array.Empty<HistorySimulationEventDto>();
            return dto;
        }
        catch (Exception exception)
        {
            Debug.LogError($"[HistorySimulationLoader] パース失敗（Safe-Fail）: {label}\n{exception}");
            return null;
        }
    }

    private static void CollectFlagsFromFile(
        HistorySimulationFileDto file,
        int turn,
        HashSet<string> unique,
        List<string> imported)
    {
        if (file?.simulationEvents == null)
        {
            return;
        }

        for (int i = 0; i < file.simulationEvents.Length; i++)
        {
            HistorySimulationEventDto evt = file.simulationEvents[i];
            if (evt == null || evt.macroTurn != turn)
            {
                continue;
            }

            TryAddFlag(evt.conditionFlag, unique, imported);
            if (evt.historicalMode?.forcedFlagsOnTrigger == null)
            {
                continue;
            }

            for (int f = 0; f < evt.historicalMode.forcedFlagsOnTrigger.Length; f++)
            {
                TryAddFlag(evt.historicalMode.forcedFlagsOnTrigger[f], unique, imported);
            }
        }
    }

    private static void CollectFlagsFromAssetFolder(
        TextAsset[] assets,
        int turn,
        HashSet<string> unique,
        List<string> imported)
    {
        if (assets == null)
        {
            return;
        }

        for (int i = 0; i < assets.Length; i++)
        {
            HistorySimulationFileDto file = ParseOrCacheAsset(assets[i]);
            CollectFlagsFromFile(file, turn, unique, imported);
        }
    }

    private static void AppendTurnEvents(HistorySimulationFileDto file, int turn, List<HistorySimulationEventDto> dest)
    {
        if (file?.simulationEvents == null)
        {
            return;
        }

        for (int i = 0; i < file.simulationEvents.Length; i++)
        {
            HistorySimulationEventDto evt = file.simulationEvents[i];
            if (evt != null && evt.macroTurn == turn)
            {
                dest.Add(evt);
            }
        }
    }

    private static void AppendTurnEventsFromAssets(TextAsset[] assets, int turn, List<HistorySimulationEventDto> dest)
    {
        if (assets == null)
        {
            return;
        }

        for (int i = 0; i < assets.Length; i++)
        {
            AppendTurnEvents(ParseOrCacheAsset(assets[i]), turn, dest);
        }
    }

    private static HistorySimulationFileDto ParseOrCacheAsset(TextAsset asset)
    {
        if (asset == null)
        {
            return null;
        }

        string key = $"asset:{asset.name}";
        if (FileCache.TryGetValue(key, out HistorySimulationFileDto cached))
        {
            return cached;
        }

        HistorySimulationFileDto parsed = ParseAsset(asset, asset.name);
        if (parsed != null)
        {
            FileCache[key] = parsed;
        }

        return parsed;
    }

    private static void TryAddFlag(string flagKey, HashSet<string> unique, List<string> imported)
    {
        if (string.IsNullOrWhiteSpace(flagKey))
        {
            return;
        }

        string trimmed = flagKey.Trim();
        if (unique.Add(trimmed))
        {
            imported.Add(trimmed);
        }
    }

    private static TextAsset[] EnsureNationAssets()
    {
        return cachedNationAssets ??= Resources.LoadAll<TextAsset>(NationsFolder);
    }

    private static TextAsset[] EnsureSectionAssets()
    {
        return cachedSectionAssets ??= Resources.LoadAll<TextAsset>(SectionsFolder);
    }

    private static TextAsset[] EnsureGreatPowerAssets()
    {
        return cachedGreatPowerAssets ??= Resources.LoadAll<TextAsset>(GreatPowersFolder);
    }
}

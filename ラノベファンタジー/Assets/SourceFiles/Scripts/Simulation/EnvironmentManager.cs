using System.Collections.Generic;
using UnityEngine;

// =============================================================================
// 環境パラメータ管理 — T≥1001 環境転換（CursorInstruction.md 準拠）
// 連携: EraContextResolver / EnvironmentBiorhythmEngine / HistoryFlagRegistry
// =============================================================================

/// <summary>管理する環境パラメータの種別。</summary>
public enum EnvironmentParameterType
{
    Temperature,
    Humidity,
    ResourceAvailability_IronOre,
    ResourceAvailability_RareEarth,
    ResourceAvailability_Water,
    GeologicalStability,
    AtmosphericComposition_Oxygen
}

/// <summary>
/// 1001年以降の環境転換パラメータを保持するシングルトン。
/// Safe-Fail: 未知パラメータは 0 を返し警告ログを出力、転換の二重適用を防止します。
/// </summary>
[DefaultExecutionOrder(-85)]
public class EnvironmentManager : MonoBehaviour
{
    public const string LogTag = "【環境パラメータ管理】";
    public const int TransformationYear = EraContextResolver.ChronicleMaxTurn + 1;

    public static EnvironmentManager Instance { get; private set; }

    private readonly Dictionary<EnvironmentParameterType, float> currentParameters =
        new Dictionary<EnvironmentParameterType, float>();

    private bool hasTransformed;

    public bool HasTransformed => hasTransformed;

    public static EnvironmentManager EnsureInstance()
    {
        if (Instance != null)
        {
            return Instance;
        }

        EnvironmentManager existing = UnityEngine.Object.FindAnyObjectByType<EnvironmentManager>();
        if (existing != null)
        {
            Instance = existing;
            return Instance;
        }

        GameObject hub = GameObject.Find("DebugSystemsHub");
        GameObject host = hub != null ? hub : new GameObject(nameof(EnvironmentManager));
        EnvironmentManager mgr = host.GetComponent<EnvironmentManager>();
        return mgr != null ? mgr : host.AddComponent<EnvironmentManager>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        InitializeDefaultParameters();
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void InitializeDefaultParameters()
    {
        currentParameters[EnvironmentParameterType.Temperature] = 20.0f;
        currentParameters[EnvironmentParameterType.Humidity] = 0.6f;
        currentParameters[EnvironmentParameterType.ResourceAvailability_IronOre] = 1.0f;
        currentParameters[EnvironmentParameterType.ResourceAvailability_RareEarth] = 0.2f;
        currentParameters[EnvironmentParameterType.ResourceAvailability_Water] = 1.0f;
        currentParameters[EnvironmentParameterType.GeologicalStability] = 1.0f;
        currentParameters[EnvironmentParameterType.AtmosphericComposition_Oxygen] = 1.0f;
    }

    /// <summary>
    /// 指定パラメータの現在値を取得します。
    /// Safe-Fail: 未知パラメータは 0.0f を返し警告を出力します。
    /// </summary>
    public float GetParameter(EnvironmentParameterType type)
    {
        if (currentParameters.TryGetValue(type, out float value))
        {
            return value;
        }

        Debug.LogWarning(
            $"[EnvironmentManager] 未知の環境パラメータ: {type}。デフォルト 0.0f を返します。");
        return 0.0f;
    }

    /// <summary>パラメータを更新します。</summary>
    public void SetParameter(EnvironmentParameterType type, float value)
    {
        currentParameters[type] = value;
    }

    /// <summary>現在のパラメータ辞書（読み取り専用）を取得します。</summary>
    public IReadOnlyDictionary<EnvironmentParameterType, float> GetCurrentParameters()
    {
        return currentParameters;
    }

    /// <summary>
    /// T≥1001 の環境パラメータ転換を適用します。
    /// 二重呼び出しは Safe-Fail でスキップします。
    /// </summary>
    public void ApplyEnvironmentalTransformation()
    {
        if (hasTransformed)
        {
            Debug.LogWarning(
                "[EnvironmentManager] 環境転換は既に適用済みです。スキップします。");
            return;
        }

        try
        {
            hasTransformed = true;
            Debug.Log(
                $"<color=#80DEEA><b>{LogTag}</b></color> " +
                "T≥1001 環境パラメータ転換を開始します。");

            // 気候変動: 温度上昇・湿度低下
            SetParameter(
                EnvironmentParameterType.Temperature,
                GetParameter(EnvironmentParameterType.Temperature) + UnityEngine.Random.Range(5.0f, 15.0f));
            SetParameter(
                EnvironmentParameterType.Humidity,
                GetParameter(EnvironmentParameterType.Humidity) * UnityEngine.Random.Range(0.3f, 0.7f));

            // 資源変動
            SetParameter(
                EnvironmentParameterType.ResourceAvailability_IronOre,
                GetParameter(EnvironmentParameterType.ResourceAvailability_IronOre) * 0.5f);
            SetParameter(
                EnvironmentParameterType.ResourceAvailability_Water,
                GetParameter(EnvironmentParameterType.ResourceAvailability_Water) * 0.7f);

            // 地質変動: 安定性低下 + レアアース出現
            SetParameter(
                EnvironmentParameterType.GeologicalStability,
                GetParameter(EnvironmentParameterType.GeologicalStability)
                * UnityEngine.Random.Range(0.1f, 0.4f));
            SetParameter(
                EnvironmentParameterType.ResourceAvailability_RareEarth,
                GetParameter(EnvironmentParameterType.ResourceAvailability_RareEarth)
                + UnityEngine.Random.Range(0.2f, 0.8f));

            // 大気変動
            SetParameter(
                EnvironmentParameterType.AtmosphericComposition_Oxygen,
                GetParameter(EnvironmentParameterType.AtmosphericComposition_Oxygen)
                * UnityEngine.Random.Range(0.8f, 1.2f));

            // HistoryFlag に記録
            HistoryFlagRegistry.EnsureWired();
            HistoryFlagRegistry.Unlock("ENV_TRANSFORMATION_T1001");

            Debug.Log(
                $"<color=#80DEEA><b>{LogTag}</b></color> " +
                "世界が変容を始めた。かつての均衡は失われ、新たな秩序が生まれつつある。" +
                $" (気温={GetParameter(EnvironmentParameterType.Temperature):F1}" +
                $" 湿度={GetParameter(EnvironmentParameterType.Humidity):F2}" +
                $" 鉄鉱={GetParameter(EnvironmentParameterType.ResourceAvailability_IronOre):F2}" +
                $" 地質={GetParameter(EnvironmentParameterType.GeologicalStability):F2})");
        }
        catch (System.Exception exception)
        {
            Debug.LogWarning(
                $"[EnvironmentManager] ApplyEnvironmentalTransformation Safe-Fail: {exception.Message}");
        }
    }

    /// <summary>検証用リセット。</summary>
    public void ResetForVerification()
    {
        hasTransformed = false;
        InitializeDefaultParameters();
    }
}

public static class EnvironmentManagerBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoAttach()
    {
        EnvironmentManager.EnsureInstance();
    }
}

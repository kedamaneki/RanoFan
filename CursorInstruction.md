# C# 実装指示プロンプト: 不遇枝エネルギー蓄積と連続自律進行システム

リードディレクター兼C#設計者として、ゲーム「フロム風戦闘×ブラインド熱科学クラフト×千年史自律シミュレーター」における「不遇枝エネルギー蓄積で連続自律進行」機能の実装を指示します。以下の指示に従い、Safe-Fail構造、MagicSanitizerEngine、Job/Magicの定義規約を厳格に守ってください。

---

## 1. データ構造定義

### 1.1. `UnfortunateBranchData` (ScriptableObject)

ゲーム世界に存在する「不遇枝」の特性を定義するScriptableObjectを作成します。これは、見過ごされがちだが潜在的なエネルギーを持つリソースです。

```csharp
// ファイル名: Assets/Scripts/Data/UnfortunateBranchData.cs
using UnityEngine;

[CreateAssetMenu(fileName = "UnfortunateBranchData", menuName = "GameData/UnfortunateBranch")]
public class UnfortunateBranchData : ScriptableObject
{
    [Tooltip("この不遇枝から抽出可能な最大エネルギーポテンシャル")]
    [Min(0)] public float MaxEnergyPotential = 100f;

    [Tooltip("不遇枝の発見難易度 (0-100)。高いほど発見しにくい")]
    [Range(0, 100)] public int DiscoveryDifficulty = 50;

    [Tooltip("不遇枝が時間と共に劣化する割合 (1秒あたりのエネルギー減少率)")]
    [Min(0)] public float DecayRatePerSecond = 0.01f;

    [Tooltip("不遇枝の熱科学的特性。エネルギー抽出時の効率や副産物に影響")]
    public HeatScienceProperty HeatProperty; // 後述のHeatSciencePropertyを参照

    [Tooltip("この不遇枝から得られる可能性がある特殊なクラフト素材")]
    public ItemData[] PotentialCraftMaterials; // ItemDataは既存のデータ構造を想定、必要に応じて定義
}

// ファイル名: Assets/Scripts/Data/HeatScienceProperty.cs
[System.Serializable]
public struct HeatScienceProperty
{
    [Tooltip("エネルギー抽出プロセスの基本効率 (0-1)。1はロスなし")]
    [Range(0f, 1f)] public float BaseExtractionEfficiency;

    [Tooltip("エネルギー抽出時に発生する熱量。クラフトや環境変化に影響")]
    [Min(0)] public float HeatGenerationOnExtraction;

    [Tooltip("特定の温度範囲で抽出効率が変化する可能性 (例: 低温で効率低下)。X軸:温度, Y軸:効率補正率")]
    public AnimationCurve EfficiencyTemperatureCurve; // 温度と効率の関係
}

// 仮のItemData定義 (必要に応じて詳細化)
// ファイル名: Assets/Scripts/Data/ItemData.cs
[CreateAssetMenu(fileName = "ItemData", menuName = "GameData/Item")]
public class ItemData : ScriptableObject
{
    public string ItemName;
    public string Description;
    public Sprite Icon;
    // 他のアイテム関連データ
}
```

### 1.2. `UnfortunateBranchInstance` (ランタイムインスタンス)

ゲーム世界に実際に存在する不遇枝のランタイム状態を管理するクラス。

```csharp
// ファイル名: Assets/Scripts/World/UnfortunateBranchInstance.cs
using UnityEngine;

public class UnfortunateBranchInstance
{
    public UnfortunateBranchData Data { get; private set; }
    public float CurrentEnergyAmount { get; private set; }
    public Vector3 WorldPosition { get; private set; }
    public float AgeSeconds { get; private set; } // 生成されてからの時間

    public UnfortunateBranchInstance(UnfortunateBranchData data, Vector3 position, float initialEnergy)
    {
        Data = data ?? throw new System.ArgumentNullException(nameof(data));
        WorldPosition = position;
        CurrentEnergyAmount = Mathf.Clamp(initialEnergy, 0, data.MaxEnergyPotential);
        AgeSeconds = 0f;
    }

    /// <summary>
    /// 時間経過による不遇枝の劣化を処理します。
    /// </summary>
    /// <param name="deltaTime">経過時間（秒）</param>
    /// <returns>劣化により減少したエネルギー量</returns>
    public float Decay(float deltaTime)
    {
        if (deltaTime < 0)
        {
            Debug.LogWarning("UnfortunateBranchInstance.Decay received negative deltaTime. Clamping to 0.");
            deltaTime = 0;
        }

        AgeSeconds += deltaTime;
        float decayAmount = Data.DecayRatePerSecond * deltaTime;
        float actualDecay = Mathf.Min(CurrentEnergyAmount, decayAmount);
        CurrentEnergyAmount -= actualDecay;
        if (CurrentEnergyAmount < 0) CurrentEnergyAmount = 0; // 念のため

        return actualDecay;
    }

    /// <summary>
    /// 不遇枝からエネルギーを抽出します。
    /// </summary>
    /// <param name="amount">抽出を試みる量</param>
    /// <returns>実際に抽出された量</returns>
    public float ExtractEnergy(float amount)
    {
        if (amount < 0)
        {
            Debug.LogWarning("UnfortunateBranchInstance.ExtractEnergy received negative amount. Clamping to 0.");
            amount = 0;
        }

        float extracted = Mathf.Min(CurrentEnergyAmount, amount);
        CurrentEnergyAmount -= extracted;
        if (CurrentEnergyAmount < 0) CurrentEnergyAmount = 0; // 念のため
        return extracted;
    }

    /// <summary>
    /// 不遇枝が枯渇したかどうかを判定します。
    /// </summary>
    public bool IsDepleted => CurrentEnergyAmount <= 0;
}
```

## 2. コアシステムインターフェースとコンテキスト

### 2.1. `IMagic` インターフェース

すべての「魔法」（社会技術）が実装すべきインターフェース。

```csharp
// ファイル名: Assets/Scripts/Core/Magic/IMagic.cs
public interface IMagic
{
    string MagicName { get; }
    string Description { get; }

    /// <summary>
    /// この魔法が現在のコンテキストで実行可能か判定します。
    /// </summary>
    /// <param name="context">魔法実行コンテキスト</param>
    /// <returns>実行可能であればtrue</returns>
    bool CanExecute(MagicContext context);

    /// <summary>
    /// 魔法を実行します。
    /// </summary>
    /// <param name="context">魔法実行コンテキスト</param>
    /// <returns>実行結果</returns>
    MagicResult Execute(MagicContext context);
}
```

### 2.2. `MagicContext` クラス

魔法実行に必要なすべての情報を含むコンテキストオブジェクト。

```csharp
// ファイル名: Assets/Scripts/Core/Magic/MagicContext.cs
using System.Collections.Generic;

public class MagicContext
{
    public PlayerState PlayerState { get; private set; } // プレイヤーの状態（リソース、スキルなど）
    public WorldState WorldState { get; private set; }   // 世界の状態（時間、環境、NPCなど）
    public ResourceInventory GlobalResources { get; private set; } // グローバルなリソース貯蔵庫
    public List<UnfortunateBranchInstance> AvailableBranches { get; private set; } // 利用可能な不遇枝インスタンス
    public float DeltaTime { get; private set; } // 魔法が作用する時間間隔

    public MagicContext(PlayerState playerState, WorldState worldState, ResourceInventory globalResources, List<UnfortunateBranchInstance> availableBranches, float deltaTime)
    {
        PlayerState = playerState ?? throw new System.ArgumentNullException(nameof(playerState));
        WorldState = worldState ?? throw new System.ArgumentNullException(nameof(worldState));
        GlobalResources = globalResources ?? throw new System.ArgumentNullException(nameof(globalResources));
        AvailableBranches = availableBranches ?? throw new System.ArgumentNullException(nameof(availableBranches));
        DeltaTime = deltaTime;
    }

    // 必要に応じて、さらに詳細な情報（例: 特定のクラフトステーション、NPCのリストなど）を追加
}

// 仮のPlayerState, WorldState, ResourceInventory定義
// ファイル名: Assets/Scripts/Core/PlayerState.cs
public class PlayerState
{
    public float CurrentEnergy { get; set; } = 0f;
    public float ResearchPoints { get; set; } = 0f;
    // 他のプレイヤー関連データ
}

// ファイル名: Assets/Scripts/Core/WorldState.cs
public class WorldState
{
    public float CurrentTime { get; private set; } = 0f; // シミュレーション時間
    public float WorldTemperature { get; set; } = 25f; // 世界の平均温度
    public void AdvanceTime(float deltaTime) => CurrentTime += deltaTime;
    // 他の世界関連データ
}

// ファイル名: Assets/Scripts/Core/ResourceInventory.cs
using System.Collections.Generic;
using UnityEngine; // Debug.LogWarningのために追加

public class ResourceInventory
{
    private Dictionary<string, float> _resources = new Dictionary<string, float>();

    public float GetResource(string resourceName)
    {
        if (string.IsNullOrEmpty(resourceName))
        {
            Debug.LogWarning("ResourceInventory.GetResource received null or empty resourceName.");
            return 0f;
        }
        _resources.TryGetValue(resourceName, out float amount);
        return amount;
    }

    public void AddResource(string resourceName, float amount)
    {
        if (string.IsNullOrEmpty(resourceName))
        {
            Debug.LogWarning("ResourceInventory.AddResource received null or empty resourceName.");
            return;
        }
        if (amount < 0)
        {
            Debug.LogWarning($"ResourceInventory.AddResource received negative amount ({amount}) for {resourceName}. Clamping to 0.");
            amount = 0;
        }
        _resources[resourceName] = GetResource(resourceName) + amount;
    }

    public bool TryConsumeResource(string resourceName, float amount)
    {
        if (string.IsNullOrEmpty(resourceName))
        {
            Debug.LogWarning("ResourceInventory.TryConsumeResource received null or empty resourceName.");
            return false;
        }
        if (amount < 0)
        {
            Debug.LogWarning($"ResourceInventory.TryConsumeResource received negative amount ({amount}) for {resourceName}. Clamping to 0.");
            amount = 0;
        }

        if (GetResource(resourceName) >= amount)
        {
            _resources[resourceName] -= amount;
            return true;
        }
        return false;
    }
}

// ファイル名: Assets/Scripts/Core/Magic/MagicResult.cs
public enum MagicResultType { Success, Failure, InsufficientResources, ConditionNotMet, Error }

public class MagicResult
{
    public MagicResultType Type { get; private set; }
    public string Message { get; private set; }
    public float EnergyConsumed { get; private set; } = 0f;
    public float EnergyProduced { get; private set; } = 0f;
    public Dictionary<string, float> ResourcesChanged { get; private set; } = new Dictionary<string, float>(); // 影響を受けたリソース

    public MagicResult(MagicResultType type, string message = "", float energyConsumed = 0f, float energyProduced = 0f)
    {
        Type = type;
        Message = message;
        EnergyConsumed = energyConsumed;
        EnergyProduced = energyProduced;
    }

    public static MagicResult Success(string message = "Magic executed successfully.", float energyConsumed = 0f, float energyProduced = 0f)
        => new MagicResult(MagicResultType.Success, message, energyConsumed, energyProduced);
    public static MagicResult Failure(string message = "Magic execution failed.")
        => new MagicResult(MagicResultType.Failure, message);
    public static MagicResult InsufficientResources(string message = "Insufficient resources to execute magic.")
        => new MagicResult(MagicResultType.InsufficientResources, message);
    public static MagicResult ConditionNotMet(string message = "Conditions for magic execution not met.")
        => new MagicResult(MagicResultType.ConditionNotMet, message);
    public static MagicResult Error(string message = "An unexpected error occurred during magic execution.")
        => new MagicResult(MagicResultType.Error, message);
}
```

### 2.3. `MagicSanitizerEngine` クラス

すべての魔法実行を安全に処理するエンジン。Safe-Fail構造を厳格に適用します。

```csharp
// ファイル名: Assets/Scripts/Core/Magic/MagicSanitizerEngine.cs
using System;
using UnityEngine;

public static class MagicSanitizerEngine
{
    /// <summary>
    /// 魔法の実行をサニタイズし、安全に実行します。
    /// </summary>
    /// <param name="magic">実行するIMagicインスタンス</param>
    /// <param name="context">魔法実行コンテキスト</param>
    /// <returns>魔法の実行結果</returns>
    public static MagicResult SanitizeAndExecute(IMagic magic, MagicContext context)
    {
        if (magic == null)
        {
            Debug.LogError("MagicSanitizerEngine: Attempted to execute a null magic.");
            return MagicResult.Error("Null magic provided.");
        }
        if (context == null)
        {
            Debug.LogError($"MagicSanitizerEngine: Null context provided for magic '{magic.MagicName}'.");
            return MagicResult.Error("Null context provided.");
        }

        try
        {
            // プリチェック: 実行可能条件の確認
            if (!magic.CanExecute(context))
            {
                return MagicResult.ConditionNotMet($"Magic '{magic.MagicName}' cannot be executed under current conditions.");
            }

            // 実行
            MagicResult result = magic.Execute(context);

            // ポストチェック: 実行結果の検証（オプション）
            if (result == null)
            {
                Debug.LogError($"MagicSanitizerEngine: Magic '{magic.MagicName}' returned a null result.");
                return MagicResult.Error("Magic returned a null result.");
            }

            return result;
        }
        catch (Exception ex)
        {
            Debug.LogError($"MagicSanitizerEngine: An unexpected error occurred during execution of magic '{magic.MagicName}'. Error: {ex.Message}\nStackTrace: {ex.StackTrace}");
            return MagicResult.Error($"An unexpected error occurred: {ex.Message}");
        }
    }
}
```

## 3. 「魔法」（社会技術）の実装

### 3.1. `UnfortunateBranchEnergyExtractionMagic` (ScriptableObject)

不遇枝からエネルギーを抽出し、グローバルなエネルギー貯蔵庫に蓄積する社会技術。これはScriptableObjectとして定義し、Inspectorから設定可能にします。

```csharp
// ファイル名: Assets/Scripts/Core/Magic/UnfortunateBranchEnergyExtractionMagic.cs
using UnityEngine;
using System.Linq;

[CreateAssetMenu(fileName = "UnfortunateBranchEnergyExtractionMagic", menuName = "GameMagic/UnfortunateBranchEnergyExtraction")]
public class UnfortunateBranchEnergyExtractionMagic : ScriptableObject, IMagic
{
    public string MagicName => "不遇枝エネルギー抽出技術";
    [TextArea]
    public string Description => "見過ごされた不遇枝からエネルギーを抽出し、貯蔵する技術。熱科学的プロセスを伴う。";

    [Tooltip("1回の抽出試行で消費する研究ポイント")]
    [Min(0)] public float ResearchPointsCostPerAttempt = 1f;
    [Tooltip("1回の抽出試行でターゲットとするエネルギー量")]
    [Min(0)] public float TargetExtractionAmount = 10f;
    [Tooltip("抽出プロセスに必要な最低限の技術レベル（プレイヤーのスキルなど、未実装の場合は0で無視）")]
    [Min(0)] public float RequiredTechLevel = 10f;

    public bool CanExecute(MagicContext context)
    {
        if (context == null) return false; // Safe-Fail
        if (context.GlobalResources.GetResource("ResearchPoints") < ResearchPointsCostPerAttempt)
        {
            return false; // 研究ポイントが足りない
        }
        // プレイヤーの技術レベルチェック（仮実装、PlayerStateにスキルシステムがあれば拡張）
        // if (context.PlayerState.GetSkillLevel("EnergyExtraction") < RequiredTechLevel) return false;

        // 抽出可能な不遇枝があるか
        return context.AvailableBranches != null && context.AvailableBranches.Any(b => !b.IsDepleted);
    }

    public MagicResult Execute(MagicContext context)
    {
        // Safe-Fail: コンテキストの再検証 (Sanitizerが保証するが、念のため)
        if (!CanExecute(context))
        {
            return MagicResult.ConditionNotMet("条件が満たされていないため、不遇枝エネルギー抽出技術を実行できません。");
        }

        // 研究ポイントを消費
        if (!context.GlobalResources.TryConsumeResource("ResearchPoints", ResearchPointsCostPerAttempt))
        {
            // CanExecuteでチェック済みだが、並行処理などで状態が変わる可能性を考慮
            return MagicResult.InsufficientResources("研究ポイントが不足しています。");
        }

        // 最もエネルギーポテンシャルの高い不遇枝を探す（またはランダム、最も近いなど）
        UnfortunateBranchInstance targetBranch = context.AvailableBranches
            .Where(b => !b.IsDepleted)
            .OrderByDescending(b => b.CurrentEnergyAmount)
            .FirstOrDefault();

        if (targetBranch == null)
        {
            context.GlobalResources.AddResource("ResearchPoints", ResearchPointsCostPerAttempt); // 消費したポイントを戻す
            return MagicResult.Failure("抽出可能な不遇枝が見つかりませんでした。");
        }

        // 抽出量の計算
        float actualExtractionAttempt = TargetExtractionAmount * context.DeltaTime; // デルタタイムを考慮
        float extractedFromBranch = targetBranch.ExtractEnergy(actualExtractionAttempt);

        // 熱科学的特性による効率計算
        float efficiency = targetBranch.Data.HeatProperty.BaseExtractionEfficiency;
        // 世界の温度による効率補正 (例: 温度曲線から取得)
        efficiency *= targetBranch.Data.HeatProperty.EfficiencyTemperatureCurve.Evaluate(context.WorldState.WorldTemperature);
        efficiency = Mathf.Clamp01(efficiency); // 効率は0-1の範囲にクランプ

        float energyProduced = extractedFromBranch * efficiency;
        float heatGenerated = extractedFromBranch * targetBranch.Data.HeatProperty.HeatGenerationOnExtraction;

        // グローバルエネルギー貯蔵庫に蓄積
        context.GlobalResources.AddResource("AccumulatedEnergy", energyProduced);
        context.WorldState.WorldTemperature += heatGenerated * 0.001f; // 熱発生による世界温度への影響（仮）

        // 抽出後の不遇枝の処理 (枯渇したらリストから削除など)
        if (targetBranch.IsDepleted)
        {
            // context.AvailableBranches からは WorldProgressionManager が削除する
            // 枯渇した不遇枝から得られる特殊素材のドロップ処理など
            // 例: context.GlobalResources.AddResource(targetBranch.Data.PotentialCraftMaterials[0].ItemName, 1);
        }

        return MagicResult.Success(
            $"不遇枝から {energyProduced:F2} エネルギーを抽出しました。熱量 {heatGenerated:F2} 発生。",
            energyConsumed: ResearchPointsCostPerAttempt,
            energyProduced: energyProduced
        );
    }
}
```

### 3.2. `AutonomousProgressionMagic` (ScriptableObject)

蓄積されたエネルギーを消費して、ゲーム世界を自律的に進行させる社会技術。これもScriptableObjectとして定義します。

```csharp
// ファイル名: Assets/Scripts/Core/Magic/AutonomousProgressionMagic.cs
using UnityEngine;
using System.Linq; // ToList()のために追加

[CreateAssetMenu(fileName = "AutonomousProgressionMagic", menuName = "GameMagic/AutonomousProgression")]
public class AutonomousProgressionMagic : ScriptableObject, IMagic
{
    public string MagicName => "歴史自律推進技術";
    [TextArea]
    public string Description => "蓄積されたエネルギーを消費し、世界の時間を自動的に進行させ、イベントをトリガーする技術。";

    [Tooltip("1回の自律進行サイクルで消費するエネルギー量")]
    [Min(0)] public float EnergyCostPerCycle = 50f;
    [Tooltip("1回の自律進行サイクルで進むゲーム内時間（秒）")]
    [Min(0)] public float TimeAdvancePerCycle = 60f; // 1サイクルで1分進む
    [Tooltip("自律進行中に発生するイベントの基本確率")]
    [Range(0f, 1f)] public float BaseEventTriggerChance = 0.1f;

    public bool CanExecute(MagicContext context)
    {
        if (context == null) return false; // Safe-Fail
        // 蓄積されたエネルギーが十分か
        return context.GlobalResources.GetResource("AccumulatedEnergy") >= EnergyCostPerCycle;
    }

    public MagicResult Execute(MagicContext context)
    {
        // Safe-Fail: コンテキストの再検証
        if (!CanExecute(context))
        {
            return MagicResult.ConditionNotMet("蓄積エネルギーが不足しているため、歴史自律推進技術を実行できません。");
        }

        // エネルギーを消費
        if (!context.GlobalResources.TryConsumeResource("AccumulatedEnergy", EnergyCostPerCycle))
        {
            // CanExecuteでチェック済みだが、念のため
            return MagicResult.InsufficientResources("蓄積エネルギーが不足しています。");
        }

        // 世界の時間を進行させる
        context.WorldState.AdvanceTime(TimeAdvancePerCycle);

        // 不遇枝の劣化を進行させる
        // ToList() でコピーを作成し、元のコレクション変更中に列挙エラーを防ぐ
        foreach (var branch in context.AvailableBranches.ToList())
        {
            branch.Decay(TimeAdvancePerCycle);
            if (branch.IsDepleted)
            {
                context.AvailableBranches.Remove(branch); // 枯渇した枝を削除
            }
        }

        // 自律進行イベントのトリガー（簡易版）
        string eventMessage = "世界が静かに進行しました。";
        if (Random.value < BaseEventTriggerChance)
        {
            // ここでWorldStateやPlayerStateに影響を与えるランダムイベントを発生させる
            eventMessage += " 不思議な出来事が起こったようです...";
            // 例: context.WorldState.TriggerRandomEvent();
            // 例: context.PlayerState.GainRandomBuff();
        }

        return MagicResult.Success(
            $"世界が {TimeAdvancePerCycle:F0} 秒進行しました。消費エネルギー: {EnergyCostPerCycle:F2}。{eventMessage}",
            energyConsumed: EnergyCostPerCycle
        );
    }
}
```

## 4. システムマネージャー

### 4.1. `EnergyStorageSystem`

グローバルなエネルギー貯蔵庫を管理するクラス。`ResourceInventory` の "AccumulatedEnergy" と連携し、UI表示や特定のロジックに利用することを想定します。

```csharp
// ファイル名: Assets/Scripts/Systems/EnergyStorageSystem.cs
using UnityEngine;

public class EnergyStorageSystem : MonoBehaviour
{
    [Tooltip("最大エネルギー貯蔵容量")]
    public float MaxStorageCapacity = 1000f;

    public delegate void EnergyChanged(float newAmount);
    public event EnergyChanged OnEnergyChanged;

    private ResourceInventory _globalResources; // GameManagerから注入される

    public float CurrentAccumulatedEnergy => _globalResources != null ? _globalResources.GetResource("AccumulatedEnergy") : 0f;

    public void Initialize(ResourceInventory globalResources)
    {
        _globalResources = globalResources ?? throw new System.ArgumentNullException(nameof(globalResources));
    }

    /// <summary>
    /// エネルギーを貯蔵庫に追加します。ResourceInventory経由で処理。
    /// </summary>
    /// <param name="amount">追加するエネルギー量</param>
    /// <returns>実際に貯蔵された量</returns>
    public float AddEnergy(float amount)
    {
        if (_globalResources == null) { Debug.LogError("EnergyStorageSystem not initialized with ResourceInventory."); return 0f; }
        if (amount < 0)
        {
            Debug.LogWarning("EnergyStorageSystem.AddEnergy received negative amount. Clamping to 0.");
            amount = 0;
        }

        float current = CurrentAccumulatedEnergy;
        float potentialNew = current + amount;
        float actualAdded = 0;

        if (potentialNew > MaxStorageCapacity)
        {
            actualAdded = MaxStorageCapacity - current;
            _globalResources.AddResource("AccumulatedEnergy", actualAdded);
        }
        else
        {
            actualAdded = amount;
            _globalResources.AddResource("AccumulatedEnergy", amount);
        }
        
        if (actualAdded > 0)
        {
            OnEnergyChanged?.Invoke(CurrentAccumulatedEnergy);
        }
        return actualAdded;
    }

    /// <summary>
    /// エネルギーを貯蔵庫から消費します。ResourceInventory経由で処理。
    /// </summary>
    /// <param name="amount">消費するエネルギー量</param>
    /// <returns>実際に消費された量</returns>
    public float ConsumeEnergy(float amount)
    {
        if (_globalResources == null) { Debug.LogError("EnergyStorageSystem not initialized with ResourceInventory."); return 0f; }
        if (amount < 0)
        {
            Debug.LogWarning("EnergyStorageSystem.ConsumeEnergy received negative amount. Clamping to 0.");
            amount = 0;
        }

        float current = CurrentAccumulatedEnergy;
        float actualConsumed = Mathf.Min(current, amount);

        if (_globalResources.TryConsumeResource("AccumulatedEnergy", actualConsumed))
        {
            if (actualConsumed > 0)
            {
                OnEnergyChanged?.Invoke(CurrentAccumulatedEnergy);
            }
            return actualConsumed;
        }
        return 0f; // 消費できなかった場合
    }

    /// <summary>
    /// 指定された量のエネルギーが利用可能かチェックします。
    /// </summary>
    public bool HasEnoughEnergy(float amount)
    {
        if (_globalResources == null) { Debug.LogError("EnergyStorageSystem not initialized with ResourceInventory."); return false; }
        return CurrentAccumulatedEnergy >= amount;
    }
}
```

### 4.2. `WorldProgressionManager`

ゲームの自律進行を管理し、定期的に魔法を実行するクラス。

```csharp
// ファイル名: Assets/Scripts/Systems/WorldProgressionManager.cs
using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public class WorldProgressionManager : MonoBehaviour
{
    [Header("Dependencies")]
    [SerializeField] private PlayerState _playerState; // プレイヤーの状態
    [SerializeField] private WorldState _worldState;   // 世界の状態
    [SerializeField] private ResourceInventory _globalResources; // グローバルリソースインベントリ
    [SerializeField] private EnergyStorageSystem _energyStorageSystem; // エネルギー貯蔵システム (UI連携用など)

    [Header("Magic Definitions")]
    [SerializeField] private UnfortunateBranchEnergyExtractionMagic _extractionMagic;
    [SerializeField] private AutonomousProgressionMagic _progressionMagic;

    [Header("Progression Settings")]
    [Tooltip("不遇枝の発見と抽出を試みる間隔（秒）")]
    [Min(0.1f)] public float ExtractionAttemptInterval = 5f;
    [Tooltip("自律進行を試みる間隔（秒）")]
    [Min(0.1f)] public float ProgressionAttemptInterval = 10f;

    private float _extractionTimer;
    private float _progressionTimer;

    private List<UnfortunateBranchInstance> _activeBranches = new List<UnfortunateBranchInstance>();

    // 初期化時に利用可能な不遇枝を生成する（テスト用）
    [Header("Debug/Initial Setup")]
    [SerializeField] private UnfortunateBranchData _initialBranchData;
    [SerializeField] private int _initialBranchCount = 5;

    public void Initialize(PlayerState playerState, WorldState worldState, ResourceInventory globalResources, EnergyStorageSystem energyStorageSystem)
    {
        _playerState = playerState ?? throw new System.ArgumentNullException(nameof(playerState));
        _worldState = worldState ?? throw new System.ArgumentNullException(nameof(worldState));
        _globalResources = globalResources ?? throw new System.ArgumentNullException(nameof(globalResources));
        _energyStorageSystem = energyStorageSystem ?? throw new System.ArgumentNullException(nameof(energyStorageSystem));

        // 初期化
        _extractionTimer = ExtractionAttemptInterval;
        _progressionTimer = ProgressionAttemptInterval;

        // 初期不遇枝の生成 (テスト用)
        if (_initialBranchData != null)
        {
            for (int i = 0; i < _initialBranchCount; i++)
            {
                Vector3 randomPos = new Vector3(Random.Range(-100f, 100f), 0, Random.Range(-100f, 100f));
                _activeBranches.Add(new UnfortunateBranchInstance(_initialBranchData, randomPos, _initialBranchData.MaxEnergyPotential));
            }
            Debug.Log($"Initialized with {_initialBranchCount} Unfortunate Branches.");
        }
        else
        {
            Debug.LogWarning("WorldProgressionManager: _initialBranchData is not assigned. No initial branches generated.");
        }
    }

    void Update()
    {
        // 依存関係が初期化されているか確認
        if (_playerState == null || _worldState == null || _globalResources == null || _energyStorageSystem == null || _extractionMagic == null || _progressionMagic == null)
        {
            Debug.LogWarning("WorldProgressionManager: Dependencies not fully initialized. Skipping Update logic.");
            return;
        }

        float deltaTime = Time.deltaTime;

        // 不遇枝エネルギー抽出の試行
        _extractionTimer -= deltaTime;
        if (_extractionTimer <= 0)
        {
            _extractionTimer = ExtractionAttemptInterval;
            TryExecuteMagic(_extractionMagic, "UnfortunateBranchEnergyExtraction");
        }

        // 自律進行の試行
        _progressionTimer -= deltaTime;
        if (_progressionTimer <= 0)
        {
            _progressionTimer = ProgressionAttemptInterval;
            TryExecuteMagic(_progressionMagic, "AutonomousProgression");
        }
    }

    /// <summary>
    /// 魔法実行コンテキストを構築し、MagicSanitizerEngineを介して魔法を実行します。
    /// </summary>
    private void TryExecuteMagic(IMagic magic, string debugName)
    {
        if (magic == null)
        {
            Debug.LogError($"WorldProgressionManager: Attempted to execute null magic for {debugName}.");
            return;
        }

        MagicContext context = new MagicContext(
            _playerState,
            _worldState,
            _globalResources,
            _activeBranches,
            Time.deltaTime // Updateからの呼び出しなので、deltaTimeを渡す
        );

        MagicResult result = MagicSanitizerEngine.SanitizeAndExecute(magic, context);

        if (result.Type != MagicResultType.Success)
        {
            Debug.LogWarning($"Magic '{magic.MagicName}' ({debugName}) failed: {result.Message}");
        }
        else
        {
            Debug.Log($"Magic '{magic.MagicName}' ({debugName}) succeeded: {result.Message}");
            // 成功した場合、UI更新などの追加処理をトリガー
            _energyStorageSystem.OnEnergyChanged?.Invoke(_energyStorageSystem.CurrentAccumulatedEnergy); // UI更新トリガー
        }
    }

    // 不遇枝を世界に追加するメソッド (外部から呼び出される可能性)
    public void AddUnfortunateBranch(UnfortunateBranchInstance branch)
    {
        if (branch != null && !_activeBranches.Contains(branch))
        {
            _activeBranches.Add(branch);
        }
    }
}
```

## 5. `GameManager` (またはエントリポイント)

上記システムを初期化し、管理するクラス。

```csharp
// ファイル名: Assets/Scripts/GameManager.cs
using UnityEngine;
using System.Collections.Generic;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    [Header("Core Systems")]
    public PlayerState PlayerState;
    public WorldState WorldState;
    public ResourceInventory GlobalResources;
    public EnergyStorageSystem EnergyStorageSystem;
    public WorldProgressionManager ProgressionManager;

    [Header("Initial Resources")]
    public float InitialResearchPoints = 100f;
    public float InitialAccumulatedEnergy = 0f;
    public float InitialWorldTemperature = 25f;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        InitializeSystems();
    }

    private void InitializeSystems()
    {
        // 各システムのインスタンス化または取得
        // シーンに既に存在する場合はGetComponentで取得、存在しない場合はnewまたはAddComponent
        PlayerState = PlayerState ?? new PlayerState();
        WorldState = WorldState ?? new WorldState();
        GlobalResources = GlobalResources ?? new ResourceInventory();
        EnergyStorageSystem = EnergyStorageSystem ?? FindObjectOfType<EnergyStorageSystem>() ?? gameObject.AddComponent<EnergyStorageSystem>();
        ProgressionManager = ProgressionManager ?? FindObjectOfType<WorldProgressionManager>() ?? gameObject.AddComponent<WorldProgressionManager>();

        // 初期リソースの設定
        GlobalResources.AddResource("ResearchPoints", InitialResearchPoints);
        GlobalResources.AddResource("AccumulatedEnergy", InitialAccumulatedEnergy);
        WorldState.WorldTemperature = InitialWorldTemperature;

        // EnergyStorageSystemの初期化
        EnergyStorageSystem.Initialize(GlobalResources);

        // ProgressionManagerへの依存関係注入
        ProgressionManager.Initialize(PlayerState, WorldState, GlobalResources, EnergyStorageSystem);

        Debug.Log("GameManager: Core systems initialized.");
    }

    // 他のゲーム管理ロジック...
}
```

---

## 実装上の注意点と拡張性

1.  **Job/Magic規約の遵守:**
    *   `UnfortunateBranchEnergyExtractionMagic` と `AutonomousProgressionMagic` は、ゲーム世界の挙動を変える「社会技術」として `IMagic` を実装し、`ScriptableObject` として定義されています。これにより、Inspectorからパラメータを調整し、異なる特性を持つ「技術」を簡単に作成できます。
    *   `Job` (生活職業) は、これらの魔法を実行するNPCの役割として定義されます。例えば、「不遇枝採掘者」というJobを持つNPCが `UnfortunateBranchEnergyExtractionMagic` を実行する、といった形で拡張可能です。今回は直接Jobの定義は含みませんが、将来的な設計の指針としてください。
2.  **Safe-Fail構造:**
    *   すべてのクラスとメソッドで、nullチェック、範囲チェック、例外ハンドリングを徹底しています。特に、`MagicSanitizerEngine` は、魔法実行の単一のエントリポイントとして、このSafe-Fail原則を強制します。
    *   `ResourceInventory` のメソッドも、無効な入力（null/空の文字列、負の量）に対して警告を発し、安全に処理するように改善されています。
3.  **ブラインド熱科学クラフト要素:**
    *   `UnfortunateBranchData` の `HeatScienceProperty` と、`UnfortunateBranchEnergyExtractionMagic` での効率計算や熱発生は、この要素の基礎となります。
    *   `EfficiencyTemperatureCurve` を使用することで、世界の温度がエネルギー抽出効率にどのように影響するかを視覚的に設定できます。
    *   将来的に、特定の温度環境下でのみ効率が上がる、熱を冷却しないと効率が下がる、などの複雑な熱力学シミュレーションを追加できます。
4.  **千年史自律シミュレーター:**
    *   `AutonomousProgressionMagic` が世界の時間を進め、不遇枝の劣化を処理し、イベントをトリガーすることで、プレイヤーの介入なしに歴史が進行する基盤を築きます。
    *   イベントシステムを拡張し、世界の状況（資源量、人口、技術レベルなど）に応じて異なるイベントが発生するようにすることで、よりリッチな千年史シミュレーションが可能です。
5.  **リソース管理:**
    *   `ResourceInventory` は汎用的なリソース管理、`EnergyStorageSystem` は特定のエネルギーリソースに特化した管理を行います。今回の実装では `ResourceInventory` が実際の値を保持し、`EnergyStorageSystem` はそのラッパーとして機能し、UI表示や特定のロジック（例：最大容量制限）に利用する想定です。これにより、リソース管理の一貫性を保ちつつ、特定のUIやシステムに特化した機能を提供できます。
6.  **UI表示:**
    *   `EnergyStorageSystem` の `OnEnergyChanged` イベントなどを利用して、現在のエネルギー量や進行状況をUIに表示するシステムを構築してください。

この指示に従い、精密なC#コードを実装してください。不明点があれば、再度質問してください。
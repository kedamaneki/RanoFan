はい、承知いたしました。
「フロム風戦闘×ブラインド熱科学クラフト×千年史自律シミュレーター」のリードディレクター兼C#設計者として、T1050からT1250の期間における「剪定理論で太いルートをコミット」する機能の実装指示を、Cursor(IDE)のCtrl+Lへそのまま読み込ませてC#コード化できる「精密な実装指示プロンプト(Markdown形式)」で出力します。

Safe-Fail構造、MagicSanitizerEngine、Job/Magicの定義規約(魔法=社会技術, ジョブ=生活職業)を厳格に守ります。

---

# C#実装指示プロンプト: T1050-T1250 剪定理論によるルートコミットメント

## 目的
シミュレーションのT1050年からT1250年の期間において、**剪定理論 (Pruning Theory)** を適用し、複数の潜在的な未来のパスの中から最も「太いルート」（安定性、主要な社会技術の発展、資源効率、特定の脅威への対処能力に優れたパス）を特定し、そのルートへシミュレーションの進行を**コミット**するシステムを実装します。これにより、この重要な歴史的転換期における特定の発展経路を確実なものとします。

## 全体構造と名前空間
`FromLikeCombatBlindHeatScienceCraftSimulator` 名前空間の下に、以下の主要なデータモデル、インターフェース、システムクラスを定義します。

```csharp
// FromLikeCombatBlindHeatScienceCraftSimulator/Core/SimulationCore.cs
// FromLikeCombatBlindHeatScienceCraftSimulator/Data/MagicData.cs
// FromLikeCombatBlindHeatScienceCraftSimulator/Data/JobData.cs
// FromLikeCombatBlindHeatScienceCraftSimulator/Data/SimulationState.cs
// FromLikeCombatBlindHeatScienceCraftSimulator/Systems/MagicSystem.cs
// FromLikeCombatBlindHeatScienceCraftSimulator/Systems/JobSystem.cs
// FromLikeCombatBlindHeatHeatScienceCraftSimulator/Systems/PruningTheoryEngine.cs
// FromLikeCombatBlindHeatScienceCraftSimulator/Engines/MagicSanitizerEngine.cs
// FromLikeCombatBlindHeatScienceCraftSimulator/Utility/ValidationResult.cs
```

## 1. データモデルの定義

### 1.1. `MagicData` (社会技術)
社会技術としての魔法を定義します。

```csharp
// FromLikeCombatBlindHeatScienceCraftSimulator/Data/MagicData.cs
using System.Collections.Generic;

namespace FromLikeCombatBlindHeatScienceCraftSimulator.Data
{
    /// <summary>
    /// 社会技術としての魔法のデータモデル。
    /// </summary>
    public record MagicData
    {
        /// <summary>魔法の一意な識別子。</summary>
        public string Id { get; init; }
        /// <summary>魔法の名称。</summary>
        public string Name { get; init; }
        /// <summary>魔法の説明。</summary>
        public string Description { get; init; }
        /// <summary>魔法のカテゴリ（例: 農業、冶金、医療、軍事）。</summary>
        public string Category { get; init; }
        /// <summary>この魔法を開発するための前提となる他の魔法のIDリスト。</summary>
        public List<string> Prerequisites { get; init; } = new List<string>();
        /// <summary>この魔法の開発や維持にかかる資源コスト（資源名と量）。</summary>
        public Dictionary<string, float> ResourceCosts { get; init; } = new Dictionary<string, float>();
        /// <summary>この魔法がシミュレーション状態に与える効果（パラメータ名と変化量）。</summary>
        public Dictionary<string, float> Effects { get; init; } = new Dictionary<string, float>();
        /// <summary>この魔法の開発難易度。高いほど開発に時間がかかる。</summary>
        public float DevelopmentDifficulty { get; init; }
        /// <summary>この魔法が社会技術であることを示すフラグ。</summary>
        public bool IsSocialTechnology => true; // 定義規約: Magic = 社会技術

        public MagicData(string id, string name, string description, string category, List<string> prerequisites, Dictionary<string, float> resourceCosts, Dictionary<string, float> effects, float developmentDifficulty)
        {
            Id = id;
            Name = name;
            Description = description;
            Category = category;
            Prerequisites = prerequisites ?? new List<string>();
            ResourceCosts = resourceCosts ?? new Dictionary<string, float>();
            Effects = effects ?? new Dictionary<string, float>();
            DevelopmentDifficulty = developmentDifficulty;
        }
    }
}
```

### 1.2. `JobData` (生活職業)
生活職業としてのジョブを定義します。

```csharp
// FromLikeCombatBlindHeatScienceCraftSimulator/Data/JobData.cs
using System.Collections.Generic;

namespace FromLikeCombatBlindHeatScienceCraftSimulator.Data
{
    /// <summary>
    /// 生活職業としてのジョブのデータモデル。
    /// </summary>
    public record JobData
    {
        /// <summary>ジョブの一意な識別子。</summary>
        public string Id { get; init; }
        /// <summary>ジョブの名称。</summary>
        public string Name { get; init; }
        /// <summary>ジョブの説明。</summary>
        public string Description { get; init; }
        /// <summary>ジョブのカテゴリ（例: 農業、採掘、職人、兵士）。</summary>
        public string Category { get; init; }
        /// <summary>このジョブに就くために必要な社会技術（Magic）のIDリスト。</summary>
        public List<string> RequiredMagicIds { get; init; } = new List<string>();
        /// <summary>このジョブが生産する資源とその量（資源名と生産率）。</summary>
        public Dictionary<string, float> ProductionRates { get; init; } = new Dictionary<string, float>();
        /// <summary>このジョブが消費する資源とその量（資源名と消費率）。</summary>
        public Dictionary<string, float> ConsumptionRates { get; init; } = new Dictionary<string, float>();
        /// <summary>このジョブの効率性に乗算されるスキル係数。</summary>
        public float SkillMultiplier { get; init; }
        /// <summary>このジョブが生活職業であることを示すフラグ。</summary>
        public bool IsLifeOccupation => true; // 定義規約: Job = 生活職業

        public JobData(string id, string name, string description, string category, List<string> requiredMagicIds, Dictionary<string, float> productionRates, Dictionary<string, float> consumptionRates, float skillMultiplier)
        {
            Id = id;
            Name = name;
            Description = description;
            Category = category;
            RequiredMagicIds = requiredMagicIds ?? new List<string>();
            ProductionRates = productionRates ?? new Dictionary<string, float>();
            ConsumptionRates = consumptionRates ?? new Dictionary<string, float>();
            SkillMultiplier = skillMultiplier;
        }
    }
}
```

### 1.3. `SimulationState`
シミュレーションの現在の状態を保持します。

```csharp
// FromLikeCombatBlindHeatScienceCraftSimulator/Data/SimulationState.cs
using System.Collections.Generic;
using System.Linq;

namespace FromLikeCombatBlindHeatScienceCraftSimulator.Data
{
    /// <summary>
    /// シミュレーションの現在の状態を保持するレコード。
    /// </summary>
    public record SimulationState
    {
        /// <summary>現在のシミュレーション時間（年）。</summary>
        public long CurrentTime { get; set; }
        /// <summary>現在の資源量（資源名と量）。</summary>
        public Dictionary<string, float> Resources { get; init; } = new Dictionary<string, float>();
        /// <summary>現在の人口（カテゴリと人数）。</summary>
        public Dictionary<string, int> Population { get; init; } = new Dictionary<string, int>();
        /// <summary>発見済みの社会技術（Magic）のIDセット。</summary>
        public HashSet<string> DiscoveredMagicIds { get; init; } = new HashSet<string>();
        /// <summary>現在割り当てられているジョブとその人数（ジョブIDと人数）。</summary>
        public Dictionary<string, int> ActiveJobAssignments { get; init; } = new Dictionary<string, int>();
        /// <summary>世界のその他のパラメータ（例: 安定度、文化レベル）。</summary>
        public Dictionary<string, float> WorldParameters { get; init; } = new Dictionary<string, float>();

        public SimulationState(long currentTime, Dictionary<string, float> resources, Dictionary<string, int> population, HashSet<string> discoveredMagicIds, Dictionary<string, int> activeJobAssignments, Dictionary<string, float> worldParameters)
        {
            CurrentTime = currentTime;
            Resources = resources?.ToDictionary(entry => entry.Key, entry => entry.Value) ?? new Dictionary<string, float>();
            Population = population?.ToDictionary(entry => entry.Key, entry => entry.Value) ?? new Dictionary<string, int>();
            DiscoveredMagicIds = discoveredMagicIds != null ? new HashSet<string>(discoveredMagicIds) : new HashSet<string>();
            ActiveJobAssignments = activeJobAssignments?.ToDictionary(entry => entry.Key, entry => entry.Value) ?? new Dictionary<string, int>();
            WorldParameters = worldParameters?.ToDictionary(entry => entry.Key, entry => entry.Value) ?? new Dictionary<string, float>();
        }

        /// <summary>
        /// 現在の状態をディープコピーして新しいインスタンスを生成します。
        /// </summary>
        /// <returns>ディープコピーされたSimulationStateの新しいインスタンス。</returns>
        public SimulationState DeepCopy()
        {
            return new SimulationState(
                CurrentTime,
                Resources.ToDictionary(k => k.Key, v => v.Value),
                Population.ToDictionary(k => k.Key, v => v.Value),
                new HashSet<string>(DiscoveredMagicIds),
                ActiveJobAssignments.ToDictionary(k => k.Key, v => v.Value),
                WorldParameters.ToDictionary(k => k.Key, v => v.Value)
            );
        }
    }
}
```

### 1.4. `PruningPath`
剪定理論で評価される潜在的な未来のパスを定義します。

```csharp
// FromLikeCombatBlindHeatScienceCraftSimulator/Data/PruningPath.cs
using System.Collections.Generic;

namespace FromLikeCombatBlindHeatScienceCraftSimulator.Data
{
    /// <summary>
    /// 剪定理論で評価される潜在的な未来のパスのデータモデル。
    /// </summary>
    public record PruningPath
    {
        /// <summary>パスの一意な識別子。</summary>
        public string PathId { get; init; }
        /// <summary>このパスの開始時点のシミュレーション状態。</summary>
        public SimulationState InitialState { get; init; }
        /// <summary>このパスが辿る未来のイベントや状態変化のリスト。</summary>
        public List<SimulationEvent> FutureEvents { get; init; } = new List<SimulationEvent>();
        /// <summary>このパスの評価スコア。高いほど「太いルート」に近い。</summary>
        public float Score { get; set; }
        /// <summary>このパスが到達した最終状態。</summary>
        public SimulationState FinalState { get; init; }

        public PruningPath(string pathId, SimulationState initialState, List<SimulationEvent> futureEvents, SimulationState finalState)
        {
            PathId = pathId;
            InitialState = initialState;
            FutureEvents = futureEvents ?? new List<SimulationEvent>();
            FinalState = finalState;
        }
    }

    /// <summary>
    /// シミュレーションイベントの抽象基底クラス。
    /// </summary>
    public abstract record SimulationEvent
    {
        public long TriggerTime { get; init; }
        public string Description { get; init; }
    }

    /// <summary>
    /// 特定のMagicが発見されるイベント。
    /// </summary>
    public record MagicDiscoveryEvent : SimulationEvent
    {
        public string MagicId { get; init; }
        public MagicDiscoveryEvent(long triggerTime, string description, string magicId)
        {
            TriggerTime = triggerTime;
            Description = description;
            MagicId = magicId;
        }
    }

    /// <summary>
    /// ジョブ割り当てが変更されるイベント。
    /// </summary>
    public record JobAssignmentChangeEvent : SimulationEvent
    {
        public string JobId { get; init; }
        public int DeltaPopulation { get; init; } // 増加または減少
        public JobAssignmentChangeEvent(long triggerTime, string description, string jobId, int deltaPopulation)
        {
            TriggerTime = triggerTime;
            Description = description;
            JobId = jobId;
            DeltaPopulation = deltaPopulation;
        }
    }

    // 他のイベントタイプも必要に応じて追加 (例: ResourceFluctuationEvent, CombatOutcomeEvent)
}
```

## 2. ユーティリティクラス

### 2.1. `ValidationResult`
操作の検証結果を返します。Safe-Fail構造の基盤となります。

```csharp
// FromLikeCombatBlindHeatScienceCraftSimulator/Utility/ValidationResult.cs
using System.Collections.Generic;
using System.Linq;

namespace FromLikeCombatBlindHeatScienceCraftSimulator.Utility
{
    /// <summary>
    /// 操作の検証結果をカプセル化するクラス。
    /// Safe-Fail構造において、成功/失敗だけでなく、理由や詳細情報を提供します。
    /// </summary>
    public class ValidationResult
    {
        /// <summary>検証が成功したかどうか。</summary>
        public bool IsSuccess { get; private set; }
        /// <summary>検証が失敗した場合のエラーメッセージのリスト。</summary>
        public List<string> Errors { get; private set; } = new List<string>();
        /// <summary>検証が成功した場合の情報メッセージのリスト。</summary>
        public List<string> Messages { get; private set; } = new List<string>();

        private ValidationResult(bool isSuccess)
        {
            IsSuccess = isSuccess;
        }

        /// <summary>
        /// 成功したValidationResultインスタンスを作成します。
        /// </summary>
        /// <returns>成功したValidationResult。</returns>
        public static ValidationResult Success() => new ValidationResult(true);

        /// <summary>
        /// 失敗したValidationResultインスタンスを作成します。
        /// </summary>
        /// <param name="error">最初のエラーメッセージ。</param>
        /// <returns>失敗したValidationResult。</returns>
        public static ValidationResult Fail(string error)
        {
            var result = new ValidationResult(false);
            result.Errors.Add(error);
            return result;
        }

        /// <summary>
        /// エラーメッセージを追加します。
        /// </summary>
        /// <param name="error">追加するエラーメッセージ。</param>
        /// <returns>現在のValidationResultインスタンス。</returns>
        public ValidationResult AddError(string error)
        {
            if (string.IsNullOrWhiteSpace(error)) return this;
            Errors.Add(error);
            IsSuccess = false; // エラーが追加されたら失敗とマーク
            return this;
        }

        /// <summary>
        /// 情報メッセージを追加します。
        /// </summary>
        /// <param name="message">追加する情報メッセージ。</param>
        /// <returns>現在のValidationResultインスタンス。</returns>
        public ValidationResult AddMessage(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return this;
            Messages.Add(message);
            return this;
        }

        /// <summary>
        /// 他のValidationResultを結合します。
        /// </summary>
        /// <param name="other">結合する他のValidationResult。</param>
        /// <returns>結合されたValidationResultインスタンス。</returns>
        public ValidationResult Merge(ValidationResult other)
        {
            if (other == null) return this;
            Errors.AddRange(other.Errors);
            Messages.AddRange(other.Messages);
            if (!other.IsSuccess) IsSuccess = false;
            return this;
        }

        /// <summary>
        /// 全てのエラーメッセージを結合した文字列を返します。
        /// </summary>
        public string FullErrorMessage => string.Join("; ", Errors);

        /// <summary>
        /// 全ての情報メッセージを結合した文字列を返します。
        /// </summary>
        public string FullMessage => string.Join("; ", Messages);
    }

    /// <summary>
    /// サニタイズ操作の結果をカプセル化するクラス。
    /// </summary>
    public class SanitizationResult : ValidationResult
    {
        /// <summary>サニタイズによって変更が加えられたかどうか。</summary>
        public bool IsModified { get; private set; }

        private SanitizationResult(bool isSuccess, bool isModified) : base(isSuccess)
        {
            IsModified = isModified;
        }

        /// <summary>
        /// 成功したSanitizationResultインスタンスを作成します。
        /// </summary>
        /// <param name="isModified">サニタイズによって変更が加えられたか。</param>
        /// <returns>成功したSanitizationResult。</returns>
        public static SanitizationResult Success(bool isModified = false) => new SanitizationResult(true, isModified);

        /// <summary>
        /// 失敗したSanitizationResultインスタンスを作成します。
        /// </summary>
        /// <param name="error">最初のエラーメッセージ。</param>
        /// <returns>失敗したSanitizationResult。</returns>
        public new static SanitizationResult Fail(string error)
        {
            var result = new SanitizationResult(false, false);
            result.AddError(error);
            return result;
        }

        /// <summary>
        /// サニタイズによって変更が加えられたことをマークします。
        /// </summary>
        /// <returns>現在のSanitizationResultインスタンス。</returns>
        public SanitizationResult MarkModified()
        {
            IsModified = true;
            return this;
        }
    }
}
```

## 3. エンジンとシステム

### 3.1. `IMagicSanitizerEngine` インターフェース
`MagicSanitizerEngine`の契約を定義します。

```csharp
// FromLikeCombatBlindHeatScienceCraftSimulator/Engines/IMagicSanitizerEngine.cs
using FromLikeCombatBlindHeatScienceCraftSimulator.Data;
using FromLikeCombatBlindHeatScienceCraftSimulator.Utility;

namespace FromLikeCombatBlindHeatScienceCraftSimulator.Engines
{
    /// <summary>
    /// 社会技術（Magic）の提案と適用を検証・サニタイズするエンジンのインターフェース。
    /// Safe-Fail構造を厳格に守り、ゲームバランスと論理的整合性を維持します。
    /// </summary>
    public interface IMagicSanitizerEngine
    {
        /// <summary>
        /// 新しい社会技術（Magic）の提案がゲームバランスを崩さないか、矛盾しないかなどを検証します。
        /// </summary>
        /// <param name="magic">検証するMagicData。</param>
        /// <param name="currentState">現在のシミュレーション状態。</param>
        /// <returns>検証結果。</returns>
        ValidationResult ValidateMagicProposal(MagicData magic, SimulationState currentState);

        /// <summary>
        /// 社会技術（Magic）が適用される際に、予期せぬ副作用や悪用を防ぐために効果を調整・制限します。
        /// </summary>
        /// <param name="magic">適用されるMagicData。</param>
        /// <param name="currentState">現在のシミュレーション状態（必要に応じて変更される）。</param>
        /// <returns>サニタイズ結果。</returns>
        SanitizationResult SanitizeMagicApplication(MagicData magic, SimulationState currentState);

        /// <summary>
        /// 社会技術（Magic）の前提条件が現在のシミュレーション状態で満たされているかを確認します。
        /// </summary>
        /// <param name="magic">確認するMagicData。</param>
        /// <param name="currentState">現在のシミュレーション状態。</param>
        /// <returns>前提条件が満たされていればtrue、そうでなければfalse。</returns>
        bool CheckMagicPrerequisites(MagicData magic, SimulationState currentState);
    }
}
```

### 3.2. `MagicSanitizerEngine` クラス
`IMagicSanitizerEngine`の実装です。

```csharp
// FromLikeCombatBlindHeatScienceCraftSimulator/Engines/MagicSanitizerEngine.cs
using FromLikeCombatBlindHeatScienceCraftSimulator.Data;
using FromLikeCombatBlindHeatScienceCraftSimulator.Utility;
using System;
using System.Linq;

namespace FromLikeCombatBlindHeatScienceCraftSimulator.Engines
{
    /// <summary>
    /// 社会技術（Magic）の提案と適用を検証・サニタイズする具体的な実装。
    /// Safe-Fail構造を厳格に守り、ゲームバランスと論理的整合性を維持します。
    /// </summary>
    public class MagicSanitizerEngine : IMagicSanitizerEngine
    {
        private const float MaxResourceEffectMultiplier = 2.0f; // 資源効果の最大乗数
        private const float MaxPopulationEffectAbsolute = 0.1f; // 人口効果の最大絶対値（割合）

        /// <summary>
        /// 新しい社会技術（Magic）の提案がゲームバランスを崩さないか、矛盾しないかなどを検証します。
        /// </summary>
        /// <param name="magic">検証するMagicData。</param>
        /// <param name="currentState">現在のシミュレーション状態。</param>
        /// <returns>検証結果。</returns>
        public ValidationResult ValidateMagicProposal(MagicData magic, SimulationState currentState)
        {
            // Safe-Fail: 引数チェック
            if (magic == null) return ValidationResult.Fail("MagicData cannot be null.");
            if (currentState == null) return ValidationResult.Fail("SimulationState cannot be null.");

            var result = ValidationResult.Success();

            // 1. IDの重複チェック (データストアから取得する必要があるが、ここでは仮に)
            // if (MagicRegistry.Instance.ContainsMagic(magic.Id)) result.AddError($"Magic ID '{magic.Id}' already exists.");

            // 2. 名称の重複チェック (同上)
            // if (MagicRegistry.Instance.ContainsMagicName(magic.Name)) result.AddError($"Magic name '{magic.Name}' already exists.");

            // 3. 開発難易度の妥当性チェック
            if (magic.DevelopmentDifficulty <= 0) result.AddError("Development difficulty must be positive.");

            // 4. 前提条件の循環参照チェック (簡易版)
            if (magic.Prerequisites.Contains(magic.Id)) result.AddError($"Magic '{magic.Id}' cannot be its own prerequisite.");

            // 5. 効果の妥当性チェック (過剰な効果を制限)
            foreach (var effect in magic.Effects)
            {
                if (effect.Key.StartsWith("Resource_"))
                {
                    // 資源効果が過剰でないか
                    if (Math.Abs(effect.Value) > MaxResourceEffectMultiplier)
                    {
                        result.AddError($"Resource effect '{effect.Key}' value {effect.Value} is too extreme. Max allowed multiplier: {MaxResourceEffectMultiplier}.");
                    }
                }
                else if (effect.Key.StartsWith("Population_"))
                {
                    // 人口効果が過剰でないか (現在の人口に対する割合でチェック)
                    var totalPopulation = currentState.Population.Values.Sum();
                    if (totalPopulation > 0 && Math.Abs(effect.Value / totalPopulation) > MaxPopulationEffectAbsolute)
                    {
                        result.AddError($"Population effect '{effect.Key}' value {effect.Value} is too extreme. Max allowed absolute change relative to total population: {MaxPopulationEffectAbsolute}.");
                    }
                }
                // 他のパラメータに対する効果も同様にチェック
            }

            return result;
        }

        /// <summary>
        /// 社会技術（Magic）が適用される際に、予期せぬ副作用や悪用を防ぐために効果を調整・制限します。
        /// </summary>
        /// <param name="magic">適用されるMagicData。</param>
        /// <param name="currentState">現在のシミュレーション状態（必要に応じて変更される）。</param>
        /// <returns>サニタイズ結果。</returns>
        public SanitizationResult SanitizeMagicApplication(MagicData magic, SimulationState currentState)
        {
            // Safe-Fail: 引数チェック
            if (magic == null) return SanitizationResult.Fail("MagicData cannot be null.");
            if (currentState == null) return SanitizationResult.Fail("SimulationState cannot be null.");

            var result = SanitizationResult.Success(false); // 初期状態では変更なし

            // MagicDataはimmutableなので、ここではcurrentStateへの適用を想定し、
            // 適用される効果がcurrentStateに与える影響をサニタイズする。
            // もしMagicData自体を変更する必要があるなら、DeepCopyして変更したものを返す。

            foreach (var effect in magic.Effects)
            {
                if (effect.Key.StartsWith("Resource_"))
                {
                    var resourceName = effect.Key.Replace("Resource_", "");
                    if (currentState.Resources.ContainsKey(resourceName))
                    {
                        // 資源の最大値・最小値を設定し、効果がその範囲を超える場合は制限
                        float currentResource = currentState.Resources[resourceName];
                        float potentialChange = effect.Value; // MagicDataの効果は直接的な変化量と仮定

                        // 例: 資源がマイナスにならないように制限
                        if (currentResource + potentialChange < 0)
                        {
                            potentialChange = -currentResource; // 0までしか減らせない
                            result.AddMessage($"Resource '{resourceName}' effect capped to prevent negative values.");
                            result.MarkModified();
                        }
                        // 例: 資源の最大値 (ここでは仮に10000)
                        if (currentResource + potentialChange > 10000)
                        {
                            potentialChange = 10000 - currentResource;
                            result.AddMessage($"Resource '{resourceName}' effect capped to prevent exceeding max value.");
                            result.MarkModified();
                        }
                        // currentState.Resources[resourceName] += potentialChange; // 実際の適用はMagicSystemで行う
                    }
                }
                else if (effect.Key.StartsWith("Population_"))
                {
                    var populationCategory = effect.Key.Replace("Population_", "");
                    if (currentState.Population.ContainsKey(populationCategory))
                    {
                        int currentPopulation = currentState.Population[populationCategory];
                        float potentialChange = effect.Value; // MagicDataの効果は直接的な変化量と仮定

                        // 人口が0未満にならないように制限
                        if (currentPopulation + potentialChange < 0)
                        {
                            potentialChange = -currentPopulation;
                            result.AddMessage($"Population '{populationCategory}' effect capped to prevent negative values.");
                            result.MarkModified();
                        }
                        // currentState.Population[populationCategory] += (int)potentialChange; // 実際の適用はMagicSystemで行う
                    }
                }
                // 他のパラメータに対する効果も同様にサニタイズ
            }

            return result;
        }

        /// <summary>
        /// 社会技術（Magic）の前提条件が現在のシミュレーション状態で満たされているかを確認します。
        /// </summary>
        /// <param name="magic">確認するMagicData。</param>
        /// <param name="currentState">現在のシミュレーション状態。</param>
        /// <returns>前提条件が満たされていればtrue、そうでなければfalse。</returns>
        public bool CheckMagicPrerequisites(MagicData magic, SimulationState currentState)
        {
            // Safe-Fail: 引数チェック
            if (magic == null) throw new ArgumentNullException(nameof(magic));
            if (currentState == null) throw new ArgumentNullException(nameof(currentState));

            foreach (var prerequisiteMagicId in magic.Prerequisites)
            {
                if (!currentState.DiscoveredMagicIds.Contains(prerequisiteMagicId))
                {
                    return false; // 必要な前提Magicが発見されていない
                }
            }
            // 他の前提条件（例: 特定の資源量、特定のJobの存在、WorldParameterの値）もここに追加
            // 例: if (magic.ResourceCosts.Any(cost => currentState.Resources.GetValueOrDefault(cost.Key, 0) < cost.Value)) return false;

            return true;
        }
    }
}
```

### 3.3. `MagicSystem`
社会技術（Magic）の管理を行います。

```csharp
// FromLikeCombatBlindHeatScienceCraftSimulator/Systems/MagicSystem.cs
using FromLikeCombatBlindHeatScienceCraftSimulator.Data;
using FromLikeCombatBlindHeatScienceCraftSimulator.Engines;
using FromLikeCombatBlindHeatScienceCraftSimulator.Utility;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FromLikeCombatBlindHeatScienceCraftSimulator.Systems
{
    /// <summary>
    /// 社会技術（Magic）の発見、適用、管理を行うシステム。
    /// </summary>
    public class MagicSystem
    {
        private readonly IMagicSanitizerEngine _sanitizer;
        private readonly Dictionary<string, MagicData> _allMagicData; // 全てのMagicDataを保持

        public MagicSystem(IMagicSanitizerEngine sanitizer, IEnumerable<MagicData> allMagicData)
        {
            _sanitizer = sanitizer ?? throw new ArgumentNullException(nameof(sanitizer));
            _allMagicData = allMagicData?.ToDictionary(m => m.Id) ?? throw new ArgumentNullException(nameof(allMagicData));
        }

        /// <summary>
        /// 新しい社会技術（Magic）を発見します。
        /// </summary>
        /// <param name="magicId">発見するMagicのID。</param>
        /// <param name="currentState">現在のシミュレーション状態。</param>
        /// <returns>発見が成功したかどうかのValidationResult。</returns>
        public ValidationResult DiscoverMagic(string magicId, SimulationState currentState)
        {
            // Safe-Fail: 引数チェック
            if (string.IsNullOrWhiteSpace(magicId)) return ValidationResult.Fail("Magic ID cannot be null or empty.");
            if (currentState == null) throw new ArgumentNullException(nameof(currentState));

            if (!_allMagicData.TryGetValue(magicId, out var magic))
            {
                return ValidationResult.Fail($"Magic with ID '{magicId}' not found in registry.");
            }

            if (currentState.DiscoveredMagicIds.Contains(magicId))
            {
                return ValidationResult.Success().AddMessage($"Magic '{magic.Name}' (ID: {magicId}) is already discovered.");
            }

            // 前提条件のチェック
            if (!_sanitizer.CheckMagicPrerequisites(magic, currentState))
            {
                return ValidationResult.Fail($"Prerequisites for Magic '{magic.Name}' (ID: {magicId}) are not met.");
            }

            // Magicの提案自体の検証 (もし動的にMagicが生成される場合)
            var proposalValidation = _sanitizer.ValidateMagicProposal(magic, currentState);
            if (!proposalValidation.IsSuccess)
            {
                return ValidationResult.Fail($"Magic '{magic.Name}' (ID: {magicId}) proposal validation failed: {proposalValidation.FullErrorMessage}");
            }

            // Magicの発見を状態に反映
            currentState.DiscoveredMagicIds.Add(magicId);
            ApplyMagicEffects(magic, currentState); // 発見時に効果を適用

            return ValidationResult.Success().AddMessage($"Magic '{magic.Name}' (ID: {magicId}) discovered and applied.");
        }

        /// <summary>
        /// 発見済みの社会技術（Magic）の効果をシミュレーション状態に適用します。
        /// </summary>
        /// <param name="magic">適用するMagicData。</param>
        /// <param name="currentState">現在のシミュレーション状態。</param>
        private void ApplyMagicEffects(MagicData magic, SimulationState currentState)
        {
            // Safe-Fail: 引数チェック
            if (magic == null) throw new ArgumentNullException(nameof(magic));
            if (currentState == null) throw new ArgumentNullException(nameof(currentState));

            // サニタイズエンジンで効果を調整
            var sanitizationResult = _sanitizer.SanitizeMagicApplication(magic, currentState);
            if (!sanitizationResult.IsSuccess)
            {
                Console.WriteLine($"Warning: Failed to sanitize effects for Magic '{magic.Name}': {sanitizationResult.FullErrorMessage}");
                // 致命的でない場合は続行、必要に応じてエラーをログに記録
            }
            if (sanitizationResult.IsModified)
            {
                Console.WriteLine($"Info: Effects for Magic '{magic.Name}' were modified by sanitizer: {sanitizationResult.FullMessage}");
            }

            foreach (var effect in magic.Effects)
            {
                if (effect.Key.StartsWith("Resource_"))
                {
                    var resourceName = effect.Key.Replace("Resource_", "");
                    currentState.Resources.TryAdd(resourceName, 0); // 存在しない場合は初期化
                    currentState.Resources[resourceName] += effect.Value;
                    // 資源は0未満にならないようにする (SanitizeMagicApplicationでもチェック済みだが、念のため)
                    if (currentState.Resources[resourceName] < 0) currentState.Resources[resourceName] = 0;
                }
                else if (effect.Key.StartsWith("Population_"))
                {
                    var populationCategory = effect.Key.Replace("Population_", "");
                    currentState.Population.TryAdd(populationCategory, 0);
                    currentState.Population[populationCategory] += (int)effect.Value;
                    if (currentState.Population[populationCategory] < 0) currentState.Population[populationCategory] = 0;
                }
                else if (effect.Key.StartsWith("WorldParameter_"))
                {
                    var paramName = effect.Key.Replace("WorldParameter_", "");
                    currentState.WorldParameters.TryAdd(paramName, 0);
                    currentState.WorldParameters[paramName] += effect.Value;
                }
                // 他の種類の効果もここに追加
            }
        }

        /// <summary>
        /// 指定されたMagic IDのMagicDataを取得します。
        /// </summary>
        /// <param name="magicId">MagicのID。</param>
        /// <returns>対応するMagicData、見つからない場合はnull。</returns>
        public MagicData GetMagicData(string magicId)
        {
            // Safe-Fail: 引数チェック
            if (string.IsNullOrWhiteSpace(magicId)) return null;
            _allMagicData.TryGetValue(magicId, out var magic);
            return magic;
        }
    }
}
```

### 3.4. `JobSystem`
生活職業（Job）の管理を行います。

```csharp
// FromLikeCombatBlindHeatScienceCraftSimulator/Systems/JobSystem.cs
using FromLikeCombatBlindHeatScienceCraftSimulator.Data;
using FromLikeCombatBlindHeatScienceCraftSimulator.Engines; // MagicSanitizerEngineへの依存は間接的
using FromLikeCombatBlindHeatScienceCraftSimulator.Utility;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FromLikeCombatBlindHeatHeatScienceCraftSimulator.Systems
{
    /// <summary>
    /// 生活職業（Job）の割り当て、生産、消費を管理するシステム。
    /// </summary>
    public class JobSystem
    {
        private readonly Dictionary<string, JobData> _allJobData; // 全てのJobDataを保持
        private readonly MagicSystem _magicSystem; // Jobの前提条件チェックに必要

        public JobSystem(IEnumerable<JobData> allJobData, MagicSystem magicSystem)
        {
            _allJobData = allJobData?.ToDictionary(j => j.Id) ?? throw new ArgumentNullException(nameof(allJobData));
            _magicSystem = magicSystem ?? throw new ArgumentNullException(nameof(magicSystem));
        }

        /// <summary>
        /// 指定されたジョブに人口を割り当てます。
        /// </summary>
        /// <param name="jobId">割り当てるジョブのID。</param>
        /// <param name="populationCount">割り当てる人口数。</param>
        /// <param name="currentState">現在のシミュレーション状態。</param>
        /// <returns>割り当てが成功したかどうかのValidationResult。</returns>
        public ValidationResult AssignJob(string jobId, int populationCount, SimulationState currentState)
        {
            // Safe-Fail: 引数チェック
            if (string.IsNullOrWhiteSpace(jobId)) return ValidationResult.Fail("Job ID cannot be null or empty.");
            if (populationCount <= 0) return ValidationResult.Fail("Population count must be positive for assignment.");
            if (currentState == null) throw new ArgumentNullException(nameof(currentState));

            if (!_allJobData.TryGetValue(jobId, out var job))
            {
                return ValidationResult.Fail($"Job with ID '{jobId}' not found in registry.");
            }

            // 前提となるMagicが発見されているかチェック
            foreach (var requiredMagicId in job.RequiredMagicIds)
            {
                if (!currentState.DiscoveredMagicIds.Contains(requiredMagicId))
                {
                    return ValidationResult.Fail($"Required Magic '{_magicSystem.GetMagicData(requiredMagicId)?.Name ?? requiredMagicId}' for Job '{job.Name}' is not discovered.");
                }
            }

            // 未割り当て人口のチェック (ここでは簡易的に総人口から引く)
            var totalPopulation = currentState.Population.Values.Sum();
            var assignedPopulation = currentState.ActiveJobAssignments.Values.Sum();
            var unassignedPopulation = totalPopulation - assignedPopulation;

            if (unassignedPopulation < populationCount)
            {
                return ValidationResult.Fail($"Not enough unassigned population to assign {populationCount} to Job '{job.Name}'. Available: {unassignedPopulation}.");
            }

            // ジョブ割り当てを状態に反映
            currentState.ActiveJobAssignments.TryAdd(jobId, 0);
            currentState.ActiveJobAssignments[jobId] += populationCount;

            return ValidationResult.Success().AddMessage($"{populationCount} people assigned to Job '{job.Name}' (ID: {jobId}).");
        }

        /// <summary>
        /// ジョブの割り当てを解除します。
        /// </summary>
        /// <param name="jobId">解除するジョブのID。</param>
        /// <param name="populationCount">解除する人口数。</param>
        /// <param name="currentState">現在のシミュレーション状態。</param>
        /// <returns>解除が成功したかどうかのValidationResult。</returns>
        public ValidationResult UnassignJob(string jobId, int populationCount, SimulationState currentState)
        {
            // Safe-Fail: 引数チェック
            if (string.IsNullOrWhiteSpace(jobId)) return ValidationResult.Fail("Job ID cannot be null or empty.");
            if (populationCount <= 0) return ValidationResult.Fail("Population count must be positive for unassignment.");
            if (currentState == null) throw new ArgumentNullException(nameof(currentState));

            if (!currentState.ActiveJobAssignments.ContainsKey(jobId))
            {
                return ValidationResult.Fail($"No population assigned to Job with ID '{jobId}'.");
            }

            if (currentState.ActiveJobAssignments[jobId] < populationCount)
            {
                return ValidationResult.Fail($"Cannot unassign {populationCount} from Job '{jobId}'. Only {currentState.ActiveJobAssignments[jobId]} are currently assigned.");
            }

            currentState.ActiveJobAssignments[jobId] -= populationCount;
            if (currentState.ActiveJobAssignments[jobId] == 0)
            {
                currentState.ActiveJobAssignments.Remove(jobId);
            }

            return ValidationResult.Success().AddMessage($"{populationCount} people unassigned from Job '{jobId}'.");
        }

        /// <summary>
        /// 現在のジョブ割り当てに基づいて、資源の生産と消費を計算し、状態を更新します。
        /// </summary>
        /// <param name="currentState">現在のシミュレーション状態。</param>
        public void UpdateJobEffects(SimulationState currentState)
        {
            // Safe-Fail: 引数チェック
            if (currentState == null) throw new ArgumentNullException(nameof(currentState));

            foreach (var assignment in currentState.ActiveJobAssignments)
            {
                if (_allJobData.TryGetValue(assignment.Key, out var job))
                {
                    int assignedCount = assignment.Value;
                    // 生産
                    foreach (var production in job.ProductionRates)
                    {
                        currentState.Resources.TryAdd(production.Key, 0);
                        currentState.Resources[production.Key] += production.Value * assignedCount * job.SkillMultiplier;
                    }
                    // 消費
                    foreach (var consumption in job.ConsumptionRates)
                    {
                        currentState.Resources.TryAdd(consumption.Key, 0);
                        currentState.Resources[consumption.Key] -= consumption.Value * assignedCount;
                        // 資源は0未満にならないようにする
                        if (currentState.Resources[consumption.Key] < 0) currentState.Resources[consumption.Key] = 0;
                    }
                }
                else
                {
                    Console.WriteLine($"Warning: Job data for ID '{assignment.Key}' not found during update. Skipping effects.");
                }
            }
        }

        /// <summary>
        /// 指定されたJob IDのJobDataを取得します。
        /// </summary>
        /// <param name="jobId">JobのID。</param>
        /// <returns>対応するJobData、見つからない場合はnull。</returns>
        public JobData GetJobData(string jobId)
        {
            // Safe-Fail: 引数チェック
            if (string.IsNullOrWhiteSpace(jobId)) return null;
            _allJobData.TryGetValue(jobId, out var job);
            return job;
        }
    }
}
```

### 3.5. `PruningTheoryEngine`
T1050-T1250期間の剪定理論ロジックを実装します。

```csharp
// FromLikeCombatBlindHeatScienceCraftSimulator/Systems/PruningTheoryEngine.cs
using FromLikeCombatBlindHeatScienceCraftSimulator.Data;
using FromLikeCombatBlindHeatScienceCraftSimulator.Utility;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FromLikeCombatBlindHeatScienceCraftSimulator.Systems
{
    /// <summary>
    /// シミュレーションの特定の期間において、剪定理論に基づき最も有望な未来のルートを特定し、
    /// シミュレーションの進行をそのルートにコミットするエンジン。
    /// T1050-T1250の期間に特化して動作します。
    /// </summary>
    public class PruningTheoryEngine
    {
        private readonly MagicSystem _magicSystem;
        private readonly JobSystem _jobSystem;
        private readonly Random _random;

        // 剪定理論がアクティブになる期間
        private const long PruningStartTime = 1050;
        private const long PruningEndTime = 1250;
        private const int LookAheadDuration = 50; // 各剪定サイクルでシミュレートする未来の期間（年）
        private const int MaxPathsToGenerate = 5; // 生成する潜在的なパスの最大数

        public PruningTheoryEngine(MagicSystem magicSystem, JobSystem jobSystem)
        {
            _magicSystem = magicSystem ?? throw new ArgumentNullException(nameof(magicSystem));
            _jobSystem = jobSystem ?? throw new ArgumentNullException(nameof(jobSystem));
            _random = new Random();
        }

        /// <summary>
        /// 現在のシミュレーション時間と状態に基づいて、最も「太いルート」を特定し、
        /// シミュレーションの進行をそのルートにコミットします。
        /// このメソッドはT1050-T1250の期間にのみ有効です。
        /// </summary>
        /// <param name="currentTime">現在のシミュレーション時間。</param>
        /// <param name="currentState">現在のシミュレーション状態。</param>
        public void CommitPrunedRoute(long currentTime, SimulationState currentState)
        {
            // Safe-Fail: 引数チェック
            if (currentState == null) throw new ArgumentNullException(nameof(currentState));

            // 指定された期間外であれば何もしない
            if (currentTime < PruningStartTime || currentTime > PruningEndTime)
            {
                Console.WriteLine($"Info: PruningTheoryEngine.CommitPrunedRoute called outside T{PruningStartTime}-T{PruningEndTime} window at T{currentTime}. Skipping.");
                return;
            }

            Console.WriteLine($"PruningTheoryEngine: Activating pruning at T{currentTime}...");

            // Step 1: 複数の潜在的な未来のパスを生成
            List<PruningPath> potentialPaths = GeneratePotentialPaths(currentTime, currentState, LookAheadDuration);

            // Safe-Fail: パスが生成されなかった場合
            if (!potentialPaths.Any())
            {
                Console.WriteLine($"Error: No potential paths generated for pruning at T{currentTime}. Simulation might proceed randomly or stall.");
                return;
            }

            // Step 2: パスを評価し、最もスコアの高い「太いルート」を選択
            PruningPath bestPath = SelectBestPath(potentialPaths);

            // Safe-Fail: 最適なパスが選択されなかった場合
            if (bestPath == null)
            {
                Console.WriteLine($"Error: Failed to select a best path for pruning at T{currentTime}. Proceeding with a default (first available) path.");
                bestPath = potentialPaths.First(); // フォールバックとして最初のパスを選択
            }

            // Step 3: 選択されたパスにシミュレーションをコミット
            ApplyPrunedPath(bestPath, currentState);

            Console.WriteLine($"PruningTheoryEngine: Committed to path '{bestPath.PathId}' at T{currentTime} with score {bestPath.Score}.");
        }

        /// <summary>
        /// 現在の状態から、複数の潜在的な未来のパスをシミュレートして生成します。
        /// </summary>
        /// <param name="currentTime">現在のシミュレーション時間。</param>
        /// <param name="initialState">現在のシミュレーション状態。</param>
        /// <param name="lookAheadDuration">未来をシミュレートする期間（年）。</param>
        /// <returns>生成された潜在的なパスのリスト。</returns>
        private List<PruningPath> GeneratePotentialPaths(long currentTime, SimulationState initialState, int lookAheadDuration)
        {
            var paths = new List<PruningPath>();
            var availableMagic = _magicSystem.GetMagicData(null); // 全てのMagicDataを取得する仮のメソッド

            // 複数の異なる意思決定シナリオを探索
            for (int i = 0; i < MaxPathsToGenerate; i++)
            {
                var pathId = $"Path_{currentTime}_{i}";
                var simulatedState = initialState.DeepCopy();
                var futureEvents = new List<SimulationEvent>();

                // 各パスで異なる戦略を試行
                // 例: 1. 農業技術に注力, 2. 冶金技術に注力, 3. 軍事技術に注力, 4. 探索に注力, 5. ランダム
                string strategy = GetStrategyForPath(i);
                Console.WriteLine($"  Generating path {pathId} with strategy: {strategy}");

                // 短期間のシミュレーションを実行
                for (long t = currentTime; t < currentTime + lookAheadDuration; t++)
                {
                    // この内部シミュレーションは簡略化されたもの
                    // - 資源の自然増減
                    // - 人口の自然増減
                    // - 潜在的なMagicの発見試行
                    // - ジョブ割り当ての調整

                    // 1. 資源の更新 (簡易版)
                    foreach (var resKey in simulatedState.Resources.Keys.ToList())
                    {
                        simulatedState.Resources[resKey] += _random.NextSingle() * 10 - 5; // ランダムな変動
                        if (simulatedState.Resources[resKey] < 0) simulatedState.Resources[resKey] = 0;
                    }

                    // 2. Magicの発見試行 (戦略に基づいて優先順位付け)
                    var potentialMagicToDiscover = _magicSystem.GetMagicData(null) // 全てのMagicDataを取得する仮のメソッド
                                                               .Where(m => !simulatedState.DiscoveredMagicIds.Contains(m.Id) && _magicSystem.CheckMagicPrerequisites(m, simulatedState))
                                                               .OrderByDescending(m => ScoreMagicForStrategy(m, strategy)) // 戦略に応じたスコアリング
                                                               .FirstOrDefault();

                    if (potentialMagicToDiscover != null && _random.NextDouble() < 0.1) // 10%の確率で発見
                    {
                        var discoverResult = _magicSystem.DiscoverMagic(potentialMagicToDiscover.Id, simulatedState);
                        if (discoverResult.IsSuccess)
                        {
                            futureEvents.Add(new MagicDiscoveryEvent(t, $"Discovered {potentialMagicToDiscover.Name}", potentialMagicToDiscover.Id));
                            Console.WriteLine($"    Path {pathId}: Discovered Magic '{potentialMagicToDiscover.Name}' at T{t}.");
                        }
                    }

                    // 3. ジョブ割り当ての調整 (戦略に基づいて)
                    AdjustJobsForStrategy(simulatedState, strategy);
                    _jobSystem.UpdateJobEffects(simulatedState); // ジョブの効果を適用
                }

                paths.Add(new PruningPath(pathId, initialState, futureEvents, simulatedState));
            }

            return paths;
        }

        /// <summary>
        /// パスを評価し、最もスコアの高い「太いルート」を選択します。
        /// T1050-T1250の期間における「太いルート」の基準を適用します。
        /// </summary>
        /// <param name="paths">評価する潜在的なパスのリスト。</param>
        /// <returns>最もスコアの高いPruningPath。</returns>
        private PruningPath SelectBestPath(List<PruningPath> paths)
        {
            // Safe-Fail: 空のリストの場合
            if (!paths.Any()) return null;

            foreach (var path in paths)
            {
                path.Score = EvaluatePath(path);
                Console.WriteLine($"  Path '{path.PathId}' evaluated with score: {path.Score}");
            }

            return paths.OrderByDescending(p => p.Score).FirstOrDefault();
        }

        /// <summary>
        /// 特定のパスを評価し、スコアを計算します。
        /// 「太いルート」の基準: 社会基盤の安定、主要な社会技術の発見・普及、資源の効率的な利用、特定の脅威への対処、文化・知識の発展。
        /// </summary>
        /// <param name="path">評価するPruningPath。</param>
        /// <returns>パスの評価スコア。</returns>
        private float EvaluatePath(PruningPath path)
        {
            // Safe-Fail: 引数チェック
            if (path?.FinalState == null) return -1000f; // 無効なパスは低いスコア

            float score = 0;
            var finalState = path.FinalState;

            // 1. 社会基盤の安定 (人口増加、食料資源の安定供給)
            float totalPopulation = finalState.Population.Values.Sum();
            score += totalPopulation * 0.1f; // 人口が多いほど良い
            score += finalState.Resources.GetValueOrDefault("Food", 0) * 0.05f; // 食料が多いほど良い

            // 2. 主要な社会技術（Magic）の発見・普及
            // T1050-T1250の主要Magic: 農業革新、冶金技術、初期機械、航海術、印刷術など
            var keyMagicIds = new HashSet<string>
            {
                "AdvancedAgriculture", "IronSmelting", "WaterWheel", "CompassNavigation", "PrintingPress"
            };
            foreach (var magicId in keyMagicIds)
            {
                if (finalState.DiscoveredMagicIds.Contains(magicId))
                {
                    score += 100f; // 主要Magicの発見は高得点
                }
            }
            score += finalState.DiscoveredMagicIds.Count * 5f; // 発見Magicが多いほど良い

            // 3. 資源の効率的な利用 (特定の重要資源の量)
            score += finalState.Resources.GetValueOrDefault("Metal", 0) * 0.08f;
            score += finalState.Resources.GetValueOrDefault("Wood", 0) * 0.03f;
            score += finalState.Resources.GetValueOrDefault("Knowledge", 0) * 0.1f; // 知識も重要資源

            // 4. 特定の脅威への対処 (WorldParameterの安定度など)
            score += finalState.WorldParameters.GetValueOrDefault("Stability", 0) * 2f; // 安定度が高いほど良い
            score -= finalState.WorldParameters.GetValueOrDefault("Unrest", 0) * 5f; // 不安要素は減点

            // 5. 文化・知識の発展 (Knowledge資源、特定のMagic)
            // 上記のKnowledge資源とPrintingPressなどで既に評価済み

            // 負の資源や人口がないかチェック (ペナルティ)
            if (finalState.Resources.Any(r => r.Value < 0)) score -= 500f;
            if (finalState.Population.Any(p => p.Value < 0)) score -= 1000f;

            return score;
        }

        /// <summary>
        /// 選択されたパスに沿ってシミュレーションの状態を強制的に調整します。
        /// </summary>
        /// <param name="bestPath">コミットする最適なパス。</param>
        /// <param name="currentState">現在のシミュレーション状態（変更される）。</param>
        private void ApplyPrunedPath(PruningPath bestPath, SimulationState currentState)
        {
            // Safe-Fail: 引数チェック
            if (bestPath == null) throw new ArgumentNullException(nameof(bestPath));
            if (currentState == null) throw new ArgumentNullException(nameof(currentState));

            Console.WriteLine($"  Applying pruned path '{bestPath.PathId}' to current state...");

            // 1. 主要なMagicの発見を強制
            foreach (var futureEvent in bestPath.FutureEvents.OfType<MagicDiscoveryEvent>())
            {
                if (!currentState.DiscoveredMagicIds.Contains(futureEvent.MagicId))
                {
                    var result = _magicSystem.DiscoverMagic(futureEvent.MagicId, currentState);
                    if (result.IsSuccess)
                    {
                        Console.WriteLine($"    Forced discovery of Magic '{_magicSystem.GetMagicData(futureEvent.MagicId)?.Name ?? futureEvent.MagicId}'.");
                    }
                    else
                    {
                        Console.WriteLine($"    Warning: Failed to force discovery of Magic '{futureEvent.MagicId}': {result.FullErrorMessage}");
                    }
                }
            }

            // 2. ジョブ割り当てを誘導 (最終状態のジョブ割り当てに近づける)
            // ここでは簡易的に、最終状態のジョブ割り当てを目標として、現在の割り当てを調整
            var totalPopulation = currentState.Population.Values.Sum();
            var currentAssigned = currentState.ActiveJobAssignments.Values.Sum();
            var unassigned = totalPopulation - currentAssigned;

            foreach (var targetJobAssignment in bestPath.FinalState.ActiveJobAssignments)
            {
                var jobId = targetJobAssignment.Key;
                var targetCount = targetJobAssignment.Value;
                var currentCount = currentState.ActiveJobAssignments.GetValueOrDefault(jobId, 0);

                int delta = targetCount - currentCount;
                if (delta > 0) // 増やす場合
                {
                    int assignAmount = Math.Min(delta, unassigned);
                    if (assignAmount > 0)
                    {
                        var result = _jobSystem.AssignJob(jobId, assignAmount, currentState);
                        if (result.IsSuccess)
                        {
                            unassigned -= assignAmount;
                            Console.WriteLine($"    Forced assignment of {assignAmount} to Job '{_jobSystem.GetJobData(jobId)?.Name ?? jobId}'.");
                        }
                        else
                        {
                            Console.WriteLine($"    Warning: Failed to force assignment to Job '{jobId}': {result.FullErrorMessage}");
                        }
                    }
                }
                else if (delta < 0) // 減らす場合
                {
                    int unassignAmount = Math.Min(Math.Abs(delta), currentCount);
                    if (unassignAmount > 0)
                    {
                        var result = _jobSystem.UnassignJob(jobId, unassignAmount, currentState);
                        if (result.IsSuccess)
                        {
                            unassigned += unassignAmount;
                            Console.WriteLine($"    Forced unassignment of {unassignAmount} from Job '{_jobSystem.GetJobData(jobId)?.Name ?? jobId}'.");
                        }
                        else
                        {
                            Console.WriteLine($"    Warning: Failed to force unassignment from Job '{jobId}': {result.FullErrorMessage}");
                        }
                    }
                }
            }

            // 3. 資源の傾向を調整 (最終状態の資源量に近づける)
            foreach (var targetResource in bestPath.FinalState.Resources)
            {
                currentState.Resources.TryAdd(targetResource.Key, 0);
                // 現在の資源と目標資源の差分を徐々に埋めるように調整
                float current = currentState.Resources[targetResource.Key];
                float target = targetResource.Value;
                float adjustment = (target - current) * 0.1f; // 10%ずつ近づける
                currentState.Resources[targetResource.Key] += adjustment;
                if (currentState.Resources[targetResource.Key] < 0) currentState.Resources[targetResource.Key] = 0;
                Console.WriteLine($"    Adjusted resource '{targetResource.Key}' by {adjustment:F2}. New value: {currentState.Resources[targetResource.Key]:F2}");
            }

            // 4. WorldParametersの調整
            foreach (var targetParam in bestPath.FinalState.WorldParameters)
            {
                currentState.WorldParameters.TryAdd(targetParam.Key, 0);
                float current = currentState.WorldParameters[targetParam.Key];
                float target = targetParam.Value;
                float adjustment = (target - current) * 0.1f;
                currentState.WorldParameters[targetParam.Key] += adjustment;
                Console.WriteLine($"    Adjusted WorldParameter '{targetParam.Key}' by {adjustment:F2}. New value: {currentState.WorldParameters[targetParam.Key]:F2}");
            }

            // その他の状態調整もここに追加
        }

        /// <summary>
        /// パス生成のための戦略を決定します。
        /// </summary>
        private string GetStrategyForPath(int pathIndex)
        {
            return pathIndex switch
            {
                0 => "FocusAgriculture",
                1 => "FocusMetallurgy",
                2 => "FocusMilitary",
                3 => "FocusExploration",
                _ => "RandomDevelopment",
            };
        }

        /// <summary>
        /// 特定の戦略に基づいてMagicのスコアを計算します。
        /// </summary>
        private float ScoreMagicForStrategy(MagicData magic, string strategy)
        {
            float score = 0;
            switch (strategy)
            {
                case "FocusAgriculture":
                    if (magic.Category == "Agriculture") score += 10;
                    if (magic.Effects.ContainsKey("Resource_Food")) score += magic.Effects["Resource_Food"] * 0.5f;
                    break;
                case "FocusMetallurgy":
                    if (magic.Category == "Metallurgy") score += 10;
                    if (magic.Effects.ContainsKey("Resource_Metal")) score += magic.Effects["Resource_Metal"] * 0.5f;
                    break;
                case "FocusMilitary":
                    if (magic.Category == "Military") score += 10;
                    if (magic.Effects.ContainsKey("WorldParameter_Stability")) score += magic.Effects["WorldParameter_Stability"] * 0.5f;
                    break;
                case "FocusExploration":
                    if (magic.Category == "Navigation" || magic.Category == "Cartography") score += 10;
                    if (magic.Effects.ContainsKey("WorldParameter_Knowledge")) score += magic.Effects["WorldParameter_Knowledge"] * 0.5f;
                    break;
                case "RandomDevelopment":
                default:
                    score = _random.NextSingle() * 5; // ランダムな優先度
                    break;
            }
            return score;
        }

        /// <summary>
        /// 特定の戦略に基づいてジョブ割り当てを調整します。
        /// </summary>
        private void AdjustJobsForStrategy(SimulationState simulatedState, string strategy)
        {
            // ここでは簡略化のため、特定のジョブカテゴリの人口を増やす/減らす
            var totalPopulation = simulatedState.Population.Values.Sum();
            if (totalPopulation == 0) return;

            // 未割り当て人口を計算
            var assignedPopulation = simulatedState.ActiveJobAssignments.Values.Sum();
            var unassignedPopulation = totalPopulation - assignedPopulation;

            // 各戦略に応じたジョブ調整
            switch (strategy)
            {
                case "FocusAgriculture":
                    AdjustJobCategory(simulatedState, "Farmer", unassignedPopulation, 0.5f); // 未割り当ての50%を農民に
                    break;
                case "FocusMetallurgy":
                    AdjustJobCategory(simulatedState, "Miner", unassignedPopulation, 0.3f);
                    AdjustJobCategory(simulatedState, "Blacksmith", unassignedPopulation, 0.2f);
                    break;
                case "FocusMilitary":
                    AdjustJobCategory(simulatedState, "Soldier", unassignedPopulation, 0.4f);
                    break;
                case "FocusExploration":
                    AdjustJobCategory(simulatedState, "Explorer", unassignedPopulation, 0.2f);
                    AdjustJobCategory(simulatedState, "Sailor", unassignedPopulation, 0.2f);
                    break;
                case "RandomDevelopment":
                default:
                    // ランダムにジョブを割り当てる
                    var allJobIds = _jobSystem.GetJobData(null).Select(j => j.Id).ToList(); // 全てのJobDataを取得する仮のメソッド
                    if (allJobIds.Any() && unassignedPopulation > 0)
                    {
                        var randomJobId = allJobIds[_random.Next(allJobIds.Count)];
                        int assignCount = _random.Next(1, Math.Min(unassignedPopulation, 10)); // 最大10人
                        _jobSystem.AssignJob(randomJobId, assignCount, simulatedState);
                    }
                    break;
            }
        }

        /// <summary>
        /// 特定のジョブカテゴリに人口を割り当てます。
        /// </summary>
        private void AdjustJobCategory(SimulationState simulatedState, string jobCategory, int availableUnassigned, float proportion)
        {
            var targetJob = _jobSystem.GetJobData(null) // 全てのJobDataを取得する仮のメソッド
                                      .FirstOrDefault(j => j.Category == jobCategory && j.RequiredMagicIds.All(mid => simulatedState.DiscoveredMagicIds.Contains(mid)));
            if (targetJob != null)
            {
                int assignCount = (int)(availableUnassigned * proportion);
                if (assignCount > 0)
                {
                    _jobSystem.AssignJob(targetJob.Id, assignCount, simulatedState);
                }
            }
        }
    }
}
```

### 3.6. `SimulationCore` (抜粋)
`PruningTheoryEngine`を統合するシミュレーションのメインループの抜粋。

```csharp
// FromLikeCombatBlindHeatScienceCraftSimulator/Core/SimulationCore.cs (抜粋)
using FromLikeCombatBlindHeatScienceCraftSimulator.Data;
using FromLikeCombatBlindHeatScienceCraftSimulator.Engines;
using FromLikeCombatBlindHeatScienceCraftSimulator.Systems;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FromLikeCombatBlindHeatScienceCraftSimulator.Core
{
    /// <summary>
    /// シミュレーションのコアロジックを管理するクラス。
    /// </summary>
    public class SimulationCore
    {
        private SimulationState _currentState;
        private readonly MagicSystem _magicSystem;
        private readonly JobSystem _jobSystem;
        private readonly PruningTheoryEngine _pruningEngine;
        private readonly long _tickInterval = 1; // 1年ごとの更新と仮定

        public SimulationCore(SimulationState initialState, IEnumerable<MagicData> allMagic, IEnumerable<JobData> allJobs)
        {
            _currentState = initialState ?? throw new ArgumentNullException(nameof(initialState));

            // エンジンとシステムを初期化
            var magicSanitizer = new MagicSanitizerEngine();
            _magicSystem = new MagicSystem(magicSanitizer, allMagic);
            _jobSystem = new JobSystem(allJobs, _magicSystem);
            _pruningEngine = new PruningTheoryEngine(_magicSystem, _jobSystem);

            // 初期状態のMagicとJobの整合性チェックなど
        }

        /// <summary>
        /// シミュレーションを指定された期間実行します。
        /// </summary>
        /// <param name="startTime">シミュレーション開始時間（年）。</param>
        /// <param name="endTime">シミュレーション終了時間（年）。</param>
        public void RunSimulation(long startTime, long endTime)
        {
            // Safe-Fail: 引数チェック
            if (startTime < 0 || endTime < startTime)
            {
                throw new ArgumentOutOfRangeException("Invalid simulation time range.");
            }

            _currentState.CurrentTime = startTime;
            Console.WriteLine($"Simulation started from T{startTime} to T{endTime}.");

            for (long t = startTime; t <= endTime; t += _tickInterval)
            {
                _currentState.CurrentTime = t;
                Console.WriteLine($"--- Current Time: T{t} ---");

                // T1050からT1250の期間で剪定理論を適用
                if (t >= 1050 && t <= 1250)
                {
                    _pruningEngine.CommitPrunedRoute(t, _currentState);
                }

                // 通常のシミュレーション更新ロジック
                UpdateSimulationState(_tickInterval);

                // 状態のログ出力 (デバッグ用)
                LogCurrentState();
            }

            Console.WriteLine($"Simulation finished at T{endTime}.");
        }

        /// <summary>
        /// シミュレーション状態を1ティック分更新します。
        /// </summary>
        /// <param name="deltaTime">更新する時間量。</param>
        private void UpdateSimulationState(long deltaTime)
        {
            // Safe-Fail: 引数チェック
            if (deltaTime <= 0) throw new ArgumentOutOfRangeException(nameof(deltaTime), "Delta time must be positive.");

            // ジョブによる資源の生産と消費を更新
            _jobSystem.UpdateJobEffects(_currentState);

            // 人口の自然増減 (簡易版)
            var totalPopulation = _currentState.Population.Values.Sum();
            if (totalPopulation > 0 && _currentState.Resources.GetValueOrDefault("Food", 0) > totalPopulation * 0.5f)
            {
                _currentState.Population["General"] = (int)(_currentState.Population.GetValueOrDefault("General", 0) * 1.01f); // 1%増加
            }
            else if (totalPopulation > 0 && _currentState.Resources.GetValueOrDefault("Food", 0) < totalPopulation * 0.2f)
            {
                _currentState.Population["General"] = (int)(_currentState.Population.GetValueOrDefault("General", 0) * 0.99f); // 1%減少
            }
            if (_currentState.Population.GetValueOrDefault("General", 0) < 0) _currentState.Population["General"] = 0;


            // その他のシステム更新 (戦闘、クラフト、イベントなど)
            // ...
        }

        /// <summary>
        /// 現在のシミュレーション状態をコンソールにログ出力します。
        /// </summary>
        private void LogCurrentState()
        {
            Console.WriteLine($"  Resources: {string.Join(", ", _currentState.Resources.Select(kv => $"{kv.Key}: {kv.Value:F2}"))}");
            Console.WriteLine($"  Population: {string.Join(", ", _currentState.Population.Select(kv => $"{kv.Key}: {kv.Value}"))}");
            Console.WriteLine($"  Discovered Magic: {string.Join(", ", _currentState.DiscoveredMagicIds)}");
            Console.WriteLine($"  Active Jobs: {string.Join(", ", _currentState.ActiveJobAssignments.Select(kv => $"{_jobSystem.GetJobData(kv.Key)?.Name ?? kv.Key}: {kv.Value}"))}");
            Console.WriteLine($"  World Params: {string.Join(", ", _currentState.WorldParameters.Select(kv => $"{kv.Key}: {kv.Value:F2}"))}");
        }
    }
}
```

## 4. 使用例 (テストケースの示唆)

上記のコンポーネントを組み合わせてシミュレーションを実行する例です。

```csharp
// Program.cs (またはテストクラス)
using FromLikeCombatBlindHeatScienceCraftSimulator.Core;
using FromLikeCombatBlindHeatScienceCraftSimulator.Data;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FromLikeCombatBlindHeatScienceCraftSimulator
{
    public class Program
    {
        public static void Main(string[] args)
        {
            Console.WriteLine("Starting FromLikeCombatBlindHeatScienceCraftSimulator...");

            // 初期状態のMagicDataを準備
            var allMagic = new List<MagicData>
            {
                new MagicData("BasicAgriculture", "基礎農業", "基本的な耕作技術。", "Agriculture", new List<string>(), new Dictionary<string, float>(), new Dictionary<string, float> { { "Resource_Food", 100 }, { "WorldParameter_Stability", 0.1f } }, 10),
                new MagicData("AdvancedAgriculture", "高度農業", "輪作や灌漑による生産性向上。", "Agriculture", new List<string> { "BasicAgriculture" }, new Dictionary<string, float> { { "Resource_Wood", 50 } }, new Dictionary<string, float> { { "Resource_Food", 300 }, { "WorldParameter_Stability", 0.2f } }, 50),
                new MagicData("IronSmelting", "製鉄技術", "鉄の精錬を可能にする。", "Metallurgy", new List<string>(), new Dictionary<string, float> { { "Resource_Wood", 100 } }, new Dictionary<string, float> { { "Resource_Metal", 50 } }, 70),
                new MagicData("WaterWheel", "水車", "水力を使った動力源。", "Engineering", new List<string> { "IronSmelting" }, new Dictionary<string, float> { { "Resource_Wood", 200 }, { "Resource_Metal", 50 } }, new Dictionary<string, float> { { "Resource_ProductionEfficiency", 0.1f } }, 120),
                new MagicData("CompassNavigation", "羅針盤航海術", "遠洋航海を可能にする。", "Navigation", new List<string>(), new Dictionary<string, float> { { "Resource_Knowledge", 50 } }, new Dictionary<string, float> { { "WorldParameter_Exploration", 0.3f } }, 80),
                new MagicData("PrintingPress", "活版印刷術", "知識の普及を加速する。", "Culture", new List<string> { "IronSmelting", "BasicAgriculture" }, new Dictionary<string, float> { { "Resource_Wood", 150 }, { "Resource_Metal", 30 } }, new Dictionary<string, float> { { "Resource_Knowledge", 200 }, { "WorldParameter_Culture", 0.5f } }, 150),
                new MagicData("EarlyIndustrialMagic", "初期産業魔法", "蒸気機関の萌芽。", "Engineering", new List<string> { "WaterWheel", "IronSmelting" }, new Dictionary<string, float> { { "Resource_Metal", 300 }, { "Resource_Knowledge", 100 } }, new Dictionary<string, float> { { "Resource_ProductionEfficiency", 0.3f }, { "WorldParameter_Unrest", 0.1f } }, 200)
            };

            // 初期状態のJobDataを準備
            var allJobs = new List<JobData>
            {
                new JobData("Farmer", "農民", "食料を生産する。", "Agriculture", new List<string> { "BasicAgriculture" }, new Dictionary<string, float> { { "Food", 10 } }, new Dictionary<string, float> { { "Food", 1 } }, 1.0f),
                new JobData("Miner", "鉱夫", "金属を採掘する。", "Mining", new List<string> { "IronSmelting" }, new Dictionary<string, float> { { "Metal", 5 } }, new Dictionary<string, float> { { "Food", 2 } }, 1.0f),
                new JobData("Blacksmith", "鍛冶屋", "金属製品を加工する。", "Craftsman", new List<string> { "IronSmelting" }, new Dictionary<string, float> { { "Tools", 2 } }, new Dictionary<string, float> { { "Metal", 3 }, { "Food", 1 } }, 1.0f),
                new JobData("Soldier", "兵士", "防衛と攻撃を行う。", "Military", new List<string>(), new Dictionary<string, float> { { "WorldParameter_Stability", 0.01f } }, new Dictionary<string, float> { { "Food", 3 }, { "Tools", 0.5f } }, 1.0f),
                new JobData("Explorer", "探検家", "新たな土地や知識を発見する。", "Exploration", new List<string> { "CompassNavigation" }, new Dictionary<string, float> { { "Knowledge", 5 } }, new Dictionary<string, float> { { "Food", 2 } }, 1.0f)
            };

            // 初期シミュレーション状態
            var initialState = new SimulationState(
                currentTime: 1000,
                resources: new Dictionary<string, float>
                {
                    { "Food", 10000 },
                    { "Wood", 5000 },
                    { "Metal", 1000 },
                    { "Knowledge", 100 },
                    { "Tools", 200 }
                },
                population: new Dictionary<string, int>
                {
                    { "General", 1000 }
                },
                discoveredMagicIds: new HashSet<string>
                {
                    "BasicAgriculture" // 基礎農業は既に発見済み
                },
                activeJobAssignments: new Dictionary<string, int>
                {
                    { "Farmer", 500 },
                    { "Miner", 100 }
                },
                worldParameters: new Dictionary<string, float>
                {
                    { "Stability", 0.5f },
                    { "Unrest", 0.1f },
                    { "Culture", 0.2f },
                    { "Exploration", 0.0f }
                }
            );

            // シミュレーションコアを初期化して実行
            var simulator = new SimulationCore(initialState, allMagic, allJobs);
            simulator.RunSimulation(1000, 1300); // T1000からT1300まで実行し、T1050-T1250で剪定理論が発動する
        }
    }
}
```
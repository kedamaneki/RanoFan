はい、承知いたしました。
「フロム風戦闘×ブラインド熱科学クラフト×千年史自律シミュレーター」における「不遇枝エネルギー蓄積モデル」を適用した連続自律進行システムのC#実装指示プロンプトを、Safe-Fail構造、MagicSanitizerEngine、Job/Magicの定義規約を厳格に守り、Cursor(IDE)のCtrl+Lへそのまま読み込ませてC#コード化できるよう、精密なMarkdown形式で出力します。

---

# C#実装指示: 不遇枝エネルギー蓄積モデルと連続自律進行システム

## 目的
ゲームの自律進行システムにおいて、「不遇枝エネルギー蓄積モデル」を導入します。これは、特定の条件下でリソースが不足している「枝」（システムの一部、地域、技術ツリーの分岐、あるいは特定の文明など）が、その不遇な状況に応じて潜在的なエネルギーを蓄積し、最終的に特定の社会技術（Magic）を解放・適用できるメカニズムです。これにより、絶望的な状況からの逆転要素や、隠された力の解放を表現し、千年史のダイナミズムを創出します。

## 前提システム
以下の既存システムが利用可能であることを前提とします。

*   **`MagicType`**: 社会技術（Magic）の列挙型。
    ```csharp
    public enum MagicType // 社会技術 (Social Technology)
    {
        None,
        BasicInfrastructure,        // 基本インフラ
        AgrarianRevolution,         // 農業革命
        Industrialization,          // 産業化
        SocialWelfareProgram,       // 社会福祉プログラム
        LocalEmpowermentInitiative, // 地域活性化イニシアティブ（不遇枝モデルで解放される主要Magic）
        AdvancedAI,                 // 高度AI開発
        InterstellarTravel          // 星間航行
    }
    ```
*   **`JobType`**: 生活職業（Job）の列挙型。
    ```csharp
    public enum JobType // 生活職業 (Livelihood Profession)
    {
        None,
        Farmer,     // 農民
        Miner,      // 鉱夫
        Artisan,    // 職人
        Scholar,    // 学者
        Soldier,    // 兵士
        Administrator // 管理者
    }
    ```
*   **`MagicApplicationResult`**: `IMagicSanitizerEngine` の戻り値型。
    ```csharp
    using System.Collections.Generic;

    public class MagicApplicationResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public Dictionary<string, float> Effects { get; set; } = new Dictionary<string, float>(); // 適用されたMagicがもたらす効果
    }
    ```
*   **`IMagicSanitizerEngine`**: 社会技術（Magic）の適用と検証を司るエンジン。
    ```csharp
    using System;
    using System.Collections.Generic;

    public interface IMagicSanitizerEngine
    {
        /// <summary>
        /// 指定された社会技術を対象の枝に適用しようと試みる。
        /// Magicの適用条件を検証し、成功すれば効果を適用する。
        /// </summary>
        /// <param name="magic">適用する社会技術のタイプ。</param>
        /// <param name="targetBranch">適用対象の枝の状態。</param>
        /// <param name="powerMultiplier">Magicの強度を調整する倍率。</param>
        /// <returns>Magicの適用結果。</returns>
        MagicApplicationResult ApplyMagic(MagicType magic, BranchState targetBranch, float powerMultiplier = 1.0f);
    }
    ```
*   シミュレーションの基本単位は「ターン」とします。

## 新規コンポーネント設計

### 1. `BranchState` クラス
各「枝」の現在の状態を保持するデータクラス。不遇枝エネルギーモデルに関連する状態もここに含めます。

```csharp
// BranchState.cs
using System;
using System.Collections.Generic;
using System.Linq; // For string.Join in debug/logging

/// <summary>
/// シミュレーションにおける各「枝」（地域、文明、技術ツリーの分岐など）の現在の状態を保持するクラス。
/// 不遇枝エネルギーモデルに関連する状態も管理する。
/// </summary>
public class BranchState
{
    public string BranchId { get; private set; }
    public Dictionary<string, float> Resources { get; private set; } = new Dictionary<string, float>();
    public Dictionary<JobType, int> JobPopulations { get; private set; } = new Dictionary<JobType, int>();
    public HashSet<MagicType> AppliedMagics { get; private set; } = new HashSet<MagicType>();

    // 不遇枝エネルギーモデル関連の状態
    public bool IsUnderprivileged { get; private set; }
    public int UnderprivilegedDurationTurns { get; private set; } // 不遇状態が続いているターン数
    public float CurrentEnergy { get; private set; } // 蓄積されたエネルギー量

    /// <summary>
    /// 新しいBranchStateインスタンスを初期化します。
    /// </summary>
    /// <param name="id">枝を一意に識別するID。</param>
    /// <exception cref="ArgumentNullException">BranchIdがnullまたは空の場合にスローされます。</exception>
    public BranchState(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentNullException(nameof(id), "BranchId cannot be null or empty.");
        }
        BranchId = id;
    }

    /// <summary>
    /// 指定されたリソースの量を更新します。
    /// </summary>
    /// <param name="resourceName">リソースの名前。</param>
    /// <param name="amount">更新量（正の値で増加、負の値で減少）。</param>
    /// <exception cref="ArgumentNullException">resourceNameがnullまたは空の場合にスローされます。</exception>
    public void UpdateResource(string resourceName, float amount)
    {
        if (string.IsNullOrWhiteSpace(resourceName))
        {
            throw new ArgumentNullException(nameof(resourceName), "Resource name cannot be null or empty.");
        }

        if (!Resources.ContainsKey(resourceName))
        {
            Resources[resourceName] = 0f;
        }
        Resources[resourceName] += amount;
        if (Resources[resourceName] < 0)
        {
            Resources[resourceName] = 0; // リソースは0以下にならない
        }
    }

    /// <summary>
    /// 指定されたジョブタイプの人口を更新します。
    /// </summary>
    /// <param name="jobType">ジョブのタイプ。</param>
    /// <param name="count">更新数（正の値で増加、負の値で減少）。</param>
    public void UpdateJobPopulation(JobType jobType, int count)
    {
        if (!JobPopulations.ContainsKey(jobType))
        {
            JobPopulations[jobType] = 0;
        }
        JobPopulations[jobType] += count;
        if (JobPopulations[jobType] < 0)
        {
            JobPopulations[jobType] = 0; // 人口は0以下にならない
        }
    }

    /// <summary>
    /// 適用された社会技術を記録します。
    /// </summary>
    /// <param name="magic">適用された社会技術のタイプ。</param>
    public void AddAppliedMagic(MagicType magic)
    {
        AppliedMagics.Add(magic);
    }

    /// <summary>
    /// 枝の不遇状態を設定します。（内部利用）
    /// </summary>
    /// <param name="status">不遇状態であればtrue。</param>
    internal void SetUnderprivilegedStatus(bool status)
    {
        IsUnderprivileged = status;
    }

    /// <summary>
    /// 不遇状態が続いているターン数をインクリメントします。（内部利用）
    /// </summary>
    internal void IncrementUnderprivilegedDuration()
    {
        UnderprivilegedDurationTurns++;
    }

    /// <summary>
    /// 不遇状態の期間をリセットします。（内部利用）
    /// </summary>
    internal void ResetUnderprivilegedDuration()
    {
        UnderprivilegedDurationTurns = 0;
    }

    /// <summary>
    /// 枝にエネルギーを追加します。（内部利用）
    /// </summary>
    /// <param name="amount">追加するエネルギー量。</param>
    /// <exception cref="ArgumentOutOfRangeException">amountが負の場合にスローされます。</exception>
    internal void AddEnergy(float amount)
    {
        if (amount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), "Energy amount cannot be negative.");
        }
        CurrentEnergy += amount;
    }

    /// <summary>
    /// 枝からエネルギーを消費します。（内部利用）
    /// </summary>
    /// <param name="amount">消費するエネルギー量。</param>
    /// <exception cref="ArgumentOutOfRangeException">amountが負の場合にスローされます。</exception>
    internal void ConsumeEnergy(float amount)
    {
        if (amount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), "Energy amount cannot be negative.");
        }
        CurrentEnergy -= amount;
        if (CurrentEnergy < 0)
        {
            CurrentEnergy = 0; // エネルギーは0以下にならない
        }
    }

    /// <summary>
    /// 枝のエネルギーをリセットします。（内部利用）
    /// </summary>
    internal void ResetEnergy()
    {
        CurrentEnergy = 0;
    }

    public override string ToString()
    {
        var resourceStr = string.Join(", ", Resources.Select(kv => $"{kv.Key}: {kv.Value:F2}"));
        var jobStr = string.Join(", ", JobPopulations.Select(kv => $"{kv.Key}: {kv.Value}"));
        var magicStr = AppliedMagics.Any() ? string.Join(", ", AppliedMagics) : "None";
        return $"Branch ID: {BranchId}\n" +
               $"  Resources: {{{resourceStr}}}\n" +
               $"  Jobs: {{{jobStr}}}\n" +
               $"  Applied Magics: {{{magicStr}}}\n" +
               $"  Underprivileged: {IsUnderprivileged} (Duration: {UnderprivilegedDurationTurns} turns)\n" +
               $"  Current Energy: {CurrentEnergy:F2}";
    }
}
```

### 2. `UnderprivilegedBranchEnergyModel` クラス
不遇枝の判定、エネルギー蓄積、解放ロジックをカプセル化するクラス。

```csharp
// UnderprivilegedBranchEnergyModel.cs
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine; // Debug.Log のために仮定。純粋なC#では独自のロガーに置き換える。

/// <summary>
/// 不遇枝エネルギー蓄積モデルのロジックを管理するクラス。
/// 枝の不遇状態を判定し、エネルギーを蓄積させ、閾値に達したら社会技術を解放・適用する。
/// </summary>
public class UnderprivilegedBranchEnergyModel
{
    private readonly IMagicSanitizerEngine _magicSanitizerEngine;

    // 設定値（ゲームデザインに応じて調整可能なパラメータ）
    public float UnderprivilegedResourceThreshold { get; set; } = 100f; // 特定リソースがこの値を下回ると不遇とみなす閾値
    public int UnderprivilegedJobThreshold { get; set; } = 1; // 特定Jobの人口がこの値を下回ると不遇とみなす閾値
    public float EnergyAccumulationRatePerTurn { get; set; } = 5f; // 1ターンあたりの基本エネルギー蓄積量
    public float EnergyDurationBonusMultiplier { get; set; } = 0.1f; // 不遇期間が長いほど蓄積量が増える倍率
    public float EnergyReleaseThreshold { get; set; } = 1000f; // エネルギー解放に必要な閾値
    public MagicType EnergyReleaseMagic { get; set; } = MagicType.LocalEmpowermentInitiative; // エネルギー解放で適用されるMagic
    public float EnergyReleaseMagicPowerMultiplier { get; set; } = 1.5f; // 解放Magicのパワー倍率
    public float EnergyDecayRatePerTurn { get; set; } = 1f; // 不遇状態でない場合にエネルギーが減少する量

    /// <summary>
    /// UnderprivilegedBranchEnergyModelの新しいインスタンスを初期化します。
    /// </summary>
    /// <param name="magicSanitizerEngine">社会技術の適用と検証を行うエンジン。</param>
    /// <exception cref="ArgumentNullException">magicSanitizerEngineがnullの場合にスローされます。</exception>
    public UnderprivilegedBranchEnergyModel(IMagicSanitizerEngine magicSanitizerEngine)
    {
        _magicSanitizerEngine = magicSanitizerEngine ?? throw new ArgumentNullException(nameof(magicSanitizerEngine));
    }

    /// <summary>
    /// 指定された枝が不遇状態であるかを判定します。
    /// 不遇の定義は、特定リソースの不足、特定Job人口の不足、または特定の基礎Magicの未適用など。
    /// </summary>
    /// <param name="branch">判定対象の枝の状態。</param>
    /// <returns>枝が不遇状態であればtrue、そうでなければfalse。</returns>
    public bool IsBranchUnderprivileged(BranchState branch)
    {
        // Safe-Fail: nullチェック
        if (branch == null)
        {
            Debug.LogError("IsBranchUnderprivileged: BranchState cannot be null. Returning false.");
            return false;
        }

        // 1. 特定のリソースが閾値を下回っているか
        // 例: 「Food」リソースが不足している場合を不遇とみなす。
        // この条件は、ゲームデザインに応じて複数のリソースをチェックするように拡張可能。
        if (branch.Resources.TryGetValue("Food", out float foodLevel) && foodLevel < UnderprivilegedResourceThreshold)
        {
            return true;
        }

        // 2. 特定のJobの人口が閾値を下回っているか
        // 例: 「Farmer」人口が不足している場合を不遇とみなす。
        // この条件も、ゲームデザインに応じて複数のJobをチェックするように拡張可能。
        if (branch.JobPopulations.TryGetValue(JobType.Farmer, out int farmerPop) && farmerPop < UnderprivilegedJobThreshold)
        {
            return true;
        }

        // 3. 重要な基礎Magicがまだ適用されていないか
        // 例: 「BasicInfrastructure」が未適用の場合、その枝は発展途上であり不遇とみなす。
        if (!branch.AppliedMagics.Contains(MagicType.BasicInfrastructure))
        {
             // この条件は不遇の定義として強力すぎる場合があるので、ゲームデザインに応じて調整
             // return true;
        }

        return false; // 上記のどの条件にも当てはまらない場合は不遇ではない
    }

    /// <summary>
    /// 枝の状態を更新し、不遇状態であればエネルギーを蓄積、そうでなければエネルギーを減少させる。
    /// </summary>
    /// <param name="branch">更新対象の枝の状態。</param>
    public void UpdateBranchEnergy(BranchState branch)
    {
        // Safe-Fail: nullチェック
        if (branch == null)
        {
            Debug.LogError("UpdateBranchEnergy: BranchState cannot be null. Aborting update.");
            return;
        }

        bool wasUnderprivileged = branch.IsUnderprivileged;
        bool isCurrentlyUnderprivileged = IsBranchUnderprivileged(branch);

        branch.SetUnderprivilegedStatus(isCurrentlyUnderprivileged);

        if (isCurrentlyUnderprivileged)
        {
            branch.IncrementUnderprivilegedDuration();
            // 不遇状態が続くほど蓄積量が増えるボーナスロジック
            float accumulatedAmount = EnergyAccumulationRatePerTurn * (1 + branch.UnderprivilegedDurationTurns * EnergyDurationBonusMultiplier);
            branch.AddEnergy(accumulatedAmount);
            Debug.Log($"Branch '{branch.BranchId}' is underprivileged (Duration: {branch.UnderprivilegedDurationTurns} turns). Energy accumulated: {accumulatedAmount:F2}. Total: {branch.CurrentEnergy:F2}");
        }
        else
        {
            if (wasUnderprivileged)
            {
                Debug.Log($"Branch '{branch.BranchId}' is no longer underprivileged. Resetting duration.");
            }
            branch.ResetUnderprivilegedDuration();
            // 不遇状態でない場合、蓄積されたエネルギーは徐々に減少させる
            branch.ConsumeEnergy(EnergyDecayRatePerTurn);
            Debug.Log($"Branch '{branch.BranchId}' is not underprivileged. Energy decayed by {EnergyDecayRatePerTurn:F2}. Current: {branch.CurrentEnergy:F2}");
        }
    }

    /// <summary>
    /// 蓄積されたエネルギーが閾値を超えているかチェックし、超えていれば指定されたMagicを解放・適用する。
    /// Magic適用後、エネルギーは消費され、不遇状態は解消される。
    /// </summary>
    /// <param name="branch">チェック対象の枝の状態。</param>
    /// <returns>Magicが正常に適用された場合はtrue、そうでなければfalse。</returns>
    public bool TryReleaseEnergyAndApplyMagic(BranchState branch)
    {
        // Safe-Fail: nullチェック
        if (branch == null)
        {
            Debug.LogError("TryReleaseEnergyAndApplyMagic: BranchState cannot be null. Returning false.");
            return false;
        }

        if (branch.CurrentEnergy >= EnergyReleaseThreshold)
        {
            Debug.Log($"Branch '{branch.BranchId}' has accumulated enough energy ({branch.CurrentEnergy:F2}) to release! Attempting to apply Magic '{EnergyReleaseMagic}'.");

            // MagicSanitizerEngine を介して社会技術を適用
            MagicApplicationResult result = _magicSanitizerEngine.ApplyMagic(EnergyReleaseMagic, branch, EnergyReleaseMagicPowerMultiplier);

            if (result.Success)
            {
                branch.AddAppliedMagic(EnergyReleaseMagic); // 適用されたMagicを記録
                branch.ConsumeEnergy(EnergyReleaseThreshold); // エネルギーを消費
                branch.ResetUnderprivilegedDuration(); // 不遇状態の期間もリセット
                branch.SetUnderprivilegedStatus(false); // 不遇状態を解消
                Debug.Log($"Successfully applied Magic '{EnergyReleaseMagic}' to Branch '{branch.BranchId}'. Remaining energy: {branch.CurrentEnergy:F2}. Message: {result.Message}");
                
                // Magicの効果をBranchStateに反映
                foreach (var effect in result.Effects)
                {
                    branch.UpdateResource(effect.Key, effect.Value);
                    Debug.Log($"  Effect: {effect.Key} changed by {effect.Value:F2}");
                }
                return true;
            }
            else
            {
                Debug.LogWarning($"Failed to apply Magic '{EnergyReleaseMagic}' to Branch '{branch.BranchId}'. Reason: {result.Message}. Energy not consumed.");
                // 失敗した場合、エネルギーは消費しない（再試行の機会を与える）
                return false;
            }
        }
        return false; // エネルギーが閾値に達していない
    }
}
```

### 3. `SimulationManager` クラス
シミュレーションの自律進行を管理し、各枝のエネルギーモデルを更新する。

```csharp
// SimulationManager.cs
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine; // Debug.Log, WaitForSeconds のために仮定。純粋なC#では独自のロガーやTask.Delayに置き換える。
using System.Collections; // For IEnumerator

/// <summary>
/// シミュレーション全体の自律進行を管理するクラス。
/// 各枝の状態更新、不遇枝エネルギーモデルの適用、グローバルイベント処理などを担当する。
/// </summary>
public class SimulationManager
{
    private readonly IMagicSanitizerEngine _magicSanitizerEngine;
    private readonly UnderprivilegedBranchEnergyModel _energyModel;
    private readonly Dictionary<string, BranchState> _branches = new Dictionary<string, BranchState>();

    public int CurrentTurn { get; private set; } = 0;

    /// <summary>
    /// SimulationManagerの新しいインスタンスを初期化します。
    /// </summary>
    /// <param name="magicSanitizerEngine">社会技術の適用と検証を行うエンジン。</param>
    /// <exception cref="ArgumentNullException">magicSanitizerEngineがnullの場合にスローされます。</exception>
    public SimulationManager(IMagicSanitizerEngine magicSanitizerEngine)
    {
        _magicSanitizerEngine = magicSanitizerEngine ?? throw new ArgumentNullException(nameof(magicSanitizerEngine));
        _energyModel = new UnderprivilegedBranchEnergyModel(_magicSanitizerEngine);

        // 初期設定値の調整（必要に応じて外部設定ファイルなどからロード）
        _energyModel.UnderprivilegedResourceThreshold = 50f; // 食料50以下で不遇
        _energyModel.EnergyReleaseThreshold = 750f; // 750エネルギーで解放
        _energyModel.EnergyReleaseMagic = MagicType.LocalEmpowermentInitiative; // 地域活性化イニシアティブを解放
        _energyModel.EnergyAccumulationRatePerTurn = 10f; // 1ターンあたり10エネルギー蓄積
        _energyModel.EnergyDurationBonusMultiplier = 0.2f; // 不遇期間ボーナスを強化
        _energyModel.EnergyDecayRatePerTurn = 2f; // 不遇でない場合のエネルギー減少量
    }

    /// <summary>
    /// シミュレーションに新しい枝を追加します。
    /// </summary>
    /// <param name="branch">追加する枝の状態。</param>
    /// <exception cref="ArgumentNullException">branchがnullの場合にスローされます。</exception>
    public void AddBranch(BranchState branch)
    {
        if (branch == null)
        {
            Debug.LogError("AddBranch: BranchState cannot be null. Aborting addition.");
            return; // Safe-Fail
        }
        if (_branches.ContainsKey(branch.BranchId))
        {
            Debug.LogWarning($"Branch with ID '{branch.BranchId}' already exists. Skipping addition.");
            return;
        }
        _branches.Add(branch.BranchId, branch);
        Debug.Log($"Branch '{branch.BranchId}' added to simulation.");
    }

    /// <summary>
    /// シミュレーションを1ターン進行させます。
    /// 各枝の状態更新、不遇枝エネルギーモデルの適用、グローバルイベント処理を行います。
    /// </summary>
    public void AdvanceSimulationTurn()
    {
        CurrentTurn++;
        Debug.Log($"\n--- Simulation Turn {CurrentTurn} ---");

        // 各枝の状態を更新
        foreach (var branch in _branches.Values)
        {
            Debug.Log($"Processing Branch: {branch.BranchId}");

            // 1. 基本的な枝の進行ロジック（リソース生産・消費、人口変動など）
            // 例: 食料消費、生産活動、人口増加
            branch.UpdateResource("Food", -5f); // 毎ターン食料を消費
            branch.UpdateResource("Wealth", 10f); // 毎ターン富を生産
            if (branch.Resources.TryGetValue("Food", out float food) && food > 100)
            {
                branch.UpdateJobPopulation(JobType.Farmer, 1); // 食料が豊富なら農民が増える
            }

            // 2. 不遇枝エネルギーモデルの更新
            _energyModel.UpdateBranchEnergy(branch);

            // 3. エネルギー解放の試行
            _energyModel.TryReleaseEnergyAndApplyMagic(branch);

            // 4. その他の枝固有のイベントやロジック
            if (branch.Resources.TryGetValue("Food", out food) && food < 10)
            {
                Debug.LogWarning($"Branch '{branch.BranchId}' is experiencing critical food shortage!");
            }
        }

        // グローバルなイベントや相互作用の処理をここに記述
        // 例: GlobalMarket.UpdatePrices();
        // 例: CheckGlobalCatastrophes();
        // 例: 枝間の相互作用（貿易、紛争など）

        Debug.Log($"--- End of Turn {CurrentTurn} ---");
    }

    /// <summary>
    /// シミュレーションを自動で連続進行させるためのコルーチン。
    /// UnityのMonoBehaviourを想定しているため、純粋なC#環境では別途タイマーやTask.Delayを使用する必要がある。
    /// </summary>
    /// <param name="totalTurns">進行させる総ターン数。</param>
    /// <param name="delayBetweenTurnsSeconds">各ターン間の遅延時間（秒）。</param>
    public IEnumerator StartContinuousSimulation(int totalTurns, float delayBetweenTurnsSeconds)
    {
        if (totalTurns <= 0)
        {
            Debug.LogError($"StartContinuousSimulation: totalTurns must be positive. Received {totalTurns}.");
            yield break; // Safe-Fail
        }
        if (delayBetweenTurnsSeconds < 0)
        {
            Debug.LogError($"StartContinuousSimulation: delayBetweenTurnsSeconds cannot be negative. Received {delayBetweenTurnsSeconds}. Setting to 0.");
            delayBetweenTurnsSeconds = 0; // Safe-Fail
        }

        Debug.Log($"Starting continuous simulation for {totalTurns} turns with {delayBetweenTurnsSeconds:F2}s delay per turn.");
        for (int i = 0; i < totalTurns; i++)
        {
            AdvanceSimulationTurn();
            yield return new WaitForSeconds(delayBetweenTurnsSeconds); // Unity Coroutine の場合
            // 純粋なC# async/await の場合: await Task.Delay(TimeSpan.FromSeconds(delayBetweenTurnsSeconds));
        }
        Debug.Log("Continuous simulation finished.");
    }

    /// <summary>
    /// 特定の枝の状態を取得します。
    /// </summary>
    /// <param name="branchId">取得したい枝のID。</param>
    /// <returns>指定されたIDの枝の状態。見つからない場合はnull。</returns>
    public BranchState GetBranchState(string branchId)
    {
        if (string.IsNullOrWhiteSpace(branchId))
        {
            Debug.LogError("GetBranchState: BranchId cannot be null or empty. Returning null.");
            return null; // Safe-Fail
        }
        if (_branches.TryGetValue(branchId, out BranchState branch))
        {
            return branch;
        }
        Debug.LogWarning($"Branch with ID '{branchId}' not found. Returning null.");
        return null; // Safe-Fail
    }
}
```

### 4. `MockMagicSanitizerEngine` クラス
`IMagicSanitizerEngine` のモック実装。実際のゲームではより複雑なロジックが適用されます。

```csharp
// MockMagicSanitizerEngine.cs
using System;
using System.Collections.Generic;
using UnityEngine; // Debug.Log のために仮定。純粋なC#では独自のロガーに置き換える。

/// <summary>
/// IMagicSanitizerEngineのモック実装。
/// 社会技術の適用条件と効果を簡易的にシミュレートする。
/// </summary>
public class MockMagicSanitizerEngine : IMagicSanitizerEngine
{
    /// <summary>
    /// 指定された社会技術を対象の枝に適用しようと試みる。
    /// </summary>
    /// <param name="magic">適用する社会技術のタイプ。</param>
    /// <param name="targetBranch">適用対象の枝の状態。</param>
    /// <param name="powerMultiplier">Magicの強度を調整する倍率。</param>
    /// <returns>Magicの適用結果。</returns>
    public MagicApplicationResult ApplyMagic(MagicType magic, BranchState targetBranch, float powerMultiplier = 1.0f)
    {
        // Safe-Fail: nullチェック
        if (targetBranch == null)
        {
            Debug.LogError($"ApplyMagic: Target branch for Magic '{magic}' cannot be null.");
            return new MagicApplicationResult { Success = false, Message = "Target branch cannot be null." };
        }

        Debug.Log($"Attempting to apply Magic '{magic}' to Branch '{targetBranch.BranchId}' with power multiplier {powerMultiplier:F2}...");

        // ここにMagicの適用条件と効果の複雑なロジックを実装
        // 例: 特定のMagicは特定のJob人口が一定以上でないと適用できない、特定のMagicは一度しか適用できない、など
        switch (magic)
        {
            case MagicType.LocalEmpowermentInitiative:
                // 不遇枝モデルで解放される主要Magic
                // 効果: 食料生産を大幅に向上させ、住民の幸福度（リソースとして仮定）を上げる
                if (targetBranch.AppliedMagics.Contains(MagicType.LocalEmpowermentInitiative))
                {
                    return new MagicApplicationResult { Success = false, Message = "Local Empowerment Initiative already applied to this branch." };
                }
                var effectsLEI = new Dictionary<string, float>
                {
                    { "Food", 200f * powerMultiplier },
                    { "Happiness", 50f * powerMultiplier },
                    { "Stability", 10f * powerMultiplier }
                };
                return new MagicApplicationResult { Success = true, Message = "Local Empowerment Initiative successfully applied. Branch empowered!", Effects = effectsLEI };

            case MagicType.SocialWelfareProgram:
                // 例: 社会福祉プログラムは、ある程度の経済力（"Wealth"リソース）がないと適用が難しい
                if (!targetBranch.Resources.TryGetValue("Wealth", out float wealth) || wealth < 200f)
                {
                    return new MagicApplicationResult { Success = false, Message = "Insufficient wealth to implement Social Welfare Program (requires 200 Wealth)." };
                }
                if (targetBranch.AppliedMagics.Contains(MagicType.SocialWelfareProgram))
                {
                    return new MagicApplicationResult { Success = false, Message = "Social Welfare Program already enacted." };
                }
                var effectsSWP = new Dictionary<string, float>
                {
                    { "Happiness", 100f * powerMultiplier },
                    { "Stability", 20f * powerMultiplier },
                    { "Wealth", -50f * powerMultiplier } // 維持コストとして富を消費
                };
                return new MagicApplicationResult { Success = true, Message = "Social Welfare Program enacted, improving well-being.", Effects = effectsSWP };

            case MagicType.BasicInfrastructure:
                // 例: 基本インフラは常に適用可能で、生産性や安定性を上げる
                if (targetBranch.AppliedMagics.Contains(MagicType.BasicInfrastructure))
                {
                    return new MagicApplicationResult { Success = false, Message = "Basic Infrastructure already in place." };
                }
                var effectsBI = new Dictionary<string, float>
                {
                    { "Production", 100f * powerMultiplier },
                    { "Stability", 10f * powerMultiplier }
                };
                return new MagicApplicationResult { Success = true, Message = "Basic Infrastructure established, boosting productivity.", Effects = effectsBI };

            default:
                return new MagicApplicationResult { Success = false, Message = $"Magic '{magic}' is not yet implemented or cannot be applied under current conditions." };
        }
    }
}
```

## 実装上の注意点とSafe-Fail構造

*   **NULLチェック**: 全ての公開メソッドおよび重要な内部処理において、引数や参照が`null`でないことを確認します。`ArgumentNullException`を適切にスローするか、`Debug.LogError`でログを出し、安全なデフォルト値を返すか処理を中断します。
*   **境界チェック**: 数値の引数やコレクションのインデックスが有効な範囲内にあることを確認します。`ArgumentOutOfRangeException`を適切にスローします。
*   **ログ出力**: `Debug.Log`, `Debug.LogWarning`, `Debug.LogError` を使用して、システムの動作状況、警告、エラーを明確に記録します。これにより、デバッグと監視が容易になります。Unity環境以外で利用する場合は、独自のロガーに置き換えてください。
*   **設定値の外部化**: `UnderprivilegedBranchEnergyModel` の閾値やレートなどの設定値は、ゲームデザインに応じて調整されるべきであるため、コンストラクタ引数、プロパティ、または設定ファイルからのロードを通じて外部から設定可能にしてください。
*   **拡張性**: `IsBranchUnderprivileged` メソッドの不遇判定ロジックは、将来的に複数の条件を組み合わせたり、動的に条件を追加できるように拡張性を考慮してください。
*   **パフォーマンス**: 大量の枝を扱う場合、`SimulationManager` の `AdvanceSimulationTurn` メソッド内のループ処理のパフォーマンスに注意してください。必要に応じて最適化を検討してください（例: 並列処理、データ構造の最適化）。
*   **依存性の注入**: `SimulationManager` や `UnderprivilegedBranchEnergyModel` は `IMagicSanitizerEngine` に依存するため、コンストラクタインジェクションを用いて依存性を注入します。これにより、テスト容易性と疎結合を保ちます。
*   **Unity依存**: `UnityEngine.Debug.Log` や `UnityEngine.WaitForSeconds` はUnity環境に依存します。純粋なC#環境で実行する場合は、それぞれ標準の `Console.WriteLine` や `System.Threading.Tasks.Task.Delay` などに置き換える必要があります。`SimulationManager.StartContinuousSimulation` メソッドは `IEnumerator` を返すため、Unityのコルーチンとして利用できます。

## 使用例（Main/Entry Point）

以下のコードは、Unityの`MonoBehaviour`を継承したクラスを想定していますが、`Start`メソッド内のロジックは、純粋なC#アプリケーションのエントリポイント（`Main`メソッドなど）に移植可能です。

```csharp
// GameInitializer.cs (UnityのMonoBehaviourを想定)
using UnityEngine;
using System.Collections; // For IEnumerator

/// <summary>
/// ゲームの初期化とシミュレーションの開始を管理するクラス。
/// </summary>
public class GameInitializer : MonoBehaviour
{
    [Header("Simulation Settings")]
    [SerializeField] private int totalSimulationTurns = 100;
    [SerializeField] private float delayBetweenTurnsSeconds = 0.1f;

    void Start()
    {
        Debug.Log("Game Initialization Started.");

        // 1. MagicSanitizerEngine のインスタンスを作成
        // 実際のゲームでは、より複雑なロジックを持つMagicSanitizerEngineを注入します。
        IMagicSanitizerEngine magicEngine = new MockMagicSanitizerEngine();

        // 2. SimulationManager のインスタンスを作成
        SimulationManager simManager = new SimulationManager(magicEngine);

        // 3. 枝（BranchState）をいくつか作成し、初期状態を設定
        // Branch A: 不遇状態になりやすい設定
        BranchState branchA = new BranchState("Branch_A");
        branchA.UpdateResource("Food", 40f); // 不遇閾値(50f)を下回るように設定
        branchA.UpdateResource("Wealth", 80f);
        branchA.UpdateJobPopulation(JobType.Farmer, 0); // 不遇閾値(1)を下回るように設定
        branchA.UpdateJobPopulation(JobType.Artisan, 5);
        simManager.AddBranch(branchA);
        Debug.Log($"Initial State of Branch_A:\n{branchA}");

        // Branch B: 比較的裕福な状態
        BranchState branchB = new BranchState("Branch_B");
        branchB.UpdateResource("Food", 250f);
        branchB.UpdateResource("Wealth", 600f);
        branchB.UpdateJobPopulation(JobType.Farmer, 15);
        branchB.UpdateJobPopulation(JobType.Scholar, 3);
        simManager.AddBranch(branchB);
        Debug.Log($"Initial State of Branch_B:\n{branchB}");

        // Branch C: 別の不遇状態になりやすい設定（食料は足りているが、Jobが少ない）
        BranchState branchC = new BranchState("Branch_C");
        branchC.UpdateResource("Food", 120f);
        branchC.UpdateResource("Wealth", 150f);
        branchC.UpdateJobPopulation(JobType.Farmer, 5);
        branchC.UpdateJobPopulation(JobType.Miner, 0); // Minerが不足している場合を想定
        simManager.AddBranch(branchC);
        Debug.Log($"Initial State of Branch_C:\n{branchC}");


        // 4. シミュレーションを連続進行させる
        // Unityのコルーチンとして実行
        StartCoroutine(simManager.StartContinuousSimulation(totalSimulationTurns, delayBetweenTurnsSeconds));

        Debug.Log("Game Initialization Completed. Simulation Running...");
    }

    // シミュレーション終了後の最終状態確認（例）
    void OnDestroy()
    {
        // シミュレーションマネージャーのインスタンスが破棄される前に最終状態をログに出すなど
        // この例ではStartCoroutineで実行しているため、終了はコルーチン内で確認するのが適切
    }
}
```
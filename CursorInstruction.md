はい、承知いたしました。
「フロム風戦闘×ブラインド熱科学クラフト×千年史自律シミュレーター」のリードディレクター兼C#設計者として、1001年以降の文明復興フェーズの動作検証を行うための、Cursor (IDE) の Ctrl+L へそのまま読み込ませてC#コード化できる「精密な実装指示プロンプト」をMarkdown形式で出力します。

---

# C#実装指示プロンプト: 文明復興フェーズ (1001年以降) 動作検証

## 目的
1001年以降の文明復興フェーズにおける、人口増加、資源生産、社会技術（Magic）の発見と適用、生活職業（Job）の創出と割り当て、文明レベルの向上といった主要なメカニズムをシミュレートし、その動作を検証するためのC#コードを実装します。

## コア原則
以下の原則を厳格に守ってください。

1.  **Safe-Fail構造**: 全ての公開メソッドは、無効な入力（null引数、範囲外の値など）に対して堅牢であり、予期せぬエラーが発生した場合でもシミュレーション全体がクラッシュせず、適切なエラーログを出力し、安全な状態を維持するか、明確な失敗を示す結果を返します。
2.  **MagicSanitizerEngine**: 社会技術（Magic）の発見・適用プロセスは、必ず`IMagicSanitizerEngine`を介して検証され、その社会に適用可能か、矛盾がないかなどがチェックされます。
3.  **Job/Magicの定義規約**:
    *   **Magic (魔法)**: 社会技術、統治システム、科学的発見など、文明の進歩を促す抽象的な概念を指します。（例: 農業技術、冶金術、教育制度、民主主義）
    *   **Job (ジョブ)**: 生活職業、役割、専門分野など、具体的な労働や活動を指します。（例: 農民、鍛冶屋、学者、兵士、統治者）

## 実装指示

### 1. 共通インターフェースとユーティリティ

#### `ILogger.cs`
シミュレーション全体のログ出力に使用するインターフェースを定義します。

```csharp
// ILogger.cs
using System;

namespace GameSimulation.Core
{
    public interface ILogger
    {
        void LogInfo(string message);
        void LogWarning(string message);
        void LogError(string message, Exception? exception = null);
        void LogDebug(string message);
    }

    // 簡易的なコンソールロガーの実装（開発用）
    public class ConsoleLogger : ILogger
    {
        public void LogInfo(string message) => Console.WriteLine($"[INFO] {message}");
        public void LogWarning(string message) => Console.WriteLine($"[WARN] {message}");
        public void LogError(string message, Exception? exception = null)
        {
            Console.Error.WriteLine($"[ERROR] {message}");
            if (exception != null) Console.Error.WriteLine($"  Exception: {exception}");
        }
        public void LogDebug(string message) => Console.WriteLine($"[DEBUG] {message}");
    }
}
```

### 2. データ構造の定義

#### `Enums.cs`
シミュレーションで使用する列挙型を定義します。

```csharp
// Enums.cs
namespace GameSimulation.Core
{
    public enum ResourceType
    {
        Food,
        Wood,
        Stone,
        MetalOre,
        Tools,
        Knowledge, // 知識ポイント
        LuxuryGoods,
        // 必要に応じて追加
    }

    public enum MagicType // 社会技術のカテゴリ
    {
        Agricultural,
        Metallurgical,
        Governance,
        Educational,
        Military,
        Spiritual,
        Economic,
        // 必要に応じて追加
    }
}
```

#### `Job.cs`
生活職業（Job）の定義です。

```csharp
// Job.cs
using System.Collections.Generic;
using System.Collections.Immutable;

namespace GameSimulation.Core
{
    /// <summary>
    /// 生活職業（Job）の定義。
    /// </summary>
    public record Job
    {
        public string Id { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;

        /// <summary>
        /// このJobをアンロックするために必要なMagicのIDリスト。
        /// </summary>
        public ImmutableHashSet<string> RequiredMagicIds { get; init; } = ImmutableHashSet<string>.Empty;

        /// <summary>
        /// このJobが生産する資源と量。
        /// </summary>
        public ImmutableDictionary<ResourceType, float> OutputResourcesPerWorker { get; init; } = ImmutableDictionary<ResourceType, float>.Empty;

        /// <summary>
        /// このJobが消費する資源と量。
        /// </summary>
        public ImmutableDictionary<ResourceType, float> InputResourcesPerWorker { get; init; } = ImmutableDictionary<ResourceType, float>.Empty;

        /// <summary>
        /// このJobの基本生産性乗数。
        /// </summary>
        public float BaseProductivityMultiplier { get; init; } = 1.0f;

        // Safe-Fail: コンストラクタでの初期化時にIdとNameが空でないことを保証する
        public Job(string id, string name)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Job Id cannot be null or whitespace.", nameof(id));
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Job Name cannot be null or whitespace.", nameof(name));
            Id = id;
            Name = name;
        }

        // レコードのプライマリコンストラクタを使用する場合の例
        public Job() { } // デフォルトコンストラクタも提供
    }

    // プリセットJobの例
    public static class PresetJobs
    {
        public static readonly Job Farmer = new Job("JOB_FARMER", "農民")
        {
            Description = "食料を生産する基本的な職業。",
            OutputResourcesPerWorker = ImmutableDictionary.CreateRange(new[] {
                KeyValuePair.Create(ResourceType.Food, 10.0f)
            }),
            InputResourcesPerWorker = ImmutableDictionary.CreateRange(new[] {
                KeyValuePair.Create(ResourceType.Tools, 0.1f) // ツールを少し消費
            })
        };

        public static readonly Job Lumberjack = new Job("JOB_LUMBERJACK", "木こり")
        {
            Description = "木材を生産する職業。",
            OutputResourcesPerWorker = ImmutableDictionary.CreateRange(new[] {
                KeyValuePair.Create(ResourceType.Wood, 8.0f)
            })
        };

        public static readonly Job Miner = new Job("JOB_MINER", "鉱夫")
        {
            Description = "石材や金属鉱石を採掘する職業。",
            RequiredMagicIds = ImmutableHashSet.Create("MAGIC_BASIC_MINING"), // 基本的な採掘技術が必要
            OutputResourcesPerWorker = ImmutableDictionary.CreateRange(new[] {
                KeyValuePair.Create(ResourceType.Stone, 5.0f),
                KeyValuePair.Create(ResourceType.MetalOre, 1.0f)
            })
        };

        public static readonly Job Scholar = new Job("JOB_SCHOLAR", "学者")
        {
            Description = "知識を研究し、新たな技術の発見に貢献する職業。",
            RequiredMagicIds = ImmutableHashSet.Create("MAGIC_BASIC_EDUCATION"), // 基本的な教育制度が必要
            OutputResourcesPerWorker = ImmutableDictionary.CreateRange(new[] {
                KeyValuePair.Create(ResourceType.Knowledge, 5.0f)
            }),
            BaseProductivityMultiplier = 0.5f // 知識生産は初期は低い
        };

        public static readonly Job Blacksmith = new Job("JOB_BLACKSMITH", "鍛冶屋")
        {
            Description = "金属鉱石から道具を生産する職業。",
            RequiredMagicIds = ImmutableHashSet.Create("MAGIC_METALLURGY"), // 冶金術が必要
            InputResourcesPerWorker = ImmutableDictionary.CreateRange(new[] {
                KeyValuePair.Create(ResourceType.MetalOre, 2.0f),
                KeyValuePair.Create(ResourceType.Wood, 0.5f) // 燃料として
            }),
            OutputResourcesPerWorker = ImmutableDictionary.CreateRange(new[] {
                KeyValuePair.Create(ResourceType.Tools, 1.0f)
            })
        };

        public static ImmutableDictionary<string, Job> AllJobs { get; } = new Dictionary<string, Job>
        {
            { Farmer.Id, Farmer },
            { Lumberjack.Id, Lumberjack },
            { Miner.Id, Miner },
            { Scholar.Id, Scholar },
            { Blacksmith.Id, Blacksmith },
            // 他のJobもここに追加
        }.ToImmutableDictionary();
    }
}
```

#### `Magic.cs`
社会技術（Magic）の定義です。

```csharp
// Magic.cs
using System.Collections.Generic;
using System.Collections.Immutable;

namespace GameSimulation.Core
{
    /// <summary>
    /// 社会技術（Magic）の定義。
    /// </summary>
    public record Magic
    {
        public string Id { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;
        public MagicType Type { get; init; } = MagicType.Agricultural;

        /// <summary>
        /// このMagicを研究・発見するために必要な前提MagicのIDリスト。
        /// </summary>
        public ImmutableHashSet<string> PrerequisiteMagicIds { get; init; } = ImmutableHashSet<string>.Empty;

        /// <summary>
        /// このMagicを適用するために必要な資源。
        /// </summary>
        public ImmutableDictionary<ResourceType, float> RequiredResourcesForApplication { get; init; } = ImmutableDictionary<ResourceType, float>.Empty;

        /// <summary>
        /// このMagicがアンロックするJobのIDリスト。
        /// </summary>
        public ImmutableHashSet<string> UnlocksJobIds { get; init; } = ImmutableHashSet<string>.Empty;

        /// <summary>
        /// このMagicが資源生産に与える影響（ResourceType -> ProductivityMultiplier）。
        /// </summary>
        public ImmutableDictionary<ResourceType, float> ResourceProductivityBonuses { get; init; } = ImmutableDictionary<ResourceType, float>.Empty;

        /// <summary>
        /// このMagicが文明レベルに与えるボーナス。
        /// </summary>
        public float CivilizationLevelBonus { get; init; } = 0.0f;

        /// <summary>
        /// このMagicの発見に必要な知識ポイント。
        /// </summary>
        public float KnowledgeCost { get; init; } = 100.0f;

        // Safe-Fail: コンストラクタでの初期化時にIdとNameが空でないことを保証する
        public Magic(string id, string name)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Magic Id cannot be null or whitespace.", nameof(id));
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Magic Name cannot be null or whitespace.", nameof(name));
            Id = id;
            Name = name;
        }

        public Magic() { } // デフォルトコンストラクタも提供
    }

    // プリセットMagicの例
    public static class PresetMagics
    {
        public static readonly Magic BasicAgriculture = new Magic("MAGIC_BASIC_AGRICULTURE", "基礎農業技術")
        {
            Description = "基本的な農耕技術。食料生産を向上させる。",
            Type = MagicType.Agricultural,
            ResourceProductivityBonuses = ImmutableDictionary.CreateRange(new[] {
                KeyValuePair.Create(ResourceType.Food, 0.2f) // 食料生産+20%
            }),
            CivilizationLevelBonus = 5.0f,
            KnowledgeCost = 50.0f
        };

        public static readonly Magic BasicMining = new Magic("MAGIC_BASIC_MINING", "基礎採掘技術")
        {
            Description = "基本的な採掘技術。鉱夫のJobをアンロックする。",
            Type = MagicType.Metallurgical,
            UnlocksJobIds = ImmutableHashSet.Create(PresetJobs.Miner.Id),
            ResourceProductivityBonuses = ImmutableDictionary.CreateRange(new[] {
                KeyValuePair.Create(ResourceType.Stone, 0.1f),
                KeyValuePair.Create(ResourceType.MetalOre, 0.1f)
            }),
            CivilizationLevelBonus = 7.0f,
            KnowledgeCost = 70.0f
        };

        public static readonly Magic BasicEducation = new Magic("MAGIC_BASIC_EDUCATION", "基礎教育制度")
        {
            Description = "基礎的な教育システム。学者のJobをアンロックし、知識生産を向上させる。",
            Type = MagicType.Educational,
            UnlocksJobIds = ImmutableHashSet.Create(PresetJobs.Scholar.Id),
            ResourceProductivityBonuses = ImmutableDictionary.CreateRange(new[] {
                KeyValuePair.Create(ResourceType.Knowledge, 0.5f) // 知識生産+50%
            }),
            CivilizationLevelBonus = 10.0f,
            KnowledgeCost = 120.0f
        };

        public static readonly Magic Metallurgy = new Magic("MAGIC_METALLURGY", "冶金術")
        {
            Description = "金属加工技術。鍛冶屋のJobをアンロックし、道具生産を向上させる。",
            Type = MagicType.Metallurgical,
            PrerequisiteMagicIds = ImmutableHashSet.Create("MAGIC_BASIC_MINING"),
            UnlocksJobIds = ImmutableHashSet.Create(PresetJobs.Blacksmith.Id),
            ResourceProductivityBonuses = ImmutableDictionary.CreateRange(new[] {
                KeyValuePair.Create(ResourceType.Tools, 0.3f)
            }),
            CivilizationLevelBonus = 15.0f,
            KnowledgeCost = 200.0f
        };

        public static readonly Magic Irrigation = new Magic("MAGIC_IRRIGATION", "灌漑技術")
        {
            Description = "灌漑システム。農業生産を大幅に向上させる。",
            Type = MagicType.Agricultural,
            PrerequisiteMagicIds = ImmutableHashSet.Create("MAGIC_BASIC_AGRICULTURE"),
            RequiredResourcesForApplication = ImmutableDictionary.CreateRange(new[] {
                KeyValuePair.Create(ResourceType.Wood, 500.0f),
                KeyValuePair.Create(ResourceType.Stone, 200.0f)
            }),
            ResourceProductivityBonuses = ImmutableDictionary.CreateRange(new[] {
                KeyValuePair.Create(ResourceType.Food, 0.5f) // 食料生産+50%
            }),
            CivilizationLevelBonus = 20.0f,
            KnowledgeCost = 300.0f
        };

        public static ImmutableDictionary<string, Magic> AllMagics { get; } = new Dictionary<string, Magic>
        {
            { BasicAgriculture.Id, BasicAgriculture },
            { BasicMining.Id, BasicMining },
            { BasicEducation.Id, BasicEducation },
            { Metallurgy.Id, Metallurgy },
            { Irrigation.Id, Irrigation },
            // 他のMagicもここに追加
        }.ToImmutableDictionary();
    }
}
```

#### `ValidationResult.cs`
MagicSanitizerEngineの検証結果を格納するレコードです。

```csharp
// ValidationResult.cs
namespace GameSimulation.Core
{
    /// <summary>
    /// MagicSanitizerEngineによる検証結果。
    /// </summary>
    public record ValidationResult
    {
        public bool IsValid { get; init; }
        public string Message { get; init; } = string.Empty;

        public static ValidationResult Success(string message = "Validation successful.") => new ValidationResult { IsValid = true, Message = message };
        public static ValidationResult Fail(string message = "Validation failed.") => new ValidationResult { IsValid = false, Message = message };
    }
}
```

#### `WorldState.cs`
シミュレーションの現在の世界状態を保持するクラスです。

```csharp
// WorldState.cs
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace GameSimulation.Core
{
    /// <summary>
    /// シミュレーションの現在の世界状態を保持するクラス。
    /// </summary>
    public class WorldState
    {
        private readonly ILogger _logger;

        public int CurrentYear { get; private set; }
        public long Population { get; private set; }
        public float CivilizationLevel { get; private set; }
        public float Happiness { get; private set; } // 0.0 - 1.0
        public float KnowledgePoints { get; private set; } // Magic研究に使用

        private Dictionary<ResourceType, float> _resources = new Dictionary<ResourceType, float>();
        public IReadOnlyDictionary<ResourceType, float> Resources => _resources;

        private HashSet<string> _discoveredMagics = new HashSet<string>();
        public IReadOnlySet<string> DiscoveredMagics => _discoveredMagics;

        private Dictionary<string, int> _activeJobs = new Dictionary<string, int>(); // JobId -> WorkerCount
        public IReadOnlyDictionary<string, int> ActiveJobs => _activeJobs;

        public WorldState(ILogger logger, int initialYear = 1000, long initialPopulation = 100, float initialCivilizationLevel = 10.0f, float initialHappiness = 0.5f)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            CurrentYear = initialYear;
            Population = initialPopulation;
            CivilizationLevel = initialCivilizationLevel;
            Happiness = initialHappiness;
            KnowledgePoints = 0.0f;

            // 初期資源の設定
            _resources[ResourceType.Food] = 500.0f;
            _resources[ResourceType.Wood] = 200.0f;
            _resources[ResourceType.Stone] = 100.0f;
            _resources[ResourceType.Tools] = 10.0f;
            _resources[ResourceType.Knowledge] = 0.0f; // 知識は初期状態ではゼロ

            // 初期Jobの割り当て
            AssignJob(PresetJobs.Farmer.Id, (int)(initialPopulation * 0.6));
            AssignJob(PresetJobs.Lumberjack.Id, (int)(initialPopulation * 0.2));
            // 残りは未割り当て
        }

        /// <summary>
        /// 年数を進める。
        /// </summary>
        /// <param name="years">進める年数。</param>
        public void AdvanceYear(int years = 1)
        {
            if (years <= 0)
            {
                _logger.LogWarning($"Attempted to advance year by non-positive value: {years}. No change applied.");
                return;
            }
            CurrentYear += years;
            _logger.LogDebug($"Advanced to year {CurrentYear}.");
        }

        /// <summary>
        /// 資源を追加する。
        /// </summary>
        /// <param name="type">資源の種類。</param>
        /// <param name="amount">追加量。</param>
        public void AddResource(ResourceType type, float amount)
        {
            if (amount < 0)
            {
                _logger.LogWarning($"Attempted to add negative resource amount for {type}: {amount}. Use RemoveResource for subtraction.");
                return;
            }
            _resources.TryAdd(type, 0.0f);
            _resources[type] += amount;
            _logger.LogDebug($"Added {amount} {type}. Current: {_resources[type]}");
        }

        /// <summary>
        /// 資源を消費する。
        /// </summary>
        /// <param name="type">資源の種類。</param>
        /// <param name="amount">消費量。</param>
        /// <returns>消費が成功したか。</returns>
        public bool RemoveResource(ResourceType type, float amount)
        {
            if (amount < 0)
            {
                _logger.LogWarning($"Attempted to remove negative resource amount for {type}: {amount}. Use AddResource for addition.");
                return false;
            }
            if (!_resources.ContainsKey(type) || _resources[type] < amount)
            {
                _logger.LogDebug($"Failed to remove {amount} {type}. Not enough resources. Current: {_resources.GetValueOrDefault(type, 0.0f)}");
                return false;
            }
            _resources[type] -= amount;
            _logger.LogDebug($"Removed {amount} {type}. Current: {_resources[type]}");
            return true;
        }

        /// <summary>
        /// 複数の資源をまとめて消費する。
        /// </summary>
        /// <param name="resourcesToConsume">消費する資源と量の辞書。</param>
        /// <returns>全ての資源の消費が成功したか。</returns>
        public bool RemoveResources(IReadOnlyDictionary<ResourceType, float> resourcesToConsume)
        {
            if (resourcesToConsume == null || !resourcesToConsume.Any()) return true; // 消費するものがない場合は成功

            // まず消費可能かチェック
            foreach (var entry in resourcesToConsume)
            {
                if (_resources.GetValueOrDefault(entry.Key, 0.0f) < entry.Value)
                {
                    _logger.LogWarning($"Cannot remove resources. Not enough {entry.Key} (needed: {entry.Value}, available: {_resources.GetValueOrDefault(entry.Key, 0.0f)}).");
                    return false;
                }
            }

            // 全て消費可能であれば実際に消費
            foreach (var entry in resourcesToConsume)
            {
                _resources[entry.Key] -= entry.Value;
                _logger.LogDebug($"Removed {entry.Value} {entry.Key}. Current: {_resources[entry.Key]}");
            }
            return true;
        }

        /// <summary>
        /// 人口を増減させる。
        /// </summary>
        /// <param name="delta">人口の増減量。</param>
        public void AdjustPopulation(long delta)
        {
            Population = Math.Max(0, Population + delta);
            _logger.LogDebug($"Population adjusted by {delta}. New population: {Population}");
        }

        /// <summary>
        /// Magicを発見済みにする。
        /// </summary>
        /// <param name="magicId">MagicのID。</param>
        /// <returns>Magicが新たに追加されたか。</returns>
        public bool AddDiscoveredMagic(string magicId)
        {
            if (string.IsNullOrWhiteSpace(magicId))
            {
                _logger.LogError("Attempted to add a null or empty Magic ID.");
                return false;
            }
            if (_discoveredMagics.Add(magicId))
            {
                _logger.LogInfo($"New Magic discovered: {magicId}");
                return true;
            }
            _logger.LogDebug($"Magic {magicId} was already discovered.");
            return false;
        }

        /// <summary>
        /// Jobにワーカーを割り当てる。
        /// </summary>
        /// <param name="jobId">JobのID。</param>
        /// <param name="count">割り当てるワーカー数。</param>
        public void AssignJob(string jobId, int count)
        {
            if (string.IsNullOrWhiteSpace(jobId))
            {
                _logger.LogError("Attempted to assign workers to a null or empty Job ID.");
                return;
            }
            if (count < 0)
            {
                _logger.LogWarning($"Attempted to assign negative worker count for Job {jobId}: {count}. Use UnassignJob for removal.");
                return;
            }
            _activeJobs.TryAdd(jobId, 0);
            _activeJobs[jobId] += count;
            _logger.LogDebug($"Assigned {count} workers to {jobId}. Total: {_activeJobs[jobId]}");
        }

        /// <summary>
        /// Jobからワーカーを解除する。
        /// </summary>
        /// <param name="jobId">JobのID。</param>
        /// <param name="count">解除するワーカー数。</param>
        public void UnassignJob(string jobId, int count)
        {
            if (string.IsNullOrWhiteSpace(jobId))
            {
                _logger.LogError("Attempted to unassign workers from a null or empty Job ID.");
                return;
            }
            if (count < 0)
            {
                _logger.LogWarning($"Attempted to unassign negative worker count for Job {jobId}: {count}. Use AssignJob for addition.");
                return;
            }
            if (_activeJobs.ContainsKey(jobId))
            {
                _activeJobs[jobId] = Math.Max(0, _activeJobs[jobId] - count);
                _logger.LogDebug($"Unassigned {count} workers from {jobId}. Remaining: {_activeJobs[jobId]}");
                if (_activeJobs[jobId] == 0)
                {
                    _activeJobs.Remove(jobId);
                    _logger.LogDebug($"Job {jobId} now has 0 workers and has been removed from active jobs.");
                }
            }
            else
            {
                _logger.LogWarning($"Attempted to unassign workers from non-existent active Job: {jobId}");
            }
        }

        /// <summary>
        /// 文明レベルを調整する。
        /// </summary>
        /// <param name="delta">文明レベルの増減量。</param>
        public void AdjustCivilizationLevel(float delta)
        {
            CivilizationLevel = Math.Max(0.0f, CivilizationLevel + delta);
            _logger.LogDebug($"Civilization Level adjusted by {delta}. New level: {CivilizationLevel}");
        }

        /// <summary>
        /// 幸福度を調整する。
        /// </summary>
        /// <param name="delta">幸福度の増減量。</param>
        public void AdjustHappiness(float delta)
        {
            Happiness = Math.Clamp(Happiness + delta, 0.0f, 1.0f);
            _logger.LogDebug($"Happiness adjusted by {delta}. New happiness: {Happiness}");
        }

        /// <summary>
        /// 知識ポイントを調整する。
        /// </summary>
        /// <param name="delta">知識ポイントの増減量。</param>
        public void AdjustKnowledgePoints(float delta)
        {
            KnowledgePoints = Math.Max(0.0f, KnowledgePoints + delta);
            _logger.LogDebug($"Knowledge Points adjusted by {delta}. New points: {KnowledgePoints}");
        }

        /// <summary>
        /// 現在の割り当てられているワーカーの総数を取得する。
        /// </summary>
        public int GetTotalAssignedWorkers()
        {
            return _activeJobs.Values.Sum();
        }
    }
}
```

### 3. コアロジックコンポーネント

#### `IMagicSanitizerEngine.cs`
Magicの検証を行うインターフェースです。

```csharp
// IMagicSanitizerEngine.cs
using System.Collections.Generic;

namespace GameSimulation.Core
{
    /// <summary>
    /// 社会技術（Magic）の検証とサニタイズを行うインターフェース。
    /// </summary>
    public interface IMagicSanitizerEngine
    {
        /// <summary>
        /// 提案されたMagicが現在の世界状態に適用可能か検証する。
        /// </summary>
        /// <param name="proposedMagic">検証するMagic。</param>
        /// <param name="currentState">現在の世界状態。</param>
        /// <returns>検証結果。</returns>
        ValidationResult SanitizeAndValidate(Magic proposedMagic, WorldState currentState);

        /// <summary>
        /// 指定されたMagicが現在利用可能（前提条件を満たしている）かどうかをチェックする。
        /// </summary>
        /// <param name="magicId">MagicのID。</param>
        /// <param name="currentState">現在の世界状態。</param>
        /// <returns>利用可能であればtrue。</returns>
        bool IsMagicAvailable(string magicId, WorldState currentState);
    }
}
```

#### `MagicSanitizerEngine.cs`
`IMagicSanitizerEngine`の実装です。

```csharp
// MagicSanitizerEngine.cs
using System;
using System.Linq;
using System.Collections.Generic;

namespace GameSimulation.Core
{
    /// <summary>
    /// 社会技術（Magic）の検証とサニタイズを行う実装。
    /// </summary>
    public class MagicSanitizerEngine : IMagicSanitizerEngine
    {
        private readonly ILogger _logger;
        private readonly IReadOnlyDictionary<string, Magic> _allMagics;

        public MagicSanitizerEngine(ILogger logger, IReadOnlyDictionary<string, Magic> allMagics)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _allMagics = allMagics ?? throw new ArgumentNullException(nameof(allMagics));
        }

        /// <summary>
        /// 提案されたMagicが現在の世界状態に適用可能か検証する。
        /// Safe-Fail構造: null引数チェック、前提条件チェック、資源チェック、矛盾チェック。
        /// </summary>
        /// <param name="proposedMagic">検証するMagic。</param>
        /// <param name="currentState">現在の世界状態。</param>
        /// <returns>検証結果。</returns>
        public ValidationResult SanitizeAndValidate(Magic proposedMagic, WorldState currentState)
        {
            if (proposedMagic == null)
            {
                _logger.LogError("Proposed Magic is null during validation.");
                return ValidationResult.Fail("Proposed Magic cannot be null.");
            }
            if (currentState == null)
            {
                _logger.LogError($"Current WorldState is null during validation for Magic: {proposedMagic.Id}");
                return ValidationResult.Fail("Current WorldState cannot be null.");
            }

            _logger.LogDebug($"Validating Magic: {proposedMagic.Name} (ID: {proposedMagic.Id})");

            // 1. 既に発見済みでないかチェック
            if (currentState.DiscoveredMagics.Contains(proposedMagic.Id))
            {
                return ValidationResult.Fail($"Magic '{proposedMagic.Name}' is already discovered.");
            }

            // 2. 前提技術の有無チェック
            foreach (var prerequisiteId in proposedMagic.PrerequisiteMagicIds)
            {
                if (!currentState.DiscoveredMagics.Contains(prerequisiteId))
                {
                    _logger.LogWarning($"Validation failed for {proposedMagic.Name}: Prerequisite Magic '{prerequisiteId}' not discovered.");
                    return ValidationResult.Fail($"Requires prerequisite Magic: '{_allMagics.GetValueOrDefault(prerequisiteId)?.Name ?? prerequisiteId}'.");
                }
            }

            // 3. 必要な資源の有無チェック
            foreach (var requiredResource in proposedMagic.RequiredResourcesForApplication)
            {
                if (currentState.Resources.GetValueOrDefault(requiredResource.Key, 0.0f) < requiredResource.Value)
                {
                    _logger.LogWarning($"Validation failed for {proposedMagic.Name}: Not enough {requiredResource.Key} (needed: {requiredResource.Value}, available: {currentState.Resources.GetValueOrDefault(requiredResource.Key, 0.0f)}).");
                    return ValidationResult.Fail($"Not enough {requiredResource.Key} to apply '{proposedMagic.Name}'.");
                }
            }

            // 4. 社会的な受容度チェック (例: 文明レベルが低いと高度なMagicは受け入れられない)
            // これはゲームデザインによって調整
            if (proposedMagic.CivilizationLevelBonus > 0 && currentState.CivilizationLevel < proposedMagic.CivilizationLevelBonus * 0.5f) // 例: ボーナスの半分以下の文明レベルでは難しい
            {
                _logger.LogWarning($"Validation failed for {proposedMagic.Name}: Civilization level too low ({currentState.CivilizationLevel}) for this advanced Magic.");
                return ValidationResult.Fail($"Civilization level ({currentState.CivilizationLevel:F1}) is too low to understand or apply '{proposedMagic.Name}'. (Requires approx. {proposedMagic.CivilizationLevelBonus * 0.5f:F1})");
            }

            // 5. 矛盾するMagicとの競合チェック (簡易版)
            // 例: 封建制と民主主義が同時に存在しないなど、より複雑なルールはここに記述
            // 現状は単純な競合は定義しないが、拡張ポイントとして残す
            // if (proposedMagic.Id == "MAGIC_DEMOCRACY" && currentState.DiscoveredMagics.Contains("MAGIC_FEUDALISM")) { ... }

            _logger.LogInfo($"Magic '{proposedMagic.Name}' validated successfully.");
            return ValidationResult.Success($"Magic '{proposedMagic.Name}' can be discovered and applied.");
        }

        /// <summary>
        /// 指定されたMagicが現在利用可能（前提条件を満たしている）かどうかをチェックする。
        /// </summary>
        /// <param name="magicId">MagicのID。</param>
        /// <param name="currentState">現在の世界状態。</param>
        /// <returns>利用可能であればtrue。</returns>
        public bool IsMagicAvailable(string magicId, WorldState currentState)
        {
            if (string.IsNullOrWhiteSpace(magicId))
            {
                _logger.LogError("Magic ID is null or empty during availability check.");
                return false;
            }
            if (currentState == null)
            {
                _logger.LogError($"Current WorldState is null during availability check for Magic: {magicId}");
                return false;
            }
            if (!_allMagics.TryGetValue(magicId, out var magic))
            {
                _logger.LogWarning($"Magic with ID '{magicId}' not found in global registry.");
                return false;
            }

            // 既に発見済みなら利用可能ではない（発見済みは「利用中」とみなす）
            if (currentState.DiscoveredMagics.Contains(magicId))
            {
                return false;
            }

            // 前提技術が全て発見済みかチェック
            foreach (var prerequisiteId in magic.PrerequisiteMagicIds)
            {
                if (!currentState.DiscoveredMagics.Contains(prerequisiteId))
                {
                    return false;
                }
            }
            return true;
        }
    }
}
```

#### `ICivilizationRecoveryPhaseSimulator.cs`
文明復興フェーズのシミュレーションロジックを定義するインターフェースです。

```csharp
// ICivilizationRecoveryPhaseSimulator.cs
namespace GameSimulation.Core
{
    /// <summary>
    /// 1001年以降の文明復興フェーズのシミュレーションロジックを定義するインターフェース。
    /// </summary>
    public interface ICivilizationRecoveryPhaseSimulator
    {
        /// <summary>
        /// シミュレーションを1年進める。
        /// </summary>
        /// <param name="state">現在の世界状態。</param>
        void SimulateYear(WorldState state);
    }
}
```

#### `CivilizationRecoveryPhaseSimulator.cs`
`ICivilizationRecoveryPhaseSimulator`の実装です。文明復興の主要ロジックをここに記述します。

```csharp
// CivilizationRecoveryPhaseSimulator.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Collections.Immutable;

namespace GameSimulation.Core
{
    /// <summary>
    /// 1001年以降の文明復興フェーズのシミュレーションロジック実装。
    /// </summary>
    public class CivilizationRecoveryPhaseSimulator : ICivilizationRecoveryPhaseSimulator
    {
        private readonly ILogger _logger;
        private readonly IMagicSanitizerEngine _magicSanitizer;
        private readonly IReadOnlyDictionary<string, Job> _allJobs;
        private readonly IReadOnlyDictionary<string, Magic> _allMagics;
        private readonly Random _random;

        // コンストラクタでの依存性注入
        public CivilizationRecoveryPhaseSimulator(
            ILogger logger,
            IMagicSanitizerEngine magicSanitizer,
            IReadOnlyDictionary<string, Job> allJobs,
            IReadOnlyDictionary<string, Magic> allMagics)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _magicSanitizer = magicSanitizer ?? throw new ArgumentNullException(nameof(magicSanitizer));
            _allJobs = allJobs ?? throw new ArgumentNullException(nameof(allJobs));
            _allMagics = allMagics ?? throw new ArgumentNullException(nameof(allMagics));
            _random = new Random(); // シード値は必要に応じて外部から注入可能にする
        }

        /// <summary>
        /// シミュレーションを1年進める。
        /// Safe-Fail構造: null引数チェック。
        /// </summary>
        /// <param name="state">現在の世界状態。</param>
        public void SimulateYear(WorldState state)
        {
            if (state == null)
            {
                _logger.LogError("WorldState is null in SimulateYear. Aborting year simulation.");
                return;
            }

            _logger.LogInfo($"--- Simulating Year {state.CurrentYear + 1} ---");
            state.AdvanceYear(); // 年数を進める

            // 1. 資源の消費と生産
            ProcessResourceConsumption(state);
            ProcessResourceProduction(state);

            // 2. 人口の増減
            AdjustPopulation(state);

            // 3. Magic（社会技術）の発見と適用
            DiscoverAndApplyMagic(state);

            // 4. Job（生活職業）の割り当てと再調整
            ReassignJobs(state);

            // 5. 文明レベルと幸福度の更新
            UpdateCivilizationLevel(state);
            UpdateHappiness(state);

            // 6. ランダムイベント (簡易版)
            TriggerRandomEvents(state);

            _logger.LogInfo($"--- End of Year {state.CurrentYear} Summary ---");
            _logger.LogInfo($"Population: {state.Population}");
            _logger.LogInfo($"Civilization Level: {state.CivilizationLevel:F2}");
            _logger.LogInfo($"Happiness: {state.Happiness:P1}");
            _logger.LogInfo($"Knowledge Points: {state.KnowledgePoints:F2}");
            _logger.LogInfo($"Resources: {string.Join(", ", state.Resources.Select(r => $"{r.Key}: {r.Value:F1}"))}");
            _logger.LogInfo($"Discovered Magics: {string.Join(", ", state.DiscoveredMagics.Select(id => _allMagics.GetValueOrDefault(id)?.Name ?? id))}");
            _logger.LogInfo($"Active Jobs: {string.Join(", ", state.ActiveJobs.Select(j => $"{_allJobs.GetValueOrDefault(j.Key)?.Name ?? j.Key}: {j.Value}"))}");
        }

        /// <summary>
        /// 資源の消費を処理する。
        /// </summary>
        private void ProcessResourceConsumption(WorldState state)
        {
            // 人口による食料消費
            float foodConsumptionPerCapita = 1.0f; // 1人あたり年間10単位の食料を消費
            float totalFoodConsumption = state.Population * foodConsumptionPerCapita;
            if (!state.RemoveResource(ResourceType.Food, totalFoodConsumption))
            {
                _logger.LogWarning($"Not enough food for population. Population will suffer. Needed: {totalFoodConsumption:F1}, Available: {state.Resources.GetValueOrDefault(ResourceType.Food, 0.0f):F1}");
                state.AdjustHappiness(-0.1f); // 食料不足は幸福度を低下させる
            }

            // Jobによる資源消費
            foreach (var entry in state.ActiveJobs)
            {
                if (!_allJobs.TryGetValue(entry.Key, out var job)) continue;

                foreach (var input in job.InputResourcesPerWorker)
                {
                    float requiredAmount = input.Value * entry.Value;
                    if (!state.RemoveResource(input.Key, requiredAmount))
                    {
                        _logger.LogWarning($"Job '{job.Name}' (workers: {entry.Value}) could not consume {requiredAmount:F1} {input.Key}. Productivity may be reduced.");
                        // 資源不足の場合、Jobの生産性を一時的に低下させるなどのペナルティを実装可能
                    }
                }
            }
        }

        /// <summary>
        /// 資源の生産を処理する。
        /// </summary>
        private void ProcessResourceProduction(WorldState state)
        {
            // Magicによる生産性ボーナスを計算
            var productivityBonuses = new Dictionary<ResourceType, float>();
            foreach (var magicId in state.DiscoveredMagics)
            {
                if (_allMagics.TryGetValue(magicId, out var magic))
                {
                    foreach (var bonus in magic.ResourceProductivityBonuses)
                    {
                        productivityBonuses.TryAdd(bonus.Key, 0.0f);
                        productivityBonuses[bonus.Key] += bonus.Value;
                    }
                }
            }

            // 各Jobの生産を計算
            foreach (var entry in state.ActiveJobs)
            {
                if (!_allJobs.TryGetValue(entry.Key, out var job)) continue;

                float currentProductivityMultiplier = job.BaseProductivityMultiplier;
                // Magicによるボーナスを適用
                foreach (var output in job.OutputResourcesPerWorker)
                {
                    if (productivityBonuses.TryGetValue(output.Key, out float magicBonus))
                    {
                        currentProductivityMultiplier += magicBonus;
                    }
                }

                foreach (var output in job.OutputResourcesPerWorker)
                {
                    float producedAmount = output.Value * entry.Value * currentProductivityMultiplier;
                    state.AddResource(output.Key, producedAmount);
                }
            }
        }

        /// <summary>
        /// 人口を調整する。
        /// </summary>
        private void AdjustPopulation(WorldState state)
        {
            float foodPerCapita = state.Resources.GetValueOrDefault(ResourceType.Food, 0.0f) / state.Population;
            float growthRate = 0.01f; // 基本成長率 1%

            if (foodPerCapita > 1.5f) // 食料が豊富
            {
                growthRate += 0.02f;
            }
            else if (foodPerCapita < 0.8f) // 食料不足
            {
                growthRate -= 0.03f;
                state.AdjustHappiness(-0.05f);
            }

            // 幸福度も人口成長に影響
            growthRate += (state.Happiness - 0.5f) * 0.02f; // 幸福度が高いほど成長率アップ

            long populationChange = (long)(state.Population * growthRate);
            state.AdjustPopulation(populationChange);
            _logger.LogDebug($"Population changed by {populationChange}. New population: {state.Population}");
        }

        /// <summary>
        /// Magic（社会技術）の発見と適用を試みる。
        /// </summary>
        private void DiscoverAndApplyMagic(WorldState state)
        {
            // 知識ポイントの蓄積
            state.AdjustKnowledgePoints(state.Resources.GetValueOrDefault(ResourceType.Knowledge, 0.0f));
            state.RemoveResource(ResourceType.Knowledge, state.Resources.GetValueOrDefault(ResourceType.Knowledge, 0.0f)); // 知識は消費される

            // 未発見のMagicの中から、前提条件を満たしているものを抽出
            var potentialMagics = _allMagics.Values
                .Where(m => !state.DiscoveredMagics.Contains(m.Id) && _magicSanitizer.IsMagicAvailable(m.Id, state))
                .ToList();

            if (!potentialMagics.Any())
            {
                _logger.LogDebug("No new potential Magics to discover this year.");
                return;
            }

            // 知識ポイントが閾値を超えたら、ランダムにMagicの発見を試みる
            if (state.KnowledgePoints >= 50.0f) // 最低限の知識ポイントが必要
            {
                // 知識コストが低いものほど発見されやすい、文明レベルが高いほど高度なものも発見されやすい、といったロジックを実装可能
                var discoverableMagicCandidates = potentialMagics
                    .Where(m => state.KnowledgePoints >= m.KnowledgeCost)
                    .OrderBy(m => m.KnowledgeCost) // 知識コストが低いものから優先
                    .ToList();

                if (discoverableMagicCandidates.Any())
                {
                    // 確率的に発見を試みる
                    Magic? magicToDiscover = null;
                    foreach (var candidate in discoverableMagicCandidates)
                    {
                        // 知識コストに対する達成度で確率を上げる
                        float discoveryChance = (state.KnowledgePoints / candidate.KnowledgeCost) * 0.1f; // 例: 知識ポイントがコストの10倍なら100%
                        if (_random.NextDouble() < discoveryChance)
                        {
                            magicToDiscover = candidate;
                            break;
                        }
                    }

                    if (magicToDiscover != null)
                    {
                        var validationResult = _magicSanitizer.SanitizeAndValidate(magicToDiscover, state);
                        if (validationResult.IsValid)
                        {
                            if (state.RemoveResources(magicToDiscover.RequiredResourcesForApplication))
                            {
                                state.AddDiscoveredMagic(magicToDiscover.Id);
                                state.AdjustKnowledgePoints(-magicToDiscover.KnowledgeCost); // 知識ポイントを消費
                                state.AdjustCivilizationLevel(magicToDiscover.CivilizationLevelBonus);
                                _logger.LogInfo($"Successfully discovered and applied Magic: {magicToDiscover.Name}. {validationResult.Message}");
                                state.AdjustHappiness(0.05f); // 新技術の発見は幸福度を上げる
                            }
                            else
                            {
                                _logger.LogWarning($"Magic '{magicToDiscover.Name}' validated but failed to consume required resources for application. {validationResult.Message}");
                            }
                        }
                        else
                        {
                            _logger.LogWarning($"Magic '{magicToDiscover.Name}' could not be discovered/applied: {validationResult.Message}");
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Job（生活職業）の割り当てと再調整を行う。
        /// </summary>
        private void ReassignJobs(WorldState state)
        {
            // 未割り当ての人口を計算
            int totalAssignedWorkers = state.GetTotalAssignedWorkers();
            long unassignedPopulation = state.Population - totalAssignedWorkers;

            if (unassignedPopulation < 0)
            {
                // 人口が減少し、Jobに割り当てられている人数が総人口を超えた場合、Jobからワーカーを強制的に解除
                _logger.LogWarning($"Assigned workers ({totalAssignedWorkers}) exceed total population ({state.Population}). Releasing excess workers.");
                ReleaseExcessWorkers(state);
                totalAssignedWorkers = state.GetTotalAssignedWorkers(); // 再計算
                unassignedPopulation = state.Population - totalAssignedWorkers;
            }

            if (unassignedPopulation <= 0)
            {
                _logger.LogDebug("No unassigned population to allocate to jobs.");
                return;
            }

            // アンロックされているJobの中から、優先度に基づいて割り当てる
            var availableJobs = _allJobs.Values
                .Where(job => job.RequiredMagicIds.IsSubsetOf(state.DiscoveredMagics))
                .ToList();

            if (!availableJobs.Any())
            {
                _logger.LogWarning("No available jobs to assign unassigned population.");
                return;
            }

            // 割り当てロジックの例:
            // 1. 食料生産が不足しているなら農民を優先
            // 2. 知識生産が不足しているなら学者を優先
            // 3. 資源生産が不足しているなら対応するJobを優先
            // 4. それ以外はランダムまたは文明レベルに応じて

            // 簡易的な割り当てロジック:
            // まず、食料生産を確保
            float targetFoodProduction = state.Population * 1.2f; // 人口の1.2倍の食料を目標
            float currentFoodProduction = CalculateResourceProduction(state, ResourceType.Food);
            if (currentFoodProduction < targetFoodProduction)
            {
                Job? farmerJob = availableJobs.FirstOrDefault(j => j.Id == PresetJobs.Farmer.Id);
                if (farmerJob != null && farmerJob.OutputResourcesPerWorker.TryGetValue(ResourceType.Food, out float foodOutput))
                {
                    int neededFarmers = (int)Math.Ceiling((targetFoodProduction - currentFoodProduction) / (foodOutput * farmerJob.BaseProductivityMultiplier));
                    int assignCount = Math.Min((int)unassignedPopulation, neededFarmers);
                    if (assignCount > 0)
                    {
                        state.AssignJob(farmerJob.Id, assignCount);
                        unassignedPopulation -= assignCount;
                        _logger.LogDebug($"Assigned {assignCount} to {farmerJob.Name} to meet food demand.");
                    }
                }
            }

            // 残りの人口を他のJobに均等に、または優先度に基づいて割り当てる
            // ここでは、単純に利用可能なJobにランダムに割り当てる
            if (unassignedPopulation > 0)
            {
                var assignableJobs = availableJobs.Where(j => j.Id != PresetJobs.Farmer.Id).ToList(); // 農民以外
                if (assignableJobs.Any())
                {
                    int workersPerJob = (int)(unassignedPopulation / assignableJobs.Count);
                    foreach (var job in assignableJobs)
                    {
                        if (workersPerJob > 0)
                        {
                            state.AssignJob(job.Id, workersPerJob);
                            unassignedPopulation -= workersPerJob;
                        }
                    }
                    // 残りの端数をランダムに割り当てる
                    while (unassignedPopulation > 0 && assignableJobs.Any())
                    {
                        var job = assignableJobs[_random.Next(assignableJobs.Count)];
                        state.AssignJob(job.Id, 1);
                        unassignedPopulation--;
                    }
                }
                else if (unassignedPopulation > 0 && availableJobs.Any()) // 農民しかいない場合
                {
                    state.AssignJob(PresetJobs.Farmer.Id, (int)unassignedPopulation);
                    unassignedPopulation = 0;
                }
            }

            _logger.LogDebug($"Remaining unassigned population: {unassignedPopulation}");
        }

        /// <summary>
        /// 人口減少によりJobに割り当てられているワーカーが総人口を超えた場合に、超過分を解除する。
        /// </summary>
        private void ReleaseExcessWorkers(WorldState state)
        {
            long totalAssigned = state.GetTotalAssignedWorkers();
            long excessWorkers = totalAssigned - state.Population;

            if (excessWorkers <= 0) return;

            // 優先度の低いJobから順に解除するか、均等に解除する
            // ここでは、単純にランダムなJobから解除していく
            var activeJobIds = state.ActiveJobs.Keys.ToList();
            while (excessWorkers > 0 && activeJobIds.Any())
            {
                string jobIdToUnassign = activeJobIds[_random.Next(activeJobIds.Count)];
                int workersInJob = state.ActiveJobs.GetValueOrDefault(jobIdToUnassign, 0);

                if (workersInJob > 0)
                {
                    int unassignCount = (int)Math.Min(workersInJob, excessWorkers);
                    state.UnassignJob(jobIdToUnassign, unassignCount);
                    excessWorkers -= unassignCount;
                }
                else
                {
                    activeJobIds.Remove(jobIdToUnassign); // 既にワーカーがいないJobはリストから削除
                }
            }
        }

        /// <summary>
        /// 特定の資源の年間生産量を計算する。
        /// </summary>
        private float CalculateResourceProduction(WorldState state, ResourceType resourceType)
        {
            float totalProduction = 0.0f;
            foreach (var entry in state.ActiveJobs)
            {
                if (!_allJobs.TryGetValue(entry.Key, out var job)) continue;
                if (job.OutputResourcesPerWorker.TryGetValue(resourceType, out float output))
                {
                    float currentProductivityMultiplier = job.BaseProductivityMultiplier;
                    // Magicによるボーナスを適用
                    foreach (var magicId in state.DiscoveredMagics)
                    {
                        if (_allMagics.TryGetValue(magicId, out var magic))
                        {
                            if (magic.ResourceProductivityBonuses.TryGetValue(resourceType, out float magicBonus))
                            {
                                currentProductivityMultiplier += magicBonus;
                            }
                        }
                    }
                    totalProduction += output * entry.Value * currentProductivityMultiplier;
                }
            }
            return totalProduction;
        }

        /// <summary>
        /// 文明レベルを更新する。
        /// </summary>
        private void UpdateCivilizationLevel(WorldState state)
        {
            float baseLevel = state.Population / 1000.0f; // 人口が多いほど基本レベルが高い
            float magicBonus = state.DiscoveredMagics.Sum(id => _allMagics.GetValueOrDefault(id)?.CivilizationLevelBonus ?? 0.0f);
            float resourceWealthBonus = state.Resources.Values.Sum() / 10000.0f; // 総資源量も影響

            float newCivilizationLevel = baseLevel + magicBonus + resourceWealthBonus;
            state.AdjustCivilizationLevel(newCivilizationLevel - state.CivilizationLevel); // 差分を適用
        }

        /// <summary>
        /// 幸福度を更新する。
        /// </summary>
        private void UpdateHappiness(WorldState state)
        {
            float happinessChange = 0.0f;

            // 食料の充足度
            float foodPerCapita = state.Resources.GetValueOrDefault(ResourceType.Food, 0.0f) / state.Population;
            if (foodPerCapita > 2.0f) happinessChange += 0.02f;
            else if (foodPerCapita < 0.5f) happinessChange -= 0.05f;

            // 文明レベル
            happinessChange += (state.CivilizationLevel / 100.0f) * 0.01f; // 文明レベルが高いほど幸福度も上がりやすい

            // Magicの数
            happinessChange += state.DiscoveredMagics.Count * 0.001f;

            // ランダムな変動
            happinessChange += (float)(_random.NextDouble() - 0.5) * 0.01f;

            state.AdjustHappiness(happinessChange);
        }

        /// <summary>
        /// ランダムイベントを発生させる (簡易版)。
        /// </summary>
        private void TriggerRandomEvents(WorldState state)
        {
            if (_random.NextDouble() < 0.05) // 5%の確率でイベント発生
            {
                int eventType = _random.Next(3);
                switch (eventType)
                {
                    case 0: // 豊作
                        float foodBonus = state.Population * 5.0f;
                        state.AddResource(ResourceType.Food, foodBonus);
                        state.AdjustHappiness(0.05f);
                        _logger.LogInfo($"[EVENT] 豊作！食料が{foodBonus:F1}増加しました。");
                        break;
                    case 1: // 疫病
                        long populationLoss = (long)(state.Population * 0.05);
                        state.AdjustPopulation(-populationLoss);
                        state.AdjustHappiness(-0.1f);
                        _logger.LogWarning($"[EVENT] 疫病が発生し、人口が{populationLoss}減少しました。");
                        break;
                    case 2: // 新しい資源の発見
                        ResourceType newResource = ResourceType.MetalOre; // 例として
                        float resourceAmount = state.Population * 0.5f;
                        state.AddResource(newResource, resourceAmount);
                        _logger.LogInfo($"[EVENT] 新しい鉱脈が発見され、{newResource}が{resourceAmount:F1}増加しました。");
                        break;
                }
            }
        }
    }
}
```

### 4. シミュレーションマネージャー

#### `SimulationManager.cs`
シミュレーション全体を管理し、実行するシングルトンクラスです。

```csharp
// SimulationManager.cs
using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace GameSimulation.Core
{
    /// <summary>
    /// シミュレーション全体を管理し、実行するクラス。
    /// </summary>
    public class SimulationManager
    {
        private readonly ILogger _logger;
        private readonly IMagicSanitizerEngine _magicSanitizer;
        private readonly ICivilizationRecoveryPhaseSimulator _recoverySimulator;
        private readonly WorldState _worldState;

        // DIを考慮したコンストラクタ
        public SimulationManager(ILogger logger, IMagicSanitizerEngine magicSanitizer, ICivilizationRecoveryPhaseSimulator recoverySimulator, WorldState initialWorldState)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _magicSanitizer = magicSanitizer ?? throw new ArgumentNullException(nameof(magicSanitizer));
            _recoverySimulator = recoverySimulator ?? throw new ArgumentNullException(nameof(recoverySimulator));
            _worldState = initialWorldState ?? throw new ArgumentNullException(nameof(initialWorldState));

            _logger.LogInfo("SimulationManager initialized.");
        }

        /// <summary>
        /// シミュレーションを実行する。
        /// Safe-Fail構造: 引数チェック。
        /// </summary>
        /// <param name="startYear">シミュレーション開始年。</param>
        /// <param name="endYear">シミュレーション終了年。</param>
        public void RunSimulation(int startYear, int endYear)
        {
            if (startYear < 0 || endYear < startYear)
            {
                _logger.LogError($"Invalid year range provided: startYear={startYear}, endYear={endYear}. Aborting simulation.");
                return;
            }

            _logger.LogInfo($"Starting simulation from year {startYear} to {endYear}.");

            // 初期状態のログ
            _logger.LogInfo($"Initial World State (Year {_worldState.CurrentYear}):");
            _logger.LogInfo($"  Population: {_worldState.Population}");
            _logger.LogInfo($"  Civilization Level: {_worldState.CivilizationLevel:F2}");
            _logger.LogInfo($"  Happiness: {_worldState.Happiness:P1}");
            _logger.LogInfo($"  Resources: {string.Join(", ", _worldState.Resources.Select(r => $"{r.Key}: {r.Value:F1}"))}");
            _logger.LogInfo($"  Discovered Magics: {string.Join(", ", _worldState.DiscoveredMagics)}");
            _logger.LogInfo($"  Active Jobs: {string.Join(", ", _worldState.ActiveJobs.Select(j => $"{j.Key}: {j.Value}"))}");


            for (int year = startYear; year <= endYear; year++)
            {
                try
                {
                    _recoverySimulator.SimulateYear(_worldState);
                }
                catch (Exception ex)
                {
                    _logger.LogError($"An unhandled exception occurred during year {year} simulation: {ex.Message}", ex);
                    // シミュレーションを停止するか、エラーを許容して続行するかはゲームデザインによる
                    // ここでは安全のため停止
                    _logger.LogError("Simulation aborted due to unhandled exception.");
                    return;
                }
            }

            _logger.LogInfo($"Simulation finished at year {_worldState.CurrentYear}.");
            _logger.LogInfo($"Final World State (Year {_worldState.CurrentYear}):");
            _logger.LogInfo($"  Population: {_worldState.Population}");
            _logger.LogInfo($"  Civilization Level: {_worldState.CivilizationLevel:F2}");
            _logger.LogInfo($"  Happiness: {_worldState.Happiness:P1}");
            _logger.LogInfo($"  Resources: {string.Join(", ", _worldState.Resources.Select(r => $"{r.Key}: {r.Value:F1}"))}");
            _logger.LogInfo($"  Discovered Magics: {string.Join(", ", _worldState.DiscoveredMagics.Select(id => PresetMagics.AllMagics.GetValueOrDefault(id)?.Name ?? id))}");
            _logger.LogInfo($"  Active Jobs: {string.Join(", ", _worldState.ActiveJobs.Select(j => $"{PresetJobs.AllJobs.GetValueOrDefault(j.Key)?.Name ?? j.Key}: {j.Value}"))}");
        }
    }
}
```

### 5. エントリポイント

#### `Program.cs`
シミュレーションを実行するためのエントリポイントです。

```csharp
// Program.cs
using GameSimulation.Core;
using System;
using System.Collections.Generic;
using System.Linq;

namespace GameSimulation
{
    public class Program
    {
        public static void Main(string[] args)
        {
            // 依存関係の解決 (簡易的なDI)
            ILogger logger = new ConsoleLogger();
            
            // 全てのJobとMagicの定義を渡す
            IReadOnlyDictionary<string, Job> allJobs = PresetJobs.AllJobs;
            IReadOnlyDictionary<string, Magic> allMagics = PresetMagics.AllMagics;

            IMagicSanitizerEngine magicSanitizer = new MagicSanitizerEngine(logger, allMagics);
            
            // 初期WorldStateの作成 (1000年時点の文明崩壊後の状態を想定)
            WorldState initialWorldState = new WorldState(logger, initialYear: 1000, initialPopulation: 1000, initialCivilizationLevel: 20.0f, initialHappiness: 0.6f);
            
            // 初期資源を少し多めに設定して、初期の人口維持を容易にする
            initialWorldState.AddResource(ResourceType.Food, 20000.0f);
            initialWorldState.AddResource(ResourceType.Wood, 5000.0f);
            initialWorldState.AddResource(ResourceType.Stone, 2000.0f);
            initialWorldState.AddResource(ResourceType.Tools, 100.0f);
            initialWorldState.AddResource(ResourceType.Knowledge, 50.0f); // 初期知識ポイント

            // 初期Jobの割り当てを調整
            initialWorldState.UnassignJob(PresetJobs.Farmer.Id, (int)(initialWorldState.Population * 0.6)); // デフォルト割り当てをリセット
            initialWorldState.UnassignJob(PresetJobs.Lumberjack.Id, (int)(initialWorldState.Population * 0.2)); // デフォルト割り当てをリセット
            initialWorldState.AssignJob(PresetJobs.Farmer.Id, 600);
            initialWorldState.AssignJob(PresetJobs.Lumberjack.Id, 200);
            initialWorldState.AssignJob(PresetJobs.Scholar.Id, 50); // 初期から学者を配置して知識生産を促す

            ICivilizationRecoveryPhaseSimulator recoverySimulator = new CivilizationRecoveryPhaseSimulator(logger, magicSanitizer, allJobs, allMagics);

            SimulationManager simulationManager = new SimulationManager(logger, magicSanitizer, recoverySimulator, initialWorldState);

            // 1001年から1200年までの200年間をシミュレート
            int startYear = 1001;
            int endYear = 1200;
            simulationManager.RunSimulation(startYear, endYear);

            Console.WriteLine("\nPress any key to exit.");
            Console.ReadKey();
        }
    }
}
```

### 6. 動作検証のポイント

*   **初期状態からの変化**: 1001年からシミュレーションを開始し、人口、資源、文明レベル、幸福度がどのように変化していくかを確認します。
*   **Magicの発見**: `KnowledgePoints`が蓄積され、`MagicSanitizerEngine`による検証を経て、新たなMagicが発見・適用されるかを確認します。特に、前提Magicが必要な`Metallurgy`や`Irrigation`が正しくアンロックされるか。
*   **Jobの割り当て**: 新たなMagicが発見された際に、それによってアンロックされるJob（例: `Miner`, `Blacksmith`, `Scholar`）に人口が適切に割り当てられるかを確認します。
*   **資源の循環**: 食料生産→人口増加→労働力増加→資源生産増加→Magic発見→生産性向上、といった循環が機能しているか。
*   **Safe-Failの確認**:
    *   `WorldState`の`RemoveResource`で資源が不足した場合に`false`が返され、ログが出力されるか。
    *   `MagicSanitizerEngine.SanitizeAndValidate`で前提条件が満たされないMagicが拒否され、理由がログに出るか。
    *   `SimulateYear`内で予期せぬエラーが発生した場合に、`SimulationManager`がそれを捕捉し、ログを出力して安全に停止するか。
*   **幸福度と人口**: 食料不足やイベントが幸福度や人口にどのように影響するか。
*   **ログ出力**: `ILogger`を通じて、シミュレーションの各ステップで適切な情報、警告、エラーがログに出力されているか。

---
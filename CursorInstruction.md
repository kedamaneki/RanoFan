はい、承知いたしました。
「フロム風戦闘×ブラインド熱科学クラフト×千年史自律シミュレーター」のリードディレクターとして、ターン1001からターン3000までの2000年間（第21〜60世代）を「不遇枝エネルギー蓄積モデル」を適用して連続自律進行させ、3000年史の正史を完成させるためのC#実装指示を生成します。

以下のコードは、Safe-Fail構造、MagicSanitizerEngine、Job/Magicの定義規約を厳格に守り、Cursor(IDE)のCtrl+Lへそのまま読み込ませてC#コード化できる形式です。

---

```csharp
// ファイル名: SimulationCore.cs (またはプロジェクトの適切な場所に配置)

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FromLikeCombatSimulator
{
    /// <summary>
    /// Safe-Fail構造を担う静的ヘルパークラス。
    /// 予期せぬエラーが発生した場合でもシミュレーションが完全に停止せず、
    /// 問題をログに記録し、可能な限りフォールバック処理を試みます。
    /// </summary>
    public static class SafeFailMechanism
    {
        /// <summary>
        /// 指定されたアクションを安全に実行し、例外が発生した場合はログを記録し、フォールバック処理を試みます。
        /// </summary>
        /// <param name="action">実行するアクション。</param>
        /// <param name="context">アクションのコンテキスト（ログ用）。</param>
        /// <param name="fallbackAction">例外発生時に実行するフォールバックアクション（オプション）。</param>
        /// <returns>アクションが成功した場合はtrue、失敗した場合はfalse。</returns>
        public static bool ExecuteSafely(Action action, string context, Action fallbackAction = null)
        {
            try
            {
                action?.Invoke();
                return true;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[SAFE-FAIL] Critical error during {context}: {ex.Message}");
                Console.Error.WriteLine(ex.StackTrace);
                fallbackAction?.Invoke();
                return false;
            }
        }

        /// <summary>
        /// 指定された関数を安全に実行し、例外が発生した場合はログを記録し、デフォルト値を返します。
        /// </summary>
        /// <typeparam name="T">関数の戻り値の型。</typeparam>
        /// <param name="func">実行する関数。</param>
        /// <param name="context">関数のコンテキスト（ログ用）。</param>
        /// <param name="defaultValue">例外発生時に返すデフォルト値。</param>
        /// <returns>関数の結果、または例外発生時のデフォルト値。</returns>
        public static T ExecuteSafely<T>(Func<T> func, string context, T defaultValue = default(T))
        {
            try
            {
                return func != null ? func.Invoke() : defaultValue;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[SAFE-FAIL] Critical error during {context}: {ex.Message}");
                Console.Error.WriteLine(ex.StackTrace);
                return defaultValue;
            }
        }
    }

    /// <summary>
    /// MagicSanitizerEngine: 魔法（社会技術）の適用前にその安全性と整合性を検証するエンジン。
    /// シミュレーションの安定性を保ちつつ、技術の導入を制御します。
    /// </summary>
    public static class MagicSanitizerEngine
    {
        /// <summary>
        /// 魔法（社会技術）が現在のワールドステートに安全に適用可能か検証します。
        /// </summary>
        /// <param name="magic">検証する魔法。</param>
        /// <param name="state">現在のワールドステート。</param>
        /// <param name="targetEntity">魔法の対象となるエンティティ（オプション）。</param>
        /// <returns>魔法が安全に適用可能であればtrue、そうでなければfalse。</returns>
        public static bool Sanitize(Magic magic, WorldState state, Entity targetEntity = null)
        {
            if (magic == null)
            {
                Console.WriteLine("[MagicSanitizer] Warning: Attempted to sanitize a null magic object.");
                return false;
            }
            if (state == null)
            {
                Console.WriteLine($"[MagicSanitizer] Warning: Attempted to sanitize magic '{magic.Name}' with a null WorldState.");
                return false;
            }

            // 1. 魔法の適用条件チェック（例: リソース要件、前提技術、倫理的制約など）
            // 実際のゲームでは、MagicクラスにSanitizationRulesのようなプロパティを持たせることで、
            // より複雑なルールを定義できます。

            // 例: 特定の魔法は特定の世代以降でしか使えない
            if (magic.MinGenerationRequired > state.CurrentGeneration)
            {
                Console.WriteLine($"[MagicSanitizer] Denied '{magic.Name}': Requires Generation {magic.MinGenerationRequired}, current is {state.CurrentGeneration}.");
                return false;
            }

            // 例: ターゲットエンティティが存在する場合、そのエンティティが魔法の対象として適切か
            if (targetEntity != null && !magic.CanApplyTo(targetEntity))
            {
                Console.WriteLine($"[MagicSanitizer] Denied '{magic.Name}': Cannot apply to entity '{targetEntity.Name}'.");
                return false;
            }

            // 例: 魔法がワールドステートに破壊的な影響を与えないか（シミュレーションの安定性維持）
            // 不遇枝エネルギーが低い状態での破壊的魔法は、社会の不安定化を招くため危険と判断する。
            if (magic.IsPotentiallyDestructive && state.UnfavoredBranchEnergy < WorldState.UNFAVORED_ENERGY_THRESHOLD / 2)
            {
                Console.WriteLine($"[MagicSanitizer] Warning: Potentially destructive magic '{magic.Name}' detected. Unfavored energy is low. Proceed with caution.");
                // ここでユーザーへの警告や、適用の一時停止などの処理を挟むことも可能
                // 今回は警告のみで進行を許可するが、厳格なゲームではここでfalseを返す。
            }

            Console.WriteLine($"[MagicSanitizer] Magic '{magic.Name}' (ID: {magic.ID}) deemed safe for application.");
            return true; // 現時点では安全と判断
        }
    }

    /// <summary>
    /// シミュレーション内の個体やグループを表す基底クラス。
    /// </summary>
    public class Entity
    {
        public Guid ID { get; } = Guid.NewGuid();
        public string Name { get; set; }
        public int Generation { get; set; } // 所属世代
        public double Energy { get; set; } // 生命力、活動力など
        public double Resources { get; set; } // 資源量
        public Job CurrentJob { get; set; } // 現在の生活職業
        public List<Magic> AppliedMagics { get; } = new List<Magic>(); // 適用されている社会技術
        public bool IsUnfavored { get; set; } // 不遇枝モデルの対象かどうか

        public Entity(string name, int generation, bool isUnfavored = false)
        {
            Name = name;
            Generation = generation;
            Energy = 100.0; // 初期エネルギー
            Resources = 50.0; // 初期資源
            IsUnfavored = isUnfavored;
        }

        /// <summary>
        /// 現在の職業の効果を適用します。
        /// </summary>
        /// <param name="state">現在のワールドステート。</param>
        public void ApplyJobEffect(WorldState state)
        {
            if (CurrentJob != null)
            {
                SafeFailMechanism.ExecuteSafely(() => CurrentJob.ApplyJobEffect(state, this), $"applying job effect for {Name}");
            }
        }

        public void ConsumeEnergy(double amount)
        {
            Energy = Math.Max(0, Energy - amount);
        }

        public void GainEnergy(double amount)
        {
            Energy += amount;
        }

        public void GainResources(double amount)
        {
            Resources += amount;
        }

        public void LoseResources(double amount)
        {
            Resources = Math.Max(0, Resources - amount);
        }
    }

    /// <summary>
    /// Magic: 社会技術を表すクラス。
    /// </summary>
    public class Magic
    {
        public Guid ID { get; } = Guid.NewGuid();
        public string Name { get; set; }
        public string EffectDescription { get; set; }
        public double CostResources { get; set; } // 適用に必要なリソース
        public int MinGenerationRequired { get; set; } // 適用に必要な最低世代
        public bool IsPotentiallyDestructive { get; set; } = false; // 破壊的な影響を持つ可能性のある魔法か

        public Magic(string name, string description, double cost, int minGen = 1, bool isDestructive = false)
        {
            Name = name;
            EffectDescription = description;
            CostResources = cost;
            MinGenerationRequired = minGen;
            IsPotentiallyDestructive = isDestructive;
        }

        /// <summary>
        /// この魔法が指定されたエンティティに適用可能かチェックします。
        /// </summary>
        /// <param name="entity">対象エンティティ。</param>
        /// <returns>適用可能であればtrue。</returns>
        public virtual bool CanApplyTo(Entity entity)
        {
            // デフォルトでは全てのエンティティに適用可能とする。
            // 特定の魔法は特定の特性を持つエンティティにのみ適用可能とするロジックをここに記述。
            return true;
        }

        /// <summary>
        /// 魔法の効果をワールドステートと対象エンティティに適用します。
        /// </summary>
        /// <param name="state">現在のワールドステート。</param>
        /// <param name="targetEntity">魔法の対象となるエンティティ。</param>
        public virtual void ApplyEffect(WorldState state, Entity targetEntity)
        {
            // 汎用的な効果（例: リソース消費）
            if (targetEntity != null)
            {
                targetEntity.LoseResources(CostResources);
                targetEntity.AppliedMagics.Add(this);
            }
            Console.WriteLine($"[Magic Applied] '{Name}' applied to '{targetEntity?.Name ?? "World"}'. Effect: {EffectDescription}");
            // ここに具体的な魔法の効果ロジックを記述
        }
    }

    /// <summary>
    /// Job: 生活職業を表すクラス。
    /// </summary>
    public class Job
    {
        public Guid ID { get; } = Guid.NewGuid();
        public string Name { get; set; }
        public string Description { get; set; }
        public double ResourceGenerationRate { get; set; } // ターンあたりの資源生成量
        public double EnergyConsumptionRate { get; set; } // ターンあたりのエネルギー消費量

        public Job(string name, string description, double genRate, double energyConRate)
        {
            Name = name;
            Description = description;
            ResourceGenerationRate = genRate;
            EnergyConsumptionRate = energyConRate;
        }

        /// <summary>
        /// 職業の効果をワーカーエンティティに適用します。
        /// </summary>
        /// <param name="state">現在のワールドステート。</param>
        /// <param name="worker">この職業を持つエンティティ。</param>
        public virtual void ApplyJobEffect(WorldState state, Entity worker)
        {
            worker.GainResources(ResourceGenerationRate);
            worker.ConsumeEnergy(EnergyConsumptionRate);
            //Console.WriteLine($"  Entity '{worker.Name}' ({worker.CurrentJob.Name}) gained {ResourceGenerationRate:F1} resources, consumed {EnergyConsumptionRate:F1} energy. (R:{worker.Resources:F1}, E:{worker.Energy:F1})");
        }
    }

    /// <summary>
    /// シミュレーションの現在の状態を保持するクラス。
    /// </summary>
    public class WorldState
    {
        public int CurrentTurn { get; set; }
        public int CurrentGeneration { get; set; }
        public List<Entity> Entities { get; } = new List<Entity>();
        public List<Magic> AvailableMagics { get; } = new List<Magic>(); // 発見済みの社会技術
        public List<Job> AvailableJobs { get; } = new List<Job>(); // 利用可能な生活職業
        public double UnfavoredBranchEnergy { get; set; } = 0.0; // 不遇枝エネルギー蓄積量
        public const double UNFAVORED_ENERGY_THRESHOLD = 500.0; // 不遇枝イベント発生閾値
        public const int TURNS_PER_GENERATION = 50; // 1世代あたりのターン数

        public WorldState(int startTurn = 1)
        {
            CurrentTurn = startTurn;
            CurrentGeneration = (startTurn - 1) / TURNS_PER_GENERATION + 1;
            InitializeDefaultJobs();
            InitializeDefaultMagics();
            InitializeEntities();
        }

        private void InitializeDefaultJobs()
        {
            AvailableJobs.Add(new Job("Gatherer", "Collects basic resources.", 5.0, 2.0));
            AvailableJobs.Add(new Job("Crafter", "Processes resources into goods.", 8.0, 3.0));
            AvailableJobs.Add(new Job("Warrior", "Protects the community.", 3.0, 4.0));
            AvailableJobs.Add(new Job("Scholar", "Researches new knowledge.", 1.0, 1.0)); // 資源生成は低いが、マジック発見に寄与する想定
        }

        private void InitializeDefaultMagics()
        {
            AvailableMagics.Add(new Magic("Basic Tool Crafting", "Enables crafting of simple tools.", 10.0));
            AvailableMagics.Add(new Magic("Communal Farming", "Increases food production efficiency.", 20.0, 2));
            AvailableMagics.Add(new Magic("Basic Defense Structure", "Provides basic protection.", 15.0, 3));
        }

        private void InitializeEntities()
        {
            // 初期エンティティの生成例
            // ターン1001から開始する場合、第21世代のエンティティを生成
            Entities.Add(new Entity("Aeliana", CurrentGeneration, false) { CurrentJob = AvailableJobs.First(j => j.Name == "Gatherer") });
            Entities.Add(new Entity("Borin", CurrentGeneration, true) { CurrentJob = AvailableJobs.First(j => j.Name == "Crafter") }); // 不遇枝の例
            Entities.Add(new Entity("Caelen", CurrentGeneration, false) { CurrentJob = AvailableJobs.First(j => j.Name == "Warrior") });
            Entities.Add(new Entity("Dara", CurrentGeneration, true) { CurrentJob = AvailableJobs.First(j => j.Name == "Gatherer") }); // 不遇枝の例
            Entities.Add(new Entity("Elara", CurrentGeneration, false) { CurrentJob = AvailableJobs.First(j => j.Name == "Scholar") });
        }
    }

    /// <summary>
    /// シミュレーションの歴史を記録するクラス。
    /// 各ターンの重要なイベントやワールドステートのスナップショットを保存し、正史を構築します。
    /// </summary>
    public class SimulationHistory
    {
        public List<ChronicleEntry> ChronicleEntries { get; } = new List<ChronicleEntry>();

        /// <summary>
        /// 現在のターンの状態とイベントを歴史に記録します。
        /// </summary>
        /// <param name="turn">現在のターン。</param>
        /// <param name="generation">現在の世代。</param>
        /// <param name="state">現在のワールドステート。</param>
        /// <param name="eventLog">このターンで発生した主要なイベントのログ。</param>
        public void RecordTurn(int turn, int generation, WorldState state, string eventLog)
        {
            var entry = new ChronicleEntry
            {
                Turn = turn,
                Generation = generation,
                EventLog = eventLog,
                // ワールドステートの重要な情報のみをスナップショットとして記録
                EntityStates = state.Entities.Select(e => new EntitySnapshot(e)).ToList(),
                UnfavoredBranchEnergy = state.UnfavoredBranchEnergy,
                PopulationCount = state.Entities.Count
            };
            ChronicleEntries.Add(entry);
            //Console.WriteLine($"[HISTORY] Turn {turn}, Gen {generation}: {eventLog}"); // 詳細ログは必要に応じて有効化
        }

        /// <summary>
        /// 歴史のエントリを表す内部クラス。
        /// </summary>
        public class ChronicleEntry
        {
            public int Turn { get; set; }
            public int Generation { get; set; }
            public string EventLog { get; set; }
            public List<EntitySnapshot> EntityStates { get; set; }
            public double UnfavoredBranchEnergy { get; set; }
            public int PopulationCount { get; set; }
        }

        /// <summary>
        /// エンティティの特定の時点での状態を記録するためのスナップショットクラス。
        /// </summary>
        public class EntitySnapshot
        {
            public Guid ID { get; set; }
            public string Name { get; set; }
            public int Generation { get; set; }
            public double Energy { get; set; }
            public double Resources { get; set; }
            public string CurrentJobName { get; set; }
            public bool IsUnfavored { get; set; }
            public List<string> AppliedMagicNames { get; set; }

            public EntitySnapshot(Entity entity)
            {
                ID = entity.ID;
                Name = entity.Name;
                Generation = entity.Generation;
                Energy = entity.Energy;
                Resources = entity.Resources;
                CurrentJobName = entity.CurrentJob?.Name ?? "None";
                IsUnfavored = entity.IsUnfavored;
                AppliedMagicNames = entity.AppliedMagics.Select(m => m.Name).ToList();
            }
        }

        /// <summary>
        /// 記録された歴史をコンソールに出力します。
        /// </summary>
        public void PrintChronicle()
        {
            Console.WriteLine("\n--- 3000 Year Chronicle (Excerpt) ---");
            // 全てのログを出力すると膨大になるため、主要なイベントや節目のみ出力
            foreach (var entry in ChronicleEntries)
            {
                if (entry.EventLog.Contains("[UNFAVORED EVENT]") || entry.EventLog.Contains("[MAGIC DISCOVERY]") ||
                    entry.EventLog.Contains("new entities born") || entry.EventLog.Contains("perished") ||
                    entry.Turn % 100 == 0 || entry.Turn == 1001 || entry.Turn == 3000)
                {
                    Console.WriteLine($"Turn {entry.Turn} (Gen {entry.Generation}, Pop: {entry.PopulationCount}, Unfavored E: {entry.UnfavoredBranchEnergy:F0}): {entry.EventLog}");
                }
            }
            Console.WriteLine("-------------------------------------\n");
        }
    }

    /// <summary>
    /// シミュレーションのメインロジックを管理するクラス。
    /// </summary>
    public class SimulationCore
    {
        private WorldState _worldState;
        private SimulationHistory _history;
        private Random _random = new Random();

        public SimulationCore(int startTurn = 1)
        {
            _worldState = new WorldState(startTurn);
            _history = new SimulationHistory();
            Console.WriteLine($"Simulation initialized. Starting at Turn {startTurn}, Generation {_worldState.CurrentGeneration}.");
        }

        /// <summary>
        /// 指定されたターン範囲でシミュレーションを連続自律進行させます。
        /// </summary>
        /// <param name="startTurn">シミュレーションを開始するターン。</param>
        /// <param name="endTurn">シミュレーションを終了するターン。</param>
        public void RunSimulation(int startTurn, int endTurn)
        {
            // 初期ターンが指定された開始ターンと異なる場合、ワールドステートを調整
            if (_worldState.CurrentTurn < startTurn)
            {
                Console.WriteLine($"Adjusting simulation to start from Turn {startTurn}. Current state is Turn {_worldState.CurrentTurn}.");
                _worldState.CurrentTurn = startTurn;
                _worldState.CurrentGeneration = (startTurn - 1) / WorldState.TURNS_PER_GENERATION + 1;
            }

            Console.WriteLine($"Starting continuous autonomous simulation from Turn {startTurn} to {endTurn}...");

            for (int turn = startTurn; turn <= endTurn; turn++)
            {
                _worldState.CurrentTurn = turn; // ターンを明示的に設定
                _worldState.CurrentGeneration = (turn - 1) / WorldState.TURNS_PER_GENERATION + 1;

                string turnLog = $"Turn {turn} (Gen {_worldState.CurrentGeneration}) processed.";
                bool success = SafeFailMechanism.ExecuteSafely(() =>
                {
                    ProcessTurn(turn);
                }, $"processing turn {turn}", () =>
                {
                    turnLog = $"Turn {turn} (Gen {_worldState.CurrentGeneration}) failed to process completely. Attempting recovery...";
                    // 失敗時のフォールバック処理（例: シミュレーションの一時停止、エラー状態の記録、状態のロールバック）
                    // ここでは簡単なログ記録と、次のターンへの進行を試みる。
                });

                _history.RecordTurn(turn, _worldState.CurrentGeneration, _worldState, turnLog);

                // 進行状況の表示
                if (turn % 100 == 0 || turn == endTurn || turn == startTurn)
                {
                    Console.WriteLine($"--- Simulation Progress: Turn {turn}/{endTurn} (Gen {_worldState.CurrentGeneration}, Pop: {_worldState.Entities.Count}, Unfavored E: {_worldState.UnfavoredBranchEnergy:F0}) ---");
                }
            }

            Console.WriteLine($"Simulation completed for turns {startTurn} to {endTurn}.");
            _history.PrintChronicle(); // 最終的な正史を出力
        }

        /// <summary>
        /// 各ターンのシミュレーションロジックを実行します。
        /// </summary>
        /// <param name="currentTurn">現在のターン。</param>
        private void ProcessTurn(int currentTurn)
        {
            // 1. エンティティの行動と状態更新
            // ToList()でコレクションのコピーを作成し、ループ中に要素が削除されても安全にする
            foreach (var entity in _worldState.Entities.ToList())
            {
                SafeFailMechanism.ExecuteSafely(() =>
                {
                    entity.ApplyJobEffect(_worldState); // 職業の効果を適用
                    entity.ConsumeEnergy(1.0 + (_random.NextDouble() * 0.5)); // 基本的なエネルギー消費にランダム要素

                    // エネルギーが尽きたエンティティの処理
                    if (entity.Energy <= 0)
                    {
                        Console.WriteLine($"  Entity '{entity.Name}' (Gen {entity.Generation}) ran out of energy and perished.");
                        _worldState.Entities.Remove(entity);
                        _history.RecordTurn(currentTurn, _worldState.CurrentGeneration, _worldState, $"Entity '{entity.Name}' perished.");
                    }
                    else if (entity.Resources < 0) // リソースがマイナスになった場合もペナルティ
                    {
                        entity.Energy -= 5.0; // エネルギーをさらに消費
                        Console.WriteLine($"  Entity '{entity.Name}' (Gen {entity.Generation}) has negative resources, losing energy.");
                    }
                }, $"entity action for {entity.Name} in turn {currentTurn}");
            }

            // 2. 不遇枝エネルギー蓄積モデルの適用
            ApplyUnfavoredBranchEnergyAccumulationModel(currentTurn);

            // 3. 新しいエンティティの誕生（世代交代のシミュレーション）
            // 世代の終わり、または人口が減りすぎた場合に新しいエンティティを生成
            if ((currentTurn % WorldState.TURNS_PER_GENERATION == 0 && _worldState.Entities.Any()) || _worldState.Entities.Count < 5)
            {
                int nextGeneration = _worldState.CurrentGeneration + 1;
                int newEntitiesCount = Math.Max(1, _worldState.Entities.Count / 2); // 現在の半数程度を新規生成
                if (_worldState.Entities.Count < 5) newEntitiesCount = Math.Max(newEntitiesCount, 3); // 最低3体は生成

                for (int i = 0; i < newEntitiesCount; i++)
                {
                    bool isUnfavored = (_random.Next(10) < 3); // 約30%が不遇枝として生まれる例
                    string newName = $"Newborn_{nextGeneration}_{_random.Next(1000)}";
                    Entity newEntity = new Entity(newName, nextGeneration, isUnfavored);
                    newEntity.CurrentJob = _worldState.AvailableJobs[_random.Next(_worldState.AvailableJobs.Count)]; // ランダムにジョブ割り当て
                    _worldState.Entities.Add(newEntity);
                    _history.RecordTurn(currentTurn, _worldState.CurrentGeneration, _worldState, $"New entity '{newName}' (Gen {nextGeneration}, Unfavored: {isUnfavored}) born.");
                }
                Console.WriteLine($"--- Generation {_worldState.CurrentGeneration} ends. {newEntitiesCount} new entities born for Gen {nextGeneration}. ---");
            }

            // 4. マジック（社会技術）の発見と適用（簡易版）
            // 不遇枝エネルギーが閾値を超えた場合に新しいマジックが発見される可能性
            if (_worldState.UnfavoredBranchEnergy >= WorldState.UNFAVORED_ENERGY_THRESHOLD)
            {
                TriggerUnfavoredBranchEvent(currentTurn);
                _worldState.UnfavoredBranchEnergy = 0; // イベント後リセット
            }

            // 既存のマジックをランダムなエンティティに適用する例
            if (_worldState.AvailableMagics.Any() && _worldState.Entities.Any() && _random.Next(100) < 10) // 各ターン10%の確率で適用を試みる
            {
                var randomMagic = _worldState.AvailableMagics[_random.Next(_worldState.AvailableMagics.Count)];
                var randomEntity = _worldState.Entities[_random.Next(_worldState.Entities.Count)];
                ApplyMagic(randomMagic, randomEntity);
            }
        }

        /// <summary>
        /// 不遇枝エネルギー蓄積モデルを適用します。
        /// 不遇なエンティティの存在がエネルギーを蓄積し、閾値を超えるとイベントをトリガーします。
        /// </summary>
        /// <param name="currentTurn">現在のターン。</param>
        private void ApplyUnfavoredBranchEnergyAccumulationModel(int currentTurn)
        {
            SafeFailMechanism.ExecuteSafely(() =>
            {
                var unfavoredEntities = _worldState.Entities.Where(e => e.IsUnfavored).ToList();
                if (unfavoredEntities.Any())
                {
                    // 不遇なエンティティの数や状態に応じてエネルギーを蓄積
                    // エネルギーが低いほど、リソースが少ないほど、蓄積が加速する
                    double accumulationRate = unfavoredEntities.Count * 0.5;
                    accumulationRate += unfavoredEntities.Sum(e => Math.Max(0, 100 - e.Energy) * 0.1); // エネルギーが低いほど
                    accumulationRate += unfavoredEntities.Sum(e => Math.Max(0, 50 - e.Resources) * 0.2); // リソースが低いほど

                    _worldState.UnfavoredBranchEnergy += accumulationRate;
                    //Console.WriteLine($"  Unfavored Branch Energy: {_worldState.UnfavoredBranchEnergy:F2} (Accumulated: {accumulationRate:F2})");

                    if (_worldState.UnfavoredBranchEnergy >= WorldState.UNFAVORED_ENERGY_THRESHOLD)
                    {
                        Console.WriteLine($"[UNFAVORED EVENT] Unfavored Branch Energy reached threshold ({_worldState.UnfavoredBranchEnergy:F2}) at Turn {currentTurn}!");
                        TriggerUnfavoredBranchEvent(currentTurn);
                        _worldState.UnfavoredBranchEnergy = 0; // イベント後リセット
                    }
                }
            }, $"applying unfavored branch energy model in turn {currentTurn}");
        }

        /// <summary>
        /// 不遇枝エネルギーが閾値を超えた際に発生するイベントを処理します。
        /// </summary>
        /// <param name="currentTurn">現在のターン。</param>
        private void TriggerUnfavoredBranchEvent(int currentTurn)
        {
            // 不遇枝イベントの具体的なロジック
            // 例: 新しい魔法の発見、既存のジョブの変革、不遇なエンティティの強化、社会構造の変化など
            string eventDescription = "A significant shift occurred due to accumulated grievances.";

            int eventType = _random.Next(4); // 0: 新しい魔法発見, 1: ジョブ変革, 2: 不遇エンティティ強化, 3: 社会構造変化

            switch (eventType)
            {
                case 0:
                    DiscoverNewMagic();
                    eventDescription = "Accumulated Unfavored Energy led to the discovery of a revolutionary new Magic!";
                    break;
                case 1:
                    TransformRandomJob();
                    eventDescription = "A long-standing Job was fundamentally transformed by the Unfavored Branch's influence!";
                    break;
                case 2:
                    EmpowerUnfavoredEntities();
                    eventDescription = "The Unfavored Branch gained strength, empowering its members!";
                    break;
                case 3:
                    ShiftSocialStructure();
                    eventDescription = "The social structure underwent a significant shift, altering the balance of power!";
                    break;
            }

            _history.RecordTurn(currentTurn, _worldState.CurrentGeneration, _worldState, $"[UNFAVORED EVENT] {eventDescription}");
        }

        /// <summary>
        /// 新しい魔法を発見し、利用可能な魔法リストに追加します。
        /// </summary>
        private void DiscoverNewMagic()
        {
            string newMagicName = $"Advanced Tech {_random.Next(1000, 9999)}";
            string newMagicDesc = $"A breakthrough technology from Generation {_worldState.CurrentGeneration}.";
            double cost = _random.Next(30, 100);
            int minGen = _worldState.CurrentGeneration;
            bool isDestructive = _random.Next(10) == 0; // 10%の確率で破壊的

            Magic newMagic = new Magic(newMagicName, newMagicDesc, cost, minGen, isDestructive);
            _worldState.AvailableMagics.Add(newMagic);
            Console.WriteLine($"[MAGIC DISCOVERY] A new Magic '{newMagic.Name}' was discovered!");
            _history.RecordTurn(_worldState.CurrentTurn, _worldState.CurrentGeneration, _worldState, $"New Magic '{newMagic.Name}' discovered.");
        }

        /// <summary>
        /// ランダムなジョブを変革します。
        /// </summary>
        private void TransformRandomJob()
        {
            if (!_worldState.AvailableJobs.Any()) return;

            var jobToTransform = _worldState.AvailableJobs[_random.Next(_worldState.AvailableJobs.Count)];
            jobToTransform.ResourceGenerationRate *= (1.0 + _random.NextDouble() * 0.5); // 効率アップ
            jobToTransform.EnergyConsumptionRate *= (0.5 + _random.NextDouble() * 0.5); // 消費ダウン
            jobToTransform.Name = $"Reformed {jobToTransform.Name}";
            jobToTransform.Description += " (Transformed by Unfavored Influence)";
            Console.WriteLine($"[JOB TRANSFORMATION] Job '{jobToTransform.Name}' was reformed!");
            _history.RecordTurn(_worldState.CurrentTurn, _worldState.CurrentGeneration, _worldState, $"Job '{jobToTransform.Name}' transformed.");
        }

        /// <summary>
        /// 不遇なエンティティを強化します。
        /// </summary>
        private void EmpowerUnfavoredEntities()
        {
            foreach (var entity in _worldState.Entities.Where(e => e.IsUnfavored))
            {
                entity.GainEnergy(50.0 + _random.NextDouble() * 50);
                entity.GainResources(20.0 + _random.NextDouble() * 30);
                Console.WriteLine($"  Entity '{entity.Name}' (Unfavored) gained significant energy and resources!");
            }
            _history.RecordTurn(_worldState.CurrentTurn, _worldState.CurrentGeneration, _worldState, "Unfavored entities received a surge of power!");
        }

        /// <summary>
        /// 社会構造を変化させます。例として、不遇枝の割合を変化させます。
        /// </summary>
        private void ShiftSocialStructure()
        {
            // 不遇枝の割合をランダムに変化させる
            foreach (var entity in _worldState.Entities)
            {
                if (_random.Next(10) < 2) // 20%の確率で状態が変化
                {
                    entity.IsUnfavored = !entity.IsUnfavored;
                    Console.WriteLine($"  Entity '{entity.Name}' status changed to {(entity.IsUnfavored ? "Unfavored" : "Favored")}.");
                }
            }
            _history.RecordTurn(_worldState.CurrentTurn, _worldState.CurrentGeneration, _worldState, "Social structure shifted, altering favored/unfavored status.");
        }

        /// <summary>
        /// 魔法（社会技術）を適用します。MagicSanitizerEngineによる検証を必ず行います。
        /// </summary>
        /// <param name="magic">適用する魔法。</param>
        /// <param name="targetEntity">魔法の対象となるエンティティ。</param>
        private void ApplyMagic(Magic magic, Entity targetEntity)
        {
            if (magic == null || targetEntity == null) return;

            bool success = SafeFailMechanism.ExecuteSafely(() =>
            {
                if (MagicSanitizerEngine.Sanitize(magic, _worldState, targetEntity))
                {
                    if (targetEntity.Resources >= magic.CostResources)
                    {
                        magic.ApplyEffect(_worldState, targetEntity);
                        _history.RecordTurn(_worldState.CurrentTurn, _worldState.CurrentGeneration, _worldState,
                            $"Magic '{magic.Name}' applied to '{targetEntity.Name}'.");
                    }
                    else
                    {
                        Console.WriteLine($"[MAGIC FAILED] '{magic.Name}' could not be applied to '{targetEntity.Name}': Insufficient resources.");
                    }
                }
                else
                {
                    Console.WriteLine($"[MAGIC FAILED] '{magic.Name}' could not be applied to '{targetEntity.Name}': Sanitization failed.");
                }
            }, $"applying magic {magic.Name} to {targetEntity.Name}");
        }
    }

    // メインエントリポイントの例 (Program.cs などに配置)
    /*
    public class Program
    {
        public static void Main(string[] args)
        {
            // ターン1001からターン3000までの2000年間（第21〜60世代）をシミュレーション
            // WorldState.TURNS_PER_GENERATION = 50 と仮定
            // ターン1001は (1001-1)/50 + 1 = 20 + 1 = 第21世代
            // ターン3000は (3000-1)/50 + 1 = 59 + 1 = 第60世代
            
            Console.OutputEncoding = Encoding.UTF8; // コンソール出力の文字化け防止
            
            SimulationCore simulator = new SimulationCore(1001); // ターン1001から開始
            simulator.RunSimulation(1001, 3000); // ターン1001からターン3000まで実行
        }
    }
    */
}
```
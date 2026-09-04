はい、承知いたしました。
「フロム風戦闘×ブラインド熱科学クラフト×千年史自律シミュレーター」のリードディレクター兼C#設計者として、T1050からT1250までの連続進行と剪定理論による「太いルート」コミットを実装するための、精密なC#コード指示プロンプトを生成します。

このプロンプトは、Cursor(IDE)のCtrl+Lにそのまま読み込ませてC#コード化できる形式です。Safe-Fail構造、MagicSanitizerEngine、Job/Magicの定義規約（魔法=社会技術、ジョブ=生活職業）を厳格に守ります。

---

```csharp
// Global using directives (if C# 10+ is used, otherwise add individual using statements)
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions; // For MagicSanitizerEngine
using System.Threading.Tasks; // Potentially for async operations, though not strictly used in this core structure

namespace FromSoftLikeSimulator
{
    /// <summary>
    /// SafeResult<T> 構造体: 操作の成功/失敗とその結果、またはエラーメッセージをカプセル化します。
    /// Safe-Fail構造の基盤となります。
    /// </summary>
    public struct SafeResult<T>
    {
        public bool IsSuccess { get; }
        public T Value { get; }
        public string ErrorMessage { get; }

        private SafeResult(bool isSuccess, T value, string errorMessage)
        {
            IsSuccess = isSuccess;
            Value = value;
            ErrorMessage = errorMessage;
        }

        /// <summary>
        /// 成功した結果を生成します。
        /// </summary>
        public static SafeResult<T> Success(T value) => new SafeResult<T>(true, value, null);

        /// <summary>
        /// 失敗した結果を生成します。
        /// </summary>
        public static SafeResult<T> Fail(string errorMessage) => new SafeResult<T>(false, default(T), errorMessage);

        /// <summary>
        /// 操作が失敗した場合に例外をスローします。
        /// </summary>
        public void ThrowIfFail()
        {
            if (!IsSuccess)
            {
                throw new InvalidOperationException(ErrorMessage);
            }
        }
    }

    /// <summary>
    /// MagicSanitizerEngine クラス: ゲームのデータ整合性とセキュリティを確保するための静的ユーティリティ。
    /// 特に、外部からの入力や設定ファイルからロードされるデータに対して適用されます。
    /// </summary>
    public static class MagicSanitizerEngine
    {
        /// <summary>
        /// 文字列（名前など）をサニタイズします。空白のトリム、複数スペースの置換、長さ制限、不正文字チェックを行います。
        /// </summary>
        /// <param name="input">サニタイズする文字列。</param>
        /// <param name="fieldName">エラーメッセージに含めるフィールド名。</param>
        /// <param name="maxLength">許容される最大長。</param>
        /// <returns>サニタイズされた文字列を含むSafeResult。</returns>
        public static SafeResult<string> SanitizeName(string input, string fieldName, int maxLength = 128)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                return SafeResult<string>.Fail($"[{fieldName}] 名前はNullまたは空にできません。");
            }
            string sanitized = input.Trim();
            sanitized = Regex.Replace(sanitized, @"\s+", " "); // 複数のスペースを単一のスペースに置換

            if (sanitized.Length > maxLength)
            {
                return SafeResult<string>.Fail($"[{fieldName}] 名前が最大長（{maxLength}文字）を超えています。");
            }
            // HTMLエンティティやSQLインジェクションに繋がりうる文字をチェック
            if (Regex.IsMatch(sanitized, @"[<>&""';\\]"))
            {
                return SafeResult<string>.Fail($"[{fieldName}] 名前に不正な文字が含まれています。");
            }
            return SafeResult<string>.Success(sanitized);
        }

        /// <summary>
        /// 正の整数値をサニタイズします。最小値チェックを行います。
        /// </summary>
        /// <param name="input">サニタイズする整数値。</param>
        /// <param name="fieldName">エラーメッセージに含めるフィールド名。</param>
        /// <param name="minValue">許容される最小値。</param>
        /// <returns>サニタイズされた整数値を含むSafeResult。</returns>
        public static SafeResult<int> SanitizePositiveInt(int input, string fieldName, int minValue = 0)
        {
            if (input < minValue)
            {
                return SafeResult<int>.Fail($"[{fieldName}] 値は {minValue} 以上である必要があります。現在の値: {input}。");
            }
            return SafeResult<int>.Success(input);
        }

        /// <summary>
        /// 浮動小数点数値をサニタイズします。範囲チェックを行います。
        /// </summary>
        /// <param name="input">サニタイズする浮動小数点数値。</param>
        /// <param name="fieldName">エラーメッセージに含めるフィールド名。</param>
        /// <param name="minValue">許容される最小値。</param>
        /// <param name="maxValue">許容される最大値。</param>
        /// <returns>サニタイズされた浮動小数点数値を含むSafeResult。</returns>
        public static SafeResult<float> SanitizeFloatRange(float input, string fieldName, float minValue = float.MinValue, float maxValue = float.MaxValue)
        {
            if (input < minValue || input > maxValue)
            {
                return SafeResult<float>.Fail($"[{fieldName}] 値は {minValue} から {maxValue} の範囲である必要があります。現在の値: {input}。");
            }
            return SafeResult<float>.Success(input);
        }

        // 必要に応じて、より複雑なオブジェクトや列挙型に対するサニタイズメソッドを追加できます。
    }

    /// <summary>
    /// Magic (魔法) クラス: ゲーム内では「社会技術」として定義されます。
    /// 人類の組織、知識、ツールの進歩を表し、社会の形態を形成します。
    /// </summary>
    public class Magic
    {
        public string Id { get; private set; }
        public string Name { get; private set; }
        public string Description { get; private set; }
        public int UnlockedYear { get; private set; } // この社会技術が最初に広く採用/発明された年
        public Dictionary<string, float> WorldStateImpact { get; private set; } // 例: "FoodProduction": 0.1, "PopulationGrowth": 0.05
        public List<string> PrerequisiteMagics { get; private set; } // この社会技術をアンロックするために必要な他の社会技術のID

        private Magic(string id, string name, string description, int unlockedYear, Dictionary<string, float> worldStateImpact, List<string> prerequisiteMagics)
        {
            Id = id; // Sanitization already done in factory
            Name = name;
            Description = description;
            UnlockedYear = unlockedYear;
            WorldStateImpact = worldStateImpact ?? new Dictionary<string, float>();
            PrerequisiteMagics = prerequisiteMagics ?? new List<string>();
        }

        /// <summary>
        /// Magicオブジェクトを安全に作成するためのファクトリメソッド。MagicSanitizerEngineを使用します。
        /// </summary>
        public static SafeResult<Magic> Create(string id, string name, string description, int unlockedYear, Dictionary<string, float> worldStateImpact, List<string> prerequisiteMagics)
        {
            var idResult = MagicSanitizerEngine.SanitizeName(id, nameof(Id));
            if (!idResult.IsSuccess) return SafeResult<Magic>.Fail(idResult.ErrorMessage);

            var nameResult = MagicSanitizerEngine.SanitizeName(name, nameof(Name));
            if (!nameResult.IsSuccess) return SafeResult<Magic>.Fail(nameResult.ErrorMessage);

            var yearResult = MagicSanitizerEngine.SanitizePositiveInt(unlockedYear, nameof(UnlockedYear), 1);
            if (!yearResult.IsSuccess) return SafeResult<Magic>.Fail(yearResult.ErrorMessage);

            return SafeResult<Magic>.Success(new Magic(idResult.Value, nameResult.Value, description, yearResult.Value, worldStateImpact, prerequisiteMagics));
        }
    }

    /// <summary>
    /// Job (ジョブ) クラス: ゲーム内では「生活職業」として定義されます。
    /// 個人が社会内で担う役割を表し、社会の機能に貢献します。
    /// </summary>
    public class Job
    {
        public string Id { get; private set; }
        public string Name { get; private set; }
        public string Description { get; private set; }
        public Dictionary<string, float> ResourceContribution { get; private set; } // 例: "Food": 1.0, "Labor": 1.0
        public List<string> RequiredMagics { get; private set; } // このジョブを可能にするために必要な社会技術のID
        public List<string> RequiredSkills { get; private set; } // このジョブに必要な個人のスキル

        private Job(string id, string name, string description, Dictionary<string, float> resourceContribution, List<string> requiredMagics, List<string> requiredSkills)
        {
            Id = id; // Sanitization already done in factory
            Name = name;
            Description = description;
            ResourceContribution = resourceContribution ?? new Dictionary<string, float>();
            RequiredMagics = requiredMagics ?? new List<string>();
            RequiredSkills = requiredSkills ?? new List<string>();
        }

        /// <summary>
        /// Jobオブジェクトを安全に作成するためのファクトリメソッド。MagicSanitizerEngineを使用します。
        /// </summary>
        public static SafeResult<Job> Create(string id, string name, string description, Dictionary<string, float> resourceContribution, List<string> requiredMagics, List<string> requiredSkills)
        {
            var idResult = MagicSanitizerEngine.SanitizeName(id, nameof(Id));
            if (!idResult.IsSuccess) return SafeResult<Job>.Fail(idResult.ErrorMessage);

            var nameResult = MagicSanitizerEngine.SanitizeName(name, nameof(Name));
            if (!nameResult.IsSuccess) return SafeResult<Job>.Fail(nameResult.ErrorMessage);

            return SafeResult<Job>.Success(new Job(idResult.Value, nameResult.Value, description, resourceContribution, requiredMagics, requiredSkills));
        }
    }

    /// <summary>
    /// WorldState クラス: シミュレーションにおける世界の現在の状態を保持します。
    /// </summary>
    public class WorldState
    {
        public int CurrentYear { get; set; }
        public Dictionary<string, float> Resources { get; set; } // 例: 食料、資材、知識、影響力
        public Dictionary<string, int> PopulationDistribution { get; set; } // 例: "Farmer": 100, "Soldier": 50
        public HashSet<string> DiscoveredMagics { get; set; } // アンロックされた社会技術のID
        public Dictionary<string, float> GlobalParameters { get; set; } // 例: "Stability" (安定度), "TechnologicalProgressRate" (技術進歩率)
        public List<HistoricalEvent> RecentEvents { get; set; } // 現在の年に発生したイベントのログ

        public WorldState(int startYear)
        {
            CurrentYear = startYear;
            Resources = new Dictionary<string, float>
            {
                { "Food", 5000f }, { "Materials", 2000f }, { "Knowledge", 500f }, { "Influence", 200f }
            };
            PopulationDistribution = new Dictionary<string, int>
            {
                { "Farmer", 1000 }, { "Laborer", 500 } // 初期人口設定
            };
            DiscoveredMagics = new HashSet<string>
            {
                "BasicFarming", // 基本的な農業技術は初期からあると仮定
                "BasicMining"   // 基本的な採掘技術も初期からあると仮定
            };
            GlobalParameters = new Dictionary<string, float>
            {
                { "Stability", 0.7f }, { "TechnologicalProgressRate", 0.01f }, { "PopulationGrowthRate", 0.005f }
            };
            RecentEvents = new List<HistoricalEvent>();
        }

        /// <summary>
        /// 発見されたMagicがWorldStateに与える影響を適用します。
        /// </summary>
        public void ApplyMagicImpact(Magic magic)
        {
            foreach (var impact in magic.WorldStateImpact)
            {
                if (Resources.ContainsKey(impact.Key))
                {
                    Resources[impact.Key] += impact.Value;
                }
                else if (GlobalParameters.ContainsKey(impact.Key))
                {
                    GlobalParameters[impact.Key] += impact.Value;
                }
                // 他のWorldState変数への影響もここに追加できます。
            }
        }

        /// <summary>
        /// Jobによる資源貢献をWorldStateに適用します。
        /// </summary>
        public void ApplyJobContribution(Job job, int count)
        {
            foreach (var contribution in job.ResourceContribution)
            {
                if (Resources.ContainsKey(contribution.Key))
                {
                    Resources[contribution.Key] += contribution.Value * count;
                }
            }
        }

        /// <summary>
        /// WorldStateの資源値を最小値0でクランプします。
        /// </summary>
        public void ClampResources()
        {
            foreach (var key in Resources.Keys.ToList())
            {
                if (Resources[key] < 0) Resources[key] = 0;
            }
        }
    }

    /// <summary>
    /// PlayerState クラス: プレイヤーの現在の状態を保持します。
    /// </summary>
    public class PlayerState
    {
        public string PlayerName { get; private set; }
        public Dictionary<string, float> PlayerResources { get; set; } // 例: 個人の富、ユニークアイテム
        public List<string> UnlockedCraftingRecipes { get; set; }
        public List<string> PlayerSkills { get; set; } // 例: 戦闘スキル、クラフトスキル、外交スキル
        public int CurrentYear { get; set; } // プレイヤーが認識する時間（WorldStateと同期）

        public PlayerState(string playerName)
        {
            PlayerName = MagicSanitizerEngine.SanitizeName(playerName, nameof(PlayerName)).Value;
            PlayerResources = new Dictionary<string, float>();
            UnlockedCraftingRecipes = new List<string>();
            PlayerSkills = new List<string>();
            CurrentYear = 1050; // プレイヤーの直接的な関与開始年
        }
    }

    /// <summary>
    /// HistoricalEvent クラス: シミュレーション中に発生する歴史的イベントを定義します。
    /// </summary>
    public class HistoricalEvent
    {
        public string Id { get; private set; }
        public string Title { get; private set; }
        public string Description { get; private set; }
        public int Year { get; private set; }
        public EventType Type { get; private set; }
        public Dictionary<string, float> WorldStateChanges { get; private set; } // 世界の状態への直接的な変更
        public List<string> TriggerConditions { get; private set; } // イベント発生のトリガー条件 (例: "Magic:Feudalism_Discovered", "Resource:Food_Low")
        public List<string> PossibleOutcomes { get; private set; } // 剪定理論のための可能な結果パス

        public enum EventType { Major, Minor, PlayerChoice, Combat, Crafting, Commitment } // Commitmentは太いルートコミットイベント用

        private HistoricalEvent(string id, string title, string description, int year, EventType type, Dictionary<string, float> worldStateChanges, List<string> triggerConditions, List<string> possibleOutcomes)
        {
            Id = id; // Sanitization already done in factory
            Title = title;
            Description = description;
            Year = year;
            Type = type;
            WorldStateChanges = worldStateChanges ?? new Dictionary<string, float>();
            TriggerConditions = triggerConditions ?? new List<string>();
            PossibleOutcomes = possibleOutcomes ?? new List<string>();
        }

        /// <summary>
        /// HistoricalEventオブジェクトを安全に作成するためのファクトリメソッド。MagicSanitizerEngineを使用します。
        /// </summary>
        public static SafeResult<HistoricalEvent> Create(string id, string title, string description, int year, EventType type, Dictionary<string, float> worldStateChanges, List<string> triggerConditions, List<string> possibleOutcomes)
        {
            var idResult = MagicSanitizerEngine.SanitizeName(id, nameof(Id));
            if (!idResult.IsSuccess) return SafeResult<HistoricalEvent>.Fail(idResult.ErrorMessage);

            var titleResult = MagicSanitizerEngine.SanitizeName(title, nameof(Title));
            if (!titleResult.IsSuccess) return SafeResult<HistoricalEvent>.Fail(titleResult.ErrorMessage);

            var yearResult = MagicSanitizerEngine.SanitizePositiveInt(year, nameof(Year), 1);
            if (!yearResult.IsSuccess) return SafeResult<HistoricalEvent>.Fail(yearResult.ErrorMessage);

            return SafeResult<HistoricalEvent>.Success(new HistoricalEvent(idResult.Value, titleResult.Value, description, yearResult.Value, type, worldStateChanges, triggerConditions, possibleOutcomes));
        }
    }

    /// <summary>
    /// TimelineNode クラス: シミュレーションの特定の時点における世界の状態とイベントを表現します。
    /// 剪定理論において、異なる未来のパスを表現するために使用される可能性があります。
    /// </summary>
    public class TimelineNode
    {
        public int Year { get; }
        public WorldState StateAtNode { get; }
        public List<HistoricalEvent> EventsThisYear { get; }
        public List<TimelineNode> PotentialFutures { get; } // 剪定理論のための可能な未来の分岐

        public TimelineNode(int year, WorldState state)
        {
            Year = year;
            StateAtNode = state;
            EventsThisYear = new List<HistoricalEvent>();
            PotentialFutures = new List<TimelineNode>();
        }
    }

    /// <summary>
    /// ICombatSystem インターフェース: フロム風戦闘システムの抽象化。
    /// </summary>
    public interface ICombatSystem
    {
        /// <summary>
        /// 戦闘を開始し、その結果を返します。
        /// </summary>
        /// <param name="context">戦闘の状況（プレイヤー、敵、環境など）。</param>
        /// <returns>戦闘の成功/失敗と結果。</returns>
        SafeResult<CombatOutcome> InitiateCombat(CombatContext context);
    }

    /// <summary>
    /// CombatContext クラス: 戦闘の入力パラメータを保持します。
    /// </summary>
    public class CombatContext { /* プレイヤーのステータス、敵のステータス、環境要因などを定義 */ }

    /// <summary>
    /// CombatOutcome クラス: 戦闘の結果を保持します。
    /// </summary>
    public class CombatOutcome { /* 勝利/敗北、報酬、受けたダメージなどを定義 */ }

    /// <summary>
    /// ICraftingSystem インターフェース: ブラインド熱科学クラフトシステムの抽象化。
    /// </summary>
    public interface ICraftingSystem
    {
        /// <summary>
        /// クラフトを試行し、その結果を返します。
        /// </summary>
        /// <param name="recipe">クラフトレシピ。</param>
        /// <param name="playerState">プレイヤーの状態。</param>
        /// <param name="worldState">世界の現在の状態。</param>
        /// <returns>クラフトの成功/失敗と結果。</returns>
        SafeResult<CraftingOutcome> AttemptCraft(CraftingRecipe recipe, PlayerState playerState, WorldState worldState);
    }

    /// <summary>
    /// CraftingRecipe クラス: クラフトのレシピを定義します。
    /// </summary>
    public class CraftingRecipe { /* 必要素材、ツール、Magic前提条件などを定義 */ }

    /// <summary>
    /// CraftingOutcome クラス: クラフトの結果を保持します。
    /// </summary>
    public class CraftingOutcome { /* 成功/失敗、作成されたアイテム、消費された資源などを定義 */ }

    /// <summary>
    /// SimulationEngine クラス: 千年史自律シミュレーターのコアロジックを担います。
    /// 時間の進行、Magicの発見、Jobのシフト、イベントのトリガー、そして剪定理論による「太いルート」のコミットを行います。
    /// </summary>
    public class SimulationEngine
    {
        private WorldState _currentWorldState;
        private List<Magic> _allAvailableMagics; // 全ての可能な社会技術
        private List<Job> _allAvailableJobs;     // 全ての可能な生活職業
        private List<HistoricalEvent> _allPotentialEvents; // 発生しうる全てのイベント
        private Dictionary<int, List<HistoricalEvent>> _committedTimeline; // 「太いルート」としてコミットされた歴史

        public SimulationEngine(WorldState initialWorldState, List<Magic> magics, List<Job> jobs, List<HistoricalEvent> events)
        {
            _currentWorldState = initialWorldState;
            _allAvailableMagics = magics;
            _allAvailableJobs = jobs;
            _allPotentialEvents = events;
            _committedTimeline = new Dictionary<int, List<HistoricalEvent>>();
        }

        /// <summary>
        /// 指定された目標年までシミュレーションを年単位で進行させます。
        /// </summary>
        /// <param name="targetYear">シミュレーションを進行させる目標年。</param>
        /// <returns>更新されたWorldStateを含むSafeResult。</returns>
        public SafeResult<WorldState> AdvanceYear(int targetYear)
        {
            if (targetYear <= _currentWorldState.CurrentYear)
            {
                return SafeResult<WorldState>.Fail($"目標年 {targetYear} は現在の年 {_currentWorldState.CurrentYear} より後である必要があります。");
            }

            for (int year = _currentWorldState.CurrentYear + 1; year <= targetYear; year++)
            {
                _currentWorldState.CurrentYear = year;
                SimulateSingleYear();

                // 現在の年のイベントを「太いルート」としてコミット
                if (!_committedTimeline.ContainsKey(year))
                {
                    _committedTimeline[year] = new List<HistoricalEvent>();
                }
                _committedTimeline[year].AddRange(_currentWorldState.RecentEvents);
                _currentWorldState.RecentEvents.Clear(); // 次の年のためにクリア
            }

            return SafeResult<WorldState>.Success(_currentWorldState);
        }

        /// <summary>
        /// 1年間のシミュレーションステップを実行します。
        /// </summary>
        private void SimulateSingleYear()
        {
            // 1. 受動的な世界の変化を適用（資源の増減、人口変動など）
            ApplyPassiveChanges();

            // 2. 新しいMagic（社会技術）の発見を評価
            EvaluateMagicDiscovery();

            // 3. Job（生活職業）の人口分布シフトを評価
            EvaluateJobShifts();

            // 4. 現在の世界の状態と発見されたMagicに基づいて歴史的イベントをトリガー
            TriggerEvents();

            // 5. 剪定理論: 潜在的な未来を評価し、「太いルート」をコミット
            // T1050-T1250の期間では、特定の封建構造、初期産業の進歩、主要な文化シフトなどがコミットされる可能性があります。
            EvaluatePotentialFuturesAndPrune();

            _currentWorldState.ClampResources(); // 資源が負の値にならないようにクランプ
        }

        /// <summary>
        /// 資源の消費/生産、人口成長などの受動的な変化を適用します。
        /// </summary>
        private void ApplyPassiveChanges()
        {
            int totalPopulation = _currentWorldState.PopulationDistribution.Values.Sum();

            // 食料消費
            _currentWorldState.Resources["Food"] -= totalPopulation * 0.1f;

            // 人口成長
            float growthFactor = _currentWorldState.GlobalParameters.GetValueOrDefault("PopulationGrowthRate", 0.005f);
            if (_currentWorldState.Resources["Food"] > totalPopulation * 1.2f) // 食料が豊富なら成長
            {
                totalPopulation = (int)(totalPopulation * (1 + growthFactor));
            }
            else if (_currentWorldState.Resources["Food"] < totalPopulation * 0.8f) // 食料が不足なら減少
            {
                totalPopulation = (int)(totalPopulation * (1 - growthFactor * 2)); // 減少は成長より速い
            }
            // 人口分布を均等に調整する簡易的なロジック（実際はもっと複雑）
            if (totalPopulation > 0)
            {
                int currentTotal = _currentWorldState.PopulationDistribution.Values.Sum();
                if (currentTotal > 0)
                {
                    float ratio = (float)totalPopulation / currentTotal;
                    foreach (var key in _currentWorldState.PopulationDistribution.Keys.ToList())
                    {
                        _currentWorldState.PopulationDistribution[key] = (int)(_currentWorldState.PopulationDistribution[key] * ratio);
                    }
                }
                else // 初期人口が0の場合のフォールバック
                {
                    _currentWorldState.PopulationDistribution["Farmer"] = totalPopulation;
                }
            }
            else
            {
                _currentWorldState.PopulationDistribution.Clear();
            }


            // 知識の蓄積
            _currentWorldState.Resources["Knowledge"] += _currentWorldState.GlobalParameters.GetValueOrDefault("TechnologicalProgressRate", 0.01f);
        }

        /// <summary>
        /// 新しいMagic（社会技術）の発見を評価し、条件が満たされればアンロックします。
        /// </summary>
        private void EvaluateMagicDiscovery()
        {
            foreach (var magic in _allAvailableMagics)
            {
                if (_currentWorldState.DiscoveredMagics.Contains(magic.Id)) continue; // 既に発見済み

                bool prerequisitesMet = true;
                foreach (var prereqId in magic.PrerequisiteMagics)
                {
                    if (!_currentWorldState.DiscoveredMagics.Contains(prereqId))
                    {
                        prerequisitesMet = false;
                        break;
                    }
                }

                // 追加の発見条件（例: 十分な知識資源、特定のグローバルパラメータ、最低年数）
                bool knowledgeCondition = _currentWorldState.Resources.GetValueOrDefault("Knowledge", 0) > 100 * magic.PrerequisiteMagics.Count; // 前提技術数に応じて知識要求が増加
                bool yearCondition = _currentWorldState.CurrentYear >= magic.UnlockedYear;

                if (prerequisitesMet && knowledgeCondition && yearCondition)
                {
                    _currentWorldState.DiscoveredMagics.Add(magic.Id);
                    _currentWorldState.ApplyMagicImpact(magic);
                    _currentWorldState.RecentEvents.Add(HistoricalEvent.Create(
                        $"MagicDiscovered_{magic.Id}_{_currentWorldState.CurrentYear}",
                        $"新社会技術: {magic.Name} 誕生",
                        $"{magic.Name} が発見され、世界の様相が変化しました。",
                        _currentWorldState.CurrentYear,
                        HistoricalEvent.EventType.Major,
                        magic.WorldStateImpact,
                        new List<string>(),
                        new List<string>()
                    ).Value);
                    Console.WriteLine($"T{_currentWorldState.CurrentYear}: 新社会技術発見: {magic.Name}");
                }
            }
        }

        /// <summary>
        /// Job（生活職業）の人口分布を評価し、必要に応じてシフトさせます。
        /// T1050-T1250では、純粋な農耕社会からより専門化された役割への移行、または都市人口の増加などが考えられます。
        /// </summary>
        private void EvaluateJobShifts()
        {
            // これは簡略化された例です。実際のシステムでは、より複雑な人口動態が必要です。
            int totalPopulation = _currentWorldState.PopulationDistribution.Values.Sum();
            if (totalPopulation == 0) return;

            // 食料生産と消費のバランスに基づいて農民の数を調整
            float foodPerCapita = _currentWorldState.Resources.GetValueOrDefault("Food", 0) / totalPopulation;
            int currentFarmers = _currentWorldState.PopulationDistribution.GetValueOrDefault("Farmer", 0);

            if (foodPerCapita < 0.8f) // 食料不足
            {
                // 農民を増やすか、他のジョブから農民にシフト
                int newFarmers = Math.Min(totalPopulation, currentFarmers + (int)(totalPopulation * 0.02f));
                _currentWorldState.PopulationDistribution["Farmer"] = newFarmers;
            }
            else if (foodPerCapita > 1.5f) // 食料過剰
            {
                // 農民を減らし、他のジョブにシフトする機会
                int newFarmers = Math.Max(0, currentFarmers - (int)(totalPopulation * 0.01f));
                _currentWorldState.PopulationDistribution["Farmer"] = newFarmers;

                // 都市化が進んでいれば商人や職人を増やす
                if (_currentWorldState.DiscoveredMagics.Contains("Urbanization"))
                {
                    _currentWorldState.PopulationDistribution["Merchant"] = _currentWorldState.PopulationDistribution.GetValueOrDefault("Merchant", 0) + (int)(totalPopulation * 0.005f);
                    _currentWorldState.PopulationDistribution["Blacksmith"] = _currentWorldState.PopulationDistribution.GetValueOrDefault("Blacksmith", 0) + (int)(totalPopulation * 0.003f);
                }
            }

            // 各ジョブの貢献を適用
            foreach (var jobEntry in _currentWorldState.PopulationDistribution.ToList()) // ToList()で変更中のコレクションエラーを回避
            {
                var job = _allAvailableJobs.FirstOrDefault(j => j.Id == jobEntry.Key);
                if (job != null)
                {
                    _currentWorldState.ApplyJobContribution(job, jobEntry.Value);
                }
            }
        }

        /// <summary>
        /// 現在の世界の状態に基づいて歴史的イベントをトリガーします。
        /// </summary>
        private void TriggerEvents()
        {
            // 条件を満たし、まだ発生していないイベントをフィルタリング
            var potentialEvents = _allPotentialEvents
                .Where(e => e.Year <= _currentWorldState.CurrentYear &&
                            !_committedTimeline.Any(kv => kv.Value.Any(ce => ce.Id == e.Id)))
                .ToList();

            foreach (var ev in potentialEvents)
            {
                bool conditionsMet = true;
                foreach (var condition in ev.TriggerConditions)
                {
                    // 条件のパースと評価の例: "Magic:Feudalism_Discovered", "Resource:Food_Low"
                    if (condition.StartsWith("Magic:"))
                    {
                        string magicId = condition.Substring("Magic:".Length);
                        if (!_currentWorldState.DiscoveredMagics.Contains(magicId))
                        {
                            conditionsMet = false;
                            break;
                        }
                    }
                    else if (condition.StartsWith("Resource:"))
                    {
                        string[] parts = condition.Substring("Resource:".Length).Split('_');
                        if (parts.Length == 2)
                        {
                            string resourceName = parts[0];
                            string thresholdType = parts[1]; // 例: "Low", "High"
                            if (_currentWorldState.Resources.ContainsKey(resourceName))
                            {
                                float value = _currentWorldState.Resources[resourceName];
                                // ゲームバランスに基づいた「Low」と「High」の閾値を定義
                                if (thresholdType == "Low" && value > _currentWorldState.PopulationDistribution.Values.Sum() * 1.0f) conditionsMet = false; // 人口の1倍以上ならLowではない
                                if (thresholdType == "High" && value < _currentWorldState.PopulationDistribution.Values.Sum() * 3.0f) conditionsMet = false; // 人口の3倍未満ならHighではない
                            }
                            else
                            {
                                conditionsMet = false; // 資源が見つからない
                            }
                        }
                    }
                    // 必要に応じて、より複雑な条件を追加
                }

                if (conditionsMet)
                {
                    _currentWorldState.RecentEvents.Add(ev);
                    // イベントの直接的な世界状態変更を適用
                    foreach (var change in ev.WorldStateChanges)
                    {
                        if (_currentWorldState.Resources.ContainsKey(change.Key))
                        {
                            _currentWorldState.Resources[change.Key] += change.Value;
                        }
                        else if (_currentWorldState.GlobalParameters.ContainsKey(change.Key))
                        {
                            _currentWorldState.GlobalParameters[change.Key] += change.Value;
                        }
                    }
                    Console.WriteLine($"T{_currentWorldState.CurrentYear}: イベント発生: {ev.Title}");
                }
            }
        }

        /// <summary>
        /// 「剪定理論」のコアロジック。
        /// 潜在的な未来のパスを評価し、「太いルート」（主要な歴史的経路）をコミットします。
        /// T1050-T1250の期間では、主要な社会構造の固化、特定の技術的軌道のコミット、
        /// または支配的な文化的規範の定義などが含まれます。
        /// </summary>
        private void EvaluatePotentialFuturesAndPrune()
        {
            // これは非常に概念的な部分であり、かなりの設計が必要です。
            // ここでは、蓄積された「影響力」や「安定度」に基づいて単純なコミットをシミュレートします。

            // T1050-T1250における主要な「太いルート」のコミットメントの例:
            // 1. 封建制の統合: 強力な中央集権国家 vs. 分裂した都市国家。
            // 2. 宗教的優位性: 一つの主要な宗教が優位に立つか、宗教的対立の時代。
            // 3. 初期都市化/交易ネットワーク: 都市の成長と長距離交易路の発展。
            // 4. 技術的停滞 vs. 革新: 進歩が遅い時代か、急速で集中的な進歩の時代。
            // 5. 文化的アイデンティティの形成: 独自の国民的または地域的アイデンティティの出現。

            // 簡略化のため、特定の「Magic」（社会技術）が支配的になったり、
            // グローバルパラメータが閾値を超えたりした場合に「太いルート」がコミットされるとします。

            // 例: 「封建制の統合」ルートのコミット
            if (_currentWorldState.DiscoveredMagics.Contains("Feudalism") &&
                _currentWorldState.GlobalParameters.GetValueOrDefault("Stability", 0) > 0.8f &&
                !_committedTimeline.Any(kv => kv.Value.Any(e => e.Id == "FeudalConsolidationCommit")))
            {
                var commitEvent = HistoricalEvent.Create(
                    "FeudalConsolidationCommit",
                    "封建制の確立と統合",
                    "各地で封建領主による統治が確立され、中央集権化への道筋が固まった。これにより、大規模な紛争が減少し、安定期が訪れる。",
                    _currentWorldState.CurrentYear,
                    HistoricalEvent.EventType.Commitment,
                    new Dictionary<string, float> { { "Stability", 0.15f }, { "Influence", 0.1f }, { "TechnologicalProgressRate", -0.002f } }, // 安定するが、革新は鈍化する可能性
                    new List<string>(),
                    new List<string>()
                ).Value;
                _currentWorldState.RecentEvents.Add(commitEvent);
                Console.WriteLine($"T{_currentWorldState.CurrentYear}: 太いルートコミット: {commitEvent.Title}");

                // コミットされた後、特定の他のパスは「剪定」されます（発生しにくくなるか、不可能になる）。
                // 例: 民主的な都市国家への急速な移行は、長期間不可能になるかもしれません。
                // これは、将来のイベントの確率を調整したり、特定のMagicを無効にしたりすることで実現できます。
                // _allPotentialEvents.RemoveAll(e => e.Id == "DemocraticCityStatesRise"); // 例
            }

            // 例: 「初期交易網の確立」ルートのコミット
            if (_currentWorldState.DiscoveredMagics.Contains("MerchantGuilds") &&
                _currentWorldState.Resources.GetValueOrDefault("Materials", 0) > 3000 &&
                !_committedTimeline.Any(kv => kv.Value.Any(e => e.Id == "EarlyTradeNetworkCommit")))
            {
                var commitEvent = HistoricalEvent.Create(
                    "EarlyTradeNetworkCommit",
                    "初期交易網の確立",
                    "商人のギルドが力をつけ、広範な交易網が確立された。これにより、資源の流通が活発化し、都市が発展する。",
                    _currentWorldState.CurrentYear,
                    HistoricalEvent.EventType.Commitment,
                    new Dictionary<string, float> { { "Materials", 0.05f }, { "TechnologicalProgressRate", 0.005f }, { "Influence", 0.05f } },
                    new List<string>(),
                    new List<string>()
                ).Value;
                _currentWorldState.RecentEvents.Add(commitEvent);
                Console.WriteLine($"T{_currentWorldState.CurrentYear}: 太いルートコミット: {commitEvent.Title}");
            }

            // 実際の剪定メカニズムは、以下を含む可能性があります:
            // 1. 「未来の可能性」のセットを生成（例: 数年先まで）。
            // 2. 現在の状態、プレイヤーの選択、コミットされたルートに基づいて、これらの可能性に「重み」または「確率」を割り当てる。
            // 3. 最も重みの高いパスを「太いルート」として選択し、他のパスを破棄/優先度を下げる。
            // これは、`_allPotentialEvents` リストや将来の `Magic` 発見の条件を変更することで実現できます。
        }
    }

    /// <summary>
    /// GameManager クラス: ゲーム全体のオーケストレーター。
    /// シミュレーションの開始、進行、プレイヤーとのインタラクションを管理します。
    /// </summary>
    public class GameManager
    {
        private WorldState _worldState;
        private PlayerState _playerState;
        private SimulationEngine _simulationEngine;
        private ICombatSystem _combatSystem;
        private ICraftingSystem _craftingSystem;

        // ゲームデータのリポジトリ（JSON/XML/DBからロードされることを想定）
        private List<Magic> _allMagics;
        private List<Job> _allJobs;
        private List<HistoricalEvent> _allHistoricalEvents;

        public GameManager(string playerName)
        {
            // コアコンポーネントの初期化
            _worldState = new WorldState(1050); // T1050から開始
            _playerState = new PlayerState(playerName);

            // ゲームデータのロード（実際のデータロードのプレースホルダー）
            _allMagics = LoadMagics();
            _allJobs = LoadJobs();
            _allHistoricalEvents = LoadHistoricalEvents();

            // シミュレーションエンジンの初期化
            _simulationEngine = new SimulationEngine(_worldState, _allMagics, _allJobs, _allHistoricalEvents);

            // プレイヤー向けシステムの初期化（具体的な実装は別途提供される）
            _combatSystem = new PlaceholderCombatSystem();
            _craftingSystem = new PlaceholderCraftingSystem();

            Console.WriteLine($"ゲームがプレイヤー: {playerName} のために初期化されました。開始年: T{_worldState.CurrentYear}");
        }

        /// <summary>
        /// シミュレーションを開始し、指定された終了年まで連続進行させます。
        /// </summary>
        /// <param name="endYear">シミュレーションの終了年。</param>
        /// <returns>シミュレーションの成功/失敗メッセージを含むSafeResult。</returns>
        public SafeResult<string> StartSimulation(int endYear = 1250)
        {
            if (endYear < _worldState.CurrentYear)
            {
                return SafeResult<string>.Fail("終了年は現在の年より後である必要があります。");
            }

            Console.WriteLine($"シミュレーションを T{_worldState.CurrentYear} から T{endYear} まで開始します...");

            while (_worldState.CurrentYear < endYear)
            {
                var advanceResult = _simulationEngine.AdvanceYear(_worldState.CurrentYear + 1);
                if (!advanceResult.IsSuccess)
                {
                    return SafeResult<string>.Fail($"T{_worldState.CurrentYear} でシミュレーションが失敗しました: {advanceResult.ErrorMessage}");
                }

                _playerState.CurrentYear = _worldState.CurrentYear; // プレイヤーの認識時間を同期

                // プレイヤーのインタラクションポイント（例: 10年ごと、または主要イベント後）
                if (_worldState.CurrentYear % 10 == 0 || _worldState.RecentEvents.Any(e => e.Type == HistoricalEvent.EventType.PlayerChoice || e.Type == HistoricalEvent.EventType.Commitment))
                {
                    Console.WriteLine($"\n--- T{_worldState.CurrentYear} ---");
                    Console.WriteLine($"世界資源: 食料={_worldState.Resources["Food"]:F0}, 知識={_worldState.Resources["Knowledge"]:F0}, 資材={_worldState.Resources["Materials"]:F0}");
                    Console.WriteLine($"発見された社会技術: {string.Join(", ", _worldState.DiscoveredMagics)}");
                    Console.WriteLine($"発生イベント: {string.Join(", ", _worldState.RecentEvents.Select(e => e.Title))}");
                    // ここでプレイヤーは選択、戦闘、クラフトなどを促される可能性があります。
                    // 連続シミュレーションの場合は、自動化されるかスキップされます。
                }
            }

            Console.WriteLine($"\nシミュレーションが T{_worldState.CurrentYear} で完了しました。");
            return SafeResult<string>.Success($"シミュレーションは T1050 から T{endYear} まで正常に実行されました。");
        }

        /// <summary>
        /// Magic（社会技術）データをロードするプレースホルダーメソッド。
        /// </summary>
        private List<Magic> LoadMagics()
        {
            var magics = new List<Magic>();
            magics.Add(Magic.Create("BasicFarming", "基礎農耕", "基本的な農耕技術。", 1,
                new Dictionary<string, float> { { "Food", 0.05f } }, new List<string>()).Value);
            magics.Add(Magic.Create("BasicMining", "基礎採掘", "基本的な採掘技術。", 1,
                new Dictionary<string, float> { { "Materials", 0.05f } }, new List<string>()).Value);

            magics.Add(Magic.Create("Feudalism", "封建制", "土地と忠誠に基づく社会構造。地方分権的だが安定をもたらす。", 1000,
                new Dictionary<string, float> { { "Stability", 0.1f }, { "Influence", 0.05f }, { "Knowledge", -0.01f } },
                new List<string>()).Value);
            magics.Add(Magic.Create("EarlyAgricultureImprovements", "初期農業改良", "三圃制や新しい農具の導入により、食料生産が向上。", 1050,
                new Dictionary<string, float> { { "Food", 0.2f }, { "PopulationGrowthRate", 0.01f } },
                new List<string> { "BasicFarming" }).Value);
            magics.Add(Magic.Create("Urbanization", "都市化", "都市の成長と人口集中。新たな職業と知識の集積を促す。", 1100,
                new Dictionary<string, float> { { "Materials", 0.05f }, { "Knowledge", 0.02f }, { "Stability", -0.05f } }, // 都市は資源を生むが、不安定さも伴う
                new List<string> { "Feudalism", "EarlyAgricultureImprovements" }).Value);
            magics.Add(Magic.Create("MerchantGuilds", "商人ギルド", "商業活動を組織化し、広範な交易を促進。富と影響力を生み出す。", 1150,
                new Dictionary<string, float> { { "Influence", 0.1f }, { "Materials", 0.1f }, { "Knowledge", 0.03f } },
                new List<string> { "Urbanization" }).Value);
            magics.Add(Magic.Create("EarlyMetallurgy", "初期冶金術", "鉄器生産の効率化と新たな金属加工技術の発展。", 1180,
                new Dictionary<string, float> { { "Materials", 0.15f }, { "TechnologicalProgressRate", 0.005f } },
                new List<string> { "BasicMining" }).Value);
            magics.Add(Magic.Create("Scholasticism", "スコラ学", "論理と信仰の統合を目指す学問体系。知識の体系化と教育の発展。", 1200,
                new Dictionary<string, float> { { "Knowledge", 0.1f }, { "Stability", 0.05f } },
                new List<string> { "Urbanization" }).Value);
            magics.Add(Magic.Create("EarlyNavigation", "初期航海術", "遠洋航海を可能にする技術と知識。新たな交易路と発見を促す。", 1230,
                new Dictionary<string, float> { { "Influence", 0.08f }, { "Knowledge", 0.04f }, { "Materials", 0.03f } },
                new List<string> { "MerchantGuilds" }).Value);
            return magics;
        }

        /// <summary>
        /// Job（生活職業）データをロードするプレースホルダーメソッド。
        /// </summary>
        private List<Job> LoadJobs()
        {
            var jobs = new List<Job>();
            jobs.Add(Job.Create("Farmer", "農民", "食料を生産し、社会を支える基盤。",
                new Dictionary<string, float> { { "Food", 1.0f }, { "Labor", 1.0f } },
                new List<string> { "BasicFarming" }, new List<string> { "FarmingSkill" }).Value);
            jobs.Add(Job.Create("Laborer", "労働者", "様々な建設や単純労働に従事する。",
                new Dictionary<string, float> { { "Materials", 0.2f }, { "Labor", 1.0f } },
                new List<string>(), new List<string> { "ManualLaborSkill" }).Value);
            jobs.Add(Job.Create("Soldier", "兵士", "秩序を維持し、領土を守る。時には遠征にも参加する。",
                new Dictionary<string, float> { { "Influence", 0.1f }, { "Labor", 0.5f } },
                new List<string> { "Feudalism" }, new List<string> { "CombatSkill" }).Value);
            jobs.Add(Job.Create("Blacksmith", "鍛冶屋", "道具や武器を生産し、社会の技術レベルを支える。",
                new Dictionary<string, float> { { "Materials", 0.5f }, { "Labor", 1.0f } },
                new List<string> { "EarlyMetallurgy" }, new List<string> { "CraftingSkill" }).Value);
            jobs.Add(Job.Create("Merchant", "商人", "資源を流通させ、富を生み出す。都市の発展に不可欠。",
                new Dictionary<string, float> { { "Influence", 0.2f }, { "Materials", 0.2f } },
                new List<string> { "MerchantGuilds" }, new List<string> { "DiplomacySkill", "TradeSkill" }).Value);
            jobs.Add(Job.Create("Scholar", "学者", "知識を研究し、新たな社会技術の発見に貢献する。",
                new Dictionary<string, float> { { "Knowledge", 0.5f }, { "Labor", 0.5f } },
                new List<string> { "Scholasticism" }, new List<string> { "ResearchSkill" }).Value);
            return jobs;
        }

        /// <summary>
        /// HistoricalEvent（歴史的イベント）データをロードするプレースホルダーメソッド。
        /// </summary>
        private List<HistoricalEvent> LoadHistoricalEvents()
        {
            var events = new List<HistoricalEvent>();
            events.Add(HistoricalEvent.Create(
                "GreatFamine1070", "大飢饉 (1070年)", "異常気象により、広範囲で飢饉が発生した。人口が減少し、社会不安が高まる。", 1070,
                HistoricalEvent.EventType.Major,
                new Dictionary<string, float> { { "Food", -1000f }, { "Stability", -0.2f }, { "PopulationGrowthRate", -0.02f } },
                new List<string> { "Resource:Food_Low" }, new List<string> { "PopulationDecline", "SocialUnrest" }
            ).Value);
            events.Add(HistoricalEvent.Create(
                "FoundingOfFirstUniversity", "最初の大学設立", "知識の集積と研究のための機関が設立された。学術的進歩が加速する。", 1120,
                HistoricalEvent.EventType.Major,
                new Dictionary<string, float> { { "Knowledge", 100f }, { "TechnologicalProgressRate", 0.005f } },
                new List<string> { "Magic:Urbanization" }, new List<string>()
            ).Value);
            events.Add(HistoricalEvent.Create(
                "CrusadesStart", "十字軍の開始", "聖地奪還を目的とした大規模な遠征が始まった。宗教的熱狂と軍事的動員が世界を揺るがす。", 1095, // 歴史的に正確な開始年
                HistoricalEvent.EventType.Major,
                new Dictionary<string, float> { { "Influence", 0.1f }, { "Materials", -0.05f }, { "Stability", -0.1f } },
                new List<string> { "Magic:Feudalism" }, new List<string>()
            ).Value);
            events.Add(HistoricalEvent.Create(
                "BlackDeathPrecursor", "黒死病の前兆", "東方からの交易路を通じて、未知の疫病が広がり始めた。後の大疫病の兆候。", 1240,
                HistoricalEvent.EventType.Major,
                new Dictionary<string, float> { { "PopulationGrowthRate", -0.05f }, { "Stability", -0.1f } },
                new List<string> { "Magic:MerchantGuilds" }, new List<string>()
            ).Value);
            events.Add(HistoricalEvent.Create(
                "MagnaCartaSigned", "マグナ・カルタ署名", "国王の権限を制限し、貴族の権利を保障する憲章が署名された。後の立憲主義の萌芽。", 1215, // 歴史的に正確な年
                HistoricalEvent.EventType.Major,
                new Dictionary<string, float> { { "Stability", 0.05f }, { "Influence", 0.05f } },
                new List<string> { "Magic:Feudalism", "Influence:High" }, new List<string>()
            ).Value);
            return events;
        }
    }

    // --- インターフェースのプレースホルダー実装 ---

    /// <summary>
    /// PlaceholderCombatSystem クラス: ICombatSystem のプレースホルダー実装。
    /// </summary>
    public class PlaceholderCombatSystem : ICombatSystem
    {
        public SafeResult<CombatOutcome> InitiateCombat(CombatContext context)
        {
            Console.WriteLine("（プレースホルダー戦闘システム: 戦闘が開始されました。結果はランダムに決定されます。）");
            // 簡略化された結果をシミュレート
            return SafeResult<CombatOutcome>.Success(new CombatOutcome { /* 実際の戦闘結果データを設定 */ });
        }
    }

    /// <summary>
    /// PlaceholderCraftingSystem クラス: ICraftingSystem のプレースホルダー実装。
    /// </summary>
    public class PlaceholderCraftingSystem : ICraftingSystem
    {
        public SafeResult<CraftingOutcome> AttemptCraft(CraftingRecipe recipe, PlayerState playerState, WorldState worldState)
        {
            Console.WriteLine($"（プレースホルダークラフトシステム: {recipe.GetType().Name} のクラフトを試行中。）");
            // 簡略化された結果をシミュレート
            return SafeResult<CraftingOutcome>.Success(new CraftingOutcome { /* 実際のクラフト結果データを設定 */ });
        }
    }

    /// <summary>
    /// Program クラス: ゲームのエントリポイント。
    /// </summary>
    public class Program
    {
        public static void Main(string[] args)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8; // 日本語文字表示のため

            try
            {
                var game = new GameManager("千年を紡ぐ者");
                var result = game.StartSimulation(1250); // T1050からT1250までシミュレーションを実行

                if (result.IsSuccess)
                {
                    Console.WriteLine(result.Value);
                }
                else
                {
                    Console.Error.WriteLine($"シミュレーションエラー: {result.ErrorMessage}");
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"予期せぬ例外が発生しました: {ex.Message}");
                Console.Error.WriteLine(ex.StackTrace);
            }
        }
    }
}
```
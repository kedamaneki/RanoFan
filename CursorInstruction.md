はい、承知いたしました。
リードディレクター兼C#設計者として、ご指示いただいた「ターン1001からターン3000までの2000年間（第21〜60世代）を不遇枝エネルギー蓄積モデルを適用して連続自律進行させ、3000年史の正史を完成させる」ための精密なC#実装指示プロンプトをMarkdown形式で出力します。

このコードは、Safe-Fail構造、MagicSanitizerEngine、Job/Magicの定義規約（魔法=社会技術, ジョブ=生活職業）を厳格に守り、Cursor(IDE)のCtrl+Lへそのまま読み込ませてC#コード化できる形式です。

---

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FromLikeSimulator
{
    /// <summary>
    /// シミュレーションの世界の状態を保持するクラス。
    /// 各ターン、各世代における世界の状況、資源、技術、イベントなどを記録します。
    /// </summary>
    public class WorldState
    {
        public long CurrentTurn { get; set; }
        public int CurrentGeneration { get; set; }
        public double UnfavoredBranchEnergyAccumulated { get; set; } // 不遇枝エネルギー蓄積量
        public Dictionary<string, double> Resources { get; set; } // 世界の主要資源
        public List<SocialTechnology> ActiveSocialTechnologies { get; set; } // 魔法 = 社会技術
        public List<LifeOccupation> ActiveLifeOccupations { get; set; } // ジョブ = 生活職業
        public List<string> GenerationEvents { get; set; } // 現在の世代で発生した主要イベントのログ
        public List<string> HistoricalRecords { get; set; } // 3000年史の正史を構成する記録

        public WorldState()
        {
            Resources = new Dictionary<string, double>();
            ActiveSocialTechnologies = new List<SocialTechnology>();
            ActiveLifeOccupations = new List<LifeOccupation>();
            GenerationEvents = new List<string>();
            HistoricalRecords = new List<string>();
        }
    }

    /// <summary>
    /// MagicSanitizerEngine: シミュレーション状態の整合性を保ち、予期せぬ値を修正するエンジン。
    /// Safe-Fail構造の一部として機能し、シミュレーションの安定性を確保します。
    /// </summary>
    public static class MagicSanitizerEngine
    {
        /// <summary>
        /// 指定されたWorldStateオブジェクトをサニタイズし、整合性を保ちます。
        /// </summary>
        /// <param name="state">サニタイズするWorldStateオブジェクト。</param>
        /// <returns>サニタイズされたWorldStateオブジェクト。</returns>
        public static WorldState Sanitize(WorldState state)
        {
            // [Safe-Fail構造] 状態の整合性チェックと修正ロジック
            if (state == null)
            {
                Console.Error.WriteLine("[MagicSanitizerEngine] ERROR: Attempted to sanitize a null WorldState. Returning default.");
                return new WorldState(); // デフォルト状態を返すか、例外をスロー
            }

            // 不遇枝エネルギーは負にならない
            if (state.UnfavoredBranchEnergyAccumulated < 0)
            {
                Console.WriteLine($"[MagicSanitizerEngine] WARNING: UnfavoredBranchEnergy was {state.UnfavoredBranchEnergyAccumulated:F2}. Corrected to 0.");
                state.UnfavoredBranchEnergyAccumulated = 0;
            }

            // 資源は負にならない
            foreach (var key in state.Resources.Keys.ToList()) // ToList() でコピーして列挙中に変更可能にする
            {
                if (state.Resources[key] < 0)
                {
                    Console.WriteLine($"[MagicSanitizerEngine] WARNING: Resource '{key}' was {state.Resources[key]:F2}. Corrected to 0.");
                    state.Resources[key] = 0;
                }
            }

            // アクティブな社会技術や生活職業リストにnullがないことを確認
            state.ActiveSocialTechnologies.RemoveAll(t => t == null);
            state.ActiveLifeOccupations.RemoveAll(j => j == null);

            // 世代がターン数と矛盾しないかチェック (簡易的なもの)
            int expectedGeneration = (int)Math.Ceiling((double)state.CurrentTurn / HistoricalSimulator.TurnsPerGeneration);
            if (state.CurrentGeneration != expectedGeneration && state.CurrentTurn > 0)
            {
                Console.WriteLine($"[MagicSanitizerEngine] WARNING: Generation mismatch. Turn {state.CurrentTurn} suggests Gen {expectedGeneration}, but state is Gen {state.CurrentGeneration}. Correcting.");
                state.CurrentGeneration = expectedGeneration;
            }

            return state;
        }
    }

    /// <summary>
    /// 魔法 = 社会技術 (Social Technology) の定義。
    /// 世界に永続的な影響を与える技術的・社会的な進歩を表します。
    /// </summary>
    public class SocialTechnology
    {
        public string Name { get; set; }
        public double ImpactFactor { get; set; } // 世界への影響度 (例: 資源生産効率、安定性)
        public bool IsDiscovered { get; set; } // 発見済みかどうか
        public string Description { get; set; }

        public SocialTechnology(string name, double impactFactor, bool isDiscovered = false, string description = "")
        {
            Name = name;
            ImpactFactor = impactFactor;
            IsDiscovered = isDiscovered;
            Description = description;
        }
    }

    /// <summary>
    /// ジョブ = 生活職業 (Life Occupation) の定義。
    /// シミュレーション内の住民が従事する具体的な職業活動を表します。
    /// </summary>
    public class LifeOccupation
    {
        public string Name { get; set; }
        public double Productivity { get; set; } // 資源生産やサービス提供の効率
        public int PopulationEngaged { get; set; } // その職業に従事する人口 (簡易モデル)
        public string Description { get; set; }

        public LifeOccupation(string name, double productivity, int populationEngaged = 100, string description = "")
        {
            Name = name;
            Productivity = productivity;
            PopulationEngaged = populationEngaged;
            Description = description;
        }
    }

    /// <summary>
    /// 千年史自律シミュレーターのコアロジックを実装するクラス。
    /// ターンベースで世界の状態を進行させ、不遇枝エネルギーモデルを適用し、正史を記録します。
    /// </summary>
    public class HistoricalSimulator
    {
        private WorldState _worldState;
        public const int TurnsPerGeneration = 50; // 1世代あたりのターン数
        private Random _random;

        /// <summary>
        /// シミュレーターの新しいインスタンスを初期化します。
        /// </summary>
        /// <param name="initialState">シミュレーションの初期状態。</param>
        public HistoricalSimulator(WorldState initialState)
        {
            _worldState = initialState ?? throw new ArgumentNullException(nameof(initialState));
            _random = new Random();
            // 初期状態のサニタイズを必ず実行
            _worldState = MagicSanitizerEngine.Sanitize(_worldState);
            Console.WriteLine($"[Simulator Init] Initialized at Turn {_worldState.CurrentTurn}, Gen {_worldState.CurrentGeneration}.");
        }

        /// <summary>
        /// 指定されたターン範囲でシミュレーションを実行します。
        /// </summary>
        /// <param name="startTurn">シミュレーションを開始するターン数。</param>
        /// <param name="endTurn">シミュレーションを終了するターン数。</param>
        /// <returns>シミュレーション中に記録された正史のリスト。</returns>
        public List<string> RunSimulation(long startTurn, long endTurn)
        {
            Console.WriteLine($"\n--- [SIMULATION START] Running from Turn {startTurn} to {endTurn} (Generations {Math.Ceiling((double)startTurn / TurnsPerGeneration)} to {Math.Ceiling((double)endTurn / TurnsPerGeneration)}) ---");

            for (long currentTurn = startTurn; currentTurn <= endTurn; currentTurn++)
            {
                try
                {
                    _worldState.CurrentTurn = currentTurn;
                    _worldState.CurrentGeneration = (int)Math.Ceiling((double)currentTurn / TurnsPerGeneration);

                    // [Safe-Fail構造] ターン開始前の状態チェックとサニタイズ
                    _worldState = MagicSanitizerEngine.Sanitize(_worldState);

                    // ターン処理の実行
                    ProcessTurn(currentTurn);

                    // 世代の変わり目処理
                    if (currentTurn % TurnsPerGeneration == 0)
                    {
                        ProcessGenerationEnd(_worldState.CurrentGeneration);
                    }

                    // [Safe-Fail構造] ターン終了後の状態チェックとサニタイズ
                    _worldState = MagicSanitizerEngine.Sanitize(_worldState);
                }
                catch (Exception ex)
                {
                    // [Safe-Fail構造] 例外発生時の処理
                    string errorMessage = $"[ERROR] Turn {currentTurn} failed: {ex.Message}. StackTrace: {ex.StackTrace}. State might be corrupted. Attempting graceful shutdown.";
                    Console.Error.WriteLine(errorMessage);
                    _worldState.HistoricalRecords.Add(errorMessage);
                    // 致命的なエラーの場合はシミュレーションを中断
                    break;
                }
            }

            Console.WriteLine($"--- [SIMULATION END] Reached Turn {endTurn}. Final Generation: {_worldState.CurrentGeneration}. ---");
            return _worldState.HistoricalRecords;
        }

        /// <summary>
        /// シミュレーションの1ターン分の処理を実行します。
        /// </summary>
        /// <param name="turn">現在のターン数。</param>
        private void ProcessTurn(long turn)
        {
            // 1. 不遇枝エネルギー蓄積モデルの適用
            double energyChange = CalculateUnfavoredBranchEnergyChange(_worldState);
            _worldState.UnfavoredBranchEnergyAccumulated += energyChange;

            // 不遇枝エネルギーが閾値を超えたらイベント発生
            const double unfavoredEnergyThreshold = 150.0; // 仮の閾値
            if (_worldState.UnfavoredBranchEnergyAccumulated >= unfavoredEnergyThreshold)
            {
                TriggerUnfavoredBranchEvent();
                _worldState.UnfavoredBranchEnergyAccumulated = 0; // イベント発生でリセット
            }

            // 2. 世界の状態更新（資源、人口、技術進歩など）
            UpdateWorldState(turn);

            // 3. ジョブ（生活職業）の活動による影響
            foreach (var job in _worldState.ActiveLifeOccupations)
            {
                PerformJobActivity(job);
            }

            // 4. 魔法（社会技術）の影響
            foreach (var magic in _worldState.ActiveSocialTechnologies)
            {
                ApplySocialTechnologyEffect(magic);
            }

            // 簡易ログ (詳細デバッグ用)
            // Console.WriteLine($"Turn {turn}: Gen {_worldState.CurrentGeneration}, Energy={_worldState.UnfavoredBranchEnergyAccumulated:F2}, Food={_worldState.Resources.GetValueOrDefault("Food", 0):F2}");
        }

        /// <summary>
        /// 不遇枝エネルギーの蓄積量を計算します。
        /// 世界の「不遇」な状態に応じてエネルギーが蓄積されます。
        /// </summary>
        /// <param name="state">現在の世界の状態。</param>
        /// <returns>このターンで蓄積される不遇枝エネルギー量。</returns>
        private double CalculateUnfavoredBranchEnergyChange(WorldState state)
        {
            double energyAccumulation = 0.05; // ベースの蓄積量

            // 例: 食料資源が少ないと蓄積加速
            if (state.Resources.ContainsKey("Food") && state.Resources["Food"] < 100.0)
            {
                energyAccumulation += 0.2 + (100.0 - state.Resources["Food"]) * 0.01; // 不足分に応じて加速
            }
            // 例: 特定の重要な社会技術が未発見だと蓄積加速
            if (!state.ActiveSocialTechnologies.Any(t => t.Name == "AdvancedAgriculture" && t.IsDiscovered))
            {
                energyAccumulation += 0.1;
            }
            if (!state.ActiveSocialTechnologies.Any(t => t.Name == "StableGovernance" && t.IsDiscovered))
            {
                energyAccumulation += 0.15;
            }
            // 例: 資源の多様性が低いと蓄積加速
            if (state.Resources.Count < 3)
            {
                energyAccumulation += 0.05;
            }

            // ランダムな変動要素
            energyAccumulation += (_random.NextDouble() - 0.5) * 0.1; // -0.05 から +0.05 の範囲で変動

            return Math.Max(0, energyAccumulation); // 負の値にならないようにする
        }

        /// <summary>
        /// 不遇枝エネルギーが閾値を超えた際に発生するイベントをトリガーします。
        /// これは、社会技術の発見、危機、変革など、シミュレーションの転換点となり得ます。
        /// </summary>
        private void TriggerUnfavoredBranchEvent()
        {
            string eventDescription = $"[EVENT] Turn {_worldState.CurrentTurn} (Gen {_worldState.CurrentGeneration}): Unfavored Branch Energy triggered a major event!";
            _worldState.GenerationEvents.Add(eventDescription);
            _worldState.HistoricalRecords.Add(eventDescription);
            Console.WriteLine(eventDescription);

            // イベントの種類をランダムに決定
            double eventRoll = _random.NextDouble();

            if (eventRoll < 0.4) // 40%の確率で新しい社会技術の発見
            {
                string techName = $"Innovation_Gen{_worldState.CurrentGeneration}_{Guid.NewGuid().ToString().Substring(0, 6)}";
                double impact = 1.0 + _random.NextDouble() * 0.5; // 影響度をランダムに決定
                var newTech = new SocialTechnology(techName, impact, true, "Emergence from accumulated societal pressure.");
                _worldState.ActiveSocialTechnologies.Add(newTech);
                string techDiscovery = $"[TECH DISCOVERY] Turn {_worldState.CurrentTurn}: A new Social Technology '{newTech.Name}' (Magic) was discovered, impacting the world with factor {newTech.ImpactFactor:F2}.";
                _worldState.GenerationEvents.Add(techDiscovery);
                _worldState.HistoricalRecords.Add(techDiscovery);
                Console.WriteLine(techDiscovery);
            }
            else if (eventRoll < 0.7) // 30%の確率で社会危機または変革
            {
                string crisisEvent = $"[CRISIS/CHANGE] Turn {_worldState.CurrentTurn}: A significant societal crisis or transformation occurred. Resources may fluctuate wildly.";
                _worldState.GenerationEvents.Add(crisisEvent);
                _worldState.HistoricalRecords.Add(crisisEvent);
                Console.WriteLine(crisisEvent);
                // 危機の影響をシミュレート (例: 資源の急減、人口減少)
                if (_worldState.Resources.ContainsKey("Food")) _worldState.Resources["Food"] *= (_random.NextDouble() * 0.5 + 0.2); // 20-70%に減少
                if (_worldState.Resources.ContainsKey("Materials")) _worldState.Resources["Materials"] *= (_random.NextDouble() * 0.5 + 0.2);
            }
            else // 30%の確率で新たな生活職業の出現または既存職業の進化
            {
                string jobName = $"NewOccupation_Gen{_worldState.CurrentGeneration}_{Guid.NewGuid().ToString().Substring(0, 6)}";
                double productivity = 0.8 + _random.NextDouble() * 1.2;
                var newJob = new LifeOccupation(jobName, productivity, _random.Next(50, 200), "A new way of life emerged from necessity.");
                _worldState.ActiveLifeOccupations.Add(newJob);
                string jobEmergence = $"[JOB EMERGENCE] Turn {_worldState.CurrentTurn}: A new Life Occupation '{newJob.Name}' (Job) emerged, with productivity {newJob.Productivity:F2}.";
                _worldState.GenerationEvents.Add(jobEmergence);
                _worldState.HistoricalRecords.Add(jobEmergence);
                Console.WriteLine(jobEmergence);
            }
        }

        /// <summary>
        /// 世界の状態を更新します（資源の消費と生産、人口変動など）。
        /// </summary>
        /// <param name="turn">現在のターン数。</param>
        private void UpdateWorldState(long turn)
        {
            // 資源の基本的な変動
            if (!_worldState.Resources.ContainsKey("Food")) _worldState.Resources["Food"] = 500.0;
            if (!_worldState.Resources.ContainsKey("Materials")) _worldState.Resources["Materials"] = 200.0;

            // 食料の消費と生産
            double foodConsumption = _worldState.ActiveLifeOccupations.Sum(j => j.PopulationEngaged * 0.1); // 人口に応じた消費
            double foodProduction = _worldState.ActiveLifeOccupations.Where(j => j.Name.Contains("Farmer") || j.Name.Contains("Gatherer")).Sum(j => j.Productivity * j.PopulationEngaged * 0.05);
            _worldState.Resources["Food"] += foodProduction - foodConsumption + (_random.NextDouble() - 0.5) * 5.0; // ランダムな変動も加える

            // 材料の消費と生産
            double materialConsumption = _worldState.ActiveLifeOccupations.Sum(j => j.PopulationEngaged * 0.05);
            double materialProduction = _worldState.ActiveLifeOccupations.Where(j => j.Name.Contains("Miner") || j.Name.Contains("Crafter")).Sum(j => j.Productivity * j.PopulationEngaged * 0.03);
            _worldState.Resources["Materials"] += materialProduction - materialConsumption + (_random.NextDouble() - 0.5) * 3.0;

            // 人口の簡易的な変動 (食料状況に依存)
            double populationGrowthFactor = 0.001;
            if (_worldState.Resources["Food"] < 50) populationGrowthFactor = -0.005; // 食料不足で人口減少
            else if (_worldState.Resources["Food"] > 200) populationGrowthFactor = 0.002; // 食料豊富で人口増加

            foreach (var job in _worldState.ActiveLifeOccupations)
            {
                job.PopulationEngaged = Math.Max(1, (int)(job.PopulationEngaged * (1 + populationGrowthFactor + (_random.NextDouble() - 0.5) * 0.001)));
            }
        }

        /// <summary>
        /// ジョブ（生活職業）が世界に与える影響をシミュレートします。
        /// </summary>
        /// <param name="job">影響を与える生活職業。</param>
        private void PerformJobActivity(LifeOccupation job)
        {
            // ここではUpdateWorldState内でまとめて処理しているため、個別の影響は簡易的に。
            // より複雑なシミュレーションでは、ここで個々の職業が資源や社会に与える影響を詳細に記述します。
            // 例: 兵士のジョブは紛争確率に影響、学者のジョブは技術発見確率に影響など。
        }

        /// <summary>
        /// 魔法（社会技術）が世界に与える影響をシミュレートします。
        /// </summary>
        /// <param name="magic">影響を与える社会技術。</param>
        private void ApplySocialTechnologyEffect(SocialTechnology magic)
        {
            // 社会技術が資源生産効率や安定性などに与える影響
            if (magic.IsDiscovered)
            {
                if (magic.Name.Contains("Agriculture") && _worldState.Resources.ContainsKey("Food"))
                {
                    _worldState.Resources["Food"] += magic.ImpactFactor * 0.5 * _worldState.ActiveLifeOccupations.Where(j => j.Name.Contains("Farmer")).Sum(j => j.PopulationEngaged);
                }
                if (magic.Name.Contains("ToolMaking") && _worldState.Resources.ContainsKey("Materials"))
                {
                    _worldState.Resources["Materials"] += magic.ImpactFactor * 0.3 * _worldState.ActiveLifeOccupations.Where(j => j.Name.Contains("Crafter")).Sum(j => j.PopulationEngaged);
                }
                // 他の社会技術の影響...
            }
        }

        /// <summary>
        /// 世代の終わりに実行される処理。
        /// 世代ごとの要約を正史に記録し、次の世代の準備を行います。
        /// </summary>
        /// <param name="generation">終了する世代の番号。</param>
        private void ProcessGenerationEnd(int generation)
        {
            string genSummary = $"\n--- GENERATION {generation} END (Turn {_worldState.CurrentTurn}) ---";
            _worldState.HistoricalRecords.Add(genSummary);
            Console.WriteLine(genSummary);

            // 世代ごとの主要イベントを正史に記録
            if (_worldState.GenerationEvents.Any())
            {
                _worldState.HistoricalRecords.Add($"  [Generation {generation} Events]:");
                _worldState.HistoricalRecords.AddRange(_worldState.GenerationEvents.Select(e => $"    - {e}"));
            }
            _worldState.GenerationEvents.Clear(); // 次の世代のためにクリア

            // 世代間の状態要約を正史に記録
            string genProgression = $"  - World State Summary: Food={_worldState.Resources.GetValueOrDefault("Food", 0):F2}, Materials={_worldState.Resources.GetValueOrDefault("Materials", 0):F2}, UnfavoredEnergy={_worldState.UnfavoredBranchEnergyAccumulated:F2}";
            _worldState.HistoricalRecords.Add(genProgression);
            Console.WriteLine(genProgression);

            string techSummary = $"  - Active Social Technologies (Magic): {string.Join(", ", _worldState.ActiveSocialTechnologies.Select(t => t.Name))}";
            _worldState.HistoricalRecords.Add(techSummary);
            Console.WriteLine(techSummary);

            string jobSummary = $"  - Active Life Occupations (Jobs): {string.Join(", ", _worldState.ActiveLifeOccupations.Select(j => $"{j.Name} ({j.PopulationEngaged} engaged)"))}";
            _worldState.HistoricalRecords.Add(jobSummary);
            Console.WriteLine(jobSummary);

            // 世代間の状態遷移やリセット、新たな挑戦の準備など
            // 例: 特定の資源の枯渇、新たな技術ツリーのアンロック、新たなジョブの出現確率調整
        }

        /// <summary>
        /// シミュレーションの全歴史記録を取得します。
        /// </summary>
        /// <returns>正史のリスト。</returns>
        public List<string> GetFullHistory()
        {
            return _worldState.HistoricalRecords;
        }
    }

    /// <summary>
    /// シミュレーションのエントリポイントとなるプログラムクラス。
    /// </summary>
    public class Program
    {
        public static void Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8; // コンソール出力の文字化け対策

            // 初期状態のセットアップ
            WorldState initialState = new WorldState
            {
                CurrentTurn = 1000, // シミュレーション開始前の最終ターン
                CurrentGeneration = 20, // シミュレーション開始前の最終世代
                UnfavoredBranchEnergyAccumulated = 0.0,
                Resources = new Dictionary<string, double>
                {
                    { "Food", 500.0 },
                    { "Materials", 200.0 }
                },
                ActiveSocialTechnologies = new List<SocialTechnology>
                {
                    new SocialTechnology("BasicAgriculture", 1.0, true, "Fundamental farming techniques."),
                    new SocialTechnology("SimpleToolMaking", 0.8, true, "Crafting basic tools for survival."),
                    new SocialTechnology("CommunityBuilding", 1.2, true, "Early forms of social organization.")
                },
                ActiveLifeOccupations = new List<LifeOccupation>
                {
                    new LifeOccupation("Farmer", 1.5, 500, "Cultivates crops for sustenance."),
                    new LifeOccupation("Gatherer", 1.0, 300, "Collects wild resources."),
                    new LifeOccupation("Crafter", 1.2, 150, "Produces tools and basic goods.")
                },
                // HistoricalRecordsはシミュレーターが進行中に蓄積するため、ここでは空で良い
                HistoricalRecords = new List<string>
                {
                    "--- ANCIENT HISTORY (Turns 1-1000, Generations 1-20) ---",
                    "The dawn of civilization. Early settlements formed, basic agriculture developed.",
                    "Numerous small conflicts and periods of peace. Foundations of society laid.",
                    "-----------------------------------------------------"
                }
            };

            HistoricalSimulator simulator = new HistoricalSimulator(initialState);

            // ターン1001からターン3000までの2000年間（第21〜60世代）をシミュレーション
            long startTurn = 1001;
            long endTurn = 3000;

            List<string> finalHistory = simulator.RunSimulation(startTurn, endTurn);

            Console.WriteLine("\n\n--- 3000 YEAR HISTORY: THE GRAND CHRONICLE (Excerpt of Last 50 Records) ---");
            // 全ての記録を表示すると膨大になるため、最後の50件を表示
            foreach (var record in finalHistory.Skip(Math.Max(0, finalHistory.Count - 50)))
            {
                Console.WriteLine(record);
            }
            Console.WriteLine($"\nTotal historical records generated: {finalHistory.Count}");
            Console.WriteLine("--- END OF CHRONICLE ---");

            // 必要であれば、全歴史をファイルに保存するなどの処理を追加
            // File.WriteAllLines("3000_year_history.txt", finalHistory);
        }
    }
}
```
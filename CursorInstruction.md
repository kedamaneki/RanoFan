はい、承知いたしました。
「フロム風戦闘×ブラインド熱科学クラフト×千年史自律シミュレーター」のリードディレクター兼C#設計者として、T1050からT1250まで連続進行し、剪定理論で太いルートをコミットするC#コードの精密な実装指示プロンプトをMarkdown形式で出力します。

Safe-Fail構造、MagicSanitizerEngine、Job/Magicの定義規約（魔法=社会技術, ジョブ=生活職業）を厳格に守ります。
Cursor(IDE)のCtrl+Lへそのまま読み込ませてC#コード化できる形式です。

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace GameSimulation
{
    // --- 1. Core Interfaces and Enums ---

    /// <summary>
    /// ロギング機能を提供するインターフェース。Safe-Fail構造の基盤。
    /// </summary>
    public interface ILogger
    {
        void LogInfo(string message);
        void LogWarning(string message);
        void LogError(string message, Exception exception = null);
        void LogCritical(string message, Exception exception = null);
    }

    /// <summary>
    /// シミュレーションの現在の状態を表す。
    /// </summary>
    public class SimulationState
    {
        public long CurrentTime { get; set; }
        public Dictionary<string, double> Resources { get; private set; } = new Dictionary<string, double>();
        public List<Job> ActiveJobs { get; private set; } = new List<Job>();
        public List<Magic> ActiveMagics { get; private set; } = new List<Magic>();
        public double CivilizationStability { get; set; } = 1.0; // 0.0 - 1.0
        public double TechnologicalAdvancement { get; set; } = 0.0; // 0.0 - 1.0
        public double ThreatLevel { get; set; } = 0.0; // 0.0 - 1.0
        public double Population { get; set; } = 0; // 人口リソースを直接管理

        public SimulationState Clone()
        {
            return new SimulationState
            {
                CurrentTime = this.CurrentTime,
                Resources = new Dictionary<string, double>(this.Resources),
                ActiveJobs = this.ActiveJobs.Select(j => j.Clone()).ToList(),
                ActiveMagics = this.ActiveMagics.Select(m => m.Clone()).ToList(),
                CivilizationStability = this.CivilizationStability,
                TechnologicalAdvancement = this.TechnologicalAdvancement,
                ThreatLevel = this.ThreatLevel,
                Population = this.Population
            };
        }
    }

    /// <summary>
    /// 生活職業のタイプ。
    /// </summary>
    public enum JobType
    {
        Farmer,
        Miner,
        Crafter,
        Scholar,
        Soldier,
        Administrator,
        Builder // 新しいジョブタイプ
    }

    /// <summary>
    /// 社会技術（魔法）のタイプ。
    /// </summary>
    public enum MagicType
    {
        AgriculturalInnovation,
        MetallurgyAdvancement,
        SocialCohesionRitual,
        DefensivePact,
        TradeNetworkExpansion,
        KnowledgeSharingProtocol,
        UrbanPlanning // 新しい社会技術
    }

    // --- 2. Job and Magic Definitions (規約厳守: 魔法=社会技術, ジョブ=生活職業) ---

    /// <summary>
    /// 生活職業（Job）のデータモデル。
    /// </summary>
    public class Job
    {
        public Guid Id { get; private set; }
        public JobType Type { get; private set; }
        public string Name { get; private set; }
        public Dictionary<string, double> ResourceConsumptionPerTickPerCapita { get; private set; } // 1人あたりの消費
        public Dictionary<string, double> ResourceProductionPerTickPerCapita { get; private set; } // 1人あたりの生産
        public double SkillLevel { get; set; } // 0.0 - 1.0
        public int PopulationAssigned { get; set; }

        public Job(JobType type, string name, Dictionary<string, double> consumption, Dictionary<string, double> production)
        {
            Id = Guid.NewGuid();
            Type = type;
            Name = name;
            ResourceConsumptionPerTickPerCapita = consumption ?? new Dictionary<string, double>();
            ResourceProductionPerTickPerCapita = production ?? new Dictionary<string, double>();
            SkillLevel = 0.1; // 初期スキルレベル
            PopulationAssigned = 0;
        }

        public Job Clone()
        {
            return new Job(Type, Name, new Dictionary<string, double>(ResourceConsumptionPerTickPerCapita), new Dictionary<string, double>(ResourceProductionPerTickPerCapita))
            {
                Id = this.Id,
                SkillLevel = this.SkillLevel,
                PopulationAssigned = this.PopulationAssigned
            };
        }
    }

    /// <summary>
    /// 社会技術（Magic）のデータモデル。
    /// </summary>
    public class Magic
    {
        public Guid Id { get; private set; }
        public MagicType Type { get; private set; }
        public string Name { get; private set; }
        public string Description { get; private set; }
        public Dictionary<string, double> ActivationCost { get; private set; }
        public Dictionary<string, double> OngoingMaintenanceCostPerTick { get; private set; }
        public MagicEffect Effect { get; private set; }
        public bool IsActive { get; set; }
        public long ActivationTime { get; set; }

        public Magic(MagicType type, string name, string description, Dictionary<string, double> activationCost, Dictionary<string, double> maintenanceCost, MagicEffect effect)
        {
            Id = Guid.NewGuid();
            Type = type;
            Name = name;
            Description = description;
            ActivationCost = activationCost ?? new Dictionary<string, double>();
            OngoingMaintenanceCostPerTick = maintenanceCost ?? new Dictionary<string, double>();
            Effect = effect;
            IsActive = false;
        }

        /// <summary>
        /// この魔法が現在の状態でアクティベート可能かチェックする。
        /// </summary>
        public bool CanActivate(SimulationState state)
        {
            if (IsActive) return false;
            foreach (var cost in ActivationCost)
            {
                if (state.Resources.GetValueOrDefault(cost.Key) < cost.Value)
                {
                    return false;
                }
            }
            return true;
        }

        public Magic Clone()
        {
            return new Magic(Type, Name, Description, new Dictionary<string, double>(ActivationCost), new Dictionary<string, double>(OngoingMaintenanceCostPerTick), Effect.Clone())
            {
                Id = this.Id,
                IsActive = this.IsActive,
                ActivationTime = this.ActivationTime
            };
        }
    }

    /// <summary>
    /// 社会技術（Magic）がシミュレーションに与える効果。
    /// </summary>
    public class MagicEffect
    {
        public Dictionary<string, double> ResourceModifier { get; private set; } // 例: "Food": 0.1 (食料生産+10%)
        public double CivilizationStabilityModifier { get; private set; } // 例: 0.05 (+5%安定性)
        public double TechnologicalAdvancementModifier { get; private set; } // 例: 0.02 (+2%技術進歩)
        public double ThreatLevelModifier { get; private set; } // 例: -0.03 (-3%脅威レベル)
        public double PopulationGrowthModifier { get; private set; } // 例: 0.01 (+1%人口成長率)
        public List<JobType> AffectedJobs { get; private set; } // 例: Farmerの生産性向上

        public MagicEffect(Dictionary<string, double> resourceModifier = null, double stability = 0, double tech = 0, double threat = 0, double populationGrowth = 0, List<JobType> affectedJobs = null)
        {
            ResourceModifier = resourceModifier ?? new Dictionary<string, double>();
            CivilizationStabilityModifier = stability;
            TechnologicalAdvancementModifier = tech;
            ThreatLevelModifier = threat;
            PopulationGrowthModifier = populationGrowth;
            AffectedJobs = affectedJobs ?? new List<JobType>();
        }

        public MagicEffect Clone()
        {
            return new MagicEffect(
                new Dictionary<string, double>(ResourceModifier),
                CivilizationStabilityModifier,
                TechnologicalAdvancementModifier,
                ThreatLevelModifier,
                PopulationGrowthModifier,
                new List<JobType>(AffectedJobs)
            );
        }
    }

    // --- 3. MagicSanitizerEngine (Safe-Fail構造の一部) ---

    /// <summary>
    /// MagicSanitizerEngineのインターフェース。社会技術の効果を検証・調整し、シミュレーションの整合性を保つ。
    /// </summary>
    public interface IMagicSanitizer
    {
        /// <summary>
        /// 指定されたMagicEffectを検証し、必要に応じてシミュレーションの整合性を保つように調整する。
        /// Safe-Fail: 調整不可能な場合は、安全なデフォルト効果を返すか、例外をスローする。
        /// </summary>
        /// <param name="effect">検証・調整対象のMagicEffect。</param>
        /// <param name="currentState">現在のシミュレーション状態。</param>
        /// <returns>検証・調整されたMagicEffect。</returns>
        MagicEffect SanitizeMagicEffect(MagicEffect effect, SimulationState currentState);
    }

    /// <summary>
    /// IMagicSanitizerのデフォルト実装。
    /// </summary>
    public class DefaultMagicSanitizer : IMagicSanitizer
    {
        private readonly ILogger _logger;

        public DefaultMagicSanitizer(ILogger logger)
        {
            _logger = logger;
        }

        public MagicEffect SanitizeMagicEffect(MagicEffect effect, SimulationState currentState)
        {
            try
            {
                // Safe-Fail: 不正な値のチェックとクランプ
                var sanitizedEffect = effect.Clone();

                // リソース修飾子が過剰でないかチェック (例: +500%は異常)
                foreach (var key in sanitizedEffect.ResourceModifier.Keys.ToList())
                {
                    if (sanitizedEffect.ResourceModifier[key] > 3.0) // 300%以上の効果は異常と判断
                    {
                        _logger.LogWarning($"MagicEffect resource modifier for {key} was too high ({sanitizedEffect.ResourceModifier[key]:P0}). Clamping to 3.0.");
                        sanitizedEffect.ResourceModifier[key] = 3.0;
                    }
                    else if (sanitizedEffect.ResourceModifier[key] < -0.95) // -95%以下の効果は異常と判断 (資源が完全に枯渇するような効果は危険)
                    {
                        _logger.LogWarning($"MagicEffect resource modifier for {key} was too low ({sanitizedEffect.ResourceModifier[key]:P0}). Clamping to -0.95.");
                        sanitizedEffect.ResourceModifier[key] = -0.95;
                    }
                }

                // 安定性、技術進歩、脅威レベル、人口成長の修飾子をクランプ
                sanitizedEffect.CivilizationStabilityModifier = Math.Clamp(sanitizedEffect.CivilizationStabilityModifier, -0.1, 0.1); // 一度に10%以上の変動は危険
                sanitizedEffect.TechnologicalAdvancementModifier = Math.Clamp(sanitizedEffect.TechnologicalAdvancementModifier, -0.05, 0.05);
                sanitizedEffect.ThreatLevelModifier = Math.Clamp(sanitizedEffect.ThreatLevelModifier, -0.05, 0.05);
                sanitizedEffect.PopulationGrowthModifier = Math.Clamp(sanitizedEffect.PopulationGrowthModifier, -0.02, 0.02);

                // 特定のジョブへの影響が、そのジョブの存在意義を否定しないかチェック
                if (sanitizedEffect.AffectedJobs.Any() && sanitizedEffect.ResourceModifier.Any(kv => kv.Value <= -1.0))
                {
                    _logger.LogWarning("MagicEffect attempts to negate job productivity. Removing such modifiers.");
                    sanitizedEffect.ResourceModifier = sanitizedEffect.ResourceModifier.Where(kv => kv.Value >= -0.95).ToDictionary(kv => kv.Key, kv => kv.Value);
                }

                return sanitizedEffect;
            }
            catch (Exception ex)
            {
                _logger.LogError("Failed to sanitize MagicEffect. Returning a safe, minimal effect.", ex);
                // Safe-Fail: 致命的なエラーが発生した場合、最小限の安全な効果を返す
                return new MagicEffect();
            }
        }
    }

    // --- 4. TimeManager ---

    /// <summary>
    /// シミュレーションの時間進行を管理する。
    /// </summary>
    public class TimeManager
    {
        public long CurrentTime { get; private set; }
        public long StartTime { get; private set; }
        public long EndTime { get; private set; }

        private readonly ILogger _logger;

        public TimeManager(ILogger logger, long startTime, long endTime)
        {
            _logger = logger;
            StartTime = startTime;
            EndTime = endTime;
            CurrentTime = startTime;
        }

        /// <summary>
        /// 時間を1ステップ進める。
        /// Safe-Fail: 終了時間を超えた場合は進行を停止し、警告をログに記録する。
        /// </summary>
        /// <returns>時間が正常に進んだ場合はtrue、終了時間を超えた場合はfalse。</returns>
        public bool AdvanceTime()
        {
            try
            {
                if (CurrentTime >= EndTime)
                {
                    _logger.LogWarning($"Attempted to advance time beyond EndTime ({EndTime}). CurrentTime: {CurrentTime}.");
                    return false;
                }
                CurrentTime++;
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError("Failed to advance time.", ex);
                // Safe-Fail: 時間進行に失敗した場合、シミュレーションを停止させるためにfalseを返す
                return false;
            }
        }

        public bool IsWithinPeriod(long periodStart, long periodEnd)
        {
            return CurrentTime >= periodStart && CurrentTime <= periodEnd;
        }
    }

    // --- 5. PruningTheoryEngine (剪定理論で太いルートをコミット) ---

    /// <summary>
    /// 剪定理論に基づいて未来のシミュレーションルートを評価し、最も有望な「太いルート」をコミットするエンジン。
    /// </summary>
    public class PruningTheoryEngine
    {
        private readonly ILogger _logger;
        private readonly IMagicSanitizer _magicSanitizer;
        private readonly int _lookAheadSteps; // 未来を何ステップ先まで評価するか
        private readonly int _branchingFactor; // 各ステップでいくつの選択肢を生成するか

        public PruningTheoryEngine(ILogger logger, IMagicSanitizer magicSanitizer, int lookAheadSteps = 5, int branchingFactor = 3)
        {
            _logger = logger;
            _magicSanitizer = magicSanitizer;
            _lookAheadSteps = lookAheadSteps;
            _branchingFactor = branchingFactor;
        }

        /// <summary>
        /// 現在の状態から複数の未来のルートをシミュレートし、最も「太い」ルートを評価・選択する。
        /// Safe-Fail: 評価に失敗した場合、現状維持ルートを返すか、最もリスクの低いデフォルトルートを提案する。
        /// </summary>
        /// <param name="currentState">現在のシミュレーション状態。</param>
        /// <param name="availableActions">現在のステップで可能なアクションのリスト。</param>
        /// <returns>コミットすべきアクションのリスト。</returns>
        public List<ISimulationAction> SelectAndCommitRoute(SimulationState currentState, List<ISimulationAction> availableActions)
        {
            try
            {
                if (!availableActions.Any())
                {
                    _logger.LogInfo("No available actions for pruning. Returning empty route.");
                    return new List<ISimulationAction>();
                }

                var potentialRoutes = new List<Tuple<List<ISimulationAction>, double>>(); // (ルート, スコア)

                // 各初期アクションから未来をシミュレートし、スコアを計算
                foreach (var initialAction in availableActions.Take(_branchingFactor)) // 分岐数を制限
                {
                    var simulatedState = currentState.Clone();
                    // 最初のステップのアクションを適用
                    initialAction.Apply(simulatedState, _logger, _magicSanitizer);

                    // 残りのステップをシミュレート
                    for (int i = 1; i < _lookAheadSteps; i++)
                    {
                        ApplyGeneralSimulationStep(simulatedState);
                        // 各ステップでランダムなアクションを適用するなどの複雑なロジックも可能だが、ここでは簡略化
                    }
                    var score = EvaluateRoute(simulatedState);
                    potentialRoutes.Add(Tuple.Create(new List<ISimulationAction> { initialAction }, score));
                }

                // 最もスコアの高いルートを選択
                var bestRoute = potentialRoutes.OrderByDescending(r => r.Item2).FirstOrDefault();

                if (bestRoute == null || bestRoute.Item2 <= 0)
                {
                    _logger.LogWarning("Pruning failed to find a positive-score route. Committing to a safe, minimal action if available, or no action.");
                    // Safe-Fail: 有効なルートが見つからない場合、最もリスクの低いアクション（例: 食料生産を維持するジョブの割り当て）を選択するか、何もしない。
                    var safeAction = availableActions.FirstOrDefault(a => a is JobAssignmentAction ja && ja.TargetJob.Type == JobType.Farmer && ja.PopulationChange > 0);
                    return safeAction != null ? new List<ISimulationAction> { safeAction } : new List<ISimulationAction>();
                }

                _logger.LogInfo($"Committed to a route with score: {bestRoute.Item2:F2}. Initial action: {bestRoute.Item1.FirstOrDefault()?.GetType().Name}");
                return bestRoute.Item1; // 最初のステップのアクションをコミット
            }
            catch (Exception ex)
            {
                _logger.LogError("Failed to select and commit route using pruning theory. Returning empty list.", ex);
                // Safe-Fail: 剪定理論の適用に失敗した場合、安全策として何もしない
                return new List<ISimulationAction>();
            }
        }

        /// <summary>
        /// シミュレーション状態に一般的な1ステップの更新を適用する。
        /// このメソッドはPruningTheoryEngine内部での未来予測用であり、GameSimulationManagerのUpdateSimulationStateとは独立している。
        /// </summary>
        private void ApplyGeneralSimulationStep(SimulationState state)
        {
            // 人口の消費と生産
            double totalFoodConsumption = 0;
            double totalFoodProduction = 0;
            double totalPopulationAssigned = 0;

            foreach (var job in state.ActiveJobs)
            {
                totalPopulationAssigned += job.PopulationAssigned;
                if (job.PopulationAssigned > 0)
                {
                    foreach (var kv in job.ResourceConsumptionPerTickPerCapita)
                    {
                        state.Resources[kv.Key] = state.Resources.GetValueOrDefault(kv.Key) - kv.Value * job.PopulationAssigned;
                        if (kv.Key == "Food") totalFoodConsumption += kv.Value * job.PopulationAssigned;
                    }
                    foreach (var kv in job.ResourceProductionPerTickPerCapita)
                    {
                        state.Resources[kv.Key] = state.Resources.GetValueOrDefault(kv.Key) + kv.Value * job.PopulationAssigned * (1 + job.SkillLevel);
                        if (kv.Key == "Food") totalFoodProduction += kv.Value * job.PopulationAssigned * (1 + job.SkillLevel);
                    }
                }
            }

            // 人口変動
            double foodBalance = totalFoodProduction - totalFoodConsumption;
            double basePopulationGrowth = 0.001; // 基本的な人口増加率
            if (foodBalance < 0)
            {
                // 食料不足の場合、人口減少
                state.Population = Math.Max(0, state.Population + foodBalance * 0.01); // 食料不足量に応じて人口減少
                state.CivilizationStability = Math.Clamp(state.CivilizationStability - 0.01, 0, 1);
            }
            else
            {
                // 食料余剰の場合、人口増加
                state.Population *= (1 + basePopulationGrowth + (foodBalance / state.Population) * 0.001);
            }
            state.Population = Math.Round(state.Population); // 人口は整数に

            // 魔法（社会技術）の維持コストと効果
            foreach (var magic in state.ActiveMagics.Where(m => m.IsActive))
            {
                foreach (var kv in magic.OngoingMaintenanceCostPerTick)
                {
                    state.Resources[kv.Key] = state.Resources.GetValueOrDefault(kv.Key) - kv.Value;
                }
                ApplyMagicEffectToState(state, magic.Effect);
            }

            // 安定性、技術、脅威の自然な変動
            state.CivilizationStability = Math.Clamp(state.CivilizationStability + (0.001 - state.ThreatLevel * 0.002), 0, 1);
            state.TechnologicalAdvancement = Math.Clamp(state.TechnologicalAdvancement + 0.0005, 0, 1);
            state.ThreatLevel = Math.Clamp(state.ThreatLevel + (0.0005 * (1 - state.CivilizationStability)), 0, 1);

            // リソースがマイナスになった場合の処理 (Safe-Fail)
            foreach (var key in state.Resources.Keys.ToList())
            {
                if (state.Resources[key] < 0)
                {
                    state.Resources[key] = 0;
                    state.CivilizationStability = Math.Clamp(state.CivilizationStability - 0.005, 0, 1); // 軽微な安定性ペナルティ
                }
            }
        }

        /// <summary>
        /// MagicEffectをシミュレーション状態に適用する。
        /// </summary>
        private void ApplyMagicEffectToState(SimulationState state, MagicEffect effect)
        {
            foreach (var kv in effect.ResourceModifier)
            {
                foreach (var jobType in effect.AffectedJobs)
                {
                    foreach (var job in state.ActiveJobs.Where(j => j.Type == jobType))
                    {
                        if (job.ResourceProductionPerTickPerCapita.ContainsKey(kv.Key))
                        {
                            job.ResourceProductionPerTickPerCapita[kv.Key] *= (1 + kv.Value);
                        }
                    }
                }
            }

            state.CivilizationStability = Math.Clamp(state.CivilizationStability + effect.CivilizationStabilityModifier, 0, 1);
            state.TechnologicalAdvancement = Math.Clamp(state.TechnologicalAdvancement + effect.TechnologicalAdvancementModifier, 0, 1);
            state.ThreatLevel = Math.Clamp(state.ThreatLevel + effect.ThreatLevelModifier, 0, 1);
            state.Population *= (1 + effect.PopulationGrowthModifier);
        }

        /// <summary>
        /// シミュレーション状態を評価し、スコアを計算する。
        /// 「太いルート」の定義はこの評価関数に集約される。
        /// T1050-T1250では、特に文明の存続可能性、技術的ブレークスルー、脅威の抑制を重視する。
        /// </summary>
        /// <param name="state">評価対象のシミュレーション状態。</param>
        /// <returns>ルートのスコア。</returns>
        private double EvaluateRoute(SimulationState state)
        {
            double score = 0;

            // 資源の安定性 (枯渇していないか、十分な量があるか)
            double resourceStabilityScore = state.Resources.Values.Sum(r => Math.Min(r / 500.0, 1.0)); // 500単位で最大1点
            score += resourceStabilityScore * 0.2; // 20%の重み

            // 文明の安定性 (高いほど良い)
            score += state.CivilizationStability * 0.3; // 30%の重み

            // 技術的進歩 (高いほど良い)
            score += state.TechnologicalAdvancement * 0.25; // 25%の重み

            // 脅威レベル (低いほど良い)
            score += (1.0 - state.ThreatLevel) * 0.15; // 15%の重み

            // 人口 (多いほど良い)
            score += Math.Min(state.Population / 200.0, 1.0) * 0.1; // 10%の重み (人口200で最大1点)

            // T1050-T1250期間の特殊な重み付け (この期間は特に重要)
            if (state.CurrentTime >= 1050 && state.CurrentTime <= 1250)
            {
                // この期間は特に技術的ブレークスルーと安定性が重要
                score += state.TechnologicalAdvancement * 0.3; // 技術進歩の重みをさらに増やす
                score += state.CivilizationStability * 0.2; // 安定性の重みをさらに増やす

                // 脅威レベルが高いと大きなペナルティ
                if (state.ThreatLevel > 0.6)
                {
                    score -= 1.0; // 重いペナルティ
                }
                // 特定の熱科学アイテムが存在するとボーナス
                if (state.Resources.ContainsKey("ThermalRegulator") && state.Resources["ThermalRegulator"] > 0)
                {
                    score += 0.2;
                }
            }

            // Safe-Fail: スコアが負になる場合は0にクランプ (最低限の生存可能性を保証)
            return Math.Max(0, score);
        }
    }

    // --- 6. Simulation Actions (ジョブ/魔法の適用など) ---

    /// <summary>
    /// シミュレーション内で実行可能なアクションのインターフェース。
    /// </summary>
    public interface ISimulationAction
    {
        /// <summary>
        /// このアクションがシミュレーション状態に適用可能かチェックする。
        /// Safe-Fail: 適用不可能な場合はfalseを返す。
        /// </summary>
        bool CanApply(SimulationState state);

        /// <summary>
        /// このアクションをシミュレーション状態に適用する。
        /// Safe-Fail: 適用に失敗した場合、状態をロールバックするか、最小限の変更に留める。
        /// </summary>
        void Apply(SimulationState state, ILogger logger, IMagicSanitizer magicSanitizer);
    }

    /// <summary>
    /// ジョブへの人口割り当てアクション。
    /// </summary>
    public class JobAssignmentAction : ISimulationAction
    {
        public Job TargetJob { get; private set; }
        public int PopulationChange { get; private set; } // 正の値で割り当て、負の値で解除

        public JobAssignmentAction(Job targetJob, int populationChange)
        {
            TargetJob = targetJob;
            PopulationChange = populationChange;
        }

        public bool CanApply(SimulationState state)
        {
            if (TargetJob == null || PopulationChange == 0) return false;

            // Safe-Fail: 割り当て人口が負にならないか、総人口を超えないかなどをチェック
            if (TargetJob.PopulationAssigned + PopulationChange < 0) return false; // 負の人口にはできない
            if (PopulationChange > 0 && state.Population < TargetJob.PopulationAssigned + PopulationChange) return false; // 総人口を超えて割り当てられない

            return true;
        }

        public void Apply(SimulationState state, ILogger logger, IMagicSanitizer magicSanitizer)
        {
            try
            {
                if (!CanApply(state))
                {
                    logger.LogWarning($"Failed to apply JobAssignmentAction for {TargetJob.Name}: CanApply returned false. PopulationChange: {PopulationChange}");
                    return; // Safe-Fail: 適用不可なら何もしない
                }

                // 状態内の対応するジョブを見つける
                var jobInState = state.ActiveJobs.FirstOrDefault(j => j.Id == TargetJob.Id);
                if (jobInState == null)
                {
                    logger.LogError($"Attempted to assign population to non-existent job: {TargetJob.Name}.");
                    return; // Safe-Fail: ジョブが存在しない
                }

                int oldPopulation = jobInState.PopulationAssigned;
                jobInState.PopulationAssigned += PopulationChange;
                state.Population -= PopulationChange; // 総人口から割り当て分を減らす/増やす

                logger.LogInfo($"Applied JobAssignmentAction: {jobInState.Name} population changed by {PopulationChange}. New population: {jobInState.PopulationAssigned}. Total Population: {state.Population}");
            }
            catch (Exception ex)
            {
                logger.LogError($"Failed to apply JobAssignmentAction for {TargetJob.Name}.", ex);
                // Safe-Fail: 適用に失敗した場合、状態は変更しない
            }
        }
    }

    /// <summary>
    /// 社会技術（Magic）をアクティブ化するアクション。
    /// </summary>
    public class ActivateMagicAction : ISimulationAction
    {
        public Magic MagicToActivate { get; private set; }

        public ActivateMagicAction(Magic magic)
        {
            MagicToActivate = magic;
        }

        public bool CanApply(SimulationState state)
        {
            return MagicToActivate.CanActivate(state);
        }

        public void Apply(SimulationState state, ILogger logger, IMagicSanitizer magicSanitizer)
        {
            try
            {
                if (!CanApply(state))
                {
                    logger.LogWarning($"Failed to apply ActivateMagicAction for {MagicToActivate.Name}: CanApply returned false (already active or insufficient resources).");
                    return; // Safe-Fail: 適用不可なら何もしない
                }

                // コストを消費
                foreach (var cost in MagicToActivate.ActivationCost)
                {
                    state.Resources[cost.Key] -= cost.Value;
                }

                // MagicSanitizerEngineを介して効果を検証・調整
                MagicToActivate.Effect = magicSanitizer.SanitizeMagicEffect(MagicToActivate.Effect, state);

                // 魔法をアクティブ化
                MagicToActivate.IsActive = true;
                MagicToActivate.ActivationTime = state.CurrentTime;
                state.ActiveMagics.Add(MagicToActivate); // 状態に魔法を追加

                logger.LogInfo($"Applied ActivateMagicAction: {MagicToActivate.Name} activated. Cost consumed, effect sanitized.");
            }
            catch (Exception ex)
            {
                logger.LogError($"Failed to apply ActivateMagicAction for {MagicToActivate.Name}.", ex);
                // Safe-Fail: 適用に失敗した場合、状態は変更しない（コスト消費もロールバックすべきだが、ここでは簡略化）
            }
        }
    }

    /// <summary>
    /// アイテムをクラフトするアクション。
    /// </summary>
    public class CraftItemAction : ISimulationAction
    {
        public string RecipeName { get; private set; }
        public bool IsBlindAttempt { get; private set; }
        private ThermalScienceCraftingSystem _craftingSystem; // 依存性注入

        public CraftItemAction(string recipeName, bool isBlindAttempt, ThermalScienceCraftingSystem craftingSystem)
        {
            RecipeName = recipeName;
            IsBlindAttempt = isBlindAttempt;
            _craftingSystem = craftingSystem;
        }

        public bool CanApply(SimulationState state)
        {
            // CraftingSystem内部で詳細なチェックが行われるため、ここでは基本的なチェックのみ
            return _craftingSystem != null;
        }

        public void Apply(SimulationState state, ILogger logger, IMagicSanitizer magicSanitizer)
        {
            try
            {
                if (!CanApply(state))
                {
                    logger.LogWarning($"Failed to apply CraftItemAction for {RecipeName}: CanApply returned false.");
                    return;
                }
                _craftingSystem.CraftItem(state, RecipeName, IsBlindAttempt);
            }
            catch (Exception ex)
            {
                logger.LogError($"Failed to apply CraftItemAction for {RecipeName}.", ex);
            }
        }
    }

    // --- 7. ThermalScienceCraftingSystem (ブラインド熱科学クラフト) ---

    /// <summary>
    /// ブラインド熱科学クラフトシステム。
    /// 未知のレシピを試行錯誤で発見し、クラフトする。
    /// </summary>
    public class ThermalScienceCraftingSystem
    {
        private readonly ILogger _logger;
        private readonly Random _random;

        // 既知のレシピ
        private Dictionary<string, CraftingRecipe> _knownRecipes = new Dictionary<string, CraftingRecipe>();
        // 未知のレシピ (発見されるまで隠蔽)
        private List<CraftingRecipe> _hiddenRecipes = new List<CraftingRecipe>();

        public ThermalScienceCraftingSystem(ILogger logger)
        {
            _logger = logger;
            _random = new Random();
            InitializeRecipes();
        }

        private void InitializeRecipes()
        {
            // 初期レシピ (例)
            _knownRecipes.Add("BasicTool", new CraftingRecipe("BasicTool", new Dictionary<string, double> { { "IronOre", 5 } }, new Dictionary<string, double> { { "Tool", 1 } }, 0.8));
            _knownRecipes.Add("RefinedMetal", new CraftingRecipe("RefinedMetal", new Dictionary<string, double> { { "IronOre", 10 }, { "Fuel", 2 } }, new Dictionary<string, double> { { "RefinedMetal", 1 } }, 0.7));

            // 隠された熱科学レシピ (T1050-T1250で発見される可能性のあるもの)
            _hiddenRecipes.Add(new CraftingRecipe("ThermalRegulator", new Dictionary<string, double> { { "RefinedMetal", 10 }, { "RareEarth", 2 }, { "Knowledge", 50 } }, new Dictionary<string, double> { { "ThermalRegulator", 1 } }, 0.3, true, true));
            _hiddenRecipes.Add(new CraftingRecipe("EnergyConduit", new Dictionary<string, double> { { "Copper", 8 }, { "Crystal", 3 }, { "Knowledge", 70 } }, new Dictionary<string, double> { { "EnergyConduit", 1 } }, 0.2, true, true));
            _hiddenRecipes.Add(new CraftingRecipe("AdvancedSensor", new Dictionary<string, double> { { "RefinedMetal", 5 }, { "Crystal", 1 }, { "RareEarth", 1 }, { "Knowledge", 100 } }, new Dictionary<string, double> { { "AdvancedSensor", 1 } }, 0.15, true, true));
        }

        /// <summary>
        /// 既知のレシピのリストを返す。
        /// </summary>
        public IEnumerable<CraftingRecipe> GetKnownRecipes() => _knownRecipes.Values;

        /// <summary>
        /// アイテムをクラフトする。ブラインドクラフトの試行を含む。
        /// Safe-Fail: リソース不足、レシピ失敗、未知のレシピ試行失敗などに対応。
        /// </summary>
        /// <param name="state">現在のシミュレーション状態。</param>
        /// <param name="recipeName">クラフトしようとするレシピ名。isBlindAttemptがtrueの場合は無視される。</param>
        /// <param name="isBlindAttempt">未知のレシピを試行するか。</param>
        /// <returns>クラフトが成功した場合はtrue。</returns>
        public bool CraftItem(SimulationState state, string recipeName, bool isBlindAttempt = false)
        {
            try
            {
                CraftingRecipe recipe = null;
                bool wasHidden = false;

                if (isBlindAttempt)
                {
                    // 未知のレシピをランダムに試行
                    recipe = _hiddenRecipes.OrderBy(_ => _random.Next()).FirstOrDefault();
                    if (recipe == null)
                    {
                        _logger.LogWarning("Blind crafting attempt failed: No hidden recipes available.");
                        return false; // Safe-Fail: 隠しレシピがない
                    }
                    wasHidden = true;
                    _logger.LogInfo($"Blind crafting attempt for a hidden recipe (potential: {recipe.Name}).");
                }
                else
                {
                    if (!_knownRecipes.TryGetValue(recipeName, out recipe))
                    {
                        _logger.LogWarning($"Crafting failed: Recipe '{recipeName}' is unknown.");
                        return false; // Safe-Fail: レシピが未知
                    }
                }

                // リソースチェック
                foreach (var cost in recipe.RequiredResources)
                {
                    if (state.Resources.GetValueOrDefault(cost.Key) < cost.Value)
                    {
                        _logger.LogWarning($"Crafting '{recipe.Name}' failed: Insufficient resource '{cost.Key}'. Needed {cost.Value:F2}, have {state.Resources.GetValueOrDefault(cost.Key):F2}.");
                        return false; // Safe-Fail: リソース不足
                    }
                }

                // リソース消費
                foreach (var cost in recipe.RequiredResources)
                {
                        state.Resources[cost.Key] -= cost.Value;
                }

                // 成功確率判定
                if (_random.NextDouble() > recipe.SuccessChance)
                {
                    _logger.LogWarning($"Crafting '{recipe.Name}' failed due to low success chance ({recipe.SuccessChance * 100:F0}%). Resources consumed.");
                    // Safe-Fail: 失敗したがリソースは消費された。一部リソースを回収するなどのフォールバックも可能。
                    return false;
                }

                // クラフト成功
                foreach (var product in recipe.ProducedItems)
                {
                    state.Resources[product.Key] = state.Resources.GetValueOrDefault(product.Key) + product.Value;
                }
                _logger.LogInfo($"Successfully crafted '{recipe.Name}'. Produced: {string.Join(", ", recipe.ProducedItems.Select(kv => $"{kv.Value:F0} {kv.Key}"))}.");

                // ブラインド試行で成功した場合、レシピを発見し既知にする
                if (wasHidden && recipe.IsHidden)
                {
                    _hiddenRecipes.Remove(recipe);
                    _knownRecipes.Add(recipe.Name, recipe);
                    recipe.IsHidden = false;
                    _logger.LogInfo($"Hidden recipe '{recipe.Name}' discovered through blind crafting!");
                    // T1050-T1250期間で熱科学レシピを発見した場合、技術進歩にボーナス
                    if (state.CurrentTime >= 1050 && state.CurrentTime <= 1250 && recipe.IsThermalScience)
                    {
                        state.TechnologicalAdvancement = Math.Clamp(state.TechnologicalAdvancement + 0.05, 0, 1);
                        _logger.LogInfo($"Significant technological advancement due to Thermal Science recipe discovery in T1050-T1250 period!");
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Critical error during crafting attempt for '{recipeName}'.", ex);
                // Safe-Fail: 致命的なエラーが発生した場合、リソースを消費せず、クラフトを失敗させる。
                // ロールバック処理をここに追加することも検討。
                return false;
            }
        }
    }

    /// <summary>
    /// クラフトレシピのデータモデル。
    /// </summary>
    public class CraftingRecipe
    {
        public string Name { get; private set; }
        public Dictionary<string, double> RequiredResources { get; private set; }
        public Dictionary<string, double> ProducedItems { get; private set; }
        public double SuccessChance { get; private set; } // 0.0 - 1.0
        public bool IsHidden { get; set; } // 未知のレシピか
        public bool IsThermalScience { get; private set; } // 熱科学レシピか

        public CraftingRecipe(string name, Dictionary<string, double> required, Dictionary<string, double> produced, double successChance, bool isHidden = false, bool isThermalScience = false)
        {
            Name = name;
            RequiredResources = required ?? new Dictionary<string, double>();
            ProducedItems = produced ?? new Dictionary<string, double>();
            SuccessChance = Math.Clamp(successChance, 0.0, 1.0);
            IsHidden = isHidden;
            IsThermalScience = isThermalScience;
        }
    }

    // --- 8. CombatSystem (フロム風戦闘) ---

    /// <summary>
    /// フロム風戦闘システム。
    /// プレイヤーの行動選択と敵の行動パターン、スタミナ管理、パリィ/ガードなどの要素を含む。
    /// シミュレーターの文脈では、戦闘結果が文明状態に影響を与える。
    /// </summary>
    public class CombatSystem
    {
        private readonly ILogger _logger;
        private readonly Random _random;

        public CombatSystem(ILogger logger)
        {
            _logger = logger;
            _random = new Random();
        }

        /// <summary>
        /// シミュレーションにおける戦闘イベントを解決する。
        /// Safe-Fail: 戦闘中に予期せぬエラーが発生した場合、デフォルトの結果（例: 引き分け、最小限の損害）にフォールバックする。
        /// </summary>
        /// <param name="state">現在のシミュレーション状態。</param>
        /// <param name="playerMilitaryStrength">プレイヤー（文明の軍事力）の強度。</param>
        /// <param name="enemyThreat">敵の脅威レベル。</param>
        /// <returns>戦闘結果 (勝利、敗北、引き分け)。</returns>
        public CombatResult ResolveCombat(SimulationState state, double playerMilitaryStrength, double enemyThreat)
        {
            try
            {
                _logger.LogInfo($"Initiating combat: Player Military Strength={playerMilitaryStrength:F2}, Enemy Threat={enemyThreat:F2}.");

                // 基本的な戦闘力比較
                double effectivePlayerStrength = playerMilitaryStrength * (1 + state.TechnologicalAdvancement * 0.5); // 技術レベルが軍事力に影響
                double effectiveEnemyThreat = enemyThreat * (1 + state.ThreatLevel * 0.5); // 全体脅威レベルが敵の強さに影響

                // フロム風要素の抽象化: スタミナ、パリィ、ガード
                // シミュレーターでは、これらは確率的な要素や文明の「準備度」として表現される
                double playerStaminaManagement = _random.NextDouble(); // 0-1, 高いほど良い
                double playerParryChance = state.TechnologicalAdvancement * 0.15; // 技術レベルが高いほどパリィ成功率も上がる
                double playerGuardEffectiveness = state.CivilizationStability * 0.25; // 安定性が高いほどガードが堅い

                double combatOutcomeFactor = (effectivePlayerStrength * (1 + playerStaminaManagement * 0.2)) - (effectiveEnemyThreat * (1 - playerParryChance));

                // T1050-T1250期間の戦闘特性
                if (state.CurrentTime >= 1050 && state.CurrentTime <= 1250)
                {
                    effectiveEnemyThreat *= 1.2; // この期間は敵の攻撃性が増す
                    if (state.Resources.ContainsKey("AncientArtifact") && state.Resources["AncientArtifact"] > 0)
                    {
                        combatOutcomeFactor += state.Resources["AncientArtifact"] * 0.1; // 特定のアーティファクトが戦闘に有利に働く
                        _logger.LogInfo("Ancient Artifact provides combat advantage in T1050-T1250 period.");
                    }
                    if (state.Resources.ContainsKey("AdvancedSensor") && state.Resources["AdvancedSensor"] > 0)
                    {
                        combatOutcomeFactor += state.Resources["AdvancedSensor"] * 0.05; // 熱科学アイテムも有利に
                        _logger.LogInfo("Advanced Sensor provides tactical advantage in T1050-T1250 period.");
                    }
                }

                CombatResult result;
                if (combatOutcomeFactor > effectiveEnemyThreat * 0.7) // プレイヤーが圧倒的に有利
                {
                    result = CombatResult.Victory;
                    state.CivilizationStability = Math.Clamp(state.CivilizationStability + 0.05, 0, 1);
                    state.ThreatLevel = Math.Clamp(state.ThreatLevel - 0.1, 0, 1);
                    _logger.LogInfo("Combat Result: Victory! Stability increased, Threat reduced.");
                }
                else if (combatOutcomeFactor > effectiveEnemyThreat * 0.2) // プレイヤーがやや有利
                {
                    result = CombatResult.MinorVictory;
                    state.CivilizationStability = Math.Clamp(state.CivilizationStability + 0.02, 0, 1);
                    state.ThreatLevel = Math.Clamp(state.ThreatLevel - 0.05, 0, 1);
                    _logger.LogInfo("Combat Result: Minor Victory. Small stability increase, threat reduction.");
                }
                else if (combatOutcomeFactor > -effectivePlayerStrength * 0.2) // 敵がやや有利
                {
                    result = CombatResult.MinorDefeat;
                    state.CivilizationStability = Math.Clamp(state.CivilizationStability - 0.03, 0, 1);
                    state.ThreatLevel = Math.Clamp(state.ThreatLevel + 0.05, 0, 1);
                    // リソース損失の可能性
                    if (_random.NextDouble() < 0.3)
                    {
                        var lostResource = state.Resources.Keys.Where(k => k != "Population").OrderBy(_ => _random.Next()).FirstOrDefault();
                        if (lostResource != null)
                        {
                            double lossAmount = state.Resources[lostResource] * 0.1;
                            state.Resources[lostResource] -= lossAmount;
                            _logger.LogWarning($"Minor Defeat: Lost {lossAmount:F2} of {lostResource}.");
                        }
                    }
                    _logger.LogInfo("Combat Result: Minor Defeat. Stability decreased, Threat increased.");
                }
                else // 敵が圧倒的に有利
                {
                    result = CombatResult.Defeat;
                    state.CivilizationStability = Math.Clamp(state.CivilizationStability - 0.1, 0, 1);
                    state.ThreatLevel = Math.Clamp(state.ThreatLevel + 0.15, 0, 1);
                    // 大規模なリソース損失
                    foreach (var key in state.Resources.Keys.Where(k => k != "Population").ToList())
                    {
                        double lossAmount = state.Resources[key] * 0.2;
                        state.Resources[key] -= lossAmount;
                        _logger.LogWarning($"Defeat: Lost {lossAmount:F2} of {key}.");
                    }
                    // 人口損失
                    double populationLoss = state.Population * 0.05;
                    state.Population = Math.Max(0, state.Population - populationLoss);
                    _logger.LogWarning($"Defeat: Lost {populationLoss:F0} population.");

                    _logger.LogInfo("Combat Result: Defeat! Significant stability decrease, threat increase, and resource/population loss.");
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError("Critical error during combat resolution. Returning default result (Minor Defeat).", ex);
                // Safe-Fail: 戦闘解決に失敗した場合、文明に最小限の損害を与える結果にフォールバック
                state.CivilizationStability = Math.Clamp(state.CivilizationStability - 0.01, 0, 1);
                state.ThreatLevel = Math.Clamp(state.ThreatLevel + 0.01, 0, 1);
                return CombatResult.MinorDefeat;
            }
        }
    }

    public enum CombatResult
    {
        Victory,
        MinorVictory,
        MinorDefeat,
        Defeat
    }

    // --- 9. GameSimulationManager (メインオーケストレーター) ---

    /// <summary>
    /// ゲームシミュレーションのメインマネージャー。
    /// T1050からT1250まで連続進行し、剪定理論で太いルートをコミットする。
    /// Safe-Fail構造、MagicSanitizerEngine、Job/Magicの定義規約を厳格に守る。
    /// </summary>
    public class GameSimulationManager
    {
        private readonly ILogger _logger;
        private readonly TimeManager _timeManager;
        private readonly PruningTheoryEngine _pruningEngine;
        private readonly ThermalScienceCraftingSystem _craftingSystem;
        private readonly CombatSystem _combatSystem;
        private readonly IMagicSanitizer _magicSanitizer;

        public SimulationState CurrentState { get; private set; }

        public GameSimulationManager(ILogger logger, long startTime = 1000, long endTime = 1300)
        {
            _logger = logger;
            _timeManager = new TimeManager(logger, startTime, endTime);
            _magicSanitizer = new DefaultMagicSanitizer(logger); // MagicSanitizerEngineのインスタンス化
            _pruningEngine = new PruningTheoryEngine(logger, _magicSanitizer); // 依存性注入
            _craftingSystem = new ThermalScienceCraftingSystem(logger);
            _combatSystem = new CombatSystem(logger);

            CurrentState = new SimulationState
            {
                CurrentTime = startTime,
                CivilizationStability = 0.8,
                TechnologicalAdvancement = 0.3,
                ThreatLevel = 0.1,
                Population = 100 // 初期人口
            };
            InitializeStartingResources();
            InitializeStartingJobs();
        }

        private void InitializeStartingResources()
        {
            CurrentState.Resources["Food"] = 1000;
            CurrentState.Resources["Wood"] = 500;
            CurrentState.Resources["IronOre"] = 200;
            CurrentState.Resources["RefinedMetal"] = 50;
            CurrentState.Resources["Tool"] = 10;
            CurrentState.Resources["Knowledge"] = 100;
            CurrentState.Resources["Fuel"] = 50;
            CurrentState.Resources["RareEarth"] = 5; // 熱科学クラフト用
            CurrentState.Resources["Copper"] = 30;
            CurrentState.Resources["Crystal"] = 10;
            CurrentState.Resources["SoldierEquipment"] = 20; // 兵士の装備
        }

        private void InitializeStartingJobs()
        {
            var farmerJob = new Job(JobType.Farmer, "Farmer", new Dictionary<string, double> { { "Food", 0.1 } }, new Dictionary<string, double> { { "Food", 1.0 } });
            farmerJob.PopulationAssigned = 50;
            CurrentState.ActiveJobs.Add(farmerJob);

            var minerJob = new Job(JobType.Miner, "Miner", new Dictionary<string, double> { { "Food", 0.05 } }, new Dictionary<string, double> { { "IronOre", 0.5 } });
            minerJob.PopulationAssigned = 20;
            CurrentState.ActiveJobs.Add(minerJob);

            var scholarJob = new Job(JobType.Scholar, "Scholar", new Dictionary<string, double> { { "Food", 0.08 } }, new Dictionary<string, double> { { "Knowledge", 0.8 } });
            scholarJob.PopulationAssigned = 10;
            CurrentState.ActiveJobs.Add(scholarJob);

            var soldierJob = new Job(JobType.Soldier, "Soldier", new Dictionary<string, double> { { "Food", 0.15 }, { "SoldierEquipment", 0.01 } }, new Dictionary<string, double>());
            soldierJob.PopulationAssigned = 10;
            CurrentState.ActiveJobs.Add(soldierJob);

            // 残りの人口を未割り当てとして管理
            CurrentState.Population -= CurrentState.ActiveJobs.Sum(j => j.PopulationAssigned);
        }

        /// <summary>
        /// シミュレーションを1ステップ進める。
        /// T1050-T1250期間の特殊処理、剪定理論によるルートコミット、Safe-Fail構造を組み込む。
        /// </summary>
        /// <returns>シミュレーションが継続可能であればtrue、終了した場合はfalse。</returns>
        public bool SimulateStep()
        {
            try
            {
                if (!_timeManager.AdvanceTime())
                {
                    _logger.LogInfo("Simulation reached end time.");
                    return false; // シミュレーション終了
                }

                CurrentState.CurrentTime = _timeManager.CurrentTime;
                _logger.LogInfo($"--- Simulating Time: T{CurrentState.CurrentTime} ---");

                // 1. 環境イベントの生成 (例: 脅威の発生、資源の発見)
                GenerateEnvironmentalEvents();

                // 2. 可能なアクションのリストを生成
                var availableActions = GenerateAvailableActions(CurrentState);

                // 3. T1050-T1250期間の特殊処理と剪定理論の適用
                if (_timeManager.IsWithinPeriod(1050, 1250))
                {
                    _logger.LogInfo("Applying Pruning Theory for critical T1050-T1250 period.");
                    // 剪定理論で「太いルート」をコミット
                    var committedActions = _pruningEngine.SelectAndCommitRoute(CurrentState, availableActions);
                    ApplyCommittedActions(committedActions);
                }
                else
                {
                    // 通常期間は、単純なヒューリスティックまたはランダムなアクションを選択
                    ApplyDefaultActions(availableActions);
                }

                // 4. シミュレーション状態の更新 (ジョブの生産、魔法の維持など)
                UpdateSimulationState();

                // 5. Safe-Fail: 状態の整合性チェック
                PerformStateIntegrityCheck();

                _logger.LogInfo($"Current State: Pop={CurrentState.Population:F0}, Stability={CurrentState.CivilizationStability:F2}, Tech={CurrentState.TechnologicalAdvancement:F2}, Threat={CurrentState.ThreatLevel:F2}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogCritical($"Critical error during simulation step at T{CurrentState.CurrentTime}. Simulation halted.", ex);
                // Safe-Fail: 致命的なエラーが発生した場合、シミュレーションを停止
                return false;
            }
        }

        /// <summary>
        /// 環境イベントを生成し、シミュレーション状態に影響を与える。
        /// </summary>
        private void GenerateEnvironmentalEvents()
        {
            // 例: 脅威レベルに応じて戦闘イベントを発生させる
            if (CurrentState.ThreatLevel > 0.4 && new Random().NextDouble() < CurrentState.ThreatLevel * 0.1)
            {
                _logger.LogWarning("High threat level triggered a combat encounter!");
                double soldierPopulation = CurrentState.ActiveJobs.FirstOrDefault(j => j.Type == JobType.Soldier)?.PopulationAssigned ?? 0;
                double militaryStrength = soldierPopulation * CurrentState.Resources.GetValueOrDefault("SoldierEquipment", 1.0);
                _combatSystem.ResolveCombat(CurrentState, militaryStrength, CurrentState.ThreatLevel);
            }

            // T1050-T1250期間の特殊イベント
            if (_timeManager.IsWithinPeriod(1050, 1250))
            {
                if (new Random().NextDouble() < 0.08) // 8%の確率で熱科学クラフトのヒント
                {
                    _logger.LogInfo("A strange energy signature is detected. Perhaps a new crafting opportunity for Thermal Science?");
                    // プレイヤーにブラインドクラフトを促すイベントを発生させる
                }
            }
        }

        /// <summary>
        /// 現在のシミュレーション状態に基づいて可能なアクションのリストを生成する。
        /// </summary>
        private List<ISimulationAction> GenerateAvailableActions(SimulationState state)
        {
            var actions = new List<ISimulationAction>();

            // ジョブ割り当てアクションの候補
            foreach (var job in state.ActiveJobs)
            {
                // 人口を増やすアクション (未割り当て人口がある場合)
                if (state.Population > 0)
                {
                    actions.Add(new JobAssignmentAction(job, 1));
                }
                // 人口を減らすアクション (割り当て人口がある場合)
                if (job.PopulationAssigned > 0)
                {
                    actions.Add(new JobAssignmentAction(job, -1));
                }
            }

            // 魔法（社会技術）アクティベーションアクションの候補
            foreach (var magic in GetAvailableMagics(state))
            {
                actions.Add(new ActivateMagicAction(magic));
            }

            // クラフトアクションの候補 (既知のレシピとブラインド試行)
            foreach (var recipe in _craftingSystem.GetKnownRecipes())
            {
                actions.Add(new CraftItemAction(recipe.Name, false, _craftingSystem));
            }
            // ブラインドクラフトの機会も提供
            actions.Add(new CraftItemAction(null, true, _craftingSystem));

            return actions;
        }

        /// <summary>
        /// 現在の状態でアクティベート可能な魔法（社会技術）のリストを返す。
        /// </summary>
        private IEnumerable<Magic> GetAvailableMagics(SimulationState state)
        {
            // ここでは仮の魔法リストを返す。実際にはゲームデータからロードされる。
            var allPossibleMagics = new List<Magic>
            {
                new Magic(MagicType.AgriculturalInnovation, "Crop Rotation", "Boosts food production.",
                    new Dictionary<string, double> { { "Knowledge", 50 }, { "Wood", 10 } },
                    new Dictionary<string, double>(),
                    new MagicEffect(new Dictionary<string, double> { { "Food", 0.2 } }, affectedJobs: new List<JobType> { JobType.Farmer })),
                new Magic(MagicType.SocialCohesionRitual, "Community Festival", "Increases civilization stability.",
                    new Dictionary<string, double> { { "Food", 100 }, { "Knowledge", 20 } },
                    new Dictionary<string, double> { { "Food", 5 } },
                    new MagicEffect(stability: 0.1)),
                new Magic(MagicType.UrbanPlanning, "Efficient City Layout", "Improves population growth and resource efficiency.",
                    new Dictionary<string, double> { { "Knowledge", 150 }, { "RefinedMetal", 30 } },
                    new Dictionary<string, double> { { "RefinedMetal", 2 } },
                    new MagicEffect(populationGrowth: 0.01, stability: 0.03, affectedJobs: new List<JobType> { JobType.Builder }))
            };

            return allPossibleMagics.Where(m => m.CanActivate(state));
        }

        /// <summary>
        /// 剪定理論によってコミットされたアクションを現在の状態に適用する。
        /// </summary>
        private void ApplyCommittedActions(List<ISimulationAction> actions)
        {
            foreach (var action in actions)
            {
                action.Apply(CurrentState, _logger, _magicSanitizer);
            }
        }

        /// <summary>
        /// 通常期間に適用されるデフォルトのアクション選択ロジック。
        /// </summary>
        private void ApplyDefaultActions(List<ISimulationAction> availableActions)
        {
            // Safe-Fail: アクションがない場合は何もしない
            if (!availableActions.Any())
            {
                _logger.LogInfo("No default actions to apply.");
                return;
            }

            // ここでは簡略化のため、ランダムに1つのアクションを選択して適用
            var random = new Random();
            var randomAction = availableActions[random.Next(availableActions.Count)];
            _logger.LogInfo($"Applying default action: {randomAction.GetType().Name}");
            randomAction.Apply(CurrentState, _logger, _magicSanitizer);

            // T1050-T1250期間外でもブラインドクラフトの機会を提供
            if (random.NextDouble() < 0.02) // 2%の確率
            {
                _logger.LogInfo("Attempting a blind thermal science craft during normal period.");
                _craftingSystem.CraftItem(CurrentState, null, true);
            }
        }

        /// <summary>
        /// シミュレーション状態を更新する（ジョブの生産、魔法の維持コスト、自然な変動など）。
        /// </summary>
        private void UpdateSimulationState()
        {
            double totalFoodConsumption = 0;
            double totalFoodProduction = 0;
            double totalAssignedPopulation = 0;

            // ジョブによるリソース生産と消費
            foreach (var job in CurrentState.ActiveJobs)
            {
                totalAssignedPopulation += job.PopulationAssigned;
                if (job.PopulationAssigned > 0)
                {
                    foreach (var kv in job.ResourceConsumptionPerTickPerCapita)
                    {
                        CurrentState.Resources[kv.Key] = CurrentState.Resources.GetValueOrDefault(kv.Key) - kv.Value * job.PopulationAssigned;
                        if (kv.Key == "Food") totalFoodConsumption += kv.Value * job.PopulationAssigned;
                    }
                    foreach (var kv in job.ResourceProductionPerTickPerCapita)
                    {
                        CurrentState.Resources[kv.Key] = CurrentState.Resources.GetValueOrDefault(kv.Key) + kv.Value * job.PopulationAssigned * (1 + job.SkillLevel);
                        if (kv.Key == "Food") totalFoodProduction += kv.Value * job.PopulationAssigned * (1 + job.SkillLevel);
                    }
                }
            }

            // 未割り当て人口の食料消費
            double unassignedPopulation = CurrentState.Population - totalAssignedPopulation;
            if (unassignedPopulation > 0)
            {
                double unassignedFoodConsumption = unassignedPopulation * 0.05; // 未割り当て人口も食料を消費
                CurrentState.Resources["Food"] = CurrentState.Resources.GetValueOrDefault("Food") - unassignedFoodConsumption;
                totalFoodConsumption += unassignedFoodConsumption;
            }


            // アクティブな魔法（社会技術）の維持コストと効果適用
            foreach (var magic in CurrentState.ActiveMagics.Where(m => m.IsActive).ToList())
            {
                bool canMaintain = true;
                foreach (var cost in magic.OngoingMaintenanceCostPerTick)
                {
                    if (CurrentState.Resources.GetValueOrDefault(cost.Key) < cost.Value)
                    {
                        _logger.LogWarning($"Magic '{magic.Name}' deactivated due to insufficient resource '{cost.Key}' for maintenance.");
                        magic.IsActive = false;
                        CurrentState.Resources[cost.Key] = 0; // リソースを0にクランプ
                        canMaintain = false;
                        break;
                    }
                    CurrentState.Resources[cost.Key] -= cost.Value;
                }
                if (canMaintain)
                {
                    ApplyMagicEffectToState(CurrentState, magic.Effect);
                }
            }

            // 人口変動
            double foodBalance = totalFoodProduction - totalFoodConsumption;
            double basePopulationGrowthRate = 0.001; // 基本的な人口増加率
            double currentPopulationGrowthModifier = CurrentState.ActiveMagics.Where(m => m.IsActive).Sum(m => m.Effect.PopulationGrowthModifier);

            if (foodBalance < 0)
            {
                // 食料不足の場合、人口減少
                double populationDecreaseFactor = Math.Abs(foodBalance) / (CurrentState.Population + 1); // 0除算回避
                CurrentState.Population = Math.Max(0, CurrentState.Population * (1 - Math.Min(populationDecreaseFactor, 0.05))); // 最大5%減少
                CurrentState.CivilizationStability = Math.Clamp(CurrentState.CivilizationStability - 0.01, 0, 1);
            }
            else
            {
                // 食料余剰の場合、人口増加
                double growthFactor = 1 + basePopulationGrowthRate + currentPopulationGrowthModifier + (foodBalance / (CurrentState.Population + 1)) * 0.0005;
                CurrentState.Population *= growthFactor;
            }
            CurrentState.Population = Math.Round(CurrentState.Population); // 人口は整数に

            // 自然な変動
            CurrentState.CivilizationStability = Math.Clamp(CurrentState.CivilizationStability + (0.001 - CurrentState.ThreatLevel * 0.002), 0, 1);
            CurrentState.TechnologicalAdvancement = Math.Clamp(CurrentState.TechnologicalAdvancement + 0.0005, 0, 1);
            CurrentState.ThreatLevel = Math.Clamp(CurrentState.ThreatLevel + (0.0005 * (1 - CurrentState.CivilizationStability)), 0, 1);
        }

        /// <summary>
        /// MagicEffectをシミュレーション状態に適用する。
        /// PruningTheoryEngine内の同名メソッドとロジックを共有。
        /// </summary>
        private void ApplyMagicEffectToState(SimulationState state, MagicEffect effect)
        {
            foreach (var kv in effect.ResourceModifier)
            {
                foreach (var jobType in effect.AffectedJobs)
                {
                    foreach (var job in state.ActiveJobs.Where(j => j.Type == jobType))
                    {
                        // 生産性向上/低下
                        if (job.ResourceProductionPerTickPerCapita.ContainsKey(kv.Key))
                        {
                            job.ResourceProductionPerTickPerCapita[kv.Key] *= (1 + kv.Value);
                        }
                    }
                }
            }

            state.CivilizationStability = Math.Clamp(state.CivilizationStability + effect.CivilizationStabilityModifier, 0, 1);
            state.TechnologicalAdvancement = Math.Clamp(state.TechnologicalAdvancement + effect.TechnologicalAdvancementModifier, 0, 1);
            state.ThreatLevel = Math.Clamp(state.ThreatLevel + effect.ThreatLevelModifier, 0, 1);
            state.Population *= (1 + effect.PopulationGrowthModifier);
        }

        /// <summary>
        /// Safe-Fail: シミュレーション状態の整合性を定期的にチェックし、異常を修正する。
        /// </summary>
        private void PerformStateIntegrityCheck()
        {
            // リソースが負になっていないかチェックし、クランプ
            foreach (var key in CurrentState.Resources.Keys.ToList())
            {
                if (CurrentState.Resources[key] < 0)
                {
                    _logger.LogCritical($"Resource '{key}' went negative ({CurrentState.Resources[key]:F2}) during state integrity check. Clamping to 0 and applying stability penalty.");
                    CurrentState.Resources[key] = 0;
                    CurrentState.CivilizationStability = Math.Clamp(CurrentState.CivilizationStability - 0.02, 0, 1); // 安定性ペナルティ
                }
            }

            // 安定性、技術、脅威レベルが範囲内に収まっているか再確認
            CurrentState.CivilizationStability = Math.Clamp(CurrentState.CivilizationStability, 0, 1);
            CurrentState.TechnologicalAdvancement = Math.Clamp(CurrentState.TechnologicalAdvancement, 0, 1);
            CurrentState.ThreatLevel = Math.Clamp(CurrentState.ThreatLevel, 0, 1);

            // ジョブの人口が負になっていないか
            foreach (var job in CurrentState.ActiveJobs)
            {
                if (job.PopulationAssigned < 0)
                {
                    _logger.LogCritical($"Job '{job.Name}' had negative population ({job.PopulationAssigned}). Clamping to 0.");
                    job.PopulationAssigned = 0;
                }
            }

            // 総人口が割り当て人口の合計を下回っていないか
            double totalAssigned = CurrentState.ActiveJobs.Sum(j => j.PopulationAssigned);
            if (CurrentState.Population < totalAssigned)
            {
                _logger.LogCritical($"Total population ({CurrentState.Population:F0}) is less than assigned population ({totalAssigned:F0}). Adjusting total population.");
                CurrentState.Population = totalAssigned;
            }
            // 人口が負にならないように
            CurrentState.Population = Math.Max(0, CurrentState.Population);
        }
    }

    // --- 10. Utility / Mock Logger ---

    /// <summary>
    /// コンソールにログを出力するシンプルなロガー実装。
    /// </summary>
    public class ConsoleLogger : ILogger
    {
        public void LogInfo(string message) => Console.WriteLine($"[INFO] {message}");
        public void LogWarning(string message) => Console.WriteLine($"[WARNING] {message}");
        public void LogError(string message, Exception exception = null) => Console.WriteLine($"[ERROR] {message} {exception?.ToString()}");
        public void LogCritical(string message, Exception exception = null) => Console.WriteLine($"[CRITICAL] {message} {exception?.ToString()}");
    }

    // --- Main Program (Example Usage) ---
    public class Program
    {
        public static void Main(string[] args)
        {
            ILogger logger = new ConsoleLogger();
            GameSimulationManager simulation = new GameSimulationManager(logger, startTime: 1000, endTime: 1300); // T1000からT1300までシミュレート

            logger.LogInfo("Starting Game Simulation...");

            while (simulation.SimulateStep())
            {
                // シミュレーションが継続する限りループ
                // Console.WriteLine("Press any key to continue to next step, or 'q' to quit.");
                // if (Console.ReadKey().KeyChar == 'q') break;
                System.Threading.Thread.Sleep(50); // 少し待機してログを見やすくする
            }

            logger.LogInfo("Game Simulation Ended.");
        }
    }
}
```
はい、承知いたしました。
「フロム風戦闘×ブラインド熱科学クラフト×千年史自律シミュレーター」のリードディレクター兼C#設計者として、ターン1050から1250までの200年間（第22〜25世代）を連続自律進行させるための、精密なC#実装指示プロンプトをMarkdown形式で出力します。

このプロンプトは、Safe-Fail構造、MagicSanitizerEngine、Job/Magicの定義規約（魔法=社会技術, ジョブ=生活職業）を厳格に守り、スクルドの剪定理論に基づき未来の選択肢・学術・街道開拓の可能性が最も太い復興ルートをMAGI自動合議で正史にコミットし続けるロジックをC#コードとして表現します。

CursorのCtrl+Lにそのまま読み込ませてご活用ください。

---

```csharp
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// ゲームシミュレーションのメイン管理クラス。
/// ターン1050から1250までの200年間（第22〜25世代）の連続自律進行を制御します。
/// スクルドの剪定理論に基づき、未来の選択肢・学術・街道開拓の可能性が最も太い復興ルートをMAGI自動合議で正史にコミットし続けます。
/// </summary>
public class GameSimulationManager
{
    private SimulationState _currentState;
    private SkuldPruningTheory _pruningTheory;
    private MAGI_ConsensusEngine _magiEngine;
    private MagicSanitizerEngine _sanitizerEngine;
    private HistoricalRecordManager _recordManager;

    private const int SimulationStartTurn = 1050;
    private const int SimulationEndTurn = 1250; // ターン1250まで実行
    private const int InitialGeneration = 22; // ターン1050が第22世代の開始
    private const int TurnsPerGeneration = 50; // 1世代あたりのターン数 (200年 / 4世代 = 50年/世代)
    private const int MaxGeneration = 25; // 指示された最大世代

    /// <summary>
    /// GameSimulationManagerの新しいインスタンスを初期化します。
    /// </summary>
    /// <param name="initialState">シミュレーションの初期状態。</param>
    public GameSimulationManager(SimulationState initialState)
    {
        _currentState = initialState ?? throw new ArgumentNullException(nameof(initialState));
        _pruningTheory = new SkuldPruningTheory();
        _magiEngine = new MAGI_ConsensusEngine();
        _sanitizerEngine = new MagicSanitizerEngine();
        _recordManager = new HistoricalRecordManager();

        // 初期状態のターンと世代を設定
        _currentState.CurrentTurn = SimulationStartTurn;
        _currentState.CurrentGeneration = InitialGeneration;
    }

    /// <summary>
    /// シミュレーションを自律的に進行させます。
    /// ターン1050から1250まで、スクルドの剪定理論とMAGI合議に基づいて正史をコミットします。
    /// Safe-Fail構造により、予期せぬエラー発生時にも堅牢に対応します。
    /// </summary>
    public void RunAutonomousSimulation()
    {
        Console.WriteLine($"--- Autonomous Simulation Started ---");
        Console.WriteLine($"Period: Turn {SimulationStartTurn} to {SimulationEndTurn} (Generations {InitialGeneration} to {MaxGeneration})");

        try
        {
            for (int turn = SimulationStartTurn; turn <= SimulationEndTurn; turn++)
            {
                _currentState.CurrentTurn = turn;
                // 世代計算: 22世代目から開始し、50ターンごとに世代を進める。ただし、最大25世代までとする。
                _currentState.CurrentGeneration = Math.Min(MaxGeneration, InitialGeneration + (turn - SimulationStartTurn) / TurnsPerGeneration);

                Console.WriteLine($"\n--- Turn {turn} (Generation {_currentState.CurrentGeneration}) ---");

                // 1. 現在の状態に基づき、未来の選択肢（復興ルート）を生成
                List<FuturePathOption> potentialPaths = GenerateFuturePathOptions(_currentState);
                if (!potentialPaths.Any())
                {
                    throw new InvalidOperationException($"No future path options generated at Turn {turn}. Simulation cannot proceed.");
                }

                // 2. スクルドの剪定理論に基づき、各パスの「可能性の太さ」を評価
                // 未来の選択肢・学術・街道開拓の可能性が最も太いルートを重視
                _pruningTheory.EvaluatePaths(potentialPaths, _currentState);

                // 3. MAGI自動合議システムが最も太いルートを決定
                FuturePathOption chosenPath = _magiEngine.AchieveConsensus(potentialPaths);

                if (chosenPath == null)
                {
                    // Safe-Fail: 合議が成立しない場合はエラーとしてシミュレーションを停止
                    throw new InvalidOperationException($"MAGI failed to achieve consensus at Turn {turn}. Simulation halted.");
                }

                Console.WriteLine($"MAGI Consensus: Chosen path '{chosenPath.Description}' with Pruning Score: {chosenPath.PruningScore:F4}");

                // 4. 決定されたルートを正史にコミットし、シミュレーション状態を更新
                _recordManager.CommitToChronicle(_currentState, chosenPath);
                ApplyChosenPath(_currentState, chosenPath);

                // 5. MagicSanitizerEngineによる社会技術の健全性チェックと調整
                _sanitizerEngine.SanitizeAllMagics(_currentState.AvailableMagics);

                // Safe-Fail: 各ターンの終了時にシミュレーション状態の健全性をチェック
                if (!_currentState.IsValid())
                {
                    throw new InvalidOperationException($"Simulation state became invalid at Turn {turn}. Halting simulation to prevent corruption.");
                }

                // 進行状況のログ出力
                LogSimulationProgress(_currentState);
            }
            Console.WriteLine($"\n--- Autonomous Simulation Completed Successfully up to Turn {SimulationEndTurn}. ---");
        }
        catch (Exception ex)
        {
            // Safe-Fail: 予期せぬ例外が発生した場合の処理
            Console.Error.WriteLine($"\nCRITICAL ERROR during autonomous simulation at Turn {_currentState.CurrentTurn}: {ex.Message}");
            Console.Error.WriteLine($"StackTrace: {ex.StackTrace}");
            _recordManager.LogCriticalFailure(_currentState, ex); // エラー発生時の状態を詳細に記録
            Console.WriteLine("Simulation halted due to a critical error. Please review logs.");
        }
    }

    /// <summary>
    /// 現在のシミュレーション状態に基づき、未来の選択肢を生成します。
    /// </summary>
    /// <param name="state">現在のシミュレーション状態。</param>
    /// <returns>生成されたFuturePathOptionのリスト。</returns>
    private List<FuturePathOption> GenerateFuturePathOptions(SimulationState state)
    {
        // TODO: 実際のゲームロジックに基づいて、現在の状況に応じた多様な選択肢を動的に生成する
        // 例: 資源量、人口、既存の技術レベル、外交関係などによって選択肢が変化
        List<FuturePathOption> options = new List<FuturePathOption>();

        // 仮の選択肢生成ロジック
        options.Add(new FuturePathOption("Focus on Advanced Arcane Research", PathType.Academic, 0.7 + state.AcademicProgress.GetValueOrDefault("Arcane", 0.0)));
        options.Add(new FuturePathOption("Expand Eastern Trade Routes", PathType.RoadDevelopment, 0.6 + (state.RoadNetworkStatus.GetValueOrDefault("East", false) ? 0.1 : 0.0)));
        options.Add(new FuturePathOption("Develop Sustainable Agriculture Magics", PathType.SocialTechnology, 0.8 + state.AvailableMagics.Count(m => m.Name.Contains("Agriculture")) * 0.05));
        options.Add(new FuturePathOption("Invest in Urban Infrastructure", PathType.Other, 0.65 + (double)state.Population / 1_000_000));
        options.Add(new FuturePathOption("Explore Ancient Ruins for Lost Knowledge", PathType.Academic, 0.75));
        options.Add(new FuturePathOption("Forge Alliance with Neighboring Faction", PathType.Other, 0.5));

        return options;
    }

    /// <summary>
    /// MAGIによって選択されたパスをシミュレーション状態に適用します。
    /// </summary>
    /// <param name="state">更新対象のシミュレーション状態。</param>
    /// <param name="path">適用するFuturePathOption。</param>
    private void ApplyChosenPath(SimulationState state, FuturePathOption path)
    {
        // TODO: 選択されたパスがシミュレーション状態に与える具体的な影響を実装する
        // 例: 学術ポイントの増減、街道開拓状況の更新、新しいMagicやJobのアンロック、資源・人口の変動など
        Console.WriteLine($"  Applying effects of: {path.Description}");
        state.ApplyPathEffects(path); // SimulationStateクラス内にApplyPathEffectsメソッドを想定
    }

    /// <summary>
    /// 現在のシミュレーション進行状況をコンソールにログ出力します。
    /// </summary>
    /// <param name="state">現在のシミュレーション状態。</param>
    private void LogSimulationProgress(SimulationState state)
    {
        Console.WriteLine($"  Current Status: Pop={state.Population}, Res={state.Resources:F0}");
        Console.WriteLine($"  Academic Progress: {string.Join(", ", state.AcademicProgress.Select(kv => $"{kv.Key}:{kv.Value:F2}"))}");
        Console.WriteLine($"  Road Network: {string.Join(", ", state.RoadNetworkStatus.Select(kv => $"{kv.Key}:{(kv.Value ? "Connected" : "Disconnected")}"))}");
        Console.WriteLine($"  Active Social Technologies (Magics): {state.AvailableMagics.Count(m => m.IsSocialTechnology)}");
        Console.WriteLine($"  Active Life Occupations (Jobs): {state.AvailableJobs.Count(j => j.IsLifeOccupation)}");
    }
}

/// <summary>
/// シミュレーションの現在の状態を保持するクラス。
/// </summary>
public class SimulationState
{
    public int CurrentTurn { get; set; }
    public int CurrentGeneration { get; set; }
    public long Population { get; set; }
    public double Resources { get; set; }
    public List<Magic> AvailableMagics { get; private set; } = new List<Magic>();
    public List<Job> AvailableJobs { get; private set; } = new List<Job>();
    public Dictionary<string, double> AcademicProgress { get; private set; } = new Dictionary<string, double>();
    public Dictionary<string, bool> RoadNetworkStatus { get; private set; } = new Dictionary<string, bool>();
    public List<FuturePathOption> CommittedHistory { get; private set; } = new List<FuturePathOption>();

    /// <summary>
    /// SimulationStateの新しいインスタンスを初期化します。
    /// </summary>
    /// <param name="initialTurn">初期ターン。</param>
    /// <param name="initialGeneration">初期世代。</param>
    public SimulationState(int initialTurn, int initialGeneration)
    {
        CurrentTurn = initialTurn;
        CurrentGeneration = initialGeneration;
        Population = 1_000_000; // 初期人口
        Resources = 100_000.0; // 初期資源
        AcademicProgress["AncientLore"] = 0.5;
        AcademicProgress["Arcane"] = 0.3;
        RoadNetworkStatus["CentralHighway"] = true;
        RoadNetworkStatus["West"] = false;
        AvailableMagics.Add(new Magic("BasicFarmingTech", "基本的な農業技術", true, 1.2));
        AvailableJobs.Add(new Job("Farmer", "食料生産者", true, 1.0));
    }

    /// <summary>
    /// 選択されたパスの効果をシミュレーション状態に適用します。
    /// </summary>
    /// <param name="path">適用するFuturePathOption。</param>
    public void ApplyPathEffects(FuturePathOption path)
    {
        // パスの種類とスコアに応じて状態を更新する具体的なロジック
        double effectMultiplier = path.PruningScore; // スコアが高いほど効果も大きい

        switch (path.Type)
        {
            case PathType.Academic:
                // 学術進捗を増加させる
                string academicField = path.Description.Contains("Arcane") ? "Arcane" : "NewField";
                AcademicProgress[academicField] = AcademicProgress.GetValueOrDefault(academicField, 0.0) + 0.1 * effectMultiplier;
                Console.WriteLine($"    Academic progress in '{academicField}' increased.");
                break;
            case PathType.RoadDevelopment:
                // 街道開拓状況を更新する
                string roadName = path.Description.Contains("Eastern") ? "East" : "NewRoad";
                RoadNetworkStatus[roadName] = true;
                Console.WriteLine($"    Road network '{roadName}' developed.");
                break;
            case PathType.SocialTechnology:
                // 新しい社会技術（Magic）を追加する
                string magicName = path.Description.Replace(" ", "");
                if (!AvailableMagics.Any(m => m.Name == magicName))
                {
                    AvailableMagics.Add(new Magic(magicName, path.Description, true, 1.0 + effectMultiplier * 0.5));
                    Console.WriteLine($"    New social technology '{magicName}' developed.");
                }
                break;
            case PathType.Other:
                // その他の影響（人口、資源など）
                Population += (long)(10000 * effectMultiplier);
                Resources += 5000 * effectMultiplier;
                Console.WriteLine($"    Population and resources adjusted.");
                break;
        }
        CommittedHistory.Add(path); // 選択されたパスを歴史として記録
    }

    /// <summary>
    /// シミュレーション状態が健全であるか（Safe-Fail構造の一部）をチェックします。
    /// </summary>
    /// <returns>状態が健全であればtrue、そうでなければfalse。</returns>
    public bool IsValid()
    {
        // TODO: シミュレーションが破綻していないか、ゲームバランスが崩れていないかなどの詳細なチェックを追加
        return Population > 0 && Resources >= 0 && AvailableMagics.All(m => m != null) && AvailableJobs.All(j => j != null);
    }
}

/// <summary>
/// 未来の選択肢（復興ルート）を表すクラス。
/// </summary>
public class FuturePathOption
{
    public string Description { get; }
    public PathType Type { get; }
    public double BasePotential { get; } // この選択肢が持つ基本的な可能性の太さ
    public double PruningScore { get; set; } // スクルドの剪定理論によって評価された最終スコア

    /// <summary>
    /// FuturePathOptionの新しいインスタンスを初期化します。
    /// </summary>
    /// <param name="description">選択肢の簡潔な説明。</param>
    /// <param name="type">選択肢の種類（学術、街道開拓、社会技術など）。</param>
    /// <param name="basePotential">この選択肢の初期的な可能性の太さ（0.0～1.0）。</param>
    public FuturePathOption(string description, PathType type, double basePotential)
    {
        Description = description;
        Type = type;
        BasePotential = basePotential;
        PruningScore = basePotential; // 初期評価スコアは基本可能性とする
    }
}

/// <summary>
/// FuturePathOptionの種類を定義する列挙型。
/// </summary>
public enum PathType
{
    Academic,           // 学術研究、知識の探求
    RoadDevelopment,    // 街道開拓、インフラ整備
    SocialTechnology,   // 社会技術（魔法）の開発・適用
    Other               // その他の戦略的選択
}

/// <summary>
/// スクルドの剪定理論を実装するクラス。
/// 未来の選択肢の「可能性の太さ」を評価します。
/// </summary>
public class SkuldPruningTheory
{
    /// <summary>
    /// 提供された未来の選択肢リストを、現在のシミュレーション状態に基づいて評価し、PruningScoreを更新します。
    /// 「未来の選択肢・学術・街道開拓の可能性が最も太い復興ルート」を重視します。
    /// </summary>
    /// <param name="paths">評価対象のFuturePathOptionリスト。</param>
    /// <param name="state">現在のシミュレーション状態。</param>
    public void EvaluatePaths(List<FuturePathOption> paths, SimulationState state)
    {
        Console.WriteLine("  Evaluating paths using Skuld's Pruning Theory...");
        foreach (var path in paths)
        {
            double score = path.BasePotential;

            // 1. 学術の可能性を評価
            if (path.Type == PathType.Academic)
            {
                // 現在の学術レベルが高いほど、学術ルートの可能性は太くなる
                score *= (1.0 + state.AcademicProgress.Values.Sum() / Math.Max(1, state.AcademicProgress.Count) * 0.5);
            }
            // 2. 街道開拓の可能性を評価
            else if (path.Type == PathType.RoadDevelopment)
            {
                // 現在の街道ネットワークが広がるほど、更なる開拓の可能性は太くなる
                score *= (1.0 + state.RoadNetworkStatus.Count(kv => kv.Value) * 0.1);
            }
            // 3. 社会技術（魔法）の可能性を評価
            else if (path.Type == PathType.SocialTechnology)
            {
                // 既存の社会技術が多いほど、新たな技術開発の可能性は太くなる
                score *= (1.0 + state.AvailableMagics.Count(m => m.IsSocialTechnology) * 0.05);
            }

            // 4. その他の全体的な復興要素を考慮
            // 人口が多いほど、大規模なプロジェクトを遂行できる可能性が太くなる
            score *= (1.0 + (double)state.Population / 5_000_000);
            // 資源が豊富であるほど、多様な選択肢を実行できる可能性が太くなる
            score *= (1.0 + state.Resources / 500_000);
            // 世代が進むにつれて、より複雑な選択肢の重要性が増す可能性
            score *= (1.0 + (state.CurrentGeneration - 20) * 0.02); // 20世代目以降で世代が上がるごとにボーナス

            // スコアを0.0～1.0の範囲に正規化し、過度な変動を抑制
            path.PruningScore = Math.Min(1.0, Math.Max(0.0, score));
            Console.WriteLine($"    Path '{path.Description}' evaluated with score: {path.PruningScore:F4}");
        }
    }
}

/// <summary>
/// MAGI自動合議システム。
/// スクルドの剪定理論で評価されたパスの中から、最も「太い」ルートを合議で決定します。
/// </summary>
public class MAGI_ConsensusEngine
{
    /// <summary>
    /// 複数の潜在的なパスの中から、合議によって最適なパスを決定します。
    /// 現在は最もPruningScoreが高いパスを選択するシンプルなロジックですが、
    /// 実際には複数の「MAGI」が異なる視点から評価し、投票や交渉を通じて決定する複雑なシステムを想定しています。
    /// </summary>
    /// <param name="potentialPaths">評価済みのFuturePathOptionリスト。</param>
    /// <returns>合議によって決定されたFuturePathOption。合議が成立しない場合はnull。</returns>
    public FuturePathOption AchieveConsensus(List<FuturePathOption> potentialPaths)
    {
        if (!potentialPaths.Any())
        {
            Console.WriteLine("  MAGI: No potential paths to achieve consensus on.");
            return null;
        }

        // TODO: 複数の仮想MAGIユニット（Melchior, Balthasar, Casparなど）が、
        // それぞれ異なる優先順位（例: 安定性、成長、革新）に基づいてパスを評価し、
        // 最終的な合議を形成する複雑なロジックを実装する。
        // 現状は最も高いPruningScoreを持つパスを単純に選択。
        FuturePathOption chosen = potentialPaths.OrderByDescending(p => p.PruningScore).FirstOrDefault();

        if (chosen == null)
        {
            Console.WriteLine("  MAGI: Consensus failed to identify a suitable path.");
        }
        else
        {
            Console.WriteLine($"  MAGI: Consensus reached. Chosen path is '{chosen.Description}'.");
        }
        return chosen;
    }
}

/// <summary>
/// MagicSanitizerEngine。
/// 魔法（社会技術）の定義規約を厳格に守り、ゲームバランスを崩す可能性のある要素を自動的に健全化します。
/// </summary>
public class MagicSanitizerEngine
{
    /// <summary>
    /// 全ての利用可能なMagicオブジェクトを検査し、定義規約に沿っているか、ゲームバランスを崩さないかを検証・調整します。
    /// </summary>
    /// <param name="magics">健全化対象のMagicオブジェクトのリスト。</param>
    public void SanitizeAllMagics(List<Magic> magics)
    {
        Console.WriteLine("  MagicSanitizerEngine: Running integrity check on all Magics...");
        foreach (var magic in magics)
        {
            SanitizeMagic(magic);
        }
        Console.WriteLine("  MagicSanitizerEngine: Check completed.");
    }

    /// <summary>
    /// 個々のMagicオブジェクトを健全化します。
    /// </summary>
    /// <param name="magic">健全化対象のMagicオブジェクト。</param>
    private void SanitizeMagic(Magic magic)
    {
        // 規約: 魔法 = 社会技術
        if (!magic.IsSocialTechnology)
        {
            Console.WriteLine($"    [Sanitizer] WARNING: Magic '{magic.Name}' is not marked as a social technology. Forcing 'IsSocialTechnology = true'.");
            magic.IsSocialTechnology = true; // 規約に強制的に合わせる
            // 必要に応じて、その効果を調整する
            magic.EffectMagnitude = Math.Min(magic.EffectMagnitude, 5.0); // 非社会技術的な効果を抑制
        }

        // ゲームバランスを崩すような強力すぎる効果を調整
        if (magic.EffectMagnitude > 10.0) // 仮の閾値
        {
            Console.WriteLine($"    [Sanitizer] WARNING: Magic '{magic.Name}' has an excessively high effect magnitude ({magic.EffectMagnitude:F2}). Reducing to 10.0.");
            magic.EffectMagnitude = 10.0; // 上限を設定
        }
        else if (magic.EffectMagnitude < 0.1) // 効果が小さすぎる場合も調整
        {
            Console.WriteLine($"    [Sanitizer] WARNING: Magic '{magic.Name}' has an excessively low effect magnitude ({magic.EffectMagnitude:F2}). Increasing to 0.1.");
            magic.EffectMagnitude = 0.1; // 下限を設定
        }

        // TODO: その他、不整合な組み合わせ、前提条件を満たさないMagicなどを検出・修正するロジックを追加
    }
}

/// <summary>
/// HistoricalRecordManager。
/// シミュレーションの進行とMAGIの決定を正史として記録し、エラー発生時には詳細なログを残します。
/// </summary>
public class HistoricalRecordManager
{
    /// <summary>
    /// 現在のシミュレーション状態とMAGIによって選択されたパスを正史として記録します。
    /// </summary>
    /// <param name="state">現在のシミュレーション状態。</param>
    /// <param name="chosenPath">MAGIによって選択されたFuturePathOption。</param>
    public void CommitToChronicle(SimulationState state, FuturePathOption chosenPath)
    {
        // TODO: データベース、ファイル、または内部の永続的な歴史ログに詳細な情報を保存するロジックを実装
        // 例: JSON形式で状態をシリアライズして保存、特定のイベントログを記録
        Console.WriteLine($"  Chronicle: Turn {state.CurrentTurn} (Gen {state.CurrentGeneration}) - Committed '{chosenPath.Description}' (Score: {chosenPath.PruningScore:F4})");
    }

    /// <summary>
    /// シミュレーション中に発生した致命的なエラーを詳細にログに記録します。
    /// Safe-Fail構造の一部として、問題発生時の状況を把握するために重要です。
    /// </summary>
    /// <param name="state">エラー発生時のシミュレーション状態。</param>
    /// <param name="ex">発生した例外オブジェクト。</param>
    public void LogCriticalFailure(SimulationState state, Exception ex)
    {
        // TODO: エラーログファイルへの書き込み、監視システムへの通知など、より堅牢なエラーハンドリングを実装
        Console.Error.WriteLine($"\n--- CRITICAL FAILURE LOG ---");
        Console.Error.WriteLine($"Timestamp: {DateTime.UtcNow}");
        Console.Error.WriteLine($"Turn: {state.CurrentTurn}, Generation: {state.CurrentGeneration}");
        Console.Error.WriteLine($"Population: {state.Population}, Resources: {state.Resources}");
        Console.Error.WriteLine($"Exception Type: {ex.GetType().Name}");
        Console.Error.WriteLine($"Message: {ex.Message}");
        Console.Error.WriteLine($"StackTrace:\n{ex.StackTrace}");
        Console.Error.WriteLine($"--- END CRITICAL FAILURE LOG ---\n");
    }
}

/// <summary>
/// Magicクラス。規約により「魔法=社会技術」として扱われます。
/// </summary>
public class Magic
{
    public string Name { get; }
    public string Description { get; }
    public bool IsSocialTechnology { get; set; } // 規約: 魔法は社会技術である
    public double EffectMagnitude { get; set; } // 効果の大きさ

    /// <summary>
    /// Magicの新しいインスタンスを初期化します。
    /// </summary>
    /// <param name="name">魔法の名前。</param>
    /// <param name="description">魔法の説明。</param>
    /// <param name="isSocialTechnology">この魔法が社会技術であるか。</param>
    /// <param name="effectMagnitude">魔法の効果の大きさ。</param>
    public Magic(string name, string description, bool isSocialTechnology, double effectMagnitude = 1.0)
    {
        Name = name;
        Description = description;
        IsSocialTechnology = isSocialTechnology;
        EffectMagnitude = effectMagnitude;
    }
}

/// <summary>
/// Jobクラス。規約により「ジョブ=生活職業」として扱われます。
/// </summary>
public class Job
{
    public string Name { get; }
    public string Description { get; }
    public bool IsLifeOccupation { get; } // 規約: ジョブは生活職業である
    public double Productivity { get; set; } // 生産性や効率

    /// <summary>
    /// Jobの新しいインスタンスを初期化します。
    /// </summary>
    /// <param name="name">ジョブの名前。</param>
    /// <param name="description">ジョブの説明。</param>
    /// <param name="isLifeOccupation">このジョブが生活職業であるか。</param>
    /// <param name="productivity">ジョブの生産性。</param>
    public Job(string name, string description, bool isLifeOccupation, double productivity = 1.0)
    {
        Name = name;
        Description = description;
        IsLifeOccupation = isLifeOccupation;
        Productivity = productivity;
    }
}

// --- シミュレーション実行例 ---
public class Program
{
    public static void Main(string[] args)
    {
        // 初期状態のシミュレーションインスタンスを作成
        SimulationState initialState = new SimulationState(1050, 22); // ターン1050, 第22世代から開始

        // ゲームシミュレーションマネージャーを初期化
        GameSimulationManager gameManager = new GameSimulationManager(initialState);

        // 自律シミュレーションを実行
        gameManager.RunAutonomousSimulation();

        Console.WriteLine("\nPress any key to exit.");
        Console.ReadKey();
    }
}
```
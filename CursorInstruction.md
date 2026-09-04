承知いたしました。リードディレクターとして、ターン1050から1250までの200年間（第22〜25世代）を連続自律進行させ、スクルドの剪定理論に基づきMAGI自動合議で正史にコミットし続けるための、精密なC#実装指示プロンプトを生成します。

このプロンプトは、Cursor(IDE)のCtrl+Lにそのまま読み込ませてC#コード化できるよう、Markdown形式で記述されています。Safe-Fail構造、MagicSanitizerEngine、Job/Magicの定義規約を厳格に守ります。

---

## 精密な実装指示プロンプト：千年史自律シミュレーター - ターン1050-1250連続進行

### 目的

ターン1050から1250までの200年間（第22〜25世代）を連続自律進行させます。各ターンにおいて、MAGI自動合議システムがスクルドの剪定理論に基づき、未来の選択肢、学術、街道開拓の可能性が最も太い復興ルートを評価・選択し、正史にコミットし続けます。

### 前提条件

以下のクラスは既に存在し、基本的な構造を持っているものと仮定します。

*   `SimulationState`: 現在のターン、世代、世界の状態（地理、資源、社会構造、技術レベル、利用可能な選択肢、学術研究状況、街道開拓状況など）を保持するクラス。
*   `Magic`: 魔法（社会技術）を表すクラス。
*   `Job`: ジョブ（生活職業）を表すクラス。
*   `Logger`: ログ出力のための簡易的なインターフェースまたはクラス（ここでは`Console.WriteLine`で代用）。

### 実装指示

以下のクラスとメソッドを定義し、指定されたロジックを実装してください。

---

### 1. `MagicSanitizerEngine` クラス

**役割**: 魔法（社会技術）がゲームバランスや世界観を逸脱しないよう、その健全性を検証・調整するエンジン。

```csharp
using System;
using System.Collections.Generic;

public class MagicSanitizerEngine
{
    /// <summary>
    /// 魔法（社会技術）の健全性を検証し、必要に応じて調整します。
    /// Safe-Fail: 不正な魔法はデフォルト値に調整するか、警告を発します。
    /// </summary>
    /// <param name="magic">検証・調整対象のMagicオブジェクト。</param>
    /// <returns>健全化されたMagicオブジェクト。</returns>
    public Magic Sanitize(Magic magic)
    {
        if (magic == null)
        {
            Console.WriteLine("[ERROR][MagicSanitizer] Null Magicオブジェクトが渡されました。デフォルトの健全なMagicを返します。");
            return CreateDefaultSafeMagic(); // Safe-Fail: デフォルトの安全な魔法を生成
        }

        // 規約: 魔法 = 社会技術
        // 社会技術としての側面を考慮し、過度な効果や矛盾する効果を調整します。

        // 例: 効果値の範囲チェック
        if (magic.EffectMagnitude > 1000)
        {
            Console.WriteLine($"[WARNING][MagicSanitizer] Magic '{magic.Name}' のEffectMagnitudeが過大です ({magic.EffectMagnitude})。1000に調整します。");
            magic.EffectMagnitude = 1000; // 調整
        }
        if (magic.EffectMagnitude < 0)
        {
            Console.WriteLine($"[WARNING][MagicSanitizer] Magic '{magic.Name}' のEffectMagnitudeが負の値です ({magic.EffectMagnitude})。0に調整します。");
            magic.EffectMagnitude = 0; // 調整
        }

        // 例: コストの妥当性チェック
        if (magic.ManaCost < 10 && magic.EffectMagnitude > 500)
        {
            Console.WriteLine($"[WARNING][MagicSanitizer] Magic '{magic.Name}' は低コスト高効果です。ManaCostを調整します。");
            magic.ManaCost = (int)(magic.EffectMagnitude / 5); // 調整
        }

        // 例: 依存関係のチェック (架空の例)
        if (magic.RequiredTechs != null && magic.RequiredTechs.Contains("ForbiddenAncientTech") && !magic.IsForbidden)
        {
            Console.WriteLine($"[WARNING][MagicSanitizer] Magic '{magic.Name}' はForbiddenAncientTechを要求しますが、Forbiddenとしてマークされていません。マークします。");
            magic.IsForbidden = true; // 調整
        }

        // その他の社会技術としての整合性チェックや調整ロジックを追加...
        // 例: 特定の社会フェーズでのみ有効な技術、倫理的制約など

        Console.WriteLine($"[INFO][MagicSanitizer] Magic '{magic.Name}' を健全化しました。");
        return magic;
    }

    private Magic CreateDefaultSafeMagic()
    {
        // Safe-Fail: 何らかの問題があった場合に返す、デフォルトの安全な魔法
        return new Magic
        {
            Name = "Default_Safe_SocialTech",
            Description = "デフォルトの安全な社会技術。システムエラー時に生成されました。",
            EffectMagnitude = 10,
            ManaCost = 5,
            RequiredTechs = new List<string>(),
            IsForbidden = false
        };
    }
}
```

---

### 2. `SkuldPruningTheory` クラス

**役割**: スクルドの剪定理論に基づき、未来の選択肢・学術・街道開拓の可能性が最も太い復興ルートを評価する。

```csharp
using System;
using System.Collections.Generic;
using System.Linq;

public static class SkuldPruningTheory
{
    /// <summary>
    /// スクルドの剪定理論に基づき、与えられたシミュレーション状態の「復興ルートの太さ」を評価します。
    /// 評価値が高いほど、未来の可能性が豊かであることを示します。
    /// </summary>
    /// <param name="state">評価対象のSimulationState。</param>
    /// <returns>復興ルートの太さを示す評価値。</returns>
    public static double CalculateRevivalRouteStrength(SimulationState state)
    {
        if (state == null)
        {
            Console.WriteLine("[ERROR][SkuldPruningTheory] Null SimulationStateが渡されました。評価できません。");
            return -1.0; // Safe-Fail: 無効な状態
        }

        double strength = 0.0;

        // 1. 未来の選択肢の多様性
        // 利用可能なアクションやイベントの数が多いほど、多様性が高いと評価
        strength += state.AvailableChoices?.Count * 10 ?? 0; // Nullチェックとデフォルト値

        // 2. 学術研究の可能性
        // 未発見の技術ツリー、未研究の知識領域が多いほど、学術的発展の余地があると評価
        strength += state.UnresearchedTechBranches?.Count * 20 ?? 0;
        strength += state.UnexploredKnowledgeDomains?.Count * 15 ?? 0;

        // 3. 街道開拓の可能性
        // 未探索の領域、未発見の資源、未接続の地域が多いほど、開拓の余地があると評価
        strength += state.UnexploredRegions?.Count * 25 ?? 0;
        strength += state.UndiscoveredResources?.Count * 18 ?? 0;
        strength += state.UnconnectedSettlements?.Count * 12 ?? 0;

        // 既存の技術レベルや社会安定度も加味 (安定しているほど、新たな可能性を追求しやすい)
        strength += state.CurrentTechLevel * 0.5;
        strength += state.SocialStability * 0.3;

        // 負の要素 (例: 災害リスク、紛争リスク) は減点
        strength -= state.DisasterRisk * 50;
        strength -= state.ConflictRisk * 70;

        // 評価値が負になるのを防ぐ (最低値を保証)
        return Math.Max(0.0, strength);
    }
}
```

---

### 3. `MAGI_ConsensusEngine` クラス

**役割**: スクルドの剪定理論に基づき、複数の未来パスから最適なものを自動合議で選択し、正史にコミットする。

```csharp
using System;
using System.Collections.Generic;
using System.Linq;

public class MAGI_ConsensusEngine
{
    private readonly MagicSanitizerEngine _magicSanitizer;

    public MAGI_ConsensusEngine(MagicSanitizerEngine magicSanitizer)
    {
        _magicSanitizer = magicSanitizer ?? throw new ArgumentNullException(nameof(magicSanitizer));
    }

    /// <summary>
    /// 現在のシミュレーション状態から、スクルドの剪定理論に基づき最適な次の状態を決定し、正史にコミットします。
    /// Safe-Fail: 合議に失敗した場合、現在の状態を維持するか、最も安全なデフォルトパスを選択します。
    /// </summary>
    /// <param name="currentState">現在のシミュレーション状態。</param>
    /// <returns>MAGIによって選択され、コミットされた次のシミュレーション状態。</returns>
    public SimulationState DecideAndCommitNextState(SimulationState currentState)
    {
        if (currentState == null)
        {
            Console.WriteLine("[ERROR][MAGI_ConsensusEngine] Null currentStateが渡されました。決定できません。");
            throw new ArgumentNullException(nameof(currentState), "MAGIはNull状態では意思決定できません。");
        }

        Console.WriteLine($"[MAGI] ターン {currentState.CurrentTurn} の意思決定を開始します。");

        try
        {
            // 1. 複数の潜在的な未来パスを生成 (仮のロジック)
            // 実際には、利用可能な選択肢に基づいて複数の分岐パスを生成する必要があります。
            List<SimulationState> potentialFutures = GeneratePotentialFutureStates(currentState);

            if (!potentialFutures.Any())
            {
                Console.WriteLine("[WARNING][MAGI] 潜在的な未来パスが生成されませんでした。現在の状態を維持します。");
                return currentState; // Safe-Fail: パスがない場合は現在の状態を維持
            }

            // 2. 各未来パスをスクルドの剪定理論で評価
            var evaluatedPaths = potentialFutures
                .Select(path => new
                {
                    Path = path,
                    Strength = SkuldPruningTheory.CalculateRevivalRouteStrength(path)
                })
                .OrderByDescending(p => p.Strength)
                .ToList();

            // 3. MAGI合議: 最も評価の高いパスを選択
            SimulationState chosenPath = evaluatedPaths.FirstOrDefault()?.Path;

            if (chosenPath == null)
            {
                Console.WriteLine("[ERROR][MAGI] 評価されたパスから最適なパスを選択できませんでした。現在の状態を維持します。");
                return currentState; // Safe-Fail: 選択失敗
            }

            Console.WriteLine($"[MAGI] 最適な未来パスを決定しました (評価値: {evaluatedPaths.First().Strength:F2})。");

            // 4. 選択されたパスを正史にコミット
            CommitToTrueHistory(chosenPath);

            // 選択されたパス内の新しいMagic（社会技術）をサニタイズ
            if (chosenPath.NewlyDiscoveredMagics != null)
            {
                foreach (var magic in chosenPath.NewlyDiscoveredMagics)
                {
                    _magicSanitizer.Sanitize(magic);
                }
            }

            return chosenPath;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[CRITICAL][MAGI_ConsensusEngine] 意思決定中に致命的なエラーが発生しました: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
            // Safe-Fail: エラー発生時は現在の状態を維持し、シミュレーションの続行を試みる
            return currentState;
        }
    }

    /// <summary>
    /// 複数の潜在的な未来の状態を生成します。
    /// このメソッドは、現在の状態から可能なアクションやイベントをシミュレートし、
    /// それぞれの結果として得られる未来の状態をリストアップする役割を担います。
    /// </summary>
    /// <param name="currentState">現在のシミュレーション状態。</param>
    /// <returns>潜在的な未来のSimulationStateのリスト。</returns>
    private List<SimulationState> GeneratePotentialFutureStates(SimulationState currentState)
    {
        List<SimulationState> futures = new List<SimulationState>();

        // ここに、現在の状態から可能なアクション（例：研究、開拓、外交、戦闘など）を適用し、
        // それぞれの結果として得られる未来の状態を生成するロジックを実装します。
        // 各アクションは、SimulationStateのクローンを作成し、そのクローンに変更を加える形で行います。

        // 例: 3つの異なる未来パスを仮に生成
        for (int i = 0; i < 3; i++)
        {
            SimulationState futureState = currentState.Clone(); // Deep Cloneを想定
            futureState.CurrentTurn++;
            futureState.GenerationsElapsed = (futureState.CurrentTurn - 1) / 50 + 1; // 50ターンで1世代と仮定

            // 各パスに異なる特徴を与える (スクルドの剪定理論の評価に影響するように)
            switch (i)
            {
                case 0: // 技術重視パス
                    futureState.CurrentTechLevel += 0.5;
                    futureState.UnresearchedTechBranches.RemoveAll(b => b.Contains("Basic"));
                    futureState.AvailableChoices.Add("AdvancedResearchProject");
                    futureState.NewlyDiscoveredMagics.Add(_magicSanitizer.Sanitize(new Magic { Name = "Tech_Boost_SocialTech", EffectMagnitude = 70, ManaCost = 30 }));
                    break;
                case 1: // 開拓重視パス
                    futureState.UnexploredRegions.RemoveAll(r => r.Contains("Near"));
                    futureState.UndiscoveredResources.Add("NewRareMineral");
                    futureState.AvailableChoices.Add("ExpandRoadNetwork");
                    futureState.NewlyDiscoveredMagics.Add(_magicSanitizer.Sanitize(new Magic { Name = "Exploration_Aid_SocialTech", EffectMagnitude = 50, ManaCost = 20 }));
                    break;
                case 2: // 社会安定重視パス
                    futureState.SocialStability += 0.1;
                    futureState.ConflictRisk -= 0.05;
                    futureState.AvailableChoices.Add("DiplomaticInitiative");
                    futureState.NewlyDiscoveredMagics.Add(_magicSanitizer.Sanitize(new Magic { Name = "Harmony_SocialTech", EffectMagnitude = 60, ManaCost = 25 }));
                    break;
            }
            futures.Add(futureState);
        }

        return futures;
    }

    /// <summary>
    /// MAGIによって選択されたパスを正史（永続的な記録）にコミットします。
    /// </summary>
    /// <param name="chosenPath">正史にコミットするSimulationState。</param>
    private void CommitToTrueHistory(SimulationState chosenPath)
    {
        // ここに、選択されたSimulationStateをデータベース、ファイル、または永続ストレージに保存するロジックを実装します。
        // これは、シミュレーションの「正史」として記録され、将来の参照や分析に利用されます。
        Console.WriteLine($"[MAGI_COMMIT] ターン {chosenPath.CurrentTurn} の状態を正史にコミットしました。");
        // 例: Database.SaveState(chosenPath);
        // 例: HistoryLog.Append(chosenPath.ToJson());
    }
}
```

---

### 4. `SimulationManager` クラス

**役割**: シミュレーション全体の進行を管理し、MAGIエンジンを呼び出して自律進行させる。

```csharp
using System;
using System.Collections.Generic;

public class SimulationManager
{
    private SimulationState _currentSimulationState;
    private readonly MAGI_ConsensusEngine _magiEngine;
    private readonly MagicSanitizerEngine _magicSanitizer; // 直接は使わないが、MAGIに渡すため保持

    public SimulationManager(SimulationState initialState)
    {
        _currentSimulationState = initialState ?? throw new ArgumentNullException(nameof(initialState));
        _magicSanitizer = new MagicSanitizerEngine();
        _magiEngine = new MAGI_ConsensusEngine(_magicSanitizer);
    }

    /// <summary>
    /// 指定されたターン範囲でシミュレーションを連続自律進行させます。
    /// Safe-Fail: 各ターンでエラーが発生しても、可能な限りシミュレーションを続行します。
    /// </summary>
    /// <param name="startTurn">シミュレーションを開始するターン。</param>
    /// <param name="endTurn">シミュレーションを終了するターン。</param>
    public void RunAutonomousSimulation(int startTurn, int endTurn)
    {
        if (startTurn < 0 || endTurn < startTurn)
        {
            Console.WriteLine("[ERROR][SimulationManager] 無効なターン範囲が指定されました。");
            throw new ArgumentOutOfRangeException("ターン範囲は正しく指定される必要があります。");
        }

        Console.WriteLine($"\n--- シミュレーション開始: ターン {startTurn} から {endTurn} ---");

        // 現在のシミュレーション状態を初期ターンに合わせる
        _currentSimulationState.CurrentTurn = startTurn - 1; // 次のターンでstartTurnになるように

        for (int turn = startTurn; turn <= endTurn; turn++)
        {
            Console.WriteLine($"\n--- ターン {turn} (世代 {(turn - 1) / 50 + 1}) 進行中 ---");

            try
            {
                // 1. ターン開始前の処理 (イベント発生、リソース更新など)
                PreTurnUpdate(_currentSimulationState);

                // 2. MAGIによる意思決定と正史コミット
                // MAGIは次の状態を決定し、その状態を_currentSimulationStateに更新します。
                _currentSimulationState = _magiEngine.DecideAndCommitNextState(_currentSimulationState);

                // 3. ターン終了後の処理 (UI更新、レポート生成など)
                PostTurnUpdate(_currentSimulationState);

                // 進行状況の表示
                Console.WriteLine($"[SIM_PROGRESS] ターン {turn} 完了。現在の技術レベル: {_currentSimulationState.CurrentTechLevel:F2}, 社会安定度: {_currentSimulationState.SocialStability:F2}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CRITICAL][SimulationManager] ターン {turn} の処理中に致命的なエラーが発生しました: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
                // Safe-Fail: エラーが発生しても、次のターンに進むか、適切なリカバリを試みる
                // ここでは、エラーが発生したターンをスキップし、次のターンに進むことで自律進行を維持します。
                Console.WriteLine($"[SIM_RECOVERY] ターン {turn} はスキップされ、次のターンに進みます。");
            }
        }

        Console.WriteLine($"\n--- シミュレーション終了: ターン {endTurn} ---");
    }

    /// <summary>
    /// 各ターン開始前の処理をシミュレートします。
    /// </summary>
    /// <param name="state">現在のシミュレーション状態。</param>
    private void PreTurnUpdate(SimulationState state)
    {
        // 例: リソースの自動生成、ランダムイベントの発生、既存のプロジェクトの進行など
        // Console.WriteLine($"[PRE_TURN] ターン {state.CurrentTurn + 1} の準備中...");
        // state.Resources.Food += 100;
        // state.Population += 10;
    }

    /// <summary>
    /// 各ターン終了後の処理をシミュレートします。
    /// </summary>
    /// <param name="state">現在のシミュレーション状態。</param>
    private void PostTurnUpdate(SimulationState state)
    {
        // 例: UIの更新、レポートの生成、次のターンの準備など
        // Console.WriteLine($"[POST_TURN] ターン {state.CurrentTurn} の後処理中...");
        // ReportGenerator.GenerateTurnSummary(state);
    }
}
```

---

### 5. データ構造のスケルトン

以下のクラスは、シミュレーションのデータモデルとして必要です。

```csharp
using System;
using System.Collections.Generic;

/// <summary>
/// シミュレーションの現在の状態を保持するクラス。
/// </summary>
public class SimulationState
{
    public int CurrentTurn { get; set; }
    public int GenerationsElapsed { get; set; }
    public double CurrentTechLevel { get; set; }
    public double SocialStability { get; set; }
    public double DisasterRisk { get; set; }
    public double ConflictRisk { get; set; }

    // 未来の選択肢の多様性に関連するデータ
    public List<string> AvailableChoices { get; set; } = new List<string>();

    // 学術研究の可能性に関連するデータ
    public List<string> UnresearchedTechBranches { get; set; } = new List<string>();
    public List<string> UnexploredKnowledgeDomains { get; set; } = new List<string>();

    // 街道開拓の可能性に関連するデータ
    public List<string> UnexploredRegions { get; set; } = new List<string>();
    public List<string> UndiscoveredResources { get; set; } = new List<string>();
    public List<string> UnconnectedSettlements { get; set; } = new List<string>();

    // 新たに発見された魔法（社会技術）
    public List<Magic> NewlyDiscoveredMagics { get; set; } = new List<Magic>();

    public SimulationState Clone()
    {
        // ディープクローンを実装する必要があります。
        // ここでは簡易的な実装ですが、実際には全ての参照型プロパティもクローンする必要があります。
        return new SimulationState
        {
            CurrentTurn = this.CurrentTurn,
            GenerationsElapsed = this.GenerationsElapsed,
            CurrentTechLevel = this.CurrentTechLevel,
            SocialStability = this.SocialStability,
            DisasterRisk = this.DisasterRisk,
            ConflictRisk = this.ConflictRisk,
            AvailableChoices = new List<string>(this.AvailableChoices),
            UnresearchedTechBranches = new List<string>(this.UnresearchedTechBranches),
            UnexploredKnowledgeDomains = new List<string>(this.UnexploredKnowledgeDomains),
            UnexploredRegions = new List<string>(this.UnexploredRegions),
            UndiscoveredResources = new List<string>(this.UndiscoveredResources),
            UnconnectedSettlements = new List<string>(this.UnconnectedSettlements),
            NewlyDiscoveredMagics = this.NewlyDiscoveredMagics.ConvertAll(m => m.Clone()) // Magicもクローン
        };
    }
}

/// <summary>
/// 魔法（社会技術）を表すクラス。
/// 規約: 魔法 = 社会技術
/// </summary>
public class Magic
{
    public string Name { get; set; }
    public string Description { get; set; }
    public int EffectMagnitude { get; set; } // 効果の大きさ
    public int ManaCost { get; set; } // 発動コスト
    public List<string> RequiredTechs { get; set; } = new List<string>(); // 前提となる技術
    public bool IsForbidden { get; set; } // 禁忌の技術か

    public Magic Clone()
    {
        return new Magic
        {
            Name = this.Name,
            Description = this.Description,
            EffectMagnitude = this.EffectMagnitude,
            ManaCost = this.ManaCost,
            RequiredTechs = new List<string>(this.RequiredTechs),
            IsForbidden = this.IsForbidden
        };
    }
}

/// <summary>
/// ジョブ（生活職業）を表すクラス。
/// 規約: ジョブ = 生活職業
/// </summary>
public class Job
{
    public string Name { get; set; }
    public string Description { get; set; }
    public string PrimarySkill { get; set; } // 主要スキル
    public double ProductivityBonus { get; set; } // 生産性ボーナス
    // その他の生活職業に関連する属性
}
```

---

### 6. エントリポイント (`Program` クラス)

シミュレーションを開始するためのメインメソッド。

```csharp
using System;
using System.Collections.Generic;

public class Program
{
    public static void Main(string[] args)
    {
        Console.WriteLine("千年史自律シミュレーター起動...");

        // 初期シミュレーション状態のセットアップ
        SimulationState initialState = new SimulationState
        {
            CurrentTurn = 1049, // 1050ターン目から開始するため、初期値は1049
            GenerationsElapsed = 21, // 第22世代から開始するため、初期値は21
            CurrentTechLevel = 50.0,
            SocialStability = 0.8,
            DisasterRisk = 0.1,
            ConflictRisk = 0.05,
            AvailableChoices = new List<string> { "ResearchBasicTech", "ExploreLocalArea", "BuildSmallSettlement" },
            UnresearchedTechBranches = new List<string> { "AdvancedMetallurgy", "BioEngineering", "SpaceFlight" },
            UnexploredKnowledgeDomains = new List<string> { "QuantumPhysics", "AncientRunes", "PsychicStudies" },
            UnexploredRegions = new List<string> { "NorthernWastes", "SunkenCity", "FloatingIslands" },
            UndiscoveredResources = new List<string> { "AdamantiteOre", "AetherCrystal" },
            UnconnectedSettlements = new List<string> { "MountainVillage", "DesertOasis" }
        };

        // シミュレーションマネージャーのインスタンス化
        SimulationManager manager = new SimulationManager(initialState);

        // ターン1050から1250までの200年間（第22〜25世代）を連続自律進行
        int startTurn = 1050;
        int endTurn = 1250; // 200年間 = 200ターン (1年1ターンと仮定)

        try
        {
            manager.RunAutonomousSimulation(startTurn, endTurn);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n[FATAL_ERROR] シミュレーション全体で予期せぬエラーが発生しました: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
        }

        Console.WriteLine("\n千年史自律シミュレーター終了。");
    }
}
```

---

### 補足事項

*   **Safe-Fail構造**: 各主要メソッドには`try-catch`ブロックを配置し、エラー発生時にはログ出力、デフォルト値の返却、または現在の状態の維持を行うことで、シミュレーションの継続性を確保しています。`ArgumentNullException`や`ArgumentOutOfRangeException`による入力検証も含まれます。
*   **ロギング**: 簡易的に`Console.WriteLine`を使用していますが、実際のプロジェクトでは専用のロギングライブラリ（例: Serilog, NLog）を導入し、ログレベル（INFO, WARNING, ERROR, CRITICAL）に応じて出力先や詳細度を制御することを推奨します。
*   **`SimulationState.Clone()`**: ディープクローンは非常に重要です。`GeneratePotentialFutureStates`で複数の未来パスを生成する際に、元の状態を破壊しないよう、全ての参照型プロパティも適切にクローンする必要があります。上記のコードでは簡易的な実装に留めていますが、実際の開発ではシリアライゼーション/デシリアライゼーションを利用したり、専用のディープクローンロジックを実装したりしてください。
*   **MAGI合議の複雑性**: `GeneratePotentialFutureStates`や`SkuldPruningTheory.CalculateRevivalRouteStrength`のロジックは、シミュレーターの核となる部分です。フロム風戦闘、ブラインド熱科学クラフトの要素を深く組み込むには、これらのメソッド内で、戦闘結果の予測、未解明技術のランダムな発見、クラフトシステムの複雑性などを考慮した詳細なシミュレーションロジックを実装する必要があります。
*   **世代の計算**: 1世代を50ターンと仮定しています。この値はプロジェクトの要件に合わせて調整してください。

このプロンプトは、ユーザーの要求を網羅し、Cursorで直接利用可能な形で提供されています。
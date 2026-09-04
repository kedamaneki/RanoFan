はい、承知いたしました。
リードディレクターとして、ターン1001以降の「文明復興フェーズ」を起動し、1001〜1050年のIF分岐を爆撃生成、MAGI自動合議で正史をコミットするC#実装指示を精密に記述します。

以下のMarkdown形式のプロンプトをCursorのCtrl+Lにそのまま読み込ませてください。

---

# フロム風戦闘×ブラインド熱科学クラフト×千年史自律シミュレーター: 文明復興フェーズ実装指示

## 目的
ターン1001以降に「文明復興フェーズ」を起動し、1001年から1050年までの期間で多数のIF分岐（代替歴史シナリオ）を生成（爆撃生成）。その後、MAGI自動合議システムがこれらの分岐を評価し、最も適切なものを「正史」としてコミットする一連のプロセスを実装する。

## 全体アーキテクチャの概要
このフェーズは、以下の主要コンポーネントで構成されます。

1.  **`SafeFailResult<T>`**: 全ての操作結果を安全に扱うための汎用構造体。
2.  **`IMagicSanitizerEngine` / `MagicSanitizerEngine`**: 生成された歴史データやゲーム状態の整合性を検証・修正するエンジン。
3.  **`GameSimulationManager`**: シミュレーションの全体を管理し、ターン進行とフェーズ起動を制御する。
4.  **`CivilizationRecoveryPhase`**: 文明復興フェーズの具体的なロジックをカプセル化する。
5.  **`HistoricalBranchGenerator`**: 1001-1050年のIF分岐を「爆撃生成」する。
6.  **`MAGI_ConsensusEngine`**: 生成された分岐を評価し、正史をコミットする。
7.  **データ構造**: `HistoricalBranch`, `HistoricalEvent`, `Magic`, `Job` など。

## コアデータ構造

### `Magic` (社会技術) 規約
```csharp
using System;
using System.Collections.Generic;

public record MagicID(Guid Value);
public record ResourceAmount(string ResourceName, int Amount);
public record GameEffect(string EffectType, string Target, double Value);
public record SkillID(Guid Value);

/// <summary>
/// 魔法（社会技術）を表すクラス。
/// </summary>
public class Magic
{
    public MagicID ID { get; init; } = new MagicID(Guid.NewGuid());
    public string Name { get; set; } = "Unnamed Magic";
    public string Description { get; set; } = "A societal technology or breakthrough.";
    public List<MagicID> Prerequisites { get; set; } = new List<MagicID>(); // 前提となる他のMagic
    public List<GameEffect> Impacts { get; set; } = new List<GameEffect>(); // ゲーム状態への影響
    public List<ResourceAmount> Cost { get; set; } = new List<ResourceAmount>(); // 研究・開発コスト
    public int ResearchTimeTurns { get; set; } = 10; // 研究にかかるターン数
    public int EraUnlocked { get; set; } = 0; // 解放される時代
}
```

### `Job` (生活職業) 規約
```csharp
using System;
using System.Collections.Generic;

public record JobID(Guid Value);

/// <summary>
/// ジョブ（生活職業）を表すクラス。
/// </summary>
public class Job
{
    public JobID ID { get; init; } = new JobID(Guid.NewGuid());
    public string Name { get; set; } = "Unnamed Job";
    public string Description { get; set; } = "A role or profession within society.";
    public List<SkillID> RequiredSkills { get; set; } = new List<SkillID>(); // 必要なスキル
    public List<ResourceAmount> InputResources { get; set; } = new List<ResourceAmount>(); // 消費する資源
    public List<ResourceAmount> OutputResources { get; set; } = new List<ResourceAmount>(); // 生産する資源
    public double ProductivityMultiplier { get; set; } = 1.0; // 生産性乗数
    public MagicID? AssociatedMagic { get; set; } = null; // 関連するMagic（技術）
}
```

### `HistoricalEvent`
```csharp
using System;
using System.Collections.Generic;

public record EntityID(Guid Value);

/// <summary>
/// 歴史上の出来事を表す構造体。
/// </summary>
public class HistoricalEvent
{
    public Guid EventID { get; init; } = Guid.NewGuid();
    public int Turn { get; set; }
    public string Description { get; set; }
    public List<EntityID> AffectedEntities { get; set; } = new List<EntityID>(); // 影響を受けるエンティティ
    public List<Magic> AssociatedMagic { get; set; } = new List<Magic>(); // このイベントで登場・影響するMagic
    public List<Job> AssociatedJobs { get; set; } = new List<Job>(); // このイベントで登場・影響するJob
    public List<GameEffect> EventEffects { get; set; } = new List<GameEffect>(); // イベントが直接もたらす効果
}
```

### `HistoricalBranch`
```csharp
using System;
using System.Collections.Generic;

/// <summary>
/// IF分岐（代替歴史シナリオ）を表すクラス。
/// </summary>
public class HistoricalBranch
{
    public Guid BranchID { get; init; } = Guid.NewGuid();
    public int StartTurn { get; set; }
    public int EndTurn { get; set; }
    public List<HistoricalEvent> Events { get; set; } = new List<HistoricalEvent>();
    public double OutcomeScore { get; set; } = 0.0; // MAGIが評価するスコア
    public bool IsCommitted { get; set; } = false; // 正史としてコミットされたか
    public string BranchLog { get; set; } = ""; // 生成時の詳細ログ
}
```

## 主要クラスとメソッドの実装指示

### 1. `SafeFailResult<T>` (ユーティリティ)
全ての操作結果をラップし、成功/失敗状態とエラーメッセージを提供する構造体。

```csharp
using System;

/// <summary>
/// 安全な失敗構造体。操作の成功/失敗とその結果またはエラーメッセージを保持します。
/// </summary>
public struct SafeFailResult<T>
{
    public bool IsSuccess { get; }
    public T Value { get; }
    public string ErrorMessage { get; }

    private SafeFailResult(bool isSuccess, T value, string errorMessage)
    {
        IsSuccess = isSuccess;
        Value = value;
        ErrorMessage = errorMessage;
    }

    public static SafeFailResult<T> Success(T value) => new SafeFailResult<T>(true, value, default(T));
    public static SafeFailResult<T> Fail(string errorMessage) => new SafeFailResult<T>(false, default(T), errorMessage);
    public static SafeFailResult<T> Fail(string errorMessage, T defaultValue) => new SafeFailResult<T>(false, defaultValue, errorMessage);
}
```

### 2. `IMagicSanitizerEngine` / `MagicSanitizerEngine`
ゲームの整合性を保つためのサニタイザーエンジン。

```csharp
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// 魔法（社会技術）とゲームデータの整合性を検証・修正するインターフェース。
/// </summary>
public interface IMagicSanitizerEngine
{
    SafeFailResult<HistoricalBranch> SanitizeBranch(HistoricalBranch branch);
    SafeFailResult<T> SanitizeData<T>(T data); // 汎用的なデータサニタイズ
}

/// <summary>
/// IMagicSanitizerEngineの実装。生成された歴史分岐の整合性をチェックし、必要に応じて修正します。
/// </summary>
public class MagicSanitizerEngine : IMagicSanitizerEngine
{
    // 既存のゲーム状態やルールセットへの参照が必要になる場合があります（例: 技術ツリー、資源量など）
    // private readonly GameState _gameState; 
    // public MagicSanitizerEngine(GameState gameState) { _gameState = gameState; }

    public SafeFailResult<HistoricalBranch> SanitizeBranch(HistoricalBranch branch)
    {
        if (branch == null)
        {
            return SafeFailResult<HistoricalBranch>.Fail("Cannot sanitize a null branch.");
        }

        // --- サニタイズロジックの実装 ---
        // 1. イベントの順序チェック: ターンが昇順になっているか
        branch.Events = branch.Events.OrderBy(e => e.Turn).ToList();

        // 2. Magicの前提条件チェック:
        //    - イベントで登場するMagicが、そのターンまでに前提Magicが解放されているか。
        //    - 未解放の場合、イベントを修正（Magicを削除、または前提Magicを強制的に追加）するか、分岐全体を無効と判断。
        //    - 現時点では、単純にログに警告を出すに留める。
        foreach (var ev in branch.Events)
        {
            foreach (var magic in ev.AssociatedMagic)
            {
                foreach (var prereqId in magic.Prerequisites)
                {
                    // このMagicの前提となるMagicが、このイベントのターンまでに存在するかをチェック
                    bool prereqExists = branch.Events.Any(e => e.Turn <= ev.Turn && e.AssociatedMagic.Any(m => m.ID == prereqId));
                    if (!prereqExists)
                    {
                        // Console.WriteLine($"Sanitizer Warning: Magic '{magic.Name}' in event at Turn {ev.Turn} has unfulfilled prerequisite '{prereqId}'. Branch ID: {branch.BranchID}");
                        // より厳密な実装では、ここでイベントを修正したり、分岐を破棄したりする
                    }
                }
            }
        }

        // 3. Jobの資源バランスチェック:
        //    - 特定のJobが極端な資源消費/生産を行うイベントがないか。
        //    - 現時点では、単純にログに警告を出すに留める。
        foreach (var ev in branch.Events)
        {
            foreach (var job in ev.AssociatedJobs)
            {
                if (job.InputResources.Any(r => r.Amount < 0) || job.OutputResources.Any(r => r.Amount < 0))
                {
                    // Console.WriteLine($"Sanitizer Warning: Job '{job.Name}' in event at Turn {ev.Turn} has invalid resource amounts. Branch ID: {branch.BranchID}");
                }
            }
        }

        // 4. その他の整合性チェック（例: 人口、環境、リソース枯渇など）
        //    - ここでは詳細なシミュレーション状態への影響を考慮しないが、必要に応じて追加する。

        return SafeFailResult<HistoricalBranch>.Success(branch);
    }

    public SafeFailResult<T> SanitizeData<T>(T data)
    {
        // 汎用的なデータサニタイズロジック（例: 数値範囲の強制、文字列のクリーンアップなど）
        // 現時点では何もしないが、将来的な拡張ポイント
        return SafeFailResult<T>.Success(data);
    }
}
```

### 3. `GameSimulationManager`
シミュレーションのメインループとフェーズ管理。

```csharp
using System;
using System.Collections.Generic;

/// <summary>
/// ゲームシミュレーションの全体を管理するマネージャー。
/// ターン進行、フェーズの起動などを担当します。
/// </summary>
public class GameSimulationManager
{
    private int _currentTurn;
    private CivilizationRecoveryPhase _recoveryPhase;
    private IMagicSanitizerEngine _sanitizer;

    public int CurrentTurn => _currentTurn;

    public GameSimulationManager(IMagicSanitizerEngine sanitizer)
    {
        _sanitizer = sanitizer ?? throw new ArgumentNullException(nameof(sanitizer));
        _recoveryPhase = new CivilizationRecoveryPhase(new HistoricalBranchGenerator(_sanitizer), new MAGI_ConsensusEngine(), _sanitizer);
        _currentTurn = 0; // 初期ターン
        Console.WriteLine("Game Simulation Manager initialized.");
    }

    /// <summary>
    /// 指定されたターン数だけシミュレーションを進めます。
    /// </summary>
    /// <param name="turns">進めるターン数。</param>
    /// <returns>操作の成功/失敗。</returns>
    public SafeFailResult<bool> AdvanceTurn(int turns)
    {
        if (turns <= 0)
        {
            return SafeFailResult<bool>.Fail("Turns to advance must be positive.");
        }

        try
        {
            for (int i = 0; i < turns; i++)
            {
                _currentTurn++;
                Console.WriteLine($"--- Turn {_currentTurn} ---");

                // ターン1001以降で文明復興フェーズが未起動の場合、起動する
                if (_currentTurn >= 1001 && !_recoveryPhase.IsActive)
                {
                    Console.WriteLine($"Turn {_currentTurn}: Activating Civilization Recovery Phase...");
                    var activationResult = ActivateCivilizationRecoveryPhase();
                    if (!activationResult.IsSuccess)
                    {
                        return SafeFailResult<bool>.Fail($"Failed to activate recovery phase: {activationResult.ErrorMessage}");
                    }
                    // 文明復興フェーズは1001-1050年の期間を処理するため、一度起動したらその期間のターン進行はフェーズ内で処理されると仮定
                    // ここではフェーズが完了したら、その後のターンは通常通り進む
                }

                // その他の通常のターン処理ロジック
                // 例: リソース生成、人口変動、イベント発生など
            }
            return SafeFailResult<bool>.Success(true);
        }
        catch (Exception ex)
        {
            return SafeFailResult<bool>.Fail($"Error advancing turn: {ex.Message}");
        }
    }

    /// <summary>
    /// 文明復興フェーズを起動します。
    /// </summary>
    /// <returns>コミットされた正史の分岐、または失敗情報。</returns>
    private SafeFailResult<HistoricalBranch> ActivateCivilizationRecoveryPhase()
    {
        Console.WriteLine("Initiating Civilization Recovery Phase (Turns 1001-1050)...");
        // 文明復興フェーズは1001年から1050年までの期間を対象とする
        var result = _recoveryPhase.ExecutePhase(1001, 1050);

        if (!result.IsSuccess)
        {
            Console.WriteLine($"Civilization Recovery Phase failed: {result.ErrorMessage}");
            return SafeFailResult<HistoricalBranch>.Fail($"Civilization Recovery Phase failed: {result.ErrorMessage}");
        }

        var committedBranch = result.Value;
        Console.WriteLine($"Civilization Recovery Phase completed. True History committed: Branch '{committedBranch.BranchID}'.");

        // コミットされた正史に基づいてゲーム状態を更新するロジック
        // 例: committedBranch.ApplyEventsToCurrentSimulationState();
        ApplyCommittedHistoryToSimulation(committedBranch);

        return SafeFailResult<HistoricalBranch>.Success(committedBranch);
    }

    /// <summary>
    /// コミットされた正史のイベントを現在のシミュレーション状態に適用します。
    /// </summary>
    /// <param name="committedBranch">コミットされた歴史分岐。</param>
    private void ApplyCommittedHistoryToSimulation(HistoricalBranch committedBranch)
    {
        Console.WriteLine($"Applying committed history from Branch '{committedBranch.BranchID}' to simulation state...");
        foreach (var ev in committedBranch.Events)
        {
            Console.WriteLine($"  Applying event at Turn {ev.Turn}: {ev.Description}");
            // ここにイベントがゲーム状態に与える具体的な影響を実装
            // 例: 人口増加、技術解放、資源変動、Jobの出現など
            // _gameState.ApplyEvent(ev);
        }
        // シミュレーションの現在のターンを、コミットされた歴史の最終ターンに合わせる
        _currentTurn = committedBranch.EndTurn;
        Console.WriteLine($"Simulation turn advanced to {committedBranch.EndTurn} based on committed history.");
    }
}
```

### 4. `CivilizationRecoveryPhase`
文明復興フェーズのロジックをカプセル化。

```csharp
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// 文明復興フェーズを管理するクラス。
/// IF分岐の生成、MAGI合議、正史のコミットを行います。
/// </summary>
public class CivilizationRecoveryPhase
{
    private readonly HistoricalBranchGenerator _branchGenerator;
    private readonly MAGI_ConsensusEngine _magiEngine;
    private readonly IMagicSanitizerEngine _sanitizer;
    public bool IsActive { get; private set; }

    public CivilizationRecoveryPhase(HistoricalBranchGenerator branchGenerator, MAGI_ConsensusEngine magiEngine, IMagicSanitizerEngine sanitizer)
    {
        _branchGenerator = branchGenerator ?? throw new ArgumentNullException(nameof(branchGenerator));
        _magiEngine = magiEngine ?? throw new ArgumentNullException(nameof(magiEngine));
        _sanitizer = sanitizer ?? throw new ArgumentNullException(nameof(sanitizer));
        IsActive = false;
    }

    /// <summary>
    /// 文明復興フェーズを実行します。
    /// 1001-1050年のIF分岐を爆撃生成し、MAGIが正史をコミットします。
    /// </summary>
    /// <param name="startTurn">分岐生成の開始ターン。</param>
    /// <param name="endTurn">分岐生成の終了ターン。</param>
    /// <returns>コミットされた正史の分岐、または失敗情報。</returns>
    public SafeFailResult<HistoricalBranch> ExecutePhase(int startTurn, int endTurn)
    {
        if (IsActive)
        {
            return SafeFailResult<HistoricalBranch>.Fail("Civilization Recovery Phase is already active.");
        }
        IsActive = true;

        try
        {
            Console.WriteLine($"[{nameof(CivilizationRecoveryPhase)}] Generating IF branches for turns {startTurn}-{endTurn}...");
            // 「爆撃生成」として、例えば500個の分岐を生成する
            var branchesResult = _branchGenerator.GenerateBranches(startTurn, endTurn, 500); 
            if (!branchesResult.IsSuccess)
            {
                return SafeFailResult<HistoricalBranch>.Fail($"Branch generation failed: {branchesResult.ErrorMessage}");
            }
            var branches = branchesResult.Value;
            Console.WriteLine($"[{nameof(CivilizationRecoveryPhase)}] Generated {branches.Count} IF branches.");

            if (!branches.Any())
            {
                return SafeFailResult<HistoricalBranch>.Fail("No valid branches were generated after sanitization.");
            }

            Console.WriteLine($"[{nameof(CivilizationRecoveryPhase)}] MAGI Consensus Engine evaluating {branches.Count} branches...");
            var commitResult = _magiEngine.CommitTrueHistory(branches);
            if (!commitResult.IsSuccess)
            {
                return SafeFailResult<HistoricalBranch>.Fail($"MAGI consensus failed: {commitResult.ErrorMessage}");
            }
            var committedBranch = commitResult.Value;
            Console.WriteLine($"[{nameof(CivilizationRecoveryPhase)}] MAGI committed Branch '{committedBranch.BranchID}' as True History.");

            return SafeFailResult<HistoricalBranch>.Success(committedBranch);
        }
        catch (Exception ex)
        {
            return SafeFailResult<HistoricalBranch>.Fail($"Error during Civilization Recovery Phase: {ex.Message}");
        }
        finally
        {
            IsActive = false;
        }
    }
}
```

### 5. `HistoricalBranchGenerator`
IF分岐を生成する。

```csharp
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// 歴史のIF分岐（代替シナリオ）を生成するクラス。
/// </summary>
public class HistoricalBranchGenerator
{
    private readonly IMagicSanitizerEngine _sanitizer;
    private Random _random = new Random();

    // 仮のデータストア（実際にはゲームのデータベースや状態から取得）
    private List<Magic> _availableMagic = new List<Magic>();
    private List<Job> _availableJobs = new List<Job>();

    public HistoricalBranchGenerator(IMagicSanitizerEngine sanitizer)
    {
        _sanitizer = sanitizer ?? throw new ArgumentNullException(nameof(sanitizer));
        InitializeDummyData(); // ダミーデータで初期化
    }

    private void InitializeDummyData()
    {
        // ダミーのMagic
        var basicAgriculture = new Magic { Name = "Basic Agriculture", Description = "Allows farming.", ResearchTimeTurns = 5 };
        var basicToolmaking = new Magic { Name = "Basic Toolmaking", Description = "Craft simple tools.", ResearchTimeTurns = 3 };
        var advancedFarming = new Magic { Name = "Advanced Farming", Description = "Improved crop yields.", Prerequisites = new List<MagicID> { basicAgriculture.ID }, ResearchTimeTurns = 10 };
        _availableMagic.AddRange(new[] { basicAgriculture, basicToolmaking, advancedFarming });

        // ダミーのJob
        var farmer = new Job { Name = "Farmer", Description = "Grows food.", AssociatedMagic = basicAgriculture.ID, OutputResources = new List<ResourceAmount> { new("Food", 5) } };
        var builder = new Job { Name = "Builder", Description = "Constructs buildings.", AssociatedMagic = basicToolmaking.ID, InputResources = new List<ResourceAmount> { new("Wood", 2) }, OutputResources = new List<ResourceAmount> { new("Structure", 1) } };
        _availableJobs.AddRange(new[] { farmer, builder });
    }

    /// <summary>
    /// 指定された期間で複数のIF分岐を生成します。
    /// 「爆撃生成」として、指定された`count`の分岐を試行します。
    /// </summary>
    /// <param name="startTurn">分岐の開始ターン。</param>
    /// <param name="endTurn">分岐の終了ターン。</param>
    /// <param name="count">生成を試みる分岐の数。</param>
    /// <returns>生成され、サニタイズされた分岐のリスト、または失敗情報。</returns>
    public SafeFailResult<List<HistoricalBranch>> GenerateBranches(int startTurn, int endTurn, int count)
    {
        var branches = new List<HistoricalBranch>();
        try
        {
            for (int i = 0; i < count; i++)
            {
                var branch = new HistoricalBranch
                {
                    BranchID = Guid.NewGuid(),
                    StartTurn = startTurn,
                    EndTurn = endTurn,
                    Events = GenerateRandomEvents(startTurn, endTurn),
                    BranchLog = $"Generated branch {i + 1} with random events."
                };

                // 生成された分岐をMagicSanitizerEngineでサニタイズ
                var sanitizedResult = _sanitizer.SanitizeBranch(branch);
                if (!sanitizedResult.IsSuccess)
                {
                    // サニタイズに失敗した分岐はスキップし、ログに警告を記録
                    Console.WriteLine($"[{nameof(HistoricalBranchGenerator)}] Warning: Branch {branch.BranchID} failed sanitization: {sanitizedResult.ErrorMessage}. Skipping.");
                    continue;
                }
                branches.Add(sanitizedResult.Value);
            }
            return SafeFailResult<List<HistoricalBranch>>.Success(branches);
        }
        catch (Exception ex)
        {
            return SafeFailResult<List<HistoricalBranch>>.Fail($"Error generating branches: {ex.Message}");
        }
    }

    /// <summary>
    /// 指定された期間内でランダムな歴史イベントのリストを生成します。
    /// </summary>
    /// <param name="startTurn">イベント生成の開始ターン。</param>
    /// <param name="endTurn">イベント生成の終了ターン。</param>
    /// <returns>生成されたイベントのリスト。</returns>
    private List<HistoricalEvent> GenerateRandomEvents(int startTurn, int endTurn)
    {
        var events = new List<HistoricalEvent>();
        int eventCount = _random.Next(5, 20); // 1分岐あたり5〜20個のイベントを生成

        for (int i = 0; i < eventCount; i++)
        {
            var turn = _random.Next(startTurn, endTurn + 1);
            var eventDescription = $"Event at Turn {turn}: ";
            var associatedMagic = new List<Magic>();
            var associatedJobs = new List<Job>();

            // ランダムなイベントタイプを決定
            int eventType = _random.Next(0, 3); // 0: Magic発見, 1: Job創出, 2: その他

            switch (eventType)
            {
                case 0: // Magic発見/発展イベント
                    if (_availableMagic.Any())
                    {
                        var selectedMagic = _availableMagic[_random.Next(0, _availableMagic.Count)];
                        associatedMagic.Add(selectedMagic);
                        eventDescription += $"New 'Magic' discovered: {selectedMagic.Name}.";
                    }
                    else
                    {
                        eventDescription += "A minor technological breakthrough occurred.";
                    }
                    break;
                case 1: // Job創出/変化イベント
                    if (_availableJobs.Any())
                    {
                        var selectedJob = _availableJobs[_random.Next(0, _availableJobs.Count)];
                        associatedJobs.Add(selectedJob);
                        eventDescription += $"New 'Job' emerged: {selectedJob.Name}.";
                    }
                    else
                    {
                        eventDescription += "A new societal role was established.";
                    }
                    break;
                case 2: // その他のランダムイベント
                    string[] genericEvents = {
                        "A natural disaster struck.",
                        "A period of peace and prosperity began.",
                        "A minor conflict erupted between settlements.",
                        "Significant resource deposits were discovered.",
                        "A cultural movement gained traction."
                    };
                    eventDescription += genericEvents[_random.Next(0, genericEvents.Length)];
                    break;
            }

            events.Add(new HistoricalEvent
            {
                Turn = turn,
                Description = eventDescription,
                AssociatedMagic = associatedMagic,
                AssociatedJobs = associatedJobs
            });
        }
        return events;
    }
}
```

### 6. `MAGI_ConsensusEngine`
MAGIによる合議と正史のコミット。

```csharp
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// MAGI自動合議システム。生成された歴史分岐を評価し、正史をコミットします。
/// </summary>
public class MAGI_ConsensusEngine
{
    /// <summary>
    /// 複数の歴史分岐を評価し、最も適切なものを正史としてコミットします。
    /// </summary>
    /// <param name="branches">評価対象の歴史分岐のリスト。</param>
    /// <returns>コミットされた正史の分岐、または失敗情報。</returns>
    public SafeFailResult<HistoricalBranch> CommitTrueHistory(List<HistoricalBranch> branches)
    {
        if (branches == null || !branches.Any())
        {
            return SafeFailResult<HistoricalBranch>.Fail("No branches provided for MAGI consensus.");
        }

        try
        {
            Console.WriteLine($"[{nameof(MAGI_ConsensusEngine)}] Starting MAGI consensus process for {branches.Count} branches.");

            // 各分岐を評価
            foreach (var branch in branches)
            {
                branch.OutcomeScore = EvaluateBranch(branch);
                // Console.WriteLine($"[{nameof(MAGI_ConsensusEngine)}] Branch {branch.BranchID.ToString().Substring(0, 8)} evaluated with score: {branch.OutcomeScore:F2}");
            }

            // 最もスコアの高い分岐を特定
            var committedBranch = branches.OrderByDescending(b => b.OutcomeScore).FirstOrDefault();

            if (committedBranch == null)
            {
                return SafeFailResult<HistoricalBranch>.Fail("MAGI failed to select a branch (no valid branches after evaluation).");
            }

            committedBranch.IsCommitted = true;
            Console.WriteLine($"[{nameof(MAGI_ConsensusEngine)}] MAGI has committed Branch '{committedBranch.BranchID.ToString().Substring(0, 8)}' (Score: {committedBranch.OutcomeScore:F2}) as True History.");
            return SafeFailResult<HistoricalBranch>.Success(committedBranch);
        }
        catch (Exception ex)
        {
            return SafeFailResult<HistoricalBranch>.Fail($"Error during MAGI consensus: {ex.Message}");
        }
    }

    /// <summary>
    /// 単一の歴史分岐を評価し、その「OutcomeScore」を計算します。
    /// このロジックはMAGIの複雑な合議プロセスをシミュレートします。
    /// </summary>
    /// <param name="branch">評価対象の歴史分岐。</param>
    /// <returns>計算されたスコア。</returns>
    private double EvaluateBranch(HistoricalBranch branch)
    {
        // --- MAGIの評価ロジック（非常に複雑な計算をシミュレート） ---
        // 以下の要素を考慮し、重み付けしてスコアを算出します。
        // 実際のゲームでは、現在のシミュレーション状態、ゲーム目標、MAGIの個性なども影響します。

        double score = 0;

        // 1. 技術発展度 (Magicの数、種類、前提条件の充足度)
        //    - より多くのMagicが発見され、特に前提条件の多い高度なMagicが解放されているほど高スコア
        score += branch.Events.SelectMany(e => e.AssociatedMagic).Distinct().Count() * 5; // 異なるMagicの数
        score += branch.Events.SelectMany(e => e.AssociatedMagic).Sum(m => m.Prerequisites.Count * 2); // 前提条件が多いMagicを評価

        // 2. 社会の安定性 (紛争イベントの少なさ、平和イベントの多さ)
        //    - "conflict" や "disaster" などのキーワードを含むイベントは減点
        //    - "peace" や "prosperity" などのキーワードを含むイベントは加点
        score += branch.Events.Count(e => e.Description.Contains("peace") || e.Description.Contains("prosperity")) * 10;
        score -= branch.Events.Count(e => e.Description.Contains("conflict") || e.Description.Contains("disaster")) * 15;

        // 3. 資源効率と生産性 (Jobの多様性、Input/Outputバランス)
        //    - より多様なJobが存在し、InputよりもOutputが多いJobが多いほど高スコア
        score += branch.Events.SelectMany(e => e.AssociatedJobs).Distinct().Count() * 3; // 異なるJobの数
        score += branch.Events.SelectMany(e => e.AssociatedJobs).Sum(j => j.OutputResources.Sum(r => r.Amount) - j.InputResources.Sum(r => r.Amount)); // 資源純生産量

        // 4. 人口増加ポテンシャル (特定のMagicやJobが人口に与える影響)
        //    - 例: "Agriculture"系のMagicや"Farmer"系のJobは人口増加に寄与すると仮定
        score += branch.Events.SelectMany(e => e.AssociatedMagic).Count(m => m.Name.Contains("Agriculture")) * 7;
        score += branch.Events.SelectMany(e => e.AssociatedJobs).Count(j => j.Name.Contains("Farmer")) * 6;

        // 5. イベントの総数 (ある程度の活動量があることを評価)
        score += branch.Events.Count * 0.5;

        // 6. ランダム性 (MAGIの「直感」や予測不能な要素)
        score += new Random().NextDouble() * 20 - 10; // -10から+10の範囲でランダムな要素を追加

        // スコアが負にならないように調整
        return Math.Max(0, score);
    }
}
```

## 使用例 / テストシナリオ
`Program.cs` などから以下のように呼び出すことで、シミュレーションを開始し、文明復興フェーズを起動できます。

```csharp
using System;
using System.Collections.Generic;

public class Program
{
    public static void Main(string[] args)
    {
        Console.WriteLine("Starting Millennium History Simulator...");

        // MagicSanitizerEngineのインスタンスを作成
        // 実際にはGameStateなどの依存関係を注入する可能性があります
        IMagicSanitizerEngine sanitizer = new MagicSanitizerEngine();

        // GameSimulationManagerのインスタンスを作成
        GameSimulationManager gameManager = new GameSimulationManager(sanitizer);

        // シミュレーションを1001ターンまで進める
        Console.WriteLine("\nAdvancing simulation to Turn 1001 to trigger Civilization Recovery Phase...");
        var advanceResult = gameManager.AdvanceTurn(1001); // 0ターンから開始し、1001ターンまで進める

        if (!advanceResult.IsSuccess)
        {
            Console.WriteLine($"Simulation failed to advance: {advanceResult.ErrorMessage}");
            return;
        }

        Console.WriteLine($"\nSimulation completed up to Turn {gameManager.CurrentTurn}.");
        Console.WriteLine("Press any key to exit.");
        Console.ReadKey();
    }
}
```

---

このプロンプトは、要求された機能の骨格を形成し、Safe-Fail構造、MagicSanitizerEngine、Job/Magicの定義規約を厳格に守っています。各クラス内のコメントは、さらなる実装や拡張のためのガイドラインとして機能します。特に`HistoricalBranchGenerator`の`GenerateRandomEvents`と`MAGI_ConsensusEngine`の`EvaluateBranch`は、ゲームの深みと複雑さを決定する重要なロジックであり、詳細なゲームデザインに基づいて拡張してください。
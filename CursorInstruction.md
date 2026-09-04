はい、承知いたしました。
「フロム風戦闘×ブラインド熱科学クラフト×千年史自律シミュレーター」における、ターン1001以降の文明復興フェーズ起動とMAGI自動合議検証のためのC#実装指示を、CursorのCtrl+Lにそのまま読み込ませられるMarkdown形式で出力します。

---

## 精密な実装指示プロンプト：文明復興フェーズとMAGI自動合議の検証

### 目的
ターン1001以降に「文明復興フェーズ」を起動し、そのフェーズ内で「MAGI自動合議システム」が社会技術（Magic）を提案・検証・選択するプロセスをシミュレートし、その挙動を検証します。Safe-Fail構造、MagicSanitizerEngineの厳格な利用、およびJob/Magicの定義規約を遵守します。

### 主要コンポーネントの設計

以下のクラス、インターフェース、およびEnumを定義・実装します。

#### 1. `GamePhase.cs` (Enum)
ゲームの進行フェーズを定義します。

```csharp
// GamePhase.cs
public enum GamePhase
{
    /// <summary>
    /// 文明崩壊前のフェーズ。
    /// </summary>
    PreCollapse,

    /// <summary>
    /// 文明崩壊後の荒廃フェーズ。
    /// </summary>
    PostCollapse,

    /// <summary>
    /// 文明復興を目指すフェーズ。ターン1001以降に起動。
    /// </summary>
    CivilizationRecovery,

    /// <summary>
    /// ゲーム終了フェーズ（任意）。
    /// </summary>
    EndGame
}
```

#### 2. `ResourceType.cs` (Enum)
ゲーム内で使用されるリソースの種類を定義します。

```csharp
// ResourceType.cs
public enum ResourceType
{
    Food,
    Materials,
    Energy,
    Knowledge,
    Influence
}
```

#### 3. `SocialTechnology.cs` (Class - Magicの定義規約遵守)
**規約:** `Magic` = `SocialTechnology` (社会技術)

MAGIが提案する社会技術のデータ構造を定義します。

```csharp
// SocialTechnology.cs
using System.Collections.Generic;

/// <summary>
/// ゲーム内の「魔法」に相当する社会技術のデータ構造。
/// </summary>
public class SocialTechnology
{
    /// <summary>社会技術の一意な識別子。</summary>
    public string ID { get; set; } = string.Empty;

    /// <summary>社会技術の名称。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>社会技術の説明。</summary>
    public string Description { get; set; = string.Empty;

    /// <summary>この社会技術を開発・適用するために必要なリソースコスト。</summary>
    public Dictionary<ResourceType, int> ResourceCost { get; set; } = new Dictionary<ResourceType, int>();

    /// <summary>MagicSanitizerEngineによって検証・調整済みであるかを示すフラグ。</summary>
    public bool IsSanitized { get; set; } = false;

    // 必要に応じて、この社会技術がもたらす効果や前提条件などを追加
    // public List<Effect> Effects { get; set; }
    // public List<string> Prerequisites { get; set; }

    /// <summary>
    /// SocialTechnologyオブジェクトの文字列表現を返します。
    /// </summary>
    public override string ToString()
    {
        return $"[SocialTechnology] ID: {ID}, Name: '{Name}', Sanitized: {IsSanitized}";
    }
}
```

#### 4. `LifeOccupation.cs` (Class - Jobの定義規約遵守)
**規約:** `Job` = `LifeOccupation` (生活職業)

社会技術によってアンロックされたり、文明復興フェーズで必要となる生活職業のデータ構造を定義します。

```csharp
// LifeOccupation.cs
using System.Collections.Generic;

/// <summary>
/// ゲーム内の「ジョブ」に相当する生活職業のデータ構造。
/// </summary>
public class LifeOccupation
{
    /// <summary>生活職業の一意な識別子。</summary>
    public string ID { get; set; } = string.Empty;

    /// <summary>生活職業の名称。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>生活職業の説明。</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>この職業に就くために必要なスキルリスト。</summary>
    public List<string> RequiredSkills { get; set; } = new List<string>();

    /// <summary>この職業がもたらす生産性ボーナス（リソースタイプと倍率）。</summary>
    public Dictionary<ResourceType, float> ProductivityBonus { get; set; } = new Dictionary<ResourceType, float>();

    // 必要に応じて、その他の特性（幸福度ボーナス、特定の施設への依存など）を追加
    // public int HappinessBonus { get; set; }

    /// <summary>
    /// LifeOccupationオブジェクトの文字列表現を返します。
    /// </summary>
    public override string ToString()
    {
        return $"[LifeOccupation] ID: {ID}, Name: '{Name}'";
    }
}
```

#### 5. `IMagicSanitizerEngine.cs` (Interface)
`MagicSanitizerEngine`のインターフェースを定義します。これは既存のコンポーネントまたは外部システムとの連携を想定しています。

```csharp
// IMagicSanitizerEngine.cs
/// <summary>
/// 提案された社会技術（Magic）の妥当性を検証し、必要に応じて調整するエンジン。
/// </summary>
public interface IMagicSanitizerEngine
{
    /// <summary>
    /// 指定された社会技術を検証し、ゲームのルールやバランスに適合するように調整します。
    /// </summary>
    /// <param name="magic">検証・調整対象の社会技術。</param>
    /// <returns>社会技術が有効で、ゲームに適用可能であればtrue。そうでなければfalse。</returns>
    bool Sanitize(SocialTechnology magic);
}
```

#### 6. `ConcreteMagicSanitizerEngine.cs` (Class - Mock Implementation)
`IMagicSanitizerEngine`のモック実装を提供します。Safe-Fail構造を厳格に適用します。

```csharp
// ConcreteMagicSanitizerEngine.cs
using System;
using System.Linq;

/// <summary>
/// IMagicSanitizerEngineの具体的なモック実装。
/// 社会技術の妥当性チェックとランダムな失敗をシミュレートします。
/// </summary>
public class ConcreteMagicSanitizerEngine : IMagicSanitizerEngine
{
    private readonly Random _random = new Random();

    /// <summary>
    /// 社会技術を検証し、ゲームのルールやバランスに適合するように調整します。
    /// Safe-Fail: nullチェック、無効なデータチェック、ランダムな失敗を組み込みます。
    /// </summary>
    /// <param name="magic">検証・調整対象の社会技術。</param>
    /// <returns>社会技術が有効で、ゲームに適用可能であればtrue。そうでなければfalse。</returns>
    public bool Sanitize(SocialTechnology magic)
    {
        // Safe-Fail: 入力検証
        if (magic == null)
        {
            Logger.LogError("MagicSanitizerEngine: Received a null SocialTechnology for sanitization. Aborting.");
            return false;
        }
        if (string.IsNullOrWhiteSpace(magic.ID) || string.IsNullOrWhiteSpace(magic.Name))
        {
            Logger.LogError($"MagicSanitizerEngine: Received an invalid SocialTechnology (ID: '{magic.ID}', Name: '{magic.Name}'). ID or Name cannot be empty. Aborting.");
            return false;
        }

        Logger.Log($"MagicSanitizerEngine: Initiating sanitization for '{magic.Name}' (ID: {magic.ID})...");

        // Safe-Fail: リソースコストの妥当性チェック (例: 負のコストは許容しない)
        if (magic.ResourceCost.Any(cost => cost.Value < 0))
        {
            Logger.LogWarning($"Sanitizer: SocialTechnology '{magic.Name}' has negative resource costs. This is invalid. Rejecting.");
            magic.IsSanitized = false;
            return false;
        }

        // Safe-Fail: 複雑な検証ルールをシミュレート (例: 技術ツリーの前提条件、バランス調整)
        // 15%の確率でランダムに検証失敗をシミュレート
        if (_random.Next(0, 100) < 15)
        {
            Logger.LogWarning($"Sanitizer: SocialTechnology '{magic.Name}' failed a complex validation check (simulated random failure). Rejecting.");
            magic.IsSanitized = false;
            return false;
        }

        // 調整ロジック（例: コストの正規化、隠し効果の追加など、今回はモック）
        // magic.ResourceCost[ResourceType.Energy] = Math.Max(10, magic.ResourceCost.GetValueOrDefault(ResourceType.Energy));

        magic.IsSanitized = true;
        Logger.LogSuccess($"MagicSanitizerEngine: SocialTechnology '{magic.Name}' (ID: {magic.ID}) successfully sanitized.");
        return true;
    }
}
```

#### 7. `MAGIConsensusSystem.cs` (Class)
MAGI自動合議システムを実装します。社会技術の提案、`MagicSanitizerEngine`による検証、および最適な社会技術の選択を行います。

```csharp
// MAGIConsensusSystem.cs
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// MAGI自動合議システム。複数のエージェントが社会技術を提案し、合議によって最適なものを選択します。
/// </summary>
public class MAGIConsensusSystem
{
    private readonly IMagicSanitizerEngine _sanitizerEngine;
    private readonly Random _random;

    /// <summary>
    /// MAGIConsensusSystemの新しいインスタンスを初期化します。
    /// </summary>
    /// <param name="sanitizerEngine">社会技術の検証に使用するMagicSanitizerEngine。</param>
    public MAGIConsensusSystem(IMagicSanitizerEngine sanitizerEngine)
    {
        // Safe-Fail: 依存性注入の検証
        _sanitizerEngine = sanitizerEngine ?? throw new ArgumentNullException(nameof(sanitizerEngine), "MAGI Consensus System requires a non-null MagicSanitizerEngine.");
        _random = new Random();
        Logger.Log("MAGI Consensus System initialized.");
    }

    /// <summary>
    /// MAGIエージェントが複数の社会技術を提案します。
    /// </summary>
    /// <param name="currentTurn">現在のターン数。</param>
    /// <returns>提案された社会技術のリスト。</returns>
    public List<SocialTechnology> ProposeSocialTechnologies(int currentTurn)
    {
        var proposals = new List<SocialTechnology>();
        int numProposals = _random.Next(2, 5); // 2～4個の社会技術を提案

        Logger.Log($"MAGI System: Generating {numProposals} proposals for Social Technologies...");
        for (int i = 0; i < numProposals; i++)
        {
            var newMagic = GenerateRandomSocialTechnology(currentTurn, i);
            proposals.Add(newMagic);
            Logger.Log($"  - MAGI Agent {i + 1} proposes: '{newMagic.Name}' (ID: {newMagic.ID})");
        }
        return proposals;
    }

    /// <summary>
    /// 提案された社会技術を評価し、MagicSanitizerEngineで検証した後、最適なものを選択します。
    /// Safe-Fail: 入力検証、検証失敗時のスキップを組み込みます。
    /// </summary>
    /// <param name="proposals">評価対象の社会技術リスト。</param>
    /// <returns>合議によって選択された最適な社会技術。選択されなかった場合はnull。</returns>
    public SocialTechnology EvaluateAndSelectBestMagic(List<SocialTechnology> proposals)
    {
        // Safe-Fail: 入力検証
        if (proposals == null || !proposals.Any())
        {
            Logger.LogWarning("MAGI Consensus System: No proposals provided for evaluation. Returning null.");
            return null;
        }

        SocialTechnology bestMagic = null;
        float bestScore = -1.0f;
        List<SocialTechnology> validProposals = new List<SocialTechnology>();

        Logger.Log("MAGI System: Evaluating proposals and validating with MagicSanitizerEngine...");

        foreach (var magic in proposals)
        {
            // Step 1: MagicSanitizerEngineによる検証
            // Sanitizerはmagicオブジェクトを直接変更し、IsValidフラグを設定する可能性がある
            bool isValid = _sanitizerEngine.Sanitize(magic);

            if (!isValid)
            {
                Logger.LogWarning($"  - Proposal '{magic.Name}' (ID: {magic.ID}) was rejected by MagicSanitizerEngine. Skipping.");
                continue; // Safe-Fail: 無効な社会技術は評価対象から除外
            }

            validProposals.Add(magic);

            // Step 2: MAGI内部基準による評価 (モック実装)
            float score = CalculateMagicScore(magic);
            Logger.Log($"  - Proposal '{magic.Name}' (ID: {magic.ID}) score: {score:F2}");

            if (score > bestScore)
            {
                bestScore = score;
                bestMagic = magic;
            }
        }

        if (!validProposals.Any())
        {
            Logger.LogWarning("MAGI Consensus System: All proposed Social Technologies were rejected by the sanitizer. No consensus reached.");
            return null;
        }

        if (bestMagic == null)
        {
            // This case should ideally not happen if validProposals is not empty,
            // but as a Safe-Fail fallback for unexpected scoring issues.
            Logger.LogError("MAGI Consensus System: No best magic could be selected despite valid proposals. This indicates a scoring logic error or an unexpected state.");
            return validProposals.First(); // Fallback to the first valid one
        }

        Logger.LogSuccess($"MAGI System: Consensus reached! Selected Social Technology: '{bestMagic.Name}' (ID: {bestMagic.ID}) with score {bestScore:F2}.");
        return bestMagic;
    }

    /// <summary>
    /// ランダムな社会技術を生成するモックメソッド。
    /// </summary>
    private SocialTechnology GenerateRandomSocialTechnology(int currentTurn, int index)
    {
        string[] magicNames = { "Basic Agriculture", "Simple Metallurgy", "Early Writing", "Communal Housing", "Basic Medicine", "Primitive Tools", "Water Purification" };
        string name = magicNames[_random.Next(magicNames.Length)];
        string id = name.Replace(" ", "_").ToUpper() + "_TECH";

        var magic = new SocialTechnology
        {
            ID = id,
            Name = name,
            Description = $"A foundational social technology proposed by MAGI at turn {currentTurn}. (Variant {index + 1})",
            ResourceCost = new Dictionary<ResourceType, int>
            {
                { ResourceType.Food, _random.Next(50, 200) },
                { ResourceType.Materials, _random.Next(20, 100) },
                { ResourceType.Knowledge, _random.Next(10, 50) }
            },
            IsSanitized = false // Sanitizerによって設定される
        };

        // ランダムに特定のコストを追加する可能性
        if (_random.NextDouble() < 0.3) magic.ResourceCost[ResourceType.Energy] = _random.Next(10, 50);
        if (_random.NextDouble() < 0.2) magic.ResourceCost[ResourceType.Influence] = _random.Next(5, 25);

        return magic;
    }

    /// <summary>
    /// 社会技術の評価スコアを計算するモックメソッド。
    /// </summary>
    private float CalculateMagicScore(SocialTechnology magic)
    {
        // モック評価ロジック: コストが高いほど影響力があるが、複雑さも増す。
        // 簡単のため、コストの合計にランダム性を加える。
        float score = 0;
        foreach (var cost in magic.ResourceCost)
        {
            score += cost.Value * GetResourceWeight(cost.Key);
        }
        // ランダム性を加えて、合議の不確実性をシミュレート
        score += (float)_random.NextDouble() * 20; // 0-20点のランダムボーナス
        return score;
    }

    /// <summary>
    /// リソースタイプに応じた重み付けを返すモックメソッド。
    /// </summary>
    private float GetResourceWeight(ResourceType type)
    {
        switch (type)
        {
            case ResourceType.Food: return 0.1f;
            case ResourceType.Materials: return 0.08f;
            case ResourceType.Knowledge: return 0.15f; // 知識は重要
            case ResourceType.Energy: return 0.12f;
            case ResourceType.Influence: return 0.05f;
            default: return 0.01f; // Safe-Fail: 未知のリソースタイプ
        }
    }
}
```

#### 8. `GameManager.cs` (Class)
ゲームのメインループ、フェーズ管理、およびMAGI自動合議の起動を制御します。

```csharp
// GameManager.cs
using System;
using System.Collections.Generic;

/// <summary>
/// ゲームの全体的な進行を管理するクラス。ターン進行、フェーズ移行、主要システムの連携を制御します。
/// </summary>
public class GameManager
{
    /// <summary>現在のターン数。</summary>
    public int CurrentTurn { get; private set; }

    /// <summary>現在のゲームフェーズ。</summary>
    public GamePhase CurrentPhase { get; private set; }

    private readonly MAGIConsensusSystem _magiSystem;
    private readonly IMagicSanitizerEngine _sanitizerEngine; // 依存性注入されたSanitizerEngine

    /// <summary>
    /// GameManagerの新しいインスタンスを初期化します。
    /// </summary>
    /// <param name="sanitizerEngine">MagicSanitizerEngineのインスタンス。</param>
    public GameManager(IMagicSanitizerEngine sanitizerEngine)
    {
        // Safe-Fail: 依存性注入の検証
        _sanitizerEngine = sanitizerEngine ?? throw new ArgumentNullException(nameof(sanitizerEngine), "GameManager requires a non-null MagicSanitizerEngine.");
        _magiSystem = new MAGIConsensusSystem(_sanitizerEngine);
        CurrentTurn = 0;
        CurrentPhase = GamePhase.PreCollapse; // 初期フェーズ設定
        Logger.Log("GameManager initialized. Starting in PreCollapse phase.");
    }

    /// <summary>
    /// ゲームのターンを1つ進めます。
    /// Safe-Fail: ターン制限チェック、フェーズ移行ロジックを組み込みます。
    /// </summary>
    public void AdvanceTurn()
    {
        CurrentTurn++;
        Logger.Log($"--- Turn {CurrentTurn} --- Current Phase: {CurrentPhase}");

        // Safe-Fail: 異常なターン数の上限チェック (無限ループ防止)
        if (CurrentTurn > 5000) // 例: 5000ターンで強制終了
        {
            Logger.LogError("Simulation reached maximum turn limit (5000). Ending simulation.");
            CurrentPhase = GamePhase.EndGame; // ゲーム終了フェーズへ移行
            return;
        }

        // フェーズ移行ロジック: ターン1001以降で文明復興フェーズを起動
        if (CurrentTurn >= 1001 && CurrentPhase < GamePhase.CivilizationRecovery)
        {
            StartCivilizationRecoveryPhase();
        }

        // 現在のフェーズに応じたアクションを実行
        switch (CurrentPhase)
        {
            case GamePhase.PreCollapse:
                // 文明崩壊前のロジック (例: 資源生産、技術開発など)
                // Logger.Log("PreCollapse phase actions...");
                if (CurrentTurn == 500) // 例: ターン500で崩壊フェーズへ
                {
                    Logger.LogWarning("Turn 500: Civilization is collapsing! Transitioning to PostCollapse phase.");
                    CurrentPhase = GamePhase.PostCollapse;
                }
                break;
            case GamePhase.PostCollapse:
                // 文明崩壊後のロジック (例: 生存、資源枯渇、小規模集落の形成など)
                // Logger.Log("PostCollapse phase actions...");
                break;
            case GamePhase.CivilizationRecovery:
                // 文明復興フェーズの主要アクション: MAGI自動合議の検証
                SimulateMAGIConsensus();
                break;
            case GamePhase.EndGame:
                Logger.Log("Game is in EndGame phase. No further actions.");
                break;
            default:
                // Safe-Fail: 未定義のフェーズに対する処理
                Logger.LogWarning($"GameManager: No specific actions defined for current phase: {CurrentPhase}.");
                break;
        }
    }

    /// <summary>
    /// 文明復興フェーズを開始します。
    /// </summary>
    private void StartCivilizationRecoveryPhase()
    {
        CurrentPhase = GamePhase.CivilizationRecovery;
        Logger.LogSuccess($"Turn {CurrentTurn}: Initiating Civilization Recovery Phase! The long journey to rebuild begins.");
        // このフェーズ固有の初期化処理などをここに追加
    }

    /// <summary>
    /// MAGI自動合議プロセスをシミュレートし、その結果を検証します。
    /// Safe-Fail: MAGIシステムの準備状況チェック、例外処理を組み込みます。
    /// </summary>
    private void SimulateMAGIConsensus()
    {
        Logger.Log("GameManager: Initiating MAGI automatic consensus process for Civilization Recovery.");
        try
        {
            // Safe-Fail: MAGIシステムが正しく初期化されているか確認
            if (_magiSystem == null)
            {
                Logger.LogError("GameManager: MAGI Consensus System is not initialized. Cannot simulate consensus.");
                return;
            }

            // Step 1: MAGIが社会技術（Magic）を提案
            var proposedMagics = _magiSystem.ProposeSocialTechnologies(CurrentTurn);
            if (proposedMagics == null || proposedMagics.Count == 0)
            {
                Logger.LogWarning("GameManager: MAGI System proposed no Social Technologies. Skipping consensus for this turn.");
                return;
            }

            // Step 2: MAGIが提案を評価し、MagicSanitizerEngineで検証後、最適なものを選択
            var selectedMagic = _magiSystem.EvaluateAndSelectBestMagic(proposedMagics);

            if (selectedMagic != null)
            {
                Logger.LogSuccess($"GameManager: MAGI consensus successfully selected Social Technology '{selectedMagic.Name}' (ID: {selectedMagic.ID}).");
                // 選択された社会技術をゲーム状態に適用 (モック実装)
                ApplySocialTechnology(selectedMagic);
            }
            else
            {
                Logger.LogWarning("GameManager: MAGI System failed to reach a conclusive consensus or no valid Social Technology was selected this turn.");
            }
        }
        catch (Exception ex)
        {
            // Safe-Fail: 予期せぬエラーを捕捉し、ログに記録
            Logger.LogError($"GameManager: An unexpected error occurred during MAGI consensus simulation: {ex.Message}\nStackTrace: {ex.StackTrace}");
            // エラー発生時のフォールバック処理 (例: デフォルトの社会技術を適用、ターンをスキップなど)
        }
    }

    /// <summary>
    /// 選択された社会技術をゲーム状態に適用するモックメソッド。
    /// </summary>
    /// <param name="magic">適用する社会技術。</param>
    private void ApplySocialTechnology(SocialTechnology magic)
    {
        // Safe-Fail: 入力検証
        if (magic == null)
        {
            Logger.LogError("GameManager: Attempted to apply a null SocialTechnology. Aborting.");
            return;
        }

        Logger.Log($"GameManager: Applying Social Technology '{magic.Name}' (ID: {magic.ID}). Is Sanitized: {magic.IsSanitized}");

        // 例: 特定の社会技術が特定の生活職業（Job）をアンロックする
        if (magic.ID == "BASIC_AGRICULTURE_TECH")
        {
            Logger.Log("GameManager: Unlocking new Life Occupation: Farmer.");
            // 実際のゲームでは、ここでゲーム状態を更新し、新しいJobをアンロックする
            // GameState.UnlockLifeOccupation(new LifeOccupation { ID = "FARMER", Name = "Farmer", ... });
        }
        else if (magic.ID == "SIMPLE_METALLURGY_TECH")
        {
            Logger.Log("GameManager: Unlocking new Life Occupation: Miner.");
            // GameState.UnlockLifeOccupation(new LifeOccupation { ID = "MINER", Name = "Miner", ... });
        }
        // 他の社会技術に応じたゲーム状態の変更ロジック
    }
}
```

#### 9. `Logger.cs` (Utility Class)
シンプルなロギングユーティリティ。

```csharp
// Logger.cs
using System;

/// <summary>
/// シミュレーションのログ出力を行うユーティリティクラス。
/// </summary>
public static class Logger
{
    public static void Log(string message) => Console.WriteLine($"[INFO] {DateTime.Now:HH:mm:ss} {message}");
    public static void LogWarning(string message) => Console.WriteLine($"[WARNING] {DateTime.Now:HH:mm:ss} {message}");
    public static void LogError(string message) => Console.Error.WriteLine($"[ERROR] {DateTime.Now:HH:mm:ss} {message}");
    public static void LogSuccess(string message) => Console.WriteLine($"[SUCCESS] {DateTime.Now:HH:mm:ss} {message}");
}
```

#### 10. `Program.cs` (Entry Point for Testing)
シミュレーションを実行するためのエントリポイント。

```csharp
// Program.cs
using System;

/// <summary>
/// シミュレーションのエントリポイント。
/// </summary>
public class Program
{
    public static void Main(string[] args)
    {
        Logger.Log("--- Starting Game Simulation ---");

        // MagicSanitizerEngineのインスタンスを生成
        IMagicSanitizerEngine sanitizer = new ConcreteMagicSanitizerEngine();

        // GameManagerを初期化（依存性注入）
        GameManager gameManager = new GameManager(sanitizer);

        // ターンをシミュレート
        // ターン1001以降で文明復興フェーズが起動し、MAGI合議が検証されることを確認
        for (int i = 0; i < 1050; i++) // 1050ターンまでシミュレート
        {
            gameManager.AdvanceTurn();
            // 各ターンの進行を視覚的に確認したい場合は、ここで短い遅延を入れる
            // System.Threading.Thread.Sleep(50); 
        }

        Logger.Log("--- Game Simulation Finished ---");
        Console.ReadKey(); // コンソールがすぐに閉じないように待機
    }
}
```

### 実装のポイントとSafe-Fail構造の適用

*   **フェーズ移行:** `GameManager.AdvanceTurn()`メソッド内で`CurrentTurn >= 1001`の条件が満たされた際に`StartCivilizationRecoveryPhase()`を呼び出し、`CurrentPhase`を`CivilizationRecovery`に設定します。
*   **MAGI自動合議の起動:** `CivilizationRecovery`フェーズに入ると、`GameManager.SimulateMAGIConsensus()`が毎ターン呼び出され、MAGIの合議プロセスが実行されます。
*   **`MagicSanitizerEngine`の厳格な利用:**
    *   `MAGIConsensusSystem`は、提案されたすべての`SocialTechnology`を`IMagicSanitizerEngine.Sanitize()`メソッドを通じて検証します。
    *   `ConcreteMagicSanitizerEngine`は、`null`入力、無効なID/名称、負のリソースコストなど、様々な異常ケースをチェックし、`false`を返すことで安全に失敗します。また、ランダムな確率で検証失敗をシミュレートし、システムの堅牢性をテストします。
*   **Safe-Fail構造:**
    *   各クラスのコンストラクタで依存オブジェクトの`null`チェックを行い、`ArgumentNullException`をスローします。
    *   メソッドの引数に対して`null`や空文字列、範囲外の値などの基本的な入力検証を行います。
    *   処理の途中で予期せぬ状態（例: MAGIが提案を生成しない、Sanitizerが全てを拒否する）が発生した場合、`Logger.LogWarning`や`Logger.LogError`で通知し、処理をスキップするか、安全なデフォルト値にフォールバックします。
    *   複雑な処理ブロック（例: `SimulateMAGIConsensus`）では`try-catch`ブロックを使用し、予期せぬ例外を捕捉してログに記録します。
    *   `GameManager.AdvanceTurn()`には、無限ループを防ぐためのターン上限チェックを設けています。
*   **規約遵守:** `SocialTechnology`クラスは`Magic`、`LifeOccupation`クラスは`Job`として明確に定義され、コード内のコメントや変数名でもこの規約が反映されています。

この指示に従ってC#コードを実装することで、要件を満たすシステムが構築されます。
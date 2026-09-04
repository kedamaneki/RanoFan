はい、承知いたしました。
リードディレクター兼C#設計者として、ご指示の「ターン1050から1250までの200年間（第22〜25世代）を連続自律進行させ、復興・新文明の正史をコミットし続ける」ためのC#実装指示を、Cursor(IDE)のCtrl+Lへそのまま読み込ませてコード化できる精密なMarkdown形式で出力します。

Safe-Fail構造、MagicSanitizerEngine、Job/Magicの定義規約（魔法=社会技術, ジョブ=生活職業）を厳格に守ります。

---

```markdown
```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading; // シミュレーションの進行を視覚的に確認するための遅延用

namespace MillenniumSimulator
{
    /// <summary>
    /// シミュレーションの現在の状態を保持するクラス。
    /// 文明の力、リソース、アクティブな社会技術、ジョブ、歴史イベントなどを管理します。
    /// </summary>
    public class SimulationState
    {
        public int CurrentTurn { get; private set; }
        public int CurrentGeneration { get; private set; }
        public long CivilizationPower { get; private set; }
        public Dictionary<string, int> Resources { get; private set; }
        public List<IMagic> ActiveSocialTechnologies { get; private set; }
        public List<IJob> ActiveJobs { get; private set; }
        public List<string> HistoricalEvents { get; private set; }

        /// <summary>
        /// SimulationStateの新しいインスタンスを初期化します。
        /// </summary>
        /// <param name="startTurn">シミュレーションの開始ターン。</param>
        public SimulationState(int startTurn)
        {
            CurrentTurn = startTurn;
            UpdateGeneration(); // 初期世代を設定
            CivilizationPower = 1000; // 文明の初期力
            Resources = new Dictionary<string, int>
            {
                { "Food", 1000 },       // 食料: 生存と人口維持に必須
                { "Materials", 500 },   // 材料: クラフトや建設に必要
                { "Knowledge", 100 }    // 知識: 技術開発や魔法（社会技術）の発見に必要
            };
            ActiveSocialTechnologies = new List<IMagic>();
            ActiveJobs = new List<IJob>();
            HistoricalEvents = new List<string>();

            // 初期ジョブの追加 (例: 農民)
            ActiveJobs.Add(new Farmer());
            AddHistoricalEvent("文明が再興し、初期の生活職業が確立された。");
        }

        /// <summary>
        /// ターンを進め、それに伴い世代を更新します。
        /// </summary>
        public void AdvanceTurn()
        {
            CurrentTurn++;
            UpdateGeneration();
            // ターンごとのリソース消費・生産の基本ロジック
            Resources["Food"] -= 50; // 基本的な食料消費
            if (Resources["Food"] < 0) Resources["Food"] = 0; // 負の値にならないように
            Resources["Materials"] += 5; // 自然な材料の増加
            Resources["Knowledge"] += 2; // 自然な知識の増加
        }

        /// <summary>
        /// 現在のターンに基づいて世代を更新します。
        /// 1世代を50ターンと仮定し、ターン1050が第22世代の開始とします。
        /// </summary>
        private void UpdateGeneration()
        {
            // ターン1050が第22世代の開始なので、基準を調整
            CurrentGeneration = 22 + (CurrentTurn - 1050) / 50;
            if (CurrentTurn < 1050) CurrentGeneration = 21; // 1050ターン以前は第21世代以前として扱う
        }

        /// <summary>
        /// 文明の力を増減させます。
        /// </summary>
        /// <param name="amount">増減量。</param>
        public void AdjustCivilizationPower(long amount)
        {
            CivilizationPower += amount;
            if (CivilizationPower < 0) CivilizationPower = 0; // 文明力は0未満にならない
        }

        /// <summary>
        /// 歴史イベントを記録リストに追加します。
        /// </summary>
        /// <param name="eventDescription">イベントの記述。</param>
        public void AddHistoricalEvent(string eventDescription)
        {
            HistoricalEvents.Add($"[T{CurrentTurn:D4}/G{CurrentGeneration:D2}] {eventDescription}");
        }
    }

    /// <summary>
    /// 魔法（社会技術）のインターフェース。
    /// </summary>
    public interface IMagic
    {
        string Name { get; }
        string Description { get; }
        /// <summary>
        /// この社会技術が現在の状態に適用可能かどうかを判断します。
        /// </summary>
        /// <param name="state">現在のシミュレーション状態。</param>
        /// <returns>適用可能であればtrue、そうでなければfalse。</returns>
        bool IsApplicable(SimulationState state);
        /// <summary>
        /// この社会技術をシミュレーション状態に適用します。
        /// Safe-Fail: 適用が成功したかどうかを返します。
        /// </summary>
        /// <param name="state">現在のシミュレーション状態。</param>
        /// <returns>適用が成功すればtrue、失敗すればfalse。</returns>
        bool Apply(SimulationState state);
    }

    /// <summary>
    /// ジョブ（生活職業）のインターフェース。
    /// </summary>
    public interface IJob
    {
        string Name { get; }
        string Description { get; }
        /// <summary>
        /// このジョブの活動をシミュレーション状態に実行します。
        /// Safe-Fail: 実行が成功したかどうかを返します。
        /// </summary>
        /// <param name="state">現在のシミュレーション状態。</param>
        /// <returns>実行が成功すればtrue、失敗すればfalse。</returns>
        bool Perform(SimulationState state);
    }

    /// <summary>
    /// 例: 農業革命 - 食料生産を大幅に向上させる社会技術。
    /// </summary>
    public class AgriculturalRevolution : IMagic
    {
        public string Name => "農業革命";
        public string Description => "食料生産を大幅に向上させる社会技術。知識を消費し、食料生産効率を永続的に高める。";

        public bool IsApplicable(SimulationState state)
        {
            // 知識が50以上あり、かつまだこの技術が導入されていない場合
            return state.Resources["Knowledge"] >= 50 && !state.ActiveSocialTechnologies.Any(m => m.Name == Name);
        }

        public bool Apply(SimulationState state)
        {
            if (!IsApplicable(state))
            {
                Console.WriteLine($"[WARN][Magic] '{Name}' は適用条件を満たしていません。");
                return false;
            }
            try
            {
                state.Resources["Knowledge"] -= 50; // 知識を消費
                // 食料生産効率を向上させる永続的な効果を付与 (ここでは簡略化のため、初期ボーナスとログのみ)
                state.Resources["Food"] += 100; // 初期ボーナス
                state.AddHistoricalEvent($"{Name}が導入され、食料生産が向上した。");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR][Magic] '{Name}' の適用中にエラー: {ex.Message}");
                return false; // Safe-Fail
            }
        }
    }

    /// <summary>
    /// 例: 錬金術の基礎 - 材料を消費して知識を生み出す社会技術。
    /// </summary>
    public class BasicAlchemy : IMagic
    {
        public string Name => "錬金術の基礎";
        public string Description => "材料を消費して知識を生み出す社会技術。";

        public bool IsApplicable(SimulationState state)
        {
            return state.Resources["Knowledge"] >= 30 && state.Resources["Materials"] >= 20 && !state.ActiveSocialTechnologies.Any(m => m.Name == Name);
        }

        public bool Apply(SimulationState state)
        {
            if (!IsApplicable(state))
            {
                Console.WriteLine($"[WARN][Magic] '{Name}' は適用条件を満たしていません。");
                return false;
            }
            try
            {
                state.Resources["Knowledge"] -= 30;
                state.Resources["Materials"] -= 20;
                state.AddHistoricalEvent($"{Name}が導入され、知識獲得の新たな道が開かれた。");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR][Magic] '{Name}' の適用中にエラー: {ex.Message}");
                return false; // Safe-Fail
            }
        }
    }

    /// <summary>
    /// 例: 農民 - 食料を生産する生活職業。
    /// </summary>
    public class Farmer : IJob
    {
        public string Name => "農民";
        public string Description => "食料を生産し、文明の生存を支える。";

        public bool Perform(SimulationState state)
        {
            try
            {
                if (state.Resources.ContainsKey("Food"))
                {
                    // 農業革命が導入されている場合、生産量にボーナス
                    int production = 20;
                    if (state.ActiveSocialTechnologies.Any(m => m.Name == "農業革命"))
                    {
                        production += 10; // ボーナス
                    }
                    state.Resources["Food"] += production;
                    return true;
                }
                Console.WriteLine($"[WARN][Job] '{Name}' は食料リソースが見つからないため実行できません。");
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR][Job] '{Name}' の実行中にエラー: {ex.Message}");
                return false; // Safe-Fail
            }
        }
    }

    /// <summary>
    /// MagicSanitizerEngine: 社会技術（魔法）がシステムの整合性を破壊したり、
    /// 倫理的・バランス的な制約に反しないかを検証するエンジン。
    /// </summary>
    public class MagicSanitizerEngine
    {
        /// <summary>
        /// 指定された社会技術がシミュレーションに安全に導入できるかを検証します。
        /// </summary>
        /// <param name="magic">検証する社会技術。</param>
        /// <param name="state">現在のシミュレーション状態。</param>
        /// <returns>安全であればtrue、そうでなければfalse。</returns>
        public bool ValidateMagic(IMagic magic, SimulationState state)
        {
            try
            {
                // 1. 不安定なキーワードチェック (例: 無限、即時滅亡など)
                if (magic.Name.Contains("無限") || magic.Description.Contains("無限") ||
                    magic.Name.Contains("即時滅亡") || magic.Description.Contains("即時滅亡"))
                {
                    Console.WriteLine($"[MagicSanitizer] 警告: 不安定なキーワードを含む魔法 '{magic.Name}' を検出しました。拒否します。");
                    return false;
                }

                // 2. 既存の技術との競合チェック (例: 既に上位互換技術がある場合など)
                // この例では簡略化。実際には複雑な依存関係グラフをチェックする。
                if (magic.Name == "農業革命" && state.ActiveSocialTechnologies.Any(m => m.Name == "超農業技術"))
                {
                    Console.WriteLine($"[MagicSanitizer] 警告: '{magic.Name}' は既存の上位技術と競合するため拒否します。");
                    return false;
                }

                // 3. リソース消費の妥当性チェック (例: 存在しないリソースを消費しようとする、過剰な消費)
                // IMagicインターフェースにコスト情報を追加すればより詳細なチェックが可能だが、ここではApplyメソッド内でチェックされることを前提とする。

                // 4. 文明の安定性への影響予測 (高度なAI分析を想定)
                // 例: この技術が導入された場合、文明力が急激に低下する可能性がないか？
                // 現状ではダミーロジック
                if (magic.Name.Contains("危険"))
                {
                    Console.WriteLine($"[MagicSanitizer] 警告: '{magic.Name}' は文明の安定性を損なう可能性があり、拒否します。");
                    return false;
                }

                Console.WriteLine($"[MagicSanitizer] 魔法 '{magic.Name}' は検証を通過しました。");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR][MagicSanitizer] 魔法検証中にエラー: {ex.Message}");
                return false; // Safe-Fail
            }
        }
    }

    /// <summary>
    /// CombatEngine: フロム風戦闘シミュレーションを処理するクラス。
    /// </summary>
    public class CombatEngine
    {
        private Random _random = new Random();

        /// <summary>
        /// フロム風戦闘をシミュレートします。
        /// </summary>
        /// <param name="state">現在のシミュレーション状態。</param>
        /// <returns>戦闘シミュレーションが成功すればtrue、致命的な失敗でシミュレーションを停止すべき場合はfalse。</returns>
        public bool SimulateCombat(SimulationState state)
        {
            try
            {
                // 50ターンごとに大規模な脅威が発生する可能性
                if (state.CurrentTurn % 50 == 0 && state.CurrentTurn >= 1050)
                {
                    Console.WriteLine($"[戦闘] ターン {state.CurrentTurn}: 異形の脅威が迫る... 文明の存亡をかけた戦いが始まる！");
                    state.AddHistoricalEvent("異形の脅威が文明に襲いかかった。");

                    // 文明力とランダム要素で勝敗を決定
                    int threatLevel = _random.Next(500, 2000); // 脅威のレベル
                    long effectivePower = state.CivilizationPower + state.Resources["Knowledge"] / 5; // 知識も戦闘力に影響

                    if (effectivePower > threatLevel)
                    {
                        // 勝利
                        long powerGain = _random.Next(50, 200);
                        state.AdjustCivilizationPower(powerGain);
                        state.Resources["Materials"] += _random.Next(20, 50); // 戦利品
                        state.AddHistoricalEvent("異形の脅威を退け、文明の力が増大した。");
                        Console.WriteLine($"[戦闘] 勝利！文明の力が {powerGain} 向上しました。現在の文明力: {state.CivilizationPower}");
                    }
                    else
                    {
                        // 敗北
                        long powerLoss = _random.Next(100, 300);
                        state.AdjustCivilizationPower(-powerLoss);
                        state.Resources["Food"] -= _random.Next(50, 100); // 食料損失
                        state.Resources["Materials"] -= _random.Next(30, 80); // 材料損失
                        state.AddHistoricalEvent("異形の脅威により、文明は甚大な被害を受け、力が衰退した。");
                        Console.WriteLine($"[戦闘] 敗北...文明は {powerLoss} の力を失いました。現在の文明力: {state.CivilizationPower}");

                        if (state.CivilizationPower <= 0)
                        {
                            Console.WriteLine("[戦闘] 文明は異形の脅威に屈し、滅亡しました。");
                            return false; // 致命的な失敗、シミュレーション停止
                        }
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR][CombatEngine] 戦闘シミュレーション中にエラー: {ex.Message}");
                return false; // Safe-Fail
            }
        }
    }

    /// <summary>
    /// CraftingEngine: ブラインド熱科学クラフトを処理するクラス。
    /// </summary>
    public class CraftingEngine
    {
        private Random _random = new Random();

        /// <summary>
        /// ブラインド熱科学クラフトを実行します。
        /// </summary>
        /// <param name="state">現在のシミュレーション状態。</param>
        /// <returns>クラフト操作が成功すればtrue、失敗すればfalse。</returns>
        public bool PerformCrafting(SimulationState state)
        {
            try
            {
                // 特定のターンでクラフトの機会を設ける (例: 25ターンごとに)
                if (state.CurrentTurn % 25 == 0 && state.CurrentTurn >= 1050)
                {
                    int requiredMaterials = 50;
                    int requiredKnowledge = 30;

                    if (state.Resources["Materials"] >= requiredMaterials && state.Resources["Knowledge"] >= requiredKnowledge)
                    {
                        Console.WriteLine($"[クラフト] ターン {state.CurrentTurn}: 未知の熱科学クラフトを試みる...");
                        state.Resources["Materials"] -= requiredMaterials;
                        state.Resources["Knowledge"] -= requiredKnowledge;

                        // 成功率をランダムで決定 (知識レベルで成功率が変動するように調整可能)
                        int successChance = 60 + (state.Resources["Knowledge"] / 100); // 知識が多いほど成功率アップ
                        if (successChance > 90) successChance = 90; // 上限設定

                        if (_random.Next(100) < successChance) // 成功
                        {
                            int knowledgeGain = _random.Next(40, 80);
                            int powerGain = _random.Next(30, 60);
                            state.Resources["Knowledge"] += knowledgeGain; // 新しい技術や知識の発見
                            state.AdjustCivilizationPower(powerGain);
                            state.AddHistoricalEvent("熱科学クラフトにより新たな技術が発見され、文明の力が向上した。");
                            Console.WriteLine($"[クラフト] 成功！新たな知識({knowledgeGain})と文明力({powerGain})を獲得しました。");
                        }
                        else // 失敗
                        {
                            int materialReturn = _random.Next(10, 30);
                            state.Resources["Materials"] += materialReturn; // 一部資源が戻る
                            state.AddHistoricalEvent("熱科学クラフトは失敗したが、貴重なデータを得た。");
                            Console.WriteLine($"[クラフト] 失敗...しかし、貴重なデータを得ました。材料が {materialReturn} 戻りました。");
                        }
                    }
                    else
                    {
                        // Console.WriteLine("[クラフト] 資源不足のためクラフトできません。"); // 毎ターン表示するとうるさいのでコメントアウト
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR][CraftingEngine] クラフト中にエラー: {ex.Message}");
                return false; // Safe-Fail
            }
        }
    }

    /// <summary>
    /// HistoryRecorder: 文明の正史を記録するクラス。
    /// </summary>
    public class HistoryRecorder
    {
        private readonly string _historyFilePath = "civilization_history.log";
        private readonly object _fileLock = new object(); // ファイル書き込みの排他制御用

        /// <summary>
        /// HistoryRecorderの新しいインスタンスを初期化します。
        /// </summary>
        public HistoryRecorder()
        {
            try
            {
                // ファイルが存在すれば追記、なければ新規作成し、開始メッセージを書き込む
                if (!File.Exists(_historyFilePath))
                {
                    File.WriteAllText(_historyFilePath, "--- 文明の正史記録開始 ---\n");
                }
                else
                {
                    File.AppendAllText(_historyFilePath, "\n--- 既存の正史に追記開始 ---\n");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR][HistoryRecorder] 履歴ファイルの初期化中にエラー: {ex.Message}");
            }
        }

        /// <summary>
        /// 現在のシミュレーション状態から最新の歴史イベントをファイルにコミットします。
        /// Safe-Fail: コミットが成功したかどうかを返します。
        /// </summary>
        /// <param name="state">現在のシミュレーション状態。</param>
        /// <returns>コミットが成功すればtrue、失敗すればfalse。</returns>
        public bool CommitHistory(SimulationState state)
        {
            try
            {
                // 最新のイベントをファイルに追記
                if (state.HistoricalEvents.Any())
                {
                    string latestEvent = state.HistoricalEvents.Last();
                    lock (_fileLock) // ファイル書き込みの競合を避ける
                    {
                        File.AppendAllText(_historyFilePath, latestEvent + "\n");
                    }
                    // ログにも出力
                    Console.WriteLine($"[正史コミット] {latestEvent}");
                    // コミットしたイベントはクリアして、次ターンで新しいイベントのみを記録するようにする
                    state.HistoricalEvents.Clear();
                }
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR][HistoryRecorder] 正史コミット中にエラー: {ex.Message}");
                return false; // Safe-Fail
            }
        }

        /// <summary>
        /// シミュレーション終了時に履歴ファイルに最終メッセージを追記します。
        /// </summary>
        /// <param name="endTurn">シミュレーションの終了ターン。</param>
        public void FinalizeHistory(int endTurn)
        {
            try
            {
                lock (_fileLock)
                {
                    File.AppendAllText(_historyFilePath, $"--- ターン {endTurn} での記録終了 ---\n");
                }
                Console.WriteLine($"[正史] 記録をファイル '{_historyFilePath}' にコミットしました。");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR][HistoryRecorder] 最終コミット中にエラー: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// GameSimulator: ゲームの主要なシミュレーションロジックを管理するクラス。
    /// </summary>
    public class GameSimulator
    {
        private SimulationState _state;
        private CombatEngine _combatEngine;
        private CraftingEngine _craftingEngine;
        private MagicSanitizerEngine _magicSanitizer;
        private HistoryRecorder _historyRecorder;

        /// <summary>
        /// GameSimulatorの新しいインスタンスを初期化します。
        /// </summary>
        public GameSimulator()
        {
            _combatEngine = new CombatEngine();
            _craftingEngine = new CraftingEngine();
            _magicSanitizer = new MagicSanitizerEngine();
            _historyRecorder = new HistoryRecorder();
        }

        /// <summary>
        /// 指定された期間、シミュレーションを連続自律進行させます。
        /// </summary>
        /// <param name="startTurn">シミュレーションの開始ターン。</param>
        /// <param name="endTurn">シミュレーションの終了ターン。</param>
        public void RunSimulationPeriod(int startTurn, int endTurn)
        {
            Console.WriteLine($"--- シミュレーション開始: ターン {startTurn} から {endTurn} ---");
            _state = new SimulationState(startTurn); // シミュレーション開始ターンで状態を初期化

            for (int turn = startTurn; turn <= endTurn; turn++)
            {
                Console.WriteLine($"\n--- ターン {turn:D4} (第 {_state.CurrentGeneration:D2} 世代) ---");
                _state.AdvanceTurn(); // ターンを進める

                // 1. ジョブの実行 (生活職業)
                if (!ManageJobs())
                {
                    Console.WriteLine("[FATAL] ジョブ管理に失敗しました。シミュレーションを中断します。");
                    break;
                }

                // 2. 魔法の適用 (社会技術) - 新規発見・研究・適用
                if (!ApplySocialTechnologies())
                {
                    Console.WriteLine("[FATAL] 社会技術の適用に失敗しました。シミュレーションを中断します。");
                    break;
                }

                // 3. フロム風戦闘
                if (!_combatEngine.SimulateCombat(_state))
                {
                    Console.WriteLine("[FATAL] 戦闘シミュレーションに失敗しました。シミュレーションを中断します。");
                    break;
                }

                // 4. ブラインド熱科学クラフト
                if (!_craftingEngine.PerformCrafting(_state))
                {
                    Console.WriteLine("[FATAL] クラフトに失敗しました。シミュレーションを中断します。");
                    break;
                }

                // 5. 正史のコミット
                if (!_historyRecorder.CommitHistory(_state))
                {
                    Console.WriteLine("[FATAL] 正史コミットに失敗しました。シミュレーションを中断します。");
                    break;
                }

                // 現在の状態をログ出力
                Console.WriteLine($"[状態] 文明力: {_state.CivilizationPower}, 食料: {_state.Resources["Food"]}, 材料: {_state.Resources["Materials"]}, 知識: {_state.Resources["Knowledge"]}");

                // 世代の変わり目に特別なイベントを発生させる
                if ((_state.CurrentTurn - startTurn) % 50 == 0 && _state.CurrentTurn != startTurn)
                {
                    Console.WriteLine($"--- 第 {_state.CurrentGeneration} 世代の終わり ---");
                    _state.AddHistoricalEvent($"第 {_state.CurrentGeneration} 世代が終わりを告げ、新たな時代が始まった。");
                    _historyRecorder.CommitHistory(_state); // 世代末のイベントを即時コミット
                }

                // 致命的な状態チェック: 文明力または食料が枯渇したら滅亡
                if (_state.CivilizationPower <= 0 || _state.Resources["Food"] <= 0)
                {
                    Console.WriteLine("[FATAL] 文明が維持不可能になりました。シミュレーションを中断します。");
                    _state.AddHistoricalEvent("文明は滅亡した。");
                    _historyRecorder.CommitHistory(_state);
                    break;
                }

                Thread.Sleep(50); // 視覚的に進行を確認するための短い遅延
            }

            _historyRecorder.FinalizeHistory(endTurn);
            Console.WriteLine($"--- シミュレーション終了: ターン {endTurn} ---");
        }

        /// <summary>
        /// アクティブなジョブ（生活職業）を実行します。
        /// Safe-Fail: ジョブ管理が成功したかどうかを返します。
        /// </summary>
        /// <returns>成功すればtrue、失敗すればfalse。</returns>
        private bool ManageJobs()
        {
            try
            {
                foreach (var job in _state.ActiveJobs.ToList()) // ToList()で列挙中にコレクション変更を避ける
                {
                    if (!job.Perform(_state))
                    {
                        Console.WriteLine($"[WARN] ジョブ '{job.Name}' の実行に失敗しました。");
                        // 失敗してもシミュレーションは続行するが、ログは残す
                    }
                }
                // 新しいジョブの発見や割り当てロジックはここに追加可能
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR][GameSimulator] ジョブ管理中にエラー: {ex.Message}");
                return false; // Safe-Fail
            }
        }

        /// <summary>
        /// 新しい社会技術（魔法）の発見・研究・適用を試みます。
        /// Safe-Fail: 社会技術の適用プロセスが成功したかどうかを返します。
        /// </summary>
        /// <returns>成功すればtrue、失敗すればfalse。</returns>
        private bool ApplySocialTechnologies()
        {
            try
            {
                // 特定のターンで新しい社会技術を発見するロジック (例)
                IMagic newMagic = null;
                if (_state.CurrentTurn == 1060 && !_state.ActiveSocialTechnologies.Any(m => m.Name == "農業革命"))
                {
                    newMagic = new AgriculturalRevolution();
                }
                else if (_state.CurrentTurn == 1120 && !_state.ActiveSocialTechnologies.Any(m => m.Name == "錬金術の基礎"))
                {
                    newMagic = new BasicAlchemy();
                }
                // 他の技術発見条件を追加...

                if (newMagic != null)
                {
                    Console.WriteLine($"[魔法] 新しい社会技術 '{newMagic.Name}' の研究が完了した。検証を開始します。");
                    if (_magicSanitizer.ValidateMagic(newMagic, _state))
                    {
                        if (newMagic.IsApplicable(_state))
                        {
                            if (newMagic.Apply(_state))
                            {
                                _state.ActiveSocialTechnologies.Add(newMagic);
                                _state.AddHistoricalEvent($"新たな社会技術 '{newMagic.Name}' が導入された。");
                                Console.WriteLine($"[魔法] 社会技術 '{newMagic.Name}' を文明に導入しました。");
                            }
                            else
                            {
                                Console.WriteLine($"[WARN] 社会技術 '{newMagic.Name}' の適用に失敗しました（Applyメソッドがfalseを返しました）。");
                            }
                        }
                        else
                        {
                            Console.WriteLine($"[INFO] 社会技術 '{newMagic.Name}' は現在適用条件を満たしていません。");
                        }
                    }
                    else
                    {
                        Console.WriteLine($"[WARN] 社会技術 '{newMagic.Name}' はMagicSanitizerEngineによって拒否されました。");
                    }
                }

                // 既存の社会技術の効果を毎ターン適用するロジック (もしあれば)
                // 現在の例では、AgriculturalRevolutionやBasicAlchemyは一度適用されると永続的な効果を持つと想定。
                // 毎ターン効果がある場合は、ここで各ActiveSocialTechnologiesのApplyを呼び出す。
                // 例: foreach (var magic in _state.ActiveSocialTechnologies) { magic.ApplyPerTurnEffect(_state); }

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR][GameSimulator] 社会技術適用中にエラー: {ex.Message}");
                return false; // Safe-Fail
            }
        }
    }

    /// <summary>
    /// プログラムのエントリポイント。
    /// </summary>
    public class Program
    {
        public static void Main(string[] args)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8; // コンソールでの日本語表示を正しくする

            // シミュレーション期間の定義
            const int StartTurn = 1050;
            const int EndTurn = 1250;

            GameSimulator simulator = new GameSimulator();
            simulator.RunSimulationPeriod(StartTurn, EndTurn);

            Console.WriteLine("\nシミュレーションが完了しました。");
            Console.WriteLine("正史は 'civilization_history.log' に記録されています。");
            Console.WriteLine("何かキーを押して終了します...");
            Console.ReadKey();
        }
    }
}
```
```
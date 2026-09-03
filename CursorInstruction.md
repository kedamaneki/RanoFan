はい、承知いたしました。
「フロム風戦闘×ブラインド熱科学クラフト×千年史自律シミュレーター」のリードディレクターとして、Cursor(IDE)のCtrl+Lへそのまま読み込ませてC#コード化できる「精密な実装指示プロンプト」をMarkdown形式で出力します。

今回の「subprocess 文字コード修正テスト」の指示は、ゲームの「ブラインド熱科学クラフト」や「千年史自律シミュレーター」において、外部の計算エンジンやデータ処理スクリプト（Python, R, etc.）と安全かつ確実に連携し、特に文字コードの問題を解決するための基盤実装と解釈します。これは、ゲーム内の高度な「社会技術（Magic）」を具現化し、「生活職業（Job）」の多様なタスクを可能にするための核心的なコンポーネントとなります。

---

# サブプロセス連携モジュール「ArcaneExecutor」の実装指示

## 1. 目的と背景
「千年史自律シミュレーター」や「ブラインド熱科学クラフト」において、外部の計算エンジンやデータ処理スクリプト（Python, R, etc.）と安全かつ確実に連携するための基盤を構築する。特に、異なるOS環境や外部スクリプトの出力における文字コードの問題を解決し、データの整合性を保証する。これは、ゲーム内の高度な「社会技術（Magic）」を具現化し、「生活職業（Job）」の多様なタスクを可能にするための核心的なコンポーネントとなる。

## 2. 設計原則
*   **Safe-Fail構造**: 外部プロセスの異常終了、タイムアウト、不正な出力など、あらゆる予期せぬ事態に対してシステム全体がクラッシュしないよう、堅牢なエラーハンドリングとフォールバックメカニズムを組み込む。
*   **MagicSanitizerEngine連携**: 外部プロセスに渡す引数は`MagicSanitizerEngine`を通じてサニタイズされ、外部プロセスからの出力も`MagicSanitizerEngine`によって検証・クリーンアップされることを前提とする。
*   **Job/Magic規約**: このサブプロセス実行機能自体を、特定の「Magic」（例: `ArcaneComputationMagic`）の基盤技術として位置づけ、様々な「Job」（例: `AlchemistJob`, `HistorianJob`）がこのMagicを利用して専門的なタスクを遂行する。

## 3. 実装詳細指示

### 3.1. クラス定義: `ArcaneExecutor`

外部プロセスを実行し、その入出力を管理する静的クラスとして`ArcaneExecutor`を定義する。

```csharp
// ファイル: Core/System/ArcaneExecutor.cs
using System;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;
using FromSoftLikeGame.Core.Sanitization; // MagicSanitizerEngineのネームスペースを想定
using FromSoftLikeGame.Core.Logging; // ログ記録用ネームスペースを想定

namespace FromSoftLikeGame.Core.System
{
    /// <summary>
    /// 外部プロセスを安全に実行し、入出力の文字コードを適切に処理する「社会技術（Magic）」の基盤。
    /// Safe-Fail構造、MagicSanitizerEngine連携、Job/Magic規約を厳格に遵守する。
    /// </summary>
    public static class ArcaneExecutor
    {
        // 実行結果を格納する内部構造体
        public struct ExecutionResult
        {
            public bool Success { get; init; }
            public int ExitCode { get; init; }
            public string StandardOutput { get; init; }
            public string StandardError { get; init; }
            public string ErrorMessage { get; init; }
            public TimeSpan ExecutionTime { get; init; }
            public bool TimedOut { get; init; }
        }

        /// <summary>
        /// 外部プロセスを非同期で実行し、標準出力/エラーをキャプチャする。
        /// Safe-Fail構造とMagicSanitizerEngine連携を厳守する。
        /// 文字コードの明示的な指定により、異なる環境での文字化け問題を解決する。
        /// </summary>
        /// <param name="fileName">実行するプログラムのパス。</param>
        /// <param name="arguments">プログラムに渡す引数。MagicSanitizerEngineで事前にサニタイズ済みであること。</param>
        /// <param name="workingDirectory">プロセスの作業ディレクトリ。nullの場合は現在の作業ディレクトリを使用。</param>
        /// <param name="timeoutMs">プロセスの最大実行時間（ミリ秒）。0以下の場合は無制限。</param>
        /// <param name="outputEncoding">標準出力の文字コード。指定がない場合はUTF8を試行。</param>
        /// <param name="errorEncoding">標準エラーの文字コード。指定がない場合はUTF8を試行。</param>
        /// <returns>ExecutionResult構造体。実行結果、出力、エラー情報を含む。</returns>
        public static async Task<ExecutionResult> ExecuteProcessAsync(
            string fileName,
            string arguments,
            string workingDirectory = null,
            int timeoutMs = 60000, // デフォルト60秒
            Encoding outputEncoding = null,
            Encoding errorEncoding = null)
        {
            // 1. MagicSanitizerEngineによる入力検証
            // 外部プロセスへのパスや引数に不正な値が含まれていないか厳格にチェックする。
            // これにより、コマンドインジェクションなどのセキュリティリスクを低減する。
            if (!MagicSanitizerEngine.IsValidFilePath(fileName) || !MagicSanitizerEngine.IsValidArguments(arguments))
            {
                Log.Error($"[ArcaneExecutor] Invalid input detected. File: '{fileName}', Args: '{arguments}'");
                return new ExecutionResult { Success = false, ErrorMessage = "Invalid input arguments or file path detected by MagicSanitizerEngine." };
            }

            var result = new ExecutionResult();
            var stopwatch = Stopwatch.StartNew();

            try
            {
                using (var process = new Process())
                {
                    process.StartInfo.FileName = fileName;
                    process.StartInfo.Arguments = arguments;
                    process.StartInfo.WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory;
                    process.StartInfo.UseShellExecute = false; // シェルを使わないことで、セキュリティリスクを低減し、リダイレクトを可能にする
                    process.StartInfo.RedirectStandardOutput = true; // 標準出力をリダイレクトしてキャプチャ
                    process.StartInfo.RedirectStandardError = true;  // 標準エラーをリダイレクトしてキャプチャ
                    process.StartInfo.CreateNoWindow = true; // 新しいウィンドウを作成しない（バックグラウンド実行）

                    // **文字コードの明示的な設定**
                    // Windows環境でのデフォルトエンコーディングはUTF-8でない場合が多く、
                    // これを明示的に指定することで文字化けを防ぐ。
                    process.StartInfo.StandardOutputEncoding = outputEncoding ?? Encoding.UTF8;
                    process.StartInfo.StandardErrorEncoding = errorEncoding ?? Encoding.UTF8;

                    // プロセス開始
                    if (!process.Start())
                    {
                        Log.Error($"[ArcaneExecutor] Failed to start process: '{fileName}' with arguments '{arguments}'.");
                        return new ExecutionResult { Success = false, ErrorMessage = "Failed to start external process." };
                    }

                    // 標準出力と標準エラーの非同期読み取り
                    // これにより、プロセスが大量の出力を生成してもデッドロックを回避できる
                    var outputTask = process.StandardOutput.ReadToEndAsync();
                    var errorTask = process.StandardError.ReadToEndAsync();

                    // プロセス終了待機とタイムアウト処理
                    // 指定された時間内にプロセスが終了しない場合、強制終了を試みる (Safe-Fail)
                    var processCompletionTask = Task.Run(() => process.WaitForExit(timeoutMs));
                    var completedTask = await Task.WhenAny(processCompletionTask, Task.Delay(timeoutMs));

                    if (completedTask == processCompletionTask && process.HasExited)
                    {
                        // プロセスが時間内に正常に（または異常終了したが時間内に）終了した
                        await Task.WhenAll(outputTask, errorTask); // 出力ストリームの読み取り完了を待つ
                        result = new ExecutionResult
                        {
                            Success = process.ExitCode == 0, // 終了コード0を成功と見なす
                            ExitCode = process.ExitCode,
                            StandardOutput = MagicSanitizerEngine.SanitizeOutput(await outputTask), // 出力もMagicSanitizerEngineでサニタイズ
                            StandardError = MagicSanitizerEngine.SanitizeOutput(await errorTask),   // エラーもMagicSanitizerEngineでサニタイズ
                            ErrorMessage = process.ExitCode != 0 ? $"Process exited with non-zero code {process.ExitCode}" : null,
                            ExecutionTime = stopwatch.Elapsed,
                            TimedOut = false
                        };
                        Log.Info($"[ArcaneExecutor] Process '{fileName}' completed in {stopwatch.Elapsed.TotalSeconds:F2}s with exit code {process.ExitCode}.");
                    }
                    else
                    {
                        // タイムアウト発生
                        result = new ExecutionResult
                        {
                            Success = false,
                            ExitCode = -1, // タイムアウトを示すカスタムコード
                            StandardOutput = MagicSanitizerEngine.SanitizeOutput(outputTask.IsCompleted ? await outputTask : "Output stream not fully read due to timeout."),
                            StandardError = MagicSanitizerEngine.SanitizeOutput(errorTask.IsCompleted ? await errorTask : "Error stream not fully read due to timeout."),
                            ErrorMessage = $"Process timed out after {timeoutMs}ms.",
                            ExecutionTime = stopwatch.Elapsed,
                            TimedOut = true
                        };
                        Log.Warn($"[ArcaneExecutor] Process '{fileName}' timed out after {timeoutMs}ms. Attempting to kill process.");
                        try
                        {
                            if (!process.HasExited)
                            {
                                process.Kill(); // プロセスを強制終了し、リソースリークを防ぐ
                                Log.Warn($"[ArcaneExecutor] Process '{fileName}' killed successfully after timeout.");
                            }
                        }
                        catch (Exception killEx)
                        {
                            Log.Error($"[ArcaneExecutor] Failed to kill process '{fileName}' after timeout: {killEx.Message}");
                            result.ErrorMessage += $" Failed to kill process: {killEx.Message}";
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Safe-Fail: 予期せぬ例外を捕捉し、システムクラッシュを防ぐ。
                // プロセス起動失敗、ファイルが見つからない、アクセス権の問題など。
                Log.Error($"[ArcaneExecutor] An unexpected error occurred while executing process '{fileName} {arguments}': {ex.Message}\n{ex.StackTrace}");
                result = new ExecutionResult
                {
                    Success = false,
                    ExitCode = -2, // 内部エラーを示すカスタムコード
                    ErrorMessage = $"Internal executor error: {ex.Message}",
                    ExecutionTime = stopwatch.Elapsed,
                    TimedOut = false
                };
            }
            finally
            {
                stopwatch.Stop();
            }

            // 最終的な結果を返す前に、出力内容がゲームのロジックにとって安全か再確認する（MagicSanitizerEngineが内部で処理済みだが、念のため）
            if (!result.Success)
            {
                Log.Error($"[ArcaneExecutor] Process '{fileName}' failed. Error: {result.ErrorMessage}. Output: '{result.StandardOutput}'. Error Output: '{result.StandardError}'");
            }

            return result;
        }
    }
}
```

### 3.2. 依存モジュール（仮定義）

`MagicSanitizerEngine`と`Logging`モジュールは、既存の規約に従って実装されているものと仮定します。もし未実装であれば、以下の仮定義を参考に実装してください。

```csharp
// ファイル: Core/Sanitization/MagicSanitizerEngine.cs (仮実装)
namespace FromSoftLikeGame.Core.Sanitization
{
    /// <summary>
    /// 入力引数や外部プロセスからの出力をサニタイズし、セキュリティとデータ整合性を保証するエンジン。
    /// </summary>
    public static class MagicSanitizerEngine
    {
        /// <summary>
        /// ファイルパスが安全で、不正な操作を意図していないか検証します。
        /// </summary>
        /// <param name="path">検証するファイルパス。</param>
        /// <returns>パスが安全であればtrue。</returns>
        public static bool IsValidFilePath(string path)
        {
            // パスインジェクション攻撃などを防ぐための厳格な検証ロジックを実装する。
            // 例: パスが許可されたディレクトリ内にあるか、不正な文字を含まないか、絶対パスがホワイトリストに含まれるかなど。
            // 現状は基本的なチェックのみ。
            if (string.IsNullOrWhiteSpace(path)) return false;
            if (path.Contains("..") || path.Contains(";") || path.Contains("&") || path.Contains("|")) return false;
            // さらに、許可された実行ファイルのみを許可するホワイトリスト方式を推奨
            // 例: Path.GetFileName(path) == "python" || Path.GetFileName(path) == "rscript" など
            return true; 
        }

        /// <summary>
        /// プロセスに渡す引数が安全で、不正なコマンドを含まないか検証します。
        /// </summary>
        /// <param name="args">検証する引数文字列。</param>
        /// <returns>引数が安全であればtrue。</returns>
        public static bool IsValidArguments(string args)
        {
            // 引数インジェクション攻撃などを防ぐための厳格な検証ロジックを実装する。
            // 例: シェルコマンドのメタ文字（`&`, `|`, `;`, `&&`, `||`, `>`, `<`, `(`, `)` など）をエスケープまたは拒否する。
            // 現状は基本的なチェックのみ。
            if (args == null) return true; // 引数なしは許可
            if (args.Contains(";") || args.Contains("&") || args.Contains("|") || args.Contains("`")) return false;
            return true;
        }

        /// <summary>
        /// 外部プロセスからの出力をゲーム内で安全に表示・利用できるようサニタイズします。
        /// </summary>
        /// <param name="output">サニタイズする出力文字列。</param>
        /// <returns>サニタイズされた文字列。</returns>
        public static string SanitizeOutput(string output)
        {
            // 外部プロセスからの出力をゲーム内で安全に表示・利用できるようサニタイズする。
            // 例: HTMLエンコード、特定の制御文字の除去、最大長制限、不正な文字シーケンスの置換など。
            // 現状はnullチェックのみ。
            return output ?? string.Empty; 
        }
    }
}

// ファイル: Core/Logging/Log.cs (仮実装)
namespace FromSoftLikeGame.Core.Logging
{
    /// <summary>
    /// ゲーム全体のログ記録システム。Safe-Fail構造の一部としてエラーや警告を記録する。
    /// </summary>
    public static class Log
    {
        public static void Info(string message) => Console.WriteLine($"[INFO] {DateTime.Now:HH:mm:ss} {message}");
        public static void Warn(string message) => Console.WriteLine($"[WARN] {DateTime.Now:HH:mm:ss} {message}");
        public static void Error(string message) => Console.Error.WriteLine($"[ERROR] {DateTime.Now:HH:mm:ss} {message}");
        public static void Debug(string message) => Console.WriteLine($"[DEBUG] {DateTime.Now:HH:mm:ss} {message}"); // デバッグ用
    }
}
```

### 3.3. Job/Magic規約への統合例

この`ArcaneExecutor`は、例えば`AlchemistJob`が特定の化学反応シミュレーション（`AlchemicalReactionMagic`）を実行するために、外部のPythonスクリプトを呼び出す際に利用されます。

```csharp
// ファイル: Game/Magic/AlchemicalReactionMagic.cs (例)
using FromSoftLikeGame.Core.System;
using FromSoftLikeGame.Core.Sanitization;
using FromSoftLikeGame.Core.Logging;
using System.Text; // Encodingのために必要

namespace FromSoftLikeGame.Game.Magic
{
    /// <summary>
    /// 錬金術の反応をシミュレートする「社会技術（Magic）」。
    /// 内部でArcaneExecutorを利用し、外部の計算エンジンと連携する。
    /// </summary>
    public static class AlchemicalReactionMagic
    {
        public static async Task<string> SimulateReactionAsync(string inputIngredients, int purityLevel)
        {
            // MagicSanitizerEngineで入力材料をサニタイズ
            // ここでは例として出力サニタイズを流用していますが、入力専用のIsValidIngredientなどが必要になるでしょう。
            var sanitizedIngredients = MagicSanitizerEngine.SanitizeOutput(inputIngredients); 

            // 外部Pythonスクリプトへの引数を構築
            // スクリプトパスはゲームの実行パスからの相対パスを想定
            var scriptPath = "Scripts/AlchemistSimulator.py"; 
            // 引数もMagicSanitizerEngineで検証済みであることを前提とする
            var arguments = $"\"{scriptPath}\" --ingredients \"{sanitizedIngredients}\" --purity {purityLevel}";

            Log.Info($"[AlchemicalReactionMagic] Initiating simulation with: {arguments}");

            // ArcaneExecutorを呼び出し、外部プロセスを実行
            // ここで文字コードを明示的にUTF-8に指定することで、PythonスクリプトのUTF-8出力を正しく解釈する
            var result = await ArcaneExecutor.ExecuteProcessAsync(
                "python", // Pythonインタープリタのパスを環境変数PATHから解決することを期待
                arguments,
                outputEncoding: Encoding.UTF8,
                errorEncoding: Encoding.UTF8
            );

            if (result.Success)
            {
                Log.Info($"[AlchemicalReactionMagic] Simulation successful. Output: {result.StandardOutput}");
                // MagicSanitizerEngineで結果を最終的に検証・整形し、ゲームロジックに渡す
                return MagicSanitizerEngine.SanitizeOutput(result.StandardOutput);
            }
            else
            {
                Log.Error($"[AlchemicalReactionMagic] Simulation failed. Error: {result.ErrorMessage}. Stderr: {result.StandardError}");
                // Safe-Fail: シミュレーション失敗時のフォールバック処理や、ユーザーへの適切なフィードバック
                return $"Simulation failed: {result.ErrorMessage}. Please check your ingredients and purity level. Details: {result.StandardError}";
            }
        }
    }
}

// ファイル: Game/Jobs/AlchemistJob.cs (例)
using FromSoftLikeGame.Game.Magic;
using System.Threading.Tasks;

namespace FromSoftLikeGame.Game.Jobs
{
    /// <summary>
    /// 錬金術師の「生活職業（Job）」。
    /// AlchemicalReactionMagicを利用してタスクを遂行する。
    /// </summary>
    public class AlchemistJob
    {
        public string JobName { get; } = "Alchemist";

        public async Task<string> PerformAlchemyTask(string rawIngredients, int skillLevel)
        {
            // ジョブのロジックに基づいて、Magicを呼び出す
            // スキルレベルに応じてpurityLevelを決定するなど、ゲーム固有のロジックを適用
            int purityLevel = skillLevel * 10;
            string reactionResult = await AlchemicalReactionMagic.SimulateReactionAsync(rawIngredients, purityLevel);
            
            // 結果をゲームの状態に反映したり、UIに表示したりする
            return $"Alchemy task completed by {JobName}. Result: {reactionResult}";
        }
    }
}
```

## 4. テストと検証
実装後、以下のシナリオで徹底的なテストを実施してください。

*   **正常系**:
    *   PythonスクリプトがUTF-8で「Hello, 世界！」のような多言語文字列を標準出力に出力し、C#側で正しく受信・表示されることを確認。
    *   Pythonスクリプトが大量のデータをUTF-8で出力し、デッドロックなく処理されることを確認。
*   **文字コード異常系**:
    *   外部スクリプトが意図的に非UTF-8（例: Shift-JIS, Latin-1）の文字を標準出力/エラーに出力した場合、`outputEncoding`/`errorEncoding`パラメータを適切に設定することで正しく処理されるか。
    *   `outputEncoding`/`errorEncoding`が指定されない場合（デフォルトUTF-8）、非UTF-8出力がどのように扱われるか（文字化け、例外など）を確認し、Safe-Failが機能することを確認。
*   **プロセス異常系**:
    *   存在しない実行ファイルを指定した場合（`FileNotFoundException`が捕捉され、`ExecutionResult.Success = false`となること）。
    *   不正な引数を指定した場合（`MagicSanitizerEngine`が捕捉すること、またはプロセスがエラー終了すること）。
    *   実行中にクラッシュするスクリプト（`ExitCode`が非ゼロとなり、`StandardError`に情報が含まれること）。
    *   意図的に無限ループする、または非常に時間がかかるスクリプトを指定し、タイムアウト処理が正しく機能し、プロセスが強制終了されることを確認。
    *   プロセスが大量の標準エラーを出力した場合。
*   **リソース管理**:
    *   プロセスが正しく終了し、関連するリソース（ファイルハンドル、メモリなど）が解放されることを確認。特にタイムアウトで強制終了された場合。

---
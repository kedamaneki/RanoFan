# C#実装指示: GitPathManager (フロム風戦闘×ブラインド熱科学クラフト×千年史自律シミュレーター)

## 目的
ゲームの外部連携（MOD管理、アセットバージョン管理、自律シミュレーターの外部データ同期など）において、Git実行ファイルのパスを動的に検出し、検証し、安全に利用するための `GitPathManager` クラスを実装します。これにより、異なる環境下でもGit連携機能が安定して動作することを保証します。特に、Gitパスが不正な場合の修正・検出ロジックに焦点を当てます。

## コンセプトとの関連性
*   **フロム風戦闘**: 直接的な関連は薄いですが、アセットやバランス調整のバージョン管理に間接的に寄与し、開発・運用における安定性を高めます。
*   **ブラインド熱科学クラフト**: クラフトレシピや素材データのバージョン管理、あるいはユーザー生成コンテンツの取り込みに利用される可能性があり、コンテンツの整合性を保つ基盤となります。
*   **千年史自律シミュレーター**: シミュレーターが参照する歴史データ、外部イベントスクリプト、MODなどのバージョン管理や同期に不可欠な「社会技術」として機能します。これにより、シミュレーターが外部リソースを安全かつ正確に取り込み、歴史の連続性を維持・管理できるようになります。

## 厳守事項
1.  **Safe-Fail構造**: Gitパスの検出失敗、コマンド実行エラーなど、あらゆる異常事態においてアプリケーションがクラッシュせず、適切なフォールバックとユーザーフィードバックを提供すること。エラーはログに記録し、可能な限りデフォルト値や代替手段を提供すること。
2.  **MagicSanitizerEngine**: Gitパスやコマンド引数など、外部からの入力は必ず `MagicSanitizerEngine` を通してサニタイズし、セキュリティリスク（例: パストラバーサル、コマンドインジェクション）を排除すること。特にパスの正規化と不正文字の除去を徹底すること。
3.  **Job/Magicの定義規約**:
    *   **Magic (社会技術)**: Gitパス管理は、ゲームの外部連携を円滑にし、コンテンツの整合性を保つための「バージョン管理」という社会技術をゲームシステムに組み込むものです。これは、千年史シミュレーターが「歴史の記録と改変」を扱う上で、その基盤となる技術的側面を担い、外部知識の安全な取り込みを可能にします。
    *   **Job (生活職業)**: 直接的なジョブではないが、ゲーム開発者やMOD制作者が「コンテンツを管理し、ゲームに提供する」という役割を果たすための基盤技術として機能します。ゲーム内では、NPCが「外部情報源を同期する学者」や「歴史を編纂する書記」といったジョブを抽象的に実行する際のバックエンド技術として解釈できます。

## 実装指示

### 1. GitPathManager クラスの定義

`GitPathManager` クラスはシングルトンパターンで実装し、アプリケーション全体で一貫したGitパス管理を提供します。

```csharp
// ファイル: Core/ExternalTools/GitPathManager.cs
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using Game.Core.Logging; // 仮定されるロギングサービス
using Game.Core.Security; // MagicSanitizerEngine が含まれると仮定

namespace Game.Core.ExternalTools
{
    /// <summary>
    /// Git実行ファイルのパスを管理し、Gitコマンドの安全な実行を仲介するシングルトンクラス。
    /// Safe-Fail構造、MagicSanitizerEngine、Job/Magic規約を厳守します。
    /// </summary>
    public class GitPathManager
    {
        // シングルトンインスタンス
        private static GitPathManager _instance;
        private static readonly object _lock = new object();

        public static GitPathManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                        {
                            _instance = new GitPathManager();
                        }
                    }
                }
                return _instance;
            }
        }

        // イベント: Gitパスが変更されたときに発火
        public event Action<string> OnGitPathChanged;
        // イベント: Gitパスの自動検出に失敗したときに発火
        public event Action<string> OnGitDetectionFailed;

        // プロパティ: 現在設定されているGit実行ファイルのパス
        public string CurrentGitPath { get; private set; }
        // プロパティ: Gitが利用可能かどうか (パスが設定され、ファイルが存在するか)
        public bool IsGitAvailable => !string.IsNullOrEmpty(CurrentGitPath) && File.Exists(CurrentGitPath);

        // コンストラクタ (プライベート): シングルトンパターンを強制
        private GitPathManager()
        {
            // 初期化時にGitパスの自動検出を試みる
            DetectGitPath();
        }

        /// <summary>
        /// GitPathManagerを初期化し、Gitパスの検出を開始します。
        /// アプリケーション起動時に一度だけ呼び出すことを推奨します。
        /// </summary>
        public static void Initialize()
        {
            // Instanceプロパティへのアクセスにより、シングルトンインスタンスが作成され、コンストラクタが実行される
            _ = Instance; 
            GameLogger.LogInfo("GitPathManager", "GitPathManager initialized. Attempting to detect Git path.");
        }

        // ... (以下に詳細メソッドを記述)
    }
}
```

### 2. Gitパス検出ロジック (`DetectGitPath`)

アプリケーション起動時や、Gitパスが不明な場合に呼び出され、システムからGit実行ファイルを自動検出します。

#### 実装詳細
*   **優先順位**:
    1.  ユーザー設定（後述の `SetGitPath` で保存されたパスがあれば、それを最優先で読み込む。TODO: 設定システムとの連携）
    2.  環境変数 `PATH` からの検出
    3.  一般的なインストールパス（Windows, macOS, Linuxそれぞれ）
*   **Safe-Fail**: 検出に失敗した場合でもクラッシュせず、`OnGitDetectionFailed` イベントを発火させ、ログに記録します。`CurrentGitPath` は `null` または空文字列に設定されます。
*   **MagicSanitizerEngine**: 検出されたパスは `MagicSanitizerEngine.SanitizeFilePath()` を通して検証・サニタイズします。

```csharp
// GitPathManager.cs 内
private void DetectGitPath()
{
    // 1. ユーザー設定からの読み込み (TODO: 永続化された設定システムとの連携を実装)
    // string userConfiguredPath = LoadGitPathFromSettings(); // 仮定されるメソッド
    // if (!string.IsNullOrEmpty(userConfiguredPath))
    // {
    //     if (ValidateAndSetGitPath(userConfiguredPath, silent: true))
    //     {
    //         GameLogger.LogInfo("GitPathManager", $"Git path loaded from settings: {CurrentGitPath}");
    //         return;
    //     }
    //     else
    //     {
    //         GameLogger.LogWarning("GitPathManager", $"User configured Git path '{userConfiguredPath}' is invalid. Attempting automatic detection.");
    //     }
    // }

    // 2. 環境変数 PATH からの検出
    string gitExecutableName = Environment.OSVersion.Platform == PlatformID.Win32NT ? "git.exe" : "git";
    string pathEnv = Environment.GetEnvironmentVariable("PATH");
    if (!string.IsNullOrEmpty(pathEnv))
    {
        string[] paths = pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        foreach (string p in paths)
        {
            try
            {
                string potentialPath = Path.Combine(p, gitExecutableName);
                if (ValidateAndSetGitPath(potentialPath, silent: true))
                {
                    GameLogger.LogInfo("GitPathManager", $"Git path detected from PATH environment variable: {CurrentGitPath}");
                    return;
                }
            }
            catch (ArgumentException ex) // Path.Combine で不正なパスが渡された場合
            {
                GameLogger.LogWarning("GitPathManager", $"Invalid path component in PATH environment variable: '{p}'. Error: {ex.Message}");
            }
        }
    }

    // 3. 一般的なインストールパスからの検出
    List<string> commonPaths = new List<string>();
    if (Environment.OSVersion.Platform == PlatformID.Win32NT)
    {
        commonPaths.Add(@"C:\Program Files\Git\cmd\git.exe");
        commonPaths.Add(@"C:\Program Files (x86)\Git\cmd\git.exe");
        commonPaths.Add(@"C:\Program Files\Git\bin\git.exe"); // Git for Windows 2.x
        commonPaths.Add(@"C:\Program Files (x86)\Git\bin\git.exe");
    }
    else // Unix-like (macOS, Linux)
    {
        commonPaths.Add("/usr/bin/git");
        commonPaths.Add("/usr/local/bin/git");
        commonPaths.Add("/opt/homebrew/bin/git"); // Homebrew on Apple Silicon
        commonPaths.Add("/snap/bin/git"); // Snap installation
    }

    foreach (string p in commonPaths)
    {
        if (ValidateAndSetGitPath(p, silent: true))
        {
            GameLogger.LogInfo("GitPathManager", $"Git path detected from common installation paths: {CurrentGitPath}");
            return;
        }
    }

    // 検出失敗 (Safe-Fail)
    GameLogger.LogError("GitPathManager", "Failed to detect Git executable path automatically. Git-dependent features may be disabled.");
    OnGitDetectionFailed?.Invoke("Automatic detection failed. Please set Git path manually via settings.");
    CurrentGitPath = null; // 明示的にnullに設定し、Gitが利用不可であることを示す
}
```

### 3. パス検証と設定 (`ValidateAndSetGitPath`)

提供されたパスが有効なGit実行ファイルであるかを検証し、問題なければ `CurrentGitPath` に設定します。

#### 実装詳細
*   **MagicSanitizerEngine**: 入力パスは `MagicSanitizerEngine.SanitizeFilePath()` でサニタイズされます。これにより、不正な文字やパストラバーサル攻撃を防ぎます。
*   **ファイル存在チェック**: サニタイズ後のパスでファイルが存在するか確認します。
*   **実行可能ファイルチェック**: (簡易的) ファイルの拡張子や、`git --version` コマンドを実行して成功するかで確認します。これは最も確実な方法です。
*   **Safe-Fail**: 検証に失敗した場合、`false` を返し、適切なエラーメッセージをログに記録します。`CurrentGitPath` は変更されません。
*   **イベント**: パスが正常に設定された場合、`OnGitPathChanged` イベントを発火させます。

```csharp
// GitPathManager.cs 内
/// <summary>
/// 指定されたパスが有効なGit実行ファイルであるかを検証し、有効であればCurrentGitPathに設定します。
/// </summary>
/// <param name="potentialPath">検証するGit実行ファイルのパス。</param>
/// <param name="silent">ログ出力を抑制するかどうか。</param>
/// <returns>パスが有効で設定された場合はtrue、それ以外はfalse。</returns>
public bool ValidateAndSetGitPath(string potentialPath, bool silent = false)
{
    if (string.IsNullOrWhiteSpace(potentialPath))
    {
        if (!silent) GameLogger.LogWarning("GitPathManager", "Attempted to set an empty or null Git path.");
        return false;
    }

    // MagicSanitizerEngine によるパスのサニタイズ (厳守事項2)
    string sanitizedPath = MagicSanitizerEngine.SanitizeFilePath(potentialPath);

    if (!File.Exists(sanitizedPath))
    {
        if (!silent) GameLogger.LogWarning("GitPathManager", $"Git executable not found at: {sanitizedPath}");
        return false;
    }

    // 簡易的な実行可能ファイルチェック: git --version コマンドが成功するか (Safe-Fail構造)
    try
    {
        ProcessStartInfo startInfo = new ProcessStartInfo
        {
            FileName = sanitizedPath,
            Arguments = "--version",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using (Process process = Process.Start(startInfo))
        {
            if (process == null)
            {
                if (!silent) GameLogger.LogError("GitPathManager", $"Failed to start Git process for validation at: {sanitizedPath}");
                return false;
            }
            // タイムアウトを設定し、プロセスがハングアップするのを防ぐ (Safe-Fail)
            bool exited = process.WaitForExit(5000); // 5秒のタイムアウト
            if (!exited)
            {
                process.Kill(); // タイムアウトしたら強制終了
                if (!silent) GameLogger.LogWarning("GitPathManager", $"Git validation command timed out for path: {sanitizedPath}");
                return false;
            }

            if (process.ExitCode == 0)
            {
                string output = process.StandardOutput.ReadToEnd();
                if (output.Contains("git version")) // 出力内容でGitであることを確認
                {
                    if (CurrentGitPath != sanitizedPath)
                    {
                        CurrentGitPath = sanitizedPath;
                        if (!silent) GameLogger.LogInfo("GitPathManager", $"Git path successfully set to: {CurrentGitPath}");
                        OnGitPathChanged?.Invoke(CurrentGitPath);
                        // TODO: 設定システムにパスを保存 (SaveGitPathToSettings(CurrentGitPath);)
                    }
                    return true;
                }
            }
            if (!silent) GameLogger.LogWarning("GitPathManager", $"Git validation failed for path '{sanitizedPath}'. ExitCode: {process.ExitCode}, Error: {process.StandardError.ReadToEnd()}");
        }
    }
    catch (Exception ex) // プロセス起動失敗などの例外を捕捉 (Safe-Fail)
    {
        if (!silent) GameLogger.LogError("GitPathManager", $"Exception during Git path validation for '{sanitizedPath}': {ex.Message}");
    }

    return false;
}
```

### 4. Gitコマンド実行ヘルパー (`ExecuteGitCommand`)

設定されたGitパスを使用して任意のGitコマンドを実行します。

#### 実装詳細
*   **Safe-Fail**: コマンド実行中のエラー、タイムアウト、非ゼロ終了コードなどを適切に処理し、例外をスローせず、結果オブジェクトを返します。
*   **MagicSanitizerEngine**: `workingDirectory` は `MagicSanitizerEngine.SanitizeDirectoryPath()` でサニタイズします。コマンド引数自体はGitの複雑な構文を考慮し、呼び出し元での注意を促しますが、必要に応じて `MagicSanitizerEngine.SanitizeCommandArgument()` のようなメソッドを実装・適用することも検討してください。
*   **非同期実行**: UIスレッドをブロックしないよう、非同期で実行します。

```csharp
// GitPathManager.cs 内
/// <summary>
/// Gitコマンドの実行結果を格納するクラス。
/// </summary>
public class GitCommandResult
{
    public bool Success { get; set; } // コマンドが成功したか
    public int ExitCode { get; set; } // プロセスの終了コード
    public string StandardOutput { get; set; } // 標準出力
    public string StandardError { get; set; } // 標準エラー出力
    public string ErrorMessage { get; set; } // 実行中に発生したエラーメッセージ
}

/// <summary>
/// 設定されたGitパスを使用してGitコマンドを実行します。
/// </summary>
/// <param name="arguments">Gitコマンドの引数 (例: "status", "pull origin main")。</param>
/// <param name="workingDirectory">コマンドを実行する作業ディレクトリ。nullの場合は現在のディレクトリ。</param>
/// <returns>GitCommandResultオブジェクト。</returns>
public async Task<GitCommandResult> ExecuteGitCommand(string arguments, string workingDirectory = null)
{
    var result = new GitCommandResult { Success = false };

    if (!IsGitAvailable) // Gitが利用不可な場合のSafe-Fail
    {
        result.ErrorMessage = "Git executable is not available or path is not set. Cannot execute command.";
        GameLogger.LogError("GitPathManager", result.ErrorMessage);
        return result;
    }

    string sanitizedWorkingDirectory = workingDirectory;
    if (!string.IsNullOrEmpty(workingDirectory))
    {
        // MagicSanitizerEngine による作業ディレクトリのサニタイズ (厳守事項2)
        sanitizedWorkingDirectory = MagicSanitizerEngine.SanitizeDirectoryPath(workingDirectory);
        if (!Directory.Exists(sanitizedWorkingDirectory)) // ディレクトリが存在しない場合のSafe-Fail
        {
            result.ErrorMessage = $"Working directory does not exist or is invalid: {sanitizedWorkingDirectory}";
            GameLogger.LogError("GitPathManager", result.ErrorMessage);
            return result;
        }
    }

    try
    {
        ProcessStartInfo startInfo = new ProcessStartInfo
        {
            FileName = CurrentGitPath,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false, // シェルを使用しないことでセキュリティリスクを低減
            CreateNoWindow = true, // コマンドプロンプトウィンドウを表示しない
            WorkingDirectory = sanitizedWorkingDirectory ?? Directory.GetCurrentDirectory() // 作業ディレクトリを設定
        };

        using (Process process = new Process { StartInfo = startInfo })
        {
            process.Start();

            // 非同期で標準出力と標準エラー出力を読み取る
            Task<string> standardOutputTask = process.StandardOutput.ReadToEndAsync();
            Task<string> standardErrorTask = process.StandardError.ReadToEndAsync();

            // プロセスが終了するのを待つが、タイムアウトも設定 (Safe-Fail)
            await Task.WhenAny(process.WaitForExitAsync(), Task.Delay(30000)); // 30秒のタイムアウト

            if (!process.HasExited)
            {
                process.Kill(); // タイムアウトしたら強制終了
                result.ErrorMessage = $"Git command timed out after 30 seconds: git {arguments}";
                GameLogger.LogError("GitPathManager", result.ErrorMessage);
                return result;
            }

            // 出力結果を待機
            result.StandardOutput = await standardOutputTask;
            result.StandardError = await standardErrorTask;

            result.ExitCode = process.ExitCode;
            result.Success = process.ExitCode == 0; // 終了コード0が成功を示す

            if (!result.Success) // コマンドが失敗した場合のSafe-Fail
            {
                result.ErrorMessage = $"Git command failed with exit code {result.ExitCode}. Error: {result.StandardError.Trim()}";
                GameLogger.LogWarning("GitPathManager", $"Command: git {arguments}, WorkingDir: {sanitizedWorkingDirectory}, {result.ErrorMessage}");
            }
            else
            {
                GameLogger.LogInfo("GitPathManager", $"Git command successful: git {arguments}, WorkingDir: {sanitizedWorkingDirectory}");
            }
        }
    }
    catch (Exception ex) // プロセス起動失敗などの例外を捕捉 (Safe-Fail)
    {
        result.ErrorMessage = $"Exception executing Git command 'git {arguments}': {ex.Message}";
        GameLogger.LogError("GitPathManager", result.ErrorMessage);
    }

    return result;
}
```

### 5. 依存する仮定クラスの定義

`GameLogger` と `MagicSanitizerEngine` は、このプロンプトの範囲外ですが、機能するために必要です。以下に最小限の定義を示します。これらはプロジェクトの`Core/Logging`および`Core/Security`名前空間に配置されることを想定しています。

```csharp
// ファイル: Core/Logging/GameLogger.cs (仮定)
using System;

namespace Game.Core.Logging
{
    /// <summary>
    /// ゲーム全体のロギングサービス。Safe-Fail構造の一部として、エラーや警告を記録します。
    /// </summary>
    public static class GameLogger
    {
        public static void LogInfo(string source, string message) => Console.WriteLine($"[INFO][{source}] {message}");
        public static void LogWarning(string source, string message) => Console.WriteLine($"[WARN][{source}] {message}");
        public static void LogError(string source, string message) => Console.Error.WriteLine($"[ERROR][{source}] {message}");
        public static void LogDebug(string source, string message) => Console.WriteLine($"[DEBUG][{source}] {message}"); // 開発用
    }
}

// ファイル: Core/Security/MagicSanitizerEngine.cs (仮定)
using System.IO;
using System.Text.RegularExpressions;

namespace Game.Core.Security
{
    /// <summary>
    /// 外部からの入力をサニタイズし、セキュリティリスクを軽減するためのエンジン。
    /// MagicSanitizerEngineは、ゲームの「魔法=社会技術」の一部として、システムの安全性を保証します。
    /// </summary>
    public static class MagicSanitizerEngine
    {
        /// <summary>
        /// ファイルパスをサニタイズし、不正な文字やパストラバーサルを防ぎます。
        /// </summary>
        /// <param name="path">サニタイズするパス。</param>
        /// <returns>サニタイズされたパス。</returns>
        public static string SanitizeFilePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return string.Empty;

            // 1. パストラバーサル攻撃を防ぐための正規化
            // GetFullPathは相対パスや".."を解決し、絶対パスに変換します。
            // Uri.LocalPathはURIエンコードされた文字をデコードします。
            string normalizedPath;
            try
            {
                // WindowsとUnix-likeでパスの区切り文字を正規化
                normalizedPath = Path.GetFullPath(new Uri(path).LocalPath)
                                     .Replace('\\', Path.DirectorySeparatorChar)
                                     .Replace('/', Path.DirectorySeparatorChar);
            }
            catch (UriFormatException)
            {
                // 不正なURI形式の場合、そのままPath.GetFullPathを試みる
                normalizedPath = Path.GetFullPath(path)
                                     .Replace('\\', Path.DirectorySeparatorChar)
                                     .Replace('/', Path.DirectorySeparatorChar);
            }
            catch (Exception ex)
            {
                GameLogger.LogError("MagicSanitizerEngine", $"Failed to normalize path '{path}': {ex.Message}");
                return string.Empty; // Safe-Fail: サニタイズ失敗
            }


            // 2. 不正な文字を削除または置き換え
            // Path.GetInvalidPathChars() と Path.GetInvalidFileNameChars() を利用
            char[] invalidPathChars = Path.GetInvalidPathChars();
            char[] invalidFileChars = Path.GetInvalidFileNameChars();

            string sanitized = new string(normalizedPath.Where(c => 
                !invalidPathChars.Contains(c) && !invalidFileChars.Contains(c)
            ).ToArray());

            // 3. 複数のパス区切り文字の連続を単一に正規化 (例: "C:\\foo\\\bar" -> "C:\foo\bar")
            sanitized = Regex.Replace(sanitized, $"{Regex.Escape(Path.DirectorySeparatorChar.ToString())}{{2,}}", Path.DirectorySeparatorChar.ToString());

            // 4. ドライブレターやUNCパスの先頭の二重スラッシュは許可
            if (Environment.OSVersion.Platform == PlatformID.Win32NT && path.StartsWith(@"\\"))
            {
                // UNCパスの最初の二重スラッシュは保持
                if (!sanitized.StartsWith(@"\\"))
                {
                    sanitized = @"\\" + sanitized.TrimStart(Path.DirectorySeparatorChar);
                }
            }
            else if (path.StartsWith("//")) // Unix-likeのルートパス
            {
                 if (!sanitized.StartsWith("//"))
                {
                    sanitized = "/" + sanitized.TrimStart(Path.DirectorySeparatorChar);
                }
            }

            return sanitized;
        }

        /// <summary>
        /// ディレクトリパスをサニタイズし、不正な文字やパストラバーサルを防ぎます。
        /// </summary>
        /// <param name="path">サニタイズするパス。</param>
        /// <returns>サニタイズされたパス。</returns>
        public static string SanitizeDirectoryPath(string path)
        {
            // ディレクトリもファイルパスと同様のロジックでサニタイズ可能
            string sanitizedPath = SanitizeFilePath(path);
            
            // ディレクトリパスの末尾に区切り文字がない場合は追加 (オプション)
            // if (!string.IsNullOrEmpty(sanitizedPath) && !sanitizedPath.EndsWith(Path.DirectorySeparatorChar.ToString()))
            // {
            //     sanitizedPath += Path.DirectorySeparatorChar;
            // }
            return sanitizedPath;
        }

        // TODO: コマンド引数用のサニタイズメソッドも必要に応じて追加
        // Gitコマンドの引数は複雑なため、一般的なサニタイズでは不十分な場合がある。
        // 特定の引数パターンに対してのみ適用するか、呼び出し元で注意深く構築する必要がある。
        // public static string SanitizeCommandArgument(string arg) { ... }
    }
}
```

### 6. 使用例 (メインアプリケーションからの呼び出し)

これらのクラスがどのように連携し、ゲームアプリケーション内で利用されるかを示します。

```csharp
// メインアプリケーションの起動時など (例: Program.cs または GameBootstrapper.cs)
using Game.Core.Logging;
using Game.Core.ExternalTools;
using System;
using System.IO;
using System.Threading.Tasks;

public class GameApplication
{
    public async Task StartGameAsync()
    {
        GameLogger.LogInfo("GameApplication", "Starting game initialization...");

        // GitPathManagerの初期化 (シングルトンインスタンスが作成され、自動検出が開始される)
        GameLogger.LogInfo("GameApplication", "Initializing GitPathManager...");
        GitPathManager.Initialize();

        // Gitパス変更イベントの購読
        GitPathManager.Instance.OnGitPathChanged += (path) =>
        {
            GameLogger.LogInfo("GameApplication", $"Git path updated to: {path}. Git-dependent features are now enabled.");
            // UIの更新や、Git連携機能の有効化など、ゲーム内の状態を更新
        };

        // Git検出失敗イベントの購読
        GitPathManager.Instance.OnGitDetectionFailed += (message) =>
        {
            GameLogger.LogError("GameApplication", $"Git detection failed: {message}. Please configure manually in game settings.");
            // ユーザーに手動設定を促すUIを表示するなど、ゲーム内の状態を更新
        };

        // Gitパスの自動検出が完了するのを待つ (非同期処理ではないため、すぐに結果が得られる)
        if (GitPathManager.Instance.IsGitAvailable)
        {
            GameLogger.LogInfo("GameApplication", $"Git is available at: {GitPathManager.Instance.CurrentGitPath}");
            
            // 例: Gitバージョンを取得 (非同期実行)
            GameLogger.LogInfo("GameApplication", "Attempting to get Git version...");
            var versionResult = await GitPathManager.Instance.ExecuteGitCommand("--version");
            if (versionResult.Success)
            {
                GameLogger.LogInfo("GameApplication", $"Git version: {versionResult.StandardOutput}");
            }
            else
            {
                GameLogger.LogError("GameApplication", $"Failed to get Git version: {versionResult.ErrorMessage}");
            }

            // 例: 特定のディレクトリで git status を実行 (MOD管理の例)
            string modRepositoryPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "GameData", "Mods");
            if (Directory.Exists(modRepositoryPath))
            {
                GameLogger.LogInfo("GameApplication", $"Checking Git status for Mod repository at: {modRepositoryPath}");
                var statusResult = await GitPathManager.Instance.ExecuteGitCommand("status", modRepositoryPath);
                if (statusResult.Success)
                {
                    GameLogger.LogInfo("GameApplication", $"Git status for Mods: {statusResult.StandardOutput}");
                }
                else
                {
                    GameLogger.LogError("GameApplication", $"Failed to get Git status for Mods: {statusResult.ErrorMessage}");
                }
            }
            else
            {
                GameLogger.LogWarning("GameApplication", $"Mod repository directory not found: {modRepositoryPath}. Skipping Git status check.");
            }
        }
        else
        {
            GameLogger.LogWarning("GameApplication", "Git is not available. Some features (e.g., MOD auto-update, external data sync for simulator) might be disabled.");
        }

        GameLogger.LogInfo("GameApplication", "Game initialization complete.");
    }

    /// <summary>
    /// ユーザーがゲーム設定UIから手動でGitパスを設定する際のハンドラを想定。
    /// </summary>
    /// <param name="userProvidedPath">ユーザーが入力したGit実行ファイルのパス。</param>
    public void OnUserSetGitPath(string userProvidedPath)
    {
        GameLogger.LogInfo("GameApplication", $"User attempting to set Git path to: {userProvidedPath}");
        if (GitPathManager.Instance.ValidateAndSetGitPath(userProvidedPath))
        {
            GameLogger.LogInfo("GameApplication", $"User successfully set Git path to: {GitPathManager.Instance.CurrentGitPath}");
            // UIを更新して成功を通知し、Git連携機能を有効化
        }
        else
        {
            GameLogger.LogError("GameApplication", $"User provided invalid Git path: {userProvidedPath}. Please check the path and try again.");
            // UIを更新して失敗を通知し、エラーメッセージを表示
        }
    }

    // アプリケーションのエントリポイント (例)
    public static async Task Main(string[] args)
    {
        GameApplication app = new GameApplication();
        await app.StartGameAsync();
        Console.WriteLine("\nPress any key to exit...");
        Console.ReadKey();
    }
}
```
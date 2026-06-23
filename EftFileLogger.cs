using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text;

namespace DynamicsGP_EFT_Worker;

/// <summary>
/// Writes human-readable log files to the Logs folder.
/// 
/// File layout:
///   {LogFolder}\
///     eft_YYYY-MM-DD.log          ← combined daily log (all events)
///     success\
///       success_YYYY-MM-DD.log    ← successful operations only
///     failed\
///       failed_YYYY-MM-DD.log     ← failures and warnings only
///
/// Every line is prefixed with a timestamp and severity tag so the
/// files are easy to read in Notepad, tail, or any log viewer.
/// </summary>
public sealed class EftFileLogger : IDisposable
{
    private readonly ILogger<EftFileLogger> _logger;
    private readonly EftOptions _options;

    // One write-lock per physical file path to prevent corruption
    // if the service ever processes files in parallel in future.
    private static readonly Dictionary<string, SemaphoreSlim> _locks = new();
    private static readonly object _lockDictLock = new();

    public EftFileLogger(ILogger<EftFileLogger> logger, IOptions<EftOptions> options)
    {
        _logger = logger;
        _options = options.Value;
    }

    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>Logs a successfully processed EFT file.</summary>
    public Task LogSuccessAsync(string sourceFile, string outputFile, bool headerFixed, bool dateFixed)
    {
        string message =
            $"SUCCESS | Source: {sourceFile} | Output: {outputFile} | " +
            $"HeaderFixed: {headerFixed} | DateInjected: {dateFixed}";

        return WriteAsync(LogCategory.Success, message);
    }

    /// <summary>Logs a file that was skipped because it was locked.</summary>
    public Task LogSkippedLockedAsync(string sourceFile)
    {
        string message = $"SKIPPED | File locked (GP still writing): {sourceFile}";
        return WriteAsync(LogCategory.Info, message);
    }

    /// <summary>Logs a warning — file processed but with a concern.</summary>
    public Task LogWarningAsync(string sourceFile, string detail)
    {
        string message = $"WARNING | {detail} | File: {sourceFile}";
        return WriteAsync(LogCategory.Warning, message);
    }

    /// <summary>Logs a failed file processing operation.</summary>
    public Task LogFailureAsync(string sourceFile, Exception ex)
    {
        string message =
            $"FAILED  | File: {sourceFile}{Environment.NewLine}" +
            $"          Error: {ex.GetType().Name}: {ex.Message}{Environment.NewLine}" +
            $"          Stack: {ex.StackTrace?.Split(Environment.NewLine).FirstOrDefault()?.Trim()}";

        return WriteAsync(LogCategory.Failure, message);
    }

    /// <summary>Logs a folder creation event.</summary>
    public Task LogFolderCreatedAsync(string folder)
    {
        string message = $"SETUP   | Created folder: {folder}";
        return WriteAsync(LogCategory.Info, message);
    }

    /// <summary>Logs a folder creation failure.</summary>
    public Task LogFolderFailedAsync(string folder, Exception ex)
    {
        string message =
            $"FAILED  | Could not create folder: {folder}{Environment.NewLine}" +
            $"          Error: {ex.GetType().Name}: {ex.Message}";
        return WriteAsync(LogCategory.Failure, message);
    }

    /// <summary>Logs service startup.</summary>
    public Task LogStartupAsync(string inputFolder, string outputFolder, string logFolder, int intervalSeconds)
    {
        string message =
            $"STARTUP | Service started{Environment.NewLine}" +
            $"          Input  : {inputFolder}{Environment.NewLine}" +
            $"          Output : {outputFolder}{Environment.NewLine}" +
            $"          Logs   : {logFolder}{Environment.NewLine}" +
            $"          Interval: {intervalSeconds}s";
        return WriteAsync(LogCategory.Info, message);
    }

    /// <summary>Logs service shutdown.</summary>
    public Task LogShutdownAsync()
    {
        return WriteAsync(LogCategory.Info, "STOP    | Service stopped.");
    }

    /// <summary>Logs a scan cycle summary.</summary>
    public Task LogScanAsync(int found, int processed, int skipped, int failed)
    {
        string message =
            $"SCAN    | Files found: {found} | Processed: {processed} | Skipped: {skipped} | Failed: {failed}";
        return WriteAsync(LogCategory.Info, message);
    }

    // ── Core write logic ───────────────────────────────────────────────────────

    private enum LogCategory { Info, Success, Warning, Failure }

    private async Task WriteAsync(LogCategory category, string message)
    {
        string today     = DateTime.Now.ToString("yyyy-MM-dd");
        string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        string logFolder = _options.LogFolder;

        // Paths
        string combinedPath = Path.Combine(logFolder, $"eft_{today}.log");
        string? categoryPath = category switch
        {
            LogCategory.Success => Path.Combine(logFolder, "success", $"success_{today}.log"),
            LogCategory.Failure => Path.Combine(logFolder, "failed",  $"failed_{today}.log"),
            LogCategory.Warning => Path.Combine(logFolder, "failed",  $"failed_{today}.log"),
            _                   => null
        };

        string line = $"[{timestamp}] {message}";

        // Ensure log directories exist
        EnsureLogFolder(logFolder);
        if (categoryPath is not null)
            EnsureLogFolder(Path.GetDirectoryName(categoryPath)!);

        // Write to combined log and category log
        await AppendLineAsync(combinedPath, line);
        if (categoryPath is not null)
            await AppendLineAsync(categoryPath, line);
    }

    private static void EnsureLogFolder(string path)
    {
        if (!Directory.Exists(path))
        {
            try { Directory.CreateDirectory(path); }
            catch { /* best-effort; if logs can't be written we shouldn't crash the service */ }
        }
    }

    private static async Task AppendLineAsync(string filePath, string line)
    {
        SemaphoreSlim sem = GetLock(filePath);
        await sem.WaitAsync();
        try
        {
            await File.AppendAllTextAsync(filePath, line + Environment.NewLine, Encoding.UTF8);
        }
        catch
        {
            // Never let a logging failure crash the worker
        }
        finally
        {
            sem.Release();
        }
    }

    private static SemaphoreSlim GetLock(string filePath)
    {
        lock (_lockDictLock)
        {
            if (!_locks.TryGetValue(filePath, out SemaphoreSlim? sem))
            {
                sem = new SemaphoreSlim(1, 1);
                _locks[filePath] = sem;
            }
            return sem;
        }
    }

    public void Dispose()
    {
        lock (_lockDictLock)
        {
            foreach (var sem in _locks.Values) sem.Dispose();
            _locks.Clear();
        }
    }
}

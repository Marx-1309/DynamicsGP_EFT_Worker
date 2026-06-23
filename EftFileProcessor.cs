using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DynamicsGP_EFT_Worker;

/// <summary>Result of a single file processing attempt.</summary>
public enum ProcessResult { Success, Skipped, Failed }

/// <summary>
/// Applies the two EFT transformations required by Dynamics GP output:
///   1. Fixes the BInSol header casing  (BINSOL - U VER 1.00 → BInSol - U ver 1.00)
///   2. Replaces the EFTDATE placeholder with today's date (YYYY-MM-DD)
///
/// Output: EFT_&lt;originalname&gt;.csv written to OutputFolder.
/// Source: moved to InputFolder\Archived\YYYY-MM-DD\ (or deleted if archiving off).
/// </summary>
public sealed class EftFileProcessor
{
    private const string OldHeader      = "BINSOL - U VER 1.00";
    private const string NewHeader      = "BInSol - U ver 1.00";
    private const string DatePlaceholder = "EFTDATE";

    private readonly ILogger<EftFileProcessor> _logger;
    private readonly EftOptions     _options;
    private readonly EftFileLogger  _fileLogger;

    public EftFileProcessor(
        ILogger<EftFileProcessor> logger,
        IOptions<EftOptions> options,
        EftFileLogger fileLogger)
    {
        _logger     = logger;
        _options    = options.Value;
        _fileLogger = fileLogger;
    }

    public async Task<ProcessResult> ProcessFileAsync(string filePath, CancellationToken ct)
    {
        string fileName = Path.GetFileName(filePath);

        // ── Guard: skip files still locked by GP ──────────────────────────────
        if (IsFileLocked(filePath))
        {
            _logger.LogDebug("Skipping locked file: {File}", fileName);
            await _fileLogger.LogSkippedLockedAsync(fileName);
            return ProcessResult.Skipped;
        }

        _logger.LogInformation("Processing: {File}", fileName);

        try
        {
            // ── 1. Read ────────────────────────────────────────────────────────
            string content = await File.ReadAllTextAsync(filePath, ct);

            // ── 2. Transform ───────────────────────────────────────────────────
            string today = DateTime.Now.ToString("yyyy-MM-dd");

            bool headerFixed = content.Contains(OldHeader,       StringComparison.Ordinal);
            bool dateFixed   = content.Contains(DatePlaceholder, StringComparison.Ordinal);

            if (!headerFixed && !dateFixed)
            {
                string warn = "Neither placeholder (BINSOL header / EFTDATE) was found — file written as-is";
                _logger.LogWarning("{Warn}: {File}", warn, fileName);
                await _fileLogger.LogWarningAsync(fileName, warn);
            }

            string transformed = content
                .Replace(OldHeader,       NewHeader, StringComparison.Ordinal)
                .Replace(DatePlaceholder, today,     StringComparison.Ordinal);

            // ── 3. Write output ────────────────────────────────────────────────
            // Ensure output folder exists (defensive — EnsureAllFolders ran at startup)
            Directory.CreateDirectory(_options.OutputFolder);

            string outputName = $"EFT_{Path.GetFileNameWithoutExtension(fileName)}.csv";
            string outputPath = GetUniqueOutputPath(Path.Combine(_options.OutputFolder, outputName));

            await File.WriteAllTextAsync(outputPath, transformed, ct);

            _logger.LogInformation(
                "Written → {Out}  (headerFixed={H}, dateInjected={D})",
                Path.GetFileName(outputPath), headerFixed, dateFixed);

            await _fileLogger.LogSuccessAsync(fileName, Path.GetFileName(outputPath), headerFixed, dateFixed);

            // ── 4. Archive / delete source ─────────────────────────────────────
            ArchiveSourceFile(filePath);

            return ProcessResult.Success;
        }
        catch (OperationCanceledException)
        {
            throw; // propagate; host handles graceful shutdown
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process file: {File}", fileName);
            await _fileLogger.LogFailureAsync(fileName, ex);
            return ProcessResult.Failed;
        }
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private void ArchiveSourceFile(string filePath)
    {
        string fileName = Path.GetFileName(filePath);

        if (!_options.ArchiveProcessedFiles)
        {
            try   { File.Delete(filePath); }
            catch (Exception ex) { _logger.LogWarning(ex, "Could not delete source: {File}", fileName); }
            return;
        }

        try
        {
            string archiveDir = Path.Combine(
                _options.InputFolder, "Archived", DateTime.Now.ToString("yyyy-MM-dd"));

            Directory.CreateDirectory(archiveDir);

            string dest = GetUniqueOutputPath(Path.Combine(archiveDir, fileName));
            File.Move(filePath, dest);

            _logger.LogDebug("Archived {File} → {Dest}", fileName, Path.GetFileName(dest));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Archive failed for {File}; deleting instead.", fileName);
            try { File.Delete(filePath); } catch { /* best-effort */ }
        }
    }

    /// <summary>Appends _1, _2 … to avoid overwriting an existing output file.</summary>
    private static string GetUniqueOutputPath(string path)
    {
        if (!File.Exists(path)) return path;

        string dir  = Path.GetDirectoryName(path)!;
        string stem = Path.GetFileNameWithoutExtension(path);
        string ext  = Path.GetExtension(path);
        int    n    = 1;

        string candidate;
        do { candidate = Path.Combine(dir, $"{stem}_{n++}{ext}"); }
        while (File.Exists(candidate));

        return candidate;
    }

    /// <summary>Returns true if another process has an exclusive lock (GP still writing).</summary>
    private static bool IsFileLocked(string filePath)
    {
        try
        {
            using FileStream fs = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.None);
            return false;
        }
        catch (IOException) { return true; }
    }
}

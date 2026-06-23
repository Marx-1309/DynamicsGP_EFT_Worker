using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DynamicsGP_EFT_Worker;

public sealed class EftWorkerService : BackgroundService
{
    private readonly ILogger<EftWorkerService> _logger;
    private readonly EftOptions _options;
    private readonly EftFileProcessor _processor;
    private readonly EftFileLogger _fileLogger;

    public EftWorkerService(
        ILogger<EftWorkerService> logger,
        IOptions<EftOptions> options,
        EftFileProcessor processor,
        EftFileLogger fileLogger)
    {
        _logger     = logger;
        _options    = options.Value;
        _processor  = processor;
        _fileLogger = fileLogger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // ── 1. Create all required folders before doing anything else ──────────
        await EnsureAllFoldersAsync();

        // ── 2. Log startup ─────────────────────────────────────────────────────
        _logger.LogInformation(
            "DynamicsGP EFT Worker started | Input: {In} | Output: {Out} | Logs: {Log} | Interval: {Sec}s",
            _options.InputFolder, _options.OutputFolder, _options.LogFolder, _options.PollIntervalSeconds);

        await _fileLogger.LogStartupAsync(
            _options.InputFolder, _options.OutputFolder, _options.LogFolder, _options.PollIntervalSeconds);

        // ── 3. Run once immediately on startup, then every N seconds ───────────
        await ProcessPendingFilesAsync(stoppingToken);

        using PeriodicTimer timer = new(TimeSpan.FromSeconds(_options.PollIntervalSeconds));

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await ProcessPendingFilesAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("DynamicsGP EFT Worker stopping.");
            await _fileLogger.LogShutdownAsync();
        }
    }

    // ── Folder setup ───────────────────────────────────────────────────────────

    /// <summary>
    /// Creates every folder the service needs. Missing folders are created;
    /// errors are logged to both the console logger and the file logger, but
    /// they do NOT abort startup — the service will retry on each scan cycle.
    /// </summary>
    private async Task EnsureAllFoldersAsync()
    {
        // Core folders
        var folders = new List<string>
        {
            _options.InputFolder,
            _options.OutputFolder,
            _options.LogFolder,
            Path.Combine(_options.LogFolder, "success"),
            Path.Combine(_options.LogFolder, "failed"),
        };

        // Archive folder tree (input\Archived)
        if (_options.ArchiveProcessedFiles)
            folders.Add(Path.Combine(_options.InputFolder, "Archived"));

        foreach (string folder in folders)
        {
            if (Directory.Exists(folder)) continue;

            try
            {
                Directory.CreateDirectory(folder);
                _logger.LogInformation("Created folder: {Folder}", folder);
                await _fileLogger.LogFolderCreatedAsync(folder);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Could not create folder: {Folder}", folder);
                await _fileLogger.LogFolderFailedAsync(folder, ex);
            }
        }
    }

    // ── Scan cycle ─────────────────────────────────────────────────────────────

    private async Task ProcessPendingFilesAsync(CancellationToken ct)
    {
        // Re-ensure the input folder exists each cycle (e.g. network drive reconnect)
        if (!Directory.Exists(_options.InputFolder))
        {
            _logger.LogWarning("Input folder missing, attempting to create: {Folder}", _options.InputFolder);
            await EnsureAllFoldersAsync();

            if (!Directory.Exists(_options.InputFolder))
            {
                _logger.LogError("Input folder still unavailable — skipping this scan cycle.");
                return;
            }
        }

        string[] files;
        try
        {
            files = Directory.GetFiles(_options.InputFolder, "*.*", SearchOption.TopDirectoryOnly);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to enumerate input folder: {Folder}", _options.InputFolder);
            return;
        }

        if (files.Length == 0)
        {
            _logger.LogDebug("No files in {Folder}", _options.InputFolder);
            return;
        }

        _logger.LogInformation("Scan: {Count} file(s) found.", files.Length);

        int processed = 0, skipped = 0, failed = 0;

        foreach (string filePath in files)
        {
            if (ct.IsCancellationRequested) break;

            var result = await _processor.ProcessFileAsync(filePath, ct);

            switch (result)
            {
                case ProcessResult.Success:  processed++; break;
                case ProcessResult.Skipped:  skipped++;   break;
                case ProcessResult.Failed:   failed++;    break;
            }
        }

        await _fileLogger.LogScanAsync(files.Length, processed, skipped, failed);

        _logger.LogInformation(
            "Scan complete — processed: {P}, skipped: {S}, failed: {F}",
            processed, skipped, failed);
    }
}

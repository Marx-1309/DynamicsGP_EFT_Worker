using System.ComponentModel.DataAnnotations;

namespace DynamicsGP_EFT_Worker;

/// <summary>
/// Configuration options for the EFT worker service.
/// Bound from the "EftWorker" section of appsettings.json.
/// </summary>
public sealed class EftOptions
{
    public const string SectionName = "EftWorker";

    /// <summary>
    /// Folder that Dynamics GP drops raw EFT files into.
    /// </summary>
    [Required]
    public string InputFolder { get; set; } = @"C:\GPDATA\EFT\GP_EFT";

    /// <summary>
    /// Folder where the corrected EFT_*.csv files are written.
    /// </summary>
    [Required]
    public string OutputFolder { get; set; } = @"C:\GPDATA\EFT";

    /// <summary>
    /// How often (in seconds) to scan the input folder. Default: 30.
    /// </summary>
    [Range(5, 86400)]
    public int PollIntervalSeconds { get; set; } = 10;

    /// <summary>
    /// When true, processed source files are moved to an Archived\YYYY-MM-DD
    /// sub-folder instead of being deleted.
    /// </summary>
    public bool ArchiveProcessedFiles { get; set; } = true;

    /// <summary>
    /// Root folder for log output files.
    /// Sub-folders "success\" and "failed\" are created automatically.
    /// Defaults to a "Logs" folder alongside the output folder.
    /// </summary>
    [Required]
    public string LogFolder { get; set; } = @"C:\GPDATA\EFT\Logs";
}

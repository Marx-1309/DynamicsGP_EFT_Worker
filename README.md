# DynamicsGP EFT Worker Service

A .NET 8 Worker Service that replaces the legacy VB.NET Windows Service.  
Polls the Dynamics GP EFT output folder every 30 seconds, transforms the files, and writes a full log trail.

---

## What it does

1. **Creates all required folders on startup** — input, output, logs, archives. No manual folder setup needed.
2. **Watches** `InputFolder` every 30 seconds (configurable).
3. For every file found it:
   - Skips files still locked by GP (picked up next cycle)
   - Fixes the BInSol header: `BINSOL - U VER 1.00` → `BInSol - U ver 1.00`
   - Injects today's date: `EFTDATE` → `YYYY-MM-DD`
   - Writes output to `OutputFolder` as `EFT_<originalname>.csv`
   - Archives the source to `InputFolder\Archived\YYYY-MM-DD\`
4. **Writes log files** (see Log Files below).

---

## Configuration (`appsettings.json`)

```json
{
  "EftWorker": {
    "InputFolder":           "D:\\GPDATA\\EFT\\GP_EFT",
    "OutputFolder":          "D:\\GPDATA\\EFT",
    "LogFolder":             "D:\\GPDATA\\EFT\\Logs",
    "PollIntervalSeconds":   30,
    "ArchiveProcessedFiles": true
  }
}
```

| Key | Default | Description |
|---|---|---|
| `InputFolder` | `D:\GPDATA\EFT\GP_EFT` | GP drops raw EFT files here |
| `OutputFolder` | `D:\GPDATA\EFT` | Corrected `EFT_*.csv` files written here |
| `LogFolder` | `D:\GPDATA\EFT\Logs` | Root folder for all log output files |
| `PollIntervalSeconds` | `30` | Scan frequency (min 5s) |
| `ArchiveProcessedFiles` | `true` | Move source to `Archived\` instead of deleting |

---

## Log Files

All folders are created automatically. Log files roll over daily.

```
D:\GPDATA\EFT\Logs\
  eft_2026-06-23.log            ← combined daily log (every event)
  success\
    success_2026-06-23.log      ← successful operations only
  failed\
    failed_2026-06-23.log       ← failures and warnings only
```

### Sample combined log

```
[2026-06-23 07:00:01] STARTUP | Service started
                                Input  : D:\GPDATA\EFT\GP_EFT
                                Output : D:\GPDATA\EFT
                                Logs   : D:\GPDATA\EFT\Logs
                                Interval: 30s
[2026-06-23 07:00:01] SETUP   | Created folder: D:\GPDATA\EFT\GP_EFT
[2026-06-23 07:00:01] SETUP   | Created folder: D:\GPDATA\EFT\Logs\success
[2026-06-23 07:00:31] SCAN    | Files found: 2 | Processed: 1 | Skipped: 1 | Failed: 0
[2026-06-23 07:00:31] SUCCESS | Source: payment_run.txt | Output: EFT_payment_run.csv | HeaderFixed: True | DateInjected: True
[2026-06-23 07:00:31] SKIPPED | File locked (GP still writing): payment_run2.txt
[2026-06-23 07:01:01] SCAN    | Files found: 1 | Processed: 1 | Skipped: 0 | Failed: 0
[2026-06-23 07:01:01] SUCCESS | Source: payment_run2.txt | Output: EFT_payment_run2.csv | HeaderFixed: True | DateInjected: True
```

### Sample failed log

```
[2026-06-23 08:15:44] FAILED  | File: bad_file.txt
                                Error: UnauthorizedAccessException: Access to the path is denied.
                                Stack: at System.IO.File.ReadAllTextAsync(...)
```

---

## Build

```bash
dotnet build

# Self-contained single .exe for deployment
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o ./publish
```

---

## Install as a Windows Service

Run in an **elevated** (Administrator) prompt:

```cmd
sc create "DynamicsGP EFT Worker" ^
   binPath= "C:\Services\DynamicsGP_EFT_Worker\DynamicsGP_EFT_Worker.exe" ^
   start= auto ^
   DisplayName= "DynamicsGP EFT Worker"

sc description "DynamicsGP EFT Worker" "Monitors the GP EFT output folder and prepares files for bank submission."

sc start "DynamicsGP EFT Worker"
```

## Remove the Windows Service

```cmd
sc stop "DynamicsGP EFT Worker"
sc delete "DynamicsGP EFT Worker"
```

---

## Run locally (development)

```bash
# Uses appsettings.Development.json (C:\TESTDATA paths, 10s interval, no archiving)
set ASPNETCORE_ENVIRONMENT=Development
dotnet run
```

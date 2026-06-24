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

## Full Deployment Guide

### Step 1 — Publish the application

Open a command prompt in the project folder and run:

```cmd
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o C:\Services\DynamicsGP_EFT_Worker
```

This produces a single `.exe` with the .NET runtime bundled — no separate runtime installation needed on the server.

---

### Step 2 — Install the Windows Service

Open **Command Prompt as Administrator** and run:

```cmd
sc create "DynamicsGP EFT Worker" ^
   binPath= "C:\Services\DynamicsGP_EFT_Worker\DynamicsGP_EFT_Worker.exe" ^
   start= auto ^
   DisplayName= "DynamicsGP EFT Worker"

sc description "DynamicsGP EFT Worker" "Monitors the GP EFT output folder and prepares files for bank submission."
```

> **Note:** The `start= auto` flag ensures the service starts automatically every time Windows boots — no manual intervention needed after a restart.

---

### Step 3 — Configure auto-recovery on crash

This tells Windows to restart the service automatically if it ever fails:

```cmd
sc failure "DynamicsGP EFT Worker" reset= 86400 actions= restart/5000/restart/10000/restart/30000
```

| Failure | Wait before restart |
|---|---|
| 1st | 5 seconds |
| 2nd | 10 seconds |
| 3rd | 30 seconds |
| Counter resets after | 24 hours |

---

### Step 4 — Start the service

```cmd
sc start "DynamicsGP EFT Worker"
```

---

### Step 5 — Verify it is running

```cmd
sc query "DynamicsGP EFT Worker"
```

You should see `STATE: 4 RUNNING`. The service will also have created the log folders and written a `STARTUP` entry in the daily log file.

---

### What happens when the machine restarts?

Because the service is installed with `start= auto`, Windows starts it automatically during boot — before anyone logs in. The sequence is:

1. Windows boots and the Service Control Manager starts all `Automatic` services
2. The service launches, creates any missing folders, and writes a `STARTUP` log entry
3. The first scan runs immediately, then every 30 seconds from that point on

No manual action is ever needed after a reboot.

> **If folders are on a network share** (e.g. `\\finsrv02\GPDATA\...`), the service must run under a domain account that has network access, not the default Local System account. Set this under **Services → Properties → Log On**, or run:
> ```cmd
> sc config "DynamicsGP EFT Worker" obj= "DOMAIN\ServiceAccount" password= "YourPassword"
> ```

---

### Managing the service

| Task | Command (run as Administrator) |
|---|---|
| Start | `sc start "DynamicsGP EFT Worker"` |
| Stop | `sc stop "DynamicsGP EFT Worker"` |
| Restart | `sc stop "DynamicsGP EFT Worker" && sc start "DynamicsGP EFT Worker"` |
| Check status | `sc query "DynamicsGP EFT Worker"` |
| Verify auto-start | `sc qc "DynamicsGP EFT Worker"` — look for `START_TYPE: 2 AUTO_START` |

You can also manage it from **Services** (`services.msc`) in Windows — right-click the service to start, stop, or restart it.

---

### Updating to a new version

```cmd
sc stop "DynamicsGP EFT Worker"

dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o C:\Services\DynamicsGP_EFT_Worker

sc start "DynamicsGP EFT Worker"
```

---

### Checking logs after deployment

```powershell
# Windows Event Log (startup, crash entries)
Get-EventLog -LogName Application -Source "DynamicsGP EFT Worker" -Newest 20
```

Or open the log files directly:

```
D:\GPDATA\EFT\Logs\eft_YYYY-MM-DD.log         ← combined log
D:\GPDATA\EFT\Logs\success\success_YYYY-MM-DD.log
D:\GPDATA\EFT\Logs\failed\failed_YYYY-MM-DD.log
```

---

## Run locally (development)

```bash
# Uses appsettings.Development.json (C:\TESTDATA paths, 10s interval, no archiving)
set ASPNETCORE_ENVIRONMENT=Development
dotnet run
```

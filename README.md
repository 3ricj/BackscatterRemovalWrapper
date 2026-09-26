# BsxtBatch

Batch-runs the Photoshop **BSXT** action on a pile of image files and exports
`<name>-BSXT.<ext>` next to each original. Originals are never modified, and
existing files are **never overwritten**.

Drop files (or whole folders) onto the window, press **Run BSXT Batch**, done.

## Requirements

> **⚠ The BSXT filter must be installed in Photoshop first.**
> This program does **not** include the BSXT action — it only automates it. Without
> the action installed and loaded, the batch cannot do anything and will abort with
> a clear error.

- **Windows** with **Adobe Photoshop** installed (developed and tested on Photoshop 27 / v27.10.0).
- **The BSXT filter/action must be installed in Photoshop**: the **`BSXT` action set**,
  containing the action **`BSXT_ACTION (Click Here)`**, must be loaded (Actions panel →
  Load Actions… if missing). If you have not installed the BSXT action in Photoshop,
  obtain it first and load it — BsxtBatch will not work without it.
  Photoshop 27 removed COM/ExtendScript *enumeration* of actions, so the app cannot
  pre-check this — if the set is missing the batch aborts with a clear message.
- Write permission in each source folder (output goes beside the source).

## Running

Prebuilt (self-contained, no .NET install needed):

    publish\BsxtBatch\BsxtBatch.App.exe

From source (.NET 9 SDK):

    dotnet run --project src/BsxtBatch.App

## Installer

Build a Windows installer (per-user, no admin rights required):

    powershell -ExecutionPolicy Bypass -File installer\build-installer.ps1 -Version 1.0.0

The script publishes the app as a self-contained single-file win-x64 exe, then compiles
`installer\BsxtBatch.iss` with Inno Setup, producing:

    installer\Output\BsxtBatch-Setup-<version>.exe   (~42 MB)

Installing puts the app in `%LOCALAPPDATA%\Programs\BsxtBatch`, adds a Start Menu entry,
an optional Desktop shortcut, and an **Apps & features** uninstall entry. No UAC prompt.
Uninstall from Apps & features, or run silently:

    "%LOCALAPPDATA%\Programs\BsxtBatch\unins000.exe" /VERYSILENT

Building the installer requires **Inno Setup 6** (install once:
`winget install --id JRSoftware.InnoSetup -e`). The `AppId` GUID in `BsxtBatch.iss` must
stay constant across releases so upgrades replace rather than side-load.

## What it does

For every file: `Open` → guard checks → `DoAction("BSXT_ACTION (Click Here)", "BSXT")`
→ `SaveAs(<name>-BSXT.tif|jpg, asCopy: true)` → `Close(discard)`.

| Input | Output |
|---|---|
| `.jpg` / `.jpeg` | `<name>-BSXT.jpg` (quality 12, embedded profile) |
| `.tif/.tiff`, `.psd/.psb` | `<name>-BSXT.tif` (LZW, flattened, embedded profile) |
| RAW (`.arw .cr2 .cr3 .nef .raf .orf .rw2 .dng` …) | `<name>-BSXT.tif` (via Camera Raw) |

Dialogs are suppressed (`DisplayDialogs = psDisplayNoDialogs`) so the batch never hangs;
a COM message filter transparently retries Photoshop's "application busy" rejections.

### Files that are skipped (deliberately)

- **Unsupported types** (anything not in the table above).
- **Names already ending in `-BSXT`** — these look like the tool's own outputs.
- **Outputs that already exist.** If `<name>-BSXT.tif` (or `.jpg`) is already present
  next to the source, the file is skipped — the tool **never overwrites existing files**.
  Delete or rename the existing output if you want to re-process the source.
- **Documents that already contain `BSXT` or `Cleanup` layers.** The action is not
  idempotent: on a document that was already processed (or otherwise contains those
  layers), its recorded `Make → mask channel → Reveal All` step fails with
  *"The command 'Make' is not available"*. The tool detects these layers after opening
  and skips the file with a warning instead of failing. Run BSXT only on un-edited files.

## Project layout

    src/BsxtBatch.Core/      Photoshop COM automation, batch runner, naming/classification
    src/BsxtBatch.App/       WPF UI (job list, drag-and-drop, progress, log)
    tools/BsxtBatch.Cli/     Headless front-end over the same pipeline (debugging/CI)
    tests/BsxtBatch.Core.Tests/
    spike/                   Phase 0 COM spike + diagnostic probes (kept for reference)

## Build & test

    dotnet build BsxtBatch.sln -c Release
    dotnet test  BsxtBatch.sln -c Release

Publish a single-file exe:

    dotnet publish src/BsxtBatch.App -c Release -r win-x64 --self-contained true `
      -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish/BsxtBatch

Headless pipeline check against a live Photoshop:

    tools\BsxtBatch.Cli\bin\Release\net9.0-windows\BsxtBatch.Cli.exe photo.ARW other.tif

## Troubleshooting

| Symptom | Cause / fix |
|---|---|
| `Photoshop COM ProgID not found` | Photoshop not installed or not registered; start it once manually. |
| Batch aborts: `could not run action` | Load the **BSXT** action set in Photoshop (Actions panel → Load Actions…). |
| File skipped: `Already processed (contains a 'BSXT'/'Cleanup' layer)` | That file already has the action's layers; only feed un-edited files. |
| File skipped: `Output '…-BSXT.tif' already exists — not overwriting` | The output name is taken; delete/rename the existing output if you want to re-process. |
| `application is busy` (RPC_E_SERVERCALL_RETRYLATER) | Handled automatically by the message filter; if seen, close other Photoshop dialogs. |
| RAW opens with unexpected look | Camera Raw uses default settings under automation (dialogs are suppressed). Tune defaults in ACR's preferences if needed. |
| Output is not written | Check write permission on the source folder (the app pre-checks and skips otherwise). |

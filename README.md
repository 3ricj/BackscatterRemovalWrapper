# BsxtBatch

Batch-runs the Photoshop **BSXT** action on a pile of image files and exports
`<name>-BSXT.<ext>` next to each original. Originals are never modified, and
existing files are **never overwritten**.

Pick the output format(s) with the **Export** checkboxes — **JPG**, **TIF**, and/or
**PSD** (TIF is on by default, and at least one must stay checked). PSD copies keep
the action's layers. Use **Browse…** next to **Output folder** to redirect every
result into one folder instead of next to each source (write permission is checked
when you press Start).

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
- Write permission in each source folder (output goes beside the source), or in the
  folder chosen via the **Output folder** override.

## Running

Prebuilt (self-contained, no .NET install needed):

    publish\BsxtBatch\BsxtBatch.App.exe

From source (.NET 9 SDK):

    dotnet run --project src/BsxtBatch.App

## Installer

Build a Windows installer (per-user, no admin rights required):

    powershell -ExecutionPolicy Bypass -File installer\build-installer.ps1 -Version 1.1.1

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
→ `SaveAs(<name>-BSXT.<ext>, asCopy: true)` once per selected format → `Close(discard)`.

The export format is chosen by the user, not by the input type. By default every
selected format is written next to the original (use **Browse…** under **Output
folder** to send all results to a single shared folder instead):

| Selected format | Output | Details |
|---|---|---|
| **TIF** | `<name>-BSXT.tif` | LZW, flattened, embedded profile |
| **JPG** | `<name>-BSXT.jpg` | quality 12, optimized, embedded profile |
| **PSD** | `<name>-BSXT.psd` | **layers preserved**, embedded profile, maximized compatibility |

Any combination is allowed (a file can produce all three). At least one format must be
selected; **TIF is the default**. When PSD is selected, the saved copy keeps the BSXT
action's layers instead of flattening them.

Dialogs are suppressed (`DisplayDialogs = psDisplayNoDialogs`) so the batch never hangs;
a COM message filter transparently retries Photoshop's "application busy" rejections.

### Output folder (default: source folder)

Outputs normally land beside each source file. Clicking **Browse…** on the
**Output folder** row redirects **all** exports of the batch into one chosen folder
(created if missing); **Clear** restores the default. When you press **Run**, the
folder is verified to be writable **before** Photoshop is touched — if it isn't, the
batch aborts with a clear message instead of failing file by file.

### Never-overwrite rule (per format)

Each selected output is checked individually:

- If **all** selected outputs already exist, the file is **skipped** with a message naming them.
- If only **some** exist (e.g. you previously exported TIF and now also tick JPG), the
  missing formats are written and the existing ones are left untouched — the log reports
  exactly which were skipped.

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

The CLI takes the same export selection via `-f` (default `tif`) and an optional
output-folder override via `-o`:

    tools\BsxtBatch.Cli\bin\Release\net9.0-windows\BsxtBatch.Cli.exe -f tif,psd -o D:\results photo.ARW

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
| Batch aborts: `No write permission in output folder` | The folder chosen via Browse… (or `-o`) is read-only or otherwise unwritable — pick another folder or clear the override. |

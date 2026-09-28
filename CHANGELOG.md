# Changelog

## v1.1 — 2026-09-28

### New: user-selectable export formats

- **Export checkboxes (JPG / TIF / PSD)** in the main window. Choose any combination;
  each file now exports every selected format next to the original
  (`photo.jpg` → `photo-BSXT.tif` / `photo-BSXT.psd` / `photo-BSXT.jpg`).
- **TIF is the default** (checked on startup, others unchecked).
- **Validation**: at least one format must be selected — an inline warning appears
  when none are checked and the batch refuses to start with a clear message.
- **PSD exports keep the layers**: the BSXT action's layer set is preserved in the
  PSD copy (never flattened), with maximized compatibility for other apps.

### Changed

- The never-overwrite rule now applies **per format**: if only some of the selected
  outputs already exist, the missing formats are still written and the existing files
  are left untouched (the log reports which were skipped). A file is skipped entirely
  only when every selected output already exists.
- Output format is now driven by the user's selection instead of the input file type
  (previously: JPG in → JPG out, everything else → TIF).
- CLI front-end: new `-f jpg,tif,psd` option (default `tif`), e.g.
  `BsxtBatch.Cli -f tif,psd photo.ARW`.

### Fixed / internal

- `OutputNameResolver` is now format-driven (`Resolve(path, format)` / `ResolveAll`);
  unused `FileFormatClassifier.ExportsAsTiff` removed.
- Test suite updated and expanded (54 tests, all passing).

**Download:** `BsxtBatch-Setup-1.1.0.exe` (per-user install, no admin rights;
installs over v1.0 in place).

## v1.0 — initial release

Batch-runs the Photoshop BSXT action on a list of files; outputs `<name>-BSXT.tif|.jpg`
next to originals; originals never modified; existing files never overwritten.

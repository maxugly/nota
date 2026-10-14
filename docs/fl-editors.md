# FL-style editors

Personal workflow branch: `codex/fl-editors`.

The clip editor offers **Notes**, **FL roll**, and **Envelopes**. The FL roll edits
Nota notes through the existing undo and engine callbacks. It does not require
FL Studio, Wine, REA, or an MCP connection to run.

## Shared foundations for the Playlist

- `Nota.Application/BeatGrid.cs` contains beat-space snapping with no UI or audio
  dependencies. The arrangement and piano roll both use it. Nearest-grid movement
  and containing-cell drawing are separate operations; bounds and Alt-key policy
  remain the responsibility of each editor.
- `Nota.App/Theme/FlEditorPalette.cs` holds the optional FL colors and toolbar
  dimensions. A future Playlist can use these tokens without changing Nota's
  application theme.
- Keep note expression, piano pitch mapping, and note audition in the piano roll.
  Playlist clip gestures should use arrangement commands and undo boundaries.
  Share gesture code only once both editors have the same tested behavior.

## Validation and running

Build the native engine using `scripts/build-linux.sh` with .NET SDK 10 on PATH.
Run the interaction checks with:

```sh
dotnet run --project tests/Nota.PianoRollTest -c Release
```

For a portable local build (after building the native engine):

```sh
dotnet publish src/managed/Nota.App -c Release -r linux-x64 --self-contained true -o dist/fl-editors
./scripts/run-fl-editors.sh
```

The launcher uses a separate `nota-fl-editors` settings profile by default.
Override `NOTA_DATA_DIR` to choose another profile. Close the installed Nota
before running the executable directly with your normal settings.
The live FL bridge is a reference tool and is maintained in the separate
`fl-studio-mcp` checkout; its local patches are not part of this repository.

The current roll does not implement FL-native slide/portamento, per-note mute,
ghost channels, or all of FL's advanced processing tools.

## Local validation (2026-10-14)

- Full .NET solution and native Linux engine/plugin scanner build succeeded.
- Piano-roll headless suite: 43 checks passed.
- Design checks rerun after token fixes: 40 checks passed.
- Self-contained app started with an isolated profile and remained running for
  the 12-second startup check; this is not a full interactive/audio-device test.
- Broader engine smoke run passed over 2,500 checks, then was stopped during
  exhaustive factory-preset audition rendering. It is **not a clean full-suite
  result**: it reported a Consort stress timing failure while compilation ran
  concurrently, plus three design failures corrected and checked separately.

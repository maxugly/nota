# Implementation Plan: Undo for Volume and Pan

**Branch**: `009-mix-param-undo` | **Date**: 2026-10-14 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/009-mix-param-undo/spec.md`

## Summary

The engine's single undo history learns a second kind of step: a **mix step** that holds
volume / pan / master-volume values before and after a gesture, next to today's graph
snapshots. A small gesture API (`begin(targets)` → handle, `end(handle)`) on the message
thread records a gesture's start values and pushes one step for whatever changed; the
plain setters stay as they are and never record, so automation playback, loads, freeze and
the template make no steps by construction. Every user source (Mixer, headers,
Return/Master rows, Session mixer, MST, Remote, MIDI Learn, MCP) is wired to begin/end at
its natural gesture boundary; resets and typed values become one-shot gestures. A gesture
that overlaps an automation-recording checkpoint merges into it, so the recorded lane and
the value undo together. See [research.md](research.md).

## Nota: Read Before Planning

- **Engine work** → [`ARCHITECTURE.md`](../../ARCHITECTURE.md): undo stays message-thread
  only; the audio thread keeps reading the same atomics (AR-6 untouched); AR-7 for the new
  C ABI.
- **Anything** → [`CONTRIBUTING.md`](../../CONTRIBUTING.md).

## Technical Context

**Language/Version**: C++20 (engine), C# / .NET 10 (managed app)

**Primary Dependencies**: Avalonia (existing); no new dependency

**Storage**: N/A — undo history is in memory, not saved

**Audio-Thread Impact**: None. History, gestures and undo/redo run on the message thread;
undo writes values through the existing atomic setters, exactly like a hand move. No new
audio-thread code.

**Testing**: `scripts/build.sh` → smoke test gains `MixUndoTests` (engine-level gestures,
MCP one-shot, debounced MIDI); headless harness for the UI sources ([quickstart.md](quickstart.md))

**Target Platform**: macOS / Windows / Linux desktop app

**Project Type**: desktop-app (engine + managed)

**Performance Goals**: gesture begin/end O(targets); undo of a mix step O(changes); no
per-frame allocation during a drag (values are only read at begin and end)

**Constraints**: exact values (no rounding, FR-004); depth limit 128 shared with
structural steps

**Scale/Scope**: engine ~200 lines (UndoEntry, gestures, merge, getter); managed helper
~120 lines; ~12 call sites wired; smoke test ~250 lines

## Constitution Check

| Principle | Status |
|---|---|
| I. Real-time audio safety | ✅ No audio-thread change; history is message-thread only |
| II. Boundary discipline | ✅ New C ABI: `NotaMixTarget`, `nota_mix_gesture_begin/end`, `nota_engine_master_volume` |
| III. Licensing hygiene | ✅ New files get the SPDX header; no dependency |
| IV. Verification gate | ✅ Engine undo behaviour changes → `MixUndoTests` in the smoke test |
| V. One logical change | ✅ Volume/pan undo end to end; sources wired in the same change because the spec requires all of them (FR-007) |
| VI. Docs in sync | ⏳ CHANGELOG, `nota-features`, `nota-docs` (undo section) |
| VII. Spec-first | ✅ |
| VIII. Rules not weakened | ✅ |

Post-design re-check: unchanged — all gates pass.

## Design

### 1. Engine — history entries ([research R1](research.md), [data model](data-model.md))

- `Engine.h`: `struct MixChange`, `struct UndoEntry { shared_ptr<Graph> graph; vector<MixChange> mix; }`;
  `undoStack_` / `redoStack_` become `vector<UndoEntry>`; `canUndo/canRedo` unchanged.
- `pushUndo()` pushes `{authoring_, {}}` and bumps `undoPushCount_` (new `uint64_t`).
- `undo()` / `redo()`: graph part as today (`publishRaw`), then apply `before` / `after`
  of each change via `setVolume` / `setPan` on the authoring track (skip missing) or the
  master volume setter; an entry that applied nothing and had no graph → continue with
  the next entry (R3).
- `endUndoGroup()`: when dropping entries, merge their `mix` into the kept entry (data
  model merge rule).
- `clearUndoHistory()` / `reset()`: also clear open gestures.

### 2. Engine — gestures ([research R2](research.md), [contract](contracts/c-abi-mix-undo.md))

- `Engine`: `int32_t beginMixGesture(const MixTarget*, int32_t)`, `void endMixGesture(int32_t)`,
  `float masterVolume() const`; open gestures in a small `vector<OpenGesture>` (message
  thread), handles from a counter.
- `end`: build changes (exact compare, drop vanished tracks); none → return; if
  `undoPushCount_ != g.pushCountAtBegin` and the stack is non-empty → merge into
  `undoStack_.back()`; else push `{nullptr, changes}`, trim to depth, clear redo.
- C ABI in `nota_engine.h` / `nota_engine_core.cpp`; update the undo block comment.

### 3. Managed port + helpers

- `Nota.Application/Engine/EngineContracts.cs`: `enum MixKind { TrackVolume, TrackPan, MasterVolume }`,
  `readonly record struct MixTarget(MixKind Kind, int TrackId)` (blittable layout).
- `IAudioEngine`: `int BeginMixGesture(params MixTarget[] targets)`,
  `void EndMixGesture(int handle)`, `float MasterVolume { get; }`; `NotaEngine` + P/Invoke.
- `Nota.Application/Editing/MixEdit.cs` (new): `Step(engine, Action set, params MixTarget[])`
  — one-shot begin → set → end.
- In the same file: `MixGesture` (a control's Begin / End / `Change(value, setter)`, one-off
  step when no gesture is open, no allocation per value) and `MixMovement` (MIDI Learn: one
  step per movement, ended by a 1 s pause checked on the learn service's tick — time passed in,
  so the smoke test drives it). *(Implementation: these replaced a planned
  `Nota.App/MixGestureWiring.cs` with a `DispatcherTimer`, which headless tests can't fire.)*

### 4. Sources ([contracts/sources.md](contracts/sources.md))

Wire, in this order: `MixerView` (pan knob, fader, dB number, master strip), `ArrangementView`
(header mini fader + pan bar, Return/Master rows), `SessionView.Mixer`, the transport MST,
`RemoteHub.Apply` (`vol` / `pan` / `mvol` — begin on the first command of a touch, end on
`Phase == 2`; keep a per-client open-gesture map), `MidiLearnService` (debounced),
`TrackTools` MCP (`MixEdit.Step`). Leave load / template / freeze callers on the plain setters.

### 5. Views after undo/redo

`MainWindow.RefreshAfterUndoRedo`: also `vm.Transport.MasterVolume = Engine.MasterVolume`
(R4). Everything else already rebuilds from the engine.

### 6. Smoke test + docs

- `tests/Nota.SmokeTest/MixUndoTests.cs` (new, SPDX): every Guarantees row of the C ABI
  contract, MCP one-shot via `McpTestSupport`, debounced MIDI (simulated clock or a short
  timeout); wired in `Program.cs` (full run + `--mixundo`).
- `CHANGELOG.md` (Added: volume and pan undo), `nota-features`, `nota-docs`.

## Project Structure

### Documentation (this feature)

```text
specs/009-mix-param-undo/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── c-abi-mix-undo.md
│   └── sources.md
├── checklists/requirements.md
└── tasks.md             # /speckit-tasks
```

### Source Code (repository root)

```text
src/native/nota.engine/
├── include/nota/nota_engine.h       # NotaMixTarget, gesture begin/end, master volume getter
└── src/
    ├── Engine.h / Engine.cpp        # UndoEntry, gestures, undo/redo/group merge
    └── nota_engine_core.cpp         # C ABI
src/managed/
├── Nota.Application/Engine/EngineContracts.cs, IAudioEngine.cs
├── Nota.Application/Editing/MixEdit.cs                     (new)
├── Nota.Infrastructure/Interop/NativeMethods.cs, NotaEngine.cs
├── Nota.App/MixerView.cs, ArrangementView.cs, SessionView.Mixer.cs, MainWindow.*.cs (MST, RefreshAfterUndoRedo)
├── Nota.App/MidiLearnService.cs
├── Nota.Remote/RemoteHub.Apply.cs
└── Nota.Mcp/Tools/TrackTools.cs
tests/Nota.SmokeTest/MixUndoTests.cs (new), Program.cs
CHANGELOG.md, FEATURES.md
```

**Structure Decision**: existing projects; two new files (`MixEdit.cs`, `MixUndoTests.cs`).

## Out of Scope / Known Gaps

- Sends, mute, solo, device params, transport values (clarified).
- Scroll-wheel bursts: no mix control handles the wheel today (research R5) — N/A until one does.
- A structural edit made by another source *during* an open gesture (e.g. an MCP edit while
  dragging) merges with that gesture's step (the automation-merge rule cannot tell them
  apart). Rare; documented rather than solved.
- Undo step labels (none in Nota).
- The Stereo Map's own wiring belongs to spec 008 (two targets in one gesture — supported).

## Complexity Tracking

No constitution violations.

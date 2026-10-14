---

description: "Task list for 009 Undo for Volume and Pan"
---

# Tasks: Undo for Volume and Pan

**Input**: Design documents from `/specs/009-mix-param-undo/`

**Prerequisites**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md),
[data-model.md](data-model.md), [contracts/](contracts/), [quickstart.md](quickstart.md)

**Tests**: Requested — FR-013 lists the smoke-test coverage, and the constitution (IV)
requires it for an engine behaviour change. Engine-level tests live in one new file,
`tests/Nota.SmokeTest/MixUndoTests.cs`, so tasks touching it are sequential.

**Organization**: by user story — US1 take back a move (P1), US2 one history (P1), US4
non-edits stay out (P1), US3 every source (P2).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: US1 / US2 / US3 / US4

---

## Phase 1: Setup

- [X] T001 Run `scripts/build.sh` on the untouched tree and confirm it is green (baseline); if red, stop and report

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: the history can hold mix steps and the gesture API exists end to end. No
source is wired yet, so the app behaves as before.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete

- [X] T002 In `src/native/nota.engine/src/Engine.h` add `enum class MixKind : int32_t { TrackVolume = 0, TrackPan = 1, MasterVolume = 2 };`, `struct MixChange { MixKind kind; int32_t trackId; float before; float after; };` and `struct UndoEntry { std::shared_ptr<Graph> graph; std::vector<MixChange> mix; };` ("graph: the state to restore; null for a mix-only step"; "mix: empty for a structural step; at most one change per target"), change `undoStack_` / `redoStack_` to `std::vector<UndoEntry>`, and add `uint64_t undoPushCount_ = 0;`
- [X] T003 In `src/native/nota.engine/src/Engine.cpp` adapt the history to `UndoEntry`: `pushUndo()` pushes `{authoring_, {}}` and increments `undoPushCount_`; `undo()` pops an entry, pushes `{graph ? authoring_ : nullptr, mix}` onto redo, `publishRaw(graph)` when it has one, then applies every change's `before` (track volume/pan via the authoring track's setter, skipping a missing track; master volume via the master setter) — if the entry has no graph and applied nothing, continue with the next entry (research R3); `redo()` symmetric with `after`; `endUndoGroup()` merges the dropped entries' `mix` into the kept first entry (same target → keep the older `before`, the newer `after`; drop a target whose merged `before == after`); `canUndo/canRedo/clearUndoHistory` keep working; update the undo comment block in `Engine.h` ("volume, pan and master volume steps from mix gestures are covered; mute, solo, sends, device params and the transport are not")
- [X] T004 In `Engine.h` / `Engine.cpp` add the gesture API (research R2, data-model § Open gesture): `int32_t beginMixGesture(const MixTarget* targets, int32_t count)` (records each valid target's current value as `before` and `pushCountAtBegin = undoPushCount_`; returns a handle > 0, or 0 if no target is valid), `void endMixGesture(int32_t handle)` (unknown handle → no-op; builds changes with exact float compare, dropping unchanged targets and vanished tracks; none → return; if `undoPushCount_ != pushCountAtBegin` and the undo stack is not empty → merge into `undoStack_.back()` by the merge rule; else push `{nullptr, changes}`, trim to `kMaxUndoDepth`, clear redo), `float masterVolume() const`; `reset()` and `clearUndoHistory()` drop all open gestures. Message thread only, no audio-thread code
- [X] T005 Add the C ABI from [contracts/c-abi-mix-undo.md](contracts/c-abi-mix-undo.md) verbatim to `src/native/nota.engine/include/nota/nota_engine.h` (`NOTA_MIX_*`, `NotaMixTarget`, `nota_mix_gesture_begin`, `nota_mix_gesture_end`, `nota_engine_master_volume`, updated undo comment) and implement it in `src/native/nota.engine/src/nota_engine_core.cpp` (null engine → 0 / no-op / 1.0f)
- [X] T006 [P] In `src/managed/Nota.Application/Engine/EngineContracts.cs` add `public enum MixKind { TrackVolume = 0, TrackPan = 1, MasterVolume = 2 }` and `[StructLayout(LayoutKind.Sequential)] public readonly record struct MixTarget(MixKind Kind, int TrackId)` with static helpers `Volume(id)`, `Pan(id)`, `Master`; in `src/managed/Nota.Application/Engine/IAudioEngine.cs` add `int BeginMixGesture(params MixTarget[] targets)`, `void EndMixGesture(int handle)`, `float MasterVolume { get; }`
- [X] T007 [P] P/Invokes in `src/managed/Nota.Infrastructure/Interop/NativeMethods.cs` (`MixGestureBegin(IntPtr, MixTarget[], int)`, `MixGestureEnd(IntPtr, int)`, `MasterVolume(IntPtr)`) and wrappers in `src/managed/Nota.Infrastructure/Interop/NotaEngine.cs`
- [X] T008 [P] Create `src/managed/Nota.Application/Editing/MixEdit.cs` (SPDX header; file comment: one-shot undoable volume/pan edits, specs/009) with `public static void Step(IAudioEngine engine, Action set, params MixTarget[] targets)` — begin, run `set`, end (end runs even if `set` throws)
- [X] T009 Create `src/managed/Nota.App/MixGestureWiring.cs` (SPDX header) per [contracts/sources.md](contracts/sources.md): `Wire(IAudioEngine engine, Action<Action> onBegin, Action<Action> onEnd, Action<Action<double>> onValue, Func<MixTarget[]> targets)` style helper (adapt the signature to the controls' events) — `GestureBegin` → open a handle, `GestureEnd` → end it, a value change while no handle is open → one-shot via `MixEdit.Step`; and `DebouncedMixGesture` (DispatcherTimer, configurable pause, default 1 s) whose `Touch()` opens a gesture if none is open and restarts the timer, ending the gesture when it fires
- [X] T010 Create `tests/Nota.SmokeTest/MixUndoTests.cs` (SPDX header; comment "Undo for volume and pan (specs/009-mix-param-undo); also `--mixundo`") with `Run()` and wire it in `tests/Nota.SmokeTest/Program.cs` (full run next to `PanLawTests`, plus a `--mixundo` branch like `--panlaw`); first checks: begin(vol t) · 50 sets · end · undo → exactly the begin value, redo → exactly the last value; begin · end with no set → `CanUndo` unchanged; set back to the start value → no step; master gesture → `MasterVolume` restored; `Reset()` with an open gesture then `EndMixGesture` → no step

**Checkpoint**: `scripts/build.sh` green; the engine API works; no source wired yet.

---

## Phase 3: User Story 1 — Take back a fader move (Priority: P1) 🎯 MVP

**Goal**: fader/knob drags, resets and typed values in the Mixer, the track headers and the
Return/Master rows are one undo step each.

**Independent Test**: quickstart § 2 steps 1–2.

- [X] T011 [US1] In `src/managed/Nota.App/MixerView.cs` wire with `MixGestureWiring`: `PanRow` knob (target pan), `FaderRow` `VFader` and the dB `DragNumber` (target volume) — keep the existing `BeginAutomationWrite`/`EndAutomationWrite` calls, opening the mix gesture *before* the automation write and ending it *after*; the master strip fader (target `MixTarget.Master`); double-click resets and typed dB values arrive with no gesture open → one-shot
- [X] T012 [US1] In `src/managed/Nota.App/ArrangementView.cs` wire the header mini fader and pan bar (~lines 1848–1862, same ordering with the automation write) and the Return/Master footer rows (~2425–2466; the master row targets `MixTarget.Master`)
- [X] T013 [US1] In `src/managed/Nota.App/MainWindow.Menu.cs` `RefreshAfterUndoRedo()`: set `vm.Transport.MasterVolume = Engine.MasterVolume` when it differs (research R4) so MST, the Master strip and the Master row follow an undo; check the MST `MiniFader` in `MainWindow.axaml.cs` (~line 223) follows `vm.Transport.MasterVolume` changes — if it does not, make it follow (skip while dragging)
- [X] T014 [US1] Headless UI check (memory "Nota UI verify recipe", `NOTA_DATA_DIR`): drive a Mixer fader drag, a pan knob drag, a double-click reset and a typed dB value with synthetic events; after each, `Undo()` restores the exact value in the engine and in the fader/dB field, `Redo()` re-applies; record results in the PR, not in code

**Checkpoint**: US1 works in the Mixer, headers and Return/Master rows.

---

## Phase 4: User Story 2 — One history for everything (Priority: P1)

**Goal**: mix steps and structural steps share one strictly ordered history.

**Independent Test**: contract Guarantees rows on ordering and deleted tracks.

- [X] T015 [US2] In `tests/Nota.SmokeTest/MixUndoTests.cs`: add track · volume gesture · `SetPanLaw(Stereo)` · pan gesture · undo ×4 → each intermediate state matches in reverse order; redo ×4 forward; a new edit after two undos drops redo
- [X] T016 [US2] In `tests/Nota.SmokeTest/MixUndoTests.cs`: begin(vol t) · set · `RemoveTrack(t)` · end → no mix step (the removal is the top step); volume gesture on t · remove t · undo · undo → t back, then its volume back; two gestures open at once on different tracks → two steps in end order; an undo group (`BeginUndoGroup`/`EndUndoGroup`) containing two volume gestures on the same track → one step from the first `before` to the last `after`
- [X] T017 [US2] Fix whatever T015–T016 expose in `src/native/nota.engine/src/Engine.cpp` (ordering, merge, skip) until they pass

**Checkpoint**: ordering guarantees hold.

---

## Phase 5: User Story 4 — Things that are not edits stay out (Priority: P1)

**Goal**: automation playback, loads, the template and freeze make no steps; automation
recording merges with its gesture.

**Independent Test**: quickstart § 2 steps 4–5; contract rows on automation.

- [X] T018 [US4] In `tests/Nota.SmokeTest/MixUndoTests.cs`: a volume + pan automation lane in read mode, clear history, render 2 s while playing → `CanUndo == false`; `ProjectTemplate.ApplyDefault` and a project save → load → `CanUndo == false` for mix steps (no volume/pan values change on undo); `TrackFreezer.Freeze` on a track → undo does not touch its volume
- [X] T019 [US4] In `tests/Nota.SmokeTest/MixUndoTests.cs`: `SetAutomationRecord(true)` · `BeginMixGesture(vol t)` · `BeginAutomationWrite` · sets while rendering · `EndAutomationWrite` · `EndMixGesture` · one `Undo()` → both the lane points and the volume are back; one `Redo()` → both again (FR-009)
- [X] T020 [US4] Confirm (grep) that `ProjectService.Apply`, `ProjectTemplate`, `MainWindow.Freeze.cs`, `MainWindow.LiveFreeze.cs` and `MainWindow.Files.cs` resets use only the plain setters, never `BeginMixGesture`/`MixEdit.Step`; fix any that do

**Checkpoint**: non-edits make no steps; recording merges.

---

## Phase 6: User Story 3 — Every way of mixing is undoable (Priority: P2)

**Goal**: Session mixer, MST, Remote, MIDI Learn and MCP changes are undoable.

**Independent Test**: one change from each source, undo each.

- [X] T021 [P] [US3] In `src/managed/Nota.App/SessionView.Mixer.cs` wire the pan knob (~line 117) and the volume fader (~126) with `MixGestureWiring` (sends stay unwired — out of scope)
- [X] T022 [P] [US3] In `src/managed/Nota.App/MainWindow.axaml.cs` wire the MST `MiniFader` (~line 223) to `MixTarget.Master` via `MixGestureWiring`
- [X] T023 [P] [US3] In `src/managed/Nota.Remote/RemoteHub.Apply.cs` (`vol`, `pan`, `mvol`): keep a per-client map `(kind, id) → handle`; open a gesture on the first command of a touch when none is open, end it when `cmd.Phase == 2`, and end any open handles when the client disconnects
- [X] T024 [P] [US3] In `src/managed/Nota.App/MidiLearnService.cs` (~lines 348–350): route `TrackVolume`, `TrackPan`, `MasterVolume` through one `DebouncedMixGesture` per mapping (1 s pause)
- [X] T025 [P] [US3] In `src/managed/Nota.Mcp/Tools/TrackTools.cs` wrap `set_track_volume` and `set_track_pan` bodies in `MixEdit.Step` (inside the existing `Mutate`)
- [X] T026 [US3] In `tests/Nota.SmokeTest/MixUndoTests.cs`: `TrackTools.SetTrackVolume` through `McpTestSupport` (`SyncDispatch`, `NoRefresh`) is one undo step; the Remote `vol` command sequence phase 0 → 1 → 2 through the hub (or its apply method) is one step

**Checkpoint**: every listed source is undoable.

---

## Phase 7: Polish & Cross-Cutting Concerns

- [X] T027 Headless UI pass of quickstart § 2 (steps 3–5: MST, automation playback, recording merge) in both themes; nothing visual changes, so no screenshots needed beyond confirmation
- [X] T028 [P] Performance sanity: a 2 s fader drag (≈120 value changes) allocates nothing per change on the managed side beyond today's (values are only read at begin/end); note it in the PR

### Nota closing tasks

- [X] T029 Run `scripts/build.sh` until native + managed + smoke test are green
- [X] T030 AGPL-3.0-only SPDX header on every new source file (`MixEdit.cs`, `MixGestureWiring.cs`, `MixUndoTests.cs`)
- [X] T031 New dependency: N/A
- [X] T032 `CHANGELOG.md` `[Unreleased]` Added: "**Undo for volume and pan:** fader, pan and master-volume moves can be undone and redone (⌘Z / ⌘⇧Z) from the Mixer, track headers, Session mixer, transport, Nota Remote, a MIDI controller and the AI assistant; one move is one step. Automation playback never adds steps."
- [X] T033 FEATURES.md via `nota-features` (Project and transport: what undo covers); user docs EN + RU via `nota-docs` (the page that describes undo); shortcuts: N/A
- [X] T034 Engine behaviour change → smoke test extended: confirm all `MixUndoTests` labels pass in the full run and with `--mixundo`

---

## Dependencies & Execution Order

- **Setup (T001)** → **Foundational (T002–T010)** → stories → **Polish (T027–T034)**
- T002 → T003 → T004 → T005 → (T006 ∥ T007) → T008 → T009; T010 after T007
- **US1 (T011–T014)**, **US2 (T015–T017)**, **US4 (T018–T020)** depend only on Foundational
- **US3 (T021–T026)** depends on Foundational (T009 for the wiring helper)
- Tests in `MixUndoTests.cs` are sequential: T010 → T015 → T016 → T018 → T019 → T026

### Parallel Opportunities

- T006 ∥ T007 (Application vs Infrastructure)
- T021 ∥ T022 ∥ T023 ∥ T024 ∥ T025 (five different files)
- US1 wiring (App) alongside US2 test work (smoke test) once Foundational is done

## Parallel Example: User Story 3

```bash
Task: "T021 Session mixer wiring in src/managed/Nota.App/SessionView.Mixer.cs"
Task: "T022 MST wiring in src/managed/Nota.App/MainWindow.axaml.cs"
Task: "T023 Remote gestures in src/managed/Nota.Remote/RemoteHub.Apply.cs"
Task: "T024 MIDI Learn debounce in src/managed/Nota.App/MidiLearnService.cs"
Task: "T025 MCP one-shot in src/managed/Nota.Mcp/Tools/TrackTools.cs"
```

## Implementation Strategy

### MVP

Foundational + US1 + US2 + US4 is the minimum shippable: the Mixer and headers become
undoable without corrupting the history or flooding it during playback. US3 completes the
spec's promise of every source (FR-007); ship all four together in one PR — a partial set
would make undo behave differently depending on where the user moved a fader.

### Incremental order

1. T001–T010 → engine + API, build green, no visible change
2. US1 → Mixer / headers / Return-Master undoable
3. US2 → ordering verified
4. US4 → playback/loads excluded, recording merges
5. US3 → Session, MST, Remote, MIDI, MCP
6. Polish → UI pass, docs, final build

## Notes

- Do not commit or push — the user reviews and commits.
- `.specify/feature.json` points at `specs/007-midi-note-editing`; run spec-kit scripts for
  this feature with `SPECIFY_FEATURE_DIRECTORY=specs/009-mix-param-undo SPECIFY_FEATURE_NO_PERSIST=1`.
- Spec 008 (Stereo Map) waits for this one: a map drag is one gesture with two targets.

## Phase 8: Convergence

- [X] T035 Reconcile the scroll-wheel rule (US1 scenario 5, FR-003 "a burst of scroll-wheel notches…"): no volume/pan control handles the wheel today, so either drop the scenario from the spec via `/speckit-clarify` or add wheel support to `PanKnob`, `VFader`, `MiniFader`, `PanBar` (`src/managed/Nota.App/Controls/`) driven through a 0.5 s `DebouncedMixGesture` per control per US1/AC5 (partial)
- [X] T036 Add an engine self-test (pattern of the existing `*SelfTest` functions in `src/native/nota.engine/src/`, exposed through `nota_engine.h` and called from `tests/Nota.SmokeTest/MixUndoTests.cs`) that pushes a mix-only undo entry for a track id not in the graph above a valid step and checks that one `undo()` skips it and undoes the valid step, and `redo()` mirrors it, per FR-010 / FR-013 (partial)
- [X] T037 Make the MIDI-controller pause testable without a UI timer — let `DebouncedMixGesture` (`src/managed/Nota.App/MixGestureWiring.cs`) take the pause timer through a small abstraction, or move the debounce logic into `Nota.Application` with an injected clock — and add a smoke-test check that two movements separated by more than the pause are two steps and a continuous one is one, per US3 scenario 3 / quickstart § 1 (partial)
- [X] T038 Remove the per-move closure allocation in the wired controls (`src/managed/Nota.App/MixerView.cs`, `ArrangementView.cs`, `SessionView.Mixer.cs`, `MainWindow.axaml.cs`): when the control's `MixGesture` is open call the setter directly and only fall back to `Change(...)` for one-off values, or record in `plan.md` Performance Goals that one small closure per move is accepted, per plan: Performance Goals (partial)

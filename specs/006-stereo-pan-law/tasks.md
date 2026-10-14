---

description: "Task list for 006 Stereo Pan Law"
---

# Tasks: Stereo Pan Law

**Input**: Design documents from `/specs/006-stereo-pan-law/`

**Prerequisites**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md),
[data-model.md](data-model.md), [contracts/](contracts/), [quickstart.md](quickstart.md)

**Tests**: Requested — FR-012 requires smoke-test coverage, and the constitution
(IV) requires it for an engine behaviour change. All tests live in one new file,
`tests/Nota.SmokeTest/PanLawTests.cs`, so test tasks touching it are sequential.

**Organization**: Grouped by user story (spec.md): US1 stereo image (P1), US2 older
projects unchanged (P1), US3 switching the law (P2).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: US1 / US2 / US3

---

## Phase 1: Setup

- [X] T001 Run `scripts/build.sh` on the untouched tree and note that it is green (baseline for the bit-identical checks); if it is red, stop and report — do not start the feature on a red baseline

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: the law exists as graph data, survives every edit, and is reachable
from C ABI and C#. No sound change yet.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete

- [X] T002 In `src/native/nota.engine/src/Graph.h` add `enum class PanLaw : int32_t { Balance = 0, Stereo = 1 };` and the field `PanLaw panLaw = PanLaw::Balance;` on `Graph` (comment: project pan law, 006; graph-level so undo/redo restore it), and add `inline void inheritGraphLevel(Graph& dst, const Graph& src)` that copies `sceneCount`, `scenes`, `masterVolume`, `masterTrack` and `panLaw`; rewrite the existing "fresh-graph builds must carry it forward" comment to say "use inheritGraphLevel()"
- [X] T003 Replace the hand-copied graph-level lines (`g->sceneCount = authoring_->sceneCount; g->scenes = …; g->masterVolume = …; g->masterTrack = …;`) with `inheritGraphLevel(*g, *authoring_);` at every production fresh-graph site: `src/native/nota.engine/src/Engine.cpp` (`republishWithTrackRaw`, ~line 520 — keep the `masterTrack` override for `kMasterTrackId` *after* the call), `src/native/nota.engine/src/Engine_Modulation.cpp` (~309), `src/native/nota.engine/src/Engine_Session.cpp` (~113 — keep `g->sceneCount = newCount` *after* the call; ~861), `src/native/nota.engine/src/Engine_Tracks.cpp` (~461, 1382, 1502, 1534, 1564, 1619, 1754, 1896, 2439, 2491, 2686). Leave the self-test graphs in `Engine_Automation.cpp` (~75, 377, 430) and the reset graph in `Engine.cpp` (~570) as they are. Afterwards `grep -n "make_shared<Graph>()" src/native/nota.engine/src/*.cpp` and confirm every remaining hit is either followed by `inheritGraphLevel` or is one of the listed exceptions
- [X] T004 In `src/native/nota.engine/src/Engine.h` / `Engine.cpp` add `bool setPanLaw(PanLaw law, bool checkpoint)` (return false for a value outside Balance/Stereo; no-op returning true if equal to `authoring_->panLaw`; else `auto g = std::make_shared<Graph>(*authoring_); g->panLaw = law;` then `publish(g)` when `checkpoint`, `publishRaw(g)` otherwise) and `PanLaw panLaw() const` (reads `authoring_`, Balance if null). Message thread only
- [X] T005 Add to `src/native/nota.engine/include/nota/nota_engine.h` the block from [contracts/c-abi-pan-law.md](contracts/c-abi-pan-law.md) verbatim (`NOTA_PAN_LAW_BALANCE 0`, `NOTA_PAN_LAW_STEREO 1`, `nota_engine_set_pan_law(engine, law, checkpoint)`, `nota_engine_pan_law(engine)` with their comments) and implement both in `src/native/nota.engine/src/nota_engine_core.cpp`: unknown law → `NOTA_ERR_INVALID_ARG`, null engine → `NOTA_ERR_INVALID_ARG` / `NOTA_PAN_LAW_BALANCE`
- [X] T006 [P] In `src/managed/Nota.Application/Engine/IAudioEngine.cs` add `public enum PanLaw { Balance = 0, Stereo = 1 }` (or in a sibling file of the same folder, SPDX header if new) and the members `PanLaw PanLaw { get; }` and `void SetPanLaw(PanLaw law, bool checkpoint = true);` with the doc comment "Project pan law (graph-level, undoable). See nota_engine_set_pan_law."; update every other `IAudioEngine` implementation the compiler reports (test fakes in `tests/Nota.SmokeTest`, MCP abstractions) with a trivial implementation
- [X] T007 [P] Add the P/Invokes `SetPanLaw(IntPtr engine, int law, int checkpoint)` → `nota_engine_set_pan_law` and `PanLaw(IntPtr engine)` → `nota_engine_pan_law` in `src/managed/Nota.Infrastructure/Interop/NativeMethods.cs`, and the wrapper members in `src/managed/Nota.Infrastructure/Interop/NotaEngine.cs` (`ThrowIfDisposed(); Check(...)` like `SetTrackPan` in `NotaEngine.Tracks.cs`)
- [X] T008 Create `tests/Nota.SmokeTest/PanLawTests.cs` (two-line SPDX header; file comment: "Project pan law (specs/006-stereo-pan-law): …; also runnable alone: `dotnet run --project tests/Nota.SmokeTest -- --panlaw`") with `internal static class PanLawTests { public static IEnumerable<(bool ok, string label)> Run() }` and shared helpers: `WriteStereoWav(float[] l, float[] r)` via `Nota.Infrastructure.WavWriter` (48 kHz, Float32, temp path), `Sine(hz, seconds, amp)`, `Render(NotaEngine eng, double seconds)` via `Seek(0); Play(); RenderOffline(...); Stop()`, `Goertzel(float[] interleaved, int channel, double hz, int from, int to)` returning linear amplitude, and `Db(x)`. Wire it in `tests/Nota.SmokeTest/Program.cs`: in the full run next to `ReturnDeleteTests.Run()` (~line 14566) and as a `--panlaw` branch modelled on `--returns` (~line 251)
- [X] T009 In `tests/Nota.SmokeTest/PanLawTests.cs` add the C ABI checks from [contracts/c-abi-pan-law.md](contracts/c-abi-pan-law.md) § Behaviour guarantees: fresh engine = Balance; `Reset()` → Balance; set Stereo (checkpoint) → `Undo()` → Balance → `Redo()` → Stereo; `SetPanLaw(same)` adds no undo step (`CanUndo` count unchanged — compare by undoing once and checking the previous edit is what comes back); `checkpoint: false` adds none; invalid value (cast `(PanLaw)7`) throws/returns error and leaves the law unchanged
- [X] T010 In `tests/Nota.SmokeTest/PanLawTests.cs` add "law survives every edit kind": set Stereo, then one each of add instrument track, add audio track, add return, remove a track, move a track, group tracks, add scene, remove scene, add a volume automation point, add a CV link (if a modulator kind is available without assets; else skip with a label), add + edit a MIDI clip, duplicate a track, undo, redo — after each assert `eng.PanLaw == PanLaw.Stereo` with a label naming the edit

**Checkpoint**: `scripts/build.sh` green; the law is stored, undoable and survives edits; nothing sounds different yet.

---

## Phase 3: User Story 1 — Panning a stereo track moves its whole image (Priority: P1) 🎯 MVP

**Goal**: under Stereo, track and return pan fold the far channel in; new projects start with Stereo.

**Independent Test**: new project, stereo file with different L/R material, sweep pan C → L100: both materials stay, right side goes silent at L100.

### Tests for User Story 1

- [X] T011 [US1] In `tests/Nota.SmokeTest/PanLawTests.cs` add the render checks (write them before T013 and confirm they fail): audio track with a 2 s WAV, L = 440 Hz sine, R = 1 000 Hz sine, both −12 dBFS. Under Stereo at pan −1: R output both tones < −90 dBFS; L output each tone at half its source amplitude (−6.0 dB ±0.5 re source). Mirror at pan +1. At pan 0: Stereo render equals Balance render sample-for-sample (FR-003). Mono WAV (440 Hz on both channels): for p ∈ {−1, −0.6, −0.25, 0.3, 0.8, 1} the per-channel 440 Hz level under Stereo matches Balance within 0.1 dB (FR-004). Unrelated L/R (the two-tone WAV): total power (L²+R² of both tones) at pan −1 within 3.5 dB of pan 0 (FR-005)
- [X] T012 [US1] In `tests/Nota.SmokeTest/PanLawTests.cs` add: a return (Utility device, unity) fed by a send from the two-tone track, return panned −1 under Stereo → both tones present on the return's L output, R silent; a group containing the two-tone track, group panned −0.5: render identical under Stereo and Balance (FR-010); `ProjectTemplate.ApplyDefault(eng)` on a reset engine → `eng.PanLaw == Stereo` and `CanUndo == false` (FR-007)

### Implementation for User Story 1

- [X] T013 [US1] In `src/native/nota.engine/src/Engine_Render.cpp` add `struct PanMatrix { float ll, lr, rl, rr; };` and `static inline PanMatrix panMatrix(float pan, float vol, PanLaw law)` implementing [research.md](research.md) R1 exactly: `θ = (pan + 1)·π/4`, `c = cos θ`, `s = sin θ`, `b = 0.5·|pan|`; Balance → `{c, 0, 0, s}`; Stereo with `pan ≤ 0` → `{c − b, b, 0, s}`; Stereo with `pan > 0` → `{c, 0, b, s − b}`; every coefficient × `vol`. Use the file's existing `kTwoPi / 8` form for θ so Balance coefficients are bit-identical to today's `cos(theta)`/`sin(theta)` (compute in double, cast to float as today)
- [X] T014 [US1] In `src/native/nota.engine/src/Engine_Render.cpp` track stage ("3) Channel fader + equal-power pan", ~line 1264) and return stage (~line 1403): replace `gl`/`gr` with `const PanMatrix m = panMatrix(pan, vol, g->panLaw)` (the live graph pointer already used by the render); if `m.lr == 0 && m.rl == 0` keep the existing loop body unchanged (`l = in_l * m.ll; r = in_r * m.rr`) so Balance renders stay bit-identical; else `l = m.ll*in_l + m.lr*in_r; r = m.rl*in_l + m.rr*in_r`. Everything downstream (record tap, route bus, dest, sends, peak/RMS meters) keeps using `l`/`r`. Do NOT touch the group stage (~line 1337)
- [X] T015 [US1] In `src/managed/Nota.Infrastructure/Persistence/ProjectTemplate.cs` `ApplyDefault`: call `engine.SetPanLaw(PanLaw.Stereo, checkpoint: false);` before `engine.ClearUndoHistory();`, and extend the file comment with one line: new projects use the Stereo pan law (specs/006)

**Checkpoint**: US1 tests pass; a new project pans stereo tracks as whole images.

---

## Phase 4: User Story 2 — Existing projects sound exactly as before (Priority: P1)

**Goal**: format v21 stores the law; every older project opens as Balance and is marked legacy.

**Independent Test**: load a v20 document with a panned stereo track → Balance + legacy, output matches the Balance law; save/reopen keeps both.

### Tests for User Story 2

- [X] T016 [US2] In `tests/Nota.SmokeTest/PanLawTests.cs` add persistence checks via `ProjectStore` into a temp dir: save with Stereo → load → Stereo, `PanLawLegacy == false`; save with Balance + `PanLawLegacy = true` in the `TransportState` → load → Balance, legacy true; and the migration: build a v20 `project.json` in code (take a freshly captured v21 document's JSON, remove `mixer`, set `formatVersion` 20; one audio track with the two-tone WAV, pan −0.5), load it → law Balance, `ProjectLoadResult.Transport.PanLawLegacy == true`, and each output tone matches the Balance law (L tone × cos θ on L, R tone × sin θ on R, no cross-feed) within 0.01 dB (FR-006, SC-002); `ProjectMigrations.Migrate` on a v20 tree yields `mixer = { panLaw: "balance", panLawLegacy: true }` and leaves an existing `mixer` untouched

### Implementation for User Story 2

- [X] T017 [P] [US2] In `src/managed/Nota.Application/Ports/IProjectStore.cs` add `bool PanLawLegacy = false` as the last positional parameter of `TransportState` and extend its doc comment: "PanLawLegacy marks a project made before the pan law (specs/006); it only drives the Mixer's OLDER PROJECT badge"
- [X] T018 [P] [US2] In `src/managed/Nota.Infrastructure/Persistence/ProjectModel.cs` add `public MixerDto? Mixer { get; set; }` to `ProjectDocument` and `public sealed class MixerDto { public string PanLaw { get; set; } = "balance"; public bool PanLawLegacy { get; set; } [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; } }` with a comment: absent `mixer` = a pre-v21 project = Balance + legacy
- [X] T019 [US2] In `src/managed/Nota.Infrastructure/Persistence/ProjectService.cs` set `CurrentFormatVersion = 21`, and in `src/managed/Nota.Infrastructure/Persistence/ProjectMigrations.cs` append `new Migration(20, root => { if (root["mixer"] is null) root["mixer"] = new JsonObject { ["panLaw"] = "balance", ["panLawLegacy"] = true }; })` with the comment from [contracts/project-format-v21.md](contracts/project-format-v21.md) § Migration (check the JSON naming policy used by the serializer — camelCase — and match it)
- [X] T020 [US2] In `src/managed/Nota.Infrastructure/Persistence/ProjectService.cs` capture: `doc.Mixer = new MixerDto { PanLaw = engine.PanLaw == PanLaw.Stereo ? "stereo" : "balance", PanLawLegacy = transport.PanLawLegacy }` (thread `TransportState` into capture the same way `Transport` is filled, ~line 61); apply: right after `engine.Reset()` (~line 436) call `engine.SetPanLaw(law, checkpoint: false)` where `law` = Stereo for `"stereo"`, Balance for `"balance"` or a null `Mixer`; any other string → Balance plus a load warning "Unknown pan law '…' — using Balance"
- [X] T021 [US2] In `src/managed/Nota.Infrastructure/Persistence/ProjectStore.cs` map `doc.Mixer?.PanLawLegacy ?? true` into `ProjectLoadResult.Transport.PanLawLegacy` (~line 25, where the transport is built)
- [X] T022 [P] [US2] In `src/managed/Nota.Infrastructure/Persistence/VersionDiff.cs` list a change of `mixer.panLaw` between two versions as the sound change "Pan law" (pattern: the `master.devices` comparison ~line 82)
- [X] T023 [US2] In `src/managed/Nota.App/MainWindow.Recovery.cs` / `MainWindow.Files.cs`: add `private bool _panLawLegacy;` to `MainWindow`; set it from `ProjectLoadResult.Transport.PanLawLegacy` wherever a project or autosave is loaded; set `false` where a new project is built (`MainWindow.Files.cs` ~line 187 and `MainWindow.Recovery.cs` ~line 97, both after `ProjectTemplate.ApplyDefault`); pass it as the last argument in `TransportSnapshot()`

**Checkpoint**: US2 tests pass; a 0.52.x project opens, sounds the same, and stays marked after save.

---

## Phase 5: User Story 3 — Switching the pan law of a project (Priority: P2)

**Goal**: the Mixer footer shows and switches the law, click-free, undoable, with the OLDER PROJECT and BAL badges.

**Independent Test**: open a legacy project, switch to Stereo while playing, hear the change without a click, undo, save/reopen — per [quickstart.md](quickstart.md) § 2.

### Tests for User Story 3

- [X] T024 [US3] In `tests/Nota.SmokeTest/PanLawTests.cs` add the click test (write before T025, confirm it fails): two-tone WAV track at pan −1, render 1 s under Balance into a buffer in blocks of 256 frames via repeated `RenderOffline` calls (keep transport playing), call `SetPanLaw(Stereo)` between two blocks, continue 0.5 s; assert the largest sample-to-sample step on each channel across the switch ≤ 1.5 × the largest step of the same channel in the steady state before it (SC-004); also flip back within 5 ms of the first flip and assert the same bound

### Implementation for User Story 3

- [X] T025 [US3] In `src/native/nota.engine/src/Engine.h` add audio-thread-owned members `PanLaw renderedPanLaw_ = PanLaw::Balance; int32_t panLawXfadeLeft_ = 0; int32_t panLawXfadeLen_ = 0; float panLawXfadeFrom_ = 0.f;` (comment: law crossfade state, render thread only, 006) and in `src/native/nota.engine/src/Engine_Render.cpp` implement the crossfade from [data-model.md](data-model.md) § Render state: at the start of each block in `processBlock` compute `panLawXfadeLen_ = max(1, int(0.010 * sampleRate))` from the rate this block renders at; if `g->panLaw != renderedPanLaw_` and no fade runs, start one; if the law flips back mid-fade, restart from the current mix point (`panLawXfadeFrom_`). In the track and return stages, while a fade runs build `mOld = panMatrix(pan, vol, renderedPanLaw_)` and `mNew = panMatrix(pan, vol, g->panLaw)` and lerp the four coefficients per sample with `t = from + (1 − from) · (done + i) / len`; after all stages of the block advance the counter by `frames` and, at 0, set `renderedPanLaw_ = g->panLaw`. No allocation, no lock; the no-fade path is the T014 code unchanged. Also set `renderedPanLaw_` directly (no fade) when the graph is replaced by `nota_engine_reset` or a project load while stopped, so a load never fades
- [X] T026 [US3] In `src/managed/Nota.App/MixerView.cs` `Footer()`: height 34 → 44; between the Show toggles and the crossfader add a 1 px divider (20 px tall, `BorderDefault`) and the PAN LAW group per [contracts/ui-mixer-footer.md](contracts/ui-mixer-footer.md): label "PAN LAW" (10 px, bold, letter-spacing .14em, `TextTertiary`), the shell segmented control (`Border.segmented` + `ToggleButton.seg` classes from `Theme/NotaTheme.axaml`) with "Stereo" / "Balance" and the tooltips "Panning moves the whole stereo image; nothing is dropped" / "Panning attenuates the opposite channel (legacy behaviour)", the hint `TextBlock` (11 px, `TextTertiary`) "Pan moves the whole image of stereo tracks and returns" / "Pan attenuates the opposite channel, as in earlier versions", and the OLDER PROJECT badge (Mono 9 px, letter-spacing .18em, 18 px tall, 1 px `BorderDefault`, radius 3, `TextTertiary`, tooltip "Projects saved before stereo pan law open with Balance so existing mixes do not change"). Clicking the unselected segment calls `_engine.SetPanLaw(law, checkpoint: true)`; the selected one does nothing
- [X] T027 [US3] In `src/managed/Nota.App/MixerView.cs` `PanRow`: for group strips (`ti.IsGroup`) add the BAL badge under the "PAN" label (Mono 7 px, letter-spacing .12em, 11 px tall, 1 px `BorderDefault`, radius 3, `TextTertiary`, tooltip "Groups keep a unity-centre balance so grouping stays level-transparent"), collected in a `List<Control> _balBadges` cleared in `Refresh()`
- [X] T028 [US3] In `src/managed/Nota.App/MixerView.cs` add `public Func<bool>? IsOlderProject { get; set; }` and `SyncPanLaw()` that reads `_engine.PanLaw` and sets the selected segment, the hint text, OLDER PROJECT visibility (`IsOlderProject?.Invoke() == true && law == Balance`) and every `_balBadges` visibility (`law == Stereo`), touching controls only when the law or the legacy flag changed since the last call; call it at the end of `Refresh()` and in `UpdateMeters()`. In `src/managed/Nota.App/MainWindow.axaml.cs` (where `_mixer` is constructed) set `_mixer.IsOlderProject = () => _panLawLegacy;`

**Checkpoint**: US3 tests pass; the footer works per quickstart § 2.

---

## Phase 6: Polish & Cross-Cutting Concerns

- [X] T029 UI verification per [quickstart.md](quickstart.md) § 2 with the headless/in-app harness (isolate with `NOTA_DATA_DIR`): new project → Stereo + BAL on a group; a 0.52.x project → Balance + OLDER PROJECT; switch during playback, ⌘Z from the main window and from the Mixer, save/reopen; screenshots of the footer in Ember Graphite and Ember Paper compared with `nota-design/Nota Mixer v2.html`
- [X] T030 [P] Performance sanity: time `RenderOffline` of the smoke-test project with 32 stereo tracks panned −0.5 under Balance vs Stereo; Stereo within +5 % (plan § Performance Goals). Record the numbers in the PR description, not in code

### Nota closing tasks

- [X] T031 Run `scripts/build.sh` until native + managed + smoke test are green (and `scripts/build-linux.sh` / `scripts/build-win.ps1` if available on this machine; otherwise note CI covers them)
- [X] T032 AGPL-3.0-only SPDX two-line header on every new source file — `tests/Nota.SmokeTest/PanLawTests.cs` (and the `PanLaw` enum file if T006 created one)
- [X] T033 New dependency: N/A
- [X] T034 `CHANGELOG.md` `[Unreleased]`: Added — "Stereo pan law: panning a stereo track or return moves its whole image instead of dropping a channel. New projects use it; the Mixer footer shows and switches the project's pan law." Changed — "Existing projects keep the Balance pan law and sound exactly as before (marked Older project in the Mixer)." Follow the repo's entry style
- [X] T035 FEATURES.md via the `nota-features` skill (Tracks and mixer: pan law); user docs EN + RU via the `nota-docs` skill (Mixer page: a "Pan law" section — the two laws, why older projects keep Balance, the BAL badge on groups — with the footer screenshot in both themes); shortcuts: N/A
- [X] T036 Engine behaviour change → smoke test extended: done in T008–T012, T016, T024 — confirm all `PanLawTests` labels appear and pass in the full run and with `--panlaw`

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (T001)** → **Foundational (T002–T010)** → user stories → **Polish (T029–T036)**
- **US1 (T011–T015)** and **US2 (T016–T023)** both depend only on Foundational
- **US3 (T024–T028)** depends on Foundational and on T014 (the crossfade wraps the
  matrix pan stage); its UI (T026–T028) uses `_panLawLegacy` from T023

### Within the phases

- T002 → T003 → T004 → T005 → T007; T006 ∥ T007 after T005
- T008 → T009 → T010 (same file)
- Tests in one file are sequential: T011 → T012 → T016 → T024
- T013 → T014; T015 after T006
- T017 ∥ T018 ∥ T022; T019 → T020 → T021; T023 after T017 + T021
- T025 after T014; T026 → T027 → T028 (same file)

### Parallel Opportunities

- T006 and T007 (Application vs Infrastructure)
- After Foundational: engine work for US1 (T013–T014) alongside managed persistence for US2 (T017–T022)
- T017, T018, T022 touch different files
- T030 alongside T029

## Parallel Example: after Foundational

```bash
Task: "T013–T014 Stereo pan matrix in src/native/nota.engine/src/Engine_Render.cpp"
Task: "T017 TransportState.PanLawLegacy in src/managed/Nota.Application/Ports/IProjectStore.cs"
Task: "T018 MixerDto in src/managed/Nota.Infrastructure/Persistence/ProjectModel.cs"
Task: "T022 Pan law in src/managed/Nota.Infrastructure/Persistence/VersionDiff.cs"
```

## Implementation Strategy

### MVP

US1 alone changes how every new project sounds, but without US2 an *old* project
would also open with the engine default — which is Balance, so old projects are
already safe after Foundational + US1. US2 adds the explicit format and the legacy
mark; US3 adds the switch. Ship **all three together** (one PR, constitution V):
US1 without US3 would leave users unable to see or change the law.

### Incremental order

1. T001–T010 → build green, nothing audible changed
2. US1 → new projects pan in stereo
3. US2 → format v21, older-project mark
4. US3 → crossfade + Mixer footer
5. Polish → UI verification, docs, final build

## Notes

- Do not commit or push — the user reviews and commits.
- `.specify/feature.json` points at `specs/007-midi-note-editing` (another session); run any spec-kit script for this feature with `SPECIFY_FEATURE_DIRECTORY=specs/006-stereo-pan-law SPECIFY_FEATURE_NO_PERSIST=1`.

## Phase 7: Convergence

- [X] T037 Reconcile the undo label in FR-009 ("one undo step (labelled "pan law")"): Nota's engine undo has no step labels (`nota_engine_undo`, research R7), so either drop the label from the spec via `/speckit-clarify` or record it as out of scope — no code change unless a labelled-undo system is specified separately per FR-009 (partial)
- [X] T038 Make a project load / `nota_engine_reset` take the new pan law without the 10 ms crossfade (in `src/native/nota.engine/src/Engine_Render.cpp` / `Engine.cpp`: when the graph is replaced by reset or load, set `panLawFrom_ = panLawTo_ = law` and `panLawPos_ = 1` on the audio side), or record in `specs/006-stereo-pan-law/research.md` R4 why the fade on load is kept per T025 / data-model § Render state (partial)
- [X] T039 Add a sample-exact null test to `tests/Nota.SmokeTest/PanLawTests.cs`: render the v20 fixture project and compare it per sample with the Balance law computed from the source WAV (`cos θ·L`, `sin θ·R` × track volume), asserting the residual is below −120 dBFS per SC-002 (partial)

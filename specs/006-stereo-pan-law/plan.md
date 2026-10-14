# Implementation Plan: Stereo Pan Law

**Branch**: `006-stereo-pan-law` | **Date**: 2026-10-13 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/006-stereo-pan-law/spec.md`

## Summary

A project-wide pan law, **Stereo** or **Balance**, decides how track and return pan
is applied to a stereo signal. Stereo folds the far channel into the near side
(a mono fold at full pan), and is identical to today's Balance at centre and for
mono material. The law is a field of the engine's `Graph` snapshot, so undo/redo
and the audio thread get it for free; a flip during playback crossfades over 10 ms.
Projects get format v21 with a `mixer` object; the v20 → v21 migration marks every
older project as Balance + legacy, so old mixes render unchanged. New projects start
with Stereo. The Mixer footer gets the **PAN LAW** control, the **OLDER PROJECT**
badge and a **BAL** badge on group strips, per `nota-design/Nota Mixer v2.html`.
See [research.md](research.md).

## Nota: Read Before Planning

- **Engine work** → [`ARCHITECTURE.md`](../../ARCHITECTURE.md): AR-5 (the law travels
  in the immutable graph snapshot), AR-6 (crossfade state is preallocated
  audio-thread members, no alloc/lock), AR-7 (two new C ABI functions).
- **UI work** → [`DESIGN.md`](../../DESIGN.md) (shell segmented control, badges) and
  the `nota-design` skill.
- **Anything** → [`CONTRIBUTING.md`](../../CONTRIBUTING.md).

## Technical Context

**Language/Version**: C++20 (engine), C# / .NET 10 (managed app)

**Primary Dependencies**: Avalonia (existing); no new dependency

**Storage**: `project.json` format v20 → **v21** (`mixer.panLaw`, `mixer.panLawLegacy`);
migration via `ProjectMigrations`

**Audio-Thread Impact**: The track and return pan stages in `Engine_Render.cpp`
switch from two gains to a 2×2 matrix (two extra multiply-adds per sample, only
for the side being folded toward). The law is read from the live graph (AR-5).
A law flip runs a 10 ms crossfade using three preallocated `Engine` members
(`renderedPanLaw_`, `panLawXfadeLeft_`, `panLawXfadeLen_`). No allocation, lock or
I/O (AR-6). With no flip and law = Balance, the inner loop is unchanged →
bit-identical renders.

**Testing**: `scripts/build.sh` → smoke test gains `PanLawTests` (deterministic stereo
WAV, Goertzel per channel); UI via the headless/in-app harness
([quickstart.md](quickstart.md))

**Target Platform**: macOS / Windows / Linux desktop app

**Project Type**: desktop-app (engine + managed UI)

**Performance Goals**: pan stage cost ≤ +5 % of today's per-track post-fader loop;
no measurable change in total render time on the smoke-test project

**Constraints**: Balance output bit-identical to 0.52.x (SC-002); law flip click-free
(SC-004); mono exact (FR-004)

**Scale/Scope**: engine ~150 lines + 15 mechanical call-site edits; managed ~200
lines; smoke test ~250 lines

## Constitution Check

| Principle | Status |
|---|---|
| I. Real-time audio safety | ✅ Law delivered by snapshot (AR-5); crossfade state preallocated; no alloc/lock/I/O on the audio thread |
| II. Boundary discipline | ✅ Two new functions in `nota_engine.h` (`nota_engine_set_pan_law`, `nota_engine_pan_law`), C types only |
| III. Licensing hygiene | ✅ One new source file (`tests/Nota.SmokeTest/PanLawTests.cs`) with the SPDX header; no new dependency |
| IV. Verification gate | ✅ Engine behaviour change → smoke test extended (`PanLawTests`); new C ABI covered |
| V. One logical change | ✅ The pan law end to end. The `inheritGraphLevel` helper is the minimum needed to add a graph field safely — kept in this change, not a separate refactor |
| VI. Docs in sync | ⏳ CHANGELOG `[Unreleased]`, `nota-features`, `nota-docs` (Mixer page, EN + RU) |
| VII. Spec-first | ✅ This run |
| VIII. Rules not weakened | ✅ |

Post-design re-check: unchanged — all gates pass.

## Design

### 1. Engine — the law in the graph

- `Graph.h`: `enum class PanLaw : int32_t { Balance = 0, Stereo = 1 };` and
  `PanLaw panLaw = PanLaw::Balance;` on `Graph`.
- `Graph.h`: `inline void inheritGraphLevel(Graph& dst, const Graph& src)` copying
  `sceneCount`, `scenes`, `masterVolume`, `masterTrack`, `panLaw`; update the
  footgun comment to point at it.
- Replace the hand-copied graph-level lines with `inheritGraphLevel(*g, *authoring_)`
  at every production fresh-graph site (15):
  `Engine.cpp:520` (republishWithTrackRaw — keep its `masterTrack` override after the
  call), `Engine_Modulation.cpp:309`, `Engine_Session.cpp:113` (keep its `sceneCount
  = newCount` after the call), `Engine_Session.cpp:861`, `Engine_Tracks.cpp:461, 1382,
  1502, 1534, 1564, 1619, 1754, 1896, 2439, 2491, 2686`. Self-test graphs in
  `Engine_Automation.cpp` (75, 377, 430) and the reset graph (`Engine.cpp:570`) stay
  default (Balance).
- `Engine`: `bool setPanLaw(PanLaw law, bool checkpoint)` — no-op if equal; else
  `auto g = make_shared<Graph>(*authoring_); g->panLaw = law;` then `publish(g)`
  (checkpoint) or `publishRaw(g)`. `PanLaw panLaw() const`.

### 2. Engine — render ([research R1, R4](research.md))

- One `static inline` helper in `Engine_Render.cpp`:
  `PanMatrix panMatrix(float pan, float vol, PanLaw law)` → `{ll, lr, rl, rr}` per R1.
- Track stage (`Engine_Render.cpp:1264`) and return stage (`:1403`): build the
  matrix from the live graph's law; inner loop computes
  `l = ll*inL + lr*inR; r = rl*inL + rr*inR`. When `lr == 0 && rl == 0` (Balance, or
  Stereo at C) use the existing two-gain loop so Balance stays bit-identical.
- Crossfade: at the start of the block, if `g->panLaw != renderedPanLaw_` and no
  fade is running, start one (`panLawXfadeLen_` = 10 ms at the current rate, recomputed
  wherever the engine's sample rate is set). During a fade each stage builds both matrices and lerps per sample;
  after the last stage of the block, advance `panLawXfadeLeft_` by `frames` and,
  at 0, set `renderedPanLaw_`. The fade progress is per block-sample, shared by all
  stages, so every track crosses at the same moment.
- Group stage (`:1337`) untouched (FR-010).
- Freeze, export, resampling, route taps and sends already read `l`/`r` after the
  pan stage → covered (FR-011).

### 3. C ABI ([contract](contracts/c-abi-pan-law.md))

`nota_engine.h` + `nota_engine_core.cpp`: `NOTA_PAN_LAW_*`, `nota_engine_set_pan_law`,
`nota_engine_pan_law`.

### 4. Managed — engine port and persistence ([contract](contracts/project-format-v21.md))

- `Nota.Application`: `enum PanLaw`, `IAudioEngine.PanLaw` / `SetPanLaw`;
  `TransportState` gains `bool PanLawLegacy = false` (it is the App-owned values
  record that crosses the persistence boundary).
- `Nota.Infrastructure`: P/Invoke (`NativeMethods.cs`), `NotaEngine` wrapper,
  `MixerDto` + `ProjectDocument.Mixer`, `CurrentFormatVersion = 21`, migration
  v20 → v21, capture/apply in `ProjectService` (apply right after `engine.Reset()`,
  `checkpoint: false`), `ProjectStore` maps `PanLawLegacy` into
  `ProjectLoadResult.Transport`, `VersionDiff` lists a pan-law change as "Pan law".
- `ProjectTemplate.ApplyDefault`: `engine.SetPanLaw(PanLaw.Stereo, checkpoint: false)`
  before `ClearUndoHistory()` (FR-007).
- `MainWindow`: `_panLawLegacy` field — set from the load result, `false` on New and
  recovery-from-template; `TransportSnapshot()` passes it; `MixerView` reads it via a
  `Func<bool> IsOlderProject` callback.

### 5. Managed — Mixer UI ([contract](contracts/ui-mixer-footer.md))

- `MixerView.Footer()`: height 34 → 44; after the Show toggles, a 1 px divider and
  the PAN LAW group (label, shell segmented control, hint, OLDER PROJECT badge).
  Crossfader placeholder stays right.
- `PanRow` for group strips: a BAL badge under the PAN label, kept in
  `_balBadges`.
- `SyncPanLaw()` — sets the segment, hint, badge and BAL visibility from
  `_engine.PanLaw` and `IsOlderProject()`; called from `Refresh()` and
  `UpdateMeters()` (cheap getter; only touches controls when the value changed).

### 6. Smoke test ([quickstart](quickstart.md) § 1)

- New `tests/Nota.SmokeTest/PanLawTests.cs` (SPDX header), wired in `Program.cs` in
  the main run and behind `--panlaw`, with the checks listed in quickstart § 1.
- A v20 fixture JSON is built in code (no binary fixture file).

### 7. Docs

- `CHANGELOG.md` `[Unreleased]`: **Added** — Stereo pan law: panning a stereo track
  or return moves its whole image instead of dropping a channel; new projects use
  it; the Mixer footer shows and switches the project's pan law. **Changed** —
  nothing for existing projects: they keep the Balance law (marked "Older project").
- `nota-features` (FEATURES.md, Tracks and mixer), `nota-docs` (Mixer page, EN + RU,
  footer screenshot in both themes).

## Project Structure

### Documentation (this feature)

```text
specs/006-stereo-pan-law/
├── spec.md
├── plan.md              # this file
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── c-abi-pan-law.md
│   ├── project-format-v21.md
│   └── ui-mixer-footer.md
├── checklists/
│   └── requirements.md
└── tasks.md             # /speckit-tasks
```

### Source Code (repository root)

```text
src/native/nota.engine/
├── include/nota/nota_engine.h      # NOTA_PAN_LAW_*, set/get
└── src/
    ├── Graph.h                     # PanLaw, Graph::panLaw, inheritGraphLevel()
    ├── Engine.h / Engine.cpp       # setPanLaw/panLaw, crossfade members, :520 site
    ├── Engine_Render.cpp           # panMatrix, track + return stages, crossfade
    ├── Engine_Tracks.cpp           # 11 fresh-graph sites → inheritGraphLevel
    ├── Engine_Session.cpp          # 2 sites
    ├── Engine_Modulation.cpp       # 1 site
    └── nota_engine_core.cpp        # C ABI
src/managed/
├── Nota.Application/Engine/IAudioEngine.cs, Ports/IProjectStore.cs (TransportState)
├── Nota.Infrastructure/Interop/NativeMethods.cs, NotaEngine.cs
├── Nota.Infrastructure/Persistence/ProjectModel.cs, ProjectService.cs,
│   ProjectMigrations.cs, ProjectStore.cs, ProjectTemplate.cs, VersionDiff.cs
└── Nota.App/MixerView.cs, MainWindow.Files.cs, MainWindow.Recovery.cs
tests/Nota.SmokeTest/PanLawTests.cs, Program.cs
CHANGELOG.md, FEATURES.md
```

**Structure Decision**: Engine + managed changes in existing projects; one new test
file. No new projects.

## Out of Scope / Known Gaps

- Ramping ordinary pan/volume changes (zipper on fast automation) — research R4.
- Group pan law, clip pan envelopes, rack-chain pans (FR-010).
- MCP / Remote exposure of the law (pan values and display are unchanged; an MCP
  `set_pan_law` tool can follow with the Stereo Map spec).
- The mock's Strips | Stereo Map tabs and strip redesign — Stereo Map spec.
- A pan-law preference for new projects (always Stereo).

## Complexity Tracking

No constitution violations.

# Implementation Plan: Stereo Map (MVP)

**Branch**: `008-stereo-map` | **Date**: 2026-10-14 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/008-stereo-map/spec.md`

## Summary

The Mixer window gets a **Strips | Stereo Map** switch in its own title bar. The Stereo Map
is one custom-drawn control: every track, return and the master as a handle at (pan, fader)
with a cloud showing where its sound really is (balance), how wide (1 − correlation) and how
loud, plus a master correlation bar. The engine measures each track's post-fader, post-pan
signal in the existing meter pass — one extra multiply-add per sample (`ΣL·R`) and three
running averages — and hands all rows over in one C ABI call per frame. Dragging a handle is
one mix gesture over pan and volume (spec 009), with the same automation rules as the strip
controls; clicking selects the track everywhere. See [research.md](research.md).

## Nota: Read Before Planning

- **Engine work** → [`ARCHITECTURE.md`](../../ARCHITECTURE.md): AR-6 (the audio thread only
  adds a sum and updates three atomics per track per segment), AR-7 (one new C ABI call).
- **UI work** → [`DESIGN.md`](../../DESIGN.md) and the `nota-design` skill; the design
  reference `nota-design/Nota Mixer v2.html`.
- **Anything** → [`CONTRIBUTING.md`](../../CONTRIBUTING.md).

## Technical Context

**Language/Version**: C++20 (engine), C# / .NET 10 (managed app)

**Primary Dependencies**: Avalonia (existing); no new dependency

**Storage**: `Settings.MixerView` (user preference); nothing in the project file

**Audio-Thread Impact**: the track, return and master loops in `Engine_Render.cpp` add
`sLR += l·r` next to the existing `sqL`/`sqR`; after each loop, three exponential averages
are updated with one `exp` per segment (cached per sample rate / frame count where possible)
and stored in atomics. No allocation, no lock, no extra pass over the buffer.

**Testing**: `scripts/build.sh` → `StereoMeasureTests` (WAV material); headless UI harness
for the map ([quickstart.md](quickstart.md))

**Target Platform**: macOS / Windows / Linux desktop app

**Project Type**: desktop-app (engine + managed UI)

**Performance Goals**: 60 fps with 128 tracks (SC-003): one custom `Render`, one native call
per frame; ≤ 1 % of the audio block time added with 64 tracks (SC-004)

**Constraints**: map Y equals the strip fader's mapping (linear gain 0…1.5); exact text and
tokens from [contracts/ui-stereo-map.md](contracts/ui-stereo-map.md)

**Scale/Scope**: engine ~80 lines; managed ~700 lines (map control ~500, Mixer layout and
title bar, settings, selection hook); smoke ~200 lines

## Constitution Check

| Principle | Status |
|---|---|
| I. Real-time audio safety | ✅ One sum per sample + three atomics per segment; no alloc / lock / I/O |
| II. Boundary discipline | ✅ `NotaStereoMeasure` + `nota_engine_stereo_measures` |
| III. Licensing hygiene | ✅ New files with SPDX; no dependency |
| IV. Verification gate | ✅ Engine behaviour added → `StereoMeasureTests` |
| V. One logical change | ✅ The map MVP; V2/V3 are separate specs |
| VI. Docs in sync | ⏳ CHANGELOG, `nota-features`, `nota-docs` (Mixer page + shots) |
| VII. Spec-first | ✅ |
| VIII. Rules not weakened | ✅ |

Post-design re-check: unchanged — all gates pass.

## Design

### 1. Engine — measuring ([research R1, R2](research.md))

- `Track.h`: `std::atomic<float> stereoL_{0}, stereoR_{0}, stereoLR_{0}` + `setStereo(…)` /
  getters. `Engine.h`: master counterparts.
- `Engine_Render.cpp`: in the track stage, the return stage and the master loop add
  `sLR += l * (double)r`; after each loop update the averages with
  `a = 1 − exp(−frames / (0.3 · sr))`; a track that is `!toMix` (muted / solo-gated) feeds
  zeros. Groups untouched.
- C ABI ([contract](contracts/c-abi-stereo-measures.md)): `NotaStereoMeasure`,
  `nota_engine_stereo_measures` — derives level / balance / width / correlation (R2) from the
  authoring graph's tracks (not groups) in track order, the master last.

### 2. Managed port

`StereoMeasure` record struct (blittable) in `EngineContracts.cs`;
`IAudioEngine.GetStereoMeasures(StereoMeasure[] buffer)` → count; P/Invoke + wrapper.
`Settings.MixerView` (`"strips"` | `"map"`).

### 3. Mixer window and layout ([contract](contracts/ui-stereo-map.md))

- `MixerWindow`: own 36 px title bar (`base(36)` like `DeviceWindow`), caption left, the
  Strips | Stereo Map segments centred (`ClipEditorKit.Segments`); Linux: the same row at the
  top of the body.
- `MixerView.View` (enum): rebuilds the content grid — Strips: strips only; Stereo Map:
  `StereoMapView` (`*`) + strips scroller (max 440 px). Reads/writes `Settings.MixerView`.

### 4. `StereoMapView` (new, `Nota.App/StereoMapView.cs`)

- Pulls per frame: track infos (id, name, colour, pan, volume, mute, solo, return/group flags),
  `GetStereoMeasures`, `MasterVolume`, the selected id (callback from `MainWindow`).
- Renders grid, clouds, handles, labels (greedy, R7), selection ring, tooltip, master
  correlation bar, per the UI contract; `RequestAnimationFrame` loop while attached and visible.
- Input: hit-test handles (16 px + 4 px slop, topmost first); drag → `MixGesture(Volume, Pan)`
  begin → `BeginAutomationWrite` (volume + pan) → live `SetTrackPan` / `SetTrackVolume` from
  the deltas (Shift: dominant axis) → `EndAutomationWrite` → gesture end; double-click /
  ⌥ double-click → one-shot reset; click without moving → `SelectTrack(id)` callback.
  Master: select only.
- Callbacks set by `MainWindow`: `SelectedTrackId` (`() => Timeline.SelectedTrackId`),
  `SelectTrack` (`id => Timeline.SelectTrackFromOutside(id)`).

### 5. Selection hook

`ArrangementView.SelectTrackFromOutside(int id)`: `Select(id, -1)` + `TrackSelected?.Invoke(id)`.

### 6. Smoke test + docs

- `tests/Nota.SmokeTest/StereoMeasureTests.cs` (new, SPDX) per the contract + SC-004 check;
  `Program.cs` full run + `--stereomap`.
- `CHANGELOG.md`, `nota-features`, `nota-docs` (Mixer page "Stereo Map" section, the `mixer`
  shots scene gains `view: map`).

## Project Structure

### Documentation (this feature)

```text
specs/008-stereo-map/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── c-abi-stereo-measures.md
│   └── ui-stereo-map.md
├── checklists/requirements.md
└── tasks.md             # /speckit-tasks
```

### Source Code (repository root)

```text
src/native/nota.engine/
├── include/nota/nota_engine.h        # NotaStereoMeasure, nota_engine_stereo_measures
└── src/
    ├── Track.h                        # stereo energy atomics
    ├── Engine.h                       # master energies
    ├── Engine_Render.cpp              # ΣL·R + averages in track / return / master loops
    └── nota_engine_core.cpp           # C ABI (derivation)
src/managed/
├── Nota.Application/Engine/EngineContracts.cs, IAudioEngine.cs; Settings.cs
├── Nota.Infrastructure/Interop/NativeMethods.cs, NotaEngine.cs
└── Nota.App/StereoMapView.cs (new), MixerView.cs, MixerWindow.cs, ArrangementView.cs,
    MainWindow.axaml.cs (callbacks)
tests/Nota.SmokeTest/StereoMeasureTests.cs (new), Program.cs
CHANGELOG.md, FEATURES.md
```

**Structure Decision**: existing projects; two new source files.

## Out of Scope / Known Gaps

- Everything listed as V2 / V3 in the spec.
- The Y axis is linear in gain to match the fader (research R3): quiet tracks bunch near the
  bottom. A dB taper for both the fader and the map would be its own change.
- Width and balance formulas differ from the feature description (research R2) — flagged
  for review.
- Pre-fader views are not offered.

## Complexity Tracking

No constitution violations.

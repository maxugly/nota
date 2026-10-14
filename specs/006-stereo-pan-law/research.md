# Research: Stereo Pan Law

## R1. The Stereo pan curve

**Decision**: per track/return, a 2×2 gain matrix applied after the fader. For pan
`p ∈ [−1, 1]`, `θ = (p + 1)·π/4`, `h = cos(π/4)`:

| | out L | out R |
|---|---|---|
| **Balance** (today) | `cos θ · L` | `sin θ · R` |
| **Stereo**, `p ≤ 0` | `(cos θ − b)·L + b·R` | `sin θ · R` |
| **Stereo**, `p ≥ 0` | `cos θ · L` | `b·L + (sin θ − b)·R` |

with `b = 0.5·|p|` (the share of the far channel folded into the near side). All
multiplied by the fader volume, as today.

Checks against the spec:

- **FR-003 (centre identical)**: `p = 0 → b = 0`, `cos θ = sin θ = h` → exactly
  Balance.
- **FR-004 (mono exact)**: with `L = R = x`, the near side is `(cos θ − b + b)·x =
  cos θ·x` and the far side is `sin θ·x` → exactly Balance at every `p` (not
  merely within 0.1 dB — the identity is algebraic; float rounding only).
- **FR-002 (nothing dropped)**: at `p = −1`: out L = `0.5·L + 0.5·R` (mono fold),
  out R = 0. Both channels' content is present at −6 dB each on the left.
- **FR-005 (≤ 3.5 dB)**: unrelated L/R of equal power P. Centre total = P. At
  `p = −1`: `0.25P + 0.25P = 0.5P` → −3.01 dB. At `p = −0.5`: `(0.674² + 0.25²)P +
  0.383²P ≈ 0.663P` → −1.8 dB. Worst case is the edge, −3.01 dB.
- **Image narrows toward the edge**: the cross term grows linearly with `|p|` until
  the image is a mono fold at the edge.

**Rationale**: the only free choice under FR-003 + FR-004 is how the near-side gain
`cos θ` is split between the near and far channels. A linear far-share reaching ½ at
the edge gives the textbook mono fold `(L+R)/2` at hard pan (what Logic's and
Ableton's stereo-pan modes converge to) and stays inside FR-005 everywhere.

**Alternatives considered**:

- *Complement fold* `near = h·L + (cos θ − h)·R` (the curve the design mock uses
  for its meter estimate): also exact for mono and centre, and loses only −2.3 dB at
  the edge, but the far channel arrives at only `1 − h ≈ 0.29` (−10.7 dB) — the
  image never collapses, the far material stays audibly buried. Rejected.
- *Width-then-balance* (narrow with M/S first, then balance-pan the result): extra
  M/S stage per track, and it is not exact for mono under the balance step (mono
  through a balance at `p ≠ 0` is fine, but the width stage then changes nothing —
  equivalent, more arithmetic). Rejected as more code for the same result.
- *Constant-power for unrelated material* (scale so unrelated L/R keep P): breaks
  FR-004 for mono (+3 dB at the edge). Rejected by the spec.

## R2. Where the law lives in the engine

**Decision**: a graph-level field `PanLaw panLaw` on `Graph` (`Graph.h`), next to
`masterVolume`. Setting it copies the authoring graph (`make_shared<Graph>(*authoring_)`)
and publishes it — with an undo checkpoint for user edits, without one for load.

**Rationale**:

- The engine's undo restores retained `Graph` snapshots (M6-6) and explicitly does
  *not* cover atomic params. FR-009 needs the switch to be one undo step → the law
  must be snapshot data. Undo/redo then flip it for free, including interleaved
  with structural edits.
- The audio thread already reads `liveGraph_` per block, so the law reaches the
  render with no new command and no lock (AR-5).

**Alternatives considered**: an `std::atomic<int>` on `Engine` (like volume/pan).
Simpler to set, but undo would need a managed-side undo stack interleaved with the
engine's — Nota has none. Rejected.

**Risk — the fresh-graph footgun**: ~20 edit sites build a *fresh* `Graph` and copy
graph-level fields by hand (`sceneCount`, `scenes`, `masterVolume`, `masterTrack`);
`Graph.h` warns about it. A site that forgets `panLaw` would silently reset the law
to its default on that edit. **Mitigation**: add one helper,
`inheritGraphLevel(Graph& dst, const Graph& src)` in `Graph.h`, copying all four
existing fields plus `panLaw`; replace the hand-copied lines at every
`make_shared<Graph>()` site with it (mechanical; the list is in the plan). The smoke
test then proves the law survives one edit of each kind (add/remove/move track,
group, scene add/remove, automation, modulation, clip edit, undo/redo).

## R3. Engine default and app defaults

**Decision**: the engine's default law is **Balance** (empty graph, `nota_engine_reset`).
The app sets **Stereo** in `ProjectTemplate.ApplyDefault` (both File → New and the
launch/recovery workspace go through it) *before* `ClearUndoHistory`, so the law is
part of the baseline and not an undoable step.

**Rationale**: keeps every existing C ABI consumer — the smoke test, MCP golden
tests, offline audition rigs — bit-identical without touching them, and makes
"absent = Balance" true at every layer.

## R4. Click-free switching (FR-009, SC-004)

**Decision**: the render keeps `renderedPanLaw_` (audio-thread-owned, plain member).
When the live graph's law differs from it, the track and return pan stages run a
**10 ms linear crossfade** between the old-law and new-law matrices (per-sample
`lerp` of four coefficients; the pan value itself is unchanged), counted by
`panLawXfadeLeft_` across blocks; when it reaches 0, `renderedPanLaw_` takes the new
law. No allocation, no lock.

**Rationale**: a law flip changes gains for every panned stereo track at once — up
to −6 dB → 0 dB steps on the far channel at hard pan, which clicks on sustained
material. 10 ms is below perceptual smearing and above one block at any supported
buffer size. With no flip the code path is the existing constant-gain loop, so
Balance renders stay bit-identical (FR-006, SC-002).

**Alternatives considered**: ramping every pan change (also fixes zipper on pan
automation) — changes legacy renders by tiny amounts and breaks the bit-identical
guarantee. Out of scope; could be its own change.

## R5. Groups, clip envelopes, rack chains

**Decision**: unchanged (FR-010). Groups keep the unity-centre balance at
`Engine_Render.cpp` (group stage); clip pan envelopes and rack-chain pans are their
own stages and never read the project law.

## R6. Persistence

**Decision**: format **v21**. New top-level `mixer` object in `project.json`:
`{ "panLaw": "stereo" | "balance", "panLawLegacy": bool }`. Migration v20 → v21 writes
`{ "panLaw": "balance", "panLawLegacy": true }` when `mixer` is absent. The DTO
treats a missing `mixer` (defensive) the same way.

**Rationale**: follows the project's migration pipeline (`ProjectMigrations`), and
makes "older project" an explicit, saved fact rather than an inference from
absence — so it survives save + reopen (FR-009c) and project history restores.

**`panLawLegacy`** is managed-only state (it never changes sound, so the engine
does not hold it). It is set by migration, cleared only by creating a new project;
the OLDER PROJECT badge shows when `panLawLegacy && law == Balance`, so switching to
Stereo hides it and undoing back shows it again.

**Alternatives considered**: no format bump, infer "older" from a missing field.
Breaks after the first save (the field is then written). Rejected.

## R7. Undo routing for the switch

**Decision**: `nota_engine_set_pan_law(engine, law, checkpoint)`; the Mixer control
calls it with `checkpoint = 1`. The engine's undo has no labels; the mock's
"Undo pan law" toast is not a Nota pattern today and is not required by the spec.

## R8. Smoke-test signal

**Decision**: a deterministic stereo WAV written with `WavWriter` — a 440 Hz sine
on L and a 1 000 Hz sine on R, −12 dBFS, 2 s — on an audio track, rendered with
`RenderOffline`. Per-tone level is measured with a Goertzel filter per output
channel, so "both materials on the left" is a number, not a guess. Mono case: the
same file with one 440 Hz sine on both channels. (Memory note: synth notes drift
±2 dB between renders; WAV clips are deterministic.)

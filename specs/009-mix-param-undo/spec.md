# Feature Specification: Undo for Volume and Pan

**Feature Branch**: `009-mix-param-undo`

**Created**: 2026-10-14

**Status**: Draft

## Clarifications

### Session 2026-10-14

- Q: Should sends, mute and solo become undoable in the same change? → A: No — volume and
  pan only (plus the master volume); sends, mute and solo can follow in a later spec on the
  same mechanism.
- Q: How should continuous MIDI-controller movement be grouped into undo steps? → A: One
  step per movement, ended by a 1 s pause in that control's messages.

### Session 2026-10-14 (convergence)

- Q: The spec asks for scroll-wheel bursts to be one step, but no volume/pan control reacts
  to the wheel. Add wheel support or drop the rule? → A: Drop it — wheel control of faders is
  a feature of its own; when it arrives it uses the same pause-based gesture.

**Input**: User description: "Undo for volume and pan, everywhere (prerequisite of specs/008-stereo-map, clarified 2026-10-14 option C). Today track/return volume and pan (and the master volume) are not undoable anywhere in Nota. Make every user change of a track's or return's volume or pan, and of the master volume, undoable and redoable with ⌘Z / ⌘⇧Z, interleaved in one history with all other edits. One gesture = one step: a fader/knob drag from press to release; a double-click reset; a typed or dragged dB value; a scroll-wheel burst; a Stereo Map drag (pan + volume together, one step). Sources: Mixer strips, track headers, Return/Master rows, Session mixer, the transport MST, MIDI controller via MIDI Learn, Nota Remote, MCP tools. Never an undo step: automation playback, project load, freeze/live-freeze internals, the new-project template. While recording automation, the lane edit and the value change undo together. Undo restores the exact previous value and every view follows."

## Nota Context *(fill in — drives plan and tasks)*

**Feature area** *(pick the main one)*:

- [ ] Engine (C++, `src/native/nota.engine`) — real-time audio rules apply, see `ARCHITECTURE.md` AR table
- [x] Managed app (C#, `src/managed/*`) — domain, application, infrastructure, presentation — plus the engine's undo history, which today covers structural edits only
- [ ] UI (Avalonia, `src/managed/Nota.Presentation`) — follow `DESIGN.md` (Ember)
- [ ] Plugin hosting (`pluginhost/`) — the only place JUCE is allowed
- [ ] Build / packaging / CI (`scripts/`, `.github/`) — see `BUILD.md`
- [ ] Docs only

**Nota docs to keep in mind** *(links relative to `specs/<slug>/`, where this file lands)*:
[`ARCHITECTURE.md`](../../ARCHITECTURE.md) (AR rules),
[`CONTRIBUTING.md`](../../CONTRIBUTING.md) (licensing, dependency policy, PR
expectations), [`BUILD.md`](../../BUILD.md), [`DESIGN.md`](../../DESIGN.md),
[`FEATURES.md`](../../FEATURES.md).

**Current state (for reference)**:

- One history (⌘Z / ⌘⇧Z, Edit menu, the command palette's undo) covers structural edits:
  tracks, clips, notes, device chains, session slots, automation lanes, the pan law. It
  explicitly does not cover volume, pan, mute, solo, sends, device parameters or the
  transport.
- Volume and pan of tracks and returns, and the master volume, are changed from: the Mixer
  window (knob, fader, dB field — typed or dragged), the arrangement track headers (pan bar,
  mini fader), the Return and Master rows under the tracks, the Session-view mixer, the
  transport's **MST** control, a MIDI controller mapped with MIDI Learn, Nota Remote (phone),
  and the MCP tools `set_track_volume` / `set_track_pan` (AI control).
- Internal operations also set them: project load, freeze / live freeze, the new-project
  template, automation playback.
- Prerequisite of the Stereo Map (specs/008), which requires a map drag to be one undo step.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Take back a fader move (Priority: P1)

A producer drags the vocal fader up by 6 dB, listens, and does not like it. ⌘Z puts the
fader back exactly where it was; ⌘⇧Z brings the 6 dB back. The same works for a pan knob, a
double-click reset and a value typed into the dB field.

**Why this priority**: The basic expectation of any DAW; today a slip on a fader cannot be
taken back at all.

**Independent Test**: In the Mixer, drag a fader, turn a pan knob, reset one with a
double-click and type a dB value; undo each and redo each.

**Acceptance Scenarios**:

1. **Given** a track at 0 dB, **When** the user drags its fader to +6 dB in one press and
   release and presses ⌘Z, **Then** it is back at exactly 0 dB, and ⌘⇧Z returns it to +6 dB.
2. **Given** a pan knob at C, **When** the user turns it to L40 and presses ⌘Z, **Then** it is
   at C again.
3. **Given** a track at −8 dB, **When** the user double-clicks its fader (reset to 0 dB) and
   presses ⌘Z, **Then** it is at −8 dB again.
4. **Given** the dB field, **When** the user types −12 and confirms, **Then** that is one undo
   step; **When** they drag the dB number, **Then** the whole drag is one step.

---

### User Story 2 - One history for everything (Priority: P1)

The producer adds a track, moves the bass fader, switches the pan law to Stereo, pans the
guitar, then presses ⌘Z four times: the guitar pan, the pan law, the bass fader and the new
track come back one by one, in reverse order. Every view — Mixer, track headers, Remote,
Stereo Map — shows the restored values.

**Why this priority**: Undo that skips some edits or jumps out of order is worse than no
undo; mixing and structural edits must share one timeline.

**Independent Test**: Alternate structural edits and volume/pan edits, undo and redo the
whole chain and check each intermediate state.

**Acceptance Scenarios**:

1. **Given** a sequence of edits mixing structure, pan law and volume/pan, **When** the user
   undoes repeatedly, **Then** each step reverts exactly the most recent remaining edit, in
   reverse order, and redo replays them in order.
2. **Given** a new edit after some undos, **When** it is made, **Then** the redo history is
   dropped (as for any edit today).
3. **Given** an undone volume change, **When** the user looks at the Mixer, the track header,
   the Session mixer and a connected phone, **Then** they all show the restored value.
4. **Given** a track deleted after its volume was changed, **When** the user undoes the delete
   and then the volume change, **Then** both come back correctly.

---

### User Story 3 - Every way of mixing is undoable (Priority: P2)

The producer moves a volume from the Session mixer, a pan from a track header, the master
from the transport's MST, a fader from Nota Remote on the phone, a knob on a MIDI controller,
and asks the AI assistant (MCP) to set a level. Each of those is undoable from the computer.

**Why this priority**: Consistency across sources; the main sources (US1) already give most
of the value.

**Independent Test**: Change volume/pan once from each source and undo each.

**Acceptance Scenarios**:

1. **Given** each source listed in Current state, **When** the user changes a volume or pan
   with it, **Then** ⌘Z reverts that change.
2. **Given** a touch on Nota Remote, **When** the finger moves a fader and lifts, **Then** the
   whole touch is one step.
3. **Given** a MIDI controller knob turned continuously, **When** the user stops turning,
   **Then** the movement up to a 1 s pause in that control's messages is one step; turning
   it again after the pause starts a new step.
4. **Given** an MCP call that sets a volume, **When** it completes, **Then** it is one step.

---

### User Story 4 - Things that are not edits stay out of the history (Priority: P1)

While a song with volume automation plays, the faders move by themselves — that never
fills the undo history. Opening a project, freezing a track or starting a new project does
not leave undoable volume steps behind either.

**Why this priority**: Without it a playing song would flood the history and undo would
become useless.

**Independent Test**: Play a song with volume and pan automation for a minute, freeze a
track, open a project; the history gains no volume/pan steps.

**Acceptance Scenarios**:

1. **Given** automation in read mode, **When** the song plays, **Then** no undo steps appear.
2. **Given** a project is opened or a new project created, **When** the user presses ⌘Z,
   **Then** nothing about volume or pan is undone (there is nothing to undo).
3. **Given** a freeze or live freeze, **When** it completes, **Then** any internal volume
   changes it made leave no undo step of their own.
4. **Given** automation recording (write/touch/latch), **When** the user moves a fader in one
   gesture, **Then** ⌘Z reverts the recorded automation and the value together, one step.

### Edge Cases

- **Undo during playback**: the value jumps back immediately; with automation in read mode
  on that parameter, the automation keeps winning, as it does for a manual move today.
- **A gesture with no net change** (press and release without moving, or a drag back to the
  start value): no undo step.
- **Gesture interrupted** (window loses focus, track deleted mid-drag): the change so far is
  one step; nothing is left half-open.
- **Two sources at once** (mouse on the fader while the phone moves the same fader): each
  gesture is its own step, in the order they finished.
- **The master volume** shares the history with track edits even though it is shown in the
  transport.
- **Undo a volume change of a track that no longer exists** (deleted, not undone): that step
  is skipped quietly; the next step is undone instead.
- **Group tracks**: their volume and pan are covered like any track's.
- **History depth**: volume/pan steps count against the existing history limit like any
  other step.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Every user change of a track's, return's or group's **volume** or **pan**, and
  of the **master volume**, MUST be undoable and redoable through the existing history
  (⌘Z / ⌘⇧Z, Edit menu, command palette).
- **FR-002**: These steps MUST share one ordered history with all existing undoable edits;
  undo and redo MUST always act on the most recent step, whatever its kind.
- **FR-003**: One gesture MUST be exactly one step: a mouse drag from press to release; a
  double-click reset; a confirmed typed value; a drag of a dB number; one touch on Nota Remote from finger-down to
  lift; one MIDI-controller movement ended by a 1 s pause; one MCP call; a Stereo Map drag (its pan and volume together).
- **FR-004**: A step MUST restore the exact value from before the gesture (no rounding) and
  redo the exact value after it.
- **FR-005**: A gesture that ends at its starting value MUST NOT add a step.
- **FR-006**: All views and connected devices (Mixer, track headers, Return/Master rows,
  Session mixer, transport MST, Nota Remote, Stereo Map) MUST show the restored value at once.
- **FR-007**: The sources MUST all be covered: Mixer window, track headers, Return/Master
  rows, Session mixer, transport MST, Nota Remote, MCP, and a MIDI controller through MIDI
  Learn (grouping per US3 scenario 3).
- **FR-008**: Automation playback, project load, a new project, freeze / live freeze and
  any other internal setting of volume or pan MUST NOT create steps.
- **FR-009**: While recording automation, one gesture's lane edit and value change MUST undo
  and redo together as one step.
- **FR-010**: A step whose track no longer exists MUST be skipped without error.
- **FR-011**: Undo/redo of volume and pan MUST be click-free during playback no worse than
  moving the same control by hand.
- **FR-012**: The scope is volume and pan (tracks, returns, groups) and the master volume
  only; sends, mute and solo stay out of the history, as today.
- **FR-013**: The automated smoke test MUST cover: a drag-like change (begin, many values,
  end) is one step and restores exactly; a no-change gesture adds no step; volume/pan steps
  interleave in order with structural edits and the pan law switch; automation playback adds
  no steps; a step for a deleted track is skipped; MCP set_track_volume is one step.
- **FR-014**: The change MUST be listed in the changelog, FEATURES.md and the user docs
  (EN + RU) where undo is described.

### Key Entities

- **Mix step** (one undo step): which parameter (track id + volume or pan, or master
  volume), the value before and the value after; a Stereo Map step carries two parameters.
- **Gesture**: a begin–end span from one source in which value changes merge into one step.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100 % of volume/pan changes made by the user from any listed source can be
  undone and redone, landing on the exact previous value.
- **SC-002**: Playing a song with volume and pan automation for 10 minutes adds 0 undo steps.
- **SC-003**: Undo/redo of a volume/pan step takes effect within one screen refresh in every
  open view.
- **SC-004**: A mixed sequence of 50 structural and volume/pan edits undoes and redoes in
  exactly the reverse/forward order, every intermediate state matching.

## Assumptions

- Group tracks are covered like tracks; the master volume is covered although the history
  never covered the transport before (it is a fader the user mixes with).
- Undo steps have no labels (Nota's history has none; specs/006 clarification).
- MCP calls run one at a time; each call is one step.
- History depth stays as it is today; mix steps count toward it.
- Device parameters, sends, mute, solo, arm, tempo and the other transport values are out
  of scope (clarified).
- Volume/pan controls have no scroll-wheel input; wheel bursts are out of scope (clarified).

## Nota Compliance Checklist *(review before implementation; re-check in tasks.md)*

- [x] Every new source file will carry the AGPL-3.0-only SPDX header (two lines, see CONTRIBUTING.md)
- [x] No new dependency — or: license checked, AGPL-compatible, `LICENSES/third-party.md` row planned
- [x] Audio-thread impact considered: no alloc / lock / I/O on the audio thread (AR-6), command queue for data in (AR-4), immutable snapshots for structural edits (AR-5)
- [x] Managed/native crossings go through the pure C ABI in `nota_engine.h` only (AR-7); no JUCE outside `pluginhost/`
- [x] Smoke-test impact assessed: engine behaviour change → extend `tests/Nota.SmokeTest`
- [x] User-visible change → `CHANGELOG.md` `[Unreleased]` entry planned (and FEATURES.md / user docs via the nota-features / nota-docs skills when working with an agent that has them)
- [x] UI change → follows `DESIGN.md` (Ember design language)

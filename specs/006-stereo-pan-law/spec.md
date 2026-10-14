# Feature Specification: Stereo Pan Law

**Feature Branch**: `006-stereo-pan-law`

**Created**: 2026-10-13

**Status**: Draft

## Clarifications

### Session 2026-10-13

- Q: Where does the pan-law switch live — there is no Project Settings window? → A: In
  the Mixer window's bottom bar, as a **PAN LAW** segmented control (Stereo | Balance),
  per the design mock `nota-design/Nota Mixer v2.html`.

### Session 2026-10-14 (convergence)

- Q: FR-009 asked for the undo step to be labelled "pan law", but Nota's undo has no step
  labels anywhere. Add labels or drop the wording? → A: Drop it — the switch is one plain
  undo step like every other; labelled undo would be its own feature.

**Design reference**: [`Nota Mixer v2.html`](../../../nota-design/Nota%20Mixer%20v2.html)
(sibling repo `nota-design`). The same mock also covers the Stereo Map (separate spec);
only its pan-law parts belong here: the PAN LAW control with its hint line, the
OLDER PROJECT badge, the BAL badge on group strips, and undo of the law.

**Input**: User description: "Stereo pan law for tracks and returns. Today the track/return pan stage in the engine is an equal-power balance: L is scaled by cos, R by sin, so a stereo track panned hard left loses its right channel. Change it to a true stereo pan: panning a stereo signal moves the whole image (both channels are folded toward the pan side, nothing is lost, the image narrows toward the edge); mono signals sound identical to today. The pan law is stored per project: new projects use stereo pan; projects saved without the field open with the legacy balance law so existing mixes do not change; Project Settings lets the user switch the law. Applies to tracks and returns (groups keep their level-transparent behaviour unless the spec decides otherwise). Smoke test: a stereo WAV at pan L100 gives ~zero right-channel energy with total energy preserved. This is a prerequisite for the Stereo Map feature (a separate spec)."

## Nota Context *(fill in — drives plan and tasks)*

**Feature area** *(pick the main one)*:

- [x] Engine (C++, `src/native/nota.engine`) — real-time audio rules apply, see `ARCHITECTURE.md` AR table
- [ ] Managed app (C#, `src/managed/*`) — domain, application, infrastructure, presentation
- [ ] UI (Avalonia, `src/managed/Nota.Presentation`) — follow `DESIGN.md` (Ember) — secondary: the Mixer bottom-bar control and badges
- [ ] Plugin hosting (`pluginhost/`) — the only place JUCE is allowed
- [ ] Build / packaging / CI (`scripts/`, `.github/`) — see `BUILD.md`
- [ ] Docs only

**Nota docs to keep in mind** *(links relative to `specs/<slug>/`, where this file lands)*:
[`ARCHITECTURE.md`](../../ARCHITECTURE.md) (AR rules),
[`CONTRIBUTING.md`](../../CONTRIBUTING.md) (licensing, dependency policy, PR
expectations), [`BUILD.md`](../../BUILD.md), [`DESIGN.md`](../../DESIGN.md),
[`FEATURES.md`](../../FEATURES.md).

**Current state (for reference)**:

- A track's and a return's pan control is applied after the fader as an equal-power
  *balance*: the left channel is attenuated as the control moves right and vice versa.
  At centre both channels sit at −3 dB. Panning a stereo source hard left silences its
  right channel — that half of the material is lost, not moved.
- Groups use a different, unity-centre balance so that grouping stays level-transparent.
- Per-clip pan envelopes and rack-chain pans are separate balance stages.
- There is no "Project Settings" window today; project-level values (key, sections,
  graph layout…) are edited where they are shown and saved with the project. The pan
  law follows that pattern: it is edited in the Mixer, where pan lives.
- The Mixer window has a footer (Show Sends / I/O toggles and a disabled A/B
  crossfader placeholder); the PAN LAW control joins it between the two.
- This spec is the prerequisite of the Stereo Map feature, which visualises where each
  track really sits in the stereo field: with a balance law a panned stereo track's
  image does not follow its pan control, which makes such a map misleading.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Panning a stereo track moves its whole image (Priority: P1)

A producer in a new project has a stereo pad (or a stereo drum loop) and pans it to
L60 to make room for a guitar on the right. Both the left and the right parts of the
pad stay audible; the whole image moves to the left and becomes narrower. At hard left
the pad is fully in the left speaker, folded to mono, with nothing missing.

**Why this priority**: It is the core change — today panning a stereo source throws
away half of it, which is what producers coming from other DAWs do not expect.

**Independent Test**: In a new project, play a stereo file whose left and right
channels carry different material; sweep pan from C to L100 and listen/measure that
both materials remain present and move left.

**Acceptance Scenarios**:

1. **Given** a new project and a stereo audio track whose channels carry different
   sounds, **When** pan is set to L100, **Then** the right output is silent and both
   sounds are heard in the left output.
2. **Given** the same track, **When** pan is set to L50, **Then** both sounds are heard,
   the image is shifted left and is narrower than at centre.
3. **Given** the same track at C, **When** playing, **Then** the output is identical to
   what the legacy law produces at C (switching laws does not change a centred track).
4. **Given** a mono source (left and right identical), **When** pan is set to any
   position, **Then** its left and right output levels match the legacy law at that
   position.

---

### User Story 2 - Existing projects sound exactly as before (Priority: P1)

A producer opens a project they mixed with an earlier Nota version. Every track sounds
exactly as it did; nothing in the mix shifts. The project reports that it uses the
legacy (balance) pan law.

**Why this priority**: Silently changing finished mixes is unacceptable; this is the
safety net for the P1 change above.

**Independent Test**: Render a legacy project before and after the change; the renders
are bit-identical (or null-test below −120 dBFS).

**Acceptance Scenarios**:

1. **Given** a project saved before this feature, **When** it is opened, **Then** its
   pan law is Balance and an offline render matches the previous version's render.
2. **Given** that legacy project, **When** it is saved and reopened, **Then** it still
   uses Balance (the law is now stored explicitly).
3. **Given** a new project (blank or from the default template), **When** it is
   created, **Then** its pan law is Stereo.

---

### User Story 3 - Switching the pan law of a project (Priority: P2)

A producer wants an old project to behave like new ones (or a new one to behave like
old ones) and switches the project's pan law. The change is audible immediately during
playback, is undoable, and is saved with the project.

**Why this priority**: Gives control to users who want consistency, but the defaults
of Stories 1–2 already cover most users.

**Independent Test**: Open a legacy project, switch the law to Stereo during playback,
hear the panned stereo tracks change, undo, hear them return.

**Acceptance Scenarios**:

1. **Given** a project using Balance, **When** the user switches to Stereo, **Then**
   panned stereo tracks and returns change immediately without clicks or dropouts.
2. **Given** that switch, **When** the user undoes, **Then** the law returns to Balance.
3. **Given** a switched project, **When** it is saved and reopened, **Then** the chosen
   law is kept.
4. **Given** the Mixer window is open, **When** the user looks at its footer, **Then**
   a **PAN LAW** control shows the current law (Stereo | Balance) with a one-line hint
   next to it, in both the Strips and the Stereo Map views.
5. **Given** a project created before this feature that still uses Balance, **When**
   the Mixer is open, **Then** an **OLDER PROJECT** badge sits next to the hint and
   explains on hover why the project uses Balance; switching to Stereo removes it.
6. **Given** a project using Stereo, **When** the user looks at a group strip, **Then**
   a **BAL** badge under its pan read-out explains on hover that groups keep a
   unity-centre balance.

### Edge Cases

- **Automated pan**: pan automation and modulation keep their values; only how the
  value is applied to stereo signals changes. Moving through positions must not click.
- **Returns**: a return's pan uses the project law just like a track's pan; a reverb
  return panned left keeps both reverb channels.
- **Groups**: groups keep their own unity-centre balance (level-transparent grouping);
  they do not follow the project law.
- **Clip pan envelopes and rack-chain pans**: unchanged (they stay balance stages).
- **Sends**: post-fader sends receive the signal after the pan stage, as today; under
  Stereo they receive the folded image.
- **Exports, freeze, resampling, sidechain routing**: use the same law as playback, so
  a render matches what was heard.
- **Remote, MCP, Mixer, arrangement headers**: the pan value range and display (L100…C…R100)
  do not change; only the sound does.
- **Project templates**: a new project made from any template uses Stereo, even if the
  template file was saved before this feature.
- **Mono-only material in a stereo track**: behaves as the mono case (Scenario 1.4).

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: A project MUST have a pan law with two values: **Stereo** and **Balance**
  (the current behaviour).
- **FR-002**: Under **Stereo**, the pan of a track or a return MUST move the whole
  stereo image: as the control moves toward one side, the opposite channel is folded
  into that side, so no channel's content is dropped at any position; at full left
  (right) the output is the source folded into the left (right) channel only.
- **FR-003**: Under **Stereo**, a centred pan MUST produce exactly the same output as
  **Balance** at centre.
- **FR-004**: Under **Stereo**, a source whose two channels are identical MUST produce
  the same left/right levels as **Balance** at every pan position (within 0.1 dB).
- **FR-005**: Under **Stereo**, at full left or right, the loudness of a source whose
  channels carry unrelated material MUST stay within 3.5 dB of its loudness at centre (a fold that is exact for mono costs unrelated material about 3 dB at the edge).
- **FR-006**: Projects saved without a pan law MUST open with **Balance**; their output
  MUST not change in any way.
- **FR-007**: New projects (blank or from any template) MUST start with **Stereo**.
- **FR-008**: The pan law MUST be saved with the project and restored on open.
- **FR-009**: The user MUST be able to see and switch the project's pan law; the
  switch is one undo step and takes effect during playback
  without clicks, dropouts or interrupting the transport.
- **FR-009a**: The switch MUST be a **PAN LAW** segmented control (Stereo | Balance) in
  the Mixer window footer, between the Show Sends / I/O toggles and the A/B
  crossfader, visible in every Mixer view. Each segment has a tooltip:
  Stereo — "Panning moves the whole stereo image; nothing is dropped";
  Balance — "Panning attenuates the opposite channel (legacy behaviour)".
- **FR-009b**: Next to the control a hint line MUST describe the current law:
  Stereo — "Pan moves the whole image of stereo tracks and returns";
  Balance — "Pan attenuates the opposite channel, as in earlier versions".
- **FR-009c**: While a project created before this feature uses Balance, an
  **OLDER PROJECT** badge MUST follow the hint, with the tooltip "Projects saved
  before stereo pan law open with Balance so existing mixes do not change". The badge
  survives save and reopen and disappears once the law is Stereo.
- **FR-009d**: Under Stereo, every group strip MUST show a **BAL** badge under its pan
  read-out, with the tooltip "Groups keep a unity-centre balance so grouping stays
  level-transparent". Under Balance the badge is hidden (all pans are balances then).
- **FR-009e**: The control, hint and badges MUST follow the Ember design language
  (`DESIGN.md`) in both themes, matching the design reference.
- **FR-010**: Group pan, clip pan envelopes and rack-chain pans MUST keep their current
  behaviour regardless of the project law.
- **FR-011**: Every path that renders the mix (playback, exports, freeze, resampling,
  routing taps, sends) MUST use the project's law.
- **FR-012**: The automated smoke test MUST cover: a stereo file with different
  left/right material at L100 under Stereo gives silence on the right (below −90 dBFS)
  and both materials on the left; a mono source matches Balance at several positions;
  a project without a law field renders identically to Balance.
- **FR-013**: The change MUST be listed in the changelog and the user documentation
  (EN + RU) with a short explanation of the two laws and why old projects keep Balance.

### Key Entities

- **Pan law** (project property): `Stereo` | `Balance`. Absent in older project
  files, which means `Balance`. Applies to every track and return pan in the project.
- **Older-project mark** (project property): whether the project predates the pan law
  (opened without a stored law). Drives the OLDER PROJECT badge only; it never
  changes sound.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: In a new project, a stereo source panned to L100 keeps 100 % of its
  material audible (right output silent, both channels' content present on the left).
- **SC-002**: 100 % of projects saved before this feature render identically after it
  (null test below −120 dBFS on the smoke-test project set).
- **SC-003**: Mono sources match the previous law within 0.1 dB at every pan position.
- **SC-004**: Switching the law during playback causes no audible click and no dropout
  (no sample-to-sample jump larger than the signal's own on the smoke test).
- **SC-005**: A user can find and change the project's pan law in under 30 seconds
  without the documentation.

## Assumptions

- The pan value, its range, its display and its automation stay as they are; only how
  the value is applied to a stereo signal changes under Stereo.
- "Total energy preserved" from the request is read as FR-005 (within 3.5 dB): a fold
  that is exact for mono sources (FR-004) cannot also keep the exact power of unrelated
  left/right material — the plan picks the curve that meets both bounds.
- Groups stay out of scope: their unity-centre balance exists to keep grouping
  level-transparent, and changing it would alter every grouped mix.
- No master pan exists, so nothing changes on the master.
- The Stereo Map feature (separate spec) depends on this one and is not part of it.

## Nota Compliance Checklist *(review before implementation; re-check in tasks.md)*

- [x] Every new source file will carry the AGPL-3.0-only SPDX header (two lines, see CONTRIBUTING.md)
- [x] No new dependency — or: license checked, AGPL-compatible, `LICENSES/third-party.md` row planned
- [x] Audio-thread impact considered: no alloc / lock / I/O on the audio thread (AR-6), command queue for data in (AR-4), immutable snapshots for structural edits (AR-5)
- [x] Managed/native crossings go through the pure C ABI in `nota_engine.h` only (AR-7); no JUCE outside `pluginhost/`
- [x] Smoke-test impact assessed: engine behaviour change → extend `tests/Nota.SmokeTest`
- [x] User-visible change → `CHANGELOG.md` `[Unreleased]` entry planned (and FEATURES.md / user docs via the nota-features / nota-docs skills when working with an agent that has them)
- [x] UI change → follows `DESIGN.md` (Ember design language)

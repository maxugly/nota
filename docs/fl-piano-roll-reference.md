# FL piano roll — behavioural reference (for the port)

Target: FL Studio **2026 (26.x)** piano roll, so Nota can match FL's real semantics.
Sources: official FL online manual (`pianoroll.htm`, `pianoroll_menu.htm`,
`basics_shortcuts.htm`, `chansettings_misc.htm`, `pianoroll_scripting_api.htm`,
`WhatsNew.htm`) and release notes. Community-sourced claims are marked.

This file **corrects** `fl-editors.md`, which listed some of these inaccurately.

---

## Per-note mute — `note.muted: bool`

A boolean on the note. A muted note **will not play** but keeps its time, length,
velocity, pan and colour; it stays in the grid.

- **Gesture:** Mute tool (**T**) → left-click/drag over notes. Right-click a note
  *while the Mute tool is active* unmutes (it does **not** delete).
- **Menu:** Piano roll menu → **Edit → Mute / Unmute**
- **Keys:** `Alt+M` / `Alt+Shift+M`
- **Menu:** Piano roll menu → **Select → Select muted**
- Drum-paint mode (**N**): left-click an existing note toggles its mute.
- **Scripting:** `note.muted`
- **Display:** muted notes render greyed/desaturated and composite **in front of**
  unmuted notes. (Grey/dark is community+changelog sourced; FL documents no icon.)
- **Introduced:** note/clip muting in **FL 9.0.0**; `Alt+M` shortcuts in **20.5**.

**Corrections vs our assumptions**
- There is **no per-note right-click mute menu**. In FL, plain right-click on a
  note **deletes** it.
- The **Note Properties dialog has no mute field** — to match FL, Nota should not
  add one either.
- Whether a muted note still emits its note-off/release is **undocumented**.
- Channel Rack *"mute removed steps"* is a *second* producer of `muted` notes —
  it preserves step data and shows them greyed in the piano roll.
- Mute is independent of channel mute and mixer mute; both are checked at playback.

**Port**
```csharp
Note { bool Muted; }   // must survive every transform tool; two dims (draw + play)
```

---

## Ghost channels — `Alt+V`

Show notes from **other channels in the same pattern** as grey semi-transparent
notes behind the one you're editing. Since **FL 2024.2** it can also show notes
from **other patterns** whose playlist clips overlap the clip you're editing.

- **Toggle:** Piano roll menu → **View → Helpers → Ghost channels** (`Alt+V`)
- **Toolbar:** Ghost icon — left-click opens the ghost-channel selector panel
  (upper-right); right-click opens the full menu (incl. Show All/None and
  per-playlist-track / per-clip toggles).
- **Swap to ghost channel:** **double-right-click** (or X1) on an **in-pattern**
  ghost. No-op on out-of-pattern ghosts.
- **Editable ghosts:** `Ctrl+Alt+V`
- **Rendering:** in-pattern ghosts = **solid** semi-transparent blocks;
  out-of-pattern ghosts = **outlined/open** blocks, time-shifted by the
  playlist clip offset. Ghosts draw **behind** normal notes.
- Ghost sources are **per-pattern**, recomputed on pattern change; the list shows
  channels that have notes in the current pattern.
- Which overlapping patterns appear depends on **which pattern clip you opened
  from the playlist**.
- Ghosts are display-only — not playable, not part of the edited score.
- Snap mode *"relative to events"* considers ghost boundaries.
- **History:** in-pattern ghost mode since **FL 3.4.0**; selector panel +
  out-of-pattern ghosts in **2024.2**.

**Port**
```csharp
GhostView { bool Enabled, Editable; HashSet<int> Channels; HashSet<int> ClipInstances; }
```

---

## Slide / portamento — `note.slide`, `note.porta`

Two independent per-note booleans. **No line or curve is drawn** — a slide note
gets a small **triangle**, a porta note a **portamento flag** icon.

- **Draw slide note:** `S` · **Draw porta note:** `O` (also in the piano roll
  context tool menu; click-and-hold can cycle slide/porta when drawing)
- **Per-note flags:** Note Properties → *Slide ON/OFF* and *Porta ON/OFF*.
  **Scripting:** `note.slide`, `note.porta`.

### Semantics (port these verbatim — this is not the usual DAW model)
1. A **slide note is its own event** (pitch + duration), *not* a reference to a
   target note. It affects whatever it overlaps, within the same colour group.
2. A slide note played alone produces **no sound**.
3. **Overlap length sets the slide time**; the slide note's length sets the rate.
   If the slid note ends early, the slide terminates early.
4. **Chords:** the **topmost** note is the pitch reference; the whole chord
   moves by the same interval; **the offset persists after the slide ends.**
5. **Colour scoping:** a slide only affects notes of the **same colour**
   (colour = MIDI channel; up to 16 independent groups).
6. During a slide, pan/cutoff/resonance/velocity **glide** toward the slide note's.
7. **Porta** is a short glide from the end of the previous note into the flagged
   note's attack; **the receiving note carries the flag.**

### Channel settings interaction
- **Porta** (Polyphony) = channel master enable; the per-note flag is documented
  as *inverting* it, so channel OFF + flag ON = porta active.
- **SLIDE** (Polyphony) sets porta duration and mono-overlap glide — it does
  **not** set slide-note duration (that's the slide note's own length).
- **Mono**: max 1 voice **per colour group**; overlapping notes glide over SLIDE.
- **Gate** truncates length and interacts with porta/slide unless **Full porta**.
- **VST/AU limitation:** FL itself does not support porta/slide via VST/AU —
  implement in Nota's engine as pitch bend, not exported MIDI.

**History:** pitch-per-note + portamento **3.3.0**; piano-roll porta button
**9.0.0**.

---

## Piano roll menu inventory (observed in live FL 2026)

The main menu is a **cascading column menu**, and different rows fan out to a
*different number* of columns — the menu system is ragged, so port it as
enumerated categories rather than one uniform submenu list.

| Row | Opens | Contents |
|---|---|---|
| **File** | menu | file/import/export for the score |
| **Edit** | menu | Transpose ±1 semitone / ±1 octave, Rotate, Shift, Discard lengths, Duplicate to right, Glue, Nudge, Change colour, **Mute/Unmute**, *Right-Alt+Right-Click = quantize selected* |
| **Tools** | **2 columns**: *Built-in* \| *Scripts* | see "Tools" below |
| **Stamp** | **3 columns**, third is context-dependent | see "Stamp" below |
| **View** | menu | **Helpers → Ghost channels (`Alt+V`)**, Editable ghosts (`Ctrl+Alt+V`), Note colour palette, etc. |
| **Snap** | menu | snap mode, incl. *"relative to events"* (considers ghost notes) |
| **Select** | menu | Select all / inverted / muted / … |
| **Group** | menu | group id assignment per note |
| **Zoom** | menu | zoom modes |
| **Time markers** | menu | — |
| **Target channel** | submenu | choose which channel the roll edits |
| **Target control** | submenu | choose the control surface / automation target |
| **Auto smoothing** | toggle | — |
| **Preview notes during playback** | toggle | audition notes on playback |
| **Center** | `Shift+D` | centre view |

Two distinct tool families — keep them separate in the port:
- **Tools** *transform* notes that already exist (quantize, chop, glue, strum…).
- **Stamp** *generates* notes in (chords, scales, percussion, rhythms).

### Tools → Built-in (transform)
`Riff machine (Alt+E)` · `Generate chord progression… (Alt+P)` · `Quick legato (Ctrl+L)` ·
`Articulate (Alt+L)` · `Quick quantize (Ctrl+Q)` · `Quick quantize start times (Shift+Q)` ·
`Quantize (Alt+Q)` · `Quick chop (Ctrl+U)` · `Chop (Alt+U)` · `Glue (Ctrl+G)` ·
`Arpeggiate (Alt+A)` · `Strum (Alt+S)` · `Flam (Alt+F)` · `Claw machine (Alt+W)` ·
`Limit (Alt+K)` · `Flip (Alt+Y)` · `Randomize (Alt+R)` · `Scale levels (Alt+X)` · `LFO (Alt+O)`.

### Tools → Scripts
`Run last script again (Ctrl+Alt+Y)` · `Open script folder…` · then one entry per
`.pyscript` in `Settings/Piano roll scripts/`. **FL ships no built-in Python
tools** — this list is entirely whatever scripts the user has installed.

> ⚠️ Provenance: the five scripts currently listed — `humanize`, `note_repeats`
> ("Note repeater sprinkler"), `scale_fill`, `strum`, `transpose` — were installed
> by **FL-Studio-MCP Route C**, not shipped by Image-Line. Their own docstrings say
> "Adapted from Image-Line's official API tutorial" / "See Route C in
> FL-Studio-MCP-Server". They are **simplified demo implementations**, not FL's own
> algorithms. Useful as API examples; **not** a source of truth for FL behaviour.
> `transpose` does *not* appear in FL's Built-in Tools menu (it lives in **Edit**).

### Stamp (generate notes in)
- **First column**: `Only one` (revert to single-note stamping after) · **`(none) (Shift+N)`** ·
  `Automatic chord (top down)` · `Automatic chord (bottom up)` ·
  `Advanced >` (the big third column) · `Melodic - chords >` · `Melodic - scales >` · `Percussions >`.
- **Melodic – chords**: Advanced, Augmented, Diminished, Fifths, Fifth 9th, Fifth Octave,
  Major, Major 7th, Minor, Minor 7th, Minor 9th, Octave, Suspended 2, Suspended 4.
- **Melodic – scales**: Major (Ionian), Major Bebop, Major Bulgarian, Major Hexatonic,
  Major Pentatonic, Major Polymode, Minor Harmonic, Minor Hungarian, Minor Melodic,
  Minor Natural (Aeolian), Minor Neapolitan, Minor Pentatonic, Minor Polymode,
  Minor Romanian, Other Arabic, Other Bebop Dominant, Other Blues, Other Blues Nonatonic,
  Other Diminished, Other Dorian, Other Eastern, Other Egyptian, Other Enigmatic,
  Other Hirajoshi, Other Iwato, Other Japanese Insen, Other Locrian, Other Locrian Super,
  Other Lydian, Other Mixolydian, Other Neapolitan, Other Phrygian, Other Phrygian Dominant,
  Other Prometheus, Other Whole Tone.
- **Third column (rhythm/roll variants, chosen by the highlighted item)**: e.g. for
  Augmented → *Fill each 4 steps / Fill each 8 steps / Flam fast / Flam med / Flam slow /
  Roll fast dynamic / Roll fast relative / Roll med dynamic / Roll med relative /
  Roll slow dynamic / Roll slow relative*; a **Special** group (Slide up, …).

Chord Stamp is a **toolbar** item (not in the Stamp menu); in FL 26 it gained
**top-down** and **bottom-up** voice-leading modes, matching the two
`Automatic chord` entries above.

---

## Advanced tools — the real Tools (wrench) menu

**Corrections:** there is **no Humanize** tool and **no Scale fill** tool in the
FL 2026 piano roll Tools menu (they were Chord-Progression-Tool / Multilink-era
things). **Note repeat, Transpose** are not Tools entries either — repeat is a
per-note property; Transpose lives under **Edit**.

Ordered as FL lists them:

| FL's label | Key | What it does |
|---|---|---|
| Riff machine | `Alt+E` | auto-hit-song melody generator (in FL 26: middle-click; right-click = Chord Progression Tool) |
| Generate chord progression… | `Alt+P` | Chord Progression Tool (new in 26) |
| Quick legato | `Ctrl+L` | instant legato — a preset for Articulate |
| Articulate | `Alt+L` | staccato/portato/legato layout dialog; Multiply, Variation (seeded), Chop chords, etc. |
| Quick quantize | `Ctrl+Opt+Cmd+Q` | quantize selection **and slides** to global snap |
| Quick quantize start times | `Shift+Q` | start times only |
| Quantize | `Alt+Q` | full Quantizer dialog |
| Quick chop | `Ctrl+U` | slice long notes by snap |
| Chop | `Alt+U` | full Chopper dialog (`.fsc` templates) |
| Glue | `Ctrl+G` | glue contiguous notes |
| Arpeggiate | `Alt+A` | non-realtime arpeggiator from score presets |
| Strum | `Alt+S` | offset time/velocity within chords |
| Flam | `Alt+F` | two near drum hits |
| Claw machine | `Alt+W` | remove/add/re-time notes into rhythms |
| Limit | `Alt+K` | transpose+clamp into a key range |
| Flip | `Alt+Y` | flip score vertically(pitch)/horizontally(time) |
| Randomize | `Alt+R` | generate notes from chord/scale and/or randomise levels |
| Scale levels | `Alt+X` | multiply/offset/centre/tension on event data |
| LFO | `Alt+O` | draw LFO event data (only for compatible property types) |
| Script… / Run last script / Script list | — | run Python piano-roll scripts |

All operate on the **selection**, or the whole roll if nothing is selected.

### High-value port targets

**Quantize** — *Groove Template* reuses FL `.fsc` score files (same format as
notes). **Strength** blends original↔quantised for **Start time**, **Sensitivity**
(how close to grid before it snaps — the forgiving, non-destructive knob most
DAWs lack), **Duration**. Modes: quantize duration / end time / leave each.
**Levels** mix note properties with the groove preset.

**Strum** — chords = overlapping notes **sharing the same start time**. Per-note
**Time** and **Velocity** start offsets + **Tension** (accel/decel); separate
**End** section; **Chop chords**; **Alternate direction**. **Positive time shift =
low→high (down-strum).**

**Randomize** — Pattern: Octave, Range, Key, Scale, Length, **Variation**
(length variance), Population, Stack, Random Portamento, Merge Same Notes, Seed.
Levels: one wheel per note property, −100%…+100% (0 = unchanged), plus
**Reset Before Processing** and **Bipolar**.

---

## Authoritative per-note data model

From the official piano-roll scripting API — this is the closest thing FL
publishes to a schema, and the right shape for Nota's note model.

| Property | Type / range | Default | In Note Properties? |
|---|---|---|---|
| `number` | MIDI note (48 = C4) | — | — |
| `time` | ticks | — | ✅ Start |
| `length` | ticks | — | ✅ Duration |
| `group` | 0 = ungrouped | 0 | (Group menu) |
| `pan` | 0.0–1.0 | 0.5 | ✅ PAN |
| `velocity` | 0.0–1.0 | 0.8 | ✅ VEL |
| `release` | 0.0–1.0 | — | ✅ REL |
| `color` | 0–15 (= MIDI channel) | 0 | ✅ Colour |
| `fcut` / `fres` | 0.0–1.0 | 0.5 | ✅ MOD X / MOD Y |
| `pitchofs` | −120…120 semitones/10 | — | — |
| `slide` / `porta` / `muted` | bool | — | ✅ / ✅ / ❌ |
| `selected` | bool | — | — |
| `repeats` | int 0–14 (1/4, 1/8 dotted, 1/4 trip … 1/64 trip) | 0 | ❌ (event editor, FL 2024) |

**Note colour is multi-purpose** — it scopes slide influence, maps to a MIDI
channel, scopes Mono polyphony per colour, allows editing a subset of overlapping
notes, and is loadable/saveable as `.ncp` palettes.

---

## Known gap — meter-aware grid (planned, not yet implemented)

FL draws a **hierarchy** of vertical lines derived from the **Pattern's** time
signature: bar lines, beat lines, and cell lines at the current time division — so a
6/8 pattern gets six eighths per bar, each subdivided per the meter [R2].

Time signatures in FL are **per-Pattern**, multiple per pattern, and independent of
the Playlist's — which is how differing meters get stacked in the Playlist as
polyrhythms. They are a **visual helper only**: they change the grid behind the
notes and the metronome downbeat, never playback or note data. Changing the meter
for any Channel affects **all** Channels in the Pattern; a *Make unique* option
detaches it [R2].

Nota's FL roll does not do this yet. Two separate defects:

1. **Hardcoded 4/4.** The FL renderer treats every bar as 4 beats in both the
   alternating-bar fill and the bar-line test (`PianoRollView.Fl.Rendering.cs`).
   The roll has no time-signature access at all.
2. **No hierarchy.** Grid lines are emitted at the raw snap division, so `Line`,
   `Cell`, `Events` and `Markers` all render identically instead of selecting
   distinct grids.

A third, cosmetic defect was fixed on this branch: `SubBeat` was `#394247` against a
`#3B4348` background — 2/255 apart, effectively invisible. Vertical lines now use
dedicated `GridSub`/`GridBeat`/`Bar` pens that read clearly against the background.
Note FL exposes an *Invert grid* command (bar-grid lines light↔dark), so the
light/dark choice is a user preference, not a hardcoded value [R2].

### Snap semantics (verified from the FL-26 offline help, `pianoroll_menu.htm`)

| Snap | Behaviour |
|---|---|
| `Main` | Use the **Global snap** value from the Snap Panel |
| `Line` | Snap to the nearest grid-line; **grid resolution changes with horizontal zoom** |
| `Cell` | Snap to the **start** of the grid-cell the note falls in |
| `(none)` | No snapping; movement limited only by the Project Timebase (PPQ) |
| `1/6`…`1/2 step`, `Step` | Fraction of a step (step = 16th = 0.25 beat) |
| `1/6`…`1/2 beat`, `Beat` | Fraction of a beat |
| `Bar` | One bar |
| `Events` | Relative to existing events, **including Ghost notes** |
| `Markers` | Snap to Time Markers |

Holding `Alt` temporarily sets snap to `none`. Scroll speed slows for any snap other
than `Line`. Note-value labels follow FL's table: `1/4 Step` = 64th, `1/2 Step` =
32nd, `1/4 Beat` = 16th, `1/3 Beat` = triplets, `1/2 Beat` = 8th, `1 Beat` = quarter,
`Bar` = whole note.

**Already correct in Nota:** every numeric division in the snap ladder matches FL
(step = 0.25 beats; `1/6 step` = 1/24; `1/4 step` = 1/16; `1/3 step` = 1/12;
`1/2 step` = 1/8; beats 1/6, 1/4, 1/3, 1/2, 1, `Bar` = 4). Only the four
non-numeric modes still need real behaviour.

**Planned implementation** (spec PR-04-050…053):
- Take the meter from the **Pattern** (not the transport); replace the hardcoded `4`
  with a signature-derived bar length, with a `Make unique` escape hatch.
- Support compound meters **inline** — 6/8, 9/8, 12/8, 3/8, 6/4 — as a grid-only
  change.
- Render bar / beat / cell tiers, culling only *after* the hierarchy is defined and
  only by whole factors that preserve bar alignment.
- Give `Line` (zoom-adaptive resolution), `Cell` (snap to cell start), `Events`
  (relative to events + ghosts) and `Markers` their distinct semantics, and decide
  whether Nota needs its own Global snap panel for `Main` to read.

---

## Open uncertainties (do not treat as fact)

1. Whether a muted note still emits note-off/release — undocumented.
2. Exact muted-note appearance (grey vs dark vs desaturated; no icon) — no
   official statement; changelog/community only.
3. Slide triangle placement — the manual contradicts itself ("right side" vs
   "at the start"); cosmetic.
4. No per-note portamento curve/slope parameter exists — duration is derived.
5. Exact first release of independent per-note muting / porta flags.
6. Note-repeat tick table is from the API docs; the event-editor UI labels were
   not separately verified against a screenshot.
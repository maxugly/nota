# Research: Undo for Volume and Pan

## R1. One history for graph snapshots and mix values

**Decision**: the engine's undo/redo stacks change from `vector<shared_ptr<Graph>>` to
`vector<UndoEntry>`:

```text
UndoEntry { shared_ptr<Graph> graph;      // null for a mix-only step
            vector<MixChange> mix; }      // empty for a structural step
MixChange { kind: TrackVolume | TrackPan | MasterVolume; trackId; before; after }
```

- **Undo** pops an entry: if it has a graph, push `{authoring_, mix}` onto redo and
  `publishRaw(graph)` (exactly today's behaviour); a mix-only entry is pushed to redo as is.
  Then every `MixChange` writes its `before` value through the normal setter.
- **Redo** is symmetric and writes `after`.
- `pushUndo()` (every structural checkpoint) pushes `{authoring_, {}}` — unchanged meaning.
- **Group collapse** (`endUndoGroup`) keeps the first entry's graph, as today, and merges
  the mix changes of the dropped entries into it: per target, the earliest `before` and the
  latest `after`.
- The depth limit (`kMaxUndoDepth` = 128) counts entries of both kinds.

**Rationale**: volume, pan and master volume live in atomics outside the graph (shared
`Track` objects across snapshots), so a snapshot cannot carry them; a value delta can, and
putting it in the same stack gives strict ordering with structural edits and the pan law
(FR-002) for free. A deleted-then-restored track is the same `Track` object, so its value
history stays valid.

**Alternatives considered**:
- *Managed-side mix history interleaved with the engine's* — two stacks that must agree
  on order; every structural undo would need to ask the managed side. Rejected.
- *Put volume/pan into the graph snapshot* — every fader move would publish a new graph
  (allocation per frame of a drag, AR-5 churn) and the audio thread reads them as atomics
  anyway. Rejected.

## R2. Gestures in the engine

**Decision**: an explicit gesture API on the message thread:

- `begin(targets[])` → handle: records each target's current value (`before`) and the
  undo-push counter.
- Value changes during the gesture go through the existing setters (unchanged, live).
- `end(handle)`: reads each target's current value (`after`); targets with
  `after == before` (exact float compare) are dropped (FR-005); a vanished track is dropped.
  If nothing is left → no step. Otherwise, **if a structural checkpoint was pushed since
  `begin`** (counter moved), every entry stacked since `begin` is collapsed into the first
  of them (as an undo group would) and the changes are merged into it — automation
  recording on a new lane pushes two checkpoints (the lane's creation, then the pre-write
  state), and lane + value must still be one step (FR-009); else a new mix-only entry is
  pushed and redo is cleared. (Implementation finding: merging into the top entry only left
  the lane creation as a second step.)

Plain setters (`nota_track_set_volume`, `…_pan`, `…_set_master_volume`) never record
anything. Automation playback (audio thread), project load, freeze/live freeze and the
template use them → never a step (FR-008) by construction.

**Rationale**: makes "one gesture = one step" explicit at the source that knows the
gesture (mouse down/up, Remote phase, MIDI pause, MCP call) and keeps the engine
attribution-free (a setter call does not need to know which gesture it belongs to).
Several gestures can be open at once (mouse + phone); each ends as its own step (spec edge
case). Overlapping gestures on the *same* parameter each record their own `before`.

**Alternatives considered**:
- *Undo groups around each gesture* — overlapping gestures would merge into one step,
  contradicting the spec's "each gesture its own step". Rejected (merge happens only for
  the automation checkpoint case above).
- *Record every setter call and coalesce by time* — automation playback and loads would
  have to be excluded by flags everywhere; fragile. Rejected.

## R3. Skipping steps of deleted tracks (FR-010)

A track removal is itself a step above any earlier volume step of that track, so undo
always brings the track back first; a dangling mix step arises only when a track is removed
while a gesture on it is open (its target is then dropped at `end`) or after history edits
that bypass that order. FR-010 is therefore a defensive rule:

**Decision**: on undo/redo, a `MixChange` whose track is not in the current authoring graph
is skipped. If an entry ends up applying nothing (mix-only, every track gone), undo/redo
continue with the next entry in the same call, so the user never presses ⌘Z "for nothing".

## R4. Master volume

**Decision**: add `nota_engine_master_volume(engine)` (getter; there is only a setter
today). `TransportViewModel.MasterVolume` stays the single owner in the app: after an
undo/redo, `RefreshAfterUndoRedo` reads the engine value back into the view model (its
setter re-sends the same value — harmless), so MST, the Master row, the Mixer and Remote
follow (FR-006).

## R5. Sources and their gesture boundaries

| Source | Begin | End |
|---|---|---|
| Mixer `PanKnob`, `VFader`, `DragNumber` (drag) | existing `GestureBegin` | `GestureEnd` |
| Header `PanBar`, `MiniFader`; Return/Master rows | existing `GestureBegin` / pointer down | `GestureEnd` / pointer up |
| Session mixer knob/fader | same controls' gesture events | |
| Double-click reset, typed dB value (no gesture open) | one-shot: begin → set → end | |
| Transport MST | its control's gesture events | |
| Nota Remote | first `vol`/`pan`/`mvol` command of a touch (phase 0/1 without an open gesture) | `phase == 2` |
| MIDI Learn | first CC of a movement | 1 s without a CC for that mapping (checked on the learn service's UI tick) |
| MCP `set_track_volume` / `set_track_pan` | one-shot inside the existing `Mutate` | |

A shared managed helper wires a control: `GestureBegin` → begin, `GestureEnd` → end, a
`ValueChanged` with no gesture open → one-shot. So every reset or typed value is covered
without touching each call site's logic.

**Scroll wheel**: none of the mix controls handle the wheel today, so FR-003's wheel rule
has no source yet; when wheel support is added it uses the same one-shot-or-debounce
helper with a 0.5 s pause. Recorded as N/A, not built.

## R6. Threading

All history work (gesture begin/end, undo/redo) is message-thread only, as the existing
undo is. Every source already runs there: UI controls, MIDI Learn (dispatched), Remote
apply (dispatched), MCP `Mutate` (`InvokeAsync` on the UI dispatcher). The audio thread
only reads the atomics — no new audio-thread code, nothing allocated there (AR-6).

## R7. Views after undo/redo

`RefreshAfterUndoRedo` already rebuilds the arrangement, Mixer and Session from the engine;
extend it with the master-volume read-back (R4). Remote is refreshed by `host.Refresh()`;
the Stereo Map (spec 008) reads values every frame.

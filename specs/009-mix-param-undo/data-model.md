# Data Model: Undo for Volume and Pan

## Engine (message thread)

### MixChange

| Field | Type | Rules |
|---|---|---|
| `kind` | `TrackVolume = 0` \| `TrackPan = 1` \| `MasterVolume = 2` | |
| `trackId` | `int32_t` | ignored for `MasterVolume` |
| `before`, `after` | `float` | exact values, never rounded; `before != after` (else dropped) |

### UndoEntry (replaces `shared_ptr<Graph>` in `undoStack_` / `redoStack_`)

| Field | Type | Rules |
|---|---|---|
| `graph` | `shared_ptr<Graph>` | the state to restore; null for a mix-only step |
| `mix` | `vector<MixChange>` | empty for a structural step; at most one change per target |

Merge rule (group collapse, automation merge): same target → keep the older `before`,
the newer `after`; a target whose merged `before == after` is dropped.

### Open gesture

| Field | Type | Rules |
|---|---|---|
| `handle` | `int32_t` | > 0, unique while open |
| `targets` | `vector<{kind, trackId, before}>` | `before` captured at begin |
| `pushCountAtBegin` | `uint64_t` | value of the engine's checkpoint counter at begin |
| `stackSizeAtBegin` | `size_t` | undo stack size at begin — entries above it collapse into one if a checkpoint was pushed during the gesture |

Lifecycle: `begin` → (values change through setters) → `end` (step pushed, merged or
dropped). `nota_engine_reset` and `clearUndoHistory` drop all open gestures; an `end` for a
dropped or unknown handle is a no-op.

## Managed

| Name | Layer | Role |
|---|---|---|
| `MixTarget` (record struct: kind, trackId) | Nota.Application | mirrors `NotaMixTarget` |
| `IAudioEngine.BeginMixGesture(params MixTarget[])` → `int`, `EndMixGesture(int)`, `MasterVolume` (get) | Nota.Application port | |
| `MixEdit` static helpers: `Step(engine, target, Action set)` (one-shot) | Nota.Application | MCP, resets, typed values |
| `MixGesture(engine, targets)` — `Begin` / `End` / `Change(value, setter)` | Nota.Application | a control's gesture; a value with no open gesture is a one-off step; no allocation per value |
| `MixMovement(engine, target, pause)` — `Touch(set, nowMs)` / `Poll(nowMs)` | Nota.Application | MIDI Learn: one step per movement, ended on the tick after a 1 s pause (time passed in, so the smoke test drives it) |

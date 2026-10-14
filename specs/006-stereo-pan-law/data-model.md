# Data Model: Stereo Pan Law

## PanLaw (engine, graph-level)

| Field | Type | Where | Default |
|---|---|---|---|
| `panLaw` | `enum class PanLaw : int32_t { Balance = 0, Stereo = 1 }` | `Graph` (`Graph.h`) | `Balance` |

- Part of the immutable graph snapshot → undo/redo restore it with the graph.
- Carried to every fresh graph by `inheritGraphLevel()` (with `sceneCount`, `scenes`,
  `masterVolume`, `masterTrack`).
- Read by the audio thread from the live graph at the track and return pan stages.

## Render state (engine, audio-thread-owned)

| Field | Type | Meaning |
|---|---|---|
| `panLawFrom_` / `panLawTo_` | `PanLaw` | the law being faded from / to (`panLawTo_` = the live law once a fade starts) |
| `panLawPos_` | `float` | fade position, 0 = from … 1 = to; 1 = no fade running |
| `panLawSnap_` | `std::atomic<bool>` | set by the message thread on reset and on a no-checkpoint `setPanLaw` (load, new-project baseline) |

Per `mixGraph` call: if `panLawSnap_` was set → `from = to = live law`, `pos = 1` (no fade);
else if the live law ≠ `panLawTo_` → start a fade (`from = to`, `to = live`, `pos = 0`), or,
if it flipped back to `panLawFrom_` mid-fade, swap them and continue from `1 − pos`. The
track/return stages lerp the two laws' gains per sample with `pos + i·step`
(`step = 1 / 10 ms` in samples); after the segment `pos += frames·step`, clamped to 1.

## Project document (`project.json`, format v21)

```json
"mixer": { "panLaw": "stereo", "panLawLegacy": false }
```

| Field | Type | Rules |
|---|---|---|
| `mixer` | object, optional | absent (pre-v21 or damaged) ⇒ `{ balance, legacy: true }` |
| `mixer.panLaw` | `"stereo"` \| `"balance"` | unknown string ⇒ `balance` + load warning |
| `mixer.panLawLegacy` | bool | default `false`; written by the v20 → v21 migration as `true` |

`MixerDto` keeps `[JsonExtensionData]` like the other DTOs (forward-compat, NFR-8).

## Managed state

| Name | Owner | Lifetime |
|---|---|---|
| `IAudioEngine.PanLaw` (get) / `SetPanLaw(law, checkpoint)` | `Nota.Application` port, `NotaEngine` impl | engine |
| `PanLawLegacy` | `TransportState` (persistence boundary record) → `MainWindow` field | per open project; set on load, `false` on New |

UI derivation (`MixerView`, re-read in `Refresh()` and on the UI tick):

- PAN LAW segment = `engine.PanLaw`
- hint line = text for that law (contract `ui-mixer-footer.md`)
- OLDER PROJECT badge visible ⇔ `PanLawLegacy && PanLaw == Balance`
- BAL badge on each group strip visible ⇔ `PanLaw == Stereo`

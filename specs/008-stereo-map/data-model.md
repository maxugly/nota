# Data Model: Stereo Map (MVP)

## Engine — stereo energies (audio thread writes, message thread reads)

| Where | Fields | Rules |
|---|---|---|
| `Track` (tracks and returns) | `std::atomic<float> stereoL_, stereoR_, stereoLR_` | exponential averages of L², R², L·R of the post-fader post-pan signal, τ = 0.3 s; fed zeros while muted / solo-gated |
| `Engine` (master) | `std::atomic<float> masterStereoL_, …R_, …LR_` | same, after the master volume |

## C ABI — one measurement row ([contract](contracts/c-abi-stereo-measures.md))

| Field | Type | Range |
|---|---|---|
| `track_id` | `int32_t` | track / return id, or the master track id |
| `level_db` | `float` | ≤ 0 typically; −∞ (−200) for silence |
| `balance` | `float` | −1 … +1, pan scale (research R2) |
| `width` | `float` | 0 … 1 |
| `correlation` | `float` | −1 … +1 |

`silent` = `level_db < −60` (derived by the caller).

## Managed

| Name | Layer | Role |
|---|---|---|
| `StereoMeasure` (record struct, mirrors the native row) | Nota.Application | |
| `IAudioEngine.GetStereoMeasures(Span<StereoMeasure>)` → count | Nota.Application port | one call per frame |
| `Settings.MixerView` | Nota.Application | `"strips"` (default) \| `"map"` |
| `MixerView.View` | Nota.App | Strips / StereoMap; switches layout |
| `StereoMapView` | Nota.App | the map control (render + input) |

### Map handle (per frame, view-local)

| Field | From |
|---|---|
| id, name, colour index | track list (`TryGetTrackInfo`, `TrackNames`, `EffectiveColorIndex`) |
| x = pan, y = fader position | `TrackInfo.Pan`, `TrackInfo.Volume` (master: x = 0, y = master volume) |
| muted, dimmed (solo active and not soloed, not a return / master) | `TrackInfo` |
| selected | `Timeline.SelectedTrackId` |
| cloud | the measurement row for the id |

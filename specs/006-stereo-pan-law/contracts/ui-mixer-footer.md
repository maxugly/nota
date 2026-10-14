# Contract: Mixer footer — PAN LAW, OLDER PROJECT, BAL

Source of truth: `nota-design/Nota Mixer v2.html` (footer and strip pan block). Built
in `src/managed/Nota.App/MixerView.cs` with Ember tokens (`DESIGN.md`, `nota-design`
skill); both themes.

## Footer layout (left → right)

`Show [Sends] [I/O]` · 1 px divider · **PAN LAW** group · (flex) · A/B crossfader
placeholder (unchanged).

Footer height grows from 34 to 44 px to fit a 26 px segmented control (mock).

## PAN LAW group

| Element | Spec |
|---|---|
| Label | "PAN LAW" — 10 px, bold, letter-spacing .14em, `TextTertiary` |
| Segmented control | The existing shell segmented control (`Border.segmented` + `ToggleButton.seg`, 11 px — `DESIGN.md` § components): sunken `BgSunken` well, `BorderDefault`; two segments "Stereo", "Balance". Selected = solid `Accent` with `TextOnAccent`; unselected = `TextSecondary` |
| Tooltips | Stereo — "Panning moves the whole stereo image; nothing is dropped"; Balance — "Panning attenuates the opposite channel (legacy behaviour)" |
| Hint (11 px, `TextTertiary`) | Stereo — "Pan moves the whole image of stereo tracks and returns"; Balance — "Pan attenuates the opposite channel, as in earlier versions" |
| OLDER PROJECT badge | Mono 9 px, letter-spacing .18em, 18 px tall, 1 px `BorderDefault`, radius 3, `TextTertiary`. Tooltip: "Projects saved before stereo pan law open with Balance so existing mixes do not change". Visible ⇔ `PanLawLegacy && law == Balance` |

Interaction: clicking the unselected segment calls `SetPanLaw(law, checkpoint: true)`;
clicking the selected one does nothing. The control re-reads `engine.PanLaw` in
`Refresh()` and on the UI tick, so undo/redo (from any window) and project load
update it.

## BAL badge on group strips

Under the PAN read-out of every group strip, when law == Stereo: "BAL", Mono 7 px,
letter-spacing .12em, 11 px tall, 1 px `BorderDefault`, radius 3, `TextTertiary`.
Tooltip: "Groups keep a unity-centre balance so grouping stays level-transparent".
Hidden under Balance. Track, return and master strips never show it.

## Not in this contract

The mock's Strips | Stereo Map tabs, the map itself, the strip redesign and the
"Undo pan law" toast — Stereo Map spec / not a Nota pattern.

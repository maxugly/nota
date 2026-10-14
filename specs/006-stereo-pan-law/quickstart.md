# Quickstart: validating Stereo Pan Law

## 1. Automated (the PR gate)

```bash
scripts/build.sh
```

Runs the smoke test, which now includes `PanLawTests` (also alone:
`dotnet run --project tests/Nota.SmokeTest -- --panlaw`). Expected passes:

| Check | Spec |
|---|---|
| Fresh engine law = Balance; set Stereo → undo → Balance → redo → Stereo; same-law set adds no undo step; `checkpoint 0` adds none | FR-001, FR-009, contract `c-abi-pan-law.md` |
| L=440 Hz / R=1 kHz WAV, Stereo, pan L100: R output < −90 dBFS; each tone on L at half its source amplitude (−6.0 dB ±0.5 re source) | FR-002, FR-012, SC-001 |
| Same at R100 (mirror) | FR-002 |
| Stereo at C ≡ Balance at C, sample-exact | FR-003 |
| Mono (440 Hz both channels): Stereo vs Balance at p ∈ {−1, −0.6, −0.25, 0.3, 0.8, 1}, per-channel level within 0.1 dB | FR-004, SC-003 |
| Unrelated L/R, Stereo at L100: total power within 3.5 dB of centre | FR-005 |
| Return panned L100 under Stereo keeps both channels of its input | Edge: returns |
| Group panned under Stereo renders identical to Balance | FR-010 |
| Law flip mid-playback on the L/R WAV at L100: no sample step larger than the signal's own maximum step | FR-009, SC-004 |
| Law survives: add/remove/move track, group, scene add/remove, automation point, CV link, clip edit, undo/redo | R2 footgun |
| Project save → load round-trips `stereo` / `balance` / `panLawLegacy` | FR-008, `project-format-v21.md` |
| A v20 document (fixture JSON, no `mixer`) with an audio track panned L50 loads as Balance + legacy, and each output tone matches the Balance law (`cos θ`, `sin θ`, no cross-feed) within 0.01 dB | FR-006, SC-002 |
| `ProjectTemplate.ApplyDefault` → Stereo, not undoable | FR-007 |

## 2. In the app (UI, by agent via the headless/in-app harness — see memory "Nota UI verify recipe")

Isolate data with `NOTA_DATA_DIR`.

1. **New project** → Mixer (⌘⇧M): footer shows `PAN LAW [Stereo|Balance]` with Stereo
   selected, hint "Pan moves the whole image…", no OLDER PROJECT badge. Group a track:
   the group strip shows **BAL**.
2. **Open a project saved by 0.52.x**: Balance selected, OLDER PROJECT badge visible,
   no BAL badges. Hover each element: tooltips per `contracts/ui-mixer-footer.md`.
3. Click **Stereo** during playback: the badge disappears, BAL appears on groups,
   panned stereo tracks audibly widen, no click. ⌘Z (in the main window and in the
   Mixer): back to Balance, badge back. Save, reopen: law and badge kept.
4. Screenshot the footer in Ember Graphite and Ember Paper; compare with
   `nota-design/Nota Mixer v2.html`.

## 3. Docs

`CHANGELOG.md` `[Unreleased]`, `FEATURES.md` (nota-features), user docs EN + RU
(nota-docs: Mixer page — pan law section with a footer screenshot).

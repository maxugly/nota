# Quickstart: validating Undo for Volume and Pan

## 1. Automated (the PR gate)

```bash
scripts/build.sh
```

The smoke test gains `MixUndoTests` (alone: `dotnet run --project tests/Nota.SmokeTest -- --mixundo`)
running every row of [contracts/c-abi-mix-undo.md](contracts/c-abi-mix-undo.md) § Guarantees,
plus: MCP `set_track_volume` through the MCP test support is one step; a debounced MIDI
gesture (two CC bursts 1.5 s apart) makes two steps.

## 2. In the app (headless harness — memory "Nota UI verify recipe")

Isolate data with `NOTA_DATA_DIR`. Drive controls with synthetic pointer events.

1. Mixer: drag a fader, ⌘Z → exact old value in the fader, the dB field, the track header
   and the arrangement Return/Master row; ⌘⇧Z → new value.
2. Double-click a pan knob (reset), type −12 in a dB field: each is one ⌘Z.
3. Move MST, ⌘Z: MST, the Master strip and the Master row all show the old value.
4. Play a project with volume automation for 10 s: the Edit ▸ Undo item stays disabled.
5. Turn on automation recording, drag a fader: one ⌘Z removes the recorded points and
   restores the value.

## 3. Docs

`CHANGELOG.md` `[Unreleased]`, `FEATURES.md` (Project and transport: undo), user docs EN + RU
(where undo is described — `basics/…` page found via coverage.yml).

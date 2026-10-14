# Quickstart: validating the Stereo Map (MVP)

## 1. Automated (the PR gate)

```bash
scripts/build.sh
```

The smoke test gains `StereoMeasureTests` (alone: `dotnet run --project tests/Nota.SmokeTest -- --stereomap`):
every row of [contracts/c-abi-stereo-measures.md](contracts/c-abi-stereo-measures.md) §
Guarantees, plus the measuring-cost check (SC-004).

## 2. In the app (headless harness — memory "Nota UI verify recipe", `NOTA_DATA_DIR`)

1. New project, Mixer (⌘⇧M), title bar **Stereo Map**: the map shows the instrument and audio
   tracks, both returns and the master; strips in the right column; the choice is remembered
   after closing and reopening the window.
2. Play a mono WAV panned L50 and a wide stereo WAV at C: the first cloud is narrow and
   centred on its handle near L50; the second spans nearly the full width; tooltips read the
   values from § 1 (± 0.05).
3. Drag a handle left-down with real mouse input: pan and volume change together, the strip
   follows; ⌘Z restores both in one step. Shift-drag moves one axis. Double-click → C;
   ⌥ double-click → 0 dB. The master handle does not move.
4. Mute a track → dimmed handle, no cloud; solo another → the rest dimmed.
5. Click a handle → the arrangement selects that track; select another track in the
   arrangement → the ring moves on the map.
6. 20 tracks stacked at C / 0 dB: no two labels overlap; the selected one is on top.
7. Screenshots of the Mixer in the Stereo Map view in both themes, compared with
   `nota-design/Nota Mixer v2.html`; frame time with 128 tracks under 16 ms.

## 3. Docs

`CHANGELOG.md` `[Unreleased]`; FEATURES.md (Tracks and mixer); user docs EN + RU — the Mixer
page gets a "Stereo Map" section with screenshots in both themes (`nota-docs`, new shots in
`shots/manifest.yml`, the `mixer` scene gains a `view: map` arg).

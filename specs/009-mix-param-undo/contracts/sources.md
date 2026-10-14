# Contract: where gestures begin and end (managed)

Every user source of a volume/pan/master-volume change maps to one gesture rule. Internal
callers keep the plain setters (never a step).

| Source | File | Target(s) | Begin | End |
|---|---|---|---|---|
| Mixer pan knob | `Nota.App/MixerView.cs` `PanRow` | track pan | `PanKnob.GestureBegin` | `GestureEnd` |
| Mixer fader + dB number | `MixerView.cs` `FaderRow` | track volume | `VFader` / `DragNumber` `GestureBegin` | `GestureEnd` |
| Mixer master strip | `MixerView.cs` `MasterStrip` | master volume | same | same |
| Header mini fader / pan bar | `Nota.App/ArrangementView.cs` (~1848–1862) | track volume / pan | `GestureBegin` | `GestureEnd` |
| Return / Master rows | `ArrangementView.cs` (~2425–2466) | track volume / master volume | `MiniFader.GestureBegin` | `GestureEnd` |
| Session mixer | `Nota.App/SessionView.Mixer.cs` | track pan / volume | control gesture events | |
| Transport MST | the MST control in `MainWindow` | master volume | its gesture events | |
| Any of the above without an open gesture (double-click reset, typed value) | — | its target | one-shot | |
| Nota Remote | `Nota.Remote/RemoteHub.Apply.cs` `vol` / `pan` / `mvol` | track volume / pan / master | first command of a touch | `cmd.Phase == 2` |
| MIDI Learn | `Nota.App/MidiLearnService.cs` (~348–350) | track volume / pan / master | first CC | 1 s without a CC on that mapping |
| MCP | `Nota.Mcp/Tools/TrackTools.cs` `set_track_volume` / `set_track_pan` | track volume / pan | one-shot inside `Mutate` | |
| Stereo Map (spec 008) | — | track volume **and** pan | pointer down | pointer up |

**Never a gesture**: `ProjectService.Apply` (load), `ProjectTemplate`, freeze / live freeze
(`MainWindow.Freeze.cs`, `MainWindow.LiveFreeze.cs`), `MainWindow.Files.cs` resets,
automation playback (engine audio thread).

The wiring helper used by every control row:

```text
Wire(control, target):
  GestureBegin → h = engine.BeginMixGesture(target)
  GestureEnd   → engine.EndMixGesture(h); h = 0
  ValueChanged while h == 0 → one-shot: h1 = Begin; set; End(h1)
```

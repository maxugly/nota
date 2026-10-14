# Piano-roll interaction checks

Run from the repository root with .NET SDK 10:

```sh
dotnet run --project tests/Nota.PianoRollTest -c Release
```

Uses Avalonia Headless with the Fluent control theme. No running Nota instance,
MCP server, native audio engine, display server, or audio device is required.

Checks original-editor behavior, FL gesture commits and live undo checkpoints,
keyboard shortcuts, snap controls, tab switching, note properties, audition
release, pitch zoom, and expression curves across a slice. These UI checks do
not replace native-engine integration tests for clip resizing and playback.

# AgbSynth

AgbSynth is an early GBA MP2K sound workstation project.

The initial target is to load `.gba` ROMs, identify MP2K song data, extract voicegroups/samples/sequences into an editable project format, and eventually rebuild those assets back into a ROM.

## Current State

- Avalonia app shell
- GBA ROM loader
- GBA ROM pointer/offset helpers
- Initial MP2K song table entry reader
- Project format planning document
- Unit tests for address conversion and song table entry parsing

## Build

```bash
dotnet build AgbSynth.sln
```

## Test

```bash
dotnet test tests/AgbSynth.Tests/AgbSynth.Tests.csproj
```

## Docs

See [Docs/Overview.md](Docs/Overview.md) for the design direction and MVP plan.

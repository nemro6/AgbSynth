# AgbSynth Overview

AgbSynth is planned as a GBA MP2K sound workstation based on the workflow and UI direction of NitroSynth.
The goal is to load `.gba` ROMs, extract MP2K sound assets, edit/play them in an application-level project format, and rebuild those assets back into a ROM at controlled addresses.

## Goal

AgbSynth should make MP2K sound data editable as a project instead of treating a ROM as the only source of truth.

Core goals:

- Load a `.gba` ROM.
- Locate or manually specify MP2K song tables.
- Extract song headers, sequence data, voicegroups, samples, and wave memory data.
- Store extracted data in an editable project format.
- Provide MIDI playback, bank audition, mixer monitoring, and sequence playback similar to NitroSynth.
- Rebuild edited assets into a ROM by compiling them to specified addresses or free space.

## Target Asset Types

### Song Table / Header

The song table maps song IDs to MP2K song header data.

Expected data:

- song ID
- sequence pointer
- voicegroup pointer
- priority
- reverb
- track count or track pointer list, depending on engine layout
- raw header bytes for preservation

The editor should preserve both parsed fields and original raw bytes where possible, because MP2K variants can differ by game.

### Sequence Data

Sequence data should initially be preserved as raw MP2K bytes.

Later layers can add:

- MP2K event decompiler
- editable text format
- MIDI export
- MIDI or midi2agb import

The project format should not use MIDI as the primary internal representation. MIDI can lose MP2K-specific behavior such as loops, commands, engine flags, and certain controller semantics.

### Bank / Voicegroup Data

MP2K voicegroups should be extracted as editable bank data.

Expected voice entry types:

- DirectSound sample instruments
- square wave instruments
- programmable wave instruments
- noise instruments
- key split / drum style mappings, if present in the target variant

Expected fields:

- voice type
- sample pointer or wave pointer
- ADSR/envelope parameters
- base key
- pan
- tuning/pitch data
- raw voice entry bytes for preservation

### Waveform / Sample Data

DirectSound sample data should be stored separately from voicegroup metadata.

Expected fields:

- original ROM pointer
- sample format
- sample rate or pitch-derived rate
- loop flag
- loop start
- sample length
- raw PCM bytes
- optional `.wav` export data

Internally, keeping the original PCM-style payload is preferable to using `.wav` as the source of truth. `.wav` should be an import/export format.

### Wave Memory Data

Programmable wave data should be stored separately from DirectSound samples.

Expected fields:

- original ROM pointer
- wave RAM bytes
- decoded preview data
- references from voicegroup entries

## Project Format Direction

AgbSynth should use its own project format as the authoritative editable state.
Formats like midi2agb should be supported as import/export layers, not as the main project storage.

Proposed layout:

```text
AgbSynthProject/
  project.json
  rom.json
  songs/
    song_000.seq.bin
    song_000.meta.json
  banks/
    voicegroup_000.json
  samples/
    sample_000.pcm.bin
    sample_000.meta.json
  wavememory/
    wave_000.bin
    wave_000.meta.json
  build/
    layout.json
```

`project.json` should identify the project and global settings:

```json
{
  "format": "AgbSynthProject",
  "version": 1,
  "engine": "MP2K",
  "romCrc32": "00000000",
  "songTableAddress": "0x08000000",
  "outputMode": "RelocateToAddress"
}
```

`rom.json` should preserve ROM-specific detection data:

```json
{
  "fileName": "base.gba",
  "crc32": "00000000",
  "gameCode": "ABCD",
  "engineVariant": "MP2K",
  "detectedSongTables": [
    "0x08000000"
  ]
}
```

## Build / Compile Model

The build step should compile the project data back into ROM binary form.

Build flow:

1. Choose target ROM.
2. Choose placement mode:
   - fixed base address
   - explicit per-object addresses
   - free-space allocation
3. Compile sequence data.
4. Compile voicegroups.
5. Compile samples and wave memory.
6. Align objects according to MP2K/GBA pointer needs.
7. Generate final ROM addresses.
8. Patch song headers, voicegroup pointers, sample pointers, and song table entries.
9. Write output `.gba`.
10. Write `build/layout.json`.

`build/layout.json` should record what was placed where:

```json
{
  "objects": [
    {
      "name": "song_000_sequence",
      "romAddress": "0x08800000",
      "size": 1024
    },
    {
      "name": "voicegroup_000",
      "romAddress": "0x08800400",
      "size": 384
    }
  ],
  "patches": [
    {
      "target": "song_table_000",
      "romAddress": "0x08012340",
      "value": "0x08800000"
    }
  ]
}
```

This makes builds inspectable and repeatable.

## Application Direction

AgbSynth can reuse NitroSynth's broad interaction model:

- tab-based asset views
- mixer-first realtime monitoring
- piano keyboard input
- MIDI input device support
- master meter and per-channel meters
- dark/light theme support

The internal data model should be separate from NitroSynth, because NDS SDAT and GBA MP2K have different archive structures, pointer models, and playback semantics.

## Suggested Source Layout

```text
src/AgbSynth.App/
  Audio/
  Controls/
  GBA/
  MP2K/
  Project/
  ViewModels/
tests/AgbSynth.Tests/
```

Suggested responsibilities:

- `GBA/`: ROM loading, address conversion, pointer helpers, free-space scanning.
- `MP2K/`: song table parsing, song header parsing, sequence parser, voicegroup parser, sample parser.
- `Project/`: AgbSynth project serialization, asset references, build layout.
- `Audio/`: mixer, voices, resampling, meters, MIDI playback.
- `ViewModels/`: UI state and app workflow.

## MVP Plan

The first working version should stay narrow.

MVP sequence:

1. Create Avalonia app shell.
2. Load `.gba` ROM.
3. Allow manual song table address input.
4. Parse song table entries.
5. Parse one selected voicegroup.
6. Extract referenced DirectSound samples.
7. Play selected bank from MIDI keyboard or on-screen piano.
8. Parse and play one selected MP2K sequence.
9. Save extracted data as an AgbSynth project.
10. Build selected assets back to a fixed address in a copied ROM.

After MVP:

- automatic song table detection
- MIDI export/import
- midi2agb import/export
- text decompiler/editor for MP2K sequence data
- free-space allocator
- richer sample and wave memory editors
- full mixer parity with NitroSynth

## Important Design Decisions

- Use AgbSynth's own project format as the source of truth.
- Treat MIDI and midi2agb as interchange formats.
- Preserve raw bytes alongside parsed metadata where engine variants are uncertain.
- Build into a copied output ROM, not the original ROM.
- Generate layout and patch metadata for every build.

## Open Questions

- Which MP2K variants should be supported first?
- Should automatic song table detection be signature-based, heuristic-based, or profile-based?
- How should edited sequence text represent MP2K-specific commands that MIDI cannot preserve?
- Should project files store samples as raw PCM only, or also cache `.wav` previews?
- What alignment rules should be enforced by default for each asset class?

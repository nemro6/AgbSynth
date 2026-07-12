# AgbSynth Project Format

## Scope

An `.agbsynth` file is a lightweight project manifest. Editable assets live in the adjacent `<project>_data` directory and are discovered by category. The manifest does not embed asset lists.

Current project version: `4`  
Current JSON asset version: `2`  
Engine identifier: `MP2K`

## Asset Identity

Every editable asset has a stable, opaque `AssetId`. References use `AssetId` as the canonical identity and keep a project-relative path as a location hint. Renaming a file does not break a reference because the loader scans the category folder, reads the embedded `AssetId`, and refreshes the path hint.

Legacy v1 assets without an `AssetId` receive a deterministic migration ID derived from their format and path. The ID is persisted on the next explicit project save.

## Envelopes

JSON assets share these fields:

```json
{
  "Format": "AgbSynthSongHeader",
  "Version": 2,
  "Engine": "MP2K",
  "AssetId": "0123456789abcdef0123456789abcdef"
}
```

Formats:

| Extension | Format | Payload |
| --- | --- | --- |
| `.agbst` | `AgbSynthSongTable` | `SongTable`, ordered `Entries` |
| `.agbsh` | `AgbSynthSongHeader` | `Header` |
| `.agbvg` | `AgbSynthVoiceGroup` | bank metadata and 128 voices |
| `.agbks` | `AgbSynthKeySplit` | `KeySplit` regions and key map |
| `.agbds` | `AgbSynthDrumSet` | `DrumSet` entries |
| `.agbwd` | `AgbSynthWaveData` | sample header and signed 8-bit PCM hex |
| `.agbwm.meta.json` | `AgbSynthWaveMemoryMetadata` | label, note, format, and 16-byte size contract |

`.agbwm` remains a raw 16-byte Wave RAM image. Its identity and editor metadata are stored in the sidecar.

## Sequence References

A SongHeader stores:

- `SequenceFormat`: `Midi` or `Midi2Agb`
- `SequenceFilePath`: active sequence file
- `MidiFilePath`: optional MIDI representation
- `Midi2AgbFilePath`: optional Midi2agb source representation

ROM import supports `MIDI`, `Midi2agb`, or `Both`. Playback loads either active representation into the same MP2K audio-clock pipeline.

## Compatibility

- Older supported projects and v1 assets are migrated in memory and written as the current version only after explicit Save.
- An unknown `Format` or non-`MP2K` engine is rejected.
- A newer project or asset version opens read-only with a diagnostic; overwrite is blocked.
- Malformed assets are skipped individually and reported with their file path.

## Diagnostics

Load validation reports duplicate IDs, missing files/references, invalid WaveMemory sizes, and non-standard VoiceGroup sizes. Diagnostics are runtime data and are not serialized into the project.


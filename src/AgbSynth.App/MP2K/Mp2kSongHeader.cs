namespace AgbSynth.App.MP2K;

public sealed record Mp2kSongHeader(
    int SongId,
    int HeaderOffset,
    byte TrackCount,
    byte BlockCount,
    byte Priority,
    byte Reverb,
    uint VoiceGroupPointer,
    int VoiceGroupOffset,
    uint[] TrackPointers,
    int[] TrackOffsets,
    byte[] RawHeader);


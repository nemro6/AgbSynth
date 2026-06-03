namespace AgbSynth.App.MP2K;

public sealed record Mp2kSongTableEntry(
    int SongId,
    int TableOffset,
    uint HeaderPointer,
    int HeaderOffset,
    byte[] RawEntry);


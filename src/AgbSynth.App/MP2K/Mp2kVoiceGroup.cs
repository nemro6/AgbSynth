using System.Collections.Generic;

namespace AgbSynth.App.MP2K;

public sealed record Mp2kVoiceGroup(
    uint Pointer,
    int Offset,
    IReadOnlyList<Mp2kVoice> Voices);

public sealed record Mp2kVoice(
    int Index,
    byte Type,
    byte Key,
    byte Length,
    byte PanOrSweep,
    uint DataPointer,
    int? DataOffset,
    byte Attack,
    byte Decay,
    byte Sustain,
    byte Release,
    uint? ExtraPointer,
    int? ExtraOffset,
    Mp2kSampleHeader? Sample,
    Mp2kDrumSet? DrumSet,
    Mp2kKeySplit? KeySplit,
    byte[] Raw);

public sealed record Mp2kSampleHeader(
    int HeaderOffset,
    byte LoopFlags,
    uint Frequency,
    uint LoopStart,
    uint Size,
    int DataOffset);

public sealed record Mp2kDrumSet(
    int TableOffset,
    IReadOnlyList<Mp2kVoice> Entries,
    byte[] Raw);

public sealed record Mp2kKeySplit(
    int RegionTableOffset,
    int? KeyMapOffset,
    IReadOnlyList<Mp2kVoice> Regions,
    byte[] KeyMap,
    byte[] RawRegionTable);

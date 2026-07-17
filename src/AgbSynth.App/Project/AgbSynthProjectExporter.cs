using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using AgbSynth.App.GBA;
using AgbSynth.App.MP2K;

namespace AgbSynth.App.Project;

public static class AgbSynthProjectExporter
{
    private const int MaxInitialSongTableEntries = 2048;
    private const int MaxEmptySongTableGap = 128;
    private static readonly string[] AssetDirectoryNames =
    [
        "songtable",
        "songheader",
        "midi",
        "midi2agb",
        "voicegroup",
        "keysplit",
        "drumset",
        "wavedata",
        "wavememory"
    ];

    public static AgbSynthProjectFile CreateBlankProject(string outputPath)
    {
        if (string.IsNullOrWhiteSpace(outputPath))
            throw new ArgumentException("Project path is required.", nameof(outputPath));

        outputPath = Path.GetFullPath(outputPath);
        string projectDirectory = Path.GetDirectoryName(outputPath) ?? Directory.GetCurrentDirectory();
        string assetRoot = Path.Combine(projectDirectory, $"{Path.GetFileNameWithoutExtension(outputPath)}_data");
        bool assetRootExisted = Directory.Exists(assetRoot);
        if (assetRootExisted && Directory.EnumerateFileSystemEntries(assetRoot).Any())
            throw new IOException($"The project data folder already contains files: {assetRoot}");

        try
        {
            Directory.CreateDirectory(projectDirectory);
            foreach (string directoryName in AssetDirectoryNames)
                Directory.CreateDirectory(Path.Combine(assetRoot, directoryName));

            var project = new AgbSynthProjectFile
            {
                Import = new ImportProjectInfo
                {
                    ReadMode = "NewProject",
                    IncludeUnreferencedVoiceGroups = false,
                    SequenceExportMode = SequenceExportMode.Midi
                },
                SongTable = new SongTableProjectInfo
                {
                    EntrySize = Mp2kSongTableParser.DefaultEntrySize,
                    ValidEntryCount = 0
                }
            };

            AgbSynthProjectSequenceExporter.ExportSongTableAndHeaders(project, outputPath);
            Save(outputPath, project);
            return project;
        }
        catch
        {
            if (!assetRootExisted && Directory.Exists(assetRoot))
                Directory.Delete(assetRoot, recursive: true);
            throw;
        }
    }

    public static AgbSynthProjectFile CreateFromRom(GbaRom rom, int songTableOffset, string songTableAddressText)
    {
        return CreateFromRom(
            rom,
            new Mp2kImportOptions
            {
                ReadMode = Mp2kRomReadMode.ManualSongTableAddress,
                SongTableOffset = songTableOffset,
                SongTableAddressText = songTableAddressText
            });
    }

    public static AgbSynthProjectFile CreateFromRom(GbaRom rom, Mp2kImportOptions options)
    {
        int songTableOffset = ResolveSongTableOffset(rom, options);
        string songTableAddressText = string.IsNullOrWhiteSpace(options.SongTableAddressText)
            ? $"0x{songTableOffset:X}"
            : options.SongTableAddressText;
        var songs = new List<SongTableEntryProjectInfo>();
        var songHeaders = new List<SongHeaderProjectInfo>();
        int lastValidSongIndex = -1;
        int emptyGap = 0;
        for (int songId = 0; songId < MaxInitialSongTableEntries; songId++)
        {
            int entryOffset = songTableOffset + songId * Mp2kSongTableParser.DefaultEntrySize;
            if (!TryReadRawSongTableEntry(rom, entryOffset, out byte[] rawEntry, out uint headerPointer))
                break;

            bool hasValidEntry = Mp2kSongTableParser.TryReadEntry(rom, songTableOffset, songId, out var entry);
            Mp2kSongHeader? header = null;
            bool hasValidHeader = hasValidEntry && Mp2kSongHeaderParser.TryReadHeader(rom, entry, out header);
            if (!hasValidHeader)
            {
                if (lastValidSongIndex < 0)
                    break;

                songs.Add(new SongTableEntryProjectInfo
                {
                    SongId = songId,
                    TableOffset = entryOffset,
                    HeaderPointer = headerPointer == 0 ? string.Empty : FormatHex(headerPointer),
                    HeaderOffset = hasValidEntry ? entry.HeaderOffset : 0,
                    Group1 = rawEntry.Length > 4 ? rawEntry[4] : 0,
                    Group2 = rawEntry.Length > 5 ? rawEntry[5] : 0,
                    Note = headerPointer == 0 ? "Empty SongTable slot" : $"Invalid SongHeader reference {FormatHex(headerPointer)}",
                    RawEntryHex = Convert.ToHexString(rawEntry)
                });

                emptyGap++;
                if (emptyGap > MaxEmptySongTableGap)
                    break;

                continue;
            }

            emptyGap = 0;
            lastValidSongIndex = songs.Count;
            var validHeader = header!;

            songs.Add(new SongTableEntryProjectInfo
            {
                SongId = entry.SongId,
                TableOffset = entry.TableOffset,
                HeaderPointer = FormatHex(entry.HeaderPointer),
                HeaderOffset = entry.HeaderOffset,
                Group1 = entry.RawEntry.Length > 4 ? entry.RawEntry[4] : 0,
                Group2 = entry.RawEntry.Length > 5 ? entry.RawEntry[5] : 0,
                RawEntryHex = Convert.ToHexString(entry.RawEntry)
            });

            songHeaders.Add(new SongHeaderProjectInfo
            {
                SongId = validHeader.SongId,
                HeaderOffset = validHeader.HeaderOffset,
                TrackCount = validHeader.TrackCount,
                BlockCount = validHeader.BlockCount,
                Priority = validHeader.Priority,
                Reverb = validHeader.Reverb,
                VoiceGroupPointer = FormatHex(validHeader.VoiceGroupPointer),
                VoiceGroupOffset = validHeader.VoiceGroupOffset,
                TrackPointers = validHeader.TrackPointers.Select(FormatHex).ToList(),
                TrackOffsets = validHeader.TrackOffsets.ToList(),
                RawHeaderHex = Convert.ToHexString(validHeader.RawHeader)
            });
        }

        if (lastValidSongIndex >= 0 && lastValidSongIndex < songs.Count - 1)
            songs.RemoveRange(lastValidSongIndex + 1, songs.Count - lastValidSongIndex - 1);

        var voiceGroups = CreateVoiceGroups(rom, songHeaders);
        if (options.IncludeUnreferencedVoiceGroups)
            AddUnreferencedVoiceGroups(rom, voiceGroups);
        bool soundModeDetected = Mp2kRomDiscovery.TryFindSoundMode(rom, songTableOffset, out var soundMode);

        return new AgbSynthProjectFile
        {
            Import = new ImportProjectInfo
            {
                ReadMode = options.ReadMode.ToString(),
                IncludeUnreferencedVoiceGroups = options.IncludeUnreferencedVoiceGroups,
                SequenceExportMode = options.SequenceExportMode
            },
            Rom = new RomProjectInfo
            {
                GameTitle = rom.GameTitle,
                GameCode = rom.GameCode,
                Crc32 = rom.Crc32.ToString("X8"),
                SizeBytes = rom.Length
            },
            SoundMode = new Mp2kSoundModeProjectInfo
            {
                Address = soundModeDetected ? FormatHex(GbaAddress.ToPointer(soundMode.Offset)) : string.Empty,
                Offset = soundModeDetected ? soundMode.Offset : null,
                RawValue = soundModeDetected ? FormatHex(soundMode.RawValue) : string.Empty,
                Reverb = soundMode.Reverb,
                MaxChannels = soundMode.MaxChannels,
                Volume = soundMode.Volume,
                FrequencyIndex = soundMode.FrequencyIndex,
                DacConfig = soundMode.DacConfig,
                FixedSampleRate = soundMode.FixedSampleRate,
                Detected = soundModeDetected
            },
            SongTable = new SongTableProjectInfo
            {
                Address = songTableAddressText,
                Offset = songTableOffset,
                EntrySize = Mp2kSongTableParser.DefaultEntrySize,
                ValidEntryCount = songs.Count
            },
            Songs = songs,
            SongHeaders = songHeaders,
            VoiceGroups = voiceGroups
        };
    }

    public static void Save(string outputPath, AgbSynthProjectFile project)
    {
        if (project.IsReadOnly)
            throw new InvalidOperationException("This project was created by a newer AgbSynth version and cannot be overwritten.");
        File.WriteAllBytes(outputPath, Serialize(project));
    }

    public static byte[] Serialize(AgbSynthProjectFile project)
    {
        if (project.IsReadOnly)
            throw new InvalidOperationException("This project was created by a newer AgbSynth version and cannot be overwritten.");
        project.Format = AgbSynthFormatContracts.ProjectFormat;
        project.Version = AgbSynthFormatContracts.ProjectVersion;
        project.Engine = AgbSynthFormatContracts.Engine;
        var options = new JsonSerializerOptions { WriteIndented = true };
        return JsonSerializer.SerializeToUtf8Bytes(project, options);
    }

    private static string FormatHex(uint value) => $"0x{value:X8}";

    private static bool TryReadRawSongTableEntry(GbaRom rom, int entryOffset, out byte[] rawEntry, out uint headerPointer)
    {
        rawEntry = [];
        headerPointer = 0;
        if (!rom.TryReadUInt32LittleEndian(entryOffset, out headerPointer))
            return false;
        if (entryOffset < 0 || entryOffset + Mp2kSongTableParser.DefaultEntrySize > rom.Length)
            return false;

        rawEntry = rom.Slice(entryOffset, Mp2kSongTableParser.DefaultEntrySize).ToArray();
        return true;
    }

    private static int ResolveSongTableOffset(GbaRom rom, Mp2kImportOptions options)
    {
        if (options.ReadMode == Mp2kRomReadMode.AutomaticDiscovery)
        {
            if (Mp2kRomDiscovery.TryFindSongTable(rom, out int discoveredOffset, out _))
                return discoveredOffset;

            throw new InvalidDataException("No plausible MP2K song table was found.");
        }

        if (options.SongTableOffset is int manualOffset)
            return manualOffset;

        throw new InvalidDataException("A song table address is required for manual import.");
    }

    private static List<VoiceGroupProjectInfo> CreateVoiceGroups(GbaRom rom, IReadOnlyList<SongHeaderProjectInfo> songHeaders)
    {
        var groups = new List<VoiceGroupProjectInfo>();
        int id = 0;
        foreach (var headerGroup in songHeaders.GroupBy(h => h.VoiceGroupPointer).OrderBy(g => g.Key))
        {
            var first = headerGroup.First();
            if (!TryParseHex(first.VoiceGroupPointer, out uint pointer))
                continue;
            if (!Mp2kVoiceGroupParser.TryReadVoiceGroup(rom, pointer, first.VoiceGroupOffset, out var voiceGroup))
                continue;

            groups.Add(new VoiceGroupProjectInfo
            {
                Id = id++,
                Pointer = FormatHex(voiceGroup.Pointer),
                Offset = voiceGroup.Offset,
                DiscoverySource = "Referenced",
                UsedBySongIds = headerGroup.Select(h => h.SongId).OrderBy(id => id).ToList(),
                Voices = voiceGroup.Voices.Select(CreateVoiceInfo).ToList()
            });
        }

        return groups;
    }

    private static void AddUnreferencedVoiceGroups(GbaRom rom, List<VoiceGroupProjectInfo> groups)
    {
        var excludedOffsets = new HashSet<int>(groups.Select(g => g.Offset));
        foreach (var group in groups)
        {
            foreach (var voice in group.Voices)
                AddNestedTableOffsets(voice, excludedOffsets);
        }

        foreach (var voiceGroup in Mp2kRomDiscovery.FindUnreferencedVoiceGroups(rom, excludedOffsets))
        {
            groups.Add(new VoiceGroupProjectInfo
            {
                Id = groups.Count,
                Pointer = FormatHex(voiceGroup.Pointer),
                Offset = voiceGroup.Offset,
                DiscoverySource = "Discovered",
                Voices = voiceGroup.Voices.Select(CreateVoiceInfo).ToList()
            });
        }
    }

    private static void AddNestedTableOffsets(VoiceProjectInfo voice, HashSet<int> offsets)
    {
        if (voice.DrumSet is not null)
        {
            offsets.Add(voice.DrumSet.TableOffset);
            foreach (var entry in voice.DrumSet.Entries)
                AddNestedTableOffsets(entry, offsets);
        }

        if (voice.KeySplit is not null)
        {
            offsets.Add(voice.KeySplit.RegionTableOffset);
            if (voice.KeySplit.KeyMapOffset is int keyMapOffset)
                offsets.Add(keyMapOffset);
            foreach (var region in voice.KeySplit.Regions)
                AddNestedTableOffsets(region, offsets);
        }
    }

    private static VoiceProjectInfo CreateVoiceInfo(Mp2kVoice voice)
    {
        string typeName = Mp2kVoiceGroupParser.GetVoiceTypeName(voice.Type, voice.DataPointer);
        return new VoiceProjectInfo
        {
            Index = voice.Index,
            Label = typeName,
            Type = voice.Type,
            TypeName = typeName,
            Key = voice.Key,
            Length = voice.Length,
            PanOrSweep = voice.PanOrSweep,
            DataPointer = FormatHex(voice.DataPointer),
            DataOffset = voice.DataOffset,
            Attack = voice.Attack,
            Decay = voice.Decay,
            Sustain = voice.Sustain,
            Release = voice.Release,
            Sample = voice.Sample is null
                ? null
                : new SampleHeaderProjectInfo
                {
                    HeaderOffset = voice.Sample.HeaderOffset,
                    LoopFlags = voice.Sample.LoopFlags,
                    Loops = (voice.Sample.LoopFlags & 0x40) != 0,
                    Frequency = voice.Sample.Frequency,
                    LoopStart = voice.Sample.LoopStart,
                    Size = voice.Sample.Size,
                    DataOffset = voice.Sample.DataOffset
                },
            PsgSquare = IsPsgSquareVoice(voice.Type)
                ? new PsgSquareProjectInfo
                {
                    DutyIndex = GetPsgSquareDutyIndex(voice.Raw),
                    DutyRatio = GetPsgSquareDutyRatio(GetPsgSquareDutyIndex(voice.Raw))
                }
                : null,
            PsgWaveMemory = IsPsgWaveMemoryVoice(voice.Type) && voice.DataOffset is int waveMemoryOffset
                ? new PsgWaveMemoryProjectInfo
                {
                    DataOffset = waveMemoryOffset
                }
                : null,
            PsgNoise = IsPsgNoiseVoice(voice.Type)
                ? CreatePsgNoiseInfo(voice.Type, voice.Raw)
                : null,
            DrumSet = voice.DrumSet is null
                ? null
                : new DrumSetProjectInfo
                {
                    TableOffset = voice.DrumSet.TableOffset,
                    Entries = voice.DrumSet.Entries.Select(CreateVoiceInfo).ToList(),
                    RawHex = Convert.ToHexString(voice.DrumSet.Raw)
                },
            KeySplit = voice.KeySplit is null
                ? null
                : new KeySplitProjectInfo
                {
                    RegionTableOffset = voice.KeySplit.RegionTableOffset,
                    KeyMapOffset = voice.KeySplit.KeyMapOffset,
                    Regions = voice.KeySplit.Regions.Select(CreateVoiceInfo).ToList(),
                    KeyMapHex = Convert.ToHexString(voice.KeySplit.KeyMap),
                    RawRegionTableHex = Convert.ToHexString(voice.KeySplit.RawRegionTable)
                },
            RawEntryHex = Convert.ToHexString(voice.Raw)
        };
    }

    private static bool IsPsgSquareVoice(byte type)
    {
        return (type & 0x07) is 0x01 or 0x02;
    }

    private static bool IsPsgNoiseVoice(byte type)
    {
        return (type & 0x07) == 0x04;
    }

    private static bool IsPsgWaveMemoryVoice(byte type)
    {
        return (type & 0x07) == 0x03;
    }

    private static int GetPsgSquareDutyIndex(byte[] raw)
    {
        return raw.Length > 4 ? raw[4] & 0x03 : 2;
    }

    private static double GetPsgSquareDutyRatio(int dutyIndex)
    {
        return dutyIndex switch
        {
            0 => 0.125,
            1 => 0.25,
            2 => 0.5,
            3 => 0.75,
            _ => 0.5
        };
    }

    private static PsgNoiseProjectInfo CreatePsgNoiseInfo(byte type, byte[] raw)
    {
        int control = raw.Length > 4 ? raw[4] : 0;
        return new PsgNoiseProjectInfo
        {
            Control = control,
            ClockDivider = control & 0x07,
            ShortLfsr = (control & 0x08) != 0,
            PrescalerShift = (control >> 4) & 0x0F,
            PinkNoise = (control & 0x08) != 0
        };
    }

    private static bool TryParseHex(string text, out uint value)
    {
        string normalized = text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? text[2..]
            : text;
        return uint.TryParse(normalized, System.Globalization.NumberStyles.HexNumber, null, out value);
    }
}

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
    private const int MaxInitialSongTableEntries = 512;

    public static AgbSynthProjectFile CreateFromRom(GbaRom rom, int songTableOffset, string songTableAddressText)
    {
        var songs = new List<SongTableEntryProjectInfo>();
        var songHeaders = new List<SongHeaderProjectInfo>();
        for (int songId = 0; songId < MaxInitialSongTableEntries; songId++)
        {
            if (!Mp2kSongTableParser.TryReadEntry(rom, songTableOffset, songId, out var entry))
                continue;

            songs.Add(new SongTableEntryProjectInfo
            {
                SongId = entry.SongId,
                TableOffset = entry.TableOffset,
                HeaderPointer = FormatHex(entry.HeaderPointer),
                HeaderOffset = entry.HeaderOffset,
                RawEntryHex = Convert.ToHexString(entry.RawEntry)
            });

            if (Mp2kSongHeaderParser.TryReadHeader(rom, entry, out var header))
            {
                songHeaders.Add(new SongHeaderProjectInfo
                {
                    SongId = header.SongId,
                    HeaderOffset = header.HeaderOffset,
                    TrackCount = header.TrackCount,
                    BlockCount = header.BlockCount,
                    Priority = header.Priority,
                    Reverb = header.Reverb,
                    VoiceGroupPointer = FormatHex(header.VoiceGroupPointer),
                    VoiceGroupOffset = header.VoiceGroupOffset,
                    TrackPointers = header.TrackPointers.Select(FormatHex).ToList(),
                    TrackOffsets = header.TrackOffsets.ToList(),
                    RawHeaderHex = Convert.ToHexString(header.RawHeader)
                });
            }
        }

        return new AgbSynthProjectFile
        {
            Rom = new RomProjectInfo
            {
                SourcePath = rom.SourcePath,
                GameTitle = rom.GameTitle,
                GameCode = rom.GameCode,
                Crc32 = rom.Crc32.ToString("X8"),
                SizeBytes = rom.Length
            },
            SongTable = new SongTableProjectInfo
            {
                Address = songTableAddressText,
                Offset = songTableOffset,
                EntrySize = Mp2kSongTableParser.DefaultEntrySize,
                ValidEntryCount = songs.Count
            },
            Songs = songs,
            SongHeaders = songHeaders
        };
    }

    public static void Save(string outputPath, AgbSynthProjectFile project)
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        File.WriteAllText(outputPath, JsonSerializer.Serialize(project, options));
    }

    private static string FormatHex(uint value) => $"0x{value:X8}";
}

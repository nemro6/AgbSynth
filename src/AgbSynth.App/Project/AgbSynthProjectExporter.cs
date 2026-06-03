using System;
using System.Collections.Generic;
using System.IO;
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
            Songs = songs
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

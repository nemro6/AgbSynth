using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace AgbSynth.App.Project;

public static class AgbSynthProjectSequenceExporter
{
    public static int ExportSongTableAndHeaders(AgbSynthProjectFile project, string projectPath)
    {
        string projectDirectory = Path.GetDirectoryName(projectPath) ?? Directory.GetCurrentDirectory();
        string assetRootName = $"{Path.GetFileNameWithoutExtension(projectPath)}_data";
        string songTableDirectory = Path.Combine(projectDirectory, assetRootName, "songtable");
        string songHeaderDirectory = Path.Combine(projectDirectory, assetRootName, "songheader");
        Directory.CreateDirectory(songTableDirectory);
        Directory.CreateDirectory(songHeaderDirectory);

        var options = new JsonSerializerOptions { WriteIndented = true };
        int exportedCount = 0;
        foreach (var header in project.SongHeaders)
        {
            string fileName = $"songheader_{header.SongId:D3}.agbsh";
            string relativePath = $"{assetRootName}/songheader/{fileName}";
            if (string.IsNullOrWhiteSpace(header.Label))
                header.Label = Path.GetFileNameWithoutExtension(fileName);
            if (string.IsNullOrWhiteSpace(header.Note))
                header.Note = $"Exported from 0x{header.HeaderOffset:X}";
            header.FilePath = relativePath;
            File.WriteAllText(
                Path.Combine(songHeaderDirectory, fileName),
                JsonSerializer.Serialize(new AgbSongHeaderDocument { Header = header }, options));
            exportedCount++;
        }

        foreach (var song in project.Songs)
        {
            SongHeaderProjectInfo? header = project.SongHeaders.Find(h => h.SongId == song.SongId);
            song.SongHeaderFilePath = header?.FilePath ?? string.Empty;
            if (string.IsNullOrWhiteSpace(song.Label) && header is not null)
                song.Label = string.IsNullOrWhiteSpace(header.Label)
                    ? Path.GetFileNameWithoutExtension(header.FilePath)
                    : header.Label;
            if (string.IsNullOrWhiteSpace(song.Note))
                song.Note = string.IsNullOrWhiteSpace(song.HeaderPointer)
                    ? $"Exported from 0x{song.HeaderOffset:X}"
                    : $"Exported from {song.HeaderPointer}";
        }

        string songTableFileName = "songtable.agbst";
        project.SongTable.FilePath = $"{assetRootName}/songtable/{songTableFileName}";
        File.WriteAllText(
            Path.Combine(songTableDirectory, songTableFileName),
            JsonSerializer.Serialize(
                new AgbSongTableDocument
                {
                    SongTable = project.SongTable,
                    Entries = project.Songs
                },
                options));

        return exportedCount + 1;
    }

    private sealed class AgbSongTableDocument
    {
        public string Format { get; set; } = "AgbSynthSongTable";
        public int Version { get; set; } = 1;
        public string Engine { get; set; } = "MP2K";
        public SongTableProjectInfo SongTable { get; set; } = new();
        public List<SongTableEntryProjectInfo> Entries { get; set; } = new();
    }

    private sealed class AgbSongHeaderDocument
    {
        public string Format { get; set; } = "AgbSynthSongHeader";
        public int Version { get; set; } = 1;
        public string Engine { get; set; } = "MP2K";
        public SongHeaderProjectInfo Header { get; set; } = new();
    }
}

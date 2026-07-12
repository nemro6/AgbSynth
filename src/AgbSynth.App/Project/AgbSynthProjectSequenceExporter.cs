using System.Collections.Generic;
using System.IO;

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
            File.WriteAllBytes(Path.Combine(songHeaderDirectory, fileName), AgbSynthProjectAssetWriter.SerializeSongHeader(header));
            exportedCount++;
        }

        foreach (var song in project.Songs)
        {
            SongHeaderProjectInfo? header = project.SongHeaders.Find(h => h.SongId == song.SongId);
            song.SongHeaderFilePath = header?.FilePath ?? string.Empty;
            song.SongHeaderAssetId = header?.AssetId ?? string.Empty;
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
        File.WriteAllBytes(
            Path.Combine(songTableDirectory, songTableFileName),
            AgbSynthProjectAssetWriter.SerializeSongTable(project.SongTable, project.Songs));

        return exportedCount + 1;
    }
}

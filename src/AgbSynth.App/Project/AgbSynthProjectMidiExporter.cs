using System;
using System.IO;
using AgbSynth.App.GBA;
using AgbSynth.App.MP2K;

namespace AgbSynth.App.Project;

public static class AgbSynthProjectMidiExporter
{
    public static int ExportMidiFiles(
        GbaRom rom,
        AgbSynthProjectFile project,
        string projectPath,
        MidiCcMapping? midiCcMapping = null)
    {
        string projectDirectory = Path.GetDirectoryName(projectPath) ?? Directory.GetCurrentDirectory();
        string assetRootName = $"{Path.GetFileNameWithoutExtension(projectPath)}_data";
        string midiDirectory = Path.Combine(projectDirectory, assetRootName, "midi");
        Directory.CreateDirectory(midiDirectory);

        int exportedCount = 0;
        foreach (var header in project.SongHeaders)
        {
            if (header.TrackCount <= 0 || header.TrackOffsets.Count == 0)
                continue;

            string fileName = $"song_{header.SongId:D3}.mid";
            string path = Path.Combine(midiDirectory, fileName);
            Mp2kSequenceMidiConverter.WriteMidiFile(rom, header, path, midiCcMapping);
            header.MidiFilePath = $"{assetRootName}/midi/{fileName}";
            exportedCount++;
        }

        return exportedCount;
    }
}

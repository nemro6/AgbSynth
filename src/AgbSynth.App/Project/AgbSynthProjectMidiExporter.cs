using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using AgbSynth.App.GBA;
using AgbSynth.App.MIDI;
using AgbSynth.App.MP2K;
using AgbSynth.App.MP2K.Sequence;

namespace AgbSynth.App.Project;

public static class AgbSynthProjectMidiExporter
{
    public sealed record ExportResult(int MidiCount, int Midi2AgbCount, int LossCount);

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

    public static ExportResult ExportSequenceFiles(
        GbaRom rom,
        AgbSynthProjectFile project,
        string projectPath,
        SequenceExportMode mode,
        MidiCcMapping? midiCcMapping = null)
    {
        midiCcMapping ??= MidiCcMapping.Default;
        string projectDirectory = Path.GetDirectoryName(projectPath) ?? Directory.GetCurrentDirectory();
        string assetRootName = $"{Path.GetFileNameWithoutExtension(projectPath)}_data";
        string midiDirectory = Path.Combine(projectDirectory, assetRootName, "midi");
        string sourceDirectory = Path.Combine(projectDirectory, assetRootName, "midi2agb");
        if (mode is SequenceExportMode.Midi or SequenceExportMode.Both)
            Directory.CreateDirectory(midiDirectory);
        if (mode is SequenceExportMode.Midi2Agb or SequenceExportMode.Both)
            Directory.CreateDirectory(sourceDirectory);

        int midiCount = 0;
        int sourceCount = 0;
        int lossCount = 0;
        foreach (SongHeaderProjectInfo header in project.SongHeaders)
        {
            if (header.TrackCount <= 0 || header.TrackOffsets.Count == 0)
                continue;

            IReadOnlyList<MidiTrack> tracks = Mp2kSequenceMidiConverter.BuildMidi(rom, header, midiCcMapping);
            byte[] midiBytes = MidiFileWriter.Build(tracks, Mp2kSequenceMidiConverter.TicksPerQuarter);
            string baseName = $"song_{header.SongId:D3}";
            if (mode is SequenceExportMode.Midi or SequenceExportMode.Both)
            {
                string midiFileName = $"{baseName}.mid";
                File.WriteAllBytes(Path.Combine(midiDirectory, midiFileName), midiBytes);
                header.MidiFilePath = $"{assetRootName}/midi/{midiFileName}";
                midiCount++;
            }
            if (mode is SequenceExportMode.Midi2Agb or SequenceExportMode.Both)
            {
                var report = new SequenceConversionReport();
                string source = Mp2kSequenceAssemblyExporter.Disassemble(rom, header, baseName, report);
                string sourceFileName = $"{baseName}.s";
                File.WriteAllText(Path.Combine(sourceDirectory, sourceFileName), source);
                if (report.HasLoss)
                {
                    string reportPath = Path.Combine(sourceDirectory, $"{baseName}.loss.json");
                    File.WriteAllText(reportPath, JsonSerializer.Serialize(report.Issues, new JsonSerializerOptions { WriteIndented = true }));
                }
                header.Midi2AgbFilePath = $"{assetRootName}/midi2agb/{sourceFileName}";
                sourceCount++;
                lossCount += report.Issues.Count;
            }

            header.SequenceFormat = mode == SequenceExportMode.Midi2Agb ? SequenceAssetFormat.Midi2Agb : SequenceAssetFormat.Midi;
            header.SequenceFilePath = header.SequenceFormat == SequenceAssetFormat.Midi2Agb
                ? header.Midi2AgbFilePath
                : header.MidiFilePath;
        }
        return new ExportResult(midiCount, sourceCount, lossCount);
    }
}

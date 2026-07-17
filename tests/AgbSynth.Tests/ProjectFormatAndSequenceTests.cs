using System.Text.Json;
using AgbSynth.App.GBA;
using AgbSynth.App.MIDI;
using AgbSynth.App.MP2K;
using AgbSynth.App.MP2K.Sequence;
using AgbSynth.App.Project;
using Xunit;

namespace AgbSynth.Tests;

public sealed class ProjectFormatAndSequenceTests
{
    [Fact]
    public void Loader_MigratesLegacyAssetsAndLinksRenamedFileByAssetId()
    {
        string directory = CreateTempDirectory();
        string projectPath = Path.Combine(directory, "legacy.agbsynth");
        try
        {
            Directory.CreateDirectory(Path.Combine(directory, "legacy_data", "songtable"));
            Directory.CreateDirectory(Path.Combine(directory, "legacy_data", "songheader"));
            File.WriteAllText(projectPath, """
                { "Format":"AgbSynthProject", "Version":3, "Engine":"MP2K", "SongTable": { "FilePath":"legacy_data/songtable/songtable.agbst" } }
                """);
            const string headerId = "0123456789abcdef0123456789abcdef";
            File.WriteAllText(Path.Combine(directory, "legacy_data", "songheader", "renamed.agbsh"), $$"""
                { "Format":"AgbSynthSongHeader", "Version":2, "Engine":"MP2K", "AssetId":"{{headerId}}", "Header": { "AssetId":"{{headerId}}", "SongId":7, "Label":"Renamed", "FilePath":"legacy_data/songheader/old.agbsh" } }
                """);
            File.WriteAllText(Path.Combine(directory, "legacy_data", "songtable", "songtable.agbst"), $$"""
                { "Format":"AgbSynthSongTable", "Version":1, "Engine":"MP2K", "SongTable": { "FilePath":"legacy_data/songtable/songtable.agbst" }, "Entries":[ { "SongId":7, "SongHeaderAssetId":"{{headerId}}", "SongHeaderFilePath":"legacy_data/songheader/old.agbsh" } ] }
                """);

            AgbSynthProjectFile project = AgbSynthProjectLoader.Load(projectPath);

            Assert.Equal(AgbSynthFormatContracts.ProjectVersion, project.Version);
            Assert.Contains(project.Diagnostics, value => value.Code == "PROJECT_MIGRATED");
            Assert.Equal(headerId, Assert.Single(project.SongHeaders).AssetId);
            Assert.Equal("legacy_data/songheader/renamed.agbsh", Assert.Single(project.Songs).SongHeaderFilePath);
            Assert.False(project.IsReadOnly);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void Loader_NewerProjectIsReadOnlyAndCannotBeSerialized()
    {
        string directory = CreateTempDirectory();
        string projectPath = Path.Combine(directory, "future.agbsynth");
        try
        {
            File.WriteAllText(projectPath, $$"""
                { "Format":"AgbSynthProject", "Version":{{AgbSynthFormatContracts.ProjectVersion + 1}}, "Engine":"MP2K" }
                """);
            AgbSynthProjectFile project = AgbSynthProjectLoader.Load(projectPath);
            Assert.True(project.IsReadOnly);
            Assert.Contains(project.Diagnostics, value => value.Code == "PROJECT_NEWER_VERSION");
            Assert.Throws<InvalidOperationException>(() => AgbSynthProjectExporter.Serialize(project));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void Loader_ReportsMalformedAssetWithFileName()
    {
        string directory = CreateTempDirectory();
        string projectPath = Path.Combine(directory, "broken.agbsynth");
        try
        {
            AgbSynthProjectExporter.CreateBlankProject(projectPath);
            string brokenPath = Path.Combine(directory, "broken_data", "songheader", "broken.agbsh");
            File.WriteAllText(brokenPath, "{ definitely not json");
            AgbSynthProjectFile project = AgbSynthProjectLoader.Load(projectPath);
            ProjectDiagnostic diagnostic = Assert.Single(project.Diagnostics, value => value.Code == "ASSET_LOAD_FAILED");
            Assert.Equal(brokenPath, diagnostic.FilePath);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void Midi2Agb_ParseSupportsTempoPatternRepeatLoopAndExtendedCommands()
    {
        const string source = """
            song:
                .byte 1, 0, 0, 0
                .word 0
                .word song_track_01
            song_track_01:
                .byte TEMPO, 90
                .byte VOICE, 3
                .byte PATT
                .word pattern_a
                .byte REPT, 2
                .word pattern_b
            loop_point:
                .byte XCMD, 0x04, 200
                .byte MEMACC, 0, 0, 0
                .byte W24
                .byte GOTO
                .word loop_point
            pattern_a:
                .byte N24, 60, 100
                .byte W24
                .byte PEND
            pattern_b:
                .byte N12, 64, 90
                .byte W12
                .byte PEND
            """;
        var report = new SequenceConversionReport();

        MidiPlaybackFile midi = Midi2AgbSequenceCodec.Parse(source, MidiCcMapping.Default, report);

        Assert.Contains(midi.Events, value => value.Kind == MidiPlaybackEventKind.Tempo && value.Data3 == 60_000_000 / 90);
        Assert.Equal(3, midi.Events.Count(value => value.Kind == MidiPlaybackEventKind.NoteOn));
        Assert.Contains(midi.Events, value => value.Kind == MidiPlaybackEventKind.ControlChange && value.Data1 == MidiCcMapping.Default.Attack && value.Data2 == 127);
        MidiPlaybackEvent loopStart = Assert.Single(midi.Events, value => value.Kind == MidiPlaybackEventKind.ControlChange && value.Data1 == MidiCcMapping.Default.LoopStart);
        MidiPlaybackEvent loopEnd = Assert.Single(midi.Events, value => value.Kind == MidiPlaybackEventKind.ControlChange && value.Data1 == MidiCcMapping.Default.LoopEnd);
        Assert.Equal(48, loopStart.Tick);
        Assert.Equal(72, loopEnd.Tick);
        Assert.Contains(report.Issues, value => value.Code == "MEMACC_PLAYBACK_ONLY");
    }

    [Fact]
    public void Midi2Agb_WriteAndParsePreservesPlayableEventsAndPerTrackLoop()
    {
        var source = new MidiPlaybackFile(48, new List<MidiPlaybackEvent>
        {
            new(0, 0, 0, MidiPlaybackEventKind.Tempo, 0, 0, 0, 500_000),
            new(0, 1, 1, MidiPlaybackEventKind.ProgramChange, 0, 5, 0, 0),
            new(0, 2, 1, MidiPlaybackEventKind.ControlChange, 0, MidiCcMapping.Default.LoopStart, 127, 0),
            new(0, 3, 1, MidiPlaybackEventKind.NoteOn, 0, 60, 100, 0),
            new(24, 4, 1, MidiPlaybackEventKind.NoteOff, 0, 60, 0, 0),
            new(24, 5, 1, MidiPlaybackEventKind.ControlChange, 0, MidiCcMapping.Default.LoopEnd, 127, 0)
        });
        var writeReport = new SequenceConversionReport();

        string assembly = Midi2AgbSequenceCodec.Write(source, "round_trip", MidiCcMapping.Default, writeReport);
        MidiPlaybackFile parsed = Midi2AgbSequenceCodec.Parse(assembly, MidiCcMapping.Default);

        Assert.False(writeReport.HasLoss);
        Assert.Contains(parsed.Events, value => value.Kind == MidiPlaybackEventKind.ProgramChange && value.Data1 == 5);
        Assert.Contains(parsed.Events, value => value.Kind == MidiPlaybackEventKind.NoteOn && value.Data1 == 60 && value.Tick == 0);
        Assert.Contains(parsed.Events, value => value.Kind == MidiPlaybackEventKind.NoteOff && value.Data1 == 60 && value.Tick == 24);
        Assert.Contains(parsed.Events, value => value.Kind == MidiPlaybackEventKind.ControlChange && value.Data1 == MidiCcMapping.Default.LoopEnd && value.Tick == 24);
    }

    [Fact]
    public async Task RomDisassembler_PreservesPatternRepeatMemoryAccessAndLoopCommands()
    {
        byte[] bytes = new byte[0x200];
        int position = 0x100;
        bytes[position++] = 0xBD; bytes[position++] = 3; // VOICE
        bytes[position++] = 0xB3; WriteU32(bytes, position, 0x08000120); position += 4; // PATT
        bytes[position++] = 0xB5; bytes[position++] = 2; WriteU32(bytes, position, 0x08000120); position += 4; // REPT
        bytes[position++] = 0xB9; bytes[position++] = 0; bytes[position++] = 1; bytes[position++] = 2; // MEMACC
        bytes[position++] = 0xB2; WriteU32(bytes, position, 0x08000100); // GOTO
        position = 0x120;
        bytes[position++] = 0xE7; bytes[position++] = 60; bytes[position++] = 100; // N24
        bytes[position++] = 0x98; // W24
        bytes[position] = 0xB4; // PEND
        await using var stream = new MemoryStream(bytes);
        GbaRom rom = await GbaRom.LoadAsync(stream, "fixture.gba");
        var header = new SongHeaderProjectInfo { TrackOffsets = [0x100], TrackCount = 1 };
        var report = new SequenceConversionReport();

        string source = Mp2kSequenceAssemblyExporter.Disassemble(rom, header, "fixture", report);
        MidiPlaybackFile playback = Midi2AgbSequenceCodec.Parse(source, MidiCcMapping.Default);

        Assert.Contains(".byte PATT", source);
        Assert.Contains(".byte REPT, 2", source);
        Assert.Contains(".byte MEMACC, 0, 1, 2", source);
        Assert.Contains(".byte GOTO", source);
        Assert.Equal(3, playback.Events.Count(value => value.Kind == MidiPlaybackEventKind.NoteOn));
        Assert.Contains(playback.Events, value => value.Kind == MidiPlaybackEventKind.ControlChange && value.Data1 == MidiCcMapping.Default.LoopEnd);
        Assert.DoesNotContain(report.Issues, value => value.Code == "UNSUPPORTED_MP2K_COMMAND");
    }

    [Fact]
    public async Task RomDisassembler_MatchesMidiConverterForRunningXcmdRootPendAndInternalLoop()
    {
        byte[] bytes = new byte[0x240];
        int position = 0x100;
        bytes[position++] = 0xBC; bytes[position++] = 0; // KEYSH
        bytes[position++] = 0xBD; bytes[position++] = 4; // VOICE
        bytes[position++] = 0xCD; bytes[position++] = 8; bytes[position++] = 12; // XCMD xIECV
        bytes[position++] = 8; bytes[position++] = 8; // Running XCMD xIECV
        bytes[position++] = 0xC2; bytes[position++] = 44; // LFOS
        bytes[position++] = 0xD5; bytes[position++] = 60; bytes[position++] = 100; // N06
        bytes[position++] = 0x86; // W06
        bytes[position++] = 0xB4; // Root PEND continues into the next contiguous track.
        int secondTrackOffset = position;
        int loopOffset = secondTrackOffset;
        bytes[position++] = 0xBD; bytes[position++] = 5;
        bytes[position++] = 0xD5; bytes[position++] = 64; bytes[position++] = 90;
        bytes[position++] = 0x86;
        bytes[position++] = 0xB2; WriteU32(bytes, position, GbaAddress.ToPointer(loopOffset));
        await using var stream = new MemoryStream(bytes);
        GbaRom rom = await GbaRom.LoadAsync(stream, "fixture.gba");
        var header = new SongHeaderProjectInfo { TrackOffsets = [0x100, secondTrackOffset], TrackCount = 2 };

        MidiPlaybackFile midi = MidiFileReader.Read(MidiFileWriter.Build(Mp2kSequenceMidiConverter.BuildMidi(rom, header), 48));
        string source = Mp2kSequenceAssemblyExporter.Disassemble(rom, header, "fixture");
        MidiPlaybackFile assembly = Midi2AgbSequenceCodec.Parse(source, MidiCcMapping.Default);

        Assert.Contains(".byte XCMD, 8, 12", source);
        Assert.Contains(".byte XCMD, 8, 8", source);
        Assert.Contains(".byte LFOS, 44", source);
        Assert.Equal(1, CountOccurrences(source, $"fixture_data_{loopOffset:X6}:"));
        for (int track = 0; track < 2; track++)
        {
            string[] expected = midi.Events.Where(value => value.TrackIndex == track + 1).Select(NormalizeEvent).ToArray();
            string[] actual = assembly.Events.Where(value => value.TrackIndex == track).Select(NormalizeEvent).ToArray();
            Assert.Equal(expected, actual);
        }
    }

    [Fact]
    public void Midi2AgbParser_DoesNotSpillMalformedTrackIntoNextTrack()
    {
        const string source = """
            song:
                .byte 2, 0, 0, 0
                .word 0
                .word song_track_01
                .word song_track_02
            song_track_01:
                .byte VOICE, 4
                .byte XCMD, 8, 127
            song_track_02:
                .byte VOICE, 5
                .byte N06, 64, 100
                .byte W06
                .byte FINE
            """;

        MidiPlaybackFile parsed = Midi2AgbSequenceCodec.Parse(source);

        Assert.DoesNotContain(parsed.Events, value => value.TrackIndex == 0 && value.Kind == MidiPlaybackEventKind.NoteOn);
        Assert.Contains(parsed.Events, value => value.TrackIndex == 1 && value.Kind == MidiPlaybackEventKind.NoteOn && value.Data1 == 64);
    }

    private static string CreateTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), $"AgbSynthFormatTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static void WriteU32(byte[] bytes, int offset, uint value) => BitConverter.TryWriteBytes(bytes.AsSpan(offset, 4), value);
    private static string NormalizeEvent(MidiPlaybackEvent value) => $"{value.Tick}:{value.Kind}:{value.Data1}:{value.Data2}:{value.Data3}";
    private static int CountOccurrences(string text, string value) => text.Split(value, StringSplitOptions.None).Length - 1;
}

using AgbSynth.App.GBA;
using AgbSynth.App.MP2K;
using AgbSynth.App.Project;
using Xunit;

namespace AgbSynth.Tests;

public sealed class Mp2kSequenceMidiConverterTests
{
    [Fact]
    public async Task BuildMidi_ConvertsBasicTrackToStandardMidi()
    {
        byte[] romBytes = CreateRomWithSong(
            0xBB, 120,
            0xBD, 5,
            0xBE, 100,
            0xBF, 64,
            0xD3, 60, 100,
            0x98,
            0xB1);

        using var ms = new MemoryStream(romBytes);
        var rom = await GbaRom.LoadAsync(ms, "test.gba");
        var project = AgbSynthProjectExporter.CreateFromRom(rom, 0x100, "0x100");

        var midi = Mp2kSequenceMidiConverter.BuildMidi(rom, project.SongHeaders.Single());
        byte[] bytes = AgbSynth.App.MIDI.MidiFileWriter.Build(midi, Mp2kSequenceMidiConverter.TicksPerQuarter);

        Assert.Equal(1, midi[0].Events.Count(e => e.Data.Length >= 2 && e.Data[0] == 0xFF && e.Data[1] == 0x51));
        Assert.Equal((byte)'M', bytes[0]);
        Assert.Equal((byte)'T', bytes[1]);
        Assert.Equal((byte)'h', bytes[2]);
        Assert.Equal((byte)'d', bytes[3]);
        Assert.Equal(0x00, bytes[12]);
        Assert.Equal(0x30, bytes[13]);
        Assert.Contains((byte)0x90, bytes);
        Assert.Contains((byte)0x80, bytes);
    }

    [Fact]
    public async Task BuildMidi_ReusesPreviousNoteParametersAndRunningNoteCommand()
    {
        byte[] romBytes = CreateRomWithSong(
            0xD1, 0x3C, 0x70,
            0x84,
            0xD1,
            0x84,
            0x30,
            0xB1);

        using var ms = new MemoryStream(romBytes);
        var rom = await GbaRom.LoadAsync(ms, "test.gba");
        var project = AgbSynthProjectExporter.CreateFromRom(rom, 0x100, "0x100");

        var midi = Mp2kSequenceMidiConverter.BuildMidi(rom, project.SongHeaders.Single());
        byte[] bytes = AgbSynth.App.MIDI.MidiFileWriter.Build(midi, Mp2kSequenceMidiConverter.TicksPerQuarter);

        Assert.Contains((byte)0x3C, bytes);
        Assert.Contains((byte)0x30, bytes);
        Assert.DoesNotContain((byte)0x7F, bytes);
    }

    [Fact]
    public async Task BuildMidi_ControlCommandDoesNotReplaceRunningNoteCommand()
    {
        byte[] romBytes = CreateRomWithSong(
            0xD1, 0x3C, 0x70,
            0x84,
            0xBB, 0x40,
            0x84,
            0x3E,
            0xB1);

        using var ms = new MemoryStream(romBytes);
        var rom = await GbaRom.LoadAsync(ms, "test.gba");
        var project = AgbSynthProjectExporter.CreateFromRom(rom, 0x100, "0x100");

        var midi = Mp2kSequenceMidiConverter.BuildMidi(rom, project.SongHeaders.Single());
        var conductorTempos = midi[0].Events.Count(e => e.Data.Length >= 2 && e.Data[0] == 0xFF && e.Data[1] == 0x51);
        byte[] bytes = AgbSynth.App.MIDI.MidiFileWriter.Build(midi, Mp2kSequenceMidiConverter.TicksPerQuarter);

        Assert.Equal(2, conductorTempos);
        Assert.Equal(2, bytes.Count(b => b == 0x90));
        Assert.Contains((byte)0x3E, bytes);
    }

    [Fact]
    public async Task BuildMidi_RepeatableControlCommandReplacesRunningNoteCommand()
    {
        byte[] romBytes = CreateRomWithSong(
            0xD1, 0x3C, 0x70,
            0x84,
            0xBE, 0x64,
            0x84,
            0x50,
            0xB1);

        using var ms = new MemoryStream(romBytes);
        var rom = await GbaRom.LoadAsync(ms, "test.gba");
        var project = AgbSynthProjectExporter.CreateFromRom(rom, 0x100, "0x100");

        var midi = Mp2kSequenceMidiConverter.BuildMidi(rom, project.SongHeaders.Single());
        byte[] bytes = AgbSynth.App.MIDI.MidiFileWriter.Build(midi, Mp2kSequenceMidiConverter.TicksPerQuarter);

        Assert.Equal(1, bytes.Count(b => b == 0x90));
        AssertContainsMidiEvent(bytes, 0xB0, 7, 0x64);
        AssertContainsMidiEvent(bytes, 0xB0, 7, 0x50);
    }

    [Fact]
    public async Task BuildMidi_ConsumesNoteAddedDuration()
    {
        byte[] romBytes = CreateRomWithSong(
            0xD1, 0x3C, 0x70, 0x03,
            0x84,
            0xB1);

        using var ms = new MemoryStream(romBytes);
        var rom = await GbaRom.LoadAsync(ms, "test.gba");
        var project = AgbSynthProjectExporter.CreateFromRom(rom, 0x100, "0x100");

        var midi = Mp2kSequenceMidiConverter.BuildMidi(rom, project.SongHeaders.Single());
        byte[] bytes = AgbSynth.App.MIDI.MidiFileWriter.Build(midi, Mp2kSequenceMidiConverter.TicksPerQuarter);

        Assert.Equal(1, bytes.Count(b => b == 0x90));
        Assert.Contains(midi[1].Events, e => e.Tick == 5 && e.Data.Length >= 3 && (e.Data[0] & 0xF0) == 0x80 && e.Data[1] == 0x3C);
    }

    [Fact]
    public async Task BuildMidi_ConsumesThreeByteMemaccWithoutEatingFollowingCommand()
    {
        byte[] romBytes = CreateRomWithSong(
            0xD1, 0x30, 0x70,
            0x8C,
            0xB9, 0x00, 0x00, 0x75,
            0xBF, 0x60,
            0x8C,
            0xD1,
            0xB1);

        using var ms = new MemoryStream(romBytes);
        var rom = await GbaRom.LoadAsync(ms, "test.gba");
        var project = AgbSynthProjectExporter.CreateFromRom(rom, 0x100, "0x100");

        var midi = Mp2kSequenceMidiConverter.BuildMidi(rom, project.SongHeaders.Single());
        byte[] bytes = AgbSynth.App.MIDI.MidiFileWriter.Build(midi, Mp2kSequenceMidiConverter.TicksPerQuarter);

        AssertContainsMidiEvent(bytes, 0xB0, 10, 0x60);
        Assert.Equal(2, bytes.Count(b => b == 0x90));
        Assert.DoesNotContain(midi[1].Events, e => e.Data.Length >= 3 && (e.Data[0] & 0xF0) == 0x90 && e.Data[1] == 0x60);
    }

    [Fact]
    public async Task BuildMidi_ConsumesConditionalMemaccPointer()
    {
        byte[] romBytes = CreateRomWithSong(
            0xB9, 0x06, 0x00, 0x01, 0x20, 0x04, 0x00, 0x08,
            0xD1, 0x30, 0x70,
            0xB1);

        using var ms = new MemoryStream(romBytes);
        var rom = await GbaRom.LoadAsync(ms, "test.gba");
        var project = AgbSynthProjectExporter.CreateFromRom(rom, 0x100, "0x100");

        var midi = Mp2kSequenceMidiConverter.BuildMidi(rom, project.SongHeaders.Single());
        byte[] bytes = AgbSynth.App.MIDI.MidiFileWriter.Build(midi, Mp2kSequenceMidiConverter.TicksPerQuarter);

        Assert.Equal(1, bytes.Count(b => b == 0x90));
        Assert.Contains((byte)0x30, bytes);
    }

    [Fact]
    public async Task BuildMidi_ConsumesEachExtendedCommandArgumentLength()
    {
        byte[] romBytes = CreateRomWithSong(
            0xCD, 0x04, 0x11,
            0xCD, 0x05, 0x22,
            0xCD, 0x01, 0x78, 0x56, 0x34, 0x12,
            0xD1, 0x3C, 0x70,
            0xB1);

        using var ms = new MemoryStream(romBytes);
        var rom = await GbaRom.LoadAsync(ms, "test.gba");
        var project = AgbSynthProjectExporter.CreateFromRom(rom, 0x100, "0x100");

        var midi = Mp2kSequenceMidiConverter.BuildMidi(rom, project.SongHeaders.Single());
        byte[] bytes = AgbSynth.App.MIDI.MidiFileWriter.Build(midi, Mp2kSequenceMidiConverter.TicksPerQuarter);
        string text = System.Text.Encoding.ASCII.GetString(bytes);

        Assert.Equal(1, bytes.Count(b => b == 0x90));
        Assert.Contains("xATTA=0x11", text);
        Assert.Contains("xDECA=0x22", text);
        Assert.Contains("xWAVE=0x12345678", text);
        AssertContainsMidiEvent(bytes, 0xB0, 85, 0x11);
        AssertContainsMidiEvent(bytes, 0xB0, 86, 0x22);
    }

    [Fact]
    public async Task BuildMidi_UsesCustomExtendedCommandControllers()
    {
        byte[] romBytes = CreateRomWithSong(
            0xCD, 0x04, 0x35,
            0xD1, 0x3C, 0x70,
            0xB1);

        using var ms = new MemoryStream(romBytes);
        var rom = await GbaRom.LoadAsync(ms, "test.gba");
        var project = AgbSynthProjectExporter.CreateFromRom(rom, 0x100, "0x100");
        var mapping = MidiCcMapping.Default;
        mapping.Attack = 81;

        var midi = Mp2kSequenceMidiConverter.BuildMidi(rom, project.SongHeaders.Single(), mapping);
        byte[] bytes = AgbSynth.App.MIDI.MidiFileWriter.Build(midi, Mp2kSequenceMidiConverter.TicksPerQuarter);

        AssertContainsMidiEvent(bytes, 0xB0, 81, 0x35);
        Assert.DoesNotContain(midi[1].Events, e =>
            e.Data.Length >= 3 && e.Data[0] == 0xB0 && e.Data[1] == 85);
    }

    [Fact]
    public async Task BuildMidi_UsesCustomStandardControllers()
    {
        byte[] romBytes = CreateRomWithSong(
            0xBE, 0x64,
            0xD1, 0x3C, 0x70,
            0xB1);

        using var ms = new MemoryStream(romBytes);
        var rom = await GbaRom.LoadAsync(ms, "test.gba");
        var project = AgbSynthProjectExporter.CreateFromRom(rom, 0x100, "0x100");
        var mapping = MidiCcMapping.Default;
        mapping.Volume = 74;

        var midi = Mp2kSequenceMidiConverter.BuildMidi(rom, project.SongHeaders.Single(), mapping);
        byte[] bytes = AgbSynth.App.MIDI.MidiFileWriter.Build(midi, Mp2kSequenceMidiConverter.TicksPerQuarter);

        AssertContainsMidiEvent(bytes, 0xB0, 74, 100);
        Assert.DoesNotContain(midi[1].Events, e =>
            e.Data.Length >= 3 && e.Data[0] == 0xB0 && e.Data[1] == 7);
    }

    [Fact]
    public async Task BuildMidi_AppliesExtendedWaitToFollowingEvents()
    {
        byte[] romBytes = CreateRomWithSong(
            0xCD, 0x0C, 0x05, 0x00,
            0xD1, 0x3C, 0x70,
            0xB1);

        using var ms = new MemoryStream(romBytes);
        var rom = await GbaRom.LoadAsync(ms, "test.gba");
        var project = AgbSynthProjectExporter.CreateFromRom(rom, 0x100, "0x100");

        var midi = Mp2kSequenceMidiConverter.BuildMidi(rom, project.SongHeaders.Single());

        Assert.Contains(midi[1].Events, e =>
            e.Tick == 5 && e.Data.Length >= 3 && (e.Data[0] & 0xF0) == 0x90 && e.Data[1] == 0x3C);
    }

    [Fact]
    public async Task BuildMidi_ExpandsRepeatPattern()
    {
        byte[] romBytes = CreateRomWithSong(
            0xB5, 0x03, 0x20, 0x04, 0x00, 0x08,
            0xB1);
        romBytes[0x420] = 0xD1;
        romBytes[0x421] = 0x3C;
        romBytes[0x422] = 0x70;
        romBytes[0x423] = 0x84;
        romBytes[0x424] = 0xB4;

        using var ms = new MemoryStream(romBytes);
        var rom = await GbaRom.LoadAsync(ms, "test.gba");
        var project = AgbSynthProjectExporter.CreateFromRom(rom, 0x100, "0x100");

        var midi = Mp2kSequenceMidiConverter.BuildMidi(rom, project.SongHeaders.Single());
        byte[] bytes = AgbSynth.App.MIDI.MidiFileWriter.Build(midi, Mp2kSequenceMidiConverter.TicksPerQuarter);

        Assert.Equal(3, bytes.Count(b => b == 0x90));
    }

    [Fact]
    public async Task BuildMidi_ReturnsFromRepeatPatternEndingWithFine()
    {
        byte[] romBytes = CreateRomWithSong(
            0xB5, 0x02, 0x20, 0x04, 0x00, 0x08,
            0xD1, 0x40, 0x70,
            0xB1);
        romBytes[0x420] = 0xD1;
        romBytes[0x421] = 0x3C;
        romBytes[0x422] = 0x70;
        romBytes[0x423] = 0x84;
        romBytes[0x424] = 0xB1;

        using var ms = new MemoryStream(romBytes);
        var rom = await GbaRom.LoadAsync(ms, "test.gba");
        var project = AgbSynthProjectExporter.CreateFromRom(rom, 0x100, "0x100");

        var midi = Mp2kSequenceMidiConverter.BuildMidi(rom, project.SongHeaders.Single());
        byte[] bytes = AgbSynth.App.MIDI.MidiFileWriter.Build(midi, Mp2kSequenceMidiConverter.TicksPerQuarter);

        Assert.Equal(3, bytes.Count(b => b == 0x90));
        Assert.Contains((byte)0x40, bytes);
    }

    [Fact]
    public async Task BuildMidi_ExportsLoopAsControllersWithoutLoopTextMarker()
    {
        byte[] romBytes = CreateRomWithSong(
            0xD1, 0x3C, 0x70,
            0x84,
            0xB2, 0x00, 0x04, 0x00, 0x08);

        using var ms = new MemoryStream(romBytes);
        var rom = await GbaRom.LoadAsync(ms, "test.gba");
        var project = AgbSynthProjectExporter.CreateFromRom(rom, 0x100, "0x100");

        var midi = Mp2kSequenceMidiConverter.BuildMidi(rom, project.SongHeaders.Single());
        byte[] bytes = AgbSynth.App.MIDI.MidiFileWriter.Build(midi, Mp2kSequenceMidiConverter.TicksPerQuarter);
        string text = System.Text.Encoding.ASCII.GetString(bytes);

        Assert.Equal(1, bytes.Count(b => b == 0x90));
        Assert.Contains((byte)89, bytes);
        Assert.Contains((byte)90, bytes);
        Assert.DoesNotContain("LOOP_START", text);
        Assert.DoesNotContain("LOOP_END", text);
    }

    [Fact]
    public async Task BuildMidi_FollowsForwardGotoInsteadOfEndingTrack()
    {
        byte[] romBytes = CreateRomWithSong(
            0xB2, 0x20, 0x04, 0x00, 0x08);
        romBytes[0x420] = 0xD1;
        romBytes[0x421] = 0x40;
        romBytes[0x422] = 0x70;
        romBytes[0x423] = 0xB1;

        using var ms = new MemoryStream(romBytes);
        var rom = await GbaRom.LoadAsync(ms, "test.gba");
        var project = AgbSynthProjectExporter.CreateFromRom(rom, 0x100, "0x100");

        var midi = Mp2kSequenceMidiConverter.BuildMidi(rom, project.SongHeaders.Single());
        byte[] bytes = AgbSynth.App.MIDI.MidiFileWriter.Build(midi, Mp2kSequenceMidiConverter.TicksPerQuarter);

        Assert.Contains((byte)0x40, bytes);
        Assert.DoesNotContain((byte)90, bytes);
    }

    [Fact]
    public async Task BuildMidi_IgnoresTopLevelPendInsteadOfEndingTrack()
    {
        byte[] romBytes = CreateRomWithSong(
            0xD1, 0x3C, 0x70,
            0x84,
            0xB4,
            0xD1, 0x40, 0x70,
            0xB1);

        using var ms = new MemoryStream(romBytes);
        var rom = await GbaRom.LoadAsync(ms, "test.gba");
        var project = AgbSynthProjectExporter.CreateFromRom(rom, 0x100, "0x100");

        var midi = Mp2kSequenceMidiConverter.BuildMidi(rom, project.SongHeaders.Single());
        byte[] bytes = AgbSynth.App.MIDI.MidiFileWriter.Build(midi, Mp2kSequenceMidiConverter.TicksPerQuarter);

        Assert.Contains((byte)0x3C, bytes);
        Assert.Contains((byte)0x40, bytes);
    }

    [Fact]
    public async Task BuildMidi_UsesAgbSynthControllerMapping()
    {
        byte[] romBytes = CreateRomWithSong(
            0xBA, 0x05,
            0xC1, 0x0C,
            0xC2, 0x20,
            0xC3, 0x30,
            0xC5, 0x02,
            0xC8, 0x40,
            0xB2, 0x00, 0x04, 0x00, 0x08);

        using var ms = new MemoryStream(romBytes);
        var rom = await GbaRom.LoadAsync(ms, "test.gba");
        var project = AgbSynthProjectExporter.CreateFromRom(rom, 0x100, "0x100");

        var midi = Mp2kSequenceMidiConverter.BuildMidi(rom, project.SongHeaders.Single());
        byte[] bytes = AgbSynth.App.MIDI.MidiFileWriter.Build(midi, Mp2kSequenceMidiConverter.TicksPerQuarter);

        Assert.Contains((byte)13, bytes);
        Assert.Contains((byte)14, bytes);
        Assert.Contains((byte)20, bytes);
        Assert.Contains((byte)21, bytes);
        Assert.Contains((byte)22, bytes);
        Assert.Contains((byte)26, bytes);
        Assert.Contains((byte)89, bytes);
        Assert.Contains((byte)90, bytes);
    }

    [Fact]
    public async Task BuildMidi_ExportsPitchBendWithMp2kCenterAt64()
    {
        byte[] romBytes = CreateRomWithSong(
            0xC0, 0x40,
            0xC0, 0x00,
            0xC0, 0x7F,
            0xB1);

        using var ms = new MemoryStream(romBytes);
        var rom = await GbaRom.LoadAsync(ms, "test.gba");
        var project = AgbSynthProjectExporter.CreateFromRom(rom, 0x100, "0x100");

        var midi = Mp2kSequenceMidiConverter.BuildMidi(rom, project.SongHeaders.Single());
        byte[] bytes = AgbSynth.App.MIDI.MidiFileWriter.Build(midi, Mp2kSequenceMidiConverter.TicksPerQuarter);

        AssertContainsMidiEvent(bytes, 0xE0, 0x00, 0x40);
        AssertContainsMidiEvent(bytes, 0xE0, 0x00, 0x00);
        AssertContainsMidiEvent(bytes, 0xE0, 0x7F, 0x7F);
    }

    [Fact]
    public async Task BuildMidi_ExportsFullEightBitBendRange()
    {
        byte[] romBytes = CreateRomWithSong(
            0xC1, 0xFF,
            0xB1);

        using var ms = new MemoryStream(romBytes);
        var rom = await GbaRom.LoadAsync(ms, "test.gba");
        var project = AgbSynthProjectExporter.CreateFromRom(rom, 0x100, "0x100");

        var midi = Mp2kSequenceMidiConverter.BuildMidi(rom, project.SongHeaders.Single());
        byte[] bytes = AgbSynth.App.MIDI.MidiFileWriter.Build(midi, Mp2kSequenceMidiConverter.TicksPerQuarter);

        AssertContainsMidiEvent(bytes, 0xB0, 20, 0x7F);
        AssertContainsMidiEvent(bytes, 0xB0, 52, 0x01);
    }

    [Fact]
    public async Task BuildMidi_DoesNotRemapTenthTrackToChannel16()
    {
        byte[][] tracks = Enumerable.Range(0, 10)
            .Select(index => index == 9
                ? new byte[] { 0xBD, 0x01, 0xD1, 0x3C, 0x40, 0xB1 }
                : [0xB1])
            .ToArray();
        byte[] romBytes = CreateRomWithTracks(tracks);

        using var ms = new MemoryStream(romBytes);
        var rom = await GbaRom.LoadAsync(ms, "test.gba");
        var project = AgbSynthProjectExporter.CreateFromRom(rom, 0x100, "0x100");

        var midi = Mp2kSequenceMidiConverter.BuildMidi(rom, project.SongHeaders.Single());
        var tenthTrackEvents = midi[10].Events.Select(e => e.Data).ToList();

        Assert.Contains(tenthTrackEvents, data => data.Length >= 1 && data[0] == 0xC9);
        Assert.Contains(tenthTrackEvents, data => data.Length >= 1 && data[0] == 0x99);
        Assert.DoesNotContain(tenthTrackEvents, data => data.Length >= 1 && data[0] == 0xCF);
        Assert.DoesNotContain(tenthTrackEvents, data => data.Length >= 1 && data[0] == 0x9F);
    }

    [Fact]
    public async Task ExportMidiFiles_CreatesMidiFolderAndStoresRelativePath()
    {
        byte[] romBytes = CreateRomWithSong(0xB1);

        using var ms = new MemoryStream(romBytes);
        var rom = await GbaRom.LoadAsync(ms, "test.gba");
        var project = AgbSynthProjectExporter.CreateFromRom(rom, 0x100, "0x100");

        string directory = Path.Combine(Path.GetTempPath(), $"AgbSynthTests_{Guid.NewGuid():N}");
        string projectPath = Path.Combine(directory, "test.agbsynth");
        try
        {
            int exported = AgbSynthProjectMidiExporter.ExportMidiFiles(rom, project, projectPath);

            Assert.Equal(1, exported);
            Assert.Equal("test_data/midi/song_000.mid", project.SongHeaders.Single().MidiFilePath);
            Assert.True(File.Exists(Path.Combine(directory, "test_data", "midi", "song_000.mid")));
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    private static byte[] CreateRomWithSong(params byte[] trackBytes)
    {
        byte[] romBytes = new byte[0x500];
        WriteAscii(romBytes, 0xA0, "TEST ROM");
        WriteAscii(romBytes, 0xAC, "TEST");
        WriteU32(romBytes, 0x100, 0x08000200);
        WriteSongHeader(romBytes, 0x200, 0x08000300, 0x08000400);
        Array.Copy(trackBytes, 0, romBytes, 0x400, trackBytes.Length);
        return romBytes;
    }

    private static byte[] CreateRomWithTracks(params byte[][] tracks)
    {
        byte[] romBytes = new byte[0x1000];
        WriteAscii(romBytes, 0xA0, "TEST ROM");
        WriteAscii(romBytes, 0xAC, "TEST");
        WriteU32(romBytes, 0x100, 0x08000200);

        romBytes[0x200 + 0] = checked((byte)tracks.Length);
        romBytes[0x200 + 1] = 0;
        romBytes[0x200 + 2] = 10;
        romBytes[0x200 + 3] = 20;
        WriteU32(romBytes, 0x200 + 4, 0x08000300);

        for (int i = 0; i < tracks.Length; i++)
        {
            int trackOffset = 0x400 + i * 0x20;
            WriteU32(romBytes, 0x200 + 8 + i * 4, (uint)(0x08000000 + trackOffset));
            Array.Copy(tracks[i], 0, romBytes, trackOffset, tracks[i].Length);
        }

        return romBytes;
    }

    private static void WriteSongHeader(byte[] bytes, int offset, uint voiceGroupPointer, uint trackPointer)
    {
        bytes[offset + 0] = 1;
        bytes[offset + 1] = 0;
        bytes[offset + 2] = 10;
        bytes[offset + 3] = 20;
        WriteU32(bytes, offset + 4, voiceGroupPointer);
        WriteU32(bytes, offset + 8, trackPointer);
    }

    private static void WriteU32(byte[] bytes, int offset, uint value)
    {
        BitConverter.TryWriteBytes(bytes.AsSpan(offset, 4), value);
    }

    private static void WriteAscii(byte[] bytes, int offset, string text)
    {
        for (int i = 0; i < text.Length; i++)
            bytes[offset + i] = (byte)text[i];
    }

    private static void AssertContainsMidiEvent(byte[] bytes, byte status, byte data1, byte data2)
    {
        for (int i = 0; i <= bytes.Length - 3; i++)
        {
            if (bytes[i] == status && bytes[i + 1] == data1 && bytes[i + 2] == data2)
                return;
        }

        Assert.Fail($"Expected MIDI event {status:X2} {data1:X2} {data2:X2}.");
    }

}

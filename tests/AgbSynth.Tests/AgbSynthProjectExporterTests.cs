using AgbSynth.App.GBA;
using AgbSynth.App.Project;
using Xunit;

namespace AgbSynth.Tests;

public sealed class AgbSynthProjectExporterTests
{
    [Fact]
    public void CreateBlankProject_WritesStandaloneEmptyProjectLayout()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"AgbSynthBlankProjectTests_{Guid.NewGuid():N}");
        string projectPath = Path.Combine(directory, "blank.agbsynth");

        try
        {
            var project = AgbSynthProjectExporter.CreateBlankProject(projectPath);
            var loaded = AgbSynthProjectLoader.Load(projectPath);

            Assert.Equal("NewProject", project.Import.ReadMode);
            Assert.Equal("blank_data/songtable/songtable.agbst", project.SongTable.FilePath);
            Assert.Equal(8, project.SongTable.EntrySize);
            Assert.Empty(project.Songs);
            Assert.Empty(loaded.Songs);
            Assert.Empty(loaded.SongHeaders);
            Assert.Empty(loaded.VoiceGroups);
            Assert.Equal(string.Empty, loaded.Rom.GameCode);
            Assert.True(File.Exists(projectPath));
            Assert.True(File.Exists(Path.Combine(directory, "blank_data", "songtable", "songtable.agbst")));
            foreach (string category in new[] { "songheader", "midi", "voicegroup", "keysplit", "drumset", "wavedata", "wavememory" })
                Assert.True(Directory.Exists(Path.Combine(directory, "blank_data", category)));
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void AssetWriter_CustomNamedFilesReloadAndLinkAcrossPages()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"AgbSynthCustomAssetTests_{Guid.NewGuid():N}");
        string projectPath = Path.Combine(directory, "blank.agbsynth");

        try
        {
            AgbSynthProjectExporter.CreateBlankProject(projectPath);
            string dataRoot = Path.Combine(directory, "blank_data");
            string keySplitPath = Path.Combine(dataRoot, "keysplit", "Bass Range.agbks");
            string drumSetPath = Path.Combine(dataRoot, "drumset", "Custom Drums.agbds");
            string voiceGroupPath = Path.Combine(dataRoot, "voicegroup", "My Instruments.agbvg");
            string songHeaderPath = Path.Combine(dataRoot, "songheader", "Opening Theme.agbsh");
            var keySplit = new KeySplitProjectInfo
            {
                Label = "Bass Range",
                Regions = new List<VoiceProjectInfo> { new() { Index = 0, Type = 1, TypeName = "Square 1" } },
                KeyMapHex = new string('0', 256)
            };
            var drumSet = new DrumSetProjectInfo
            {
                Label = "Custom Drums",
                Entries = Enumerable.Range(0, 128).Select(index => new VoiceProjectInfo { Index = index, Type = 1, TypeName = "Square 1" }).ToList()
            };
            var voiceGroup = new VoiceGroupProjectInfo
            {
                Id = 0,
                Label = "My Instruments",
                FilePath = "blank_data/voicegroup/My Instruments.agbvg",
                DiscoverySource = "User",
                Voices = new List<VoiceProjectInfo>
                {
                    new()
                    {
                        Index = 0,
                        Type = 0x40,
                        TypeName = "Key Split",
                        DataFilePath = "blank_data/keysplit/Bass Range.agbks"
                    }
                }
            };

            AgbSynthProjectAssetWriter.SaveKeySplit(keySplitPath, new KeySplitAssetProjectInfo
            {
                Id = 0,
                Label = keySplit.Label,
                FilePath = "blank_data/keysplit/Bass Range.agbks",
                VoiceGroupId = -1,
                ParentVoiceIndex = -1,
                KeySplit = keySplit
            });
            AgbSynthProjectAssetWriter.SaveDrumSet(drumSetPath, new DrumSetAssetProjectInfo
            {
                Id = 0,
                Label = drumSet.Label,
                FilePath = "blank_data/drumset/Custom Drums.agbds",
                VoiceGroupId = -1,
                ParentVoiceIndex = -1,
                DrumSet = drumSet
            });
            AgbSynthProjectAssetWriter.SaveVoiceGroup(voiceGroupPath, voiceGroup);
            AgbSynthProjectAssetWriter.SaveSongHeader(songHeaderPath, new SongHeaderProjectInfo
            {
                SongId = 0,
                Label = "Opening Theme",
                FilePath = "blank_data/songheader/Opening Theme.agbsh",
                VoiceGroupFilePath = voiceGroup.FilePath
            });

            var loaded = AgbSynthProjectLoader.Load(projectPath);

            Assert.Equal("Opening Theme", Assert.Single(loaded.SongHeaders).Label);
            Assert.Equal("My Instruments", Assert.Single(loaded.VoiceGroups).Label);
            Assert.Equal("Bass Range", Assert.Single(loaded.KeySplits).Label);
            Assert.Equal("Custom Drums", Assert.Single(loaded.DrumSets).Label);
            Assert.Same(loaded.KeySplits[0].KeySplit, loaded.VoiceGroups[0].Voices[0].KeySplit);
            Assert.Equal(loaded.VoiceGroups[0].FilePath, loaded.SongHeaders[0].VoiceGroupFilePath);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task CreateFromRom_StoresRomAndSongTableMetadata()
    {
        byte[] romBytes = new byte[0x2400];
        WriteAscii(romBytes, 0xA0, "TEST ROM");
        WriteAscii(romBytes, 0xAC, "TEST");
        WriteU32(romBytes, 0x100, 0x08000200);
        WriteSongHeader(romBytes, 0x200, trackCount: 1, voiceGroupPointer: 0x08000300, trackPointer: 0x08000400);
        WriteVoice(romBytes, 0x300, 0, type: 0x80, key: 0, dataPointer: 0x08000C00);
        WriteKeySplitVoice(romBytes, 0x300, 1, regionPointer: 0x08001800, keyMapPointer: 0x08001A00);
        WriteVoice(romBytes, 0x300, 5, type: 0x08, key: 60, dataPointer: 0x08000A00);
        WriteVoice(romBytes, 0x300, 6, type: 0x03, key: 60, dataPointer: 0x08001C00);
        WriteSampleHeader(romBytes, 0xA00, loopFlags: 0x40, frequency: 0x00344300, loopStart: 12, size: 32);
        WriteWaveRam(romBytes, 0x1C00);
        WriteVoice(romBytes, 0xC00, 60, type: 0x01, key: 60, dataPointer: 0x00000002);
        WriteVoice(romBytes, 0x1800, 0, type: 0x01, key: 48, dataPointer: 0x00000002);
        for (int i = 0; i < 128; i++)
            romBytes[0x1A00 + i] = 0;
        WriteSoundModeReference(romBytes, songTableOffset: 0x100, referenceOffset: 0x1F00, playerTableOffset: 0x2000, signatureOffset: 0x2100, rawValue: 0x0094C500);

        using var ms = new MemoryStream(romBytes);
        var rom = await GbaRom.LoadAsync(ms, "test.gba");

        var project = AgbSynthProjectExporter.CreateFromRom(rom, 0x100, "0x100");

        Assert.Equal("AgbSynthProject", project.Format);
        Assert.Equal("MP2K", project.Engine);
        Assert.Equal("TEST", project.Rom.GameCode);
        Assert.True(project.SoundMode.Detected);
        Assert.Equal(5, project.SoundMode.MaxChannels);
        Assert.Equal(4, project.SoundMode.FrequencyIndex);
        Assert.Equal(13379, project.SoundMode.FixedSampleRate);
        Assert.Equal(0x100, project.SongTable.Offset);
        Assert.Contains(project.Songs, s => s.SongId == 0 && s.HeaderPointer == "0x08000200");
        Assert.Contains(project.SongHeaders, h =>
            h.SongId == 0 &&
            h.TrackCount == 1 &&
            h.VoiceGroupPointer == "0x08000300" &&
            h.TrackPointers.Contains("0x08000400"));
        Assert.Contains(project.VoiceGroups, g =>
            g.Pointer == "0x08000300" &&
            g.Voices.Any(v => v.Index == 5 && v.TypeName == "DirectSound Fixed" && v.Sample is { Loops: true, Size: 32 }));
        Assert.Contains(project.VoiceGroups.Single().Voices, v => v.Index == 6 && v.PsgWaveMemory is { DataOffset: 0x1C00 });
        var squareDrumEntry = project.VoiceGroups.Single().Voices[0].DrumSet!.Entries[60];
        Assert.Equal("Square 1", squareDrumEntry.TypeName);
        Assert.NotNull(squareDrumEntry.PsgSquare);
        Assert.Equal(2, squareDrumEntry.PsgSquare!.DutyIndex);
        Assert.Equal(0.5, squareDrumEntry.PsgSquare.DutyRatio);
    }

    [Fact]
    public async Task ExportVoiceGroups_WritesIndependentVoiceGroupFiles()
    {
        byte[] romBytes = new byte[0x2400];
        WriteAscii(romBytes, 0xA0, "TEST ROM");
        WriteAscii(romBytes, 0xAC, "TEST");
        WriteU32(romBytes, 0x100, 0x08000200);
        WriteSongHeader(romBytes, 0x200, trackCount: 1, voiceGroupPointer: 0x08000300, trackPointer: 0x08000400);
        WriteVoice(romBytes, 0x300, 0, type: 0x80, key: 0, dataPointer: 0x08000C00);
        WriteKeySplitVoice(romBytes, 0x300, 1, regionPointer: 0x08001800, keyMapPointer: 0x08001A00);
        WriteVoice(romBytes, 0x300, 5, type: 0x08, key: 60, dataPointer: 0x08000A00);
        WriteVoice(romBytes, 0x300, 6, type: 0x03, key: 60, dataPointer: 0x08001C00);
        WriteSampleHeader(romBytes, 0xA00, loopFlags: 0x40, frequency: 0x00344300, loopStart: 12, size: 32);
        WriteWaveRam(romBytes, 0x1C00);
        WriteVoice(romBytes, 0xC00, 60, type: 0x01, key: 60, dataPointer: 0x00000002);
        WriteVoice(romBytes, 0x1800, 0, type: 0x01, key: 48, dataPointer: 0x00000002);
        for (int i = 0; i < 128; i++)
            romBytes[0x1A00 + i] = 0;

        using var ms = new MemoryStream(romBytes);
        var rom = await GbaRom.LoadAsync(ms, "test.gba");
        var project = AgbSynthProjectExporter.CreateFromRom(rom, 0x100, "0x100");
        string directory = Path.Combine(Path.GetTempPath(), $"AgbSynthVoiceGroupTests_{Guid.NewGuid():N}");
        string projectPath = Path.Combine(directory, "test.agbsynth");

        try
        {
            int exported = AgbSynthProjectVoiceGroupExporter.ExportVoiceGroups(rom, project, projectPath);

            Assert.Equal(5, exported);
            Assert.Equal("test_data/voicegroup/voicegroup_000.agbvg", project.VoiceGroups.Single().FilePath);
            Assert.Equal("test_data/drumset/drumset_000.agbds", project.VoiceGroups.Single().Voices[0].DataFilePath);
            Assert.Equal("test_data/keysplit/keysplit_000.agbks", project.VoiceGroups.Single().Voices[1].DataFilePath);
            Assert.Equal("test_data/wavedata/wavedata_000.agbwd", project.VoiceGroups.Single().Voices[5].Sample!.FilePath);
            Assert.Equal("test_data/wavememory/wavememory_000.agbwm", project.VoiceGroups.Single().Voices[6].PsgWaveMemory!.FilePath);
            Assert.Equal(0, project.SongHeaders.Single().VoiceGroupId);
            Assert.Equal("test_data/voicegroup/voicegroup_000.agbvg", project.SongHeaders.Single().VoiceGroupFilePath);
            Assert.Single(project.WaveData);
            Assert.Single(project.WaveMemory);
            Assert.Equal("test_data/wavedata/wavedata_000.agbwd", project.WaveData.Single().FilePath);
            Assert.Equal("test_data/wavememory/wavememory_000.agbwm", project.WaveMemory.Single().FilePath);
            string voiceGroupPath = Path.Combine(directory, "test_data", "voicegroup", "voicegroup_000.agbvg");
            string drumSetPath = Path.Combine(directory, "test_data", "drumset", "drumset_000.agbds");
            string keySplitPath = Path.Combine(directory, "test_data", "keysplit", "keysplit_000.agbks");
            string waveDataPath = Path.Combine(directory, "test_data", "wavedata", "wavedata_000.agbwd");
            string waveMemoryPath = Path.Combine(directory, "test_data", "wavememory", "wavememory_000.agbwm");
            Assert.True(File.Exists(voiceGroupPath));
            Assert.True(File.Exists(drumSetPath));
            Assert.True(File.Exists(keySplitPath));
            Assert.True(File.Exists(waveDataPath));
            Assert.True(File.Exists(waveMemoryPath));
            Assert.Equal(16, new FileInfo(waveMemoryPath).Length);
            string text = File.ReadAllText(voiceGroupPath);
            Assert.Contains("AgbSynthVoiceGroup", text);
            Assert.Contains("DirectSound", text);
            Assert.Contains("drumset_000.agbds", text);
            Assert.Contains("keysplit_000.agbks", text);
            Assert.Contains("wavedata_000.agbwd", text);
            Assert.Contains("wavememory_000.agbwm", text);
            Assert.DoesNotContain("\"Pointer\"", text);
            Assert.DoesNotContain("\"Offset\"", text);
            Assert.DoesNotContain("\"DataPointer\"", text);
            Assert.DoesNotContain("\"DataOffset\"", text);

            string drumSetText = File.ReadAllText(drumSetPath);
            string keySplitText = File.ReadAllText(keySplitPath);
            string waveDataText = File.ReadAllText(waveDataPath);
            Assert.DoesNotContain("\"VoiceGroupPointer\"", drumSetText);
            Assert.DoesNotContain("\"SourcePointer\"", drumSetText);
            Assert.DoesNotContain("\"SourceOffset\"", drumSetText);
            Assert.DoesNotContain("\"VoiceGroupPointer\"", keySplitText);
            Assert.DoesNotContain("\"SourcePointer\"", keySplitText);
            Assert.DoesNotContain("\"SourceOffset\"", keySplitText);
            Assert.DoesNotContain("\"SourceOffset\"", waveDataText);
            Assert.DoesNotContain("\"HeaderOffset\"", waveDataText);
            Assert.DoesNotContain("\"DataOffset\"", waveDataText);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ExportVoiceGroups_DeduplicatesSharedVoiceAssetsAndWaveData()
    {
        byte[] romBytes = new byte[0x4000];
        WriteAscii(romBytes, 0xA0, "TEST ROM");
        WriteAscii(romBytes, 0xAC, "TEST");
        WriteU32(romBytes, 0x100, 0x08000200);
        WriteU32(romBytes, 0x108, 0x08000220);
        WriteSongHeader(romBytes, 0x200, trackCount: 1, voiceGroupPointer: 0x08000300, trackPointer: 0x08000400);
        WriteSongHeader(romBytes, 0x220, trackCount: 1, voiceGroupPointer: 0x08001200, trackPointer: 0x08000410);
        WriteVoice(romBytes, 0x300, 0, type: 0x80, key: 0, dataPointer: 0x08002600);
        WriteKeySplitVoice(romBytes, 0x300, 1, regionPointer: 0x08003200, keyMapPointer: 0x08003800);
        WriteVoice(romBytes, 0x300, 5, type: 0x08, key: 60, dataPointer: 0x08002200);
        WriteVoice(romBytes, 0x1200, 0, type: 0x80, key: 0, dataPointer: 0x08002600);
        WriteKeySplitVoice(romBytes, 0x1200, 1, regionPointer: 0x08003200, keyMapPointer: 0x08003800);
        WriteVoice(romBytes, 0x1200, 5, type: 0x08, key: 60, dataPointer: 0x08002200);
        WriteSampleHeader(romBytes, 0x2200, loopFlags: 0x40, frequency: 0x00344300, loopStart: 0, size: 16);
        WriteVoice(romBytes, 0x2600, 60, type: 0x08, key: 60, dataPointer: 0x08002200);
        WriteVoice(romBytes, 0x3200, 0, type: 0x08, key: 48, dataPointer: 0x08002200);
        for (int i = 0; i < 128; i++)
            romBytes[0x3800 + i] = 0;

        using var ms = new MemoryStream(romBytes);
        var rom = await GbaRom.LoadAsync(ms, "test.gba");
        var project = AgbSynthProjectExporter.CreateFromRom(rom, 0x100, "0x100");
        string directory = Path.Combine(Path.GetTempPath(), $"AgbSynthVoiceGroupTests_{Guid.NewGuid():N}");
        string projectPath = Path.Combine(directory, "test.agbsynth");

        try
        {
            int exported = AgbSynthProjectVoiceGroupExporter.ExportVoiceGroups(rom, project, projectPath);

            Assert.Equal(2, project.VoiceGroups.Count);
            Assert.Equal(5, exported);
            Assert.Equal("test_data/drumset/drumset_000.agbds", project.VoiceGroups[0].Voices[0].DataFilePath);
            Assert.Equal(project.VoiceGroups[0].Voices[0].DataFilePath, project.VoiceGroups[1].Voices[0].DataFilePath);
            Assert.Equal("test_data/keysplit/keysplit_000.agbks", project.VoiceGroups[0].Voices[1].DataFilePath);
            Assert.Equal(project.VoiceGroups[0].Voices[1].DataFilePath, project.VoiceGroups[1].Voices[1].DataFilePath);
            Assert.Equal("test_data/wavedata/wavedata_000.agbwd", project.VoiceGroups[0].Voices[5].Sample!.FilePath);
            Assert.Equal(project.VoiceGroups[0].Voices[5].Sample!.FilePath, project.VoiceGroups[1].Voices[5].Sample!.FilePath);
            Assert.Single(project.WaveData);
            Assert.Single(Directory.GetFiles(Path.Combine(directory, "test_data", "drumset"), "*.agbds"));
            Assert.Single(Directory.GetFiles(Path.Combine(directory, "test_data", "keysplit"), "*.agbks"));
            Assert.Single(Directory.GetFiles(Path.Combine(directory, "test_data", "wavedata"), "*.agbwd"));
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task CreateFromRom_AutomaticallyFindsSongTableAndOptionalUnreferencedVoiceGroup()
    {
        byte[] romBytes = new byte[0x3000];
        WriteAscii(romBytes, 0xA0, "TEST ROM");
        WriteAscii(romBytes, 0xAC, "TEST");
        for (int songId = 0; songId < 4; songId++)
        {
            int headerOffset = 0x200 + songId * 0x20;
            WriteU32(romBytes, 0x100 + songId * 8, 0x08000000u + (uint)headerOffset);
            WriteSongHeader(romBytes, headerOffset, trackCount: 1, voiceGroupPointer: 0x08000300, trackPointer: 0x08000800u + (uint)songId);
        }

        WriteVoice(romBytes, 0x1200, 0, type: 0x08, key: 60, dataPointer: 0x08002200);
        WriteSampleHeader(romBytes, 0x2200, loopFlags: 0, frequency: 0x00344300, loopStart: 0, size: 16);

        using var ms = new MemoryStream(romBytes);
        var rom = await GbaRom.LoadAsync(ms, "test.gba");
        var project = AgbSynthProjectExporter.CreateFromRom(
            rom,
            new Mp2kImportOptions
            {
                ReadMode = Mp2kRomReadMode.AutomaticDiscovery,
                IncludeUnreferencedVoiceGroups = true
            });

        Assert.Equal(0x100, project.SongTable.Offset);
        Assert.Equal(nameof(Mp2kRomReadMode.AutomaticDiscovery), project.Import.ReadMode);
        Assert.True(project.Import.IncludeUnreferencedVoiceGroups);
        Assert.Contains(project.VoiceGroups, g => g.Offset == 0x300 && g.DiscoverySource == "Referenced");
        Assert.Contains(project.VoiceGroups, g => g.Offset == 0x1200 && g.DiscoverySource == "Discovered");
    }

    [Fact]
    public async Task CreateFromRom_AutomaticDiscoveryPrefersReferencedSongTable()
    {
        byte[] romBytes = new byte[0x9000];
        WriteAscii(romBytes, 0xA0, "TEST ROM");
        WriteAscii(romBytes, 0xAC, "TEST");

        for (int songId = 0; songId < 4; songId++)
        {
            int headerOffset = 0x1000 + songId * 0x20;
            WriteU32(romBytes, 0x100 + songId * 8, 0x08000000u + (uint)headerOffset);
            WriteSongHeader(romBytes, headerOffset, trackCount: 1, voiceGroupPointer: 0x08003000, trackPointer: 0x08004000u + (uint)songId);
        }

        for (int songId = 0; songId < 20; songId++)
        {
            int headerOffset = 0x2000 + songId * 0x20;
            WriteU32(romBytes, 0x900 + songId * 8, 0x08000000u + (uint)headerOffset);
            WriteSongHeader(romBytes, headerOffset, trackCount: 1, voiceGroupPointer: 0x08003000, trackPointer: 0x08005000u + (uint)songId);
        }

        WriteSoundModeReference(romBytes, songTableOffset: 0x100, referenceOffset: 0x6000, playerTableOffset: 0x7000, signatureOffset: 0x8000, rawValue: 0x0094C500);

        using var ms = new MemoryStream(romBytes);
        var rom = await GbaRom.LoadAsync(ms, "test.gba");

        var project = AgbSynthProjectExporter.CreateFromRom(
            rom,
            new Mp2kImportOptions { ReadMode = Mp2kRomReadMode.AutomaticDiscovery });

        Assert.Equal(0x100, project.SongTable.Offset);
        Assert.Equal(4, project.Songs.Count);
    }

    [Fact]
    public async Task CreateFromRom_AutomaticDiscoveryUsesKnownEmeraldSongTable()
    {
        const int emeraldSongTableOffset = 0x6B49F0;
        byte[] romBytes = new byte[0x6C0000];
        WriteAscii(romBytes, 0xA0, "POKEMON EMER");
        WriteAscii(romBytes, 0xAC, "BPEE");

        for (int songId = 0; songId < 20; songId++)
        {
            int headerOffset = 0x2000 + songId * 0x20;
            WriteU32(romBytes, 0x100 + songId * 8, 0x08000000u + (uint)headerOffset);
            WriteSongHeader(romBytes, headerOffset, trackCount: 1, voiceGroupPointer: 0x08003000, trackPointer: 0x08005000u + (uint)songId);
        }

        for (int songId = 0; songId < 4; songId++)
        {
            int headerOffset = 0x6000 + songId * 0x20;
            WriteU32(romBytes, emeraldSongTableOffset + songId * 8, 0x08000000u + (uint)headerOffset);
            WriteSongHeader(romBytes, headerOffset, trackCount: 1, voiceGroupPointer: 0x08003000, trackPointer: 0x08007000u + (uint)songId);
        }

        using var ms = new MemoryStream(romBytes);
        var rom = await GbaRom.LoadAsync(ms, "test.gba");

        var project = AgbSynthProjectExporter.CreateFromRom(
            rom,
            new Mp2kImportOptions { ReadMode = Mp2kRomReadMode.AutomaticDiscovery });

        Assert.Equal(emeraldSongTableOffset, project.SongTable.Offset);
        Assert.Equal(4, project.Songs.Count);
    }

    [Fact]
    public async Task CreateFromRom_KeepsSongTableEntriesAfterEmptyGap()
    {
        byte[] romBytes = new byte[0x4000];
        WriteAscii(romBytes, 0xA0, "TEST ROM");
        WriteAscii(romBytes, 0xAC, "TEST");
        for (int songId = 0; songId < 4; songId++)
        {
            int headerOffset = 0x800 + songId * 0x20;
            WriteU32(romBytes, 0x100 + songId * 8, 0x08000000u + (uint)headerOffset);
            WriteSongHeader(romBytes, headerOffset, trackCount: 1, voiceGroupPointer: 0x08000300, trackPointer: 0x08002000u + (uint)songId);
        }

        const int laterSongId = 90;
        WriteU32(romBytes, 0x100 + laterSongId * 8, 0x08001000);
        WriteSongHeader(romBytes, 0x1000, trackCount: 1, voiceGroupPointer: 0x08000300, trackPointer: 0x08002100);

        using var ms = new MemoryStream(romBytes);
        var rom = await GbaRom.LoadAsync(ms, "test.gba");

        var manualProject = AgbSynthProjectExporter.CreateFromRom(rom, 0x100, "0x100");
        var autoProject = AgbSynthProjectExporter.CreateFromRom(
            rom,
            new Mp2kImportOptions { ReadMode = Mp2kRomReadMode.AutomaticDiscovery });

        Assert.Equal(laterSongId + 1, manualProject.Songs.Count);
        Assert.Equal(5, manualProject.SongHeaders.Count);
        Assert.Contains(manualProject.Songs, s => s.SongId == laterSongId && s.HeaderPointer == "0x08001000");
        Assert.Contains(manualProject.Songs, s => s.SongId == 4 && s.Note == "Empty SongTable slot");
        Assert.Equal(0x100, autoProject.SongTable.Offset);
        Assert.Equal(laterSongId + 1, autoProject.Songs.Count);
        Assert.Contains(autoProject.Songs, s => s.SongId == laterSongId && s.HeaderPointer == "0x08001000");
    }

    [Fact]
    public async Task ExportSongTableAndHeaders_WritesLinkedIndependentFiles()
    {
        byte[] romBytes = new byte[0x1200];
        WriteAscii(romBytes, 0xA0, "TEST ROM");
        WriteAscii(romBytes, 0xAC, "TEST");
        WriteU32(romBytes, 0x100, 0x08000200);
        WriteSongHeader(romBytes, 0x200, trackCount: 1, voiceGroupPointer: 0x08000300, trackPointer: 0x08000400);

        using var ms = new MemoryStream(romBytes);
        var rom = await GbaRom.LoadAsync(ms, "test.gba");
        var project = AgbSynthProjectExporter.CreateFromRom(rom, 0x100, "0x100");
        string directory = Path.Combine(Path.GetTempPath(), $"AgbSynthSequenceAssetTests_{Guid.NewGuid():N}");
        string projectPath = Path.Combine(directory, "test.agbsynth");

        try
        {
            int exported = AgbSynthProjectSequenceExporter.ExportSongTableAndHeaders(project, projectPath);

            Assert.Equal(2, exported);
            Assert.Equal("test_data/songtable/songtable.agbst", project.SongTable.FilePath);
            Assert.Equal("test_data/songheader/songheader_000.agbsh", project.SongHeaders.Single().FilePath);
            Assert.Equal("songheader_000", project.SongHeaders.Single().Label);
            Assert.Equal(project.SongHeaders.Single().FilePath, project.Songs.Single().SongHeaderFilePath);
            Assert.Equal("songheader_000", project.Songs.Single().Label);
            Assert.Equal("Exported from 0x08000200", project.Songs.Single().Note);
            Assert.True(File.Exists(Path.Combine(directory, "test_data", "songtable", "songtable.agbst")));
            Assert.True(File.Exists(Path.Combine(directory, "test_data", "songheader", "songheader_000.agbsh")));
            string songTableJson = File.ReadAllText(Path.Combine(directory, "test_data", "songtable", "songtable.agbst"));
            Assert.Contains("songheader_000.agbsh", songTableJson);
            Assert.Contains("Exported from 0x08000200", songTableJson);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Save_DoesNotInlineExtractedAssetListsInProjectFile()
    {
        byte[] romBytes = new byte[0x1200];
        WriteAscii(romBytes, 0xA0, "TEST ROM");
        WriteAscii(romBytes, 0xAC, "TEST");
        WriteU32(romBytes, 0x100, 0x08000200);
        WriteSongHeader(romBytes, 0x200, trackCount: 1, voiceGroupPointer: 0x08000300, trackPointer: 0x08000400);

        using var ms = new MemoryStream(romBytes);
        var rom = await GbaRom.LoadAsync(ms, "test.gba");
        var project = AgbSynthProjectExporter.CreateFromRom(rom, 0x100, "0x100");
        string directory = Path.Combine(Path.GetTempPath(), $"AgbSynthProjectFileTests_{Guid.NewGuid():N}");
        string projectPath = Path.Combine(directory, "test.agbsynth");

        try
        {
            AgbSynthProjectSequenceExporter.ExportSongTableAndHeaders(project, projectPath);
            AgbSynthProjectExporter.Save(projectPath, project);

            string projectJson = File.ReadAllText(projectPath);
            string songTableJson = File.ReadAllText(Path.Combine(directory, "test_data", "songtable", "songtable.agbst"));

            Assert.Contains("\"SongTable\"", projectJson);
            Assert.Contains("test_data/songtable/songtable.agbst", projectJson);
            Assert.DoesNotContain("\"Songs\"", projectJson);
            Assert.DoesNotContain("\"SongHeaders\"", projectJson);
            Assert.DoesNotContain("\"VoiceGroups\"", projectJson);
            Assert.DoesNotContain("\"WaveData\"", projectJson);
            Assert.DoesNotContain("\"WaveMemory\"", projectJson);
            Assert.DoesNotContain("\"SourcePath\"", projectJson);
            Assert.DoesNotContain("\"Address\"", projectJson);
            Assert.DoesNotContain("\"Offset\"", projectJson);
            Assert.DoesNotContain("\"RawValue\"", projectJson);
            Assert.Contains("\"Entries\"", songTableJson);
            Assert.Contains("songheader_000.agbsh", songTableJson);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Load_ReadsExtractedAssetsAndManuallyAddedVoiceGroupFiles()
    {
        byte[] romBytes = new byte[0x2400];
        WriteAscii(romBytes, 0xA0, "TEST ROM");
        WriteAscii(romBytes, 0xAC, "TEST");
        WriteU32(romBytes, 0x100, 0x08000200);
        WriteSongHeader(romBytes, 0x200, trackCount: 1, voiceGroupPointer: 0x08000300, trackPointer: 0x08000400);
        WriteVoice(romBytes, 0x300, 5, type: 0x08, key: 60, dataPointer: 0x08000A00);
        WriteSampleHeader(romBytes, 0xA00, loopFlags: 0x40, frequency: 0x00344300, loopStart: 12, size: 32);

        using var ms = new MemoryStream(romBytes);
        var rom = await GbaRom.LoadAsync(ms, "test.gba");
        var project = AgbSynthProjectExporter.CreateFromRom(rom, 0x100, "0x100");
        string directory = Path.Combine(Path.GetTempPath(), $"AgbSynthProjectLoadTests_{Guid.NewGuid():N}");
        string projectPath = Path.Combine(directory, "test.agbsynth");

        try
        {
            AgbSynthProjectVoiceGroupExporter.ExportVoiceGroups(rom, project, projectPath);
            AgbSynthProjectSequenceExporter.ExportSongTableAndHeaders(project, projectPath);
            AgbSynthProjectExporter.Save(projectPath, project);

            string manualVoiceGroupPath = Path.Combine(directory, "test_data", "voicegroup", "voicegroup_999.agbvg");
            File.WriteAllText(
                manualVoiceGroupPath,
                """
                {
                  "Format": "AgbSynthVoiceGroup",
                  "Version": 1,
                  "Engine": "MP2K",
                  "Id": 999,
                  "Pointer": "0x08999999",
                  "Offset": 999,
                  "DiscoverySource": "Manual",
                  "UsedBySongIds": [],
                  "Voices": []
                }
                """);

            var loaded = AgbSynthProjectLoader.Load(projectPath);

            Assert.Single(loaded.Songs);
            Assert.Single(loaded.SongHeaders);
            Assert.Contains(loaded.VoiceGroups, v => v.Id == 0 && v.FilePath == "test_data/voicegroup/voicegroup_000.agbvg");
            Assert.Contains(loaded.VoiceGroups, v => v.Id == 999 && v.DiscoverySource == "Manual");
            Assert.Equal("test_data/voicegroup/voicegroup_000.agbvg", loaded.SongHeaders.Single().VoiceGroupFilePath);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
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

    private static void WriteSongHeader(byte[] bytes, int offset, byte trackCount, uint voiceGroupPointer, uint trackPointer)
    {
        bytes[offset + 0] = trackCount;
        bytes[offset + 1] = 0;
        bytes[offset + 2] = 10;
        bytes[offset + 3] = 20;
        WriteU32(bytes, offset + 4, voiceGroupPointer);
        WriteU32(bytes, offset + 8, trackPointer);
    }

    private static void WriteVoice(byte[] bytes, int voiceGroupOffset, int index, byte type, byte key, uint dataPointer)
    {
        int offset = voiceGroupOffset + index * 12;
        bytes[offset + 0] = type;
        bytes[offset + 1] = key;
        WriteU32(bytes, offset + 4, dataPointer);
        bytes[offset + 8] = 1;
        bytes[offset + 9] = 2;
        bytes[offset + 10] = 3;
        bytes[offset + 11] = 4;
    }

    private static void WriteKeySplitVoice(byte[] bytes, int voiceGroupOffset, int index, uint regionPointer, uint keyMapPointer)
    {
        int offset = voiceGroupOffset + index * 12;
        bytes[offset + 0] = 0x40;
        WriteU32(bytes, offset + 4, regionPointer);
        WriteU32(bytes, offset + 8, keyMapPointer);
    }

    private static void WriteSampleHeader(byte[] bytes, int offset, byte loopFlags, uint frequency, uint loopStart, uint size)
    {
        bytes[offset + 3] = loopFlags;
        WriteU32(bytes, offset + 4, frequency);
        WriteU32(bytes, offset + 8, loopStart);
        WriteU32(bytes, offset + 12, size);
    }

    private static void WriteWaveRam(byte[] bytes, int offset)
    {
        for (int i = 0; i < 16; i++)
            bytes[offset + i] = 0xF0;
    }

    private static void WriteSoundModeReference(byte[] bytes, int songTableOffset, int referenceOffset, int playerTableOffset, int signatureOffset, uint rawValue)
    {
        WriteU32(bytes, referenceOffset - 4, 0x08000000u + (uint)playerTableOffset);
        WriteU32(bytes, referenceOffset, 0x08000000u + (uint)songTableOffset);

        WriteU32(bytes, playerTableOffset + 0x00, 0x03000000);
        WriteU32(bytes, playerTableOffset + 0x04, 0x03000100);
        bytes[playerTableOffset + 0x08] = 16;

        WriteU32(bytes, signatureOffset + 0x00, 0x08002200);
        WriteU32(bytes, signatureOffset + 0x04, 0x03000200);
        WriteU32(bytes, signatureOffset + 0x08, 0x04000010);
        WriteU32(bytes, signatureOffset + 0x0C, 0x03000300);
        WriteU32(bytes, signatureOffset + 0x10, 0x03000400);
        WriteU32(bytes, signatureOffset + 0x14, rawValue);
        WriteU32(bytes, signatureOffset + 0x18, 1);
        WriteU32(bytes, signatureOffset + 0x1C, 0x08000000u + (uint)playerTableOffset);
        WriteU32(bytes, signatureOffset + 0x20, 0x02000000);
    }
}

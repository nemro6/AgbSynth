using AgbSynth.App.Project;
using AgbSynth.App.ViewModels;
using Xunit;

namespace AgbSynth.Tests;

public sealed class ProjectEditingTests
{
    [Fact]
    public async Task Undo_AfterSave_RestoresVoiceEditToCleanCheckpoint()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"AgbSynth.Tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            string projectPath = Path.Combine(directory, "VoiceHistory.agbsynth");
            var viewModel = new MainWindowViewModel();
            Assert.True(await viewModel.CreateBlankProjectAsync(projectPath));

            string voiceGroupPath = Path.Combine(directory, "VoiceHistory_data", "voicegroup", "test.agbvg");
            viewModel.AddVoiceGroup(voiceGroupPath);
            Assert.True(await viewModel.SaveProjectAsync());
            Assert.False(viewModel.IsProjectDirty);

            VoiceRow voice = Assert.IsType<VoiceRow>(viewModel.SelectedVoice);
            string originalLabel = voice.Label;
            voice.Label = "Lead";
            voice.Label = originalLabel;
            Assert.False(viewModel.IsProjectDirty);

            voice.Label = "Lead";
            Assert.True(viewModel.IsProjectDirty);

            viewModel.UndoProjectEdit();
            Assert.Equal(originalLabel, voice.Label);
            Assert.False(viewModel.IsProjectDirty);

            viewModel.RedoProjectEdit();
            Assert.Equal("Lead", voice.Label);
            Assert.True(viewModel.IsProjectDirty);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task UndoRedo_CoalescesPropertyEditsAndRestoresCollectionChanges()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"AgbSynth.Tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            string projectPath = Path.Combine(directory, "History.agbsynth");
            var viewModel = new MainWindowViewModel();
            Assert.True(await viewModel.CreateBlankProjectAsync(projectPath));

            viewModel.AddSongTableEntryToEnd();
            SongTableEntryRow row = Assert.Single(viewModel.SongTableEntries);
            row.Label = "o";
            row.Label = "opening";

            viewModel.UndoProjectEdit();
            Assert.Equal(string.Empty, row.Label);
            Assert.Single(viewModel.SongTableEntries);

            viewModel.UndoProjectEdit();
            Assert.Empty(viewModel.SongTableEntries);
            Assert.False(viewModel.IsProjectDirty);

            viewModel.RedoProjectEdit();
            viewModel.RedoProjectEdit();
            Assert.Same(row, Assert.Single(viewModel.SongTableEntries));
            Assert.Equal("opening", row.Label);
            Assert.True(viewModel.IsProjectDirty);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task SaveProjectAsync_PersistsSongTableEditsAndClearsDirtyState()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"AgbSynth.Tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            string projectPath = Path.Combine(directory, "Editing.agbsynth");
            var viewModel = new MainWindowViewModel();
            Assert.True(await viewModel.CreateBlankProjectAsync(projectPath));
            Assert.False(viewModel.IsProjectDirty);

            viewModel.AddSongTableEntryToEnd();
            viewModel.SongTableEntries[0].Label = "opening";
            viewModel.SongTableEntries[0].Note = "test note";
            Assert.True(viewModel.IsProjectDirty);
            Assert.True(viewModel.CanSaveProject);

            Assert.True(await viewModel.SaveProjectAsync());
            Assert.False(viewModel.IsProjectDirty);
            Assert.False(viewModel.CanSaveProject);

            AgbSynthProjectFile reloaded = AgbSynthProjectLoader.Load(projectPath);
            SongTableEntryProjectInfo song = Assert.Single(reloaded.Songs);
            Assert.Equal("opening", song.Label);
            Assert.Equal("test note", song.Note);

            string transientHeaderPath = Path.Combine(directory, "Editing_data", "songheader", "transient.agbsh");
            viewModel.AddSequenceToEnd(transientHeaderPath);
            Assert.True(File.Exists(transientHeaderPath));
            viewModel.DeleteSelectedSequence();
            Assert.True(await viewModel.SaveProjectAsync());
            Assert.False(File.Exists(transientHeaderPath));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}

using AgbSynth.App.MIDI;
using Xunit;

namespace AgbSynth.Tests;

public sealed class MidiFileReaderTests
{
    [Fact]
    public void Read_ParsesPlaybackEventsAndLoopControllers()
    {
        string path = Path.Combine(Path.GetTempPath(), $"agbsynth_midi_reader_{Guid.NewGuid():N}.mid");
        try
        {
            var conductor = new MidiTrack("Conductor");
            conductor.AddTempo(0, 120);
            var track = new MidiTrack("Track");
            track.Add(0, 0, 0xC0, 5);
            track.Add(12, 0, 0xB0, 89, 127);
            track.Add(12, 1, 0x90, 60, 100);
            track.Add(24, -1, 0x80, 60, 0);
            track.Add(24, 0, 0xB0, 90, 127);

            File.WriteAllBytes(path, MidiFileWriter.Build([conductor, track], ticksPerQuarter: 48));

            MidiPlaybackFile midi = MidiFileReader.Read(path);

            Assert.Equal(48, midi.TicksPerQuarter);
            Assert.Contains(midi.Events, e => e.TrackIndex == 0 && e.Kind == MidiPlaybackEventKind.Tempo && e.Data3 == 500_000);
            Assert.Contains(midi.Events, e => e.TrackIndex == 1 && e.Kind == MidiPlaybackEventKind.ProgramChange && e.Data1 == 5);
            Assert.Contains(midi.Events, e => e.Kind == MidiPlaybackEventKind.ControlChange && e.Tick == 12 && e.Data1 == 89);
            Assert.Contains(midi.Events, e => e.Kind == MidiPlaybackEventKind.ControlChange && e.Tick == 24 && e.Data1 == 90);
            Assert.Contains(midi.Events, e => e.Kind == MidiPlaybackEventKind.NoteOn && e.Data1 == 60 && e.Data2 == 100);
            Assert.Contains(midi.Events, e => e.Kind == MidiPlaybackEventKind.NoteOff && e.Data1 == 60);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}

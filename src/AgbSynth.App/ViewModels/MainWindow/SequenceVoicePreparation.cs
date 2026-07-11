using System.Collections.Generic;
using System.Linq;
using AgbSynth.App.Audio;
using AgbSynth.App.MIDI;
using AgbSynth.App.Project;

namespace AgbSynth.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private Mp2kPreparedVoiceBank BuildSequenceVoiceBank(MidiPlaybackFile midi)
    {
        var bank = new Mp2kPreparedVoiceBank();
        if (SelectedVoiceGroup is null)
            return bank;

        int[] usedNotes = midi.Events
            .Where(midiEvent => midiEvent.Kind == MidiPlaybackEventKind.NoteOn && midiEvent.Data2 > 0)
            .Select(midiEvent => midiEvent.Data1)
            .Where(note => note is >= 0 and < 128)
            .Distinct()
            .ToArray();
        var payloads = new Dictionary<VoiceProjectInfo, PreparedPayload>(ReferenceEqualityComparer.Instance);
        var preparedPrograms = new HashSet<int>();

        foreach (VoiceProjectInfo rootVoice in SelectedVoiceGroup.Voices)
        {
            int program = rootVoice.Index;
            if ((uint)program >= 128 || !preparedPrograms.Add(program))
                continue;
            bank.SetProgramLabel(program, ResolveVoiceLabel(rootVoice));

            foreach (int note in usedNotes)
            {
                ResolvedPlayableVoice? resolved = ResolvePlayableVoice(rootVoice, note, isDrumEntry: false, program);
                if (resolved is null)
                    continue;

                if (!payloads.TryGetValue(resolved.Voice, out PreparedPayload payload))
                {
                    payload = PrepareVoicePayload(resolved.Voice);
                    payloads.Add(resolved.Voice, payload);
                }

                bank.Set(
                    program,
                    note,
                    new Mp2kPreparedVoice(
                        resolved.Voice,
                        resolved.BaseKey,
                        resolved.PlaybackNote,
                        resolved.ForcedPan,
                        resolved.ProgramId,
                        payload.Pcm,
                        payload.SampleHeader,
                        payload.WaveRam));
            }
        }

        return bank;
    }

    private PreparedPayload PrepareVoicePayload(VoiceProjectInfo voice)
    {
        float[]? pcm = null;
        SampleHeaderProjectInfo? sampleHeader = null;
        byte[]? waveRam = null;

        if (voice.Sample is { } sample &&
            TryLoadSamplePcm(voice, sample, out byte[] loadedPcm, out SampleHeaderProjectInfo loadedHeader))
        {
            pcm = new float[loadedPcm.Length];
            for (int i = 0; i < loadedPcm.Length; i++)
                pcm[i] = unchecked((sbyte)loadedPcm[i]) / 128f;
            sampleHeader = loadedHeader;
        }

        if (IsPsgWaveMemoryVoice(voice) &&
            GetPsgWaveMemoryDataOffset(voice) is int waveMemoryOffset &&
            TryLoadWaveMemoryData(voice, waveMemoryOffset, out byte[] loadedWaveRam))
        {
            waveRam = loadedWaveRam;
        }

        return new PreparedPayload(pcm, sampleHeader, waveRam);
    }

    private readonly record struct PreparedPayload(
        float[]? Pcm,
        SampleHeaderProjectInfo? SampleHeader,
        byte[]? WaveRam);
}

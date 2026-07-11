using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia.Media;
using AgbSynth.App.Project;

namespace AgbSynth.App.ViewModels;
public sealed record VoiceTypeOption(int Type, string Label)
{
    public string Display => $"{Label} (0x{Type:X2})";
}

public sealed record VoiceDataOption(int Value, string Label)
{
    public string Display => Label;
}

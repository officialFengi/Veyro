using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Veyro;

internal sealed record Preferences(string Theme = "Graphite", int Width = 560,
    bool SmartSort = false, Dictionary<string, string>? Assignments = null,
    List<string>? CustomGroups = null, List<string>? AppOrder = null, List<string>? HiddenGroups = null,
    Dictionary<string, string>? CustomColors = null, bool MultiSound = false,
    Dictionary<string, string>? ExtraOutputs = null, int ShortcutKey = 0, int ShortcutModifiers = 0,
    int? BackgroundDimming = null, int ElementTransparency = 0,
    Dictionary<string, EqualizerProfile>? Equalizers = null)
{
    internal static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Veyro", "settings.json");

    internal static Preferences Load()
    {
        var saved = new Preferences();
        try
        {
            if (File.Exists(FilePath))
            {
                using var stream = File.OpenRead(FilePath);
                saved = JsonSerializer.Deserialize(stream, PreferencesJson.Default.Preferences) ?? saved;
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { }

        return saved with
        {
            Equalizers = EqualizerProfile.Normalize(saved.Equalizers),
            ExtraOutputs = saved.ExtraOutputs ?? new(), Assignments = saved.Assignments ?? new(),
            CustomGroups = saved.CustomGroups ?? new(), AppOrder = saved.AppOrder ?? new(), HiddenGroups = saved.HiddenGroups ?? new(),
            BackgroundDimming = saved.BackgroundDimming is int dimming ? Math.Clamp(dimming, 0, 100) : null,
            ElementTransparency = Math.Clamp(saved.ElementTransparency, 0, 100)
        };
    }
}

[JsonSerializable(typeof(Preferences))]
internal partial class PreferencesJson : JsonSerializerContext { }

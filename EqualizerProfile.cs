using System;
using System.Collections.Generic;
using System.Linq;
using NAudio.Dsp;
using NAudio.Wave;

namespace Veyro;

internal sealed record EqualizerProfile(string Name, int[] Gains, bool Enabled = true, string? CableId = null)
{
    internal static readonly int[] Frequencies = { 63, 160, 400, 1000, 2500, 6300, 16000 };
    internal static readonly (string Name, int[] Gains)[] Presets = {
        ("Ровно", new[] { 0, 0, 0, 0, 0, 0, 0 }),
        ("Бас", new[] { 5, 3, 0, -1, 0, 1, 0 }),
        ("Речь", new[] { -5, -3, -1, 2, 4, 1, -2 }),
        ("Детали", new[] { 0, -1, -2, 0, 2, 3, 2 })
    };

    internal static Dictionary<string, EqualizerProfile> Normalize(Dictionary<string, EqualizerProfile>? saved)
    {
        var result = new Dictionary<string, EqualizerProfile>(StringComparer.OrdinalIgnoreCase);
        if (saved is null) return result;
        var cables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in saved)
        {
            if (string.IsNullOrWhiteSpace(key) || value?.Gains?.Length != Frequencies.Length) continue;
            result[key] = value with {
                Gains = value.Gains.Select(x => Math.Clamp(x, -12, 12)).ToArray(),
                CableId = value.CableId is { Length: > 0 } id && cables.Add(id) ? id : null
            };
        }
        return result;
    }
}

// Coefficients and filter histories are owned by the playback thread.
// Published profiles are immutable snapshots, including their gain arrays.
internal sealed class EqualizerSamples : ISampleProvider
{
    private readonly ISampleProvider source;
    private readonly BiQuadFilter[,] filters;
    private volatile EqualizerProfile target;
    private EqualizerProfile? current;
    private int channel;
    private bool flat;
    public WaveFormat WaveFormat => source.WaveFormat;

    public EqualizerSamples(ISampleProvider source, EqualizerProfile profile)
    {
        this.source = source;
        target = profile;
        filters = new BiQuadFilter[WaveFormat.Channels, EqualizerProfile.Frequencies.Length];
    }

    public void Update(EqualizerProfile profile) => target = profile;

    public int Read(float[] buffer, int offset, int count)
    {
        int read = source.Read(buffer, offset, count);
        var snapshot = target;
        if (current != snapshot)
        {
            flat = !snapshot.Enabled || snapshot.Gains.All(g => g == 0);
            for (int c = 0; c < filters.GetLength(0); c++)
            for (int band = 0; band < filters.GetLength(1); band++)
            {
                int hz = EqualizerProfile.Frequencies[band];
                if (hz >= WaveFormat.SampleRate * 0.49 || !snapshot.Enabled || snapshot.Gains[band] == 0)
                {
                    filters[c, band] = null!;
                    continue;
                }
                var filter = filters[c, band];
                if (filter is null)
                    filters[c, band] = BiQuadFilter.PeakingEQ(WaveFormat.SampleRate, hz, 0.95f, snapshot.Gains[band]);
                else
                    filter.SetPeakingEq(WaveFormat.SampleRate, hz, 0.95f, snapshot.Gains[band]);
            }
            current = snapshot;
        }
        if (flat) { channel = (channel + read) % WaveFormat.Channels; return read; }
        for (int i = offset; i < offset + read; i++)
        {
            float sample = buffer[i];
            for (int b = 0; b < filters.GetLength(1); b++)
                if (filters[channel, b] is { } filter) sample = filter.Transform(sample);
            buffer[i] = Math.Clamp(sample, -1f, 1f);
            if (++channel == WaveFormat.Channels) channel = 0;
        }
        return read;
    }
}

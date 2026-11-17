using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Media;

namespace Veyro;

internal sealed class MixerChannel(string key, string name, ImageSource? icon) : INotifyPropertyChanged
{
    private double volume;
    private bool muted;
    private Action<float>? writeVolume;
    private Action<bool>? writeMute;

    public string Key { get; } = key;
    public string Name { get; } = name;
    public ImageSource? Icon => muted ? null : icon;
    public string Glyph => muted ? "\uE74F" : icon is null ? "\uE767" : "";
    public string MuteHint => $"{(muted ? "Включить звук" : "Выключить звук")} — {Name}";

    public double Volume
    {
        get => volume;
        set
        {
            var rounded = Math.Round(value);
            if (rounded == volume)
                return;
            try
            {
                writeVolume?.Invoke((float)(rounded / 100));
                SetVolume(rounded);
            }
            catch (COMException)
            {
                Notify(nameof(Volume));
            }
        }
    }

    public void Refresh(float level, bool isMuted, Action<float> setVolume, Action<bool> setMute)
    {
        writeVolume = setVolume;
        writeMute = setMute;
        SetVolume(Math.Round(level * 100));
        SetMute(isMuted);
    }

    public void ToggleMute()
    {
        try
        {
            writeMute?.Invoke(!muted);
            SetMute(!muted);
        }
        catch (COMException)
        {
            // The next refresh will recover the actual device state.
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void SetVolume(double value)
    {
        if (volume == value)
            return;
        volume = value;
        Notify(nameof(Volume));
    }

    private void SetMute(bool value)
    {
        if (muted == value)
            return;
        muted = value;
        Notify(nameof(Icon));
        Notify(nameof(Glyph));
        Notify(nameof(MuteHint));
    }

    private void Notify(string property) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
}

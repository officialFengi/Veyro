using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NAudio.CoreAudioApi;

namespace Veyro;

public partial class MainWindow
{
    private sealed record OutputControl(MMDevice Device, MixerChannel Channel);
    private readonly Dictionary<string, OutputControl> extraOutputs = new(StringComparer.OrdinalIgnoreCase);

    private void ClearExtraOutputs()
    {
        foreach (var output in extraOutputs.Values) output.Device.Dispose();
        extraOutputs.Clear();
    }

    private bool RefreshExtraOutputs(string primaryId)
    {
        bool changed = false;
        var selected = preferences.ExtraOutputs!;
        foreach (var id in extraOutputs.Keys.ToArray())
        {
            if (preferences.MultiSound && selected.ContainsKey(id) && id != primaryId) continue;
            extraOutputs[id].Device.Dispose();
            extraOutputs.Remove(id);
            changed = true;
        }
        if (!preferences.MultiSound) return changed;

        foreach (var id in selected.Keys)
        {
            if (id == primaryId) continue;
            try
            {
                if (!extraOutputs.TryGetValue(id, out var output))
                {
                    var device = audio.GetDevice(id);
                    // The saved label also works while Windows is reconnecting this endpoint.
                    output = new OutputControl(device, new MixerChannel("device:" + id, selected[id], null));
                    extraOutputs.Add(id, output);
                    changed = true;
                    var volume = device.AudioEndpointVolume;
                    volume.OnVolumeNotification += changes.OnEndpointVolume;
                    output.Channel.Bind(value => volume.MasterVolumeLevelScalar = value, mute => volume.Mute = mute);
                }
                if (output.Device.State != DeviceState.Active)
                {
                    output.Device.Dispose();
                    extraOutputs.Remove(id);
                    changed = true;
                    continue;
                }
            }
            catch (Exception error) when (error is COMException or System.IO.FileNotFoundException or InvalidComObjectException)
            {
                if (extraOutputs.Remove(id, out var lost)) { lost.Device.Dispose(); changed = true; }
            }
        }
        return changed;
    }

    private void AddMultiSoundOptions(StackPanel parent, Dictionary<string, string> available)
    {
        var section = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        parent.Children.Add(section);

        void AddButton(string title, Action click)
        {
            var button = new Button
            {
                Style = (Style)FindResource(typeof(Button)),
                Margin = new Thickness(0, 0, 0, 4),
                Content = new TextBlock { Text = title, Width = 306, TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(10, 8, 10, 8), Foreground = (Brush)Resources["Ink"] }
            };
            button.Click += (_, _) =>
            {
                click();
                SavePreferences();
                RefreshMixer();
                Render();
            };
            section.Children.Add(button);
        }

        void Render()
        {
            section.Children.Clear();
            AddButton(preferences.MultiSound ? "Мультизвук · Включён" : "Мультизвук · Выключен",
                () => preferences = preferences with { MultiSound = !preferences.MultiSound });
            if (!preferences.MultiSound) return;
            var selected = preferences.ExtraOutputs!;
            var ids = available.Keys.Concat(selected.Keys).Distinct()
                .Where(id => id != endpoint?.ID || selected.ContainsKey(id)).ToArray();
            section.Children.Add(new TextBlock
            {
                Text = ids.Length == 0 ? "Подключите ещё одно устройство вывода." : "Дополнительные регуляторы",
                Margin = new Thickness(10, 8, 10, 8), Foreground = (Brush)Resources["Secondary"]
            });
            foreach (var id in ids)
            {
                var added = selected.ContainsKey(id);
                var name = available.TryGetValue(id, out var activeName) ? activeName : selected[id] + " (отключено)";
                AddButton((added ? "Убрать · " : "Добавить · ") + name, () =>
                {
                    if (!selected.Remove(id)) selected[id] = available[id];
                });
            }
        }
        Render();
    }
}

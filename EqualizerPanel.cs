using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using NAudio.CoreAudioApi;

namespace Veyro;

public partial class MainWindow
{
    private Border? eqHost;
    private EqualizerEditor? eqEditor;
    private string? eqKey;
    private readonly DispatcherTimer eqSave = new() { Interval = TimeSpan.FromMilliseconds(250) };

    private void ToggleEqualizer(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: MixerChannel row } button || !row.HasEqualizer) return;
        if (FindRow(button)?.Parent is not StackPanel stack || stack.Children[1] is not Border host) return;
        bool close = eqHost == host;
        CloseEqualizerEditor();
        if (close) return;
        eqKey = row.Key;
        eqHost = host;
        var initial = preferences.Equalizers!.GetValueOrDefault(row.Key)
            ?? new EqualizerProfile(row.Name, EqualizerProfile.Presets[0].Gains);
        eqEditor = new EqualizerEditor(initial, () => ListCables(row.Key), profile => {
            var profiles = preferences.Equalizers!;
            if (profile.CableId is not null && profiles.Any(entry =>
                !StringComparer.OrdinalIgnoreCase.Equals(entry.Key, row.Key) && entry.Value.CableId == profile.CableId))
            {
                eqEditor?.SetStatus("Этот кабель уже выделен другому приложению.");
                return;
            }
            bool routeChanged = profiles.GetValueOrDefault(row.Key)?.CableId != profile.CableId;
            profiles[row.Key] = profile;
            if (routeChanged)
            {
                eqSave.Stop();
                SavePreferences();
                RefreshEqualizerRoutes();
                cachedSessions = null;
                if (IsVisible) RefreshMixer();
            }
            else
            {
                if (eqRoutes.TryGetValue(row.Key, out var route)) route.Update(profile);
                eqSave.Stop();
                eqSave.Start();
                UpdateEqualizerStatus();
            }
        });
        host.Child = eqEditor;
        host.Visibility = Visibility.Visible;
        host.Unloaded += EqualizerHostUnloaded;
        UpdateEqualizerStatus();
        host.BringIntoView();
    }

    private void EqualizerHostUnloaded(object sender, RoutedEventArgs e)
    {
        if (ReferenceEquals(sender, eqHost)) CloseEqualizerEditor();
    }

    private void CloseEqualizerEditor()
    {
        if (eqHost is null) return;
        eqHost.Unloaded -= EqualizerHostUnloaded;
        eqHost.Visibility = Visibility.Collapsed;
        eqHost.Child = null;
        eqHost = null;
        eqEditor = null;
        eqKey = null;
    }

    private List<CableChoice> ListCables(string key)
    {
        var choices = new List<CableChoice>();
        try
        {
            foreach (var item in audio.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            {
                using var device = item;
                if (!CanIsolate(device) || device.ID == endpoint?.ID) continue;
                var owner = preferences.Equalizers!.FirstOrDefault(entry => entry.Value.CableId == device.ID);
                bool free = owner.Key is null || StringComparer.OrdinalIgnoreCase.Equals(owner.Key, key);
                choices.Add(new CableChoice(device.ID, device.FriendlyName + (free ? "" : "  ·  " + owner.Value.Name), free));
            }
        }
        catch (Exception ex) when (AudioUnavailable(ex)) { }
        return choices;
    }

    private void UpdateEqualizerStatus()
    {
        if (eqEditor is null || eqKey is null) return;
        string status = eqStates.GetValueOrDefault(eqKey) ?? "Для обработки звука выберите выход в разделе «Маршрут».";
        eqEditor.SetStatus(status);
    }

    private void FlushEqualizerSave()
    {
        if (!eqSave.IsEnabled) return;
        eqSave.Stop();
        SavePreferences();
    }
}

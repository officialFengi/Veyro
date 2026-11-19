using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Veyro;

public partial class MainWindow
{
    private readonly ObservableCollection<MixerChannel> displayedRows = new();
    private readonly Dictionary<string, MixerChannel> groupRows = new();
    private readonly Dictionary<MixerChannel, MixerChannel[]> groupMembers = new();
    private readonly HashSet<string> expandedGroups = new();
    private static readonly string[] Categories = { "Мессенджеры", "Браузеры", "Музыка и видео", "Игры", "Другое" };

    private string CategoryFor(string key, string name)
    {
        var category = InferCategory(key, name);
        return preferences.HiddenGroups!.Contains(category) ? "" : category;
    }
    private string InferCategory(string key, string name)
    {
        if (preferences.Assignments!.TryGetValue(key, out var assigned))
            return assigned;
        if (!preferences.SmartSort)
            return "";
        var text = (key + " " + name).ToLowerInvariant();
        if (new[] { "discord", "telegram", "whatsapp", "slack", "teams", "skype", "viber", "zoom", "signal" }.Any(text.Contains))
            return Categories[0];
        if (new[] { "chrome", "firefox", "opera", "msedge", "brave", "browser" }.Any(text.Contains))
            return Categories[1];
        if (new[] { "spotify", "music", "музыка", "vlc", "foobar", "aimp", "mpv", "itunes" }.Any(text.Contains))
            return Categories[2];
        if (new[] { "steamapps", "games", "minecraft", "fortnite", "valorant", "cs2.exe" }.Any(text.Contains))
            return Categories[3];
        return Categories[4];
    }

    private void RefreshGroups(Dictionary<string, AppSessions> apps)
    {
        groupMembers.Clear();
        var desired = new List<MixerChannel>();
        var master = rows.FirstOrDefault(row => row.Key == "master");
        if (master is not null)
            desired.Add(master);
        desired.AddRange(extraOutputs.Values.Select(output => output.Channel));
        var ordered = rows.Where(row => row.Key != "master")
            .OrderBy(row =>
            {
                var index = preferences.AppOrder!.IndexOf(row.Key);
                return index < 0 ? int.MaxValue : index;
            }).ThenBy(row => row.Name, StringComparer.CurrentCultureIgnoreCase);
        foreach (var group in ordered.GroupBy(row => CategoryFor(row.Key, row.Name)))
        {
            if (group.Key == "")
            {
                desired.AddRange(group);
                continue;
            }
            var key = "group:" + group.Key;
            if (!groupRows.TryGetValue(key, out var channel))
            {
                channel = new MixerChannel(key, "▸ " + group.Key, null);
                groupRows.Add(key, channel);
            }
            var volumes = group.Where(row => apps.ContainsKey(row.Key)).SelectMany(row => apps[row.Key].Volumes).ToList();
            if (volumes.Count == 0)
                continue;
            groupMembers[channel] = group.ToArray();
            channel.Bind(
                value => ApplyToSessions(volumes, volume => volume.Volume = value),
                muted => ApplyToSessions(volumes, volume => volume.Mute = muted));
            desired.Add(channel);
            if (expandedGroups.Contains(key))
                desired.AddRange(group);
        }
        var currentGroups = desired.Where(row => row.Key.StartsWith("group:")).Select(row => row.Key).ToHashSet();
        foreach (var stale in groupRows.Keys.Where(key => !currentGroups.Contains(key)).ToArray())
            groupRows.Remove(stale);
        // Retain row instances and mouse capture while audio values refresh.
        for (var index = 0; index < desired.Count; index++)
        {
            if (index < displayedRows.Count && displayedRows[index] == desired[index])
                continue;
            var existing = displayedRows.IndexOf(desired[index]);
            if (existing >= 0)
                displayedRows.Move(existing, index);
            else
                displayedRows.Insert(index, desired[index]);
        }
        while (displayedRows.Count > desired.Count)
            displayedRows.RemoveAt(displayedRows.Count - 1);
    }

    private void ToggleGroup(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: MixerChannel row } || !row.Key.StartsWith("group:"))
            return;
        if (!expandedGroups.Remove(row.Key))
            expandedGroups.Add(row.Key);
        RefreshMixer();
        e.Handled = true;
    }

    private void ToggleSmartSort(object sender, RoutedEventArgs e)
    {
        preferences = preferences with { SmartSort = !preferences.SmartSort };
        SavePreferences();

        RefreshMixer();
    }

    private void CreateGroup(object sender, RoutedEventArgs e)
    {
        var name = NewGroupName.Text.Trim();
        if (name.Length == 0)
            return;
        if (!Categories.Concat(preferences.CustomGroups!).Contains(name, StringComparer.CurrentCultureIgnoreCase))
            preferences.CustomGroups!.Add(name);
        SavePreferences();
        NewGroupName.Clear();
        SettingsSummary.Text = $"Папка «{name}» доступна в меню сортировки приложения";
    }

    private void ShowSorting(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: MixerChannel row } || row.IsOutput || row.Key.StartsWith("group:"))
            return;
        OpenFolderPicker(row);
        e.Handled = true;
    }

    private void MoveApplication(MixerChannel row, int direction)
    {
        var category = CategoryFor(row.Key, row.Name);
        var siblings = rows.Where(item => item.Key != "master" && CategoryFor(item.Key, item.Name) == category)
            .OrderBy(item =>
            {
                var index = preferences.AppOrder!.IndexOf(item.Key);
                return index < 0 ? int.MaxValue : index;
            }).ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase).Select(item => item.Key).ToList();
        var oldIndex = siblings.IndexOf(row.Key);
        if (oldIndex < 0) return;
        var newIndex = Math.Clamp(oldIndex + direction, 0, siblings.Count - 1);
        (siblings[oldIndex], siblings[newIndex]) = (siblings[newIndex], siblings[oldIndex]);
        preferences.AppOrder!.RemoveAll(siblings.Contains);
        preferences.AppOrder.InsertRange(0, siblings);
    }
}





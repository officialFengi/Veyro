using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Veyro;

public partial class MainWindow
{
    private void RefreshFolderManagement()
    {
        FolderManagement.Children.Clear();
        foreach (var name in Categories.Concat(preferences.CustomGroups!).Distinct(StringComparer.CurrentCultureIgnoreCase)
                     .Where(name => !preferences.HiddenGroups!.Contains(name)))
        {
            var panel = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            var remove = new Button { Content = "Удалить папку", FontSize = 18, Width = 175, Height = 44 };
            DockPanel.SetDock(remove, Dock.Right);
            remove.Click += (_, _) => DeleteFolder(name);
            panel.Children.Add(remove);
            panel.Children.Add(new TextBlock { Text = name, FontSize = 21, VerticalAlignment = VerticalAlignment.Center });
            FolderManagement.Children.Add(panel);
        }
    }

    private void DeleteFolder(string name)
    {
        foreach (var key in preferences.Assignments!.Where(pair => pair.Value == name).Select(pair => pair.Key).ToArray())
            preferences.Assignments![key] = "";
        foreach (var row in rows.Where(row => row.Key != "master" && CategoryFor(row.Key, row.Name) == name))
            preferences.Assignments![row.Key] = "";
        preferences.CustomGroups!.RemoveAll(value => value == name);
        if (Categories.Contains(name) && !preferences.HiddenGroups!.Contains(name))
            preferences.HiddenGroups.Add(name);
        expandedGroups.Remove("group:" + name);
        SavePreferences();
        RefreshFolderManagement();
        RefreshMixer();
        SettingsSummary.Text = $"Папка «{name}» удалена. Приложения перенесены в общий список.";
    }
}


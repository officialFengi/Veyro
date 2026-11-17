using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Veyro;

public partial class MainWindow
{
    private MixerChannel? folderTarget;

    private void OpenFolderPicker(MixerChannel row)
    {
        folderTarget = row;
        FolderAppName.Text = row.Name;
        FolderNameInput.Clear();
        FolderFeedback.Text = "";
        MixerHeader.Visibility = Visibility.Collapsed;
        SessionScroll.Visibility = Visibility.Collapsed;
        Status.Visibility = Visibility.Collapsed;
        FolderPanel.Visibility = Visibility.Visible;
        timer.Stop();
        PopulateFolderOptions();
    }

    private void PopulateFolderOptions()
    {
        if (folderTarget is null) return;
        FolderOptions.Children.Clear();
        var manual = preferences.Assignments!.TryGetValue(folderTarget.Key, out var assigned);
        void Add(string title, string subtitle, string? category, bool selected)
        {
            var button = new Button
            {
                Margin = new Thickness(0, 0, 0, 8),
                BorderBrush = selected ? (Brush)Resources["Accent"] : Brushes.Transparent,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                ToolTip = subtitle
            };
            var grid = new Grid { Width = 715, Margin = new Thickness(18, 14, 18, 14) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(45) });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(35) });
            grid.Children.Add(new TextBlock
            {
                Text = category is null ? "" : "", FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 24, VerticalAlignment = VerticalAlignment.Center, Foreground = (Brush)Resources["Accent"]
            });
            var text = new StackPanel();
            text.Children.Add(new TextBlock { Text = title, FontSize = 23, FontWeight = FontWeights.SemiBold });
            text.Children.Add(new TextBlock { Text = subtitle, FontSize = 17, Foreground = (Brush)Resources["Secondary"], Margin = new Thickness(0, 5, 0, 0) });
            Grid.SetColumn(text, 1);
            grid.Children.Add(text);
            var check = new TextBlock { Text = selected ? "✓" : "", FontSize = 25, Foreground = (Brush)Resources["Accent"], VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(check, 2);
            grid.Children.Add(check);
            button.Content = grid;
            button.Click += (_, _) => AssignFolder(category);
            FolderOptions.Children.Add(button);
        }
        Add("Автоматически", preferences.SmartSort ? "Папка определяется умной сортировкой" : "Будет работать после включения умной сортировки", null, !manual);
        Add("Убрать из папки", "Показывать отдельно, даже при умной сортировке", "", manual && assigned == "");
        foreach (var name in Categories.Concat(preferences.CustomGroups!).Where(name => !preferences.HiddenGroups!.Contains(name)).Distinct(StringComparer.CurrentCultureIgnoreCase))
        {
            var count = rows.Count(row => row.Key != "master" && CategoryFor(row.Key, row.Name) == name);
            Add(name, $"Приложений сейчас · {count}", name, manual && assigned == name);
        }
    }

    private void AssignFolder(string? category)
    {
        if (folderTarget is null) return;
        if (category is null) preferences.Assignments!.Remove(folderTarget.Key);
        else preferences.Assignments![folderTarget.Key] = category;
        SavePreferences();
        if (category?.Length > 0) expandedGroups.Add("group:" + category);
        CloseFolderPicker(this, new RoutedEventArgs());
    }

    private void CreateAndAssignFolder(object sender, RoutedEventArgs e)
    {
        var name = FolderNameInput.Text.Trim();
        if (name.Length == 0)
        {
            FolderFeedback.Text = "Введите название папки";
            FolderNameInput.Focus();
            return;
        }
        var existing = Categories.Concat(preferences.CustomGroups!).FirstOrDefault(item => string.Equals(item, name, StringComparison.CurrentCultureIgnoreCase));
        if (existing is null) preferences.CustomGroups!.Add(name);
        preferences.HiddenGroups!.Remove(existing ?? name);
        AssignFolder(existing ?? name);
    }

    private void MoveFromPicker(object sender, RoutedEventArgs e)
    {
        if (folderTarget is null) return;
        MoveApplication(folderTarget, int.Parse((string)((Button)sender).Tag));
        SavePreferences();
        FolderFeedback.Text = "Порядок сохранён";
    }

    private void CloseFolderPicker(object sender, RoutedEventArgs e)
    {
        FolderPanel.Visibility = Visibility.Collapsed;
        MixerHeader.Visibility = Visibility.Visible;
        SessionScroll.Visibility = Visibility.Visible;
        Status.Visibility = Visibility.Visible;
        folderTarget = null;
        RefreshMixer();
        if (IsVisible) timer.Start();
    }
}



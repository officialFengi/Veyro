using System.Collections.Generic;
using System;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Veyro;

public partial class MainWindow
{
    private sealed record Preferences(string Theme = "Graphite", int Width = 560,
        bool SmartSort = false, Dictionary<string, string>? Assignments = null,
        List<string>? CustomGroups = null, List<string>? AppOrder = null, List<string>? HiddenGroups = null, Dictionary<string, string>? CustomColors = null);
    private Preferences preferences = new();
    private static readonly string PreferencesPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Veyro", "settings.json");

    private void LoadPreferences()
    {
        try
        {
            if (File.Exists(PreferencesPath))
                preferences = JsonSerializer.Deserialize<Preferences>(File.ReadAllText(PreferencesPath)) ?? new();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            preferences = new();
        }
        preferences = preferences with
        {
            Assignments = preferences.Assignments ?? new(),
            CustomGroups = preferences.CustomGroups ?? new(),
            AppOrder = preferences.AppOrder ?? new(), HiddenGroups = preferences.HiddenGroups ?? new()
        };
        ApplyPreferences();
    }

    private void ApplyPreferences()
    {
        var palette = ThemePalette.Resolve(preferences.Theme, preferences.CustomColors);
        if (preferences.Theme is not ("Graphite" or "Pearl" or "Iris" or "Forest" or "Custom"))
            preferences = preferences with { Theme = "Graphite" };
        if (preferences.Width is not (460 or 560 or 660))
            preferences = preferences with { Width = 560 };
        if (preferences.Theme == "Custom")
            palette = ThemePalette.Resolve("Custom", preferences.CustomColors);
        var keys = new[] { "Panel", "Surface", "Stroke", "Ink", "Secondary", "Accent" };
        for (var i = 0; i < keys.Length; i++)
            Resources[keys[i]] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(palette[i]));
        Width = preferences.Width;
        InterfaceScale.ScaleX = InterfaceScale.ScaleY = preferences.Width / 924.0;
        var themeName = preferences.Theme switch
        {
            "Custom" => "Своя тема", "Pearl" => "Жемчуг", "Iris" => "Ирис", "Forest" => "Хвоя", _ => "Графит"
        };
        SettingsSummary.Text = $"{themeName} · {preferences.Width} px · Сохраняется автоматически";
        foreach (var theme in new[] { "Graphite", "Pearl", "Iris", "Forest" })
            ((Button)FindName("Theme" + theme)).BorderBrush = theme == preferences.Theme
                ? (Brush)Resources["Accent"] : Brushes.Transparent;
        foreach (var width in new[] { 460, 560, 660 })
            ((Button)FindName("Size" + width)).BorderBrush = width == preferences.Width
                ? (Brush)Resources["Accent"] : Brushes.Transparent;
        SmartSortButton.Tag = preferences.SmartSort.ToString();
        SmartSortButton.Content = preferences.SmartSort ? "Вкл. ●" : "Выкл. ○";
        SmartSortButton.BorderBrush = preferences.SmartSort ? (Brush)Resources["Accent"] : Brushes.Transparent;
    }

    private void SavePreferences()
    {
        preferences = preferences with
        {
            Assignments = preferences.Assignments ?? new(),
            CustomGroups = preferences.CustomGroups ?? new(),
            AppOrder = preferences.AppOrder ?? new(), HiddenGroups = preferences.HiddenGroups ?? new()
        };
        ApplyPreferences();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PreferencesPath)!);
            var temporary = PreferencesPath + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(preferences));
            File.Move(temporary, PreferencesPath, true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            SettingsSummary.Text += " · Не удалось сохранить";
        }
    }

    private void ToggleSettings(object sender, RoutedEventArgs e)
    {
        var opening = SettingsPanel.Visibility != Visibility.Visible;
        SettingsPanel.Visibility = opening ? Visibility.Visible : Visibility.Collapsed;
        SessionScroll.Visibility = opening ? Visibility.Collapsed : Visibility.Visible;
        MixerHeader.Visibility = opening ? Visibility.Collapsed : Visibility.Visible;
        Status.Visibility = opening ? Visibility.Collapsed : Visibility.Visible;
        if (opening)
            timer.Stop();
        else if (IsVisible)
        {
            RefreshMixer();
            timer.Start();
        }
    }

    private void SelectTheme(object sender, RoutedEventArgs e)
    {
        preferences = preferences with { Theme = (string)((Button)sender).Tag };
        SavePreferences();
    }

    private void SelectSize(object sender, RoutedEventArgs e)
    {
        preferences = preferences with { Width = int.Parse((string)((Button)sender).Tag) };
        SavePreferences();
    }
}







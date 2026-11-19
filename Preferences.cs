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
    private Preferences preferences;
    private bool saveFailed;
    private FrameworkElement? settingsView;

    private T Setting<T>(string name) where T : FrameworkElement => (T)settingsView!.FindName(name);
    private Button ShortcutButton => Setting<Button>(nameof(ShortcutButton));
    private Button SmartSortButton => Setting<Button>(nameof(SmartSortButton));
    private TextBlock ShortcutHint => Setting<TextBlock>(nameof(ShortcutHint));
    private TextBlock SettingsSummary => Setting<TextBlock>(nameof(SettingsSummary));
    private TextBox NewGroupName => Setting<TextBox>(nameof(NewGroupName));
    private StackPanel FolderManagement => Setting<StackPanel>(nameof(FolderManagement));
    private ScrollViewer SettingsScroll => Setting<ScrollViewer>(nameof(SettingsScroll));

    private void EnsureSettingsView()
    {
        if (settingsView is not null) return;
        settingsView = (FrameworkElement)((DataTemplate)FindResource("SettingsContent")).LoadContent();
        SettingsPanel.Child = settingsView;
        UpdateSettingsSummary();
        UpdateShortcutLabel();
        if (preferences.ShortcutKey != 0 && !shortcut.IsRegistered)
            ShortcutHint.Text = "Сохранённое сочетание недоступно. Назначьте его снова или выберите другое.";
    }
    private static string PreferencesPath => Preferences.FilePath;

    private void ApplyPreferences(Dictionary<string, string>? previewColors = null)
    {
        if (preferences.Theme is not ("Graphite" or "Pearl" or "Iris" or "Forest" or "Custom"))
            preferences = preferences with { Theme = "Graphite" };
        if (preferences.Width is not (460 or 560 or 660))
            preferences = preferences with { Width = 560 };
        bool preview = previewColors is not null;
        var palette = ThemePalette.Resolve(preview ? "Custom" : preferences.Theme, previewColors ?? preferences.CustomColors);
        int transparency = preview ? themeStudio!.ElementTransparency :
            preferences.Theme == "Custom" ? preferences.ElementTransparency : 0;
        double opacity = 1 - transparency / 100.0;
        for (var i = 0; i < ThemePalette.Keys.Length; i++)
        {
            var key = ThemePalette.Keys[i];
            SetThemeBrush(key, palette[i], key == "Surface" ? opacity : 1);
        }
        SetThemeBrush("ElementPanel", palette[0], opacity);
        SetThemeBrush("ElementAccent", palette[5], opacity);
        ApplyThemeBackground(preview);
        if (preview) return;
        Width = preferences.Width;
        InterfaceScale.ScaleX = InterfaceScale.ScaleY = preferences.Width / 924.0;
        if (settingsView is not null) UpdateSettingsSummary();
    }

    private void UpdateSettingsSummary()
    {
        var themeName = preferences.Theme switch
        {
            "Custom" => "Своя тема", "Pearl" => "Жемчуг", "Iris" => "Ирис", "Forest" => "Хвоя", _ => "Графит"
        };
        SettingsSummary.Text = $"{themeName} · {preferences.Width} px · Сохраняется автоматически";
        foreach (var theme in new[] { "Graphite", "Pearl", "Iris", "Forest" })
            Setting<Button>("Theme" + theme).BorderBrush = theme == preferences.Theme
                ? (Brush)Resources["Accent"] : Brushes.Transparent;
        foreach (var width in new[] { 460, 560, 660 })
            Setting<Button>("Size" + width).BorderBrush = width == preferences.Width
                ? (Brush)Resources["Accent"] : Brushes.Transparent;
        SmartSortButton.Tag = preferences.SmartSort.ToString();
        SmartSortButton.Content = preferences.SmartSort ? "Вкл. ●" : "Выкл. ○";
        SmartSortButton.BorderBrush = preferences.SmartSort ? (Brush)Resources["Accent"] : Brushes.Transparent;
    }

    private void SetThemeBrush(string name, string hex, double opacity)
    {
        var color = (Color)ColorConverter.ConvertFromString(hex);
        if (Resources[name] is SolidColorBrush current && current.Color == color && current.Opacity == opacity) return;
        var brush = new SolidColorBrush(color) { Opacity = opacity };
        brush.Freeze();
        Resources[name] = brush;
    }

    private void SavePreferences()
    {
        ApplyPreferences();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PreferencesPath)!);
            var temporary = PreferencesPath + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(preferences, PreferencesJson.Default.Preferences));
            File.Move(temporary, PreferencesPath, true);
            saveFailed = false;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            saveFailed = true;
            if (settingsView is not null) SettingsSummary.Text += " · Не удалось сохранить";
        }
    }

    private void ToggleSettings(object sender, RoutedEventArgs e)
    {
        var opening = SettingsPanel.Visibility != Visibility.Visible;
        if (opening) EnsureSettingsView();
        SettingsPanel.Visibility = opening ? Visibility.Visible : Visibility.Collapsed;
        SessionScroll.Visibility = opening ? Visibility.Collapsed : Visibility.Visible;
        MixerHeader.Visibility = opening ? Visibility.Collapsed : Visibility.Visible;
        Status.Visibility = opening ? Visibility.Collapsed : Visibility.Visible;
        if (opening)
        {
            UpdateShortcutLabel();
            timer.Stop();
        }
        else
            CancelShortcutCapture();
        if (!opening && IsVisible)
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







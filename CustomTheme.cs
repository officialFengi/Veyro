using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Veyro;

public partial class MainWindow
{
    private bool choosingColor;
    private ThemeEditorWindow? themeStudio;
    private BitmapSource? savedBackground;
    private bool backgroundLoaded;
    private static string BackgroundPath => Path.Combine(Path.GetDirectoryName(PreferencesPath)!, "background.png");

    internal static BitmapSource ReadBackground(string path)
    {
        using var stream = File.OpenRead(path);
        var frame = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None).Frames[0];
        int width = frame.PixelWidth, height = frame.PixelHeight;
        stream.Position = 0;
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        if (width >= height && width > 1200) bitmap.DecodePixelWidth = 1200;
        else if (height > 1200) bitmap.DecodePixelHeight = 1200;
        bitmap.StreamSource = stream;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    internal static bool IsImageError(Exception error) =>
        error is IOException or UnauthorizedAccessException or NotSupportedException or FileFormatException or ArgumentException or COMException;

    private BitmapSource? GetSavedBackground()
    {
        if (!backgroundLoaded)
        {
            try { if (File.Exists(BackgroundPath)) savedBackground = ReadBackground(BackgroundPath); }
            catch (Exception error) when (IsImageError(error)) { savedBackground = null; }
            backgroundLoaded = true;
        }
        return savedBackground;
    }

    private void ApplyThemeBackground(bool preview)
    {
        var image = preview ? themeStudio?.BackgroundImage :
            preferences.Theme == "Custom" ? GetSavedBackground() : null;
        if (image is null)
            ThemeBackdrop.Background = null;
        else if ((ThemeBackdrop.Background as ImageBrush)?.ImageSource != image)
        {
            var brush = new ImageBrush(image) { Stretch = Stretch.UniformToFill };
            brush.Freeze();
            ThemeBackdrop.Background = brush;
        }
        int dimming = preview ? themeStudio!.BackgroundDimming :
            preferences.Theme == "Custom" ? DefaultDimming(image) : 0;
        ThemeShade.Opacity = dimming / 100.0;
        ThemeShade.Visibility = dimming == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    // Older themes used a 55% image blend and an undimmed solid background.
    private int DefaultDimming(BitmapSource? image) => preferences.BackgroundDimming ?? (image is null ? 0 : 45);

    private void SaveBackground(BitmapSource? image)
    {
        if (ReferenceEquals(image, GetSavedBackground())) return;
        if (image is null) File.Delete(BackgroundPath);
        else
        {
            Directory.CreateDirectory(Path.GetDirectoryName(BackgroundPath)!);
            var temporary = BackgroundPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(image));
                using (var file = File.Create(temporary)) encoder.Save(file);
                File.Move(temporary, BackgroundPath, true);
            }
            finally { File.Delete(temporary); }
        }
        savedBackground = image;
    }

    private void OpenThemeEditor(object sender, RoutedEventArgs e)
    {
        if (themeStudio is not null) { themeStudio.Activate(); return; }
        choosingColor = true;
        var image = GetSavedBackground();
        themeStudio = new ThemeEditorWindow(ThemePalette.Resolve(preferences.Theme, preferences.CustomColors),
            image, DefaultDimming(image), preferences.ElementTransparency,
            colors => ApplyPreferences(colors), (colors, image) =>
            {
                SaveBackground(image);
                preferences = preferences with { Theme = "Custom", CustomColors = colors,
                    BackgroundDimming = themeStudio!.BackgroundDimming, ElementTransparency = themeStudio.ElementTransparency };
                SavePreferences();
            });
        SettingsPanel.Visibility = Visibility.Collapsed;
        MixerHeader.Visibility = Visibility.Visible;
        SessionScroll.Visibility = Visibility.Visible;
        Status.ClearValue(VisibilityProperty);
        ApplyPreferences(themeStudio.Colors);
        RefreshMixer();
        UpdateLayout();
        var area = SystemParameters.WorkArea;
        themeStudio.Left = Left - themeStudio.Width - 12;
        if (themeStudio.Left < area.Left) themeStudio.Left = Math.Min(area.Right - themeStudio.Width, Left + ActualWidth + 12);
        themeStudio.Top = Math.Max(area.Top, Top - 80);
        themeStudio.Closed += (_, _) =>
        {
            themeStudio = null;
            choosingColor = false;
            ApplyPreferences();
            if (IsVisible) timer.Start();
        };
        themeStudio.Show();
        themeStudio.Top = Math.Clamp(themeStudio.Top, area.Top, Math.Max(area.Top, area.Bottom - themeStudio.ActualHeight));
    }
}

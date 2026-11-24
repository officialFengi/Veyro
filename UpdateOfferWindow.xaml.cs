using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Veyro;

public partial class UpdateOfferWindow : Window
{
    private static string InstalledVersion => typeof(UpdateOfferWindow).Assembly.GetName().Version!.ToString(3);

    internal UpdateOfferWindow(string tag) : this(Preferences.Load())
    {
        Body.ContentTemplate = (DataTemplate)Resources["OfferTemplate"];
        Body.Content = new { Installed = InstalledVersion, Available = tag.TrimStart('v', 'V') };
    }

    internal static UpdateOfferWindow CreateResult(bool current, Preferences? settings)
    {
        var window = new UpdateOfferWindow(settings ?? Preferences.Load());
        window.SetResult(current);
        return window;
    }

    internal void SetResult(bool current)
    {
        Title = "Обновления Veyro";
        Body.ContentTemplate = (DataTemplate)Resources["ResultTemplate"];
        Body.Content = new
        {
            Glyph = current ? "\uE73E" : "\uE946",
            Heading = current ? "Обновлений нет" : "Проверка не удалась",
            Description = current ? $"У вас актуальная версия Veyro {InstalledVersion}."
                                  : "Проверьте подключение к интернету и попробуйте ещё раз."
        };
    }

    private UpdateOfferWindow(Preferences settings)
    {
        InitializeComponent();
        var colors = ThemePalette.Resolve(settings.Theme, settings.CustomColors);
        for (int i = 0; i < colors.Length; i++)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors[i]));
            brush.Freeze();
            Resources[ThemePalette.Keys[i]] = brush;
        }
        var accent = ((SolidColorBrush)Resources["Accent"]).Color;
        double luminance = .2126 * Linear(accent.R) + .7152 * Linear(accent.G) + .0722 * Linear(accent.B);
        Resources["OnAccent"] = luminance > .179 ? Brushes.Black : Brushes.White;
    }

    private static double Linear(byte channel)
    {
        double s = channel / 255.0;
        return s <= .04045 ? s / 12.92 : Math.Pow((s + .055) / 1.055, 2.4);
    }

    private void DragHeader(object sender, MouseButtonEventArgs e) => DragMove();
    private void Accept(object sender, RoutedEventArgs e) => DialogResult = true;
    private void Dismiss(object sender, RoutedEventArgs e) => Close();

    private void HandleKey(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        e.Handled = true;
        Close();
    }
}

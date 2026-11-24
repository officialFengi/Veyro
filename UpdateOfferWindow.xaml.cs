using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Veyro;

public partial class UpdateOfferWindow : Window
{
    internal UpdateOfferWindow(string tag)
    {
        InitializeComponent();
        var settings = Preferences.Load();
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
        Installed.Text = typeof(UpdateOfferWindow).Assembly.GetName().Version!.ToString(3);
        Available.Text = tag.TrimStart('v', 'V');
    }

    private static double Linear(byte channel)
    {
        double s = channel / 255.0;
        return s <= .04045 ? s / 12.92 : Math.Pow((s + .055) / 1.055, 2.4);
    }

    private void DragHeader(object sender, MouseButtonEventArgs e) => DragMove();
    private void Accept(object sender, RoutedEventArgs e) => DialogResult = true;
    private void Dismiss(object sender, RoutedEventArgs e) => DialogResult = false;
}

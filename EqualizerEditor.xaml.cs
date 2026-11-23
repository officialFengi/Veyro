using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace Veyro;

public sealed class BandGraph : Grid
{
    internal int[] Gains { get; set; } = EqualizerProfile.Presets[0].Gains;

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        double width = ActualWidth, height = ActualHeight, pad = 10.5;
        if (width < 1 || height <= 2 * pad) return;
        var accent = (SolidColorBrush)FindResource("Accent");
        var grid = new Pen((Brush)FindResource("Stroke"), 1);
        double step = width / Gains.Length;
        var points = Gains.Select((g, i) => new Point(step * (i + 0.5),
            pad + (12 - g) / 24d * (height - 2 * pad))).ToArray();
        foreach (var p in points) dc.DrawLine(grid, new Point(p.X, pad), new Point(p.X, height - pad));
        for (int i = 0; i < 3; i++)
        {
            double y = pad + i * (height - 2 * pad) / 2;
            dc.DrawLine(grid, new Point(0, y), new Point(width, y));
        }
        var curve = new PathFigure { StartPoint = new Point(0, points[0].Y) };
        curve.Segments.Add(new LineSegment(points[0], true));
        for (int i = 1; i < points.Length; i++)
        {
            Point a = points[i - 1], b = points[i];
            double middle = (a.X + b.X) / 2;
            curve.Segments.Add(new BezierSegment(new Point(middle, a.Y), new Point(middle, b.Y), b, true));
        }
        curve.Segments.Add(new LineSegment(new Point(width, points[^1].Y), true));
        var fill = curve.Clone();
        fill.Segments.Add(new LineSegment(new Point(width, height), false));
        fill.Segments.Add(new LineSegment(new Point(0, height), false));
        fill.IsClosed = true;
        var color = accent.Color;
        dc.DrawGeometry(new LinearGradientBrush(Color.FromArgb(68, color.R, color.G, color.B),
            Color.FromArgb(4, color.R, color.G, color.B), 90), null, new PathGeometry(new[] { fill }));
        dc.DrawGeometry(null, new Pen(accent, 3), new PathGeometry(new[] { curve }));
    }
}

internal sealed record CableChoice(string Id, string Label, bool Available);

public partial class EqualizerEditor : UserControl
{
    private readonly Slider[] sliders = new Slider[EqualizerProfile.Frequencies.Length];
    private readonly TextBlock[] values = new TextBlock[EqualizerProfile.Frequencies.Length];
    private readonly Func<List<CableChoice>> choices;
    private readonly Action<EqualizerProfile> save;
    private EqualizerProfile profile;
    private bool updating;

    internal EqualizerEditor(EqualizerProfile initial, Func<List<CableChoice>> choices, Action<EqualizerProfile> save)
    {
        InitializeComponent();
        this.choices = choices;
        this.save = save;
        profile = initial;
        for (int i = 0; i < sliders.Length; i++)
        {
            int band = i, hz = EqualizerProfile.Frequencies[i];
            string frequency = hz < 1000 ? hz.ToString() : (hz / 1000d).ToString("0.#") + "к";
            Plot.ColumnDefinitions.Add(new ColumnDefinition());
            var slider = sliders[i] = new Slider { Style = (Style)FindResource("BandSlider") };
            AutomationProperties.SetName(slider, hz + " Гц, усиление в децибелах");
            Grid.SetColumn(slider, i);
            Plot.Children.Add(slider);
            slider.ValueChanged += (_, _) => {
                if (updating) return;
                var gains = (int[])profile.Gains.Clone();
                gains[band] = (int)slider.Value;
                Commit(profile with { Gains = gains });
            };
            values[i] = Label(Readouts, "");
            Label(Labels, frequency + " Гц");
        }
        foreach (var (name, gains) in EqualizerProfile.Presets)
        {
            var button = Chip(name);
            button.Tag = gains;
            button.Click += (_, _) => Commit(profile with { Gains = gains });
            Presets.Children.Add(button);
        }
        Render();
    }

    private static TextBlock Label(Panel parent, string text)
    {
        var label = new TextBlock { Text = text, FontSize = 17, HorizontalAlignment = HorizontalAlignment.Center };
        label.SetResourceReference(TextBlock.ForegroundProperty, "Secondary");
        parent.Children.Add(label);
        return label;
    }

    private static Button Chip(string text) => new() {
        Content = text, MinWidth = 94, Height = 44, FontSize = 18, Margin = new Thickness(0, 0, 6, 0)
    };

    private void Commit(EqualizerProfile changed)
    {
        profile = changed;
        Render();
        save(profile);
    }

    private void Render()
    {
        updating = true;
        for (int i = 0; i < sliders.Length; i++)
        {
            int gain = profile.Gains[i];
            sliders[i].Value = gain;
            string value = gain.ToString("+0;-0;0") + " дБ";
            values[i].Text = value;
            sliders[i].ToolTip = EqualizerProfile.Frequencies[i] + " Гц  ·  " + value;
        }
        Plot.Gains = profile.Gains;
        Plot.InvalidateVisual();
        foreach (Button button in Presets.Children)
            button.SetResourceReference(BorderBrushProperty,
                profile.Gains.SequenceEqual((int[])button.Tag) ? "Accent" : "Stroke");
        Bypass.Content = profile.Enabled ? "EQ вкл." : "Обход";
        Bypass.SetResourceReference(BorderBrushProperty, profile.Enabled ? "Accent" : "Stroke");
        updating = false;
    }

    internal void SetStatus(string status) => State.Text = status;

    private void ToggleBypass(object sender, RoutedEventArgs e) => Commit(profile with { Enabled = !profile.Enabled });

    private void ToggleRouting(object sender, RoutedEventArgs e)
    {
        Routing.Visibility = Routing.IsVisible ? Visibility.Collapsed : Visibility.Visible;
        if (Routing.Visibility == Visibility.Visible) RenderCables();
    }

    private void RefreshCableList(object sender, RoutedEventArgs e) => RenderCables();

    private void RenderCables()
    {
        Cables.Children.Clear();
        var available = choices();
        if (available.Count == 0)
            Label(Cables, "Доступных выходов VB-CABLE или S/PDIF нет.");
        foreach (var cable in available)
        {
            bool selected = cable.Id == profile.CableId;
            var button = Chip((selected ? "✓  " : "") + cable.Label);
            button.IsEnabled = cable.Available;
            button.Click += (_, _) => { Commit(profile with { CableId = cable.Id }); RenderCables(); };
            Cables.Children.Add(button);
        }
        if (profile.CableId is not null)
        {
            Label(Cables, "Перед отключением верните приложению обычный выход Windows.");
            var clear = Chip("Отключить маршрут");
            clear.Click += (_, _) => { Commit(profile with { CableId = null }); RenderCables(); };
            Cables.Children.Add(clear);
        }
    }

    private void OpenWindowsMixer(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo("ms-settings:apps-volume") { UseShellExecute = true }); }
        catch (Win32Exception) { State.Text = "Откройте Параметры Windows → Система → Звук → Микшер громкости."; }
    }
}

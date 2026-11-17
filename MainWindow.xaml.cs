using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using Forms = System.Windows.Forms;

namespace Veyro;

public partial class MainWindow : Window
{
    private readonly MMDeviceEnumerator audio = new();
    private readonly ObservableCollection<MixerChannel> rows = new();
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(750) };


    private MMDevice? endpoint;
    private Dictionary<string, AppSessions>? cachedSessions;
    private long nextSessionScan;
    private bool exiting;
    private System.Windows.Controls.Primitives.Popup? outputMenu;
    private long hiddenAt;


    public MainWindow()
    {
        InitializeComponent();
        LoadPreferences();
        MixerRows.ItemsSource = displayedRows;
        PreviewMouseDown += (_, mouse) =>
        {
            if (outputMenu?.IsOpen != true) return;
            var point = mouse.GetPosition(OutputButton);
            if (!new Rect(OutputButton.RenderSize).Contains(point))
                outputMenu.IsOpen = false;
        };
        Deactivated += (_, _) => { if (!choosingColor) DismissMixer(); };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
                DismissMixer();
        };
        SizeChanged += (_, _) =>
        {
            if (IsVisible)
                PositionMixer();
        };
        timer.Tick += (_, _) => RefreshMixer();
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible)
            {
                RefreshMixer();
                if (SettingsPanel.Visibility != Visibility.Visible && FolderPanel.Visibility != Visibility.Visible) timer.Start();
            }
            else
            {
                timer.Stop();
            }
        };
        Closing += (_, e) =>
        {
            if (exiting || Application.Current.Dispatcher.HasShutdownStarted)
                return;
            e.Cancel = true;
            Hide();
        };
        Closed += (_, _) =>
        {
            timer.Stop();



            endpoint?.Dispose();
            audio.Dispose();
            Application.Current.Shutdown();
        };
    }

    private void RefreshMixer()
    {
        try
        {
            if (!audio.HasDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia))
            {
                rows.Clear();
                displayedRows.Clear();
                endpoint?.Dispose();
                endpoint = null;
                cachedSessions = null;
                Status.Text = "Подключите устройство вывода звука.";
                return;
            }

            using var current = audio.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            if (endpoint?.ID != current.ID)
            {
                var replacement = audio.GetDevice(current.ID);
                rows.Clear();
                displayedRows.Clear();
                endpoint?.Dispose();
                endpoint = replacement;
                cachedSessions = null;
            }
            var device = endpoint!;
            var master = device.AudioEndpointVolume;
            UpdateChannel("master", device.FriendlyName, null,
                () => master.MasterVolumeLevelScalar, value => master.MasterVolumeLevelScalar = value,
                () => master.Mute, muted => master.Mute = muted);

            if (cachedSessions is null || Environment.TickCount64 >= nextSessionScan)
            {
                cachedSessions = ReadSessions(device);
                nextSessionScan = Environment.TickCount64 + 3000;
            }
            var apps = cachedSessions;
            foreach (var (key, app) in apps.OrderBy(item => item.Value.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                var volumes = app.Volumes;
                UpdateChannel(key, app.Name, app.Executable,
                    () => volumes.Max(volume => volume.Volume),
                    value => ApplyToSessions(volumes, volume => volume.Volume = value),
                    () => volumes.All(volume => volume.Mute),
                    muted => ApplyToSessions(volumes, volume => volume.Mute = muted));
            }
            for (var i = rows.Count - 1; i > 0; i--)
            {
                if (!apps.ContainsKey(rows[i].Key))
                    rows.RemoveAt(i);
            }
            RefreshGroups(apps);
            Status.Text = apps.Count == 0 ? "Запустите звук в приложении, чтобы оно появилось в микшере." : "";
        }
        catch (COMException)
        {
            cachedSessions = null;
            Status.Text = "Устройство вывода временно недоступно.";
        }
    }

    private static readonly Dictionary<string, (string Name, string? Executable)> sessionMetadata = new();

    private static Dictionary<string, AppSessions> ReadSessions(MMDevice device)
    {
        var result = new Dictionary<string, AppSessions>(StringComparer.OrdinalIgnoreCase);
        var manager = device.AudioSessionManager;
        manager.RefreshSessions();
        var sessions = manager.Sessions;
        var alive = new HashSet<string>();
        for (var i = 0; i < sessions.Count; i++)
        {
            try
            {
                using var session = sessions[i];
                if (session.State == AudioSessionState.AudioSessionStateExpired)
                    continue;

                var pid = session.GetProcessID;
                var system = session.IsSystemSoundsSession;
                var name = system ? "Системные звуки" : session.DisplayName;
                string? executable = null;
                var identity = session.GetSessionInstanceIdentifier;
                alive.Add(identity);
                var cached = sessionMetadata.TryGetValue(identity, out var metadata);
                if (cached)
                {
                    name = metadata.Name;
                    executable = metadata.Executable;
                }
                if (!cached && !system && pid != 0)
                {
                    try
                    {
                        using var process = Process.GetProcessById((int)pid);
                        if (string.IsNullOrWhiteSpace(name) || name.StartsWith('@'))
                            name = process.ProcessName;
                        executable = process.MainModule?.FileName;
                        if (!string.IsNullOrWhiteSpace(executable))
                        {
                            var description = FileVersionInfo.GetVersionInfo(executable).FileDescription;
                            if (!string.IsNullOrWhiteSpace(description))
                                name = description;
                        }
                    }
                    catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or Win32Exception or IOException)
                    {
                        // Protected or recently exited processes may not expose their executable.
                    }
                }

                if (string.IsNullOrWhiteSpace(name) || name.StartsWith('@'))
                    name = pid == 0 ? "Аудиосеанс" : $"Приложение ({pid})";
                sessionMetadata[identity] = (name, executable);
                var key = system ? "system" : executable ?? (pid == 0 ? session.GetSessionInstanceIdentifier : $"pid:{pid}");
                if (!result.TryGetValue(key, out var app))
                    result.Add(key, app = new AppSessions(name, executable));
                app.Volumes.Add(session.SimpleAudioVolume);
            }
            catch (COMException)
            {
                // A session can end while Windows is enumerating it.
            }
        }
        foreach (var stale in sessionMetadata.Keys.Where(key => !alive.Contains(key)).ToArray())
            sessionMetadata.Remove(stale);
        return result;
    }

    private void UpdateChannel(string key, string name, string? executable, Func<float> readVolume,
        Action<float> writeVolume, Func<bool> readMute, Action<bool> writeMute)
    {
        var row = rows.FirstOrDefault(item => StringComparer.OrdinalIgnoreCase.Equals(item.Key, key));
        var volume = readVolume();
        var muted = readMute();
        if (row is null)
        {
            row = new MixerChannel(key, name, LoadIcon(executable));
            rows.Add(row);
        }
        row.Refresh(volume, muted, writeVolume, writeMute);
    }

    private static void ApplyToSessions(IEnumerable<SimpleAudioVolume> volumes, Action<SimpleAudioVolume> apply)
    {
        foreach (var volume in volumes)
        {
            try { apply(volume); }
            catch (COMException) { /* The session may have ended since the last refresh. */ }
        }
    }

    private static ImageSource? LoadIcon(string? path)
    {
        if (path is null)
            return null;
        try
        {
            using var icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
            return icon is null ? null : Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty,
                BitmapSizeOptions.FromWidthAndHeight(24, 24));
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or Win32Exception)
        {
            return null;
        }
    }

    private void ShowOutputs(object sender, RoutedEventArgs e)
    {
        if (outputMenu?.IsOpen == true)
        {
            outputMenu.IsOpen = false;
            outputMenu = null;
            return;
        }

        var options = new StackPanel();
        options.Children.Add(new TextBlock
        {
            Text = "Устройство вывода", FontSize = 13,
            Foreground = (Brush)Resources["Secondary"], Margin = new Thickness(10, 6, 10, 12)
        });
        var popup = new System.Windows.Controls.Primitives.Popup
        {
            PlacementTarget = OutputButton,
            Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom,
            HorizontalOffset = OutputButton.ActualWidth - 370,
            VerticalOffset = 8,
            AllowsTransparency = true,
            StaysOpen = true,
            PopupAnimation = System.Windows.Controls.Primitives.PopupAnimation.None
        };
        try
        {
            foreach (var device in audio.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            {
                using var output = device;
                var id = output.ID;
                var selected = id == endpoint?.ID;
                var button = new Button
                {
                    Style = (Style)FindResource(typeof(Button)),
                    Margin = new Thickness(0, 0, 0, 5),
                    BorderBrush = selected ? (Brush)Resources["Accent"] : Brushes.Transparent,
                    Content = new TextBlock
                    {
                        Text = (selected ? "✓  " : "") + output.FriendlyName,
                        Width = 316, TextWrapping = TextWrapping.Wrap, FontSize = 13,
                        Margin = new Thickness(10, 10, 10, 10),
                        Foreground = (Brush)Resources["Ink"]
                    }
                };
                button.Click += (_, _) =>
                {
                    try
                    {
                        OutputPolicy.Select(id);
                        RefreshMixer();
                        popup.IsOpen = false;
                    }
                    catch (Exception exception) when (exception is COMException or InvalidCastException)
                    {
                        Status.Text = "Windows не удалось переключить устройство вывода.";
                    }
                };
                options.Children.Add(button);
            }
        }
        catch (COMException)
        {
            Status.Text = "Устройства вывода временно недоступны.";
        }
        if (options.Children.Count == 1)
            options.Children.Add(new TextBlock { Text = "Нет доступных устройств", Margin = new Thickness(10), Foreground = (Brush)Resources["Ink"] });
        popup.Child = new Border
        {
            Width = 370, Padding = new Thickness(10), CornerRadius = new CornerRadius(16),
            Background = (Brush)Resources["Panel"], BorderBrush = (Brush)Resources["Stroke"],
            BorderThickness = new Thickness(1),
            Child = new ScrollViewer { MaxHeight = 340, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = options }
        };
        outputMenu = popup;
        popup.Closed += (_, _) =>
        {
            if (ReferenceEquals(outputMenu, popup)) outputMenu = null;
        };
        popup.IsOpen = true;
    }
    private void ToggleMute(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: MixerChannel channel })
            channel.ToggleMute();
    }

    private void AdjustWithWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is Slider slider)
        {
            slider.SetCurrentValue(Slider.ValueProperty, Math.Clamp(slider.Value + Math.Sign(e.Delta) * 2, 0, 100));
            e.Handled = true;
        }
    }

    private void DragHeader(object sender, MouseButtonEventArgs e) => DragMove();
    private void HideMixer(object sender, RoutedEventArgs e) => Hide();

    private void DismissMixer()
    {
        if (outputMenu?.IsOpen == true) outputMenu.IsOpen = false;
        if (!IsVisible)
            return;
        Hide();
        hiddenAt = Environment.TickCount64;
    }

    internal void ExitMixer() { exiting = true; Close(); }
    internal void ToggleMixer()
    {
        if (IsVisible)
            DismissMixer();
        else if (Environment.TickCount64 - hiddenAt > 300)
            OpenMixer();
    }

    internal void OpenMixer()
    {
        nextSessionScan = 0;
        Show();
        UpdateLayout();
        PositionMixer();
        Activate();
    }

    private void PositionMixer()
    {
        var screen = Forms.Screen.FromPoint(Forms.Cursor.Position);
        var work = screen.WorkingArea;
        var source = PresentationSource.FromVisual(this);
        if (source?.CompositionTarget is null)
            return;

        var transform = source.CompositionTarget.TransformFromDevice;
        var topLeft = transform.Transform(new Point(work.Left, work.Top));
        var bottomRight = transform.Transform(new Point(work.Right, work.Bottom));
        Left = Math.Max(topLeft.X + 8, bottomRight.X - ActualWidth - 12);
        Top = Math.Max(topLeft.Y + 8, bottomRight.Y - ActualHeight - 12);
    }
    private sealed record AppSessions(string Name, string? Executable)
    {
        public List<SimpleAudioVolume> Volumes { get; } = new();
    }
}
















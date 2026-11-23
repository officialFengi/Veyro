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
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(3) };


    private MMDevice? endpoint;
    private Dictionary<string, AppSessions>? cachedSessions;
    private readonly AudioChanges changes;
    private sealed record WatchedSession(AudioSessionControl Control, string Key, string Name, string? Executable);
    private readonly Dictionary<string, WatchedSession> watchedSessions = new();
    private readonly HashSet<string> liveSessionIds = new();
    private bool observingDevices;
    private MixerChannel? masterRow;
    private bool exiting;
    private System.Windows.Controls.Primitives.Popup? outputMenu;
    private long hiddenAt;


    internal MainWindow(IMixerShortcut shortcut, Preferences settings)
    {
        this.shortcut = shortcut;
        preferences = settings;
        changes = new AudioChanges(Dispatcher, topology =>
        {
            if (!timer.IsEnabled || !IsVisible) return;
            if (topology) RefreshMixer();
            else RefreshLevels();
        });
        InitializeComponent();
        InitializeEqualizer();
        ApplyPreferences();
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
            if (CaptureShortcut(e)) return;
            if (e.Key == Key.Escape)
                DismissMixer();
        };
        SizeChanged += (_, _) =>
        {
            if (IsVisible)
                PositionMixer();
        };
        timer.Tick += (_, _) => RefreshMixer(rebuildGroups: false);
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible)
            {
                observingDevices = audio.RegisterEndpointNotificationCallback(changes) >= 0;
                ApplyThemeBackground(themeStudio is not null);
                RefreshMixer();
                if (SettingsPanel.Visibility != Visibility.Visible && FolderPanel.Visibility != Visibility.Visible) timer.Start();
            }
            else
            {
                timer.Stop();
                CancelShortcutCapture();
                CloseEqualizerEditor();
                FlushEqualizerSave();
                StopObserving();
                if (themeStudio is null)
                {
                    ThemeBackdrop.Background = null;
                    savedBackground = null;
                    backgroundLoaded = false;
                }
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
            FlushEqualizerSave();
            StopEqualizer();
            StopObserving();
            audio.Dispose();
        };
    }

    private void RefreshMixer(bool rebuildGroups = true)
    {
        try
        {
            if (!audio.HasDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia))
            {
                ReleaseMixerRows();
                Status.Text = "Подключите устройство вывода звука.";
                return;
            }

            using var current = audio.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            var id = current.ID;
            if (endpoint?.ID != id)
            {
                ReleaseMixerRows();
                endpoint = audio.GetDevice(id);
                var master = endpoint.AudioEndpointVolume;
                master.OnVolumeNotification += changes.OnEndpointVolume;
                endpoint.AudioSessionManager.OnSessionCreated += changes.OnSessionCreated;
                masterRow = new MixerChannel("master", endpoint.FriendlyName, null);
                masterRow.Bind(value => master.MasterVolumeLevelScalar = value, mute => master.Mute = mute);
                rows.Add(masterRow);
            }
            bool outputsChanged = RefreshExtraOutputs(id);
            bool sessionsChanged = ScanSessions(endpoint!);
            var apps = cachedSessions!;
            if (sessionsChanged) BindAppRows(apps);
            if (sessionsChanged || outputsChanged || rebuildGroups) RefreshGroups(apps);
            RefreshLevels();
            Status.Text = apps.Count == 0 ? "Запустите звук в приложении, чтобы оно появилось в микшере." : "";
        }
        catch (Exception ex) when (AudioUnavailable(ex)) { Status.Text = "Устройство вывода временно недоступно."; }
    }

    private void BindAppRows(Dictionary<string, AppSessions> apps)
    {
        foreach (var (key, app) in apps)
        {
            var row = rows.FirstOrDefault(item => StringComparer.OrdinalIgnoreCase.Equals(item.Key, key));
            if (row is null)
            {
                row = new MixerChannel(key, app.Name, LoadIcon(app.Executable));
                rows.Add(row);
            }
            app.Channel = row;
            var volumes = app.Volumes;
            row.SetAvailable(volumes.Count > 0);
            row.Bind(value => ApplyToSessions(volumes, volume => volume.Volume = value),
                mute => ApplyToSessions(volumes, volume => volume.Mute = mute));
        }
        for (var i = rows.Count - 1; i > 0; i--)
            if (!apps.ContainsKey(rows[i].Key)) rows.RemoveAt(i);
    }

    private void RefreshLevels()
    {
        if (endpoint is null || cachedSessions is null) return;
        try
        {
            var master = endpoint.AudioEndpointVolume;
            masterRow?.SetLevels(master.MasterVolumeLevelScalar, master.Mute);
            foreach (var output in extraOutputs.Values)
            {
                var volume = output.Device.AudioEndpointVolume;
                output.Channel.SetLevels(volume.MasterVolumeLevelScalar, volume.Mute);
            }
            foreach (var app in cachedSessions.Values)
            {
                float level = 0;
                bool muted = true;
                foreach (var volume in app.Volumes)
                {
                    level = Math.Max(level, volume.Volume);
                    muted &= volume.Mute;
                }
                app.Channel?.SetLevels(level, muted);
            }
            foreach (var (group, members) in groupMembers)
            {
                double highest = 0;
                bool muted = true;
                foreach (var member in members)
                {
                    highest = Math.Max(highest, member.Volume);
                    muted &= member.IsMuted;
                }
                group.SetLevels((float)(highest / 100), muted);
            }
        }
        catch (COMException) { /* The next topology refresh replaces disconnected sessions. */ }
    }

    private void StopObserving()
    {
        if (observingDevices)
        {
            audio.UnregisterEndpointNotificationCallback(changes);
            observingDevices = false;
        }
        ReleaseMixerRows();
    }

    private void ReleaseMixerRows()
    {
        foreach (var session in watchedSessions.Values) DisposeSession(session.Control);
        watchedSessions.Clear();
        liveSessionIds.Clear();
        cachedSessions = null;
        groupRows.Clear();
        groupMembers.Clear();
        if (rows.Count > 0) rows.Clear();
        if (displayedRows.Count > 0) displayedRows.Clear();
        ClearExtraOutputs();
        masterRow = null;
        endpoint?.Dispose();
        endpoint = null;
    }

    private static void DisposeSession(AudioSessionControl session)
    {
        try { session.Dispose(); }
        catch (COMException) { /* Windows already removed this session. */ }
    }

    private bool ScanSessions(MMDevice device)
    {
        bool changed = cachedSessions is null;
        liveSessionIds.Clear();
        changed |= ReadEndpoint(device, null);
        foreach (var (key, profile) in preferences.Equalizers!)
        {
            if (profile.CableId is not { } cableId || cableId == device.ID) continue;
            try
            {
                using var cable = audio.GetDevice(cableId);
                if (cable.State == DeviceState.Active) changed |= ReadEndpoint(cable, key);
            }
            catch (Exception ex) when (AudioUnavailable(ex)) { }
        }

        if (!liveSessionIds.SetEquals(watchedSessions.Keys))
        {
            foreach (var id in watchedSessions.Keys.Where(id => !liveSessionIds.Contains(id)).ToArray())
            {
                DisposeSession(watchedSessions[id].Control);
                watchedSessions.Remove(id);
                changed = true;
            }
        }
        if (!changed) return false;
        var apps = new Dictionary<string, AppSessions>(StringComparer.OrdinalIgnoreCase);
        foreach (var session in watchedSessions.Values)
        {
            if (!apps.TryGetValue(session.Key, out var app))
                apps.Add(session.Key, app = new AppSessions(session.Name, session.Executable));
            app.Volumes.Add(session.Control.SimpleAudioVolume);
        }
        foreach (var (key, profile) in preferences.Equalizers!)
            if (profile.CableId is not null && !apps.ContainsKey(key))
                apps.Add(key, new AppSessions(profile.Name, key));
        cachedSessions = apps;
        return true;
    }

    private bool ReadEndpoint(MMDevice source, string? owner)
    {
        bool changed = false;
        var manager = source.AudioSessionManager;
        manager.RefreshSessions();
        var sessions = manager.Sessions;
        if (sessions is null) throw new InvalidOperationException("Недоступен список аудиосеансов.");
        int count = sessions.Count;
        for (int i = 0; i < count; i++)
        {
            AudioSessionControl? control = null;
            try
            {
                control = sessions[i];
                if (control.GetProcessID == Environment.ProcessId) continue;
                if (control.State == AudioSessionState.AudioSessionStateExpired) continue;
                string id = control.GetSessionInstanceIdentifier;
                if (watchedSessions.TryGetValue(id, out var watched))
                {
                    if (owner is null || string.Equals(owner, watched.Key, StringComparison.OrdinalIgnoreCase))
                        liveSessionIds.Add(id);
                    continue;
                }
                var info = DescribeSession(control, id);
                if (owner is not null && !string.Equals(owner, info.Key, StringComparison.OrdinalIgnoreCase)) continue;
                liveSessionIds.Add(id);
                try { control.RegisterEventClient(changes); }
                catch (COMException) { /* The periodic refresh also reads volume and mute. */ }
                watchedSessions.Add(id, info);
                control = null; // The entry owns its notification registration until the session ends.
                changed = true;
            }
            catch (COMException) { /* The session may have ended during enumeration. */ }
            finally { if (control is not null) DisposeSession(control); }
        }
        return changed;
    }

    private static WatchedSession DescribeSession(AudioSessionControl session, string id)
    {
        uint pid = session.GetProcessID;
        bool system = session.IsSystemSoundsSession;
        string name = system ? "Системные звуки" : session.DisplayName;
        string? executable = null;
        if (!system && pid != 0)
        {
            try
            {
                using var process = Process.GetProcessById((int)pid);
                if (string.IsNullOrWhiteSpace(name) || name.StartsWith('@')) name = process.ProcessName;
                executable = process.MainModule?.FileName;
                if (!string.IsNullOrWhiteSpace(executable))
                {
                    string? description = FileVersionInfo.GetVersionInfo(executable).FileDescription;
                    if (!string.IsNullOrWhiteSpace(description)) name = description;
                }
            }
            catch (Exception error) when (error is ArgumentException or InvalidOperationException or Win32Exception or IOException)
            { /* Protected processes may only expose the session name. */ }
        }
        if (string.IsNullOrWhiteSpace(name) || name.StartsWith('@'))
            name = pid == 0 ? "Аудиосеанс" : $"Приложение ({pid})";
        string key = system ? "system" : executable ?? (pid == 0 ? id : $"pid:{pid}");
        return new WatchedSession(session, key, name, executable);
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
        var available = new Dictionary<string, string>();
        try
        {
            foreach (var device in audio.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            {
                using var output = device;
                var id = output.ID;
                available[id] = output.FriendlyName;
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
        AddMultiSoundOptions(options, available);
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

    internal Preferences CurrentSettings => preferences;
    internal bool CanUnload => !IsVisible && !exiting && themeStudio is null && !saveFailed && !assigningShortcut && !HasEqualizerRoutes;

    internal MixerViewState CaptureView() => new(expandedGroups.ToArray(), SettingsPanel.Visibility == Visibility.Visible,
        settingsView is null ? "" : NewGroupName.Text, folderTarget?.Key, folderTarget?.Name, FolderNameInput.Text,
        settingsView is null ? 0 : SettingsScroll.VerticalOffset);

    internal void RestoreView(MixerViewState? state)
    {
        if (state is null) return;
        expandedGroups.UnionWith(state.ExpandedGroups);
        if (state.NewGroupName.Length > 0) { EnsureSettingsView(); NewGroupName.Text = state.NewGroupName; }
        if (state.SettingsOpen)
        {
            ToggleSettings(this, new RoutedEventArgs());
            SettingsScroll.ScrollToVerticalOffset(state.SettingsOffset);
        }
        else if (state.FolderKey is not null && state.FolderName is not null)
        {
            OpenFolderPicker(new MixerChannel(state.FolderKey, state.FolderName, null));
            FolderNameInput.Text = state.FolderDraft;
        }
    }
    internal void CloseForRelease() { exiting = true; Close(); }
    internal void ToggleMixer()
    {
        if (IsVisible)
            DismissMixer();
        else if (Environment.TickCount64 - hiddenAt > 300)
            OpenMixer();
    }

    internal void OpenMixer()
    {
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
        public MixerChannel? Channel { get; set; }
        public List<SimpleAudioVolume> Volumes { get; } = new();
    }
}















